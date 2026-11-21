using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for cold-chain thermal excursion logs (<c>weaver_lot_thermal_excursions</c>)
/// and SHA-256 hash-chained physical custody handoffs (<c>weaver_lot_custody_handoffs</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Dialect-neutral data-access interface supporting chronological appends
/// and lot-scoped queries across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P210): Decouples Arrhenius stability modeling and custody
/// verification from physical SQL database engines while cooperating with patient-level GDPR Article 17 erasures.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="ColdChainCustodyRepository"/> using
/// <see cref="NanoWeaverHrt.Database.Access.IWeaverDbConnectionFactory"/>.
/// </para>
/// </remarks>
public interface IColdChainCustodyRepository
{
    /// <summary>
    /// Appends a new cold-chain thermal excursion log entry for a fabrication lot.
    /// </summary>
    /// <param name="record">The validated thermal excursion record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task AddThermalExcursionAsync(
        ThermalExcursionRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all thermal excursion log entries recorded for a fabrication lot in strict chronological order.
    /// </summary>
    /// <param name="lotId">The target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronologically ordered list of <see cref="ThermalExcursionRecord"/> entries.</returns>
    Task<IReadOnlyList<ThermalExcursionRecord>> ListThermalExcursionsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new SHA-256 hash-chained physical custody handoff entry for a fabrication lot.
    /// </summary>
    /// <param name="record">The validated custody handoff record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task AddCustodyHandoffAsync(
        CustodyHandoffRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all physical custody handoff entries recorded for a fabrication lot in strict chronological order.
    /// </summary>
    /// <param name="lotId">The target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronologically ordered list of <see cref="CustodyHandoffRecord"/> entries.</returns>
    Task<IReadOnlyList<CustodyHandoffRecord>> ListCustodyHandoffsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default);
}
