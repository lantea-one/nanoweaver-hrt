using System.Security.Cryptography;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Exception thrown when a cryptographic operation cannot proceed because a subject's key was deliberately
/// crypto-shredded or was never stored on this node.
/// </summary>
public sealed class SubjectKeyUnavailableException : CryptographicException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyUnavailableException"/> class.
    /// </summary>
    /// <param name="subjectId">The subject identifier requested by the caller.</param>
    /// <param name="reason">The reason the subject key is unavailable.</param>
    /// <param name="shreddedAtUtc">The UTC erasure timestamp when <paramref name="reason"/> is <see cref="SubjectKeyUnavailableReason.Erased"/>; otherwise <c>null</c>.</param>
    internal SubjectKeyUnavailableException(
        string subjectId,
        SubjectKeyUnavailableReason reason,
        DateTimeOffset? shreddedAtUtc)
        : base(reason == SubjectKeyUnavailableReason.Erased
            // Erased reason always supplies a non-null shreddedAtUtc timestamp.
            ? ErasedMessage(subjectId, shreddedAtUtc!.Value)
            : UnknownSubjectMessage(subjectId))
    {
        Reason = reason;
        ShreddedAtUtc = shreddedAtUtc;
    }

    /// <summary>
    /// Gets the reason why the subject key is unavailable.
    /// </summary>
    public SubjectKeyUnavailableReason Reason { get; }

    /// <summary>
    /// Gets the UTC timestamp at which the subject key was erased, or <c>null</c> when <see cref="Reason"/> is <see cref="SubjectKeyUnavailableReason.UnknownSubject"/>.
    /// </summary>
    public DateTimeOffset? ShreddedAtUtc { get; }

    /// <summary>
    /// Formats the client-safe diagnostic message for a deliberately crypto-shredded subject.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <param name="shreddedAtUtc">The UTC timestamp when the key was erased.</param>
    /// <returns>The formatted client-safe error message.</returns>
    internal static string ErasedMessage(string subjectId, DateTimeOffset shreddedAtUtc) =>
        $"Subject '{subjectId}' was crypto-shredded at {shreddedAtUtc:O}. This node deliberately destroyed the subject's private decryption key (GDPR Article 17 erasure), so records encrypted to that key can no longer be decrypted and no new key will be issued for this subject identifier.";

    /// <summary>
    /// Formats the client-safe diagnostic message for a subject with no key stored on this node.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The formatted client-safe error message.</returns>
    internal static string UnknownSubjectMessage(string subjectId) =>
        $"This node holds no stored key for subject '{subjectId}'. No erasure took place: a key for this subject was never persisted on this node. Records encrypted before subject keys were persisted, or by another node, cannot be decrypted here.";
}
