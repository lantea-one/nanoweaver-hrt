using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Database repository for the <c>weaver_peer_keys</c> table built on the <see cref="IWeaverDbConnectionFactory"/> seam.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The persistence adapter for peer dataset-signing public keys and their revocation state.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides engine-neutral SQL persistence across SQLite and PostgreSQL using
/// <c>ON CONFLICT (key_id) DO NOTHING</c> so a revoked peer key can never be overwritten or reactivated.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses a single SQL statement per operation with dialect-bound timestamp parameters
/// and <see cref="WeaverDbValues"/> reader coercions, referencing no database-driver-specific types.
/// </para>
/// </remarks>
public sealed class PeerKeyRepository : IPeerKeyRegistry
{
    /// <summary>
    /// Ordered column projection shared across queries on <c>weaver_peer_keys</c>.
    /// </summary>
    private const string Columns =
        "key_id, label, key_algorithm, public_key, registered_by, registered_at_utc, revoked_by, revoked_at_utc";

    /// <summary>
    /// Database connection factory providing open sessions and dialect primitives.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerKeyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory that opens connections to the configured database backend.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <c>null</c>.</exception>
    public PeerKeyRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<bool> TryRegisterAsync(PeerKeyRecord record, CancellationToken ct = default)
    {
        // WHAT: Inserts a new peer key row into weaver_peer_keys if key_id is not already present.
        // WHY:  ON CONFLICT (key_id) DO NOTHING prevents duplicate registration and forbids resurrecting revoked keys.
        // HOW:  Binds all columns through WeaverDbParameters and session.Dialect timestamp helpers and checks affected rows.
        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"INSERT INTO weaver_peer_keys ({Columns}) VALUES (@id, @label, @alg, @pub, @regBy, @regAt, @revBy, @revAt) ON CONFLICT (key_id) DO NOTHING");
            WeaverDbParameters.Add(cmd, "@id", record.KeyId);
            WeaverDbParameters.Add(cmd, "@label", record.Label);
            WeaverDbParameters.Add(cmd, "@alg", record.Algorithm.ToString());
            WeaverDbParameters.Add(cmd, "@pub", record.PublicKeyBase64);
            WeaverDbParameters.Add(cmd, "@regBy", record.RegisteredBy);
            WeaverDbParameters.Add(cmd, "@regAt", session.Dialect.TimestampValue(record.RegisteredAtUtc));
            WeaverDbParameters.AddNullable(cmd, "@revBy", record.RevokedBy);
            WeaverDbParameters.Add(cmd, "@revAt", session.Dialect.TimestampValueOrNull(record.RevokedAtUtc));
            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected == 1;
    }

    /// <inheritdoc/>
    public async Task<PeerKeyRecord?> FindActiveAsync(string keyId, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_peer_keys for an active (unrevoked) row matching keyId.
        // WHY:  Import verification trusts a peer key only while revoked_at_utc IS NULL.
        // HOW:  Executes a parameterized SELECT filtering on key_id and revoked_at_utc IS NULL.
        PeerKeyRecord? record = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {Columns} FROM weaver_peer_keys WHERE key_id = @id AND revoked_at_utc IS NULL");
            WeaverDbParameters.Add(cmd, "@id", keyId);
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                record = MapPeerKey(reader);
            }
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PeerKeyRecord>> ListAsync(CancellationToken ct = default)
    {
        // WHAT: Reads all peer key rows, both active and revoked, in deterministic registration order.
        // WHY:  Administrators audit the full history of trusted and revoked peer keys via GET /api/peer-keys.
        // HOW:  Orders by registered_at_utc, key_id and maps each row through MapPeerKey.
        List<PeerKeyRecord> records = new();
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {Columns} FROM weaver_peer_keys ORDER BY registered_at_utc, key_id");
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                records.Add(MapPeerKey(reader));
            }
        }

        return records;
    }

    /// <inheritdoc/>
    public async Task<PeerKeyRecord?> RevokeAsync(string keyId, string revokedBy, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
    {
        // WHAT: Marks an active peer key as permanently revoked and returns the updated record.
        // WHY:  Guards the UPDATE with revoked_at_utc IS NULL so unknown or already-revoked keys return null without mutating history.
        // HOW:  Updates revoked_by and revoked_at_utc in place, then re-reads the row on the same session when 1 row was updated.
        PeerKeyRecord? revoked = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            int affected;
            await using (DbCommand update = session.CreateCommand(
                "UPDATE weaver_peer_keys SET revoked_by = @by, revoked_at_utc = @at WHERE key_id = @id AND revoked_at_utc IS NULL"))
            {
                WeaverDbParameters.Add(update, "@by", revokedBy);
                WeaverDbParameters.Add(update, "@at", session.Dialect.TimestampValue(revokedAtUtc));
                WeaverDbParameters.Add(update, "@id", keyId);
                affected = await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (affected == 1)
            {
                await using DbCommand select = session.CreateCommand(
                    $"SELECT {Columns} FROM weaver_peer_keys WHERE key_id = @id");
                WeaverDbParameters.Add(select, "@id", keyId);
                await using DbDataReader reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await reader.ReadAsync(ct).ConfigureAwait(false);
                revoked = MapPeerKey(reader);
            }
        }

        return revoked;
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="PeerKeyRecord"/> instance.
    /// </summary>
    /// <param name="reader">The active data reader positioned on a row.</param>
    /// <returns>The hydrated <see cref="PeerKeyRecord"/>.</returns>
    internal static PeerKeyRecord MapPeerKey(DbDataReader reader) =>
        new()
        {
            KeyId = reader.GetString(0),
            Label = reader.GetString(1),
            Algorithm = Enum.Parse<KeyAlgorithm>(reader.GetString(2)),
            PublicKeyBase64 = reader.GetString(3),
            RegisteredBy = reader.GetString(4),
            RegisteredAtUtc = WeaverDbValues.ReadTimestamp(reader, 5),
            RevokedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
            RevokedAtUtc = WeaverDbValues.ReadNullableTimestamp(reader, 7)
        };
}
