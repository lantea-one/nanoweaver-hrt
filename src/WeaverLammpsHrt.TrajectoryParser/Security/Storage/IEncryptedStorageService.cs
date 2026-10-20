using WeaverLammpsHrt.TrajectoryParser.Security.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Storage;

/// <summary>
/// Service contract providing authenticated post-quantum encryption at rest for health records,
/// genomic sequences, and trajectory simulation bundles.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The storage abstraction providing field-level and file-level post-quantum encryption at rest.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces HIPAA § 164.312(a)(2)(iv) and GDPR Article 32 at-rest encryption requirements
/// using quantum-resistant algorithms (NIST FIPS 203 ML-KEM-768 + AES-256-GCM).
/// </para>
/// <para>
/// <strong>How it works:</strong> Handles serialization, envelope encryption, and decryption with strict enforcement
/// of GDPR Article 17 crypto-shredding checks.
/// </para>
/// </remarks>
public interface IEncryptedStorageService
{
    /// <summary>
    /// Encrypts an arbitrary strongly-typed health or genomic record using the subject's ML-KEM-768 key.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="record">The record instance to serialize and encrypt.</param>
    /// <returns>An authenticated <see cref="EncryptedEnvelope"/> ready for storage.</returns>
    EncryptedEnvelope EncryptSubjectRecord<T>(string subjectId, T record);

    /// <summary>
    /// Decrypts an authenticated <see cref="EncryptedEnvelope"/> using the subject's private ML-KEM-768 key.
    /// </summary>
    /// <typeparam name="T">The expected record payload type.</typeparam>
    /// <param name="subjectId">The pseudonymized subject identifier.</param>
    /// <param name="envelope">The encrypted envelope.</param>
    /// <returns>The deserialized record, or default if deserialization fails.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown if the subject has been crypto-shredded or the envelope ciphertext is corrupted.
    /// </exception>
    T? DecryptSubjectRecord<T>(string subjectId, EncryptedEnvelope envelope);

    /// <summary>
    /// Packages and envelope-encrypts an active trajectory file into a post-quantum protected export bundle signed with ML-DSA-65.
    /// </summary>
    /// <param name="trajectoryFileName">The filename of the trajectory inside shared data storage.</param>
    /// <param name="subjectId">The pseudonymized subject identifier receiving the export.</param>
    /// <param name="exportDirectory">The destination export directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task returning the absolute path to the generated encrypted export bundle.</returns>
    Task<string> ExportEncryptedDatasetBundleAsync(string trajectoryFileName, string subjectId, string exportDirectory, CancellationToken cancellationToken = default);
}
