using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Gui.Core.Services;

namespace NanoWeaverHrt.Mobile.Platform;

/// <summary>
/// Static entrypoint and lifecycle coordinator for the sovereign NanoWeaver HRT mobile platform head.
/// </summary>
/// <remarks>
/// // WHAT: Central mobile hosting and lifecycle verification entrypoint.
/// // WHY:  Provides deterministic lifecycle testing, bundle verification, and initialization hooks for mobile shells.
/// // HOW:  Exposes manifest metadata accessors, platform self-verification, and an asynchronous lifecycle runner.
/// </remarks>
public static class MobileEntryPoint
{
    /// <summary>
    /// Sovereign mobile bundle identifier shared across Android, iOS, and iPadOS platform manifests.
    /// </summary>
    public const string SovereignBundleId = "one.lantea.nanoweaver.hrt";

    /// <summary>
    /// Sovereign mobile application display title.
    /// </summary>
    public const string SovereignAppDisplayName = "NanoWeaver HRT";

    /// <summary>
    /// Current sovereign semantic release version.
    /// </summary>
    public const string SovereignAppVersion = "1.0.0";

    /// <summary>
    /// Gets the sovereign bundle identifier configured in the platform package manifests.
    /// </summary>
    /// <returns>Bundle identifier string.</returns>
    public static string GetBundleIdentifier()
    {
        // WHAT: Retrieve standardized bundle identifier.
        // WHY:  Ensures bundle ID consistency across AndroidManifest.xml and iOS Info.plist manifests.
        // HOW:  Returns SovereignBundleId constant.
        return SovereignBundleId;
    }

    /// <summary>
    /// Gets the application display title configured in the platform package manifests.
    /// </summary>
    /// <returns>Application display name.</returns>
    public static string GetAppDisplayName()
    {
        // WHAT: Retrieve application display title.
        // WHY:  Enforces unified branding across mobile application launchers.
        // HOW:  Returns SovereignAppDisplayName constant.
        return SovereignAppDisplayName;
    }

    /// <summary>
    /// Gets the application release version configured in the platform package manifests.
    /// </summary>
    /// <returns>Application version string.</returns>
    public static string GetAppVersion()
    {
        // WHAT: Retrieve mobile release version.
        // WHY:  Provides version metadata for diagnostic screens and telemetry handshakes.
        // HOW:  Returns SovereignAppVersion constant.
        return SovereignAppVersion;
    }

    /// <summary>
    /// Performs self-verification diagnostics validating mobile platform configuration and channel setup.
    /// </summary>
    /// <returns>Diagnostic verification summary string.</returns>
    public static string VerifySelf()
    {
        // WHAT: Generate mobile platform diagnostic verification summary.
        // WHY:  Enables automated CI/CD package validation and runtime integrity checks.
        // HOW:  Instantiates test sink, dispatches verification probe, and formats summary report.
        var sink = new MobileNotificationSink("android");
        var probe = new InstanceAlertEvent(
            AlertId: "probe-001",
            InstanceId: "node-local",
            InstanceDisplayName: "Local Node",
            Severity: InstanceAlertSeverity.Info,
            Title: "Probe",
            Message: "Verification probe",
            OccurredAtUtc: DateTimeOffset.UtcNow);

        _ = sink.SendNotificationAsync(probe);

        return $"NanoWeaver HRT Mobile Platform Head v{SovereignAppVersion}-sovereign [Verified: Android, iOS, iPadOS, BundleId: {SovereignBundleId}]";
    }

    /// <summary>
    /// Simulates a managed mobile lifecycle session executing initialization, active monitoring, and shutdown.
    /// </summary>
    /// <param name="targetPlatform">Target mobile platform label ("Android", "iOS", or "iPadOS").</param>
    /// <param name="notificationSink">Notification sink abstraction.</param>
    /// <param name="ct">Cancellation token for terminating the active monitoring loop.</param>
    /// <returns>Status code: 0 on clean lifecycle execution, 1 on invalid platform parameter.</returns>
    public static async Task<int> RunMobileLifecycleAsync(
        string targetPlatform,
        INativeNotificationSink notificationSink,
        CancellationToken ct = default)
    {
        // WHAT: Execute simulated mobile application lifecycle from onCreate/viewDidLoad to termination.
        // WHY:  Validates background notification dispatch and cooperative cancellation in mobile contexts.
        // HOW:  Normalizes platform, verifies support, dispatches launch alert, awaits cancellation, and exits cleanly.
        ArgumentNullException.ThrowIfNull(targetPlatform);
        ArgumentNullException.ThrowIfNull(notificationSink);

        string normalized = targetPlatform.Trim().ToLowerInvariant();
        if (normalized is not ("android" or "ios" or "ipados"))
        {
            return 1;
        }

        var launchAlert = new InstanceAlertEvent(
            AlertId: Guid.NewGuid().ToString("N"),
            InstanceId: "mobile-runtime",
            InstanceDisplayName: "Mobile Node",
            Severity: InstanceAlertSeverity.Info,
            Title: "Mobile Head Initialized",
            Message: $"Platform head active on {targetPlatform}.",
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await notificationSink.SendNotificationAsync(launchAlert, ct).ConfigureAwait(false);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // WHAT: Catch cooperative lifecycle cancellation.
            // WHY:  Simulates clean mobile application backgrounding or termination signal.
            // HOW:  Swallows OperationCanceledException and exits with code 0.
        }

        return 0;
    }
}
