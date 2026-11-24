using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying <see cref="Migration022_MultiYearHomeostasisForecasts"/>,
/// <see cref="MultiYearHomeostasisRepository"/>, persistence DTO example providers,
/// and GDPR Article 17 patient erasure isolation.
/// </summary>
public sealed class MultiYearHomeostasisRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite test database file.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection factory bound to the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiYearHomeostasisRepositoryTests"/> class.
    /// </summary>
    public MultiYearHomeostasisRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 22 schema migrations applied.
        // WHY:  Guarantees deterministic, side-effect-free multi-year homeostasis repository testing.
        // HOW:  Creates a unique temporary directory and invokes WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_multiyear_repo_{Guid.NewGuid():N}");
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
    /// Verifies <see cref="Migration022_MultiYearHomeostasisForecasts"/> emits valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration022_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        Migration022_MultiYearHomeostasisForecasts migration = new();
        Assert.Equal(22, migration.Version);
        Assert.Equal("022_MultiYearHomeostasisForecasts", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_depot", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_multiyear_checkpoints_run", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("REAL NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_run", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="MultiYearHomeostasisRunRecord"/> and <see cref="MultiYearHomeostasisCheckpointRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModels_GetExamples_ReturnValidPopulatedRecords()
    {
        MultiYearHomeostasisRunRecord run = new MultiYearHomeostasisRunRecord().GetExamples();
        Assert.Equal("myh_20261124_001", run.RunId);
        Assert.Equal(10, run.HorizonYears);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, run.DurabilityStatus);

        MultiYearHomeostasisCheckpointRecord cp = new MultiYearHomeostasisCheckpointRecord().GetExamples();
        Assert.Equal("chk_20261124_y05", cp.CheckpointId);
        Assert.Equal(5, cp.YearIndex);
        Assert.Equal(548.2, cp.EstradiolPmolL);
    }

    /// <summary>
    /// Verifies constructor and method argument validation guards on <see cref="MultiYearHomeostasisRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisRepository(null!));
        MultiYearHomeostasisRepository repo = new(_factory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveForecastRunAsync(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = "r", DepotId = "d", PatientId = "p" }, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = " ", DepotId = "d", PatientId = "p" }, []));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = "r", DepotId = " ", PatientId = "p" }, []));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = "r", DepotId = "d", PatientId = " " }, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = "r", DepotId = "d", PatientId = "p" }, [null!]));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveForecastRunAsync(
            new MultiYearHomeostasisRunRecord { RunId = "r", DepotId = "d", PatientId = "p" },
            [new MultiYearHomeostasisCheckpointRecord { CheckpointId = " " }]));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetRunByIdAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListRunsByDepotAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetLatestRunByDepotAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetCheckpointsByRunAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteByPatientIdAsync(" "));
    }

    /// <summary>
    /// Verifies atomic save, newest-first queries, checkpoint ordering, enum fallback, and GDPR patient erasure isolation.
    /// </summary>
    [Fact]
    public async Task Repository_RoundTripOrderingEnumFallbackAndGdprErasure_WorkDeterministically()
    {
        MultiYearHomeostasisRepository repo = new(_factory);
        DateTimeOffset t0 = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

        Assert.Null(await repo.GetRunByIdAsync("missing-run"));
        Assert.Null(await repo.GetLatestRunByDepotAsync("missing-depot"));

        MultiYearHomeostasisRunRecord runOld = new()
        {
            RunId = "run-1",
            DepotId = "depot-a",
            CircuitId = null,
            PatientId = "pat-myh-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            HorizonYears = 10,
            MitoticFidelityRate = 0.999,
            TransducedProgenitorFraction = 0.88,
            ProjectedTenYearTtrPercent = 98.2,
            DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
            CreatedAtUtc = t0
        };

        MultiYearHomeostasisRunRecord runNew = new()
        {
            RunId = "run-2",
            DepotId = "depot-a",
            CircuitId = "circ-7",
            PatientId = "pat-myh-1",
            Regimen = TherapeuticRegimen.FemaleToMale,
            HorizonYears = 5,
            MitoticFidelityRate = 0.985,
            TransducedProgenitorFraction = 0.60,
            ProjectedTenYearTtrPercent = 72.5,
            DurabilityStatus = MultiYearDurabilityStatus.MitoticDriftAdvisory,
            CreatedAtUtc = t0.AddHours(2)
        };

        MultiYearHomeostasisRunRecord otherPatientRun = new()
        {
            RunId = "run-other",
            DepotId = "depot-b",
            CircuitId = "circ-other",
            PatientId = "pat-myh-2",
            Regimen = TherapeuticRegimen.MaleToFemale,
            HorizonYears = 10,
            MitoticFidelityRate = 0.9985,
            TransducedProgenitorFraction = 0.85,
            ProjectedTenYearTtrPercent = 97.6,
            DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
            CreatedAtUtc = t0
        };

        await repo.SaveForecastRunAsync(runOld,
        [
            new MultiYearHomeostasisCheckpointRecord
            {
                CheckpointId = "cp-1-y1",
                RunId = "run-1",
                PatientId = "pat-myh-1",
                YearIndex = 1,
                EstradiolPmolL = 540.0,
                TestosteroneNmolL = 1.0,
                LhIuL = 0.4,
                FshIuL = 0.7,
                MethylatedProgenitorFraction = 0.87,
                HydrogelContributionFraction = 0.01
            },
            new MultiYearHomeostasisCheckpointRecord
            {
                CheckpointId = "cp-1-y0",
                RunId = "run-1",
                PatientId = "pat-myh-1",
                YearIndex = 0,
                EstradiolPmolL = 120.0,
                TestosteroneNmolL = 18.0,
                LhIuL = 6.5,
                FshIuL = 5.0,
                MethylatedProgenitorFraction = 0.88,
                HydrogelContributionFraction = 0.0
            }
        ]);
        await repo.SaveForecastRunAsync(runNew, []);
        await repo.SaveForecastRunAsync(otherPatientRun,
        [
            new MultiYearHomeostasisCheckpointRecord
            {
                CheckpointId = "cp-other-y0",
                RunId = "run-other",
                PatientId = "pat-myh-2",
                YearIndex = 0,
                EstradiolPmolL = 520.0,
                TestosteroneNmolL = 1.1,
                LhIuL = 0.5,
                FshIuL = 0.8,
                MethylatedProgenitorFraction = 0.85,
                HydrogelContributionFraction = 0.0
            }
        ]);

        IReadOnlyList<MultiYearHomeostasisRunRecord> listed = await repo.ListRunsByDepotAsync("depot-a");
        Assert.Equal(2, listed.Count);
        Assert.Equal("run-2", listed[0].RunId);
        Assert.Equal("circ-7", listed[0].CircuitId);
        Assert.Equal("run-1", listed[1].RunId);
        Assert.Null(listed[1].CircuitId);

        MultiYearHomeostasisRunRecord? latest = await repo.GetLatestRunByDepotAsync("depot-a");
        Assert.NotNull(latest);
        Assert.Equal("run-2", latest.RunId);
        Assert.Equal(MultiYearDurabilityStatus.MitoticDriftAdvisory, latest.DurabilityStatus);

        MultiYearHomeostasisRunRecord? loadedOld = await repo.GetRunByIdAsync("run-1");
        Assert.NotNull(loadedOld);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, loadedOld.DurabilityStatus);

        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> checkpoints = await repo.GetCheckpointsByRunAsync("run-1");
        Assert.Equal(2, checkpoints.Count);
        Assert.Equal(0, checkpoints[0].YearIndex);
        Assert.Equal(1, checkpoints[1].YearIndex);

        // Exercise corrupted raw enum fallback cleanly
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_multiyear_homeostasis_runs SET durability_status = 'CorruptStatus', regimen = 'UnknownRegimen' WHERE run_id = 'run-2'");
            await cmd.ExecuteNonQueryAsync();
        }

        MultiYearHomeostasisRunRecord? fallbackLoaded = await repo.GetRunByIdAsync("run-2");
        Assert.NotNull(fallbackLoaded);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, fallbackLoaded.DurabilityStatus);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, fallbackLoaded.Regimen);

        // GDPR Article 17 erasure removes pat-myh-1 (2 checkpoints + 2 runs = 4) and leaves pat-myh-2 intact
        int deleted = await repo.DeleteByPatientIdAsync("pat-myh-1");
        Assert.Equal(4, deleted);
        Assert.Empty(await repo.ListRunsByDepotAsync("depot-a"));
        Assert.Empty(await repo.GetCheckpointsByRunAsync("run-1"));
        Assert.NotNull(await repo.GetRunByIdAsync("run-other"));
        Assert.Single(await repo.GetCheckpointsByRunAsync("run-other"));
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
