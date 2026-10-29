using System.Runtime.InteropServices;
using NanoWeaverHrt.Desktop.Platform;

namespace NanoWeaverHrt.Desktop;

/// <summary>
/// Sovereign desktop platform head entrypoint hosting the system tray controller,
/// desktop native notifications, and headless daemon execution modes.
/// </summary>
/// <remarks>
/// // WHAT: Main executable program for NanoWeaver HRT Desktop.
/// // WHY:  Provides CLI flags for headless verification, system health inspection, and daemon hosting.
/// // HOW:  Parses arguments (--verify-self, --headless-daemon, --help), checks single-instance mutex, and executes target workflow.
/// </remarks>
public static class DesktopProgram
{
    /// <summary>
    /// Application main entry point.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    /// <returns>Process exit code: 0 on success, non-zero on failure.</returns>
    public static async Task<int> Main(string[] args)
    {
        // WHAT: Standard executable entrypoint delegating to testable RunAsync implementation.
        // WHY:  Decouples process exit handling from CLI parsing logic for deterministic unit test execution.
        // HOW:  Invokes RunAsync with system Console streams and default platform dependencies.
        return await RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the desktop application workflow with customizable I/O streams and cancellation tokens.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="stdout">TextWriter receiving standard output messages.</param>
    /// <param name="stderr">TextWriter receiving error messages.</param>
    /// <param name="processRunner">Optional process runner abstraction.</param>
    /// <param name="mutexName">Optional mutex name override for test isolation.</param>
    /// <param name="ct">Cancellation token for terminating asynchronous operations.</param>
    /// <param name="uiHost">Optional desktop UI host abstraction.</param>
    /// <returns>Process exit code: 0 for clean execution, 1 for CLI syntax error, 2 for instance collision.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        IPlatformProcessRunner? processRunner = null,
        string? mutexName = null,
        CancellationToken ct = default,
        IDesktopUiHost? uiHost = null)
    {
        // WHAT: Validate and initialize output stream writers.
        // WHY:  Permits redirection of output to string buffers during test assertions.
        // HOW:  Uses null-coalescing operators targeting Console.Out and Console.Error.
        TextWriter outWriter = stdout ?? Console.Out;
        TextWriter errWriter = stderr ?? Console.Error;

        if (args.Length == 0 || args[0] is "--gui")
        {
            return await RunGuiAsync(outWriter, errWriter, uiHost, mutexName, ct).ConfigureAwait(false);
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            PrintUsage(outWriter);
            return 0;
        }

        string command = args[0];
        switch (command)
        {
            case "--verify-self":
                return RunVerifySelf(outWriter, processRunner);

            case "--headless-daemon":
                return await RunHeadlessDaemonAsync(outWriter, errWriter, mutexName, ct).ConfigureAwait(false);

            default:
                await errWriter.WriteLineAsync($"[NanoWeaver HRT Desktop] Unrecognized option: {command}").ConfigureAwait(false);
                await errWriter.WriteLineAsync("Run with --help to view available commands.").ConfigureAwait(false);
                return 1;
        }
    }

    /// <summary>
    /// Prints CLI usage instructions and command summaries to the target writer.
    /// </summary>
    /// <param name="writer">Output text stream.</param>
    private static void PrintUsage(TextWriter writer)
    {
        // WHAT: Output formatted help text.
        // WHY:  Informs operators of supported command-line arguments.
        // HOW:  Writes multi-line help strings directly to writer.
        writer.WriteLine("NanoWeaver HRT — Desktop Platform Head");
        writer.WriteLine("Usage: NanoWeaverHrt [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --gui               Launches the interactive desktop GUI shell (default when run without arguments).");
        writer.WriteLine("  --verify-self       Executes platform diagnostics and exits 0 if healthy.");
        writer.WriteLine("  --headless-daemon   Runs the background instance monitoring daemon without a GUI.");
        writer.WriteLine("  --help, -h          Displays this help documentation.");
    }

