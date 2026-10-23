using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.ExceptionHandling;
using NanoWeaverHrt.Security.Secrets;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Direct tests for the database access seam: dialects, backend resolution, parameter and value helpers,
/// session disposal, the connection factory against real SQLite, and the unavailable-backend exception.
/// No database server is required.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class WeaverDbAccessSeamTests : IDisposable
{
    /// <summary>
    /// Shared PostgreSQL fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// Test output sink used to report why a test did not run.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Per-test directory under the system temp path; removed on dispose.
    /// </summary>
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_seam_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbAccessSeamTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">Test output sink.</param>
    public WeaverDbAccessSeamTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
    {
        _postgres = postgres;
        _output = output;
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Builds a configuration pointing the data directory at this test's temp directory.
    /// </summary>
    /// <param name="postgresConnectionString">Optional PostgreSQL connection string.</param>
    /// <returns>The configuration.</returns>
    private IConfiguration BuildConfiguration(string? postgresConnectionString = null)
    {
        Dictionary<string, string?> values = new() { ["TRAJECTORY_INCOMING_DIR"] = _tempDir };
        if (postgresConnectionString is not null)
        {
            values["POSTGRES_CONNECTION_STRING"] = postgresConnectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Creates a profile data protector whose key files live in this test's temp directory.
    /// </summary>
    /// <returns>The protector.</returns>
    private ProfileDataProtector CreateProtector()
    {
        IConfiguration keyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WEAVER_NODE_KEYS_DIR"] = Path.Combine(_tempDir, "node-keys") })
            .Build();
        return new ProfileDataProtector(new NodeSecretStore(keyConfiguration));
    }

    /// <summary>
    /// Builds a data table with a single-row, single-column layout and returns a reader over it.
    /// </summary>
    /// <param name="columnType">The CLR type of the column.</param>
    /// <param name="value">The cell value.</param>
    /// <returns>A reader positioned on the row.</returns>
    private static DbDataReader ReaderOf(Type columnType, object value)
    {
        DataTable table = new();
        table.Columns.Add("c", columnType);
        table.Rows.Add(value);
        DbDataReader reader = table.CreateDataReader();
        reader.Read();
        return reader;
    }

    /// <summary>
    /// Verifies all seven <see cref="ISqlDialect"/> members on both implementations.
    /// </summary>
    [Fact]
    public void Dialects_ExposeExactlyTheEngineSpecificValues()
    {
        // WHAT: Exercises every dialect member on SQLite and PostgreSQL.
        // WHY:  The dialect is the only place the two engines differ; each value must be exact.
        // HOW:  Asserts names, literals, and the parameter coercions including null handling.
        DateTimeOffset moment = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

        ISqlDialect sqlite = SqliteDialect.Instance;
        Assert.Equal("sqlite", sqlite.Name);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, sqlite.Backend);
        Assert.Equal("1", sqlite.TrueLiteral);
        Assert.Equal("0", sqlite.FalseLiteral);
        Assert.Equal(1, sqlite.BooleanValue(true));
        Assert.Equal(0, sqlite.BooleanValue(false));
        Assert.Equal(moment.ToString("o"), sqlite.TimestampValue(moment));
        Assert.Equal(moment.ToString("o"), sqlite.TimestampValueOrNull(moment));
        Assert.Same(DBNull.Value, sqlite.TimestampValueOrNull(null));

        ISqlDialect pg = PostgreSqlDialect.Instance;
        Assert.Equal("postgresql", pg.Name);
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, pg.Backend);
        Assert.Equal("TRUE", pg.TrueLiteral);
        Assert.Equal("FALSE", pg.FalseLiteral);
        Assert.Equal(true, pg.BooleanValue(true));
        Assert.Equal(false, pg.BooleanValue(false));
        Assert.Equal(moment.UtcDateTime, pg.TimestampValue(moment));
        Assert.Equal(moment.UtcDateTime, pg.TimestampValueOrNull(moment));
        Assert.Same(DBNull.Value, pg.TimestampValueOrNull(null));
    }

    /// <summary>
    /// Verifies the boolean reader coercion on both the native and integer branch.
    /// </summary>
    [Fact]
    public void WeaverDbValues_ReadBoolean_CoversNativeAndIntegerBranches()
    {
        // WHAT: Reads booleans from a bool column (PostgreSQL shape) and a long column (SQLite shape).
        // WHY:  Both engines must yield identical results through one helper.
        // HOW:  In-memory DataTable readers; no SQL runs.
        using (DbDataReader native = ReaderOf(typeof(bool), true))
        {
            Assert.True(WeaverDbValues.ReadBoolean(native, 0));
        }

        using (DbDataReader one = ReaderOf(typeof(long), 1L))
        {
            Assert.True(WeaverDbValues.ReadBoolean(one, 0));
        }

        using (DbDataReader zero = ReaderOf(typeof(long), 0L))
        {
            Assert.False(WeaverDbValues.ReadBoolean(zero, 0));
        }
    }

    /// <summary>
    /// Verifies the timestamp reader coercions on the native, text and null branches.
    /// </summary>
    [Fact]
    public void WeaverDbValues_ReadTimestamp_CoversNativeTextAndNullBranches()
    {
        // WHAT: Reads timestamps from DateTime and string columns, and a nullable variant.
        // WHY:  PostgreSQL returns DateTime; SQLite returns ISO-8601 text.
        // HOW:  In-memory DataTable readers; no SQL runs.
        DateTime utc = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        using (DbDataReader native = ReaderOf(typeof(DateTime), utc))
        {
            Assert.Equal(new DateTimeOffset(utc, TimeSpan.Zero), WeaverDbValues.ReadTimestamp(native, 0));
            Assert.Equal(new DateTimeOffset(utc, TimeSpan.Zero), WeaverDbValues.ReadNullableTimestamp(native, 0));
        }

        DateTimeOffset moment = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        using (DbDataReader text = ReaderOf(typeof(string), moment.ToString("o")))
        {
            Assert.Equal(moment, WeaverDbValues.ReadTimestamp(text, 0));
        }

        using (DbDataReader nullCell = ReaderOf(typeof(string), DBNull.Value))
        {
            Assert.Null(WeaverDbValues.ReadNullableTimestamp(nullCell, 0));
        }
    }

    /// <summary>
    /// Verifies the parameter helpers against a real SQLite command.
    /// </summary>
    [Fact]
    public void WeaverDbParameters_AddAndAddNullable_AttachParameters()
    {
        // WHAT: Attaches a value parameter and a null parameter.
        // WHY:  Four private copies were lifted into one helper; null must map to DBNull.
        // HOW:  Uses a real SqliteCommand that is never executed.
        using SqliteConnection connection = new("Data Source=:memory:");
        using DbCommand command = connection.CreateCommand();

        DbParameter named = WeaverDbParameters.Add(command, "@a", "x");
        DbParameter nulled = WeaverDbParameters.AddNullable(command, "@b", null);
        DbParameter valued = WeaverDbParameters.AddNullable(command, "@c", 5);

        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal("@a", named.ParameterName);
        Assert.Equal("x", named.Value);
        Assert.Same(DBNull.Value, nulled.Value);
        Assert.Equal(5, valued.Value);
    }

    /// <summary>
    /// Verifies the session rejects blank SQL and disposes idempotently.
    /// </summary>
    [Fact]
    public async Task WeaverDbSession_CreateCommandAndDispose_BehaveSafely()
    {
        // WHAT: Opens a real SQLite session, creates a command, and disposes twice.
        // WHY:  Callers rely on blank SQL being rejected and on double disposal being harmless.
        // HOW:  Obtains the session from the real factory over an isolated temp directory.
        using DatabaseMigrationRunner runner = new();
        WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(BuildConfiguration(), runner);

        WeaverDbSession session = await factory.OpenAsync();
        Assert.Same(SqliteDialect.Instance, session.Dialect);
        Assert.Equal(ConnectionState.Open, session.Connection.State);
        Assert.Throws<ArgumentException>(() => session.CreateCommand(""));
        Assert.ThrowsAny<ArgumentException>(() => session.CreateCommand(null!));

        await using (DbCommand command = session.CreateCommand("SELECT 1;"))
        {
            Assert.Equal("SELECT 1;", command.CommandText);
        }

        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Equal(ConnectionState.Closed, session.Connection.State);
    }

    /// <summary>
    /// Verifies SQLite open, migration, and failure wrapping through the real factory.
    /// </summary>
    [Fact]
    public async Task WeaverDbConnectionFactory_Sqlite_OpensMigratesAndWrapsFailures()
    {
        // WHAT: Opens SQLite successfully, then makes the database path unopenable.
        // WHY:  A failed open must surface as WeaverDatabaseUnavailableException naming only the backend,
        //       and cancellation must not be disguised as an availability failure.
        // HOW:  Replaces weaver_config.db with a directory so SQLite cannot open it.
        using DatabaseMigrationRunner runner = new();
        IConfiguration configuration = BuildConfiguration();
        WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(configuration, runner);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, factory.ActiveBackend);

        await using (WeaverDbSession ok = await factory.OpenAsync())
        {
            await using DbCommand check = ok.CreateCommand("SELECT COUNT(*) FROM weaver_user_profiles;");
            Assert.Equal(0L, await check.ExecuteScalarAsync());
        }

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.OpenAsync(cancelled.Token));

        SqliteConnection.ClearAllPools();
        string dbPath = Path.Combine(_tempDir, "weaver_config.db");
        File.Delete(dbPath);
        Directory.CreateDirectory(dbPath);

        WeaverDatabaseUnavailableException ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.OpenAsync());
        Assert.Equal(WeaverDatabaseBackend.Sqlite, ex.Backend);
        Assert.DoesNotContain(_tempDir, ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// Verifies the exception message names only the backend.
    /// </summary>
    [Fact]
    public void WeaverDatabaseUnavailableException_MessageNamesOnlyBackend()
    {
        // WHAT: Wraps a driver error that contains connection details.
        // WHY:  Directive 14 forbids connection strings in anything a client could see.
        // HOW:  Asserts the message is the backend name sentence and the detail is only in InnerException.
        Exception driver = new InvalidOperationException("Host=db.internal;Password=hunter2");
        WeaverDatabaseUnavailableException ex = new(WeaverDatabaseBackend.PostgreSql, driver);

        Assert.Equal("The PostgreSql database backend is unavailable.", ex.Message);
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, ex.Backend);
        Assert.Same(driver, ex.InnerException);
    }

    /// <summary>
    /// Verifies a PostgreSQL backend lost after selection yields an opaque RFC 7807 response.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnavailableBackend_ThroughApiPipeline_ReturnsSanitizedProblemDetails()
    {
        // WHAT: Drives a real repository call at a dropped PostgreSQL database through the exception middleware.
        // WHY:  The driver exception, host, password, paths and SQL must never reach an API client.
        // HOW:  Resolves a real factory against PostgreSQL, drops the database, runs SecureExceptionHandlerMiddleware
        //       over a delegate that calls the repository, then reads the response body.
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state; a set-but-dead variable fails elsewhere.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        (WeaverDbConnectionFactory factory, string deadConnectionString) = await _postgres.CreateUnavailableAfterResolutionAsync(_tempDir);
        using UserProfileRepository repository = new(factory, CreateProtector());

        SecureExceptionHandlerMiddleware middleware = new(
            async _ => await repository.GetProfileAsync("anyone"),
            NullLogger<SecureExceptionHandlerMiddleware>.Instance);

        DefaultHttpContext context = new();
        context.Request.Path = "/api/profile";
        using MemoryStream body = new();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("application/problem+json", context.Response.ContentType);
        body.Seek(0, SeekOrigin.Begin);
        string text = await new StreamReader(body).ReadToEndAsync();
        Assert.Contains("urn:weaver:error:", text);
        Npgsql.NpgsqlConnectionStringBuilder dead = new(deadConnectionString);
        foreach (string forbidden in new[] { "Host=", "Password", dead.Database!, dead.Host!, "/home/", "/tmp/", "SELECT", "INSERT", "weaver_user_profiles" }) // Both are always present in a string that connected.
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }
}
