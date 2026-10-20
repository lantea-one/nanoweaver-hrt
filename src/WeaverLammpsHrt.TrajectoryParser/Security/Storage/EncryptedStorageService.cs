using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Compliance;
using WeaverLammpsHrt.TrajectoryParser.Security.Cryptography;
using WeaverLammpsHrt.TrajectoryParser.Security.KeyManagement;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Storage;

/// <summary>
/// Production implementation of <see cref="IEncryptedStorageService"/> providing field-level and file-level
/// post-quantum envelope encryption at rest, integrated with FDA 21 CFR Part 11 audit logging.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The storage encryption service encrypting data assets at rest using NIST FIPS 203 &amp; 204.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces HIPAA § 164.312(a)(2)(iv) and GDPR Article 32 at-rest encryption requirements.
/// </para>
/// <para>
/// <strong>How it works:</strong> Wraps individual subject records and trajectory bundles in AES-256-GCM authenticated ciphertexts
/// with ML-KEM-768 encapsulated keys and ML-DSA-65 signed provenance manifests.
/// </para>
/// </remarks>
public sealed class EncryptedStorageService : IEncryptedStorageService
{
    /// <summary>
    /// Key management service for subject key profiles and crypto-shredding status.
    /// </summary>
    private readonly IKeyManagementService _keyManagement;

    /// <summary>
    /// Post-quantum cryptographic algorithm provider.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Compliance audit service for FDA 21 CFR Part 11 hash-chained audit trails.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Path to shared simulation data volume.
    /// </summary>
    private readonly string _sharedDataPath;

