using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Gui.Core.Views;

namespace NanoWeaverHrt.Desktop.Shell;

/// <summary>
/// Sovereign bootstrapper initializing cross-platform desktop UI hosts, dependency graphs,
/// system tray controllers, and single-instance mutex guards.
/// </summary>
/// <remarks>
/// // WHAT: Central bootstrapper constructing ViewModels and platform adapters for the windowed desktop head.
/// // WHY:  Issue #70 (P67): Pure C# bootstrapper enabling deterministic headless testing and execution across Linux, macOS, and Windows.
/// // HOW:  Initializes storage, API clients, ViewModels, and tray controller, coordinating startup and clean shutdown.
/// </remarks>
public sealed class AvaloniaShellBootstrapper : IDisposable
{
    /// <summary>
    /// System tray icon controller for background state and minimize-to-tray handling.
    /// </summary>
    private readonly DesktopTrayIconController _trayController;

    /// <summary>
    /// Single-instance mutex guard preventing duplicate concurrent desktop heads.
    /// </summary>
    private readonly SingleInstanceMutexGuard? _mutexGuard;

    /// <summary>
    /// Indicates whether the bootstrapper instance has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Gets the window shell host coordinating the active ViewModels and page navigation.
    /// </summary>
    public MainShellWindowHost WindowHost { get; }

    /// <summary>
    /// Gets the native desktop tray icon controller.
    /// </summary>
    public DesktopTrayIconController TrayController => _trayController;

    /// <summary>
    /// Gets the multi-instance coordinator managing cluster profiles and connections.
    /// </summary>
    public IMultiInstanceCoordinator Coordinator { get; }

    /// <summary>
    /// Gets the zeroizable node API client.
    /// </summary>
    public WeaverNodeApiClient NodeClient { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaShellBootstrapper"/> class with explicit dependencies.
    /// </summary>
    /// <param name="windowHost">Pre-configured window shell host.</param>
    /// <param name="trayController">Pre-configured tray controller.</param>
    /// <param name="coordinator">Pre-configured coordinator.</param>
    /// <param name="nodeClient">Pre-configured node client.</param>
    /// <param name="mutexGuard">Optional single-instance mutex guard.</param>
    public AvaloniaShellBootstrapper(
        MainShellWindowHost windowHost,
        DesktopTrayIconController trayController,
        IMultiInstanceCoordinator coordinator,
        WeaverNodeApiClient nodeClient,
        SingleInstanceMutexGuard? mutexGuard = null)
    {
        // WHAT: Assigns dependencies for desktop bootstrapper.
        // WHY:  Permits test injection and deterministic headless assertions.
        // HOW:  Validates non-null mandatory arguments and assigns properties.
        WindowHost = windowHost ?? throw new ArgumentNullException(nameof(windowHost));
        _trayController = trayController ?? throw new ArgumentNullException(nameof(trayController));
        Coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        NodeClient = nodeClient ?? throw new ArgumentNullException(nameof(nodeClient));
        _mutexGuard = mutexGuard;
    }

    /// <summary>
    /// Creates and configures a default production <see cref="AvaloniaShellBootstrapper"/> instance.
    /// </summary>
    /// <param name="connectionString">Optional SQLite connection string override.</param>
    /// <param name="mutexName">Optional single-instance mutex name override.</param>
    /// <param name="mutexGuard">Optional pre-acquired single-instance mutex guard.</param>
    /// <param name="baseDirectory">Optional base directory override for local application data storage.</param>
    /// <returns>A fully configured <see cref="AvaloniaShellBootstrapper"/> instance.</returns>
    public static AvaloniaShellBootstrapper CreateDefault(
        string? connectionString = null,
        string? mutexName = null,
        SingleInstanceMutexGuard? mutexGuard = null,
        string? baseDirectory = null)
    {
        // WHAT: Factories default dependencies for desktop shell execution.
        // WHY:  Encapsulates production wiring in a single reusable entrypoint.
        // HOW:  Instantiates storage, clients, coordinator, ViewModels, and window host.
        string? effectiveConn = connectionString;
        if (effectiveConn == null)
        {
            string appData = baseDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string baseDir = string.IsNullOrWhiteSpace(appData) ? Path.GetTempPath() : appData;
            string dir = Path.Combine(baseDir, "NanoWeaverHrt");
            Directory.CreateDirectory(dir);
            effectiveConn = $"Data Source={Path.Combine(dir, "profiles.db")}";
        }
        var repo = new SqliteInstanceProfileRepository(effectiveConn);
        var httpClient = new HttpClient();
        var keyVault = new AesGcmClientKeyRingVault();
        var nodeClient = new WeaverNodeApiClient(httpClient, keyVault);
        var coordinator = new MultiInstanceCoordinator(repo, nodeClient);
        var shellVm = new MainShellViewModel(repo, coordinator, nodeClient);
        var instanceManagerVm = new InstanceManagerViewModel(repo, coordinator);
        var vaultUnlockVm = new VaultUnlockViewModel();
        var aboutVm = new AboutViewModel();
        var windowHost = new MainShellWindowHost(shellVm, instanceManagerVm, vaultUnlockVm, aboutVm);
        var trayController = new DesktopTrayIconController();
        var effectiveGuard = mutexGuard ?? new SingleInstanceMutexGuard(mutexName ?? SingleInstanceMutexGuard.DefaultMutexName);

        return new AvaloniaShellBootstrapper(
            windowHost,
            trayController,
            coordinator,
            nodeClient,
            effectiveGuard);
    }

    /// <summary>
    /// Executes the desktop application lifecycle loop until cancelled.
    /// </summary>
    /// <param name="ct">Cancellation token signalling process termination.</param>
    /// <returns>Integer exit code: 0 on normal exit, 2 on mutex collision.</returns>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // WHAT: Executes desktop shell lifecycle loop.
        // WHY:  Keeps desktop head active, initializes ViewModels, and handles clean termination.
        // HOW:  Validates mutex ownership; initializes WindowHost; awaits cancellation; disposes resources.
        if (_mutexGuard != null && !_mutexGuard.IsPrimaryInstance)
        {
            return 2;
        }

        await WindowHost.InitializeAsync(ct).ConfigureAwait(false);

        _trayController.ApplyState(new TrayMenuState(
            Badge: TrayHealthBadge.Idle,
            TooltipText: "NanoWeaver HRT — Active",
            IsMainWindowVisible: true,
            AreNotificationsMuted: false,
            InstanceSummaries: []));

        if (ct.IsCancellationRequested)
        {
            return 0;
        }

        var tcs = new TaskCompletionSource();
        using (ct.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        return 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Disposes owned resources and zeroizes sensitive buffers.
        // WHY:  Ensures clean teardown on application exit.
        // HOW:  Disposes WindowHost, NodeClient, Coordinator, and mutex guard.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WindowHost.Dispose();
        NodeClient.Dispose();

        if (Coordinator is IDisposable disposableCoord)
        {
            disposableCoord.Dispose();
        }

        _mutexGuard?.Dispose();
    }
}
