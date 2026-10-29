namespace NanoWeaverHrt.Gui.Core.Services;

/// <summary>
/// Aggregate cluster health badge displayed on the system tray icon.
/// </summary>
public enum TrayHealthBadge
{
    /// <summary>No instances are currently connected.</summary>
    Idle = 0,
    /// <summary>All connected instances are online and healthy.</summary>
    Healthy = 1,
    /// <summary>At least one connected instance is degraded or thermally throttled.</summary>
    Warning = 2,
    /// <summary>At least one connected instance is offline or unauthorized.</summary>
    Critical = 3
}

/// <summary>
/// Immutable snapshot of the system tray icon tooltip, badge, and context menu items.
/// </summary>
/// <param name="Badge">Aggregate health badge.</param>
/// <param name="TooltipText">Summary tooltip text (e.g., "NanoWeaver HRT — 3/3 Nodes Online").</param>
/// <param name="IsMainWindowVisible">True if the main dashboard window is currently shown.</param>
/// <param name="AreNotificationsMuted">True if user muted desktop/mobile notifications.</param>
/// <param name="InstanceSummaries">Per-instance single-line status labels for the tray menu.</param>
public sealed record TrayMenuState(
    TrayHealthBadge Badge,
    string TooltipText,
    bool IsMainWindowVisible,
    bool AreNotificationsMuted,
    IReadOnlyList<string> InstanceSummaries);

/// <summary>
/// Abstraction over the native OS system tray icon (Linux StatusNotifierItem, macOS NSStatusItem, Windows Shell_NotifyIcon).
/// </summary>
public interface ITrayIconController
{
    /// <summary>
    /// Applies an updated tray icon badge, tooltip, and context menu state.
    /// </summary>
    /// <param name="state">The new tray menu state to render.</param>
    void ApplyState(TrayMenuState state);
}

/// <summary>
/// Abstraction over native OS notification delivery (FreeDesktop DBus, macOS UNUserNotificationCenter, Windows Toast, Android/iOS local notifications).
/// </summary>
public interface INativeNotificationSink
{
    /// <summary>
    /// Dispatches a native OS notification for an instance alert event.
    /// </summary>
    /// <param name="alert">The alert event to display.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if delivered to the OS notification subsystem; otherwise false.</returns>
    ValueTask<bool> SendNotificationAsync(NanoWeaverHrt.Client.Api.InstanceAlertEvent alert, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates background polling, tray state synchronization, notification deduplication, and 15-minute UI idle lock.
/// </summary>
public interface IBackgroundInstanceMonitor
{
    /// <summary>
    /// Gets the most recently computed system tray state.
    /// </summary>
    TrayMenuState CurrentTrayState { get; }

    /// <summary>
    /// Gets a value indicating whether the interactive GUI session is locked due to idle inactivity.
    /// </summary>
    bool IsSessionLocked { get; }

    /// <summary>
    /// Records user interaction activity to reset the 15-minute idle lock timer.
    /// </summary>
    /// <param name="nowUtc">Current UTC timestamp.</param>
    void RecordUserActivity(DateTimeOffset nowUtc);

    /// <summary>
    /// Toggles main window visibility (minimize-to-tray when closed vs restore from tray).
    /// </summary>
    /// <param name="isVisible">True to show the main dashboard window; false to hide to tray.</param>
    void SetMainWindowVisibility(bool isVisible);

    /// <summary>
    /// Toggles notification mute state.
    /// </summary>
    /// <param name="isMuted">True to suppress OS notifications; false to enable.</param>
    void SetNotificationsMuted(bool isMuted);

    /// <summary>
    /// Executes a single deterministic tick of background monitoring, idle evaluation, tray update, and alert dispatch.
    /// </summary>
    /// <param name="nowUtc">Current UTC timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of native notifications dispatched during this tick.</returns>
    Task<int> ExecuteMonitoringTickAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}
