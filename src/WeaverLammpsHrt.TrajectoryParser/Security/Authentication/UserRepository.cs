using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;
using WeaverLammpsHrt.TrajectoryParser.Configuration;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Authentication;

/// <summary>
/// Production repository persisting user accounts across PostgreSQL (pg-duckdb) with embedded SQLite fallback.
/// </summary>
public sealed class UserRepository : IUserRepository, IDisposable
{
    /// <summary>
    /// Thread synchronization lock for SQLite operations.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Local fallback SQLite database path.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    private readonly string _postgresConnString;

    /// <summary>
    /// Configuration repository used to discover active backend name.
    /// </summary>
    private readonly IConfigurationRepository _configRepo;

    /// <summary>
    /// Flag indicating if user tables have been created and verified.
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRepository"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="configRepo">Authoritative configuration repository.</param>
    public UserRepository(IConfiguration configuration, IConfigurationRepository configRepo)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configRepo = configRepo ?? throw new ArgumentNullException(nameof(configRepo));

        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        _postgresConnString = envPostgres ?? "Host=localhost;Port=5432;Database=weaver_hrt;Username=weaver;Password=weaver_secret_change_me;Timeout=3";

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_isInitialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isInitialized) return;

            // WHAT: Ensures weaver_users table exists in the active database engine.
            // WHY:  Stores user accounts, roles, access gates, and public keys securely.
            // HOW:  Checks active backend in IConfigurationRepository and creates schema accordingly.
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);

            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection pgConn = new(_postgresConnString);
                    await pgConn.OpenAsync(ct);
                    await CreateTablesPostgresAsync(pgConn, ct);
                }
                catch
                {
                    // Fall back to SQLite if PostgreSQL fails
                    using SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
                    await sqliteConn.OpenAsync(ct);
                    await CreateTablesSqliteAsync(sqliteConn, ct);
                }
            }
            else
            {
                using SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
                await sqliteConn.OpenAsync(ct);
                await CreateTablesSqliteAsync(sqliteConn, ct);
            }

            _isInitialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HasAnyAdministratorAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    using NpgsqlCommand cmd = new("SELECT COUNT(*) FROM weaver_users WHERE role = 'Administrator' AND is_active = TRUE", conn);
                    object? res = await cmd.ExecuteScalarAsync(ct);
                    return Convert.ToInt64(res) > 0;
                }
                catch
                {
                    // If PostgreSQL connection fails, try fallback
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            using SqliteCommand sqliteCmd = new("SELECT COUNT(*) FROM weaver_users WHERE role = 'Administrator' AND is_active = 1", sqlite);
            object? sRes = await sqliteCmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(sRes) > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<UserAccount?> GetUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    const string sql = "SELECT username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active FROM weaver_users WHERE username = @u";
                    using NpgsqlCommand cmd = new(sql, conn);
                    cmd.Parameters.AddWithValue("@u", username);
                    using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        return MapUserFromReader(reader);
                    }
                    return null;
                }
                catch
                {
                    // Fall back to SQLite if PostgreSQL fails
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            const string sqliteSql = "SELECT username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active FROM weaver_users WHERE username = @u";
            using SqliteCommand sqliteCmd = new(sqliteSql, sqlite);
            sqliteCmd.Parameters.AddWithValue("@u", username);
            using SqliteDataReader sReader = await sqliteCmd.ExecuteReaderAsync(ct);
            if (await sReader.ReadAsync(ct))
            {
                return MapUserFromReader(sReader);
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);
        List<UserAccount> users = new();

        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    const string sql = "SELECT username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active FROM weaver_users ORDER BY username ASC";
                    using NpgsqlCommand cmd = new(sql, conn);
                    using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        users.Add(MapUserFromReader(reader));
                    }
                    return users;
                }
                catch
                {
                    // Fall back to SQLite
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            const string sqliteSql = "SELECT username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active FROM weaver_users ORDER BY username ASC";
            using SqliteCommand sqliteCmd = new(sqliteSql, sqlite);
            using SqliteDataReader sReader = await sqliteCmd.ExecuteReaderAsync(ct);
            while (await sReader.ReadAsync(ct))
            {
                users.Add(MapUserFromReader(sReader));
            }
            return users;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CreateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.PublicKeyBase64);

        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    const string sql = @"
                        INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active)
                        VALUES (@u, @r, @g, @a, @p, @c, @l, @act)
                        ON CONFLICT (username) DO UPDATE
                        SET role = EXCLUDED.role,
                            access_gate = EXCLUDED.access_gate,
                            key_algorithm = EXCLUDED.key_algorithm,
                            public_key = EXCLUDED.public_key,
                            is_active = EXCLUDED.is_active;
                    ";
                    using NpgsqlCommand cmd = new(sql, conn);
                    cmd.Parameters.AddWithValue("@u", user.Username);
                    cmd.Parameters.AddWithValue("@r", user.Role.ToString());
                    cmd.Parameters.AddWithValue("@g", user.AccessGate.ToString());
                    cmd.Parameters.AddWithValue("@a", user.Algorithm.ToString());
                    cmd.Parameters.AddWithValue("@p", user.PublicKeyBase64);
                    cmd.Parameters.AddWithValue("@c", user.CreatedAtUtc.UtcDateTime);
                    cmd.Parameters.AddWithValue("@l", (object?)user.LastLoginUtc?.UtcDateTime ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@act", user.IsActive);
                    await cmd.ExecuteNonQueryAsync(ct);
                    return;
                }
                catch
                {
                    // Fall back to SQLite
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            const string sqliteSql = @"
                INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active)
                VALUES (@u, @r, @g, @a, @p, @c, @l, @act)
                ON CONFLICT(username) DO UPDATE
                SET role = excluded.role,
                    access_gate = excluded.access_gate,
                    key_algorithm = excluded.key_algorithm,
                    public_key = excluded.public_key,
                    is_active = excluded.is_active;
            ";
            using SqliteCommand sqliteCmd = new(sqliteSql, sqlite);
            sqliteCmd.Parameters.AddWithValue("@u", user.Username);
            sqliteCmd.Parameters.AddWithValue("@r", user.Role.ToString());
            sqliteCmd.Parameters.AddWithValue("@g", user.AccessGate.ToString());
            sqliteCmd.Parameters.AddWithValue("@a", user.Algorithm.ToString());
            sqliteCmd.Parameters.AddWithValue("@p", user.PublicKeyBase64);
            sqliteCmd.Parameters.AddWithValue("@c", user.CreatedAtUtc.ToString("o"));
            sqliteCmd.Parameters.AddWithValue("@l", (object?)user.LastLoginUtc?.ToString("o") ?? DBNull.Value);
            sqliteCmd.Parameters.AddWithValue("@act", user.IsActive ? 1 : 0);
            await sqliteCmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpdateLastLoginAsync(string username, DateTimeOffset loginTimeUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    using NpgsqlCommand cmd = new("UPDATE weaver_users SET last_login_utc = @l WHERE username = @u", conn);
                    cmd.Parameters.AddWithValue("@l", loginTimeUtc.UtcDateTime);
                    cmd.Parameters.AddWithValue("@u", username);
                    await cmd.ExecuteNonQueryAsync(ct);
                    return;
                }
                catch
                {
                    // Fall back
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            using SqliteCommand sqliteCmd = new("UPDATE weaver_users SET last_login_utc = @l WHERE username = @u", sqlite);
            sqliteCmd.Parameters.AddWithValue("@l", loginTimeUtc.ToString("o"));
            sqliteCmd.Parameters.AddWithValue("@u", username);
            await sqliteCmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await InitializeAsync(ct);
        await _lock.WaitAsync(ct);

        try
        {
            bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
            if (isPostgres)
            {
                try
                {
                    using NpgsqlConnection conn = new(_postgresConnString);
                    await conn.OpenAsync(ct);
                    using NpgsqlCommand cmd = new("DELETE FROM weaver_users WHERE username = @u", conn);
                    cmd.Parameters.AddWithValue("@u", username);
                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    return affected > 0;
                }
                catch
                {
                    // Fall back
                }
            }

            using SqliteConnection sqlite = new($"Data Source={_sqlitePath}");
            await sqlite.OpenAsync(ct);
            using SqliteCommand sqliteCmd = new("DELETE FROM weaver_users WHERE username = @u", sqlite);
            sqliteCmd.Parameters.AddWithValue("@u", username);
            int sAffected = await sqliteCmd.ExecuteNonQueryAsync(ct);
            return sAffected > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates schema tables in PostgreSQL.
    /// </summary>
    private static async Task CreateTablesPostgresAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS weaver_users (
                username VARCHAR(64) PRIMARY KEY,
                role VARCHAR(32) NOT NULL,
                access_gate VARCHAR(32) NOT NULL,
                key_algorithm VARCHAR(32) NOT NULL,
                public_key TEXT NOT NULL,
                created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                last_login_utc TIMESTAMP,
                is_active BOOLEAN NOT NULL DEFAULT TRUE
            );
        ";
        using NpgsqlCommand cmd = new(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Creates schema tables in SQLite.
    /// </summary>
    private static async Task CreateTablesSqliteAsync(SqliteConnection conn, CancellationToken ct)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS weaver_users (
                username TEXT PRIMARY KEY,
                role TEXT NOT NULL,
                access_gate TEXT NOT NULL,
                key_algorithm TEXT NOT NULL,
                public_key TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_login_utc TEXT,
                is_active INTEGER NOT NULL DEFAULT 1
            );
        ";
        using SqliteCommand cmd = new(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Maps a database reader record into a strongly-typed <see cref="UserAccount"/>.
    /// </summary>
    internal static UserAccount MapUserFromReader(DbDataReader reader)
    {
        string username = reader.GetString(0);
        string roleStr = reader.GetString(1);
        string gateStr = reader.GetString(2);
        string algoStr = reader.GetString(3);
        string pubKey = reader.GetString(4);

        DateTimeOffset created;
        if (reader.GetFieldType(5) == typeof(DateTime))
        {
            created = new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero);
        }
        else
        {
            created = DateTimeOffset.Parse(reader.GetString(5));
        }

        DateTimeOffset? lastLogin = null;
        if (!reader.IsDBNull(6))
        {
            if (reader.GetFieldType(6) == typeof(DateTime))
            {
                lastLogin = new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero);
            }
            else
            {
                lastLogin = DateTimeOffset.Parse(reader.GetString(6));
            }
        }

        bool isActive;
        if (reader.GetFieldType(7) == typeof(bool))
        {
            isActive = reader.GetBoolean(7);
        }
        else
        {
            isActive = reader.GetInt64(7) != 0;
        }

        Enum.TryParse(roleStr, ignoreCase: true, out UserRole role);
        Enum.TryParse(gateStr, ignoreCase: true, out AccessGate gate);
        Enum.TryParse(algoStr, ignoreCase: true, out KeyAlgorithm algo);

        return new UserAccount
        {
            Username = username,
            Role = role,
            AccessGate = gate,
            Algorithm = algo,
            PublicKeyBase64 = pubKey,
            CreatedAtUtc = created,
            LastLoginUtc = lastLogin,
            IsActive = isActive
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
