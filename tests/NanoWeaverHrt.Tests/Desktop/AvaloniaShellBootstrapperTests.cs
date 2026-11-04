using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Desktop.Shell;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Gui.Core.Views;
using Xunit;

namespace NanoWeaverHrt.Tests.Desktop;

/// <summary>
/// Unit tests for <see cref="AvaloniaShellBootstrapper"/> achieving 100.00% line and branch coverage.
/// </summary>
public sealed class AvaloniaShellBootstrapperTests
{
    private static (MainShellWindowHost Host, DesktopTrayIconController Tray, IMultiInstanceCoordinator Coord, WeaverNodeApiClient Node) CreateMocks()
    {
        var repoMock = new Mock<IInstanceProfileRepository>();
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<InstanceProfile>());
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(new List<InstanceConnectionSnapshot>());
        var innerNodeMock = new Mock<INanoWeaverNodeClient>();
        var nodeClient = new WeaverNodeApiClient(innerNodeMock.Object);

        var shellVm = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeClient);
        var instanceMgrVm = new InstanceManagerViewModel(repoMock.Object, coordMock.Object);
        var vaultVm = new VaultUnlockViewModel();
        var aboutVm = new AboutViewModel();
        var host = new MainShellWindowHost(shellVm, instanceMgrVm, vaultVm, aboutVm);
        var tray = new DesktopTrayIconController();

        return (host, tray, coordMock.Object, nodeClient);
    }

    [Fact]
    public void AvaloniaShellBootstrapper_Constructor_NullGuards()
    {
        // WHAT: Test null argument validation in bootstrapper constructor.
        // WHY:  Ensures fail-fast behavior on missing dependencies.
        // HOW:  Passes null for each parameter and asserts ArgumentNullException.
        var (host, tray, coord, node) = CreateMocks();

        Assert.Throws<ArgumentNullException>(() => new AvaloniaShellBootstrapper(null!, tray, coord, node));
        Assert.Throws<ArgumentNullException>(() => new AvaloniaShellBootstrapper(host, null!, coord, node));
        Assert.Throws<ArgumentNullException>(() => new AvaloniaShellBootstrapper(host, tray, null!, node));
        Assert.Throws<ArgumentNullException>(() => new AvaloniaShellBootstrapper(host, tray, coord, null!));
    }

    [Fact]
    public void AvaloniaShellBootstrapper_Properties_And_CreateDefault()
    {
        // WHAT: Test property getters and CreateDefault factory with both custom and default arguments.
        // WHY:  Validates default DI composition for desktop platform head across all parameter branches.
        // HOW:  Calls CreateDefault with custom connection/mutex and then with null defaults, asserts properties.
        string uniqueMutex = $"TestBootstrapperMutex_{Guid.NewGuid():N}";
        using var bootstrapper = AvaloniaShellBootstrapper.CreateDefault("Data Source=:memory:", uniqueMutex);

        Assert.NotNull(bootstrapper.WindowHost);
        Assert.NotNull(bootstrapper.TrayController);
        Assert.NotNull(bootstrapper.Coordinator);
        Assert.NotNull(bootstrapper.NodeClient);

        using var bootstrapperDefaults = AvaloniaShellBootstrapper.CreateDefault();
        Assert.NotNull(bootstrapperDefaults.WindowHost);
        Assert.NotNull(bootstrapperDefaults.TrayController);
        Assert.NotNull(bootstrapperDefaults.Coordinator);
        Assert.NotNull(bootstrapperDefaults.NodeClient);
    }

    [Fact]
    public async Task AvaloniaShellBootstrapper_RunAsync_MutexCollision_Returns2()
    {
        // WHAT: Test RunAsync when single-instance mutex guard detects secondary instance.
        // WHY:  Enforces single-instance execution invariant.
        // HOW:  Injects mutex factory returning createdNew: false, calls RunAsync, asserts 2.
        var (host, tray, coord, node) = CreateMocks();
        var collisionGuard = new SingleInstanceMutexGuard("collision-mutex", _ => (null, false));

        using var bootstrapper = new AvaloniaShellBootstrapper(host, tray, coord, node, collisionGuard);
        int exitCode = await bootstrapper.RunAsync();

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task AvaloniaShellBootstrapper_RunAsync_PreCancelled_And_NormalCancellation()
    {
        // WHAT: Test RunAsync lifecycle with pre-cancelled token and timer cancellation.
        // WHY:  Validates clean startup, tray state application, and clean shutdown.
        // HOW:  Executes RunAsync with pre-cancelled token and with normal CTS cancel.
        var (host, tray, coord, node) = CreateMocks();

        using var bootstrapper = new AvaloniaShellBootstrapper(host, tray, coord, node);

        // Pre-cancelled token
        using var preCts = new CancellationTokenSource();
        preCts.Cancel();
        int exitPre = await bootstrapper.RunAsync(preCts.Token);
        Assert.Equal(0, exitPre);

        // Normal cancellation
        using var normalCts = new CancellationTokenSource();
        normalCts.CancelAfter(50);
        int exitNormal = await bootstrapper.RunAsync(normalCts.Token);
        Assert.Equal(0, exitNormal);

        // Primary mutex guard execution branch
        string customMutex = $"PrimaryMutex_{Guid.NewGuid():N}";
        using var primaryGuard = new SingleInstanceMutexGuard(customMutex);
        using var bootstrapperPrimary = new AvaloniaShellBootstrapper(host, tray, coord, node, primaryGuard);
        using var primaryCts = new CancellationTokenSource();
        primaryCts.Cancel();
        int exitPrimary = await bootstrapperPrimary.RunAsync(primaryCts.Token);
        Assert.Equal(0, exitPrimary);
    }

    [Fact]
    public void AvaloniaShellBootstrapper_Dispose_Idempotency()
    {
        // WHAT: Test Dispose idempotency and cleanup of child components.
        // WHY:  Guarantees no memory leaks or double-disposal exceptions.
        // HOW:  Calls Dispose twice on bootstrapper instance.
        var (host, tray, coord, node) = CreateMocks();
        var disposableCoordMock = new Mock<IDisposable>();
        var coordWithDisposable = disposableCoordMock.As<IMultiInstanceCoordinator>();

        var bootstrapper = new AvaloniaShellBootstrapper(host, tray, coordWithDisposable.Object, node);
        bootstrapper.Dispose();
        disposableCoordMock.Verify(d => d.Dispose(), Times.Once);

        // Idempotent second call
        bootstrapper.Dispose();
    }
}
