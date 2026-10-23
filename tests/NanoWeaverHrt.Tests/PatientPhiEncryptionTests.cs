using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
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
/// Proves that patient identifiers are encrypted at rest in the database file itself, on both backends.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why these tests read raw columns:</strong> a test that writes through the repository and reads back
/// through the repository passes even if nothing is encrypted, because the round trip hides the stored form.
/// Every test here therefore also issues its own <c>SELECT</c> against the table and inspects the exact bytes
/// the database holds.
/// </para>
/// <para>
/// Each backend scenario runs against SQLite always, and against a real PostgreSQL server when
/// <c>WEAVER_TEST_POSTGRES</c> is set (the same opt-in convention as the other PostgreSQL suites).
/// </para>
/// </remarks>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class PatientPhiEncryptionTests : IDisposable
{
    /// <summary>
    /// The envelope prefix every encrypted value must carry.
    /// </summary>
    private const string EnvelopePrefix = "data:application/encrypted;base64,";

    /// <summary>
    /// Distinctive plaintext tokens used across the scenarios. None may ever appear in a raw column.
    /// </summary>
    private static readonly string[] PlaintextTokens =
    [
        "Zephyrine", "Quillfeather", "Bramblewick", "zephyrine@example.invalid", "+1-555-0142-7788",
        "1987-06-05", "allergic to Quillium", "bio-Marchwarden", "221B Marchwarden Lane", "Flat Nine-Tarn",
        "Wexcombe", "Northumbria", "NE1 4XQ", "Oberon Vale", "Cousin-Thessaly", "+44-7700-900555",
        "oberon@example.invalid", "call-after-nine-notes"
    ];

    /// <summary>
    /// Every PHI column this migration encrypts, as (table, column).
    /// </summary>
    private static readonly (string Table, string Column)[] ProtectedColumns =
    [
        ("weaver_user_profiles", "first_name"), ("weaver_user_profiles", "last_name"), ("weaver_user_profiles", "preferred_name"),
        ("weaver_user_profiles", "email_address"), ("weaver_user_profiles", "phone_number"), ("weaver_user_profiles", "date_of_birth"),
        ("weaver_user_profiles", "emergency_medical_notes"), ("weaver_user_profiles", "bio"),
        ("weaver_patient_addresses", "address_line1"), ("weaver_patient_addresses", "address_line2"), ("weaver_patient_addresses", "city"),
        ("weaver_patient_addresses", "state_or_province"), ("weaver_patient_addresses", "postal_code"),
        ("weaver_patient_contacts", "contact_name"), ("weaver_patient_contacts", "relationship"), ("weaver_patient_contacts", "phone_number"),
        ("weaver_patient_contacts", "email_address"), ("weaver_patient_contacts", "notes")
    ];

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
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_phi_tests_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientPhiEncryptionTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">Test output sink.</param>
    public PatientPhiEncryptionTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
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
    /// A stand-in key store whose PHI key is empty, modelling a missing pepper.
    /// </summary>
    private sealed class EmptyKeyStore : INodeSecretStore
    {
        /// <inheritdoc/>
        public ReadOnlyMemory<byte> TokenSigningKey => ReadOnlyMemory<byte>.Empty;

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> PhiMasterKey => ReadOnlyMemory<byte>.Empty;

        /// <inheritdoc/>
        public string NodeKeysDirectory => string.Empty;

        /// <inheritdoc/>
        public NodeSecretOrigin TokenSigningKeyOrigin => NodeSecretOrigin.GeneratedFile;

        /// <inheritdoc/>
        public NodeSecretOrigin PhiMasterKeyOrigin => NodeSecretOrigin.GeneratedFile;
    }

    /// <summary>
    /// One database backend prepared for a scenario.
    /// </summary>
    /// <param name="Configuration">Configuration pointing at this backend's isolated database.</param>
    /// <param name="ConfigRepository">Configuration repository that selects this backend.</param>
    private sealed record Backend(IConfiguration Configuration, IConfigurationRepository ConfigRepository)
    {
        /// <summary>
        /// Builds a connection factory for this backend over the supplied migration runner.
        /// </summary>
        /// <param name="runner">Runner that decides which migrations apply.</param>
        /// <returns>A factory bound to this backend.</returns>
        public WeaverDbConnectionFactory CreateFactory(DatabaseMigrationRunner runner) => new(Configuration, ConfigRepository, runner);
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
            return new Backend(sqliteConfiguration, new SystemConfigurationStore(sqliteConfiguration));
        }

        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the PostgreSQL half unexecuted when no server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state; the gate prints the mode.
            // HOW:  Reports through ITestOutputHelper and lets the caller return.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return null;
        }

        settings["POSTGRES_CONNECTION_STRING"] = await _postgres.CreateIsolatedDatabaseAsync();
        IConfiguration pgConfiguration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new Backend(pgConfiguration, new MockPostgresConfigRepo());
    }

    /// <summary>
    /// Creates a protector over a key directory unique to the given name.
    /// </summary>
    /// <param name="keySetName">Name of the key set; the same name always yields the same key.</param>
    /// <returns>A protector holding that key set's PHI master key.</returns>
    private ProfileDataProtector CreateProtector(string keySetName)
    {
        IConfiguration keyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = Path.Combine(_tempDir, "keys-" + keySetName)
            })
            .Build();
        return new ProfileDataProtector(new NodeSecretStore(keyConfiguration));
    }

    /// <summary>
    /// Builds the migrations 1 to 4 list: the schema as it was before PHI was encrypted.
    /// </summary>
    /// <returns>Migrations that leave every identifier column readable.</returns>
    private static IDatabaseMigration[] PreEncryptionMigrations() =>
    [
        new Migration001_InitialSchema(),
        new Migration002_UserAuditAndIndexes(),
        new Migration003_UserProfileAddressesAndContacts(),
        new Migration004_PatientDnaImportsAndResults()
    ];

    /// <summary>
    /// Inserts a patient account row so that profile, address and contact rows have a parent.
    /// </summary>
    /// <param name="session">Open database session.</param>
    /// <param name="username">Account name.</param>
    /// <returns>A task representing the insert.</returns>
    private static async Task InsertUserAsync(WeaverDbSession session, string username)
    {
        await using DbCommand insert = session.CreateCommand(
            "INSERT INTO weaver_users (username, role, access_gate, key_algorithm, public_key, created_at_utc) VALUES (@u, 'Patient', 'ApiWeb', 'Ed25519', 'k', @t);");
        WeaverDbParameters.Add(insert, "@u", username);
        WeaverDbParameters.Add(insert, "@t", session.Dialect.TimestampValue(DateTimeOffset.UtcNow));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts one profile, address and contact row directly, bypassing the repository and its encryption.
    /// </summary>
    /// <param name="session">Open database session.</param>
    /// <param name="username">Owning account.</param>
    /// <param name="profile">Values for first_name, last_name, preferred_name, email, phone, dob, notes, bio.</param>
    /// <param name="address">Values for line1, line2, city, state, postal code.</param>
    /// <param name="contact">Values for name, relationship, phone, email, notes.</param>
    /// <returns>A task representing the inserts.</returns>
    private static async Task InsertRawPhiRowsAsync(WeaverDbSession session, string username, string?[] profile, string?[] address, string?[] contact)
    {
        await InsertUserAsync(session, username);
        object now = session.Dialect.TimestampValue(DateTimeOffset.UtcNow);

        await using (DbCommand cmd = session.CreateCommand(@"
            INSERT INTO weaver_user_profiles (username, first_name, last_name, preferred_name, email_address, phone_number,
                date_of_birth, emergency_medical_notes, bio, gender_assigned_at_birth, pronouns_json, language_preference,
                created_at_utc, updated_at_utc)
            VALUES (@u, @a, @b, @c, @d, @e, @f, @g, @h, 'Female', '[]', 'en-US', @t, @t);"))
        {
            WeaverDbParameters.Add(cmd, "@u", username);
            AddRawValues(cmd, profile);
            WeaverDbParameters.Add(cmd, "@t", now);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (DbCommand cmd = session.CreateCommand(@"
            INSERT INTO weaver_patient_addresses (id, username, address_type, address_line1, address_line2, city, state_or_province,
                postal_code, country, is_primary, created_at_utc, updated_at_utc)
            VALUES (@id, @u, 'Home', @a, @b, @c, @d, @e, 'United Kingdom', @p, @t, @t);"))
        {
            WeaverDbParameters.Add(cmd, "@id", "addr-" + username);
            WeaverDbParameters.Add(cmd, "@u", username);
            AddRawValues(cmd, address);
            WeaverDbParameters.Add(cmd, "@p", session.Dialect.BooleanValue(true));
            WeaverDbParameters.Add(cmd, "@t", now);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (DbCommand cmd = session.CreateCommand(@"
            INSERT INTO weaver_patient_contacts (id, username, contact_name, relationship, phone_number, email_address,
                priority_order, can_discuss_medical_info, notes, created_at_utc, updated_at_utc)
            VALUES (@id, @u, @a, @b, @c, @d, 1, @p, @e, @t, @t);"))
        {
            WeaverDbParameters.Add(cmd, "@id", "cont-" + username);
            WeaverDbParameters.Add(cmd, "@u", username);
            AddRawValues(cmd, contact);
            WeaverDbParameters.Add(cmd, "@p", session.Dialect.BooleanValue(false));
            WeaverDbParameters.Add(cmd, "@t", now);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Binds up to eight values to the parameters <c>@a</c> to <c>@h</c>, in order.
    /// </summary>
    /// <param name="cmd">Command receiving the parameters.</param>
    /// <param name="values">Values to bind; <c>null</c> becomes SQL NULL.</param>
    private static void AddRawValues(DbCommand cmd, string?[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            WeaverDbParameters.AddNullable(cmd, "@" + (char)('a' + i), values[i]);
        }
    }

    /// <summary>
    /// Reads every protected column of every row exactly as the database stores it.
    /// </summary>
    /// <param name="factory">Factory for the backend.</param>
    /// <returns>One entry per non-NULL stored value, as (table.column, raw value).</returns>
    private static async Task<List<(string Where, string Raw)>> ReadAllRawAsync(IWeaverDbConnectionFactory factory)
    {
        List<(string Where, string Raw)> values = [];
        await using WeaverDbSession session = await factory.OpenAsync();
        foreach ((string table, string column) in ProtectedColumns)
        {
            await using DbCommand cmd = session.CreateCommand($"SELECT {column} FROM {table};");
            await using DbDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    values.Add(($"{table}.{column}", reader.GetString(0)));
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Asserts that no stored value contains any known plaintext token, and that every non-empty one is an envelope.
    /// </summary>
    /// <param name="stored">Raw values as read from the database.</param>
    private static void AssertNoPlaintext(List<(string Where, string Raw)> stored)
    {
        Assert.NotEmpty(stored);
        foreach ((string where, string raw) in stored)
        {
            foreach (string token in PlaintextTokens)
            {
                Assert.False(raw.Contains(token, StringComparison.OrdinalIgnoreCase), $"{where} holds plaintext '{token}'.");
            }

            Assert.True(raw.Length == 0 || raw.StartsWith(EnvelopePrefix, StringComparison.Ordinal), $"{where} is not an encrypted envelope.");
        }
    }

    /// <summary>
    /// Builds a profile carrying distinctive plaintext in every protected column.
    /// </summary>
    /// <param name="username">Owning account.</param>
    /// <returns>A fully populated profile.</returns>
    private static UserProfile FullProfile(string username) => new()
    {
        Username = username,
        FirstName = "Zephyrine",
        LastName = "Quillfeather",
        PreferredName = "Bramblewick",
        EmailAddress = "zephyrine@example.invalid",
        PhoneNumber = "+1-555-0142-7788",
        DateOfBirth = "1987-06-05",
        EmergencyMedicalNotes = "allergic to Quillium",
        Bio = "bio-Marchwarden",
        Picture = "data:image/png;base64,iVBORw0KGgo=",
        Regimen = TherapeuticRegimen.MaleToFemale,
        GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
        Pronouns = ["she", "her"]
    };

    /// <summary>
    /// Builds an address carrying distinctive plaintext in every protected column.
    /// </summary>
    /// <param name="username">Owning account.</param>
    /// <returns>A fully populated address.</returns>
    private static PatientAddress FullAddress(string username) => new()
    {
        Id = "addr-1",
        Username = username,
        AddressType = "Home",
        AddressLine1 = "221B Marchwarden Lane",
        AddressLine2 = "Flat Nine-Tarn",
        City = "Wexcombe",
        StateOrProvince = "Northumbria",
        PostalCode = "NE1 4XQ",
        Country = "United Kingdom",
        IsPrimary = true
    };

    /// <summary>
    /// Builds a contact carrying distinctive plaintext in every protected column.
    /// </summary>
    /// <param name="username">Owning account.</param>
    /// <returns>A fully populated contact.</returns>
    private static PatientContact FullContact(string username) => new()
    {
        Id = "cont-1",
        Username = username,
        ContactName = "Oberon Vale",
        Relationship = "Cousin-Thessaly",
        PhoneNumber = "+44-7700-900555",
        EmailAddress = "oberon@example.invalid",
        PriorityOrder = 1,
        CanDiscussMedicalInfo = true,
        Notes = "call-after-nine-notes"
    };

    /// <summary>
    /// Writes through the repository, then proves the stored columns hold ciphertext while the API still round-trips.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawColumns_HoldCiphertextNotPlaintext_AndRoundTripThroughRepository(bool postgres)
    {
        // WHAT: Submits distinctive plaintext through UserProfileRepository, then SELECTs each column directly.
        // WHY:  Only the raw stored value shows whether encryption happened; a repository round-trip cannot.
        // HOW:  Asserts every protected column is an AES-GCM envelope containing none of the submitted text,
        //       asserts the deliberately plaintext columns are still plain, then reads back through the API.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        using DatabaseMigrationRunner runner = new(null, CreateProtector("node"));
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        await using (WeaverDbSession seed = await factory.OpenAsync())
        {
            await InsertUserAsync(seed, "patient-raw");
        }

        using UserProfileRepository repo = new(factory, CreateProtector("node"));
        await repo.UpsertProfileAsync(FullProfile("patient-raw"));
        await repo.UpsertAddressAsync(FullAddress("patient-raw"));
        await repo.UpsertContactAsync(FullContact("patient-raw"));

        List<(string Where, string Raw)> stored = await ReadAllRawAsync(factory);
        Assert.Equal(ProtectedColumns.Length, stored.Count);
        AssertNoPlaintext(stored);

        await using (WeaverDbSession session = await factory.OpenAsync())
        {
            await using DbCommand pic = session.CreateCommand("SELECT picture FROM weaver_user_profiles;");
            Assert.StartsWith(EnvelopePrefix, (string)(await pic.ExecuteScalarAsync())!, StringComparison.Ordinal);

            await using DbCommand country = session.CreateCommand("SELECT country FROM weaver_patient_addresses;");
            Assert.Equal("United Kingdom", await country.ExecuteScalarAsync());
        }

        UserProfile? profile = await repo.GetProfileAsync("patient-raw");
        Assert.NotNull(profile);
        Assert.Equal("Zephyrine", profile.FirstName);
        Assert.Equal("Quillfeather", profile.LastName);
        Assert.Equal("Bramblewick", profile.PreferredName);
        Assert.Equal("zephyrine@example.invalid", profile.EmailAddress);
        Assert.Equal("+1-555-0142-7788", profile.PhoneNumber);
        Assert.Equal("1987-06-05", profile.DateOfBirth);
        Assert.Equal("allergic to Quillium", profile.EmergencyMedicalNotes);
        Assert.Equal("bio-Marchwarden", profile.Bio);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", profile.Picture);

        PatientAddress address = Assert.Single(await repo.GetAddressesAsync("patient-raw"));
        Assert.Equal("221B Marchwarden Lane", address.AddressLine1);
        Assert.Equal("Flat Nine-Tarn", address.AddressLine2);
        Assert.Equal("Wexcombe", address.City);
        Assert.Equal("Northumbria", address.StateOrProvince);
        Assert.Equal("NE1 4XQ", address.PostalCode);

        PatientContact contact = Assert.Single(await repo.GetContactsAsync("patient-raw"));
        Assert.Equal("Oberon Vale", contact.ContactName);
        Assert.Equal("Cousin-Thessaly", contact.Relationship);
        Assert.Equal("+44-7700-900555", contact.PhoneNumber);
        Assert.Equal("oberon@example.invalid", contact.EmailAddress);
        Assert.Equal("call-after-nine-notes", contact.Notes);

        // An update must re-seal with a fresh nonce rather than leave plaintext behind.
        await repo.UpsertProfileAsync(profile with { Bio = "bio-Marchwarden" });
        AssertNoPlaintext(await ReadAllRawAsync(factory));
    }

    /// <summary>
    /// Verifies optional columns stay NULL, empty values stay empty, and long free text survives encryption.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalAndEmptyFields_StayNullOrEmpty_AndOversizeValuesFit(bool postgres)
    {
        // WHAT: Stores NULL optionals, empty strings, and values far longer than the old VARCHAR limits.
        // WHY:  Encrypting NULL would turn absence into data; and ciphertext of a 64-character postal code
        //       would have overflowed VARCHAR(32) on PostgreSQL before the columns were widened.
        // HOW:  Round-trips minimal and oversize rows and inspects the raw NULL/empty/length facts.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        using DatabaseMigrationRunner runner = new(null, CreateProtector("node"));
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        await using (WeaverDbSession seed = await factory.OpenAsync())
        {
            await InsertUserAsync(seed, "patient-min");
        }

        using UserProfileRepository repo = new(factory, CreateProtector("node"));
        await repo.UpsertProfileAsync(new UserProfile { Username = "patient-min", FirstName = string.Empty, LastName = string.Empty });
        string longPostal = new('9', 64);
        await repo.UpsertAddressAsync(FullAddress("patient-min") with { AddressLine2 = null, PostalCode = longPostal });
        await repo.UpsertContactAsync(FullContact("patient-min") with { EmailAddress = null, Notes = new string('n', 4000) });

        UserProfile? profile = await repo.GetProfileAsync("patient-min");
        Assert.NotNull(profile);
        Assert.Equal(string.Empty, profile.FirstName);
        Assert.Null(profile.PreferredName);
        Assert.Null(profile.Bio);

        PatientAddress address = Assert.Single(await repo.GetAddressesAsync("patient-min"));
        Assert.Null(address.AddressLine2);
        Assert.Equal(longPostal, address.PostalCode);
        PatientContact contact = Assert.Single(await repo.GetContactsAsync("patient-min"));
        Assert.Null(contact.EmailAddress);
        Assert.Equal(new string('n', 4000), contact.Notes);

        await using WeaverDbSession session = await factory.OpenAsync();
        await using DbCommand raw = session.CreateCommand("SELECT first_name, preferred_name FROM weaver_user_profiles;");
        await using DbDataReader reader = await raw.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(string.Empty, reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
    }

    /// <summary>
    /// Verifies a database holding migration-003-era plaintext is upgraded, and that running again changes nothing.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaintextRowsFromBeforeTheMigration_AreEncryptedInPlace_AndRerunningIsIdempotent(bool postgres)
    {
        // WHAT: Builds a pre-005 database full of readable PHI, applies migration 005, then applies it twice more.
        // WHY:  This is the real upgrade path for every existing deployment, and the migration must neither
        //       skip rows nor encrypt an envelope a second time (which would make it unreadable).
        // HOW:  Seeds plaintext, NULL, empty and already-sealed values (sealed only where the old PostgreSQL column
        //       was wide enough to hold an envelope); migrates; reads the raw columns;
        //       snapshots them; re-runs the migration body directly; and requires a byte-identical snapshot.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        ProfileDataProtector protector = CreateProtector("node");
        using (DatabaseMigrationRunner legacyRunner = new(PreEncryptionMigrations()))
        {
            WeaverDbConnectionFactory legacyFactory = backend.CreateFactory(legacyRunner);
            await using WeaverDbSession seed = await legacyFactory.OpenAsync();

            await InsertRawPhiRowsAsync(
                seed,
                "stage-full",
                ["Zephyrine", "Quillfeather", "Bramblewick", "zephyrine@example.invalid", "+1-555-0142-7788", "1987-06-05", "allergic to Quillium", "bio-Marchwarden"],
                ["221B Marchwarden Lane", "Flat Nine-Tarn", "Wexcombe", "Northumbria", "NE1 4XQ"],
                ["Oberon Vale", "Cousin-Thessaly", "+44-7700-900555", "oberon@example.invalid", "call-after-nine-notes"]);
            await InsertRawPhiRowsAsync(
                seed,
                "stage-sparse",
                ["Zephyrine", string.Empty, null, null, null, null, null, null],
                ["221B Marchwarden Lane", null, "Wexcombe", "Northumbria", "NE1 4XQ"],
                ["Oberon Vale", "Cousin-Thessaly", "+44-7700-900555", null, string.Empty]);
            await InsertRawPhiRowsAsync(
                seed,
                "stage-mixed",
                [protector.EncryptRequiredField("Sealed"), "Quillfeather", null, null, null, null, null, null],
                [protector.EncryptRequiredField("Sealed"), null, protector.EncryptRequiredField("Sealed"), protector.EncryptRequiredField("Sealed"), "NE1 4XQ"],
                [protector.EncryptRequiredField("Sealed"), "Cousin-Thessaly", "+44-7700-900555", null, null]);
            await InsertRawPhiRowsAsync(
                seed,
                "stage-done",
                [string.Empty, string.Empty, null, null, null, null, null, null],
                [string.Empty, null, string.Empty, string.Empty, string.Empty],
                [string.Empty, string.Empty, string.Empty, null, null]);
        }

        using DatabaseMigrationRunner runner = new(null, protector);
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        List<(string Where, string Raw)> snapshot;
        await using (WeaverDbSession migrated = await factory.OpenAsync())
        {
            Assert.Equal(0, await runner.ApplyPendingMigrationsAsync(migrated.Connection, migrated.Dialect.Name));
            snapshot = await ReadAllRawAsync(factory);
            AssertNoPlaintext(snapshot);

            if (postgres)
            {
                foreach ((string table, string column) in ProtectedColumns)
                {
                    await using DbCommand type = migrated.CreateCommand(
                        "SELECT data_type FROM information_schema.columns WHERE table_name = @t AND column_name = @c;");
                    WeaverDbParameters.Add(type, "@t", table);
                    WeaverDbParameters.Add(type, "@c", column);
                    Assert.Equal("text", await type.ExecuteScalarAsync());
                }
            }

            // Running the data step again, directly and repeatedly, must not touch an envelope.
            Migration005_EncryptPatientPhi again = new(protector);
            await again.UpAsync(migrated.Connection, null, migrated.Dialect.Name, CancellationToken.None);
            await again.UpAsync(migrated.Connection, null, migrated.Dialect.Name, CancellationToken.None);
        }

        List<(string Where, string Raw)> afterRerun = await ReadAllRawAsync(factory);
        Assert.Equal(snapshot, afterRerun);

        using UserProfileRepository repo = new(factory, protector);
        UserProfile? full = await repo.GetProfileAsync("stage-full");
        Assert.NotNull(full);
        Assert.Equal("Zephyrine", full.FirstName);
        Assert.Equal("+1-555-0142-7788", full.PhoneNumber);
        Assert.Equal("bio-Marchwarden", full.Bio);
        UserProfile? mixed = await repo.GetProfileAsync("stage-mixed");
        Assert.NotNull(mixed);
        Assert.Equal("Sealed", mixed.FirstName);
        Assert.Equal("Quillfeather", mixed.LastName);
        Assert.Equal("221B Marchwarden Lane", Assert.Single(await repo.GetAddressesAsync("stage-full")).AddressLine1);
        Assert.Equal("Sealed", Assert.Single(await repo.GetAddressesAsync("stage-mixed")).City);
        Assert.Equal("call-after-nine-notes", Assert.Single(await repo.GetContactsAsync("stage-full")).Notes);
        Assert.Null(Assert.Single(await repo.GetContactsAsync("stage-sparse")).EmailAddress);
    }

    /// <summary>
    /// Verifies the migration refuses to run, and changes nothing, when plaintext exists but no key does.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnusableKey_FailsLoudlyAndRollsBack_WithoutRecordingTheMigration(bool postgres)
    {
        // WHAT: Applies migration 005 with no protector, then with a protector whose key is empty.
        // WHY:  A migration that "succeeds" having encrypted nothing would be recorded as applied and never
        //       run again, leaving every patient in plaintext while the operator believes otherwise.
        // HOW:  Expects WeaverDatabaseUnavailableException wrapping the real cause, then checks the plaintext is
        //       untouched, the column width unchanged, and version 5 unrecorded.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        using (DatabaseMigrationRunner legacyRunner = new(PreEncryptionMigrations()))
        {
            await using WeaverDbSession seed = await backend.CreateFactory(legacyRunner).OpenAsync();
            await InsertRawPhiRowsAsync(
                seed,
                "stage-nokey",
                ["Zephyrine", "Quillfeather", null, null, "+1-555-0142-7788", null, null, null],
                ["221B Marchwarden Lane", null, "Wexcombe", "Northumbria", "NE1 4XQ"],
                ["Oberon Vale", "Cousin-Thessaly", "+44-7700-900555", null, null]);
        }

        IDatabaseMigration[] withoutKey = [.. PreEncryptionMigrations(), new Migration005_EncryptPatientPhi(null)];
        using (DatabaseMigrationRunner noKeyRunner = new(withoutKey))
        {
            WeaverDatabaseUnavailableException missing = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(
                () => backend.CreateFactory(noKeyRunner).OpenAsync());
            InvalidOperationException cause = Assert.IsType<InvalidOperationException>(missing.InnerException);
            Assert.Contains("no PHI encryption key", cause.Message, StringComparison.Ordinal);
        }

        ProfileDataProtector emptyKey = new(new EmptyKeyStore());
        IDatabaseMigration[] withEmptyKey = [.. PreEncryptionMigrations(), new Migration005_EncryptPatientPhi(emptyKey)];
        using (DatabaseMigrationRunner emptyKeyRunner = new(withEmptyKey))
        {
            WeaverDatabaseUnavailableException unusable = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(
                () => backend.CreateFactory(emptyKeyRunner).OpenAsync());
            Assert.NotNull(unusable.InnerException);
        }

        using DatabaseMigrationRunner inspectRunner = new(PreEncryptionMigrations());
        WeaverDbConnectionFactory inspect = backend.CreateFactory(inspectRunner);
        List<(string Where, string Raw)> stored = await ReadAllRawAsync(inspect);
        Assert.Contains(stored, v => v.Raw == "Zephyrine");
        Assert.Contains(stored, v => v.Raw == "NE1 4XQ");

        await using WeaverDbSession session = await inspect.OpenAsync();
        await using DbCommand versions = session.CreateCommand("SELECT COUNT(*) FROM weaver_schema_migrations WHERE version = 5;");
        Assert.Equal(0L, Convert.ToInt64(await versions.ExecuteScalarAsync()));

        if (postgres)
        {
            await using DbCommand width = session.CreateCommand(
                "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'weaver_patient_addresses' AND column_name = 'postal_code';");
            Assert.Equal(32, Convert.ToInt32(await width.ExecuteScalarAsync()));
        }
    }

    /// <summary>
    /// Verifies foreign-key, tampered and unencrypted values are refused, never rendered, never echoed.
    /// </summary>
    /// <param name="postgres"><c>true</c> to run against PostgreSQL.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignTamperedOrPlaintextValues_AreRefusedAndNeverEchoed(bool postgres)
    {
        // WHAT: Reads rows written under a different key, with a flipped tag, and with a plaintext value planted.
        // WHY:  Corrupt or foreign ciphertext must behave like the picture column: an exception the middleware
        //       turns into a redacted RFC 7807 500, never plaintext and never the ciphertext itself.
        // HOW:  Triggers each case, asserts CryptographicException text omits the stored value, then drives one
        //       through SecureExceptionHandlerMiddleware and inspects the response body.
        Backend? backend = await CreateBackendAsync(postgres);
        if (backend is null)
        {
            return;
        }

        using DatabaseMigrationRunner runner = new(null, CreateProtector("node"));
        WeaverDbConnectionFactory factory = backend.CreateFactory(runner);
        await using (WeaverDbSession seed = await factory.OpenAsync())
        {
            await InsertUserAsync(seed, "patient-foreign");
        }

        using UserProfileRepository writer = new(factory, CreateProtector("node"));
        await writer.UpsertProfileAsync(FullProfile("patient-foreign"));
        await writer.UpsertAddressAsync(FullAddress("patient-foreign"));
        await writer.UpsertContactAsync(FullContact("patient-foreign"));
        string ciphertext = (await ReadAllRawAsync(factory)).First(v => v.Where == "weaver_user_profiles.email_address").Raw;

        // A different pepper: authentication fails, so nothing is returned.
        using UserProfileRepository foreignReader = new(factory, CreateProtector("someone-else"));
        CryptographicException profileEx = await Assert.ThrowsAsync<CryptographicException>(() => foreignReader.GetProfileAsync("patient-foreign"));
        CryptographicException addressEx = await Assert.ThrowsAsync<CryptographicException>(() => foreignReader.GetAddressesAsync("patient-foreign"));
        CryptographicException contactEx = await Assert.ThrowsAsync<CryptographicException>(() => foreignReader.GetContactsAsync("patient-foreign"));
        foreach (CryptographicException ex in new[] { profileEx, addressEx, contactEx })
        {
            Assert.DoesNotContain(ciphertext, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("zephyrine", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // The same failure through the API pipeline is a redacted problem+json 500.
        SecureExceptionHandlerMiddleware middleware = new(
            async _ => await foreignReader.GetProfileAsync("patient-foreign"),
            NullLogger<SecureExceptionHandlerMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/api/profile";
        using MemoryStream body = new();
        context.Response.Body = body;
        await middleware.InvokeAsync(context);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("application/problem+json", context.Response.ContentType);
        string responseText = Encoding.UTF8.GetString(body.ToArray());
        Assert.DoesNotContain(ciphertext, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("zephyrine", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EnvelopePrefix, responseText, StringComparison.Ordinal);

        // A planted plaintext value is refused rather than rendered as if it were trustworthy.
        await using (WeaverDbSession session = await factory.OpenAsync())
        {
            await using DbCommand plant = session.CreateCommand("UPDATE weaver_patient_contacts SET notes = 'planted-plaintext-leak';");
            await plant.ExecuteNonQueryAsync();
        }

        using UserProfileRepository reader = new(factory, CreateProtector("node"));
        CryptographicException plantedEx = await Assert.ThrowsAsync<CryptographicException>(() => reader.GetContactsAsync("patient-foreign"));
        Assert.DoesNotContain("planted-plaintext-leak", plantedEx.Message, StringComparison.Ordinal);

        // A flipped authentication tag is a forgery.
        byte[] payload = Convert.FromBase64String(ciphertext[EnvelopePrefix.Length..]);
        payload[^1] ^= 0xFF;
        string forged = EnvelopePrefix + Convert.ToBase64String(payload);
        await using (WeaverDbSession session = await factory.OpenAsync())
        {
            await using DbCommand forge = session.CreateCommand("UPDATE weaver_user_profiles SET email_address = @v;");
            WeaverDbParameters.Add(forge, "@v", forged);
            await forge.ExecuteNonQueryAsync();
        }

        CryptographicException forgedEx = await Assert.ThrowsAsync<CryptographicException>(() => reader.GetProfileAsync("patient-foreign"));
        Assert.DoesNotContain(forged, forgedEx.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the single-field protector API: null, empty, whitespace, look-alike envelopes and strict reads.
    /// </summary>
    [Fact]
    public void FieldProtector_PreservesNullAndEmpty_EncryptsLookAlikes_AndRefusesPlaintext()
    {
        // WHAT: Exercises EncryptField, DecryptField and their NOT NULL counterparts.
        // WHY:  These four methods are the whole contract the repository relies on.
        // HOW:  Covers each branch explicitly, including a patient typing a string that looks like an envelope.
        ProfileDataProtector protector = CreateProtector("node");

        Assert.Null(protector.EncryptField(null));
        Assert.Equal(string.Empty, protector.EncryptField(string.Empty));
        Assert.Null(protector.DecryptField(null));
        Assert.Equal(string.Empty, protector.DecryptField(string.Empty));
        Assert.Equal(string.Empty, protector.EncryptRequiredField(string.Empty));
        Assert.Equal(string.Empty, protector.DecryptRequiredField(string.Empty));

        string sealedSpaces = protector.EncryptField("   ")!;
        Assert.True(ProfileDataProtector.IsEncrypted(sealedSpaces));
        Assert.Equal("   ", protector.DecryptField(sealedSpaces));

        string lookAlike = EnvelopePrefix + "AAAA";
        string sealedLookAlike = protector.EncryptRequiredField(lookAlike);
        Assert.NotEqual(lookAlike, sealedLookAlike);
        Assert.Equal(lookAlike, protector.DecryptRequiredField(sealedLookAlike));

        Assert.False(ProfileDataProtector.IsEncrypted(null));
        Assert.False(ProfileDataProtector.IsEncrypted("plain"));
        Assert.True(ProfileDataProtector.IsEncrypted(EnvelopePrefix.ToUpperInvariant()));

        CryptographicException plain = Assert.Throws<CryptographicException>(() => protector.DecryptField("plain"));
        Assert.DoesNotContain("plain", plain.Message, StringComparison.Ordinal);
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredField("plain"));
    }

    /// <summary>
    /// Verifies the migration's identity and the DDL it emits for each dialect.
    /// </summary>
    [Fact]
    public void Migration005_Identity_AndDialectSql()
    {
        // WHAT: Checks version, name, and the generated SQL.
        // WHY:  PostgreSQL must widen exactly the encrypted columns; SQLite needs no DDL; username must stay put.
        // HOW:  Compares the ALTER statements against the authoritative protected-column list.
        Migration005_EncryptPatientPhi migration = new();
        Assert.Equal(5, migration.Version);
        Assert.Equal("005_EncryptPatientPhi", migration.Name);
        Assert.Equal(string.Empty, migration.GetSql("sqlite"));
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string postgresSql = migration.GetSql("postgresql");
        foreach ((string table, string column) in ProtectedColumns)
        {
            Assert.Contains($"ALTER TABLE {table} ALTER COLUMN {column} TYPE TEXT", postgresSql, StringComparison.Ordinal);
        }

        Assert.Equal(ProtectedColumns.Length, postgresSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length);
        Assert.DoesNotContain("username", postgresSql, StringComparison.Ordinal);
        Assert.DoesNotContain("country", postgresSql, StringComparison.Ordinal);
    }
}
