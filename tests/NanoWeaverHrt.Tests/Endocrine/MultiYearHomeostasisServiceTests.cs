using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Endocrine;

/// <summary>
/// Unit tests verifying <see cref="MultiYearHomeostasisService"/> coupled Day 0–180 hydrogel-to-epigenetic
/// handoff kinetics, 1–10y RK4 mitotic cell-turnover durability, low-fidelity washout detection,
/// linked epigenetic circuit modulation, FDA 21 CFR Part 11 auditing, and DTO examples.
/// </summary>
public sealed class MultiYearHomeostasisServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary root directory holding SQLite database and node cryptographic keys.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite instance.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Persistent depot formulation repository.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepository;

    /// <summary>
    /// Persistent epigenetic circuit repository.
    /// </summary>
    private readonly EpigeneticCircuitRepository _circuitRepository;

    /// <summary>
    /// Persistent multi-year homeostasis repository.
    /// </summary>
    private readonly MultiYearHomeostasisRepository _homeostasisRepository;

    /// <summary>
    /// Semi-annual time-release depot simulator.
    /// </summary>
    private readonly TimeReleaseDepotService _depotService = new();

    /// <summary>
    /// Node PQC key management host supplying ledger integrity signing.
    /// </summary>
    private readonly KeyManagementService _keyManagementService;

    /// <summary>
    /// 21 CFR Part 11 compliance audit ledger service.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Multi-year homeostasis service under test with deterministic UTC clock.
    /// </summary>
    private readonly MultiYearHomeostasisService _service;

    /// <summary>
    /// Deterministic UTC timestamp used by the injected clock delegate.
    /// </summary>
    private DateTimeOffset _clockNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiYearHomeostasisServiceTests"/> class.
    /// </summary>
    public MultiYearHomeostasisServiceTests()
    {
        // WHAT: Provisions isolated SQLite persistence, PQC key store, compliance ledger, and MultiYearHomeostasisService.
        // WHY:  Enables deterministic 100%-reproducible verification of 10-year RK4 durability and washout regimes.
        // HOW:  Initializes temporary test directory, WeaverTestDb, SubjectKeyVault, ComplianceAuditService, and injectable clock.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_myh_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _clockNow = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty,
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();

        _factory = WeaverTestDb.CreateFactory(config);
        _depotRepository = new DepotFormulationRepository(_factory);
        _circuitRepository = new EpigeneticCircuitRepository(_factory);
        _homeostasisRepository = new MultiYearHomeostasisRepository(_factory);

        NodeSecretStore secretStore = new(config);
        ProfileDataProtector protector = new(secretStore);
        PostQuantumCryptoProvider cryptoProvider = new();
        SubjectKeyRepository subjectKeyRepo = new(_factory);
        SubjectErasureJournal journal = new(secretStore);
        SubjectKeyVault vault = new(subjectKeyRepo, protector, cryptoProvider, journal);
        vault.InitializeAsync().GetAwaiter().GetResult();
        _keyManagementService = new KeyManagementService(cryptoProvider, vault, secretStore);
        _auditService = new ComplianceAuditService(_keyManagementService, cryptoProvider);

        _service = new MultiYearHomeostasisService(
            _depotRepository,
            _circuitRepository,
            _homeostasisRepository,
            _depotService,
            _auditService,
            () => _clockNow);
    }

    /// <summary>
    /// Verifies constructor null guards, default UTC clock branch, and API DTO example providers.
    /// </summary>
    [Fact]
    public async Task Service_GuardsAndExamples_ValidateInputsCleanly()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisService(null!, _circuitRepository, _homeostasisRepository, _depotService, _auditService));
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisService(_depotRepository, null!, _homeostasisRepository, _depotService, _auditService));
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisService(_depotRepository, _circuitRepository, null!, _depotService, _auditService));
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisService(_depotRepository, _circuitRepository, _homeostasisRepository, null!, _auditService));
        Assert.Throws<ArgumentNullException>(() => new MultiYearHomeostasisService(_depotRepository, _circuitRepository, _homeostasisRepository, _depotService, null!));

        MultiYearHomeostasisService defaultClockSvc = new(_depotRepository, _circuitRepository, _homeostasisRepository, _depotService, _auditService);
        Assert.Empty(await defaultClockSvc.ListForecastsByDepotAsync("missing-depot"));
        Assert.Null(await defaultClockSvc.GetLatestForecastByDepotAsync("missing-depot"));
        Assert.Null(await defaultClockSvc.SimulateAndPersistAsync("missing-depot", null, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateAndPersistAsync(" ", null, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateAndPersistAsync("d1", null, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ListForecastsByDepotAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetLatestForecastByDepotAsync(" "));

        SimulateMultiYearHomeostasisRequest reqExample = new SimulateMultiYearHomeostasisRequest().GetExamples();
        Assert.Equal(10, reqExample.HorizonYears);
        Assert.Equal("epicirc_20261123_001", reqExample.CircuitId);

        MultiYearHomeostasisForecastReport repExample = new MultiYearHomeostasisForecastReport().GetExamples();
        Assert.Equal("myh_20261124_001", repExample.RunId);
        Assert.NotEmpty(repExample.AnnualCheckpoints);
        Assert.NotEmpty(repExample.TrajectorySamples);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { HorizonYears = 0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { HorizonYears = 11 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { MitoticFidelityRate = 0.85 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { MitoticFidelityRate = double.NaN }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { MitoticFidelityRate = 1.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { TransducedProgenitorFraction = 0.05 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { QuiescentStemCellFraction = -0.1 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { TissueDivisionIntervalDays = 2.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { ClearanceMultiplier = 6.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateAndPersistAsync("d1", new SimulateMultiYearHomeostasisRequest { PromoterReinforcementStrength = 2.5 }, "admin"));
    }

    /// <summary>
    /// Verifies conserved 10-year permanent homeostasis across MtF, FtM, and Custom regimens with seamless Day 90–180 handoff.
    /// </summary>
    [Fact]
    public async Task SimulateAndPersistAsync_MtfFtmCustomConserved_VerifyTenYearHomeostasis()
    {
        DepotFormulationRecord mtfDepot = new()
        {
            DepotId = "depot-mtf-conserved",
            PatientId = "pat-myh-mtf",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow.AddDays(-1)
        };
        DepotFormulationRecord ftmDepot = new()
        {
            DepotId = "depot-ftm-conserved",
            PatientId = "pat-myh-ftm",
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 1025.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow.AddDays(-1)
        };
        DepotFormulationRecord customDepot = new()
        {
            DepotId = "depot-custom-conserved",
            PatientId = "pat-myh-custom",
            Regimen = TherapeuticRegimen.Custom,
            PolymerMatrixType = "Cyclodextrin-Scaffold",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "ZeroOrderErosion",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow.AddDays(-1)
        };

        await _depotRepository.UpsertDepotFormulationAsync(mtfDepot);
        await _depotRepository.UpsertDepotFormulationAsync(ftmDepot);
        await _depotRepository.UpsertDepotFormulationAsync(customDepot);

        MultiYearHomeostasisForecastReport? mtfReport = await _service.SimulateAndPersistAsync(
            mtfDepot.DepotId,
            null,
            "clinician_ada");

        Assert.NotNull(mtfReport);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, mtfReport.DurabilityStatus);
        Assert.False(mtfReport.MitoticDriftAlert);
        Assert.False(mtfReport.HandoffTroughDropDetected);
        Assert.True(mtfReport.ProjectedTenYearTtrPercent >= 95.0);
        Assert.True(mtfReport.HandoffWindowTtrPercent >= 95.0);
        Assert.True(mtfReport.FinalMethylatedProgenitorFraction >= 0.80);
        Assert.Null(mtfReport.WashoutOnsetYear);
        Assert.Equal(11, mtfReport.AnnualCheckpoints.Count);
        Assert.True(mtfReport.AnnualCheckpoints[0].HydrogelContributionFraction > 0.99);
        Assert.True(mtfReport.AnnualCheckpoints[10].HydrogelContributionFraction < 0.01);
        Assert.Contains(_auditService.GetAuditRecords(50), e => e.Action == "DEPOT_MULTIYEAR_SIMULATION_RUN" && e.Resource.Contains(mtfReport.RunId, StringComparison.Ordinal));

        MultiYearHomeostasisForecastReport? ftmReport = await _service.SimulateAndPersistAsync(
            ftmDepot.DepotId,
            new SimulateMultiYearHomeostasisRequest { HorizonYears = 10, MitoticFidelityRate = 0.999 },
            "clinician_ada");

        Assert.NotNull(ftmReport);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, ftmReport.Regimen);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, ftmReport.DurabilityStatus);
        Assert.True(ftmReport.ProjectedTenYearTtrPercent >= 95.0);
        Assert.True(ftmReport.AnnualCheckpoints[10].TestosteroneNmolL >= 15.0);

        MultiYearHomeostasisForecastReport? customReport = await _service.SimulateAndPersistAsync(
            customDepot.DepotId,
            new SimulateMultiYearHomeostasisRequest { HorizonYears = 2 },
            "clinician_ada");

        Assert.NotNull(customReport);
        Assert.Equal(TherapeuticRegimen.Custom, customReport.Regimen);
        Assert.Equal(MultiYearDurabilityStatus.PermanentHomeostasisVerified, customReport.DurabilityStatus);
    }

    /// <summary>
    /// Verifies intentional low-fidelity washout drift, sub-therapeutic Day 90–180 handoff detection, and hormone-miss drift.
    /// </summary>
    [Fact]
    public async Task SimulateAndPersistAsync_LowFidelityAndLowTransduction_DetectWashoutAndHandoffDrop()
    {
        DepotFormulationRecord depot = new()
        {
            DepotId = "depot-washout-test",
            PatientId = "pat-washout",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow.AddDays(-2)
        };
        await _depotRepository.UpsertDepotFormulationAsync(depot);

        // Low DNMT1 fidelity triggers mitotic chromatin washout below floor in late horizon
        MultiYearHomeostasisForecastReport? washoutReport = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest
            {
                HorizonYears = 10,
                MitoticFidelityRate = 0.985,
                TransducedProgenitorFraction = 0.85,
                QuiescentStemCellFraction = 0.35,
                TissueDivisionIntervalDays = 45.0,
                PromoterReinforcementStrength = 1.0
            },
            "researcher_bob");

        Assert.NotNull(washoutReport);
        Assert.Equal(MultiYearDurabilityStatus.MitoticDriftAdvisory, washoutReport.DurabilityStatus);
        Assert.True(washoutReport.MitoticDriftAlert);
        Assert.True(washoutReport.RequiresBoosterDose);
        Assert.True(washoutReport.FinalMethylatedProgenitorFraction < 0.55);
        Assert.NotNull(washoutReport.WashoutOnsetYear);
        Assert.Contains("washout", washoutReport.RecommendedIntervention, StringComparison.OrdinalIgnoreCase);

        // Low initial progenitor fraction causes Day 90-180 handoff trough drop as hydrogel tapers
        MultiYearHomeostasisForecastReport? handoffFailReport = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest
            {
                HorizonYears = 3,
                MitoticFidelityRate = 0.9985,
                TransducedProgenitorFraction = 0.35
            },
            "researcher_bob");

        Assert.NotNull(handoffFailReport);
        Assert.True(handoffFailReport.HandoffTroughDropDetected);
        Assert.Equal(MultiYearDurabilityStatus.MitoticDriftAdvisory, handoffFailReport.DurabilityStatus);
        Assert.Contains("handoff", handoffFailReport.RecommendedIntervention, StringComparison.OrdinalIgnoreCase);

        // Borderline progenitor fraction >= 0.55 triggers !inTarget branch cleanly without falling below 0.55 floor on Day 181
        MultiYearHomeostasisForecastReport? subTherapeuticSerumReport = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest
            {
                HorizonYears = 1,
                MitoticFidelityRate = 0.9999,
                TransducedProgenitorFraction = 0.58,
                PromoterReinforcementStrength = 0.0
            },
            "researcher_bob");

        Assert.NotNull(subTherapeuticSerumReport);
        Assert.True(subTherapeuticSerumReport.FinalMethylatedProgenitorFraction >= 0.55);
        Assert.NotNull(subTherapeuticSerumReport.WashoutOnsetYear);
    }

    /// <summary>
    /// Verifies linked epigenetic circuit coupling, validation against cross-patient/missing/blocked circuits, and GET reconstruction.
    /// </summary>
    [Fact]
    public async Task SimulateAndList_LinkedCircuitsAndPersistedHydration_WorkAcrossPlatformsAndDeletedDepots()
    {
        DepotFormulationRecord depot = new()
        {
            DepotId = "depot-linked-circ",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow.AddDays(-1)
        };
        await _depotRepository.UpsertDepotFormulationAsync(depot);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "missing-circ" },
            "admin"));

        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ-other-pat",
            PatientId = "pat-different",
            Regimen = TherapeuticRegimen.MaleToFemale,
            EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            SeparatrixMargin = 0.5,
            CreatedAtUtc = _clockNow
        }, []);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "circ-other-pat" },
            "admin"));

        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ-blocked",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
            CircuitStatus = EpigeneticCircuitStatus.Blocked,
            SeparatrixMargin = 0.0,
            CreatedAtUtc = _clockNow
        }, []);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "circ-blocked" },
            "admin"));

        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ-crisproff",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            SeparatrixMargin = 0.48,
            CreatedAtUtc = _clockNow
        }, []);

        _clockNow = _clockNow.AddMinutes(2);
        MultiYearHomeostasisForecastReport? r0 = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "circ-crisproff", HorizonYears = 2 },
            "admin");
        Assert.NotNull(r0);

        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ-dnmt",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1,
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            SeparatrixMargin = 0.42,
            CreatedAtUtc = _clockNow
        }, []);

        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ-krab",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            EffectorPlatform = EpigeneticEffectorPlatform.DCas9KrabVp64,
            CircuitStatus = EpigeneticCircuitStatus.Evaluated,
            SeparatrixMargin = 0.30,
            CreatedAtUtc = _clockNow
        }, []);

        _clockNow = _clockNow.AddMinutes(5);
        MultiYearHomeostasisForecastReport? r1 = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "circ-dnmt", HorizonYears = 2 },
            "admin");
        Assert.NotNull(r1);

        _clockNow = _clockNow.AddMinutes(5);
        MultiYearHomeostasisForecastReport? r2 = await _service.SimulateAndPersistAsync(
            depot.DepotId,
            new SimulateMultiYearHomeostasisRequest { CircuitId = "circ-krab", HorizonYears = 2 },
            "admin");
        Assert.NotNull(r2);

        MultiYearHomeostasisForecastReport? latest = await _service.GetLatestForecastByDepotAsync(depot.DepotId);
        Assert.NotNull(latest);
        Assert.Equal(r2.RunId, latest.RunId);
        Assert.Equal("circ-krab", latest.CircuitId);
        Assert.NotEmpty(latest.TrajectorySamples);

        // Save a header-only FtM run (0 stored checkpoints) and exercise hydration fallback when depot was removed or circuit blocked
        await _homeostasisRepository.SaveForecastRunAsync(new MultiYearHomeostasisRunRecord
        {
            RunId = "run-orphan-ftm",
            DepotId = "depot-deleted-ftm",
            CircuitId = "circ-blocked",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.FemaleToMale,
            HorizonYears = 1,
            MitoticFidelityRate = 0.9985,
            TransducedProgenitorFraction = 0.85,
            ProjectedTenYearTtrPercent = 98.0,
            DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
            CreatedAtUtc = _clockNow.AddMinutes(10)
        }, []);

        IReadOnlyList<MultiYearHomeostasisForecastReport> orphanList = await _service.ListForecastsByDepotAsync("depot-deleted-ftm");
        Assert.Single(orphanList);
        Assert.Equal(2, orphanList[0].AnnualCheckpoints.Count);
        Assert.False(orphanList[0].MitoticDriftAlert);

        await _homeostasisRepository.SaveForecastRunAsync(new MultiYearHomeostasisRunRecord
        {
            RunId = "run-orphan-advisory",
            DepotId = "depot-deleted-mtf",
            CircuitId = "missing-later-circ",
            PatientId = "pat-linked",
            Regimen = TherapeuticRegimen.MaleToFemale,
            HorizonYears = 1,
            MitoticFidelityRate = 0.999,
            TransducedProgenitorFraction = 0.85,
            ProjectedTenYearTtrPercent = 90.0,
            DurabilityStatus = MultiYearDurabilityStatus.MitoticDriftAdvisory,
            CreatedAtUtc = _clockNow.AddMinutes(15)
        }, []);

        MultiYearHomeostasisForecastReport? advisoryLatest = await _service.GetLatestForecastByDepotAsync("depot-deleted-mtf");
        Assert.NotNull(advisoryLatest);
        Assert.True(advisoryLatest.MitoticDriftAlert);
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
