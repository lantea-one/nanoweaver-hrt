using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Domain service contract for Arrhenius cold-chain thermal stability modeling, SHA-256 hash-chained
/// chain-of-custody recording, fail-closed patient administration gating, and Certificate of Analysis stability verification.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The clinical cold-chain and provenance service bridging physical wet-lab batch
/// release (<see cref="IFabricationLotService"/>) to safe clinic-to-patient administration.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P211): Ensures thermally denatured, expired, or custody-tampered
/// DNA origami HRT lots fail closed before clinical implantation even if their ML-DSA-65 CoA signature is intact.
/// </para>
/// <para>
/// <strong>How it works:</strong> Integrates first-order Arrhenius kinetics with cooperative dsDNA melting penalties,
/// encrypts optional excursion notes with AES-256-GCM, links custody handoffs via portable microsecond-normalized
/// SHA-256 chains, and writes 21 CFR Part 11 audit events.
/// </para>
/// </remarks>
public interface IColdChainStabilityService
{
    /// <summary>
    /// Computes the first-order Arrhenius degradation rate constant k(T) in h^-1 including dsDNA melting-cliff penalties.
    /// </summary>
    /// <param name="temperatureCelsius">Observed storage or excursion temperature in degrees Celsius.</param>
    /// <returns>Effective degradation rate constant in inverse hours (h^-1).</returns>
    double ComputeDegradationRatePerHour(double temperatureCelsius);

    /// <summary>
    /// Computes the canonical lowercase SHA-256 hex digest committing a custody handoff record to its predecessor.
    /// </summary>
    /// <param name="record">The custody handoff record (using <c>ChainPrevSha256</c>, normalized timestamp, and 4-decimal numbers).</param>
    /// <returns>64-character lowercase hexadecimal SHA-256 digest.</returns>
    string ComputeCustodyEntrySha256(CustodyHandoffRecord record);

    /// <summary>
    /// Verifies SHA-256 hash-chain continuity from genesis across an ordered sequence of custody handoffs.
    /// </summary>
    /// <param name="handoffs">Chronologically ordered custody handoffs for a single fabrication lot.</param>
    /// <returns>A <see cref="CustodyChainVerificationResult"/> detailing integrity validity and any tamper reason.</returns>
    CustodyChainVerificationResult VerifyCustodyChain(IReadOnlyList<CustodyHandoffRecord> handoffs);

    /// <summary>
    /// Validates, persists, and audits a new cold-chain thermal excursion log entry and returns updated Arrhenius stability.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="request">Thermal excursion temperature, duration, storage tier, sensor, and optional notes.</param>
    /// <param name="operatorId">Authenticated operator username logging the observation.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted <see cref="ThermalExcursionResponse"/> with recalculated stability, or null if the lot does not exist.</returns>
    Task<ThermalExcursionResponse?> RecordThermalExcursionAsync(
        string lotId,
        RecordThermalExcursionRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates current Arrhenius potency, remaining shelf life, thermal budget, and custody-chain validity for a lot.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The computed <see cref="LotStabilityReport"/>, or null if the lot does not exist.</returns>
    Task<LotStabilityReport?> EvaluateLotStabilityAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, hash-chains, persists, and audits a physical chain-of-custody handoff, blocking unsafe
    /// <see cref="CustodyStage.PatientAdministration"/> and post-<see cref="CustodyStage.QuarantineDisposal"/> transitions fail-closed.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="request">Custody stage, releasing/receiving actors, facility code, and handoff temperature.</param>
    /// <param name="operatorId">Authenticated operator username recording the handoff.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted hash-chained <see cref="CustodyHandoffRecord"/>, or null if the lot does not exist.</returns>
    Task<CustodyHandoffRecord?> RecordCustodyHandoffAsync(
        string lotId,
        RecordCustodyHandoffRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full hash-chained custody ledger and live integrity verification status for a lot.
    /// </summary>
    /// <param name="lotId">Target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The <see cref="CustodyHandoffLedgerResponse"/>, or null if the lot does not exist.</returns>
    Task<CustodyHandoffLedgerResponse?> GetCustodyLedgerAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies fail-closed local cold-chain stability and custody-chain gating onto a base Certificate of Analysis verification report.
    /// </summary>
    /// <param name="baseReport">The cryptographic verification verdict produced by <see cref="IFabricationLotService"/>.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>An enriched <see cref="FabricationCertificateVerificationReport"/> reflecting cold-chain safety when a local lot exists.</returns>
    Task<FabricationCertificateVerificationReport> ApplyColdChainStabilityGateAsync(
        FabricationCertificateVerificationReport baseReport,
        CancellationToken cancellationToken = default);
}
