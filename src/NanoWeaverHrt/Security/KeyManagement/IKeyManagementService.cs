using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Service contract managing the persistent node ML-DSA-65 signing key, durable subject-level key encapsulation keys,
/// and GDPR Article 17 cryptographic erasure (Crypto-Shredding).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The central key lifecycle and cryptographic key management service.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces cryptographic key segregation, persists the node's audit signing key and
/// sealed per-subject keys across restarts, and enables permanent, irreversible data erasure without requiring
/// destructive bulk database drops.
/// </para>
/// <para>
/// <strong>How it works:</strong> Holds the node's persistent ML-DSA-65 signing key pair in private memory (exposing only
/// the public key and a signing operation) and delegates per-subject ML-KEM-768 profiles to
/// <see cref="SubjectKeyVault"/>, providing atomic destruction of subject private keys on demand.
/// </para>
/// </remarks>
public interface IKeyManagementService
{
    /// <summary>
    /// Gets the public half of the node's authoritative ML-DSA-65 signing key pair for FDA 21 CFR Part 11 audit
    /// trails and dataset export manifests. The returned <see cref="PqcKeyMaterial.PrivateKeyBase64"/> is always <c>null</c>.
    /// </summary>
    /// <returns>The node's public signing key descriptor with <c>PrivateKeyBase64</c> set to <c>null</c>.</returns>
    PqcKeyMaterial GetNodeSigningKey();

    /// <summary>
    /// Gets an existing active subject key profile or generates, seals, and persists a new one if it does not already exist.
    /// </summary>
    /// <param name="subjectId">The unique pseudonymized subject identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active <see cref="SubjectKeyProfile"/>.</returns>
    /// <exception cref="SubjectKeyUnavailableException">The subject was previously crypto-shredded.</exception>
    Task<SubjectKeyProfile> GetOrCreateSubjectKeyAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Gets the subject key profile for the specified identifier, or <c>null</c> if not found.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The profile if found; otherwise, <c>null</c>.</returns>
    SubjectKeyProfile? GetSubjectKey(string subjectId);

    /// <summary>
    /// Permanently destroys (Crypto-Shreds) the subject's stored private decryption key pursuant to GDPR Article 17.
    /// </summary>
    /// <param name="subjectId">The subject identifier whose private key material will be destroyed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="SubjectErasureOutcome"/> describing whether the key was erased, already erased, or unknown.</returns>
    Task<SubjectErasureOutcome> CryptoShredSubjectKeyAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Returns a list of all known subject identifiers and their crypto-shredded status with private key material omitted.
    /// </summary>
    /// <returns>A read-only list of active and shredded subject profiles with <c>PrivateKeyBase64</c> set to <c>null</c>.</returns>
    IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles();

    /// <summary>
    /// Retrieves the subject's unsealed Base64 private key for envelope decryption, or throws <see cref="SubjectKeyUnavailableException"/>.
    /// </summary>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <returns>The Base64-encoded ML-KEM-768 private key.</returns>
    /// <exception cref="SubjectKeyUnavailableException">The subject is unknown on this node or has been crypto-shredded.</exception>
    string GetSubjectPrivateKeyForDecryption(string subjectId);

    /// <summary>
    /// Gets the provenance of the node ML-DSA-65 signing key for this process start (<see cref="NodeSecretOrigin.GeneratedFile"/>
    /// on first launch or <see cref="NodeSecretOrigin.ExistingFile"/> on subsequent starts).
    /// </summary>
    NodeSecretOrigin NodeSigningKeyOrigin { get; }

    /// <summary>
    /// Signs raw bytes with the node's persistent ML-DSA-65 signing key without exposing the private key outside this service.
    /// </summary>
    /// <param name="data">The raw byte span to sign.</param>
    /// <returns>A <see cref="PqcSignature"/> whose <c>SignerKeyId</c> equals <c>GetNodeSigningKey().KeyId</c>.</returns>
    PqcSignature SignWithNodeSigningKey(ReadOnlySpan<byte> data);
}
