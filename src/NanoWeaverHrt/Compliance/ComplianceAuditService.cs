using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;

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
/// <para>
/// <strong>Pre-persistence signatures:</strong> Before the persistent node signing key design landed, each process start
/// generated an in-memory ML-DSA-65 key pair identified by <see cref="LegacyEphemeralSigningKeyId"/> and discarded both
/// halves of that key when the process exited. Because the public half of those ephemeral keys was never saved anywhere,
/// any audit record or export manifest carrying <see cref="LegacyEphemeralSigningKeyId"/> can never be cryptographically
/// verified by anyone. Verification therefore classifies those records as
/// <see cref="AuditLedgerViolationKind.LegacyEphemeralSigningKey"/> rather than conflating them with tampered signatures.
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
    /// Optional persistent database repository storing audit ledger entries.
    /// </summary>
    private readonly IAuditLedgerRepository? _repository;

    /// <summary>
    /// Thread synchronization lock ensuring atomic ledger append operations.
    /// </summary>
    private readonly object _ledgerLock = new();

    /// <summary>
    /// In-memory append-only ledger of committed audit records.
    /// </summary>
    private readonly List<AuditRecord> _records = new();

    /// <summary>
    /// Canonical genesis previous hash digest consisting of 96 zero characters ('0') for SHA-384 hash chain genesis.
    /// </summary>
    public const string GenesisPreviousHash = "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Case-insensitive clinical key-value tags recognized across FASTA headers and plain-text streams for HIPAA Safe Harbor de-identification.
    /// </summary>
    private static readonly string[] s_clinicalTags =
    [
        "mrn:",
        "ssn:",
        "patient:",
        "subject:",
        "name:",
        "dob:",
        "birth:",
        "phone:",
        "email:",
        "address:",
        "hospital:",
        "id:"
    ];

    /// <summary>
    /// Secret key bytes used for HMAC-SHA256 subject pseudonymization.
    /// </summary>
    private readonly byte[] _pseudonymizationKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceAuditService"/> class.
    /// </summary>
    /// <param name="keyManagement">The key management service.</param>
    /// <param name="cryptoProvider">The post-quantum cryptographic algorithm provider.</param>
    /// <param name="eventBus">Optional event bus instance for real-time compliance broadcasting.</param>
    /// <param name="repository">Optional persistent repository storing audit records across restarts.</param>
    /// <param name="configuration">Optional application configuration providing custom pseudonymization secrets.</param>
    /// <param name="secretStore">Optional node secret store providing persistent key material across restarts.</param>
    public ComplianceAuditService(
        IKeyManagementService keyManagement,
        IPostQuantumCryptoProvider cryptoProvider,
        IInternalEventBus? eventBus = null,
        IAuditLedgerRepository? repository = null,
        IConfiguration? configuration = null,
        INodeSecretStore? secretStore = null)
    {
        ArgumentNullException.ThrowIfNull(keyManagement);
        ArgumentNullException.ThrowIfNull(cryptoProvider);

        _keyManagement = keyManagement;
        _cryptoProvider = cryptoProvider;
        _eventBus = eventBus;
        _repository = repository;
        _pseudonymizationKey = ResolvePseudonymizationKey(configuration, secretStore);

        // WHAT: Initializes or restores the FDA 21 CFR Part 11 audit ledger.
        // WHY:  Provides a deterministic, cryptographic origin for the unbroken hash chain, restored across restarts.
        // HOW:  Loads existing records from the repository if present; mints Genesis only when the store is empty.
        lock (_ledgerLock)
        {
            if (_repository != null)
            {
                IReadOnlyList<AuditRecord> existing = _repository.LoadAll();
                if (existing.Count > 0)
                {
                    _records.AddRange(existing);
                    return;
                }
            }

            DateTimeOffset genesisTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
            string previousHash = GenesisPreviousHash;
            PqcKeyMaterial signingKey = _keyManagement.GetNodeSigningKey();
            string genesisDetailsJson = $"{{\"genesis\":true,\"signingKeyId\":\"{signingKey.KeyId}\",\"signingKeyOrigin\":\"{_keyManagement.NodeSigningKeyOrigin}\"}}";
            string currentHash = ComputeHash(0, genesisTime, previousHash, "SYSTEM", "GenesisAuditLedger", "NodeLedger", genesisDetailsJson);

            PqcSignature signature = _keyManagement.SignWithNodeSigningKey(Encoding.UTF8.GetBytes(currentHash));

            AuditRecord genesisRecord = new()
            {
                SequenceNumber = 0,
                TimestampUtc = genesisTime,
                PreviousHash = previousHash,
                CurrentHash = currentHash,
                OperatorId = "SYSTEM",
                Action = "GenesisAuditLedger",
                Resource = "NodeLedger",
                DetailsJson = genesisDetailsJson,
                Signature = signature
            };

            _records.Add(genesisRecord);
            _repository?.Append(genesisRecord);
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
            DateTimeOffset timestamp = TruncateToMicroseconds(DateTimeOffset.UtcNow);
            string previousHash = lastRecord.CurrentHash;

            string currentHash = ComputeHash(sequenceNumber, timestamp, previousHash, operatorId, action, resource, safeDetails);

            PqcSignature signature = _keyManagement.SignWithNodeSigningKey(Encoding.UTF8.GetBytes(currentHash));

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
            _repository?.Append(record);

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
                        Message = $"Sequence discontinuity at index {i}: expected sequence {i}, got {record.SequenceNumber}.",
                        ViolationKind = AuditLedgerViolationKind.SequenceDiscontinuity,
                        SigningKeyId = signingKey.KeyId
                    };
                }

                // 2. Verify hash chaining against previous record
                if (i == 0)
                {
                    if (!string.Equals(record.PreviousHash, GenesisPreviousHash, StringComparison.Ordinal))
                    {
                        return new AuditLedgerVerificationResult
                        {
                            IsValid = false,
                            TotalRecordsVerified = 0,
                            FirstViolationSequence = record.SequenceNumber,
                            Message = "Broken genesis hash link at sequence 0: PreviousHash does not match GenesisPreviousHash.",
                            ViolationKind = AuditLedgerViolationKind.BrokenHashLink,
                            SigningKeyId = signingKey.KeyId
                        };
                    }
                }
                else
                {
                    AuditRecord previousRecord = _records[i - 1];
                    if (!string.Equals(record.PreviousHash, previousRecord.CurrentHash, StringComparison.Ordinal))
                    {
                        return new AuditLedgerVerificationResult
                        {
                            IsValid = false,
                            TotalRecordsVerified = i,
                            FirstViolationSequence = record.SequenceNumber,
                            Message = $"Broken hash link at sequence {record.SequenceNumber}: PreviousHash does not match record {previousRecord.SequenceNumber} CurrentHash.",
                            ViolationKind = AuditLedgerViolationKind.BrokenHashLink,
                            SigningKeyId = signingKey.KeyId
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
                        Message = $"Tampered audit payload at sequence {record.SequenceNumber}: computed hash does not match committed CurrentHash.",
                        ViolationKind = AuditLedgerViolationKind.TamperedPayload,
                        SigningKeyId = signingKey.KeyId
                    };
                }

                // 4. Classify signer key and verify post-quantum ML-DSA-65 digital signature
                // WHAT: Classify the record's signer key identifier before attempting ML-DSA-65 cryptographic verification.
                // WHY:  Separates records signed by the pre-persistence ephemeral key (whose public key was never stored) and
                //       records signed by a different node's key from genuine cryptographic signature failures against this node's key.
                // HOW:  Compares SignerKeyId against LegacyEphemeralSigningKeyId and TryGetVerificationKey before calling VerifySignature.
                if (string.Equals(record.Signature.SignerKeyId, LegacyEphemeralSigningKeyId, StringComparison.Ordinal))
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Audit record {record.SequenceNumber} carries a signature from the pre-persistence ephemeral node key '{LegacyEphemeralSigningKeyId}'. That key was never stored, so this signature can never be verified. This is not evidence of tampering.",
                        ViolationKind = AuditLedgerViolationKind.LegacyEphemeralSigningKey,
                        SigningKeyId = signingKey.KeyId
                    };
                }

                if (!TryGetVerificationKey(record.Signature.SignerKeyId, out string verificationPublicKeyBase64))
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Audit record {record.SequenceNumber} is signed by a key that is not this node's audit signing key, so its signature cannot be verified here.",
                        ViolationKind = AuditLedgerViolationKind.UnknownSigningKey,
                        SigningKeyId = signingKey.KeyId
                    };
                }

                bool signatureValid = _cryptoProvider.VerifySignature(
                    Encoding.UTF8.GetBytes(record.CurrentHash),
                    record.Signature.SignatureBase64,
                    verificationPublicKeyBase64);

                if (!signatureValid)
                {
                    return new AuditLedgerVerificationResult
                    {
                        IsValid = false,
                        TotalRecordsVerified = i,
                        FirstViolationSequence = record.SequenceNumber,
                        Message = $"Invalid ML-DSA-65 signature on audit record {record.SequenceNumber}.",
                        ViolationKind = AuditLedgerViolationKind.InvalidSignature,
                        SigningKeyId = signingKey.KeyId
                    };
                }
            }

            return new AuditLedgerVerificationResult
            {
                IsValid = true,
                TotalRecordsVerified = _records.Count,
                FirstViolationSequence = null,
                Message = $"All {_records.Count} audit records verified cleanly with valid hash chains and ML-DSA-65 signatures.",
                ViolationKind = AuditLedgerViolationKind.None,
                SigningKeyId = signingKey.KeyId
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
        // WHY:  HIPAA Safe Harbor (§ 164.514(b)) requires stripping direct personal identifiers; HMAC-SHA256 with node-bound key prevents rainbow table reversal.
        // HOW:  Delegates to PseudonymizeSubjectId.
        string pseudonym = PseudonymizeSubjectId(rawSubjectIdentifier);
        ReadOnlySpan<char> subjectSpan = rawSubjectIdentifier.AsSpan().Trim();

        // WHAT: Scan and sanitize direct identifiers found in FASTA headers and full text streams with zero regex/split allocations.
        // WHY:  Complies with AGENTS.md Prime Directive 1 while purging clinical identifiers and subject occurrences across lines.
        // HOW:  Enumerates lines with MemoryExtensions.EnumerateLines, detects headers vs text, and sanitizes into StringBuilder.
        List<string> strippedIdentifiers = new();
        StringBuilder sanitized = new();

        foreach (ReadOnlySpan<char> rawLine in rawGenomicData.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.TrimEnd('\r');
            if (!line.IsEmpty && line[0] == '>')
            {
                ScanAndExtractHeaderIdentifiers(line, subjectSpan, strippedIdentifiers);
                sanitized.Append('>').Append(pseudonym).Append(" [HIPAA Safe Harbor De-Identified Scaffold]").AppendLine();
            }
            else
            {
                SanitizeNonHeaderLine(line, subjectSpan, pseudonym, strippedIdentifiers, sanitized);
                sanitized.AppendLine();
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

    /// <inheritdoc/>
    public string PseudonymizeSubjectId(string rawSubjectIdentifier)
    {
        // WHAT: Derives an irreversible pseudonym for the subject.
        // WHY:  HIPAA Safe Harbor (§ 164.514(b)) requires stripping direct personal identifiers; HMAC-SHA256 with node-bound key prevents rainbow table reversal.
        // HOW:  Computes HMAC-SHA256 digest of normalized identifier using _pseudonymizationKey and formats as "sub_<16-hex>".
        ArgumentException.ThrowIfNullOrWhiteSpace(rawSubjectIdentifier);
        byte[] rawBytes = Encoding.UTF8.GetBytes(rawSubjectIdentifier.Trim().ToLowerInvariant());
        byte[] hash = HMACSHA256.HashData(_pseudonymizationKey, rawBytes);
        return "sub_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Evaluates the end-to-end security and compliance posture of the platform.
    /// </summary>
    /// <returns>A comprehensive <see cref="ComplianceStatusReport"/>.</returns>
    public ComplianceStatusReport GetComplianceStatus()
    {
        AuditLedgerVerificationResult verification = VerifyLedgerIntegrity();
        PqcKeyMaterial signingKey = _keyManagement.GetNodeSigningKey();
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
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            AuditSigningKeyId = signingKey.KeyId,
            AuditSigningPublicKeyBase64 = signingKey.PublicKeyBase64
        };
    }

    /// <summary>
    /// Helper method computing the deterministic SHA-384 hash of an audit record's fields.
    /// </summary>
    /// <param name="sequenceNumber">The position of this record in the append-only ledger, counted from one.</param>
    /// <param name="timestamp">The moment the audited action happened, in UTC.</param>
    /// <param name="previousHash">The digest of the record immediately before this one, which chains the ledger together.</param>
    /// <param name="operatorId">The user account or background service that performed the action.</param>
    /// <param name="action">A short machine-readable name for what was done, for example IngestScaffold.</param>
    /// <param name="resource">The thing the action was performed on, such as a patient record or a configuration key.</param>
    /// <param name="detailsJson">Any extra context about the action, serialized as JSON.</param>
    /// <returns>The SHA-384 digest of all supplied fields, formatted as a lowercase hexadecimal string.</returns>
    private static string ComputeHash(
        long sequenceNumber,
        DateTimeOffset timestamp,
        string previousHash,
        string operatorId,
        string action,
        string resource,
        string detailsJson)
    {
        // WHAT: Computes deterministic SHA-384 hash with length-prefixed canonical fields.
        // WHY:  Length prefixing prevents hash canonicalization collisions even if fields contain delimiter '|' characters.
        // HOW:  Normalizes timestamp to microsecond precision, encodes each field as "{length}:{value}|", and computes SHA-384.
        DateTimeOffset normalizedTimestamp = TruncateToMicroseconds(timestamp);
        string seqStr = sequenceNumber.ToString();
        string tsStr = normalizedTimestamp.ToString("O");
        string raw = $"{seqStr.Length}:{seqStr}|{tsStr.Length}:{tsStr}|{previousHash.Length}:{previousHash}|{operatorId.Length}:{operatorId}|{action.Length}:{action}|{resource.Length}:{resource}|{detailsJson.Length}:{detailsJson}";
        byte[] hash = SHA384.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Fixed key identifier used by pre-persistence ephemeral node signing keys, whose public half was never saved.
    /// </summary>
    public const string LegacyEphemeralSigningKeyId = "node_signing_master";

    /// <summary>
    /// Resolves the Base64-encoded SubjectPublicKeyInfo DER public key for a given audit record signer key identifier.
    /// </summary>
    /// <param name="signerKeyId">The signer key identifier recorded on the audit record.</param>
    /// <param name="publicKeyBase64">When this method returns <c>true</c>, contains the matching Base64 public key; otherwise, <see cref="string.Empty"/>.</param>
    /// <returns><c>true</c> if <paramref name="signerKeyId"/> matches this node's active audit signing key; otherwise, <c>false</c>.</returns>
    private bool TryGetVerificationKey(string signerKeyId, out string publicKeyBase64)
    {
        PqcKeyMaterial currentKey = _keyManagement.GetNodeSigningKey();
        if (string.Equals(signerKeyId, currentKey.KeyId, StringComparison.Ordinal))
        {
            publicKeyBase64 = currentKey.PublicKeyBase64;
            return true;
        }

        publicKeyBase64 = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves or generates the 32-byte secret key used for HMAC-SHA256 subject pseudonymization.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="secretStore">Optional node secret store providing persistent key material.</param>
    /// <returns>A 32-byte secret key for HMAC pseudonymization.</returns>
    private static byte[] ResolvePseudonymizationKey(IConfiguration? configuration, INodeSecretStore? secretStore = null)
    {
        // WHAT: Resolves configured pseudonymization secret, persisted node secret store key, or generates a secure CSPRNG fallback key.
        // WHY:  Keyed pseudonymization prevents rainbow table lookups while ensuring node consistency across restarts.
        // HOW:  Attempts Base64, hex, or SHA-256 derivation of Security:PseudonymizationSecret, queries INodeSecretStore, falling back to RandomNumberGenerator.
        string? secret = configuration?["Security:PseudonymizationSecret"];
        if (!string.IsNullOrWhiteSpace(secret))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(secret.Trim());
                if (decoded.Length == 32)
                {
                    return decoded;
                }
            }
            catch (FormatException)
            {
                // Not valid Base64
            }

            try
            {
                byte[] hexDecoded = Convert.FromHexString(secret.Trim());
                if (hexDecoded.Length == 32)
                {
                    return hexDecoded;
                }
            }
            catch (FormatException)
            {
                // Not valid hex
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()));
        }

        if (secretStore is not null)
        {
            byte[]? persisted = secretStore.GetOrCreateSecret("pseudonymization-hmac.key", 32);
            if (persisted is { Length: 32 })
            {
                return persisted;
            }
        }

        byte[] fallbackKey = new byte[32];
        RandomNumberGenerator.Fill(fallbackKey);
        return fallbackKey;
    }

    /// <summary>
    /// Truncates a timestamp to microsecond precision by dropping sub-microsecond tick fractions.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp to normalize.</param>
    /// <returns>A <see cref="DateTimeOffset"/> truncated to microsecond precision.</returns>
    internal static DateTimeOffset TruncateToMicroseconds(DateTimeOffset timestampUtc)
    {
        // WHAT: Drops sub-microsecond tick fractions (modulo 10) from the timestamp.
        // WHY:  PostgreSQL TIMESTAMPTZ stores timestamps to microsecond precision; normalizing ensures SHA-384 verification determinism.
        // HOW:  Subtracts timestampUtc.UtcDateTime.Ticks % 10L ticks from the DateTimeOffset value.
        return new DateTimeOffset(timestampUtc.UtcDateTime.Ticks - (timestampUtc.UtcDateTime.Ticks % 10L), TimeSpan.Zero);
    }

    /// <summary>
    /// Scans a FASTA header line starting with '&gt;' to extract direct HIPAA and clinical identifiers.
    /// </summary>
    /// <param name="headerLine">The FASTA header line span.</param>
    /// <param name="subjectSpan">The trimmed raw subject identifier span to detect and record.</param>
    /// <param name="strippedIdentifiers">Destination list accumulating extracted identifier strings.</param>
    private static void ScanAndExtractHeaderIdentifiers(
        ReadOnlySpan<char> headerLine,
        ReadOnlySpan<char> subjectSpan,
        List<string> strippedIdentifiers)
    {
        // WHAT: Scans FASTA header spans for clinical key-value tags and subject identifiers without Regex or allocations.
        // WHY:  Complies with AGENTS.md Prime Directive 1 (zero-allocation parsing) while extracting HIPAA Safe Harbor PII.
        // HOW:  Iterates over headerLine characters, matching clinical tags at delimiter boundaries and extracting values.
        if (!subjectSpan.IsEmpty && headerLine.IndexOf(subjectSpan, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string subj = subjectSpan.ToString();
            if (!strippedIdentifiers.Contains(subj, StringComparer.OrdinalIgnoreCase))
            {
                strippedIdentifiers.Add(subj);
            }
        }

        int i = 0;
        while (i < headerLine.Length)
        {
            bool isBoundary = (i == 0 || i == 1 && headerLine[0] == '>') ||
                              char.IsWhiteSpace(headerLine[i - 1]) ||
                              headerLine[i - 1] == ',' ||
                              headerLine[i - 1] == ';' ||
                              headerLine[i - 1] == '|';

            if (isBoundary)
            {
                bool matchedTag = false;
                for (int t = 0; t < s_clinicalTags.Length; t++)
                {
                    string tag = s_clinicalTags[t];
                    if (headerLine[i..].StartsWith(tag.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedTag = true;
                        int valStart = i + tag.Length;
                        while (valStart < headerLine.Length && char.IsWhiteSpace(headerLine[valStart]))
                        {
                            valStart++;
                        }

                        int valEnd = FindTagValueEnd(headerLine, valStart, subjectSpan);
                        if (valEnd > valStart)
                        {
                            ReadOnlySpan<char> valSpan = headerLine[valStart..valEnd].Trim();
                            if (!valSpan.IsEmpty)
                            {
                                string val = valSpan.ToString();
                                if (!strippedIdentifiers.Contains(val, StringComparer.OrdinalIgnoreCase))
                                {
                                    strippedIdentifiers.Add(val);
                                }
                            }
                        }

                        i = valEnd;
                        break;
                    }
                }

                if (matchedTag)
                {
                    continue;
                }
            }

            i++;
        }
    }

    /// <summary>
    /// Scans a non-FASTA line to redact clinical identifiers and raw subject occurrences.
    /// </summary>
    /// <param name="line">The text line span.</param>
    /// <param name="subjectSpan">The raw subject identifier to redact.</param>
    /// <param name="pseudonym">The pseudonym replacement string.</param>
    /// <param name="strippedIdentifiers">Destination list accumulating extracted identifier strings.</param>
    /// <param name="sanitized">The destination StringBuilder receiving sanitized text.</param>
    private static void SanitizeNonHeaderLine(
        ReadOnlySpan<char> line,
        ReadOnlySpan<char> subjectSpan,
        string pseudonym,
        List<string> strippedIdentifiers,
        StringBuilder sanitized)
    {
        // WHAT: Scans and redacts clinical key-value tags and raw subject identifier occurrences in non-header lines.
        // WHY:  Protects PHI across full text streams including CSV, JSON, clinical notes, and inline metadata without Regex.
        // HOW:  Iterates line characters, matching subjectSpan and s_clinicalTags, appending redaction tokens to StringBuilder.
        int i = 0;
        while (i < line.Length)
        {
            if (!subjectSpan.IsEmpty && line[i..].StartsWith(subjectSpan, StringComparison.OrdinalIgnoreCase))
            {
                string subj = subjectSpan.ToString();
                if (!strippedIdentifiers.Contains(subj, StringComparer.OrdinalIgnoreCase))
                {
                    strippedIdentifiers.Add(subj);
                }

                sanitized.Append(pseudonym);
                i += subjectSpan.Length;
                continue;
            }

            bool isBoundary = (i == 0 ||
                              char.IsWhiteSpace(line[i - 1]) ||
                              line[i - 1] == ',' ||
                              line[i - 1] == ';' ||
                              line[i - 1] == '|' ||
                              line[i - 1] == '{' ||
                              line[i - 1] == '"');

            if (isBoundary)
            {
                bool matchedTag = false;
                for (int t = 0; t < s_clinicalTags.Length; t++)
                {
                    string tag = s_clinicalTags[t];
                    if (line[i..].StartsWith(tag.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedTag = true;
                        sanitized.Append(line.Slice(i, tag.Length));
                        int valStart = i + tag.Length;
                        while (valStart < line.Length && char.IsWhiteSpace(line[valStart]))
                        {
                            sanitized.Append(line[valStart]);
                            valStart++;
                        }

                        int valEnd = FindTagValueEnd(line, valStart, subjectSpan);
                        if (valEnd > valStart)
                        {
                            ReadOnlySpan<char> valSpan = line[valStart..valEnd].Trim();
                            if (!valSpan.IsEmpty)
                            {
                                string val = valSpan.ToString();
                                if (!strippedIdentifiers.Contains(val, StringComparer.OrdinalIgnoreCase))
                                {
                                    strippedIdentifiers.Add(val);
                                }
                            }

                            bool isSubjectTag = string.Equals(tag, "patient:", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(tag, "subject:", StringComparison.OrdinalIgnoreCase);

                            sanitized.Append(isSubjectTag ? pseudonym : "[REDACTED]");
                        }

                        i = valEnd;
                        break;
                    }
                }

                if (matchedTag)
                {
                    continue;
                }
            }

            sanitized.Append(line[i]);
            i++;
        }
    }

    /// <summary>
    /// Finds the end of a clinical tag value in a character span by scanning for delimiters or subsequent clinical tags.
    /// </summary>
    /// <param name="span">The line span being scanned.</param>
    /// <param name="valStart">The start index of the tag value.</param>
    /// <param name="subjectSpan">The raw subject identifier to detect.</param>
    /// <returns>The index where the tag value terminates.</returns>
    private static int FindTagValueEnd(ReadOnlySpan<char> span, int valStart, ReadOnlySpan<char> subjectSpan)
    {
        // WHAT: Locates the end boundary of a clinical tag value.
        // WHY:  Addresses multi-word values (e.g. "123 Main St", "Jane Doe") while stopping at delimiters or next tags.
        // HOW:  If starts with subjectSpan, returns valStart + subjectSpan.Length; otherwise scans until delimiter or next tag.
        if (!subjectSpan.IsEmpty && span[valStart..].StartsWith(subjectSpan, StringComparison.OrdinalIgnoreCase))
        {
            return valStart + subjectSpan.Length;
        }

        int idx = valStart;
        while (idx < span.Length)
        {
            char c = span[idx];
            if (c == ',' || c == ';' || c == '|' || c == '"' || c == '}' || c == '\r' || c == '\n')
            {
                break;
            }

            if (char.IsWhiteSpace(c))
            {
                int nextIdx = idx + 1;
                while (nextIdx < span.Length && char.IsWhiteSpace(span[nextIdx]))
                {
                    nextIdx++;
                }

                if (nextIdx < span.Length)
                {
                    bool nextIsTag = false;
                    for (int t = 0; t < s_clinicalTags.Length; t++)
                    {
                        if (span[nextIdx..].StartsWith(s_clinicalTags[t].AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            nextIsTag = true;
                            break;
                        }
                    }

                    if (nextIsTag)
                    {
                        break;
                    }
                }
            }

            idx++;
        }

        return idx;
    }
}
