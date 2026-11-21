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
/// Unit tests verifying <see cref="Migration019_ColdChainStabilityAndCustody"/>,
/// <see cref="ColdChainCustodyRepository"/>, persistence DTO example providers, and GDPR Article 17 erasure cascades.
/// </summary>
public sealed class ColdChainCustodyRepositoryTests : IDisposable
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
    /// Initializes a new instance of the <see cref="ColdChainCustodyRepositoryTests"/> class.
    /// </summary>
    public ColdChainCustodyRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 19 schema migrations applied.
        // WHY:  Guarantees deterministic, side-effect-free repository testing.
        // HOW:  Creates a unique temporary directory and calls WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_cold_chain_repo_{Guid.NewGuid():N}");
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
    /// Verifies <see cref="Migration019_ColdChainStabilityAndCustody"/> generates valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration019_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        Migration019_ColdChainStabilityAndCustody migration = new();
        Assert.Equal(19, migration.Version);
        Assert.Equal("019_ColdChainStabilityAndCustody", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_lot", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_custody_lot", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("REAL NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_patient", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="ThermalExcursionRecord"/> and <see cref="CustodyHandoffRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModels_GetExamples_ReturnValidPopulatedRecords()
    {
        ThermalExcursionRecord ex = new ThermalExcursionRecord().GetExamples();
        Assert.Equal("texc_20261119_001", ex.ExcursionId);
        Assert.Equal(ThermalStorageTier.Refrigerated2To8, ex.StorageTier);
        Assert.Null(ex.NotesEncrypted);

        CustodyHandoffRecord h = new CustodyHandoffRecord().GetExamples();
        Assert.Equal("chnd_20261119_001", h.HandoffId);
        Assert.Equal(CustodyStage.ClinicReceipt, h.Stage);
        Assert.Equal(64, h.ChainPrevSha256.Length);
        Assert.Equal(64, h.EntrySha256.Length);
    }

    /// <summary>
    /// Verifies argument validation guards on <see cref="ColdChainCustodyRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => new ColdChainCustodyRepository(null!));
        ColdChainCustodyRepository repo = new(_factory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddThermalExcursionAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddThermalExcursionAsync(new ThermalExcursionRecord { ExcursionId = " ", LotId = "l", PatientId = "p", SensorId = "s", RecordedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddThermalExcursionAsync(new ThermalExcursionRecord { ExcursionId = "e", LotId = " ", PatientId = "p", SensorId = "s", RecordedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddThermalExcursionAsync(new ThermalExcursionRecord { ExcursionId = "e", LotId = "l", PatientId = " ", SensorId = "s", RecordedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddThermalExcursionAsync(new ThermalExcursionRecord { ExcursionId = "e", LotId = "l", PatientId = "p", SensorId = " ", RecordedBy = "u" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddThermalExcursionAsync(new ThermalExcursionRecord { ExcursionId = "e", LotId = "l", PatientId = "p", SensorId = "s", RecordedBy = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListThermalExcursionsByLotAsync(" "));

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddCustodyHandoffAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = " ", LotId = "l", PatientId = "p", FromActor = "a", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = " ", PatientId = "p", FromActor = "a", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = " ", FromActor = "a", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = "p", FromActor = " ", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = "p", FromActor = "a", ToActor = " ", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = "p", FromActor = "a", ToActor = "b", FacilityCode = " ", ChainPrevSha256 = "0", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = "p", FromActor = "a", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = " ", EntrySha256 = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddCustodyHandoffAsync(new CustodyHandoffRecord { HandoffId = "h", LotId = "l", PatientId = "p", FromActor = "a", ToActor = "b", FacilityCode = "f", ChainPrevSha256 = "0", EntrySha256 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListCustodyHandoffsByLotAsync(" "));
    }

    /// <summary>
    /// Verifies chronological ordering, nullable notes, enum fallbacks, and GDPR Article 17 erasure cascade.
    /// </summary>
    [Fact]
    public async Task Repository_RoundTripEnumFallbackAndGdprErasureCascade_WorkDeterministically()
    {
        DepotFormulationRepository depotRepo = new(_factory);
        FabricationLotRepository lotRepo = new(_factory);
        ColdChainCustodyRepository coldRepo = new(_factory);

        DateTimeOffset t0 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        await depotRepo.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "dep-cc-1",
            PatientId = "pat-cc-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = t0
        });
        await lotRepo.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot-cc-1",
            DepotId = "dep-cc-1",
            PatientId = "pat-cc-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 16.0,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('a', 64),
            LotStatus = FabricationLotStatus.Released,
            CreatedAtUtc = t0,
            ReleasedAtUtc = t0.AddHours(1)
        });

        // Insert excursions out of chronological order (one with notes, one null)
        await coldRepo.AddThermalExcursionAsync(new ThermalExcursionRecord
        {
            ExcursionId = "exc-2",
            LotId = "lot-cc-1",
            PatientId = "pat-cc-1",
            RecordedAtUtc = t0.AddHours(6),
            TemperatureCelsius = 22.5,
            DurationHours = 2.0,
            StorageTier = ThermalStorageTier.AmbientControlled,
            SensorId = "PROBE-B",
            NotesEncrypted = "enc:note-payload",
            RecordedBy = "tech_b"
        });
        await coldRepo.AddThermalExcursionAsync(new ThermalExcursionRecord
        {
            ExcursionId = "exc-1",
            LotId = "lot-cc-1",
            PatientId = "pat-cc-1",
            RecordedAtUtc = t0.AddHours(2),
            TemperatureCelsius = 4.0,
            DurationHours = 4.0,
            StorageTier = ThermalStorageTier.Refrigerated2To8,
            SensorId = "PROBE-A",
            NotesEncrypted = null,
            RecordedBy = "tech_a"
        });

        IReadOnlyList<ThermalExcursionRecord> excursions = await coldRepo.ListThermalExcursionsByLotAsync("lot-cc-1");
        Assert.Equal(2, excursions.Count);
        Assert.Equal("exc-1", excursions[0].ExcursionId);
        Assert.Null(excursions[0].NotesEncrypted);
        Assert.Equal("exc-2", excursions[1].ExcursionId);
        Assert.Equal("enc:note-payload", excursions[1].NotesEncrypted);

        // Insert custody handoffs out of chronological order
        await coldRepo.AddCustodyHandoffAsync(new CustodyHandoffRecord
        {
            HandoffId = "hnd-2",
            LotId = "lot-cc-1",
            PatientId = "pat-cc-1",
            Stage = CustodyStage.ClinicReceipt,
            FromActor = "courier",
            ToActor = "clinic",
            FacilityCode = "CLINIC-01",
            TemperatureCelsius = -18.5,
            PotencyPercentAtHandoff = 99.5,
            ChainPrevSha256 = new string('1', 64),
            EntrySha256 = new string('2', 64),
            RecordedAtUtc = t0.AddHours(5)
        });
        await coldRepo.AddCustodyHandoffAsync(new CustodyHandoffRecord
        {
            HandoffId = "hnd-1",
            LotId = "lot-cc-1",
            PatientId = "pat-cc-1",
            Stage = CustodyStage.WetLabRelease,
            FromActor = "lab",
            ToActor = "courier",
            FacilityCode = "LAB-01",
            TemperatureCelsius = -20.0,
            PotencyPercentAtHandoff = 99.9,
            ChainPrevSha256 = new string('0', 64),
            EntrySha256 = new string('1', 64),
            RecordedAtUtc = t0.AddHours(1)
        });

        IReadOnlyList<CustodyHandoffRecord> handoffs = await coldRepo.ListCustodyHandoffsByLotAsync("lot-cc-1");
        Assert.Equal(2, handoffs.Count);
        Assert.Equal("hnd-1", handoffs[0].HandoffId);
        Assert.Equal("hnd-2", handoffs[1].HandoffId);

        // Exercise corrupted raw enum fallbacks cleanly
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd1 = session.CreateCommand(
                "UPDATE weaver_lot_thermal_excursions SET storage_tier = 'UnknownTier' WHERE excursion_id = 'exc-1'");
            await cmd1.ExecuteNonQueryAsync();
            await using DbCommand cmd2 = session.CreateCommand(
                "UPDATE weaver_lot_custody_handoffs SET stage = 'UnknownStage' WHERE handoff_id = 'hnd-1'");
            await cmd2.ExecuteNonQueryAsync();
        }

        Assert.Equal(ThermalStorageTier.ThermalExcursion, (await coldRepo.ListThermalExcursionsByLotAsync("lot-cc-1"))[0].StorageTier);
        Assert.Equal(CustodyStage.WetLabRelease, (await coldRepo.ListCustodyHandoffsByLotAsync("lot-cc-1"))[0].Stage);

        // Atomic GDPR Article 17 erasure removes both cold-chain tables + parent lot
        int deleted = await lotRepo.DeleteByPatientIdAsync("pat-cc-1");
        Assert.Equal(5, deleted); // 2 custody + 2 excursions + 1 lot
        Assert.Empty(await coldRepo.ListThermalExcursionsByLotAsync("lot-cc-1"));
        Assert.Empty(await coldRepo.ListCustodyHandoffsByLotAsync("lot-cc-1"));
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
