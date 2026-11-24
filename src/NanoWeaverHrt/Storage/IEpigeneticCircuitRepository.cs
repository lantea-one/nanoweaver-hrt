using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Repository contract for persisting and erasing epigenetic steroidogenic toggle circuits
/// and associated single-guide RNA specificity records across all supported SQL backends.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Data-access abstraction over <c>weaver_epigenetic_circuits</c> and
/// <c>weaver_epigenetic_guide_rnas</c> supporting patient-scoped CRUD and GDPR Article 17 erasure cascades.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P220): Decouples systems-biology circuit evaluation from SQL dialect details.
/// </para>
/// <para>
/// <strong>How it works:</strong> Provides atomic save, single-circuit lookup, chronological patient listing,
/// guide-RNA querying, status/signature updates, and patient erasure cascades.
/// </para>
/// </remarks>
public interface IEpigeneticCircuitRepository
{
    /// <summary>
    /// Atomically persists or replaces an epigenetic circuit record and its complete set of guide-RNA loci.
    /// </summary>
    /// <param name="circuit">Epigenetic circuit header record to store.</param>
    /// <param name="guides">Guide-RNA records belonging to the circuit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous persistence operation.</returns>
    Task SaveCircuitAsync(
        EpigeneticCircuitRecord circuit,
        IReadOnlyList<EpigeneticGuideRnaRecord> guides,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single epigenetic circuit record by its unique identifier.
    /// </summary>
    /// <param name="circuitId">Unique circuit identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="EpigeneticCircuitRecord"/>, or <see langword="null"/> if not found.</returns>
    Task<EpigeneticCircuitRecord?> GetCircuitByIdAsync(
        string circuitId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all epigenetic circuits belonging to a patient ordered chronologically ascending.
    /// </summary>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of circuits ordered by creation timestamp and circuit identifier.</returns>
    Task<IReadOnlyList<EpigeneticCircuitRecord>> ListCircuitsByPatientAsync(
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all single-guide RNA records assigned to an epigenetic circuit ordered deterministically by guide identifier.
    /// </summary>
    /// <param name="circuitId">Parent epigenetic circuit identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of guide-RNA records ordered by <c>guide_id ASC</c>.</returns>
    Task<IReadOnlyList<EpigeneticGuideRnaRecord>> ListGuidesByCircuitAsync(
        string circuitId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates evaluation metrics, fail-closed safety status, and optional ML-DSA-65 approval fields for an existing circuit.
    /// </summary>
    /// <param name="circuitId">Target circuit identifier.</param>
    /// <param name="status">Updated lifecycle safety status.</param>
    /// <param name="predictedTtrPercent">Predicted time-in-therapeutic-range percentage.</param>
    /// <param name="separatrixMargin">Normalized bistable separatrix margin.</param>
    /// <param name="minOffTargetSpecificityScore">Minimum genome-wide CFD score across all guides.</param>
    /// <param name="approvedAtUtc">Approval UTC timestamp when approved; otherwise <see langword="null"/>.</param>
    /// <param name="approvalPayloadSha256">Canonical SHA-256 hex digest when approved; otherwise <see langword="null"/>.</param>
    /// <param name="approvalSignatureBase64">Base64 ML-DSA-65 signature when approved; otherwise <see langword="null"/>.</param>
    /// <param name="approvalSignerKeyId">Signing key identifier when approved; otherwise <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if an existing row was updated; otherwise <see langword="false"/>.</returns>
    Task<bool> UpdateCircuitEvaluationAsync(
        string circuitId,
        EpigeneticCircuitStatus status,
        double predictedTtrPercent,
        double separatrixMargin,
        double minOffTargetSpecificityScore,
        DateTimeOffset? approvedAtUtc,
        string? approvalPayloadSha256,
        string? approvalSignatureBase64,
        string? approvalSignerKeyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an atomic GDPR Article 17 erasure cascade removing all guide-RNA and circuit rows owned by a patient.
    /// </summary>
    /// <param name="patientId">Patient identifier whose epigenetic records must be erased.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of child and parent database rows removed.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);
}
