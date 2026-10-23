using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Npgsql;
using NanoWeaverHrt.Database.Migrations;

namespace NanoWeaverHrt.Configuration;

/// <summary>
/// Authoritative configuration and single-use share key store implementing database-first precedence.
/// Tries connecting to PostgreSQL (pg-duckdb) if available, and gracefully falls back to an embedded
/// local SQLite database so the stack operates seamlessly with or without Docker containers running.
/// </summary>
public sealed class SystemConfigurationStore : IConfigurationRepository, IDisposable
{
    /// <summary>
    /// Metadata definition for known stack settings and their environment variable mappings.
    /// </summary>
    /// <param name="Key">Canonical setting key (e.g. 'Storage:PostgresConnectionString').</param>
    /// <param name="DefaultValue">Built-in fallback used when neither the database nor the environment supplies a value.</param>
    /// <param name="EnvironmentVariable">Environment variable consulted before the built-in default, or null when the setting has none.</param>
    /// <param name="Category">Categorical grouping used to organize the setting in the Web UI.</param>
    /// <param name="Description">Plain-English explanation of what the setting controls.</param>
    /// <param name="IsSecret">
    /// True when the value carries a credential and must never be rendered, logged, or serialized. This flag is
    /// the single authority consulted by both the REST API redaction and the Web UI, so the two cannot drift.
    /// </param>
    private sealed record ConfigDefinition(
        string Key,
        string DefaultValue,
        string? EnvironmentVariable,
        string Category,
        string Description,
        bool IsSecret = false);

    /// <summary>
    /// Case-insensitive key-name markers identifying a credential-bearing setting that is not in the catalog.
    /// </summary>
    /// <remarks>
    /// Operators may persist ad-hoc database overrides whose keys are absent from <see cref="Definitions"/>.
    /// The markers keep such a key protected by default, so secrecy can only ever be added, never silently lost.
    /// </remarks>
    private static readonly string[] SecretKeyNameMarkers =
    [
        "connectionstring", "password", "secret", "credential", "privatekey", "apikey", "token", "pepper"
    ];

    /// <summary>
    /// Master catalog of supported system settings across simulation, storage, and peer collaboration.
    /// </summary>
    private static readonly ConfigDefinition[] Definitions =
    [
        new("Simulation:EndpointUrl", "http://simulation-node:8080", "SIMULATION_ENDPOINT_URL", "Simulation Engine", "Network URL of the oxDNA / LAMMPS simulation engine or external compute cluster."),
        new("Simulation:AcceleratorBackend", "Intel iGPU / OpenCL (Default)", "ACCELERATOR_BACKEND", "Simulation Engine", "Active hardware accelerator backend (e.g., Arc A770, NVIDIA CUDA, AMD ROCm, or CPU)."),
        new("Simulation:DefaultTimeStep", "0.002", "SIMULATION_TIMESTEP", "Simulation Engine", "Integration time step in picoseconds for molecular dynamics solvers."),
        new("Storage:DatabaseProvider", "AutoDetect", "DATABASE_PROVIDER", "Storage & Database", "Database engine used for metadata ('PostgreSQL', 'SQLite', or 'AutoDetect')."),
        new("Storage:PostgresConnectionString", "", "POSTGRES_CONNECTION_STRING", "Storage & Database", "Connection string for the PostgreSQL / pg-duckdb storage node. Deliberately empty by default: there is no shipped credential, so leaving it unset makes the stack use its embedded SQLite database instead.", IsSecret: true),
        new("Storage:TrajectoryIncomingDir", "./simulations/shared-data", "TRAJECTORY_INCOMING_DIR", "Storage & Database", "Directory containing raw incoming molecular trajectory files (.xyz, .dcd)."),
        new("Storage:TrajectoryExportDir", "./simulations/exports", "TRAJECTORY_EXPORT_DIR", "Storage & Database", "Directory where signed dataset tarballs and SHA-256 manifests are saved."),
        new("Storage:TrajectoryParquetDir", "./simulations/parquet", "TRAJECTORY_PARQUET_DIR", "Storage & Database", "Directory where columnar Apache Parquet files are stored for DuckDB queries."),
        new("Peer:AllowExternalConnections", "true", "PEER_ALLOW_EXTERNAL", "Peer Collaboration", "Enables or disables remote collaborative UI connections via single-use share links."),
        new("Peer:DefaultTokenLifetimeHours", "24", "PEER_DEFAULT_LIFETIME_HOURS", "Peer Collaboration", "Default expiration window for newly generated collaborative share links."),
        new("Peer:CollaborativeWorkerEndpoint", "http://localhost:5000", "COLLABORATIVE_WORKER_ENDPOINT", "Peer Collaboration", "Local node address advertised to external instances in generated share links.")
    ];

