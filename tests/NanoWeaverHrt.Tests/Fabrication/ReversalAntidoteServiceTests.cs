using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit tests verifying <see cref="ReversalAntidoteService"/> Watson-Crick invading ssDNA design,
/// nearest-neighbor ΔG°37/Tm thermodynamics, ML-DSA-65 pairing signature verification &amp; tamper detection,
/// 24-hour hormone cessation kinetics, irreversible lot neutralization, and validation bounds.
/// </summary>
public sealed class ReversalAntidoteServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary root directory storing the SQLite database and node PQC key files.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Depot formulation repository for seeding parent formulations.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepository;

    /// <summary>
    /// Wet-lab fabrication lot repository for seeding primary batches and reading revocations.
    /// </summary>
    private readonly FabricationLotRepository _lotRepository;

    /// <summary>
    /// Reversal antidote repository under test.
    /// </summary>
    private readonly ReversalAntidoteRepository _antidoteRepository;

    /// <summary>
    /// Node ML-DSA-65 signing key service.
    /// </summary>
    private readonly KeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptographic verification provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// 21 CFR Part 11 tamper-evident compliance audit ledger service.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Reversal antidote domain service under test with deterministic UTC clock.
    /// </summary>
    private readonly ReversalAntidoteService _service;

    /// <summary>
    /// Deterministic UTC timestamp used across pairing and neutralization tests.
    /// </summary>
    private DateTimeOffset _clockNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReversalAntidoteServiceTests"/> class.
    /// </summary>
    public ReversalAntidoteServiceTests()
    {
        // WHAT: Provisions an isolated SQLite database, PQC signing vault, compliance ledger, and ReversalAntidoteService.
        // WHY:  Enables deterministic 100%-reproducible verification of strand-displacement design and ML-DSA-65 signing.
        // HOW:  Sets up NodeSecretStore, PostQuantumCryptoProvider, repositories, and an injectable UTC clock delegate.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_antidote_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _clockNow = new DateTimeOffset(2026, 11, 24, 14, 0, 0, TimeSpan.Zero);

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
        _lotRepository = new FabricationLotRepository(_factory);
        _antidoteRepository = new ReversalAntidoteRepository(_factory);

        NodeSecretStore secretStore = new(config);
        ProfileDataProtector protector = new(secretStore);
        _cryptoProvider = new PostQuantumCryptoProvider();
        SubjectKeyRepository subjectKeyRepo = new(_factory);
        SubjectErasureJournal erasureJournal = new(secretStore);
        SubjectKeyVault vault = new(subjectKeyRepo, protector, _cryptoProvider, erasureJournal);
        vault.InitializeAsync().GetAwaiter().GetResult();
        _keyManagementService = new KeyManagementService(_cryptoProvider, vault, secretStore);
        _auditService = new ComplianceAuditService(_keyManagementService, _cryptoProvider);

        _service = new ReversalAntidoteService(
            _lotRepository,
            _antidoteRepository,
            _keyManagementService,
            _cryptoProvider,
            _auditService,
            () => _clockNow);
    }

    /// <summary>
    /// Verifies OpenAPI example providers across all Milestone 44 request and response models.
    /// </summary>
    [Fact]
    public void ApiAndDomainModels_GetExamples_ReturnValidPopulatedInstances()
    {
        // WHAT: Calls GetExamples() on every API request and domain response record for Milestone 44.
        // WHY:  Guarantees OpenAPI documentation renders realistic examples and exercises 100% of model code.
        // HOW:  Asserts non-empty sequence, positive kinetics metrics, and expected statuses on example DTOs.
        PairReversalAntidoteRequest pairEx = new PairReversalAntidoteRequest().GetExamples();
        Assert.Equal(8, pairEx.ToeholdLengthNt);
        Assert.Equal(5.0, pairEx.MolarExcessRatio);

        SimulateNeutralizationRequest simEx = new SimulateNeutralizationRequest().GetExamples();
        Assert.Equal(24, simEx.HorizonHours);

        AdministerAntidoteRequest adminEx = new AdministerAntidoteRequest().GetExamples();
        Assert.Equal(5.0, adminEx.DosageMg);
        Assert.False(string.IsNullOrWhiteSpace(adminEx.ClinicalReason));

        ReversalAntidotePairingReport pairResp = new ReversalAntidotePairingReport().GetExamples();
        Assert.True(pairResp.SignatureVerified);
        Assert.Equal("ML-DSA-65", pairResp.SignatureAlgorithm);

        NeutralizationSimulationReport simResp = new NeutralizationSimulationReport().GetExamples();
        Assert.Single(simResp.Trajectory);
        Assert.Equal(99.6, simResp.TwentyFourHourCessationPercent);

        AntidoteAdministrationResponse admResp = new AntidoteAdministrationResponse().GetExamples();
        Assert.Equal(FabricationLotStatus.Revoked, admResp.LotStatus);
        Assert.Equal(ReversalAntidoteStatus.Neutralized, admResp.Antidote.Status);
    }

    /// <summary>
    /// Verifies constructor null checks, input validation bounds, and missing-lot handling across <see cref="ReversalAntidoteService"/>.
    /// </summary>
    [Fact]
    public async Task Service_GuardsAndMissingEntities_EnforceBoundaries()
    {
        // WHAT: Tests constructor guards, sequence validation, stoichiometry bounds, and missing-lot responses.
        // WHY:  Ensures physically impossible parameters and unknown lot IDs fail deterministically without side effects.
        // HOW:  Invokes every public method with invalid/missing arguments and asserts exact exception types or null.
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteService(null!, _antidoteRepository, _keyManagementService, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteService(_lotRepository, null!, _keyManagementService, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteService(_lotRepository, _antidoteRepository, null!, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteService(_lotRepository, _antidoteRepository, _keyManagementService, null!, _auditService));
        Assert.Throws<ArgumentNullException>(() => new ReversalAntidoteService(_lotRepository, _antidoteRepository, _keyManagementService, _cryptoProvider, null!));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.PairAntidoteAsync(" ", null, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PairAntidoteAsync("lot-1", null, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { ToeholdLengthNt = 5 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { ToeholdLengthNt = 13 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { MolarExcessRatio = double.NaN }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { MolarExcessRatio = 1.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { MolarExcessRatio = 25.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { StockConcentrationMicromolar = double.PositiveInfinity }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { StockConcentrationMicromolar = 5.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.PairAntidoteAsync("lot-1", new PairReversalAntidoteRequest { StockConcentrationMicromolar = 1500.0 }, "admin"));

        Assert.Null(await _service.PairAntidoteAsync("lot-unknown", null, "admin"));
        Assert.Null(await _service.GetLatestAntidoteReportAsync("lot-unknown"));
        Assert.Null(await _service.SimulateNeutralizationAsync("lot-unknown", null));
        Assert.Null(await _service.AdministerAntidoteAsync("lot-unknown", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Test" }, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetLatestAntidoteReportAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SimulateNeutralizationAsync(" ", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateNeutralizationAsync("lot-1", new SimulateNeutralizationRequest { HorizonHours = 5 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateNeutralizationAsync("lot-1", new SimulateNeutralizationRequest { HorizonHours = 73 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateNeutralizationAsync("lot-1", new SimulateNeutralizationRequest { DosageMg = double.NaN }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateNeutralizationAsync("lot-1", new SimulateNeutralizationRequest { DosageMg = 0.01 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SimulateNeutralizationAsync("lot-1", new SimulateNeutralizationRequest { DosageMg = 300.0 }));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AdministerAntidoteAsync(" ", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Test" }, "admin"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AdministerAntidoteAsync("lot-1", null!, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AdministerAntidoteAsync("lot-1", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Test" }, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.AdministerAntidoteAsync("lot-1", new AdministerAntidoteRequest { DosageMg = 0.01, ClinicalReason = "Test" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AdministerAntidoteAsync("lot-1", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = " " }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AdministerAntidoteAsync("lot-1", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = new string('x', 1001) }, "admin"));

        Assert.Throws<ArgumentNullException>(() => _service.VerifyAntidotePairingSignature(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.VerifyActivePairedAntidoteAsync(" "));

        // Seed a lot with NO antidote yet and confirm missing-antidote branches
        await SeedLotAsync("lot-empty-antidote", "pat-empty", TherapeuticRegimen.MaleToFemale, 18.0);
        Assert.Null(await _service.GetLatestAntidoteReportAsync("lot-empty-antidote"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SimulateNeutralizationAsync("lot-empty-antidote", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AdministerAntidoteAsync("lot-empty-antidote", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Emergency" }, "admin"));
        (bool readyMissing, string missingReason) = await _service.VerifyActivePairedAntidoteAsync("lot-empty-antidote");
        Assert.False(readyMissing);
        Assert.Contains("no reversal antidote paired", missingReason, StringComparison.OrdinalIgnoreCase);

        // Sequence length and base validation
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PairAntidoteAsync("lot-empty-antidote", new PairReversalAntidoteRequest { TargetAptamerLockSequence5To3 = "ACGT" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PairAntidoteAsync("lot-empty-antidote", new PairReversalAntidoteRequest { TargetAptamerLockSequence5To3 = new string('A', 81) }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PairAntidoteAsync("lot-empty-antidote", new PairReversalAntidoteRequest { TargetAptamerLockSequence5To3 = "ACGTACGTACGTACGTACXZ" }, "admin"));
    }

    /// <summary>
    /// Verifies deterministic auto/custom complement design, thermodynamics, ML-DSA-65 signing/tamper detection,
    /// 24h kinetics across MtF/FtM/Custom regimens, emergency administration, and permanent primary lot recall.
    /// </summary>
    [Fact]
    public async Task PairSimulateAndAdminister_EndToEndAcrossRegimensAndTamperChecks_WorkCorrectly()
    {
        // WHAT: Tests complete antidote lifecycle across MtF, FtM, and Custom regimens including tamper rejection.
        // WHY:  Confirms Watson-Crick inversion accuracy, exergonic thermodynamics, >95% 24h hormone shutdown, and fail-closed recall.
        // HOW:  Pairs custom & default sequences, tampers signature metadata, runs simulations, executes administration, and inspects DB/audit state.
        await SeedLotAsync("lot-mtf-rev", "pat-mtf", TherapeuticRegimen.MaleToFemale, 18.0);
        await SeedLotAsync("lot-ftm-rev", "pat-ftm", TherapeuticRegimen.FemaleToMale, 1000.0);
        await SeedLotAsync("lot-cust-rev", "pat-cust", TherapeuticRegimen.Custom, 120.0);

        // 1. Custom 20-nt aptamer lock containing all 4 canonical bases (5'-AAACCCTTTGGGATCGATCG-3')
        // Reverse complement 5'->3': CGATCGATCCCAAAGGGTTT
        ReversalAntidotePairingReport? customPair = await _service.PairAntidoteAsync(
            "lot-mtf-rev",
            new PairReversalAntidoteRequest
            {
                TargetAptamerLockSequence5To3 = "aaaccctttgggatcgatcg",
                ToeholdLengthNt = 8,
                MolarExcessRatio = 5.0,
                StockConcentrationMicromolar = 100.0
            },
            "clinician_ada");

        Assert.NotNull(customPair);
        Assert.Equal("CGATCGATCCCAAAGGGTTT", customPair.Recipe.AntidoteSequence5To3);
        Assert.Equal("CGATCGAT", customPair.Recipe.ToeholdDomain5To3);
        Assert.True(customPair.SignatureVerified);
        Assert.True(customPair.Recipe.BindingDeltaGKcalMol < -25.0);
        Assert.True(customPair.Recipe.MeltingTempCelsius > 50.0);
        Assert.Equal(50.0, customPair.Recipe.GcContentPercent);
        Assert.Equal(ReversalAntidoteStatus.ActiveStandby, customPair.Status);
        Assert.Contains(_auditService.GetAuditRecords(50), e => e.Action == "FABRICATION_ANTIDOTE_PAIR" && e.Resource.Contains(customPair.AntidoteId, StringComparison.Ordinal));

        ReversalAntidotePairingReport? loadedCustom = await _service.GetLatestAntidoteReportAsync("lot-mtf-rev");
        Assert.NotNull(loadedCustom);
        Assert.True(loadedCustom.SignatureVerified);
        Assert.Equal("AAACCCTTTGGGATCGATCG", loadedCustom.Recipe.TargetAptamerLockSequence5To3);

        (bool activeValid, string activeMsg) = await _service.VerifyActivePairedAntidoteAsync("lot-mtf-rev");
        Assert.True(activeValid);
        Assert.Contains("ActiveStandby", activeMsg, StringComparison.Ordinal);

        // 2. Signature tamper verification branches
        ReversalAntidoteRecord storedRecord = (await _antidoteRepository.GetLatestAntidoteByLotAsync("lot-mtf-rev"))!;
        Assert.False(_service.VerifyAntidotePairingSignature(storedRecord with { PairedPayloadSha256 = " " }));
        Assert.False(_service.VerifyAntidotePairingSignature(storedRecord with { SignerKeyId = "foreign-key-id" }));
        Assert.False(_service.VerifyAntidotePairingSignature(storedRecord with { MeltingTempCelsius = storedRecord.MeltingTempCelsius + 1.0 }));
        Assert.False(_service.VerifyAntidotePairingSignature(storedRecord with { PairedSignatureBase64 = Convert.ToBase64String(new byte[64]) }));

        // Corrupt persisted signature directly and confirm VerifyActivePairedAntidoteAsync + AdministerAntidoteAsync fail closed
        await _antidoteRepository.SaveAntidoteAsync(storedRecord with { MeltingTempCelsius = storedRecord.MeltingTempCelsius + 3.0 });
        (bool readyTampered, string reasonTampered) = await _service.VerifyActivePairedAntidoteAsync("lot-mtf-rev");
        Assert.False(readyTampered);
        Assert.Contains("ML-DSA-65", reasonTampered, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AdministerAntidoteAsync("lot-mtf-rev", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Acute event" }, "admin"));

        // Restore untampered signed record before continuing
        await _antidoteRepository.SaveAntidoteAsync(storedRecord);

        // 3. Auto-derived sequences + 24h kinetics simulation across FtM and Custom
        ReversalAntidotePairingReport? ftmPair = await _service.PairAntidoteAsync("lot-ftm-rev", null, "admin");
        ReversalAntidotePairingReport? custPair = await _service.PairAntidoteAsync("lot-cust-rev", null, "admin");
        Assert.NotNull(ftmPair);
        Assert.NotNull(custPair);
        Assert.Equal(28, ftmPair.Recipe.AntidoteSequence5To3.Length);

        NeutralizationSimulationReport? mtfSim = await _service.SimulateNeutralizationAsync("lot-mtf-rev", null);
        NeutralizationSimulationReport? ftmSim = await _service.SimulateNeutralizationAsync("lot-ftm-rev", new SimulateNeutralizationRequest { DosageMg = 120.0, HorizonHours = 36 });
        NeutralizationSimulationReport? custSim = await _service.SimulateNeutralizationAsync("lot-cust-rev", null);

        Assert.NotNull(mtfSim);
        Assert.Equal(25, mtfSim.Trajectory.Count);
        Assert.True(mtfSim.TwentyFourHourCessationPercent >= 95.0);
        Assert.True(mtfSim.EstimatedClearanceHours < 24.0);
        Assert.Equal(550.0, mtfSim.Trajectory[0].PredictedEstradiolPmolL);
        Assert.True(mtfSim.Trajectory[24].PredictedEstradiolPmolL < 60.0);

        Assert.NotNull(ftmSim);
        Assert.Equal(37, ftmSim.Trajectory.Count);
        Assert.Equal(22.5, ftmSim.Trajectory[0].PredictedTestosteroneNmolL);
        Assert.True(ftmSim.Trajectory[24].PredictedTestosteroneNmolL < 2.0);

        Assert.NotNull(custSim);
        Assert.Equal(360.0, custSim.Trajectory[0].PredictedEstradiolPmolL);

        // 4. Execute emergency neutralization on lot-mtf-rev
        _clockNow = _clockNow.AddHours(2);
        AntidoteAdministrationResponse? adm = await _service.AdministerAntidoteAsync(
            "lot-mtf-rev",
            new AdministerAntidoteRequest
            {
                DosageMg = 3.25,
                ClinicalReason = "Immediate reversal requested for persistent hypersensitivity"
            },
            "clinician_ada");

        Assert.NotNull(adm);
        Assert.Equal(ReversalAntidoteStatus.Neutralized, adm.Antidote.Status);
        Assert.Equal(FabricationLotStatus.Revoked, adm.LotStatus);
        Assert.Contains("Emergency Antidote Neutralization", adm.Revocation.RevocationReason, StringComparison.Ordinal);
        Assert.Contains(_auditService.GetAuditRecords(50), e => e.Action == "FABRICATION_ANTIDOTE_ADMINISTER" && e.Resource.Contains(adm.Event.NeutralizationId, StringComparison.Ordinal));

        FabricationLotRecord? revokedLot = await _lotRepository.GetLotByIdAsync("lot-mtf-rev");
        Assert.NotNull(revokedLot);
        Assert.Equal(FabricationLotStatus.Revoked, revokedLot.LotStatus);
        Assert.NotNull(await _lotRepository.GetLotRevocationAsync("lot-mtf-rev"));

        // Re-attempting standby check or second administration fails closed because antidote is Neutralized
        (bool readyAfterNeutralize, string neutralizedReason) = await _service.VerifyActivePairedAntidoteAsync("lot-mtf-rev");
        Assert.False(readyAfterNeutralize);
        Assert.Contains("Neutralized", neutralizedReason, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AdministerAntidoteAsync("lot-mtf-rev", new AdministerAntidoteRequest { DosageMg = 2.0, ClinicalReason = "Duplicate" }, "admin"));
    }

    /// <summary>
    /// Seeds a parent depot formulation and released wet-lab fabrication lot for reversal antidote testing.
    /// </summary>
    /// <param name="lotId">Fabrication lot identifier.</param>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="regimen">Target hormone replacement regimen.</param>
    /// <param name="massMg">Target encapsulated payload mass in milligrams.</param>
    /// <returns>A task representing the asynchronous seed operation.</returns>
    private async Task SeedLotAsync(string lotId, string patientId, TherapeuticRegimen regimen, double massMg)
    {
        // WHAT: Creates parent depot and released fabrication lot rows satisfying DB foreign keys.
        // WHY:  Reversal antidote tables enforce FK links to weaver_fabrication_lots.
        // HOW:  Upserts depot formulation then fabrication lot with Released status.
        string depotId = $"dep_{lotId}";
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = regimen,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = massMg,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = _clockNow.AddDays(-2)
        });
        await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = lotId,
            DepotId = depotId,
            PatientId = patientId,
            Regimen = regimen,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = massMg,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('d', 64),
            LotStatus = FabricationLotStatus.Released,
            CreatedAtUtc = _clockNow.AddDays(-2),
            ReleasedAtUtc = _clockNow.AddDays(-1)
        });
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
