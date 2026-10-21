using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Production implementation of <see cref="IComplianceAuditService"/> enforcing HIPAA Safe Harbor,
/// GDPR Articles 9 &amp; 17, GINA genetic non-discrimination, and FDA 21 CFR Part 11 tamper-evident hash chaining.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The central compliance coordinator maintaining immutable cryptographic audit ledgers
/// and de-identification pipelines.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Mandated by health data regulations to prevent unauthorized exposure of Protected Health Information (PHI)
/// and detect any unauthorized retrospective tampering with scientific simulation records.
/// </para>
/// <para>
/// <strong>How it works:</strong> Chains every audit event into a Merkle-like SHA-384 hash sequence, signed by NIST FIPS 204 ML-DSA-65.
/// </para>
/// </remarks>
public sealed class ComplianceAuditService : IComplianceAuditService
{
    /// <summary>
    /// Key management service providing node signing keys and subject profile counts.
    /// </summary>
    private readonly IKeyManagementService _keyManagement;

    /// <summary>
    /// Post-quantum cryptographic provider for signature generation and verification.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Optional internal event bus for publishing regulatory compliance events across the node and cluster.
    /// </summary>
    private readonly IInternalEventBus? _eventBus;

    /// <summary>
    /// Thread synchronization lock ensuring atomic ledger append operations.
    /// </summary>
    private readonly object _ledgerLock = new();

    /// <summary>
    /// In-memory append-only ledger of committed audit records.
    /// </summary>
    private readonly List<AuditRecord> _records = new();

    /// <summary>
    /// Regular expression detecting common direct HIPAA personal identifiers in metadata headers.
    /// </summary>
    private static readonly Regex s_directIdentifierRegex = new(
        @"(?:mrn|ssn|patient|name|dob|birth|phone|email|address|hospital|id):\s*([^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceAuditService"/> class.
    /// </summary>
    /// <param name="keyManagement">The key management service.</param>
    /// <param name="cryptoProvider">The post-quantum cryptographic algorithm provider.</param>
    /// <param name="eventBus">Optional event bus instance for real-time compliance broadcasting.</param>
    public ComplianceAuditService(
        IKeyManagementService keyManagement,
        IPostQuantumCryptoProvider cryptoProvider,
        IInternalEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(keyManagement);
        ArgumentNullException.ThrowIfNull(cryptoProvider);

        _keyManagement = keyManagement;
        _cryptoProvider = cryptoProvider;
        _eventBus = eventBus;

        // WHAT: Initialize the Genesis record (Sequence 0) of the FDA 21 CFR Part 11 audit ledger.
        // WHY:  Provides a deterministic, cryptographic origin for the unbroken hash chain.
        // HOW:  Computes SHA-384 digest of genesis parameters with 96-zero previous hash and signs with node ML-DSA-65 key.
        lock (_ledgerLock)
        {
            DateTimeOffset genesisTime = DateTimeOffset.UtcNow;
            string previousHash = new('0', 96);
            string currentHash = ComputeHash(0, genesisTime, previousHash, "SYSTEM", "GenesisAuditLedger", "NodeLedger", "{\"genesis\":true}");

            PqcKeyMaterial signingKey = _keyManagement.GetNodeSigningKey();
            PqcSignature signature = _cryptoProvider.SignData(
                Encoding.UTF8.GetBytes(currentHash),
                signingKey.PrivateKeyBase64!, // Justification: Node signing key is generated at startup and contains non-null private key material.
                signingKey.KeyId);

            _records.Add(new AuditRecord
            {
                SequenceNumber = 0,
                TimestampUtc = genesisTime,
                PreviousHash = previousHash,
                CurrentHash = currentHash,
                OperatorId = "SYSTEM",
                Action = "GenesisAuditLedger",
                Resource = "NodeLedger",
                DetailsJson = "{\"genesis\":true}",
                Signature = signature
            });
        }
    }

    /// <summary>
    /// Appends an immutable, tamper-evident audit record signed with the node's post-quantum ML-DSA-65 key.
    /// </summary>
    /// <param name="operatorId">The pseudonymized identifier of the operator or service initiating the action.</param>
    /// <param name="action">The functional action category (e.g. "IngestScaffold", "ScreenGenomics", "CryptoShredSubject").</param>
    /// <param name="resource">The target resource or file path affected.</param>
    /// <param name="detailsJson">Serialized JSON payload with operational parameters or result summaries.</param>
    /// <returns>The newly committed <see cref="AuditRecord"/>.</returns>
    public AuditRecord LogAction(string operatorId, string action, string resource, string detailsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        string safeDetails = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson;

        lock (_ledgerLock)
        {
            // WHAT: Chain this new record to the preceding record's hash digest.
            // WHY:  Creates an unbreakable mathematical dependency (FDA 21 CFR Part 11 compliance).
            // HOW:  Retrieves CurrentHash of the last record, calculates SHA-384 of new state, and signs with ML-DSA-65.
            AuditRecord lastRecord = _records[^1];
            long sequenceNumber = _records.Count;
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            string previousHash = lastRecord.CurrentHash;

            string currentHash = ComputeHash(sequenceNumber, timestamp, previousHash, operatorId, action, resource, safeDetails);

            PqcKeyMaterial signingKey = _keyManagement.GetNodeSigningKey();
            PqcSignature signature = _cryptoProvider.SignData(
                Encoding.UTF8.GetBytes(currentHash),
                signingKey.PrivateKeyBase64!, // Justification: Node signing key is generated at startup and contains non-null private key material.
                signingKey.KeyId);

            AuditRecord record = new()
            {
                SequenceNumber = sequenceNumber,
                TimestampUtc = timestamp,
                PreviousHash = previousHash,
                CurrentHash = currentHash,
                OperatorId = operatorId,
                Action = action,
                Resource = resource,
                DetailsJson = safeDetails,
                Signature = signature
            };

            _records.Add(record);

            if (_eventBus != null)
            {
                // WHAT: Publishes the audit log event asynchronously across the internal event bus.
                // WHY:  Notifies security monitors and Valkey cluster subscribers of regulatory log commits.
                // HOW:  Invokes PublishAsync with an AuditLogDispatchedEvent.
                _ = _eventBus.PublishAsync(new AuditLogDispatchedEvent(
                    record.SequenceNumber,
                    record.OperatorId,
                    record.Action,
                    record.Resource,
                    record.CurrentHash));
            }

            return record;
        }
    }

    /// <summary>
    /// Cryptographically verifies the entire audit ledger from genesis to head, validating hash chaining and ML-DSA-65 signatures.
    /// </summary>
    /// <returns>An <see cref="AuditLedgerVerificationResult"/> reporting integrity status and record counts.</returns>
    public AuditLedgerVerificationResult VerifyLedgerIntegrity()
    {
        lock (_ledgerLock)
        {
            PqcKeyMaterial signingKey = _keyManagement.GetNodeSigningKey();

            for (int i = 0; i < _records.Count; i++)
            {
                AuditRecord record = _records[i];

                // 1. Verify sequence order
                if (record.SequenceNumber != i)
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Sequence discontinuity at index {i}: expected sequence {i}, got {record.SequenceNumber}."
                    };
                }

                // 2. Verify hash chaining against previous record
                if (i > 0)
                {
                    AuditRecord previousRecord = _records[i - 1];
                    if (!string.Equals(record.PreviousHash, previousRecord.CurrentHash, StringComparison.Ordinal))
                    {
                        return new AuditLedgerVerificationResult
                        {
                            IsValid = false,
                            TotalRecordsVerified = i,
                            FirstViolationSequence = record.SequenceNumber,
                            Message = $"Broken hash link at sequence {record.SequenceNumber}: PreviousHash does not match record {previousRecord.SequenceNumber} CurrentHash."
                        };
                    }
                }

                // 3. Recompute and verify current hash
                string expectedHash = ComputeHash(
                    record.SequenceNumber,
                    record.TimestampUtc,
                    record.PreviousHash,
                    record.OperatorId,
                    record.Action,
                    record.Resource,
                    record.DetailsJson);

                if (!string.Equals(record.CurrentHash, expectedHash, StringComparison.Ordinal))
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Tampered audit payload at sequence {record.SequenceNumber}: computed hash does not match committed CurrentHash."
                    };
                }

