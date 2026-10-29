namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Abstracts operating system process execution for dispatching native desktop notifications
/// and invoking platform tools without spawning real OS processes during unit test suites.
/// </summary>
/// <remarks>
/// // WHAT: Interface abstracting external process execution.
/// // WHY:  Enables 100.00% deterministic test execution and prevents shell injection risks.
/// // HOW:  Defines an asynchronous method accepting an argument list rather than a composite shell string.
/// </remarks>
public interface IPlatformProcessRunner
{
    /// <summary>
    /// Executes an operating system command with an argument list and returns true if the exit code is 0.
    /// </summary>
    /// <param name="executable">Executable binary name or path.</param>
    /// <param name="arguments">Sanitized argument list (never concatenated into a shell command string).</param>
    /// <param name="ct">Cancellation token for process execution timeout or abort.</param>
    /// <returns>True if the process was launched and completed with status 0; otherwise false.</returns>
    ValueTask<bool> TryRunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default);
}
