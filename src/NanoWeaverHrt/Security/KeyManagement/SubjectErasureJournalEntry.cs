namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Represents a single GDPR Article 17 subject key erasure record persisted in <c>subject_erasures.journal</c>.
/// </summary>
/// <param name="SubjectKey">The normalized uppercase subject key that was erased.</param>
/// <param name="ShreddedAtUtc">The UTC timestamp when the erasure was recorded.</param>
public readonly record struct SubjectErasureJournalEntry(string SubjectKey, DateTimeOffset ShreddedAtUtc);
