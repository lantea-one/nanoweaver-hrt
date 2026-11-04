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
