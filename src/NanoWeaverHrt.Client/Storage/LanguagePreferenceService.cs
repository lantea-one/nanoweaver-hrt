using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NanoWeaverHrt.Localization;
using Npgsql;

namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Dual-engine persistence service managing persistent user language and locale preferences,
/// backed by SQLite (local client) or PostgreSQL (centralized/enterprise node).
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
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class using the specified connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite or PostgreSQL database connection string.</param>
    public LanguagePreferenceService(string connectionString)
    {
        // WHAT: Validates and assigns the database connection string and identifies the storage dialect.
        // WHY:  Permits client to connect to either SQLite or enterprise PostgreSQL preference stores.
        // HOW:  Guards against null or whitespace, inspects connection string tokens for PostgreSQL keywords.
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        _isPostgreSql = IsPostgreSqlConnectionString(connectionString);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguagePreferenceService"/> class with a custom connection factory for testing.
    /// </summary>
    /// <param name="connectionString">The SQLite or PostgreSQL database connection string.</param>
    /// <param name="connectionFactory">Factory generating database connections.</param>
    internal LanguagePreferenceService(string connectionString, Func<string, DbConnection> connectionFactory)
        : this(connectionString)
    {
        // WHAT: Assigns custom connection factory delegate for unit testing.
        // WHY:  Permits executing PostgreSQL dialect statements without external daemon dependencies.
        // HOW:  Validates factory is non-null and assigns to _connectionFactory.
        ArgumentNullException.ThrowIfNull(connectionFactory);
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
        // WHY:  Guarantees tables exist before executing read/write commands on SQLite or PostgreSQL.
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
                cmd.CommandText = _isPostgreSql ? PostgresSchemaDdl : SqliteSchemaDdl;
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
        // WHAT: Retrieves persistent user language preference from SQLite or PostgreSQL.
        // WHY:  Restores user's chosen UI locale on application launch regardless of storage engine.
        // HOW:  Initializes schema, executes parameterized SELECT for LanguageKey, parses WeaverLanguage enum, defaulting to English.
        await InitializeAsync(ct).ConfigureAwait(false);

        WeaverLanguage resolved = WeaverLanguage.English;
        {
            await using DbConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT preference_value FROM weaver_gui_preferences WHERE preference_key = @key;";

            DbParameter pKey = cmd.CreateParameter();
            pKey.ParameterName = "@key";
            pKey.Value = LanguageKey;
            cmd.Parameters.Add(pKey);

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
        // WHAT: Persists the selected WeaverLanguage preference to SQLite or PostgreSQL.
        // WHY:  Stores user preference across application sessions on the configured storage backend.
        // HOW:  Initializes schema, formats timestamp, and executes dual-engine compatible ON CONFLICT statement.
        await InitializeAsync(ct).ConfigureAwait(false);

        string value = language.ToString();
        string updatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        {
            await using DbConnection connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO weaver_gui_preferences (preference_key, preference_value, updated_at_utc)
                VALUES (@key, @value, @updatedAt)
                ON CONFLICT (preference_key) DO UPDATE SET
                    preference_value = EXCLUDED.preference_value,
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """;

            DbParameter pKey = cmd.CreateParameter();
            pKey.ParameterName = "@key";
            pKey.Value = LanguageKey;
            cmd.Parameters.Add(pKey);

            DbParameter pVal = cmd.CreateParameter();
            pVal.ParameterName = "@value";
            pVal.Value = value;
            cmd.Parameters.Add(pVal);

            DbParameter pTime = cmd.CreateParameter();
            pTime.ParameterName = "@updatedAt";
            pTime.Value = _isPostgreSql ? (object)DateTimeOffset.UtcNow : updatedAt;
            cmd.Parameters.Add(pTime);

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
        // WHAT: Instantiates and opens an NpgsqlConnection or SqliteConnection based on configuration.
        // WHY:  Abstracts connection construction across SQLite and PostgreSQL engines.
        // HOW:  If custom connection factory is configured, delegates to it; otherwise checks directory and instantiates appropriate connection.
        if (!_isPostgreSql && _connectionFactory is null)
        {
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
        }

        DbConnection connection = _connectionFactory?.Invoke(_connectionString)
            ?? (_isPostgreSql ? (DbConnection)new NpgsqlConnection(_connectionString) : new SqliteConnection(_connectionString));
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
