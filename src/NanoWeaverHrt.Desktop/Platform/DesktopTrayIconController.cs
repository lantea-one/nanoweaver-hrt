using NanoWeaverHrt.Gui.Core.Services;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Desktop tray icon controller implementing <see cref="ITrayIconController"/> for cross-platform
/// desktop environments (Linux FreeDesktop StatusNotifierItem, macOS NSStatusItem, Windows Shell_NotifyIcon).
/// </summary>
/// <remarks>
/// // WHAT: System tray controller managing icon tooltip, badge, and contextual items.
/// // WHY:  Provides non-intrusive status monitoring when the primary dashboard window is hidden.
/// // HOW:  Captures immutable <see cref="TrayMenuState"/> snapshots and invokes listeners.
/// </remarks>
public sealed class DesktopTrayIconController : ITrayIconController
{
    /// <summary>
    /// Optional state transition callback for tests and UI runners.
    /// </summary>
    private readonly Action<TrayMenuState>? _onStateApplied;

    /// <summary>
    /// Current tray state representation.
    /// </summary>
    private TrayMenuState _currentState;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopTrayIconController"/> class.
    /// </summary>
    /// <param name="onStateApplied">Optional callback invoked whenever a new state is applied.</param>
    public DesktopTrayIconController(Action<TrayMenuState>? onStateApplied = null)
    {
        // WHAT: Initialize default idle tray menu state and configure callback.
        // WHY:  Ensures tray controller begins in a predictable state prior to first coordinator poll.
        // HOW:  Instantiates TrayMenuState with Idle badge and empty summary collection.
        _onStateApplied = onStateApplied;
        _currentState = new TrayMenuState(
            Badge: TrayHealthBadge.Idle,
            TooltipText: "NanoWeaver HRT — Initializing",
            IsMainWindowVisible: true,
            AreNotificationsMuted: false,
            InstanceSummaries: Array.Empty<string>());
    }

    /// <summary>
    /// Occurs when an updated tray menu state has been applied.
    /// </summary>
    public event EventHandler<TrayMenuState>? StateChanged;

    /// <summary>
    /// Gets the most recently applied tray menu state.
    /// </summary>
    public TrayMenuState CurrentState => _currentState;

    /// <inheritdoc/>
    public void ApplyState(TrayMenuState state)
    {
        // WHAT: Apply new tray state and notify subscribers.
        // WHY:  Maintains synchronized visual feedback across desktop shells.
        // HOW:  Validates non-null state, updates field, and dispatches callbacks.
        ArgumentNullException.ThrowIfNull(state);

        _currentState = state;
        _onStateApplied?.Invoke(state);
        StateChanged?.Invoke(this, state);
    }
}
