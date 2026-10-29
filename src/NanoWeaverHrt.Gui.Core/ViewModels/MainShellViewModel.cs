using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Localization;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// Root shell ViewModel coordinating the multi-instance sidebar, active node dashboard, background tray state, and alert feed.
/// </summary>
public sealed class MainShellViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Repository used for loading and managing persistent instance connection profiles.
    /// </summary>
    private readonly IInstanceProfileRepository _profileRepository;

    /// <summary>
    /// Multi-instance coordinator managing authentication handshakes, background polling, and connection snapshots.
    /// </summary>
    private readonly IMultiInstanceCoordinator _coordinator;

    /// <summary>
    /// REST client for triggering simulations against connected instances.
    /// </summary>
    private readonly INanoWeaverNodeClient? _nodeClient;

    /// <summary>
    /// Cached bearer tokens for authenticated instances.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _activeTokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Backing list for configured instance profiles.
    /// </summary>
    private IReadOnlyList<InstanceProfile> _profiles = Array.Empty<InstanceProfile>();

    /// <summary>
    /// Backing list for live connection snapshots across all instances.
    /// </summary>
    private IReadOnlyList<InstanceConnectionSnapshot> _snapshots = Array.Empty<InstanceConnectionSnapshot>();

    /// <summary>
    /// Backing list for the alert event history feed.
    /// </summary>
    private readonly List<InstanceAlertEvent> _alertFeed = new();

    /// <summary>
    /// Currently selected instance ID in the multi-instance switcher.
    /// </summary>
    private string? _selectedInstanceId;

    /// <summary>
    /// Live snapshot for the currently selected instance.
    /// </summary>
    private InstanceConnectionSnapshot? _selectedSnapshot;

    /// <summary>
    /// Localization provider for translating UI strings and system tray items.
    /// </summary>
    private readonly IWeaverLocalizer _localizer;

    /// <summary>
    /// Service managing persistent user language preferences.
    /// </summary>
    private readonly ILanguagePreferenceService? _languagePreferenceService;

    /// <summary>
    /// Backing field for currently selected user interface language.
    /// </summary>
    private WeaverLanguage _selectedLanguage;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainShellViewModel"/> class.
    /// </summary>
    /// <param name="profileRepository">Repository for querying and removing instance profiles.</param>
    /// <param name="coordinator">Coordinator managing instance snapshots and authentication.</param>
    /// <param name="nodeClient">Optional REST client for triggering simulations.</param>
    /// <param name="localizer">Optional localization provider. Defaults to a new instance of <see cref="WeaverLocalizer"/>.</param>
    /// <param name="languagePreferenceService">Optional language preference persistence service.</param>
    public MainShellViewModel(
        IInstanceProfileRepository profileRepository,
        IMultiInstanceCoordinator coordinator,
        INanoWeaverNodeClient? nodeClient = null,
        IWeaverLocalizer? localizer = null,
        ILanguagePreferenceService? languagePreferenceService = null)
    {
        // WHAT: Assigns dependencies for profile storage, multi-instance coordination, REST API access, and localization.
        // WHY:  Coordinates data flows between client-side services, localizer catalogs, and Avalonia presentation layer.
        // HOW:  Validates mandatory parameters, sets optional dependencies with sensible fallbacks, and initializes empty collections.
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _nodeClient = nodeClient;
        _localizer = localizer ?? new WeaverLocalizer();
        _languagePreferenceService = languagePreferenceService;
        _selectedLanguage = WeaverLanguage.English;
    }

    /// <summary>
    /// Gets the collection of configured instance profiles.
    /// </summary>
    public IReadOnlyList<InstanceProfile> Profiles => _profiles;

    /// <summary>
    /// Gets the collection of live connection snapshots across all instances.
    /// </summary>
    public IReadOnlyList<InstanceConnectionSnapshot> Snapshots => _snapshots;

    /// <summary>
    /// Gets the recent alert history feed.
    /// </summary>
    public IReadOnlyList<InstanceAlertEvent> AlertFeed => _alertFeed;

    /// <summary>
    /// Gets or sets the currently selected instance ID in the multi-instance switcher.
    /// </summary>
    public string? SelectedInstanceId
    {
        get => _selectedInstanceId;
        set
        {
            if (!string.Equals(_selectedInstanceId, value, StringComparison.OrdinalIgnoreCase))
            {
                _selectedInstanceId = value;
                UpdateSelectedSnapshot();
                OnPropertyChanged(nameof(SelectedInstanceId));
                OnPropertyChanged(nameof(SelectedSnapshot));
            }
        }
    }

    /// <summary>
    /// Gets the snapshot for the currently selected instance, if any.
    /// </summary>
    public InstanceConnectionSnapshot? SelectedSnapshot => _selectedSnapshot;

    /// <summary>
    /// Loads saved profiles from SQLite and refreshes the shell state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Loads saved instance profiles from storage, synchronizes snapshots, and selects default instance.
        // WHY:  Populates the multi-instance switcher when the shell window opens.
        // HOW:  Initializes repository, queries profiles, synchronizes coordinator snapshots,
        //       selects the first instance if none is selected, and notifies observers.
        await _profileRepository.InitializeAsync(ct);
        _profiles = await _profileRepository.ListProfilesAsync(ct);

        if (_coordinator is MultiInstanceCoordinator multiCoord)
        {
            await multiCoord.SyncProfilesAsync(ct);
        }

        _snapshots = _coordinator.GetSnapshots();

        if (string.IsNullOrWhiteSpace(_selectedInstanceId) && _profiles.Count > 0)
        {
            _selectedInstanceId = _profiles[0].InstanceId;
        }

        UpdateSelectedSnapshot();

        if (_languagePreferenceService != null)
        {
            _selectedLanguage = await _languagePreferenceService.GetPreferredLanguageAsync(ct).ConfigureAwait(false);
            NotifyLocalizationPropertiesChanged();
        }

        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(Snapshots));
        OnPropertyChanged(nameof(SelectedInstanceId));
        OnPropertyChanged(nameof(SelectedSnapshot));
    }

    /// <summary>
    /// Connects the currently selected instance using the provided passphrase.
    /// </summary>
    /// <param name="passphrase">Passphrase for unsealing the selected instance's private key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if authentication succeeded; otherwise false.</returns>
    public async Task<bool> ConnectSelectedInstanceAsync(
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Authenticates the selected instance using the provided passphrase and updates UI state.
        // WHY:  Establishes session with node while ensuring passphrases are not stored in memory properties.
        // HOW:  Guards against null/whitespace SelectedInstanceId and empty passphrase;
        //       calls coordinator.ConnectInstanceAsync; updates snapshots and tokens; returns true on Online.
        if (string.IsNullOrWhiteSpace(_selectedInstanceId) || passphrase.IsEmpty || passphrase.Span.IsWhiteSpace())
        {
            return false;
        }

        InstanceProfile? profile = _profiles.FirstOrDefault(
            p => string.Equals(p.InstanceId, _selectedInstanceId, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            return false;
        }

        try
        {
            InstanceConnectionSnapshot snapshot = await _coordinator.ConnectInstanceAsync(
                _selectedInstanceId,
                passphrase,
                ct);

            if (_nodeClient != null && snapshot.State == InstanceConnectionState.Online)
            {
                try
                {
                    NodeAuthSession session = await _nodeClient.AuthenticateAsync(profile, passphrase, ct);
                    _activeTokens[_selectedInstanceId] = session.Token;
                }
                catch
                {
                    // If nodeClient authentication fails, we don't hold the token
                }
            }

            _snapshots = _coordinator.GetSnapshots();
            UpdateSelectedSnapshot();

            OnPropertyChanged(nameof(Snapshots));
            OnPropertyChanged(nameof(SelectedSnapshot));

            return snapshot.State == InstanceConnectionState.Online;
        }
        catch (Exception)
        {
            _snapshots = _coordinator.GetSnapshots();
            UpdateSelectedSnapshot();

            OnPropertyChanged(nameof(Snapshots));
            OnPropertyChanged(nameof(SelectedSnapshot));

            return false;
        }
    }

    /// <summary>
    /// Disconnects the currently selected instance and wipes its session credentials.
    /// </summary>
    public void DisconnectSelectedInstance()
    {
        // WHAT: Disconnects the active instance and purges its cached token.
        // WHY:  Allows users to manually sign out or disconnect an instance from the UI.
        // HOW:  Guards against null SelectedInstanceId, removes active token, and delegates to coordinator.
        if (string.IsNullOrWhiteSpace(_selectedInstanceId))
        {
            return;
        }

        _activeTokens.TryRemove(_selectedInstanceId, out _);
        _coordinator.DisconnectInstance(_selectedInstanceId);
        _snapshots = _coordinator.GetSnapshots();
        UpdateSelectedSnapshot();

        OnPropertyChanged(nameof(Snapshots));
        OnPropertyChanged(nameof(SelectedSnapshot));
    }

    /// <summary>
    /// Triggers a simulation campaign on the currently selected online instance.
    /// </summary>
    /// <param name="simulationType">Campaign slug ("serum-stability", "aptamer-gating", "payload-leakage", "pbpk-endocrine").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the simulation started; otherwise false.</returns>
    public async Task<bool> TriggerSimulationOnSelectedAsync(
        string simulationType,
        CancellationToken ct = default)
    {
        // WHAT: Triggers a named simulation campaign on the currently selected online instance.
        // WHY:  Initiates computational nanotech simulation runs (e.g. serum-stability, pbpk-endocrine).
        // HOW:  Validates selected instance, online state, simulation slug, and active session token;
        //       delegates to _nodeClient.StartSimulationCampaignAsync.
        if (string.IsNullOrWhiteSpace(_selectedInstanceId) ||
            string.IsNullOrWhiteSpace(simulationType) ||
            _selectedSnapshot == null ||
            _selectedSnapshot.State != InstanceConnectionState.Online ||
            _nodeClient == null)
        {
            return false;
        }

        InstanceProfile? profile = _profiles.FirstOrDefault(
            p => string.Equals(p.InstanceId, _selectedInstanceId, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            return false;
        }

        if (!_activeTokens.TryGetValue(_selectedInstanceId, out string? bearerToken) || string.IsNullOrWhiteSpace(bearerToken))
        {
            return false;
        }

        try
        {
            return await _nodeClient.StartSimulationCampaignAsync(profile, bearerToken, simulationType, ct);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes an instance profile from the local registry and disconnects any active session.
    /// </summary>
    /// <param name="instanceId">Instance identifier to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if removed; otherwise false.</returns>
    public async Task<bool> RemoveInstanceAsync(
        string instanceId,
        CancellationToken ct = default)
    {
        // WHAT: Disconnects the instance, purges its active tokens, and deletes it from storage.
        // WHY:  Cleans up removed profiles from the multi-instance switcher and local database.
        // HOW:  Validates instanceId, calls coordinator.DisconnectInstance, deletes from repository,
        //       refreshes profiles and snapshots, updates selection, and fires PropertyChanged.
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        _activeTokens.TryRemove(instanceId, out _);
        _coordinator.DisconnectInstance(instanceId);

        bool deleted = await _profileRepository.DeleteProfileAsync(instanceId, ct);
        if (!deleted)
        {
            return false;
        }

        _profiles = await _profileRepository.ListProfilesAsync(ct);
        _snapshots = _coordinator.GetSnapshots();

        if (string.Equals(_selectedInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedInstanceId = _profiles.Count > 0 ? _profiles[0].InstanceId : null;
        }

        UpdateSelectedSnapshot();

        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(Snapshots));
        OnPropertyChanged(nameof(SelectedInstanceId));
        OnPropertyChanged(nameof(SelectedSnapshot));

        return true;
    }

    /// <summary>
    /// Appends an alert event to the history feed and dispatches a property change notification.
    /// </summary>
    /// <param name="alertEvent">The alert event to append.</param>
    public void AddAlert(InstanceAlertEvent alertEvent)
    {
        // WHAT: Appends an alert to the alert feed list and notifies observers.
        // WHY:  Displays incoming node alerts, thermal warnings, and completed simulations in the UI.
        // HOW:  Validates non-null alert, adds to _alertFeed, caps size if necessary, and triggers PropertyChanged.
        ArgumentNullException.ThrowIfNull(alertEvent);
        _alertFeed.Insert(0, alertEvent);
        if (_alertFeed.Count > 100)
        {
            _alertFeed.RemoveAt(_alertFeed.Count - 1);
        }

        OnPropertyChanged(nameof(AlertFeed));
    }

    /// <summary>
    /// Polls all connected instances via the coordinator and records any newly generated alert events.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of new alerts added.</returns>
    public async Task<int> PollAlertsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries coordinator for fresh alerts across connected instances and appends them to AlertFeed.
        // WHY:  Keeps the GUI alert feed up-to-date with node state transitions.
        // HOW:  Calls coordinator.PollAllConnectedAsync, prepends each alert to _alertFeed, updates snapshots, and notifies observers.
        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync(ct);
        if (alerts.Count > 0)
        {
            foreach (InstanceAlertEvent alert in alerts)
            {
                _alertFeed.Insert(0, alert);
            }

            while (_alertFeed.Count > 100)
            {
                _alertFeed.RemoveAt(_alertFeed.Count - 1);
            }

            _snapshots = _coordinator.GetSnapshots();
            UpdateSelectedSnapshot();

            OnPropertyChanged(nameof(AlertFeed));
            OnPropertyChanged(nameof(Snapshots));
            OnPropertyChanged(nameof(SelectedSnapshot));
        }

        return alerts.Count;
    }

    /// <summary>
    /// Clears all recorded alerts from the feed.
    /// </summary>
    public void ClearAlerts()
    {
        // WHAT: Removes all alert events from the feed.
        // WHY:  Allows users to dismiss all notifications in the UI notification center.
        // HOW:  Clears _alertFeed list and raises PropertyChanged for AlertFeed.
        _alertFeed.Clear();
        OnPropertyChanged(nameof(AlertFeed));
    }

    /// <summary>
    /// Synchronizes SelectedSnapshot with the snapshot corresponding to SelectedInstanceId.
    /// </summary>
    private void UpdateSelectedSnapshot()
    {
        // WHAT: Synchronizes SelectedSnapshot with the snapshot corresponding to SelectedInstanceId.
        // WHY:  Ensures the active node dashboard displays the correct connection state and telemetry.
        // HOW:  Searches _snapshots for SelectedInstanceId; if not found but a profile exists, falls back to a Disconnected snapshot.
        if (string.IsNullOrWhiteSpace(_selectedInstanceId))
        {
            _selectedSnapshot = null;
            OnPropertyChanged(nameof(LocalizedStatusText));
            return;
        }

        InstanceConnectionSnapshot? found = _snapshots.FirstOrDefault(
            s => string.Equals(s.InstanceId, _selectedInstanceId, StringComparison.OrdinalIgnoreCase));

        if (found != null)
        {
            _selectedSnapshot = found;
        }
        else
        {
            InstanceProfile? profile = _profiles.FirstOrDefault(
                p => string.Equals(p.InstanceId, _selectedInstanceId, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
            {
                _selectedSnapshot = new InstanceConnectionSnapshot(
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceConnectionState.Disconnected,
                    0.0,
                    0.0,
                    0.0,
                    0,
                    0.0,
                    profile.AddedAtUtc,
                    null);
            }
            else
            {
                _selectedSnapshot = null;
            }
        }

        OnPropertyChanged(nameof(LocalizedStatusText));
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">Name of the modified property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Invokes the PropertyChanged event.
        // WHY:  Notifies data-bound Avalonia UI components of property state changes.
        // HOW:  Raises PropertyChanged with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Gets the localization provider configured for this shell ViewModel.
    /// </summary>
    public IWeaverLocalizer Localizer => _localizer;

    /// <summary>
    /// Gets the language preference persistence service, if configured.
    /// </summary>
    public ILanguagePreferenceService? LanguagePreferenceService => _languagePreferenceService;

    /// <summary>
    /// Gets or sets the currently active display language.
    /// </summary>
    public WeaverLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            // WHAT: Mutates the selected language and notifies observers if changed.
            // WHY:  Powers interactive language pickers and reactive UI translation refresh.
            // HOW:  Compares new value with _selectedLanguage; updates field, fires PropertyChanged,
            //       notifies dependent localized properties, and initiates async persistence if service is present.
            if (_selectedLanguage != value)
            {
                _selectedLanguage = value;
                OnPropertyChanged(nameof(SelectedLanguage));
                NotifyLocalizationPropertiesChanged();

                if (_languagePreferenceService != null)
                {
                    _ = SetPreferredLanguageSafeAsync(value);
                }
            }
        }
    }

    /// <summary>
    /// Gets the list of community languages supported by the platform.
    /// </summary>
    public IReadOnlyList<WeaverLanguageInfo> AvailableLanguages => _localizer.GetSupportedLanguages();

    /// <summary>
    /// Gets the localized connection status string for the currently selected instance.
    /// </summary>
    public string LocalizedStatusText
    {
        get
        {
            // WHAT: Maps the current selected connection state to a localized label.
            // WHY:  Ensures node connectivity status appears translated in the UI sidebar and header.
            // HOW:  Switches over SelectedSnapshot.State, choosing corresponding key, and asks localizer.
            if (_selectedSnapshot == null)
            {
                return _localizer.GetString("Status.Offline", _selectedLanguage);
            }

            string key = _selectedSnapshot.State switch
            {
                InstanceConnectionState.Online => "Status.Online",
                InstanceConnectionState.Connecting => "Status.Connecting",
                _ => "Status.Offline"
            };

            return _localizer.GetString(key, _selectedLanguage);
        }
    }

    /// <summary>
    /// Gets the localized idle session lock alert message.
    /// </summary>
    public string LocalizedIdleLockMessage => _localizer.GetString("Alert.IdleTimeout", _selectedLanguage);

    /// <summary>
    /// Gets the localized label for the system tray Show/Restore action.
    /// </summary>
    public string LocalizedTrayShowText => GetLocalizedText("Tray.Show", "Show / Restore");

    /// <summary>
    /// Gets the localized label for the system tray Lock Key Vault action.
    /// </summary>
    public string LocalizedTrayLockVaultText => GetLocalizedText("Tray.LockVault", "Lock Key Vault");

    /// <summary>
    /// Gets the localized label for the system tray Exit / Quit action.
    /// </summary>
    public string LocalizedTrayExitText => GetLocalizedText("Tray.Exit", "Quit");

    /// <summary>
    /// Sets the active language and awaits persistence to storage.
    /// </summary>
    /// <param name="language">Target language to select.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public async Task SetLanguageAsync(WeaverLanguage language, CancellationToken ct = default)
    {
        // WHAT: Changes active UI language, dispatches notifications, and awaits persistence.
        // WHY:  Allows deterministic awaiting of language switches in GUI and test contexts.
        // HOW:  Updates backing field, raises property changes, and awaits ILanguagePreferenceService.
        _selectedLanguage = language;
        OnPropertyChanged(nameof(SelectedLanguage));
        NotifyLocalizationPropertiesChanged();

        if (_languagePreferenceService != null)
        {
            await _languagePreferenceService.SetPreferredLanguageAsync(language, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a localized string for the specified key using the currently selected language, returning a fallback if missing.
    /// </summary>
    /// <param name="key">Localization resource key.</param>
    /// <param name="fallback">Fallback string if key is missing or unresolved.</param>
    /// <returns>Localized string value, or fallback if not found.</returns>
    public string GetLocalizedText(string key, string fallback = "")
    {
        // WHAT: Resolves a localized string key against the active language catalog.
        // WHY:  Ensures graceful degradation to fallback text when a key is absent or empty.
        // HOW:  Guards against empty key, invokes localizer, and compares result to key identifier.
        if (string.IsNullOrEmpty(key))
        {
            return fallback;
        }

        string val = _localizer.GetString(key, _selectedLanguage);
        return string.IsNullOrEmpty(val) || string.Equals(val, key, StringComparison.Ordinal)
            ? (string.IsNullOrEmpty(fallback) ? key : fallback)
            : val;
    }

    /// <summary>
    /// Resolves a localized notification or alert message for the given alert key.
    /// </summary>
    /// <param name="alertKey">Alert resource key.</param>
    /// <param name="defaultMessage">Default message body if unlocalized.</param>
    /// <returns>Localized message string.</returns>
    public string GetLocalizedAlertMessage(string alertKey, string defaultMessage)
    {
        // WHAT: Translates alert titles and messages for display in GUI alert feeds.
        // WHY:  Allows multi-language alerts across native desktop and mobile notification centers.
        // HOW:  Delegates to GetLocalizedText with fallback to defaultMessage.
        return GetLocalizedText(alertKey, defaultMessage);
    }

    /// <summary>
    /// Raises property changed notifications for all localized string properties.
    /// </summary>
    private void NotifyLocalizationPropertiesChanged()
    {
        // WHAT: Dispatches PropertyChanged events for all dependent localized string properties.
        // WHY:  Fulfills reactive UI update invariant without requiring application restart.
        // HOW:  Invokes OnPropertyChanged for each localized property identifier.
        OnPropertyChanged(nameof(LocalizedStatusText));
        OnPropertyChanged(nameof(LocalizedIdleLockMessage));
        OnPropertyChanged(nameof(LocalizedTrayShowText));
        OnPropertyChanged(nameof(LocalizedTrayLockVaultText));
        OnPropertyChanged(nameof(LocalizedTrayExitText));
    }

    /// <summary>
    /// Safely persists the preferred language in a background fire-and-forget task.
    /// </summary>
    /// <param name="language">Language to persist.</param>
    /// <returns>A task representing the background save operation.</returns>
    private async Task SetPreferredLanguageSafeAsync(WeaverLanguage language)
    {
        // WHAT: Persists language preference in background without blocking synchronous property setters.
        // WHY:  Prevents UI freezes while ensuring preference changes are captured to disk.
        // HOW:  Awaits SetPreferredLanguageAsync in try/catch block to suppress transient storage exceptions.
        try
        {
            await _languagePreferenceService!.SetPreferredLanguageAsync(language).ConfigureAwait(false);
        }
        catch
        {
            // Suppress background persistence exceptions during synchronous property setter dispatches.
        }
    }
}
