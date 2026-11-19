using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit and integration tests for <see cref="Migration015_FabricationLotLedger"/> and <see cref="FabricationLotRepository"/>.
/// </summary>
public sealed class FabricationLotRepositoryTests : IDisposable
{
    /// <summary>
    /// Temporary directory hosting the isolated SQLite test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection string for the isolated SQLite test database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Database connection factory under test.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Depot formulation repository used to seed parent formulations for foreign key integrity.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepository;

    /// <summary>
    /// Fabrication lot repository under test.
    /// </summary>
    private readonly FabricationLotRepository _lotRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="FabricationLotRepositoryTests"/> class.
    /// </summary>
    public FabricationLotRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_fab_lot_repo_test_" + Guid.NewGuid().ToString("N"));
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
        _lotRepository = new FabricationLotRepository(_connectionFactory);
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
    /// Verifies Migration 015 metadata, null guards, and dialect DDL generation across all four engines.
    /// </summary>
    [Fact]
    public void Migration015_MetadataAndDialectValidation()
    {
        // WHAT: Verifies Version, Name, null guard, and DDL across PostgreSQL, DuckDB, MySQL, MariaDB, and SQLite.
        // WHY:  Guarantees multi-engine relational schema compatibility across all supported database backends.
        // HOW:  Invokes GetSql for every engine dialect and asserts expected table, column, and index statements.
        Migration015_FabricationLotLedger migration = new();
        Assert.Equal(15L, migration.Version);
        Assert.Equal("015_FabricationLotLedger", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pgSql = migration.GetSql("postgresql");
        Assert.Contains("weaver_fabrication_lots", pgSql, StringComparison.Ordinal);
        Assert.Contains("weaver_fabrication_qc_checks", pgSql, StringComparison.Ordinal);
        Assert.Contains("DOUBLE PRECISION", pgSql, StringComparison.Ordinal);
        Assert.Contains("idx_fab_lot_patient_id", pgSql, StringComparison.Ordinal);
        Assert.Contains("idx_fab_qc_lot_id", pgSql, StringComparison.Ordinal);

        string duckSql = migration.GetSql("duckdb");
        Assert.Contains("weaver_fabrication_lots", duckSql, StringComparison.Ordinal);
        Assert.Contains("DOUBLE PRECISION", duckSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duckSql, StringComparison.Ordinal);

        string mysqlSql = migration.GetSql("mysql");
        Assert.Contains("weaver_fabrication_lots", mysqlSql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (depot_id)", mysqlSql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (lot_id)", mysqlSql, StringComparison.Ordinal);

        string mariaSql = migration.GetSql("mariadb");
        Assert.Equal(mysqlSql, mariaSql);

        string sqliteSql = migration.GetSql("sqlite");
        Assert.Contains("weaver_fabrication_lots", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("REAL", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("idx_fab_lot_depot_id", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("idx_fab_qc_patient_id", sqliteSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies constructor and method argument guards plus OpenAPI example providers on persistence records.
    /// </summary>
    [Fact]
    public async Task RepositoryGuards_AndPersistenceModelExamples_BehaveCorrectly()
    {
        // WHAT: Exercises null/whitespace guards on FabricationLotRepository and GetExamples() on persistence records.
        // WHY:  Ensures defensive input validation and deterministic Swagger documentation samples.
        // HOW:  Calls every repository guard branch and validates non-empty sample record outputs.
        Assert.Throws<ArgumentNullException>(() => new FabricationLotRepository(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _lotRepository.UpsertLotAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            DepotId = " ",
            PatientId = "p1",
            OpentronsProtocolSha256 = "abc"
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            DepotId = "d1",
            PatientId = "",
            OpentronsProtocolSha256 = "abc"
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            DepotId = "d1",
            PatientId = "p1",
            OpentronsProtocolSha256 = " "
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.GetLotByIdAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.GetLotsByPatientIdAsync(" "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _lotRepository.RecordQcCheckAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordQcCheckAsync(new FabricationQcCheckRecord
        {
            LotId = "",
            PatientId = "p1"
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordQcCheckAsync(new FabricationQcCheckRecord
        {
            LotId = "l1",
            PatientId = " "
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.GetQcChecksByLotAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.DeleteByPatientIdAsync(" "));

        FabricationLotRecord lotSample = new FabricationLotRecord().GetExamples();
        Assert.Equal("lot_sample_001", lotSample.LotId);
        Assert.Equal("depot_sample_001", lotSample.DepotId);
        Assert.Equal(FabricationLotStatus.QcPending, lotSample.LotStatus);

        FabricationQcCheckRecord qcSample = new FabricationQcCheckRecord().GetExamples();
        Assert.Equal("qc_sample_001", qcSample.CheckId);
        Assert.Equal(FabricationQcGateType.GelFoldingYield, qcSample.GateType);
        Assert.True(qcSample.Passed);
    }

    /// <summary>
    /// Verifies complete lot CRUD, QC check logging, certificate update, enum fallbacks, and GDPR erasure cascade.
    /// </summary>
    [Fact]
    public async Task LotAndQcLifecycle_PersistsQueriesUpdatesAndErasesCorrectly()
    {
        // WHAT: Exercises full persistence lifecycle on real SQLite including auto-ID, explicit-ID upsert,
        //       nullable certificate fields, QC check chronological ordering, unknown enum fallback, and GDPR delete.
        // WHY:  Validates 100% of relational SQL paths and row-mapping branches in FabricationLotRepository.
        // HOW:  Seeds a parent depot formulation, performs lot & QC operations, injects an unrecognized enum row,
        //       verifies fallback hydration, and confirms transactional erasure removes all patient rows.
        await SeedDepotAsync("depot_m35_01", "patient_m35_01");

        Assert.Null(await _lotRepository.GetLotByIdAsync("lot_does_not_exist"));

        DateTimeOffset t0 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        FabricationLotRecord created = await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = string.Empty,
            DepotId = "depot_m35_01",
            CalibrationSnapshotId = null,
            PatientId = "patient_m35_01",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 19.4,
            TargetErosionRatePercent = 0.58,
            CpGMethylationRequired = false,
            OpentronsProtocolSha256 = "aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff6666aaaa1111bbbb2222",
            LotStatus = FabricationLotStatus.Draft,
            CreatedAtUtc = t0,
            ReleasedAtUtc = null
        });

        Assert.StartsWith("lot_", created.LotId, StringComparison.Ordinal);

        FabricationLotRecord? fetchedDraft = await _lotRepository.GetLotByIdAsync(created.LotId);
        Assert.NotNull(fetchedDraft);
        Assert.Null(fetchedDraft.CalibrationSnapshotId);
        Assert.False(fetchedDraft.CpGMethylationRequired);
        Assert.Null(fetchedDraft.CertificatePayloadSha256);
        Assert.Null(fetchedDraft.CertificateSignatureBase64);
        Assert.Null(fetchedDraft.CertificateSignerKeyId);
        Assert.Null(fetchedDraft.ReleasedAtUtc);

        DateTimeOffset releasedAt = t0.AddHours(2);
        FabricationLotRecord updated = await _lotRepository.UpsertLotAsync(created with
        {
            CalibrationSnapshotId = "calib_001",
            CpGMethylationRequired = true,
            LotStatus = FabricationLotStatus.Released,
            CertificatePayloadSha256 = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            CertificateSignatureBase64 = "SIG_BASE64",
            CertificateSignerKeyId = "node-key-1",
            ReleasedAtUtc = releasedAt
        });

        FabricationLotRecord? fetchedReleased = await _lotRepository.GetLotByIdAsync(updated.LotId);
        Assert.NotNull(fetchedReleased);
        Assert.Equal("calib_001", fetchedReleased.CalibrationSnapshotId);
        Assert.True(fetchedReleased.CpGMethylationRequired);
        Assert.Equal(FabricationLotStatus.Released, fetchedReleased.LotStatus);
        Assert.Equal("SIG_BASE64", fetchedReleased.CertificateSignatureBase64);
        Assert.Equal("node-key-1", fetchedReleased.CertificateSignerKeyId);
        Assert.Equal(releasedAt, fetchedReleased.ReleasedAtUtc);

        FabricationQcCheckRecord qc1 = await _lotRepository.RecordQcCheckAsync(new FabricationQcCheckRecord
        {
            CheckId = string.Empty,
            LotId = created.LotId,
            PatientId = "patient_m35_01",
            GateType = FabricationQcGateType.GelFoldingYield,
            MeasuredValue = 91.5,
            SecondaryValue = null,
            ThresholdValue = 85.0,
            Passed = true,
            RecordedAtUtc = t0.AddMinutes(10),
            NotesEncrypted = null
        });
        Assert.StartsWith("qc_", qc1.CheckId, StringComparison.Ordinal);

        FabricationQcCheckRecord qc2 = await _lotRepository.RecordQcCheckAsync(new FabricationQcCheckRecord
        {
            CheckId = "qc_explicit_02",
            LotId = created.LotId,
            PatientId = "patient_m35_01",
            GateType = FabricationQcGateType.HydrodynamicSizePdi,
            MeasuredValue = 68.2,
            SecondaryValue = 0.14,
            ThresholdValue = 120.0,
            Passed = false,
            RecordedAtUtc = t0.AddMinutes(20),
            NotesEncrypted = "ENC_NOTES_PAYLOAD"
        });
        Assert.Equal("qc_explicit_02", qc2.CheckId);

        IReadOnlyList<FabricationQcCheckRecord> checks = await _lotRepository.GetQcChecksByLotAsync(created.LotId);
        Assert.Equal(2, checks.Count);
        Assert.Equal(qc1.CheckId, checks[0].CheckId);
        Assert.Null(checks[0].SecondaryValue);
        Assert.Null(checks[0].NotesEncrypted);
        Assert.True(checks[0].Passed);
        Assert.Equal("qc_explicit_02", checks[1].CheckId);
        Assert.Equal(0.14, checks[1].SecondaryValue);
        Assert.Equal("ENC_NOTES_PAYLOAD", checks[1].NotesEncrypted);
        Assert.False(checks[1].Passed);

        // Inject unrecognized enum strings directly into SQLite to cover safe fallback branches
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync();
            await using DbCommand updateLotCmd = session.CreateCommand(
                "UPDATE weaver_fabrication_lots SET regimen = 'UNKNOWN_REGIMEN', lot_status = 'UNKNOWN_STATUS' WHERE lot_id = @lid");
            WeaverDbParameters.Add(updateLotCmd, "lid", created.LotId);
            await updateLotCmd.ExecuteNonQueryAsync();

            await using DbCommand updateQcCmd = session.CreateCommand(
                "UPDATE weaver_fabrication_qc_checks SET gate_type = 'UNKNOWN_GATE' WHERE check_id = @cid");
            WeaverDbParameters.Add(updateQcCmd, "cid", qc1.CheckId);
            await updateQcCmd.ExecuteNonQueryAsync();
        }

        IReadOnlyList<FabricationLotRecord> patientLots = await _lotRepository.GetLotsByPatientIdAsync("patient_m35_01");
        Assert.Single(patientLots);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, patientLots[0].Regimen);
        Assert.Equal(FabricationLotStatus.Draft, patientLots[0].LotStatus);

        IReadOnlyList<FabricationQcCheckRecord> fallbackChecks = await _lotRepository.GetQcChecksByLotAsync(created.LotId);
        Assert.Equal(FabricationQcGateType.GelFoldingYield, fallbackChecks[0].GateType);

        // Verify GDPR Article 17 erasure cascade deletes both QC checks (2) and lot (1)
        int deletedRows = await _lotRepository.DeleteByPatientIdAsync("patient_m35_01");
        Assert.Equal(3, deletedRows);
        Assert.Empty(await _lotRepository.GetLotsByPatientIdAsync("patient_m35_01"));
        Assert.Empty(await _lotRepository.GetQcChecksByLotAsync(created.LotId));
    }

    /// <summary>
    /// Verifies Migration 016 DDL across all four engines, revocation record persistence/retrieval, guards,
    /// and GDPR Article 17 erasure cascade across <c>weaver_fabrication_lot_revocations</c>.
    /// </summary>
    [Fact]
    public async Task Migration016_AndLotRevocationPersistence_BehaveCorrectly()
    {
        // WHAT: Exercises Migration016_FabricationLotRevocation DDL, RecordLotRevocationAsync, GetLotRevocationAsync,
        //       nullable PayloadSha256 branches, argument guards, and GDPR erasure cascade.
        // WHY:  Milestone 36 (P190): Guarantees 100.00% line and branch coverage for lot revocation storage.
        // HOW:  Validates SQL across all 4 dialects, tests null/whitespace guards, persists/updates revocations, and deletes by patient.
        Migration016_FabricationLotRevocation migration = new();
        Assert.Equal(16L, migration.Version);
        Assert.Equal("016_FabricationLotRevocation", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        Assert.Contains("weaver_fabrication_lot_revocations", migration.GetSql("postgresql"), StringComparison.Ordinal);
        Assert.Contains("weaver_fabrication_lot_revocations", migration.GetSql("duckdb"), StringComparison.Ordinal);
        string mysqlSql = migration.GetSql("mysql");
        Assert.Contains("FOREIGN KEY (lot_id)", mysqlSql, StringComparison.Ordinal);
        Assert.Equal(mysqlSql, migration.GetSql("mariadb"));
        Assert.Contains("idx_fab_rev_patient_id", migration.GetSql("sqlite"), StringComparison.Ordinal);

        FabricationLotRevocationRecord ex = new FabricationLotRevocationRecord().GetExamples();
        Assert.Equal("lot_sample_001", ex.LotId);
        Assert.Equal("admin", ex.RevokedBy);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _lotRepository.RecordLotRevocationAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordLotRevocationAsync(ex with { LotId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordLotRevocationAsync(ex with { PatientId = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordLotRevocationAsync(ex with { RevokedBy = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.RecordLotRevocationAsync(ex with { RevocationReason = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _lotRepository.GetLotRevocationAsync(" "));

        await SeedDepotAsync("depot_m36_01", "patient_m36_01");
        FabricationLotRecord lot = await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            DepotId = "depot_m36_01",
            PatientId = "patient_m36_01",
            OpentronsProtocolSha256 = "aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff6666aaaa1111bbbb2222",
            LotStatus = FabricationLotStatus.Released
        });

        Assert.Null(await _lotRepository.GetLotRevocationAsync(lot.LotId));

        DateTimeOffset revokedAt = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
        await _lotRepository.RecordLotRevocationAsync(new FabricationLotRevocationRecord
        {
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            PayloadSha256 = null,
            RevokedBy = "admin",
            RevocationReason = "Initial thermal excursion",
            RevokedAtUtc = revokedAt
        });

        FabricationLotRevocationRecord? fetchedNullSha = await _lotRepository.GetLotRevocationAsync(lot.LotId);
        Assert.NotNull(fetchedNullSha);
        Assert.Null(fetchedNullSha.PayloadSha256);

        await _lotRepository.RecordLotRevocationAsync(fetchedNullSha with
        {
            PayloadSha256 = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            RevocationReason = "Confirmed cold-chain excursion"
        });

        FabricationLotRevocationRecord? fetchedUpdated = await _lotRepository.GetLotRevocationAsync(lot.LotId);
        Assert.NotNull(fetchedUpdated);
        Assert.Equal("1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef", fetchedUpdated.PayloadSha256);
        Assert.Equal("Confirmed cold-chain excursion", fetchedUpdated.RevocationReason);

        int deleted = await _lotRepository.DeleteByPatientIdAsync("patient_m36_01");
        Assert.Equal(2, deleted);
        Assert.Null(await _lotRepository.GetLotRevocationAsync(lot.LotId));
    }

    /// <summary>
    /// Seeds a parent depot formulation record required by foreign key constraints on <c>weaver_fabrication_lots</c>.
    /// </summary>
    /// <param name="depotId">Parent depot formulation identifier.</param>
    /// <param name="patientId">Patient identifier owning the depot.</param>
    /// <returns>A task representing the asynchronous seed operation.</returns>
    private async Task SeedDepotAsync(string depotId, string patientId)
    {
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero)
        });
    }
}
