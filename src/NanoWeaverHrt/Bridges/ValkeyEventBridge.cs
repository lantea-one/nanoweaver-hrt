using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;

namespace NanoWeaverHrt.Bridges;

/// <summary>
/// Background bridge coordinating bi-directional event synchronization between the in-memory
/// <see cref="IInternalEventBus"/> and a distributed Valkey cluster.
/// </summary>
public sealed class ValkeyEventBridge : BackgroundService, IValkeyEventBridge
{
    /// <summary>
    /// Channel prefix for all Valkey pub/sub event channels.
    /// </summary>
    private const string ChannelPrefix = "weaver:events:";

    /// <summary>
    /// Local in-memory event bus instance.
    /// </summary>
    private readonly IInternalEventBus _eventBus;

    /// <summary>
    /// Valkey connection factory providing access to cluster subscribers.
    /// </summary>
    private readonly IValkeyConnectionFactory _connectionFactory;

    /// <summary>
    /// Logger instance used to record bridge activity and cluster diagnostics.
    /// </summary>
    private readonly ILogger<ValkeyEventBridge> _logger;

    /// <summary>
    /// List of subscriptions holding local event bus callback unregistration tokens.
    /// </summary>
    private readonly List<IDisposable> _localSubscriptions = new();

    /// <summary>
    /// Set of recently produced local event IDs used to prevent circular echo loops when receiving cluster broadcasts.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _locallyProducedEvents = new();

    /// <summary>
    /// Counter tracking total events published outbound to Valkey.
    /// </summary>
    private long _outboundCount;

    /// <summary>
    /// Counter tracking total events received inbound from Valkey and redispatched locally.
    /// </summary>
    private long _inboundCount;

    /// <summary>
    /// Active status flag indicating whether the bridge is actively broadcasting.
    /// </summary>
    private bool _isActive;

    /// <inheritdoc/>
    public bool IsActive => _isActive && _connectionFactory.IsConnected;

    /// <inheritdoc/>
    public long OutboundEventsCount => Interlocked.Read(ref _outboundCount);

    /// <inheritdoc/>
    public long InboundEventsCount => Interlocked.Read(ref _inboundCount);

    /// <summary>
    /// Initializes a new instance of the <see cref="ValkeyEventBridge"/> class.
    /// </summary>
    /// <param name="eventBus">The in-memory event bus.</param>
    /// <param name="connectionFactory">The Valkey connection factory.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ValkeyEventBridge(
        IInternalEventBus eventBus,
        IValkeyConnectionFactory connectionFactory,
        ILogger<ValkeyEventBridge>? logger = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? NullLogger<ValkeyEventBridge>.Instance;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // WHAT: Registers local event bus listeners, marks bridge active if Valkey is connected, and starts background worker.
        // WHY:  Guarantees all event subscriptions are active before StartAsync returns.
        // HOW:  Registers local outbound forwarders, sets _isActive flag, and delegates to base.StartAsync.
        RegisterLocalOutboundForwarders();
        _isActive = _connectionFactory.IsConnected;
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // WHAT: Deactivates bridge and stops background execution.
        // WHY:  Ensures clean teardown without processing further events during shutdown.
        // HOW:  Clears _isActive flag and delegates to base.StopAsync.
        _isActive = false;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // WHAT: Initializes bridge subscriptions and enters the cluster monitoring loop.
        // WHY:  Maintains continuous pub/sub connectivity across server restarts or temporary disconnects.
        // HOW:  Subscribes to remote Valkey pub/sub channels and runs idle loop with echo pruning.
        if (!_connectionFactory.IsConnected)
        {
            _logger.LogInformation("Valkey is not connected. Event bridge idling in local mode.");
        }

        // Register inbound cluster listeners from Valkey
        await RegisterInboundClusterListenersAsync().ConfigureAwait(false);