    /// <summary>
    /// Standard JSON serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedStorageService"/> class.
    /// </summary>
    /// <param name="keyManagement">Key management service.</param>
    /// <param name="cryptoProvider">Post-quantum crypto provider.</param>
    /// <param name="auditService">Compliance audit service.</param>
    /// <param name="configuration">Application configuration.</param>
    public EncryptedStorageService(
        IKeyManagementService keyManagement,
        IPostQuantumCryptoProvider cryptoProvider,
        IComplianceAuditService auditService,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(keyManagement);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(auditService);
        ArgumentNullException.ThrowIfNull(configuration);

        _keyManagement = keyManagement;
        _cryptoProvider = cryptoProvider;
        _auditService = auditService;

        _sharedDataPath = configuration["TRAJECTORY_INCOMING_DIR"]
            ?? configuration["SharedDataPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
    }

    /// <summary>
    /// Encrypts an arbitrary strongly-typed health or genomic record using the subject's ML-KEM-768 key.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="record">The record instance to serialize and encrypt.</param>
    /// <returns>An authenticated <see cref="EncryptedEnvelope"/> ready for storage.</returns>
    public EncryptedEnvelope EncryptSubjectRecord<T>(string subjectId, T record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(record);

        // WHAT: Retrieve subject key profile and verify crypto-shredding status.
        // WHY:  Prevent storing data for a subject whose keys have been erased pursuant to GDPR Article 17.
        // HOW:  GetOrCreateSubjectKey checks IsShredded flag.
        SubjectKeyProfile profile = _keyManagement.GetOrCreateSubjectKey(subjectId);
        if (profile.IsShredded)
        {
            throw new CryptographicException($"Cannot encrypt record for subject '{subjectId}': Profile was permanently crypto-shredded pursuant to GDPR Article 17.");
        }

        // WHAT: Serialize record to UTF-8 JSON bytes.
        // WHY:  Provides a standardized byte representation for encryption.
        // HOW:  JsonSerializer.SerializeToUtf8Bytes formats payload.
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(record, s_jsonOptions);

        // WHAT: Encrypt payload using post-quantum envelope encryption (ML-KEM-768 + AES-256-GCM).
        // WHY:  Provides quantum resistance and authenticated integrity.
        // HOW:  Calls IPostQuantumCryptoProvider.EncryptEnvelope.
        EncryptedEnvelope envelope = _cryptoProvider.EncryptEnvelope(jsonBytes, profile.KeyMaterial.PublicKeyBase64, subjectId);

        _auditService.LogAction(
            "ENCRYPTED_STORAGE",
            "EncryptSubjectRecord",
            $"subject:{subjectId}",
            $"{{\"recordType\":\"{typeof(T).Name}\",\"algorithm\":\"{envelope.Algorithm}\"}}");

        return envelope;
    }

    /// <summary>
    /// Decrypts an authenticated <see cref="EncryptedEnvelope"/> using the subject's private ML-KEM-768 key.
    /// </summary>
    /// <typeparam name="T">The expected record payload type.</typeparam>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="envelope">The encrypted envelope.</param>
    /// <returns>The deserialized record, or default if deserialization fails.</returns>
    public T? DecryptSubjectRecord<T>(string subjectId, EncryptedEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(envelope);

        // WHAT: Verify subject key availability and crypto-shredding status.
        // WHY:  If the subject's private key was destroyed, decryption is mathematically impossible (GDPR Art. 17).
        // HOW:  Inspects profile and private key existence.
        SubjectKeyProfile? profile = _keyManagement.GetSubjectKey(subjectId);
        if (profile == null || profile.IsShredded || string.IsNullOrWhiteSpace(profile.KeyMaterial.PrivateKeyBase64))
        {
            throw new CryptographicException($"Decryption refused for subject '{subjectId}': Private key has been permanently destroyed (Crypto-Shredded pursuant to GDPR Article 17).");
        }

        // WHAT: Decrypt payload using ML-KEM-768 decapsulation and AES-256-GCM.
        // WHY:  Verifies authenticity tag and recovers plaintext.
        // HOW:  Calls IPostQuantumCryptoProvider.DecryptEnvelope.
        byte[] plaintextBytes = _cryptoProvider.DecryptEnvelope(envelope, profile.KeyMaterial.PrivateKeyBase64);

        _auditService.LogAction(
            "ENCRYPTED_STORAGE",
            "DecryptSubjectRecord",
            $"subject:{subjectId}",
            $"{{\"recordType\":\"{typeof(T).Name}\",\"envelopeKeyId\":\"{envelope.KeyId}\"}}");

        return JsonSerializer.Deserialize<T>(plaintextBytes, s_jsonOptions);
    }

    /// <summary>
    /// Packages and envelope-encrypts an active trajectory file into a post-quantum protected export bundle signed with ML-DSA-65.
    /// </summary>
    /// <param name="trajectoryFileName">The filename of the trajectory inside shared data storage.</param>
    /// <param name="subjectId">The pseudonymized subject identifier receiving the export.</param>
    /// <param name="exportDirectory">The destination export directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task returning the absolute path to the generated encrypted export bundle.</returns>
    public async Task<string> ExportEncryptedDatasetBundleAsync(
        string trajectoryFileName,
        string subjectId,
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoryFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);

        SubjectKeyProfile profile = _keyManagement.GetOrCreateSubjectKey(subjectId);
        if (profile.IsShredded)
        {
            throw new CryptographicException($"Cannot export bundle for subject '{subjectId}': Subject has been crypto-shredded.");
        }

        string sourcePath = Path.Combine(_sharedDataPath, trajectoryFileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source trajectory file not found: {sourcePath}");
        }

        Directory.CreateDirectory(exportDirectory);

        // WHAT: Compress the trajectory file into a tar.gz byte stream.
        // WHY:  Reduces volume size before encryption.
        // HOW:  Streams file bytes through GZipStream into memory.
        using MemoryStream uncompressedStream = new();
        using (FileStream sourceFileStream = File.OpenRead(sourcePath))
        {
            await sourceFileStream.CopyToAsync(uncompressedStream, cancellationToken).ConfigureAwait(false);
        }
        byte[] rawBytes = uncompressedStream.ToArray();

        using MemoryStream compressedStream = new();
        using (GZipStream gzipStream = new(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzipStream.WriteAsync(rawBytes, cancellationToken).ConfigureAwait(false);
        }
        byte[] compressedBytes = compressedStream.ToArray();

        // WHAT: Encrypt the compressed payload using post-quantum envelope encryption (ML-KEM-768 + AES-256-GCM).
        // WHY:  Protects sensitive research trajectory coordinates at rest and during transit.
        // HOW:  EncryptEnvelope wraps AES key under subject's ML-KEM public key.
        EncryptedEnvelope envelope = _cryptoProvider.EncryptEnvelope(compressedBytes, profile.KeyMaterial.PublicKeyBase64, subjectId);

        // WHAT: Compute SHA-256 manifest hash and sign with node's ML-DSA-65 key.
        // WHY:  Guarantees cryptographic provenance and tamper-evident authentication.
        // HOW:  Serializes manifest and calls SignData.
        byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, s_jsonOptions);
        byte[] envelopeHash = SHA256.HashData(envelopeBytes);
        string envelopeHashHex = Convert.ToHexString(envelopeHash).ToLowerInvariant();

        PqcKeyMaterial nodeSigningKey = _keyManagement.GetNodeSigningKey();
        PqcSignature manifestSignature = _cryptoProvider.SignData(
            Encoding.UTF8.GetBytes(envelopeHashHex),
            nodeSigningKey.PrivateKeyBase64 ?? string.Empty,
            nodeSigningKey.KeyId);

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        string bundleFileName = $"{Path.GetFileNameWithoutExtension(trajectoryFileName)}_{subjectId}_{timestamp}.enc.tar.gz";
        string bundlePath = Path.Combine(exportDirectory, bundleFileName);

        await File.WriteAllBytesAsync(bundlePath, envelopeBytes, cancellationToken).ConfigureAwait(false);

        // Write PQC-signed provenance manifest
        string manifestFileName = $"{Path.GetFileNameWithoutExtension(trajectoryFileName)}_{subjectId}_{timestamp}.manifest.pqc.json";
        string manifestPath = Path.Combine(exportDirectory, manifestFileName);

        var manifestObj = new
        {
            bundleFileName,
            subjectId,
            sha256 = envelopeHashHex,
            algorithm = envelope.Algorithm,
            signatureAlgorithm = manifestSignature.Algorithm,
            signature = manifestSignature.SignatureBase64,
            signerKeyId = manifestSignature.SignerKeyId,
            createdAtUtc = DateTimeOffset.UtcNow
        };

        string manifestJson = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            "ENCRYPTED_STORAGE",
            "ExportEncryptedDatasetBundle",
            bundlePath,
            $"{{\"trajectory\":\"{trajectoryFileName}\",\"subject\":\"{subjectId}\",\"sha256\":\"{envelopeHashHex}\"}}");

        return bundlePath;
    }
}
