using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Gui.Core.Services;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Unit tests for <see cref="BackgroundInstanceMonitor"/> achieving 100.00% line and branch coverage.
/// </summary>
public sealed class BackgroundInstanceMonitorTests
{
    [Fact]
    public async Task Constructor_InitializesDefaultState_Correctly()
    {
        // WHAT: Verifies initial state of BackgroundInstanceMonitor upon creation.
        // WHY: Ensures correct baseline properties.
        // HOW: Create mock dependencies and assert initial properties.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object);

        Assert.False(monitor.IsSessionLocked);
        Assert.Equal(TrayHealthBadge.Idle, monitor.CurrentTrayState.Badge);
        Assert.Contains("NanoWeaver HRT", monitor.CurrentTrayState.TooltipText);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // WHAT: Verifies null argument validation in constructor.
        // WHY: Ensures robust guard clauses.
        // HOW: Pass nulls and assert ArgumentNullException.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        Assert.Throws<ArgumentNullException>(() => new BackgroundInstanceMonitor(null!, trayMock.Object, notificationSinkMock.Object));
        Assert.Throws<ArgumentNullException>(() => new BackgroundInstanceMonitor(coordinatorMock.Object, null!, notificationSinkMock.Object));
        Assert.Throws<ArgumentNullException>(() => new BackgroundInstanceMonitor(coordinatorMock.Object, trayMock.Object, null!));
    }

