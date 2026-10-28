using System.Globalization;
using Microsoft.Data.Sqlite;
using NanoWeaverHrt.Client.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Client;

/// <summary>
/// Comprehensive unit tests verifying SQLite instance profile persistence, schema initialization,
/// input validation, URI boundary gating, username normalization, and CRUD lifecycle operations.
/// </summary>
public sealed class SqliteInstanceProfileRepositoryTests : IDisposable
{
    /// <summary>
    /// Temporary SQLite database file path.
    /// </summary>
    private readonly string _tempDbFile;

    /// <summary>
    /// SQLite connection string for the isolated test database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Repository instance under test.
    /// </summary>
    private readonly SqliteInstanceProfileRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteInstanceProfileRepositoryTests"/> class.
    /// </summary>
    public SqliteInstanceProfileRepositoryTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"weaver_client_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbFile}";
        _repository = new SqliteInstanceProfileRepository(_connectionString);
    }

    /// <summary>
    /// Cleans up the test database file and clears the SQLite connection pool for this database.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Clears connection pool for the specific test connection string and deletes the database file.
        // WHY:  Prime Directive mandates per-connection pool clearing (SqliteConnection.ClearPool) to avoid test cross-talk.
        // HOW:  Opens a SqliteConnection instance, calls ClearPool, and deletes the temporary file.
        using (SqliteConnection conn = new(_connectionString))
        {
            SqliteConnection.ClearPool(conn);
        }

        if (File.Exists(_tempDbFile))
        {
            try
            {
                File.Delete(_tempDbFile);
            }
            catch
            {
                // Best-effort cleanup for transient OS file locks.
            }
        }
    }

    /// <summary>
    /// Verifies constructor argument validation on <see cref="SqliteInstanceProfileRepository"/>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidConnectionString_ThrowsArgumentException(string? invalidConnStr)
    {
        // WHAT: Validates constructor rejecting null, empty, or whitespace connection strings.
        // WHY:  Guarantees fail-fast behavior on misconfigured persistence layers.
        // HOW:  Asserts ArgumentException on invalid connection string values.
        Assert.ThrowsAny<ArgumentException>(() => new SqliteInstanceProfileRepository(invalidConnStr!));
    }

    /// <summary>
    /// Verifies schema initialization creates the tables idempotently.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task InitializeAsync_ExecutedMultipleTimes_SucceedsIdempotently()
    {
        // WHAT: Calls InitializeAsync twice on the same database.
        // WHY:  Schema creation must be idempotent across application restarts.
        // HOW:  Awaits InitializeAsync consecutively without exception.
        await _repository.InitializeAsync();
        await _repository.InitializeAsync();

        IReadOnlyList<InstanceProfile> profiles = await _repository.ListProfilesAsync();
        Assert.Empty(profiles);
    }

    /// <summary>
    /// Verifies that <see cref="IInstanceProfileRepository.GetProfileAsync"/> validates its input argument.
    /// </summary>
    /// <param name="invalidId">The invalid instance identifier.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetProfileAsync_InvalidInstanceId_ThrowsArgumentException(string? invalidId)
    {
        // WHAT: Validates GetProfileAsync rejects null or whitespace identifiers.
        // WHY:  Prevents invalid query execution.
        // HOW:  Asserts ArgumentException on invalid inputs.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.GetProfileAsync(invalidId!));
    }

    /// <summary>
    /// Verifies that <see cref="IInstanceProfileRepository.UpdateLastConnectedAsync"/> validates its identifier argument.
    /// </summary>
    /// <param name="invalidId">The invalid instance identifier.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateLastConnectedAsync_InvalidInstanceId_ThrowsArgumentException(string? invalidId)
    {
        // WHAT: Validates UpdateLastConnectedAsync rejects null or whitespace identifiers.
        // WHY:  Prevents invalid database updates.
        // HOW:  Asserts ArgumentException on invalid inputs.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpdateLastConnectedAsync(invalidId!, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Verifies that <see cref="IInstanceProfileRepository.DeleteProfileAsync"/> validates its identifier argument.
    /// </summary>
    /// <param name="invalidId">The invalid instance identifier.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteProfileAsync_InvalidInstanceId_ThrowsArgumentException(string? invalidId)
    {
        // WHAT: Validates DeleteProfileAsync rejects null or whitespace identifiers.
        // WHY:  Prevents invalid database deletes.
        // HOW:  Asserts ArgumentException on invalid inputs.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.DeleteProfileAsync(invalidId!));
    }

    /// <summary>
    /// Verifies that <see cref="IInstanceProfileRepository.UpsertProfileAsync"/> validates required string fields.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UpsertProfileAsync_NullProfile_ThrowsArgumentNullException()
    {
        // WHAT: Asserts null profile throws ArgumentNullException.
        // WHY:  Enforces non-null parameter contract.
        // HOW:  Calls UpsertProfileAsync with null argument.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.UpsertProfileAsync(null!));
    }

    /// <summary>
    /// Verifies that <see cref="IInstanceProfileRepository.UpsertProfileAsync"/> validates empty string fields.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UpsertProfileAsync_InvalidFields_ThrowsArgumentException()
    {
        // WHAT: Tests various missing fields in InstanceProfile.
        // WHY:  Enforces data integrity before touching database.
        // HOW:  Creates profiles with empty/whitespace fields and asserts ArgumentException.
        InstanceProfile valid = CreateValidProfile("node-1");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { InstanceId = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { DisplayName = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { Username = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { Role = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { KeyAlgorithm = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { PublicKeyPem = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(valid with { EncryptedPrivateKeyEnvelopeBase64 = " " }));
    }

    /// <summary>
    /// Verifies URI boundary validation enforcing HTTPS or loopback HTTP (Prime Directive 9).
    /// </summary>
    /// <param name="invalidUri">The invalid base URI string.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-uri")]
    [InlineData("relative/path/not/absolute")]
    [InlineData("ftp://sovereign.local:5000")]
    [InlineData("http://remote-cluster.example.org:5000")]
    [InlineData("http://192.168.1.50:5000")]
    public async Task UpsertProfileAsync_InvalidOrInsecureUri_ThrowsArgumentException(string? invalidUri)
    {
        // WHAT: Validates BaseUri must be HTTPS or loopback HTTP.
        // WHY:  Prime Directive 9 prohibits unencrypted traffic across non-loopback networks.
        // HOW:  Passes insecure or malformed URIs and asserts ArgumentException.
        InstanceProfile profile = CreateValidProfile("node-uri-test") with { BaseUri = invalidUri! };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _repository.UpsertProfileAsync(profile));
    }

    /// <summary>
    /// Verifies valid URI combinations (HTTPS remote and loopback HTTP).
    /// </summary>
    /// <param name="validUri">Valid HTTPS or loopback HTTP URI.</param>
    /// <param name="expectedLocal">Expected value of IsLocalInstance.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("https://node.example.org:5000", false)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://[::1]:5000", true)]
    public async Task UpsertProfileAsync_ValidUris_SucceedsWithExpectedLocalFlag(string validUri, bool expectedLocal)
    {
        // WHAT: Persists profiles with HTTPS and loopback HTTP URIs.
        // WHY:  Loopback endpoints must be marked as local; remote HTTPS endpoints preserve configured locality.
        // HOW:  Initializes repository, upserts profile, reads back, and asserts IsLocalInstance flag.
        await _repository.InitializeAsync();
        string instanceId = "uri-" + Guid.NewGuid().ToString("N");
        InstanceProfile profile = CreateValidProfile(instanceId) with { BaseUri = validUri, IsLocalInstance = false };

        await _repository.UpsertProfileAsync(profile);

        InstanceProfile? retrieved = await _repository.GetProfileAsync(instanceId);
        Assert.NotNull(retrieved);
        Assert.Equal(expectedLocal, retrieved.IsLocalInstance);
    }

    /// <summary>
    /// Verifies username normalization and interval clamping logic.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UpsertProfileAsync_NormalizesUsernameAndClampsInterval()
    {
        // WHAT: Upserts profiles with mixed-case username and out-of-bounds intervals.
        // WHY:  Guarantees username normalization to lowercase and bounds polling intervals to [5, 300].
        // HOW:  Upserts profile with username " MixedCaseUser " and interval 2; asserts normalized to "mixedcaseuser" and 5.
        await _repository.InitializeAsync();

        InstanceProfile under = CreateValidProfile("under-clamp") with
        {
            Username = " MixedCaseUser ",
            PollingIntervalSeconds = 2
        };

        await _repository.UpsertProfileAsync(under);

        InstanceProfile? retrievedUnder = await _repository.GetProfileAsync("under-clamp");
        Assert.NotNull(retrievedUnder);
        Assert.Equal("mixedcaseuser", retrievedUnder.Username);
        Assert.Equal(5, retrievedUnder.PollingIntervalSeconds);

        InstanceProfile over = CreateValidProfile("over-clamp") with
        {
            Username = "OTHER_USER",
            PollingIntervalSeconds = 999
        };

        await _repository.UpsertProfileAsync(over);

        InstanceProfile? retrievedOver = await _repository.GetProfileAsync("over-clamp");
        Assert.NotNull(retrievedOver);
        Assert.Equal("other_user", retrievedOver.Username);
        Assert.Equal(300, retrievedOver.PollingIntervalSeconds);
    }

    /// <summary>
    /// Verifies full CRUD lifecycle, including upsert conflict update and last connected tracking.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FullCrudLifecycle_InsertsUpdatesListsAndDeletesCorrectly()
    {
        // WHAT: Executes full CRUD lifecycle on multiple instance profiles.
        // WHY:  Guarantees repository faithfully manages persistent multi-instance state.
        // HOW:  Inserts profiles, queries by ID and list, updates conflict, updates last connected, and deletes.
        await _repository.InitializeAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        InstanceProfile profileBeta = CreateValidProfile("node-beta") with
        {
            DisplayName = "Beta Node",
            NotificationsEnabled = false,
            LastConnectedAtUtc = null
        };
        InstanceProfile profileAlpha = CreateValidProfile("node-alpha") with
        {
            DisplayName = "Alpha Node",
            NotificationsEnabled = true,
            LastConnectedAtUtc = now
        };

        await _repository.UpsertProfileAsync(profileBeta);
        await _repository.UpsertProfileAsync(profileAlpha);

        // List ordering check (Alpha Node before Beta Node)
        IReadOnlyList<InstanceProfile> list = await _repository.ListProfilesAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("node-alpha", list[0].InstanceId);
        Assert.Equal("Alpha Node", list[0].DisplayName);
        Assert.True(list[0].NotificationsEnabled);
        Assert.NotNull(list[0].LastConnectedAtUtc);

        Assert.Equal("node-beta", list[1].InstanceId);
        Assert.Equal("Beta Node", list[1].DisplayName);
        Assert.False(list[1].NotificationsEnabled);
        Assert.Null(list[1].LastConnectedAtUtc);

        // Get single profile
        InstanceProfile? retrieved = await _repository.GetProfileAsync("node-alpha");
        Assert.NotNull(retrieved);
        Assert.Equal("Alpha Node", retrieved.DisplayName);

        InstanceProfile? nonExistent = await _repository.GetProfileAsync("does-not-exist");
        Assert.Null(nonExistent);

        // Upsert update on conflict
        InstanceProfile updatedAlpha = profileAlpha with
        {
            DisplayName = "Alpha Node Updated",
            NotificationsEnabled = false,
            PollingIntervalSeconds = 45
        };
        await _repository.UpsertProfileAsync(updatedAlpha);

        InstanceProfile? reRetrieved = await _repository.GetProfileAsync("node-alpha");
        Assert.NotNull(reRetrieved);
        Assert.Equal("Alpha Node Updated", reRetrieved.DisplayName);
        Assert.False(reRetrieved.NotificationsEnabled);
        Assert.Equal(45, reRetrieved.PollingIntervalSeconds);

        // UpdateLastConnectedAsync on existing vs non-existing
        DateTimeOffset connectedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        bool updatedConnected = await _repository.UpdateLastConnectedAsync("node-beta", connectedAt);
        Assert.True(updatedConnected);

        InstanceProfile? retrievedBeta = await _repository.GetProfileAsync("node-beta");
        Assert.NotNull(retrievedBeta);
        Assert.NotNull(retrievedBeta.LastConnectedAtUtc);
        Assert.Equal(connectedAt.ToUnixTimeSeconds(), retrievedBeta.LastConnectedAtUtc.Value.ToUnixTimeSeconds());

        bool updatedMissing = await _repository.UpdateLastConnectedAsync("node-missing", connectedAt);
        Assert.False(updatedMissing);

        // Delete existing vs non-existing
        bool deleted = await _repository.DeleteProfileAsync("node-beta");
        Assert.True(deleted);

        bool deletedAgain = await _repository.DeleteProfileAsync("node-beta");
        Assert.False(deletedAgain);

        IReadOnlyList<InstanceProfile> remaining = await _repository.ListProfilesAsync();
        Assert.Single(remaining);
        Assert.Equal("node-alpha", remaining[0].InstanceId);
    }

    /// <summary>
    /// Creates a valid default <see cref="InstanceProfile"/> fixture for testing.
    /// </summary>
    /// <param name="instanceId">The identifier to assign to the test profile.</param>
    /// <returns>A valid initialized <see cref="InstanceProfile"/> instance.</returns>
    private static InstanceProfile CreateValidProfile(string instanceId)
    {
        // WHAT: Builds a standard test instance profile with valid field values.
        // WHY:  Reduces fixture boilerplate in unit tests while guaranteeing valid base invariants.
        // HOW:  Returns a populated InstanceProfile record with localhost HTTP URI and Ed25519 defaults.
        return new InstanceProfile(
            InstanceId: instanceId,
            DisplayName: "Test Instance " + instanceId,
            BaseUri: "http://localhost:5000",
            IsLocalInstance: true,
            Username: "researcher_user",
            Role: "Researcher",
            KeyAlgorithm: "Ed25519",
            PublicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=\n-----END PUBLIC KEY-----\n",
            EncryptedPrivateKeyEnvelopeBase64: Convert.ToBase64String(new byte[64]),
            NotificationsEnabled: true,
            PollingIntervalSeconds: 15,
            AddedAtUtc: DateTimeOffset.UtcNow,
            LastConnectedAtUtc: null);
    }
}
