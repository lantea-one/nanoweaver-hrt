using Npgsql;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Declares the xUnit collection that shares one <see cref="PostgresTestEnvironment"/> across every
/// PostgreSQL-backed test class.
/// </summary>
[CollectionDefinition(PostgresTestEnvironment.CollectionName)]
public sealed class PostgresTestCollection : ICollectionFixture<PostgresTestEnvironment>
{
}

/// <summary>
/// Supplies fresh, uniquely named PostgreSQL databases to tests, and removes them afterwards.
/// </summary>
/// <remarks>
/// The server is optional: it is used only when the WEAVER_TEST_POSTGRES environment variable holds a full
/// Npgsql connection string (for example a throwaway local container). When the variable is absent,
/// <see cref="IsAvailable"/> is false and PostgreSQL-backed tests return early. When it IS set but the
/// server cannot be reached, <see cref="CreateIsolatedDatabaseAsync"/> throws and the tests fail loudly.
/// </remarks>
public sealed class PostgresTestEnvironment : IAsyncLifetime
{
    /// <summary>
    /// Name of the xUnit collection that shares this fixture.
    /// </summary>
    public const string CollectionName = "postgres";

    /// <summary>
    /// Name of the environment variable holding the maintenance-database connection string.
    /// </summary>
    private const string VariableName = "WEAVER_TEST_POSTGRES";

    /// <summary>
    /// Connection string of the maintenance database; empty when no server was supplied.
    /// </summary>
    private string _maintenanceConnectionString = string.Empty;

    /// <summary>
    /// Names of every database this environment created and must drop.
    /// </summary>
    private readonly List<string> _createdDatabases = [];

    /// <summary>
    /// Gets a value indicating whether WEAVER_TEST_POSTGRES holds a non-empty connection string.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        // WHAT: Reads the optional PostgreSQL connection string from the environment.
        // WHY:  Directive 6 forbids depending on ambient network state; nothing is contacted here.
        // HOW:  Only records whether the variable holds a value.
        _maintenanceConnectionString = Environment.GetEnvironmentVariable(VariableName) ?? string.Empty;
        IsAvailable = !string.IsNullOrWhiteSpace(_maintenanceConnectionString);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a fresh, uniquely named database and returns a connection string pointing at it.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connection string for the new, empty database.</returns>
    /// <exception cref="InvalidOperationException">No server was supplied.</exception>
    public async Task<string> CreateIsolatedDatabaseAsync(CancellationToken ct = default)
    {
        // WHAT: Creates one empty database per test.
        // WHY:  The migration chain uses unqualified table names, so a separate database guarantees it runs
        //       from zero with no search_path interference and no leftovers from another test.
        // HOW:  Connects to the maintenance database, runs CREATE DATABASE with a GUID-based name (never
        //       user input), records the name for teardown, and swaps Database= in the returned string.
        if (!IsAvailable)
        {
            throw new InvalidOperationException("WEAVER_TEST_POSTGRES is not set.");
        }

        string name = "weaver_test_" + Guid.NewGuid().ToString("N");
        await using (NpgsqlConnection maintenance = new(_maintenanceConnectionString))
        {
            await maintenance.OpenAsync(ct);
            await using NpgsqlCommand create = maintenance.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync(ct);
        }

        _createdDatabases.Add(name);
        NpgsqlConnectionStringBuilder builder = new(_maintenanceConnectionString) { Database = name };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Drops every database this environment created.
    /// </summary>
    /// <returns>A task that completes when all databases are dropped.</returns>
    public async Task DisposeAsync()
    {
        // WHAT: Removes the per-test databases.
        // WHY:  A leftover database must not be able to make a later run pass for the wrong reason.
        // HOW:  Clears pooled connections first, then DROP DATABASE ... WITH (FORCE) from the maintenance database.
        if (_createdDatabases.Count == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using NpgsqlConnection maintenance = new(_maintenanceConnectionString);
        await maintenance.OpenAsync();
        foreach (string name in _createdDatabases)
        {
            await using NpgsqlCommand drop = maintenance.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }

        _createdDatabases.Clear();
    }
}
