using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for semi-annual time-release depot formulations and discrete pharmacokinetic release schedules.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The data access interface governing storage and retrieval of nanocarrier depot
/// formulations and their corresponding simulated or measured time-release schedules.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Decouples higher-level clinical and simulation engines from relational database
/// operations across SQLite, PostgreSQL, DuckDB, and MySQL engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Declares asynchronous methods for upserting formulations, point lookups by depot ID,
/// patient-level cohort querying, and transactional schedule point replacement.
/// </para>
/// </remarks>
public interface IDepotFormulationRepository
{
    /// <summary>
    /// Inserts or updates a semi-annual time-release depot formulation in persistent storage.
    /// </summary>
    /// <param name="record">The formulation record to insert or update.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <returns>A task representing the asynchronous upsert operation.</returns>
    Task UpsertDepotFormulationAsync(DepotFormulationRecord record, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a semi-annual time-release depot formulation by its unique identifier.
    /// </summary>
    /// <param name="depotId">The unique identifier of the depot formulation.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <returns>The formulation record if found; otherwise, <see langword="null"/>.</returns>
    Task<DepotFormulationRecord?> GetDepotFormulationByIdAsync(string depotId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all semi-annual time-release depot formulations associated with a specific patient.
    /// </summary>
    /// <param name="patientId">The patient identifier to filter formulations by.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <returns>A read-only list of formulation records matching the patient identifier, ordered by creation date descending.</returns>
    Task<IReadOnlyList<DepotFormulationRecord>> GetDepotsByPatientIdAsync(string patientId, CancellationToken ct = default);

    /// <summary>
    /// Atomically replaces the discrete release schedule timepoints associated with a depot formulation.
    /// </summary>
    /// <param name="depotId">The parent depot formulation identifier.</param>
    /// <param name="points">The collection of discrete schedule timepoints to store.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <returns>A task representing the asynchronous transactional replace operation.</returns>
    Task SaveReleaseScheduleAsync(string depotId, IReadOnlyList<DepotSchedulePoint> points, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the discrete release schedule timepoints associated with a depot formulation.
    /// </summary>
    /// <param name="depotId">The parent depot formulation identifier.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <returns>A read-only list of discrete schedule timepoints ordered chronologically by day.</returns>
    Task<IReadOnlyList<DepotSchedulePoint>> GetReleaseScheduleAsync(string depotId, CancellationToken ct = default);
}
