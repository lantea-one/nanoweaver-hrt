namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Represents the live connection and health status of a managed nanoweaver-hrt instance.
/// </summary>
public enum InstanceConnectionState
{
    /// <summary>
    /// Instance has not yet been polled or is disconnected.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// Challenge-response handshake or health probe is in progress.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// Instance is authenticated and responding normally.
    /// </summary>
    Online = 2,

    /// <summary>
    /// Instance responded but reported degraded subsystem health or thermal throttling.
    /// </summary>
    Degraded = 3,

    /// <summary>
    /// Instance failed authentication or rejected credentials (401/403).
    /// </summary>
    Unauthorized = 4,

    /// <summary>
    /// Instance is unreachable due to network or transport error.
    /// </summary>
    Offline = 5
}
