using System.Data.Common;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying multi-engine SQL dialect generation and execution of all 13 database migrations
/// across SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB backends.
/// </summary>
public sealed class MultiEngineMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NodeSecretStore _secretStore;
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiEngineMigrationTests"/> class.
    /// </summary>
    public MultiEngineMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"weaver_multi_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();

        _secretStore = new NodeSecretStore(config);
        _protector = new ProfileDataProtector(_secretStore);
    }

    /// <summary>
    /// Cleans up temporary test files.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Verifies that all 13 database migrations emit valid, non-null SQL across all four supported dialects:
    /// SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB.
    /// </summary>
    [Fact]
    public void GetSql_All13Migrations_ReturnExpectedSqlAcrossAllDialects()
    {
        DatabaseMigrationRunner runner = new(protector: _protector);
        string[] dialects = ["sqlite", "postgresql", "duckdb", "mysql", "mariadb"];

        Assert.Equal(13, runner.RegisteredMigrations.Count);

        foreach (IDatabaseMigration migration in runner.RegisteredMigrations)
        {
            foreach (string dialect in dialects)
            {
                string sql = migration.GetSql(dialect);
                Assert.NotNull(sql);

                // Migrations 005, 006, and 010 are data encryption migrations whose DDL is PostgreSQL and MySQL column widening;
                // for SQLite and DuckDB, their GetSql is empty string by architectural design.
                if (migration.Version is 5 or 6 or 10)
                {
                    if (dialect is "sqlite" or "duckdb")
                    {
                        Assert.Empty(sql);
                    }
                    else
                    {
                        Assert.NotEmpty(sql);
                    }
                }
                else
                {
                    Assert.NotEmpty(sql);
                }
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="DatabaseMigrationRunner.ApplyPendingMigrationsAsync"/> executes all 13 migrations cleanly
    /// on an in-memory SQLite database and is fully idempotent on subsequent calls.
    /// </summary>
    [Fact]
    public async Task ApplyPendingMigrationsAsync_InMemorySqlite_ExecutesAll13MigrationsCleanly()
    {
        DatabaseMigrationRunner runner = new(protector: _protector);
        await using SqliteConnection connection = new("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        int applied = await runner.ApplyPendingMigrationsAsync(connection, "sqlite");
        Assert.Equal(13, applied);

        IReadOnlyList<long> versions = await runner.GetAppliedVersionsAsync(connection, "sqlite");
        Assert.Equal(13, versions.Count);
        for (int i = 1; i <= 13; i++)
        {
            Assert.Contains(i, versions);
        }

        // Idempotency: second run applies 0 migrations
        int appliedSecond = await runner.ApplyPendingMigrationsAsync(connection, "sqlite");
        Assert.Equal(0, appliedSecond);
    }

    /// <summary>
    /// Verifies that <see cref="DatabaseMigrationRunner.ApplyPendingMigrationsAsync"/> executes all 13 migrations cleanly
    /// on an in-process DuckDB database and is fully idempotent on subsequent calls.
    /// </summary>
    [Fact]
    public async Task ApplyPendingMigrationsAsync_InProcessDuckDb_ExecutesAll13MigrationsCleanly()
    {
        DatabaseMigrationRunner runner = new(protector: _protector);
        string dbPath = Path.Combine(_tempDir, "duck_migrations.duckdb");
        await using DuckDBConnection connection = new($"Data Source={dbPath}");
        await connection.OpenAsync();

        int applied = await runner.ApplyPendingMigrationsAsync(connection, "duckdb");
        Assert.Equal(13, applied);

        IReadOnlyList<long> versions = await runner.GetAppliedVersionsAsync(connection, "duckdb");
        Assert.Equal(13, versions.Count);
        for (int i = 1; i <= 13; i++)
        {
            Assert.Contains(i, versions);
        }

        // Idempotency: second run applies 0 migrations
        int appliedSecond = await runner.ApplyPendingMigrationsAsync(connection, "duckdb");
        Assert.Equal(0, appliedSecond);
    }

    /// <summary>
    /// Verifies that <see cref="Migration005_EncryptPatientPhi"/> and <see cref="Migration006_EncryptGenderIdentity"/>
    /// encrypt existing plaintext rows in place using DuckDB-specific parameter syntax ($v0, $key).
    /// </summary>
    [Fact]
    public async Task Migrations005And006_DuckDbWithPlaintextData_EncryptsRowsInPlace()
    {
        string dbPath = Path.Combine(_tempDir, "duck_encrypt_test.duckdb");
        await using DuckDBConnection connection = new($"Data Source={dbPath}");
        await connection.OpenAsync();

        DatabaseMigrationRunner runner = new(protector: _protector);

        // Apply migrations 001 to 004
        IDatabaseMigration m1 = new Migration001_InitialSchema();
        IDatabaseMigration m2 = new Migration002_UserAuditAndIndexes();
        IDatabaseMigration m3 = new Migration003_UserProfileAddressesAndContacts();
        IDatabaseMigration m4 = new Migration004_PatientDnaImportsAndResults();

        DatabaseMigrationRunner partialRunner = new([m1, m2, m3, m4], _protector);
        int initialApplied = await partialRunner.ApplyPendingMigrationsAsync(connection, "duckdb");
        Assert.Equal(4, initialApplied);

        // Insert a user and plaintext profile into DuckDB
        await using (DbCommand cmdUser = connection.CreateCommand())
        {
            cmdUser.CommandText = "INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, is_active) VALUES ('patient_duck', 'Patient', 'ApiWeb', 'Ed25519', 'pk_test', TRUE);";
            await cmdUser.ExecuteNonQueryAsync();
        }

        await using (DbCommand cmdProfile = connection.CreateCommand())
        {
            cmdProfile.CommandText = "INSERT INTO weaver_user_profiles (username, first_name, last_name, preferred_name, email_address, phone_number, regimen, gender_assigned_at_birth, pronouns_json) VALUES ('patient_duck', 'Jane', 'Doe', 'Janey', 'jane@example.org', '+1-555-0199', 'MaleToFemale', 'Male', '[\"she/her\"]');";
            await cmdProfile.ExecuteNonQueryAsync();
        }

        await using (DbCommand cmdDna = connection.CreateCommand())
        {
            cmdDna.CommandText = "INSERT INTO weaver_patient_dna_records (id, username, title, regimen, target_tissue, active_ligand, nanocarrier_geometry, scaffold_fasta, provenance_sha256, notes) VALUES ('dna_1', 'patient_duck', 'Title', 'MaleToFemale', 'Tissue', 'Ligand', 'Geometry', 'ATGC', 'sha_prov', 'Secret clinician notes');";
            await cmdDna.ExecuteNonQueryAsync();
        }

        // Apply migrations 005, 006, 010
        Migration005_EncryptPatientPhi m5 = new(_protector);
        Migration006_EncryptGenderIdentity m6 = new(_protector);
        Migration010_EncryptPatientDnaPhi m10 = new(_protector);

        await m5.UpAsync(connection, null, "duckdb", CancellationToken.None);
        await m6.UpAsync(connection, null, "duckdb", CancellationToken.None);
        await m10.UpAsync(connection, null, "duckdb", CancellationToken.None);

        // Verify that fields in weaver_user_profiles are now encrypted
        await using (DbCommand checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT first_name, last_name, phone_number, regimen, gender_assigned_at_birth FROM weaver_user_profiles WHERE username = 'patient_duck';";
            await using DbDataReader reader = await checkCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            string encFirstName = reader.GetString(0);
            string encLastName = reader.GetString(1);
            string encPhone = reader.GetString(2);
            string encRegimen = reader.GetString(3);
            string encSex = reader.GetString(4);

            Assert.True(ProfileDataProtector.IsEncrypted(encFirstName));
            Assert.True(ProfileDataProtector.IsEncrypted(encLastName));
            Assert.True(ProfileDataProtector.IsEncrypted(encPhone));
            Assert.True(ProfileDataProtector.IsEncrypted(encRegimen));
            Assert.True(ProfileDataProtector.IsEncrypted(encSex));

            Assert.Equal("Jane", _protector.DecryptField(encFirstName));
            Assert.Equal("Doe", _protector.DecryptField(encLastName));
            Assert.Equal("+1-555-0199", _protector.DecryptField(encPhone));
            Assert.Equal("MaleToFemale", _protector.DecryptField(encRegimen));
            Assert.Equal("Male", _protector.DecryptField(encSex));
        }

        // Verify that fields in weaver_patient_dna_records are now encrypted
        await using (DbCommand checkDnaCmd = connection.CreateCommand())
        {
            checkDnaCmd.CommandText = "SELECT regimen, notes FROM weaver_patient_dna_records WHERE id = 'dna_1';";
            await using DbDataReader reader = await checkDnaCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            string encRegimen = reader.GetString(0);
            string encNotes = reader.GetString(1);

            Assert.True(ProfileDataProtector.IsEncrypted(encRegimen));
            Assert.True(ProfileDataProtector.IsEncrypted(encNotes));

            Assert.Equal("MaleToFemale", _protector.DecryptField(encRegimen));
            Assert.Equal("Secret clinician notes", _protector.DecryptField(encNotes));
        }
    }
}
