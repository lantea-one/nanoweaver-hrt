using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.ViewModels;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Default implementation of <see cref="IDesktopUiHost"/> hosting the sovereign desktop shell,
/// ViewModels, and native system tray controller.
/// </summary>
/// <remarks>
/// // WHAT: Coordinates ViewModel initialization, system tray icon state, and desktop lifecycle.
/// // WHY:  Provides a concrete hosting layer that executes the sovereign GUI and handles clean termination.
/// // HOW:  Initializes MainShellViewModel and DesktopTrayIconController, running until cancellation is signalled.
/// </remarks>
public sealed class DefaultDesktopUiHost : IDesktopUiHost
{
    private readonly MainShellViewModel? _shellViewModel;
    private readonly DesktopTrayIconController _trayController;
    private readonly TextWriter _outWriter;

    /// <summary>
    /// Gets the active <see cref="MainShellViewModel"/> managed by this UI host.
    /// </summary>
    public MainShellViewModel? ShellViewModel => _shellViewModel;

    /// <summary>
    /// Gets the <see cref="DesktopTrayIconController"/> managed by this UI host.
    /// </summary>
    public DesktopTrayIconController TrayController => _trayController;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDesktopUiHost"/> class.
    /// </summary>
    /// <param name="shellViewModel">Optional shell ViewModel instance.</param>
    /// <param name="trayController">Optional tray icon controller instance.</param>
    /// <param name="outWriter">Optional text writer for logging output.</param>
    public DefaultDesktopUiHost(
        MainShellViewModel? shellViewModel = null,
        DesktopTrayIconController? trayController = null,
        TextWriter? outWriter = null)
    {
        // WHAT: Assign dependencies with fallback defaults.
        // WHY:  Allows dependency injection during test execution while providing sane defaults in production.
        // HOW:  Uses null-coalescing operators targeting new DesktopTrayIconController and Console.Out.
        _shellViewModel = shellViewModel;
        _trayController = trayController ?? new DesktopTrayIconController();
        _outWriter = outWriter ?? Console.Out;
    }

    /// <summary>
    /// Executes the desktop UI shell lifecycle loop until cancelled.
    /// </summary>
    /// <param name="ct">Cancellation token signalling application termination.</param>
    /// <returns>Process exit code 0 on clean exit.</returns>
    public async Task<int> RunUiAsync(CancellationToken ct = default)
    {
        // WHAT: Run the desktop UI message loop.
        // WHY:  Keeps the desktop window and tray icon resident in memory until user initiates shutdown.
        // HOW:  Initializes tray status, logs startup, and awaits cancellation token completion.
        _trayController.ApplyState(new TrayMenuState(
            Badge: TrayHealthBadge.Idle,
            TooltipText: "NanoWeaver HRT — Desktop Shell Active",
            IsMainWindowVisible: true,
            AreNotificationsMuted: false,
            InstanceSummaries: []));

        await _outWriter.WriteLineAsync("[NanoWeaver HRT Desktop] Windowed UI Shell initialized.").ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            return 0;
        }

        var tcs = new TaskCompletionSource();
        using (ct.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        await _outWriter.WriteLineAsync("[NanoWeaver HRT Desktop] Windowed UI Shell shutting down.").ConfigureAwait(false);
        return 0;
    }
}
