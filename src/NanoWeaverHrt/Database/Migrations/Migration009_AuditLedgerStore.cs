namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 009: Creates the durable FDA 21 CFR Part 11 append-only cryptographic audit ledger table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The database schema table (<c>weaver_audit_ledger</c>) storing the chained,
/// post-quantum signed audit records from genesis to head.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Regulatory compliance and cryptographic provenance require the audit log
/// to survive service restarts without losing hash-chain continuity or ML-DSA-65 signatures.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores sequence number as primary key, SHA-384 previous and current hashes,
/// operator ID, action category, resource, JSON details, and ML-DSA-65 signature metadata.
/// </para>
/// </remarks>
public sealed class Migration009_AuditLedgerStore : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 9;

    /// <inheritdoc/>
    public override string Name => "009_AuditLedgerStore";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific CREATE TABLE statement for weaver_audit_ledger.
        // WHY:  Persists FDA 21 CFR Part 11 audit records across restarts on SQLite and PostgreSQL.
        // HOW:  Uses TIMESTAMPTZ on PostgreSQL and TEXT on SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_audit_ledger (
    sequence_number     BIGINT      PRIMARY KEY,
    timestamp_utc       TIMESTAMPTZ NOT NULL,
    previous_hash       TEXT        NOT NULL,
    current_hash        TEXT        NOT NULL,
    operator_id         TEXT        NOT NULL,
    action              TEXT        NOT NULL,
    resource            TEXT        NOT NULL,
    details_json        TEXT        NOT NULL,
    signature_algorithm TEXT        NOT NULL,
    signer_key_id       TEXT        NOT NULL,
    signature_base64    TEXT        NOT NULL,
    signed_at_utc       TIMESTAMPTZ NOT NULL
);";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_audit_ledger (
    sequence_number     BIGINT      PRIMARY KEY,
    timestamp_utc       TEXT        NOT NULL,
    previous_hash       TEXT        NOT NULL,
    current_hash        TEXT        NOT NULL,
    operator_id         TEXT        NOT NULL,
    action              TEXT        NOT NULL,
    resource            TEXT        NOT NULL,
    details_json        TEXT        NOT NULL,
    signature_algorithm TEXT        NOT NULL,
    signer_key_id       TEXT        NOT NULL,
    signature_base64    TEXT        NOT NULL,
    signed_at_utc       TEXT        NOT NULL
);";
    }
}
