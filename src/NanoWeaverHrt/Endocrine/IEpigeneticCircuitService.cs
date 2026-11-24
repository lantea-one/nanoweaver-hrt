using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Systems-biology and post-quantum safety gating contract for permanent epigenetic steroidogenic
/// toggle circuits, off-target CFD enforcement, Hill phase-plane stability checks, and ML-DSA-65 approval.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Domain service orchestrating non-nuclease chromatin reprogramming blueprints
/// targeting endogenous pathways (<c>CYP19A1</c>, <c>HSD17B1</c>, <c>SRD5A2</c>, <c>HSD17B3</c>, <c>CYP17A1</c>).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P221): Ensures no epigenetic circuit can be cryptographically approved
/// for clinical synthesis if off-target CFD specificity falls below 98.0%, bistability collapses, or hormone setpoints miss bounds.
/// </para>
/// <para>
/// <strong>How it works:</strong> Validates inputs, persists encrypted blueprints via <see cref="NanoWeaverHrt.Storage.IEpigeneticCircuitRepository"/>,
/// computes mutual-repression Hill nullclines and separatrix margins, signs safe circuits with ML-DSA-65, and emits 21 CFR Part 11 audits.
/// </para>
/// </remarks>
public interface IEpigeneticCircuitService
{
    /// <summary>
    /// Designs, validates, encrypts notes for, evaluates, and persists a patient-specific epigenetic toggle circuit.
    /// </summary>
    /// <param name="request">Circuit architecture, regimen, effector platform, and guide-RNA specification.</param>
    /// <param name="operatorId">Authenticated operator username recorded in the 21 CFR Part 11 audit ledger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete biophysical evaluation report for the newly designed circuit.</returns>
    Task<EpigeneticCircuitEvaluationReport> DesignCircuitAsync(
        DesignEpigeneticCircuitRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all epigenetic toggle circuits belonging to a patient with decrypted notes and reconstructed phase planes.
    /// </summary>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chronologically ordered read-only list of circuit evaluation reports.</returns>
    Task<IReadOnlyList<EpigeneticCircuitEvaluationReport>> ListPatientCircuitsAsync(
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates or re-evaluates Hill phase-plane bistability, CFD specificity, and endocrine setpoints for an existing circuit.
    /// </summary>
    /// <param name="circuitId">Target epigenetic circuit identifier.</param>
    /// <param name="request">Optional evaluation stability and clearance multipliers (defaults to 1.0).</param>
    /// <param name="operatorId">Authenticated operator username performing the evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated evaluation report, or <see langword="null"/> if <paramref name="circuitId"/> does not exist.</returns>
    Task<EpigeneticCircuitEvaluationReport?> EvaluateCircuitAsync(
        string circuitId,
        EvaluateEpigeneticCircuitRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs live fail-closed safety gating and signs approved circuits with the node's NIST FIPS 204 ML-DSA-65 key.
    /// </summary>
    /// <param name="circuitId">Target epigenetic circuit identifier.</param>
    /// <param name="request">Optional reviewer remarks and evaluation multipliers checked prior to signing.</param>
    /// <param name="operatorId">Authenticated approver username recorded in the 21 CFR Part 11 audit ledger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated circuit report (<c>Approved</c> when safe or <c>Blocked</c> fail-closed on gate failure), or <see langword="null"/> if not found.</returns>
    Task<EpigeneticCircuitEvaluationReport?> ApproveCircuitAsync(
        string circuitId,
        ApproveEpigeneticCircuitRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Independently validates the canonical SHA-256 hash and NIST FIPS 204 ML-DSA-65 approval signature
    /// attached to an <see cref="EpigeneticCircuitEvaluationReport"/>.
    /// </summary>
    /// <param name="report">Evaluation report carrying ML-DSA-65 approval fields.</param>
    /// <returns><see langword="true"/> when both canonical digest and ML-DSA-65 signature validate; otherwise <see langword="false"/>.</returns>
    bool VerifyApprovalSignature(EpigeneticCircuitEvaluationReport report);
}
