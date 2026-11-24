using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Clinical post-implantation pharmacovigilance surveillance, CARPA/TLR9 immunogenicity detection,
/// and automated fail-closed quarantine escalation service contract.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Domain service recording CTCAE v5.0 adverse events and quantitative
/// complement/cytokine/anti-DNA biomarker assays, escalating affected fabrication lots to quarantine,
/// and gating cross-node Certificate of Analysis verification.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P216): Protects patients against CARPA hypersensitivity,
/// innate TLR9 cytokine storms, and humoral anti-PEG/anti-dsDNA reactivity after depot implantation.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="ClinicalPharmacovigilanceService"/> with
/// AES-256-GCM clinical note encryption, zero-LINQ threshold evaluation, and 21 CFR Part 11 audit events.
/// </para>
/// </remarks>
public interface IClinicalPharmacovigilanceService
{
    /// <summary>
    /// Validates and persists a post-implantation CTCAE v5.0 adverse event, encrypts clinical notes at rest,
    /// escalates severe events (Grade >= 3) to automatic lot quarantine, and emits 21 CFR Part 11 audit events.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="request">Adverse event grading, symptom category, onset hours, and optional notes.</param>
    /// <param name="operatorId">Authenticated clinician or administrator username recording the event.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted summary and updated pharmacovigilance report, or <see langword="null"/> if the lot does not exist.</returns>
    Task<AdverseEventResponse?> RecordAdverseEventAsync(
        string lotId,
        RecordAdverseEventRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all chronologically ordered post-implantation adverse events for a fabrication lot with notes decrypted.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronological adverse event summaries, or <see langword="null"/> if the lot does not exist.</returns>
    Task<IReadOnlyList<LotAdverseEventSummary>?> ListAdverseEventsAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and persists a CARPA complement, TLR9 cytokine, and humoral anti-DNA immunogenicity panel,
    /// escalates critical excursions to automatic lot quarantine, and emits 21 CFR Part 11 audit events.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="request">Biomarker assay values and optional UTC timestamp.</param>
    /// <param name="operatorId">Authenticated clinician or administrator username recording the panel.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted assay and updated pharmacovigilance report, or <see langword="null"/> if the lot does not exist.</returns>
    Task<ImmunogenicityAssayResponse?> RecordImmunogenicityAssayAsync(
        string lotId,
        RecordImmunogenicityAssayRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the aggregate pharmacovigilance posture, CARPA/TLR9 signals, and quarantine flags for a fabrication lot.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The evaluated <see cref="LotPharmacovigilanceReport"/>, or <see langword="null"/> if the lot does not exist.</returns>
    Task<LotPharmacovigilanceReport?> EvaluateLotPharmacovigilanceAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enforces fail-closed pharmacovigilance gating on a <see cref="FabricationCertificateVerificationReport"/>,
    /// marking <c>IsTrusted = false</c> if the corresponding local lot is flagged for CARPA or CTCAE quarantine.
    /// </summary>
    /// <param name="baseReport">Base Certificate of Analysis verification report.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Enriched and fail-closed gated verification report.</returns>
    Task<FabricationCertificateVerificationReport> ApplyPharmacovigilanceGateAsync(
        FabricationCertificateVerificationReport baseReport,
        CancellationToken cancellationToken = default);
}
