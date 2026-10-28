using NanoWeaverHrt.Security.KeyManagement;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Wraps a real <see cref="SubjectKeyRepository"/> with configurable per-operation fault injection switches
/// so tests can deterministically exercise database failure paths without mocking any <c>Db*</c> primitive.
/// </summary>
internal sealed class FaultInjectingSubjectKeyRepository : ISubjectKeyRepository
{
    /// <summary>
    /// The real underlying database repository.
    /// </summary>
    private readonly SubjectKeyRepository _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultInjectingSubjectKeyRepository"/> class.
    /// </summary>
    /// <param name="inner">The real repository to forward non-faulted calls to.</param>
    public FaultInjectingSubjectKeyRepository(SubjectKeyRepository inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Gets or sets the exception thrown before <see cref="ListAllAsync"/> executes, or <c>null</c> for normal operation.
    /// </summary>
    public Exception? ListAllFault { get; set; }

    /// <summary>
    /// Gets or sets the exception thrown before <see cref="GetRequiredAsync"/> executes, or <c>null</c> for normal operation.
    /// </summary>
    public Exception? GetRequiredFault { get; set; }

    /// <summary>
    /// Gets or sets the exception thrown before <see cref="TryInsertActiveAsync"/> executes, or <c>null</c> for normal operation.
    /// </summary>
    public Exception? TryInsertActiveFault { get; set; }

    /// <summary>
    /// Gets or sets the exception thrown before <see cref="MarkShreddedAsync"/> executes, or <c>null</c> for normal operation.
    /// </summary>
    public Exception? MarkShreddedFault { get; set; }

    /// <summary>
    /// Gets or sets the exception thrown before <see cref="CompactStorageAsync"/> executes, or <c>null</c> for normal operation.
    /// </summary>
    public Exception? CompactStorageFault { get; set; }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SubjectKeyRow>> ListAllAsync(CancellationToken ct = default)
    {
        if (ListAllFault is not null)
        {
            throw ListAllFault;
        }

        return _inner.ListAllAsync(ct);
    }

    /// <inheritdoc/>
    public Task<SubjectKeyRow> GetRequiredAsync(string subjectKey, CancellationToken ct = default)
    {
        if (GetRequiredFault is not null)
        {
            throw GetRequiredFault;
        }

        return _inner.GetRequiredAsync(subjectKey, ct);
    }

    /// <inheritdoc/>
    public Task<bool> TryInsertActiveAsync(SubjectKeyRow row, CancellationToken ct = default)
    {
        if (TryInsertActiveFault is not null)
        {
            throw TryInsertActiveFault;
        }

        return _inner.TryInsertActiveAsync(row, ct);
    }

    /// <inheritdoc/>
    public Task<bool> MarkShreddedAsync(string subjectKey, DateTimeOffset shreddedAtUtc, CancellationToken ct = default)
    {
        if (MarkShreddedFault is not null)
        {
            throw MarkShreddedFault;
        }

        return _inner.MarkShreddedAsync(subjectKey, shreddedAtUtc, ct);
    }

    /// <inheritdoc/>
    public Task CompactStorageAsync(CancellationToken ct = default)
    {
        if (CompactStorageFault is not null)
        {
            throw CompactStorageFault;
        }

        return _inner.CompactStorageAsync(ct);
    }

    /// <inheritdoc/>
    public Task<bool> TryInsertTombstoneAsync(string subjectKey, string subjectId, DateTimeOffset shreddedAtUtc, CancellationToken ct = default) =>
        _inner.TryInsertTombstoneAsync(subjectKey, subjectId, shreddedAtUtc, ct);
}
