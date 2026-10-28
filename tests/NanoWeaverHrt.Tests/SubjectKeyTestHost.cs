using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Test fixture builder that constructs a real <see cref="NodeSecretStore"/>, <see cref="ProfileDataProtector"/>,
/// <see cref="WeaverDbConnectionFactory"/>, <see cref="SubjectKeyRepository"/>, <see cref="SubjectKeyVault"/>,
/// and <see cref="KeyManagementService"/> over an isolated temporary directory, and supports reopening fresh
/// instances over the same directory to simulate a process restart.
/// </summary>
internal sealed class SubjectKeyTestHost : IDisposable
{
    /// <summary>
    /// Root directory containing the database and node keys for this host.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Optional PostgreSQL connection string when running against a real PostgreSQL test database.
    /// </summary>
    public string? PostgresConnectionString { get; }

    /// <summary>
    /// Configuration bound to <see cref="RootDirectory"/> and <see cref="PostgresConnectionString"/>.
    /// </summary>
    public IConfiguration Configuration { get; }

    /// <summary>
    /// Node secret store holding <c>phi_master.key</c> and <c>token_signing.key</c>.
    /// </summary>
    public NodeSecretStore SecretStore { get; }

    /// <summary>
    /// AES-256-GCM protector keyed by <see cref="SecretStore"/>.
    /// </summary>
    public ProfileDataProtector Protector { get; }

    /// <summary>
    /// Real database connection factory for this host.
    /// </summary>
    public WeaverDbConnectionFactory ConnectionFactory { get; }

    /// <summary>
    /// Real SQL repository for <c>weaver_subject_keys</c>.
    /// </summary>
    public SubjectKeyRepository InnerRepository { get; }

    /// <summary>
    /// Fault-injecting wrapper around <see cref="InnerRepository"/>.
    /// </summary>
    public FaultInjectingSubjectKeyRepository Repository { get; }

    /// <summary>
    /// Post-quantum cryptographic provider.
    /// </summary>
    public PostQuantumCryptoProvider CryptoProvider { get; }

    /// <summary>
    /// External append-only subject erasure journal.
    /// </summary>
    public SubjectErasureJournal Journal { get; }

    /// <summary>
    /// Subject key vault under test.
    /// </summary>
    public SubjectKeyVault Vault { get; }

    /// <summary>
    /// Key management service wired to <see cref="Vault"/>.
    /// </summary>
    public KeyManagementService KeyManagement { get; }

    /// <summary>
    /// Load report returned when <see cref="Vault"/> was initialized, or <c>null</c> if constructed unloaded.
    /// </summary>
    public SubjectKeyLoadReport? LoadReport { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyTestHost"/> class without calling <see cref="SubjectKeyVault.InitializeAsync"/>.
    /// </summary>
    /// <param name="rootDirectory">Temporary root directory for the database and node keys.</param>
    /// <param name="postgresConnectionString">Optional PostgreSQL connection string.</param>
    /// <param name="nodeKeysDirectoryOverride">Optional explicit node-keys directory override.</param>
    public SubjectKeyTestHost(
        string rootDirectory,
        string? postgresConnectionString = null,
        string? nodeKeysDirectoryOverride = null)
    {
        // WHAT: Builds the real cryptographic and database collaborators over an isolated directory.
        // WHY:  Tests must exercise the real NodeSecretStore, ProfileDataProtector, and SQL repository without mocks.
        // HOW:  Configures TRAJECTORY_INCOMING_DIR and WEAVER_NODE_KEYS_DIR inside rootDirectory.
        Directory.CreateDirectory(rootDirectory);
        RootDirectory = rootDirectory;
        PostgresConnectionString = postgresConnectionString;

        string keysDir = nodeKeysDirectoryOverride ?? Path.Combine(rootDirectory, "node-keys");
        Directory.CreateDirectory(keysDir);

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = rootDirectory,
                ["WEAVER_NODE_KEYS_DIR"] = keysDir,
                ["POSTGRES_CONNECTION_STRING"] = postgresConnectionString ?? string.Empty
            })
            .Build();

        SecretStore = new NodeSecretStore(Configuration);
        Protector = new ProfileDataProtector(SecretStore);
        ConnectionFactory = WeaverTestDb.CreateFactory(Configuration, new DatabaseMigrationRunner(protector: Protector));
        InnerRepository = new SubjectKeyRepository(ConnectionFactory);
        Repository = new FaultInjectingSubjectKeyRepository(InnerRepository);
        CryptoProvider = new PostQuantumCryptoProvider();
        Journal = new SubjectErasureJournal(SecretStore);
        Vault = new SubjectKeyVault(Repository, Protector, CryptoProvider, Journal);
        KeyManagement = new KeyManagementService(CryptoProvider, Vault, SecretStore);
    }

    /// <summary>
    /// Creates a new <see cref="SubjectKeyTestHost"/> and synchronously loads its <see cref="SubjectKeyVault"/>.
    /// </summary>
    /// <param name="rootDirectory">Temporary root directory.</param>
    /// <param name="postgresConnectionString">Optional PostgreSQL connection string.</param>
    /// <returns>The initialized <see cref="SubjectKeyTestHost"/>.</returns>
    public static SubjectKeyTestHost CreateLoaded(string rootDirectory, string? postgresConnectionString = null)
    {
        SubjectKeyTestHost host = new(rootDirectory, postgresConnectionString);
        host.LoadReport = host.Vault.InitializeAsync().GetAwaiter().GetResult();
        return host;
    }

    /// <summary>
    /// Creates a new <see cref="SubjectKeyTestHost"/> and asynchronously loads its <see cref="SubjectKeyVault"/>.
    /// </summary>
    /// <param name="rootDirectory">Temporary root directory.</param>
    /// <param name="postgresConnectionString">Optional PostgreSQL connection string.</param>
    /// <returns>The initialized <see cref="SubjectKeyTestHost"/>.</returns>
    public static async Task<SubjectKeyTestHost> CreateLoadedAsync(string rootDirectory, string? postgresConnectionString = null)
    {
        SubjectKeyTestHost host = new(rootDirectory, postgresConnectionString);
        host.LoadReport = await host.Vault.InitializeAsync();
        return host;
    }

    /// <summary>
    /// Disposes the current vault and builds a fresh loaded <see cref="SubjectKeyTestHost"/> over the same root directory and backend to simulate a service restart.
    /// </summary>
    /// <returns>A freshly constructed and initialized <see cref="SubjectKeyTestHost"/>.</returns>
    public async Task<SubjectKeyTestHost> ReopenAsync()
    {
        Vault.Dispose();
        return await CreateLoadedAsync(RootDirectory, PostgresConnectionString);
    }

    /// <summary>
    /// Disposes the underlying <see cref="SubjectKeyVault"/>.
    /// </summary>
    public void Dispose()
    {
        Vault.Dispose();
    }
}
