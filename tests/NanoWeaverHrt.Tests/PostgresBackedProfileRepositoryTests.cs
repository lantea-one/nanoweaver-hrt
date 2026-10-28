using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Profile, address and contact behaviour against a REAL PostgreSQL server, supplied through
/// WEAVER_TEST_POSTGRES. Every test returns early when no server was supplied.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class PostgresBackedProfileRepositoryTests : IDisposable
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
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_pgtests_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresBackedProfileRepositoryTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">Test output sink.</param>
    public PostgresBackedProfileRepositoryTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
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
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Builds a repository wired to a fresh PostgreSQL database, with a seeded user row.
    /// </summary>
    /// <param name="username">The user the profile rows will belong to.</param>
    /// <returns>The repository, with the factory used to build it.</returns>
    private async Task<UserProfileRepository> CreateRepositoryAsync(string username)
    {
        string connectionString = await _postgres.CreateIsolatedDatabaseAsync();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = connectionString,
                ["WEAVER_NODE_KEYS_DIR"] = Path.Combine(_tempDir, "node-keys")
            })
            .Build();

        WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(configuration);
        await using (WeaverDbSession session = await factory.OpenAsync())
        {
            await using DbCommand insert = session.CreateCommand(
                "INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, created_at_utc) VALUES (@u, 'Patient', 'ApiWeb', 'Ed25519', 'k', @t);");
            WeaverDbParameters.Add(insert, "@u", username);
            WeaverDbParameters.Add(insert, "@t", session.Dialect.TimestampValue(DateTimeOffset.UtcNow));
            await insert.ExecuteNonQueryAsync();
        }

        return new UserProfileRepository(factory, new ProfileDataProtector(new NodeSecretStore(configuration)));
    }

    /// <summary>
    /// Verifies profile, address and contact round trips on real PostgreSQL.
    /// </summary>
    [Fact]
    public async Task PostgreSql_ProfileAddressAndContact_RoundTrip()
    {
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state, and this project's xUnit pin
            //       offers no runtime-skip API we have verified. The gate prints the WEAVER_TEST_POSTGRES
            //       mode so a vacuous pass is visible rather than silent, and a variable that IS set but
            //       unreachable fails loudly instead of quietly skipping.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        const string username = "pg-patient";
        using UserProfileRepository repo = await CreateRepositoryAsync(username);

        Assert.Null(await repo.GetProfileAsync(username));
        UserProfile profile = new()
        {
            Username = username,
            FirstName = "Alice",
            LastName = "Morgan",
            EmailAddress = "alice@example.org",
            Picture = "data:image/png;base64,iVBORw0KGgo=",
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
            Pronouns = ["she", "her"]
        };
        await repo.UpsertProfileAsync(profile);
        await repo.UpsertProfileAsync(profile with { PreferredName = "Al" });
        UserProfile? read = await repo.GetProfileAsync(username);
        Assert.NotNull(read);
        Assert.Equal("Al", read.PreferredName);
        Assert.Equal(profile.Picture, read.Picture);
        Assert.Equal(["she", "her"], read.Pronouns);

        PatientAddress address = new()
        {
            Id = "a1",
            Username = username,
            AddressType = "Home",
            AddressLine1 = "1 Road",
            City = "Portland",
            StateOrProvince = "OR",
            PostalCode = "97201",
            Country = "US",
            IsPrimary = true
        };
        await repo.UpsertAddressAsync(address);
        await repo.UpsertAddressAsync(address with { City = "Salem" });
        IReadOnlyList<PatientAddress> addresses = await repo.GetAddressesAsync(username);
        Assert.Single(addresses);
        Assert.Equal("Salem", addresses[0].City);
        Assert.True(addresses[0].IsPrimary);
        Assert.True(await repo.DeleteAddressAsync("a1", username));
        Assert.False(await repo.DeleteAddressAsync("a1", username));

        PatientContact contact = new()
        {
            Id = "c1",
            Username = username,
            ContactName = "Bob",
            Relationship = "Partner",
            PhoneNumber = "+1-555-0100",
            PriorityOrder = 1,
            CanDiscussMedicalInfo = true
        };
        await repo.UpsertContactAsync(contact);
        await repo.UpsertContactAsync(contact with { ContactName = "Robert" });
        IReadOnlyList<PatientContact> contacts = await repo.GetContactsAsync(username);
        Assert.Single(contacts);
        Assert.Equal("Robert", contacts[0].ContactName);
        Assert.True(contacts[0].CanDiscussMedicalInfo);
        Assert.True(await repo.DeleteContactAsync("c1", username));
        Assert.False(await repo.DeleteContactAsync("c1", username));
    }

    /// <summary>
    /// Verifies migrations actually record their version rows on real PostgreSQL.
    /// </summary>
    [Fact]
    public async Task PostgreSql_Migrations_RecordEveryAppliedVersion()
    {
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state, and this project's xUnit pin
            //       offers no runtime-skip API we have verified. The gate prints the WEAVER_TEST_POSTGRES
            //       mode so a vacuous pass is visible rather than silent, and a variable that IS set but
            //       unreachable fails loudly instead of quietly skipping.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        // WHAT: Opens a session on a brand-new PostgreSQL database and reads weaver_schema_migrations back.
        // WHY:  The migration runner once bound its applied-at timestamp as text to a TIMESTAMPTZ column, so
        //       PostgreSQL rejected every migration; a swallowing catch upstream hid the failure, which means
        //       "no exception escaped" proves nothing. Only the recorded rows prove the migrations applied.
        // HOW:  Asserts the table holds versions 1 to 5, each with a real, recent timestamp.
        string connectionString = await _postgres.CreateIsolatedDatabaseAsync();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = connectionString
            })
            .Build();
        WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(configuration);

        await using WeaverDbSession session = await factory.OpenAsync();
        await using DbCommand read = session.CreateCommand("SELECT version, applied_at_utc FROM weaver_schema_migrations ORDER BY version;");
        await using DbDataReader reader = await read.ExecuteReaderAsync();
        List<long> versions = [];
        while (await reader.ReadAsync())
        {
            versions.Add(Convert.ToInt64(reader.GetValue(0)));
            Assert.True(WeaverDbValues.ReadTimestamp(reader, 1) > DateTimeOffset.UtcNow.AddHours(-1));
        }

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L], versions);
    }

    /// <summary>
    /// Verifies a value too wide for a PostgreSQL column surfaces as SQLSTATE 22001 and never reaches SQLite.
    /// The postal code used to be the probe, but it is now encrypted into a TEXT column and fits at any length;
    /// <c>country</c> stays a plaintext VARCHAR(64) and keeps the silent-fallback regression guarded.
    /// </summary>
    [Fact]
    public async Task PostgreSql_OversizedPlaintextColumn_RaisesTruncationAndWritesNothingToSqlite()
    {
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state, and this project's xUnit pin
            //       offers no runtime-skip API we have verified. The gate prints the WEAVER_TEST_POSTGRES
            //       mode so a vacuous pass is visible rather than silent, and a variable that IS set but
            //       unreachable fails loudly instead of quietly skipping.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        const string username = "pg-trap";
        using UserProfileRepository repo = await CreateRepositoryAsync(username);

        PatientAddress address = new()
        {
            Id = "trap",
            Username = username,
            AddressType = "Home",
            AddressLine1 = "1 Road",
            City = "Portland",
            StateOrProvince = "OR",
            PostalCode = "97201",
            Country = new string('U', 65)
        };

        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => repo.UpsertAddressAsync(address));
        Assert.Equal("22001", ex.SqlState);
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }
}
