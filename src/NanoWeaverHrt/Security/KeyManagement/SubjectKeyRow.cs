namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Represents a single persisted row in the <c>weaver_subject_keys</c> database table.
/// </summary>
public sealed record SubjectKeyRow
{
    /// <summary>
    /// Gets the normalized uppercase primary key (<c>subjectId.ToUpperInvariant()</c>).
    /// </summary>
    public required string SubjectKey { get; init; }

    /// <summary>
    /// Gets the subject identifier in the casing first presented when the row was created.
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>
    /// Gets the post-quantum key identifier, or <c>null</c> only on journal-rebuilt tombstones.
    /// </summary>
    public string? KeyId { get; init; }

    /// <summary>
    /// Gets the post-quantum key encapsulation algorithm name (<c>ML-KEM-768</c>).
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Gets the Base64-encoded public key, or <c>null</c> if and only if <see cref="IsShredded"/> is <c>true</c>.
    /// </summary>
    public string? PublicKeyBase64 { get; init; }

    /// <summary>
    /// Gets the AES-256-GCM sealed private key envelope, or <c>null</c> if and only if <see cref="IsShredded"/> is <c>true</c>.
    /// </summary>
    public string? SealedPrivateKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether the subject's key material has been permanently erased.
    /// </summary>
    public required bool IsShredded { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the subject key row was created.
    /// </summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the subject key was crypto-shredded, or <c>null</c> when active.
    /// </summary>
    public DateTimeOffset? ShreddedAtUtc { get; init; }
}