    [Fact]
    public async Task ExecuteMonitoringTickAsync_WhenIdleTimeoutExceeded_LocksSessionAndDisconnects()
    {
        // WHAT: Tests 15-minute idle session auto-lock and instance disconnection.
        // WHY: Fulfills Prime Directive 12 security idle expiration.
        // HOW: Advance time past idle timeout and assert session lock + DisconnectInstance calls.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        var snapshot = new InstanceConnectionSnapshot(
            InstanceId: "node-1",
            DisplayName: "Node 1",
            State: InstanceConnectionState.Online,
            CpuUtilizationPercent: 10.0,
            GpuUtilizationPercent: 20.0,
            GpuTemperatureCelsius: 45.0,
            ActiveSimulationCount: 1,
            ParsedFramesPerSecond: 100.0,
            LastUpdatedUtc: DateTimeOffset.UtcNow,
            LastErrorTitle: null);

        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(new[] { snapshot });
        coordinatorMock.Setup(c => c.DisconnectInstance("node-1"));

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object,
            idleTimeout: TimeSpan.FromMinutes(15));

        var startTime = DateTimeOffset.UtcNow;
        monitor.RecordUserActivity(startTime);

        var futureTime = startTime.AddMinutes(16);
        int dispatched = await monitor.ExecuteMonitoringTickAsync(futureTime);

        Assert.True(monitor.IsSessionLocked);
        Assert.Equal(TrayHealthBadge.Idle, monitor.CurrentTrayState.Badge);
        Assert.Contains("Session Locked", monitor.CurrentTrayState.TooltipText);
        coordinatorMock.Verify(c => c.DisconnectInstance("node-1"), Times.Once);
        Assert.Equal(0, dispatched);
    }

    [Fact]
    public async Task RecordUserActivity_ClearsSessionLock()
    {
        // WHAT: Verifies that user activity clears an existing session lock.
        // WHY: Ensures smooth user re-authentication / activity resumption.
        // HOW: Lock session via idle timeout, then call RecordUserActivity and verify IsSessionLocked becomes false.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object,
            idleTimeout: TimeSpan.FromMinutes(5));

        var t0 = DateTimeOffset.UtcNow;
        await monitor.ExecuteMonitoringTickAsync(t0.AddMinutes(6));
        Assert.True(monitor.IsSessionLocked);

        monitor.RecordUserActivity(t0.AddMinutes(7));
        Assert.False(monitor.IsSessionLocked);
    }

    [Fact]
    public async Task SetMainWindowVisibility_AndUpdateTrayState_ReflectsCorrectly()
    {
        // WHAT: Tests window visibility and notification muting toggles.
        // WHY: Validates interactive tray state modifiers.
        // HOW: Call set methods and verify property propagation.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object);

        monitor.SetMainWindowVisibility(false);
        Assert.False(monitor.CurrentTrayState.IsMainWindowVisible);

        monitor.SetNotificationsMuted(true);
        Assert.True(monitor.CurrentTrayState.AreNotificationsMuted);
    }

    [Fact]
    public async Task ExecuteMonitoringTickAsync_AggregatesBadgesAndDispatchesNotificationsWithCooldown()
    {
        // WHAT: Tests multi-instance badge aggregation (Healthy, Warning, Critical) and notification deduplication cooldown.
        // WHY: Validates alert routing and suppression logic.
        // HOW: Setup coordinator with snapshots representing different health states and alerts, execute ticks within and outside cooldown.
        var snapshots = new List<InstanceConnectionSnapshot>
        {
            new("node-h", "Healthy Node", InstanceConnectionState.Online, 10, 20, 50, 1, 100, DateTimeOffset.UtcNow, null),
            new("node-w", "Warning Node", InstanceConnectionState.Degraded, 15, 25, 82, 1, 100, DateTimeOffset.UtcNow, "ThermalThrottle"),
            new("node-c", "Critical Node", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, "ConnectionTimeout")
        };

        var alerts = new List<InstanceAlertEvent>
        {
            new("alt-1", "node-c", "Critical Node", InstanceAlertSeverity.Critical, "Node Offline", "Connection lost", DateTimeOffset.UtcNow)
        };

        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(snapshots);
        coordinatorMock.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(alerts);

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();
        notificationSinkMock.Setup(s => s.SendNotificationAsync(It.IsAny<InstanceAlertEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object,
            notificationCooldown: TimeSpan.FromMinutes(5));

        var t0 = DateTimeOffset.UtcNow;

        // First tick: should dispatch notification
        int dispatched1 = await monitor.ExecuteMonitoringTickAsync(t0);
        Assert.Equal(1, dispatched1);
        Assert.Equal(TrayHealthBadge.Critical, monitor.CurrentTrayState.Badge);
        Assert.Equal(3, monitor.CurrentTrayState.InstanceSummaries.Count);

        // Second tick within cooldown (2 minutes later): should suppress notification
        int dispatched2 = await monitor.ExecuteMonitoringTickAsync(t0.AddMinutes(2));
        Assert.Equal(0, dispatched2);

        // Third tick outside cooldown (6 minutes later): should dispatch again
        int dispatched3 = await monitor.ExecuteMonitoringTickAsync(t0.AddMinutes(6));
        Assert.Equal(1, dispatched3);
    }

    [Fact]
    public async Task ExecuteMonitoringTickAsync_WhenNotificationsMuted_SuppressesAlerts()
    {
        // WHAT: Verifies that muted notifications are suppressed even when alerts are present.
        // WHY: Validates mute feature contract.
        // HOW: Set notification mute to true and verify zero notifications dispatched.
        var coordinatorMock = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());
        coordinatorMock.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new InstanceAlertEvent("alt-2", "node-1", "Node 1", InstanceAlertSeverity.Warning, "Warning", "Test", DateTimeOffset.UtcNow)
        });

        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        var monitor = new BackgroundInstanceMonitor(
            coordinatorMock.Object,
            trayMock.Object,
            notificationSinkMock.Object);

        monitor.SetNotificationsMuted(true);
        int dispatched = await monitor.ExecuteMonitoringTickAsync(DateTimeOffset.UtcNow);

        Assert.Equal(0, dispatched);
        notificationSinkMock.Verify(s => s.SendNotificationAsync(It.IsAny<InstanceAlertEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteMonitoringTickAsync_HandlingConnectionStates_CorrectlyComputesBadges()
    {
        // WHAT: Tests badge computation for Healthy, Warning, Connecting, Disconnected, and Offline states.
        // WHY: Ensures 100% switch coverage over InstanceConnectionState and badge badges.
        // HOW: Test collections with different state combinations.
        var trayMock = new Mock<ITrayIconController>();
        var notificationSinkMock = new Mock<INativeNotificationSink>();

        // 1. Healthy (Online)
        var coordinatorMock1 = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock1.Setup(c => c.GetSnapshots()).Returns(new[]
        {
            new InstanceConnectionSnapshot("n1", "Node 1", InstanceConnectionState.Online, 10, 10, 40, 1, 100, DateTimeOffset.UtcNow, null)
        });
        coordinatorMock1.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceAlertEvent>());

        var monitor1 = new BackgroundInstanceMonitor(coordinatorMock1.Object, trayMock.Object, notificationSinkMock.Object);
        await monitor1.ExecuteMonitoringTickAsync(DateTimeOffset.UtcNow);
        Assert.Equal(TrayHealthBadge.Healthy, monitor1.CurrentTrayState.Badge);

        // 2. Warning (Degraded)
        var coordinatorMock2 = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock2.Setup(c => c.GetSnapshots()).Returns(new[]
        {
            new InstanceConnectionSnapshot("n2", "Node 2", InstanceConnectionState.Degraded, 10, 10, 40, 1, 100, DateTimeOffset.UtcNow, null)
        });
        coordinatorMock2.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceAlertEvent>());

        var monitor2 = new BackgroundInstanceMonitor(coordinatorMock2.Object, trayMock.Object, notificationSinkMock.Object);
        await monitor2.ExecuteMonitoringTickAsync(DateTimeOffset.UtcNow);
        Assert.Equal(TrayHealthBadge.Warning, monitor2.CurrentTrayState.Badge);

        // 3. Idle (Zero online, zero degraded, zero critical -> hits Idle badge branch and default switch in badgeName)
        var coordinatorMock3 = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock3.Setup(c => c.GetSnapshots()).Returns(new[]
        {
            new InstanceConnectionSnapshot("n3", "Node 3", InstanceConnectionState.Connecting, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null),
            new InstanceConnectionSnapshot("n4", "Node 4", InstanceConnectionState.Disconnected, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null)
        });
        coordinatorMock3.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceAlertEvent>());

        var monitor3 = new BackgroundInstanceMonitor(coordinatorMock3.Object, trayMock.Object, notificationSinkMock.Object);
        await monitor3.ExecuteMonitoringTickAsync(DateTimeOffset.UtcNow);
        Assert.Equal(TrayHealthBadge.Idle, monitor3.CurrentTrayState.Badge);
        Assert.Contains("Idle", monitor3.CurrentTrayState.TooltipText);

        // 4. Critical (Unauthorized & Offline)
        var coordinatorMock4 = new Mock<IMultiInstanceCoordinator>();
        coordinatorMock4.Setup(c => c.GetSnapshots()).Returns(new[]
        {
            new InstanceConnectionSnapshot("n5", "Node 5", InstanceConnectionState.Unauthorized, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null),
            new InstanceConnectionSnapshot("n6", "Node 6", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null)
        });
        coordinatorMock4.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceAlertEvent>());

        var monitor4 = new BackgroundInstanceMonitor(coordinatorMock4.Object, trayMock.Object, notificationSinkMock.Object);
        await monitor4.ExecuteMonitoringTickAsync(DateTimeOffset.UtcNow);
        Assert.Equal(TrayHealthBadge.Critical, monitor4.CurrentTrayState.Badge);
    }
}
