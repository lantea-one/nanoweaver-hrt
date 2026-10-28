namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Reports the counts of active and crypto-shredded subject keys loaded during <see cref="SubjectKeyVault.InitializeAsync"/>.
/// </summary>
public sealed record SubjectKeyLoadReport
{
    /// <summary>
    /// Gets the number of active subject keys opened and verified at startup.
    /// </summary>
    public int ActiveCount { get; init; }

    /// <summary>
    /// Gets the number of crypto-shredded subject tombstones loaded at startup.
    /// </summary>
    public int ErasedCount { get; init; }

    /// <summary>
    /// Gets the number of active database rows that were re-shredded at startup because the erasure journal recorded them as erased.
    /// </summary>
    public int ReappliedErasureCount { get; init; }

    /// <summary>
    /// Gets the number of missing tombstone rows rebuilt in the database at startup from the erasure journal.
    /// </summary>
    public int RebuiltTombstoneCount { get; init; }

    /// <summary>
    /// Gets the number of database tombstone rows that were backfilled into the erasure journal at startup.
    /// </summary>
    public int JournalBackfillCount { get; init; }
}
