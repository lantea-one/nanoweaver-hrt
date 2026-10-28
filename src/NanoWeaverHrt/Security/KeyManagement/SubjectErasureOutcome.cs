namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Describes the outcome of a GDPR Article 17 cryptographic erasure (crypto-shredding) request for a subject.
/// </summary>
public enum SubjectErasureOutcome
{
    /// <summary>
    /// The subject had an active key on this node and its private key material was destroyed.
    /// </summary>
    Erased,

    /// <summary>
    /// The subject was already crypto-shredded on an earlier request; nothing further was destroyed.
    /// </summary>
    AlreadyErased,

    /// <summary>
    /// No key was ever stored on this node for the specified subject identifier.
    /// </summary>
    UnknownSubject
}
