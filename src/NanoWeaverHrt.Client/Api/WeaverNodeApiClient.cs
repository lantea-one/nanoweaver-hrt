using System.Text.Json;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations.Models;
using NanoWeaverHrt;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Sovereign API client managing an authenticated session with zeroizable in-memory credential storage.
/// </summary>
/// <remarks>
/// // WHAT: Authenticated REST node client wrapping an underlying <see cref="INanoWeaverNodeClient"/> with zeroizable session caching.
/// // WHY:  Fulfills Issue #84 (P81): Replaces persistent plaintext string tokens with zeroizable byte buffers erased on logout or timeout.
/// // HOW:  Caches active session bearer tokens in <see cref="ZeroizableSecretBuffer"/>, provides <see cref="ClearSessionToken"/>, and unrolls tokens on demand.
/// </remarks>
public sealed class WeaverNodeApiClient : INanoWeaverNodeClient, IDisposable
{
    /// <summary>
    /// Underlying REST node client executing network requests and cryptographic challenges.
    /// </summary>
    private readonly INanoWeaverNodeClient _innerClient;

    /// <summary>
    /// Zeroizable secret buffer holding the active session bearer token.
    /// </summary>
    private ZeroizableSecretBuffer? _sessionTokenBuffer;

    /// <summary>
    /// Synchronization object protecting access to the session token buffer.
    /// </summary>
    private readonly object _syncLock = new();

