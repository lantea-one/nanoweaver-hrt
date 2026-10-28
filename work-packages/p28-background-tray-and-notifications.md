# Work Package P28: Background Daemon, System Tray & Cross-Platform Notification Service (`NanoWeaverHrt.Gui.Core`)

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: The standalone desktop and mobile applications must run continuously in the background with a system tray icon and dispatch native notifications when managed instances change state, complete simulations, or encounter alerts.
- **Goal & Scope**:
  - **Included**: Create `src/NanoWeaverHrt.Gui.Core/NanoWeaverHrt.Gui.Core.csproj` (`net10.0`), add it to `NanoWeaverHrt.slnx` and `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`, and implement platform-agnostic tray menu modeling (`ITrayIconController`, `TrayMenuState`, `TrayHealthBadge`), notification routing/deduplication (`INativeNotificationSink`, `NotificationDispatcher`), and background polling lifecycle (`IBackgroundInstanceMonitor`, `BackgroundInstanceMonitor`) including client-side idle lock timeout (Prime Directive 12).
  - **Excluded**: Avalonia XAML views (P29) and OS-specific executable entrypoints (P30).
- **Target Role / Boundary**: `Administrator`, `Researcher`, `Patient`; local/remote GUI client daemon.

## 2. Technical Contracts & Invariants

### 2.1 Interfaces & Models (`namespace NanoWeaverHrt.Gui.Core.Services`)

```csharp
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
    ValueTask<bool> SendNotificationAsync(InstanceAlertEvent alert, CancellationToken ct = default);
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
```

### 2.2 Determinism & Idle Session Invariants
- Per Prime Directive 6 and 12:
  - Never use `Thread.Sleep` in tests; `ExecuteMonitoringTickAsync(DateTimeOffset nowUtc, CancellationToken ct)` accepts an explicit `DateTimeOffset nowUtc` so idle expiration (default `TimeSpan.FromMinutes(15)`) and notification cooldowns (`TimeSpan.FromMinutes(5)` per identical `(InstanceId, Severity, Title)`) are 100% deterministic in unit tests.
  - When `nowUtc - lastActivityUtc >= IdleTimeout`, `ExecuteMonitoringTickAsync` sets `IsSessionLocked = true`, disconnects active Bearer tokens via `IMultiInstanceCoordinator`, and updates the tray state to `TrayHealthBadge.Idle`.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create `src/NanoWeaverHrt.Gui.Core/NanoWeaverHrt.Gui.Core.csproj` (referencing `NanoWeaverHrt.Client`), add it to `NanoWeaverHrt.slnx`, and add a `<ProjectReference>` in `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`.
2. **Task 2**: Implement `TrayHealthBadge`, `TrayMenuState`, `ITrayIconController`, `INativeNotificationSink`, `IBackgroundInstanceMonitor`, and `BackgroundInstanceMonitor`.
3. **Task 3**: Write 100% line and branch unit tests in `tests/NanoWeaverHrt.Tests/Gui/BackgroundInstanceMonitorTests.cs` covering all badge states (`Idle`, `Healthy`, `Warning`, `Critical`), notification muting, deduplication cooldowns, minimize-to-tray transitions, and 15-minute idle session auto-lock.
4. **Task 4**: Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
