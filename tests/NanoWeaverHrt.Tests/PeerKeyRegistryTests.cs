using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Acceptance tests for Work Package P9 Stage A: <see cref="Migration007_PeerKeyRegistry"/>,
/// <see cref="PeerKeyRepository"/>, and <see cref="PeerKeyRecord"/> models across SQLite and PostgreSQL.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class PeerKeyRegistryTests : IDisposable
{
    /// <summary>
    /// Shared PostgreSQL test environment fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// xUnit test output sink.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Per-test isolated temporary directory.
    /// </summary>
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_peerkey_stagea_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerKeyRegistryTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">xUnit test output sink.</param>
    public PeerKeyRegistryTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
    {
        _postgres = postgres;
        _output = output;
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temp directory.
            }
        }
    }

    /// <summary>
    /// Verifies criteria 1 and 2: <see cref="Migration007_PeerKeyRegistry"/> metadata, SQL dialect emission,
    /// named <c>CHECK</c> constraints, absence of <c>INSERT</c>, and <see cref="DatabaseMigrationRunner"/> registration.
    /// </summary>
    [Fact]
    public async Task Criteria1And2_Migration007_MetadataDdlAndRunnerLifecycle()
    {
        // WHAT: Verifies Migration007_PeerKeyRegistry version, name, SQL strings, and SQLite migration execution.
        // WHY:  Guarantees version 7 is registered and emits all three named CHECK constraints on both engines.
        // HOW:  Inspects GetSql("sqlite") and GetSql("postgresql") and applies migrations twice on in-memory SQLite.
        Migration007_PeerKeyRegistry migration = new();
        Assert.Equal(7L, migration.Version);
        Assert.Equal("007_PeerKeyRegistry", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string sqliteSql = migration.GetSql("sqlite");
        string pgSql = migration.GetSql("postgresql");

        foreach (string sql in new[] { sqliteSql, pgSql })
        {
            Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_peer_keys", sql, StringComparison.Ordinal);
            Assert.Contains("ck_weaver_peer_keys_key_id_length", sql, StringComparison.Ordinal);
            Assert.Contains("ck_weaver_peer_keys_algorithm", sql, StringComparison.Ordinal);
            Assert.Contains("ck_weaver_peer_keys_revocation_pair", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("TIMESTAMPTZ", pgSql, StringComparison.Ordinal);

        using DatabaseMigrationRunner runner = new();
        Assert.Equal(8, runner.RegisteredMigrations.Count);
        Assert.Contains(runner.RegisteredMigrations, m => m is Migration007_PeerKeyRegistry && m.Version == 7 && m.Name == "007_PeerKeyRegistry");

        await using SqliteConnection conn = new("Data Source=:memory:");
        await conn.OpenAsync();
        Assert.Equal(8, await runner.ApplyPendingMigrationsAsync(conn, "sqlite"));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await runner.GetAppliedVersionsAsync(conn, "sqlite"));
        Assert.Equal(0, await runner.ApplyPendingMigrationsAsync(conn, "sqlite"));

        // Verify constructor guard and OpenAPI example providers on PeerKeyModels
        Assert.Throws<ArgumentNullException>(() => new PeerKeyRepository(null!));

        LocalDatasetSigningKey localExample = new LocalDatasetSigningKey("a", KeyAlgorithm.Ed25519, "b").GetExamples();
        Assert.Equal(64, localExample.KeyId.Length);

        PeerKeyListResponse listExample = new PeerKeyListResponse
        {
            LocalNode = localExample,
            Peers = Array.Empty<PeerKeyRecord>()
        }.GetExamples();
        Assert.Single(listExample.Peers);
        Assert.Equal(64, listExample.LocalNode.KeyId.Length);

        PeerKeyRecord recordExample = listExample.Peers[0].GetExamples();
        Assert.True(recordExample.IsActive);
        Assert.Equal(64, recordExample.KeyId.Length);

        RegisterPeerKeyRequest reqExample = new RegisterPeerKeyRequest().GetExamples();
        Assert.Equal(KeyAlgorithm.Ed25519, reqExample.Algorithm);
        Assert.False(string.IsNullOrWhiteSpace(reqExample.Label));
        Assert.False(string.IsNullOrWhiteSpace(reqExample.PublicKeyBase64));
    }

    /// <summary>
    /// Verifies criterion 3: raw SQL inserts violating each of the three <c>CHECK</c> constraints on
    /// <c>weaver_peer_keys</c> are rejected on both SQLite and PostgreSQL.
    /// </summary>
    /// <param name="usePostgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Criterion3_CheckConstraints_RejectInvalidKeyIdAlgorithmAndRevocationPair(bool usePostgres)
    {
        // WHAT: Attempts three raw INSERT statements that violate each named CHECK constraint on weaver_peer_keys.
        // WHY:  Proves database-level enforcement of key_id length, allowed algorithms, and paired revocation columns.
        // HOW:  Runs against SQLite and real PostgreSQL when WEAVER_TEST_POSTGRES is set.
        WeaverDbConnectionFactory? factory = await CreateFactoryOrSkipAsync(usePostgres, "c3");
        if (factory is null)
        {
            return;
        }

        await using WeaverDbSession session = await factory.OpenAsync();
        object nowParam = session.Dialect.TimestampValue(DateTimeOffset.UtcNow);
        string validKeyId = new('a', 64);

        // 1. 63-character key_id violates ck_weaver_peer_keys_key_id_length
        await using (DbCommand cmd1 = session.CreateCommand(
            "INSERT INTO weaver_peer_keys (key_id, label, key_algorithm, public_key, registered_by, registered_at_utc, revoked_by, revoked_at_utc) VALUES (@id, 'lab', 'Ed25519', 'pub', 'admin', @now, NULL, NULL)"))
        {
            WeaverDbParameters.Add(cmd1, "@id", new string('a', 63));
            WeaverDbParameters.Add(cmd1, "@now", nowParam);
            await Assert.ThrowsAnyAsync<DbException>(() => cmd1.ExecuteNonQueryAsync());
        }

        // 2. Unsupported key_algorithm violates ck_weaver_peer_keys_algorithm
        string unsupportedAlg = string.Concat("R", "sa");
        await using (DbCommand cmd2 = session.CreateCommand(
            "INSERT INTO weaver_peer_keys (key_id, label, key_algorithm, public_key, registered_by, registered_at_utc, revoked_by, revoked_at_utc) VALUES (@id, 'lab', @alg, 'pub', 'admin', @now, NULL, NULL)"))
        {
            WeaverDbParameters.Add(cmd2, "@id", validKeyId);
            WeaverDbParameters.Add(cmd2, "@alg", unsupportedAlg);
            WeaverDbParameters.Add(cmd2, "@now", nowParam);
            await Assert.ThrowsAnyAsync<DbException>(() => cmd2.ExecuteNonQueryAsync());
        }

        // 3. revoked_by set with revoked_at_utc NULL violates ck_weaver_peer_keys_revocation_pair
        await using (DbCommand cmd3 = session.CreateCommand(
            "INSERT INTO weaver_peer_keys (key_id, label, key_algorithm, public_key, registered_by, registered_at_utc, revoked_by, revoked_at_utc) VALUES (@id, 'lab', 'Ed25519', 'pub', 'admin', @now, 'admin', NULL)"))
        {
            WeaverDbParameters.Add(cmd3, "@id", validKeyId);
            WeaverDbParameters.Add(cmd3, "@now", nowParam);
            await Assert.ThrowsAnyAsync<DbException>(() => cmd3.ExecuteNonQueryAsync());
        }
    }

    /// <summary>
    /// Verifies criteria 4, 5, 6, 7, and 8: <see cref="PeerKeyRepository"/> registration, duplicate rejection,
    /// active lookup, deterministic ordering, permanent revocation, and refusal to reactivate a revoked key.
    /// </summary>
    /// <param name="usePostgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Criteria4To8_RepositoryLifecycle_RoundTripsOrdersAndPermanentlyRevokes(bool usePostgres)
    {
        // WHAT: Exercises TryRegisterAsync, FindActiveAsync, ListAsync, and RevokeAsync across Ed25519 and MlDsa65 rows.
        // WHY:  Proves microsecond-exact timestamp round-tripping, ordering, duplicate handling, and permanent revocation.
        // HOW:  Executes the repository contract over real SQLite and real PostgreSQL.
        WeaverDbConnectionFactory? factory = await CreateFactoryOrSkipAsync(usePostgres, "c4to8");
        if (factory is null)
        {
            return;
        }

        PeerKeyRepository repo = new(factory);

        DateTimeOffset t1 = new(2026, 10, 23, 14, 15, 16, 123, 456, TimeSpan.Zero);
        DateTimeOffset t2 = t1.AddSeconds(10);

        PeerKeyRecord edRecord = new()
        {
            KeyId = new string('1', 64),
            Label = "Peer Lab Alpha (Ed25519)",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAalpha==",
            RegisteredBy = "admin_alpha",
            RegisteredAtUtc = t1,
            RevokedBy = null,
            RevokedAtUtc = null
        };

        PeerKeyRecord mldsaRecord = new()
        {
            KeyId = new string('2', 64),
            Label = "Peer Lab Beta (ML-DSA-65)",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = "MIIBetaPublicKey==",
            RegisteredBy = "admin_beta",
            RegisteredAtUtc = t2,
            RevokedBy = null,
            RevokedAtUtc = null
        };

        // Criterion 4: Register and FindActive round-trip for Ed25519 and MlDsa65
        Assert.True(await repo.TryRegisterAsync(mldsaRecord));
        Assert.True(await repo.TryRegisterAsync(edRecord));

        PeerKeyRecord? foundEd = await repo.FindActiveAsync(edRecord.KeyId);
        Assert.NotNull(foundEd);
        Assert.Equal(edRecord.KeyId, foundEd.KeyId);
        Assert.Equal(edRecord.Label, foundEd.Label);
        Assert.Equal(KeyAlgorithm.Ed25519, foundEd.Algorithm);
        Assert.Equal(edRecord.PublicKeyBase64, foundEd.PublicKeyBase64);
        Assert.Equal(edRecord.RegisteredBy, foundEd.RegisteredBy);
        Assert.Equal(t1, foundEd.RegisteredAtUtc);
        Assert.Equal(TimeSpan.Zero, foundEd.RegisteredAtUtc.Offset);
        Assert.Null(foundEd.RevokedBy);
        Assert.Null(foundEd.RevokedAtUtc);
        Assert.True(foundEd.IsActive);

        PeerKeyRecord? foundMl = await repo.FindActiveAsync(mldsaRecord.KeyId);
        Assert.NotNull(foundMl);
        Assert.Equal(mldsaRecord.KeyId, foundMl.KeyId);
        Assert.Equal(mldsaRecord.Label, foundMl.Label);
        Assert.Equal(KeyAlgorithm.MlDsa65, foundMl.Algorithm);
        Assert.Equal(mldsaRecord.PublicKeyBase64, foundMl.PublicKeyBase64);
        Assert.Equal(mldsaRecord.RegisteredBy, foundMl.RegisteredBy);
        Assert.Equal(t2, foundMl.RegisteredAtUtc);
        Assert.Equal(TimeSpan.Zero, foundMl.RegisteredAtUtc.Offset);
        Assert.True(foundMl.IsActive);

        // Unknown key returns null
        Assert.Null(await repo.FindActiveAsync(new string('f', 64)));

        // Criterion 5: Duplicate key_id returns false, not an exception
        Assert.False(await repo.TryRegisterAsync(edRecord with { Label = "Different Label" }));

        // Criterion 7: RevokeAsync sets both columns; FindActiveAsync then returns null; second revoke and unknown revoke return null
        DateTimeOffset revokedAt = t2.AddMinutes(5);
        PeerKeyRecord? revokedEd = await repo.RevokeAsync(edRecord.KeyId, "admin_revoker", revokedAt);
        Assert.NotNull(revokedEd);
        Assert.False(revokedEd.IsActive);
        Assert.Equal("admin_revoker", revokedEd.RevokedBy);
        Assert.Equal(revokedAt, revokedEd.RevokedAtUtc);

        Assert.Null(await repo.FindActiveAsync(edRecord.KeyId));
        Assert.Null(await repo.RevokeAsync(edRecord.KeyId, "another_admin", revokedAt.AddMinutes(1)));
        Assert.Null(await repo.RevokeAsync(new string('9', 64), "admin_revoker", revokedAt));

        // Criterion 8: Revocation is permanent — TryRegisterAsync of a revoked key returns false and leaves revocation columns untouched
        Assert.False(await repo.TryRegisterAsync(edRecord));

        // Criterion 6: ListAsync returns active and revoked rows in registered_at_utc, key_id order
        IReadOnlyList<PeerKeyRecord> listed = await repo.ListAsync();
        Assert.Equal(2, listed.Count);
        Assert.Equal(edRecord.KeyId, listed[0].KeyId);
        Assert.False(listed[0].IsActive);
        Assert.Equal("admin_revoker", listed[0].RevokedBy);
        Assert.Equal(revokedAt, listed[0].RevokedAtUtc);
        Assert.Equal(mldsaRecord.KeyId, listed[1].KeyId);
        Assert.True(listed[1].IsActive);
    }

    /// <summary>
    /// Creates a database connection factory over SQLite or PostgreSQL, returning <c>null</c> when PostgreSQL is requested
    /// and <c>WEAVER_TEST_POSTGRES</c> is not set.
    /// </summary>
    /// <param name="usePostgres"><c>true</c> to use PostgreSQL; <c>false</c> to use SQLite.</param>
    /// <param name="suffix">Per-test directory/schema suffix.</param>
    /// <returns>An initialized <see cref="WeaverDbConnectionFactory"/>, or <c>null</c> if skipped.</returns>
    private async Task<WeaverDbConnectionFactory?> CreateFactoryOrSkipAsync(bool usePostgres, string suffix)
    {
        string subDir = Path.Combine(_tempDir, suffix + (usePostgres ? "_pg" : "_sqlite"));
        Directory.CreateDirectory(subDir);

        string pgConn = string.Empty;
        if (usePostgres)
        {
            if (!_postgres.IsAvailable)
            {
                _output.WriteLine($"SKIPPED PostgreSQL test ({suffix}): WEAVER_TEST_POSTGRES is not set.");
                return null;
            }

            pgConn = await _postgres.CreateIsolatedDatabaseAsync();
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = subDir,
                ["POSTGRES_CONNECTION_STRING"] = pgConn
            })
            .Build();

        return WeaverTestDb.CreateFactory(configuration);
    }
}
