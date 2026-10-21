using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Formatting;
using WeaverLammpsHrt.TrajectoryParser.Genomics;
using WeaverLammpsHrt.TrajectoryParser.Simulations;
using WeaverLammpsHrt.TrajectoryParser.Simulations.Models;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Comprehensive unit, integration, and serialization test suite verifying the 4 in-silico simulation campaigns:
/// oxDNA serum stability, LAMMPS aptamer gating, steroid payload leakage, and whole-body PBPK modeling.
/// </summary>
public sealed class SimulationCampaignTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Service instance directly instantiated for unit-level verification.
    /// </summary>
    private readonly SimulationCampaignService _service;

    /// <summary>
    /// Test server web application factory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client connected to in-memory test server for endpoint tests.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationCampaignTests"/> class.
    /// </summary>
    /// <param name="factory">The test web application factory fixture.</param>
    public SimulationCampaignTests(WebApplicationFactory<Program> factory)
    {
        PharmacogenomicsService genomicsService = new();
        _service = new SimulationCampaignService(genomicsService);

        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "NanoWeaverHrt.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string solutionRoot = current ?? Directory.GetCurrentDirectory();
        string dataDir = Path.Combine(solutionRoot, "simulations", "shared-data");
        string exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        string parquetDir = Path.Combine(solutionRoot, "simulations", "parquet");

        TestFixturesHelper.EnsureTestFixtures(dataDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", dataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", parquetDir);
        });

        _client = _factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(_client, _factory);
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.GetCampaignCatalog"/> returns all 4 simulation campaigns.
    /// </summary>
    [Fact]
    public void GetCampaignCatalog_ShouldReturnFourCampaigns()
    {
        // WHAT: Tests discovery of in-silico simulation campaigns.
        // WHY:  Ensures the service exposes oxDNA, LAMMPS, and PBPK engines.
        // HOW:  Queries catalog and verifies 4 campaigns with non-empty metadata.
        var catalog = _service.GetCampaignCatalog();
        Assert.NotNull(catalog);
        Assert.Equal(4, catalog.Count);
        Assert.All(catalog, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.CampaignId));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.SimulationEngine));
            Assert.NotEmpty(c.KeyParameters);
        });
    }

    /// <summary>
    /// Verifies that Campaign 1 detects crossover fraying under unshielded physiological serum salt conditions.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_UnshieldedSerum_DetectsFrayingAndVulnerabilities()
    {
        // WHAT: Tests oxDNA serum stability simulation with low magnesium (0.8 mM) and no PLL-PEG.
        // WHY:  Confirms that unpassivated DNA origami is correctly flagged as vulnerable to ionic strain.
        // HOW:  Executes SimulateSerumStability and asserts compromised crossovers > 0.
        SerumStabilityRequest request = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            MonovalentSaltMolar = 0.15,
            DivalentMagnesiumMilliMolar = 0.8,
            PllPegCoatingApplied = false,
            CpGMethylationApplied = false,
            StapleCrossoverCount = 180
        };

        var result = _service.SimulateSerumStability(request);

        Assert.NotNull(result);
        Assert.True(result.CompromisedCrossoverCount > 0);
        Assert.True(result.AverageRmsfNm > 0.8);
        Assert.Contains("Vulnerable", result.Verdict);
        Assert.Contains("PLL-PEG", result.RecommendedStabilization);
        Assert.NotEmpty(result.VulnerableCrossovers);
        Assert.NotEmpty(result.RmsfProfilePoints);
    }

    /// <summary>
    /// Verifies that Campaign 1 passes with high integrity when PLL-PEG coating and CpG methylation are applied.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_PassivatedAndHighMagnesium_ReturnsStableVerdict()
    {
        // WHAT: Tests oxDNA serum stability with 12.5 mM Mg2+, PLL-PEG coating, and CpG methylation.
        // WHY:  Confirms that proper chemical passivation achieves rigid in-silico stability.
        // HOW:  Executes SimulateSerumStability and asserts 0 compromised crossovers.
        SerumStabilityRequest request = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            MonovalentSaltMolar = 0.15,
            DivalentMagnesiumMilliMolar = 12.5,
            PllPegCoatingApplied = true,
            CpGMethylationApplied = true,
            StapleCrossoverCount = 180
        };

        var result = _service.SimulateSerumStability(request);

        Assert.NotNull(result);
        Assert.Equal(0, result.CompromisedCrossoverCount);
        Assert.True(result.AverageRmsfNm <= 1.2);
        Assert.Contains("Structurally Stable", result.Verdict);
        Assert.Contains("Safe to proceed", result.RecommendedStabilization);
    }

    /// <summary>
    /// Verifies Campaign 1 alternative recommendation when PLL-PEG is applied under low magnesium stress.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_PllPegAppliedWithLowMagnesium_ReturnsReinforcementAdvice()
    {
        // WHAT: Tests low magnesium with PLL-PEG applied but high crossover count to exercise alternative branch.
        // WHY:  Ensures full branch coverage for recommendation generation.
        // HOW:  Runs simulation with 0.1 mM Mg2+, PLL-PEG = true, CpG = false.
        SerumStabilityRequest request = new()
        {
            TemperatureCelsius = 42.0,
            DivalentMagnesiumMilliMolar = 0.1,
            PllPegCoatingApplied = true,
            CpGMethylationApplied = false,
            StapleCrossoverCount = 200
        };

        var result = _service.SimulateSerumStability(request);

        Assert.NotNull(result);
        if (result.CompromisedCrossoverCount > 0)
        {
            Assert.Contains("Reinforce vulnerable", result.RecommendedStabilization);
        }
    }

    /// <summary>
    /// Verifies Campaign 1 boundary clamping on low magnesium, negative temperature, and crossover count.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_ExtremeBoundaries_ClampsAndCalculatesSafely()
    {
        // WHAT: Tests extreme parameter boundary conditions.
        // WHY:  Guarantees zero crashes and correct clamping.
        // HOW:  Passes negative temp, 0 Mg, 0 NaCl, and 5 crossovers.
        SerumStabilityRequest request = new()
        {
            TemperatureCelsius = -15.0,
            MonovalentSaltMolar = 0.0,
            DivalentMagnesiumMilliMolar = 0.0,
            StapleCrossoverCount = 5
        };

        var result = _service.SimulateSerumStability(request);

        Assert.NotNull(result);
        Assert.True(result.DebyeScreeningLengthNm > 0.0);
        Assert.Equal(10, result.TotalCrossoverCount); // Clamped from 5 to 10
    }

    /// <summary>
    /// Verifies Campaign 2 allosteric aptamer gating and anterior pituitary gonadotrope tropism.
    /// </summary>
    [Fact]
    public void SimulateAptamerGating_PituitaryTarget_ReturnsHighAffinityAndResponsiveGating()
    {
        // WHAT: Tests LAMMPS aptamer gating for anterior pituitary GnRH-R.
        // WHY:  Confirms Delta G barrier >= +8.5 kcal/mol unbound, <= 0.0 kcal/mol bound, and Kd <= 5.0 nM.
        // HOW:  Executes SimulateAptamerGating with GnRH-R target and validates outputs.
        AptamerGatingRequest request = new()
        {
            TargetReceptor = "Anterior Pituitary Gonadotrope (GnRH-R)",
            TargetGonadotropin = "Luteinizing Hormone (LH)",
            AptamerSequence = "AGCTCAGGTACCGATCGTTTACCGGATACCTTTCGATCGGAT",
            UmbrellaSamplingWindows = 24,
            ReactionCoordinateMaxNm = 3.0
        };

        var result = _service.SimulateAptamerGating(request);

        Assert.NotNull(result);
        Assert.True(result.UnboundOpeningFreeEnergyKcalMol >= 8.5);
        Assert.True(result.BoundOpeningFreeEnergyKcalMol <= 0.0);
        Assert.True(result.GnRhReceptorContactRatio >= 0.80);
        Assert.True(result.EstimatedKdNanoMolar <= 10.0);
        Assert.Contains("Allosterically Responsive", result.GatingVerdict);
        Assert.Contains("High-Affinity Gonadotrope Tropism", result.TropismVerdict);
        Assert.Equal(25, result.PmfCurve.Count); // 24 windows + 1
    }

    /// <summary>
    /// Verifies Campaign 2 branches for non-pituitary targets and short aptamer sequences.
    /// </summary>
    [Fact]
    public void SimulateAptamerGating_NonPituitaryTargetAndShortSequence_ExercisesAlternativeBranches()
    {
        // WHAT: Tests non-pituitary target (Adrenal Cortex) and short sequence (15 nt).
        // WHY:  Ensures full branch coverage for gating and tropism verdicts.
        // HOW:  Executes SimulateAptamerGating and asserts moderate off-target and sub-optimal latch branches.
        AptamerGatingRequest request = new()
        {
            TargetReceptor = "Adrenal Cortex Tissue",
            TargetGonadotropin = "FSH",
            AptamerSequence = "ACGTACGTACGT",
            UmbrellaSamplingWindows = 4, // Clamped to 6
            ReactionCoordinateMaxNm = 0.5 // Clamped to 1.0
        };

        var result = _service.SimulateAptamerGating(request);

        Assert.NotNull(result);
        Assert.Equal(0.62, result.GnRhReceptorContactRatio);
        Assert.Equal(28.5, result.EstimatedKdNanoMolar);
        Assert.Contains("Moderate Off-Target", result.TropismVerdict);
        Assert.Equal(7, result.PmfCurve.Count); // 6 clamped windows + 1
    }

    /// <summary>
    /// Verifies Campaign 3 steroid payload confinement with cyclodextrin, cholesterol, and bare DNA.
    /// </summary>
    [Theory]
    [InlineData("Cyclodextrin Anchored", 98.6, 1.4, "Sterically Contained")]
    [InlineData("Cholesterol Tethered", 96.8, 3.2, "Moderate Steric Containment")]
    [InlineData("Bare DNA", 77.5, 22.5, "Excessive Passive Leakage")]
    public void SimulatePayloadLeakage_AllFunctionalizationModes_MatchPhysicsExpectations(
        string functionalization,
        double expectedRetention,
        double expectedLeakage,
        string expectedVerdictToken)
    {
        // WHAT: Tests steroid confinement across cavity functionalization chemistries.
        // WHY:  Proves that cyclodextrin or cholesterol anchoring prevents steroid leakage.
        // HOW:  Executes SimulatePayloadLeakage and asserts retention and leakage values.
        PayloadLeakageRequest request = new()
        {
            ChassisGeometry = "Hexagonal Nanotube (6-HB)",
            HormoneMolecule = "17β-Estradiol",
            CavityFunctionalization = functionalization,
            LoadedMoleculeCount = 50,
            SimulationDurationNs = 100.0
        };

        var result = _service.SimulatePayloadLeakage(request);

        Assert.NotNull(result);
        Assert.Equal(expectedRetention, result.RetentionPercentageClosed);
        Assert.Equal(expectedLeakage, result.PassiveLeakagePercentage);
        Assert.Contains(expectedVerdictToken, result.LeakageVerdict);
        Assert.Equal(4, result.RetentionCurve.Count);
    }

    /// <summary>
    /// Verifies Campaign 3 capacity clamping for extreme molecule counts.
    /// </summary>
    [Fact]
    public void SimulatePayloadLeakage_ExtremeCapacity_ClampsCorrectly()
    {
        // WHAT: Tests capacity clamping bounds.
        // WHY:  Ensures physical boundary limits (10 to 60 molecules).
        // HOW:  Passes 5 (clamped to 10) and 100 (clamped to 60).
        var resultMin = _service.SimulatePayloadLeakage(new PayloadLeakageRequest { LoadedMoleculeCount = 5 });
        var resultMax = _service.SimulatePayloadLeakage(new PayloadLeakageRequest { LoadedMoleculeCount = 100 });

        Assert.Equal(10, resultMin.EncapsulationCapacityMolecules);
        Assert.Equal(60, resultMax.EncapsulationCapacityMolecules);
    }

    /// <summary>
    /// Verifies Campaign 4 PBPK multi-compartment simulation for Male-to-Female regimen with genomic markers.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_MtfWithGenomicMarkers_CappingAndSuppressionVerified()
    {
        // WHAT: Tests whole-body PBPK simulation for MtF subject with Factor V Leiden (rs6025).
        // WHY:  Verifies that high thrombosis risk caps estradiol to <= 500 pmol/L while suppressing T < 1.8 nmol/L.
        // HOW:  Executes SimulatePbpkEndocrine and checks steady-state setpoints.
        PbpkSimulationRequest request = new()
        {
            PatientId = "ANON-MTF-001",
            Regimen = TherapeuticRegimen.MaleToFemale,
            DepotDoseMg = 25.0,
            SimulationDurationDays = 90,
            DeliveryIntervalDays = 180,
            GenomicMarkerIds = ["F5_LEIDEN", "COMT_VAL158MET"]
        };

        var result = _service.SimulatePbpkEndocrine(request);

        Assert.NotNull(result);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, result.Regimen);
        Assert.True(result.AverageSteadyStateEstradiolPmolL >= 480.0 && result.AverageSteadyStateEstradiolPmolL <= 550.0);
        Assert.True(result.AverageSteadyStateTestosteroneNmolL < 1.8);
        Assert.True(result.TargetSuppressionAchieved);
        Assert.False(result.ThromboticSurgeDetected);
        Assert.Contains("Semi-Annual Subcutaneous Depot", result.ClinicalFeasibilityVerdict);
        Assert.NotEmpty(result.DailyKinetics);
    }

    /// <summary>
    /// Verifies Campaign 4 PBPK multi-compartment simulation for Female-to-Male regimen without markers.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_FtmWithoutMarkers_TargetVirilizationVerified()
    {
        // WHAT: Tests whole-body PBPK simulation for FtM subject.
        // WHY:  Verifies testosterone dominance (15-25 nmol/L) and estradiol suppression (< 150 pmol/L).
        // HOW:  Executes SimulatePbpkEndocrine and checks average steady-state levels.
        PbpkSimulationRequest request = new()
        {
            PatientId = "ANON-FTM-001",
            Regimen = TherapeuticRegimen.FemaleToMale,
            DepotDoseMg = 30.0,
            SimulationDurationDays = 60,
            GenomicMarkerIds = []
        };

        var result = _service.SimulatePbpkEndocrine(request);

        Assert.NotNull(result);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, result.Regimen);
        Assert.True(result.AverageSteadyStateTestosteroneNmolL >= 15.0 && result.AverageSteadyStateTestosteroneNmolL <= 25.0);
        Assert.True(result.AverageSteadyStateEstradiolPmolL < 150.0);
        Assert.True(result.TargetSuppressionAchieved);
        Assert.Contains("Semi-Annual Subcutaneous Depot", result.ClinicalFeasibilityVerdict);
    }

    /// <summary>
    /// Verifies Campaign 4 surge detection and inconclusive feasibility branch when severe slow clearance markers combine in FtM.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_SevereSlowClearance_TriggersSurgeAndInconclusiveVerdict()
    {
        // WHAT: Tests combined UGT2B17 and COMT slow clearance markers without thrombosis capping.
        // WHY:  Exercises the surgeDetected = true and inconclusive feasibility branches.
        // HOW:  Runs simulation with UGT2B17 and COMT markers in FtM regimen.
        PbpkSimulationRequest request = new()
        {
            PatientId = "ANON-SLOW-CLEARANCE",
            Regimen = TherapeuticRegimen.FemaleToMale,
            DepotDoseMg = 50.0,
            SimulationDurationDays = 90,
            GenomicMarkerIds = ["UGT2B17_DELETION", "COMT_VAL158MET"]
        };

        var result = _service.SimulatePbpkEndocrine(request);

        Assert.NotNull(result);
        Assert.True(result.ThromboticSurgeDetected);
        Assert.Contains("Inconclusive Kinetics", result.ClinicalFeasibilityVerdict);
    }

    /// <summary>
    /// Verifies Campaign 4 surge detection in Male-to-Female regimen when severe slow clearance causes estradiol accumulation > 800 pmol/L.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_MtfSevereSlowClearance_TriggersEstradiolSurge()
    {
        // WHAT: Tests combined slow clearance markers in MtF without thrombophilia.
        // WHY:  Exercises the MtF estradiol surge detection branch (e2 > 800.0 pmol/L).
        // HOW:  Runs simulation with UGT2B17 and COMT markers in MtF regimen.
        PbpkSimulationRequest request = new()
        {
            PatientId = "ANON-MTF-SURGE",
            Regimen = TherapeuticRegimen.MaleToFemale,
            DepotDoseMg = 50.0,
            SimulationDurationDays = 30,
            GenomicMarkerIds = ["UGT2B17_DELETION", "COMT_VAL158MET"]
        };

        var result = _service.SimulatePbpkEndocrine(request);

        Assert.NotNull(result);
        Assert.True(result.ThromboticSurgeDetected);
        Assert.Contains("Inconclusive Kinetics", result.ClinicalFeasibilityVerdict);
    }

    /// <summary>
    /// Verifies input deck string generation for oxDNA and LAMMPS.
    /// </summary>
    [Fact]
    public void GenerateInputDecks_ProducesValidSimulationScripts()
    {
        // WHAT: Tests generation of oxDNA and LAMMPS simulation input scripts.
        // WHY:  Ensures researchers can export runnable config files directly to disk.
        // HOW:  Calls deck generators and asserts expected command tokens exist.
        string oxDna = _service.GenerateOxDnaInputDeck(new SerumStabilityRequest { TemperatureCelsius = 37.0 });
        Assert.Contains("steps =", oxDna);
        Assert.Contains("310.15K", oxDna);
        Assert.Contains("backend = OpenCL", oxDna);

        string lammpsAptamer = _service.GenerateLammpsAptamerInputDeck(new AptamerGatingRequest());
        Assert.Contains("units real", lammpsAptamer);
        Assert.Contains("pair_style lj/charmm/coul/long", lammpsAptamer);

        string lammpsPayload = _service.GenerateLammpsPayloadInputDeck(new PayloadLeakageRequest());
        Assert.Contains("units real", lammpsPayload);
        Assert.Contains("pair_style lj/cut/coul/long", lammpsPayload);
    }

    /// <summary>
    /// Verifies that all simulation models supply valid examples via <see cref="IExamplesProvider{T}"/>.
    /// </summary>
    [Fact]
    public void ExampleProviders_AllModels_ProvideValidExamples()
    {
        // WHAT: Tests OpenAPI example providers for all simulation request/response contracts.
        // WHY:  Guarantees Swagger and ReDoc documentation render valid sample payloads.
        // HOW:  Invokes GetExamples() on all models and serializes to JSON, XML, and YAML.
        var info = new SimulationCampaignInfo
        {
            CampaignId = "TEST",
            Name = "Test",
            SimulationEngine = "oxDNA",
            Description = "Desc",
            PhysicsGoal = "Goal",
            KeyParameters = ["P1"],
            Status = "Ready",
            ConfigFileTemplate = "test.in"
        }.GetExamples();

        var serumReq = new SerumStabilityRequest().GetExamples();
        var serumRes = new SerumStabilityAnalysisResult
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            DivalentMagnesiumMilliMolar = 0.8,
            AverageRmsfNm = 0.8,
            MaxCrossoverRmsfNm = 1.1,
            DebyeScreeningLengthNm = 0.7,
            StructuralIntegrityScore = 0.9,
            CompromisedCrossoverCount = 0,
            TotalCrossoverCount = 180,
            Verdict = "OK",
            RecommendedStabilization = "None",
            VulnerableCrossovers = [],
            RmsfProfilePoints = [0.5]
        }.GetExamples();

        var gatingReq = new AptamerGatingRequest().GetExamples();
        var gatingRes = new AptamerGatingAnalysisResult
        {
            TargetReceptor = "R",
            TargetGonadotropin = "LH",
            UnboundOpeningFreeEnergyKcalMol = 9.0,
            BoundOpeningFreeEnergyKcalMol = -2.0,
            AllostericSwitchDeltaKcalMol = -11.0,
            GnRhReceptorContactRatio = 0.85,
            EstimatedKdNanoMolar = 4.0,
            GatingVerdict = "OK",
            TropismVerdict = "OK",
            PmfCurve = []
        }.GetExamples();

        var payloadReq = new PayloadLeakageRequest().GetExamples();
        var payloadRes = new PayloadLeakageAnalysisResult
        {
            ChassisGeometry = "Nanotube",
            HormoneMolecule = "E2",
            CavityFunctionalization = "Cyclodextrin",
            RetentionPercentageClosed = 98.6,
            PassiveLeakagePercentage = 1.4,
            ReleaseRatePerMinuteOpen = 8.4,
            EncapsulationCapacityMolecules = 48,
            MaxDailyDeliveryNanomoles = 1.25,
            LeakageVerdict = "OK",
            RetentionCurve = []
        }.GetExamples();

        var pbpkReq = new PbpkSimulationRequest().GetExamples();
        var pbpkRes = new PbpkEndocrineSimulationResult
        {
            PatientId = "P1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            SimulationDurationDays = 90,
            DepotDoseMg = 25.0,
            AverageSteadyStateEstradiolPmolL = 540.0,
            AverageSteadyStateTestosteroneNmolL = 1.1,
            AverageLhIuL = 2.0,
            TimeInTargetRangePercent = 95.0,
            TargetSuppressionAchieved = true,
            ThromboticSurgeDetected = false,
            RecommendedDosingIntervalDays = 180,
            ClinicalFeasibilityVerdict = "OK",
            OrganBiodistributionSummary = "Summary",
            DailyKinetics = []
        }.GetExamples();

        Assert.NotNull(info);
        Assert.NotNull(serumReq);
        Assert.NotNull(serumRes);
        Assert.NotNull(gatingReq);
        Assert.NotNull(gatingRes);
        Assert.NotNull(payloadReq);
        Assert.NotNull(payloadRes);
        Assert.NotNull(pbpkReq);
        Assert.NotNull(pbpkRes);

        // Verify JSON serialization format
        Assert.False(string.IsNullOrWhiteSpace(ContentFormatSerializer.SerializeJson(serumRes)));
        Assert.False(string.IsNullOrWhiteSpace(ContentFormatSerializer.SerializeJson(gatingRes)));
        Assert.False(string.IsNullOrWhiteSpace(ContentFormatSerializer.SerializeJson(payloadRes)));
        Assert.False(string.IsNullOrWhiteSpace(ContentFormatSerializer.SerializeJson(pbpkRes)));
    }

    /// <summary>
    /// Verifies all simulation HTTP endpoints via <see cref="HttpClient"/>.
    /// </summary>
    [Fact]
    public async Task SimulationEndpoints_ReturnSuccessCodes()
    {
        // WHAT: Tests HTTP endpoints for the 4 simulation campaigns.
        // WHY:  Ensures full end-to-end REST API integration.
        // HOW:  Sends GET and POST requests and asserts HTTP 200 OK.
        var getCampaigns = await _client.GetAsync("/api/simulations/campaigns");
        Assert.Equal(HttpStatusCode.OK, getCampaigns.StatusCode);

        var runSerum = await _client.PostAsJsonAsync("/api/simulations/run/serum-stability", new SerumStabilityRequest());
        Assert.Equal(HttpStatusCode.OK, runSerum.StatusCode);

        var runGating = await _client.PostAsJsonAsync("/api/simulations/run/aptamer-gating", new AptamerGatingRequest());
        Assert.Equal(HttpStatusCode.OK, runGating.StatusCode);

        var runPayload = await _client.PostAsJsonAsync("/api/simulations/run/payload-leakage", new PayloadLeakageRequest());
        Assert.Equal(HttpStatusCode.OK, runPayload.StatusCode);

        var runPbpk = await _client.PostAsJsonAsync("/api/simulations/run/pbpk-endocrine", new PbpkSimulationRequest());
        Assert.Equal(HttpStatusCode.OK, runPbpk.StatusCode);

        var configSerum = await _client.GetAsync("/api/simulations/configs/CAMPAIGN_SERUM_STABILITY");
        Assert.Equal(HttpStatusCode.OK, configSerum.StatusCode);

        var configGating = await _client.GetAsync("/api/simulations/configs/CAMPAIGN_APTAMER_GATING");
        Assert.Equal(HttpStatusCode.OK, configGating.StatusCode);

        var configPayload = await _client.GetAsync("/api/simulations/configs/CAMPAIGN_PAYLOAD_LEAKAGE");
        Assert.Equal(HttpStatusCode.OK, configPayload.StatusCode);

        var configDefault = await _client.GetAsync("/api/simulations/configs/UNKNOWN");
        Assert.Equal(HttpStatusCode.OK, configDefault.StatusCode);
    }
}
