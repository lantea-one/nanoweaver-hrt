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
/// Unit tests verifying <see cref="Migration020_ClinicalPharmacovigilance"/>,
/// <see cref="ClinicalPharmacovigilanceRepository"/>, persistence DTO example providers,
/// and GDPR Article 17 erasure cascades.
/// </summary>
public sealed class ClinicalPharmacovigilanceRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite database file.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection factory bound to the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClinicalPharmacovigilanceRepositoryTests"/> class.
    /// </summary>
    public ClinicalPharmacovigilanceRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 20 schema migrations applied.
        // WHY:  Guarantees deterministic, side-effect-free pharmacovigilance repository testing.
        // HOW:  Creates a unique temporary directory and calls WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_pharmacovigilance_repo_{Guid.NewGuid():N}");
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
    /// Verifies <see cref="Migration020_ClinicalPharmacovigilance"/> generates valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration020_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        Migration020_ClinicalPharmacovigilance migration = new();
        Assert.Equal(20, migration.Version);
        Assert.Equal("020_ClinicalPharmacovigilance", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_lot", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_immuno_lot", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("REAL NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_patient", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="LotAdverseEventRecord"/> and <see cref="LotImmunogenicityAssayRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModels_GetExamples_ReturnValidPopulatedRecords()
    {
        LotAdverseEventRecord ev = new LotAdverseEventRecord().GetExamples();
        Assert.Equal("aevt_20261123_001", ev.EventId);
        Assert.Equal(AdverseEventSeverityGrade.Grade1Mild, ev.SeverityGrade);
        Assert.Null(ev.ClinicalNotesEncrypted);

        LotImmunogenicityAssayRecord assay = new LotImmunogenicityAssayRecord().GetExamples();
        Assert.Equal("immu_20261123_001", assay.AssayId);
        Assert.Equal(96.0, assay.Ch50Percent);
        Assert.Equal(1.1, assay.Tlr9ActivationIndex);
    }

    /// <summary>
    /// Verifies argument validation guards on <see cref="ClinicalPharmacovigilanceRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => new ClinicalPharmacovigilanceRepository(null!));
        ClinicalPharmacovigilanceRepository repo = new(_factory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddAdverseEventAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAdverseEventAsync(new LotAdverseEventRecord { EventId = " ", LotId = "l", PatientId = "p", SymptomCategory = "s", ReportedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAdverseEventAsync(new LotAdverseEventRecord { EventId = "e", LotId = " ", PatientId = "p", SymptomCategory = "s", ReportedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAdverseEventAsync(new LotAdverseEventRecord { EventId = "e", LotId = "l", PatientId = " ", SymptomCategory = "s", ReportedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAdverseEventAsync(new LotAdverseEventRecord { EventId = "e", LotId = "l", PatientId = "p", SymptomCategory = " ", ReportedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddAdverseEventAsync(new LotAdverseEventRecord { EventId = "e", LotId = "l", PatientId = "p", SymptomCategory = "s", ReportedBy = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListAdverseEventsByLotAsync(" "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddImmunogenicityAssayAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddImmunogenicityAssayAsync(new LotImmunogenicityAssayRecord { AssayId = " ", LotId = "l", PatientId = "p" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddImmunogenicityAssayAsync(new LotImmunogenicityAssayRecord { AssayId = "a", LotId = " ", PatientId = "p" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddImmunogenicityAssayAsync(new LotImmunogenicityAssayRecord { AssayId = "a", LotId = "l", PatientId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListImmunogenicityAssaysByLotAsync(" "));
    }

    /// <summary>
    /// Verifies chronological ordering, nullable notes, enum fallbacks, and GDPR Article 17 erasure cascade.
    /// </summary>
    [Fact]
    public async Task Repository_RoundTripEnumFallbackAndGdprErasureCascade_WorkDeterministically()
    {
        DepotFormulationRepository depotRepo = new(_factory);
        FabricationLotRepository lotRepo = new(_factory);
        ClinicalPharmacovigilanceRepository pvRepo = new(_factory);

        DateTimeOffset t0 = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
        await depotRepo.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "dep-pv-1",
            PatientId = "pat-pv-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = t0
        });
        await lotRepo.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot-pv-1",
            DepotId = "dep-pv-1",
            PatientId = "pat-pv-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 16.0,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('a', 64),
            LotStatus = FabricationLotStatus.Released,
            CreatedAtUtc = t0,
            ReleasedAtUtc = t0.AddHours(1)
        });

        // Insert adverse events out of chronological order (one with notes, one null)
        await pvRepo.AddAdverseEventAsync(new LotAdverseEventRecord
        {
            EventId = "ae-2",
            LotId = "lot-pv-1",
            PatientId = "pat-pv-1",
            SeverityGrade = AdverseEventSeverityGrade.Grade3Severe,
            SymptomCategory = "CARPA",
            OnsetHoursPostImplant = 12.0,
            ClinicalNotesEncrypted = "enc:clinical-note",
            ReportedBy = "dr_chen",
            RecordedAtUtc = t0.AddHours(6)
        });
        await pvRepo.AddAdverseEventAsync(new LotAdverseEventRecord
        {
            EventId = "ae-1",
            LotId = "lot-pv-1",
            PatientId = "pat-pv-1",
            SeverityGrade = AdverseEventSeverityGrade.Grade2Moderate,
            SymptomCategory = "InjectionSiteErythema",
            OnsetHoursPostImplant = 2.0,
            ClinicalNotesEncrypted = null,
            ReportedBy = "rn_alex",
            RecordedAtUtc = t0.AddHours(2)
        });

        IReadOnlyList<LotAdverseEventRecord> events = await pvRepo.ListAdverseEventsByLotAsync("lot-pv-1");
        Assert.Equal(2, events.Count);
        Assert.Equal("ae-1", events[0].EventId);
        Assert.Null(events[0].ClinicalNotesEncrypted);
        Assert.Equal(AdverseEventSeverityGrade.Grade2Moderate, events[0].SeverityGrade);
        Assert.Equal("ae-2", events[1].EventId);
        Assert.Equal("enc:clinical-note", events[1].ClinicalNotesEncrypted);
        Assert.Equal(AdverseEventSeverityGrade.Grade3Severe, events[1].SeverityGrade);

        // Insert immunogenicity assays out of chronological order
        await pvRepo.AddImmunogenicityAssayAsync(new LotImmunogenicityAssayRecord
        {
            AssayId = "imm-2",
            LotId = "lot-pv-1",
            PatientId = "pat-pv-1",
            Ch50Percent = 32.0,
            C3aNgMl = 610.0,
            C5aNgMl = 52.0,
            Il6PgMl = 125.0,
            TnfaPgMl = 64.0,
            AntiPegIgmTiter = 640.0,
            AntiDsDnaIgGIuMl = 115.0,
            Tlr9ActivationIndex = 4.8,
            RecordedAtUtc = t0.AddHours(5)
        });
        await pvRepo.AddImmunogenicityAssayAsync(new LotImmunogenicityAssayRecord
        {
            AssayId = "imm-1",
            LotId = "lot-pv-1",
            PatientId = "pat-pv-1",
            Ch50Percent = 94.0,
            C3aNgMl = 105.0,
            C5aNgMl = 4.2,
            Il6PgMl = 3.5,
            TnfaPgMl = 4.0,
            AntiPegIgmTiter = 16.0,
            AntiDsDnaIgGIuMl = 9.0,
            Tlr9ActivationIndex = 1.1,
            RecordedAtUtc = t0.AddHours(1)
        });

        IReadOnlyList<LotImmunogenicityAssayRecord> assays = await pvRepo.ListImmunogenicityAssaysByLotAsync("lot-pv-1");
        Assert.Equal(2, assays.Count);
        Assert.Equal("imm-1", assays[0].AssayId);
        Assert.Equal(94.0, assays[0].Ch50Percent);
        Assert.Equal("imm-2", assays[1].AssayId);
        Assert.Equal(4.8, assays[1].Tlr9ActivationIndex);

        // Exercise corrupted raw enum fallback cleanly
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_lot_adverse_events SET severity_grade = 'UnknownGrade' WHERE event_id = 'ae-1'");
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(AdverseEventSeverityGrade.Grade1Mild, (await pvRepo.ListAdverseEventsByLotAsync("lot-pv-1"))[0].SeverityGrade);

        // Atomic GDPR Article 17 erasure removes both pharmacovigilance tables + parent lot
        int deleted = await lotRepo.DeleteByPatientIdAsync("pat-pv-1");
        Assert.Equal(5, deleted); // 2 assays + 2 adverse events + 1 lot
        Assert.Empty(await pvRepo.ListAdverseEventsByLotAsync("lot-pv-1"));
        Assert.Empty(await pvRepo.ListImmunogenicityAssaysByLotAsync("lot-pv-1"));
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
