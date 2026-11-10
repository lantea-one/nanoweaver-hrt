using Moq;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Simulations.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Comprehensive unit test suite for <see cref="SimulationWorkspaceViewModel"/>,
/// <see cref="ClinicalGenomicsWorkspaceViewModel"/>, <see cref="WetLabComplianceWorkspaceViewModel"/>,
/// and their integration within <see cref="MainShellViewModel"/>.
/// </summary>
public sealed class GuiWorkspaceViewModelsTests
{
    /// <summary>
    /// Creates a dummy instance profile for testing purposes.
    /// </summary>
    /// <param name="id">Instance identifier.</param>
    /// <returns>A populated <see cref="InstanceProfile"/> record.</returns>
    private static InstanceProfile CreateProfile(string id = "inst-001") =>
        new(id, "Test Node", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUBKEY", "ENVELOPE", true, 15, DateTimeOffset.UtcNow, null);

    /// <summary>
    /// Creates a representative <see cref="SerumStabilityAnalysisResult"/> instance for testing.
    /// </summary>
    /// <returns>A populated serum stability analysis result.</returns>
    private static SerumStabilityAnalysisResult CreateSerumStabilityResult() => new SerumStabilityAnalysisResult
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

    /// <summary>
    /// Creates a representative <see cref="AptamerGatingAnalysisResult"/> instance for testing.
    /// </summary>
    /// <returns>A populated aptamer gating analysis result.</returns>
    private static AptamerGatingAnalysisResult CreateAptamerGatingResult() => new AptamerGatingAnalysisResult
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

    /// <summary>
    /// Creates a representative <see cref="PayloadLeakageAnalysisResult"/> instance for testing.
    /// </summary>
    /// <returns>A populated payload leakage analysis result.</returns>
    private static PayloadLeakageAnalysisResult CreatePayloadLeakageResult() => new PayloadLeakageAnalysisResult
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

    /// <summary>
    /// Creates a representative <see cref="PbpkEndocrineSimulationResult"/> instance for testing.
    /// </summary>
    /// <returns>A populated PBPK simulation result.</returns>
    private static PbpkEndocrineSimulationResult CreatePbpkResult() => new()
    {
        PatientId = "P1",
        Regimen = TherapeuticRegimen.MaleToFemale,
        SimulationDurationDays = 90,
        DepotDoseMg = 25.0,
        AverageSteadyStateEstradiolPmolL = 500.0,
        AverageSteadyStateTestosteroneNmolL = 1.0,
        AverageLhIuL = 2.0,
        TimeInTargetRangePercent = 95.0,
        TargetSuppressionAchieved = true,
        ThromboticSurgeDetected = false,
        RecommendedDosingIntervalDays = 180,
        ClinicalFeasibilityVerdict = "Feasible",
        OrganBiodistributionSummary = "Summary",
        TotalNanocarrierMassMg = 25.0,
        SerumNucleaseHalfLifeHours = 120.0,
        DepotExhaustionDay = 411,
        DailyKinetics = []
    };

    /// <summary>
    /// Creates a representative <see cref="PatientGenomicProfile"/> instance for testing.
    /// </summary>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="regimen">Therapeutic regimen.</param>
    /// <returns>A populated patient genomic profile.</returns>
    private static PatientGenomicProfile CreatePatientGenomicProfile(
        string patientId = "pt1",
        TherapeuticRegimen regimen = TherapeuticRegimen.MaleToFemale) => new()
        {
            PatientId = patientId,
            Regimen = regimen,
            ObservedMarkers = [],
            Alerts = [],
            ThrombosisRiskIndex = 1.0,
            OverallClearanceMultiplier = 1.0,
            CpGMethylationMandatory = false,
            CalculatedAtUtc = "2026-11-04T00:00:00Z"
        };

    /// <summary>
    /// Creates a representative <see cref="OpentronsProtocolResult"/> instance for testing.
    /// </summary>
    /// <returns>A populated protocol synthesis result.</returns>
    private static OpentronsProtocolResult CreateOpentronsResult() => new()
    {
        PythonScript = "from opentrons import protocol_api\n",
        ScriptSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        TransferCount = 42,
        SourcePlatesRequired = 1,
        TipRacksRequired = 2,
        HasThermocyclerRamp = true,
        EstimatedRunDurationMinutes = 35.0
    };

    /// <summary>
    /// Creates a representative <see cref="TrajectoryFederationManifest"/> instance for testing.
    /// </summary>
    /// <param name="dataset">Target trajectory dataset name.</param>
    /// <returns>A populated trajectory federation manifest.</returns>
    private static TrajectoryFederationManifest CreateTrajectoryManifest(string dataset = "traj-run1.xyz") => new()
    {
        DatasetName = dataset,
        TotalSizeBytes = 1048576,
        ChunkSizeBytes = 262144,
        TotalChunks = 4,
        Chunks = [],
        SigningNodeId = "node-alpha-01",
        SignatureAlgorithm = "Ed25519",
        SignatureBase64 = "c2lnbmF0dXJl",
        ManifestTimestampUtc = DateTimeOffset.UtcNow
    };

    // =========================================================================
    // SimulationWorkspaceViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void SimulationWorkspaceViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in SimulationWorkspaceViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new SimulationWorkspaceViewModel(null!));
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="SimulationWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public void SimulationWorkspaceViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests all observable properties of SimulationWorkspaceViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-xyz";
        Assert.Equal("token-xyz", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-xyz";
        Assert.Empty(fired);

        vm.ActiveDatasetName = "dataset-1";
        Assert.Equal("dataset-1", vm.ActiveDatasetName);
        Assert.Contains(nameof(vm.ActiveDatasetName), fired);
        fired.Clear();
        vm.ActiveDatasetName = "dataset-1";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastSerumStabilityResult);
        Assert.Null(vm.LastAptamerGatingResult);
        Assert.Null(vm.LastPayloadLeakageResult);
        Assert.Null(vm.LastPbpkSimulationResult);
        Assert.Null(vm.LastTrajectoryAnalyticsResult);
    }