    /// <summary>
    /// Indicates whether this client instance has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverNodeApiClient"/> class wrapping the specified node client.
    /// </summary>
    /// <param name="innerClient">The underlying REST node client.</param>
    public WeaverNodeApiClient(INanoWeaverNodeClient innerClient)
    {
        // WHAT: Assigns underlying client dependency.
        // WHY:  Permits composition and test double injection.
        // HOW:  Validates non-null parameter and assigns internal field.
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverNodeApiClient"/> class using an HTTP client and key ring vault.
    /// </summary>
    /// <param name="httpClient">HTTP client used for REST communications.</param>
    /// <param name="keyRingVault">Key ring vault used for cryptographic envelope unsealing.</param>
    public WeaverNodeApiClient(HttpClient httpClient, IClientKeyRingVault keyRingVault)
        : this(new NanoWeaverNodeClient(httpClient, keyRingVault))
    {
    }

    /// <summary>
    /// Gets a cloned snapshot of the current session token buffer, or <c>null</c> if unauthenticated.
    /// </summary>
    public ZeroizableSecretBuffer? SessionTokenBuffer
    {
        get
        {
            lock (_syncLock)
            {
                if (_sessionTokenBuffer == null || _sessionTokenBuffer.IsDisposed)
                {
                    return null;
                }

                return _sessionTokenBuffer.Clone();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this client holds an active, un-disposed session token.
    /// </summary>
    public bool HasActiveSession
    {
        get
        {
            lock (_syncLock)
            {
                return _sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed && _sessionTokenBuffer.Length > 0;
            }
        }
    }

    /// <summary>
    /// Sets or updates the active session bearer token using a zeroizable secret buffer.
    /// </summary>
    /// <param name="tokenBuffer">The secret buffer containing the bearer token.</param>
    public void SetSessionToken(ZeroizableSecretBuffer tokenBuffer)
    {
        // WHAT: Assigns a zeroizable token buffer to this client, clearing any previously cached token.
        // WHY:  Configures client authentication without retaining plaintext string references on the heap.
        // HOW:  Locks state, disposes old buffer if present, and clones the incoming buffer.
        ArgumentNullException.ThrowIfNull(tokenBuffer);

        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed)
            {
                _sessionTokenBuffer.Dispose();
            }

            _sessionTokenBuffer = tokenBuffer.Clone();
        }
    }

    /// <summary>
    /// Sets or updates the active session bearer token using a string secret.
    /// </summary>
    /// <param name="token">Bearer token string to wrap and store.</param>
    public void SetSessionToken(string token)
    {
        // WHAT: Ingests a string bearer token into an internal ZeroizableSecretBuffer.
        // WHY:  Facilitates interoperability with string-based auth responses while enabling zeroization.
        // HOW:  Wraps token with ZeroizableSecretBuffer.FromUtf8 and stores it.
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var buffer = ZeroizableSecretBuffer.FromUtf8(token);
        SetSessionToken(buffer);
    }

    /// <summary>
    /// Cryptographically zeroizes and clears the active session token from memory.
    /// </summary>
    public void ClearSessionToken()
    {
        // WHAT: Wipes cached session token bytes and resets the session buffer.
        // WHY:  Fulfills Issue #84 (P81) zero-knowledge credentials hygiene on disconnect or idle timeout.
        // HOW:  Disposes _sessionTokenBuffer and sets field to null under lock.
        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null)
            {
                _sessionTokenBuffer.Dispose();
                _sessionTokenBuffer = null;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<NodeAuthSession> AuthenticateAsync(
        InstanceProfile profile,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Performs PKI challenge-response handshake and stores the resulting session token in zeroizable memory.
        // WHY:  Ensures single-handshake authentication and zeroizable token lifetime tracking.
        // HOW:  Delegates to _innerClient.AuthenticateAsync, converts token to ZeroizableSecretBuffer, and returns session.
        ThrowIfDisposed();
        NodeAuthSession session = await _innerClient.AuthenticateAsync(profile, passphrase, ct).ConfigureAwait(false);

        using var buffer = ZeroizableSecretBuffer.FromUtf8(session.Token);
        SetSessionToken(buffer);

        return session;
    }

    /// <inheritdoc/>
    public Task<InstanceConnectionSnapshot> PollSnapshotAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Polls target node telemetry snapshot using provided or cached session token.
        // WHY:  Permits polling without callers having to hold or pass raw bearer tokens.
        // HOW:  Resolves effective token, delegates to _innerClient.PollSnapshotAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.PollSnapshotAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<bool> StartSimulationCampaignAsync(
        InstanceProfile profile,
        string bearerToken,
        string simulationType,
        CancellationToken ct = default)
    {
        // WHAT: Triggers simulation campaign on node using provided or cached session token.
        // WHY:  Simplifies campaign dispatch from GUI ViewModels.
        // HOW:  Resolves effective token, delegates to _innerClient.StartSimulationCampaignAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.StartSimulationCampaignAsync(profile, effectiveToken, simulationType, ct);
    }

    /// <inheritdoc/>
    public Task<NodeTelemetrySnapshot> GetTelemetryAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries raw telemetry metrics from node using provided or cached session token.
        // WHY:  Provides hardware and GC telemetry metrics to client dashboard.
        // HOW:  Resolves effective token, delegates to _innerClient.GetTelemetryAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetTelemetryAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SimulationCampaignSummary>> GetCampaignsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches catalog of supported simulation campaigns.
        // WHY:  Enumerates campaign configurations available on the connected node.
        // HOW:  Resolves effective token, delegates to _innerClient.GetCampaignsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetCampaignsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<EndocrineProfileSummary> GetEndocrineProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches active endocrine profile from connected node.
        // WHY:  Enables display of hormone levels and target feedback loops.
        // HOW:  Resolves effective token, delegates to _innerClient.GetEndocrineProfileAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetEndocrineProfileAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<ClosedLoopSimulationResult> SimulateClosedLoopEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        ClosedLoopSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches closed-loop numerical integration parameters using provided or cached session token.
        // WHY:  Models real-time endocrine feedback trajectories from client GUI.
        // HOW:  Resolves effective token, delegates to _innerClient.SimulateClosedLoopEndocrineAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.SimulateClosedLoopEndocrineAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FederationPeerNode>> DiscoverFederationPeersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Discovers active P2P swarm peers using provided or cached session token.
        // WHY:  Permits swarm monitoring without explicit token management in GUI layers.
        // HOW:  Resolves effective token, delegates to _innerClient.DiscoverFederationPeersAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DiscoverFederationPeersAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<TrajectoryFederationManifest> BuildFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        FederationBuildManifestRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Builds a federation manifest for a trajectory file using provided or cached token.
        // WHY:  Allows client to initiate swarm distribution jobs.
        // HOW:  Resolves effective token, delegates to _innerClient.BuildFederationManifestAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.BuildFederationManifestAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<FederationVerifyManifestResponse> VerifyFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryFederationManifest manifest,
        CancellationToken ct = default)
    {
        // WHAT: Verifies federation manifest signatures and Merkle chunk integrity.
        // WHY:  Ensures untampered trajectory data before peer transfer.
        // HOW:  Resolves effective token, delegates to _innerClient.VerifyFederationManifestAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.VerifyFederationManifestAsync(profile, effectiveToken, manifest, ct);
    }

    /// <inheritdoc/>
    public Task<GelAnalysisResult> AnalyzeGelAsync(
        InstanceProfile profile,
        string bearerToken,
        GelAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Analyzes gel electrophoresis images using provided or cached session token.
        // WHY:  Enables automated folding quality verification from client UI.
        // HOW:  Resolves effective token, delegates to _innerClient.AnalyzeGelAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AnalyzeGelAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Executes on-demand hardware benchmark using provided or cached session token.
        // WHY:  Evaluates GPU/NPU compute performance before running heavy workloads.
        // HOW:  Resolves effective token, delegates to _innerClient.RunBenchmarkAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RunBenchmarkAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<AcceleratorBenchmarkReport?> GetLatestBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches most recent hardware benchmark report using provided or cached token.
        // WHY:  Supplies hardware readiness status to dashboard cards.
        // HOW:  Resolves effective token, delegates to _innerClient.GetLatestBenchmarkAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetLatestBenchmarkAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<OpentronsProtocolResult> GenerateOpentronsProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Synthesizes Opentrons liquid handler protocol scripts using provided or cached token.
        // WHY:  Enables automated pipetting script export from client workspace.
        // HOW:  Resolves effective token, delegates to _innerClient.GenerateOpentronsProtocolAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GenerateOpentronsProtocolAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        InstanceProfile profile,
        string bearerToken,
        string? patientId = null,
        CancellationToken ct = default)
    {
        // WHAT: Downloads patient Digital DNA package using provided or cached session token.
        // WHY:  Provides patient nanocarrier sequences and annealing schedules.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientDnaRegimensAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientDnaRegimensAsync(profile, effectiveToken, patientId, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientDigitalDnaRecord>> GetPatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves digitized DNA blueprint records for a patient using provided or cached token.
        // WHY:  Allows reviewing clinical nanocarrier records in the client application.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientDigitalDnaAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientDigitalDnaAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Discovers trajectory files using provided or cached session token.
        // WHY:  Allows reviewing active molecular dynamics files in simulation storage.
        // HOW:  Resolves effective token, delegates to _innerClient.ListTrajectoriesAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListTrajectoriesAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<DatasetManifest> ExportDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Initiates dataset export bundle with manifest using provided or cached token.
        // WHY:  Allows exporting trajectories with cryptographic SHA-256 provenance.
        // HOW:  Resolves effective token, delegates to _innerClient.ExportDatasetAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ExportDatasetAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries all stack configuration settings using provided or cached token.
        // WHY:  Allows inspecting configuration state and active database overrides.
        // HOW:  Resolves effective token, delegates to _innerClient.GetAllConfigurationsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetAllConfigurationsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task SetConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        ConfigOverrideRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Persists configuration override using provided or cached token.
        // WHY:  Enables updating stack runtime configuration with database precedence.
        // HOW:  Resolves effective token, delegates to _innerClient.SetConfigurationOverrideAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.SetConfigurationOverrideAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task DeleteConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        string key,
        CancellationToken ct = default)
    {
        // WHAT: Reverts configuration override using provided or cached token.
        // WHY:  Restores default environment variable configuration setting.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteConfigurationOverrideAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteConfigurationOverrideAsync(profile, effectiveToken, key, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string? researcher = null,
        string? subject = null,
        CancellationToken ct = default)
    {
        // WHAT: Queries active subject access grants using provided or cached token.
        // WHY:  Enables auditing clinical trial and genomic access permissions.
        // HOW:  Resolves effective token, delegates to _innerClient.ListSubjectGrantsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListSubjectGrantsAsync(profile, effectiveToken, researcher, subject, ct);
    }

    /// <inheritdoc/>
    public Task<ComplianceStatusReport> GetComplianceStatusAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries compliance status report using provided or cached session token.
        // WHY:  Allows checking regulatory scorecards without managing raw Bearer strings.
        // HOW:  Resolves effective token, delegates to _innerClient.GetComplianceStatusAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetComplianceStatusAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AuditRecord>> GetAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        int? limit = null,
        CancellationToken ct = default)
    {
        // WHAT: Queries immutable audit log records using provided or cached session token.
        // WHY:  Permits verification of FDA 21 CFR Part 11 hash-chained audit trails.
        // HOW:  Resolves effective token, delegates to _innerClient.GetAuditLedgerAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetAuditLedgerAsync(profile, effectiveToken, limit, ct);
    }

