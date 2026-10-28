using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Provides SQLite-backed CRUD persistence for desktop and mobile GUI instance profiles.
/// </summary>
public sealed class SqliteInstanceProfileRepository : IInstanceProfileRepository
{
    /// <summary>
    /// Schema creation DDL for the client-side instance profiles table.
    /// </summary>
    private const string SchemaDdl =
        """
        CREATE TABLE IF NOT EXISTS weaver_gui_instances (
            instance_id TEXT PRIMARY KEY NOT NULL,
            display_name TEXT NOT NULL,
            base_uri TEXT NOT NULL,
            is_local_instance INTEGER NOT NULL,
            username TEXT NOT NULL,
            role TEXT NOT NULL,
            key_algorithm TEXT NOT NULL,
            public_key_pem TEXT NOT NULL,
            encrypted_private_key_envelope TEXT NOT NULL,
            notifications_enabled INTEGER NOT NULL,
            polling_interval_seconds INTEGER NOT NULL,
            added_at_utc TEXT NOT NULL,
            last_connected_at_utc TEXT NULL
        );
        """;

    /// <summary>
    /// Column projection for instance profile queries.
    /// </summary>
    private const string Columns =
        "instance_id, display_name, base_uri, is_local_instance, username, role, key_algorithm, public_key_pem, encrypted_private_key_envelope, notifications_enabled, polling_interval_seconds, added_at_utc, last_connected_at_utc";

