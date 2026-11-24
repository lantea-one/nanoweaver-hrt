using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for post-implantation clinical adverse events (<c>weaver_lot_adverse_events</c>)
/// and CARPA/TLR9 immunogenicity laboratory assays (<c>weaver_lot_immunogenicity_assays</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Dialect-neutral data-access interface supporting chronological appends
/// and lot-scoped queries across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P215): Decouples pharmacovigilance surveillance and automated
/// quarantine escalation from physical SQL engines while cooperating with patient-level GDPR Article 17 erasures.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="ClinicalPharmacovigilanceRepository"/> using
/// <see cref="NanoWeaverHrt.Database.Access.IWeaverDbConnectionFactory"/>.
/// </para>
/// </remarks>
public interface IClinicalPharmacovigilanceRepository
{
    /// <summary>
    /// Appends a new CTCAE v5.0 post-implantation adverse event record for a fabrication lot.
    /// </summary>
    /// <param name="record">The validated adverse event record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task AddAdverseEventAsync(
        LotAdverseEventRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all post-implantation adverse events recorded for a fabrication lot in strict chronological order.
    /// </summary>
    /// <param name="lotId">The target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronologically ordered list of <see cref="LotAdverseEventRecord"/> entries.</returns>
    Task<IReadOnlyList<LotAdverseEventRecord>> ListAdverseEventsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new CARPA complement, cytokine, and anti-DNA immunogenicity assay panel for a fabrication lot.
    /// </summary>
    /// <param name="record">The validated immunogenicity assay record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task AddImmunogenicityAssayAsync(
        LotImmunogenicityAssayRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all immunogenicity assay panels recorded for a fabrication lot in strict chronological order.
    /// </summary>
    /// <param name="lotId">The target wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Chronologically ordered list of <see cref="LotImmunogenicityAssayRecord"/> entries.</returns>
    Task<IReadOnlyList<LotImmunogenicityAssayRecord>> ListImmunogenicityAssaysByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default);
}
