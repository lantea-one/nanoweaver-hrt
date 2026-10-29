using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Localization;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Desktop tray icon controller implementing <see cref="ITrayIconController"/> for cross-platform
/// desktop environments (Linux FreeDesktop StatusNotifierItem, macOS NSStatusItem, Windows Shell_NotifyIcon).
/// </summary>
/// <remarks>
/// // WHAT: System tray controller managing icon tooltip, badge, and contextual items with localization support.
/// // WHY:  Provides non-intrusive status monitoring and localized context menus when the primary dashboard window is hidden.
/// // HOW:  Captures immutable <see cref="TrayMenuState"/> snapshots and provides localized menu item text via <see cref="IWeaverLocalizer"/>.
/// </remarks>
public sealed class DesktopTrayIconController : ITrayIconController
{
    /// <summary>
    /// Optional state transition callback for tests and UI runners.
    /// </summary>
    private readonly Action<TrayMenuState>? _onStateApplied;

    /// <summary>
    /// Optional localization provider for translating tray menu items and tooltips.
    /// </summary>
    private readonly IWeaverLocalizer? _localizer;

    /// <summary>
    /// Current language used for localized tray menu labels.
    /// </summary>
    private WeaverLanguage _currentLanguage;

    /// <summary>
    /// Current tray state representation.
    /// </summary>
    private TrayMenuState _currentState;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopTrayIconController"/> class.
    /// </summary>
    /// <param name="onStateApplied">Optional callback invoked whenever a new state is applied.</param>
    /// <param name="localizer">Optional localization provider for translating menu items.</param>
    /// <param name="language">Initial language preference.</param>
    public DesktopTrayIconController(
        Action<TrayMenuState>? onStateApplied = null,
        IWeaverLocalizer? localizer = null,
        WeaverLanguage language = WeaverLanguage.English)
    {
        // WHAT: Initialize default idle tray menu state, configure callback, and set localization dependencies.
        // WHY:  Ensures tray controller begins in a predictable state prior to first coordinator poll.
        // HOW:  Instantiates TrayMenuState with Idle badge and empty summary collection, storing localizer reference.
        _onStateApplied = onStateApplied;
        _localizer = localizer;
        _currentLanguage = language;
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

    /// <summary>
    /// Gets the current language configured for tray menu localization.
    /// </summary>
    public WeaverLanguage CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Gets a value indicating whether the current language uses right-to-left layout direction.
    /// </summary>
    public bool IsRightToLeft => _currentLanguage == WeaverLanguage.Arabic;

    /// <summary>
    /// Gets the localization provider configured for this tray controller.
    /// </summary>
    public IWeaverLocalizer? Localizer => _localizer;

    /// <summary>
    /// Gets the localized label for the Show/Restore tray context menu item.
    /// </summary>
    public string LocalizedShowLabel => GetLocalizedLabel("Tray.Show", "Show / Restore");

    /// <summary>
    /// Gets the localized label for the Lock Key Vault tray context menu item.
    /// </summary>
    public string LocalizedLockVaultLabel => GetLocalizedLabel("Tray.LockVault", "Lock Key Vault");

    /// <summary>
    /// Gets the localized label for the Quit/Exit tray context menu item.
    /// </summary>
    public string LocalizedExitLabel => GetLocalizedLabel("Tray.Exit", "Quit");

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

    /// <summary>
    /// Updates the active language used for tray menu item translations.
    /// </summary>
    /// <param name="language">The new target language.</param>
    public void SetLanguage(WeaverLanguage language)
    {
        // WHAT: Updates the current language enumeration.
        // WHY:  Allows dynamic runtime language changes from the MainShellViewModel.
        // HOW:  Sets _currentLanguage backing field.
        _currentLanguage = language;
    }

    /// <summary>
    /// Resolves the standard localized context menu items (Show/Restore, Lock Key Vault, Quit).
    /// </summary>
    /// <returns>A list containing the localized menu item labels in display order.</returns>
    public IReadOnlyList<string> GetStandardMenuItems()
    {
        // WHAT: Collects standard tray menu item labels in order.
        // WHY:  Provides desktop shell status menus with localized item text.
        // HOW:  Returns a 3-element array with LocalizedShowLabel, LocalizedLockVaultLabel, and LocalizedExitLabel.
        return [LocalizedShowLabel, LocalizedLockVaultLabel, LocalizedExitLabel];
    }

    /// <summary>
    /// Resolves a localized label for the specified key using the active language, falling back to a default value.
    /// </summary>
    /// <param name="key">Localization resource key.</param>
    /// <param name="fallback">Fallback text when unlocalized.</param>
    /// <returns>Resolved label string.</returns>
    public string GetLocalizedLabel(string key, string fallback)
    {
        // WHAT: Resolves a localized tray label or returns fallback text.
        // WHY:  Ensures desktop tray menus display translated commands with valid English fallbacks.
        // HOW:  Guards against null localizer or empty key; invokes GetString and returns fallback if unresolved or empty.
        if (_localizer == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        string val = _localizer.GetString(key, _currentLanguage);
        return string.IsNullOrEmpty(val) || string.Equals(val, key, StringComparison.Ordinal)
            ? fallback
            : val;
    }

    /// <summary>
    /// Formats and applies an updated tray menu state with a localized status tooltip.
    /// </summary>
    /// <param name="badge">Health badge.</param>
    /// <param name="statusKey">Status localization key (e.g. 'Status.Online', 'Status.Offline').</param>
    /// <param name="isMainWindowVisible">Visibility flag.</param>
    /// <param name="areNotificationsMuted">Notification mute flag.</param>
    /// <param name="instanceSummaries">Instance summary messages.</param>
    public void ApplyLocalizedState(
        TrayHealthBadge badge,
        string statusKey,
        bool isMainWindowVisible,
        bool areNotificationsMuted,
        IReadOnlyList<string> instanceSummaries)
    {
        // WHAT: Builds and applies a localized TrayMenuState snapshot.
        // WHY:  Allows desktop shell runners to emit translated tray states.
        // HOW:  Resolves statusKey with GetLocalizedLabel, formats tooltip, and delegates to ApplyState.
        string statusText = GetLocalizedLabel(statusKey, statusKey);
        string tooltip = $"NanoWeaver HRT — {statusText}";
        ApplyState(new TrayMenuState(badge, tooltip, isMainWindowVisible, areNotificationsMuted, instanceSummaries));
    }
}
