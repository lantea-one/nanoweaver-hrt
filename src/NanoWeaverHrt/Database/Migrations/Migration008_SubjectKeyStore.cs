namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 008: Creates the durable subject ML-KEM-768 key store and GDPR Article 17 erasure tombstone table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The database schema table (<c>weaver_subject_keys</c>) that stores per-subject
/// post-quantum key encapsulation keys and permanent cryptographic erasure tombstones.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Subject keys must survive process restarts so historical encrypted health records
/// remain readable, while GDPR Article 17 erasures must permanently destroy private key material and leave an
/// immutable tombstone that prevents key re-issuance.
/// </para>
/// <para>
/// <strong>How it works:</strong> Enforces a table-level <c>CHECK</c> constraint (<c>ck_weaver_subject_keys_state</c>)
/// guaranteeing that active rows carry sealed key material and shredded rows carry <c>NULL</c> key columns.
/// </para>
/// </remarks>
public sealed class Migration008_SubjectKeyStore : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 8;

    /// <inheritdoc/>
    public override string Name => "008_SubjectKeyStore";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits the dialect-specific CREATE TABLE statement for weaver_subject_keys.
        // WHY:  Enforces at the database constraint level that a shredded subject row cannot retain key material.
        // HOW:  Uses BOOLEAN/TIMESTAMPTZ on PostgreSQL and INTEGER/TEXT on SQLite with ck_weaver_subject_keys_state.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_subject_keys (
    subject_key TEXT PRIMARY KEY,
    subject_id TEXT NOT NULL,
    key_id TEXT NULL,
    algorithm TEXT NOT NULL,
    public_key_base64 TEXT NULL,
    sealed_private_key TEXT NULL,
    is_shredded BOOLEAN NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    shredded_at_utc TIMESTAMPTZ NULL,
    CONSTRAINT ck_weaver_subject_keys_state CHECK (
        (is_shredded = FALSE AND key_id IS NOT NULL AND public_key_base64 IS NOT NULL
             AND sealed_private_key IS NOT NULL AND shredded_at_utc IS NULL)
        OR (is_shredded = TRUE AND public_key_base64 IS NULL
             AND sealed_private_key IS NULL AND shredded_at_utc IS NOT NULL))
);";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_subject_keys (
    subject_key TEXT PRIMARY KEY,
    subject_id TEXT NOT NULL,
    key_id TEXT NULL,
    algorithm TEXT NOT NULL,
    public_key_base64 TEXT NULL,
    sealed_private_key TEXT NULL,
    is_shredded INTEGER NOT NULL CHECK (is_shredded IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    shredded_at_utc TEXT NULL,
    CONSTRAINT ck_weaver_subject_keys_state CHECK (
        (is_shredded = 0 AND key_id IS NOT NULL AND public_key_base64 IS NOT NULL
             AND sealed_private_key IS NOT NULL AND shredded_at_utc IS NULL)
        OR (is_shredded = 1 AND public_key_base64 IS NULL
             AND sealed_private_key IS NULL AND shredded_at_utc IS NOT NULL))
);";
    }
}
