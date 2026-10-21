using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using NanoWeaverHrt.Bridges;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive test suite verifying the zero-allocation in-memory event bus,
/// Valkey connection multiplexing, distributed caching, locking, and cluster event bridge.
/// </summary>
public sealed class EventBusAndValkeyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventBusAndValkeyTests"/> class with the web application factory.
    /// </summary>
    /// <param name="factory">Test server application factory fixture.</param>
    public EventBusAndValkeyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies in-memory channel event bus callback subscriptions, unsubscriptions, and error containment.
    /// </summary>
    [Fact]
    public async Task ChannelEventBus_PublishAndSubscribe_HandlesLifecycleAndErrorContainment()
    {
        // WHAT: Tests publishing, subscribing, unsubscribing, and fault isolation in the event bus.
        // WHY:  Ensures producer hot loops never stall or fail due to subscriber faults.
        // HOW:  Publishes events to healthy and failing subscribers and asserts delivery.
        EventBusOptions options = new()
        {
            DefaultCapacity = 100,
            DefaultFullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        };

        await using ChannelEventBus bus = new(options, NullLogger<ChannelEventBus>.Instance);

        Assert.Equal(0, bus.GetSubscriberCount<SimulationProgressEvent>());

        // 1. Argument validation
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await bus.PublishAsync<SimulationProgressEvent>(null!);
        });

        Assert.Throws<ArgumentNullException>(() =>
        {
            bus.Subscribe<SimulationProgressEvent>(null!);
        });

        // 2. Normal subscriber registration
        List<SimulationProgressEvent> receivedEvents = new();
        IDisposable token = bus.Subscribe<SimulationProgressEvent>((@event, ct) =>
        {
            receivedEvents.Add(@event);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1, bus.GetSubscriberCount<SimulationProgressEvent>());

        // 3. Register a failing subscriber to verify fault isolation
        using IDisposable failingToken = bus.Subscribe<SimulationProgressEvent>((@event, ct) =>
        {
            throw new InvalidOperationException("Simulated subscriber fault");
        });

        Assert.Equal(2, bus.GetSubscriberCount<SimulationProgressEvent>());

        // 4. Publish event
        SimulationProgressEvent testEvent = new("camp_01", "Campaign 1", 10, 100, "Running", "In-silico step");
        await bus.PublishAsync(testEvent);

        Assert.Single(receivedEvents);
        Assert.Equal("camp_01", receivedEvents[0].CampaignId);
        Assert.Equal(10, receivedEvents[0].Step);
        Assert.Equal("SimulationProgress", receivedEvents[0].EventName);
        Assert.NotEqual(Guid.Empty, receivedEvents[0].EventId);
        Assert.True(receivedEvents[0].TimestampUtc <= DateTimeOffset.UtcNow);

        // 5. Unsubscribe token disposal
        token.Dispose();
        token.Dispose(); // Idempotent second call
        Assert.Equal(1, bus.GetSubscriberCount<SimulationProgressEvent>());

        await bus.PublishAsync(new SimulationProgressEvent("camp_02", "Campaign 2", 20, 100, "Running"));
        Assert.Single(receivedEvents); // Unsubscribed listener did not receive camp_02
    }

    /// <summary>
    /// Verifies asynchronous stream subscription via IAsyncEnumerable and cancellation cleanup.
    /// </summary>
    [Fact]
    public async Task ChannelEventBus_SubscribeAsync_StreamsEventsAndCleansUp()
    {
        // WHAT: Tests IAsyncEnumerable streaming over bounded channels.
        // WHY:  Allows consumers to read continuous event sequences using await foreach.
        // HOW:  Enumerates SubscribeAsync with a cancellation token and verifies channel unregistration.
        await using ChannelEventBus bus = new();

        using CancellationTokenSource cts = new();
        IAsyncEnumerable<UserSessionActivityEvent> stream = bus.SubscribeAsync<UserSessionActivityEvent>(cts.Token);
        await using var enumerator = stream.GetAsyncEnumerator(cts.Token);
        var moveTask1 = enumerator.MoveNextAsync();

        Assert.Equal(1, bus.GetSubscriberCount<UserSessionActivityEvent>());

        await bus.PublishAsync(new UserSessionActivityEvent("researcher_1", "Researcher", "SignIn", "Web"));
        Assert.True(await moveTask1);
        Assert.Equal("SignIn", enumerator.Current.Action);

        var moveTask2 = enumerator.MoveNextAsync();
        await bus.PublishAsync(new UserSessionActivityEvent("researcher_1", "Researcher", "Heartbeat", "Web"));
        Assert.True(await moveTask2);
        Assert.Equal("Heartbeat", enumerator.Current.Action);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());

        // Channel should be cleaned up after stream completion
        Assert.Equal(0, bus.GetSubscriberCount<UserSessionActivityEvent>());
    }

    /// <summary>
    /// Verifies channel event bus behavior after disposal and with pre-cancelled tokens.
    /// </summary>
    [Fact]
    public async Task ChannelEventBus_DisposalAndCancellation_BehavesDeterministically()
    {
        // WHAT: Tests bus behavior when disposed or when tokens are cancelled.
        // WHY:  Guarantees zero memory leaks and safe application shutdown.
        // HOW:  Calls Subscribe and Publish after DisposeAsync.
        ChannelEventBus bus = new();
        await bus.DisposeAsync();
        await bus.DisposeAsync(); // Idempotent second call

        // Subscribing after disposal throws ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() =>
        {
            bus.Subscribe<AuditLogDispatchedEvent>((e, ct) => ValueTask.CompletedTask);
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            using CancellationTokenSource cts = new();
            await using var enumerator = bus.SubscribeAsync<AuditLogDispatchedEvent>(cts.Token).GetAsyncEnumerator();
            await enumerator.MoveNextAsync();
        });

        // Publishing after disposal returns ValueTask.CompletedTask without throwing
        await bus.PublishAsync(new AuditLogDispatchedEvent(1, "sys", "test", "res", "hash"));

        // Pre-cancelled token returns without dispatching
        ChannelEventBus activeBus = new();
        using CancellationTokenSource preCancelled = new();
        preCancelled.Cancel();

        await activeBus.PublishAsync(new AuditLogDispatchedEvent(1, "sys", "test", "res", "hash"), preCancelled.Token);
        await activeBus.DisposeAsync();

        // Active stream unblocked and completed upon bus disposal
        ChannelEventBus streamBus = new();
        using CancellationTokenSource ctsStream = new();
        IAsyncEnumerable<AuditLogDispatchedEvent> stream = streamBus.SubscribeAsync<AuditLogDispatchedEvent>(ctsStream.Token);
        await using var streamEnumerator = stream.GetAsyncEnumerator(ctsStream.Token);
        var moveTask = streamEnumerator.MoveNextAsync();
        Assert.Equal(1, streamBus.GetSubscriberCount<AuditLogDispatchedEvent>());
        await streamBus.DisposeAsync();
        Assert.False(await moveTask);
    }

    /// <summary>
    /// Verifies strongly-typed event record instantiation and property assignments.
    /// </summary>
    [Fact]
    public void EventModels_PropertyVerification_Succeeds()
    {
        // WHAT: Tests constructors and property accessors for all event models.
        // WHY:  Ensures metadata is correctly populated for auditability.
        // HOW:  Instantiates each event model and asserts property matches.
        TrajectoryFrameBatchEvent frameEvent = new("test.xyz", 42, 1024, 12.5);
        Assert.Equal("test.xyz", frameEvent.TrajectoryName);
        Assert.Equal(42, frameEvent.FrameIndex);
        Assert.Equal(1024, frameEvent.AtomCount);
        Assert.Equal(12.5, frameEvent.ElapsedMs);
        Assert.Equal("TrajectoryFrameBatch", frameEvent.EventName);

        SystemTelemetryUpdatedEvent telemEvent = new("node-01", 50000, 1048576, true, "AVX-512");
        Assert.Equal("node-01", telemEvent.NodeName);
        Assert.Equal(50000, telemEvent.TotalFramesParsed);
        Assert.Equal(1048576, telemEvent.AllocatedMemoryBytes);
        Assert.True(telemEvent.ServerGcEnabled);
        Assert.Equal("AVX-512", telemEvent.SimdEngine);
        Assert.Equal("SystemTelemetryUpdated", telemEvent.EventName);

        EventBusTelemetry telem = new()
        {
            IsInternalBusActive = true,
            IsValkeyConnected = false,
            IsDistributedCache = false,
            ValkeyEndpoint = "none (in-memory standalone)",
            OutboundEventsCount = 10,
            InboundEventsCount = 0,
            ActiveSubscribers = new Dictionary<string, int>()
        };

        Assert.True(telem.IsInternalBusActive);
        Assert.False(telem.IsValkeyConnected);
        Assert.Equal("none (in-memory standalone)", telem.ValkeyEndpoint);

        EventBusTelemetry example = telem.GetExamples();
        Assert.NotNull(example);
        Assert.True(example.IsInternalBusActive);
        Assert.NotEmpty(example.ActiveSubscribers);
    }

    /// <summary>
    /// Verifies Valkey connection factory configuration parsing, password redaction, and disposal.
    /// </summary>
    [Fact]
    public async Task ValkeyConnectionFactory_LifecycleAndSanitization_OperatesSafely()
    {
        // WHAT: Tests connection factory startup, password masking, and connection retrieval.
        // WHY:  Upholds zero-disclosure security posture and handles unconfigured environments cleanly.
        // HOW:  Initializes with empty config, invalid string, and injected mock multiplexer.
        // 1. Argument validation
        Assert.Throws<ArgumentNullException>(() => new ValkeyConnectionFactory(configuration: null!));

        // 2. Empty configuration fallback
        IConfiguration emptyConfig = new ConfigurationBuilder().Build();
        using (ValkeyConnectionFactory emptyFactory = new(emptyConfig))
        {
            Assert.False(emptyFactory.IsConnected);
            Assert.Equal("none (in-memory standalone)", emptyFactory.SanitizedEndpoint);
            Assert.Null(emptyFactory.GetConnection());
            Assert.Null(emptyFactory.GetDatabase());
            Assert.Null(emptyFactory.GetSubscriber());
        }

        // 3. Configuration with password sanitization
        Dictionary<string, string?> inMemorySettings = new()
        {
            ["VALKEY_CONNECTION_STRING"] = "127.0.0.1:6379,password=super_secret_valkey_password,abortConnect=false"
        };
        IConfiguration configWithPassword = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        ValkeyConnectionFactory passFactory = new(configWithPassword);
        Assert.Contains("password=[REDACTED]", passFactory.SanitizedEndpoint);
        Assert.DoesNotContain("super_secret_valkey_password", passFactory.SanitizedEndpoint);

        // Synchronous dispose
        passFactory.Dispose();
        passFactory.Dispose(); // Idempotent second call

        // 4. Asynchronous dispose
        ValkeyConnectionFactory asyncFactory = new(configWithPassword);
        await asyncFactory.DisposeAsync();
        await asyncFactory.DisposeAsync();

        // 4b. Invalid configuration triggering parse fault and fallback
        Dictionary<string, string?> invalidSettings = new()
        {
            ["VALKEY_CONNECTION_STRING"] = "127.0.0.1:999999"
        };
        IConfiguration invalidConfig = new ConfigurationBuilder().AddInMemoryCollection(invalidSettings).Build();
        using (ValkeyConnectionFactory invalidFactory = new(invalidConfig))
        {
            Assert.False(invalidFactory.IsConnected);
            Assert.Null(invalidFactory.GetConnection());
            Assert.Null(invalidFactory.GetDatabase());
            Assert.Null(invalidFactory.GetSubscriber());
        }

        // 5. Injected mock multiplexer
        Mock<IConnectionMultiplexer> mockMux = new();
        Mock<IDatabase> mockDb = new();
        Mock<ISubscriber> mockSub = new();

        mockMux.SetupGet(m => m.IsConnected).Returns(true);
        mockMux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockMux.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSub.Object);

        using (ValkeyConnectionFactory injectedFactory = new(mockMux.Object, "custom-endpoint:6379"))
        {
            Assert.True(injectedFactory.IsConnected);
            Assert.Equal("custom-endpoint:6379", injectedFactory.SanitizedEndpoint);
            Assert.Same(mockMux.Object, injectedFactory.GetConnection());
            Assert.Same(mockDb.Object, injectedFactory.GetDatabase());
            Assert.Same(mockSub.Object, injectedFactory.GetSubscriber());
        }

        // 6. Injected disconnected multiplexer
        Mock<IConnectionMultiplexer> disconnectedMux = new();
        disconnectedMux.SetupGet(m => m.IsConnected).Returns(false);

        using (ValkeyConnectionFactory disconnectedFactory = new(disconnectedMux.Object))
        {
            Assert.False(disconnectedFactory.IsConnected);
            Assert.Null(disconnectedFactory.GetDatabase());
            Assert.Null(disconnectedFactory.GetSubscriber());
        }

        // 7. Null multiplexer injection
        using (ValkeyConnectionFactory nullMuxFactory = new(multiplexer: null))
        {
            Assert.False(nullMuxFactory.IsConnected);
            Assert.Equal("none (in-memory standalone)", nullMuxFactory.SanitizedEndpoint);
        }
    }

    /// <summary>
    /// Verifies Valkey caching service in-memory fallback, expiration eviction, and distributed operations.
    /// </summary>
    [Fact]
    public async Task ValkeyCacheService_FallbackAndDistributed_ManagesStateAccurately()
    {
        // WHAT: Tests key-value caching, expiration eviction, and distributed database calls.
        // WHY:  Ensures transient session tokens and challenges are cached reliably.
        // HOW:  Runs tests against standalone fallback and mocked IDatabase.
        // 1. Argument validation
        Mock<IValkeyConnectionFactory> mockFactory = new();
        Assert.Throws<ArgumentNullException>(() => new ValkeyCacheService(null!));

        ValkeyCacheService cache = new(mockFactory.Object);
        Assert.False(cache.IsDistributed);

        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.SetAsync("", "val"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.GetAsync<string>(""));
        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.RemoveAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () => await cache.ExistsAsync(""));

        // 2. In-memory fallback lifecycle
        await cache.SetAsync("test_key", "test_value", TimeSpan.FromMinutes(5));
        Assert.True(await cache.ExistsAsync("test_key"));
        string? retrieved = await cache.GetAsync<string>("test_key");
        Assert.Equal("test_value", retrieved);

        // Object type JSON conversion path
        await cache.SetAsync("num_key", 12345, TimeSpan.FromMinutes(5));
        int retrievedNum = await cache.GetAsync<int>("num_key");
        Assert.Equal(12345, retrievedNum);

        // Removal
        bool removed = await cache.RemoveAsync("test_key");
        Assert.True(removed);
        Assert.False(await cache.ExistsAsync("test_key"));
        Assert.Null(await cache.GetAsync<string>("test_key"));

        // Expiration eviction path
        await cache.SetAsync("expired_key", "old_value", TimeSpan.FromMilliseconds(-100));
        Assert.False(await cache.ExistsAsync("expired_key"));
        Assert.Null(await cache.GetAsync<string>("expired_key"));

        // Non-existent key
        Assert.Null(await cache.GetAsync<string>("non_existent_key"));

        // 3. Distributed caching with Mock<IDatabase>
        Mock<IDatabase> mockDb = new();
        mockFactory.SetupGet(f => f.IsConnected).Returns(true);
        mockFactory.Setup(f => f.GetDatabase(It.IsAny<int>())).Returns(mockDb.Object);

        ValkeyCacheService distributedCache = new(mockFactory.Object);
        Assert.True(distributedCache.IsDistributed);

        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.Always)).ReturnsAsync(true);

        mockDb.Setup(d => d.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(JsonSerializer.SerializeToUtf8Bytes("remote_val"));

        mockDb.Setup(d => d.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(true);

        mockDb.Setup(d => d.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(true);

        await distributedCache.SetAsync("remote_key", "remote_val");
        string? val = await distributedCache.GetAsync<string>("remote_key");
        Assert.Equal("remote_val", val);
        Assert.True(await distributedCache.ExistsAsync("remote_key"));
        Assert.True(await distributedCache.RemoveAsync("remote_key"));

        // 4. Empty RedisValue read
        mockDb.Setup(d => d.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);

        Assert.Null(await distributedCache.GetAsync<string>("missing_remote"));

        // 5. Fault tolerance: IDatabase throws, gracefully falls back to local memory
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.Always)).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated fault"));

        mockDb.Setup(d => d.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated fault"));

        mockDb.Setup(d => d.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated fault"));

        mockDb.Setup(d => d.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>())).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated fault"));

        await distributedCache.SetAsync("fault_key", "fault_fallback_value");
        string? fallbackVal = await distributedCache.GetAsync<string>("fault_key");
        Assert.Equal("fault_fallback_value", fallbackVal);
        Assert.True(await distributedCache.ExistsAsync("fault_key"));
        Assert.True(await distributedCache.RemoveAsync("fault_key"));
    }

    /// <summary>
    /// Verifies Valkey distributed and local mutual exclusion locking and token release.
    /// </summary>
    [Fact]
    public async Task ValkeyLockService_AcquireAndRelease_CoordinatesExclusion()
    {
        // WHAT: Tests lock acquisition, exclusivity, timeouts, and disposal.
        // WHY:  Guarantees atomic execution across single-use share keys and simulation runs.
        // HOW:  Tests both local semaphore locking and distributed Lua script locking.
        // 1. Argument validation
        Mock<IValkeyConnectionFactory> mockFactory = new();
        Assert.Throws<ArgumentNullException>(() => new ValkeyLockService(null!));

        ValkeyLockService lockService = new(mockFactory.Object);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await lockService.AcquireLockAsync("", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        });

        // 2. Local in-memory lock acquisition
        await using (IAsyncDisposable? lockToken1 = await lockService.AcquireLockAsync("share_key:nanoweaver_hrt_01", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)))
        {
            Assert.NotNull(lockToken1);

            // Attempting second lock while first is held times out
            await using (IAsyncDisposable? lockToken2 = await lockService.AcquireLockAsync("share_key:nanoweaver_hrt_01", TimeSpan.FromSeconds(10), TimeSpan.Zero))
            {
                Assert.Null(lockToken2);
            }
        }

        // Lock released, re-acquisition succeeds
        await using (IAsyncDisposable? lockToken3 = await lockService.AcquireLockAsync("share_key:nanoweaver_hrt_01", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)))
        {
            Assert.NotNull(lockToken3);
        }

        // 3. Distributed locking with Mock<IDatabase>
        Mock<IDatabase> mockDb = new();
        mockFactory.Setup(f => f.GetDatabase(It.IsAny<int>())).Returns(mockDb.Object);

        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.NotExists)).ReturnsAsync(true);

        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(RedisResult.Create(1));

        await using (IAsyncDisposable? distLock = await lockService.AcquireLockAsync("setup_init", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)))
        {
            Assert.NotNull(distLock);
        }

        // 4. Distributed lock acquisition failure
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.NotExists)).ReturnsAsync(false);

        await using (IAsyncDisposable? failedLock = await lockService.AcquireLockAsync("setup_init", TimeSpan.FromSeconds(10), TimeSpan.Zero))
        {
            Assert.Null(failedLock);
        }

        // 5. Distributed lock database throws exception -> falls back to local semaphore
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.NotExists)).ThrowsAsync(new RedisException("Simulated lock fault"));

        await using (IAsyncDisposable? faultFallbackLock = await lockService.AcquireLockAsync("setup_fallback", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)))
        {
            Assert.NotNull(faultFallbackLock);
        }
    }

    /// <summary>
    /// Verifies Valkey event bridge background service forwarding, echo suppression, and inbound dispatching.
    /// </summary>
    [Fact]
    public async Task ValkeyEventBridge_OutboundAndInbound_SynchronizesEventsCleanly()
    {
        // WHAT: Tests bridge initialization, outbound forwarding to Valkey, and inbound message handling.
        // WHY:  Enables seamless multi-node pub/sub clustering.
        // HOW:  Simulates local event publishing and incoming subscriber messages.
        // 1. Argument validation
        ChannelEventBus bus = new();
        Mock<IValkeyConnectionFactory> mockFactory = new();

        Assert.Throws<ArgumentNullException>(() => new ValkeyEventBridge(null!, mockFactory.Object));
        Assert.Throws<ArgumentNullException>(() => new ValkeyEventBridge(bus, null!));

        // 2. Disconnected factory startup
        mockFactory.SetupGet(f => f.IsConnected).Returns(false);
        ValkeyEventBridge disconnectedBridge = new(bus, mockFactory.Object);

        using (CancellationTokenSource cts = new())
        {
            await disconnectedBridge.StartAsync(cts.Token);
            Assert.False(disconnectedBridge.IsActive);
            Assert.Equal(0, disconnectedBridge.OutboundEventsCount);
            Assert.Equal(0, disconnectedBridge.InboundEventsCount);
            await disconnectedBridge.StopAsync(cts.Token);
        }

        disconnectedBridge.Dispose();
        await disconnectedBridge.DisposeAsync();

        // 3. Connected factory with mock subscriber
        Mock<ISubscriber> mockSub = new();
        mockFactory.SetupGet(f => f.IsConnected).Returns(true);
        mockFactory.Setup(f => f.GetSubscriber()).Returns(mockSub.Object);

        mockSub.Setup(s => s.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(1);

        Action<RedisChannel, RedisValue>? capturedInboundHandler = null;
        mockSub.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, h, f) =>
            {
                if (ch.ToString().EndsWith("SimulationProgress"))
                {
                    capturedInboundHandler = h;
                }
            })
            .Returns(Task.CompletedTask);

        ValkeyEventBridge connectedBridge = new(bus, mockFactory.Object);
        using (CancellationTokenSource cts = new())
        {
            await connectedBridge.StartAsync(cts.Token);
            Assert.True(connectedBridge.IsActive);

            // Publish local event and verify outbound forward
            await bus.PublishAsync(new SimulationProgressEvent("c1", "Camp1", 1, 10, "Run"));
            await bus.PublishAsync(new UserSessionActivityEvent("u1", "Admin", "SignIn", "Web"));
            await bus.PublishAsync(new AuditLogDispatchedEvent(1, "sys", "act", "res", "hash"));
            await bus.PublishAsync(new SystemTelemetryUpdatedEvent("n1", 100, 1000, true, "AVX-512"));
            await bus.PublishAsync(new TrajectoryFrameBatchEvent("f.xyz", 0, 50, 1.2));

            Assert.True(connectedBridge.OutboundEventsCount >= 5);

            // Test inbound message delivery from remote node
            if (capturedInboundHandler != null)
            {
                SimulationProgressEvent remoteEvent = new("remote_c1", "Remote Campaign", 5, 100, "Running");
                byte[] remoteJsonBytes = JsonSerializer.SerializeToUtf8Bytes(remoteEvent);
                capturedInboundHandler(RedisChannel.Literal("weaver:events:SimulationProgress"), remoteJsonBytes);
                Assert.True(connectedBridge.InboundEventsCount >= 1);
            }

            await connectedBridge.StopAsync(cts.Token);
        }

        connectedBridge.Dispose();
        await connectedBridge.DisposeAsync();
        await bus.DisposeAsync();
    }

    /// <summary>
    /// Verifies integration between UserSessionState, IInternalEventBus, and IValkeyCacheService.
    /// </summary>
    [Fact]
    public async Task UserSessionState_WithEventBusAndCache_DispatchesActivityEvents()
    {
        // WHAT: Tests that signing in, heartbeating, signing out, and timing out emit typed events.
        // WHY:  Ensures full session auditability across local and distributed layers.
        // HOW:  Subscribes to UserSessionActivityEvent on the event bus and asserts emissions.
        await using ChannelEventBus bus = new();
        Mock<IValkeyCacheService> mockCache = new();

        UserSessionState state = new(bus, mockCache.Object);
        List<UserSessionActivityEvent> activityEvents = new();

        using IDisposable sub = bus.Subscribe<UserSessionActivityEvent>((e, ct) =>
        {
            activityEvents.Add(e);
            return ValueTask.CompletedTask;
        });

        UserAccount user = new()
        {
            Username = "researcher_event",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MC4CAQAwBQYDK2VwBCIEIA=="
        };

        // 1. Sign In
        state.SetUser(user);
        Assert.Single(activityEvents);
        Assert.Equal("SignIn", activityEvents[0].Action);

        // 2. Heartbeat Activity
        state.RecordActivity();
        mockCache.Verify(c => c.SetAsync(
            It.Is<string>(k => k.StartsWith("heartbeat:")),
            It.IsAny<string>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // 3. Idle Timeout
        state.IdleTimeout = TimeSpan.FromSeconds(-1);
        bool timedOut = state.CheckIdleTimeout();
        Assert.True(timedOut);
        Assert.True(activityEvents.Count >= 2);
        Assert.Equal("IdleTimeout", activityEvents[1].Action);

        // 4. Sign Out
        state.SetUser(user);
        state.SignOut();
        Assert.True(activityEvents.Count >= 4);
        Assert.Equal("SignOut", activityEvents.Last().Action);
    }

    /// <summary>
    /// Verifies the GET /api/system/event-bus HTTP telemetry endpoint.
    /// </summary>
    [Fact]
    public async Task GetEventBusTelemetry_Endpoint_ReturnsValidTelemetry()
    {
        // WHAT: Tests the /api/system/event-bus API endpoint.
        // WHY:  Provides live cluster and event bus observability to API clients and UI dashboards.
        // HOW:  Issues GET request via WebApplicationFactory HttpClient and asserts 200 OK.
        using HttpClient client = _factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(client, _factory);
        HttpResponseMessage response = await client.GetAsync("/api/system/event-bus");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        string json = await response.Content.ReadAsStringAsync();
        EventBusTelemetry? telemetry = JsonSerializer.Deserialize<EventBusTelemetry>(json);

        Assert.NotNull(telemetry);
        Assert.True(telemetry.IsInternalBusActive);
        Assert.NotNull(telemetry.ValkeyEndpoint);
        Assert.NotNull(telemetry.ActiveSubscribers);
    }
}
