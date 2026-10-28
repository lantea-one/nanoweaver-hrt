# Work Package P27: Multi-Instance REST Client SDK & PKI Auth Orchestrator (`NanoWeaverHrt.Client`)

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: The standalone desktop and mobile GUI must connect concurrently to multiple local and remote `nanoweaver-hrt` instances, perform `Ed25519` / `ML-DSA-65` challenge-response authentication (`POST /api/auth/challenge` -> `POST /api/auth/token`), refresh sessions before the 15-minute idle expiration, query node telemetry/simulations/endocrine kinetics/services, and emit structured health/simulation transition events.
- **Goal & Scope**:
  - **Included**: Implement `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, `IMultiInstanceCoordinator`, `MultiInstanceCoordinator`, and strongly-typed client DTOs (`NodeTelemetrySnapshot`, `NodeHealthSnapshot`, `SimulationCampaignSummary`, `EndocrineProfileSummary`, `InstanceConnectionSnapshot`, `InstanceAlertEvent`) inside `src/NanoWeaverHrt.Client`.
  - **Excluded**: OS-specific system tray and notification adapters (P28, P30) and Avalonia UI views (P29).
- **Target Role / Boundary**: `Administrator`, `Researcher`, `Patient`; `Api` / `ApiWeb` transport boundary (using Bearer JWT from `/api/auth/token`).

## 2. Technical Contracts & Invariants

### 2.1 Interfaces & DTOs (`namespace NanoWeaverHrt.Client.Api`)

```csharp
namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Represents the live connection and health status of a managed nanoweaver-hrt instance.
/// </summary>
public enum InstanceConnectionState
{
    /// <summary>Instance has not yet been polled or is disconnected.</summary>
    Disconnected = 0,
    /// <summary>Challenge-response handshake or health probe is in progress.</summary>
    Connecting = 1,
    /// <summary>Instance is authenticated and responding normally.</summary>
    Online = 2,
    /// <summary>Instance responded but reported degraded subsystem health or thermal throttling.</summary>
    Degraded = 3,
    /// <summary>Instance failed authentication or rejected credentials (401/403).</summary>
    Unauthorized = 4,
    /// <summary>Instance is unreachable due to network or transport error.</summary>
    Offline = 5
}

/// <summary>
/// Represents a point-in-time telemetry and status snapshot for a single managed instance.
/// </summary>
/// <param name="InstanceId">Unique identifier of the managed instance.</param>
/// <param name="DisplayName">Human-readable name of the instance.</param>
/// <param name="State">Current connection state.</param>
/// <param name="CpuUtilizationPercent">Node CPU utilization percentage (0.0 to 100.0).</param>
/// <param name="GpuUtilizationPercent">Node GPU utilization percentage (0.0 to 100.0).</param>
/// <param name="GpuTemperatureCelsius">GPU core temperature in degrees Celsius.</param>
/// <param name="ActiveSimulationCount">Number of running oxDNA or LAMMPS campaigns.</param>
/// <param name="ParsedFramesPerSecond">Zero-allocation trajectory parser throughput in frames/s.</param>
/// <param name="LastUpdatedUtc">UTC timestamp when this snapshot was captured.</param>
/// <param name="LastErrorTitle">RFC 7807 problem title or safe error summary if faulted; otherwise null.</param>
public sealed record InstanceConnectionSnapshot(
    string InstanceId,
    string DisplayName,
    InstanceConnectionState State,
    double CpuUtilizationPercent,
    double GpuUtilizationPercent,
    double GpuTemperatureCelsius,
    int ActiveSimulationCount,
    double ParsedFramesPerSecond,
    DateTimeOffset LastUpdatedUtc,
    string? LastErrorTitle);

/// <summary>
/// Severity level for cross-instance notifications and tray alerts.
/// </summary>
public enum InstanceAlertSeverity
{
    /// <summary>Informational event such as simulation completion or node recovery.</summary>
    Info = 0,
    /// <summary>Warning event such as elevated GPU temperature or approaching idle expiry.</summary>
    Warning = 1,
    /// <summary>Critical event such as node offline, authentication failure, or audit ledger fault.</summary>
    Critical = 2
}

/// <summary>
/// Represents a state-change alert emitted by the multi-instance coordinator.
/// </summary>
/// <param name="AlertId">Unique identifier for the alert event.</param>
/// <param name="InstanceId">Identifier of the source instance.</param>
/// <param name="InstanceDisplayName">Display name of the source instance.</param>
/// <param name="Severity">Alert severity level.</param>
/// <param name="Title">Short notification title.</param>
/// <param name="Message">Plain-English notification description.</param>
/// <param name="OccurredAtUtc">UTC timestamp of the transition.</param>
public sealed record InstanceAlertEvent(
    string AlertId,
    string InstanceId,
    string InstanceDisplayName,
    InstanceAlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset OccurredAtUtc);

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
}

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
```

### 2.2 Security & Zero-Allocation Invariants
- Never log or expose Bearer tokens, challenge signatures, or passphrases in exceptions (`NodeApiException` must parse RFC 7807 `title`, `status`, and `traceId` only, adhering to Prime Directive 14).
- Enforce `BaseUri` security: remote non-loopback instances MUST use `https://` (rejecting plaintext remote `http://` per Prime Directive 9).

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Define `NodeAuthSession`, `NodeApiException`, `InstanceConnectionState`, `InstanceConnectionSnapshot`, `InstanceAlertSeverity`, and `InstanceAlertEvent` in `src/NanoWeaverHrt.Client/Api/`.
2. **Task 2**: Implement `INanoWeaverNodeClient` and `NanoWeaverNodeClient` using `System.Net.Http.Json` and `IClientKeyRingVault`.
3. **Task 3**: Implement `IMultiInstanceCoordinator` and `MultiInstanceCoordinator` with state-transition diffing (`Online -> Offline`, `Offline -> Online`, `ActiveSimulationCount > 0 -> 0` simulation completed, and `GpuTemperatureCelsius >= 85.0` thermal warning).
4. **Task 4**: Write deterministic unit tests in `tests/NanoWeaverHrt.Tests/Client/NanoWeaverNodeClientTests.cs` and `tests/NanoWeaverHrt.Tests/Client/MultiInstanceCoordinatorTests.cs` using an in-memory `HttpMessageHandler` stub covering 100% of lines and branches.
5. **Task 5**: Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
