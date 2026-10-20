namespace WeaverLammpsHrt.TrajectoryParser.Compliance;

/// <summary>
/// Service contract providing HIPAA Safe Harbor de-identification, GDPR Article 9 &amp; 17 enforcement,
/// GINA purpose limitation verification, and FDA 21 CFR Part 11 tamper-evident cryptographic audit trails.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The regulatory compliance coordinator enforcing health data protection standards.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Synthetic biology and pharmacogenomic HRT data constitutes sensitive health and genetic records
/// subject to strict international legal standards.
/// </para>
/// <para>
/// <strong>How it works:</strong> Sanitizes and pseudonymizes patient inputs, chains audit events with SHA-384 and ML-DSA-65 signatures,
/// and validates full ledger cryptographic integrity.
/// </para>
/// </remarks>
public interface IComplianceAuditService
{
    /// <summary>
    /// Appends an immutable, tamper-evident audit record signed with the node's post-quantum ML-DSA-65 key.
    /// </summary>
    /// <param name="operatorId">The pseudonymized identifier of the operator or service initiating the action.</param>
    /// <param name="action">The functional action category (e.g. "IngestScaffold", "ScreenGenomics", "CryptoShredSubject").</param>
    /// <param name="resource">The target resource or file path affected.</param>
    /// <param name="detailsJson">Serialized JSON payload with operational parameters or result summaries.</param>
    /// <returns>The newly committed <see cref="AuditRecord"/>.</returns>
    AuditRecord LogAction(string operatorId, string action, string resource, string detailsJson);

    /// <summary>
    /// Cryptographically verifies the entire audit ledger from genesis to head, validating hash chaining and ML-DSA-65 signatures.
    /// </summary>
    /// <returns>An <see cref="AuditLedgerVerificationResult"/> reporting integrity status and record counts.</returns>
    AuditLedgerVerificationResult VerifyLedgerIntegrity();

    /// <summary>
    /// Returns a read-only list of all committed audit records.
    /// </summary>
    /// <param name="limit">The maximum number of recent records to return (defaults to 100).</param>
    /// <returns>A list of recent audit records ordered by sequence number descending.</returns>
    IReadOnlyList<AuditRecord> GetAuditRecords(int limit = 100);

    /// <summary>
    /// De-identifies raw patient data pursuant to HIPAA Safe Harbor (§ 164.514) and verifies GDPR Article 9 &amp; GINA consent.
    /// </summary>
    /// <param name="rawSubjectIdentifier">The raw patient identifier or name to be pseudonymized.</param>
    /// <param name="rawGenomicData">The raw genomic sequence or SNP profile string.</param>
    /// <param name="gdprConsentConfirmed">Whether explicit subject consent has been granted for processing genetic/health data.</param>
    /// <param name="ginaPurposeLimited">Whether the data use is strictly limited to therapeutic nanocarrier design without discriminatory export.</param>
    /// <returns>A <see cref="DeIdentificationResult"/> containing the cryptographic pseudonym and sanitized payload.</returns>
    DeIdentificationResult DeIdentifySubject(string rawSubjectIdentifier, string rawGenomicData, bool gdprConsentConfirmed, bool ginaPurposeLimited);

    /// <summary>
    /// Evaluates the end-to-end security and compliance posture of the platform.
    /// </summary>
    /// <returns>A comprehensive <see cref="ComplianceStatusReport"/>.</returns>
    ComplianceStatusReport GetComplianceStatus();
}
