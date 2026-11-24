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
/// Unit tests verifying <see cref="Migration021_EpigeneticToggleCircuits"/>,
/// <see cref="EpigeneticCircuitRepository"/>, persistence DTO example providers,
/// and GDPR Article 17 patient erasure isolation.
/// </summary>
public sealed class EpigeneticCircuitRepositoryTests : IDisposable
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
    /// Initializes a new instance of the <see cref="EpigeneticCircuitRepositoryTests"/> class.
    /// </summary>
    public EpigeneticCircuitRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 21 schema migrations applied.
        // WHY:  Guarantees deterministic, side-effect-free epigenetic circuit repository testing.
        // HOW:  Creates a unique temporary directory and invokes WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_epigenetic_repo_{Guid.NewGuid():N}");
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
    /// Verifies <see cref="Migration021_EpigeneticToggleCircuits"/> generates valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration021_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        Migration021_EpigeneticToggleCircuits migration = new();
        Assert.Equal(21, migration.Version);
        Assert.Equal("021_EpigeneticToggleCircuits", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_circuits_patient", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT fk_weaver_epigenetic_guides_circuit", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("REAL NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_circuit", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="EpigeneticCircuitRecord"/> and <see cref="EpigeneticGuideRnaRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModels_GetExamples_ReturnValidPopulatedRecords()
    {
        EpigeneticCircuitRecord circuit = new EpigeneticCircuitRecord().GetExamples();
        Assert.Equal("epicirc_20261123_001", circuit.CircuitId);
        Assert.Equal(EpigeneticEffectorPlatform.CrisprOffOn, circuit.EffectorPlatform);
        Assert.Equal(EpigeneticCircuitStatus.Evaluated, circuit.CircuitStatus);

        EpigeneticGuideRnaRecord guide = new EpigeneticGuideRnaRecord().GetExamples();
        Assert.Equal("g_20261123_001", guide.GuideId);
        Assert.Equal("CYP19A1", guide.TargetGeneSymbol);
        Assert.Equal(GuideRnaModulationMode.Activate, guide.ModulationMode);
    }

    /// <summary>
    /// Verifies constructor and method argument validation guards on <see cref="EpigeneticCircuitRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitRepository(null!));
        EpigeneticCircuitRepository repo = new(_factory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveCircuitAsync(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = "c", PatientId = "p" }, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = " ", PatientId = "p" }, []));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = "c", PatientId = " " }, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = "c", PatientId = "p" }, [null!]));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = "c", PatientId = "p" }, [new EpigeneticGuideRnaRecord { GuideId = " ", TargetGeneSymbol = "CYP19A1" }]));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveCircuitAsync(new EpigeneticCircuitRecord { CircuitId = "c", PatientId = "p" }, [new EpigeneticGuideRnaRecord { GuideId = "g", TargetGeneSymbol = " " }]));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetCircuitByIdAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListCircuitsByPatientAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListGuidesByCircuitAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpdateCircuitEvaluationAsync(" ", EpigeneticCircuitStatus.Blocked, 0, 0, 0, null, null, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteByPatientIdAsync(" "));
    }

    /// <summary>
    /// Verifies atomic save, replacement upsert, evaluation updates, enum fallbacks, guide ordering, and GDPR erasure isolation.
    /// </summary>
    [Fact]
    public async Task Repository_RoundTripUpdateEnumFallbackAndGdprErasure_WorkDeterministically()
    {
        EpigeneticCircuitRepository repo = new(_factory);
        DateTimeOffset t0 = new(2026, 11, 23, 14, 0, 0, TimeSpan.Zero);

        Assert.Null(await repo.GetCircuitByIdAsync("missing-circuit"));
        Assert.False(await repo.UpdateCircuitEvaluationAsync("missing-circuit", EpigeneticCircuitStatus.Blocked, 0.0, 0.0, 90.0, null, null, null, null));

        EpigeneticCircuitRecord c2 = new()
        {
            CircuitId = "circ-2",
            PatientId = "pat-epi-1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = TargetTissueType.GonadalTarget,
            EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1,
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            PredictedTtrPercent = 95.5,
            SeparatrixMargin = 0.44,
            MinOffTargetSpecificityScore = 99.1,
            NotesEncrypted = "enc:secret-notes",
            CreatedAtUtc = t0.AddHours(2),
            ApprovedAtUtc = t0.AddHours(3),
            ApprovalPayloadSha256 = new string('a', 64),
            ApprovalSignatureBase64 = "c2ln",
            ApprovalSignerKeyId = "key-1"
        };

        EpigeneticCircuitRecord c1 = new()
        {
            CircuitId = "circ-1",
            PatientId = "pat-epi-1",
            Regimen = TherapeuticRegimen.FemaleToMale,
            TargetTissue = TargetTissueType.AdrenalSteroidogenic,
            EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
            CircuitStatus = EpigeneticCircuitStatus.Draft,
            PredictedTtrPercent = 88.0,
            SeparatrixMargin = 0.31,
            MinOffTargetSpecificityScore = 98.4,
            NotesEncrypted = null,
            CreatedAtUtc = t0,
            ApprovedAtUtc = null
        };

        EpigeneticCircuitRecord otherPatientCircuit = new()
        {
            CircuitId = "circ-other",
            PatientId = "pat-epi-2",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = TargetTissueType.GonadalTarget,
            EffectorPlatform = EpigeneticEffectorPlatform.DCas9KrabVp64,
            CircuitStatus = EpigeneticCircuitStatus.Evaluated,
            PredictedTtrPercent = 91.0,
            SeparatrixMargin = 0.38,
            MinOffTargetSpecificityScore = 98.5,
            CreatedAtUtc = t0
        };

        await repo.SaveCircuitAsync(c2,
        [
            new EpigeneticGuideRnaRecord
            {
                GuideId = "g-b",
                CircuitId = "circ-2",
                PatientId = "pat-epi-1",
                TargetGeneSymbol = "SRD5A2",
                ModulationMode = GuideRnaModulationMode.Repress,
                ProtospacerSequence = "CGATCGATGCTAGCTAGCTA",
                PamSequence = "CGG",
                CfdSpecificityScore = 99.1,
                PredictedFoldChange = 0.14
            },
            new EpigeneticGuideRnaRecord
            {
                GuideId = "g-a",
                CircuitId = "circ-2",
                PatientId = "pat-epi-1",
                TargetGeneSymbol = "CYP19A1",
                ModulationMode = GuideRnaModulationMode.Activate,
                ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
                PamSequence = "AGG",
                CfdSpecificityScore = 99.4,
                PredictedFoldChange = 4.2
            }
        ]);
        await repo.SaveCircuitAsync(c1, []);
        await repo.SaveCircuitAsync(otherPatientCircuit, []);

        IReadOnlyList<EpigeneticCircuitRecord> listed = await repo.ListCircuitsByPatientAsync("pat-epi-1");
        Assert.Equal(2, listed.Count);
        Assert.Equal("circ-1", listed[0].CircuitId);
        Assert.Null(listed[0].NotesEncrypted);
        Assert.Null(listed[0].ApprovedAtUtc);
        Assert.Equal("circ-2", listed[1].CircuitId);
        Assert.Equal("enc:secret-notes", listed[1].NotesEncrypted);
        Assert.Equal("key-1", listed[1].ApprovalSignerKeyId);

        IReadOnlyList<EpigeneticGuideRnaRecord> guides = await repo.ListGuidesByCircuitAsync("circ-2");
        Assert.Equal(2, guides.Count);
        Assert.Equal("g-a", guides[0].GuideId);
        Assert.Equal("g-b", guides[1].GuideId);

        // Update c1 to Approved with signature metadata, then verify GetCircuitByIdAsync
        Assert.True(await repo.UpdateCircuitEvaluationAsync(
            "circ-1",
            EpigeneticCircuitStatus.Approved,
            97.2,
            0.49,
            99.0,
            t0.AddHours(1),
            new string('b', 64),
            "c2lnMg==",
            "key-2"));

        EpigeneticCircuitRecord? loadedC1 = await repo.GetCircuitByIdAsync("circ-1");
        Assert.NotNull(loadedC1);
        Assert.Equal(EpigeneticCircuitStatus.Approved, loadedC1.CircuitStatus);
        Assert.Equal("key-2", loadedC1.ApprovalSignerKeyId);
        Assert.NotNull(loadedC1.ApprovedAtUtc);

        // Exercise corrupted raw enum fallback cleanly
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_epigenetic_circuits SET circuit_status = 'CorruptStatus' WHERE circuit_id = 'circ-1'");
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(EpigeneticCircuitStatus.Draft, (await repo.GetCircuitByIdAsync("circ-1"))!.CircuitStatus);

        // GDPR Article 17 erasure deletes only pat-epi-1 (2 guides + 2 circuits = 4) and leaves pat-epi-2 intact
        int deleted = await repo.DeleteByPatientIdAsync("pat-epi-1");
        Assert.Equal(4, deleted);
        Assert.Empty(await repo.ListCircuitsByPatientAsync("pat-epi-1"));
        Assert.Empty(await repo.ListGuidesByCircuitAsync("circ-2"));
        Assert.NotNull(await repo.GetCircuitByIdAsync("circ-other"));
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
