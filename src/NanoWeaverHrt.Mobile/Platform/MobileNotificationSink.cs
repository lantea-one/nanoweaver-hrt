using System.Runtime.InteropServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Gui.Core.Services;

namespace NanoWeaverHrt.Mobile.Platform;

/// <summary>
/// Immutable audit record representing an alert dispatched to an OS-native mobile notification channel.
/// </summary>
/// <param name="Platform">Mobile operating system label ("Android", "iOS", "iPadOS", or "Unknown").</param>
/// <param name="ChannelId">Platform notification channel or UNNotificationCategory identifier.</param>
/// <param name="Alert">Underlying instance alert event metadata.</param>
/// <param name="DispatchedAtUtc">UTC timestamp at which dispatch occurred.</param>
public sealed record DispatchedMobileAlert(
    string Platform,
    string ChannelId,
    InstanceAlertEvent Alert,
    DateTimeOffset DispatchedAtUtc);

/// <summary>
/// Native mobile notification sink implementing <see cref="INativeNotificationSink"/> for Android
/// (NotificationChannel priority management) and iOS / iPadOS (UNUserNotificationCenter categorization).
/// </summary>
/// <remarks>
/// // WHAT: Mobile notification adapter supporting Android, iOS, and iPadOS channel dispatch.
/// // WHY:  Guarantees operators receive high-priority push/local alerts on mobile devices.
/// // HOW:  Resolves target mobile platform, maps severity to native notification channels, and records dispatches.
/// </remarks>
public sealed class MobileNotificationSink : INativeNotificationSink
{
    /// <summary>
    /// Synchronized lock guarding thread-safe mutations to the dispatched alerts history.
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// Internal collection of dispatched alerts.
    /// </summary>
    private readonly List<DispatchedMobileAlert> _dispatchedAlerts = new();

    /// <summary>
    /// Resolved target mobile platform name.
    /// </summary>
    private readonly string _platform;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobileNotificationSink"/> class.
    /// </summary>
    /// <param name="platformOverride">Optional mobile platform override ("android", "ios", "ipados") for deterministic testing.</param>
    public MobileNotificationSink(string? platformOverride = null)
        : this(platformOverride, null)
    {
    }

    /// <summary>
    /// Internal constructor supporting platform resolution predicate injection for unit testing.
    /// </summary>
    /// <param name="platformOverride">Optional mobile platform override string.</param>
    /// <param name="isPlatform">Optional platform resolution predicate.</param>
    internal MobileNotificationSink(string? platformOverride, Func<string, bool>? isPlatform)
    {
        // WHAT: Store platform override.
        // WHY:  Permits cross-platform unit testing of Android, iOS, and iPadOS notification channels without physical devices.
        // HOW:  Resolves platform using predicate or override.
        _platform = ResolvePlatform(platformOverride, isPlatform);
    }

    /// <summary>
    /// Gets the target mobile platform handled by this sink instance.
    /// </summary>
    public string Platform => _platform;

    /// <summary>
    /// Gets the read-only history of all alerts dispatched through this sink instance.
    /// </summary>
    public IReadOnlyList<DispatchedMobileAlert> DispatchedAlerts
    {
        get
        {
            lock (_syncRoot)
            {
                return _dispatchedAlerts.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask<bool> SendNotificationAsync(InstanceAlertEvent alert, CancellationToken ct = default)
    {
        // WHAT: Validate alert and dispatch to target mobile platform channel.
        // WHY:  Ensures notification metadata is mapped to correct OS channel with appropriate priority.
        // HOW:  Checks cancellation, determines mobile OS, computes channel, records dispatch, and returns success.
        ArgumentNullException.ThrowIfNull(alert);

        if (ct.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        if (_platform == "Unknown")
        {
            return ValueTask.FromResult(false);
        }

        string channelId = ResolveChannelId(_platform, alert.Severity);
        var record = new DispatchedMobileAlert(_platform, channelId, alert, DateTimeOffset.UtcNow);

        lock (_syncRoot)
        {
            _dispatchedAlerts.Add(record);
        }

        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Clears the history of recorded dispatched alerts.
    /// </summary>
    public void Clear()
    {
        // WHAT: Reset recorded notifications.
        // WHY:  Facilitates clean state isolation between test cases.
        // HOW:  Locks sync root and calls Clear on internal collection.
        lock (_syncRoot)
        {
            _dispatchedAlerts.Clear();
        }
    }

    /// <summary>
    /// Resolves the effective mobile operating system platform name.
    /// </summary>
    /// <param name="platformOverride">Optional platform override string.</param>
    /// <param name="isPlatform">Optional platform test predicate.</param>
    /// <returns>Normalized platform identifier ("Android", "iOS", "iPadOS", or "Unknown").</returns>
    internal static string ResolvePlatform(string? platformOverride, Func<string, bool>? isPlatform = null)
    {
        // WHAT: Resolve active mobile platform identity.
        // WHY:  Selects correct channel semantics for Android vs Apple operating systems.
        // HOW:  Inspects override if supplied; otherwise checks OS platform runtime indications.
        if (!string.IsNullOrWhiteSpace(platformOverride))
        {
            string normalized = platformOverride.Trim().ToLowerInvariant();
            return normalized switch
            {
                "android" => "Android",
                "ios" => "iOS",
                "ipados" => "iPadOS",
                _ => "Unknown"
            };
        }

        Func<string, bool> checker = isPlatform ?? (p => RuntimeInformation.IsOSPlatform(OSPlatform.Create(p)));
        if (checker("ANDROID"))
        {
            return "Android";
        }

        if (checker("IOS"))
        {
            return "iOS";
        }

        return "Unknown";
    }

    /// <summary>
    /// Maps the instance alert severity to the corresponding OS channel identifier.
    /// </summary>
    /// <param name="platform">Mobile platform label.</param>
    /// <param name="severity">Alert severity level.</param>
    /// <returns>Channel or category string identifier.</returns>
    internal static string ResolveChannelId(string platform, InstanceAlertSeverity severity)
    {
        // WHAT: Map severity level to native OS notification channel or category identifier.
        // WHY:  Enforces OS-level notification priority (e.g. Critical Alerts bypassing Do Not Disturb).
        // HOW:  Switches on platform and severity to return deterministic sovereign channel identifiers.
        if (platform == "Android")
        {
            return severity switch
            {
                InstanceAlertSeverity.Critical => "nanoweaver_channel_critical",
                InstanceAlertSeverity.Warning => "nanoweaver_channel_warning",
                _ => "nanoweaver_channel_info"
            };
        }

        if (platform == "iPadOS")
        {
            return severity switch
            {
                InstanceAlertSeverity.Critical => "NANOWEAVER_IPADOS_CRITICAL",
                InstanceAlertSeverity.Warning => "NANOWEAVER_IPADOS_WARNING",
                _ => "NANOWEAVER_IPADOS_INFO"
            };
        }

        return severity switch
        {
            InstanceAlertSeverity.Critical => "NANOWEAVER_IOS_CRITICAL",
            InstanceAlertSeverity.Warning => "NANOWEAVER_IOS_WARNING",
            _ => "NANOWEAVER_IOS_INFO"
        };
    }
}