        _isActive = _connectionFactory.IsConnected;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Prune expired echo prevention tokens older than 5 minutes
                // WHAT: Delegates the pruning pass to a synchronous method.
                // WHY:  BackgroundService runs this worker on a thread-pool thread, so whether the first pass
                //       finds any tokens depends on whether a test thread published first. Keeping the
                //       pruning logic here made its coverage depend on that race.
                // HOW:  PruneExpiredEchoTokens is a plain method that tests call directly with seeded data.
                PruneExpiredEchoTokens(DateTimeOffset.UtcNow.AddMinutes(-5));

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean cancellation during service shutdown
        }
        finally
        {
            _isActive = false;
        }
    }

    /// <summary>
    /// Registers local in-memory event subscriptions that serialize and forward events to Valkey.
    /// </summary>
    private void RegisterLocalOutboundForwarders()
    {
        // WHAT: Listens to typed events on the local bus and dispatches them outbound to Valkey.
        // WHY:  Broadcasts node progress and security audits to peer nodes across the cluster.
        // HOW:  Subscribes to each typed event and calls ForwardOutboundAsync.
        _localSubscriptions.Add(_eventBus.Subscribe<TrajectoryFrameBatchEvent>((e, ct) => ForwardOutboundAsync(e.EventName, e, e.EventId, ct)));
        _localSubscriptions.Add(_eventBus.Subscribe<SimulationProgressEvent>((e, ct) => ForwardOutboundAsync(e.EventName, e, e.EventId, ct)));
        _localSubscriptions.Add(_eventBus.Subscribe<UserSessionActivityEvent>((e, ct) => ForwardOutboundAsync(e.EventName, e, e.EventId, ct)));
        _localSubscriptions.Add(_eventBus.Subscribe<AuditLogDispatchedEvent>((e, ct) => ForwardOutboundAsync(e.EventName, e, e.EventId, ct)));
        _localSubscriptions.Add(_eventBus.Subscribe<SystemTelemetryUpdatedEvent>((e, ct) => ForwardOutboundAsync(e.EventName, e, e.EventId, ct)));
    }

    /// <summary>
    /// Forwards a local event outbound to the appropriate Valkey pub/sub channel.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="eventName">The channel discriminator name.</param>
    /// <param name="eventPayload">The event payload object.</param>
    /// <param name="eventId">The unique event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    private async ValueTask ForwardOutboundAsync<TEvent>(string eventName, TEvent eventPayload, Guid eventId, CancellationToken ct)
    {
        // WHAT: Publishes the event to Valkey if connected.
        // WHY:  Synchronizes state with other cluster nodes.
        // HOW:  Checks IsConnected, serializes payload to UTF-8 JSON, and calls ISubscriber.PublishAsync.
        ISubscriber? subscriber = _connectionFactory.GetSubscriber();
        if (subscriber == null || !_connectionFactory.IsConnected)
        {
            return;
        }

        // Record event ID locally so inbound listener skips it (echo prevention)
        _locallyProducedEvents[eventId] = DateTimeOffset.UtcNow;

        try
        {
            string channel = ChannelPrefix + eventName;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(eventPayload);
            await subscriber.PublishAsync(RedisChannel.Literal(channel), bytes).ConfigureAwait(false);
            Interlocked.Increment(ref _outboundCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish outbound event {EventName} to Valkey.", eventName);
        }
    }

    /// <summary>
    /// Registers subscriptions to Valkey pub/sub channels to receive events from remote nodes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous subscription setup.</returns>
    private async Task RegisterInboundClusterListenersAsync()
    {
        // WHAT: Subscribes to Valkey channels and republishes received payloads into the local bus.
        // WHY:  Allows local UI and workers to react to actions taken on other cluster nodes.
        // HOW:  Calls subscriber.SubscribeAsync with message handling delegates.
        ISubscriber? subscriber = _connectionFactory.GetSubscriber();
        if (subscriber == null || !_connectionFactory.IsConnected)
        {
            return;
        }

        try
        {
            await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelPrefix + "SimulationProgress"), async (_, message) =>
            {
                await ProcessInboundMessageAsync<SimulationProgressEvent>(message).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelPrefix + "UserSessionActivity"), async (_, message) =>
            {
                await ProcessInboundMessageAsync<UserSessionActivityEvent>(message).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelPrefix + "AuditLogDispatched"), async (_, message) =>
            {
                await ProcessInboundMessageAsync<AuditLogDispatchedEvent>(message).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelPrefix + "SystemTelemetryUpdated"), async (_, message) =>
            {
                await ProcessInboundMessageAsync<SystemTelemetryUpdatedEvent>(message).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register inbound Valkey pub/sub cluster listeners.");
        }
    }

    /// <summary>
    /// Deserializes an inbound cluster message and republishes it to the local event bus if not an echo.
    /// </summary>
    /// <typeparam name="TEvent">The event contract type.</typeparam>
    /// <param name="message">The raw message payload from Valkey.</param>
    /// <returns>A <see cref="ValueTask"/> representing the republishing operation.</returns>
    private async ValueTask ProcessInboundMessageAsync<TEvent>(RedisValue message) where TEvent : class, IWeaverEvent
    {
        // WHAT: Deserializes the message and republishes it locally if not produced by this node.
        // WHY:  Delivers cluster events to local subscribers without infinite recursion loops.
        // HOW:  Validates message bytes, checks _locallyProducedEvents, and calls _eventBus.PublishAsync.
        if (message.IsNullOrEmpty)
        {
            return;
        }

        try
        {
            // WHY !: StackExchange.Redis annotates the explicit (byte[]?) conversion as nullable,
            //        but the preceding message.IsNullOrEmpty guard guarantees a non-null byte[].
            byte[] bytes = (byte[])message!;
            TEvent? parsed = JsonSerializer.Deserialize<TEvent>(bytes);

            if (parsed == null)
            {
                return;
            }

            // Check if this node originally generated the event
            if (_locallyProducedEvents.ContainsKey(parsed.EventId))
            {
                return;
            }

            Interlocked.Increment(ref _inboundCount);
            await _eventBus.PublishAsync(parsed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process inbound Valkey message for type {EventType}.", typeof(TEvent).Name);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // WHAT: Asynchronously disposes the bridge and unregisters local bus subscriptions.
        // WHY:  Releases resources and detached delegates cleanly without blocking the thread pool.
        // HOW:  Disposes each subscription in _localSubscriptions and calls Dispose().
        foreach (IDisposable sub in _localSubscriptions)
        {
            sub.Dispose();
        }
        _localSubscriptions.Clear();
        Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        // WHAT: Unregisters local event bus subscriptions.
        // WHY:  Releases resources and detached delegates cleanly.
        // HOW:  Disposes each subscription in _localSubscriptions and calls base.Dispose().
        foreach (IDisposable sub in _localSubscriptions)
        {
            sub.Dispose();
        }
        _localSubscriptions.Clear();
        base.Dispose();
    }

    /// <summary>
    /// Removes echo-prevention tokens that were recorded before the supplied cutoff.
    /// </summary>
    /// <param name="cutoff">Tokens recorded strictly earlier than this instant are discarded.</param>
    /// <returns>The number of expired tokens found and removed.</returns>
    /// <remarks>
    /// An echo-prevention token is the ID of an event this node itself published to Valkey; remembering it
    /// stops the node from re-processing its own broadcast when the cluster echoes it back. Old tokens can
    /// never be echoed any more, so they are dropped to keep memory bounded.
    /// </remarks>
    internal int PruneExpiredEchoTokens(DateTimeOffset cutoff)
    {
        // WHAT: Scans the token dictionary and removes every entry older than the cutoff.
        // WHY:  Prevents unbounded growth of the echo-prevention set on long-lived nodes.
        // HOW:  Enumerates the concurrent dictionary (safe against concurrent writers) and TryRemove each stale key.
        int removed = 0;
        foreach (KeyValuePair<Guid, DateTimeOffset> kvp in _locallyProducedEvents)
        {
            if (kvp.Value < cutoff)
            {
                _locallyProducedEvents.TryRemove(kvp.Key, out _);
                removed++;
            }
        }

        return removed;
    }
}
