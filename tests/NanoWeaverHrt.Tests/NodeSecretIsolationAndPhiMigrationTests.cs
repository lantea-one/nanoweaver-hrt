using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Test suite proving that per-node key material actually isolates nodes from one another, and that patient
/// profile data written under the historic published key is migrated to the node's own key without loss.
/// </summary>
/// <remarks>
/// These tests are the unit-level statement of the two security fixes: a bearer token minted by one node is
/// rejected by another, and a profile protector cannot read another node's ciphertext. They also pin the
/// upgrade path, because a half-finished migration would silently lose patient records.
/// </remarks>
public sealed class NodeSecretIsolationAndPhiMigrationTests : IDisposable
{
    /// <summary>
    /// The profile-protection pepper that earlier releases hard-coded in this public repository.
    /// </summary>
    /// <remarks>
    /// This literal exists in this test only. It is the input whose SHA-256 digest was the AES-256-GCM key on
    /// every default deployment, and it is needed here to manufacture genuine pre-upgrade ciphertext.
    /// </remarks>
    private const string HistoricProfilePepper = "nanoweaver_hrt_default_profile_protection_master_key_v1";

    /// <summary>
    /// Sample avatar data URI standing in for protected health information.
    /// </summary>
    private const string SamplePicture = "data:image/png;base64,VGhpcyBpcyBhIHBhdGllbnQgYXZhdGFy";

