namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Abstraction defining the desktop graphical user interface execution lifecycle.
/// </summary>
/// <remarks>
/// // WHAT: Contract orchestrating the desktop GUI window and system tray message loop.
/// // WHY:  Decouples GUI presentation logic from the CLI entrypoint for headless testability.
/// // HOW:  Defines an asynchronous RunUiAsync method cancelled via CancellationToken.
/// </remarks>
public interface IDesktopUiHost
{
    /// <summary>
    /// Launches the desktop user interface shell and runs the active window lifecycle until terminated.
    /// </summary>
    /// <param name="ct">Cancellation token signalling process shutdown.</param>
    /// <returns>Process exit code: 0 on normal exit, non-zero on unhandled exception.</returns>
    Task<int> RunUiAsync(CancellationToken ct = default);
}
