using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.Storage;

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
    /// Encrypts an arbitrary strongly-typed health or genomic record using the subject's persisted ML-KEM-768 key.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="record">The record instance to serialize and encrypt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An authenticated <see cref="EncryptedEnvelope"/> ready for storage.</returns>
    public async Task<EncryptedEnvelope> EncryptSubjectRecordAsync<T>(string subjectId, T record, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(record);

        // WHAT: Retrieve or persist the subject's active key profile, refusing if previously crypto-shredded.
        // WHY:  Prevents encrypting data to an unpersisted key or re-issuing a key after GDPR Article 17 erasure.
        // HOW:  Awaits IKeyManagementService.GetOrCreateSubjectKeyAsync, which throws SubjectKeyUnavailableException if erased.
        SubjectKeyProfile profile = await _keyManagement.GetOrCreateSubjectKeyAsync(subjectId, ct).ConfigureAwait(false);

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

        // WHAT: Resolve the subject's unsealed private decryption key from the vault.
        // WHY:  Distinguishes between an unknown subject and a deliberately crypto-shredded subject.
        // HOW:  Calls GetSubjectPrivateKeyForDecryption, which throws SubjectKeyUnavailableException with the exact reason.
        string privateKey = _keyManagement.GetSubjectPrivateKeyForDecryption(subjectId);

        // WHAT: Decrypt payload using ML-KEM-768 decapsulation and AES-256-GCM.
        // WHY:  Verifies authenticity tag and recovers plaintext.
        // HOW:  Calls IPostQuantumCryptoProvider.DecryptEnvelope.
        byte[] plaintextBytes = _cryptoProvider.DecryptEnvelope(envelope, privateKey);

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

        SubjectKeyProfile profile = await _keyManagement.GetOrCreateSubjectKeyAsync(subjectId, cancellationToken).ConfigureAwait(false);

        // WHAT: Sanitize trajectory file name to prevent directory traversal.
        // WHY:  Ensures that callers cannot reference arbitrary files outside the shared data volume.
        // HOW:  Uses Path.GetFileName to extract only the safe file name.
        string safeFileName = Path.GetFileName(trajectoryFileName);
        string sourcePath = Path.Combine(_sharedDataPath, safeFileName);
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

        PqcSignature manifestSignature = _keyManagement.SignWithNodeSigningKey(
            Encoding.UTF8.GetBytes(envelopeHashHex));

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