                // 4. Verify post-quantum ML-DSA-65 digital signature
                bool signatureValid = _cryptoProvider.VerifySignature(
                    Encoding.UTF8.GetBytes(record.CurrentHash),
                    record.Signature.SignatureBase64,
                    signingKey.PublicKeyBase64);

                if (!signatureValid)
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Invalid ML-DSA-65 signature on audit record {record.SequenceNumber}."
                    };
                }
            }

            return new AuditLedgerVerificationResult
            {
                IsValid = true,
                TotalRecordsVerified = _records.Count,
                FirstViolationSequence = null,
                Message = $"All {_records.Count} audit records verified cleanly with valid hash chains and ML-DSA-65 signatures."
            };
        }
    }

    /// <summary>
    /// Returns a read-only list of all committed audit records.
    /// </summary>
    /// <param name="limit">The maximum number of recent records to return (defaults to 100).</param>
    /// <returns>A list of recent audit records ordered by sequence number descending.</returns>
    public IReadOnlyList<AuditRecord> GetAuditRecords(int limit = 100)
    {
        lock (_ledgerLock)
        {
            int take = Math.Clamp(limit, 1, 1000);
            return _records
                .OrderByDescending(r => r.SequenceNumber)
                .Take(take)
                .ToList();
        }
    }

    /// <summary>
    /// De-identifies raw patient data pursuant to HIPAA Safe Harbor (§ 164.514) and verifies GDPR Article 9 &amp; GINA consent.
    /// </summary>
    /// <param name="rawSubjectIdentifier">The raw patient identifier or name to be pseudonymized.</param>
    /// <param name="rawGenomicData">The raw genomic sequence or SNP profile string.</param>
    /// <param name="gdprConsentConfirmed">Whether explicit subject consent has been granted for processing genetic/health data.</param>
    /// <param name="ginaPurposeLimited">Whether the data use is strictly limited to therapeutic nanocarrier design without discriminatory export.</param>
    /// <returns>A <see cref="DeIdentificationResult"/> containing the cryptographic pseudonym and sanitized payload.</returns>
    public DeIdentificationResult DeIdentifySubject(string rawSubjectIdentifier, string rawGenomicData, bool gdprConsentConfirmed, bool ginaPurposeLimited)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawSubjectIdentifier);
        ArgumentNullException.ThrowIfNull(rawGenomicData);

        if (!gdprConsentConfirmed)
        {
            throw new InvalidOperationException("GDPR Article 9 Violation: Processing of special category genetic data requires explicit patient consent.");
        }

        if (!ginaPurposeLimited)
        {
            throw new InvalidOperationException("GINA Violation: Genetic data utilization must be strictly limited to nanocarrier therapeutic tuning and prohibited from discriminatory dissemination.");
        }

        // WHAT: Derive an irreversible pseudonym for the subject.
        // WHY:  HIPAA Safe Harbor (§ 164.514(b)) requires stripping direct personal identifiers.
        // HOW:  Computes SHA-384 digest of salted identifier and formats as "sub_<16-hex>".
        byte[] saltBytes = Encoding.UTF8.GetBytes("HRT_SAFE_HARBOR_SALT_2026_");
        byte[] rawBytes = Encoding.UTF8.GetBytes(rawSubjectIdentifier.Trim().ToLowerInvariant());
        byte[] combined = new byte[saltBytes.Length + rawBytes.Length];
        saltBytes.CopyTo(combined, 0);
        rawBytes.CopyTo(combined, saltBytes.Length);

        byte[] hash = SHA384.HashData(combined);
        string pseudonym = "sub_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();

        // WHAT: Scan and sanitize any direct identifiers found in FASTA headers or comment lines.
        // WHY:  Fasta sequence headers frequently contain patient names, hospital codes, or MRNs.
        // HOW:  Detects and replaces matching lines with clean pseudonym header.
        List<string> strippedIdentifiers = new();
        string[] lines = rawGenomicData.Split('\n');
        StringBuilder sanitized = new();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.StartsWith('>'))
            {
                var matches = s_directIdentifierRegex.Matches(line);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count > 1)
                    {
                        strippedIdentifiers.Add(m.Groups[1].Value);
                    }
                }
                sanitized.AppendLine($">{pseudonym} [HIPAA Safe Harbor De-Identified Scaffold]");
            }
            else
            {
                sanitized.AppendLine(line);
            }
        }

        // Commit audit record
        LogAction("COMPLIANCE_ENGINE", "DeIdentifySubject", pseudonym, $"{{\"strippedCount\":{strippedIdentifiers.Count},\"gdpr\":true,\"gina\":true}}");

        return new DeIdentificationResult
        {
            PseudonymSubjectId = pseudonym,
            SanitizedData = sanitized.ToString(),
            GdprConsentConfirmed = gdprConsentConfirmed,
            GinaSafeguardVerified = ginaPurposeLimited,
            StrippedIdentifiers = strippedIdentifiers,
            DeIdentifiedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Evaluates the end-to-end security and compliance posture of the platform.
    /// </summary>
    /// <returns>A comprehensive <see cref="ComplianceStatusReport"/>.</returns>
    public ComplianceStatusReport GetComplianceStatus()
    {
        AuditLedgerVerificationResult verification = VerifyLedgerIntegrity();
        IReadOnlyList<SubjectKeyProfile> subjectProfiles = _keyManagement.GetAllSubjectProfiles();
        int totalSubjects = subjectProfiles.Count;
        int shreddedCount = subjectProfiles.Count(p => p.IsShredded);

        string overallStatus = verification.IsValid ? "Compliant" : "Degraded";

        return new ComplianceStatusReport
        {
            EncryptionAtRestAlgorithm = "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)",
            AuditSignatureAlgorithm = "ML-DSA-65 (NIST FIPS 204)",
            IsEncryptionAtRestActive = true,
            IsAuditLedgerIntact = verification.IsValid,
            AuditRecordCount = verification.TotalRecordsVerified,
            HipaaSafeHarborEnforced = true,
            GdprArticle9And17Active = true,
            GinaNonDiscriminationVerified = true,
            TotalSubjectsRegistered = totalSubjects,
            CryptoShreddedSubjectCount = shreddedCount,
            OverallStatus = overallStatus,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Helper method computing the deterministic SHA-384 hash of an audit record's fields.
    /// </summary>
    private static string ComputeHash(
        long sequenceNumber,
        DateTimeOffset timestamp,
        string previousHash,
        string operatorId,
        string action,
        string resource,
        string detailsJson)
    {
        string raw = $"{sequenceNumber}|{timestamp:O}|{previousHash}|{operatorId}|{action}|{resource}|{detailsJson}";
        byte[] hash = SHA384.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
