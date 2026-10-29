using System.Data.Common;
using System.Globalization;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Localization;
using Npgsql;

namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Multi-engine persistence service managing persistent user language and locale preferences,
/// backed by SQLite (local client), PostgreSQL (centralized/enterprise node), DuckDB (columnar), or MySQL (clustered).
/// </summary>
public sealed class LanguagePreferenceService : ILanguagePreferenceService, IDisposable
{
    /// <summary>
    /// Schema creation DDL for SQLite preferences table.
    /// </summary>
    private const string SqliteSchemaDdl =
        """
        CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
            preference_key TEXT PRIMARY KEY NOT NULL,
            preference_value TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Schema creation DDL for PostgreSQL preferences table.
    /// </summary>
    private const string PostgresSchemaDdl =
        """
        CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
            preference_key VARCHAR(128) PRIMARY KEY NOT NULL,
            preference_value TEXT NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );
        """;

    /// <summary>
    /// Schema creation DDL for DuckDB preferences table.
    /// </summary>
    private const string DuckDbSchemaDdl =
        "CREATE TABLE IF NOT EXISTS weaver_gui_preferences (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP);";

    /// <summary>
    /// Schema creation DDL for MySQL preferences table.
    /// </summary>
    private const string MySqlSchemaDdl =
        "CREATE TABLE IF NOT EXISTS weaver_gui_preferences (key VARCHAR(128) PRIMARY KEY, value VARCHAR(512) NOT NULL, updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6));";

    /// <summary>
    /// Upsert DML statement for DuckDB preferences.
    /// </summary>
    private const string DuckDbUpsertDml =
        "INSERT INTO weaver_gui_preferences (key, value, updated_at_utc) VALUES (@k, @v, @t) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at_utc = EXCLUDED.updated_at_utc;";

    /// <summary>
    /// Upsert DML statement for MySQL preferences.
    /// </summary>
    private const string MySqlUpsertDml =
        "INSERT INTO weaver_gui_preferences (key, value, updated_at_utc) VALUES (@k, @v, @t) ON DUPLICATE KEY UPDATE value = VALUES(value), updated_at_utc = VALUES(updated_at_utc);";

    /// <summary>
    /// Key used to store language preference in the database.
    /// </summary>
    private const string LanguageKey = "ui.language";

    /// <summary>
    /// Database connection string for the preferences storage engine.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Indicates whether the target storage engine is PostgreSQL (<c>true</c>) or SQLite (<c>false</c>).
    /// </summary>
    private readonly bool _isPostgreSql;

    /// <summary>
    /// The resolved database backend for preferences storage.
    /// </summary>
    private readonly WeaverDatabaseBackend _backend;

    /// <summary>
    /// Optional connection factory used for testing connection branches without an external database server.
    /// </summary>
    private readonly Func<string, DbConnection>? _connectionFactory;

    /// <summary>
    /// Synchronization primitive for one-time schema initialization.
    /// </summary>
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Indicates whether the database schema has been verified and initialized.
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// Gets a value indicating whether this preference service is configured for PostgreSQL storage.
    /// </summary>
    public bool IsPostgreSql => _isPostgreSql;

    /// <summary>
    /// Gets a value indicating whether this preference service is configured for DuckDB storage.
    /// </summary>
    public bool IsDuckDb => _backend == WeaverDatabaseBackend.DuckDb;

    /// <summary>
    /// Gets a value indicating whether this preference service is configured for MySQL storage.
    /// </summary>
    public bool IsMySql => _backend == WeaverDatabaseBackend.MySql;

    /// <summary>
    /// Gets the target database backend engine for this preference store.
    /// </summary>
    public WeaverDatabaseBackend Backend => _backend;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class using the specified connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite, PostgreSQL, DuckDB, or MySQL database connection string.</param>
    public LanguagePreferenceService(string connectionString)
        : this(connectionString, ResolveBackend(connectionString), connectionFactory: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class with a custom connection factory for testing.
    /// </summary>
    /// <param name="connectionString">The SQLite or PostgreSQL database connection string.</param>
    /// <param name="connectionFactory">Factory generating database connections.</param>
    internal LanguagePreferenceService(string connectionString, Func<string, DbConnection> connectionFactory)
        : this(connectionString, ResolveBackend(connectionString), connectionFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class with an explicit backend and optional connection factory.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="backend">The explicit database backend.</param>
    /// <param name="connectionFactory">Optional factory generating database connections.</param>
    public LanguagePreferenceService(string connectionString, WeaverDatabaseBackend backend, Func<string, DbConnection>? connectionFactory = null)
    {
        // WHAT: Validates and assigns the database connection string, backend dialect, and optional connection factory.
        // WHY:  Permits client to connect to SQLite, PostgreSQL, DuckDB, or MySQL preference stores.
        // HOW:  Guards against null or whitespace, stores backend, computes IsPostgreSql flag for backward compatibility.
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _backend = backend;
        _isPostgreSql = backend == WeaverDatabaseBackend.PostgreSql;
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class using the default application data storage path.
    /// </summary>
    public LanguagePreferenceService() : this(GetDefaultConnectionString())
    {
    }

    /// <summary>
    /// Ensures that the preferences schema table has been created on the configured database engine.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Creates the weaver_gui_preferences table on the active backend if not already present.
        // WHY:  Guarantees tables exist before executing read/write commands on SQLite, PostgreSQL, DuckDB, or MySQL.
        // HOW:  Acquires SemaphoreSlim lock, opens a DbConnection, and executes engine-specific DDL.
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            {
                await using DbConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
                await using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = _backend switch
                {
                    WeaverDatabaseBackend.DuckDb => DuckDbSchemaDdl,
                    WeaverDatabaseBackend.MySql => MySqlSchemaDdl,
                    WeaverDatabaseBackend.PostgreSql => PostgresSchemaDdl,
                    _ => SqliteSchemaDdl
                };
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<WeaverLanguage> GetPreferredLanguageAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves persistent user language preference from SQLite, PostgreSQL, DuckDB, or MySQL.
        // WHY:  Restores user's chosen UI locale on application launch regardless of storage engine.
        // HOW:  Initializes schema, executes parameterized SELECT for LanguageKey, parses WeaverLanguage enum, defaulting to English.
        await InitializeAsync(ct).ConfigureAwait(false);

        WeaverLanguage resolved = WeaverLanguage.English;
        {
            await using DbConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            bool isNewSchema = _backend is WeaverDatabaseBackend.DuckDb or WeaverDatabaseBackend.MySql;
            cmd.CommandText = isNewSchema
                ? "SELECT value FROM weaver_gui_preferences WHERE key = @k;"
                : "SELECT preference_value FROM weaver_gui_preferences WHERE preference_key = @key;";

            AddParameter(cmd, isNewSchema ? "@k" : "@key", LanguageKey);

            object? result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

            if (result is string value && Enum.TryParse<WeaverLanguage>(value, ignoreCase: true, out WeaverLanguage parsed))
            {
                resolved = parsed;
            }
        }

        return resolved;
    }

    /// <inheritdoc/>
    public async ValueTask SetPreferredLanguageAsync(WeaverLanguage language, CancellationToken ct = default)
    {
        // WHAT: Persists the selected WeaverLanguage preference across SQLite, PostgreSQL, DuckDB, or MySQL.
        // WHY:  Stores user preference across application sessions on the configured storage backend.
        // HOW:  Initializes schema, formats timestamp, and executes engine-compatible upsert statement.
        await InitializeAsync(ct).ConfigureAwait(false);

        string value = language.ToString();
        string updatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        {
            await using DbConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();

            if (_backend == WeaverDatabaseBackend.DuckDb)
            {
                cmd.CommandText = DuckDbUpsertDml;
                AddParameter(cmd, "@k", LanguageKey);
                AddParameter(cmd, "@v", value);
                AddParameter(cmd, "@t", DateTime.UtcNow);
            }
            else if (_backend == WeaverDatabaseBackend.MySql)
            {
                cmd.CommandText = MySqlUpsertDml;
                AddParameter(cmd, "@k", LanguageKey);
                AddParameter(cmd, "@v", value);
                AddParameter(cmd, "@t", DateTime.UtcNow);
            }
            else if (_backend == WeaverDatabaseBackend.PostgreSql)
            {
                cmd.CommandText =
                    """
                    INSERT INTO weaver_gui_preferences (preference_key, preference_value, updated_at_utc)
                    VALUES (@key, @value, @updatedAt)
                    ON CONFLICT (preference_key) DO UPDATE SET
                        preference_value = EXCLUDED.preference_value,
                        updated_at_utc = EXCLUDED.updated_at_utc;
                    """;
                AddParameter(cmd, "@key", LanguageKey);
                AddParameter(cmd, "@value", value);
                AddParameter(cmd, "@updatedAt", DateTimeOffset.UtcNow);
            }
            else
            {
                cmd.CommandText =
                    """
                    INSERT INTO weaver_gui_preferences (preference_key, preference_value, updated_at_utc)
                    VALUES (@key, @value, @updatedAt)
                    ON CONFLICT (preference_key) DO UPDATE SET
                        preference_value = EXCLUDED.preference_value,
                        updated_at_utc = EXCLUDED.updated_at_utc;
                    """;
                AddParameter(cmd, "@key", LanguageKey);
                AddParameter(cmd, "@value", value);
                AddParameter(cmd, "@updatedAt", updatedAt);
            }

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases unmanaged resources used by the service.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Disposes the synchronization semaphore.
        // WHY:  Prevents resource leaks upon teardown.
        // HOW:  Calls Dispose on _initLock.
        _initLock.Dispose();
    }

    /// <summary>
    /// Opens and returns a new database connection for the target storage backend.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open database connection.</returns>
    private async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        // WHAT: Instantiates and opens a connection based on the resolved backend.
        // WHY:  Abstracts connection construction across SQLite, PostgreSQL, DuckDB, and MySQL engines.
        // HOW:  If custom connection factory is configured, delegates to it; otherwise instantiates the driver.
        if (_connectionFactory is not null)
        {
            DbConnection custom = _connectionFactory(_connectionString);
            await custom.OpenAsync(ct).ConfigureAwait(false);
            return custom;
        }

        DbConnection connection;
        switch (_backend)
        {
            case WeaverDatabaseBackend.DuckDb:
                connection = new DuckDBConnection(_connectionString);
                break;
            case WeaverDatabaseBackend.PostgreSql:
                connection = new NpgsqlConnection(_connectionString);
                break;
            case WeaverDatabaseBackend.MySql:
                throw new InvalidOperationException("No MySQL ADO.NET provider is available. Supply a custom connection factory delegate to LanguagePreferenceService.");
            case WeaverDatabaseBackend.Sqlite:
            default:
                var builder = new SqliteConnectionStringBuilder(_connectionString);
                if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
                    !builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
                {
                    string? dir = Path.GetDirectoryName(builder.DataSource);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                connection = new SqliteConnection(_connectionString);
                break;
        }

        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Inspects a connection string to determine if it targets a PostgreSQL database.
    /// </summary>
    /// <param name="connectionString">The connection string to evaluate.</param>
    /// <returns><c>true</c> if PostgreSQL connection parameters are detected; otherwise, <c>false</c>.</returns>
    private static bool IsPostgreSqlConnectionString(string connectionString)
    {
        // WHAT: Detects standard PostgreSQL connection string tokens.
        // WHY:  Distinguishes PostgreSQL strings from local SQLite file or memory strings.
        // HOW:  Checks for Host=, Server=, Port=, User Id=, or Username= substrings case-insensitively.
        return connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inspects a connection string to detect the target database backend.
    /// </summary>
    /// <param name="connectionString">The connection string to evaluate.</param>
    /// <returns>The resolved <see cref="WeaverDatabaseBackend"/>.</returns>
    private static WeaverDatabaseBackend ResolveBackend(string? connectionString)
    {
        // WHAT: Detects database engine dialect from connection string tokens and file extensions.
        // WHY:  Routes queries and DDL to the correct relational or columnar SQL dialect.
        // HOW:  Checks for DuckDB tokens first, then MySQL tokens, then PostgreSQL tokens, defaulting to SQLite.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return WeaverDatabaseBackend.Sqlite;
        }

        if (connectionString.Contains("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return WeaverDatabaseBackend.DuckDb;
        }

        if (connectionString.Contains("mysql", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("mariadb", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Port=3306", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase))
        {
            return WeaverDatabaseBackend.MySql;
        }

        if (IsPostgreSqlConnectionString(connectionString))
        {
            return WeaverDatabaseBackend.PostgreSql;
        }

        return WeaverDatabaseBackend.Sqlite;
    }

    /// <summary>
    /// Adds a named parameter with the specified value to a database command.
    /// </summary>
    /// <param name="cmd">The command to append the parameter to.</param>
    /// <param name="name">The parameter name including prefix.</param>
    /// <param name="value">The parameter value.</param>
    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        // WHAT: Creates and appends a single DbParameter to the DbCommand.
        // WHY:  Standardizes parameter binding across all database engines and statements.
        // HOW:  Creates parameter via cmd.CreateParameter, assigns name and value, adds to Parameters collection.
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }

    /// <summary>
    /// Resolves the default SQLite connection string pointing to local application data.
    /// </summary>
    /// <returns>Default SQLite connection string.</returns>
    private static string GetDefaultConnectionString()
    {
        // WHAT: Computes default SQLite connection string targeting the user's LocalApplicationData folder.
        // WHY:  Provides out-of-the-box persistent preference storage for desktop and mobile runtimes.
        // HOW:  Combines LocalApplicationData path with 'NanoWeaverHrt' subdirectory and 'weaver_preferences.db'.
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoWeaverHrt");
        return $"Data Source={Path.Combine(baseDir, "weaver_preferences.db")}";
    }
}
