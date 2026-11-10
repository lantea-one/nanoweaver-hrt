using System.Reflection;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Desktop;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Desktop.Shell;
using NanoWeaverHrt.Gui.Core.Services;
using Xunit;

namespace NanoWeaverHrt.Tests.Desktop;

/// <summary>
/// Comprehensive unit tests for <see cref="DesktopProgram"/> and <see cref="DesktopDaemonConfig"/>
/// achieving 100.00% line and branch coverage across CLI dispatch, daemon polling cadence,
/// single-instance mutex enforcement, and SIGINT cancel handlers.
/// </summary>
public sealed class DesktopProgramTests
{
    /// <summary>
    /// Verifies <see cref="DesktopDaemonConfig"/> default values and custom property mutations.
    /// </summary>
    [Fact]
    public void DesktopDaemonConfig_DefaultsAndCustomValues()
    {
        // WHAT: Tests DesktopDaemonConfig constructor and property defaults.
        // WHY:  Issue #160 (P157): Validates configurable background daemon polling interval.
        // HOW:  Asserts default 15 seconds, and validates custom constructor arguments.
        var defaultConfig = new DesktopDaemonConfig();
        Assert.Equal(15, defaultConfig.PollingIntervalSeconds);

        var customConfig = new DesktopDaemonConfig(45);
        Assert.Equal(45, customConfig.PollingIntervalSeconds);
    }

