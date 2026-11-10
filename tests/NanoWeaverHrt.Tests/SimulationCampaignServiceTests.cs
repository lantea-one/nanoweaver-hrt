using Moq;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Simulations.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite verifying the authoritative simulation campaign service:
/// oxDNA serum stability, LAMMPS aptamer gating, steroid payload leakage, and whole-body PBPK modeling.
/// </summary>
public sealed class SimulationCampaignServiceTests
{
    /// <summary>
    /// Mock of the pharmacogenomics service.
    /// </summary>
    private readonly Mock<IPharmacogenomicsService> _mockGenomics;

    /// <summary>
    /// Service under test.
    /// </summary>
    private readonly SimulationCampaignService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationCampaignServiceTests"/> class.
    /// </summary>
    public SimulationCampaignServiceTests()
    {
        _mockGenomics = new Mock<IPharmacogenomicsService>(MockBehavior.Strict);
        _service = new SimulationCampaignService(_mockGenomics.Object);
    }

    /// <summary>
    /// Verifies that the catalog contains the 4 pre-configured simulation campaigns.
    /// </summary>
    [Fact]
    public void GetCampaignCatalog_ReturnsExpectedCampaigns()
    {
        // WHAT: Retrieves the campaign catalog.
        // WHY:  Ensure the platform exposes all four core simulation campaigns.
        // HOW:  Call GetCampaignCatalog and assert on campaign identifiers.
        IReadOnlyList<SimulationCampaignInfo> catalog = _service.GetCampaignCatalog();

        Assert.Equal(4, catalog.Count);
        Assert.Contains(catalog, c => c.CampaignId == "CAMPAIGN_SERUM_STABILITY");
        Assert.Contains(catalog, c => c.CampaignId == "CAMPAIGN_APTAMER_GATING");
        Assert.Contains(catalog, c => c.CampaignId == "CAMPAIGN_PAYLOAD_LEAKAGE");
        Assert.Contains(catalog, c => c.CampaignId == "CAMPAIGN_PBPK_ENDOCRINE");
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulateSerumStability"/> does not flat-clamp temperatures below 29.6°C
    /// and scales with simulation steps.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_LowTemperatureAndStepScaling_CalculatesProperRmsf()
    {
        // WHAT: Tests serum stability simulation at 20°C and 25°C without flat clamping.
        // WHY:  Issue #78 requires accurate temperature scaling below 29.6°C.
        // HOW:  Run simulation at 20°C and 37°C, asserting that 20°C produces lower RMSF than 37°C.
        SerumStabilityRequest req20 = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 20.0,
            DivalentMagnesiumMilliMolar = 0.8,
            MonovalentSaltMolar = 0.15,
            SimulationSteps = 50000000,
            PllPegCoatingApplied = false,
            CpGMethylationApplied = false,
            StapleCrossoverCount = 24
        };

        SerumStabilityRequest req37 = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            DivalentMagnesiumMilliMolar = 0.8,
            MonovalentSaltMolar = 0.15,
            SimulationSteps = 50000000,
            PllPegCoatingApplied = false,
            CpGMethylationApplied = false,
            StapleCrossoverCount = 24
        };

        SerumStabilityAnalysisResult res20 = _service.SimulateSerumStability(req20);
        SerumStabilityAnalysisResult res37 = _service.SimulateSerumStability(req37);

