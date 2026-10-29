namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Coordinates authentication sessions, telemetry polling, and alert detection across multiple instances.
/// </summary>
public interface IMultiInstanceCoordinator
{
    /// <summary>
    /// Gets the current snapshots of all registered instances.
    /// </summary>
    /// <returns>Read-only list of instance snapshots.</returns>
    IReadOnlyList<InstanceConnectionSnapshot> GetSnapshots();

    /// <summary>
    /// Unlocks and authenticates a configured instance profile using the user's passphrase.
    /// </summary>
    /// <param name="instanceId">Target instance identifier.</param>
    /// <param name="passphrase">Passphrase for the instance's sealed private key envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated connection snapshot after handshake.</returns>
    Task<InstanceConnectionSnapshot> ConnectInstanceAsync(
        string instanceId,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Disconnects an instance and zeroes its cached Bearer token.
    /// </summary>
    /// <param name="instanceId">Target instance identifier.</param>
    void DisconnectInstance(string instanceId);

    /// <summary>
    /// Polls all connected instances once and returns any newly detected state-transition alerts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of alerts generated during this poll cycle.</returns>
    Task<IReadOnlyList<InstanceAlertEvent>> PollAllConnectedAsync(CancellationToken ct = default);
}
