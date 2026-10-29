using System.Runtime.InteropServices;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Desktop;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Mobile.Platform;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Comprehensive test suite verifying Desktop and Mobile platform heads and adapters.
/// </summary>
public sealed class DesktopAndMobilePlatformTests
{
    /// <summary>
    /// Helper creating a standardized test alert event for assertions.
    /// </summary>
    /// <param name="severity">Target severity level.</param>
    /// <param name="title">Alert title string.</param>
    /// <param name="message">Alert message body.</param>
    /// <returns>Populated <see cref="InstanceAlertEvent"/> record.</returns>
    private static InstanceAlertEvent CreateAlert(
        InstanceAlertSeverity severity = InstanceAlertSeverity.Warning,
        string title = "Node Degraded",
        string message = "Memory pressure elevated on cluster node.")
    {
        // WHAT: Construct immutable InstanceAlertEvent test fixture.
        // WHY:  Standardizes alert payload structure across unit test assertions.
        // HOW:  Instantiates record with fixed IDs and deterministic UTC timestamps.
        return new InstanceAlertEvent(
            AlertId: "alert-001",
            InstanceId: "node-test-1",
            InstanceDisplayName: "Alpha Node",
            Severity: severity,
            Title: title,
            Message: message,
            OccurredAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Verifies that <see cref="SystemPlatformProcessRunner"/> validates arguments and executes successfully.
    /// </summary>
    [Fact]
    public async Task SystemPlatformProcessRunner_ValidatesArguments_And_Executes()
    {
        // WHAT: Test execution paths in SystemPlatformProcessRunner.
        // WHY:  Ensures process execution safely validates inputs and handles outcomes.
        // HOW:  Tests null validation, success exit code, non-zero exit code, invalid executable, and cancellation.
        var runner = new SystemPlatformProcessRunner();

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.TryRunAsync(null!, []).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.TryRunAsync("dotnet", null!).AsTask());

        // Success execution with dotnet --version
        bool success = await runner.TryRunAsync("dotnet", ["--version"]);
        Assert.True(success);

        // Failure execution with non-zero exit code
        bool nonZeroExit = await runner.TryRunAsync("dotnet", ["nonexistent-command-xyz-987"]);
        Assert.False(nonZeroExit);

        // Failure execution with non-existent executable (catches Win32Exception)
        bool invalidExe = await runner.TryRunAsync("nonexistent_binary_xyz_12345", []);
        Assert.False(invalidExe);

        // Cancelled token before execution completes
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool cancelled = await runner.TryRunAsync("dotnet", ["--version"], cts.Token);
        Assert.False(cancelled);
    }

    /// <summary>
    /// Verifies that <see cref="DesktopNativeNotificationSink"/> validates constructor and method arguments.
    /// </summary>
    [Fact]
    public async Task DesktopNativeNotificationSink_ValidatesArguments()
    {
        // WHAT: Verify null argument protection in DesktopNativeNotificationSink.
        // WHY:  Guarantees defense against null references.
        // HOW:  Asserts ArgumentNullException is thrown on null process runner and null alert.
        Assert.Throws<ArgumentNullException>(() => new DesktopNativeNotificationSink(null!));

        var mockRunner = new Mock<IPlatformProcessRunner>();
        var sink = new DesktopNativeNotificationSink(mockRunner.Object, "linux");

        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.SendNotificationAsync(null!).AsTask());
    }

