using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Opens real SQLite or PostgreSQL connections for the backend chosen at startup. There is no probe,
/// retry or fallback here: if the selected backend fails, the failure propagates.
/// </summary>
public sealed class WeaverDbConnectionFactory : IWeaverDbConnectionFactory
{
    /// <summary>
    /// Source of the backend decision recorded at startup.
    /// </summary>
    private readonly IConfigurationRepository _configurationRepository;

    /// <summary>
    /// Applies schema migrations after each connection opens.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// PostgreSQL connection string; empty when none is configured.
    /// </summary>
    private readonly string _postgresConnectionString;

    /// <summary>
    /// Path of the local SQLite database file.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbConnectionFactory"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="configurationRepository">Authoritative configuration repository holding the backend decision.</param>
    /// <param name="migrationRunner">Migration runner that versions the schema.</param>
    public WeaverDbConnectionFactory(
        IConfiguration configuration,
        IConfigurationRepository configurationRepository,
        DatabaseMigrationRunner migrationRunner)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configurationRepository = configurationRepository ?? throw new ArgumentNullException(nameof(configurationRepository));
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));

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
        _postgresConnectionString = envPostgres ?? string.Empty;

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <inheritdoc/>
    public WeaverDatabaseBackend ActiveBackend => ResolveBackend(_configurationRepository.ActiveBackendName);

    /// <summary>
    /// Maps the recorded backend name to a backend value.
    /// </summary>
    /// <param name="activeBackendName">The human-readable backend name; may be null.</param>
    /// <returns><see cref="WeaverDatabaseBackend.PostgreSql"/> when the name starts with "PostgreSQL" (case-insensitive); otherwise SQLite.</returns>
    internal static WeaverDatabaseBackend ResolveBackend(string? activeBackendName) =>
        activeBackendName is not null && activeBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            ? WeaverDatabaseBackend.PostgreSql
            : WeaverDatabaseBackend.Sqlite;

    /// <inheritdoc/>
    public async Task<WeaverDbSession> OpenAsync(CancellationToken ct = default)
    {
        // WHAT: Opens a connection to exactly the backend selected at startup and migrates its schema.
        // WHY:  The former per-call fallback silently redirected a failed PostgreSQL call to the local
        //       SQLite file, splitting the data across two databases. A selected backend that fails must
        //       be an error, never a quiet switch.
        // HOW:  Reads the existing decision, opens the matching real driver connection, applies pending
        //       migrations, and on any non-cancellation failure disposes the connection before throwing
        //       an exception that names only the backend.
        if (ActiveBackend == WeaverDatabaseBackend.PostgreSql)
        {
            NpgsqlConnection pgConn = new(_postgresConnectionString);
            try
            {
                await pgConn.OpenAsync(ct);
                await _migrationRunner.ApplyPendingMigrationsAsync(pgConn, PostgreSqlDialect.Instance.Name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // WHAT: Releases the half-opened connection before reporting failure.
                // WHY:  A failed open must not leak a pooled connection.
                // HOW:  Disposes, then wraps the driver error so its connection details stay in InnerException.
                await pgConn.DisposeAsync();
                throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.PostgreSql, ex);
            }

            return new WeaverDbSession(pgConn, PostgreSqlDialect.Instance);
        }

        SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
        try
        {
            await sqliteConn.OpenAsync(ct);
            await _migrationRunner.ApplyPendingMigrationsAsync(sqliteConn, SqliteDialect.Instance.Name, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WHAT: Releases the half-opened connection before reporting failure.
            // WHY:  A failed open must not leak a file handle on the database.
            // HOW:  Disposes, then wraps the driver error so file paths stay in InnerException.
            await sqliteConn.DisposeAsync();
            throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.Sqlite, ex);
        }

        return new WeaverDbSession(sqliteConn, SqliteDialect.Instance);
    }
}
