namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Persistence contract for the FDA 21 CFR Part 11 append-only cryptographic audit ledger.
/// </summary>
public interface IAuditLedgerRepository
{
    /// <summary>
    /// Loads all committed audit records ordered by ascending <see cref="AuditRecord.SequenceNumber"/>.
    /// </summary>
    /// <returns>The ordered list of persisted <see cref="AuditRecord"/> entries.</returns>
    IReadOnlyList<AuditRecord> LoadAll();

    /// <summary>
    /// Appends a single <see cref="AuditRecord"/> to the persistent store if its sequence number is not already present.
    /// </summary>
    /// <param name="record">The signed audit record to persist.</param>
    void Append(AuditRecord record);
}
