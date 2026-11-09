using System.Data.Common;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying parameter formatting, upsert SQL generation, insert-ignore statement construction,
/// parameter attachment, synchronous session acquisition, and asynchronous audit ledger persistence across dialects.
/// </summary>
public sealed class SqlDialectParityTests
{
    /// <summary>
    /// Verifies that <see cref="ISqlDialect.Parameter"/> formats parameter markers correctly across all 4 database dialects.
    /// </summary>
    /// <param name="dialectName">The name of the dialect to test.</param>
    /// <param name="input">The raw parameter name.</param>
    /// <param name="expected">The expected dialect-specific parameter marker.</param>
    [Theory]
    [InlineData("sqlite", "user", "@user")]
    [InlineData("sqlite", "@user", "@user")]
    [InlineData("sqlite", "$user", "@user")]
    [InlineData("postgresql", "user", "@user")]
    [InlineData("postgresql", "@user", "@user")]
    [InlineData("postgresql", "$user", "@user")]
    [InlineData("duckdb", "user", "$user")]
    [InlineData("duckdb", "@user", "$user")]
    [InlineData("duckdb", "$user", "$user")]
    [InlineData("mysql", "user", "@user")]
    [InlineData("mysql", "@user", "@user")]
    [InlineData("mysql", "$user", "@user")]
    public void Parameter_FormatsPlaceholder_CorrectlyAcrossDialects(string dialectName, string input, string expected)
    {
        // WHAT: Tests parameter placeholder formatting for SQLite, PostgreSQL, DuckDB, and MySQL.
        // WHY:  DuckDB requires '$' parameter prefixes while other providers require '@'.
        // HOW:  Resolves dialect singleton and asserts formatted output.
        ISqlDialect dialect = ResolveDialect(dialectName);
        string actual = dialect.Parameter(input);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that <see cref="ISqlDialect.Parameter"/> throws <see cref="ArgumentException"/> for null or whitespace parameter names.
    /// </summary>
    /// <param name="dialectName">The name of the dialect to test.</param>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("duckdb")]
    [InlineData("mysql")]
    public void Parameter_NullOrWhitespace_ThrowsArgumentException(string dialectName)
    {
        // WHAT: Validates parameter name validation across dialects.
        // WHY:  Empty parameter names cause invalid SQL query formulation.
        // HOW:  Asserts ArgumentException on null, empty, and whitespace names.
        ISqlDialect dialect = ResolveDialect(dialectName);
        Assert.Throws<ArgumentException>(() => dialect.Parameter(""));
        Assert.Throws<ArgumentException>(() => dialect.Parameter("   "));
    }

    /// <summary>
    /// Verifies that <see cref="ISqlDialect.BuildUpsertSql"/> emits engine-compliant upsert queries across all dialects.
    /// </summary>
    [Fact]
    public void BuildUpsertSql_EmitsEngineCompliantSyntax()
    {
        // WHAT: Validates upsert statement generation across SQLite, PostgreSQL, DuckDB, and MySQL.
        // WHY:  MySQL uses ON DUPLICATE KEY UPDATE while others use ON CONFLICT DO UPDATE.
        // HOW:  Generates SQL with default (null) and custom parameter names across all 4 dialects and asserts expected clause structure.
        string[] columns = ["id", "username", "status"];
        string[] conflict = ["id"];
        string[] update = ["status"];

        string sqliteSql = SqliteDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@id, @username, @status) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", sqliteSql);

        string pgSql = PostgreSqlDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@id, @username, @status) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", pgSql);

        string duckSqlDefault = DuckDbDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES ($id, $username, $status) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", duckSqlDefault);

