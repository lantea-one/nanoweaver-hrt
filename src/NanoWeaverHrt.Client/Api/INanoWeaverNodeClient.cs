using NanoWeaverHrt.Client.Storage;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Provides authenticated REST API operations against a single nanoweaver-hrt instance.
/// </summary>
public interface INanoWeaverNodeClient
{
    /// <summary>
    /// Executes the Ed25519 or ML-DSA-65 challenge-response handshake against /api/auth/challenge and /api/auth/token.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="passphrase">User passphrase to unseal the client private key envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Issued Bearer JWT and expiration metadata.</returns>
    Task<NodeAuthSession> AuthenticateAsync(
        InstanceProfile profile,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Polls /api/system/telemetry and /api/simulations/campaigns to build a live instance snapshot.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token obtained from AuthenticateAsync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current telemetry and health snapshot.</returns>
    Task<InstanceConnectionSnapshot> PollSnapshotAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Triggers a named simulation campaign (serum-stability, aptamer-gating, payload-leakage, or pbpk-endocrine).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="simulationType">Simulation campaign route slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the campaign was accepted; otherwise throws a sanitized NodeApiException.</returns>
    Task<bool> StartSimulationCampaignAsync(
        InstanceProfile profile,
        string bearerToken,
        string simulationType,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves raw telemetry metrics from /api/system/telemetry.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized telemetry snapshot.</returns>
    Task<NodeTelemetrySnapshot> GetTelemetryAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the list of available simulation campaigns from /api/simulations/campaigns.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of simulation campaign summaries.</returns>
    Task<IReadOnlyList<SimulationCampaignSummary>> GetCampaignsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the currently active endocrine profile from /api/endocrine/profile.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active endocrine profile summary.</returns>
    Task<EndocrineProfileSummary> GetEndocrineProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);
}
