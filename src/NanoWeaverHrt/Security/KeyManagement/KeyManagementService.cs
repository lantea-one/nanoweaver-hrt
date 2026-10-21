using System.Collections.Concurrent;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Production implementation of <see cref="IKeyManagementService"/> managing post-quantum master keys,
/// subject-specific key encapsulation keys, and GDPR Article 17 crypto-shredding.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The key coordinator managing lattice key pairs and subject erasure lifecycles.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Guarantees that sensitive health records can be rendered mathematically unreadable
/// on demand (Right to Erasure) without impacting other patients or system stability.
/// </para>
/// <para>
/// <strong>How it works:</strong> Holds node master keys and manages subject key maps using thread-safe collections.
/// </para>
/// </remarks>
public sealed class KeyManagementService : IKeyManagementService
{
    /// <summary>
    /// Post-quantum cryptographic provider for key generation.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// The node's authoritative ML-DSA-65 signing key pair.
    /// </summary>
    private readonly PqcKeyMaterial _nodeSigningKey;

    /// <summary>
    /// The node's master ML-KEM-768 encryption key pair.
    /// </summary>
    private readonly PqcKeyMaterial _nodeMasterKey;

    /// <summary>
    /// Thread-safe map of subject identifiers to their cryptographic key profiles.
    /// </summary>
    private readonly ConcurrentDictionary<string, SubjectKeyProfile> _subjectProfiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyManagementService"/> class.
    /// </summary>
    /// <param name="cryptoProvider">The post-quantum cryptographic algorithm provider.</param>
    public KeyManagementService(IPostQuantumCryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        _cryptoProvider = cryptoProvider;

        // WHAT: Generate or initialize node-level post-quantum identities.
        // WHY:  Node requires an authoritative ML-DSA-65 key to sign Part 11 audit records and an ML-KEM-768 key for node storage.
        // HOW:  Calls IPostQuantumCryptoProvider.GenerateSigningKeyPair and GenerateKemKeyPair.
        _nodeSigningKey = _cryptoProvider.GenerateSigningKeyPair("node_signing_master");
        _nodeMasterKey = _cryptoProvider.GenerateKemKeyPair("node_storage_master");
    }

    /// <summary>
    /// Gets the node's authoritative ML-DSA-65 signing key pair for FDA 21 CFR Part 11 audit trails and manifests.
    /// </summary>
    /// <returns>The node signing key pair.</returns>
    public PqcKeyMaterial GetNodeSigningKey() => _nodeSigningKey;

    /// <summary>
    /// Gets the node's master ML-KEM-768 key pair for infrastructure and dataset envelope encryption.
    /// </summary>
    /// <returns>The node master encryption key pair.</returns>
    public PqcKeyMaterial GetNodeMasterKey() => _nodeMasterKey;

    /// <summary>
    /// Gets an existing subject key profile or generates a new one if it does not already exist.
    /// </summary>
    /// <param name="subjectId">The unique pseudonymized subject identifier.</param>
    /// <returns>The active <see cref="SubjectKeyProfile"/>.</returns>
    public SubjectKeyProfile GetOrCreateSubjectKey(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        // WHAT: Retrieve existing subject profile or atomically generate a new one.
        // WHY:  Ensures that each patient has an isolated ML-KEM key pair for envelope encryption.
        // HOW:  ConcurrentDictionary.GetOrAdd ensures idempotent key generation.
        return _subjectProfiles.GetOrAdd(subjectId, id =>
        {
            PqcKeyMaterial keyMaterial = _cryptoProvider.GenerateKemKeyPair($"subj_kem_{id}");
            return new SubjectKeyProfile
            {
                SubjectId = id,
                KeyMaterial = keyMaterial,
                IsShredded = false,
                ShreddedAtUtc = null,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        });
    }

    /// <summary>
    /// Gets the subject key profile for the specified identifier, or null if not found.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The profile if found; otherwise, null.</returns>
    public SubjectKeyProfile? GetSubjectKey(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        _subjectProfiles.TryGetValue(subjectId, out SubjectKeyProfile? profile);
        return profile;
    }

    /// <summary>
    /// Permanently destroys (Crypto-Shreds) the subject's private decryption keys pursuant to GDPR Article 17.
    /// </summary>
    /// <param name="subjectId">The subject identifier whose private key material will be destroyed.</param>
    /// <returns><c>true</c> if the subject was found and shredded; otherwise, <c>false</c>.</returns>
    public bool CryptoShredSubjectKey(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        if (!_subjectProfiles.TryGetValue(subjectId, out SubjectKeyProfile? existingProfile))
        {
            return false;
        }

        if (existingProfile.IsShredded)
        {
            return false;
        }

        // WHAT: Atomically overwrite subject profile with a shredded record lacking private key bytes.
        // WHY:  Without the private lattice key, all historical encrypted records become mathematically unrecoverable.
        // HOW:  Sets PrivateKeyBase64 to null, IsShredded = true, and records ShreddedAtUtc timestamp.
        PqcKeyMaterial shreddedMaterial = existingProfile.KeyMaterial with
        {
            PrivateKeyBase64 = null
        };

        SubjectKeyProfile shreddedProfile = existingProfile with
        {
            KeyMaterial = shreddedMaterial,
            IsShredded = true,
            ShreddedAtUtc = DateTimeOffset.UtcNow
        };

        _subjectProfiles[subjectId] = shreddedProfile;
        return true;
    }

    /// <summary>
    /// Returns a list of all known subject identifiers and their crypto-shredded status.
    /// </summary>
    /// <returns>A read-only list of active and shredded subject profiles.</returns>
    public IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles()
    {
        return _subjectProfiles.Values.ToList();
    }
}
