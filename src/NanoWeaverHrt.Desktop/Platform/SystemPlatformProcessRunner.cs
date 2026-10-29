using System.Diagnostics;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Production process runner implementing <see cref="IPlatformProcessRunner"/> using <see cref="Process"/>
/// and safe argument vector passing without shell invocation.
/// </summary>
/// <remarks>
/// // WHAT: OS process execution engine adhering to sovereign zero-injection security directives.
/// // WHY:  Guarantees that arguments are forwarded directly via OS exec/CreateProcess boundaries.
/// // HOW:  Iterates over the argument list and appends each element to <see cref="ProcessStartInfo.ArgumentList"/>.
/// </remarks>
public sealed class SystemPlatformProcessRunner : IPlatformProcessRunner
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPlatformProcessRunner"/> class.
    /// </summary>
    public SystemPlatformProcessRunner()
    {
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryRunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        // WHAT: Validate non-null inputs before attempting process instantiation.
        // WHY:  Enforces non-nullable reference contracts and prevents null pointer faults.
        // HOW:  Uses standard ArgumentNullException guard clauses.
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        // WHAT: Launch external command with strict argument list isolation.
        // WHY:  Avoids cmd.exe or /bin/sh command injection vulnerabilities.
        // HOW:  Sets UseShellExecute to false and populates ProcessStartInfo.ArgumentList directly.
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = executable;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            for (int i = 0; i < arguments.Count; i++)
            {
                process.StartInfo.ArgumentList.Add(arguments[i]);
            }

            process.Start();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