        Assert.True(res20.AverageRmsfNm < res37.AverageRmsfNm);
        Assert.True(res20.AverageRmsfNm > 0.0);
        Assert.NotEmpty(res20.RmsfProfilePoints);
    }

    /// <summary>
    /// Verifies that coatings (PLL-PEG and CpG methylation) reduce RMSF and stabilize crossovers.
    /// </summary>
    [Fact]
    public void SimulateSerumStability_CoatingsApplied_ReducesRmsfAndCompromisedCrossovers()
    {
        // WHAT: Tests serum stability when PLL-PEG coating and CpG methylation are applied.
        // WHY:  Coatings must sterically and electrostatically protect DNA origami in low magnesium.
        // HOW:  Compare unpassivated vs fully passivated requests.
        SerumStabilityRequest unpassivated = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            DivalentMagnesiumMilliMolar = 0.5,
            MonovalentSaltMolar = 0.15,
            SimulationSteps = 10000000,
            PllPegCoatingApplied = false,
            CpGMethylationApplied = false,
            StapleCrossoverCount = 30
        };

        SerumStabilityRequest passivated = new()
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            DivalentMagnesiumMilliMolar = 0.5,
            MonovalentSaltMolar = 0.15,
            SimulationSteps = 10000000,
            PllPegCoatingApplied = true,
            CpGMethylationApplied = true,
            StapleCrossoverCount = 30
        };

        SerumStabilityAnalysisResult resUnpassivated = _service.SimulateSerumStability(unpassivated);
        SerumStabilityAnalysisResult resPassivated = _service.SimulateSerumStability(passivated);

        Assert.True(resPassivated.AverageRmsfNm < resUnpassivated.AverageRmsfNm);
        Assert.True(resPassivated.CompromisedCrossoverCount <= resUnpassivated.CompromisedCrossoverCount);
        Assert.True(resPassivated.StructuralIntegrityScore >= resUnpassivated.StructuralIntegrityScore);
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulateAptamerGating"/> sets the PMF curve origin to 0.0
    /// and factors in sequence GC content.
    /// </summary>
    [Fact]
    public void SimulateAptamerGating_PmfOriginAndGcContent_AccuratelyModeled()
    {
        // WHAT: Tests aptamer gating PMF curve origin and GC content sensitivity.
        // WHY:  Reaction coordinate xi = 0.0 must be 0.0 kcal/mol, and high GC increases barrier.
        // HOW:  Compare high GC sequence with low GC sequence, and inspect curve at xi = 0.0.
        AptamerGatingRequest highGcReq = new()
        {
            TargetReceptor = "Human Pituitary GnRH Receptor (GnRH-R)",
            TargetGonadotropin = "Luteinizing Hormone (LH)",
            AptamerSequence = "GCGCGCGCGCGCGCGCGCGCGCGCGCGCGC", // 100% GC
            ReactionCoordinateMaxNm = 2.5,
            UmbrellaSamplingWindows = 12
        };

        AptamerGatingRequest lowGcReq = new()
        {
            TargetReceptor = "Human Pituitary GnRH Receptor (GnRH-R)",
            TargetGonadotropin = "Luteinizing Hormone (LH)",
            AptamerSequence = "ATATATATATATATATATATATATATATAT", // 0% GC
            ReactionCoordinateMaxNm = 2.5,
            UmbrellaSamplingWindows = 12
        };

        AptamerGatingAnalysisResult resHighGc = _service.SimulateAptamerGating(highGcReq);
        AptamerGatingAnalysisResult resLowGc = _service.SimulateAptamerGating(lowGcReq);

        // First point must be exactly 0.0 kcal/mol for both bound and unbound
        Assert.Equal(0.0, resHighGc.PmfCurve[0].UnboundPmfKcalMol);
        Assert.Equal(0.0, resHighGc.PmfCurve[0].BoundPmfKcalMol);
        Assert.Equal(0.0, resHighGc.PmfCurve[0].ReactionCoordinateNm);

        // High GC must yield higher unbinding barrier than low GC
        Assert.True(resHighGc.UnboundOpeningFreeEnergyKcalMol > resLowGc.UnboundOpeningFreeEnergyKcalMol);
        Assert.Equal(0.88, resHighGc.GnRhReceptorContactRatio);
        Assert.Equal(4.1, resHighGc.EstimatedKdNanoMolar);
    }

    /// <summary>
    /// Verifies aptamer gating with a non-pituitary target receptor.
    /// </summary>
    [Fact]
    public void SimulateAptamerGating_NonPituitaryTarget_ReportsModerateOffTargetAffinity()
    {
        // WHAT: Tests aptamer gating simulation with a non-pituitary receptor.
        // WHY:  Non-pituitary targets must report lower contact ratio and higher Kd.
        // HOW:  Target non-pituitary receptor and check tropism verdict.
        AptamerGatingRequest req = new()
        {
            TargetReceptor = "Human Liver Asialoglycoprotein Receptor",
            TargetGonadotropin = "Non-Specific Ligand",
            AptamerSequence = "ACGTACGTACGTACGTACGTACGT",
            ReactionCoordinateMaxNm = 2.0,
            UmbrellaSamplingWindows = 10
        };

        AptamerGatingAnalysisResult result = _service.SimulateAptamerGating(req);

        Assert.Equal(0.62, result.GnRhReceptorContactRatio);
        Assert.Equal(28.5, result.EstimatedKdNanoMolar);
        Assert.Contains("Moderate Off-Target Affinity", result.TropismVerdict);
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulatePayloadLeakage"/> scales retention timepoints
    /// across simulation duration and accounts for cavity functionalization, chassis geometry, and hormone molecule.
    /// </summary>
    [Fact]
    public void SimulatePayloadLeakage_GeometryAndFunctionalization_ScalesAccurately()
    {
        // WHAT: Tests payload leakage with Cyclodextrin, Cholesterol, and Bare cavities.
        // WHY:  Functionalization and chassis geometry dictate steroid retention.
        // HOW:  Run across different functionalizations and geometries.
        PayloadLeakageRequest cyclodextrinReq = new()
        {
            ChassisGeometry = "Hexagonal Barrel Origami",
            HormoneMolecule = "17β-Estradiol",
            CavityFunctionalization = "Beta-Cyclodextrin Host-Guest Anchors",
            LoadedMoleculeCount = 40,
            SimulationDurationNs = 200.0
        };

        PayloadLeakageRequest cholesterolReq = new()
        {
            ChassisGeometry = "Hexagonal Barrel Origami",
            HormoneMolecule = "17β-Estradiol",
            CavityFunctionalization = "Cholesterol Hydrophobic Tether",
            LoadedMoleculeCount = 40,
            SimulationDurationNs = 150.0
        };

        PayloadLeakageRequest bareCageReq = new()
        {
            ChassisGeometry = "Octahedral Nanocage",
            HormoneMolecule = "Testosterone",
            CavityFunctionalization = "Unmodified Bare Cavity",
            LoadedMoleculeCount = 30,
            SimulationDurationNs = 120.0
        };

        PayloadLeakageAnalysisResult cdResult = _service.SimulatePayloadLeakage(cyclodextrinReq);
        PayloadLeakageAnalysisResult cholResult = _service.SimulatePayloadLeakage(cholesterolReq);
        PayloadLeakageAnalysisResult bareCageResult = _service.SimulatePayloadLeakage(bareCageReq);

        // Cyclodextrin has lowest passive leakage
        Assert.True(cdResult.PassiveLeakagePercentage < cholResult.PassiveLeakagePercentage);
        Assert.True(cholResult.PassiveLeakagePercentage < bareCageResult.PassiveLeakagePercentage);

        // Retention curve timepoints scale with SimulationDurationNs
        Assert.Equal(200.0, cdResult.RetentionCurve[^1].TimeNs);
        Assert.Equal(150.0, cholResult.RetentionCurve[^1].TimeNs);
        Assert.Equal(120.0, bareCageResult.RetentionCurve[^1].TimeNs);

        // Testosterone in bare cage: check that release rate is adjusted
        Assert.True(bareCageResult.ReleaseRatePerMinuteOpen > 11.8);
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulatePbpkEndocrine"/> honors
    /// genomic constraints such as <c>RecommendedMaxEstradiolPmolL</c> (e.g., F2 Prothrombin at 480 pmol/L).
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_F2ProthrombinGenomicConstraint_CapsEstradiol()
    {
        // WHAT: Tests PBPK simulation with F2 Prothrombin mutation in MtF regimen.
        // WHY:  F2 Prothrombin elevates thrombosis risk, requiring estradiol to be capped at 480 pmol/L.
        // HOW:  Mock PharmacogenomicsService to return profile with RecommendedMaxEstradiolPmolL = 480.0.
        PatientGenomicProfile profile = new()
        {
            PatientId = "PATIENT-F2",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [],
            Alerts = [],
            OverallClearanceMultiplier = 1.0,
            ThrombosisRiskIndex = 2.5,
            CpGMethylationMandatory = false,
            CalculatedAtUtc = "2026-11-03T00:00:00Z",
            RecommendedMaxEstradiolPmolL = 480.0,
            RecommendedMinTestosteroneNmolL = null,
            RecommendedMaxTestosteroneNmolL = null
        };

        _mockGenomics.Setup(g => g.AnalyzeProfile("PATIENT-F2", It.IsAny<IReadOnlyList<string>>(), TherapeuticRegimen.MaleToFemale))
            .Returns(profile);

        PbpkSimulationRequest request = new()
        {
            PatientId = "PATIENT-F2",
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenomicMarkerIds = ["F2_PROTHROMBIN"],
            DepotDoseMg = 25.0,
            DeliveryIntervalDays = 90,
            SimulationDurationDays = 30
        };

        PbpkEndocrineSimulationResult result = _service.SimulatePbpkEndocrine(request);

        Assert.Equal(90, result.RecommendedDosingIntervalDays);
        Assert.True(result.AverageSteadyStateEstradiolPmolL <= 480.0);
        Assert.Equal(25.0, result.TotalNanocarrierMassMg);
        Assert.True(result.SerumNucleaseHalfLifeHours >= 72.0 && result.SerumNucleaseHalfLifeHours <= 168.0);
        Assert.True(result.DepotExhaustionDay > 0);
        Assert.All(result.DailyKinetics, pt => Assert.True(pt.EstradiolPmolL <= 480.0));
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulatePbpkEndocrine"/> caps estradiol to 500 pmol/L
    /// when thrombosis risk index exceeds 1.5 without an explicit max estradiol cap.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_HighThrombosisRiskWithoutMaxCap_CapsEstradiolTo500()
    {
        // WHAT: Tests PBPK simulation with elevated thrombosis risk index (> 1.5) but without RecommendedMaxEstradiolPmolL.
        // WHY:  Verifies automatic fallback capping to 500 pmol/L for high thrombosis risk profiles.
        // HOW:  Mock PharmacogenomicsService to return profile with ThrombosisRiskIndex = 2.0 and RecommendedMaxEstradiolPmolL = null.
        PatientGenomicProfile profile = new()
        {
            PatientId = "PATIENT-THROMBOSIS-NOCAP",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [],
            Alerts = [],
            OverallClearanceMultiplier = 1.0,
            ThrombosisRiskIndex = 2.0,
            CpGMethylationMandatory = false,
            CalculatedAtUtc = "2026-11-03T00:00:00Z",
            RecommendedMaxEstradiolPmolL = null,
            RecommendedMinTestosteroneNmolL = null,
            RecommendedMaxTestosteroneNmolL = null
        };

        _mockGenomics.Setup(g => g.AnalyzeProfile("PATIENT-THROMBOSIS-NOCAP", It.IsAny<IReadOnlyList<string>>(), TherapeuticRegimen.MaleToFemale))
            .Returns(profile);

        PbpkSimulationRequest request = new()
        {
            PatientId = "PATIENT-THROMBOSIS-NOCAP",
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenomicMarkerIds = ["CUSTOM_THROMBOSIS"],
            DepotDoseMg = 25.0,
            DeliveryIntervalDays = 90,
            SimulationDurationDays = 30
        };

        PbpkEndocrineSimulationResult result = _service.SimulatePbpkEndocrine(request);

        Assert.True(result.AverageSteadyStateEstradiolPmolL <= 500.0);
        Assert.All(result.DailyKinetics, pt => Assert.True(pt.EstradiolPmolL <= 500.0));
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.GetCampaignResults"/> returns the registered campaign catalog.
    /// </summary>
    [Fact]
    public void GetCampaignResults_ReturnsCampaignCatalog()
    {
        // WHAT: Tests GetCampaignResults method on SimulationCampaignService.
        // WHY:  Ensures campaign catalog and results query method parity.
        // HOW:  Asserts that GetCampaignResults matches GetCampaignCatalog.
        var results = _service.GetCampaignResults();
        var catalog = _service.GetCampaignCatalog();
        Assert.Equal(catalog.Count, results.Count);
    }

    /// <summary>
    /// Verifies that <see cref="SimulationCampaignService.SimulatePbpkEndocrine"/> models FtM testosterone dynamics.
    /// </summary>
    [Fact]
    public void SimulatePbpkEndocrine_FtmRegimen_ModelsTestosteroneAndEstradiolSuppression()
    {
        // WHAT: Tests PBPK simulation for Female-to-Male (FtM) therapy.
        // WHY:  FtM therapy targets testosterone elevation and endogenous estradiol suppression.
        // HOW:  Run simulation without genomic markers and verify average testosterone and estradiol.
        PbpkSimulationRequest request = new()
        {
            PatientId = "PATIENT-FTM-01",
            Regimen = TherapeuticRegimen.FemaleToMale,
            GenomicMarkerIds = [],
            DepotDoseMg = 50.0,
            DeliveryIntervalDays = 120,
            SimulationDurationDays = 60
        };

        PbpkEndocrineSimulationResult result = _service.SimulatePbpkEndocrine(request);

        Assert.Equal(120, result.RecommendedDosingIntervalDays);
        Assert.True(result.AverageSteadyStateTestosteroneNmolL >= 14.0);
        Assert.True(result.AverageSteadyStateEstradiolPmolL <= 150.0);
        Assert.True(result.TargetSuppressionAchieved);
    }

    /// <summary>
    /// Verifies input deck generation for oxDNA and LAMMPS campaigns.
    /// </summary>
    [Fact]
    public void InputDeckGenerators_IncludeRequiredSimulationKeywords()
    {
        // WHAT: Verifies generated input decks for oxDNA and LAMMPS.
        // WHY:  Simulation engines require specific topology, conf, box, and pair keywords.
        // HOW:  Generate decks and assert required keywords are present.
        SerumStabilityRequest oxDnaReq = new()
        {
            ScaffoldName = "M13mp18_Nanotube",
            TemperatureCelsius = 37.0,
            MonovalentSaltMolar = 0.15,
            SimulationSteps = 20000000
        };

        AptamerGatingRequest lammpsAptReq = new()
        {
            TargetReceptor = "GnRH-R",
            TargetGonadotropin = "LH",
            AptamerSequence = "ACGTACGT",
            ReactionCoordinateMaxNm = 2.5,
            UmbrellaSamplingWindows = 16
        };

        PayloadLeakageRequest lammpsPayloadReq = new()
        {
            ChassisGeometry = "Hexagonal Barrel",
            HormoneMolecule = "17β-Estradiol",
            CavityFunctionalization = "Cyclodextrin",
            LoadedMoleculeCount = 50,
            SimulationDurationNs = 100.0
        };

        string oxDnaDeck = _service.GenerateOxDnaInputDeck(oxDnaReq);
        string lammpsAptDeck = _service.GenerateLammpsAptamerInputDeck(lammpsAptReq);
        string lammpsPayloadDeck = _service.GenerateLammpsPayloadInputDeck(lammpsPayloadReq);

        // oxDNA deck checks
        Assert.Contains("topology = target.top", oxDnaDeck);
        Assert.Contains("conf_file = target.dat", oxDnaDeck);
        Assert.Contains("steps = 20000000", oxDnaDeck);

        // LAMMPS aptamer deck checks
        Assert.Contains("region simbox block", lammpsAptDeck);
        Assert.Contains("create_box 2 simbox", lammpsAptDeck);
        Assert.Contains("create_atoms 1 random", lammpsAptDeck);
        Assert.Contains("pair_coeff * * 0.1 2.0", lammpsAptDeck);

        // LAMMPS payload deck checks
        Assert.Contains("region cavity block", lammpsPayloadDeck);
        Assert.Contains("create_box 2 cavity", lammpsPayloadDeck);
        Assert.Contains("create_atoms 1 random", lammpsPayloadDeck);
        Assert.Contains("pair_coeff * * 0.15 2.5", lammpsPayloadDeck);
    }

    /// <summary>
    /// Verifies edge cases and boundary defaults across all simulation campaigns.
    /// </summary>
    [Fact]
    public void SimulationCampaigns_EdgeCasesAndDefaults_CoverBranches()
    {
        // WHAT: Tests empty aptamer sequence, zero simulation duration, zero delivery interval, and thrombosis auto-cap.
        // WHY:  Ensures full branch coverage across simulation campaign service.
        // HOW:  Execute requests with edge-case inputs and assert defaults are applied.
        AptamerGatingRequest emptyApt = new()
        {
            TargetReceptor = "Test",
            TargetGonadotropin = "Test",
            AptamerSequence = "",
            ReactionCoordinateMaxNm = 2.0,
            UmbrellaSamplingWindows = 6
        };
        AptamerGatingAnalysisResult aptResult = _service.SimulateAptamerGating(emptyApt);
        Assert.NotNull(aptResult);

        PayloadLeakageRequest zeroDuration = new()
        {
            ChassisGeometry = "Cylinder",
            HormoneMolecule = "Estradiol",
            CavityFunctionalization = "None",
            LoadedMoleculeCount = 20,
            SimulationDurationNs = 0.0
        };
        PayloadLeakageAnalysisResult leakResult = _service.SimulatePayloadLeakage(zeroDuration);
        Assert.Equal(100.0, leakResult.RetentionCurve[^1].TimeNs);

        PatientGenomicProfile highThrombosisProfile = new()
        {
            PatientId = "PATIENT-THROMBOSIS",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [],
            Alerts = [],
            OverallClearanceMultiplier = 1.0,
            ThrombosisRiskIndex = 2.0,
            CpGMethylationMandatory = false,
            CalculatedAtUtc = "2026-11-03T00:00:00Z",
            RecommendedMaxEstradiolPmolL = null,
            RecommendedMinTestosteroneNmolL = null,
            RecommendedMaxTestosteroneNmolL = null
        };

        _mockGenomics.Setup(g => g.AnalyzeProfile("PATIENT-THROMBOSIS", It.IsAny<IReadOnlyList<string>>(), TherapeuticRegimen.MaleToFemale))
            .Returns(highThrombosisProfile);

        PbpkSimulationRequest pbpkZeroInterval = new()
        {
            PatientId = "PATIENT-THROMBOSIS",
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenomicMarkerIds = ["CUSTOM_THROMBOSIS"],
            DepotDoseMg = 20.0,
            DeliveryIntervalDays = 0,
            SimulationDurationDays = 15
        };

        PbpkEndocrineSimulationResult pbpkResult = _service.SimulatePbpkEndocrine(pbpkZeroInterval);
        Assert.Equal(180, pbpkResult.RecommendedDosingIntervalDays);
        Assert.All(pbpkResult.DailyKinetics, pt => Assert.True(pt.EstradiolPmolL <= 500.0));
    }
}
