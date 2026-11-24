using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for multi-year endocrine homeostasis forecast runs and annual mitotic durability checkpoints
/// stored in <c>weaver_multiyear_homeostasis_runs</c> and <c>weaver_multiyear_homeostasis_checkpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Dialect-agnostic repository interface managing atomic creation, ordered depot queries,
/// annual checkpoint loading, and patient-scoped GDPR Article 17 erasure across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P225): Decouples longitudinal 10-year coupled depot/epigenetic forecasting
/// storage from underlying SQL connection management.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="MultiYearHomeostasisRepository"/> using parameterized SQL
/// and explicit transaction scopes without LINQ allocations.
/// </para>
/// </remarks>
public interface IMultiYearHomeostasisRepository
{
    /// <summary>
    /// Atomically persists a multi-year homeostasis forecast run together with all annual checkpoint snapshots.
    /// </summary>
    /// <param name="run">Run header record containing depot, optional circuit, horizon, and durability summary metrics.</param>
    /// <param name="checkpoints">Chronological annual checkpoint records belonging to <paramref name="run"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous transactional write.</returns>
    Task SaveForecastRunAsync(
        MultiYearHomeostasisRunRecord run,
        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> checkpoints,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single multi-year homeostasis run record by its primary identifier.
    /// </summary>
    /// <param name="runId">Unique run identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="MultiYearHomeostasisRunRecord"/>, or <see langword="null"/> if not found.</returns>
    Task<MultiYearHomeostasisRunRecord?> GetRunByIdAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all persisted multi-year homeostasis forecast runs for a given depot in newest-first order.
    /// </summary>
    /// <param name="depotId">Parent semi-annual depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest-first read-only list of <see cref="MultiYearHomeostasisRunRecord"/> items.</returns>
    Task<IReadOnlyList<MultiYearHomeostasisRunRecord>> ListRunsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent multi-year homeostasis forecast run for a given depot formulation.
    /// </summary>
    /// <param name="depotId">Parent semi-annual depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest <see cref="MultiYearHomeostasisRunRecord"/>, or <see langword="null"/> when none exists.</returns>
    Task<MultiYearHomeostasisRunRecord?> GetLatestRunByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all annual checkpoint snapshots associated with a specific multi-year forecast run in ascending year order.
    /// </summary>
    /// <param name="runId">Parent forecast run identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chronologically ordered list of <see cref="MultiYearHomeostasisCheckpointRecord"/> items.</returns>
    Task<IReadOnlyList<MultiYearHomeostasisCheckpointRecord>> GetCheckpointsByRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes patient-scoped GDPR Article 17 cascading deletion across child checkpoints and parent forecast runs.
    /// </summary>
    /// <param name="patientId">Patient identifier whose longitudinal homeostasis forecasts should be erased.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total count of deleted checkpoint and forecast run rows.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);
}
