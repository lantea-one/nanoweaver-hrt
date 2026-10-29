using System.Collections.Concurrent;
using NanoWeaverHrt.Client.Api;

namespace NanoWeaverHrt.Gui.Core.Services;

/// <summary>
/// Implementation of <see cref="IBackgroundInstanceMonitor"/> managing multi-instance polling, notification routing,
/// cooldown deduplication, and 15-minute idle session auto-locking.
/// </summary>
public sealed class BackgroundInstanceMonitor : IBackgroundInstanceMonitor
{
    /// <summary>
    /// Default idle timeout duration before locking the session (15 minutes).
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Default notification deduplication cooldown window (5 minutes).
    /// </summary>
    public static readonly TimeSpan DefaultNotificationCooldown = TimeSpan.FromMinutes(5);

    private readonly IMultiInstanceCoordinator _coordinator;
    private readonly ITrayIconController _trayController;
    private readonly INativeNotificationSink _notificationSink;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _notificationCooldown;

    private DateTimeOffset _lastActivityUtc;
    private bool _isSessionLocked;
    private bool _isMainWindowVisible = true;
    private bool _areNotificationsMuted;
    private TrayMenuState _currentTrayState;

    // WHAT: Tracks last notification dispatch time per deduplication key (InstanceId, Severity, Title).
    // WHY: Prevents notification floods during transient network or thermal flapping.
    // HOW: ConcurrentDictionary storing UTC timestamp of the last sent notification.
    private readonly ConcurrentDictionary<(string InstanceId, InstanceAlertSeverity Severity, string Title), DateTimeOffset> _lastNotificationTimes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundInstanceMonitor"/> class.
    /// </summary>
    /// <param name="coordinator">Multi-instance coordinator for fetching node snapshots and polling.</param>
    /// <param name="trayController">System tray icon controller for rendering state changes.</param>
    /// <param name="notificationSink">Native OS notification sink for dispatches.</param>
    /// <param name="idleTimeout">Optional custom idle timeout (defaults to 15 minutes).</param>
    /// <param name="notificationCooldown">Optional custom notification cooldown (defaults to 5 minutes).</param>
    public BackgroundInstanceMonitor(
        IMultiInstanceCoordinator coordinator,
        ITrayIconController trayController,
        INativeNotificationSink notificationSink,
        TimeSpan? idleTimeout = null,
        TimeSpan? notificationCooldown = null)
    {
        // WHAT: Assign injected services and optional configuration parameters.
        // WHY: Enables robust unit testing with accelerated timeouts.
        // HOW: Store in private fields and initialize baseline tray state.
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _trayController = trayController ?? throw new ArgumentNullException(nameof(trayController));
        _notificationSink = notificationSink ?? throw new ArgumentNullException(nameof(notificationSink));
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _notificationCooldown = notificationCooldown ?? DefaultNotificationCooldown;

        _lastActivityUtc = DateTimeOffset.UtcNow;
        _currentTrayState = new TrayMenuState(
            Badge: TrayHealthBadge.Idle,
            TooltipText: "NanoWeaver HRT — Idle",
            IsMainWindowVisible: _isMainWindowVisible,
            AreNotificationsMuted: _areNotificationsMuted,
            InstanceSummaries: Array.Empty<string>());
    }

    /// <inheritdoc />
    public TrayMenuState CurrentTrayState => _currentTrayState;

    /// <inheritdoc />
    public bool IsSessionLocked => _isSessionLocked;

    /// <inheritdoc />
    public void RecordUserActivity(DateTimeOffset nowUtc)
    {
        // WHAT: Updates the last user activity timestamp and clears session lock if locked.
        // WHY: Fulfills Prime Directive 12 interactive session activity heartbeat.
        // HOW: Set _lastActivityUtc to nowUtc and reset _isSessionLocked to false.
        _lastActivityUtc = nowUtc;
        if (_isSessionLocked)
        {
            _isSessionLocked = false;
        }
    }

    /// <inheritdoc />
    public void SetMainWindowVisibility(bool isVisible)
    {
        // WHAT: Updates main window visibility flag and synchronizes tray state.
        // WHY: Supports minimize-to-tray workflows.
        // HOW: Mutate private flag and refresh tray representation.
        _isMainWindowVisible = isVisible;
        UpdateTrayState(_coordinator.GetSnapshots());
    }

    /// <inheritdoc />
    public void SetNotificationsMuted(bool isMuted)
    {
        // WHAT: Updates notification mute preference.
        // WHY: Allows users to suppress desktop alerts.
        // HOW: Mutate private flag and refresh tray representation.
        _areNotificationsMuted = isMuted;
        UpdateTrayState(_coordinator.GetSnapshots());
    }

    /// <inheritdoc />
    public async Task<int> ExecuteMonitoringTickAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        // WHAT: Evaluates idle timeout, polls coordinator, updates tray state, and dispatches deduplicated notifications.
        // WHY: Core background daemon heartbeat logic.
        // HOW: Check idle elapsed time; if exceeded, lock session and disconnect instances; otherwise poll, compute badge, and send alerts.

