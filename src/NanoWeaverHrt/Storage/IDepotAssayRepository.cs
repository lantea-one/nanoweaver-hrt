using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for longitudinal patient serum hormone assay observations and adaptive depot
/// calibration snapshots linked to semi-annual time-release depot formulations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational data access contract governing storage, chronological retrieval,
/// and GDPR Article 17 erasure of depot serum assays and Bayesian calibration snapshots.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Decouples the longitudinal calibration engine, Minimal APIs, and sovereign UI
/// from underlying SQLite, PostgreSQL, DuckDB, and MySQL database drivers.
/// </para>
/// <para>
/// <strong>How it works:</strong> Provides asynchronous methods for recording observations, querying chronological
/// assay histories by depot, saving/retrieving calibration snapshots, and cascading erasure by patient ID.
/// </para>
/// </remarks>
public interface IDepotAssayRepository
{
    /// <summary>
    /// Persists a longitudinal serum hormone assay observation linked to an existing depot formulation.
    /// </summary>
    /// <param name="observation">The serum assay observation record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="DepotAssayObservationRecord"/> (with an assigned <c>ObservationId</c> if omitted).</returns>
    Task<DepotAssayObservationRecord> RecordObservationAsync(
        DepotAssayObservationRecord observation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all longitudinal serum hormone assay observations for a specific depot formulation in chronological order.
    /// </summary>
    /// <param name="depotId">The parent depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>A read-only list of assay observation records ordered ascending by <c>DaySinceImplant</c> and timestamp.</returns>
    Task<IReadOnlyList<DepotAssayObservationRecord>> GetObservationsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an adaptive Bayesian depot calibration snapshot.
    /// </summary>
    /// <param name="snapshot">The calibration snapshot record to save.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="DepotCalibrationSnapshotRecord"/> (with an assigned <c>SnapshotId</c> if omitted).</returns>
    Task<DepotCalibrationSnapshotRecord> SaveCalibrationSnapshotAsync(
        DepotCalibrationSnapshotRecord snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recently computed calibration snapshot for a given depot formulation.
    /// </summary>
    /// <param name="depotId">The parent depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The most recent <see cref="DepotCalibrationSnapshotRecord"/> if any exist; otherwise, <see langword="null"/>.</returns>
    Task<DepotCalibrationSnapshotRecord?> GetLatestCalibrationSnapshotAsync(
        string depotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all longitudinal serum assay observations and calibration snapshots belonging to a specific patient.
    /// </summary>
    /// <param name="patientId">The patient identifier whose longitudinal assay and calibration records should be erased.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The total number of database rows deleted across observations and calibration snapshots.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);
}
