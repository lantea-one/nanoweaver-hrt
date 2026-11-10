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
/// Unit and integration tests for <see cref="Migration013_TimeReleaseDepotFormulations"/> and <see cref="DepotFormulationRepository"/>.
/// </summary>
public sealed class DepotFormulationRepositoryTests : IDisposable
{
    /// <summary>
    /// Temporary directory hosting the isolated test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// File path to the isolated SQLite database.
    /// </summary>
    private readonly string _dbPath;

    /// <summary>
    /// Connection string for the isolated test database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Database connection factory under test.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Depot formulation repository under test.
    /// </summary>
    private readonly DepotFormulationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotFormulationRepositoryTests"/> class.
    /// </summary>
    public DepotFormulationRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_depot_repo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "weaver_config.db");
        _connectionString = $"Data Source={_dbPath}";

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        _connectionFactory = WeaverTestDb.CreateFactory(config);
        _repository = new DepotFormulationRepository(_connectionFactory);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        using (SqliteConnection conn = new(_connectionString))
        {
            SqliteConnection.ClearPool(conn);
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies domain models instantiate, assign properties, and generate OpenAPI documentation examples.
    /// </summary>
    [Fact]
    public void DomainModels_InstantiationAndExampleProviders()
    {
        // WHAT: Tests property accessors and OpenAPI example generators for DepotFormulationRecord and DepotSchedulePoint.
        // WHY:  Ensures full coverage of model properties and Swagger documentation vectors without state machine regressions.
        // HOW:  Instantiates records with default and custom values, and validates GetExamples outputs.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DepotFormulationRecord record = new()
        {
            DepotId = "depot_custom",
            PatientId = "patient_bob",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "Alginate-Chitosan",
            InitialNanocarrierMassMg = 250.0,
            DailyErosionRatePercent = 0.42,
            ReleaseKineticsModel = "Coupled-Fickian",
            TargetHorizonDays = 90,
            CreatedAtUtc = now
        };

        Assert.Equal("depot_custom", record.DepotId);
        Assert.Equal("patient_bob", record.PatientId);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, record.Regimen);
        Assert.Equal("Alginate-Chitosan", record.PolymerMatrixType);
        Assert.Equal(250.0, record.InitialNanocarrierMassMg);
        Assert.Equal(0.42, record.DailyErosionRatePercent);
        Assert.Equal("Coupled-Fickian", record.ReleaseKineticsModel);
        Assert.Equal(90, record.TargetHorizonDays);
        Assert.Equal(now, record.CreatedAtUtc);

        DepotFormulationRecord defaultRecord = new();
        Assert.Equal(string.Empty, defaultRecord.DepotId);
        Assert.Equal(string.Empty, defaultRecord.PatientId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, defaultRecord.Regimen);
        Assert.Equal("PLGA-PEG-PLGA", defaultRecord.PolymerMatrixType);
        Assert.Equal(0.0, defaultRecord.InitialNanocarrierMassMg);
        Assert.Equal(0.0, defaultRecord.DailyErosionRatePercent);
        Assert.Equal("Korsmeyer-Peppas", defaultRecord.ReleaseKineticsModel);
        Assert.Equal(180, defaultRecord.TargetHorizonDays);

        DepotFormulationRecord example = record.GetExamples();
        Assert.NotNull(example);
        Assert.False(string.IsNullOrWhiteSpace(example.DepotId));
        Assert.False(string.IsNullOrWhiteSpace(example.PatientId));
        Assert.True(example.InitialNanocarrierMassMg > 0);

        DepotSchedulePoint point = new()
        {
            ScheduleId = "pt_1",
            DepotId = "depot_custom",
            TimepointDay = 15,
            CumulativeReleaseFraction = 0.08,
            InstantaneousRateMgDay = 0.75,
            RemainingReserveMg = 230.0
        };

        Assert.Equal("pt_1", point.ScheduleId);
        Assert.Equal("depot_custom", point.DepotId);
        Assert.Equal(15, point.TimepointDay);
        Assert.Equal(0.08, point.CumulativeReleaseFraction);
        Assert.Equal(0.75, point.InstantaneousRateMgDay);
        Assert.Equal(230.0, point.RemainingReserveMg);

        DepotSchedulePoint defaultPoint = new();
        Assert.Equal(string.Empty, defaultPoint.ScheduleId);
        Assert.Equal(string.Empty, defaultPoint.DepotId);
        Assert.Equal(0, defaultPoint.TimepointDay);

        DepotSchedulePoint pointExample = point.GetExamples();
        Assert.NotNull(pointExample);
        Assert.False(string.IsNullOrWhiteSpace(pointExample.ScheduleId));
        Assert.False(string.IsNullOrWhiteSpace(pointExample.DepotId));
        Assert.True(pointExample.TimepointDay > 0);
    }

    /// <summary>
    /// Verifies migration 013 metadata, null guards, and SQL generation across all supported database dialects.
    /// </summary>
    [Fact]
    public void Migration013_MetadataAndDialectValidation()
    {
        // WHAT: Tests Migration013 properties and dialect-specific SQL output.
        // WHY:  Ensures the migration adheres to engine dialect specifications (PostgreSQL, DuckDB, MySQL, SQLite).
        // HOW:  Asserts Version, Name, ArgumentNullException on null, and dialect SQL strings.
        Migration013_TimeReleaseDepotFormulations migration = new();
        Assert.Equal(13L, migration.Version);
        Assert.Equal("013_TimeReleaseDepotFormulations", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pgSql = migration.GetSql("postgresql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_formulations", pgSql);
        Assert.Contains("DOUBLE PRECISION", pgSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules", pgSql);
        Assert.Contains("idx_depot_patient_id", pgSql);
        Assert.Contains("idx_schedule_depot_id", pgSql);

        string duckSql = migration.GetSql("duckdb");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_formulations", duckSql);
        Assert.Contains("DOUBLE PRECISION", duckSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules", duckSql);

        string mysqlSql = migration.GetSql("mysql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_formulations", mysqlSql);
        Assert.Contains("DOUBLE", mysqlSql);
        Assert.Contains("CREATE INDEX idx_depot_patient_id", mysqlSql);
        Assert.Contains("FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE", mysqlSql);

        string mariadbSql = migration.GetSql("mariadb");
        Assert.Equal(mysqlSql, mariadbSql);

        string sqliteSql = migration.GetSql("sqlite");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_formulations", sqliteSql);
        Assert.Contains("TEXT PRIMARY KEY", sqliteSql);
        Assert.Contains("REAL NOT NULL", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules", sqliteSql);
    }

    /// <summary>
    /// Verifies constructor guard against a null connection factory.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsOnNullFactory()
    {
        // WHAT: Tests constructor null parameter guard.
        // WHY:  Guarantees repository cannot be instantiated without a valid connection factory.
        // HOW:  Asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new DepotFormulationRepository(null!));
    }

    /// <summary>
    /// Verifies input parameter validation across all repository methods.
    /// </summary>
    [Fact]
    public async Task ValidationGuards_ThrowOnInvalidArguments()
    {
        // WHAT: Tests argument validation on all repository methods.
        // WHY:  Guarantees fail-fast behavior on missing, null, or whitespace arguments.
        // HOW:  Invokes repository methods with invalid values and asserts ArgumentNullException / ArgumentException.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.UpsertDepotFormulationAsync(null!));

        DepotFormulationRecord emptyDepotId = new()
        {
            DepotId = "",
            PatientId = "patient_1"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertDepotFormulationAsync(emptyDepotId));

        DepotFormulationRecord whitespaceDepotId = new()
        {
            DepotId = "   ",
            PatientId = "patient_1"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertDepotFormulationAsync(whitespaceDepotId));

        DepotFormulationRecord emptyPatientId = new()
        {
            DepotId = "depot_1",
            PatientId = ""
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertDepotFormulationAsync(emptyPatientId));

        DepotFormulationRecord whitespacePatientId = new()
        {
            DepotId = "depot_1",
            PatientId = "   "
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertDepotFormulationAsync(whitespacePatientId));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetDepotFormulationByIdAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetDepotFormulationByIdAsync("   "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetDepotsByPatientIdAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetDepotsByPatientIdAsync("   "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveReleaseScheduleAsync(null!, []));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveReleaseScheduleAsync("   ", []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveReleaseScheduleAsync("depot_1", null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetReleaseScheduleAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetReleaseScheduleAsync("   "));
    }

    /// <summary>
    /// Verifies formulation upsert, primary key retrieval, and update semantics against SQLite.
    /// </summary>
    [Fact]
    public async Task Upsert_And_GetById_Lifecycle()
    {
        // WHAT: Tests UpsertDepotFormulationAsync and GetDepotFormulationByIdAsync.
        // WHY:  Validates complete insert, point lookup, miss handling, and update-on-collision flow.
        // HOW:  Inserts a formulation record, retrieves and verifies it, updates it, and re-verifies.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 1. Miss lookup
        DepotFormulationRecord? missing = await _repository.GetDepotFormulationByIdAsync("nonexistent_depot");
        Assert.Null(missing);

        // 2. Insert new formulation
        DepotFormulationRecord initial = new()
        {
            DepotId = "depot_alpha",
            PatientId = "patient_alice",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 180.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = now
        };

        await _repository.UpsertDepotFormulationAsync(initial);

        DepotFormulationRecord? fetched = await _repository.GetDepotFormulationByIdAsync("depot_alpha");
        Assert.NotNull(fetched);
        Assert.Equal("depot_alpha", fetched.DepotId);
        Assert.Equal("patient_alice", fetched.PatientId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, fetched.Regimen);
        Assert.Equal("PLGA-PEG-PLGA", fetched.PolymerMatrixType);
        Assert.Equal(180.0, fetched.InitialNanocarrierMassMg);
        Assert.Equal(0.55, fetched.DailyErosionRatePercent);
        Assert.Equal("Korsmeyer-Peppas", fetched.ReleaseKineticsModel);
        Assert.Equal(180, fetched.TargetHorizonDays);
        Assert.Equal(now.ToUnixTimeSeconds(), fetched.CreatedAtUtc.ToUnixTimeSeconds());

        // 3. Update existing formulation on conflict
        DepotFormulationRecord updated = initial with
        {
            Regimen = TherapeuticRegimen.Custom,
            PolymerMatrixType = "Alginate-Chitosan",
            InitialNanocarrierMassMg = 220.0,
            DailyErosionRatePercent = 0.40,
            ReleaseKineticsModel = "Higuchi",
            TargetHorizonDays = 120
        };

        await _repository.UpsertDepotFormulationAsync(updated);

        DepotFormulationRecord? reFetched = await _repository.GetDepotFormulationByIdAsync("depot_alpha");
        Assert.NotNull(reFetched);
        Assert.Equal(TherapeuticRegimen.Custom, reFetched.Regimen);
        Assert.Equal("Alginate-Chitosan", reFetched.PolymerMatrixType);
        Assert.Equal(220.0, reFetched.InitialNanocarrierMassMg);
        Assert.Equal(0.40, reFetched.DailyErosionRatePercent);
        Assert.Equal("Higuchi", reFetched.ReleaseKineticsModel);
        Assert.Equal(120, reFetched.TargetHorizonDays);
    }

    /// <summary>
    /// Verifies patient formulation querying and chronological ordering.
    /// </summary>
    [Fact]
    public async Task GetDepotsByPatientId_Lifecycle()
    {
        // WHAT: Tests GetDepotsByPatientIdAsync for empty patient cohorts and multiple ordered formulations.
        // WHY:  Ensures patients and clinicians can inspect all personalized formulations ordered chronologically.
        // HOW:  Inserts multiple depots for different patients and verifies returned collections and sorting.
        DateTimeOffset baseTime = DateTimeOffset.UtcNow;

        // 1. Empty cohort
        var emptyList = await _repository.GetDepotsByPatientIdAsync("patient_unknown");
        Assert.Empty(emptyList);

        // 2. Insert two depots for patient_carol with different timestamps
        DepotFormulationRecord carol1 = new()
        {
            DepotId = "depot_carol_1",
            PatientId = "patient_carol",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 100.0,
            DailyErosionRatePercent = 0.50,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = baseTime.AddHours(-2)
        };

        DepotFormulationRecord carol2 = new()
        {
            DepotId = "depot_carol_2",
            PatientId = "patient_carol",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "Cyclodextrin-Scaffold",
            InitialNanocarrierMassMg = 150.0,
            DailyErosionRatePercent = 0.60,
            ReleaseKineticsModel = "Coupled-Fickian",
            TargetHorizonDays = 180,
            CreatedAtUtc = baseTime
        };

        DepotFormulationRecord dave1 = new()
        {
            DepotId = "depot_dave_1",
            PatientId = "patient_dave",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 200.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Higuchi",
            TargetHorizonDays = 180,
            CreatedAtUtc = baseTime
        };

        await _repository.UpsertDepotFormulationAsync(carol1);
        await _repository.UpsertDepotFormulationAsync(carol2);
        await _repository.UpsertDepotFormulationAsync(dave1);

        var carolDepots = await _repository.GetDepotsByPatientIdAsync("patient_carol");
        Assert.Equal(2, carolDepots.Count);
        // Ordered by created_at_utc descending -> carol2 first, then carol1
        Assert.Equal("depot_carol_2", carolDepots[0].DepotId);
        Assert.Equal("depot_carol_1", carolDepots[1].DepotId);

        var daveDepots = await _repository.GetDepotsByPatientIdAsync("patient_dave");
        Assert.Single(daveDepots);
        Assert.Equal("depot_dave_1", daveDepots[0].DepotId);
    }

    /// <summary>
    /// Verifies transactional replacement and chronological querying of release schedule timepoints.
    /// </summary>
    [Fact]
    public async Task SaveReleaseSchedule_And_GetReleaseSchedule_Lifecycle()
    {
        // WHAT: Tests SaveReleaseScheduleAsync and GetReleaseScheduleAsync.
        // WHY:  Ensures release schedule timepoints are transactionally replaced and ordered chronologically by day.
        // HOW:  Saves points, verifies retrieval, updates with auto-generated IDs, and tests clearing via empty collection.
        string depotId = "depot_schedule_test";
        DepotFormulationRecord formulation = new()
        {
            DepotId = depotId,
            PatientId = "patient_eve",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 150.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await _repository.UpsertDepotFormulationAsync(formulation);

        // 1. Initial schedule is empty
        var initialPoints = await _repository.GetReleaseScheduleAsync(depotId);
        Assert.Empty(initialPoints);

        // 2. Save schedule points with explicit schedule IDs
        List<DepotSchedulePoint> points =
        [
            new()
            {
                ScheduleId = "pt_60",
                DepotId = depotId,
                TimepointDay = 60,
                CumulativeReleaseFraction = 0.33,
                InstantaneousRateMgDay = 0.80,
                RemainingReserveMg = 100.5
            },
            new()
            {
                ScheduleId = "pt_0",
                DepotId = depotId,
                TimepointDay = 0,
                CumulativeReleaseFraction = 0.0,
                InstantaneousRateMgDay = 0.85,
                RemainingReserveMg = 150.0
            },
            new()
            {
                ScheduleId = "pt_30",
                DepotId = depotId,
                TimepointDay = 30,
                CumulativeReleaseFraction = 0.165,
                InstantaneousRateMgDay = 0.82,
                RemainingReserveMg = 125.25
            }
        ];

        await _repository.SaveReleaseScheduleAsync(depotId, points);

        var fetchedPoints = await _repository.GetReleaseScheduleAsync(depotId);
        Assert.Equal(3, fetchedPoints.Count);
        // Ordered by timepoint_day ASC: 0, 30, 60
        Assert.Equal(0, fetchedPoints[0].TimepointDay);
        Assert.Equal("pt_0", fetchedPoints[0].ScheduleId);
        Assert.Equal(30, fetchedPoints[1].TimepointDay);
        Assert.Equal("pt_30", fetchedPoints[1].ScheduleId);
        Assert.Equal(60, fetchedPoints[2].TimepointDay);
        Assert.Equal("pt_60", fetchedPoints[2].ScheduleId);

        // 3. Save points with null or whitespace ScheduleId (tests auto-generated GUID branch)
        List<DepotSchedulePoint> autoIdPoints =
        [
            new()
            {
                ScheduleId = "",
                DepotId = depotId,
                TimepointDay = 10,
                CumulativeReleaseFraction = 0.05,
                InstantaneousRateMgDay = 0.84,
                RemainingReserveMg = 142.5
            },
            new()
            {
                ScheduleId = "   ",
                DepotId = depotId,
                TimepointDay = 20,
                CumulativeReleaseFraction = 0.11,
                InstantaneousRateMgDay = 0.83,
                RemainingReserveMg = 133.5
            }
        ];

        await _repository.SaveReleaseScheduleAsync(depotId, autoIdPoints);

        var fetchedAuto = await _repository.GetReleaseScheduleAsync(depotId);
        Assert.Equal(2, fetchedAuto.Count);
        Assert.False(string.IsNullOrWhiteSpace(fetchedAuto[0].ScheduleId));
        Assert.False(string.IsNullOrWhiteSpace(fetchedAuto[1].ScheduleId));
        Assert.Equal(10, fetchedAuto[0].TimepointDay);
        Assert.Equal(20, fetchedAuto[1].TimepointDay);

        // 4. Save empty list clears the schedule (tests points.Count == 0 branch)
        await _repository.SaveReleaseScheduleAsync(depotId, []);
        var cleared = await _repository.GetReleaseScheduleAsync(depotId);
        Assert.Empty(cleared);
    }

    /// <summary>
    /// Verifies reader fallback logic when an unrecognized regimen string is stored in the database.
    /// </summary>
    [Fact]
    public async Task MapDepotRecord_FallbackOnInvalidRegimen()
    {
        // WHAT: Directly inserts a formulation with an unknown regimen and queries it through the repository.
        // WHY:  Validates Enum.TryParse fallback branch in MapDepotRecord for corrupted database entries.
        // HOW:  Executes raw INSERT with 'CorruptedOrUnknownRegimen' and verifies GetDepotFormulationByIdAsync defaults to MaleToFemale.
        string depotId = "depot_corrupted_regimen";
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync();
            await using DbCommand cmd = session.CreateCommand(@"
                INSERT INTO weaver_depot_formulations (
                    depot_id, patient_id, regimen, polymer_matrix_type, initial_nanocarrier_mass_mg,
                    daily_erosion_rate_percent, release_kinetics_model, target_horizon_days, created_at_utc
                ) VALUES (
                    'depot_corrupted_regimen', 'patient_corrupt', 'NonexistentRegimenValue', 'PLGA-PEG-PLGA',
                    100.0, 0.5, 'Korsmeyer-Peppas', 180, '2026-11-10T00:00:00Z'
                );");
            await cmd.ExecuteNonQueryAsync();
        }

        DepotFormulationRecord? record = await _repository.GetDepotFormulationByIdAsync(depotId);
        Assert.NotNull(record);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, record.Regimen);
    }
}