    /// <summary>
    /// The SQLite connection string used to connect to the local profile database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteInstanceProfileRepository"/> class.
    /// </summary>
    /// <param name="connectionString">The SQLite database connection string.</param>
    public SqliteInstanceProfileRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Initializes the weaver_gui_instances schema if not already present.
        // WHY:  Guarantees tables exist prior to attempting profile queries or mutations.
        // HOW:  Opens a SqliteConnection in an explicit scope block and executes the DDL statement.
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = SchemaDdl;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstanceProfile>> ListProfilesAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves all configured instance profiles ordered alphabetically by display name.
        // WHY:  Populates the instance selector in client applications and background pollers.
        // HOW:  Queries weaver_gui_instances ORDER BY display_name and maps each row via MapProfile.
        List<InstanceProfile> profiles = new();
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM weaver_gui_instances ORDER BY display_name;";
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                profiles.Add(MapProfile(reader));
            }
        }

        return profiles;
    }

    /// <inheritdoc/>
    public async Task<InstanceProfile?> GetProfileAsync(string instanceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        // WHAT: Retrieves a single instance profile matching the given identifier.
        // WHY:  Enables detail inspection and connection configuration for a target node.
        // HOW:  Executes a parameterized SELECT filtering by instance_id within an explicit using block.
        InstanceProfile? profile = null;
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM weaver_gui_instances WHERE instance_id = @id;";
            cmd.Parameters.AddWithValue("@id", instanceId);

            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                profile = MapProfile(reader);
            }
        }

        return profile;
    }

    /// <inheritdoc/>
    public async Task UpsertProfileAsync(InstanceProfile profile, CancellationToken ct = default)
    {
        // WHAT: Validates, normalizes, and upserts an instance profile into SQLite.
        // WHY:  Persists new nodes or updates configuration while strictly enforcing URI and polling invariants.
        // HOW:  Validates inputs, normalizes username to lowercase, clamps interval, and executes ON CONFLICT UPDATE.
        InstanceProfile valid = ValidateAndNormalize(profile);
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                $"""
                INSERT INTO weaver_gui_instances ({Columns})
                VALUES (
                    @id, @displayName, @baseUri, @isLocal, @username, @role,
                    @keyAlg, @pubPem, @encPriv,
                    @notif, @interval, @addedAt, @lastConnected
                )
                ON CONFLICT (instance_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    base_uri = excluded.base_uri,
                    is_local_instance = excluded.is_local_instance,
                    username = excluded.username,
                    role = excluded.role,
                    key_algorithm = excluded.key_algorithm,
                    public_key_pem = excluded.public_key_pem,
                    encrypted_private_key_envelope = excluded.encrypted_private_key_envelope,
                    notifications_enabled = excluded.notifications_enabled,
                    polling_interval_seconds = excluded.polling_interval_seconds,
                    added_at_utc = excluded.added_at_utc,
                    last_connected_at_utc = excluded.last_connected_at_utc;
                """;

            cmd.Parameters.AddWithValue("@id", valid.InstanceId);
            cmd.Parameters.AddWithValue("@displayName", valid.DisplayName);
            cmd.Parameters.AddWithValue("@baseUri", valid.BaseUri);
            cmd.Parameters.AddWithValue("@isLocal", valid.IsLocalInstance ? 1 : 0);
            cmd.Parameters.AddWithValue("@username", valid.Username);
            cmd.Parameters.AddWithValue("@role", valid.Role);
            cmd.Parameters.AddWithValue("@keyAlg", valid.KeyAlgorithm);
            cmd.Parameters.AddWithValue("@pubPem", valid.PublicKeyPem);
            cmd.Parameters.AddWithValue("@encPriv", valid.EncryptedPrivateKeyEnvelopeBase64);
            cmd.Parameters.AddWithValue("@notif", valid.NotificationsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@interval", valid.PollingIntervalSeconds);
            cmd.Parameters.AddWithValue("@addedAt", valid.AddedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@lastConnected", valid.LastConnectedAtUtc.HasValue ? valid.LastConnectedAtUtc.Value.ToString("O") : DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateLastConnectedAsync(string instanceId, DateTimeOffset connectedAtUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        // WHAT: Updates the last_connected_at_utc timestamp for an existing profile.
        // WHY:  Records successful connection handshakes without altering other profile configuration.
        // HOW:  Executes a parameterized UPDATE filtering on instance_id and checks affected row count.
        int affected;
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE weaver_gui_instances SET last_connected_at_utc = @lastConnected WHERE instance_id = @id;";
            cmd.Parameters.AddWithValue("@id", instanceId);
            cmd.Parameters.AddWithValue("@lastConnected", connectedAtUtc.ToString("O"));

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteProfileAsync(string instanceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        // WHAT: Deletes an instance profile by its unique identifier.
        // WHY:  Allows users to remove decommissioned or unwanted instance connections.
        // HOW:  Executes a parameterized DELETE on instance_id and returns whether a row was removed.
        int affected;
        {
            await using SqliteConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM weaver_gui_instances WHERE instance_id = @id;";
            cmd.Parameters.AddWithValue("@id", instanceId);

            affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return affected > 0;
    }

    /// <summary>
    /// Creates and opens a new SQLite database connection.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        // WHAT: Instantiates and opens a connection to the configured SQLite database.
        // WHY:  Centralizes connection opening and async state machine boundary.
        // HOW:  Creates SqliteConnection using _connectionString and awaits OpenAsync.
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Validates profile invariants and normalizes mutable fields.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <returns>A validated and normalized <see cref="InstanceProfile"/>.</returns>
    private static InstanceProfile ValidateAndNormalize(InstanceProfile profile)
    {
        // WHAT: Validates required fields, normalizes username to lowercase, validates HTTPS or loopback HTTP, and clamps interval.
        // WHY:  Enforces Prime Directive 9 and guarantees consistent persistence state.
        // HOW:  Validates strings, inspects URI scheme and loopback status, clamps interval to [5, 300], and creates normalized record.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.DisplayName);

        if (string.IsNullOrWhiteSpace(profile.BaseUri) || !Uri.TryCreate(profile.BaseUri, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("BaseUri must be a valid absolute URI.", nameof(profile));
        }

        bool isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isLoopbackHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new ArgumentException("BaseUri must use HTTPS for remote instances, or HTTP for localhost/loopback instances.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            throw new ArgumentException("Username cannot be empty or whitespace.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.Role))
        {
            throw new ArgumentException("Role cannot be empty or whitespace.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.KeyAlgorithm))
        {
            throw new ArgumentException("KeyAlgorithm cannot be empty or whitespace.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.PublicKeyPem))
        {
            throw new ArgumentException("PublicKeyPem cannot be empty or whitespace.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.EncryptedPrivateKeyEnvelopeBase64))
        {
            throw new ArgumentException("EncryptedPrivateKeyEnvelopeBase64 cannot be empty or whitespace.", nameof(profile));
        }

        string normalizedUsername = profile.Username.Trim().ToLowerInvariant();
        int clampedInterval = Math.Clamp(profile.PollingIntervalSeconds, 5, 300);
        bool isLocal = uri.IsLoopback || profile.IsLocalInstance;

        return profile with
        {
            Username = normalizedUsername,
            PollingIntervalSeconds = clampedInterval,
            IsLocalInstance = isLocal
        };
    }

    /// <summary>
    /// Maps the current row of an active <see cref="SqliteDataReader"/> to an <see cref="InstanceProfile"/> record.
    /// </summary>
    /// <param name="reader">The active reader positioned on a valid record.</param>
    /// <returns>A mapped <see cref="InstanceProfile"/> record.</returns>
    private static InstanceProfile MapProfile(SqliteDataReader reader)
    {
        // WHAT: Extracts column values from the active reader and maps them to an InstanceProfile record.
        // WHY:  Centralizes database column reading and ISO-8601 timestamp parsing.
        // HOW:  Reads strings and integers by ordinal index, parsing UTC timestamps with roundtrip kind.
        DateTimeOffset addedAt = DateTimeOffset.Parse(
            reader.GetString(11),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        DateTimeOffset? lastConnected = reader.IsDBNull(12)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(12),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

        return new InstanceProfile(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9) != 0,
            reader.GetInt32(10),
            addedAt,
            lastConnected);
    }
}