    /// <summary>
    /// Verifies platform resolution logic in <see cref="DesktopNativeNotificationSink.ResolvePlatform"/>.
    /// </summary>
    [Fact]
    public void DesktopNativeNotificationSink_ResolvePlatform_CoversAllBranches()
    {
        // WHAT: Test string override and OSPlatform predicate branches.
        // WHY:  Ensures 100.00% branch coverage across Linux, macOS, Windows, and unsupported scenarios.
        // HOW:  Passes explicit overrides and injected OSPlatform predicates to ResolvePlatform.
        Assert.Equal("linux", DesktopNativeNotificationSink.ResolvePlatform("linux"));
        Assert.Equal("macos", DesktopNativeNotificationSink.ResolvePlatform("macos"));
        Assert.Equal("macos", DesktopNativeNotificationSink.ResolvePlatform("osx"));
        Assert.Equal("windows", DesktopNativeNotificationSink.ResolvePlatform("windows"));
        Assert.Equal("windows", DesktopNativeNotificationSink.ResolvePlatform("win"));
        Assert.Equal("unsupported", DesktopNativeNotificationSink.ResolvePlatform("solaris"));

        // Predicate-based resolution for fallback when override is null
        Assert.Equal("linux", DesktopNativeNotificationSink.ResolvePlatform(null, p => p == OSPlatform.Linux));
        Assert.Equal("macos", DesktopNativeNotificationSink.ResolvePlatform(null, p => p == OSPlatform.OSX));
        Assert.Equal("windows", DesktopNativeNotificationSink.ResolvePlatform(null, p => p == OSPlatform.Windows));
        Assert.Equal("unsupported", DesktopNativeNotificationSink.ResolvePlatform(null, _ => false));

        // Default resolution on current test host
        string hostDefault = DesktopNativeNotificationSink.ResolvePlatform(null);
        Assert.False(string.IsNullOrWhiteSpace(hostDefault));
    }