    /// <summary>
    /// Underlying active database connection provider ('postgres' or 'sqlite').
    /// </summary>
    private string _activeBackend = "Local SQLite Fallback";

    /// <summary>
    /// Thread synchronization lock for database queries in fallback SQLite mode.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Path to the local fallback SQLite database file.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// Connection string used if PostgreSQL is selected or reachable.
    /// </summary>
    private readonly string _postgresConnString;

    /// <summary>
    /// Migration runner used to execute versioned database schema migrations.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// Path of the currently initialized database file.
    /// </summary>
    private string? _initializedPath;

    /// <summary>
    /// Gets the human-readable name of the active database backend.
    /// </summary>
    public string ActiveBackendName => _activeBackend;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemConfigurationStore"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="migrationRunner">Optional explicit database migration runner.</param>
    public SystemConfigurationStore(IConfiguration configuration, DatabaseMigrationRunner? migrationRunner = null)
    {
        _migrationRunner = migrationRunner ?? new DatabaseMigrationRunner();
        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        // WHAT: Uses the configured PostgreSQL connection string, or no connection string at all.
        // WHY:  Shipping a built-in default that embeds a password means every deployment which
        //       forgets to set POSTGRES_CONNECTION_STRING shares one publicly known credential,
        //       and that credential also leaks through any surface that echoes configuration.
        //       PostgreSQL is therefore opt-in: with nothing configured the stack uses its
        //       embedded SQLite database, exactly as it already does when PostgreSQL is
        //       unreachable. An empty string fails the connection attempt immediately and that
        //       existing fallback path takes over.
        // HOW:  Falls back to the empty string instead of a hardcoded credential.
        _postgresConnString = envPostgres ?? string.Empty;

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <summary>
    /// Initializes database tables and schemas if they do not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initializedPath == _sqlitePath && File.Exists(_sqlitePath)) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_initializedPath == _sqlitePath && File.Exists(_sqlitePath)) return;

            // WHAT: Attempts to connect to PostgreSQL (pg-duckdb); on failure, smoothly falls back to SQLite.
            // WHY:  Ensures that the microservice starts seamlessly in standalone mode or Docker Compose.
            // HOW:  Tries opening an NpgsqlConnection; if it throws a SocketException, marks SQLite as active.
            bool postgresOk = false;
            try
            {
                using NpgsqlConnection pgConn = new(_postgresConnString);
                await pgConn.OpenAsync(ct);
                postgresOk = true;
                _activeBackend = "PostgreSQL (pg-duckdb)";

                await CreateTablesPostgresAsync(pgConn, ct);
                _initializedPath = "postgres";
            }
            catch
            {
                postgresOk = false;
            }

            if (!postgresOk)
            {
                _activeBackend = "Local SQLite Fallback";
                using SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
                await sqliteConn.OpenAsync(ct);
                await CreateTablesSqliteAsync(sqliteConn, ct);
                _initializedPath = _sqlitePath;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Applies database migrations to PostgreSQL.
    /// </summary>
    /// <param name="conn">An already-open PostgreSQL connection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once every pending migration has been applied.</returns>
    private async Task CreateTablesPostgresAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await _migrationRunner.ApplyPendingMigrationsAsync(conn, "postgresql", ct);
    }

    /// <summary>
    /// Applies database migrations to SQLite.
    /// </summary>
    /// <param name="conn">An already-open SQLite connection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once every pending migration has been applied.</returns>
    private async Task CreateTablesSqliteAsync(SqliteConnection conn, CancellationToken ct)
    {
        await _migrationRunner.ApplyPendingMigrationsAsync(conn, "sqlite", ct);
    }

    /// <summary>
    /// Determines whether a configuration key carries a credential that must never be published or rendered.
    /// </summary>
    /// <param name="key">Configuration key such as 'Storage:PostgresConnectionString'.</param>
    /// <returns>True when the key's value must stay inside the node; otherwise false.</returns>
    /// <remarks>
    /// The single authority behind both the REST API redaction (GET /api/configuration) and the Web UI
    /// configuration screen. Catalog entries declare secrecy explicitly through
    /// <see cref="ConfigDefinition.IsSecret"/>; keys outside the catalog fall back to name markers, so the
    /// classification is monotone — a catalog entry can add secrecy but never remove it.
    /// </remarks>
    public static bool IsSecretConfigurationKey(string key)
    {
        // WHAT: Classifies a configuration key as credential-bearing.
        // WHY:  Prime Directive 14 forbids exposing credentials to API clients, and the same value must not be
        //       painted onto an operator's screen either; one classifier keeps both surfaces in agreement.
        // HOW:  Consults the declarative catalog flag first, then the key-name markers for ad-hoc overrides.
        ArgumentNullException.ThrowIfNull(key);

        foreach (ConfigDefinition def in Definitions)
        {
            if (def.IsSecret && string.Equals(def.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string marker in SecretKeyNameMarkers)
        {
            if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retrieves all configuration settings, evaluating database overrides against environment variables and defaults.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of all defined configuration items.</returns>
    public async Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        Dictionary<string, (string Value, DateTime? UpdatedAt)> dbOverrides = await ReadAllDbOverridesAsync(ct);
        List<ConfigurationItem> result = new(Definitions.Length);

        // WHAT: Evaluates configuration precedence for each registered system setting.
        // WHY:  Enforces the rule: "Whatever is in the database wins, stack environment variables do nothing if configuration exists in the database."
        // HOW:  1. Database override -> 2. Environment variable -> 3. Code default.
        foreach (ConfigDefinition def in Definitions)
        {
            if (dbOverrides.TryGetValue(def.Key, out (string Value, DateTime? UpdatedAt) dbVal))
            {
                result.Add(new ConfigurationItem
                {
                    Key = def.Key,
                    Value = dbVal.Value,
                    DefaultValue = def.DefaultValue,
                    Description = def.Description,
                    Category = def.Category,
                    Source = ConfigurationSource.DatabaseOverride,
                    LastUpdatedUtc = dbVal.UpdatedAt,
                    IsSecret = def.IsSecret
                });
            }
            else
            {
                string? envVal = def.EnvironmentVariable != null ? Environment.GetEnvironmentVariable(def.EnvironmentVariable) : null;
                if (!string.IsNullOrEmpty(envVal))
                {
                    result.Add(new ConfigurationItem
                    {
                        Key = def.Key,
                        Value = envVal,
                        DefaultValue = def.DefaultValue,
                        Description = def.Description,
                        Category = def.Category,
                        Source = ConfigurationSource.EnvironmentVariable,
                        LastUpdatedUtc = null,
                        IsSecret = def.IsSecret
                    });
                }
                else
                {
                    result.Add(new ConfigurationItem
                    {
                        Key = def.Key,
                        Value = def.DefaultValue,
                        DefaultValue = def.DefaultValue,
                        Description = def.Description,
                        Category = def.Category,
                        Source = ConfigurationSource.DefaultFallback,
                        LastUpdatedUtc = null,
                        IsSecret = def.IsSecret
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Retrieves the effective value for a specific configuration key, prioritizing database overrides first.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="defaultValue">The fallback value if neither database nor environment defines one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective configuration value.</returns>
    public async Task<string> GetValueAsync(string key, string defaultValue = "", CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        string? dbVal = await ReadSingleDbOverrideAsync(key, ct);
        if (dbVal != null)
        {
            // Database wins!
            return dbVal;
        }

        ConfigDefinition? def = Array.Find(Definitions, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        if (def?.EnvironmentVariable != null)
        {
            string? envVal = Environment.GetEnvironmentVariable(def.EnvironmentVariable);
            if (!string.IsNullOrEmpty(envVal))
            {
                return envVal;
            }
        }

        return def?.DefaultValue ?? defaultValue;
    }

    /// <summary>
    /// Writes or updates a configuration setting in the database, establishing it as the authoritative override.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="value">The new value to persist.</param>
    /// <param name="description">Optional description of the setting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the database write operation.</returns>
    public async Task SetDatabaseOverrideAsync(string key, string value, string description = "", CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            DateTime now = DateTime.UtcNow;

            if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                using NpgsqlConnection conn = new(_postgresConnString);
                await conn.OpenAsync(ct);
                const string sql = @"
                    INSERT INTO weaver_configuration (key, value, description, updated_at_utc)
                    VALUES (@key, @value, @desc, @updated)
                    ON CONFLICT (key) DO UPDATE
                    SET value = EXCLUDED.value,
                        description = EXCLUDED.description,
                        updated_at_utc = EXCLUDED.updated_at_utc;
                ";
                using NpgsqlCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@updated", now);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                using SqliteConnection conn = new($"Data Source={_sqlitePath}");
                await conn.OpenAsync(ct);
                const string sql = @"
                    INSERT INTO weaver_configuration (key, value, description, updated_at_utc)
                    VALUES (@key, @value, @desc, @updated)
                    ON CONFLICT(key) DO UPDATE
                    SET value = excluded.value,
                        description = excluded.description,
                        updated_at_utc = excluded.updated_at_utc;
                ";
                using SqliteCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@updated", now.ToString("o"));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes a configuration override from the database, allowing the system to revert to environment variables or defaults.
    /// </summary>
    /// <param name="key">The configuration key name to revert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the deletion operation.</returns>
    public async Task RevertToDefaultAsync(string key, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                using NpgsqlConnection conn = new(_postgresConnString);
                await conn.OpenAsync(ct);
                using NpgsqlCommand cmd = new("DELETE FROM weaver_configuration WHERE key = @key", conn);
                cmd.Parameters.AddWithValue("@key", key);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                using SqliteConnection conn = new($"Data Source={_sqlitePath}");
                await conn.OpenAsync(ct);
                using SqliteCommand cmd = new("DELETE FROM weaver_configuration WHERE key = @key", conn);
                cmd.Parameters.AddWithValue("@key", key);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Generates a cryptographically secure, single-use share key and persists it to the database.
    /// </summary>
    /// <param name="label">Descriptive label identifying the intended collaborator or node.</param>
    /// <param name="lifetime">Duration before the invitation expires.</param>
    /// <param name="permissions">Access level granted upon redemption.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created share key record.</returns>
    public async Task<ShareKeyRecord> CreateShareKeyAsync(string label, TimeSpan lifetime, string permissions = "FullCollaborator", CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        // WHAT: Generates a high-entropy 256-bit cryptographic token.
        // WHY:  Prevents token enumeration and guarantees sovereign peer authentication.
        // HOW:  Uses RandomNumberGenerator and encodes as an alphanumeric URL-safe string.
        byte[] entropy = new byte[32];
        RandomNumberGenerator.Fill(entropy);
        string keyToken = "nanoweaver_hrt_" + Convert.ToHexString(entropy).ToLowerInvariant();
        string tokenId = Guid.NewGuid().ToString("N")[..12];

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.Add(lifetime);

        ShareKeyRecord record = new()
        {
            TokenId = tokenId,
            KeyToken = keyToken,
            Label = label,
            CreatedAtUtc = now,
            ExpiresAtUtc = expires,
            IsConsumed = false,
            ConsumedAtUtc = null,
            RemotePeerInfo = null,
            Permissions = permissions
        };

        await _lock.WaitAsync(ct);
        try
        {
            if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                using NpgsqlConnection conn = new(_postgresConnString);
                await conn.OpenAsync(ct);
                const string sql = @"
                    INSERT INTO weaver_share_keys (token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, permissions)
                    VALUES (@id, @tok, @lbl, @created, @expires, FALSE, @perm);
                ";
                using NpgsqlCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@id", record.TokenId);
                cmd.Parameters.AddWithValue("@tok", record.KeyToken);
                cmd.Parameters.AddWithValue("@lbl", record.Label);
                cmd.Parameters.AddWithValue("@created", record.CreatedAtUtc);
                cmd.Parameters.AddWithValue("@expires", record.ExpiresAtUtc);
                cmd.Parameters.AddWithValue("@perm", record.Permissions);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                using SqliteConnection conn = new($"Data Source={_sqlitePath}");
                await conn.OpenAsync(ct);
                const string sql = @"
                    INSERT INTO weaver_share_keys (token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, permissions)
                    VALUES (@id, @tok, @lbl, @created, @expires, 0, @perm);
                ";
                using SqliteCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@id", record.TokenId);
                cmd.Parameters.AddWithValue("@tok", record.KeyToken);
                cmd.Parameters.AddWithValue("@lbl", record.Label);
                cmd.Parameters.AddWithValue("@created", record.CreatedAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@expires", record.ExpiresAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@perm", record.Permissions);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }

        return record;
    }

    /// <summary>
    /// Validates and atomically consumes a single-use share key, establishing an authenticated peer connection.
    /// </summary>
    /// <param name="keyToken">The secret token provided by the external connecting instance.</param>
    /// <param name="remotePeerInfo">Network address or client metadata of the connecting instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The redemption outcome including session credentials if successful.</returns>
    public async Task<ShareKeyConsumeResult> ConsumeShareKeyAsync(string keyToken, string remotePeerInfo, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            ShareKeyRecord? existing = await FindKeyByTokenAsync(keyToken, ct);
            if (existing == null)
            {
                return new ShareKeyConsumeResult
                {
                    Success = false,
                    Message = "Share key token not recognized or does not exist."
                };
            }

            if (existing.IsConsumed)
            {
                return new ShareKeyConsumeResult
                {
                    Success = false,
                    Message = $"Single-use share key was already consumed at {existing.ConsumedAtUtc:u} by {existing.RemotePeerInfo}."
                };
            }

            if (!existing.IsNeverExpires && DateTime.UtcNow > existing.ExpiresAtUtc)
            {
                return new ShareKeyConsumeResult
                {
                    Success = false,
                    Message = $"Share key expired at {existing.ExpiresAtUtc:u}."
                };
            }

            // WHAT: Marks the single-use key as consumed atomically in the database.
            // WHY:  Guarantees each share link can only be used once, preventing replay attacks.
            // HOW:  Updates is_consumed = true and records peer metadata.
            DateTime now = DateTime.UtcNow;
            string sessionToken = "peer_sess_" + Guid.NewGuid().ToString("N");

            if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                using NpgsqlConnection conn = new(_postgresConnString);
                await conn.OpenAsync(ct);
                const string sql = @"
                    UPDATE weaver_share_keys
                    SET is_consumed = TRUE,
                        consumed_at_utc = @now,
                        remote_peer_info = @peer
                    WHERE token_id = @id AND is_consumed = FALSE;
                ";
                using NpgsqlCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.Parameters.AddWithValue("@peer", remotePeerInfo);
                cmd.Parameters.AddWithValue("@id", existing.TokenId);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                {
                    return new ShareKeyConsumeResult { Success = false, Message = "Concurrent consumption detected." };
                }
            }
            else
            {
                using SqliteConnection conn = new($"Data Source={_sqlitePath}");
                await conn.OpenAsync(ct);
                const string sql = @"
                    UPDATE weaver_share_keys
                    SET is_consumed = 1,
                        consumed_at_utc = @now,
                        remote_peer_info = @peer
                    WHERE token_id = @id AND is_consumed = 0;
                ";
                using SqliteCommand cmd = new(sql, conn);
                cmd.Parameters.AddWithValue("@now", now.ToString("o"));
                cmd.Parameters.AddWithValue("@peer", remotePeerInfo);
                cmd.Parameters.AddWithValue("@id", existing.TokenId);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                {
                    return new ShareKeyConsumeResult { Success = false, Message = "Concurrent consumption detected." };
                }
            }

            ShareKeyRecord updatedRecord = existing with
            {
                IsConsumed = true,
                ConsumedAtUtc = now,
                RemotePeerInfo = remotePeerInfo
            };

            return new ShareKeyConsumeResult
            {
                Success = true,
                Message = "Single-use share key successfully validated and consumed. Collaborative session established.",
                Record = updatedRecord,
                PeerSessionToken = sessionToken
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Retrieves all generated share keys, including active, consumed, and expired keys.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of share key records.</returns>
    public async Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        List<ShareKeyRecord> records = [];

        if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            using NpgsqlConnection conn = new(_postgresConnString);
            await conn.OpenAsync(ct);
            const string sql = "SELECT token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, consumed_at_utc, remote_peer_info, permissions FROM weaver_share_keys ORDER BY created_at_utc DESC;";
            using NpgsqlCommand cmd = new(sql, conn);
            using NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                records.Add(new ShareKeyRecord
                {
                    TokenId = rdr.GetString(0),
                    KeyToken = rdr.GetString(1),
                    Label = rdr.GetString(2),
                    CreatedAtUtc = rdr.GetDateTime(3),
                    ExpiresAtUtc = rdr.GetDateTime(4),
                    IsConsumed = rdr.GetBoolean(5),
                    ConsumedAtUtc = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
                    RemotePeerInfo = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    Permissions = rdr.GetString(8)
                });
            }
        }
        else
        {
            using SqliteConnection conn = new($"Data Source={_sqlitePath}");
            await conn.OpenAsync(ct);
            const string sql = "SELECT token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, consumed_at_utc, remote_peer_info, permissions FROM weaver_share_keys ORDER BY created_at_utc DESC;";
            using SqliteCommand cmd = new(sql, conn);
            using SqliteDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                records.Add(new ShareKeyRecord
                {
                    TokenId = rdr.GetString(0),
                    KeyToken = rdr.GetString(1),
                    Label = rdr.GetString(2),
                    CreatedAtUtc = DateTime.Parse(rdr.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    ExpiresAtUtc = DateTime.Parse(rdr.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    IsConsumed = rdr.GetInt32(5) != 0,
                    ConsumedAtUtc = rdr.IsDBNull(6) ? null : DateTime.Parse(rdr.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    RemotePeerInfo = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    Permissions = rdr.GetString(8)
                });
            }
        }

        return records;
    }

    /// <summary>
    /// Manually revokes an active share key before its natural expiration.
    /// </summary>
    /// <param name="tokenId">Unique identifier of the token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the token was found and revoked, false otherwise.</returns>
    public async Task<bool> RevokeShareKeyAsync(string tokenId, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            DateTime past = DateTime.UtcNow.AddMinutes(-1);
            if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                using NpgsqlConnection conn = new(_postgresConnString);
                await conn.OpenAsync(ct);
                using NpgsqlCommand cmd = new("UPDATE weaver_share_keys SET is_consumed = TRUE, remote_peer_info = 'REVOKED_MANUALLY' WHERE token_id = @id;", conn);
                cmd.Parameters.AddWithValue("@id", tokenId);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
            else
            {
                using SqliteConnection conn = new($"Data Source={_sqlitePath}");
                await conn.OpenAsync(ct);
                using SqliteCommand cmd = new("UPDATE weaver_share_keys SET is_consumed = 1, remote_peer_info = 'REVOKED_MANUALLY' WHERE token_id = @id;", conn);
                cmd.Parameters.AddWithValue("@id", tokenId);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reads all database overrides into an in-memory dictionary.
    /// </summary>
    private async Task<Dictionary<string, (string Value, DateTime? UpdatedAt)>> ReadAllDbOverridesAsync(CancellationToken ct)
    {
        Dictionary<string, (string Value, DateTime? UpdatedAt)> map = new(StringComparer.OrdinalIgnoreCase);

        if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            using NpgsqlConnection conn = new(_postgresConnString);
            await conn.OpenAsync(ct);
            using NpgsqlCommand cmd = new("SELECT key, value, updated_at_utc FROM weaver_configuration;", conn);
            using NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                map[rdr.GetString(0)] = (rdr.GetString(1), rdr.IsDBNull(2) ? null : rdr.GetDateTime(2));
            }
        }
        else
        {
            using SqliteConnection conn = new($"Data Source={_sqlitePath}");
            await conn.OpenAsync(ct);
            using SqliteCommand cmd = new("SELECT key, value, updated_at_utc FROM weaver_configuration;", conn);
            using SqliteDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                DateTime? dt = rdr.IsDBNull(2) ? null : DateTime.Parse(rdr.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
                map[rdr.GetString(0)] = (rdr.GetString(1), dt);
            }
        }

        return map;
    }

    /// <summary>
    /// Reads a single database override for the specified key.
    /// </summary>
    /// <param name="key">The canonical setting name to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored override value, or null when the database holds no row for the key.</returns>
    private async Task<string?> ReadSingleDbOverrideAsync(string key, CancellationToken ct)
    {
        if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            using NpgsqlConnection conn = new(_postgresConnString);
            await conn.OpenAsync(ct);
            using NpgsqlCommand cmd = new("SELECT value FROM weaver_configuration WHERE key = @key;", conn);
            cmd.Parameters.AddWithValue("@key", key);
            object? val = await cmd.ExecuteScalarAsync(ct);
            return val?.ToString();
        }
        else
        {
            using SqliteConnection conn = new($"Data Source={_sqlitePath}");
            await conn.OpenAsync(ct);
            using SqliteCommand cmd = new("SELECT value FROM weaver_configuration WHERE key = @key;", conn);
            cmd.Parameters.AddWithValue("@key", key);
            object? val = await cmd.ExecuteScalarAsync(ct);
            return val?.ToString();
        }
    }

    /// <summary>
    /// Finds a share key record by its token string.
    /// </summary>
    /// <param name="token">The single-use share token presented by a collaborating peer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching share key record, or null when no key carries that token.</returns>
    private async Task<ShareKeyRecord?> FindKeyByTokenAsync(string token, CancellationToken ct)
    {
        if (_activeBackend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            using NpgsqlConnection conn = new(_postgresConnString);
            await conn.OpenAsync(ct);
            const string sql = "SELECT token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, consumed_at_utc, remote_peer_info, permissions FROM weaver_share_keys WHERE key_token = @tok LIMIT 1;";
            using NpgsqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@tok", token);
            using NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
            {
                return new ShareKeyRecord
                {
                    TokenId = rdr.GetString(0),
                    KeyToken = rdr.GetString(1),
                    Label = rdr.GetString(2),
                    CreatedAtUtc = rdr.GetDateTime(3),
                    ExpiresAtUtc = rdr.GetDateTime(4),
                    IsConsumed = rdr.GetBoolean(5),
                    ConsumedAtUtc = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
                    RemotePeerInfo = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    Permissions = rdr.GetString(8)
                };
            }
        }
        else
        {
            using SqliteConnection conn = new($"Data Source={_sqlitePath}");
            await conn.OpenAsync(ct);
            const string sql = "SELECT token_id, key_token, label, created_at_utc, expires_at_utc, is_consumed, consumed_at_utc, remote_peer_info, permissions FROM weaver_share_keys WHERE key_token = @tok LIMIT 1;";
            using SqliteCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@tok", token);
            using SqliteDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
            {
                return new ShareKeyRecord
                {
                    TokenId = rdr.GetString(0),
                    KeyToken = rdr.GetString(1),
                    Label = rdr.GetString(2),
                    CreatedAtUtc = DateTime.Parse(rdr.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    ExpiresAtUtc = DateTime.Parse(rdr.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    IsConsumed = rdr.GetInt32(5) != 0,
                    ConsumedAtUtc = rdr.IsDBNull(6) ? null : DateTime.Parse(rdr.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    RemotePeerInfo = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    Permissions = rdr.GetString(8)
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Releases disposable resources.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
        _migrationRunner.Dispose();
    }
}
