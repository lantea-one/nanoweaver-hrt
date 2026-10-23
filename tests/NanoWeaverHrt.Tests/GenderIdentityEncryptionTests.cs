using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Proves that the regimen, sex assigned at birth and pronouns of a patient are encrypted at rest (migration 006).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why these tests read raw columns:</strong> a save-then-load round trip passes even when nothing is
/// encrypted, so every scenario also issues its own <c>SELECT</c> and inspects the exact stored text.
/// </para>
/// <para>
/// Each backend scenario runs against SQLite always, and against a real PostgreSQL server when
/// <c>WEAVER_TEST_POSTGRES</c> is set.
/// </para>
/// </remarks>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class GenderIdentityEncryptionTests : IDisposable
{
    /// <summary>
    /// The envelope prefix every encrypted value must carry.
    /// </summary>
    private const string EnvelopePrefix = "data:application/encrypted;base64,";

    /// <summary>
    /// Distinctive pronoun tokens that must never appear in a raw column.
    /// </summary>
    private static readonly string[] PronounTokens = ["xe-Quillpron", "zir-Marchpron"];

    /// <summary>
    /// Shared PostgreSQL fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// Test output sink used to report an unexecuted PostgreSQL half.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Isolated directory for SQLite files and key files.
    /// </summary>
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_gender_tests_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="GenderIdentityEncryptionTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">Test output sink.</param>
    public GenderIdentityEncryptionTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
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
    /// One raw stored row of the three protected columns.
    /// </summary>
    /// <param name="Regimen">Stored regimen text, or <c>null</c>.</param>
    /// <param name="Gender">Stored sex-assigned-at-birth text.</param>
    /// <param name="Pronouns">Stored pronouns text.</param>
    private sealed record RawTrio(string? Regimen, string Gender, string Pronouns);

    /// <summary>
    /// One database backend prepared for a scenario.
    /// </summary>
    /// <param name="Configuration">Configuration pointing at this backend's isolated database.</param>
    private sealed record Backend(IConfiguration Configuration)
    {
        /// <summary>
        /// Builds a connection factory for this backend over the supplied migration runner.
        /// </summary>
        /// <param name="runner">Runner that decides which migrations apply.</param>
        /// <returns>A factory bound to this backend.</returns>
        public WeaverDbConnectionFactory CreateFactory(DatabaseMigrationRunner runner) => WeaverTestDb.CreateFactory(Configuration, runner);
    }

    /// <summary>
    /// Prepares an empty, isolated database on the requested backend.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL, <c>false</c> for SQLite.</param>
    /// <returns>The backend, or <c>null</c> when PostgreSQL was requested but is not configured.</returns>
    private async Task<Backend?> CreateBackendAsync(bool postgres)
    {
        // WHAT: Creates a brand-new database for one scenario.
        // WHY:  Migrations must run from zero with nothing left over from another test.
        // HOW:  SQLite gets its own directory; PostgreSQL gets its own CREATE DATABASE from the fixture.
        string scenarioDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scenarioDir);
        Dictionary<string, string?> settings = new()
        {
            ["TRAJECTORY_INCOMING_DIR"] = scenarioDir,
            ["POSTGRES_CONNECTION_STRING"] = string.Empty
        };

        if (!postgres)
        {
            IConfiguration sqliteConfiguration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new Backend(sqliteConfiguration);
        }

        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the PostgreSQL half unexecuted when no server was supplied.
            // WHY:  Assert.Skip does not exist in xunit 2.9.2 and ambient network state must not decide a result.
            // HOW:  Reports through ITestOutputHelper and lets the caller return.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return null;
        }

        settings["POSTGRES_CONNECTION_STRING"] = await _postgres.CreateIsolatedDatabaseAsync();
        IConfiguration pgConfiguration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new Backend(pgConfiguration);
    }

    /// <summary>
    /// Creates a protector over a key directory unique to the test instance.
    /// </summary>
    /// <returns>A protector holding a fresh PHI master key.</returns>
    private ProfileDataProtector CreateProtector()
    {
        IConfiguration keyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = Path.Combine(_tempDir, "keys")
            })
            .Build();
        return new ProfileDataProtector(new NodeSecretStore(keyConfiguration));
    }

    /// <summary>
    /// Builds the list of migrations 1 to 5, the schema before the gender-identity columns were encrypted.
    /// </summary>
    /// <param name="protector">Protector handed to migration 005.</param>
    /// <returns>Migrations 1 to 5.</returns>
    private static IDatabaseMigration[] UpToMigration005(ProfileDataProtector protector) =>
    [
        new Migration001_InitialSchema(),
        new Migration002_UserAuditAndIndexes(),
        new Migration003_UserProfileAddressesAndContacts(),
        new Migration004_PatientDnaImportsAndResults(),
        new Migration005_EncryptPatientPhi(protector)
    ];

    /// <summary>
    /// Inserts an account and a profile row whose three gender-identity columns are written exactly as supplied.
    /// </summary>
    /// <param name="session">Open database session.</param>
    /// <param name="username">Account name.</param>
    /// <param name="trio">Raw values for regimen, gender and pronouns.</param>
    /// <returns>A task representing the inserts.</returns>
    private static async Task InsertRawProfileAsync(WeaverDbSession session, string username, RawTrio trio)
    {
        object now = session.Dialect.TimestampValue(DateTimeOffset.UtcNow);
        await using (DbCommand user = session.CreateCommand(
            "INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, created_at_utc) VALUES (@u, 'Patient', 'ApiWeb', 'Ed25519', 'k', @t);"))
        {
            WeaverDbParameters.Add(user, "@u", username);
            WeaverDbParameters.Add(user, "@t", now);
            await user.ExecuteNonQueryAsync();
        }

        await using DbCommand cmd = session.CreateCommand(@"
            INSERT INTO weaver_user_profiles (username, first_name, last_name, regimen, gender_assigned_at_birth, pronouns_json,
                language_preference, created_at_utc, updated_at_utc)
            VALUES (@u, '', '', @reg, @gen, @pro, 'en-US', @t, @t);");
        WeaverDbParameters.Add(cmd, "@u", username);
        WeaverDbParameters.AddNullable(cmd, "@reg", trio.Regimen);
        WeaverDbParameters.Add(cmd, "@gen", trio.Gender);
        WeaverDbParameters.Add(cmd, "@pro", trio.Pronouns);
        WeaverDbParameters.Add(cmd, "@t", now);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads the three protected columns of every profile row exactly as the database stores them.
    /// </summary>
    /// <param name="factory">Factory for the backend.</param>
    /// <returns>Raw trios keyed by username.</returns>
    private static async Task<Dictionary<string, RawTrio>> ReadRawAsync(IWeaverDbConnectionFactory factory)
    {
        Dictionary<string, RawTrio> rows = [];
        await using WeaverDbSession session = await factory.OpenAsync();
        await using DbCommand cmd = session.CreateCommand("SELECT username, regimen, gender_assigned_at_birth, pronouns_json FROM weaver_user_profiles;");
        await using DbDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows[reader.GetString(0)] = new RawTrio(reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        return rows;
    }

    /// <summary>
    /// Asserts a stored trio is sealed and leaks none of the plaintext tokens.
    /// </summary>
    /// <param name="trio">The raw stored values.</param>
    /// <param name="expectRegimenNull">Whether the regimen must be stored as SQL NULL.</param>
    private static void AssertSealed(RawTrio trio, bool expectRegimenNull)
    {
        if (expectRegimenNull)
        {
            Assert.Null(trio.Regimen);
        }
        else
        {
            Assert.NotNull(trio.Regimen);
            Assert.StartsWith(EnvelopePrefix, trio.Regimen, StringComparison.Ordinal);
        }

        Assert.StartsWith(EnvelopePrefix, trio.Gender, StringComparison.Ordinal);
        Assert.StartsWith(EnvelopePrefix, trio.Pronouns, StringComparison.Ordinal);
        foreach (string stored in new[] { trio.Regimen ?? string.Empty, trio.Gender, trio.Pronouns })
        {
            Assert.DoesNotContain("MaleToFemale", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("FemaleToMale", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Male", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Female", stored, StringComparison.Ordinal);
            foreach (string pronoun in PronounTokens)
            {
                Assert.DoesNotContain(pronoun, stored, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Verifies each regimen round-trips unchanged, is stored sealed, and a null regimen stays NULL.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAndLoad_RoundTripsAndStoresOnlyEnvelopes(bool postgres)
    {
        // WHAT: Saves one profile per regimen value plus one without a regimen, then reads back raw and decrypted.
        // WHY:  A round trip alone cannot show encryption; the raw SELECT proves what the database holds.
        // HOW:  Asserts the rows exist, the stored text is an envelope without any plaintext token, NULL stays NULL,
        //       and the repository returns the original values.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        ProfileDataProtector protector = CreateProtector();
        using DatabaseMigrationRunner runner = new(null, protector);
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        (string Name, TherapeuticRegimen? Regimen, GenderAssignedAtBirth Gender)[] cases =
        [
            ("rt-mtf", TherapeuticRegimen.MaleToFemale, GenderAssignedAtBirth.Male),
            ("rt-ftm", TherapeuticRegimen.FemaleToMale, GenderAssignedAtBirth.Female),
            ("rt-none", null, GenderAssignedAtBirth.Both)
        ];

        await using (WeaverDbSession seed = await factory.OpenAsync())
        {
            foreach ((string name, _, _) in cases)
            {
                await InsertRawProfileAsync(seed, name, new RawTrio(null, string.Empty, string.Empty));
            }
        }

        using UserProfileRepository repo = new(factory, protector);
        foreach ((string name, TherapeuticRegimen? regimen, GenderAssignedAtBirth gender) in cases)
        {
            await repo.UpsertProfileAsync(new UserProfile
            {
                Username = name,
                FirstName = "A",
                LastName = "B",
                Regimen = regimen,
                GenderAssignedAtBirth = gender,
                Pronouns = [.. PronounTokens]
            });
        }

        Dictionary<string, RawTrio> raw = await ReadRawAsync(factory);
        Assert.Equal(3, raw.Count);
        foreach ((string name, TherapeuticRegimen? regimen, GenderAssignedAtBirth gender) in cases)
        {
            AssertSealed(raw[name], expectRegimenNull: regimen is null);
            UserProfile? loaded = await repo.GetProfileAsync(name);
            Assert.NotNull(loaded);
            Assert.Equal(regimen, loaded.Regimen);
            Assert.Equal(gender, loaded.GenderAssignedAtBirth);
            Assert.Equal(PronounTokens, loaded.Pronouns);
        }
    }

    /// <summary>
    /// Verifies plaintext rows written by migration 003 are sealed by 006, the data step is idempotent, and the PostgreSQL DDL applies.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration006_SealsLegacyRows_IsIdempotent_AndAppliesPostgresDdl(bool postgres)
    {
        // WHAT: Seeds plaintext, partly-sealed, sparse and empty rows, applies 006, then reruns its data step twice.
        // WHY:  Existing patients must be protected, a rerun must not double-encrypt, and on PostgreSQL the
        //       columns must be TEXT with no DEFAULT.
        // HOW:  Compares raw snapshots before/after the reruns and queries information_schema on PostgreSQL.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        ProfileDataProtector protector = CreateProtector();
        using (DatabaseMigrationRunner legacyRunner = new(UpToMigration005(protector)))
        {
            await using WeaverDbSession seed = await backend.CreateFactory(legacyRunner).OpenAsync();
            await InsertRawProfileAsync(seed, "leg-full", new RawTrio("MaleToFemale", "Male", "[\"xe-Quillpron\",\"zir-Marchpron\"]"));
            await InsertRawProfileAsync(seed, "leg-sparse", new RawTrio(null, "Female", "[\"xe-Quillpron\"]"));
            await InsertRawProfileAsync(seed, "leg-mixed", new RawTrio(null, "Female", protector.EncryptRequiredField("[\"zir-Marchpron\"]")));
            await InsertRawProfileAsync(seed, "leg-empty", new RawTrio(string.Empty, string.Empty, string.Empty));
        }

        using DatabaseMigrationRunner runner = new(null, protector);
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        Dictionary<string, RawTrio> snapshot;
        await using (WeaverDbSession migrated = await factory.OpenAsync())
        {
            Assert.Equal(0, await runner.ApplyPendingMigrationsAsync(migrated.Connection, migrated.Dialect.Name));
            snapshot = await ReadRawAsync(factory);
            Assert.Equal(4, snapshot.Count);
            AssertSealed(snapshot["leg-full"], expectRegimenNull: false);
            AssertSealed(snapshot["leg-sparse"], expectRegimenNull: true);
            AssertSealed(snapshot["leg-mixed"], expectRegimenNull: true);
            Assert.Equal(new RawTrio(string.Empty, string.Empty, string.Empty), snapshot["leg-empty"]);

            if (postgres)
            {
                foreach (string column in new[] { "regimen", "gender_assigned_at_birth", "pronouns_json" })
                {
                    await using DbCommand type = migrated.CreateCommand(
                        "SELECT data_type, column_default FROM information_schema.columns WHERE table_name = 'weaver_user_profiles' AND column_name = @c;");
                    WeaverDbParameters.Add(type, "@c", column);
                    await using DbDataReader info = await type.ExecuteReaderAsync();
                    Assert.True(await info.ReadAsync());
                    Assert.Equal("text", info.GetString(0));
                    Assert.True(info.IsDBNull(1), $"{column} still has a DEFAULT.");
                }

                // With no DEFAULT left, an INSERT that omits the columns is refused instead of storing plaintext.
                await using DbCommand omit = migrated.CreateCommand(
                    "INSERT INTO weaver_user_profiles (username, first_name, last_name, created_at_utc, updated_at_utc) VALUES ('leg-full', 'x', 'y', now(), now());");
                await Assert.ThrowsAnyAsync<DbException>(() => omit.ExecuteNonQueryAsync());
            }

            Migration006_EncryptGenderIdentity again = new(protector);
            await again.UpAsync(migrated.Connection, null, migrated.Dialect.Name, CancellationToken.None);
            await again.UpAsync(migrated.Connection, null, migrated.Dialect.Name, CancellationToken.None);
        }

        Assert.Equal(snapshot, await ReadRawAsync(factory));

        using UserProfileRepository repo = new(factory, protector);
        UserProfile? full = await repo.GetProfileAsync("leg-full");
        Assert.NotNull(full);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, full.Regimen);
        Assert.Equal(GenderAssignedAtBirth.Male, full.GenderAssignedAtBirth);
        Assert.Equal(PronounTokens, full.Pronouns);
        UserProfile? mixed = await repo.GetProfileAsync("leg-mixed");
        Assert.NotNull(mixed);
        Assert.Null(mixed.Regimen);
        Assert.Equal(GenderAssignedAtBirth.Female, mixed.GenderAssignedAtBirth);
        Assert.Equal(["zir-Marchpron"], mixed.Pronouns);
    }

    /// <summary>
    /// Verifies the migration refuses to run, and changes nothing, when plaintext exists but no protector does.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration006_WithoutProtector_FailsLoudlyAndIsNotRecorded(bool postgres)
    {
        // WHAT: Applies 006 with no protector over a plaintext row.
        // WHY:  A migration that "succeeds" having encrypted nothing would never run again.
        // HOW:  Expects WeaverDatabaseUnavailableException wrapping the cause, then checks plaintext untouched and version 6 unrecorded.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        ProfileDataProtector protector = CreateProtector();
        using (DatabaseMigrationRunner legacyRunner = new(UpToMigration005(protector)))
        {
            await using WeaverDbSession seed = await backend.CreateFactory(legacyRunner).OpenAsync();
            await InsertRawProfileAsync(seed, "nokey", new RawTrio("FemaleToMale", "Female", "[\"xe-Quillpron\"]"));
        }

        IDatabaseMigration[] withoutKey = [.. UpToMigration005(protector), new Migration006_EncryptGenderIdentity(null)];
        using (DatabaseMigrationRunner noKeyRunner = new(withoutKey))
        {
            WeaverDatabaseUnavailableException missing = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(
                () => backend.CreateFactory(noKeyRunner).OpenAsync());
            InvalidOperationException cause = Assert.IsType<InvalidOperationException>(missing.InnerException);
            Assert.Contains("no PHI encryption key", cause.Message, StringComparison.Ordinal);
        }

        using DatabaseMigrationRunner inspectRunner = new(UpToMigration005(protector));
        WeaverDbConnectionFactory inspect = backend.CreateFactory(inspectRunner);
        Dictionary<string, RawTrio> stored = await ReadRawAsync(inspect);
        Assert.Equal(new RawTrio("FemaleToMale", "Female", "[\"xe-Quillpron\"]"), Assert.Single(stored).Value);

        await using WeaverDbSession session = await inspect.OpenAsync();
        await using DbCommand versions = session.CreateCommand("SELECT COUNT(*) FROM weaver_schema_migrations WHERE version = 6;");
        Assert.Equal(0L, Convert.ToInt64(await versions.ExecuteScalarAsync()));
    }

    /// <summary>
    /// Verifies a legacy plaintext value left in the table is refused on read, never rendered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PlaintextGenderIdentity_IsRefusedOnRead()
    {
        // WHAT: Plants a plaintext gender in a fully migrated database and reads the profile.
        // WHY:  Decryption is strict, which is why the column DEFAULT had to go; this documents the consequence.
        // HOW:  Expects CryptographicException from the repository.
        Backend? backend = await CreateBackendAsync(false);
        Assert.NotNull(backend);
        ProfileDataProtector protector = CreateProtector();
        using DatabaseMigrationRunner runner = new(null, protector);
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        await using (WeaverDbSession seed = await factory.OpenAsync())
        {
            await InsertRawProfileAsync(seed, "planted", new RawTrio(null, "Female", protector.EncryptRequiredField("[]")));
        }

        using UserProfileRepository repo = new(factory, protector);
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => repo.GetProfileAsync("planted"));
    }

    /// <summary>
    /// Verifies the migration's identity and the DDL it emits for each dialect.
    /// </summary>
    [Fact]
    public void Migration006_Identity_AndDialectSql()
    {
        // WHAT: Checks version, name, and the generated SQL.
        // WHY:  PostgreSQL must widen exactly three columns and drop exactly two defaults; SQLite needs no DDL.
        // HOW:  Compares the statements against the expected list.
        Migration006_EncryptGenderIdentity migration = new();
        Assert.Equal(6, migration.Version);
        Assert.Equal("006_EncryptGenderIdentity", migration.Name);
        Assert.Equal(string.Empty, migration.GetSql("sqlite"));
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string[] statements = migration.GetSql("postgresql").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(
            [
                "ALTER TABLE weaver_user_profiles ALTER COLUMN regimen TYPE TEXT",
                "ALTER TABLE weaver_user_profiles ALTER COLUMN gender_assigned_at_birth TYPE TEXT",
                "ALTER TABLE weaver_user_profiles ALTER COLUMN pronouns_json TYPE TEXT",
                "ALTER TABLE weaver_user_profiles ALTER COLUMN gender_assigned_at_birth DROP DEFAULT",
                "ALTER TABLE weaver_user_profiles ALTER COLUMN pronouns_json DROP DEFAULT"
            ],
            statements);
    }
}
