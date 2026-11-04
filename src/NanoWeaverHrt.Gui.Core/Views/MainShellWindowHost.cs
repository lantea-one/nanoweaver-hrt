using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Gui.Core.ViewModels;

namespace NanoWeaverHrt.Gui.Core.Views;

/// <summary>
/// Supported top-level views and pages hosted within the sovereign desktop window shell.
/// </summary>
public enum ShellNavigationPage
{
    /// <summary>
    /// Active node monitoring dashboard and telemetry graphs.
    /// </summary>
    Dashboard,

    /// <summary>
    /// Cluster node instance profile manager and registration.
    /// </summary>
    InstanceManager,

    /// <summary>
    /// Master vault unlocking dialog for sealed PKI envelopes.
    /// </summary>
    VaultUnlock,

    /// <summary>
    /// System diagnostics, platform info, and cryptographic capabilities.
    /// </summary>
    About
}

/// <summary>
/// Cross-platform window shell host orchestrating the active desktop view, coordinating
/// navigation between <see cref="MainShellViewModel"/>, <see cref="InstanceManagerViewModel"/>,
/// <see cref="VaultUnlockViewModel"/>, and <see cref="AboutViewModel"/>.
/// </summary>
/// <remarks>
/// // WHAT: Central cross-platform windowed shell host and view-model coordinator.
/// // WHY:  Issue #70 (P67): Pure C# view host avoiding XAML compiler state machine regressions while providing 100% headless testability.
/// // HOW:  Binds four core ViewModels, coordinates navigation state transitions, handles unlock-and-connect, and manages disposal.
/// </remarks>
public sealed class MainShellWindowHost : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Backing field for currently active shell navigation page.
    /// </summary>
    private ShellNavigationPage _currentPage = ShellNavigationPage.Dashboard;

    /// <summary>
    /// Indicates whether the host has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the root shell ViewModel coordinating instances, alerts, and active connections.
    /// </summary>
    public MainShellViewModel ShellViewModel { get; }

    /// <summary>
    /// Gets the instance manager ViewModel for cluster profile configuration.
    /// </summary>
    public InstanceManagerViewModel InstanceManager { get; }

    /// <summary>
    /// Gets the vault unlock ViewModel coordinating passphrase prompt dialogs.
    /// </summary>
    public VaultUnlockViewModel VaultUnlock { get; }

    /// <summary>
    /// Gets the about ViewModel providing platform diagnostics and metadata.
    /// </summary>
    public AboutViewModel About { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainShellWindowHost"/> class.
    /// </summary>
    /// <param name="shellViewModel">Root shell ViewModel.</param>
    /// <param name="instanceManager">Instance profile manager ViewModel.</param>
    /// <param name="vaultUnlock">Optional vault unlock ViewModel.</param>
    /// <param name="about">Optional about ViewModel.</param>
    public MainShellWindowHost(
        MainShellViewModel shellViewModel,
        InstanceManagerViewModel instanceManager,
        VaultUnlockViewModel? vaultUnlock = null,
        AboutViewModel? about = null)
    {
        // WHAT: Assigns ViewModels for shell hosting.
        // WHY:  Permits modular dependency injection and isolated headless testing.
        // HOW:  Validates mandatory ViewModels and initializes optional ones with defaults.
        ShellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
        InstanceManager = instanceManager ?? throw new ArgumentNullException(nameof(instanceManager));
        VaultUnlock = vaultUnlock ?? new VaultUnlockViewModel();
        About = about ?? new AboutViewModel();
    }

    /// <summary>
    /// Gets or sets the currently active navigation page.
    /// </summary>
    public ShellNavigationPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    /// <summary>
    /// Gets the dynamic window title based on the active page.
    /// </summary>
    public string Title => $"NanoWeaver HRT — {_currentPage}";

    /// <summary>
    /// Navigates the shell to the specified page.
    /// </summary>
    /// <param name="page">Target page to display.</param>
    public void NavigateTo(ShellNavigationPage page)
    {
        // WHAT: Transitions shell window to target page.
        // WHY:  Drives top-level navigation bar dispatches.
        // HOW:  Mutates CurrentPage property triggering PropertyChanged.
        CurrentPage = page;
    }

    /// <summary>
    /// Initializes the shell view model and loads stored instance profiles.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Asynchronously initializes child ViewModels.
        // WHY:  Populates initial snapshot feeds and stored profiles on window display.
        // HOW:  Awaits InitializeAsync on ShellViewModel and LoadProfilesAsync on InstanceManager.
        ThrowIfDisposed();
        await ShellViewModel.InitializeAsync(ct).ConfigureAwait(false);
        await InstanceManager.LoadProfilesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Initiates a vault unlock flow and connects the selected instance upon successful passphrase validation.
    /// </summary>
    /// <param name="instanceId">Target instance identifier.</param>
    /// <param name="passphrase">Passphrase memory span.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if unlock and connection succeeded; otherwise false.</returns>
    public async Task<bool> UnlockAndConnectAsync(
        string instanceId,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Coordinates VaultUnlock prompt and MainShell connection handshake.
        // WHY:  Executes safe end-to-end unsealing and connection from UI button click.
        // HOW:  Updates ShellViewModel selection, calls VaultUnlock.UnlockAsync, and connects instance on success.
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        ShellViewModel.SelectedInstanceId = instanceId;
        VaultUnlock.InstanceId = instanceId;

        bool unlocked = await VaultUnlock.UnlockAsync(
            async pass => await ShellViewModel.ConnectSelectedInstanceAsync(pass, ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        if (unlocked)
        {
            NavigateTo(ShellNavigationPage.Dashboard);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Disposes owned ViewModels and releases resources.
        // WHY:  Guarantees zeroization of sensitive credentials when window is closed.
        // HOW:  Disposes ShellViewModel and VaultUnlock, flags _disposed = true.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShellViewModel.Dispose();
        VaultUnlock.Dispose();
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the host instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        // WHAT: Asserts non-disposed state before processing operations.
        // WHY:  Guarantees fail-fast semantics on disposed host access.
        // HOW:  Inspects _disposed flag and throws ObjectDisposedException if true.
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MainShellWindowHost));
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
