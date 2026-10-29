using System.Runtime.InteropServices;
using System.Text;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Gui.Core.Services;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Cross-platform desktop notification sink supporting Linux (FreeDesktop <c>notify-send</c>),
/// macOS (<c>osascript</c> user notifications), and Windows (PowerShell WinRT/NotifyIcon Toast).
/// </summary>
/// <remarks>
/// // WHAT: OS-native notification dispatcher for desktop operating systems.
/// // WHY:  Alerts operators immediately when sovereign nodes experience state transitions.
/// // HOW:  Detects host OS, sanitizes alert metadata, and invokes isolated platform notification binaries.
/// </remarks>
public sealed class DesktopNativeNotificationSink : INativeNotificationSink
{
    /// <summary>
    /// Operating system command execution engine.
    /// </summary>
    private readonly IPlatformProcessRunner _processRunner;

    /// <summary>
    /// Resolved target operating system identifier ("linux", "macos", "windows", or "unsupported").
    /// </summary>
    private readonly string _effectiveOs;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopNativeNotificationSink"/> class.
    /// </summary>
    /// <param name="processRunner">Process execution abstraction for launching notification utilities.</param>
    /// <param name="osPlatformOverride">Optional OS label ("linux", "macos", "windows") for deterministic cross-OS testing.</param>
    public DesktopNativeNotificationSink(IPlatformProcessRunner processRunner, string? osPlatformOverride = null)
        : this(processRunner, osPlatformOverride, null)
    {
    }

    /// <summary>
    /// Internal constructor supporting injected platform detection delegates for testing.
    /// </summary>
    /// <param name="processRunner">Process execution abstraction.</param>
    /// <param name="osPlatformOverride">Optional OS label override.</param>
    /// <param name="isPlatform">Optional platform detection delegate.</param>
    internal DesktopNativeNotificationSink(
        IPlatformProcessRunner processRunner,
        string? osPlatformOverride,
        Func<OSPlatform, bool>? isPlatform)
    {
        // WHAT: Validate and store process runner dependency and resolve target OS platform.
        // WHY:  Ensures notification subsystem has a valid command executor and predetermined OS strategy.
        // HOW:  Guards against null reference with ArgumentNullException and resolves effective OS.
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
        _effectiveOs = ResolvePlatform(osPlatformOverride, isPlatform);
    }

    /// <inheritdoc/>
    public ValueTask<bool> SendNotificationAsync(InstanceAlertEvent alert, CancellationToken ct = default)
    {
        // WHAT: Validate incoming alert payload before dispatching.
        // WHY:  Prevents invalid state or null reference exceptions during notification formatting.
        // HOW:  Evaluates null check and delegates to the appropriate platform dispatch algorithm.
        ArgumentNullException.ThrowIfNull(alert);

        return _effectiveOs switch
        {
            "linux" => SendLinuxNotificationAsync(alert, ct),
            "macos" => SendMacOsNotificationAsync(alert, ct),
            "windows" => SendWindowsNotificationAsync(alert, ct),
            _ => ValueTask.FromResult(false)
        };
    }

    /// <summary>
    /// Resolves the target operating system identifier in lowercase invariant format.
    /// </summary>
    /// <param name="osPlatformOverride">Optional platform override string.</param>
    /// <param name="isPlatform">Optional runtime platform predicate for test verification.</param>
    /// <returns>A normalized OS identifier string ("linux", "macos", "windows", or "unsupported").</returns>
    internal static string ResolvePlatform(string? osPlatformOverride, Func<OSPlatform, bool>? isPlatform = null)
    {
        // WHAT: Determine operating system identity using override or runtime inspection.
        // WHY:  Allows deterministic test scenarios on any host while defaulting to real host platform.
        // HOW:  Checks string override first, otherwise invokes OS platform predicate.
        if (!string.IsNullOrWhiteSpace(osPlatformOverride))
        {
            string normalized = osPlatformOverride.Trim().ToLowerInvariant();
            if (normalized is "linux")
            {
                return "linux";
            }

            if (normalized is "macos" or "osx")
            {
                return "macos";
            }

            if (normalized is "windows" or "win")
            {
                return "windows";
            }

            return "unsupported";
        }

        Func<OSPlatform, bool> checker = isPlatform ?? RuntimeInformation.IsOSPlatform;
        if (checker(OSPlatform.Linux))
        {
            return "linux";
        }

        if (checker(OSPlatform.OSX))
        {
            return "macos";
        }

        if (checker(OSPlatform.Windows))
        {
            return "windows";
        }

        return "unsupported";
    }

