using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;

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
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainShellViewModel"/> class.
    /// </summary>
    /// <param name="profileRepository">Repository for querying and removing instance profiles.</param>
    /// <param name="coordinator">Coordinator managing instance snapshots and authentication.</param>
    /// <param name="nodeClient">Optional REST client for triggering simulations.</param>
    public MainShellViewModel(
        IInstanceProfileRepository profileRepository,
        IMultiInstanceCoordinator coordinator,
        INanoWeaverNodeClient? nodeClient = null)
    {
        // WHAT: Assigns dependencies for profile storage, multi-instance coordination, and REST API access.
        // WHY:  Coordinates data flows between client-side services and the Avalonia UI presentation layer.
        // HOW:  Validates mandatory parameters and initializes empty collections.
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _nodeClient = nodeClient;
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
}
