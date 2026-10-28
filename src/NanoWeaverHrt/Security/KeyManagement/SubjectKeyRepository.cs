using System.Data.Common;
using NanoWeaverHrt.Database.Access;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Database repository for <c>weaver_subject_keys</c> built on the <see cref="IWeaverDbConnectionFactory"/> seam.
/// Performs SQL persistence only; all cryptographic sealing and verification lives in <see cref="SubjectKeyVault"/>.
/// </summary>
public sealed class SubjectKeyRepository : ISubjectKeyRepository
{
    /// <summary>
    /// Ordered column projection shared across queries on <c>weaver_subject_keys</c>.
    /// </summary>
    private const string Columns =
        "subject_key, subject_id, key_id, algorithm, public_key_base64, sealed_private_key, is_shredded, created_at_utc, shredded_at_utc";

    /// <summary>
    /// Database connection factory providing open sessions and dialect primitives.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory that opens connections to the configured database backend.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <c>null</c>.</exception>
    public SubjectKeyRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectKeyRow>> ListAllAsync(CancellationToken ct = default)
    {
        // WHAT: Reads all subject key rows ordered by normalized primary key.
        // WHY:  Allows SubjectKeyVault to eagerly load and verify the entire store at startup.
        // HOW:  Opens a database session, streams rows with DbDataReader, and returns the materialized list.
        List<SubjectKeyRow> rows = new();
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {Columns} FROM weaver_subject_keys ORDER BY subject_key ASC");
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(MapRow(reader));
            }
        }

        return rows;
    }

    /// <inheritdoc/>
    public async Task<SubjectKeyRow> GetRequiredAsync(string subjectKey, CancellationToken ct = default)
    {
        // WHAT: Fetches a single subject key row that is expected to exist after a conflicting insert.
        // WHY:  Resolves concurrent key creation races by loading the winning row from the database.
        // HOW:  Executes a parameterized SELECT by primary key and throws if no row is returned.
        SubjectKeyRow? row = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {Columns} FROM weaver_subject_keys WHERE subject_key = @k");
            WeaverDbParameters.Add(cmd, "@k", subjectKey);
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                row = MapRow(reader);
            }
        }

        if (row is null)
        {
            throw new InvalidOperationException(SubjectKeyVault.RowMissingMessage);
        }

        return row;
    }

    /// <inheritdoc/>
    public async Task<bool> TryInsertActiveAsync(SubjectKeyRow row, CancellationToken ct = default)
    {
        // WHAT: Inserts a new active subject key row if no row exists for the same subject_key.
        // WHY:  Uses ON CONFLICT DO NOTHING so an existing active key or erasure tombstone can never be overwritten.
        // HOW:  Validates that active key columns are present, then executes a single parameterized INSERT.
        if (row.IsShredded || row.KeyId is null || row.PublicKeyBase64 is null || row.SealedPrivateKey is null)
        {
            throw new ArgumentException("Active subject key rows must not be shredded and must populate all key columns.", nameof(row));
        }

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"INSERT INTO weaver_subject_keys ({Columns}) VALUES (@k, @id, @kid, @alg, @pub, @priv, {session.Dialect.FalseLiteral}, @c, NULL) ON CONFLICT (subject_key) DO NOTHING");
            WeaverDbParameters.Add(cmd, "@k", row.SubjectKey);
            WeaverDbParameters.Add(cmd, "@id", row.SubjectId);
            WeaverDbParameters.Add(cmd, "@kid", row.KeyId);
            WeaverDbParameters.Add(cmd, "@alg", row.Algorithm);
            WeaverDbParameters.Add(cmd, "@pub", row.PublicKeyBase64);
            WeaverDbParameters.Add(cmd, "@priv", row.SealedPrivateKey);
            WeaverDbParameters.Add(cmd, "@c", session.Dialect.TimestampValue(row.CreatedAtUtc));
            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> MarkShreddedAsync(string subjectKey, DateTimeOffset shreddedAtUtc, CancellationToken ct = default)
    {
        // WHAT: Destroys stored key material on an active row and transitions it to a permanent tombstone.
        // WHY:  Enforces GDPR Article 17 erasure in storage by nulling both key columns and setting is_shredded.
        // HOW:  Runs a conditional UPDATE guarded by is_shredded = FALSE so tombstones cannot be re-updated.
        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"UPDATE weaver_subject_keys SET sealed_private_key = NULL, public_key_base64 = NULL, is_shredded = {session.Dialect.TrueLiteral}, shredded_at_utc = @t WHERE subject_key = @k AND is_shredded = {session.Dialect.FalseLiteral}");
            WeaverDbParameters.Add(cmd, "@k", subjectKey);
            WeaverDbParameters.Add(cmd, "@t", session.Dialect.TimestampValue(shreddedAtUtc));
            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected == 1;
    }

    /// <inheritdoc/>
    public async Task CompactStorageAsync(CancellationToken ct = default)
    {
        // WHAT: Compacts database storage after an erasure.
        // WHY:  Rebuilds the SQLite database file from live rows so freed pages no longer retain sealed key bytes.
        // HOW:  Executes the standard SQL maintenance command supported by both SQLite and PostgreSQL.
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand("VACUUM");
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="SubjectKeyRow"/> record.
    /// </summary>
    /// <param name="reader">The active data reader positioned on a row.</param>
    /// <returns>The hydrated <see cref="SubjectKeyRow"/>.</returns>
    private static SubjectKeyRow MapRow(DbDataReader reader) =>
        new()
        {
            SubjectKey = reader.GetString(0),
            SubjectId = reader.GetString(1),
            KeyId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Algorithm = reader.GetString(3),
            PublicKeyBase64 = reader.IsDBNull(4) ? null : reader.GetString(4),
            SealedPrivateKey = reader.IsDBNull(5) ? null : reader.GetString(5),
            IsShredded = WeaverDbValues.ReadBoolean(reader, 6),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 7),
            ShreddedAtUtc = WeaverDbValues.ReadNullableTimestamp(reader, 8)
        };

    /// <inheritdoc/>
    public async Task<bool> TryInsertTombstoneAsync(
        string subjectKey,
        string subjectId,
        DateTimeOffset shreddedAtUtc,
        CancellationToken ct = default)
    {
        // WHAT: Inserts a key-free tombstone row for a journalled erasure missing from the database.
        // WHY:  Restoring a database backup taken before the subject existed must still block key issuance for that erased subject.
        // HOW:  Executes INSERT ... ON CONFLICT (subject_key) DO NOTHING with NULL key columns and is_shredded = TRUE.
        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"INSERT INTO weaver_subject_keys ({Columns}) VALUES (@k, @id, NULL, 'ML-KEM-768', NULL, NULL, {session.Dialect.TrueLiteral}, @t, @t) ON CONFLICT (subject_key) DO NOTHING");
            WeaverDbParameters.Add(cmd, "@k", subjectKey);
            WeaverDbParameters.Add(cmd, "@id", subjectId);
            WeaverDbParameters.Add(cmd, "@t", session.Dialect.TimestampValue(shreddedAtUtc));
            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected == 1;
    }
}
