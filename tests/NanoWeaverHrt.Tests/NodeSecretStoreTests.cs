using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite verifying first-launch provisioning, persistence, permission hardening, fail-closed
/// validation and configured overrides of the node's bearer-token signing key and patient data (PHI) master key.
/// </summary>
/// <remarks>
/// Every test works inside its own GUID-suffixed temporary directory and builds its configuration in memory.
/// No test reads or writes the repository's <c>simulations/</c> tree, no test sets a process-wide environment
/// variable (which would leak across the parallel test classes), and no test uses threads or sleeps.
/// </remarks>
public sealed class NodeSecretStoreTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding this instance's key files.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSecretStoreTests"/> class.
    /// </summary>
    public NodeSecretStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_node_secret_tests_" + Guid.NewGuid().ToString("N"));
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
    /// Builds an in-memory configuration from the supplied key/value pairs.
    /// </summary>
    /// <param name="values">Configuration entries to expose.</param>
    /// <returns>A configuration root containing only <paramref name="values"/>.</returns>
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Builds a configuration that points the key directory at this instance's temporary directory.
    /// </summary>
    /// <param name="extra">Optional additional configuration entries.</param>
    /// <returns>A configuration root naming the isolated key directory.</returns>
    private IConfiguration BuildDirectoryConfiguration(Dictionary<string, string?>? extra = null)
    {
        Dictionary<string, string?> values = new()
        {
            [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
        };

        if (extra is not null)
        {
            foreach (KeyValuePair<string, string?> entry in extra)
            {
                values[entry.Key] = entry.Value;
            }
        }

        return BuildConfiguration(values);
    }

    /// <summary>
    /// Verifies that a null configuration is rejected.
    /// </summary>
    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeSecretStore(null!));
    }

    /// <summary>
    /// Verifies that the very first start generates both key files with 256 bits each and mode 0600.
    /// </summary>
    [Fact]
    public void FirstLaunch_GeneratesBothKeyFilesWithOwnerOnlyPermissions()
    {
        // WHAT: Constructs the store against an empty directory.
        // WHY:  Zero operator configuration must be enough to obtain two strong, independent secrets.
        // HOW:  Asserts file existence, exact length, Unix mode 0600, provenance and key independence.
        NodeSecretStore store = new(BuildDirectoryConfiguration());

        string tokenPath = Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName);
        string phiPath = Path.Combine(_tempDir, NodeSecretStore.PhiMasterKeyFileName);

        Assert.True(File.Exists(tokenPath));
        Assert.True(File.Exists(phiPath));
        Assert.Equal(NodeSecretStore.SecretLengthBytes, new FileInfo(tokenPath).Length);
        Assert.Equal(NodeSecretStore.SecretLengthBytes, new FileInfo(phiPath).Length);
        // CA1416 is suppressed for the two assertions below for the same reason the production code suppresses
        // it: the key files are owner-only by design and the whole subsystem is Unix-only, so reading the Unix
        // mode is exactly the right assertion and an OS branch here would never be taken.
