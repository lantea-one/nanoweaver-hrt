using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.ExceptionHandling;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive test suite verifying browser idle session timeouts, SQL database migration pipelines,
/// and RFC 7807 secure API exception handling with zero sensitive data disclosure.
/// </summary>
public sealed class SecurityHardeningAndMigrationsTests
{
    /// <summary>
    /// Custom HTTP response feature simulating a response where headers have already started streaming.
    /// </summary>
    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    /// <summary>
    /// Dummy migration returning empty DDL to test empty script bypass paths.
    /// </summary>
    private sealed class EmptySqlMigration : DatabaseMigrationBase
    {
        public override long Version => 999;
        public override string Name => "999_EmptySql";
        public override string GetSql(string dialect) => string.Empty;
    }

    /// <summary>
    /// Failing migration used to verify transaction rollback on unhandled DDL exceptions.
    /// </summary>
    private sealed class FailingMigration : DatabaseMigrationBase
    {
        public override long Version => 100;
        public override string Name => "100_Failing";
        public override string GetSql(string dialect) => "INVALID SQL SYNTAX HERE";
    }

    /// <summary>
    /// Verifies that UserRepository and SystemConfigurationStore work seamlessly together on SQLite.
    /// </summary>
    [Fact]
    public async Task ConfigStoreAndUserRepository_SQLite_InitializeSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "test_db_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = tempDir,
                    ["POSTGRES_CONNECTION_STRING"] = string.Empty
                })
                .Build();

            using SystemConfigurationStore configStore = new(WeaverTestDb.CreateFactory(config));
            await configStore.InitializeAsync();

            using UserRepository userRepo = new(WeaverTestDb.CreateFactory(config, new DatabaseMigrationRunner()));
            await userRepo.InitializeAsync();

            bool hasAdmin = await userRepo.HasAnyAdministratorAsync();
            Assert.False(hasAdmin);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies session state idle timeout calculation, activity recording, and automated sign-out.
    /// </summary>
    [Fact]
    public void UserSessionState_IdleTimeoutLifecycle_EnforcedCleanly()
    {
        // WHAT: Tests activity tracking, idle threshold evaluation, and automated signout.
        // WHY:  Protects sensitive research data when terminals remain unattended.
        // HOW:  Simulates activity refresh, elapsed intervals, and CheckIdleTimeout sign-out.
        UserSessionState state = new();
        int eventCount = 0;
        state.OnChange += () => eventCount++;

        // 1. Unauthenticated state checks
        Assert.False(state.IsAuthenticated);
        Assert.False(state.IsExpired());
        Assert.False(state.CheckIdleTimeout());
        Assert.False(state.IsExpired(TimeSpan.FromSeconds(10)));
        Assert.False(state.CheckIdleTimeout(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, eventCount);

        // 2. Set authenticated user
        UserAccount user = new()
        {
            Username = "researcher_1",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MC4CAQAwBQYDK2VwBCIEIA=="
        };
        state.SetUser(user);
        Assert.True(state.IsAuthenticated);
        Assert.Equal(1, eventCount);

        // 3. Record Activity
        DateTimeOffset initialTime = state.LastActivityUtc;
        state.RecordActivity();
        Assert.True(state.LastActivityUtc >= initialTime);

        // 4. Test within idle timeout (both default IdleTimeout and explicit custom timeout)
        Assert.False(state.IsExpired());
        Assert.False(state.CheckIdleTimeout());
        Assert.False(state.IsExpired(TimeSpan.FromHours(1)));
        Assert.False(state.CheckIdleTimeout(TimeSpan.FromHours(1)));
        Assert.True(state.IsAuthenticated);

        // 5. Test expired using default IdleTimeout configured to negative duration
        state.IdleTimeout = TimeSpan.FromSeconds(-1);
        Assert.True(state.IsExpired());
        bool defaultSignedOut = state.CheckIdleTimeout();
        Assert.True(defaultSignedOut);
        Assert.False(state.IsAuthenticated);
        Assert.Null(state.CurrentUser);

        // Reset user and test custom negative timeout
        state.IdleTimeout = TimeSpan.FromMinutes(15);
        state.SetUser(user);
        Assert.True(state.IsAuthenticated);
        Assert.True(state.IsExpired(TimeSpan.FromSeconds(-1)));
        bool customSignedOut = state.CheckIdleTimeout(TimeSpan.FromSeconds(-1));
        Assert.True(customSignedOut);
        Assert.False(state.IsAuthenticated);
        Assert.Null(state.CurrentUser);
        Assert.True(eventCount >= 3);
    }

    /// <summary>
    /// Verifies SQL migrations execution, version tracking, idempotence, and dialect emission on SQLite.
    /// </summary>
    [Fact]
    public async Task DatabaseMigrations_SQLiteLifecycle_ExecutesIdempotently()
    {
        // WHAT: Tests migration execution, schema tracking, and repeat run idempotency.
        // WHY:  Ensures future structural changes to the database proceed reliably across restarts.
        // HOW:  Runs DatabaseMigrationRunner against an in-memory SQLite database connection.
        using SqliteConnection conn = new("Data Source=:memory:");
        await conn.OpenAsync();

        using DatabaseMigrationRunner runner = new();
        Assert.Equal(6, runner.RegisteredMigrations.Count);

        // Initial state: no applied migrations
        IReadOnlyList<long> initialApplied = await runner.GetAppliedVersionsAsync(conn, "sqlite");
        Assert.Empty(initialApplied);

        IReadOnlyList<IDatabaseMigration> initialPending = await runner.GetPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(6, initialPending.Count);

        // Apply migrations
        int appliedCount = await runner.ApplyPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(6, appliedCount);

        // Post-migration verification
        IReadOnlyList<long> postApplied = await runner.GetAppliedVersionsAsync(conn, "sqlite");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, postApplied);

        IReadOnlyList<IDatabaseMigration> postPending = await runner.GetPendingMigrationsAsync(conn, "sqlite");
        Assert.Empty(postPending);

        // Second run must be idempotent (0 applied)
        int secondRunCount = await runner.ApplyPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(0, secondRunCount);

        // Verify table existence in SQLite schema
        using SqliteCommand verifyCmd = new("SELECT name FROM sqlite_master WHERE type='table' AND name='weaver_users'", conn);
        object? userTable = await verifyCmd.ExecuteScalarAsync();
        Assert.Equal("weaver_users", userTable?.ToString());

        using SqliteCommand verifyConfigCmd = new("SELECT name FROM sqlite_master WHERE type='table' AND name='weaver_configuration'", conn);
        object? configTable = await verifyConfigCmd.ExecuteScalarAsync();
        Assert.Equal("weaver_configuration", configTable?.ToString());

        using SqliteCommand verifyProfileCmd = new("SELECT name FROM sqlite_master WHERE type='table' AND name='weaver_user_profiles'", conn);
        object? profileTable = await verifyProfileCmd.ExecuteScalarAsync();
        Assert.Equal("weaver_user_profiles", profileTable?.ToString());

        using SqliteCommand verifyAddrCmd = new("SELECT name FROM sqlite_master WHERE type='table' AND name='weaver_patient_addresses'", conn);
        object? addrTable = await verifyAddrCmd.ExecuteScalarAsync();
        Assert.Equal("weaver_patient_addresses", addrTable?.ToString());

        using SqliteCommand verifyContactCmd = new("SELECT name FROM sqlite_master WHERE type='table' AND name='weaver_patient_contacts'", conn);
        object? contactTable = await verifyContactCmd.ExecuteScalarAsync();
        Assert.Equal("weaver_patient_contacts", contactTable?.ToString());
    }

    /// <summary>
    /// Verifies dialect-specific SQL generation for PostgreSQL and SQLite.
    /// </summary>
    [Fact]
    public void DatabaseMigrations_DialectSqlGeneration_ProducesAccurateDdl()
    {
        // WHAT: Validates SQL strings produced by Migration001 and Migration002 for Postgres and SQLite.
        // WHY:  Ensures proper SQL syntax and data types before deployment to containerized Postgres nodes.
        // HOW:  Calls GetSql on both migrations with "postgresql" and "sqlite" parameters.
        Migration001_InitialSchema m1 = new();
        string pgSql1 = m1.GetSql("postgresql");
        string sqliteSql1 = m1.GetSql("sqlite");

        Assert.Contains("TIMESTAMP", pgSql1);
        Assert.Contains("VARCHAR(128)", pgSql1);
        Assert.Contains("TEXT PRIMARY KEY", sqliteSql1);

        Migration002_UserAuditAndIndexes m2 = new();
        string pgSql2 = m2.GetSql("postgresql");
        string sqliteSql2 = m2.GetSql("sqlite");

        Assert.Contains("idx_weaver_users_role_active", pgSql2);
        Assert.Contains("idx_weaver_users_role_active", sqliteSql2);

        Migration004_PatientDnaImportsAndResults m4 = new();
        string pgSql4 = m4.GetSql("postgresql");
        string sqliteSql4 = m4.GetSql("sqlite");
        Assert.Contains("weaver_patient_dna_records", pgSql4);
        Assert.Contains("weaver_patient_dna_imports", pgSql4);
        Assert.Contains("weaver_patient_results", pgSql4);
        Assert.Contains("weaver_patient_dna_records", sqliteSql4);
        Assert.Contains("weaver_patient_dna_imports", sqliteSql4);
        Assert.Contains("weaver_patient_results", sqliteSql4);
        Assert.Throws<ArgumentNullException>(() => m4.GetSql(null!));

        // Empty SQL migration
        EmptySqlMigration emptyMig = new();
        Assert.Equal(string.Empty, emptyMig.GetSql("sqlite"));
    }

    /// <summary>
    /// Verifies argument validation and error handling across database migration components.
    /// </summary>
    [Fact]
    public async Task DatabaseMigrations_ArgumentValidationAndRollback_HandledProperly()
    {
        // WHAT: Tests null parameter rejections and transactional rollback on migration syntax error.
        // WHY:  Prevents partial or corrupted database schemas if a migration encounters an error.
        // HOW:  Asserts ArgumentNullException on null parameters, and checks rollback on failing migration.
        using SqliteConnection conn = new("Data Source=:memory:");
        await conn.OpenAsync();

        using DatabaseMigrationRunner runner = new();
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.EnsureTrackingTableAsync(null!, "sqlite"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.EnsureTrackingTableAsync(conn, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.GetAppliedVersionsAsync(null!, "sqlite"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.GetAppliedVersionsAsync(conn, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.ApplyPendingMigrationsAsync(null!, "sqlite"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.ApplyPendingMigrationsAsync(conn, null!));

        EmptySqlMigration emptyMig = new();
        await Assert.ThrowsAsync<ArgumentNullException>(() => emptyMig.UpAsync(null!, null, "sqlite", default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => emptyMig.UpAsync(conn, null, null!, default));

        // Test empty SQL migration executes without error
        await emptyMig.UpAsync(conn, null, "sqlite", default);

        // Test transaction rollback on failing migration
        using DatabaseMigrationRunner failingRunner = new(new IDatabaseMigration[] { new FailingMigration() });
        await Assert.ThrowsAsync<SqliteException>(() => failingRunner.ApplyPendingMigrationsAsync(conn, "sqlite"));

        // Ensure tracking table does not contain failed version
        IReadOnlyList<long> applied = await runner.GetAppliedVersionsAsync(conn, "sqlite");
        Assert.DoesNotContain(100, applied);
    }

    /// <summary>
    /// Verifies RFC 7807 problem details properties, example generation, and data sanitization.
    /// </summary>
    [Fact]
    public void SecureProblemDetails_SanitizationAndModels_RedactsConfidentialData()
    {
        // WHAT: Tests SecureProblemDetails model instantiation, examples, and redaction rules.
        // WHY:  Prevents accidental leakage of server filesystem paths, connection strings, and private keys.
        // HOW:  Runs SecureExceptionSanitizer against strings containing paths, credentials, and PEM keys.
        SecureProblemDetails model = new()
        {
            Type = "urn:weaver:error:custom",
            Title = "Custom Error",
            Status = 400,
            Detail = "Detailed message",
            Instance = "/api/test",
            TraceId = "TRACE-123",
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.Equal("urn:weaver:error:custom", model.Type);
        Assert.Equal("Custom Error", model.Title);
        Assert.Equal(400, model.Status);

        SecureProblemDetails example = model.GetExamples();
        Assert.NotNull(example);
        Assert.Equal(400, example.Status);

        // 1. Redaction of 500 internal errors
        string sanitized500 = SecureExceptionSanitizer.SanitizeDetail("Critical database timeout at /var/data/internal.db", 500, "TR-500");
        Assert.Contains("Correlation ID: TR-500", sanitized500);
        Assert.DoesNotContain("/var/data", sanitized500);

        // 2. Redaction of file paths in 400 errors
        string pathMsg = "Could not locate dataset at /home/operator/secret_simulations/test.xyz";
        string sanitizedPath = SecureExceptionSanitizer.SanitizeDetail(pathMsg, 400, "TR-400");
        Assert.DoesNotContain("/home/operator", sanitizedPath);
        Assert.Contains("[REDACTED_PATH]", sanitizedPath);

        // 3. Redaction of connection strings
        string connMsg = "Failed connecting to Host=db.internal;Port=5432;Password=super_secret_pw;Database=weaver;";
        string sanitizedConn = SecureExceptionSanitizer.SanitizeDetail(connMsg, 400, "TR-CONN");
        Assert.DoesNotContain("super_secret_pw", sanitizedConn);
        Assert.Contains("[REDACTED]", sanitizedConn);

        string dbIdentityMsg = "Failed: Server=db1;User Id=admin1;Database=weaver_hrt;Username=weaver;superuser=keepme;Persist Security Info=True";
        string sanitizedIdentity = SecureExceptionSanitizer.SanitizeDetail(dbIdentityMsg, 400, "TR-DB");
        Assert.DoesNotContain("weaver_hrt", sanitizedIdentity);
        Assert.DoesNotContain("weaver;", sanitizedIdentity);
        Assert.DoesNotContain("admin1", sanitizedIdentity);
        Assert.DoesNotContain(" id=", sanitizedIdentity);
        Assert.Contains("Database=[REDACTED]", sanitizedIdentity);
        Assert.Contains("Username=[REDACTED]", sanitizedIdentity);
        Assert.Contains("User Id=[REDACTED]", sanitizedIdentity);
        Assert.Contains("superuser=keepme", sanitizedIdentity);

        // 4. Redaction of PEM cryptographic keys
        string pemMsg = "Invalid key: -----BEGIN PRIVATE KEY-----\nMC4CAQAwBQYDK2VwBCIEIKey\n-----END PRIVATE KEY----- provided.";
        string sanitizedPem = SecureExceptionSanitizer.SanitizeDetail(pemMsg, 400, "TR-PEM");
        Assert.DoesNotContain("MC4CAQAwBQYDK2Vw", sanitizedPem);
        Assert.Contains("[REDACTED_KEY]", sanitizedPem);

        // 5. Default detail when message is null or empty
        string defaultDetail = SecureExceptionSanitizer.SanitizeDetail(null, 404, "TR-NULL");
        Assert.Equal("The requested resource was not found.", defaultDetail);

        string emptyDetail = SecureExceptionSanitizer.SanitizeDetail("   ", 401, "TR-EMPTY");
        Assert.Equal("Authentication credentials are missing or invalid.", emptyDetail);
    }

    /// <summary>
    /// Verifies status code mappings, problem types, and titles in <see cref="SecureExceptionSanitizer"/>.
    /// </summary>
    [Fact]
    public void SecureExceptionSanitizer_MappingsAndTypes_AccuratelyClassified()
    {
        // WHAT: Tests status codes, problem types, titles, and exception categorization.
        // WHY:  Ensures that HTTP responses conform strictly to standard HTTP semantics.
        // HOW:  Invokes GetProblemType, GetTitle, GetDefaultDetail, and MapExceptionToStatusCode.
        Assert.Equal("urn:weaver:error:bad-request", SecureExceptionSanitizer.GetProblemType(400));
        Assert.Equal("urn:weaver:error:unauthorized", SecureExceptionSanitizer.GetProblemType(401));
        Assert.Equal("urn:weaver:error:forbidden", SecureExceptionSanitizer.GetProblemType(403));
        Assert.Equal("urn:weaver:error:not-found", SecureExceptionSanitizer.GetProblemType(404));
        Assert.Equal("urn:weaver:error:conflict", SecureExceptionSanitizer.GetProblemType(409));
        Assert.Equal("urn:weaver:error:unsupported-media-type", SecureExceptionSanitizer.GetProblemType(415));
        Assert.Equal("urn:weaver:error:service-unavailable", SecureExceptionSanitizer.GetProblemType(503));
        Assert.Equal("urn:weaver:error:internal", SecureExceptionSanitizer.GetProblemType(500));
        Assert.Equal("urn:weaver:error:internal", SecureExceptionSanitizer.GetProblemType(999));

        Assert.Equal("Bad Request", SecureExceptionSanitizer.GetTitle(400));
        Assert.Equal("Unauthorized", SecureExceptionSanitizer.GetTitle(401));
        Assert.Equal("Forbidden", SecureExceptionSanitizer.GetTitle(403));
        Assert.Equal("Resource Not Found", SecureExceptionSanitizer.GetTitle(404));
        Assert.Equal("Conflict", SecureExceptionSanitizer.GetTitle(409));
        Assert.Equal("Unsupported Media Type", SecureExceptionSanitizer.GetTitle(415));
        Assert.Equal("Service Unavailable", SecureExceptionSanitizer.GetTitle(503));
        Assert.Equal("Internal Server Error", SecureExceptionSanitizer.GetTitle(500));
        Assert.Equal("Internal Server Error", SecureExceptionSanitizer.GetTitle(999));

        Assert.Equal("The request parameters or body could not be validated.", SecureExceptionSanitizer.GetDefaultDetail(400));
        Assert.Equal("The authenticated identity is forbidden from accessing this resource.", SecureExceptionSanitizer.GetDefaultDetail(403));
        Assert.Equal("The operation conflicts with existing system state.", SecureExceptionSanitizer.GetDefaultDetail(409));
        Assert.Equal("The provided media format is not supported.", SecureExceptionSanitizer.GetDefaultDetail(415));
        Assert.Equal("The service is temporarily unavailable or requires initial setup.", SecureExceptionSanitizer.GetDefaultDetail(503));
        Assert.Equal("An unexpected error occurred.", SecureExceptionSanitizer.GetDefaultDetail(500));

        // Exception mapping
        Assert.Throws<ArgumentNullException>(() => SecureExceptionSanitizer.MapExceptionToStatusCode(null!));
        Assert.Equal(400, SecureExceptionSanitizer.MapExceptionToStatusCode(new BadHttpRequestException("bad")));
        Assert.Equal(400, SecureExceptionSanitizer.MapExceptionToStatusCode(new ArgumentException("invalid arg")));
        Assert.Equal(400, SecureExceptionSanitizer.MapExceptionToStatusCode(new FormatException("bad format")));
        Assert.Equal(400, SecureExceptionSanitizer.MapExceptionToStatusCode(new JsonException("bad json")));
        Assert.Equal(401, SecureExceptionSanitizer.MapExceptionToStatusCode(new UnauthorizedAccessException("denied")));
        Assert.Equal(404, SecureExceptionSanitizer.MapExceptionToStatusCode(new KeyNotFoundException("missing key")));
        Assert.Equal(404, SecureExceptionSanitizer.MapExceptionToStatusCode(new FileNotFoundException("missing file")));
        Assert.Equal(409, SecureExceptionSanitizer.MapExceptionToStatusCode(new InvalidOperationException("User already exists in system")));
        Assert.Equal(409, SecureExceptionSanitizer.MapExceptionToStatusCode(new InvalidOperationException("Setup already completed")));
        Assert.Equal(409, SecureExceptionSanitizer.MapExceptionToStatusCode(new InvalidOperationException("Entity conflict")));
        Assert.Equal(400, SecureExceptionSanitizer.MapExceptionToStatusCode(new InvalidOperationException("Standard invalid state")));
        Assert.Equal(500, SecureExceptionSanitizer.MapExceptionToStatusCode(new NotSupportedException("media unsupported")));
        Assert.Equal(500, SecureExceptionSanitizer.MapExceptionToStatusCode(new InvalidCastException("unexpected cast error")));
    }

    /// <summary>
    /// Verifies <see cref="SecureExceptionHandlerMiddleware"/> request interception, JSON formatting, XML formatting, and passthrough.
    /// </summary>
    [Fact]
    public async Task SecureExceptionHandlerMiddleware_PipelineExecution_FormatsProblemDetails()
    {
        // WHAT: Tests middleware exception trapping, JSON/XML problem responses, and bypass on non-API routes.
        // WHY:  Ensures that unhandled API errors return uniform RFC 7807 responses across all accepted MIME formats.
        // HOW:  Simulates HttpContext requests passing through SecureExceptionHandlerMiddleware.
        NullLogger<SecureExceptionHandlerMiddleware> logger = NullLogger<SecureExceptionHandlerMiddleware>.Instance;

        Assert.Throws<ArgumentNullException>(() => new SecureExceptionHandlerMiddleware(null!, logger));
        Assert.Throws<ArgumentNullException>(() => new SecureExceptionHandlerMiddleware(_ => Task.CompletedTask, null!));

        // 1. Non-/api path bypasses exception trapping (passes to next)
        bool nonApiExecuted = false;
        SecureExceptionHandlerMiddleware nonApiMiddleware = new(ctx =>
        {
            nonApiExecuted = true;
            return Task.CompletedTask;
        }, logger);

        DefaultHttpContext nonApiCtx = new();
        nonApiCtx.Request.Path = "/dashboard";
        await nonApiMiddleware.InvokeAsync(nonApiCtx);
        Assert.True(nonApiExecuted);

        // 2. /api success path
        bool apiExecuted = false;
        SecureExceptionHandlerMiddleware successMiddleware = new(ctx =>
        {
            apiExecuted = true;
            return Task.CompletedTask;
        }, logger);

        DefaultHttpContext successCtx = new();
        successCtx.Request.Path = "/api/data";
        await successMiddleware.InvokeAsync(successCtx);
        Assert.True(apiExecuted);

        // 3. /api JSON problem details on exception
        SecureExceptionHandlerMiddleware jsonErrorMiddleware = new(_ =>
        {
            throw new ArgumentException("Invalid simulation step size at /var/lib/configs/run.cfg");
        }, logger);

        DefaultHttpContext jsonCtx = new();
        jsonCtx.Request.Path = "/api/simulations";
        using MemoryStream jsonBody = new();
        jsonCtx.Response.Body = jsonBody;

        await jsonErrorMiddleware.InvokeAsync(jsonCtx);

        Assert.Equal(400, jsonCtx.Response.StatusCode);
        Assert.Contains("application/problem+json", jsonCtx.Response.ContentType);

        jsonBody.Seek(0, SeekOrigin.Begin);
        using StreamReader jsonReader = new(jsonBody);
        string jsonText = await jsonReader.ReadToEndAsync();
        Assert.Contains("urn:weaver:error:bad-request", jsonText);
        Assert.Contains("[REDACTED_PATH]", jsonText);
        Assert.DoesNotContain("/var/lib", jsonText);

        // 4. /api XML problem details when Accept: application/xml
        SecureExceptionHandlerMiddleware xmlErrorMiddleware = new(_ =>
        {
            throw new KeyNotFoundException("Specified trajectory key not found");
        }, logger);

        DefaultHttpContext xmlCtx = new();
        xmlCtx.Request.Path = "/api/trajectories/missing";
        xmlCtx.Request.Headers.Accept = "application/xml";
        using MemoryStream xmlBody = new();
        xmlCtx.Response.Body = xmlBody;

        await xmlErrorMiddleware.InvokeAsync(xmlCtx);

        Assert.Equal(404, xmlCtx.Response.StatusCode);
        Assert.Contains("application/problem+xml", xmlCtx.Response.ContentType);

        xmlBody.Seek(0, SeekOrigin.Begin);
        using StreamReader xmlReader = new(xmlBody);
        string xmlText = await xmlReader.ReadToEndAsync();
        Assert.Contains("<ProblemDetails", xmlText);
        Assert.Contains("Specified trajectory key not found", xmlText);

        // 5. Already started response is not written
        SecureExceptionHandlerMiddleware startedMiddleware = new(_ =>
        {
            return Task.FromException(new InvalidOperationException("Late error"));
        }, logger);

        DefaultHttpContext startedCtx = new();
        startedCtx.Request.Path = "/api/stream";
        startedCtx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());

        // Should return cleanly without throwing or writing
        await startedMiddleware.InvokeAsync(startedCtx);

        // 6. Null context argument check
        await Assert.ThrowsAsync<ArgumentNullException>(() => startedMiddleware.InvokeAsync(null!));
    }
}
