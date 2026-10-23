using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive unit, integration, and security test suite validating user profiles,
/// structured patient addresses, emergency outreach contacts, envelope encryption, and migrations.
/// </summary>
public sealed class UserProfileAndContactTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for test database fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance targeting isolated database directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client for API verification.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProfileAndContactTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public UserProfileAndContactTests(WebApplicationFactory<Program> baseFactory)
    {
        // WHAT: Prepares isolated test directories and WebApplicationFactory host.
        // WHY:  Ensures test database isolation without affecting workspace state.
        // HOW:  Creates temporary directory, sets configuration environment overrides, and mints HTTP client.
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_profile_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });

        _client = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Cleans up temporary test files.
        // WHY:  Prevents disk clutter across repeated test runs.
        // HOW:  Recursively deletes the isolated directory.
        _client.Dispose();
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_tempDataDir))
            {
                Directory.Delete(_tempDataDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures on Windows/temp locks
        }
    }

    /// <summary>
    /// Creates a <see cref="ProfileDataProtector"/> whose PHI master key is provisioned inside this test
    /// instance's isolated temporary directory.
    /// </summary>
    /// <returns>A protector backed by a freshly generated, test-local 256-bit key.</returns>
    /// <remarks>
    /// The protector no longer accepts configuration directly: its key comes from
    /// <see cref="INodeSecretStore"/>, because the previous implementation fell back to the SHA-256 of a
    /// constant published in this repository. Pointing the store at the per-test directory keeps the generated
    /// key files out of the repository working tree and out of every other test class's way.
    /// </remarks>
    private ProfileDataProtector CreateIsolatedProtector()
    {
        IConfiguration keyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEAVER_NODE_KEYS_DIR"] = Path.Combine(_tempDataDir, "node-keys")
            })
            .Build();

        return new ProfileDataProtector(new NodeSecretStore(keyConfiguration));
    }

    /// <summary>
    /// Verifies that <see cref="ProfileDataProtector"/> encrypts and decrypts sensitive picture payloads with AES-256-GCM.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_RoundTrip_EncryptsAndDecryptsAccurately()
    {
        // WHAT: Tests cryptographic envelope encryption on data-URIs.
        // WHY:  Ensures patient avatars and sensitive PHI are encrypted at rest using AES-256-GCM.
        // HOW:  Encrypts a sample image data-URI, checks the prefix, and decrypts back to identical plaintext.
        ProfileDataProtector protector = CreateIsolatedProtector();
        string original = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        string encrypted = protector.EncryptDataUri(original);
        Assert.NotNull(encrypted);
        Assert.StartsWith("data:application/encrypted;base64,", encrypted);
        Assert.NotEqual(original, encrypted);

        string? decrypted = protector.DecryptDataUri(encrypted);
        Assert.Equal(original, decrypted);

        // Edge case: empty or whitespace
        Assert.Equal(string.Empty, protector.EncryptDataUri(string.Empty));
        Assert.Equal(string.Empty, protector.EncryptDataUri(null));
        Assert.Equal(string.Empty, protector.DecryptDataUri(string.Empty));
        Assert.Equal(string.Empty, protector.DecryptDataUri(null));

        // Edge case: plain non-encrypted string passed to Decrypt returns as-is
        Assert.Equal("plain_string", protector.DecryptDataUri("plain_string"));
    }

    /// <summary>
    /// Verifies edge cases in <see cref="ProfileDataProtector"/> including already encrypted payloads and short byte slices.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_EdgeCases_CoverAllBranches()
    {
        // WHAT: Verifies handling of pre-encrypted strings and structurally impossible envelopes.
        // WHY:  Guarantees complete branch coverage, and pins the contract that a value claiming to be a
        //       protected envelope is never handed back to the caller as if it were plaintext.
        // HOW:  Passes a prefixed string to EncryptDataUri and a too-short buffer to DecryptDataUri.
        ProfileDataProtector protector = CreateIsolatedProtector();

        // Already encrypted payload returns as-is
        string alreadyEnc = "data:application/encrypted;base64,alreadyencryptedpayload";
        Assert.Equal(alreadyEnc, protector.EncryptDataUri(alreadyEnc));

        // Short payload (shorter than nonce + tag: 12 + 16 = 28 bytes) is a malformed envelope, which is now
        // reported rather than returned unchanged: returning it would dress ciphertext up as plaintext.
        byte[] tiny = new byte[10];
        string shortEnc = "data:application/encrypted;base64," + Convert.ToBase64String(tiny);
        CryptographicException shortEx = Assert.Throws<CryptographicException>(() => protector.DecryptDataUri(shortEnc));
        Assert.Contains("malformed", shortEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies constructor argument validation for the repository and the connection factory.
    /// </summary>
    [Fact]
    public void UserProfileRepository_AndFactory_ConstructorsRejectNulls()
    {
        // WHAT: Verifies constructor argument validation on the repository and the factory.
        // WHY:  Ensures a missing dependency fails at construction rather than on first use.
        // HOW:  Passes null for each dependency in turn and asserts ArgumentNullException.
        ProfileDataProtector protector = CreateIsolatedProtector();
        using DatabaseMigrationRunner runner = new();
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_ctor_" + Guid.NewGuid().ToString("N"));
        var isolated = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TRAJECTORY_INCOMING_DIR"] = tempDir })
            .Build();
        try
        {
            Assert.Throws<ArgumentNullException>(() => new WeaverDbConnectionFactory(null!, runner, NullLogger<WeaverDbConnectionFactory>.Instance));
            Assert.Throws<ArgumentNullException>(() => new WeaverDbConnectionFactory(isolated, null!, NullLogger<WeaverDbConnectionFactory>.Instance));
            Assert.Throws<ArgumentNullException>(() => new WeaverDbConnectionFactory(isolated, runner, null!));
            Assert.Throws<ArgumentNullException>(() => new SystemConfigurationStore(null!));

            WeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(isolated, runner);
            Assert.Throws<ArgumentNullException>(() => new UserProfileRepository(null!, protector));
            Assert.Throws<ArgumentNullException>(() => new UserProfileRepository(factory, null!));

            using UserProfileRepository repo = new(factory, protector);
            Assert.NotNull(repo);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies mapping branches for null fields, corrupted pronouns, and boolean/datetime reader types.
    /// </summary>
    [Fact]
    public async Task UserProfileRepository_NullFieldsAndCorruptedPronouns_CoverMappingBranches()
    {
        // WHAT: Verifies null database columns and corrupted JSON fallbacks in UserProfileRepository.
        // WHY:  Ensures patient demographic records with optional fields map cleanly without null reference errors.
        // HOW:  Persists a minimal profile with nulls, queries SQLite, corrupts pronouns_json, and tests reader helpers.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_null_map_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = tempDir
                })
                .Build();

            SystemConfigurationStore configStore = new(WeaverTestDb.CreateFactory(config));
            DatabaseMigrationRunner migrationRunner = new();
            UserRepository userRepo = new(WeaverTestDb.CreateFactory(config, migrationRunner));
            await userRepo.InitializeAsync();

            string username = "null-user";
            await userRepo.CreateUserAsync(new UserAccount
            {
                Username = username,
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEA123456789012345678901234567890123456789012="
            });

            ProfileDataProtector protector = CreateIsolatedProtector();
            using UserProfileRepository repo = new(WeaverTestDb.CreateFactory(config, migrationRunner), protector);

            // Upsert a profile where all optional nullable fields are null, and pronouns are empty
            UserProfile minimalProfile = new()
            {
                Username = username,
                FirstName = "Min",
                LastName = "User",
                PreferredName = null,
                EmailAddress = null,
                PhoneNumber = null,
                DateOfBirth = null,
                Picture = null,
                Regimen = null,
                GenderAssignedAtBirth = GenderAssignedAtBirth.Both,
                EmergencyMedicalNotes = null,
                Bio = null,
                Pronouns = []
            };
            await repo.UpsertProfileAsync(minimalProfile);

            UserProfile? fetched = await repo.GetProfileAsync(username);
            Assert.NotNull(fetched);
            Assert.Equal(GenderAssignedAtBirth.Both, fetched.GenderAssignedAtBirth);
            Assert.Null(fetched.PreferredName);
            Assert.Null(fetched.EmailAddress);
            Assert.Null(fetched.PhoneNumber);
            Assert.Null(fetched.DateOfBirth);
            Assert.True(string.IsNullOrEmpty(fetched.Picture));
            Assert.Null(fetched.Regimen);
            Assert.Null(fetched.EmergencyMedicalNotes);
            Assert.Null(fetched.Bio);
            Assert.Empty(fetched.Pronouns);

            // Directly corrupt pronouns_json in SQLite to test corrupted JSON branch
            using (SqliteConnection conn = new($"Data Source={Path.Combine(tempDir, "weaver_config.db")}"))
            {
                await conn.OpenAsync();
                // The column holds an encrypted envelope; seal the payload so decryption succeeds and the JSON handling is what runs.
                using var cmd = new SqliteCommand("UPDATE weaver_user_profiles SET pronouns_json = @sealed WHERE username = 'null-user'", conn);
                cmd.Parameters.AddWithValue("@sealed", protector.EncryptRequiredField("{invalid_json"));
                await cmd.ExecuteNonQueryAsync();
            }

            UserProfile? corruptedPronounsProfile = await repo.GetProfileAsync(username);
            Assert.NotNull(corruptedPronounsProfile);
            Assert.Empty(corruptedPronounsProfile.Pronouns);

            // Test literal "null" JSON string deserializing to null array triggering ?? [] fallback
            using (SqliteConnection conn = new($"Data Source={Path.Combine(tempDir, "weaver_config.db")}"))
            {
                await conn.OpenAsync();
                // The column holds an encrypted envelope; seal the payload so decryption succeeds and the JSON handling is what runs.
                using var cmd = new SqliteCommand("UPDATE weaver_user_profiles SET pronouns_json = @sealed WHERE username = 'null-user'", conn);
                cmd.Parameters.AddWithValue("@sealed", protector.EncryptRequiredField("null"));
                await cmd.ExecuteNonQueryAsync();
            }

            UserProfile? nullJsonPronounsProfile = await repo.GetProfileAsync(username);
            Assert.NotNull(nullJsonPronounsProfile);
            Assert.Empty(nullJsonPronounsProfile.Pronouns);

            // Address with null AddressLine2
            PatientAddress addr = new()
            {
                Id = "addr-null-2",
                Username = username,
                AddressType = "Delivery",
                AddressLine1 = "777 Sovereign St",
                AddressLine2 = null,
                City = "Olympia",
                StateOrProvince = "WA",
                PostalCode = "98501",
                Country = "United States",
                IsPrimary = false
            };
            await repo.UpsertAddressAsync(addr);

            var addrs = await repo.GetAddressesAsync(username);
            Assert.Single(addrs);
            Assert.Null(addrs[0].AddressLine2);
            Assert.False(addrs[0].IsPrimary);

            // Contact with null EmailAddress and null Notes
            PatientContact cont = new()
            {
                Id = "cont-null-notes",
                Username = username,
                ContactName = "Dave",
                Relationship = "Friend",
                PhoneNumber = "+1-555-7777",
                EmailAddress = null,
                Notes = null,
                PriorityOrder = 1,
                CanDiscussMedicalInfo = false
            };
            await repo.UpsertContactAsync(cont);

            var contacts = await repo.GetContactsAsync(username);
            Assert.Single(contacts);
            Assert.Null(contacts[0].EmailAddress);
            Assert.Null(contacts[0].Notes);
            Assert.False(contacts[0].CanDiscussMedicalInfo);

            // Test MockDateTimeDataReader for Address mapping
            PatientAddress mappedAddress = UserProfileRepository.MapAddressFromReader(new MockDateTimeAddressDataReader(protector), protector);
            Assert.Equal("test_id", mappedAddress.Id);
            Assert.True(mappedAddress.IsPrimary);

            // Test MockDateTimeContactDataReader for Contact mapping
            PatientContact mappedContact = UserProfileRepository.MapContactFromReader(new MockDateTimeContactDataReader(protector), protector);
            Assert.Equal("test_id", mappedContact.Id);
            Assert.True(mappedContact.CanDiscussMedicalInfo);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Mock reader supplying DateTime and bool types for testing MapAddressFromReader branches.
    /// </summary>
    private sealed class MockDateTimeAddressDataReader : DbDataReader
    {
        /// <summary>Envelope standing in for an encrypted PHI column.</summary>
        private readonly string _encryptedValue;

        /// <summary>Creates the mock with a real envelope produced by the protector under test.</summary>
        /// <param name="protector">Protector used to manufacture the encrypted column value.</param>
        public MockDateTimeAddressDataReader(ProfileDataProtector protector)
        {
            _encryptedValue = protector.EncryptRequiredField("test");
        }

        public override bool Read() => true;
        public override bool NextResult() => false;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int FieldCount => 12;
        public override int RecordsAffected => 1;
        public override int Depth => 0;
        public override object this[int ordinal] => "test";
        public override object this[string name] => "test";
        public override bool IsDBNull(int ordinal) => false;
        public override Type GetFieldType(int ordinal) => ordinal switch
        {
            9 => typeof(bool),
            10 => typeof(DateTime),
            11 => typeof(DateTime),
            _ => typeof(string)
        };
        public override string GetString(int ordinal) => ordinal == 0 ? "test_id" : (ordinal is >= 3 and <= 7) ? _encryptedValue : "test";
        public override bool GetBoolean(int ordinal) => true;
        public override DateTime GetDateTime(int ordinal) => DateTime.UtcNow;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => 'a';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "text";
        public override decimal GetDecimal(int ordinal) => 0m;
        public override double GetDouble(int ordinal) => 0.0;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
        public override float GetFloat(int ordinal) => 0f;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 1;
        public override long GetInt64(int ordinal) => 1L;
        public override string GetName(int ordinal) => "col";
        public override int GetOrdinal(string name) => 0;
        public override object GetValue(int ordinal) => "test";
        public override int GetValues(object[] values) => 0;
    }

    /// <summary>
    /// Mock reader supplying DateTime and bool types for testing MapContactFromReader branches.
    /// </summary>
    private sealed class MockDateTimeContactDataReader : DbDataReader
    {
        /// <summary>Envelope standing in for an encrypted PHI column.</summary>
        private readonly string _encryptedValue;

        /// <summary>Creates the mock with a real envelope produced by the protector under test.</summary>
        /// <param name="protector">Protector used to manufacture the encrypted column value.</param>
        public MockDateTimeContactDataReader(ProfileDataProtector protector)
        {
            _encryptedValue = protector.EncryptRequiredField("test");
        }

        public override bool Read() => true;
        public override bool NextResult() => false;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int FieldCount => 11;
        public override int RecordsAffected => 1;
        public override int Depth => 0;
        public override object this[int ordinal] => "test";
        public override object this[string name] => "test";
        public override bool IsDBNull(int ordinal) => false;
        public override Type GetFieldType(int ordinal) => ordinal switch
        {
            6 => typeof(int),
            7 => typeof(bool),
            9 => typeof(DateTime),
            10 => typeof(DateTime),
            _ => typeof(string)
        };
        public override string GetString(int ordinal) => ordinal == 0 ? "test_id" : (ordinal is (>= 2 and <= 5) or 8) ? _encryptedValue : "test";
        public override int GetInt32(int ordinal) => 1;
        public override bool GetBoolean(int ordinal) => true;
        public override DateTime GetDateTime(int ordinal) => DateTime.UtcNow;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => 'a';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "text";
        public override decimal GetDecimal(int ordinal) => 0m;
        public override double GetDouble(int ordinal) => 0.0;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
        public override float GetFloat(int ordinal) => 0f;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override long GetInt64(int ordinal) => 1L;
        public override string GetName(int ordinal) => "col";
        public override int GetOrdinal(string name) => 0;
        public override object GetValue(int ordinal) => "test";
        public override int GetValues(object[] values) => 0;
    }

    /// <summary>
    /// Verifies that <see cref="ProfileDataProtector"/> reports tampered ciphertext and bad envelopes instead of
    /// returning the ciphertext as if it were plaintext.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_TamperedPayload_ReportsCryptographicFailure()
    {
        // WHAT: Tests tampering detection with AES-256-GCM authentication tags.
        // WHY:  A flipped tag byte means the value is forged or corrupt. The previous behaviour returned the
        //       ciphertext unchanged, which the caller could not distinguish from a value that was never
        //       encrypted, so a wrong key looked exactly like plaintext. The failure is now observable.
        // HOW:  Mangles the authentication tag and asserts DecryptDataUri raises CryptographicException with a
        //       message that leaks no key material, path or SQL.
        ProfileDataProtector protector = CreateIsolatedProtector();

        string original = "data:image/png;base64,VGhpcyBpcyBhIHRlc3Q=";
        string encrypted = protector.EncryptDataUri(original);

        // Tamper with payload
        string rawBase64 = encrypted["data:application/encrypted;base64,".Length..];
        byte[] bytes = Convert.FromBase64String(rawBase64);
        bytes[^1] ^= 0xFF; // Flip bits in authentication tag
        string tampered = "data:application/encrypted;base64," + Convert.ToBase64String(bytes);

        CryptographicException tamperEx = Assert.Throws<CryptographicException>(() => protector.DecryptDataUri(tampered));
        Assert.Contains("could not be decrypted", tamperEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", tamperEx.Message);
        Assert.DoesNotContain("SELECT", tamperEx.Message, StringComparison.OrdinalIgnoreCase);

        // Invalid base64 is a malformed envelope and is likewise reported.
        string invalidPayload = "data:application/encrypted;base64,invalid!base64";
        CryptographicException formatEx = Assert.Throws<CryptographicException>(() => protector.DecryptDataUri(invalidPayload));
        Assert.IsType<FormatException>(formatEx.InnerException);
    }

    /// <summary>
    /// Verifies that <see cref="Migration003_UserProfileAddressesAndContacts"/> generates valid SQL and executes Up across dialects.
    /// </summary>
    [Fact]
    public async Task Migration003_GeneratesValidSqlAndExecutesAcrossDialects()
    {
        // WHAT: Tests schema migration 003 DDL generation and execution.
        // WHY:  Guarantees deterministic schema evolution for user profiles, addresses, and contacts.
        // HOW:  Runs UpAsync on an in-memory SQLite connection and validates PostgreSQL DDL generation.
        Migration003_UserProfileAddressesAndContacts migration = new();
        Assert.Equal(3, migration.Version);
        Assert.Equal("003_UserProfileAddressesAndContacts", migration.Name);

        string sqliteSql = migration.GetSql("sqlite");
        Assert.Contains("weaver_user_profiles", sqliteSql);
        Assert.Contains("weaver_patient_addresses", sqliteSql);
        Assert.Contains("weaver_patient_contacts", sqliteSql);

        string pgSql = migration.GetSql("postgresql");
        Assert.Contains("weaver_user_profiles", pgSql);
        Assert.Contains("weaver_patient_addresses", pgSql);
        Assert.Contains("weaver_patient_contacts", pgSql);
        Assert.Contains("TIMESTAMP", pgSql);

        using SqliteConnection conn = new("Data Source=:memory:");
        await conn.OpenAsync();

        // SQLite Up
        await migration.UpAsync(conn, null, "sqlite", default);

        // Verify tables exist
        using (var cmd = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('weaver_user_profiles', 'weaver_patient_addresses', 'weaver_patient_contacts')", conn))
        {
            long count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(3, count);
        }

        // Argument null checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => migration.UpAsync(null!, null, "sqlite", default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => migration.UpAsync(conn, null, null!, default));
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));
    }

    /// <summary>
    /// Verifies that <see cref="UserProfileRepository"/> supports full CRUD operations across profiles, addresses, and contacts on SQLite.
    /// </summary>
    [Fact]
    public async Task UserProfileRepository_SqliteCrud_WorksAccurately()
    {
        // WHAT: Tests complete repository CRUD lifecycle on local SQLite backend.
        // WHY:  Verifies persistent demographic data, addresses, and contacts lookup tables.
        // HOW:  Instantiates UserProfileRepository with memory configuration and tests each method.
        string dbDir = Path.Combine(Path.GetTempPath(), "weaver_repo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbDir);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = dbDir,
                    ["POSTGRES_CONNECTION_STRING"] = string.Empty
                })
                .Build();

            SystemConfigurationStore configStore = new(WeaverTestDb.CreateFactory(config));
            DatabaseMigrationRunner migrationRunner = new();
            UserRepository userRepo = new(WeaverTestDb.CreateFactory(config, migrationRunner));
            await userRepo.InitializeAsync();

            string username = "patient-test-01";
            await userRepo.CreateUserAsync(new UserAccount
            {
                Username = username,
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEA123456789012345678901234567890123456789012="
            });

            ProfileDataProtector protector = CreateIsolatedProtector();
            UserProfileRepository repo = new(WeaverTestDb.CreateFactory(config, migrationRunner), protector);

            // 1. Profile CRUD
            UserProfile? initialProfile = await repo.GetProfileAsync(username);
            Assert.Null(initialProfile);

            UserProfile newProfile = new()
            {
                Username = username,
                FirstName = "Alice",
                LastName = "Morgan",
                PreferredName = "Alice",
                EmailAddress = "alice@example.org",
                PhoneNumber = "+1-555-0123",
                DateOfBirth = "1995-04-12",
                Picture = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
                Regimen = TherapeuticRegimen.MaleToFemale,
                GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
                Pronouns = ["she", "her"],
                EmergencyMedicalNotes = "None",
                Bio = "Researcher and patient.",
                LanguagePreference = "en-US"
            };

            await repo.UpsertProfileAsync(newProfile);

            UserProfile? retrieved = await repo.GetProfileAsync(username);
            Assert.NotNull(retrieved);
            Assert.Equal("Alice", retrieved.FirstName);
            Assert.Equal("Morgan", retrieved.LastName);
            Assert.Equal(TherapeuticRegimen.MaleToFemale, retrieved.Regimen);
            Assert.Equal(GenderAssignedAtBirth.Male, retrieved.GenderAssignedAtBirth);
            Assert.Equal(["she", "her"], retrieved.Pronouns);
            Assert.Equal(newProfile.Picture, retrieved.Picture); // Decrypted back to plaintext

            // Update profile
            UserProfile updatedProfile = retrieved with
            {
                PreferredName = "Dr. Alice Morgan",
                EmergencyMedicalNotes = "Updated allergy note."
            };
            await repo.UpsertProfileAsync(updatedProfile);

            UserProfile? updated = await repo.GetProfileAsync(username);
            Assert.NotNull(updated);
            Assert.Equal("Dr. Alice Morgan", updated.PreferredName);
            Assert.Equal("Updated allergy note.", updated.EmergencyMedicalNotes);

            // 2. Address CRUD
            var addresses = await repo.GetAddressesAsync(username);
            Assert.Empty(addresses);

            PatientAddress addr1 = new()
            {
                Id = "addr-01",
                Username = username,
                AddressType = "Primary",
                AddressLine1 = "100 Sovereign Rd",
                AddressLine2 = "Suite 3",
                City = "Portland",
                StateOrProvince = "OR",
                PostalCode = "97201",
                Country = "United States",
                IsPrimary = true
            };

            await repo.UpsertAddressAsync(addr1);

            addresses = await repo.GetAddressesAsync(username);
            Assert.Single(addresses);
            Assert.Equal("100 Sovereign Rd", addresses[0].AddressLine1);
            Assert.True(addresses[0].IsPrimary);

            // Delete address
            bool deletedAddr = await repo.DeleteAddressAsync("addr-01", username);
            Assert.True(deletedAddr);

            bool deletedNonExistent = await repo.DeleteAddressAsync("addr-nonexistent", username);
            Assert.False(deletedNonExistent);

            addresses = await repo.GetAddressesAsync(username);
            Assert.Empty(addresses);

            // 3. Contact CRUD
            var contacts = await repo.GetContactsAsync(username);
            Assert.Empty(contacts);

            PatientContact contact1 = new()
            {
                Id = "c-01",
                Username = username,
                ContactName = "Bob Smith",
                Relationship = "Partner",
                PhoneNumber = "+1-555-9999",
                EmailAddress = "bob@example.org",
                PriorityOrder = 1,
                CanDiscussMedicalInfo = true,
                Notes = "Primary contact"
            };

            PatientContact contact2 = new()
            {
                Id = "c-02",
                Username = username,
                ContactName = "Carol Smith",
                Relationship = "Sibling",
                PhoneNumber = "+1-555-8888",
                EmailAddress = "carol@example.org",
                PriorityOrder = 2,
                CanDiscussMedicalInfo = false,
                Notes = "Secondary contact"
            };

            await repo.UpsertContactAsync(contact2);
            await repo.UpsertContactAsync(contact1);

            contacts = await repo.GetContactsAsync(username);
            Assert.Equal(2, contacts.Count);
            // Verify priority ordering: 1 before 2
            Assert.Equal("Bob Smith", contacts[0].ContactName);
            Assert.Equal("Carol Smith", contacts[1].ContactName);

            // Delete contact
            bool deletedContact = await repo.DeleteContactAsync("c-01", username);
            Assert.True(deletedContact);

            bool deletedNonExistentContact = await repo.DeleteContactAsync("c-999", username);
            Assert.False(deletedNonExistentContact);

            contacts = await repo.GetContactsAsync(username);
            Assert.Single(contacts);
            Assert.Equal("Carol Smith", contacts[0].ContactName);

            // Argument checks
            await Assert.ThrowsAsync<ArgumentException>(() => repo.GetProfileAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertProfileAsync(null!));
            await Assert.ThrowsAsync<ArgumentException>(() => repo.GetAddressesAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertAddressAsync(null!));
            await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteAddressAsync(string.Empty, username));
            await Assert.ThrowsAsync<ArgumentException>(() => repo.GetContactsAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertContactAsync(null!));
            await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteContactAsync(string.Empty, username));
        }
        finally
        {
            try
            {
                Directory.Delete(dbDir, true);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Verifies example models and property initializers for <see cref="UserProfile"/>, <see cref="PatientAddress"/>, and <see cref="PatientContact"/>.
    /// </summary>
    [Fact]
    public void Models_GetExamplesAndPropertySetters_FunctionCorrectly()
    {
        // WHAT: Verifies model contracts and example generation for Swagger.
        // WHY:  Ensures interactive OpenAPI metadata and default serialization states are valid.
        // HOW:  Calls GetExamples() on UserProfile, PatientAddress, and PatientContact.
        UserProfile profileExample = new UserProfile().GetExamples();
        Assert.NotNull(profileExample);
        Assert.Equal("Jane", profileExample.FirstName);
        Assert.Equal("Doe", profileExample.LastName);
        Assert.Equal(GenderAssignedAtBirth.Male, profileExample.GenderAssignedAtBirth);

        PatientAddress addressExample = new PatientAddress().GetExamples();
        Assert.NotNull(addressExample);
        Assert.Equal("742 Evergreen Terrace", addressExample.AddressLine1);
        Assert.True(addressExample.IsPrimary);

        PatientContact contactExample = new PatientContact().GetExamples();
        Assert.NotNull(contactExample);
        Assert.Equal("Alex Smith", contactExample.ContactName);
        Assert.True(contactExample.CanDiscussMedicalInfo);

        // Enum verification
        Assert.Equal(GenderAssignedAtBirth.Female, Enum.Parse<GenderAssignedAtBirth>("Female"));
        Assert.Equal(GenderAssignedAtBirth.Male, Enum.Parse<GenderAssignedAtBirth>("Male"));
        Assert.Equal(GenderAssignedAtBirth.Both, Enum.Parse<GenderAssignedAtBirth>("Both"));
    }

    /// <summary>
    /// Verifies the REST API endpoints for User Profiles, Structured Addresses, and Emergency Contacts end-to-end.
    /// </summary>
    [Fact]
    public async Task RestApi_ProfilesAddressesAndContacts_EnforcesAuthAndExecutesOperations()
    {
        // WHAT: Integration test covering all 8 new REST endpoints under Program.cs.
        // WHY:  Ensures RBAC authorization, self-access, validation, and RFC 7807 error responses work properly.
        // HOW:  Initializes admin and regular user accounts via setup, issues bearer tokens, and invokes HTTP requests.
        // 1. Setup admin account
        GenerateKeyRequest keyGenReq = new()
        {
            Username = "admin-sys",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage keyGenRes = await _client.PostAsJsonAsync("/api/setup/generate-keypair", keyGenReq);
        Assert.Equal(HttpStatusCode.OK, keyGenRes.StatusCode);
        GeneratedKeyBundle? adminBundle = await keyGenRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(adminBundle);

        InitialSetupRequest initReq = new()
        {
            AdminUsername = "admin-sys",
            PublicKeyBase64 = adminBundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };

        HttpResponseMessage initRes = await _client.PostAsJsonAsync("/api/setup/initialize", initReq);
        Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);

        // Sign in admin to get bearer token
        HttpResponseMessage adminLoginResp = await _client.PostAsJsonAsync("/api/auth/login-key", new { username = "admin-sys", privateKeyPem = adminBundle.PrivateKeyPem });
        Assert.Equal(HttpStatusCode.OK, adminLoginResp.StatusCode);
        JsonElement adminLoginJson = await adminLoginResp.Content.ReadFromJsonAsync<JsonElement>();
        string adminToken = adminLoginJson.GetProperty("token").GetString()!;

        // 2. Create regular user (patient-bob) using admin client
        using HttpRequestMessage createUserReq = new(HttpMethod.Post, "/api/users/generate-keypair")
        {
            Content = JsonContent.Create(new GenerateKeyRequest
            {
                Username = "patient-bob",
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519
            })
        };
        createUserReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        HttpResponseMessage createUserResp = await _client.SendAsync(createUserReq);
        Assert.Equal(HttpStatusCode.OK, createUserResp.StatusCode);
        GeneratedKeyBundle? bobBundle = await createUserResp.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(bobBundle);

        // Sign in bob to get bob's bearer token
        HttpResponseMessage bobLoginResp = await _client.PostAsJsonAsync("/api/auth/login-key", new { username = "patient-bob", privateKeyPem = bobBundle.PrivateKeyPem });
        Assert.Equal(HttpStatusCode.OK, bobLoginResp.StatusCode);
        JsonElement bobLoginJson = await bobLoginResp.Content.ReadFromJsonAsync<JsonElement>();
        string bobToken = bobLoginJson.GetProperty("token").GetString()!;

        // 3. Unauthenticated requests to /profile, /addresses, /contacts should return 401
        using HttpClient cleanClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        HttpResponseMessage unauthResp = await cleanClient.GetAsync("/api/users/patient-bob/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        // 4. Bob trying to view admin's profile should return 403 Forbidden
        using (HttpRequestMessage bobForbiddenReq = new(HttpMethod.Get, "/api/users/admin-sys/profile"))
        {
            bobForbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage forbiddenResp = await _client.SendAsync(bobForbiddenReq);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);
        }

        // 5. Bob viewing his own profile before creation should return 404
        using (HttpRequestMessage bobGetReq = new(HttpMethod.Get, "/api/users/patient-bob/profile"))
        {
            bobGetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage getResp = await _client.SendAsync(bobGetReq);
            Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
        }

        // 6. Bob updating his own profile via PUT /api/users/patient-bob/profile should return 200
        var bobProfileInput = new
        {
            username = "patient-bob",
            firstName = "Robert",
            lastName = "Patterson",
            preferredName = "Bob",
            emailAddress = "bob@example.org",
            phoneNumber = "+1-555-4321",
            dateOfBirth = "1992-08-20",
            regimen = "FemaleToMale",
            genderAssignedAtBirth = "Female",
            pronouns = new[] { "he", "him" },
            emergencyMedicalNotes = "Asthma inhaler required",
            bio = "Sovereign patient",
            languagePreference = "en-US"
        };

        using (HttpRequestMessage bobPutReq = new(HttpMethod.Put, "/api/users/patient-bob/profile"))
        {
            bobPutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            bobPutReq.Content = JsonContent.Create(bobProfileInput);
            HttpResponseMessage putResp = await _client.SendAsync(bobPutReq);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        }

        // 7. Bob fetching his own profile now returns 200
        using (HttpRequestMessage bobGetReq2 = new(HttpMethod.Get, "/api/users/patient-bob/profile"))
        {
            bobGetReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage getResp2 = await _client.SendAsync(bobGetReq2);
            Assert.Equal(HttpStatusCode.OK, getResp2.StatusCode);
            JsonElement pJson = await getResp2.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Robert", pJson.GetProperty("firstName").GetString());
            Assert.Equal("FemaleToMale", pJson.GetProperty("regimen").GetString());
            Assert.Equal("Female", pJson.GetProperty("genderAssignedAtBirth").GetString());
        }

        // 8. Admin can also view Bob's profile
        using (HttpRequestMessage adminGetBobReq = new(HttpMethod.Get, "/api/users/patient-bob/profile"))
        {
            adminGetBobReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            HttpResponseMessage adminGetBobResp = await _client.SendAsync(adminGetBobReq);
            Assert.Equal(HttpStatusCode.OK, adminGetBobResp.StatusCode);
        }

        // 9. Bob adds an address via POST /api/users/patient-bob/addresses
        string createdAddrId;
        using (HttpRequestMessage addAddrReq = new(HttpMethod.Post, "/api/users/patient-bob/addresses"))
        {
            addAddrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            addAddrReq.Content = JsonContent.Create(new
            {
                addressType = "Primary",
                addressLine1 = "456 Sovereign Blvd",
                city = "Seattle",
                stateOrProvince = "WA",
                postalCode = "98101",
                country = "United States",
                isPrimary = true
            });
            HttpResponseMessage addAddrResp = await _client.SendAsync(addAddrReq);
            Assert.Equal(HttpStatusCode.Created, addAddrResp.StatusCode);
            JsonElement addrJson = await addAddrResp.Content.ReadFromJsonAsync<JsonElement>();
            createdAddrId = addrJson.GetProperty("id").GetString()!;
        }

        // 10. Address validation error on missing Line1 or City
        using (HttpRequestMessage badAddrReq = new(HttpMethod.Post, "/api/users/patient-bob/addresses"))
        {
            badAddrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            badAddrReq.Content = JsonContent.Create(new
            {
                addressType = "Primary",
                addressLine1 = "",
                city = ""
            });
            HttpResponseMessage badAddrResp = await _client.SendAsync(badAddrReq);
            Assert.Equal(HttpStatusCode.BadRequest, badAddrResp.StatusCode);
        }

        // 11. Bob lists his addresses
        using (HttpRequestMessage listAddrReq = new(HttpMethod.Get, "/api/users/patient-bob/addresses"))
        {
            listAddrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage listAddrResp = await _client.SendAsync(listAddrReq);
            Assert.Equal(HttpStatusCode.OK, listAddrResp.StatusCode);
            JsonElement addrs = await listAddrResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, addrs.GetArrayLength());
        }

        // 12. Bob deletes his address
        using (HttpRequestMessage delAddrReq = new(HttpMethod.Delete, $"/api/users/patient-bob/addresses/{createdAddrId}"))
        {
            delAddrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage delAddrResp = await _client.SendAsync(delAddrReq);
            Assert.Equal(HttpStatusCode.OK, delAddrResp.StatusCode);
        }

        // Deleting non-existent address returns 404
        using (HttpRequestMessage delNonExAddrReq = new(HttpMethod.Delete, $"/api/users/patient-bob/addresses/non-existent-id"))
        {
            delNonExAddrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage delNonExAddrResp = await _client.SendAsync(delNonExAddrReq);
            Assert.Equal(HttpStatusCode.NotFound, delNonExAddrResp.StatusCode);
        }

        // 13. Bob adds an outreach contact
        string createdContactId;
        using (HttpRequestMessage addContactReq = new(HttpMethod.Post, "/api/users/patient-bob/contacts"))
        {
            addContactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            addContactReq.Content = JsonContent.Create(new
            {
                contactName = "Sarah Connor",
                relationship = "Caregiver",
                phoneNumber = "+1-555-1000",
                emailAddress = "sarah@example.org",
                priorityOrder = 1,
                canDiscussMedicalInfo = true,
                notes = "Contact first in emergency"
            });
            HttpResponseMessage addContactResp = await _client.SendAsync(addContactReq);
            Assert.Equal(HttpStatusCode.Created, addContactResp.StatusCode);
            JsonElement cJson = await addContactResp.Content.ReadFromJsonAsync<JsonElement>();
            createdContactId = cJson.GetProperty("id").GetString()!;
        }

        // 14. Contact validation error on missing name or phone
        using (HttpRequestMessage badContactReq = new(HttpMethod.Post, "/api/users/patient-bob/contacts"))
        {
            badContactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            badContactReq.Content = JsonContent.Create(new
            {
                contactName = "",
                phoneNumber = ""
            });
            HttpResponseMessage badContactResp = await _client.SendAsync(badContactReq);
            Assert.Equal(HttpStatusCode.BadRequest, badContactResp.StatusCode);
        }

        // 15. Bob lists contacts
        using (HttpRequestMessage listContactsReq = new(HttpMethod.Get, "/api/users/patient-bob/contacts"))
        {
            listContactsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage listContactsResp = await _client.SendAsync(listContactsReq);
            Assert.Equal(HttpStatusCode.OK, listContactsResp.StatusCode);
            JsonElement contacts = await listContactsResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, contacts.GetArrayLength());
        }

        // 16. Bob deletes his contact
        using (HttpRequestMessage delContactReq = new(HttpMethod.Delete, $"/api/users/patient-bob/contacts/{createdContactId}"))
        {
            delContactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage delContactResp = await _client.SendAsync(delContactReq);
            Assert.Equal(HttpStatusCode.OK, delContactResp.StatusCode);
        }

        // Deleting non-existent contact returns 404
        using (HttpRequestMessage delNonExContactReq = new(HttpMethod.Delete, $"/api/users/patient-bob/contacts/non-existent-contact-id"))
        {
            delNonExContactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
            HttpResponseMessage delNonExContactResp = await _client.SendAsync(delNonExContactReq);
            Assert.Equal(HttpStatusCode.NotFound, delNonExContactResp.StatusCode);
        }
    }
}