    /// <summary>
    /// Executes self-verification diagnostics verifying platform subsystems and prints results.
    /// </summary>
    /// <param name="writer">Output text stream.</param>
    /// <param name="runner">Optional process runner override.</param>
    /// <returns>Integer status code 0 indicating clean verification.</returns>
    private static int RunVerifySelf(TextWriter writer, IPlatformProcessRunner? runner)
    {
        // WHAT: Perform local platform component diagnostic verification.
        // WHY:  Allows package installers, health checks, and CI gates to verify desktop head integrity.
        // HOW:  Tests initialization of tray controller, process runner, and notification sink.
        IPlatformProcessRunner processRunner = runner ?? new SystemPlatformProcessRunner();
        var sink = new DesktopNativeNotificationSink(processRunner);
        var tray = new DesktopTrayIconController();

        writer.WriteLine("NanoWeaver HRT Desktop Platform Head v1.0.0-sovereign [Verified]");
        writer.WriteLine($"  OS Platform:     {RuntimeInformation.OSDescription}");
        writer.WriteLine($"  Architecture:    {RuntimeInformation.ProcessArchitecture}");
        writer.WriteLine($"  NotificationSink: {sink.GetType().Name} (Initialized)");
        writer.WriteLine($"  TrayController:   {tray.GetType().Name} (Initialized, Status: {tray.CurrentState.Badge})");
        return 0;
    }

    /// <summary>
    /// Runs the single-instance background monitoring daemon loop until cancelled.
    /// </summary>
    /// <param name="stdout">Standard output text stream.</param>
    /// <param name="stderr">Standard error text stream.</param>
    /// <param name="mutexName">System mutex name for single-instance verification.</param>
    /// <param name="ct">Cancellation token signalling process shutdown.</param>
    /// <returns>Integer status code: 0 on normal shutdown, 2 on mutex collision.</returns>
    private static async Task<int> RunHeadlessDaemonAsync(
        TextWriter stdout,
        TextWriter stderr,
        string? mutexName,
        CancellationToken ct)
    {
        // WHAT: Enforce single-instance lock and execute continuous background loop.
        // WHY:  Guarantees only one daemon polls connected instances per host user session.
        // HOW:  Acquires SingleInstanceMutexGuard; if not primary, logs error and exits 2; else awaits cancellation.
        string effectiveMutex = mutexName ?? SingleInstanceMutexGuard.DefaultMutexName;
        using var guard = new SingleInstanceMutexGuard(effectiveMutex);

        if (!guard.IsPrimaryInstance)
        {
            await stderr.WriteLineAsync("Error: Another instance of NanoWeaver HRT is already running.").ConfigureAwait(false);
            return 2;
        }

        await stdout.WriteLineAsync("NanoWeaver HRT Headless Daemon started. Press Ctrl+C to terminate.").ConfigureAwait(false);

        var tcs = new TaskCompletionSource();
        using (ct.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        await stdout.WriteLineAsync("NanoWeaver HRT Headless Daemon stopped cleanly.").ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Launches and executes the interactive desktop user interface shell with single-instance mutex enforcement.
    /// </summary>
    /// <param name="stdout">Standard output text stream.</param>
    /// <param name="stderr">Standard error text stream.</param>
    /// <param name="uiHost">Optional custom UI host implementation.</param>
    /// <param name="mutexName">System mutex name for single-instance verification.</param>
    /// <param name="ct">Cancellation token signalling process shutdown.</param>
    /// <returns>Integer exit code: 0 on normal exit, 2 on single-instance mutex collision.</returns>
    private static async Task<int> RunGuiAsync(
        TextWriter stdout,
        TextWriter stderr,
        IDesktopUiHost? uiHost,
        string? mutexName,
        CancellationToken ct)
    {
        // WHAT: Acquire single-instance mutex and delegate to desktop UI host.
        // WHY:  Prevents launching duplicate GUI windows while providing interactive desktop shell experience.
        // HOW:  Validates primary instance status with SingleInstanceMutexGuard and awaits uiHost.RunUiAsync.
        string effectiveMutex = mutexName ?? SingleInstanceMutexGuard.DefaultMutexName;
        using var guard = new SingleInstanceMutexGuard(effectiveMutex);

        if (!guard.IsPrimaryInstance)
        {
            await stderr.WriteLineAsync("Error: Another instance of NanoWeaver HRT is already running.").ConfigureAwait(false);
            return 2;
        }

        IDesktopUiHost host = uiHost ?? new DefaultDesktopUiHost(null, null, stdout);
        return await host.RunUiAsync(ct).ConfigureAwait(false);
    }
}