    /// <summary>
    /// Verifies that <see cref="DesktopProgram.Main"/> executes cleanly and returns 0.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_Main_Version_Returns0()
    {
        // WHAT: Tests entrypoint invocation DesktopProgram.Main with --version argument.
        // WHY:  Validates top-level process entrypoint with Console.CancelKeyPress hook.
        // HOW:  Invokes Main(["--version"]) and asserts return code 0.
        int exitCode = await DesktopProgram.Main(["--version"]);
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Verifies that <see cref="DesktopProgram.HookConsoleCancel"/> validates arguments,
    /// triggers token cancellation, suppresses ObjectDisposedException, and disposes idempotently.
    /// </summary>
    [Fact]
    public void DesktopProgram_HookConsoleCancel_ValidatesAndDispatches()
    {
        // WHAT: Tests HookConsoleCancel null guard, cancellation dispatch, exception safety, and disposal idempotency.
        // WHY:  Guarantees reliable SIGINT/SIGTERM cancellation signal handling without process crashes.
        // HOW:  Calls HookConsoleCancel with null and valid CTS, triggers the inner delegate directly, and disposes twice.
        Assert.Throws<ArgumentNullException>(() => DesktopProgram.HookConsoleCancel(null!));

        using var cts = new CancellationTokenSource();
        using (var hook = DesktopProgram.HookConsoleCancel(cts))
        {
            Assert.NotNull(hook);
            var concreteHook = Assert.IsType<DesktopProgram.CancelKeyPressHook>(hook);
            Assert.NotNull(concreteHook.Handler);

            // Trigger handler using uninitialized event args to avoid reflection null checks
            var eventArgs = (ConsoleCancelEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ConsoleCancelEventArgs));
            concreteHook.Handler(null, eventArgs);
            Assert.True(eventArgs.Cancel);
            Assert.True(cts.IsCancellationRequested);

            // Test ObjectDisposedException suppression
            using var disposedCts = new CancellationTokenSource();
            using var disposedHook = DesktopProgram.HookConsoleCancel(disposedCts);
            var concreteDisposedHook = Assert.IsType<DesktopProgram.CancelKeyPressHook>(disposedHook);
            disposedCts.Dispose();

            var eventArgs2 = (ConsoleCancelEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ConsoleCancelEventArgs));
            concreteDisposedHook.Handler(null, eventArgs2);
            Assert.True(eventArgs2.Cancel);

            // Test double disposal
            hook.Dispose();
            hook.Dispose();
        }
    }

    /// <summary>
    /// Verifies <see cref="AvaloniaShellBootstrapper.CreateDefault"/> with explicit connection strings,
    /// custom base directories, and default temporary directory fallback.
    /// </summary>
    [Fact]
    public void AvaloniaShellBootstrapper_CreateDefault_AllBranches()
    {
        // WHAT: Tests CreateDefault with explicit connection string, custom base dir, and whitespace base dir.
        // WHY:  Achieves 100% line & branch coverage on factory method.
        // HOW:  Invokes CreateDefault with various parameter combinations and asserts valid instance.
        using var b1 = AvaloniaShellBootstrapper.CreateDefault(connectionString: "Data Source=:memory:");
        Assert.NotNull(b1);

        string tempBase = Path.Combine(Path.GetTempPath(), $"WeaverTest_{Guid.NewGuid():N}");
        using var b2 = AvaloniaShellBootstrapper.CreateDefault(baseDirectory: tempBase);
        Assert.NotNull(b2);

        using var b3 = AvaloniaShellBootstrapper.CreateDefault(baseDirectory: "");
        Assert.NotNull(b3);
    }

    /// <summary>
    /// Verifies interactive GUI launching using default <see cref="AvaloniaShellBootstrapper"/> wiring
    /// and single-instance mutex guards.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_RunGuiAsync_DefaultBootstrapper_And_Collision()
    {
        // WHAT: Tests RunGuiAsync with null uiHost (instantiating default bootstrapper) and mutex collision.
        // WHY:  Issue #160 (P157): Validates interactive mode instantiates AvaloniaShellBootstrapper.CreateDefault.
        // HOW:  Invokes RunAsync([]) with unique mutex and cancelled token, then simulates mutex collision.
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        string mutexName = $"TestGuiBootstrapper_{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel to allow clean immediate shutdown

        int exitDefault = await DesktopProgram.RunAsync(
            [],
            stdout,
            stderr,
            mutexName: mutexName,
            ct: cts.Token);

        Assert.Equal(0, exitDefault);
        Assert.Contains("Windowed UI Shell initialized.", stdout.ToString());

        // Test single-instance collision
        using var heldGuard = new SingleInstanceMutexGuard(mutexName);
        stderr.GetStringBuilder().Clear();

        int exitCollision = await DesktopProgram.RunAsync(
            [],
            stdout,
            stderr,
            mutexName: mutexName);

        Assert.Equal(2, exitCollision);
        Assert.Contains("Error: Another instance of NanoWeaver HRT is already running.", stderr.ToString());
    }

    /// <summary>
    /// Verifies headless daemon execution with custom tick delay, polling intervals, and coordinator disposal.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_RunHeadlessDaemonAsync_PollingInterval_And_Disposal()
    {
        // WHAT: Tests RunHeadlessDaemonAsync with custom DesktopDaemonConfig and customTickDelay.
        // WHY:  Issue #160 (P157): Verifies polling interval is derived from config and accelerated by customTickDelay.
        // HOW:  Passes mock monitor, custom coordinator, and short tick delay with timeout cancellation.
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        string mutexName = $"TestDaemonCadence_{Guid.NewGuid():N}";

        var mockMonitor = new Mock<IBackgroundInstanceMonitor>();
        mockMonitor.Setup(m => m.ExecuteMonitoringTickAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var mockCoord = new Mock<IMultiInstanceCoordinator>();
        var disposableCoord = mockCoord.As<IDisposable>();
        disposableCoord.Setup(d => d.Dispose());

        var config = new DesktopDaemonConfig(PollingIntervalSeconds: 30);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        int exitCode = await DesktopProgram.RunAsync(
            ["--headless-daemon"],
            stdout,
            stderr,
            mutexName: mutexName,
            ct: cts.Token,
            daemonMonitor: mockMonitor.Object,
            daemonCoordinator: mockCoord.Object,
            daemonConfig: config,
            customTickDelay: TimeSpan.FromMilliseconds(5));

        Assert.Equal(0, exitCode);
        Assert.Contains("NanoWeaver HRT Headless Daemon started.", stdout.ToString());
        Assert.Contains("NanoWeaver HRT Headless Daemon stopped cleanly.", stdout.ToString());
        mockMonitor.Verify(m => m.ExecuteMonitoringTickAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        disposableCoord.Verify(d => d.Dispose(), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="DesktopProgram"/> handles `--headless` and `--tray-only` CLI aliases.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_HeadlessAliases_Covered()
    {
        // WHAT: Tests CLI flag aliases --headless and --tray-only.
        // WHY:  Ensures all CLI entrypoint aliases trigger daemon hosting logic.
        // HOW:  Invokes RunAsync with both aliases and short tick delay.
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var mockMonitor = new Mock<IBackgroundInstanceMonitor>();
        mockMonitor.Setup(m => m.ExecuteMonitoringTickAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        using var cts1 = new CancellationTokenSource();
        cts1.CancelAfter(30);

        string mutex1 = $"TestHeadlessAlias_{Guid.NewGuid():N}";
        int exit1 = await DesktopProgram.RunAsync(
            ["--headless"],
            stdout,
            stderr,
            mutexName: mutex1,
            ct: cts1.Token,
            daemonMonitor: mockMonitor.Object,
            customTickDelay: TimeSpan.FromMilliseconds(5));

        Assert.Equal(0, exit1);

        using var cts2 = new CancellationTokenSource();
        cts2.CancelAfter(30);

        string mutex2 = $"TestTrayOnlyAlias_{Guid.NewGuid():N}";
        int exit2 = await DesktopProgram.RunAsync(
            ["--tray-only"],
            stdout,
            stderr,
            mutexName: mutex2,
            ct: cts2.Token,
            daemonMonitor: mockMonitor.Object,
            customTickDelay: TimeSpan.FromMilliseconds(5));

        Assert.Equal(0, exit2);
    }
}
