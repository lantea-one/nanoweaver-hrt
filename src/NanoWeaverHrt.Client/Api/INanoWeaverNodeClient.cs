using NanoWeaverHrt.Analytics;
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

    /// <summary>
    /// Simulates dynamic closed-loop endocrine feedback and LH/FSH gonadotropin suppression via POST /api/endocrine/closed-loop/simulate.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Closed-loop numerical integration parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Closed-loop numerical integration simulation result.</returns>
    Task<ClosedLoopSimulationResult> SimulateClosedLoopEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        ClosedLoopSimulationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Discovers active peer nodes in the trajectory federation swarm via GET /api/federation/peers.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only collection of discovered swarm peer nodes.</returns>
    Task<IReadOnlyList<FederationPeerNode>> DiscoverFederationPeersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Builds and cryptographically signs a trajectory federation chunk manifest via POST /api/federation/manifests/build.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Parameters specifying the trajectory file name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated trajectory federation manifest.</returns>
    Task<TrajectoryFederationManifest> BuildFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        FederationBuildManifestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies the digital signature and chunk integrity of a federation manifest via POST /api/federation/manifests/verify.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="manifest">Trajectory federation manifest to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result detailing validity status and chunk statistics.</returns>
    Task<FederationVerifyManifestResponse> VerifyFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryFederationManifest manifest,
        CancellationToken ct = default);

    /// <summary>
    /// Analyzes an agarose gel electrophoresis image for DNA origami nanocarrier yield via POST /api/fabrication/gel/analyze.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Gel image analysis request containing pixel data and lane counts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Analytical gel electrophoresis segmentation and folding yield result.</returns>
    Task<GelAnalysisResult> AnalyzeGelAsync(
        InstanceProfile profile,
        string bearerToken,
        GelAnalysisRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Executes an on-demand hardware qualification benchmark via POST /api/system/benchmark/run.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Hardware accelerator compute and memory bandwidth report.</returns>
    Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the most recent cached hardware accelerator benchmark report via GET /api/system/benchmark/latest.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Most recent benchmark report, or <see langword="null"/> if none exists.</returns>
    Task<AcceleratorBenchmarkReport?> GetLatestBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Generates an automated Opentrons OT-2 or Flex Python protocol script via POST /api/fabrication/opentrons/generate.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Specification containing sample ID, regimen, and protocol options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Synthesized Opentrons protocol script and deck summary.</returns>
    Task<OpentronsProtocolResult> GenerateOpentronsProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Generates or fetches a patient-specific Digital DNA package via GET /api/patient/dna.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="patientId">Optional patient identifier to retrieve; if omitted, defaults to the authenticated subject.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete patient Digital DNA package including staple oligos and annealing ramps.</returns>
    Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        InstanceProfile profile,
        string bearerToken,
        string? patientId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves persistent digitized DNA records for a specific patient via GET /api/patients/{username}/dna.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of digitized DNA blueprint records for the patient.</returns>
    Task<IReadOnlyList<PatientDigitalDnaRecord>> GetPatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the list of available trajectory files in the active simulation volume via GET /api/datasets.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of trajectory file descriptors.</returns>
    Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a dataset export bundle with an immutable cryptographic manifest via POST /api/datasets/export.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Export specification specifying dataset name and files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created dataset manifest.</returns>
    Task<DatasetManifest> ExportDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all stack configuration entries via GET /api/configuration (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of effective configuration items.</returns>
    Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Persists an authoritative configuration override to the database via POST /api/configuration/override (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Key-value override parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        ConfigOverrideRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Reverts an authoritative configuration override to default/environment fallback via DELETE /api/configuration/override/{key} (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="key">Configuration key to revert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        string key,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves direct researcher subject access grants via GET /api/subject-access/grants (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="researcher">Optional researcher username filter.</param>
    /// <param name="subject">Optional subject username filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of active subject grants.</returns>
    Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string? researcher = null,
        string? subject = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves current regulatory compliance scorecard and post-quantum encryption status via GET /api/compliance/status (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Regulatory compliance and cryptographic status scorecard.</returns>
    Task<ComplianceStatusReport> GetComplianceStatusAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves FDA 21 CFR Part 11 immutable hash-chained audit log records via GET /api/compliance/audit-ledger (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="limit">Maximum number of audit records to retrieve (defaults to 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of signed audit ledger records.</returns>
    Task<IReadOnlyList<AuditRecord>> GetAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates patient genetic markers and generates clinical severity-tiered call-out alerts via POST /api/genomics/analyze.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Genomic analysis request specifying patient ID, marker IDs, and HRT regimen.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Synthesized patient genomic profile containing severity alerts and kinetic adjustments.</returns>
    Task<PatientGenomicProfile> AnalyzeGenomicsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Cryptographically verifies unbroken hash chaining and ML-DSA-65 signatures across the audit ledger via GET /api/compliance/verify-ledger.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cryptographic verification result detailing ledger integrity.</returns>
    Task<AuditLedgerVerificationResult> VerifyAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Scans nucleotide sequences for CpG density (TLR9 immunogenicity) and CAG repeat length (AR sensitivity) via POST /api/genomics/analyze-sequence.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Sequence analysis request containing patient ID, raw FASTA string, and therapeutic regimen.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sequence-derived patient genomic profile with kinetic recommendations.</returns>
    Task<PatientGenomicProfile> AnalyzeGenomicSequenceAsync(
        InstanceProfile profile,
        string bearerToken,
        SequenceAnalysisRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a read-only analytical SQL query against a trajectory dataset via POST /api/trajectories/analytics/query.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Analytical query request containing dataset name, SQL text, and optional row limit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tabular trajectory analytical query results.</returns>
    Task<TrajectoryAnalyticsApiResponse> QueryTrajectoryAnalyticsAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryAnalyticsApiRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lists this node's public dataset-signing identity and all registered peer laboratory keys via GET /api/peer-keys (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Peer key listing response containing local node public key and registered peer keys.</returns>
    Task<PeerKeyListResponse> ListPeerKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Registers a trusted peer laboratory's dataset-signing public key via POST /api/peer-keys (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Registration payload containing peer label, algorithm, and Base64 public key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Registered peer key record.</returns>
    Task<PeerKeyRecord> RegisterPeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        RegisterPeerKeyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Permanently revokes an active peer dataset-signing public key via POST /api/peer-keys/{keyId}/revoke (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="keyId">Identifier of the peer key to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RevokePeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        string keyId,
        CancellationToken ct = default);
}
