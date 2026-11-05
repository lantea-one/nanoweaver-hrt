using System.Text.Json;
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
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations.Models;
using NanoWeaverHrt;

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

    /// <summary>
    /// Grants a researcher direct tiered access to a patient via POST /api/subject-access/grants (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Subject grant request containing researcher username, subject username, access level, and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="SubjectGrant"/> record.</returns>
    Task<SubjectGrant> CreateSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        GrantSubjectAccessRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a researcher's direct access to a patient via DELETE /api/subject-access/grants/{researcherUsername}/{subjectUsername} (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="researcherUsername">Username of the researcher whose access is being revoked.</param>
    /// <param name="subjectUsername">Username of the target patient or subject.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RevokeSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Lists patient cohort groups accessible to the caller via GET /api/subject-groups.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of accessible <see cref="SubjectGroup"/> records.</returns>
    Task<IReadOnlyList<SubjectGroup>> ListSubjectGroupsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new patient cohort group via POST /api/subject-groups.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Cohort group creation request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="SubjectGroup"/> record.</returns>
    Task<SubjectGroup> CreateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateSubjectGroupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a patient cohort group via DELETE /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all created single-use share keys and connected remote peer instances via GET /api/share-keys.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of <see cref="ShareKeyRecord"/> entries.</returns>
    Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Lists enrolled subject usernames for a patient cohort group via GET /api/subject-groups/{groupId}/members.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of enrolled subject usernames.</returns>
    Task<IReadOnlyList<string>> ListGroupMembersAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Enrolls a patient into a cohort group via POST /api/subject-groups/{groupId}/members.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="request">Payload containing subject username to enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        AddGroupMemberRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Un-enrolls a patient from a cohort group via DELETE /api/subject-groups/{groupId}/members/{subjectUsername}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="subjectUsername">Username of the subject to un-enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Lists researcher access grants for a cohort group via GET /api/subject-groups/{groupId}/grants (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of <see cref="SubjectGroupGrant"/> records.</returns>
    Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Grants a researcher tiered access over a cohort group via POST /api/subject-groups/{groupId}/grants (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="request">Payload containing researcher username, tiered access level, and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="SubjectGroupGrant"/> record.</returns>
    Task<SubjectGroupGrant> CreateGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        GrantGroupAccessRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a researcher's access grant over a cohort group via DELETE /api/subject-groups/{groupId}/grants/{researcherUsername} (Administrator role required).
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="researcherUsername">Username of the researcher whose grant is being revoked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RevokeGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string researcherUsername,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a single-use collaborative share key via POST /api/share-keys/create.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Request parameters specifying label, lifetime, and permissions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created single-use <see cref="ShareKeyRecord"/>.</returns>
    Task<ShareKeyRecord> CreateShareKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateShareKeyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Validates and atomically consumes a single-use share key via POST /api/share-keys/redeem.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="request">Payload containing single-use share token to redeem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of redemption including granted session token and role.</returns>
    Task<ShareKeyConsumeResult> RedeemShareKeyAsync(
        InstanceProfile profile,
        RedeemShareKeyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single cohort group record by its identifier via GET /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="SubjectGroup"/> record details.</returns>
    Task<SubjectGroup> GetSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the display name or description of an existing cohort group via PUT /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="groupId">Identifier of the cohort group to update.</param>
    /// <param name="request">Payload containing updated group name and optional description.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="SubjectGroup"/> record.</returns>
    Task<SubjectGroup> UpdateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        UpdateSubjectGroupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all patient subjects accessible to the current authenticated caller via GET /api/subject-access/my-subjects.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of accessible subject usernames with effective access levels.</returns>
    Task<IReadOnlyList<AccessibleSubjectItem>> ListMySubjectsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves standard built-in DNA origami fabrication presets via GET /api/fabrication/presets.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of sample DNA origami architectures and geometries.</returns>
    Task<IReadOnlyList<DnaOrigamiSample>> GetFabricationPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Synthesizes staple routing manifest, annealing schedule, and SHA-256 hash via POST /api/fabrication/generate.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Fabrication parameters specifying scaffold sequence, geometry, and target dissociation constant.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Synthesized <see cref="FabricationPackage"/> ready for pipetting.</returns>
    Task<FabricationPackage> GenerateFabricationProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        FabricationGenerateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves standard clinical genetic panels for rapid patient profiling via GET /api/genomics/presets.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of preset clinical genomic profiles.</returns>
    Task<IReadOnlyList<PatientGenomicProfile>> GetGenomicPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves catalog of clinically characterized pharmacogenomic markers via GET /api/genomics/markers.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of genetic markers and risk alleles.</returns>
    Task<IReadOnlyList<GeneticMarker>> GetGenomicMarkersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves real-time status and health telemetry for all container services via GET /api/services.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of runtime statuses for database, cache, and simulation engines.</returns>
    Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity and latency to a single stack dependency via POST /api/services/ping/{serviceId}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="serviceId">Identifier of target service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refreshed health and latency metrics for the service.</returns>
    Task<StackServiceInfo> PingStackServiceAsync(
        InstanceProfile profile,
        string bearerToken,
        string serviceId,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates pharmacodynamic hormone concentration stabilization curves over a 72-hour horizon via GET /api/endocrine/kinetics.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Chronological sequence of simulated hormone concentration curve points.</returns>
    Task<IReadOnlyList<HormoneCurvePoint>> GetEndocrineKineticsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Toggles synthetic endocrine regulatory setpoints between Estrogenic and Androgenic dominance via POST /api/endocrine/toggle.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Request specifying target dominance regime and setpoint adjustments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated target endocrine profile.</returns>
    Task<EndocrineTargetProfile> ToggleEndocrineSwitchAsync(
        InstanceProfile profile,
        string bearerToken,
        ToggleSwitchRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all registered sovereign user accounts via GET /api/users.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of registered user accounts.</returns>
    Task<IReadOnlyList<UserAccount>> ListUsersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Provisions a new sovereign user account with an existing public key via POST /api/users.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">User account creation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created user account record.</returns>
    Task<UserAccount> CreateUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateUserRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes and permanently deletes a user account via DELETE /api/users/{username}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Username of account to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeleteUserAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves demographic, clinical, and pronoun profile data for a user via GET /api/users/{username}/profile.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Demographic and clinical user profile.</returns>
    Task<UserProfile> GetUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates demographic, clinical, and pronoun profile data for a user via PUT /api/users/{username}/profile.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="userProfile">Updated profile payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated user profile.</returns>
    Task<UserProfile> UpdateUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        UserProfile userProfile,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all structured addresses registered for a user via GET /api/users/{username}/addresses.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of registered addresses.</returns>
    Task<IReadOnlyList<PatientAddress>> ListUserAddressesAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Adds or updates a structured address record for a user via POST /api/users/{username}/addresses.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="address">Address to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persisted address record.</returns>
    Task<PatientAddress> AddUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientAddress address,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an address record for a user via DELETE /api/users/{username}/addresses/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="addressId">Identifier of the address to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeleteUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string addressId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves emergency and caregiver contacts for a user via GET /api/users/{username}/contacts.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of emergency and caregiver contacts.</returns>
    Task<IReadOnlyList<PatientContact>> ListUserContactsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Adds or updates an emergency or caregiver contact for a user via POST /api/users/{username}/contacts.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="contact">Contact to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persisted contact record.</returns>
    Task<PatientContact> AddUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientContact contact,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an emergency contact record for a user via DELETE /api/users/{username}/contacts/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target username.</param>
    /// <param name="contactId">Identifier of contact to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeleteUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string contactId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific digitized DNA blueprint by ID via GET /api/patients/{username}/dna/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the DNA record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Digitized DNA record.</returns>
    Task<PatientDigitalDnaRecord> GetPatientDigitalDnaRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a digitized DNA blueprint record by ID via DELETE /api/patients/{username}/dna/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the DNA record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeletePatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all imported sequence and dataset files for a patient via GET /api/patients/{username}/imports.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of imported sequence records.</returns>
    Task<IReadOnlyList<PatientDnaImportRecord>> ListPatientImportsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves an imported sequence or dataset record by ID via GET /api/patients/{username}/imports/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the import record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Imported sequence record.</returns>
    Task<PatientDnaImportRecord> GetPatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Imports DNA sequence data or variant panels for a patient via POST /api/patients/{username}/imports.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="request">Import payload containing file name and sequence data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created import record.</returns>
    Task<PatientDnaImportRecord> CreatePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientImportRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an imported sequence or dataset record by ID via DELETE /api/patients/{username}/imports/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the import record to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeletePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all simulation and clinical results for a patient via GET /api/patients/{username}/results.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="resultType">Optional result type filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of clinical and simulation result records.</returns>
    Task<IReadOnlyList<PatientResultRecord>> ListPatientClinicalResultsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string? resultType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific simulation or clinical result record by ID via GET /api/patients/{username}/results/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the result record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Clinical or simulation result record.</returns>
    Task<PatientResultRecord> GetPatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Registers a new simulation or clinical result record via POST /api/patients/{username}/results.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="request">Result registration payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created result record.</returns>
    Task<PatientResultRecord> CreatePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientResultRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a simulation or clinical result record by ID via DELETE /api/patients/{username}/results/{id}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of result record to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeletePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves identity claims and account details for the authenticated user via GET /api/auth/me.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current authenticated user account.</returns>
    Task<UserAccount> GetCurrentUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Terminates the interactive session via POST /api/auth/logout.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task LogoutAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates whether the first-launch setup wizard is required via GET /api/setup/status.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Setup status payload indicating if initial administrator provisioning is needed.</returns>
    Task<SetupStatusResponse> GetSetupStatusAsync(
        InstanceProfile profile,
        CancellationToken ct = default);

    /// <summary>
    /// Executes cryptographic destruction of subject decryption keys via POST /api/compliance/crypto-shred.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Crypto-shred payload specifying target subject ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task CryptoShredUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CryptoShredRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Applies HIPAA Safe Harbor de-identification and GINA/GDPR Article 9 consent validation via POST /api/compliance/de-identify.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">De-identification request containing subject identifier and genomic sequence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>De-identification result containing pseudonymized identifier and sanitized payload.</returns>
    Task<DeIdentificationResult> DeIdentifyDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        DeIdentifyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Envelope-encrypts an arbitrary JSON payload under a subject's post-quantum ML-KEM-768 key via POST /api/compliance/encrypt-record.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Encryption request containing subject ID and payload object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Post-quantum encrypted envelope.</returns>
    Task<EncryptedEnvelope> EncryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        EncryptRecordRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts an authenticated post-quantum envelope under a subject's post-quantum ML-KEM-768 key via POST /api/compliance/decrypt-record.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Decryption request containing subject ID and encrypted envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decrypted JSON element payload.</returns>
    Task<JsonElement> DecryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        DecryptRecordRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Cryptographically verifies and imports a simulation dataset archive and manifest via POST /api/datasets/import.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="archiveBytes">Raw compressed archive byte payload (.tar.gz).</param>
    /// <param name="archiveFileName">File name of the archive bundle.</param>
    /// <param name="manifestBytes">Raw JSON bytes of the dataset provenance manifest.</param>
    /// <param name="manifestFileName">File name of the provenance manifest JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result and provenance descriptor from the server.</returns>
    Task<DatasetImportResponse> ImportDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        byte[] archiveBytes,
        string archiveFileName,
        byte[] manifestBytes,
        string manifestFileName,
        CancellationToken ct = default);

    /// <summary>
    /// Packages and envelope-encrypts a trajectory dataset using post-quantum ML-KEM-768 and signs the manifest with ML-DSA-65 via POST /api/datasets/export-encrypted.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Request parameters specifying source trajectory, subject ID, and optional destination directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result metadata detailing bundle path, algorithm suite, and subject identifier.</returns>
    Task<ExportEncryptedDatasetResult> ExportEncryptedDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportEncryptedDatasetRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all packaged dataset archives, manifests, and signatures in the export volume via GET /api/datasets/exports.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of export file metadata items.</returns>
    Task<IReadOnlyList<ExportFileItem>> ListExportedDatasetBundlesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Purges a packaged dataset bundle or manifest from the export volume via DELETE /api/datasets/exports/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the exported file to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    Task DeleteExportedDatasetBundleAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Streams raw binary bytes of an exported dataset archive or manifest via GET /api/datasets/download/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the exported archive or manifest file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw byte buffer of the downloaded artifact.</returns>
    Task<byte[]> DownloadDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Streams raw simulation trajectory coordinates from the shared volume via GET /api/trajectories/download/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the trajectory file (.xyz, .dcd, .dump).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw byte buffer of the simulation trajectory.</returns>
    Task<byte[]> DownloadTrajectoryAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Bootstraps the sovereign node by provisioning the master Administrator account and initial public key via POST /api/setup/initialize.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="request">Initial administrator credentials, public key, and access gate configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Authentication response containing initial Bearer JWT and administrator user profile.</returns>
    Task<AuthTokenResponse> InitializeSetupAdminAsync(
        InstanceProfile profile,
        InitialSetupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the operational health status and diagnostic telemetry via GET /api/system/health.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Optional Bearer token for granular subsystem diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>System health overview report.</returns>
    Task<SystemHealthOverview> GetSystemHealthAsync(
        InstanceProfile profile,
        string? bearerToken = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves real-time telemetry on the in-memory event bus and distributed Valkey bridge via GET /api/system/event-bus.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Event bus telemetry metrics.</returns>
    Task<EventBusTelemetry> GetEventBusMetricsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Triggers zero-allocation stream parsing for a specific trajectory file and returns SIMD throughput metrics via POST /api/parser/process/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the trajectory file in the incoming storage volume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parsing performance and frame count metrics.</returns>
    Task<TrajectoryParserProcessResult> ProcessTrajectoryCoordinatesAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Converts a raw trajectory stream to a columnar Apache Parquet file using DuckDB and SIMD intrinsics via POST /api/parser/export-parquet/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the source trajectory file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parquet serialization metadata and byte size metrics.</returns>
    Task<TrajectoryParquetExportResult> ExportCoordinatesParquetAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Streams trajectory coordinates to compute real-time ligand-pocket contact ratios and binding stability via POST /api/endocrine/analyze/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the trajectory file to analyze.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aptamer binding occupancy and stability metrics.</returns>
    Task<AptamerTrajectoryMetrics> AnalyzeTrajectoryEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Generates baseline simulation input deck script for the specified campaign ID via GET /api/simulations/configs/{campaignId}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="campaignId">Identifier of the simulation campaign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation input deck configuration result.</returns>
    Task<SimulationInputDeckResult> GetSimulationCampaignConfigAsync(
        InstanceProfile profile,
        string bearerToken,
        string campaignId,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates patient pharmacogenomics and adjusts active endocrine setpoints via POST /api/genomics/apply-adjustments.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Genomic markers and target HRT regimen request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Evaluated genomic profile and adjusted endocrine target setpoints.</returns>
    Task<GenomicAdjustmentResult> ApplyGenomicAdjustmentsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Generates and downloads an executable Opentrons Python protocol script (.py) via POST /api/fabrication/opentrons/download.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Fabrication parameters and liquid handler robot options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>UTF-8 byte buffer of the generated Python protocol script.</returns>
    Task<byte[]> DownloadOpentronsScriptAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a specific binary trajectory chunk by index for P2P swarm distribution via GET /api/federation/chunks/{fileName}.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="fileName">Name of the federated trajectory file.</param>
    /// <param name="chunkIndex">Zero-based chunk index.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Binary bytes of the requested chunk.</returns>
    Task<byte[]> GetFederationChunkAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        int? chunkIndex = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a serum stability simulation campaign via POST /api/simulations/run/serum-stability.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Serum stability simulation parameters and environmental conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including degradation kinetics and half-life projections.</returns>
    Task<SerumStabilityAnalysisResult> RunSerumStabilityAsync(
        InstanceProfile profile,
        string bearerToken,
        SerumStabilityRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Executes an aptamer gating simulation campaign via POST /api/simulations/run/aptamer-gating.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Aptamer gating simulation parameters and target ligand conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including opening probability and gating thermodynamics.</returns>
    Task<AptamerGatingAnalysisResult> RunAptamerGatingAsync(
        InstanceProfile profile,
        string bearerToken,
        AptamerGatingRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a payload leakage simulation campaign via POST /api/simulations/run/payload-leakage.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">Payload retention parameters and membrane stability conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including premature leakage rates and retention efficiency.</returns>
    Task<PayloadLeakageAnalysisResult> RunPayloadLeakageAsync(
        InstanceProfile profile,
        string bearerToken,
        PayloadLeakageRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a PBPK endocrine pharmacokinetic simulation campaign via POST /api/simulations/run/pbpk-endocrine.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="request">PBPK endocrine dosing, receptor kinetics, and patient demographic parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including systemic bioavailability and compartmental kinetics.</returns>
    Task<PbpkEndocrineSimulationResult> RunPbpkSimulationAsync(
        InstanceProfile profile,
        string bearerToken,
        PbpkSimulationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a browser-signed challenge nonce and establishes an authenticated Web UI session cookie via POST /api/auth/web-token.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="request">Token exchange credentials including username, challenge ID, and cryptographic signature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Issued session token and expiration metadata.</returns>
    Task<AuthTokenResponse> ExchangeWebTokenAsync(
        InstanceProfile profile,
        TokenExchangeRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates cluster container runtime operational health via the GET /healthz liveness probe.
    /// </summary>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>High-level system health overview.</returns>
    Task<SystemHealthOverview> GetHealthzAsync(
        InstanceProfile profile,
        CancellationToken ct = default);
}