    /// <summary>
    /// Dispatches a desktop notification on Linux using the FreeDesktop <c>notify-send</c> binary.
    /// </summary>
    /// <param name="alert">The instance alert event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the notification process succeeded; otherwise false.</returns>
    private ValueTask<bool> SendLinuxNotificationAsync(InstanceAlertEvent alert, CancellationToken ct)
    {
        // WHAT: Dispatch FreeDesktop notification via notify-send with explicit urgency.
        // WHY:  Adheres to XDG desktop notification standards without shell interpolation.
        // HOW:  Maps severity to -u low/normal/critical, sets app name, and passes title and body in ArgumentList.
        string urgency = alert.Severity switch
        {
            InstanceAlertSeverity.Critical => "critical",
            InstanceAlertSeverity.Warning => "normal",
            _ => "low"
        };

        string title = $"[{Sanitize(alert.InstanceDisplayName)}] {Sanitize(alert.Title)}";
        string message = Sanitize(alert.Message);
        string[] args = ["-u", urgency, "-a", "NanoWeaver HRT", title, message];

        return _processRunner.TryRunAsync("notify-send", args, ct);
    }

    /// <summary>
    /// Dispatches a desktop notification on macOS using AppleScript display notification via <c>osascript</c>.
    /// </summary>
    /// <param name="alert">The instance alert event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the notification process succeeded; otherwise false.</returns>
    private ValueTask<bool> SendMacOsNotificationAsync(InstanceAlertEvent alert, CancellationToken ct)
    {
        // WHAT: Dispatch macOS user notification via osascript AppleScript engine.
        // WHY:  Displays native Notification Center alerts on macOS Sonoma and Sequoia.
        // HOW:  Constructs sanitized AppleScript command string and invokes osascript with -e flag.
        string cleanMsg = SanitizeAppleScript(alert.Message);
        string cleanTitle = SanitizeAppleScript(alert.Title);
        string cleanSub = SanitizeAppleScript(alert.InstanceDisplayName);

        string script = $"display notification \"{cleanMsg}\" with title \"{cleanTitle}\" subtitle \"{cleanSub}\";";
        string[] args = ["-e", script];

        return _processRunner.TryRunAsync("osascript", args, ct);
    }

    /// <summary>
    /// Dispatches a desktop notification on Windows using PowerShell WinRT Toast notification.
    /// </summary>
    /// <param name="alert">The instance alert event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the notification process succeeded; otherwise false.</returns>
    private ValueTask<bool> SendWindowsNotificationAsync(InstanceAlertEvent alert, CancellationToken ct)
    {
        // WHAT: Dispatch Windows toast notification via PowerShell WinRT XML notifier.
        // WHY:  Provides native Action Center toasts without external native C++ dependencies.
        // HOW:  Passes title and message as discrete arguments to a static parameterized PowerShell script.
        const string script = """
            param($title, $message)
            try {
                [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
                $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
                $textNodes = $template.GetElementsByTagName('text')
                $textNodes.Item(0).AppendChild($template.CreateTextNode($title)) | Out-Null
                $textNodes.Item(1).AppendChild($template.CreateTextNode($message)) | Out-Null
                $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
                [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('one.lantea.nanoweaver.hrt').Show($toast)
            } catch {
                [void] [System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms')
                $notify = New-Object System.Windows.Forms.NotifyIcon
                $notify.Icon = [System.Drawing.SystemIcons]::Information
                $notify.Visible = $true
                $notify.ShowBalloonTip(5000, $title, $message, [System.Windows.Forms.ToolTipIcon]::Info)
            }
            """;

        string title = $"[{Sanitize(alert.InstanceDisplayName)}] {Sanitize(alert.Title)}";
        string message = Sanitize(alert.Message);
        string[] args = ["-NoProfile", "-NonInteractive", "-Command", script, title, message];

        return _processRunner.TryRunAsync("powershell", args, ct);
    }

    /// <summary>
    /// Sanitizes plain text by removing ASCII and Unicode control characters.
    /// </summary>
    /// <param name="input">Raw string to sanitize.</param>
    /// <returns>Sanitized string with control characters replaced by spaces.</returns>
    internal static string Sanitize(string input)
    {
        // WHAT: Strip control characters from user-supplied strings.
        // WHY:  Prevents terminal escape sequence injection or formatting truncation.
        // HOW:  Iterates characters and converts any control character to a space.
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsControl(c))
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Escapes double quotes and backslashes for AppleScript string literals.
    /// </summary>
    /// <param name="input">Raw string to escape.</param>
    /// <returns>Escaped AppleScript-safe string.</returns>
    private static string SanitizeAppleScript(string input)
    {
        // WHAT: Escape quotation marks and backslashes in AppleScript literals.
        // WHY:  Ensures that arbitrary strings do not terminate AppleScript string tokens.
        // HOW:  First sanitizes control characters, then performs string replacement of backslash and quote.
        string cleaned = Sanitize(input);
        return cleaned.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
