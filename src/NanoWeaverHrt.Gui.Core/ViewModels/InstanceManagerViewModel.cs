using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel managing the registry of cluster instance profiles, adding, updating, and removing node endpoints.
/// </summary>
/// <remarks>
/// // WHAT: Coordinates instance profile lifecycle, validation, and persistence for the desktop shell.
/// // WHY:  Issue #70 (P67): Provides headless, testable MVVM logic for multi-node configuration.
/// // HOW:  Queries and updates <see cref="IInstanceProfileRepository"/>, syncs with <see cref="IMultiInstanceCoordinator"/>, and raises PropertyChanged.
/// </remarks>
public sealed class InstanceManagerViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Storage repository for instance connection profiles.
    /// </summary>
    private readonly IInstanceProfileRepository _profileRepository;

    /// <summary>
    /// Multi-instance coordinator for synchronizing snapshot state.
    /// </summary>
    private readonly IMultiInstanceCoordinator? _coordinator;

    /// <summary>
    /// Backing list of loaded instance profiles.
    /// </summary>
    private IReadOnlyList<InstanceProfile> _profiles = Array.Empty<InstanceProfile>();

    /// <summary>
    /// Currently selected instance profile.
    /// </summary>
    private InstanceProfile? _selectedProfile;

    /// <summary>
    /// Filter or search query text for narrowing instance profiles.
    /// </summary>
    private string _filterText = string.Empty;

    /// <summary>
    /// Indicates whether an asynchronous operation is currently in flight.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Current status or error message string.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceManagerViewModel"/> class.
    /// </summary>
    /// <param name="profileRepository">Repository for loading and persisting instance profiles.</param>
    /// <param name="coordinator">Optional multi-instance coordinator.</param>
    public InstanceManagerViewModel(
        IInstanceProfileRepository profileRepository,
        IMultiInstanceCoordinator? coordinator = null)
    {
        // WHAT: Assigns profile storage and coordinator dependencies.
        // WHY:  Decouples MVVM logic from concrete UI controls.
        // HOW:  Validates mandatory profileRepository and assigns internal fields.
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _coordinator = coordinator;
    }

    /// <summary>
    /// Gets the list of configured instance profiles.
    /// </summary>
    public IReadOnlyList<InstanceProfile> Profiles => _profiles;

    /// <summary>
    /// Gets or sets the currently selected instance profile.
    /// </summary>
    public InstanceProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Equals(_selectedProfile, value))
            {
                _selectedProfile = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the search filter text.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!string.Equals(_filterText, value, StringComparison.Ordinal))
            {
                _filterText = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredProfiles));
            }
        }
    }

    /// <summary>
    /// Gets the list of instance profiles filtered by <see cref="FilterText"/>.
    /// </summary>
    public IReadOnlyList<InstanceProfile> FilteredProfiles
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_filterText))
            {
                return _profiles;
            }

            return _profiles
                .Where(p => p.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                            p.Username.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                            p.BaseUri.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a background operation is underway.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the current status or error feedback message.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Loads instance profiles from repository storage into view model state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the load operation.</returns>
    public async Task LoadProfilesAsync(CancellationToken ct = default)
    {
        // WHAT: Queries storage for all configured profiles and updates collections.
        // WHY:  Populates the instance management list with saved node profiles.
        // HOW:  Sets IsBusy, fetches profiles, synchronizes coordinator if available, and fires PropertyChanged.
        IsBusy = true;
        StatusMessage = "Loading instance profiles...";

        try
        {
            await _profileRepository.InitializeAsync(ct).ConfigureAwait(false);
            _profiles = await _profileRepository.ListProfilesAsync(ct).ConfigureAwait(false);

            if (_selectedProfile != null)
            {
                _selectedProfile = _profiles.FirstOrDefault(
                    p => string.Equals(p.InstanceId, _selectedProfile.InstanceId, StringComparison.OrdinalIgnoreCase));
            }

            if (_coordinator is MultiInstanceCoordinator multiCoord)
            {
                await multiCoord.SyncProfilesAsync(ct).ConfigureAwait(false);
            }

            StatusMessage = $"Loaded {_profiles.Count} instance profile(s).";
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(FilteredProfiles));
            OnPropertyChanged(nameof(SelectedProfile));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load instance profiles: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Adds a new instance profile to persistent storage and refreshes the view model.
    /// </summary>
    /// <param name="profile">The instance profile to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if added successfully; otherwise false.</returns>
    public async Task<bool> AddProfileAsync(InstanceProfile profile, CancellationToken ct = default)
    {
        // WHAT: Validates and persists a new instance profile.
        // WHY:  Enables connecting to new NanoWeaver cluster nodes.
        // HOW:  Validates profile, invokes UpsertProfileAsync, and reloads profiles.
        ArgumentNullException.ThrowIfNull(profile);
        IsBusy = true;
        StatusMessage = $"Adding instance '{profile.DisplayName}'...";

        try
        {
            await _profileRepository.UpsertProfileAsync(profile, ct).ConfigureAwait(false);
            await LoadProfilesAsync(ct).ConfigureAwait(false);
            SelectedProfile = _profiles.FirstOrDefault(
                p => string.Equals(p.InstanceId, profile.InstanceId, StringComparison.OrdinalIgnoreCase));
            StatusMessage = $"Instance '{profile.DisplayName}' saved successfully.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding instance: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes an instance profile from storage and updates the view model.
    /// </summary>
    /// <param name="instanceId">Identifier of instance to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    public async Task<bool> DeleteProfileAsync(string instanceId, CancellationToken ct = default)
    {
        // WHAT: Removes an instance profile from repository storage.
        // WHY:  Enables decommissioning of stale or offline node connections.
        // HOW:  Validates instanceId, calls DeleteProfileAsync, disconnects coordinator, and reloads profiles.
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            StatusMessage = "Invalid instance identifier.";
            return false;
        }

        IsBusy = true;
        StatusMessage = $"Deleting instance '{instanceId}'...";

        try
        {
            if (_coordinator != null)
            {
                _coordinator.DisconnectInstance(instanceId);
            }

            bool deleted = await _profileRepository.DeleteProfileAsync(instanceId, ct).ConfigureAwait(false);
            if (deleted)
            {
                await LoadProfilesAsync(ct).ConfigureAwait(false);
                StatusMessage = $"Instance '{instanceId}' deleted successfully.";
                return true;
            }

            StatusMessage = $"Instance '{instanceId}' not found.";
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting instance: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for the calling property name.
    /// </summary>
    /// <param name="propertyName">Name of the property that changed.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Dispatches PropertyChanged event to subscribers.
        // WHY:  Fulfills INotifyPropertyChanged reactive data binding contract.
        // HOW:  Invokes event handler with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
