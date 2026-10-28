namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Persistence contract for storing, loading, and crypto-shredding per-subject ML-KEM-768 keys in <c>weaver_subject_keys</c>.
/// </summary>
public interface ISubjectKeyRepository
{
    /// <summary>
    /// Lists every subject key row ordered ascending by <see cref="SubjectKeyRow.SubjectKey"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All persisted active and shredded subject key rows.</returns>
    Task<IReadOnlyList<SubjectKeyRow>> ListAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads a single required subject key row by its normalized primary key.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="SubjectKeyRow"/>.</returns>
    /// <exception cref="InvalidOperationException">No row exists for <paramref name="subjectKey"/>.</exception>
    Task<SubjectKeyRow> GetRequiredAsync(string subjectKey, CancellationToken ct = default);

    /// <summary>
    /// Attempts to insert a new active subject key row without overwriting any existing active or tombstone row.
    /// </summary>
    /// <param name="row">The active subject key row to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if and only if a new row was inserted; <c>false</c> if a row for <see cref="SubjectKeyRow.SubjectKey"/> already existed.</returns>
    /// <exception cref="ArgumentException"><paramref name="row"/> is marked shredded or is missing a required key column.</exception>
    Task<bool> TryInsertActiveAsync(SubjectKeyRow row, CancellationToken ct = default);

    /// <summary>
    /// Atomically transitions an active subject key row into a permanent tombstone with <c>NULL</c> key columns.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase primary key to erase.</param>
    /// <param name="shreddedAtUtc">The UTC timestamp of the cryptographic erasure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if and only if an active row was transitioned to a tombstone; <c>false</c> if the row was absent or already shredded.</returns>
    Task<bool> MarkShreddedAsync(string subjectKey, DateTimeOffset shreddedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Compacts the underlying database storage to purge freed pages after a key erasure.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when storage compaction finishes.</returns>
    Task CompactStorageAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to insert a journal-rebuilt tombstone row when the database lacks any row for an erased subject.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase primary key.</param>
    /// <param name="subjectId">The subject identifier to store on the tombstone.</param>
    /// <param name="shreddedAtUtc">The UTC timestamp from the erasure journal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if and only if a new tombstone row was inserted; <c>false</c> if a row already existed.</returns>
    Task<bool> TryInsertTombstoneAsync(string subjectKey, string subjectId, DateTimeOffset shreddedAtUtc, CancellationToken ct = default);
}
