using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying <see cref="Migration018_FederatedPeerSyncState"/>,
/// <see cref="FederatedPeerSyncRepository"/>, and <see cref="FederatedPeerSyncEndpointRecord"/> models.
/// </summary>
public sealed class FederatedPeerSyncRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding this test run's SQLite database file.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection factory bound to the isolated SQLite database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedPeerSyncRepositoryTests"/> class.
    /// </summary>
    public FederatedPeerSyncRepositoryTests()
    {
        // WHAT: Creates an isolated temp SQLite database and applies all 18 built-in schema migrations.
        // WHY:  Prevents state leakage between concurrent or sequential repository tests.
        // HOW:  Allocates a unique temp folder and uses WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_fed_sync_repo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();
        _factory = WeaverTestDb.CreateFactory(config);
    }

    /// <summary>
    /// Verifies that <see cref="Migration018_FederatedPeerSyncState"/> generates dialect-specific SQL
    /// for SQLite, PostgreSQL, DuckDB, MySQL, and MariaDB and rejects null dialects.
    /// </summary>
    [Fact]
    public void Migration018_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        Migration018_FederatedPeerSyncState migration = new();
        Assert.Equal(18, migration.Version);
        Assert.Equal("018_FederatedPeerSyncState", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_fed_sync_enabled", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_fed_sync_pinned_key", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("TINYINT(1)", mysql, StringComparison.Ordinal);
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX idx_fed_sync_enabled", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("TEXT    PRIMARY KEY", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_fed_sync_enabled", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI <c>GetExamples()</c> providers across all Milestone 39 DTO models.
    /// </summary>
    [Fact]
    public void Models_GetExamples_ReturnPopulatedValidSamples()
    {
        FederatedPeerSyncEndpointRecord endpoint = new FederatedPeerSyncEndpointRecord().GetExamples();
        Assert.Equal("clinic-zurich-01", endpoint.PeerId);
        Assert.Equal(FederatedPeerSyncStatus.Succeeded, endpoint.LastStatus);
        Assert.Equal(4, endpoint.TotalImportedCount);

        UpsertFederatedPeerSyncEndpointRequest request = new UpsertFederatedPeerSyncEndpointRequest().GetExamples();
        Assert.Equal("clinic-zurich-01", request.PeerId);
        Assert.False(request.AllowLoopbackHttp);

        FederatedPeerSyncRunResult run = new FederatedPeerSyncRunResult().GetExamples();
        Assert.True(run.EndpointFound);
        Assert.True(run.Succeeded);
        Assert.NotNull(run.Endpoint);

        FederatedPeerSyncSweepResult sweep = new FederatedPeerSyncSweepResult().GetExamples();
        Assert.Equal(1, sweep.PeersSucceeded);
        Assert.Single(sweep.Results);

        PeerEndpointValidationResult validation = new PeerEndpointValidationResult().GetExamples();
        Assert.True(validation.IsValid);
        Assert.Null(validation.Error);
    }

    /// <summary>
    /// Verifies constructor and method argument validation guards on <see cref="FederatedPeerSyncRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncRepository(null!));

        FederatedPeerSyncRepository repo = new(_factory);
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertEndpointAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = " ",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = "key-1",
            SyncIntervalMinutes = 15
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-1",
            DisplayName = " ",
            BaseUrl = "https://lab.example",
            PinnedKeyId = "key-1",
            SyncIntervalMinutes = 15
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = " ",
            PinnedKeyId = "key-1",
            SyncIntervalMinutes = 15
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = " ",
            SyncIntervalMinutes = 15
        }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = "key-1",
            SyncIntervalMinutes = 0
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetEndpointAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteEndpointAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.RecordSyncOutcomeAsync(" ", DateTimeOffset.UtcNow, null, FederatedPeerSyncStatus.Failed, "err", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.RecordSyncOutcomeAsync("peer-1", DateTimeOffset.UtcNow, null, FederatedPeerSyncStatus.Failed, "err", -1));
    }

    /// <summary>
    /// Verifies end-to-end CRUD, conflict upsert telemetry preservation, outcome recording,
    /// enabled filtering, and unknown-status fallback mapping.
    /// </summary>
    [Fact]
    public async Task Repository_CrudOutcomeAndTelemetryPreservation_WorkDeterministically()
    {
        FederatedPeerSyncRepository repo = new(_factory);
        DateTimeOffset t0 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset t1 = t0.AddMinutes(15);
        DateTimeOffset t2 = t0.AddMinutes(30);
        DateTimeOffset t3 = t0.AddMinutes(45);

        Assert.Null(await repo.GetEndpointAsync("missing-peer"));
        Assert.False(await repo.DeleteEndpointAsync("missing-peer"));
        Assert.Null(await repo.RecordSyncOutcomeAsync("missing-peer", t1, t1, FederatedPeerSyncStatus.Succeeded, null, 1));

        // Insert enabled peer and disabled peer
        FederatedPeerSyncEndpointRecord inserted = await repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-a",
            DisplayName = "Lab Alpha",
            BaseUrl = "https://alpha.lantea.one",
            PinnedKeyId = "key-alpha",
            Enabled = true,
            SyncIntervalMinutes = 15,
            AllowLoopbackHttp = false,
            LastStatus = FederatedPeerSyncStatus.Pending,
            CreatedAtUtc = t0,
            UpdatedAtUtc = t0
        });
        Assert.Equal("peer-a", inserted.PeerId);
        Assert.Equal(FederatedPeerSyncStatus.Pending, inserted.LastStatus);
        Assert.Null(inserted.LastAttemptUtc);
        Assert.Null(inserted.LastSuccessUtc);
        Assert.Null(inserted.LastError);

        await repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-b",
            DisplayName = "Lab Beta",
            BaseUrl = "http://127.0.0.1:5055",
            PinnedKeyId = "key-beta",
            Enabled = false,
            SyncIntervalMinutes = 30,
            AllowLoopbackHttp = true,
            LastStatus = FederatedPeerSyncStatus.Pending,
            CreatedAtUtc = t0,
            UpdatedAtUtc = t0
        });

        IReadOnlyList<FederatedPeerSyncEndpointRecord> all = await repo.ListEndpointsAsync(enabledOnly: false);
        Assert.Equal(2, all.Count);
        Assert.Equal("peer-a", all[0].PeerId);
        Assert.Equal("peer-b", all[1].PeerId);

        IReadOnlyList<FederatedPeerSyncEndpointRecord> enabledOnly = await repo.ListEndpointsAsync(enabledOnly: true);
        Assert.Single(enabledOnly);
        Assert.Equal("peer-a", enabledOnly[0].PeerId);

        // Record successful sync outcome
        FederatedPeerSyncEndpointRecord? afterSuccess = await repo.RecordSyncOutcomeAsync(
            "peer-a", t1, t1, FederatedPeerSyncStatus.Succeeded, null, 2);
        Assert.NotNull(afterSuccess);
        Assert.Equal(FederatedPeerSyncStatus.Succeeded, afterSuccess.LastStatus);
        Assert.Equal(t1, afterSuccess.LastAttemptUtc);
        Assert.Equal(t1, afterSuccess.LastSuccessUtc);
        Assert.Null(afterSuccess.LastError);
        Assert.Equal(2, afterSuccess.LastImportedCount);
        Assert.Equal(2, afterSuccess.TotalImportedCount);

        // Record failed sync outcome — must preserve last_success_utc = t1 and total_imported_count = 2
        FederatedPeerSyncEndpointRecord? afterFail = await repo.RecordSyncOutcomeAsync(
            "peer-a", t2, null, FederatedPeerSyncStatus.Failed, "HTTP 503", 0);
        Assert.NotNull(afterFail);
        Assert.Equal(FederatedPeerSyncStatus.Failed, afterFail.LastStatus);
        Assert.Equal(t2, afterFail.LastAttemptUtc);
        Assert.Equal(t1, afterFail.LastSuccessUtc);
        Assert.Equal("HTTP 503", afterFail.LastError);
        Assert.Equal(0, afterFail.LastImportedCount);
        Assert.Equal(2, afterFail.TotalImportedCount);

        // Config upsert conflict — must update config fields while preserving created_at_utc and sync telemetry
        FederatedPeerSyncEndpointRecord updated = await repo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "peer-a",
            DisplayName = "Lab Alpha Renamed",
            BaseUrl = "https://alpha-v2.lantea.one",
            PinnedKeyId = "key-alpha-v2",
            Enabled = true,
            SyncIntervalMinutes = 60,
            AllowLoopbackHttp = false,
            LastStatus = FederatedPeerSyncStatus.Pending,
            CreatedAtUtc = t3,
            UpdatedAtUtc = t3
        });
        Assert.Equal("Lab Alpha Renamed", updated.DisplayName);
        Assert.Equal("https://alpha-v2.lantea.one", updated.BaseUrl);
        Assert.Equal("key-alpha-v2", updated.PinnedKeyId);
        Assert.Equal(60, updated.SyncIntervalMinutes);
        Assert.Equal(t0, updated.CreatedAtUtc);
        Assert.Equal(t3, updated.UpdatedAtUtc);
        Assert.Equal(t1, updated.LastSuccessUtc);
        Assert.Equal(2, updated.TotalImportedCount);
        Assert.Equal("HTTP 503", updated.LastError);

        // Unknown raw status string falls back cleanly to Pending
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_federated_peer_sync_endpoints SET last_status = 'CorruptedValue' WHERE peer_id = 'peer-b'");
            await cmd.ExecuteNonQueryAsync();
        }

        FederatedPeerSyncEndpointRecord? fallbackRow = await repo.GetEndpointAsync("peer-b");
        Assert.NotNull(fallbackRow);
        Assert.Equal(FederatedPeerSyncStatus.Pending, fallbackRow.LastStatus);

        // Delete works once and returns false on repeat
        Assert.True(await repo.DeleteEndpointAsync("peer-b"));
        Assert.False(await repo.DeleteEndpointAsync("peer-b"));
        Assert.Null(await repo.GetEndpointAsync("peer-b"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