    /// <summary>
    /// Verifies serum stability simulation execution, credential validation, and error propagation.
    /// </summary>
    [Fact]
    public async Task SimulationWorkspaceViewModel_RunSerumStabilityAsync_AllPaths()
    {
        // WHAT: Tests RunSerumStabilityAsync happy path, null request, missing profile/token, and client fault.
        // WHY:  Covers all branch conditions in serum stability execution.
        // HOW:  Sets up mocks, tests ArgumentNullException, InvalidOperationException, success, and fault handling.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object);

        // Null request
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RunSerumStabilityAsync(null!));

        // Missing profile
        var request = new SerumStabilityRequest();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunSerumStabilityAsync(request));

        // Missing token
        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunSerumStabilityAsync(request));

        // Success path
        vm.ActiveBearerToken = "test-token";
        var expectedResult = CreateSerumStabilityResult();
        clientMock.Setup(c => c.RunSerumStabilityAsync(profile, "test-token", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.RunSerumStabilityAsync(request);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastSerumStabilityResult);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Fault path
        clientMock.Setup(c => c.RunSerumStabilityAsync(profile, "test-token", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulation server error"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunSerumStabilityAsync(request));
        Assert.Equal("Simulation server error", ex.Message);
        Assert.Equal("Simulation server error", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies aptamer gating simulation execution, session validation, and error handling.
    /// </summary>
    [Fact]
    public async Task SimulationWorkspaceViewModel_RunAptamerGatingAsync_AllPaths()
    {
        // WHAT: Tests RunAptamerGatingAsync happy path, null request, missing credentials, and client fault.
        // WHY:  Covers all branch conditions in aptamer gating execution.
        // HOW:  Configures client mock, executes calls, asserts state transitions and exception reporting.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object, () => profile, () => "ambient-token");

        // Null request
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RunAptamerGatingAsync(null!));

        // Missing session via empty token provider
        var unauthenticatedVm = new SimulationWorkspaceViewModel(clientMock.Object, () => profile, () => string.Empty);
        var request = new AptamerGatingRequest();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unauthenticatedVm.RunAptamerGatingAsync(request));

        // Success path
        var expectedResult = CreateAptamerGatingResult();
        clientMock.Setup(c => c.RunAptamerGatingAsync(profile, "ambient-token", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.RunAptamerGatingAsync(request);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastAptamerGatingResult);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Fault path
        clientMock.Setup(c => c.RunAptamerGatingAsync(profile, "ambient-token", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection aborted"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => vm.RunAptamerGatingAsync(request));
        Assert.Equal("Connection aborted", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies payload leakage simulation execution, session validation, and error handling.
    /// </summary>
    [Fact]
    public async Task SimulationWorkspaceViewModel_RunPayloadLeakageAsync_AllPaths()
    {
        // WHAT: Tests RunPayloadLeakageAsync happy path, null request, missing credentials, and client fault.
        // WHY:  Covers all branches in payload leakage simulation.
        // HOW:  Sets up mocks, tests null checks, successful response, and error recording.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object, () => profile, () => "token-1");

        // Null request
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RunPayloadLeakageAsync(null!));

        // Success path
        var request = new PayloadLeakageRequest();
        var expectedResult = CreatePayloadLeakageResult();
        clientMock.Setup(c => c.RunPayloadLeakageAsync(profile, "token-1", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.RunPayloadLeakageAsync(request);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastPayloadLeakageResult);
        Assert.False(vm.IsBusy);

        // Fault path
        clientMock.Setup(c => c.RunPayloadLeakageAsync(profile, "token-1", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Operation timed out"));

        await Assert.ThrowsAsync<TimeoutException>(() => vm.RunPayloadLeakageAsync(request));
        Assert.Equal("Operation timed out", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies PBPK endocrine simulation execution, session validation, and error handling.
    /// </summary>
    [Fact]
    public async Task SimulationWorkspaceViewModel_RunPbpkSimulationAsync_AllPaths()
    {
        // WHAT: Tests RunPbpkSimulationAsync happy path, null request, and error handling.
        // WHY:  Covers all branches in PBPK endocrine simulation.
        // HOW:  Sets up mock client, calls method, verifies state and results.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object, () => profile, () => "pbpk-token");

        // Null request
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RunPbpkSimulationAsync(null!));

        // Success path
        var request = new PbpkSimulationRequest();
        var expectedResult = CreatePbpkResult();
        clientMock.Setup(c => c.RunPbpkSimulationAsync(profile, "pbpk-token", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.RunPbpkSimulationAsync(request);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastPbpkSimulationResult);
        Assert.False(vm.IsBusy);

        // Fault path
        clientMock.Setup(c => c.RunPbpkSimulationAsync(profile, "pbpk-token", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PBPK model failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunPbpkSimulationAsync(request));
        Assert.Equal("PBPK model failure", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies analytical trajectory querying for string and request overloads, validation, and errors.
    /// </summary>
    [Fact]
    public async Task SimulationWorkspaceViewModel_QueryTrajectoryAnalyticsAsync_AllPaths()
    {
        // WHAT: Tests QueryTrajectoryAnalyticsAsync with string query and TrajectoryAnalyticsApiRequest overloads.
        // WHY:  Covers all branches including string validation, default dataset name usage, and error states.
        // HOW:  Validates whitespace query text, null request model, success path, and exception path.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new SimulationWorkspaceViewModel(clientMock.Object, () => profile, () => "query-token")
        {
            ActiveDatasetName = "dataset-alpha"
        };

        // String overload validation
        await Assert.ThrowsAsync<ArgumentException>(() => vm.QueryTrajectoryAnalyticsAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.QueryTrajectoryAnalyticsAsync("   "));

        // Request overload null check
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.QueryTrajectoryAnalyticsAsync((TrajectoryAnalyticsApiRequest)null!));

        // Success path via string overload
        var expectedResult = new TrajectoryAnalyticsApiResponse(
            DatasetName: "dataset-alpha",
            Columns: ["frame_id", "energy"],
            Rows: [["0", "-120.4"], ["1", "-121.1"]],
            TotalRows: 2,
            ElapsedMilliseconds: 4.5);

        clientMock.Setup(c => c.QueryTrajectoryAnalyticsAsync(
                profile,
                "query-token",
                It.Is<TrajectoryAnalyticsApiRequest>(r => r.DatasetName == "dataset-alpha" && r.Query == "SELECT * FROM frames" && r.MaxRows == 50),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.QueryTrajectoryAnalyticsAsync("SELECT * FROM frames", 50);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastTrajectoryAnalyticsResult);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Fault path via request overload
        var failRequest = new TrajectoryAnalyticsApiRequest("dataset-alpha", "INVALID SQL", 10);
        clientMock.Setup(c => c.QueryTrajectoryAnalyticsAsync(profile, "query-token", failRequest, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SQL parser error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.QueryTrajectoryAnalyticsAsync(failRequest));
        Assert.Equal("SQL parser error", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    // =========================================================================
    // ClinicalGenomicsWorkspaceViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void ClinicalGenomicsWorkspaceViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in ClinicalGenomicsWorkspaceViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new ClinicalGenomicsWorkspaceViewModel(null!));
    }

    /// <summary>
    /// Verifies request projections in <see cref="ClosedLoopRequest"/> and <see cref="PharmacogenomicsAdjustmentRequest"/>.
    /// </summary>
    [Fact]
    public void ClinicalGenomics_RequestModels_ProjectionAndDefaults()
    {
        // WHAT: Tests property values and conversion helpers on ClosedLoopRequest and PharmacogenomicsAdjustmentRequest.
        // WHY:  Guarantees correct mapping from UI DTOs to server API models.
        // HOW:  Instantiates records with default and custom values, invokes conversion methods, asserts fields.
        var defaultClosedLoop = new ClosedLoopRequest();
        Assert.Equal(120.0, defaultClosedLoop.InitialEstradiolPmolL);
        Assert.Equal(18.0, defaultClosedLoop.InitialTestosteroneNmolL);
        Assert.Equal(6.5, defaultClosedLoop.InitialLhIuL);
        Assert.Equal(5.0, defaultClosedLoop.InitialFshIuL);
        Assert.Equal(550.0, defaultClosedLoop.TargetEstradiolPmolL);
        Assert.Equal(1.2, defaultClosedLoop.TargetTestosteroneNmolL);
        Assert.Equal(ToggleState.EstrogenicDominant, defaultClosedLoop.TargetState);
        Assert.Equal(72, defaultClosedLoop.TotalHours);
        Assert.Equal(1.0, defaultClosedLoop.StepHours);
        Assert.Equal(1.0, defaultClosedLoop.ClearanceMultiplier);

        var customClosedLoop = new ClosedLoopRequest(
            InitialEstradiolPmolL: 50.0,
            InitialTestosteroneNmolL: 22.0,
            InitialLhIuL: 8.0,
            InitialFshIuL: 7.0,
            TargetEstradiolPmolL: 600.0,
            TargetTestosteroneNmolL: 0.9,
            TargetState: ToggleState.AndrogenicDominant,
            TotalHours: 96,
            StepHours: 0.5,
            ClearanceMultiplier: 1.2);

        var simReq = customClosedLoop.ToSimulationRequest();
        Assert.Equal(50.0, simReq.InitialEstradiolPmolL);
        Assert.Equal(22.0, simReq.InitialTestosteroneNmolL);
        Assert.Equal(8.0, simReq.InitialLhIuL);
        Assert.Equal(7.0, simReq.InitialFshIuL);
        Assert.Equal(600.0, simReq.TargetEstradiolPmolL);
        Assert.Equal(0.9, simReq.TargetTestosteroneNmolL);
        Assert.Equal(ToggleState.AndrogenicDominant, simReq.TargetState);
        Assert.Equal(96, simReq.TotalHours);
        Assert.Equal(0.5, simReq.StepHours);
        Assert.Equal(1.2, simReq.ClearanceMultiplier);

        var defaultPharma = new PharmacogenomicsAdjustmentRequest();
        Assert.Equal("ANON-PATIENT-001", defaultPharma.PatientId);
        Assert.Null(defaultPharma.MarkerIds);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, defaultPharma.Regimen);

        var customPharma = new PharmacogenomicsAdjustmentRequest("PATIENT-42", ["F5_LEIDEN"], TherapeuticRegimen.FemaleToMale);
        var analysisReq = customPharma.ToGenomicAnalysisRequest();
        Assert.Equal("PATIENT-42", analysisReq.PatientId);
        Assert.NotNull(analysisReq.MarkerIds);
        Assert.Equal(["F5_LEIDEN"], analysisReq.MarkerIds!);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, analysisReq.Regimen);
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="ClinicalGenomicsWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public void ClinicalGenomicsWorkspaceViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests all observable properties of ClinicalGenomicsWorkspaceViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-abc";
        Assert.Equal("token-abc", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-abc";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastClosedLoopResult);
        Assert.Null(vm.LastGenomicProfile);
        Assert.Null(vm.LastAdjustmentResult);
        Assert.Null(vm.LastPatientDnaPackage);
        Assert.Null(vm.LastCreatedDnaRecord);
    }

    /// <summary>
    /// Verifies closed loop endocrine simulation across request overloads, validation, and errors.
    /// </summary>
    [Fact]
    public async Task ClinicalGenomicsWorkspaceViewModel_SimulateClosedLoopAsync_AllPaths()
    {
        // WHAT: Tests SimulateClosedLoopAsync with ClosedLoopRequest and ClosedLoopSimulationRequest overloads.
        // WHY:  Covers null validation, credential resolution, success paths, and exception reporting.
        // HOW:  Asserts null guards, missing profile check, mock response, and failure path.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "token-cl");

        // Null checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.SimulateClosedLoopAsync((ClosedLoopRequest)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.SimulateClosedLoopAsync((ClosedLoopSimulationRequest)null!));

        // Missing profile check via provider
        var noProfileVm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => null, () => "token-cl");
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.SimulateClosedLoopAsync(new ClosedLoopRequest()));

        // Missing token check via provider
        var noTokenVm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTokenVm.SimulateClosedLoopAsync(new ClosedLoopRequest()));

        // Whitespace token check via provider
        var whitespaceTokenVm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => whitespaceTokenVm.SimulateClosedLoopAsync(new ClosedLoopRequest()));

        // Null provider checks (1-arg constructor)
        var nullProviderVm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.SimulateClosedLoopAsync(new ClosedLoopRequest()));
        nullProviderVm.ActiveProfile = profile;
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.SimulateClosedLoopAsync(new ClosedLoopRequest()));

        // Success path via ClosedLoopRequest
        var expectedResult = new ClosedLoopSimulationResult().GetExamples();

        clientMock.Setup(c => c.SimulateClosedLoopEndocrineAsync(
                profile,
                "token-cl",
                It.IsAny<ClosedLoopSimulationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.SimulateClosedLoopAsync(new ClosedLoopRequest());
        Assert.Same(expectedResult, result);

        // Explicit property overrides on ViewModel
        var propVm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object)
        {
            ActiveProfile = profile,
            ActiveBearerToken = "token-cl"
        };
        var propResult = await propVm.SimulateClosedLoopAsync(new ClosedLoopRequest());
        Assert.Same(expectedResult, propResult);
        Assert.Same(expectedResult, vm.LastClosedLoopResult);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Success path via ClosedLoopSimulationRequest overload
        var directResult = await vm.SimulateClosedLoopAsync(new ClosedLoopSimulationRequest());
        Assert.Same(expectedResult, directResult);

        // Fault path
        clientMock.Setup(c => c.SimulateClosedLoopEndocrineAsync(
                profile,
                "token-cl",
                It.IsAny<ClosedLoopSimulationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulation divergence"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SimulateClosedLoopAsync(new ClosedLoopRequest()));
        Assert.Equal("Simulation divergence", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies genomic profile analysis with various regimen string representations and error handling.
    /// </summary>
    [Fact]
    public async Task ClinicalGenomicsWorkspaceViewModel_AnalyzeGenomicProfileAsync_AllPaths()
    {
        // WHAT: Tests AnalyzeGenomicProfileAsync validation, regimen parsing ("FemaleToMale", "FtM", "MaleToFemale", other), and faults.
        // WHY:  Covers all branch conditions in FASTA sequence immunogenicity screening.
        // HOW:  Tests null/whitespace sequence, regimen branches, mock client return, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "token-gen");

        // Sequence validation
        await Assert.ThrowsAsync<ArgumentException>(() => vm.AnalyzeGenomicProfileAsync(string.Empty, "MtF"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.AnalyzeGenomicProfileAsync("   ", "MtF"));

        var expectedProfile = CreatePatientGenomicProfile("P1", TherapeuticRegimen.FemaleToMale);

        clientMock.Setup(c => c.AnalyzeGenomicSequenceAsync(
                profile,
                "token-gen",
                It.Is<SequenceAnalysisRequest>(r => r.Regimen == TherapeuticRegimen.FemaleToMale),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedProfile);

        // "FemaleToMale" branch
        var result1 = await vm.AnalyzeGenomicProfileAsync("ATCGATCG", "FemaleToMale");
        Assert.Same(expectedProfile, result1);
        Assert.Same(expectedProfile, vm.LastGenomicProfile);

        // "FtM" branch
        var result2 = await vm.AnalyzeGenomicProfileAsync("ATCGATCG", "FtM");
        Assert.Same(expectedProfile, result2);

        // "MaleToFemale" / default branch
        var mtfProfile = CreatePatientGenomicProfile("P2", TherapeuticRegimen.MaleToFemale);
        clientMock.Setup(c => c.AnalyzeGenomicSequenceAsync(
                profile,
                "token-gen",
                It.Is<SequenceAnalysisRequest>(r => r.Regimen == TherapeuticRegimen.MaleToFemale),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mtfProfile);

        var result3 = await vm.AnalyzeGenomicProfileAsync("ATCGATCG", "MaleToFemale");
        Assert.Same(mtfProfile, result3);

        // Fault path
        clientMock.Setup(c => c.AnalyzeGenomicSequenceAsync(
                profile,
                "token-gen",
                It.IsAny<SequenceAnalysisRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Sequence parser failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AnalyzeGenomicProfileAsync("ATCG", "MtF"));
        Assert.Equal("Sequence parser failure", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies pharmacogenomic adjustments across overloads, validation, and error paths.
    /// </summary>
    [Fact]
    public async Task ClinicalGenomicsWorkspaceViewModel_ApplyGenomicAdjustmentsAsync_AllPaths()
    {
        // WHAT: Tests ApplyGenomicAdjustmentsAsync with UI request and API request models.
        // WHY:  Covers null validation, credential verification, and error states.
        // HOW:  Asserts null guards, mock responses, and failure state handling.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "token-adj");

        // Null guards
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ApplyGenomicAdjustmentsAsync((PharmacogenomicsAdjustmentRequest)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ApplyGenomicAdjustmentsAsync((GenomicAnalysisRequest)null!));

        // Success path
        var expectedResult = new GenomicAdjustmentResult().GetExamples();

        clientMock.Setup(c => c.ApplyGenomicAdjustmentsAsync(
                profile,
                "token-adj",
                It.IsAny<GenomicAnalysisRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.ApplyGenomicAdjustmentsAsync(new PharmacogenomicsAdjustmentRequest());
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastAdjustmentResult);

        var directResult = await vm.ApplyGenomicAdjustmentsAsync(new GenomicAnalysisRequest());
        Assert.Same(expectedResult, directResult);

        // Fault path
        clientMock.Setup(c => c.ApplyGenomicAdjustmentsAsync(
                profile,
                "token-adj",
                It.IsAny<GenomicAnalysisRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Adjustment error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ApplyGenomicAdjustmentsAsync(new PharmacogenomicsAdjustmentRequest()));
        Assert.Equal("Adjustment error", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies patient Digital DNA query filtering, regimen parsing, and error reporting.
    /// </summary>
    [Fact]
    public async Task ClinicalGenomicsWorkspaceViewModel_ListPatientDnaAsync_AllPaths()
    {
        // WHAT: Tests ListPatientDnaAsync argument validation, regimen parsing branches, and error reporting.
        // WHY:  Covers all filter conditions for patient Digital DNA blueprint packages.
        // HOW:  Tests null/whitespace username, "FemaleToMale", "FtM", "MaleToFemale", "MtF", null regimen, and faults.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "token-dna");

        // Username validation
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListPatientDnaAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListPatientDnaAsync("   "));

        var expectedPackage = new PatientDigitalDnaPackage().GetExamples();

        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(
                profile,
                "token-dna",
                "alice",
                TherapeuticRegimen.FemaleToMale,
                "Hexagonal Nanotube",
                4.5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPackage);

        // FemaleToMale regimen
        var result1 = await vm.ListPatientDnaAsync("alice", "FemaleToMale", "Hexagonal Nanotube", 4.5);
        Assert.Same(expectedPackage, result1);
        Assert.Same(expectedPackage, vm.LastPatientDnaPackage);

        // FtM regimen
        var result2 = await vm.ListPatientDnaAsync("alice", "FtM", "Hexagonal Nanotube", 4.5);
        Assert.Same(expectedPackage, result2);

        // MaleToFemale regimen
        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(
                profile,
                "token-dna",
                "alice",
                TherapeuticRegimen.MaleToFemale,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPackage);

        var result3 = await vm.ListPatientDnaAsync("alice", "MaleToFemale");
        Assert.Same(expectedPackage, result3);

        var result4 = await vm.ListPatientDnaAsync("alice", "MtF");
        Assert.Same(expectedPackage, result4);

        // Null regimen
        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(
                profile,
                "token-dna",
                "alice",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPackage);

        var result5 = await vm.ListPatientDnaAsync("alice");
        Assert.Same(expectedPackage, result5);

        // Unrecognized regimen
        var result6 = await vm.ListPatientDnaAsync("alice", "unknown-regimen");
        Assert.Same(expectedPackage, result6);

        // Fault path
        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(
                profile,
                "token-dna",
                "alice",
                It.IsAny<TherapeuticRegimen?>(),
                It.IsAny<string?>(),
                It.IsAny<double?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DNA fetch failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListPatientDnaAsync("alice"));
        Assert.Equal("DNA fetch failure", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies patient Digital DNA registration across package and request overloads, title formatting, and errors.
    /// </summary>
    [Fact]
    public async Task ClinicalGenomicsWorkspaceViewModel_CreatePatientDnaAsync_AllPaths()
    {
        // WHAT: Tests CreatePatientDnaAsync package and request overloads, title branching, and exception reporting.
        // WHY:  Covers all branches in Digital DNA database record creation.
        // HOW:  Tests null guards, TargetTissue title formatting, mock return, and fault handling.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new ClinicalGenomicsWorkspaceViewModel(clientMock.Object, () => profile, () => "token-create");

        // Validation guards
        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreatePatientDnaAsync(string.Empty, new PatientDigitalDnaPackage().GetExamples()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreatePatientDnaAsync("alice", (PatientDigitalDnaPackage)null!));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreatePatientDnaAsync(string.Empty, new CreatePatientDnaRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreatePatientDnaAsync("alice", (CreatePatientDnaRequest)null!));

        var expectedRecord = new PatientDigitalDnaRecord().GetExamples();

        clientMock.Setup(c => c.CreatePatientDnaAsync(
                profile,
                "token-create",
                "alice",
                It.Is<CreatePatientDnaRequest>(r => r.Title == "Hexagonal Nanotube (Anterior Pituitary)"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRecord);

        // Package with TargetTissue
        var packageWithTissue = new PatientDigitalDnaPackage
        {
            PatientId = "alice",
            NanocarrierGeometry = "Hexagonal Nanotube",
            TargetTissue = "Anterior Pituitary",
            ScaffoldFasta = "ATCG",
            ProvenanceSha256 = "sha256"
        };

        var result1 = await vm.CreatePatientDnaAsync("alice", packageWithTissue);
        Assert.Same(expectedRecord, result1);
        Assert.Same(expectedRecord, vm.LastCreatedDnaRecord);

        // Package without TargetTissue
        var expectedRecordNoTissue = new PatientDigitalDnaRecord().GetExamples();

        clientMock.Setup(c => c.CreatePatientDnaAsync(
                profile,
                "token-create",
                "alice",
                It.Is<CreatePatientDnaRequest>(r => r.Title == "Hexagonal Nanotube"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRecordNoTissue);

        var packageNoTissue = new PatientDigitalDnaPackage
        {
            PatientId = "alice",
            NanocarrierGeometry = "Hexagonal Nanotube",
            TargetTissue = string.Empty,
            ScaffoldFasta = "ATCG",
            ProvenanceSha256 = "sha256"
        };

        var result2 = await vm.CreatePatientDnaAsync("alice", packageNoTissue);
        Assert.Same(expectedRecordNoTissue, result2);

        // Request overload
        var directResult = await vm.CreatePatientDnaAsync("alice", new CreatePatientDnaRequest { Title = "Hexagonal Nanotube" });
        Assert.Same(expectedRecordNoTissue, directResult);

        // Fault path
        clientMock.Setup(c => c.CreatePatientDnaAsync(
                profile,
                "token-create",
                "alice",
                It.IsAny<CreatePatientDnaRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ledger creation failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreatePatientDnaAsync("alice", packageNoTissue));
        Assert.Equal("Ledger creation failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    // =========================================================================
    // WetLabComplianceWorkspaceViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void WetLabComplianceWorkspaceViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in WetLabComplianceWorkspaceViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new WetLabComplianceWorkspaceViewModel(null!));
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="WetLabComplianceWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public void WetLabComplianceWorkspaceViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests all observable properties of WetLabComplianceWorkspaceViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new WetLabComplianceWorkspaceViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-wetlab";
        Assert.Equal("token-wetlab", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-wetlab";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastOpentronsResult);
        Assert.Null(vm.LastGelAnalysisResult);
        Assert.Null(vm.LastAuditLedgerVerification);
        Assert.Null(vm.LastFederationManifest);
    }

    /// <summary>
    /// Verifies <see cref="OpentronsGenerationRequest"/> defaults and script model projection.
    /// </summary>
    [Fact]
    public void OpentronsGenerationRequest_DefaultsAndProjection()
    {
        // WHAT: Tests OpentronsGenerationRequest default values and ToScriptRequest() mapping.
        // WHY:  Ensures correct transformation from UI parameters to API request payload.
        // HOW:  Instantiates record with defaults and custom options, converts, and verifies fields.
        var req = new OpentronsGenerationRequest("SAMPLE-001");
        Assert.Equal("SAMPLE-001", req.SequenceOrSampleId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, req.Regimen);
        Assert.Equal("Nanotube", req.Geometry);
        Assert.Equal(4.8, req.TargetKd);
        Assert.Null(req.Options);

        var scriptReq = req.ToScriptRequest();
        Assert.Equal("SAMPLE-001", scriptReq.SequenceOrSampleId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, scriptReq.Regimen);
        Assert.Equal("Nanotube", scriptReq.Geometry);
        Assert.Equal(4.8, scriptReq.TargetKd);
        Assert.NotNull(scriptReq.Options);

        var customOptions = new OpentronsProtocolOptions { TotalReactionVolumeMicroliters = 100.0 };
        var customReq = new OpentronsGenerationRequest("SEQ-XYZ", TherapeuticRegimen.FemaleToMale, "Cuboid", 2.5, customOptions);
        var customScriptReq = customReq.ToScriptRequest();
        Assert.Equal(100.0, customScriptReq.Options.TotalReactionVolumeMicroliters);
    }

    /// <summary>
    /// Verifies Opentrons protocol generation across request overloads, validation, and error states.
    /// </summary>
    [Fact]
    public async Task WetLabComplianceWorkspaceViewModel_GenerateOpentronsProtocolAsync_AllPaths()
    {
        // WHAT: Tests GenerateOpentronsProtocolAsync with OpentronsGenerationRequest and GenerateOpentronsScriptRequest overloads.
        // WHY:  Covers null validation, credential resolution, mock response, and exception reporting.
        // HOW:  Tests null checks, missing session checks, mock execution, and failure paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => "token-ot2");

        // Null checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.GenerateOpentronsProtocolAsync((OpentronsGenerationRequest)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.GenerateOpentronsProtocolAsync((GenerateOpentronsScriptRequest)null!));

        // Missing session via provider
        var noTokenVm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTokenVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));

        var noProfileVm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => null, () => "token-ot2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));

        var whitespaceTokenVm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => "   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => whitespaceTokenVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));

        // Null provider checks (1-arg constructor)
        var nullProviderVm = new WetLabComplianceWorkspaceViewModel(clientMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));
        nullProviderVm.ActiveProfile = profile;
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));

        // Success path
        var expectedResult = CreateOpentronsResult();

        clientMock.Setup(c => c.GenerateOpentronsProtocolAsync(
                profile,
                "token-ot2",
                It.IsAny<GenerateOpentronsScriptRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1"));
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastOpentronsResult);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Explicit property overrides on ViewModel
        var propVm = new WetLabComplianceWorkspaceViewModel(clientMock.Object)
        {
            ActiveProfile = profile,
            ActiveBearerToken = "token-ot2"
        };
        var propResult = await propVm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1"));
        Assert.Same(expectedResult, propResult);

        var directResult = await vm.GenerateOpentronsProtocolAsync(new GenerateOpentronsScriptRequest { SequenceOrSampleId = "S1" });
        Assert.Same(expectedResult, directResult);

        // Fault path
        clientMock.Setup(c => c.GenerateOpentronsProtocolAsync(
                profile,
                "token-ot2",
                It.IsAny<GenerateOpentronsScriptRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Protocol generator error"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GenerateOpentronsProtocolAsync(new OpentronsGenerationRequest("S1")));
        Assert.Equal("Protocol generator error", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies agarose gel image analysis execution, validation, and error handling.
    /// </summary>
    [Fact]
    public async Task WetLabComplianceWorkspaceViewModel_AnalyzeGelImageAsync_AllPaths()
    {
        // WHAT: Tests AnalyzeGelImageAsync null check, mock return, and fault handling.
        // WHY:  Covers all branches in gel image analysis.
        // HOW:  Asserts null guard, mock execution, and exception capture.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => "token-gel");

        // Null guard
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.AnalyzeGelImageAsync(null!));

        // Success path
        var request = new GelAnalysisRequest().GetExamples();
        var expectedResult = new GelAnalysisResult().GetExamples();

        clientMock.Setup(c => c.AnalyzeGelAsync(profile, "token-gel", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.AnalyzeGelImageAsync(request);
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastGelAnalysisResult);
        Assert.False(vm.IsBusy);

        // Fault path
        clientMock.Setup(c => c.AnalyzeGelAsync(profile, "token-gel", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Densitometry segmentation fault"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AnalyzeGelImageAsync(request));
        Assert.Equal("Densitometry segmentation fault", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies audit ledger cryptographic verification execution and error handling.
    /// </summary>
    [Fact]
    public async Task WetLabComplianceWorkspaceViewModel_VerifyAuditLedgerAsync_AllPaths()
    {
        // WHAT: Tests VerifyAuditLedgerAsync happy path and fault handling.
        // WHY:  Covers all branches in audit ledger verification.
        // HOW:  Sets up mock client response, asserts state updates, and verifies exception reporting.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => "token-ledger");

        // Success path
        var expectedResult = new AuditLedgerVerificationResult().GetExamples();

        clientMock.Setup(c => c.VerifyAuditLedgerAsync(profile, "token-ledger", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await vm.VerifyAuditLedgerAsync();
        Assert.Same(expectedResult, result);
        Assert.Same(expectedResult, vm.LastAuditLedgerVerification);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // Fault path
        clientMock.Setup(c => c.VerifyAuditLedgerAsync(profile, "token-ledger", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hash chain verification failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.VerifyAuditLedgerAsync());
        Assert.Equal("Hash chain verification failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// Verifies trajectory federation manifest generation across string and request overloads.
    /// </summary>
    [Fact]
    public async Task WetLabComplianceWorkspaceViewModel_BuildFederatedManifestAsync_AllPaths()
    {
        // WHAT: Tests BuildFederatedManifestAsync with string dataset name and request overloads.
        // WHY:  Covers string validation, request construction, and exception reporting.
        // HOW:  Asserts null/whitespace checks, mock execution, and failure state capture.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new WetLabComplianceWorkspaceViewModel(clientMock.Object, () => profile, () => "token-fed");

        // String overload validation
        await Assert.ThrowsAsync<ArgumentException>(() => vm.BuildFederatedManifestAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.BuildFederatedManifestAsync("   "));

        // Request overload null check
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.BuildFederatedManifestAsync((FederationBuildManifestRequest)null!));

        // Success path
        var expectedManifest = CreateTrajectoryManifest("traj-run1.xyz");

        clientMock.Setup(c => c.BuildFederationManifestAsync(
                profile,
                "token-fed",
                It.Is<FederationBuildManifestRequest>(r => r.TrajectoryFileName == "traj-run1.xyz"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedManifest);

        var result = await vm.BuildFederatedManifestAsync("traj-run1.xyz");
        Assert.Same(expectedManifest, result);
        Assert.Same(expectedManifest, vm.LastFederationManifest);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        var directResult = await vm.BuildFederatedManifestAsync(new FederationBuildManifestRequest { TrajectoryFileName = "traj-run1.xyz" });
        Assert.Same(expectedManifest, directResult);

        // Fault path
        clientMock.Setup(c => c.BuildFederationManifestAsync(
                profile,
                "token-fed",
                It.IsAny<FederationBuildManifestRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Manifest creation failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.BuildFederatedManifestAsync("traj-run1.xyz"));
        Assert.Equal("Manifest creation failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    // =========================================================================
    // MainShellViewModel Integration Tests
    // =========================================================================

    /// <summary>
    /// Verifies that <see cref="MainShellViewModel"/> initializes child workspace ViewModels with ambient credentials.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_ChildWorkspaces_AmbientCredentials_And_Execution()
    {
        // WHAT: Tests MainShellViewModel's SimulationWorkspace, ClinicalGenomicsWorkspace, and WetLabComplianceWorkspace properties.
        // WHY:  Verifies child ViewModels successfully resolve active instance profile and token from the parent shell.
        // HOW:  Initializes shell with profile, connects instance to establish active token, executes workspace calls.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var profile = CreateProfile("inst-alpha");
        repoMock.Setup(r => r.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([profile]);

        var snapshotOnline = new InstanceConnectionSnapshot(
            "inst-alpha",
            "Test Node",
            InstanceConnectionState.Online,
            10.0,
            20.0,
            45.0,
            0,
            120.0,
            DateTimeOffset.UtcNow,
            null);

        coordMock.Setup(c => c.GetSnapshots()).Returns([snapshotOnline]);
        coordMock.Setup(c => c.ConnectInstanceAsync("inst-alpha", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotOnline);

        nodeMock.Setup(n => n.AuthenticateAsync(profile, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("active-jwt-token", "Bearer", 3600, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "admin", "Administrator"));

        using var shellVm = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);

        // Verify default child workspaces are not null
        Assert.NotNull(shellVm.SimulationWorkspace);
        Assert.NotNull(shellVm.ClinicalGenomicsWorkspace);
        Assert.NotNull(shellVm.WetLabComplianceWorkspace);

        await shellVm.InitializeAsync();
        Assert.Equal("inst-alpha", shellVm.SelectedInstanceId);

        // Attempting workspace call before authentication throws InvalidOperationException (no token)
        await Assert.ThrowsAsync<InvalidOperationException>(() => shellVm.SimulationWorkspace.RunSerumStabilityAsync(new SerumStabilityRequest()));

        // Authenticate instance
        bool connected = await shellVm.ConnectSelectedInstanceAsync("secret123".AsMemory());
        Assert.True(connected);

        // Now workspace call succeeds using ambient token!
        var serumResult = CreateSerumStabilityResult();
        nodeMock.Setup(n => n.RunSerumStabilityAsync(profile, "active-jwt-token", It.IsAny<SerumStabilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(serumResult);

        var simResult = await shellVm.SimulationWorkspace.RunSerumStabilityAsync(new SerumStabilityRequest());
        Assert.Same(serumResult, simResult);

        // Clinical genomics workspace call
        var closedLoopResult = new ClosedLoopSimulationResult().GetExamples();
        nodeMock.Setup(n => n.SimulateClosedLoopEndocrineAsync(profile, "active-jwt-token", It.IsAny<ClosedLoopSimulationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(closedLoopResult);

        var clResult = await shellVm.ClinicalGenomicsWorkspace.SimulateClosedLoopAsync(new ClosedLoopRequest());
        Assert.Same(closedLoopResult, clResult);

        // Wet lab compliance workspace call
        var auditResult = new AuditLedgerVerificationResult().GetExamples();
        nodeMock.Setup(n => n.VerifyAuditLedgerAsync(profile, "active-jwt-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(auditResult);

        var alResult = await shellVm.WetLabComplianceWorkspace.VerifyAuditLedgerAsync();
        Assert.Same(auditResult, alResult);
    }

    /// <summary>
    /// Verifies that custom workspace ViewModel injection into <see cref="MainShellViewModel"/> is respected.
    /// </summary>
    [Fact]
    public void MainShellViewModel_CustomWorkspaceInjection_Respected()
    {
        // WHAT: Tests constructor override parameters for child workspace ViewModels in MainShellViewModel.
        // WHY:  Allows dependency injection overrides during modular unit testing.
        // HOW:  Constructs custom workspace instances, passes to MainShellViewModel, verifies properties.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var customSim = new SimulationWorkspaceViewModel(nodeMock.Object);
        var customClin = new ClinicalGenomicsWorkspaceViewModel(nodeMock.Object);
        var customWet = new WetLabComplianceWorkspaceViewModel(nodeMock.Object);

        using var shellVm = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeMock.Object,
            simulationWorkspace: customSim,
            clinicalGenomicsWorkspace: customClin,
            wetLabComplianceWorkspace: customWet);

        Assert.Same(customSim, shellVm.SimulationWorkspace);
        Assert.Same(customClin, shellVm.ClinicalGenomicsWorkspace);
        Assert.Same(customWet, shellVm.WetLabComplianceWorkspace);
    }
}
