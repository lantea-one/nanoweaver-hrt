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
/// Unit tests verifying <see cref="Migration023_ReversalAntidoteProtocols"/>,
/// <see cref="ReversalAntidoteRepository"/>, persistence model example providers, and GDPR Article 17 erasure cascades.
/// </summary>
public sealed class ReversalAntidoteRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test instance.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReversalAntidoteRepositoryTests"/> class.
    /// </summary>
    public ReversalAntidoteRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 23 schema migrations applied.
        // WHY:  Prevents pooled SQLite handle cross-talk across parallel repository test classes.
        // HOW:  Creates a unique temporary directory and constructs WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_antidote_repo_{Guid.NewGuid():N}");
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
    /// Verifies <see cref="Migration023_ReversalAntidoteProtocols"/> emits valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration023_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        // WHAT: Validates Migration 023 metadata, null guard, and DDL syntax across PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite.
        // WHY:  DuckDB rejects ON DELETE CASCADE while MySQL rejects CREATE INDEX IF NOT EXISTS.
        // HOW:  Invokes GetSql for each supported dialect prefix and asserts expected SQL clauses.
        Migration023_ReversalAntidoteProtocols migration = new();
        Assert.Equal(23, migration.Version);
        Assert.Equal("023_ReversalAntidoteProtocols", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_lot", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_antidotes_lot", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_neutralizations_antidote", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("REAL NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_lot", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="ReversalAntidoteRecord"/> and <see cref="LotNeutralizationEventRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModels_GetExamples_ReturnValidPopulatedRecords()
    {
        // WHAT: Exercises IExamplesProvider<T>.GetExamples on both Milestone 44 persistence records.
        // WHY:  Guarantees OpenAPI schema documentation renders realistic domain payloads and 100% line coverage.
        // HOW:  Instantiates each record, invokes GetExamples, and checks key example identifiers and values.
        ReversalAntidoteRecord antidote = new ReversalAntidoteRecord().GetExamples();
        Assert.Equal("anti_20261124_001", antidote.AntidoteId);
        Assert.Equal(8, antidote.ToeholdLengthNt);
        Assert.Equal(ReversalAntidoteStatus.ActiveStandby, antidote.Status);

        LotNeutralizationEventRecord ev = new LotNeutralizationEventRecord().GetExamples();
        Assert.Equal("neut_20261124_001", ev.NeutralizationId);
        Assert.Equal(6.25, ev.DosageMg);
    }

    /// <summary>
    /// Verifies constructor and method validation guards on <see cref="ReversalAntidoteRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        // WHAT: Exercises null and blank argument guards across every public method on ReversalAntidoteRepository.
        // WHY:  Prevents silent persistence of malformed identifiers or incomplete cryptographic pairing payloads.
        // HOW:  Invokes constructor and each async method with invalid inputs and asserts ArgumentNullException / ArgumentException.
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteRepository(null!));

        ReversalAntidoteRepository repo = new(_factory);
        ReversalAntidoteRecord validAntidote = new ReversalAntidoteRecord().GetExamples();
        LotNeutralizationEventRecord validEvent = new LotNeutralizationEventRecord().GetExamples();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveAntidoteAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { AntidoteId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { PrimaryLotId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { PatientId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { AntidoteSequence5To3 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { PairedPayloadSha256 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { PairedSignatureBase64 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAntidoteAsync(validAntidote with { SignerKeyId = " " }));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetLatestAntidoteByLotAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetAntidoteByIdAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpdateAntidoteStatusAsync(" ", ReversalAntidoteStatus.Neutralized));

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AddNeutralizationEventAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { NeutralizationId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { AntidoteId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { PrimaryLotId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { PatientId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { ClinicalReason = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddNeutralizationEventAsync(validEvent with { ExecutedBy = " " }));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListNeutralizationEventsByLotAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteByPatientIdAsync(" "));
    }

    /// <summary>
    /// Verifies saving, querying, updating, enum fallback parsing, chronological neutralization event ordering,
    /// and atomic GDPR Article 17 erasure cascades via both repositories.
    /// </summary>
    [Fact]
    public async Task Repository_SaveQueryUpdateNeutralizeAndGdprErase_WorksAcrossLifecycle()
    {
        // WHAT: Runs complete persistence lifecycle tests across two patients and two lots.
        // WHY:  Confirms FK integrity, newest-first latest antidote selection, missing row behavior, and GDPR cascades.
        // HOW:  Seeds depots and lots, saves antidotes, logs events out of order, updates status, exercises fallbacks, and erases.
        DepotFormulationRepository depotRepo = new(_factory);
        FabricationLotRepository lotRepo = new(_factory);
        ReversalAntidoteRepository antidoteRepo = new(_factory);

        DateTimeOffset t0 = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        await depotRepo.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "dep-anti-1",
            PatientId = "pat-anti-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = t0
        });
        await lotRepo.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot-anti-1",
            DepotId = "dep-anti-1",
            PatientId = "pat-anti-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 18.0,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('a', 64),
            LotStatus = FabricationLotStatus.Released,
            CreatedAtUtc = t0,
            ReleasedAtUtc = t0.AddHours(1)
        });

        Assert.Null(await antidoteRepo.GetLatestAntidoteByLotAsync("lot-anti-1"));
        Assert.Null(await antidoteRepo.GetAntidoteByIdAsync("anti-missing"));
        Assert.False(await antidoteRepo.UpdateAntidoteStatusAsync("anti-missing", ReversalAntidoteStatus.Revoked));

        ReversalAntidoteRecord anti1 = new()
        {
            AntidoteId = "anti-1",
            PrimaryLotId = "lot-anti-1",
            PatientId = "pat-anti-1",
            AntidoteSequence5To3 = "AGCTAGCTAGCTAGCTAGCTAGCTAGCT",
            ToeholdLengthNt = 8,
            MeltingTempCelsius = 68.5,
            BindingDeltaGKcalMol = -38.4,
            PairedPayloadSha256 = new string('1', 64),
            PairedSignatureBase64 = "c2lnMQ==",
            SignerKeyId = "key-1",
            Status = ReversalAntidoteStatus.ActiveStandby,
            CreatedAtUtc = t0.AddMinutes(10)
        };
        await antidoteRepo.SaveAntidoteAsync(anti1);

        // Saving the same antidoteId idempotently replaces cleanly inside transaction
        await antidoteRepo.SaveAntidoteAsync(anti1 with { MeltingTempCelsius = 69.2 });

        ReversalAntidoteRecord anti2 = anti1 with
        {
            AntidoteId = "anti-2",
            AntidoteSequence5To3 = "TGCATGCATGCATGCATGCATGCATGCA",
            CreatedAtUtc = t0.AddMinutes(30)
        };
        await antidoteRepo.SaveAntidoteAsync(anti2);

        ReversalAntidoteRecord? latest = await antidoteRepo.GetLatestAntidoteByLotAsync("lot-anti-1");
        Assert.NotNull(latest);
        Assert.Equal("anti-2", latest.AntidoteId);

        ReversalAntidoteRecord? byId1 = await antidoteRepo.GetAntidoteByIdAsync("anti-1");
        Assert.NotNull(byId1);
        Assert.Equal(69.2, byId1.MeltingTempCelsius);

        Assert.True(await antidoteRepo.UpdateAntidoteStatusAsync("anti-2", ReversalAntidoteStatus.Neutralized));
        ReversalAntidoteRecord? updatedAnti2 = await antidoteRepo.GetAntidoteByIdAsync("anti-2");
        Assert.NotNull(updatedAnti2);
        Assert.Equal(ReversalAntidoteStatus.Neutralized, updatedAnti2.Status);

        // Append two neutralization events out of chronological order
        await antidoteRepo.AddNeutralizationEventAsync(new LotNeutralizationEventRecord
        {
            NeutralizationId = "neut-2",
            AntidoteId = "anti-2",
            PrimaryLotId = "lot-anti-1",
            PatientId = "pat-anti-1",
            DosageMg = 6.5,
            ClinicalReason = "Follow-up washout bolus",
            EstimatedClearanceHours = 12.0,
            ExecutedBy = "clinician_ada",
            AdministeredAtUtc = t0.AddHours(4)
        });
        await antidoteRepo.AddNeutralizationEventAsync(new LotNeutralizationEventRecord
        {
            NeutralizationId = "neut-1",
            AntidoteId = "anti-2",
            PrimaryLotId = "lot-anti-1",
            PatientId = "pat-anti-1",
            DosageMg = 5.0,
            ClinicalReason = "Initial acute reversal",
            EstimatedClearanceHours = 14.5,
            ExecutedBy = "clinician_ada",
            AdministeredAtUtc = t0.AddHours(2)
        });

        IReadOnlyList<LotNeutralizationEventRecord> events = await antidoteRepo.ListNeutralizationEventsByLotAsync("lot-anti-1");
        Assert.Equal(2, events.Count);
        Assert.Equal("neut-1", events[0].NeutralizationId);
        Assert.Equal("neut-2", events[1].NeutralizationId);

        // Verify unrecognized enum status string falls back cleanly to ActiveStandby
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_lot_reversal_antidotes SET status = 'LegacyUnknownStatus' WHERE antidote_id = 'anti-1'");
            await cmd.ExecuteNonQueryAsync();
        }
        ReversalAntidoteRecord? fallbackRow = await antidoteRepo.GetAntidoteByIdAsync("anti-1");
        Assert.NotNull(fallbackRow);
        Assert.Equal(ReversalAntidoteStatus.ActiveStandby, fallbackRow.Status);

        // Direct ReversalAntidoteRepository erasure removes child events and antidotes (4 rows)
        int deletedByRepo = await antidoteRepo.DeleteByPatientIdAsync("pat-anti-1");
        Assert.Equal(4, deletedByRepo);
        Assert.Null(await antidoteRepo.GetLatestAntidoteByLotAsync("lot-anti-1"));
        Assert.Empty(await antidoteRepo.ListNeutralizationEventsByLotAsync("lot-anti-1"));

        // Verify FabricationLotRepository.DeleteByPatientIdAsync cascades across neutralization events + antidotes + lot
        await depotRepo.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "dep-anti-2",
            PatientId = "pat-anti-2",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 1000.0,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = t0
        });
        await lotRepo.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot-anti-2",
            DepotId = "dep-anti-2",
            PatientId = "pat-anti-2",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 1000.0,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('b', 64),
            LotStatus = FabricationLotStatus.Released,
            CreatedAtUtc = t0,
            ReleasedAtUtc = t0.AddHours(1)
        });
        await antidoteRepo.SaveAntidoteAsync(anti1 with
        {
            AntidoteId = "anti-3",
            PrimaryLotId = "lot-anti-2",
            PatientId = "pat-anti-2"
        });
        await antidoteRepo.AddNeutralizationEventAsync(new LotNeutralizationEventRecord
        {
            NeutralizationId = "neut-3",
            AntidoteId = "anti-3",
            PrimaryLotId = "lot-anti-2",
            PatientId = "pat-anti-2",
            DosageMg = 8.0,
            ClinicalReason = "Cascade erasure test",
            EstimatedClearanceHours = 11.0,
            ExecutedBy = "admin",
            AdministeredAtUtc = t0.AddHours(5)
        });

        int lotCascadeDeleted = await lotRepo.DeleteByPatientIdAsync("pat-anti-2");
        Assert.Equal(3, lotCascadeDeleted); // 1 neutralization + 1 antidote + 1 lot
        Assert.Null(await antidoteRepo.GetLatestAntidoteByLotAsync("lot-anti-2"));
        Assert.Empty(await antidoteRepo.ListNeutralizationEventsByLotAsync("lot-anti-2"));
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
