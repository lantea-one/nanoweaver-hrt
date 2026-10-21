using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Service contract managing node master keys, subject-level key encapsulation keys,
/// and GDPR Article 17 cryptographic erasure (Crypto-Shredding).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The central key lifecycle and cryptographic key management service.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces cryptographic key segregation and enables permanent, irreversible data erasure
/// without requiring destructive bulk database drops.
/// </para>
/// <para>
/// <strong>How it works:</strong> Maintains node master KEKs and per-subject ML-KEM profiles, providing atomic destruction
/// of subject private keys on demand.
/// </para>
/// </remarks>
public interface IKeyManagementService
{
    /// <summary>
    /// Gets the node's authoritative ML-DSA-65 signing key pair for FDA 21 CFR Part 11 audit trails and manifests.
    /// </summary>
    /// <returns>The node signing key pair.</returns>
    PqcKeyMaterial GetNodeSigningKey();

    /// <summary>
    /// Gets the node's master ML-KEM-768 key pair for infrastructure and dataset envelope encryption.
    /// </summary>
    /// <returns>The node master encryption key pair.</returns>
    PqcKeyMaterial GetNodeMasterKey();

    /// <summary>
    /// Gets an existing subject key profile or generates a new one if it does not already exist.
    /// </summary>
    /// <param name="subjectId">The unique pseudonymized subject identifier.</param>
    /// <returns>The active <see cref="SubjectKeyProfile"/>.</returns>
    SubjectKeyProfile GetOrCreateSubjectKey(string subjectId);

    /// <summary>
    /// Gets the subject key profile for the specified identifier, or null if not found.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The profile if found; otherwise, null.</returns>
    SubjectKeyProfile? GetSubjectKey(string subjectId);

    /// <summary>
    /// Permanently destroys (Crypto-Shreds) the subject's private decryption keys pursuant to GDPR Article 17.
    /// </summary>
    /// <param name="subjectId">The subject identifier whose private key material will be destroyed.</param>
    /// <returns><c>true</c> if the subject was found and shredded; otherwise, <c>false</c>.</returns>
    bool CryptoShredSubjectKey(string subjectId);

    /// <summary>
    /// Returns a list of all known subject identifiers and their crypto-shredded status.
    /// </summary>
    /// <returns>A read-only list of active and shredded subject profiles.</returns>
    IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles();
}