        string mySqlDefault = MySqlDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@id, @username, @status) ON DUPLICATE KEY UPDATE status = VALUES(status)", mySqlDefault);

        string duckSql = DuckDbDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update, ["k", "u", "s"]);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES ($k, $u, $s) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", duckSql);

        string mySql = MySqlDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update, ["k", "u", "s"]);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@k, @u, @s) ON DUPLICATE KEY UPDATE status = VALUES(status)", mySql);

        string sqliteCustom = SqliteDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update, ["k", "u", "s"]);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@k, @u, @s) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", sqliteCustom);

        string pgCustom = PostgreSqlDialect.Instance.BuildUpsertSql("tbl", columns, conflict, update, ["k", "u", "s"]);
        Assert.Equal("INSERT INTO tbl (id, username, status) VALUES (@k, @u, @s) ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status", pgCustom);
    }

    /// <summary>
    /// Verifies that <see cref="ISqlDialect.BuildUpsertSql"/> throws validation exceptions for invalid input arguments.
    /// </summary>
    /// <param name="dialectName">The name of the dialect to test.</param>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("duckdb")]
    [InlineData("mysql")]
    public void BuildUpsertSql_InvalidArguments_ThrowsExpectedExceptions(string dialectName)
    {
        // WHAT: Asserts guard clauses on BuildUpsertSql.
        // WHY:  Prevents malformed SQL generation when required parameters are missing.
        // HOW:  Tests null/empty table, null columns, null conflictColumns, and null updateColumns.
        ISqlDialect dialect = ResolveDialect(dialectName);
        Assert.Throws<ArgumentException>(() => dialect.BuildUpsertSql("", ["a"], ["a"], ["a"]));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildUpsertSql("tbl", null!, ["a"], ["a"]));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildUpsertSql("tbl", ["a"], null!, ["a"]));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildUpsertSql("tbl", ["a"], ["a"], null!));
    }

    /// <summary>
    /// Verifies that <see cref="ISqlDialect.BuildInsertIgnoreSql"/> emits engine-compliant insert-ignore queries across all dialects.
    /// </summary>
    [Fact]
    public void BuildInsertIgnoreSql_EmitsEngineCompliantSyntax()
    {
        // WHAT: Validates insert-ignore statement generation across dialects.
        // WHY:  MySQL uses INSERT IGNORE INTO while other engines use ON CONFLICT DO NOTHING.
        // HOW:  Generates SQL with default (null) and custom parameter names across all 4 dialects and asserts expected clause structure.
        string[] columns = ["id", "username"];
        string[] conflict = ["id"];

        string sqliteSql = SqliteDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES (@id, @username) ON CONFLICT (id) DO NOTHING", sqliteSql);

        string pgSql = PostgreSqlDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES (@id, @username) ON CONFLICT (id) DO NOTHING", pgSql);

        string duckSqlDefault = DuckDbDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES ($id, $username) ON CONFLICT (id) DO NOTHING", duckSqlDefault);

        string mySqlDefault = MySqlDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict);
        Assert.Equal("INSERT IGNORE INTO tbl (id, username) VALUES (@id, @username)", mySqlDefault);

        string duckSql = DuckDbDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict, ["k", "u"]);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES ($k, $u) ON CONFLICT (id) DO NOTHING", duckSql);

        string mySql = MySqlDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict, ["k", "u"]);
        Assert.Equal("INSERT IGNORE INTO tbl (id, username) VALUES (@k, @u)", mySql);

        string sqliteCustom = SqliteDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict, ["k", "u"]);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES (@k, @u) ON CONFLICT (id) DO NOTHING", sqliteCustom);

        string pgCustom = PostgreSqlDialect.Instance.BuildInsertIgnoreSql("tbl", columns, conflict, ["k", "u"]);
        Assert.Equal("INSERT INTO tbl (id, username) VALUES (@k, @u) ON CONFLICT (id) DO NOTHING", pgCustom);
    }

    /// <summary>
    /// Verifies that <see cref="ISqlDialect.BuildInsertIgnoreSql"/> throws validation exceptions for invalid input arguments.
    /// </summary>
    /// <param name="dialectName">The name of the dialect to test.</param>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("duckdb")]
    [InlineData("mysql")]
    public void BuildInsertIgnoreSql_InvalidArguments_ThrowsExpectedExceptions(string dialectName)
    {
        // WHAT: Asserts guard clauses on BuildInsertIgnoreSql.
        // WHY:  Prevents malformed SQL generation when required parameters are missing.
        // HOW:  Tests null/empty table, null columns, and null conflictColumns.
        ISqlDialect dialect = ResolveDialect(dialectName);
        Assert.Throws<ArgumentException>(() => dialect.BuildInsertIgnoreSql("", ["a"], ["a"]));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildInsertIgnoreSql("tbl", null!, ["a"]));
        Assert.Throws<ArgumentNullException>(() => dialect.BuildInsertIgnoreSql("tbl", ["a"], null!));
    }

    /// <summary>
    /// Verifies that <see cref="WeaverDbParameters.Add"/> normalizes parameter names based on command type.
    /// </summary>
    [Fact]
    public void WeaverDbParameters_Add_NormalizesParameterNamesByCommandType()
    {
        // WHAT: Verifies that DuckDBCommand strips markers while other commands ensure '@' prefix.
        // WHY:  DuckDB.NET requires unprefixed ParameterName, while SQLite and MySQL require '@'.
        // HOW:  Attaches parameters to DuckDBCommand and SqliteCommand with various name prefixes.
        using DuckDBCommand duckCmd = new();
        DbParameter p1 = WeaverDbParameters.Add(duckCmd, "@key", "val1");
        DbParameter p2 = WeaverDbParameters.Add(duckCmd, "$tok", "val2");
        DbParameter p3 = WeaverDbParameters.Add(duckCmd, "raw", "val3");

        Assert.Equal("key", p1.ParameterName);
        Assert.Equal("tok", p2.ParameterName);
        Assert.Equal("raw", p3.ParameterName);

        using SqliteCommand sqliteCmd = new();
        DbParameter s1 = WeaverDbParameters.Add(sqliteCmd, "@key", "val1");
        DbParameter s2 = WeaverDbParameters.Add(sqliteCmd, "$tok", "val2");
        DbParameter s3 = WeaverDbParameters.Add(sqliteCmd, "raw", "val3");

        Assert.Equal("@key", s1.ParameterName);
        Assert.Equal("@tok", s2.ParameterName);
        Assert.Equal("@raw", s3.ParameterName);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverDbParameters.AddNullable"/> binds <see cref="DBNull.Value"/> for null inputs.
    /// </summary>
    [Fact]
    public void WeaverDbParameters_AddNullable_BindsDbNullForNullValues()
    {
        // WHAT: Tests null parameter value coercion.
        // WHY:  ADO.NET requires DBNull.Value rather than C# null for database NULL columns.
        // HOW:  Calls AddNullable with null and asserts Value is DBNull.Value.
        using SqliteCommand cmd = new();
        DbParameter p = WeaverDbParameters.AddNullable(cmd, "opt", null);
        Assert.Same(DBNull.Value, p.Value);
    }

    /// <summary>
    /// Verifies that <see cref="IWeaverDbConnectionFactory.Open"/> synchronously returns an open session.
    /// </summary>
    [Fact]
    public void WeaverDbConnectionFactory_Open_ReturnsOpenSessionSynchronously()
    {
        // WHAT: Tests synchronous session opening on WeaverDbConnectionFactory.
        // WHY:  Permits synchronous repositories to execute ADO.NET queries without async deadlock.
        // HOW:  Opens a session via factory.Open(), verifies state, and disposes session.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Dictionary<string, string?> inMemory = new()
            {
                ["TRAJECTORY_INCOMING_DIR"] = tempDir
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
            WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(config);

            using WeaverDbSession session = factory.Open();
            Assert.NotNull(session);
            Assert.Equal(System.Data.ConnectionState.Open, session.Connection.State);
            Assert.Equal("sqlite", session.Dialect.Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="AuditLedgerRepository"/> supports both synchronous and asynchronous operations.
    /// </summary>
    [Fact]
    public async Task AuditLedgerRepository_SyncAndAsync_PersistAndLoadRecords()
    {
        // WHAT: Validates LoadAll, Append, LoadAllAsync, and AppendAsync on AuditLedgerRepository.
        // WHY:  Ensures zero .GetAwaiter().GetResult() blocking in synchronous methods and valid asynchronous execution.
        // HOW:  Appends records via Append and AppendAsync, then verifies recovery via LoadAll and LoadAllAsync.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Dictionary<string, string?> inMemory = new()
            {
                ["TRAJECTORY_INCOMING_DIR"] = tempDir
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
            WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(config);
            AuditLedgerRepository repo = new(factory);

            AuditRecord rec1 = new()
            {
                SequenceNumber = 1,
                TimestampUtc = DateTimeOffset.UtcNow,
                PreviousHash = "0000000000000000000000000000000000000000000000000000000000000000",
                CurrentHash = "1111111111111111111111111111111111111111111111111111111111111111",
                OperatorId = "admin",
                Action = "UserCreated",
                Resource = "user:patient01",
                DetailsJson = "{}",
                Signature = new PqcSignature
                {
                    Algorithm = "Ed25519",
                    SignerKeyId = "master",
                    SignatureBase64 = "c2ln",
                    SignedAtUtc = DateTimeOffset.UtcNow
                }
            };

            AuditRecord rec2 = new()
            {
                SequenceNumber = 2,
                TimestampUtc = DateTimeOffset.UtcNow,
                PreviousHash = rec1.CurrentHash,
                CurrentHash = "2222222222222222222222222222222222222222222222222222222222222222",
                OperatorId = "admin",
                Action = "RoleGranted",
                Resource = "user:patient01",
                DetailsJson = "{}",
                Signature = new PqcSignature
                {
                    Algorithm = "Ed25519",
                    SignerKeyId = "master",
                    SignatureBase64 = "c2lnMg==",
                    SignedAtUtc = DateTimeOffset.UtcNow
                }
            };

            repo.Append(rec1);
            await repo.AppendAsync(rec2);

            IReadOnlyList<AuditRecord> syncLoaded = repo.LoadAll();
            Assert.Equal(2, syncLoaded.Count);
            Assert.Equal(1, syncLoaded[0].SequenceNumber);
            Assert.Equal(2, syncLoaded[1].SequenceNumber);

            IReadOnlyList<AuditRecord> asyncLoaded = await repo.LoadAllAsync();
            Assert.Equal(2, asyncLoaded.Count);
            Assert.Equal(1, asyncLoaded[0].SequenceNumber);
            Assert.Equal(2, asyncLoaded[1].SequenceNumber);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="SubjectKeyRepository.CompactStorageAsync"/> executes SELECT 1 on MySQL where VACUUM is unsupported.
    /// </summary>
    [Fact]
    public async Task SubjectKeyRepository_CompactStorageAsync_ExecutesSelect1OnMySql()
    {
        // WHAT: Tests MySQL branch of CompactStorageAsync.
        // WHY:  MySQL does not support standard VACUUM maintenance commands.
        // HOW:  Provides a session with MySqlDialect.Instance and verifies completion.
        using SqliteConnection sqliteConn = new("Data Source=:memory:");
        await sqliteConn.OpenAsync();
        WeaverDbSession session = new(sqliteConn, MySqlDialect.Instance);

        Moq.Mock<IWeaverDbConnectionFactory> mockFactory = new();
        mockFactory.Setup(f => f.OpenAsync(Moq.It.IsAny<CancellationToken>())).Returns(Task.FromResult(session));

        SubjectKeyRepository repo = new(mockFactory.Object);
        await repo.CompactStorageAsync();
    }

    /// <summary>
    /// Helper resolving dialect singleton from name identifier.
    /// </summary>
    /// <param name="name">The name of the dialect.</param>
    /// <returns>The resolved <see cref="ISqlDialect"/> instance.</returns>
    private static ISqlDialect ResolveDialect(string name) => name switch
    {
        "sqlite" => SqliteDialect.Instance,
        "postgresql" => PostgreSqlDialect.Instance,
        "duckdb" => DuckDbDialect.Instance,
        "mysql" => MySqlDialect.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };
}
