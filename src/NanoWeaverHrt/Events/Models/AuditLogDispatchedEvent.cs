namespace NanoWeaverHrt.Events.Models;

/// <summary>
/// Event emitted when an FDA 21 CFR Part 11 compliant audit record is cryptographically signed and committed to the ledger.
/// </summary>
/// <param name="Sequence">The monotonically increasing sequence index of the audit record.</param>
/// <param name="Operator">The operator, user, or system service executing the auditable transaction.</param>
/// <param name="Action">The regulatory action description (e.g. IngestScaffold, EncryptRecord, CryptoShred).</param>
/// <param name="Resource">The targeted biomedical, configuration, or user identity resource.</param>
/// <param name="CurrentHash">The SHA-384 cryptographic digest linking this record to its predecessor.</param>
public sealed record AuditLogDispatchedEvent(
    long Sequence,
    string Operator,
    string Action,
    string Resource,
    string CurrentHash) : IWeaverEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string EventName => "AuditLogDispatched";
}
