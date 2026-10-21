using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NanoWeaverHrt.Events;

/// <summary>
/// High-performance, zero-allocation internal event bus backed by <see cref="System.Threading.Channels.Channel{T}"/>.
/// Dispatches typed domain events asynchronously across the platform without blocking producer hot loops.
/// </summary>
public sealed class ChannelEventBus : IInternalEventBus
{
    /// <summary>
    /// Thread-safe dictionary holding active callback subscriber delegates organized by event type.
    /// </summary>
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>>> _handlers = new();

    /// <summary>
    /// Thread-safe dictionary holding active bounded channel streams organized by event type.
    /// </summary>
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, IChannelStreamEntry>> _channelStreams = new();

    /// <summary>
    /// Configuration options specifying default queue capacity and backpressure behaviors.
    /// </summary>
    private readonly EventBusOptions _options;

    /// <summary>
    /// Logger instance used to record unexpected subscriber exceptions without terminating event propagation.
    /// </summary>
    private readonly ILogger<ChannelEventBus> _logger;

    /// <summary>
    /// Tracking flag indicating whether the event bus has been disposed.
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelEventBus"/> class with the specified options and logger.
    /// </summary>
    /// <param name="options">Optional configuration options governing channel capacities and backpressure.</param>
    /// <param name="logger">Optional logger for logging subscriber faults.</param>
    public ChannelEventBus(EventBusOptions? options = null, ILogger<ChannelEventBus>? logger = null)
    {
        _options = options ?? new EventBusOptions();
        _logger = logger ?? NullLogger<ChannelEventBus>.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : notnull
    {
        // WHAT: Dispatches the event payload to all active registered callbacks and channel streams.
        // WHY:  Decouples the event publisher from downstream consumer latency and potential failures.
        // HOW:  Iterates over registered handler delegates and active channel writers for typeof(TEvent).
        ArgumentNullException.ThrowIfNull(@event);

        if (_isDisposed || ct.IsCancellationRequested)
        {
            return;
        }

        Type eventType = typeof(TEvent);

        // 1. Dispatch to registered callback delegates
        if (_handlers.TryGetValue(eventType, out ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>>? typeHandlers))
        {
            foreach (KeyValuePair<Guid, Func<object, CancellationToken, ValueTask>> kvp in typeHandlers)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await kvp.Value(@event, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Clean cancellation, rethrow to respect caller token
                    throw;
                }
                catch (Exception ex)
                {
                    // WHAT: Catches and logs subscriber exceptions.
                    // WHY:  A faulty consumer must never crash the producer hot loop or prevent other subscribers from receiving events.
                    // HOW:  Logs warning via Microsoft.Extensions.Logging.
                    _logger.LogWarning(ex, "Subscriber handler {HandlerId} threw an exception while processing {EventType}", kvp.Key, eventType.Name);
                }
            }
        }

        // 2. Dispatch to active async channel streams
        if (_channelStreams.TryGetValue(eventType, out ConcurrentDictionary<Guid, IChannelStreamEntry>? typeStreams))
        {
            foreach (KeyValuePair<Guid, IChannelStreamEntry> kvp in typeStreams)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (kvp.Value is ChannelStreamEntry<TEvent> entry)
                {
                    // Try writing synchronously to avoid allocations when channel is not full
                    if (!entry.Channel.Writer.TryWrite(@event))
                    {
                        try
                        {
                            await entry.Channel.Writer.WriteAsync(@event, ct).ConfigureAwait(false);
                        }
                        catch (ChannelClosedException)
                        {
                            // Subscriber channel was closed, unregister it safely
                            typeStreams.TryRemove(kvp.Key, out _);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>([EnumeratorCancellation] CancellationToken ct = default) where TEvent : notnull
    {
        // WHAT: Creates a dedicated bounded channel queue for the caller's asynchronous stream.
        // WHY:  Provides isolated buffering and consumption so one slow stream does not stall another.
        // HOW:  Registers a Channel<TEvent> wrapped in ChannelStreamEntry in _channelStreams and yields items via ReadAllAsync.
        ThrowIfDisposed();

        Type eventType = typeof(TEvent);
        Guid subscriptionId = Guid.NewGuid();

        BoundedChannelOptions channelOptions = new(_options.DefaultCapacity)
        {
            FullMode = _options.DefaultFullMode,
            SingleWriter = _options.SingleWriter,
            SingleReader = true
        };

        Channel<TEvent> channel = Channel.CreateBounded<TEvent>(channelOptions);
        ChannelStreamEntry<TEvent> entry = new(channel);

        ConcurrentDictionary<Guid, IChannelStreamEntry> typeStreams = _channelStreams.GetOrAdd(eventType, _ => new ConcurrentDictionary<Guid, IChannelStreamEntry>());
        typeStreams[subscriptionId] = entry;

        try
        {
            await foreach (TEvent item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            // WHAT: Cleanly removes and completes the subscriber channel on exit or cancellation.
            // WHY:  Prevents memory leaks from terminated stream enumerations.
            // HOW:  Removes key from typeStreams and marks writer complete.
            typeStreams.TryRemove(subscriptionId, out _);
            entry.Complete();
        }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : notnull
    {
        // WHAT: Registers an asynchronous delegate handler for the specified event type.
        // WHY:  Enables simple callback-based subscriptions with deterministic unregistration.
        // HOW:  Stores a wrapped Func in the concurrent dictionary and returns a SubscriptionToken.
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();

        Type eventType = typeof(TEvent);
        Guid subscriptionId = Guid.NewGuid();

        ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>> typeHandlers =
            _handlers.GetOrAdd(eventType, _ => new ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>>());

        typeHandlers[subscriptionId] = (boxedEvent, token) => handler((TEvent)boxedEvent, token);

        return new SubscriptionToken(() =>
        {
            if (_handlers.TryGetValue(eventType, out ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>>? dict))
            {
                dict.TryRemove(subscriptionId, out _);
            }
        });
    }

    /// <inheritdoc/>
    public int GetSubscriberCount<TEvent>() where TEvent : notnull
    {
        // WHAT: Computes the total active subscribers (handlers + streams) for an event type.
        // WHY:  Provides diagnostic observability into system event topology.
        // HOW:  Sums the Count of both concurrent dictionaries for typeof(TEvent).
        Type eventType = typeof(TEvent);
        int total = 0;

        if (_handlers.TryGetValue(eventType, out ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>>? h))
        {
            total += h.Count;
        }

        if (_channelStreams.TryGetValue(eventType, out ConcurrentDictionary<Guid, IChannelStreamEntry>? s))
        {
            total += s.Count;
        }

        return total;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // WHAT: Asynchronously disposes all channels and removes all registered handlers.
        // WHY:  Ensures all waiting readers are unblocked and process resources are released cleanly.
        // HOW:  Sets _isDisposed flag, completes all channels, and clears handler dictionaries.
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;

        foreach (ConcurrentDictionary<Guid, IChannelStreamEntry> typeStreams in _channelStreams.Values)
        {
            foreach (IChannelStreamEntry entry in typeStreams.Values)
            {
                entry.Complete();
            }
            typeStreams.Clear();
        }
        _channelStreams.Clear();

        foreach (ConcurrentDictionary<Guid, Func<object, CancellationToken, ValueTask>> typeHandlers in _handlers.Values)
        {
            typeHandlers.Clear();
        }
        _handlers.Clear();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Contract for untyped channel stream completion during bus shutdown and disposal.
    /// </summary>
    private interface IChannelStreamEntry
    {
        /// <summary>
        /// Marks the underlying channel writer as complete, releasing any waiting readers.
        /// </summary>
        void Complete();
    }

    /// <summary>
    /// Strongly-typed wrapper around a bounded subscriber channel holding completion semantics.
    /// </summary>
    /// <typeparam name="TEvent">The event contract type.</typeparam>
    private sealed class ChannelStreamEntry<TEvent> : IChannelStreamEntry where TEvent : notnull
    {
        /// <summary>
        /// Gets the underlying bounded event channel instance.
        /// </summary>
        public Channel<TEvent> Channel { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelStreamEntry{TEvent}"/> class.
        /// </summary>
        /// <param name="channel">The underlying bounded channel instance.</param>
        public ChannelStreamEntry(Channel<TEvent> channel)
        {
            Channel = channel;
        }

        /// <inheritdoc/>
        public void Complete()
        {
            Channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if the bus has been closed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    /// <summary>
    /// Disposable token returned by <see cref="Subscribe{TEvent}"/> that unregisters the handler on disposal.
    /// </summary>
    private sealed class SubscriptionToken : IDisposable
    {
        /// <summary>
        /// Action invoked upon disposal to detach the subscription.
        /// </summary>
        private Action? _unsubscribe;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionToken"/> class with the specified unsubscribe action.
        /// </summary>
        /// <param name="unsubscribe">The cleanup action to execute on disposal.</param>
        public SubscriptionToken(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Action? action = Interlocked.Exchange(ref _unsubscribe, null);
            action?.Invoke();
        }
    }
}
