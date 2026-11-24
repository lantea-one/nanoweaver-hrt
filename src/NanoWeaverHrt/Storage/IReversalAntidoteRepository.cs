using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository contract managing paired toehold strand-displacement antidotes and
/// emergency clinical depot neutralization events across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Data-access abstraction over <c>weaver_lot_reversal_antidotes</c> and
/// <c>weaver_lot_neutralization_events</c>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P230): Isolates antidote persistence, status transitions, and
/// GDPR Article 17 erasure cascades from biophysical kinetics and cryptographic pairing services.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="ReversalAntidoteRepository"/> via
/// <see cref="NanoWeaverHrt.Database.Access.IWeaverDbConnectionFactory"/>.
/// </para>
/// </remarks>
public interface IReversalAntidoteRepository
{
    /// <summary>
    /// Atomically persists or updates a paired reversal antidote protocol record.
    /// </summary>
    /// <param name="antidote">Reversal antidote protocol record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous persistence operation.</returns>
    Task SaveAntidoteAsync(
        ReversalAntidoteRecord antidote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recently persisted reversal antidote protocol paired with a primary fabrication lot.
    /// </summary>
    /// <param name="primaryLotId">Primary wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The latest <see cref="ReversalAntidoteRecord"/> if paired; otherwise <see langword="null"/>.</returns>
    Task<ReversalAntidoteRecord?> GetLatestAntidoteByLotAsync(
        string primaryLotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paired reversal antidote record by its unique identifier.
    /// </summary>
    /// <param name="antidoteId">Unique reversal antidote identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The matching <see cref="ReversalAntidoteRecord"/> if found; otherwise <see langword="null"/>.</returns>
    Task<ReversalAntidoteRecord?> GetAntidoteByIdAsync(
        string antidoteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the lifecycle status of an existing paired reversal antidote record.
    /// </summary>
    /// <param name="antidoteId">Target reversal antidote identifier.</param>
    /// <param name="status">New operational lifecycle status.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns><see langword="true"/> when a matching row was updated; otherwise <see langword="false"/>.</returns>
    Task<bool> UpdateAntidoteStatusAsync(
        string antidoteId,
        ReversalAntidoteStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an immutable emergency clinical depot neutralization event record.
    /// </summary>
    /// <param name="neutralizationEvent">Neutralization administration record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task AddNeutralizationEventAsync(
        LotNeutralizationEventRecord neutralizationEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all recorded emergency neutralization events for a primary fabrication lot ordered chronologically ascending.
    /// </summary>
    /// <param name="primaryLotId">Primary wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronologically ordered list of <see cref="LotNeutralizationEventRecord"/> rows.</returns>
    Task<IReadOnlyList<LotNeutralizationEventRecord>> ListNeutralizationEventsByLotAsync(
        string primaryLotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically erases all patient-owned neutralization events and reversal antidotes for GDPR Article 17 compliance.
    /// </summary>
    /// <param name="patientId">Patient identifier whose records should be deleted.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Total count of deleted rows across neutralization events and reversal antidotes.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);
}