    /// <summary>
    /// Verifies Linux notification dispatch with notify-send across all urgency levels.
    /// </summary>
    /// <param name="severity">Alert severity to test.</param>
    /// <param name="expectedUrgency">Expected urgency parameter value.</param>
    [Theory]
    [InlineData(InstanceAlertSeverity.Critical, "critical")]
    [InlineData(InstanceAlertSeverity.Warning, "normal")]
    [InlineData(InstanceAlertSeverity.Info, "low")]
    public async Task DesktopNativeNotificationSink_Linux_DispatchesExpectedArguments(
        InstanceAlertSeverity severity,
        string expectedUrgency)
    {
        // WHAT: Verify notify-send CLI argument vector on Linux.
        // WHY:  Ensures urgency mappings and alert content sanitization match FreeDesktop specifications.
        // HOW:  Captures arguments passed to IPlatformProcessRunner and asserts formatting.
        var mockRunner = new Mock<IPlatformProcessRunner>();
        IReadOnlyList<string>? capturedArgs = null;
        string? capturedExe = null;

        mockRunner.Setup(r => r.TryRunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((exe, args, _) =>
            {
                capturedExe = exe;
                capturedArgs = args;
            })
            .ReturnsAsync(true);

        var sink = new DesktopNativeNotificationSink(mockRunner.Object, "linux");
        var alert = CreateAlert(severity, "Test\r\nTitle", "Test\tMessage\0");

        bool result = await sink.SendNotificationAsync(alert);
        Assert.True(result);
        Assert.Equal("notify-send", capturedExe);
        Assert.NotNull(capturedArgs);
        Assert.Equal("-u", capturedArgs[0]);
        Assert.Equal(expectedUrgency, capturedArgs[1]);
        Assert.Equal("-a", capturedArgs[2]);
        Assert.Equal("NanoWeaver HRT", capturedArgs[3]);
        Assert.Contains("Test  Title", capturedArgs[4]);
        Assert.Equal("Test Message", capturedArgs[5]);
    }

    /// <summary>
    /// Verifies macOS notification dispatch with osascript.
    /// </summary>
    [Fact]
    public async Task DesktopNativeNotificationSink_MacOs_DispatchesAppleScript()
    {
        // WHAT: Verify osascript command invocation on macOS.
        // WHY:  Ensures AppleScript literals are sanitized and double quotes/backslashes are escaped.
        // HOW:  Captures osascript arguments and verifies AppleScript display notification script.
        var mockRunner = new Mock<IPlatformProcessRunner>();
        IReadOnlyList<string>? capturedArgs = null;
        string? capturedExe = null;

        mockRunner.Setup(r => r.TryRunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((exe, args, _) =>
            {
                capturedExe = exe;
                capturedArgs = args;
            })
            .ReturnsAsync(true);

        var sink = new DesktopNativeNotificationSink(mockRunner.Object, "macos");
        var alert = CreateAlert(InstanceAlertSeverity.Warning, "Title with \"quotes\" & \\slashes\\", "Alert message.");

        bool result = await sink.SendNotificationAsync(alert);
        Assert.True(result);
        Assert.Equal("osascript", capturedExe);
        Assert.NotNull(capturedArgs);
        Assert.Equal("-e", capturedArgs[0]);
        Assert.Contains("display notification \"Alert message.\"", capturedArgs[1]);
        Assert.Contains("with title \"Title with \\\"quotes\\\" & \\\\slashes\\\\\"", capturedArgs[1]);
    }

    /// <summary>
    /// Verifies Windows notification dispatch with PowerShell Toast arguments.
    /// </summary>
    [Fact]
    public async Task DesktopNativeNotificationSink_Windows_DispatchesPowerShellToast()
    {
        // WHAT: Verify PowerShell invocation for Windows toast notifications.
        // WHY:  Ensures parameter binding without shell script interpolation.
        // HOW:  Captures powershell argument vector and asserts parameters.
        var mockRunner = new Mock<IPlatformProcessRunner>();
        IReadOnlyList<string>? capturedArgs = null;
        string? capturedExe = null;

        mockRunner.Setup(r => r.TryRunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((exe, args, _) =>
            {
                capturedExe = exe;
                capturedArgs = args;
            })
            .ReturnsAsync(true);

        var sink = new DesktopNativeNotificationSink(mockRunner.Object, "windows");
        var alert = CreateAlert(InstanceAlertSeverity.Critical, "Windows Alert", "Simulation fault.");

        bool result = await sink.SendNotificationAsync(alert);
        Assert.True(result);
        Assert.Equal("powershell", capturedExe);
        Assert.NotNull(capturedArgs);
        Assert.Equal("-NoProfile", capturedArgs[0]);
        Assert.Equal("-NonInteractive", capturedArgs[1]);
        Assert.Equal("-Command", capturedArgs[2]);
        Assert.Contains("[Windows.UI.Notifications.ToastNotificationManager", capturedArgs[3]);
        Assert.Equal("[Alpha Node] Windows Alert", capturedArgs[4]);
        Assert.Equal("Simulation fault.", capturedArgs[5]);
    }

    /// <summary>
    /// Verifies sanitization helper in <see cref="DesktopNativeNotificationSink.Sanitize"/>.
    /// </summary>
    [Fact]
    public void DesktopNativeNotificationSink_Sanitize_CoversEmptyAndControlChars()
    {
        // WHAT: Test text sanitization branches.
        // WHY:  Ensures null or empty strings return empty string and control characters are converted.
        // HOW:  Invokes Sanitize with null, empty, and control character strings.
        Assert.Equal(string.Empty, DesktopNativeNotificationSink.Sanitize(null!));
        Assert.Equal(string.Empty, DesktopNativeNotificationSink.Sanitize(string.Empty));
        Assert.Equal("Hello World", DesktopNativeNotificationSink.Sanitize("Hello\0World"));
        Assert.Equal("Hello    World", DesktopNativeNotificationSink.Sanitize("Hello\0\r\n\tWorld"));
    }

    /// <summary>
    /// Verifies that <see cref="DesktopNativeNotificationSink"/> returns false for unsupported platforms and runner failures.
    /// </summary>
    [Fact]
    public async Task DesktopNativeNotificationSink_HandlesUnsupportedPlatform_AndRunnerFailure()
    {
        // WHAT: Test failure modes in DesktopNativeNotificationSink.
        // WHY:  Ensures clean failure reporting when runner returns false or platform is unsupported.
        // HOW:  Passes unsupported platform override and mock runner returning false.
        var mockRunner = new Mock<IPlatformProcessRunner>();
        mockRunner.Setup(r => r.TryRunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var unsupportedSink = new DesktopNativeNotificationSink(mockRunner.Object, "unknown-os");
        bool unsupportedResult = await unsupportedSink.SendNotificationAsync(CreateAlert());
        Assert.False(unsupportedResult);

        var failingSink = new DesktopNativeNotificationSink(mockRunner.Object, "linux");
        bool failingResult = await failingSink.SendNotificationAsync(CreateAlert());
        Assert.False(failingResult);
    }

    /// <summary>
    /// Verifies default initialization, state changes, and event dispatch in <see cref="DesktopTrayIconController"/>.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_StateTransitions_AndEvents()
    {
        // WHAT: Verify DesktopTrayIconController functionality.
        // WHY:  Ensures tray state is retained and subscribers are notified of mutations.
        // HOW:  Constructs controller with callback, asserts initial state, applies updates, and verifies event firing.
        TrayMenuState? callbackState = null;
        TrayMenuState? eventState = null;

        var controller = new DesktopTrayIconController(s => callbackState = s);
        controller.StateChanged += (_, s) => eventState = s;

        Assert.Equal(TrayHealthBadge.Idle, controller.CurrentState.Badge);
        Assert.Equal("NanoWeaver HRT — Initializing", controller.CurrentState.TooltipText);
        Assert.True(controller.CurrentState.IsMainWindowVisible);
        Assert.False(controller.CurrentState.AreNotificationsMuted);

        Assert.Throws<ArgumentNullException>(() => controller.ApplyState(null!));

        var newState = new TrayMenuState(
            Badge: TrayHealthBadge.Healthy,
            TooltipText: "NanoWeaver HRT — 3/3 Nodes Online",
            IsMainWindowVisible: false,
            AreNotificationsMuted: true,
            InstanceSummaries: ["Node 1: Online", "Node 2: Online", "Node 3: Online"]);

        controller.ApplyState(newState);

        Assert.Equal(newState, controller.CurrentState);
        Assert.Equal(newState, callbackState);
        Assert.Equal(newState, eventState);

        // Test ApplyState without callbacks or event subscribers (null branch of ?.)
        var controllerWithoutSubscribers = new DesktopTrayIconController();
        controllerWithoutSubscribers.ApplyState(newState);
        Assert.Equal(newState, controllerWithoutSubscribers.CurrentState);
    }

    /// <summary>
    /// Verifies mutex acquisition, locking, release, and idempotency in <see cref="SingleInstanceMutexGuard"/>.
    /// </summary>
    [Fact]
    public void SingleInstanceMutexGuard_Acquisition_Locking_And_Disposal()
    {
        // WHAT: Test cross-instance concurrency lock with SingleInstanceMutexGuard.
        // WHY:  Guarantees only one platform head holds the system lock concurrently.
        // HOW:  Creates two guards with identical mutex names; asserts second fails; disposes first; asserts third succeeds.
        Assert.Throws<ArgumentNullException>(() => new SingleInstanceMutexGuard(null!));

        string uniqueMutexName = $"NanoWeaverHrt_TestMutex_{Guid.NewGuid():N}";

        using (var guard1 = new SingleInstanceMutexGuard(uniqueMutexName))
        {
            Assert.True(guard1.IsPrimaryInstance);
            Assert.Equal(uniqueMutexName, guard1.MutexName);

            using (var guard2 = new SingleInstanceMutexGuard(uniqueMutexName))
            {
                Assert.False(guard2.IsPrimaryInstance);
            }
        }

        // After guard1 is disposed, a new guard on the same mutex should succeed
        using (var guard3 = new SingleInstanceMutexGuard(uniqueMutexName))
        {
            Assert.True(guard3.IsPrimaryInstance);
            guard3.Dispose();
            // Test idempotent double-disposal
            guard3.Dispose();
        }

        // Test factory returning non-throwing result
        using (var guardFactory = new SingleInstanceMutexGuard("factory-mutex", _ => (null, true)))
        {
            Assert.True(guardFactory.IsPrimaryInstance);
        }

        // Test constructor catch block and null mutex disposal via injected factory
        using var guardFault = new SingleInstanceMutexGuard("fault-mutex", _ => throw new InvalidOperationException("Simulated mutex error"));
        Assert.False(guardFault.IsPrimaryInstance);
        guardFault.Dispose();
    }

    /// <summary>
    /// Verifies CLI flag handling in <see cref="DesktopProgram"/>.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_CommandLineFlags_CoverAllBranches()
    {
        // WHAT: Test CLI entrypoints in DesktopProgram.
        // WHY:  Ensures --help, -h, help, --verify-self, --headless-daemon, and unrecognized options execute as specified.
        // HOW:  Invokes DesktopProgram.RunAsync with custom StringWriter streams.
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        // --help / -h / help
        stdout.GetStringBuilder().Clear();
        int exitCodeHelp = await DesktopProgram.RunAsync(["--help"], stdout, stderr);
        Assert.Equal(0, exitCodeHelp);
        Assert.Contains("Usage: NanoWeaverHrt [options]", stdout.ToString());

        stdout.GetStringBuilder().Clear();
        int exitCodeShortHelp = await DesktopProgram.RunAsync(["-h"], stdout, stderr);
        Assert.Equal(0, exitCodeShortHelp);

        stdout.GetStringBuilder().Clear();
        int exitCodeWordHelp = await DesktopProgram.RunAsync(["help"], stdout, stderr);
        Assert.Equal(0, exitCodeWordHelp);

        // --verify-self with custom runner
        stdout.GetStringBuilder().Clear();
        var mockRunner = new Mock<IPlatformProcessRunner>();
        int exitCodeVerifyMock = await DesktopProgram.RunAsync(["--verify-self"], stdout, stderr, mockRunner.Object);
        Assert.Equal(0, exitCodeVerifyMock);
        Assert.Contains("NanoWeaver HRT Desktop Platform Head v1.0.0-sovereign [Verified]", stdout.ToString());
        Assert.Contains("NotificationSink: DesktopNativeNotificationSink", stdout.ToString());

        // --verify-self with default null runner (covers runner ?? new SystemPlatformProcessRunner())
        stdout.GetStringBuilder().Clear();
        int exitCodeVerifyDefault = await DesktopProgram.RunAsync(["--verify-self"], stdout, stderr);
        Assert.Equal(0, exitCodeVerifyDefault);
        Assert.Contains("NanoWeaver HRT Desktop Platform Head v1.0.0-sovereign [Verified]", stdout.ToString());

        // --headless-daemon (clean shutdown via cancellation token)
        stdout.GetStringBuilder().Clear();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(80);
        string daemonMutex = $"TestDaemonMutex_{Guid.NewGuid():N}";
        int exitCodeDaemon = await DesktopProgram.RunAsync(["--headless-daemon"], stdout, stderr, mutexName: daemonMutex, ct: cts.Token);
        Assert.Equal(0, exitCodeDaemon);
        Assert.Contains("NanoWeaver HRT Headless Daemon started.", stdout.ToString());
        Assert.Contains("NanoWeaver HRT Headless Daemon stopped cleanly.", stdout.ToString());

        // --headless-daemon (collision when mutex is already held)
        using var heldGuard = new SingleInstanceMutexGuard(daemonMutex);
        stderr.GetStringBuilder().Clear();
        int exitCodeCollision = await DesktopProgram.RunAsync(["--headless-daemon"], stdout, stderr, mutexName: daemonMutex);
        Assert.Equal(2, exitCodeCollision);
        Assert.Contains("Error: Another instance of NanoWeaver HRT is already running.", stderr.ToString());

        // Unrecognized argument
        stderr.GetStringBuilder().Clear();
        int exitCodeUnrecognized = await DesktopProgram.RunAsync(["--invalid-arg-xyz"], stdout, stderr);
        Assert.Equal(1, exitCodeUnrecognized);
        Assert.Contains("Unrecognized option: --invalid-arg-xyz", stderr.ToString());

        // Main static entrypoint
        int mainExit = await DesktopProgram.Main(["--help"]);
        Assert.Equal(0, mainExit);
    }

    /// <summary>
    /// Verifies <see cref="DefaultDesktopUiHost"/> state transitions, property accessors, and lifecycle execution.
    /// </summary>
    [Fact]
    public async Task DefaultDesktopUiHost_Properties_And_Lifecycle_ExecuteCleanly()
    {
        // WHAT: Test DefaultDesktopUiHost constructor defaults, properties, and RunUiAsync execution.
        // WHY:  Validates desktop windowed UI shell hosting layer and clean cancellation semantics.
        // HOW:  Asserts initial state, executes with cancellation tokens, and tests pre-cancelled token fast path.
        using var stdout = new StringWriter();
        var host = new DefaultDesktopUiHost(null, null, stdout);
        Assert.Null(host.ShellViewModel);
        Assert.NotNull(host.TrayController);

        // Run with normal cancellation
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(40);
        int exitCode = await host.RunUiAsync(cts.Token);
        Assert.Equal(0, exitCode);
        Assert.Contains("Windowed UI Shell initialized.", stdout.ToString());
        Assert.Contains("Windowed UI Shell shutting down.", stdout.ToString());
        Assert.Equal(TrayHealthBadge.Idle, host.TrayController.CurrentState.Badge);

        // Run with pre-cancelled token (covers ct.IsCancellationRequested early return)
        using var preCancelledCts = new CancellationTokenSource();
        preCancelledCts.Cancel();
        int preCancelledExit = await host.RunUiAsync(preCancelledCts.Token);
        Assert.Equal(0, preCancelledExit);

        // Test non-null custom tray controller and null outWriter fallback to Console.Out
        var customTray = new DesktopTrayIconController();
        var hostWithCustomTray = new DefaultDesktopUiHost(null, customTray, null);
        Assert.Same(customTray, hostWithCustomTray.TrayController);
    }

    /// <summary>
    /// Verifies interactive GUI launching and single-instance mutex enforcement in <see cref="DesktopProgram"/>.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_RunGuiAsync_CoversAllBranches()
    {
        // WHAT: Test empty args and --gui flag invocation of RunGuiAsync.
        // WHY:  Ensures DesktopProgram defaults to interactive UI mode while respecting mutex guards and custom UI hosts.
        // HOW:  Invokes RunAsync with empty args and --gui, testing clean exit, mock host delegation, and mutex collision.
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var mockHost = new Mock<IDesktopUiHost>();
        mockHost.Setup(h => h.RunUiAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        string guiMutex = $"TestGuiMutex_{Guid.NewGuid():N}";

        // Empty args with mock UI host
        int exitEmpty = await DesktopProgram.RunAsync([], stdout, stderr, mutexName: guiMutex, uiHost: mockHost.Object);
        Assert.Equal(0, exitEmpty);
        mockHost.Verify(h => h.RunUiAsync(It.IsAny<CancellationToken>()), Times.Once);

        // --gui flag with mock UI host
        mockHost.Invocations.Clear();
        int exitGuiFlag = await DesktopProgram.RunAsync(["--gui"], stdout, stderr, mutexName: guiMutex, uiHost: mockHost.Object);
        Assert.Equal(0, exitGuiFlag);
        mockHost.Verify(h => h.RunUiAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Default UI host fallback (uiHost is null) with cancellation
        using var defaultHostCts = new CancellationTokenSource();
        defaultHostCts.CancelAfter(40);
        string defaultMutex = $"TestDefaultGuiMutex_{Guid.NewGuid():N}";
        int exitDefaultHost = await DesktopProgram.RunAsync([], stdout, stderr, mutexName: defaultMutex, ct: defaultHostCts.Token);
        Assert.Equal(0, exitDefaultHost);
        Assert.Contains("Windowed UI Shell initialized.", stdout.ToString());

        // Single-instance collision during GUI launch
        using var heldGuard = new SingleInstanceMutexGuard(guiMutex);
        stderr.GetStringBuilder().Clear();
        int exitCollision = await DesktopProgram.RunAsync([], stdout, stderr, mutexName: guiMutex);
        Assert.Equal(2, exitCollision);
        Assert.Contains("Error: Another instance of NanoWeaver HRT is already running.", stderr.ToString());
    }

    /// <summary>
    /// Verifies mobile notification sink channel routing across Android, iOS, and iPadOS.
    /// </summary>
    /// <param name="platform">Mobile platform label.</param>
    /// <param name="severity">Alert severity.</param>
    /// <param name="expectedChannel">Expected channel identifier.</param>
    [Theory]
    [InlineData("android", InstanceAlertSeverity.Critical, "nanoweaver_channel_critical")]
    [InlineData("android", InstanceAlertSeverity.Warning, "nanoweaver_channel_warning")]
    [InlineData("android", InstanceAlertSeverity.Info, "nanoweaver_channel_info")]
    [InlineData("ipados", InstanceAlertSeverity.Critical, "NANOWEAVER_IPADOS_CRITICAL")]
    [InlineData("ipados", InstanceAlertSeverity.Warning, "NANOWEAVER_IPADOS_WARNING")]
    [InlineData("ipados", InstanceAlertSeverity.Info, "NANOWEAVER_IPADOS_INFO")]
    [InlineData("ios", InstanceAlertSeverity.Critical, "NANOWEAVER_IOS_CRITICAL")]
    [InlineData("ios", InstanceAlertSeverity.Warning, "NANOWEAVER_IOS_WARNING")]
    [InlineData("ios", InstanceAlertSeverity.Info, "NANOWEAVER_IOS_INFO")]
    public async Task MobileNotificationSink_Channels_And_History(
        string platform,
        InstanceAlertSeverity severity,
        string expectedChannel)
    {
        // WHAT: Test MobileNotificationSink channel routing and history recording.
        // WHY:  Ensures notifications route to correct native channels on Android, iOS, and iPadOS.
        // HOW:  Dispatches alert, asserts success, checks DispatchedAlerts collection, and tests Clear().
        var sink = new MobileNotificationSink(platform);
        Assert.NotNull(sink.Platform);
        var alert = CreateAlert(severity);

        bool result = await sink.SendNotificationAsync(alert);
        Assert.True(result);
        Assert.Single(sink.DispatchedAlerts);

        var record = sink.DispatchedAlerts[0];
        Assert.Equal(expectedChannel, record.ChannelId);
        Assert.Equal(alert, record.Alert);

        sink.Clear();
        Assert.Empty(sink.DispatchedAlerts);
    }

    /// <summary>
    /// Verifies error handling and platform resolution branches in <see cref="MobileNotificationSink"/>.
    /// </summary>
    [Fact]
    public async Task MobileNotificationSink_HandlesErrors_And_PlatformResolution()
    {
        // WHAT: Test boundary conditions in MobileNotificationSink.
        // WHY:  Ensures null checks, cancellation tokens, and unknown platforms fail safely.
        // HOW:  Tests null alert, pre-cancelled token, unknown platform, and predicate resolution.
        var sink = new MobileNotificationSink("android");
        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.SendNotificationAsync(null!).AsTask());

        // Cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool cancelledResult = await sink.SendNotificationAsync(CreateAlert(), cts.Token);
        Assert.False(cancelledResult);

        // Unknown platform
        var unknownSink = new MobileNotificationSink("solaris");
        bool unknownResult = await unknownSink.SendNotificationAsync(CreateAlert());
        Assert.False(unknownResult);

        // Platform resolution helper branches
        Assert.Equal("Android", MobileNotificationSink.ResolvePlatform("android"));
        Assert.Equal("iOS", MobileNotificationSink.ResolvePlatform("ios"));
        Assert.Equal("iPadOS", MobileNotificationSink.ResolvePlatform("ipados"));
        Assert.Equal("Unknown", MobileNotificationSink.ResolvePlatform("other"));

        // Fallback resolution with injected predicate and default system fallback
        Assert.Equal("Android", MobileNotificationSink.ResolvePlatform(null, p => p == "ANDROID"));
        Assert.Equal("iOS", MobileNotificationSink.ResolvePlatform(null, p => p == "IOS"));
        Assert.Equal("Unknown", MobileNotificationSink.ResolvePlatform(null, _ => false));
        Assert.Equal("Unknown", MobileNotificationSink.ResolvePlatform(null, null));
    }

    /// <summary>
    /// Verifies <see cref="MobileEntryPoint"/> metadata accessors, self-verification, and lifecycle loop.
    /// </summary>
    [Fact]
    public async Task MobileEntryPoint_Metadata_SelfVerification_And_Lifecycle()
    {
        // WHAT: Test MobileEntryPoint static methods.
        // WHY:  Validates mobile package metadata, self-verification probe, and lifecycle management.
        // HOW:  Calls metadata getters, executes VerifySelf, and runs lifecycle loop with cancellation and invalid args.
        Assert.Equal("one.lantea.nanoweaver.hrt", MobileEntryPoint.GetBundleIdentifier());
        Assert.Equal("NanoWeaver HRT", MobileEntryPoint.GetAppDisplayName());
        Assert.Equal("1.0.0", MobileEntryPoint.GetAppVersion());

        string report = MobileEntryPoint.VerifySelf();
        Assert.Contains("NanoWeaver HRT Mobile Platform Head v1.0.0-sovereign [Verified: Android, iOS, iPadOS", report);

        var sink = new MobileNotificationSink("android");

        // Null argument checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => MobileEntryPoint.RunMobileLifecycleAsync(null!, sink));
        await Assert.ThrowsAsync<ArgumentNullException>(() => MobileEntryPoint.RunMobileLifecycleAsync("android", null!));

        // Invalid platform argument
        int invalidExit = await MobileEntryPoint.RunMobileLifecycleAsync("unsupported-platform", sink);
        Assert.Equal(1, invalidExit);

        // Clean lifecycle with cancellation token hitting catch block
        using var cts = new CancellationTokenSource();
        var lifecycleTask = MobileEntryPoint.RunMobileLifecycleAsync("android", sink, cts.Token);
        cts.Cancel();
        int cleanExit = await lifecycleTask;
        Assert.Equal(0, cleanExit);
        Assert.NotEmpty(sink.DispatchedAlerts);

        // Clean lifecycle with pre-cancelled token hitting while loop natural exit
        using var preCancelledCts = new CancellationTokenSource();
        preCancelledCts.Cancel();
        int preCancelledExit = await MobileEntryPoint.RunMobileLifecycleAsync("android", sink, preCancelledCts.Token);
        Assert.Equal(0, preCancelledExit);

        // Exercise DispatchedMobileAlert record methods (ToString, Equals, GetHashCode, Deconstruct, with-expression)
        var alertRecord = sink.DispatchedAlerts[0];
        Assert.NotNull(alertRecord.ToString());
        Assert.Equal(alertRecord.GetHashCode(), alertRecord.GetHashCode());
        Assert.True(alertRecord.Equals(alertRecord));
        Assert.False(alertRecord.Equals((object?)null));
        var (recPlatform, recChannel, recAlert, recUtc) = alertRecord;
        Assert.Equal(alertRecord.Platform, recPlatform);
        Assert.Equal(alertRecord.ChannelId, recChannel);
        Assert.Equal(alertRecord.Alert, recAlert);
        Assert.Equal(alertRecord.DispatchedAtUtc, recUtc);
        var copyRecord = alertRecord with { Platform = "iOS" };
        Assert.Equal("iOS", copyRecord.Platform);
    }
}
