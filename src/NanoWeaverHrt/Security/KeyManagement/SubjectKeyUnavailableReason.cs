namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Identifies why a subject's ML-KEM-768 key is unavailable for encryption, decryption, or dataset export.
/// </summary>
public enum SubjectKeyUnavailableReason
{
    /// <summary>
    /// The subject's private key was deliberately destroyed pursuant to GDPR Article 17 erasure.
    /// </summary>
    Erased,

    /// <summary>
    /// No key was ever stored on this node for the requested subject identifier.
    /// </summary>
    UnknownSubject
}