#pragma warning disable CA1416 // Platform compatibility: node key material is Unix-only by design.
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokenPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(phiPath));
#pragma warning restore CA1416

        Assert.Equal(NodeSecretOrigin.GeneratedFile, store.TokenSigningKeyOrigin);
        Assert.Equal(NodeSecretOrigin.GeneratedFile, store.PhiMasterKeyOrigin);
        Assert.Equal(NodeSecretStore.SecretLengthBytes, store.TokenSigningKey.Length);
        Assert.Equal(NodeSecretStore.SecretLengthBytes, store.PhiMasterKey.Length);
        Assert.False(store.TokenSigningKey.Span.SequenceEqual(store.PhiMasterKey.Span));
        Assert.Equal(_tempDir, store.NodeKeysDirectory);

        // No temporary artefact of the atomic publication may survive.
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    /// <summary>
    /// Verifies that a restart adopts the persisted keys byte for byte, so tokens and PHI survive it.
    /// </summary>
    [Fact]
    public void SecondLaunch_ReusesPersistedKeysByteForByte()
    {
        NodeSecretStore first = new(BuildDirectoryConfiguration());
        byte[] firstToken = first.TokenSigningKey.ToArray();
        byte[] firstPhi = first.PhiMasterKey.ToArray();

        NodeSecretStore second = new(BuildDirectoryConfiguration());

        Assert.Equal(firstToken, second.TokenSigningKey.ToArray());
        Assert.Equal(firstPhi, second.PhiMasterKey.ToArray());
        Assert.Equal(NodeSecretOrigin.ExistingFile, second.TokenSigningKeyOrigin);
        Assert.Equal(NodeSecretOrigin.ExistingFile, second.PhiMasterKeyOrigin);
    }

    /// <summary>
    /// Regression guard: the generated keys must not equal the constants that earlier releases published.
    /// </summary>
    [Fact]
    public void GeneratedKeys_AreNotTheHistoricPublishedConstants()
    {
        // The two literals below appear in this test and nowhere else in the repository. They are the exact
        // strings whose SHA-256 digests used to be the bearer-token signing key and the PHI master key on every
        // deployment on earth. Asserting against them stops either defect from being reintroduced.
        byte[] historicTokenKey = SHA256.HashData(Encoding.UTF8.GetBytes("node_signing_master:WEAVER_BEARER_TOKEN_SECRET_V1"));
        byte[] historicPhiKey = SHA256.HashData(Encoding.UTF8.GetBytes("nanoweaver_hrt_default_profile_protection_master_key_v1"));

        NodeSecretStore store = new(BuildDirectoryConfiguration());

        Assert.NotEqual(historicTokenKey, store.TokenSigningKey.ToArray());
        Assert.NotEqual(historicPhiKey, store.PhiMasterKey.ToArray());
    }

    /// <summary>
    /// Verifies every branch of the key directory resolution order.
    /// </summary>
    [Fact]
    public void ResolveNodeKeysDirectory_CoversExplicitDerivedAndDefaultPaths()
    {
        // WHAT: Exercises the three-step resolution order as a pure path computation.
        // WHY:  The service must come up with nothing configured, must follow the data directory when only
        //       that is configured, and must obey an explicit setting when one is given.
        // HOW:  Calls the resolver directly; it creates nothing and touches no file.
        string explicitDir = NodeSecretStore.ResolveNodeKeysDirectory(BuildConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.NodeKeysDirectoryConfigKey] = "/srv/weaver/keys"
        }));
        Assert.Equal("/srv/weaver/keys", explicitDir);

        string derivedDir = NodeSecretStore.ResolveNodeKeysDirectory(BuildConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.NodeKeysDirectoryConfigKey] = "   ",
            [NodeSecretStore.IncomingDirectoryConfigKey] = "/workspace/data"
        }));
        Assert.Equal(Path.Combine("/workspace/data", NodeSecretStore.NodeKeysDirectoryName), derivedDir);

        string defaultDir = NodeSecretStore.ResolveNodeKeysDirectory(BuildConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.IncomingDirectoryConfigKey] = "  "
        }));
        Assert.Equal(
            Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data", NodeSecretStore.NodeKeysDirectoryName),
            defaultDir);

        string emptyConfigDir = NodeSecretStore.ResolveNodeKeysDirectory(new ConfigurationBuilder().Build());
        Assert.Equal(defaultDir, emptyConfigDir);
    }

    /// <summary>
    /// Verifies that a key directory nested several levels deep is created on demand.
    /// </summary>
    [Fact]
    public void MissingNestedDirectory_IsCreatedOnFirstLaunch()
    {
        string nested = Path.Combine(_tempDir, "nested", "deeper");
        NodeSecretStore store = new(BuildConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.NodeKeysDirectoryConfigKey] = nested
        }));

        Assert.True(Directory.Exists(nested));
        Assert.Equal(nested, store.NodeKeysDirectory);
        Assert.True(File.Exists(Path.Combine(nested, NodeSecretStore.TokenSigningKeyFileName)));
    }

    /// <summary>
    /// Verifies that the key directory is derived from the trajectory data directory when nothing explicit is set.
    /// </summary>
    [Fact]
    public void DataDirectoryConfiguration_PlacesKeysInNodeKeysSubdirectory()
    {
        // WHY: Placing the keys one level below the data directory keeps them out of reach of every HTTP route,
        //      because the trajectory listing, export, import and download paths all work on the top level only.
        NodeSecretStore store = new(BuildConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.IncomingDirectoryConfigKey] = _tempDir
        }));

        string expected = Path.Combine(_tempDir, NodeSecretStore.NodeKeysDirectoryName);
        Assert.Equal(expected, store.NodeKeysDirectory);
        Assert.True(File.Exists(Path.Combine(expected, NodeSecretStore.PhiMasterKeyFileName)));
    }

    /// <summary>
    /// Verifies that a truncated token key file stops the node and that the message leaks no directory path.
    /// </summary>
    [Fact]
    public void TruncatedTokenKeyFile_ThrowsAndNamesOnlyTheFile()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName), new byte[31]);

        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => new NodeSecretStore(BuildDirectoryConfiguration()));

        Assert.Contains(NodeSecretStore.TokenSigningKeyFileName, ex.Message);
        Assert.DoesNotContain(_tempDir, ex.Message);
    }

    /// <summary>
    /// Verifies that an over-long PHI key file stops the node instead of being silently regenerated.
    /// </summary>
    [Fact]
    public void OverLongPhiKeyFile_Throws()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, NodeSecretStore.PhiMasterKeyFileName), new byte[33]);

        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => new NodeSecretStore(BuildDirectoryConfiguration()));

        Assert.Contains(NodeSecretStore.PhiMasterKeyFileName, ex.Message);
    }

    /// <summary>
    /// Verifies that a zero-length key file is a hard failure, not an invitation to regenerate.
    /// </summary>
    [Fact]
    public void EmptyKeyFile_Throws()
    {
        // WHY: A "length == 0 means regenerate" shortcut would destroy the ability to decrypt stored PHI the
        //      first time a backup restored an empty file.
        File.WriteAllBytes(Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName), []);

        Assert.Throws<NodeSecretProvisioningException>(() => new NodeSecretStore(BuildDirectoryConfiguration()));
    }

    /// <summary>
    /// Verifies that an unreadable key file surfaces as a wrapped <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    [Fact]
    public void UnreadableKeyFile_WrapsUnauthorizedAccessException()
    {
        // HOW: On Linux, reading a directory as a file raises UnauthorizedAccessException, which is the same
        //      exception the runtime raises for a key file whose mode denies the service account.
        string directoryShapedPath = Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName);
        Directory.CreateDirectory(directoryShapedPath);

        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => NodeSecretStore.ReadSecretFile(directoryShapedPath));

        Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        Assert.Contains(NodeSecretStore.TokenSigningKeyFileName, ex.Message);
    }

    /// <summary>
    /// Verifies that an absent key file surfaces as a wrapped <see cref="IOException"/>.
    /// </summary>
    [Fact]
    public void MissingKeyFile_WrapsIoException()
    {
        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => NodeSecretStore.ReadSecretFile(Path.Combine(_tempDir, "absent.key")));

        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that exclusive creation refuses to write over an existing path.
    /// </summary>
    [Fact]
    public void WriteSecretFileExclusive_ExistingPath_Throws()
    {
        string path = Path.Combine(_tempDir, "exclusive.key");
        byte[] secret = RandomNumberGenerator.GetBytes(NodeSecretStore.SecretLengthBytes);

        NodeSecretStore.WriteSecretFileExclusive(path, secret);
        Assert.Equal(secret, File.ReadAllBytes(path));

        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => NodeSecretStore.WriteSecretFileExclusive(path, secret));
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that the loser of a simultaneous first launch adopts the winner's key.
    /// </summary>
    [Fact]
    public void ConcurrentFirstLaunch_LoserAdoptsWinnerKey()
    {
        // WHAT: Exercises the lost-publication-race branch exactly, with no threads and no sleeps.
        // WHY:  Two replicas cold-starting on shared storage must converge on one key; returning our own
        //       generated bytes would split the node and invalidate the other replica's tokens.
        // HOW:  The concurrentWriterHook test seam publishes a competing key at the precise instant between
        //       writing the temporary file and renaming it into place.
        byte[] winnerKey = RandomNumberGenerator.GetBytes(NodeSecretStore.SecretLengthBytes);

        var (key, origin) = NodeSecretStore.LoadOrCreateKeyFile(
            _tempDir,
            NodeSecretStore.TokenSigningKeyFileName,
            path => File.WriteAllBytes(path, winnerKey));

        Assert.Equal(winnerKey, key);
        Assert.Equal(NodeSecretOrigin.ExistingFile, origin);
        Assert.Single(Directory.GetFiles(_tempDir, "*.key"));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    /// <summary>
    /// Verifies that the test seam itself does not change the outcome when no competitor appears.
    /// </summary>
    [Fact]
    public void ConcurrentFirstLaunch_WinnerKeepsOwnKey()
    {
        var (key, origin) = NodeSecretStore.LoadOrCreateKeyFile(
            _tempDir,
            NodeSecretStore.TokenSigningKeyFileName,
            _ => { });

        Assert.Equal(NodeSecretOrigin.GeneratedFile, origin);
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName)));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    /// <summary>
    /// Verifies that a Base64 token signing key supplied through configuration is honoured and persists nothing.
    /// </summary>
    [Fact]
    public void TokenSigningKeyOverride_IsHonouredAndWritesNoFile()
    {
        // WHY: Several replicas behind a load balancer need the same signing key, supplied from the
        //      orchestrator's secret store. The PHI key must still be resolved independently.
        byte[] configuredKey = RandomNumberGenerator.GetBytes(NodeSecretStore.SecretLengthBytes);
        NodeSecretStore store = new(BuildDirectoryConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.TokenSigningKeyConfigKey] = Convert.ToBase64String(configuredKey)
        }));

        Assert.Equal(configuredKey, store.TokenSigningKey.ToArray());
        Assert.Equal(NodeSecretOrigin.ConfiguredOverride, store.TokenSigningKeyOrigin);
        Assert.False(File.Exists(Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName)));
        Assert.True(File.Exists(Path.Combine(_tempDir, NodeSecretStore.PhiMasterKeyFileName)));
        Assert.Equal(NodeSecretOrigin.GeneratedFile, store.PhiMasterKeyOrigin);
    }

    /// <summary>
    /// Verifies that a token signing key override which is not Base64 stops the node.
    /// </summary>
    [Fact]
    public void TokenSigningKeyOverride_NotBase64_Throws()
    {
        NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
            () => new NodeSecretStore(BuildDirectoryConfiguration(new Dictionary<string, string?>
            {
                [NodeSecretStore.TokenSigningKeyConfigKey] = "!!!not base64!!!"
            })));

        Assert.IsType<FormatException>(ex.InnerException);
        Assert.Contains(NodeSecretStore.TokenSigningKeyConfigKey, ex.Message);
    }

    /// <summary>
    /// Verifies that a token signing key override of the wrong length stops the node, too short or too long.
    /// </summary>
    [Fact]
    public void TokenSigningKeyOverride_WrongLength_Throws()
    {
        Assert.Throws<NodeSecretProvisioningException>(
            () => new NodeSecretStore(BuildDirectoryConfiguration(new Dictionary<string, string?>
            {
                [NodeSecretStore.TokenSigningKeyConfigKey] = Convert.ToBase64String(new byte[16])
            })));

        Assert.Throws<NodeSecretProvisioningException>(
            () => new NodeSecretStore(BuildDirectoryConfiguration(new Dictionary<string, string?>
            {
                [NodeSecretStore.TokenSigningKeyConfigKey] = Convert.ToBase64String(new byte[64])
            })));
    }

    /// <summary>
    /// Verifies that a blank token signing key override is ignored in favour of the persisted key file.
    /// </summary>
    [Fact]
    public void TokenSigningKeyOverride_Whitespace_FallsBackToKeyFile()
    {
        NodeSecretStore store = new(BuildDirectoryConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.TokenSigningKeyConfigKey] = "  "
        }));

        Assert.Equal(NodeSecretOrigin.GeneratedFile, store.TokenSigningKeyOrigin);
        Assert.True(File.Exists(Path.Combine(_tempDir, NodeSecretStore.TokenSigningKeyFileName)));
    }

    /// <summary>
    /// Verifies that the historic profile pepper setting still derives the same PHI key it always did.
    /// </summary>
    [Fact]
    public void ProfilePepperOverride_PrimaryConfigurationKey_IsHonoured()
    {
        // WHY: A deployment that already configured a pepper encrypted its patient data with SHA-256 of that
        //      text. Honouring it unchanged is what keeps that data readable after this upgrade.
        const string pepper = "sovereign_test_pepper_key_material_123";
        NodeSecretStore store = new(BuildDirectoryConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.ProfilePepperConfigKey] = pepper
        }));

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(pepper)), store.PhiMasterKey.ToArray());
        Assert.Equal(NodeSecretOrigin.ConfiguredOverride, store.PhiMasterKeyOrigin);
        Assert.False(File.Exists(Path.Combine(_tempDir, NodeSecretStore.PhiMasterKeyFileName)));
    }

    /// <summary>
    /// Verifies that the environment-style pepper configuration key is honoured as the second choice.
    /// </summary>
    [Fact]
    public void ProfilePepperOverride_EnvironmentConfigurationKey_IsHonoured()
    {
        const string pepper = "sovereign_env_pepper_material_456";
        NodeSecretStore store = new(BuildDirectoryConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.ProfilePepperEnvConfigKey] = pepper
        }));

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(pepper)), store.PhiMasterKey.ToArray());
        Assert.Equal(NodeSecretOrigin.ConfiguredOverride, store.PhiMasterKeyOrigin);
    }

    /// <summary>
    /// Verifies that blank pepper settings are ignored in favour of the generated PHI key file.
    /// </summary>
    [Fact]
    public void ProfilePepperOverride_Whitespace_FallsBackToKeyFile()
    {
        NodeSecretStore store = new(BuildDirectoryConfiguration(new Dictionary<string, string?>
        {
            [NodeSecretStore.ProfilePepperConfigKey] = "   ",
            [NodeSecretStore.ProfilePepperEnvConfigKey] = "   "
        }));

        Assert.Equal(NodeSecretOrigin.GeneratedFile, store.PhiMasterKeyOrigin);
        Assert.True(File.Exists(Path.Combine(_tempDir, NodeSecretStore.PhiMasterKeyFileName)));
    }

    /// <summary>
    /// Verifies that the provisioning exception type exposes both of its constructors.
    /// </summary>
    [Fact]
    public void NodeSecretProvisioningException_ExposesMessageAndInnerException()
    {
        NodeSecretProvisioningException simple = new("token_signing.key is unusable.");
        Assert.Equal("token_signing.key is unusable.", simple.Message);
        Assert.Null(simple.InnerException);

        InvalidOperationException cause = new("cause");
        NodeSecretProvisioningException wrapped = new("phi_master.key is unusable.", cause);
        Assert.Same(cause, wrapped.InnerException);
    }
}
