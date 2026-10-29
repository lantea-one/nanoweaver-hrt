namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Severity level for cross-instance notifications and tray alerts.
/// </summary>
public enum InstanceAlertSeverity
{
    /// <summary>
    /// Informational event such as simulation completion or node recovery.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning event such as elevated GPU temperature or approaching idle expiry.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Critical event such as node offline, authentication failure, or audit ledger fault.
    /// </summary>
    Critical = 2
}
