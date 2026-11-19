using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for patient-specific wet-lab nanocarrier fabrication lots and 5-gate
/// pharmaceutical quality control verification measurements.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational data access contract governing creation, status transitions,
/// QC check logging, certificate metadata persistence, and GDPR Article 17 erasure for physical fabrication batches.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Decouples the wet-lab release service, Minimal API endpoints, and sovereign UI
/// from underlying SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Provides asynchronous CRUD and cascade-delete methods backed by dialect-neutral
/// parameterized SQL queries.
/// </para>
/// </remarks>
public interface IFabricationLotRepository
{
    /// <summary>
    /// Inserts or updates a patient-specific wet-lab nanocarrier fabrication lot record.
    /// </summary>
    /// <param name="lot">The fabrication lot record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="FabricationLotRecord"/> (with an assigned <c>LotId</c> if omitted).</returns>
    Task<FabricationLotRecord> UpsertLotAsync(
        FabricationLotRecord lot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single wet-lab fabrication lot by its unique identifier.
    /// </summary>
    /// <param name="lotId">The unique fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The matching <see cref="FabricationLotRecord"/> if found; otherwise, <see langword="null"/>.</returns>
    Task<FabricationLotRecord?> GetLotByIdAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all wet-lab fabrication lots belonging to a specific patient ordered newest first.
    /// </summary>
    /// <param name="patientId">The patient identifier whose lots should be retrieved.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>A read-only list of fabrication lot records ordered descending by creation timestamp.</returns>
    Task<IReadOnlyList<FabricationLotRecord>> GetLotsByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a single wet-lab pharmaceutical quality control check measurement against a lot.
    /// </summary>
    /// <param name="check">The QC check record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="FabricationQcCheckRecord"/> (with an assigned <c>CheckId</c> if omitted).</returns>
    Task<FabricationQcCheckRecord> RecordQcCheckAsync(
        FabricationQcCheckRecord check,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all pharmaceutical wet-lab QC checks recorded against a specific lot in chronological order.
    /// </summary>
    /// <param name="lotId">The parent fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>A read-only list of QC check records ordered ascending by recording timestamp.</returns>
    Task<IReadOnlyList<FabricationQcCheckRecord>> GetQcChecksByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all wet-lab fabrication lots and associated QC checks belonging to a specific patient.
    /// </summary>
    /// <param name="patientId">The patient identifier whose fabrication lots and QC records should be erased.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The total number of database rows deleted across revocations, QC checks, and fabrication lots.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a permanent revocation tombstone for a wet-lab fabrication lot.
    /// </summary>
    /// <param name="revocation">The revocation record containing lot ID, revoking administrator, and clinical reason.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="FabricationLotRevocationRecord"/>.</returns>
    Task<FabricationLotRevocationRecord> RecordLotRevocationAsync(
        FabricationLotRevocationRecord revocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the permanent revocation record for a fabrication lot if it has been revoked.
    /// </summary>
    /// <param name="lotId">The unique fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The <see cref="FabricationLotRevocationRecord"/> if revoked; otherwise, <see langword="null"/>.</returns>
    Task<FabricationLotRevocationRecord?> GetLotRevocationAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all local permanent lot revocation tombstones ordered newest first for signed revocation feed export.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>Read-only list of local <see cref="FabricationLotRevocationRecord"/> entries.</returns>
    Task<IReadOnlyList<FabricationLotRevocationRecord>> ListLocalRevocationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a verified upstream peer lot revocation tombstone into <c>weaver_federated_lot_revocations</c>.
    /// </summary>
    /// <param name="record">The verified federated lot revocation record to upsert.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The persisted <see cref="FederatedLotRevocationRecord"/>.</returns>
    Task<FederatedLotRevocationRecord> UpsertFederatedRevocationAsync(
        FederatedLotRevocationRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a verified federated peer revocation tombstone for the specified lot identifier, if present.
    /// </summary>
    /// <param name="lotId">The unique fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>The matching <see cref="FederatedLotRevocationRecord"/> if recalled upstream; otherwise <see langword="null"/>.</returns>
    Task<FederatedLotRevocationRecord?> GetFederatedRevocationAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all verified federated peer lot revocation tombstones ordered newest imported first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for cancelling the operation.</param>
    /// <returns>Read-only list of <see cref="FederatedLotRevocationRecord"/> entries.</returns>
    Task<IReadOnlyList<FederatedLotRevocationRecord>> ListFederatedRevocationsAsync(
        CancellationToken cancellationToken = default);
}