    /// <inheritdoc/>
    public Task<PatientGenomicProfile> AnalyzeGenomicsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches genomic analysis request using provided or cached session token.
        // WHY:  Calculates kinetic modifiers and alerts from patient SNPs.
        // HOW:  Resolves effective token, delegates to _innerClient.AnalyzeGenomicsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AnalyzeGenomicsAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<AuditLedgerVerificationResult> VerifyAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Cryptographically verifies unbroken hash chaining and post-quantum signatures using provided or cached token.
        // WHY:  Permits administrative audits without manual token management in GUI layers.
        // HOW:  Resolves effective token, delegates to _innerClient.VerifyAuditLedgerAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.VerifyAuditLedgerAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<PatientGenomicProfile> AnalyzeGenomicSequenceAsync(
        InstanceProfile profile,
        string bearerToken,
        SequenceAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches raw FASTA sequence analysis using provided or cached session token.
        // WHY:  Calculates CpG immunogenicity and CAG repeat length without exposing token state.
        // HOW:  Resolves effective token, delegates to _innerClient.AnalyzeGenomicSequenceAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AnalyzeGenomicSequenceAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<TrajectoryAnalyticsApiResponse> QueryTrajectoryAnalyticsAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryAnalyticsApiRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Executes read-only SQL queries against trajectory datasets using provided or cached session token.
        // WHY:  Permits analytical queries against DuckDB engine from GUI clients.
        // HOW:  Resolves effective token, delegates to _innerClient.QueryTrajectoryAnalyticsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.QueryTrajectoryAnalyticsAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<PeerKeyListResponse> ListPeerKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries local node public signing identity and trusted peer keys using provided or cached token.
        // WHY:  Supports peer laboratory key management in desktop and mobile interfaces.
        // HOW:  Resolves effective token, delegates to _innerClient.ListPeerKeysAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListPeerKeysAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<PeerKeyRecord> RegisterPeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        RegisterPeerKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Registers a trusted peer laboratory public signing key using provided or cached session token.
        // WHY:  Allows administrators to onboard external peer labs into the verification ring.
        // HOW:  Resolves effective token, delegates to _innerClient.RegisterPeerKeyAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RegisterPeerKeyAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task RevokePeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        string keyId,
        CancellationToken ct = default)
    {
        // WHAT: Revokes an active peer laboratory public signing key using provided or cached session token.
        // WHY:  Allows immediate revocation of compromised or retired peer signing keys.
        // HOW:  Resolves effective token, delegates to _innerClient.RevokePeerKeyAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RevokePeerKeyAsync(profile, effectiveToken, keyId, ct);
    }

    /// <inheritdoc/>
    public Task<SubjectGrant> CreateSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        GrantSubjectAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Creates a researcher direct subject access grant using provided or cached session token.
        // WHY:  Allows administrators to assign patient access without exposing raw tokens.
        // HOW:  Resolves effective token, delegates to _innerClient.CreateSubjectGrantAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreateSubjectGrantAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task RevokeSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Revokes a direct subject access grant using provided or cached session token.
        // WHY:  Terminates access for completed studies or transferred care.
        // HOW:  Resolves effective token, delegates to _innerClient.RevokeSubjectGrantAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RevokeSubjectGrantAsync(profile, effectiveToken, researcherUsername, subjectUsername, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SubjectGroup>> ListSubjectGroupsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries accessible patient cohort groups using provided or cached session token.
        // WHY:  Permits cohort study listing with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListSubjectGroupsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListSubjectGroupsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<SubjectGroup> CreateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Creates a new patient cohort group using provided or cached session token.
        // WHY:  Permits cohort study organization with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreateSubjectGroupAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreateSubjectGroupAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task DeleteSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Permanently deletes a cohort group using provided or cached session token.
        // WHY:  Allows cohort retirement with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteSubjectGroupAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteSubjectGroupAsync(profile, effectiveToken, groupId, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries all created single-use share keys using provided or cached session token.
        // WHY:  Allows inspecting federation tokens with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListShareKeysAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListShareKeysAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListGroupMembersAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId}/members using provided or cached session token.
        // WHY:  Allows querying cohort membership without manual Bearer token management.
        // HOW:  Resolves effective token, delegates to _innerClient.ListGroupMembersAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListGroupMembersAsync(profile, effectiveToken, groupId, ct);
    }

    /// <inheritdoc/>
    public Task AddGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        AddGroupMemberRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-groups/{groupId}/members using provided or cached session token.
        // WHY:  Allows enrolling a subject into a cohort group with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.AddGroupMemberAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AddGroupMemberAsync(profile, effectiveToken, groupId, request, ct);
    }

    /// <inheritdoc/>
    public Task RemoveGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-groups/{groupId}/members/{subjectUsername} using provided or cached session token.
        // WHY:  Allows un-enrolling a subject with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RemoveGroupMemberAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RemoveGroupMemberAsync(profile, effectiveToken, groupId, subjectUsername, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId}/grants using provided or cached session token.
        // WHY:  Allows auditing group grants with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListGroupGrantsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListGroupGrantsAsync(profile, effectiveToken, groupId, ct);
    }

    /// <inheritdoc/>
    public Task<SubjectGroupGrant> CreateGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        GrantGroupAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-groups/{groupId}/grants using provided or cached session token.
        // WHY:  Allows administrators to assign group access with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreateGroupGrantAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreateGroupGrantAsync(profile, effectiveToken, groupId, request, ct);
    }

    /// <inheritdoc/>
    public Task RevokeGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string researcherUsername,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-groups/{groupId}/grants/{researcherUsername} using provided or cached session token.
        // WHY:  Allows administrators to revoke group access with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RevokeGroupGrantAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RevokeGroupGrantAsync(profile, effectiveToken, groupId, researcherUsername, ct);
    }

    /// <inheritdoc/>
    public Task<ShareKeyRecord> CreateShareKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/share-keys/create using provided or cached session token.
        // WHY:  Allows generating peering invitation tokens with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreateShareKeyAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreateShareKeyAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public async Task<ShareKeyConsumeResult> RedeemShareKeyAsync(
        InstanceProfile profile,
        RedeemShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/share-keys/redeem to consume a peering invitation key.
        // WHY:  Allows remote instances to authenticate, redeem single-use invitation tokens, and cache session.
        // HOW:  Validates non-disposed state, delegates to _innerClient.RedeemShareKeyAsync, and caches token on success.
        ThrowIfDisposed();
        ShareKeyConsumeResult result = await _innerClient.RedeemShareKeyAsync(profile, request, ct).ConfigureAwait(false);
        if (result.Success && !string.IsNullOrWhiteSpace(result.SessionToken))
        {
            SetSessionToken(result.SessionToken);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<SubjectGroup> GetSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId} using provided or cached session token.
        // WHY:  Allows inspecting cohort group details with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetSubjectGroupAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetSubjectGroupAsync(profile, effectiveToken, groupId, ct);
    }

    /// <inheritdoc/>
    public Task<SubjectGroup> UpdateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        UpdateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches PUT /api/subject-groups/{groupId} using provided or cached session token.
        // WHY:  Allows updating cohort group details with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.UpdateSubjectGroupAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.UpdateSubjectGroupAsync(profile, effectiveToken, groupId, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AccessibleSubjectItem>> ListMySubjectsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-access/my-subjects using provided or cached session token.
        // WHY:  Allows retrieving caller accessible subjects with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListMySubjectsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListMySubjectsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DnaOrigamiSample>> GetFabricationPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/fabrication/presets using provided or cached session token.
        // WHY:  Allows retrieving origami sample presets with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetFabricationPresetsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetFabricationPresetsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<FabricationPackage> GenerateFabricationProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        FabricationGenerateRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/fabrication/generate using provided or cached session token.
        // WHY:  Allows generating staple routing protocols with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GenerateFabricationProtocolAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GenerateFabricationProtocolAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientGenomicProfile>> GetGenomicPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/genomics/presets using provided or cached session token.
        // WHY:  Allows loading clinical genomic presets with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetGenomicPresetsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetGenomicPresetsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<GeneticMarker>> GetGenomicMarkersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/genomics/markers using provided or cached session token.
        // WHY:  Allows loading clinical marker reference panels with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetGenomicMarkersAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetGenomicMarkersAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/services using provided or cached session token.
        // WHY:  Allows monitoring container stack telemetry with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetStackServicesAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetStackServicesAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<StackServiceInfo> PingStackServiceAsync(
        InstanceProfile profile,
        string bearerToken,
        string serviceId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/services/ping/{serviceId} using provided or cached session token.
        // WHY:  Allows latency probing individual services with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.PingStackServiceAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.PingStackServiceAsync(profile, effectiveToken, serviceId, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HormoneCurvePoint>> GetEndocrineKineticsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/endocrine/kinetics using provided or cached session token.
        // WHY:  Allows retrieving pharmacodynamic curves with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetEndocrineKineticsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetEndocrineKineticsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<EndocrineTargetProfile> ToggleEndocrineSwitchAsync(
        InstanceProfile profile,
        string bearerToken,
        ToggleSwitchRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/endocrine/toggle using provided or cached session token.
        // WHY:  Allows toggling endocrine profiles with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ToggleEndocrineSwitchAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ToggleEndocrineSwitchAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserAccount>> ListUsersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users using provided or cached session token.
        // WHY:  Allows enumerating user accounts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListUsersAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListUsersAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<UserAccount> CreateUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users using provided or cached session token.
        // WHY:  Allows creating user accounts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreateUserAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreateUserAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task DeleteUserAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username} using provided or cached session token.
        // WHY:  Allows deleting user accounts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteUserAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteUserAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<UserProfile> GetUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/profile using provided or cached session token.
        // WHY:  Allows fetching demographic and clinical profiles with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetUserProfileAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetUserProfileAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<UserProfile> UpdateUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        UserProfile userProfile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches PUT /api/users/{username}/profile using provided or cached session token.
        // WHY:  Allows updating user profile metadata with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.UpdateUserProfileAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.UpdateUserProfileAsync(profile, effectiveToken, username, userProfile, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientAddress>> ListUserAddressesAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/addresses using provided or cached session token.
        // WHY:  Allows retrieving registered addresses with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListUserAddressesAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListUserAddressesAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<PatientAddress> AddUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientAddress address,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users/{username}/addresses using provided or cached session token.
        // WHY:  Allows adding or updating addresses with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.AddUserAddressAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AddUserAddressAsync(profile, effectiveToken, username, address, ct);
    }

    /// <inheritdoc/>
    public Task DeleteUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string addressId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username}/addresses/{id} using provided or cached session token.
        // WHY:  Allows removing addresses with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteUserAddressAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteUserAddressAsync(profile, effectiveToken, username, addressId, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientContact>> ListUserContactsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/contacts using provided or cached session token.
        // WHY:  Allows retrieving emergency outreach contacts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListUserContactsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListUserContactsAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<PatientContact> AddUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientContact contact,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users/{username}/contacts using provided or cached session token.
        // WHY:  Allows recording caregiver relationships with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.AddUserContactAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AddUserContactAsync(profile, effectiveToken, username, contact, ct);
    }

    /// <inheritdoc/>
    public Task DeleteUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string contactId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username}/contacts/{id} using provided or cached session token.
        // WHY:  Allows removing emergency contacts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteUserContactAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteUserContactAsync(profile, effectiveToken, username, contactId, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDigitalDnaRecord> GetPatientDigitalDnaRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/dna/{id} using provided or cached session token.
        // WHY:  Allows retrieving digitized DNA blueprints with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientDigitalDnaRecordAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientDigitalDnaRecordAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task DeletePatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/dna/{id} using provided or cached session token.
        // WHY:  Allows deleting digitized DNA blueprints with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeletePatientDigitalDnaAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeletePatientDigitalDnaAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientDnaImportRecord>> ListPatientImportsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/imports using provided or cached session token.
        // WHY:  Allows retrieving imported sequence files with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListPatientImportsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListPatientImportsAsync(profile, effectiveToken, username, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDnaImportRecord> GetPatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/imports/{id} using provided or cached session token.
        // WHY:  Allows retrieving a specific sequence import record with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientImportAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientImportAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDnaImportRecord> CreatePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientImportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/patients/{username}/imports using provided or cached session token.
        // WHY:  Allows uploading sequence data or variant panels with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreatePatientImportAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreatePatientImportAsync(profile, effectiveToken, username, request, ct);
    }

    /// <inheritdoc/>
    public Task DeletePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/imports/{id} using provided or cached session token.
        // WHY:  Allows removing sequence import records with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeletePatientImportAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeletePatientImportAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatientResultRecord>> ListPatientClinicalResultsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string? resultType = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/results using provided or cached session token.
        // WHY:  Allows retrieving simulation and clinical outcome records with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListPatientClinicalResultsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListPatientClinicalResultsAsync(profile, effectiveToken, username, resultType, ct);
    }

    /// <inheritdoc/>
    public Task<PatientResultRecord> GetPatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/results/{id} using provided or cached session token.
        // WHY:  Allows retrieving a specific simulation or clinical result record with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientClinicalResultAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientClinicalResultAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task<PatientResultRecord> CreatePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientResultRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/patients/{username}/results using provided or cached session token.
        // WHY:  Allows registering new clinical or simulation results with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CreatePatientClinicalResultAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreatePatientClinicalResultAsync(profile, effectiveToken, username, request, ct);
    }

    /// <inheritdoc/>
    public Task DeletePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/results/{id} using provided or cached session token.
        // WHY:  Allows removing clinical or simulation results with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeletePatientClinicalResultAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeletePatientClinicalResultAsync(profile, effectiveToken, username, id, ct);
    }

    /// <inheritdoc/>
    public Task<UserAccount> GetCurrentUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/auth/me using provided or cached session token.
        // WHY:  Allows retrieving current user account identity with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetCurrentUserAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetCurrentUserAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/auth/logout using provided or cached session token and clears cached credentials.
        // WHY:  Allows invalidating interactive session with managed token lifecycle and zeroizing cached session.
        // HOW:  Resolves effective token, executes logout, and purges session token in a finally block.
        ThrowIfDisposed();
        try
        {
            string effectiveToken = ResolveBearerToken(bearerToken);
            await _innerClient.LogoutAsync(profile, effectiveToken, ct).ConfigureAwait(false);
        }
        finally
        {
            ClearSessionToken();
        }
    }

    /// <inheritdoc/>
    public Task<SetupStatusResponse> GetSetupStatusAsync(
        InstanceProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/setup/status to inspect first-launch setup requirements.
        // WHY:  Allows checking node initialization state without requiring authentication.
        // HOW:  Asserts non-disposed state, delegates to _innerClient.GetSetupStatusAsync.
        ThrowIfDisposed();
        return _innerClient.GetSetupStatusAsync(profile, ct);
    }

    /// <inheritdoc/>
    public Task CryptoShredUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CryptoShredRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/crypto-shred using provided or cached session token.
        // WHY:  Allows destroying subject decryption keys with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.CryptoShredUserAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CryptoShredUserAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<DeIdentificationResult> DeIdentifyDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        DeIdentifyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/de-identify using provided or cached session token.
        // WHY:  Allows de-identifying subject data with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeIdentifyDatasetAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeIdentifyDatasetAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<EncryptedEnvelope> EncryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        EncryptRecordRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/encrypt-record using provided or cached session token.
        // WHY:  Allows post-quantum envelope encryption with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.EncryptRecordAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.EncryptRecordAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<JsonElement> DecryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        DecryptRecordRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/decrypt-record using provided or cached session token.
        // WHY:  Allows post-quantum envelope decryption with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DecryptRecordAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DecryptRecordAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<DatasetImportResponse> ImportDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        byte[] archiveBytes,
        string archiveFileName,
        byte[] manifestBytes,
        string manifestFileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/datasets/import using provided or cached session token.
        // WHY:  Allows dataset bundle import with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ImportDatasetArchiveAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ImportDatasetArchiveAsync(profile, effectiveToken, archiveBytes, archiveFileName, manifestBytes, manifestFileName, ct);
    }

    /// <inheritdoc/>
    public Task<ExportEncryptedDatasetResult> ExportEncryptedDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportEncryptedDatasetRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/datasets/export-encrypted using provided or cached session token.
        // WHY:  Allows encrypted dataset export with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ExportEncryptedDatasetAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ExportEncryptedDatasetAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExportFileItem>> ListExportedDatasetBundlesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/datasets/exports using provided or cached session token.
        // WHY:  Allows listing exported archives with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ListExportedDatasetBundlesAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ListExportedDatasetBundlesAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task DeleteExportedDatasetBundleAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/datasets/exports/{fileName} using provided or cached session token.
        // WHY:  Allows deleting exported bundles with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DeleteExportedDatasetBundleAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DeleteExportedDatasetBundleAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public Task<byte[]> DownloadDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/datasets/download/{fileName} using provided or cached session token.
        // WHY:  Allows downloading archives with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DownloadDatasetArchiveAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DownloadDatasetArchiveAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public Task<byte[]> DownloadTrajectoryAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/trajectories/download/{fileName} using provided or cached session token.
        // WHY:  Allows downloading simulation trajectories with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DownloadTrajectoryAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DownloadTrajectoryAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public async Task<AuthTokenResponse> InitializeSetupAdminAsync(
        InstanceProfile profile,
        InitialSetupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches unauthenticated POST /api/setup/initialize to provision master Administrator.
        // WHY:  Initial bootstrap operation executed before bearer tokens exist; caches session for immediate access.
        // HOW:  Validates non-disposed state, delegates to _innerClient.InitializeSetupAdminAsync, and stores result token.
        ThrowIfDisposed();
        AuthTokenResponse result = await _innerClient.InitializeSetupAdminAsync(profile, request, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.Token))
        {
            SetSessionToken(result.Token);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<SystemHealthOverview> GetSystemHealthAsync(
        InstanceProfile profile,
        string? bearerToken = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/system/health using provided, cached, or null token.
        // WHY:  Allows querying public health overview or detailed telemetry with managed token lifecycle.
        // HOW:  Resolves token if available without throwing when absent, delegates to _innerClient.GetSystemHealthAsync.
        ThrowIfDisposed();
        string? effectiveToken = null;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            effectiveToken = bearerToken;
        }
        else
        {
            lock (_syncLock)
            {
                if (_sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed)
                {
                    effectiveToken = _sessionTokenBuffer.ToUnprotectedString();
                }
            }
        }
        return _innerClient.GetSystemHealthAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<EventBusTelemetry> GetEventBusMetricsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/system/event-bus using provided or cached session token.
        // WHY:  Allows monitoring event bus telemetry with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetEventBusMetricsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetEventBusMetricsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<TrajectoryParserProcessResult> ProcessTrajectoryCoordinatesAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/parser/process/{fileName} using provided or cached session token.
        // WHY:  Allows running parser benchmarks with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ProcessTrajectoryCoordinatesAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ProcessTrajectoryCoordinatesAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public Task<TrajectoryParquetExportResult> ExportCoordinatesParquetAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/parser/export-parquet/{fileName} using provided or cached session token.
        // WHY:  Allows serializing coordinates to Parquet with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ExportCoordinatesParquetAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ExportCoordinatesParquetAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public Task<AptamerTrajectoryMetrics> AnalyzeTrajectoryEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/endocrine/analyze/{fileName} using provided or cached session token.
        // WHY:  Allows analyzing aptamer binding contacts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.AnalyzeTrajectoryEndocrineAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.AnalyzeTrajectoryEndocrineAsync(profile, effectiveToken, fileName, ct);
    }

    /// <inheritdoc/>
    public Task<SimulationInputDeckResult> GetSimulationCampaignConfigAsync(
        InstanceProfile profile,
        string bearerToken,
        string campaignId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/simulations/configs/{campaignId} using provided or cached session token.
        // WHY:  Allows generating simulation input decks with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetSimulationCampaignConfigAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetSimulationCampaignConfigAsync(profile, effectiveToken, campaignId, ct);
    }

    /// <inheritdoc/>
    public Task<GenomicAdjustmentResult> ApplyGenomicAdjustmentsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/genomics/apply-adjustments using provided or cached session token.
        // WHY:  Allows calculating precision genomic dosage setpoints with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.ApplyGenomicAdjustmentsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.ApplyGenomicAdjustmentsAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<byte[]> DownloadOpentronsScriptAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/fabrication/opentrons/download using provided or cached session token.
        // WHY:  Allows downloading Opentrons liquid handler scripts with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.DownloadOpentronsScriptAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.DownloadOpentronsScriptAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<byte[]> GetFederationChunkAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        int? chunkIndex = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/federation/chunks/{fileName}?index={chunkIndex} using provided or cached session token.
        // WHY:  Allows downloading federation swarm chunks with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.GetFederationChunkAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetFederationChunkAsync(profile, effectiveToken, fileName, chunkIndex, ct);
    }

    /// <inheritdoc/>
    public Task<SerumStabilityAnalysisResult> RunSerumStabilityAsync(
        InstanceProfile profile,
        string bearerToken,
        SerumStabilityRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/serum-stability using provided or cached session token.
        // WHY:  Allows running serum stability simulation campaign with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RunSerumStabilityAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RunSerumStabilityAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<AptamerGatingAnalysisResult> RunAptamerGatingAsync(
        InstanceProfile profile,
        string bearerToken,
        AptamerGatingRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/aptamer-gating using provided or cached session token.
        // WHY:  Allows running aptamer gating conformational simulation with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RunAptamerGatingAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RunAptamerGatingAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<PayloadLeakageAnalysisResult> RunPayloadLeakageAsync(
        InstanceProfile profile,
        string bearerToken,
        PayloadLeakageRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/payload-leakage using provided or cached session token.
        // WHY:  Allows running cargo retention and leakage simulation with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RunPayloadLeakageAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RunPayloadLeakageAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public Task<PbpkEndocrineSimulationResult> RunPbpkSimulationAsync(
        InstanceProfile profile,
        string bearerToken,
        PbpkSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/pbpk-endocrine using provided or cached session token.
        // WHY:  Allows running physiological pharmacokinetic simulation with managed token lifecycle.
        // HOW:  Resolves effective token, delegates to _innerClient.RunPbpkSimulationAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.RunPbpkSimulationAsync(profile, effectiveToken, request, ct);
    }

    /// <inheritdoc/>
    public async Task<AuthTokenResponse> ExchangeWebTokenAsync(
        InstanceProfile profile,
        TokenExchangeRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/auth/web-token to authenticate and establish a browser session cookie.
        // WHY:  Allows web challenge verification with session token emission and zeroizable caching.
        // HOW:  Validates lifecycle, delegates to _innerClient.ExchangeWebTokenAsync, and stores result token.
        ThrowIfDisposed();
        AuthTokenResponse result = await _innerClient.ExchangeWebTokenAsync(profile, request, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.Token))
        {
            SetSessionToken(result.Token);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<SystemHealthOverview> GetHealthzAsync(
        InstanceProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /healthz to evaluate cluster liveness probe.
        // WHY:  Provides low-overhead container liveness health reporting.
        // HOW:  Validates lifecycle, delegates to _innerClient.GetHealthzAsync.
        ThrowIfDisposed();
        return _innerClient.GetHealthzAsync(profile, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDigitalDnaRecord> CreatePatientDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientDnaRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/patients/{username}/dna using provided or cached token.
        // WHY:  Synthesizes and registers a digitized DNA blueprint for a patient.
        // HOW:  Resolves effective token, delegates to _innerClient.CreatePatientDnaAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.CreatePatientDnaAsync(profile, effectiveToken, username, request, ct);
    }

    /// <inheritdoc/>
    public Task<SystemHealthOverview> GetHealthAsync(
        InstanceProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /health to evaluate system operational health.
        // WHY:  Provides low-overhead container health reporting via the /health endpoint.
        // HOW:  Validates lifecycle, delegates to _innerClient.GetHealthAsync.
        ThrowIfDisposed();
        return _innerClient.GetHealthAsync(profile, ct);
    }

    /// <inheritdoc/>
    public Task<GranularSystemHealth> GetGranularSystemHealthAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/system/health to inspect detailed subsystem diagnostics using provided or cached token.
        // WHY:  Enables diagnostic health inspection across internal services.
        // HOW:  Resolves effective token, delegates to _innerClient.GetGranularSystemHealthAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetGranularSystemHealthAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        InstanceProfile profile,
        string bearerToken,
        string? patientId,
        TherapeuticRegimen? regimen,
        string? geometry,
        double? targetKd,
        CancellationToken ct = default)
    {
        // WHAT: Downloads patient Digital DNA package with filter parameters using provided or cached session token.
        // WHY:  Provides patient nanocarrier sequences and annealing schedules tailored by regimen or geometry.
        // HOW:  Resolves effective token, delegates to _innerClient.GetPatientDnaRegimensAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetPatientDnaRegimensAsync(profile, effectiveToken, patientId, regimen, geometry, targetKd, ct);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Zeroizes cached token and disposes wrapped client if disposable.
        // WHY:  Guarantees credential wiping upon service shutdown.
        // HOW:  Calls ClearSessionToken, disposes inner client if IDisposable, and marks disposed.
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_sessionTokenBuffer != null)
            {
                _sessionTokenBuffer.Dispose();
                _sessionTokenBuffer = null;
            }

            if (_innerClient is IDisposable disposableInner)
            {
                disposableInner.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves the effective bearer token to use for a request.
    /// </summary>
    /// <param name="explicitToken">Explicit token passed to method.</param>
    /// <returns>Non-empty bearer token string.</returns>
    private string ResolveBearerToken(string explicitToken)
    {
        // WHAT: Resolves token from explicit argument or cached session buffer.
        // WHY:  Allows caller to pass explicit token or rely on internal session cache.
        // HOW:  Inspects explicitToken; if non-empty, returns it; otherwise decodes _sessionTokenBuffer.
        if (!string.IsNullOrWhiteSpace(explicitToken))
        {
            return explicitToken;
        }

        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed)
            {
                string token = _sessionTokenBuffer.ToUnprotectedString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
        }

        throw new InvalidOperationException("No bearer token provided and no active session token is cached.");
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if this client instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        // WHAT: Asserts non-disposed state before processing operations.
        // WHY:  Guarantees fail-fast semantics when client is accessed after disposal.
        // HOW:  Checks boolean flag under lock and throws ObjectDisposedException if true.
        lock (_syncLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WeaverNodeApiClient));
            }
        }
    }
}
