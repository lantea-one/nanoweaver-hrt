namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 007: Creates the trusted peer dataset-signing public key registry table (<c>weaver_peer_keys</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The database schema table (<c>weaver_peer_keys</c>) that stores trusted peer laboratories'
/// Ed25519 and ML-DSA-65 public keys along with permanent revocation audit metadata.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows a sovereign node to verify who signed an imported dataset manifest and
/// permanently record key revocations without ever deleting or reactivating a previously revoked key.
/// </para>
/// <para>
/// <strong>How it works:</strong> Enforces three named <c>CHECK</c> constraints guaranteeing a 64-character SHA-256
/// key identifier, an allowed signature algorithm (<c>Ed25519</c> or <c>MlDsa65</c>), and paired revocation columns.
/// </para>
/// </remarks>
public sealed class Migration007_PeerKeyRegistry : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 7;

    /// <inheritdoc/>
    public override string Name => "007_PeerKeyRegistry";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits the dialect-specific CREATE TABLE statement for weaver_peer_keys.
        // WHY:  Enforces key fingerprint length, supported signature algorithms, and paired revocation state in DDL.
        // HOW:  Uses TIMESTAMPTZ on PostgreSQL and TEXT on SQLite with three named CHECK constraints.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_peer_keys (
    key_id            VARCHAR(64)  PRIMARY KEY,
    label             VARCHAR(128) NOT NULL,
    key_algorithm     VARCHAR(16)  NOT NULL,
    public_key        TEXT         NOT NULL,
    registered_by     VARCHAR(128) NOT NULL,
    registered_at_utc TIMESTAMPTZ  NOT NULL,
    revoked_by        VARCHAR(128),
    revoked_at_utc    TIMESTAMPTZ,
    CONSTRAINT ck_weaver_peer_keys_key_id_length CHECK (length(key_id) = 64),
    CONSTRAINT ck_weaver_peer_keys_algorithm CHECK (key_algorithm IN ('Ed25519', 'MlDsa65')),
    CONSTRAINT ck_weaver_peer_keys_revocation_pair CHECK ((revoked_by IS NULL) = (revoked_at_utc IS NULL))
);";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_peer_keys (
    key_id            TEXT PRIMARY KEY,
    label             TEXT NOT NULL,
    key_algorithm     TEXT NOT NULL,
    public_key        TEXT NOT NULL,
    registered_by     TEXT NOT NULL,
    registered_at_utc TEXT NOT NULL,
    revoked_by        TEXT,
    revoked_at_utc    TEXT,
    CONSTRAINT ck_weaver_peer_keys_key_id_length CHECK (length(key_id) = 64),
    CONSTRAINT ck_weaver_peer_keys_algorithm CHECK (key_algorithm IN ('Ed25519', 'MlDsa65')),
    CONSTRAINT ck_weaver_peer_keys_revocation_pair CHECK ((revoked_by IS NULL) = (revoked_at_utc IS NULL))
);";
    }
}