        // 1. Evaluate 15-minute idle timeout
        if (nowUtc - _lastActivityUtc >= _idleTimeout)
        {
            if (!_isSessionLocked)
            {
                _isSessionLocked = true;

                // Disconnect all active instances upon idle lock per P28 specification
                var snapshots = _coordinator.GetSnapshots();
                foreach (var snapshot in snapshots)
                {
                    if (snapshot.State != InstanceConnectionState.Disconnected)
                    {
                        _coordinator.DisconnectInstance(snapshot.InstanceId);
                    }
                }
            }

            // Force tray state to Idle when session is locked
            var lockedSummaries = new[] { "Session Locked (Idle)" };
            _currentTrayState = new TrayMenuState(
                Badge: TrayHealthBadge.Idle,
                TooltipText: "NanoWeaver HRT — Session Locked",
                IsMainWindowVisible: _isMainWindowVisible,
                AreNotificationsMuted: _areNotificationsMuted,
                InstanceSummaries: lockedSummaries);

            _trayController.ApplyState(_currentTrayState);
            return 0;
        }

        // 2. If session is not locked, poll coordinator for alerts and status snapshots
        var alerts = await _coordinator.PollAllConnectedAsync(ct).ConfigureAwait(false);
        var currentSnapshots = _coordinator.GetSnapshots();

        // 3. Compute aggregate tray badge and summaries
        UpdateTrayState(currentSnapshots);

        // 4. Process and dispatch notifications if not muted
        int notificationsDispatched = 0;
        if (!_areNotificationsMuted)
        {
            foreach (var alert in alerts)
            {
                var dedupKey = (alert.InstanceId, alert.Severity, alert.Title);
                bool shouldSend = true;

                if (_lastNotificationTimes.TryGetValue(dedupKey, out var lastSentTime))
                {
                    if (nowUtc - lastSentTime < _notificationCooldown)
                    {
                        shouldSend = false;
                    }
                }

                if (shouldSend)
                {
                    bool delivered = await _notificationSink.SendNotificationAsync(alert, ct).ConfigureAwait(false);
                    if (delivered)
                    {
                        _lastNotificationTimes[dedupKey] = nowUtc;
                        notificationsDispatched++;
                    }
                }
            }
        }

        return notificationsDispatched;
    }

    /// <summary>
    /// Computes and applies the aggregate tray state from a collection of instance snapshots.
    /// </summary>
    /// <param name="snapshots">List of current instance snapshots.</param>
    private void UpdateTrayState(IReadOnlyList<InstanceConnectionSnapshot> snapshots)
    {
        // WHAT: Computes aggregate health badge (Idle, Healthy, Warning, Critical) and formats tooltip/summaries.
        // WHY: Implements robust status rollup for system tray indicator.
        // HOW: Iterate snapshots, evaluate severity rules, and construct immutable TrayMenuState.

        if (snapshots.Count == 0)
        {
            _currentTrayState = new TrayMenuState(
                Badge: TrayHealthBadge.Idle,
                TooltipText: "NanoWeaver HRT — No Instances Configured",
                IsMainWindowVisible: _isMainWindowVisible,
                AreNotificationsMuted: _areNotificationsMuted,
                InstanceSummaries: new[] { "No instances configured" });

            _trayController.ApplyState(_currentTrayState);
            return;
        }

        int onlineCount = 0;
        int degradedCount = 0;
        int criticalCount = 0;
        int offlineCount = 0;

        var summaries = new List<string>(snapshots.Count);

        foreach (var s in snapshots)
        {
            string statusText = s.State switch
            {
                InstanceConnectionState.Online => "Online",
                InstanceConnectionState.Degraded => "Degraded",
                InstanceConnectionState.Unauthorized => "Unauthorized",
                InstanceConnectionState.Offline => "Offline",
                InstanceConnectionState.Connecting => "Connecting...",
                _ => "Disconnected"
            };

            summaries.Add($"{s.DisplayName}: {statusText} (CPU: {s.CpuUtilizationPercent:F1}%, GPU: {s.GpuUtilizationPercent:F1}%, Temp: {s.GpuTemperatureCelsius:F1}°C)");

            switch (s.State)
            {
                case InstanceConnectionState.Online:
                    onlineCount++;
                    break;
                case InstanceConnectionState.Degraded:
                    degradedCount++;
                    break;
                case InstanceConnectionState.Unauthorized:
                case InstanceConnectionState.Offline:
                    criticalCount++;
                    break;
                default:
                    offlineCount++;
                    break;
            }
        }

        TrayHealthBadge badge = criticalCount > 0
            ? TrayHealthBadge.Critical
            : degradedCount > 0
                ? TrayHealthBadge.Warning
                : onlineCount > 0
                    ? TrayHealthBadge.Healthy
                    : TrayHealthBadge.Idle;

        string badgeName = badge switch
        {
            TrayHealthBadge.Healthy => "Healthy",
            TrayHealthBadge.Warning => "Warning",
            TrayHealthBadge.Critical => "Critical",
            _ => "Idle"
        };

        string tooltip = $"NanoWeaver HRT — Status: {badgeName} ({onlineCount} Online, {degradedCount} Degraded, {criticalCount} Critical)";

        _currentTrayState = new TrayMenuState(
            Badge: badge,
            TooltipText: tooltip,
            IsMainWindowVisible: _isMainWindowVisible,
            AreNotificationsMuted: _areNotificationsMuted,
            InstanceSummaries: summaries);

        _trayController.ApplyState(_currentTrayState);
    }
}
