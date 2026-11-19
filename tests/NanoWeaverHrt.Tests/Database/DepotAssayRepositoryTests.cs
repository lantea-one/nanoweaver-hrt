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
/// Unit and integration tests for <see cref="Migration014_DepotLongitudinalAssays"/> and <see cref="DepotAssayRepository"/>.
/// </summary>
public sealed class DepotAssayRepositoryTests : IDisposable
{
    /// <summary>
    /// Temporary directory hosting the isolated test SQLite database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection string for the isolated test SQLite database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Database connection factory under test.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Depot formulation repository used to seed parent formulations.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepository;

    /// <summary>
    /// Longitudinal depot assay repository under test.
    /// </summary>
    private readonly DepotAssayRepository _assayRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotAssayRepositoryTests"/> class.
    /// </summary>
    public DepotAssayRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_depot_assay_repo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        string dbPath = Path.Combine(_tempDir, "weaver_config.db");
        _connectionString = $"Data Source={dbPath}";

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        _connectionFactory = WeaverTestDb.CreateFactory(config);
        _depotRepository = new DepotFormulationRepository(_connectionFactory);
        _assayRepository = new DepotAssayRepository(_connectionFactory);
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
    /// Verifies Migration 014 metadata, null guards, and dialect DDL generation across all four engines.
    /// </summary>
    [Fact]
    public void Migration014_MetadataAndDialectValidation()
    {
        // WHAT: Verifies Version, Name, null argument guard, and SQL output across PostgreSQL, DuckDB, MySQL, MariaDB, and SQLite.
        // WHY:  Ensures schema portability across all four supported relational persistence engines.
        // HOW:  Invokes GetSql for every dialect and asserts expected table, column, and index definitions.
        Migration014_DepotLongitudinalAssays migration = new();
        Assert.Equal(14L, migration.Version);
        Assert.Equal("014_DepotLongitudinalAssays", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pgSql = migration.GetSql("postgresql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations", pgSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots", pgSql);
        Assert.Contains("DOUBLE PRECISION", pgSql);
        Assert.Contains("idx_depot_assay_patient_id", pgSql);
        Assert.Contains("idx_depot_assay_depot_day", pgSql);
        Assert.Contains("idx_depot_calib_depot_id", pgSql);

        string duckSql = migration.GetSql("duckdb");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations", duckSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots", duckSql);
        Assert.Contains("DOUBLE PRECISION", duckSql);
        Assert.DoesNotContain("ON DELETE CASCADE", duckSql);

        string mysqlSql = migration.GetSql("mysql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations", mysqlSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots", mysqlSql);
        Assert.Contains("DOUBLE", mysqlSql);
        Assert.Contains("FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE", mysqlSql);

        string mariadbSql = migration.GetSql("mariadb");
        Assert.Equal(mysqlSql, mariadbSql);

        string sqliteSql = migration.GetSql("sqlite");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots", sqliteSql);
        Assert.Contains("REAL    NOT NULL", sqliteSql);
    }

    /// <summary>
    /// Verifies persistence and domain model properties, defaults, and OpenAPI example generators.
    /// </summary>
    [Fact]
    public void PersistenceModels_DefaultsAndExamples()
    {
        // WHAT: Exercises default properties, init accessors, and GetExamples() for persistence records.
        // WHY:  Guarantees 100% coverage of DTO model types and Swagger example providers.
        // HOW:  Constructs default and example records and validates field values.
        DepotAssayObservationRecord obsDefault = new();
        Assert.Equal(string.Empty, obsDefault.ObservationId);
        Assert.Equal(string.Empty, obsDefault.DepotId);
        Assert.Equal(string.Empty, obsDefault.PatientId);
        Assert.Equal(0, obsDefault.DaySinceImplant);
        Assert.Null(obsDefault.NotesEncrypted);

        DepotAssayObservationRecord obsExample = obsDefault.GetExamples();
        Assert.False(string.IsNullOrWhiteSpace(obsExample.ObservationId));
        Assert.Equal(60, obsExample.DaySinceImplant);
        Assert.True(obsExample.ObservedE2PmolL > 0);

        DepotCalibrationSnapshotRecord snapDefault = new();
        Assert.Equal(string.Empty, snapDefault.SnapshotId);
        Assert.Equal(1.0, snapDefault.InferredClearanceMultiplier);
        Assert.Equal(0.55, snapDefault.InferredDailyErosionRatePercent);
        Assert.Equal(180, snapDefault.AdjustedRedoseDay);
        Assert.Equal(18.0, snapDefault.RecommendedNextMassMg);
        Assert.Equal(DepotSafetyStatus.Optimal, snapDefault.SafetyStatus);

        DepotCalibrationSnapshotRecord snapExample = snapDefault.GetExamples();
        Assert.False(string.IsNullOrWhiteSpace(snapExample.SnapshotId));
        Assert.Equal(164, snapExample.AdjustedRedoseDay);
    }

    /// <summary>
    /// Verifies constructor and method argument validation guards.
    /// </summary>
    [Fact]
    public async Task ValidationGuards_ThrowOnInvalidArguments()
    {
        // WHAT: Tests null and whitespace parameter checks across constructor and all repository methods.
        // WHY:  Prevents invalid keys or null payloads from reaching relational drivers.
        // HOW:  Invokes each method with null/whitespace inputs and asserts ArgumentNullException / ArgumentException.
        Assert.Throws<ArgumentNullException>(() => new DepotAssayRepository(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _assayRepository.RecordObservationAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.RecordObservationAsync(new DepotAssayObservationRecord { DepotId = " ", PatientId = "p1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.RecordObservationAsync(new DepotAssayObservationRecord { DepotId = "d1", PatientId = " " }));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _assayRepository.GetObservationsByDepotAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.GetObservationsByDepotAsync("   "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _assayRepository.SaveCalibrationSnapshotAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.SaveCalibrationSnapshotAsync(new DepotCalibrationSnapshotRecord { DepotId = " ", PatientId = "p1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.SaveCalibrationSnapshotAsync(new DepotCalibrationSnapshotRecord { DepotId = "d1", PatientId = " " }));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _assayRepository.GetLatestCalibrationSnapshotAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.GetLatestCalibrationSnapshotAsync("   "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _assayRepository.DeleteByPatientIdAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _assayRepository.DeleteByPatientIdAsync("   "));
    }

    /// <summary>
    /// Verifies full CRUD lifecycle for longitudinal assay observations, chronological sorting,
    /// calibration snapshots, latest selection, and GDPR patient erasure.
    /// </summary>
    [Fact]
    public async Task FullLifecycle_RecordQueryCalibrateAndErase()
    {
        // WHAT: Seeds a parent depot formulation, records out-of-order assays, saves multiple calibration snapshots,
        //       and verifies chronological retrieval, latest snapshot lookup, and GDPR patient-level deletion.
        // WHY:  Validates relational integrity, sorting guarantees, and erasure completeness end-to-end on SQLite.
        // HOW:  Persists parent formulation, exercises all five repository methods, and verifies post-delete empty state.
        string depotId = "depot_longitudinal_01";
        string patientId = "patient_maya";
        DateTimeOffset t0 = DateTimeOffset.Parse("2026-11-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PlgaPegPlga",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "KorsmeyerPeppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = t0
        });

        // Empty reads initially
        Assert.Empty(await _assayRepository.GetObservationsByDepotAsync(depotId));
        Assert.Null(await _assayRepository.GetLatestCalibrationSnapshotAsync(depotId));

        // Record Day 90 first (explicit ID, encrypted notes) and Day 30 second (auto-generated ID, null notes)
        DepotAssayObservationRecord obs90 = await _assayRepository.RecordObservationAsync(new DepotAssayObservationRecord
        {
            ObservationId = "assay_explicit_90",
            DepotId = depotId,
            PatientId = patientId,
            DaySinceImplant = 90,
            ObservedE2PmolL = 490.0,
            ObservedTNmolL = 1.0,
            ObservedLhIuL = 0.5,
            ObservedFshIuL = 0.8,
            ObservedShbgNmolL = 82.0,
            ObservedHematocritPct = 40.5,
            RecordedAtUtc = t0.AddDays(90),
            NotesEncrypted = "enc:v1:sample_cipher"
        });

        DepotAssayObservationRecord obs30 = await _assayRepository.RecordObservationAsync(new DepotAssayObservationRecord
        {
            ObservationId = string.Empty,
            DepotId = depotId,
            PatientId = patientId,
            DaySinceImplant = 30,
            ObservedE2PmolL = 560.0,
            ObservedTNmolL = 1.2,
            ObservedLhIuL = 0.7,
            ObservedFshIuL = 1.1,
            ObservedShbgNmolL = 74.0,
            ObservedHematocritPct = 41.0,
            RecordedAtUtc = t0.AddDays(30),
            NotesEncrypted = null
        });

        Assert.Equal("assay_explicit_90", obs90.ObservationId);
        Assert.StartsWith("assay_", obs30.ObservationId, StringComparison.Ordinal);

        IReadOnlyList<DepotAssayObservationRecord> fetchedObs = await _assayRepository.GetObservationsByDepotAsync(depotId);
        Assert.Equal(2, fetchedObs.Count);
        Assert.Equal(30, fetchedObs[0].DaySinceImplant);
        Assert.Null(fetchedObs[0].NotesEncrypted);
        Assert.Equal(90, fetchedObs[1].DaySinceImplant);
        Assert.Equal("enc:v1:sample_cipher", fetchedObs[1].NotesEncrypted);

        // Save older snapshot (auto-ID) and newer snapshot (explicit ID)
        DepotCalibrationSnapshotRecord olderSnap = await _assayRepository.SaveCalibrationSnapshotAsync(new DepotCalibrationSnapshotRecord
        {
            SnapshotId = string.Empty,
            DepotId = depotId,
            PatientId = patientId,
            InferredClearanceMultiplier = 1.05,
            InferredDailyErosionRatePercent = 0.56,
            AdjustedRedoseDay = 172,
            RecommendedNextMassMg = 18.8,
            SafetyStatus = DepotSafetyStatus.Optimal,
            ResidualRmse = 0.03,
            ComputedAtUtc = t0.AddDays(30)
        });
        Assert.StartsWith("calib_", olderSnap.SnapshotId, StringComparison.Ordinal);

        DepotCalibrationSnapshotRecord newerSnap = await _assayRepository.SaveCalibrationSnapshotAsync(new DepotCalibrationSnapshotRecord
        {
            SnapshotId = "calib_explicit_90",
            DepotId = depotId,
            PatientId = patientId,
            InferredClearanceMultiplier = 1.22,
            InferredDailyErosionRatePercent = 0.62,
            AdjustedRedoseDay = 152,
            RecommendedNextMassMg = 21.5,
            SafetyStatus = DepotSafetyStatus.AdvisoryDrift,
            ResidualRmse = 0.04,
            ComputedAtUtc = t0.AddDays(90)
        });
        Assert.Equal("calib_explicit_90", newerSnap.SnapshotId);

        DepotCalibrationSnapshotRecord? latest = await _assayRepository.GetLatestCalibrationSnapshotAsync(depotId);
        Assert.NotNull(latest);
        Assert.Equal("calib_explicit_90", latest.SnapshotId);
        Assert.Equal(1.22, latest.InferredClearanceMultiplier);
        Assert.Equal(0.62, latest.InferredDailyErosionRatePercent);
        Assert.Equal(152, latest.AdjustedRedoseDay);
        Assert.Equal(21.5, latest.RecommendedNextMassMg);
        Assert.Equal(DepotSafetyStatus.AdvisoryDrift, latest.SafetyStatus);
        Assert.Equal(0.04, latest.ResidualRmse);

        // GDPR Article 17 erasure deletes all 2 observations + 2 snapshots = 4 rows
        int erased = await _assayRepository.DeleteByPatientIdAsync(patientId);
        Assert.Equal(4, erased);
        Assert.Empty(await _assayRepository.GetObservationsByDepotAsync(depotId));
        Assert.Null(await _assayRepository.GetLatestCalibrationSnapshotAsync(depotId));
    }

    /// <summary>
    /// Verifies fallback to <see cref="DepotSafetyStatus.Optimal"/> when a snapshot row contains an unrecognized status string.
    /// </summary>
    [Fact]
    public async Task MapSnapshot_FallbackOnInvalidSafetyStatus()
    {
        // WHAT: Inserts a raw calibration snapshot row with an invalid safety_status string and queries it back.
        // WHY:  Exercises the Enum.TryParse failure fallback branch in MapSnapshot.
        // HOW:  Seeds parent depot, executes raw INSERT with 'UnknownStatusValue', and asserts fallback to Optimal.
        string depotId = "depot_corrupt_status";
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = "patient_corrupt_status",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PlgaPegPlga",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "KorsmeyerPeppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync();
            await using DbCommand cmd = session.CreateCommand(@"
                INSERT INTO weaver_depot_calibration_snapshots (
                    snapshot_id, depot_id, patient_id, inferred_clearance_multiplier,
                    inferred_erosion_rate_percent, adjusted_redose_day, recommended_next_mass_mg,
                    safety_status, residual_rmse, computed_at_utc
                ) VALUES (
                    'calib_bad_status', 'depot_corrupt_status', 'patient_corrupt_status', 1.0,
                    0.55, 180, 18.0, 'UnknownStatusValue', 0.01, '2026-11-19T00:00:00Z'
                );");
            await cmd.ExecuteNonQueryAsync();
        }

        DepotCalibrationSnapshotRecord? snapshot = await _assayRepository.GetLatestCalibrationSnapshotAsync(depotId);
        Assert.NotNull(snapshot);
        Assert.Equal(DepotSafetyStatus.Optimal, snapshot.SafetyStatus);
    }
}