    /// <summary>
    /// Isolated temporary directory holding this instance's databases and key files.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSecretIsolationAndPhiMigrationTests"/> class.
    /// </summary>
    public NodeSecretIsolationAndPhiMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_node_isolation_tests_" + Guid.NewGuid().ToString("N"));
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
                // Ignored in test cleanup.
            }
        }
    }

    /// <summary>
    /// Builds a configuration whose database lives in a named subdirectory of this instance's temp directory.
    /// </summary>
    /// <param name="dataSubdirectory">Subdirectory name holding the SQLite database and the key directory.</param>
    /// <returns>A configuration root with an isolated data directory and an unreachable PostgreSQL endpoint.</returns>
    /// <remarks>
    /// The PostgreSQL connection string deliberately points at a closed local port so that the repositories
    /// fall back to SQLite without ever probing whatever database happens to be running on the build machine.
    /// </remarks>
    private IConfiguration BuildDataConfiguration(string dataSubdirectory)
    {
        string dataDir = Path.Combine(_tempDir, dataSubdirectory);
        Directory.CreateDirectory(dataDir);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = dataDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1"
            })
            .Build();
    }

    /// <summary>
    /// Creates a node secret store with freshly generated keys in a named subdirectory.
    /// </summary>
    /// <param name="keysSubdirectory">Subdirectory name that will hold the two key files.</param>
    /// <returns>A store whose keys exist nowhere else.</returns>
    private NodeSecretStore CreateStore(string keysSubdirectory)
    {
        return new NodeSecretStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = Path.Combine(_tempDir, keysSubdirectory)
            })
            .Build());
    }

    /// <summary>
    /// Creates a node secret store whose PHI key is the historic published key, for manufacturing legacy data.
    /// </summary>
    /// <param name="keysSubdirectory">Subdirectory name that will hold the token key file.</param>
    /// <returns>A store whose PHI master key equals the pre-upgrade key.</returns>
    private NodeSecretStore CreateHistoricPhiStore(string keysSubdirectory)
    {
        return new NodeSecretStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = Path.Combine(_tempDir, keysSubdirectory),
                [NodeSecretStore.ProfilePepperConfigKey] = HistoricProfilePepper
            })
            .Build());
    }

    /// <summary>
    /// Verifies that a bearer token minted with one node's key is rejected by a node with a different key.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TokenMintedUnderOneNodeKey_IsRejectedByAnotherNode()
    {
        // WHAT: Mints a token on node A and validates it on node B over the same user database.
        // WHY:  This is the exact property that was missing: the signing key used to be the SHA-256 of a
        //       constant published in this repository, so a token forged anywhere was valid everywhere. It is
        //       also how a token minted before the upgrade behaves afterwards: a clean rejection with a
        //       signature message, which the middleware answers with HTTP 401 and no server detail.
        // HOW:  Two UserAuthenticationService instances share one repository but hold two different key stores.
        IConfiguration configuration = BuildDataConfiguration("tokens");
        SystemConfigurationStore configStore = new(configuration);
        await configStore.InitializeAsync();

        PostQuantumCryptoProvider pqc = new();
        KeyManagementService keyManagement = new(pqc);
        ComplianceAuditService audit = new(keyManagement, pqc);

        using UserRepository userRepository = new(configuration, configStore);
        await userRepository.InitializeAsync();

        UserAuthenticationService nodeA = new(userRepository, pqc, audit, CreateStore("keys-node-a"));
        UserAuthenticationService nodeB = new(userRepository, pqc, audit, CreateStore("keys-node-b"));

        UserAccount admin = await nodeA.CompleteInitialSetupAsync(
            "isolation_admin",
            nodeA.GenerateSecureKeyBundle("isolation_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519).PublicKeyBase64,
            KeyAlgorithm.Ed25519,
            AccessGate.ApiWeb);

        string token = nodeA.GenerateBearerToken(admin);

        var (validOnA, userOnA, reasonOnA) = await nodeA.ValidateTokenAsync(token);
        Assert.True(validOnA);
        Assert.NotNull(userOnA);
        Assert.Null(reasonOnA);

        var (validOnB, userOnB, reasonOnB) = await nodeB.ValidateTokenAsync(token);
        Assert.False(validOnB);
        Assert.Null(userOnB);
        Assert.Equal("Token signature verification failed.", reasonOnB);
    }

    /// <summary>
    /// Verifies that the profile protector rejects a null secret store.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_NullSecretStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileDataProtector(null!));
    }

    /// <summary>
    /// Verifies that one node cannot read another node's protected patient data.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_SeparateNodes_CannotDecryptEachOthersCiphertext()
    {
        ProfileDataProtector nodeA = new(CreateStore("phi-node-a"));
        ProfileDataProtector nodeB = new(CreateStore("phi-node-b"));

        string ciphertext = nodeA.EncryptDataUri(SamplePicture);

        Assert.Equal(SamplePicture, nodeA.DecryptDataUri(ciphertext));
        CryptographicException ex = Assert.Throws<CryptographicException>(() => nodeB.DecryptDataUri(ciphertext));
        Assert.DoesNotContain(_tempDir, ex.Message);
        Assert.Equal(0, nodeB.LegacyDecryptionCount);
    }

    /// <summary>
    /// Verifies that data encrypted with the historic published key is still readable and is flagged for upgrade.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_HistoricCiphertext_DecryptsAndIsCountedForMigration()
    {
        // WHAT: Encrypts with the pre-upgrade key, then decrypts with a freshly provisioned node key.
        // WHY:  Upgrading the service must not lose a single patient record, and the host needs a signal
        //       telling it which rows are still protected by the world-known key.
        // HOW:  The protector tries the node key first and the decrypt-only legacy key second, counting the
        //       legacy hits.
        ProfileDataProtector historic = new(CreateHistoricPhiStore("phi-historic"));
        string legacyCiphertext = historic.EncryptDataUri(SamplePicture);

        ProfileDataProtector upgraded = new(CreateStore("phi-upgraded"));
        Assert.Equal(0, upgraded.LegacyDecryptionCount);
        Assert.Equal(SamplePicture, upgraded.DecryptDataUri(legacyCiphertext));
        Assert.Equal(1, upgraded.LegacyDecryptionCount);

        // Re-encrypting under the node key stops the legacy path being used at all.
        string nodeCiphertext = upgraded.EncryptDataUri(SamplePicture);
        Assert.Equal(SamplePicture, upgraded.DecryptDataUri(nodeCiphertext));
        Assert.Equal(1, upgraded.LegacyDecryptionCount);
    }

    /// <summary>
    /// Verifies that the historic key constant compiled into the protector is exactly the published digest.
    /// </summary>
    [Fact]
    public void HistoricPhiKey_MatchesThePublishedPepperDigest()
    {
        // WHY: The protector embeds the digest rather than the pepper text, so this test is what proves the
        //      embedded value is the right one and that pre-upgrade data really can be read.
        ProfileDataProtector historic = new(CreateHistoricPhiStore("phi-digest"));
        NodeSecretStore store = CreateHistoricPhiStore("phi-digest-store");

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(HistoricProfilePepper)), store.PhiMasterKey.ToArray());

        // A value encrypted with that key is readable by a node whose own key is different, which can only
        // happen through the decrypt-only legacy path.
        string ciphertext = historic.EncryptDataUri(SamplePicture);
        ProfileDataProtector nodeKeyed = new(CreateStore("phi-digest-node"));
        Assert.Equal(SamplePicture, nodeKeyed.DecryptDataUri(ciphertext));
    }

    /// <summary>
    /// Verifies that the startup pass re-encrypts legacy patient profiles and leaves current ones untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReEncryptLegacyProfilePhiAsync_UpgradesOnlyLegacyRowsAndLosesNoData()
    {
        // WHAT: Stages one profile encrypted with the historic key, one with the node key and one account with
        //       no profile at all, then runs the startup migration pass.
        // WHY:  Existing PHI is migrated automatically so no operator ever handles a secret, and the pass must
        //       be idempotent and must not rewrite rows that are already correct.
        // HOW:  Two repositories over one SQLite database, one holding the historic protector and one the
        //       node protector, then Program.ReEncryptLegacyProfilePhiAsync.
        IConfiguration configuration = BuildDataConfiguration("phi-migration");
        SystemConfigurationStore configStore = new(configuration);
        await configStore.InitializeAsync();

        using UserRepository userRepository = new(configuration, configStore);
        await userRepository.InitializeAsync();

        foreach (string username in new[] { "legacy_patient", "current_patient", "profileless_patient" })
        {
            await userRepository.CreateUserAsync(new UserAccount
            {
                Username = username,
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEA1111111111111111111111111111111111111111111="
            });
        }

        ProfileDataProtector historicProtector = new(CreateHistoricPhiStore("phi-migration-historic"));
        using (UserProfileRepository historicRepo = new(new WeaverDbConnectionFactory(configuration, configStore, new DatabaseMigrationRunner()), historicProtector))
        {
            await historicRepo.UpsertProfileAsync(BuildProfile("legacy_patient"));
        }

        NodeSecretStore nodeStore = CreateStore("phi-migration-node");
        ProfileDataProtector nodeProtector = new(nodeStore);
        using UserProfileRepository nodeRepo = new(new WeaverDbConnectionFactory(configuration, configStore, new DatabaseMigrationRunner()), nodeProtector);
        await nodeRepo.UpsertProfileAsync(BuildProfile("current_patient"));

        int upgraded = await Program.ReEncryptLegacyProfilePhiAsync(userRepository, nodeRepo, nodeProtector);

        Assert.Equal(1, upgraded);
        Assert.Equal(1, nodeProtector.LegacyDecryptionCount);

        // A second pass, with a protector holding the same persisted key, must find nothing left to do and must
        // never touch the legacy key again: the stored ciphertext now belongs to this node.
        ProfileDataProtector verifyProtector = new(CreateStore("phi-migration-node"));
        using UserProfileRepository verifyRepo = new(new WeaverDbConnectionFactory(configuration, configStore, new DatabaseMigrationRunner()), verifyProtector);
        int secondPass = await Program.ReEncryptLegacyProfilePhiAsync(userRepository, verifyRepo, verifyProtector);

        Assert.Equal(0, secondPass);
        Assert.Equal(0, verifyProtector.LegacyDecryptionCount);

        // No patient data was lost or altered by the re-encryption.
        UserProfile? migrated = await verifyRepo.GetProfileAsync("legacy_patient");
        Assert.NotNull(migrated);
        Assert.Equal(SamplePicture, migrated.Picture);
        Assert.Equal("Legacy", migrated.FirstName);

        UserProfile? untouched = await verifyRepo.GetProfileAsync("current_patient");
        Assert.NotNull(untouched);
        Assert.Equal(SamplePicture, untouched.Picture);

        Assert.Null(await verifyRepo.GetProfileAsync("profileless_patient"));
    }

    /// <summary>
    /// Builds a patient profile carrying a protected avatar.
    /// </summary>
    /// <param name="username">Account the profile belongs to.</param>
    /// <returns>A profile whose picture field is protected health information.</returns>
    private static UserProfile BuildProfile(string username)
    {
        return new UserProfile
        {
            Username = username,
            FirstName = username == "legacy_patient" ? "Legacy" : "Current",
            LastName = "Patient",
            Picture = SamplePicture,
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
            Pronouns = ["she", "her"],
            LanguagePreference = "en-US",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
