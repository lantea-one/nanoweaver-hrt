using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and integration test suite for <see cref="DepotStudio"/> validating interactive 180-day depot parameter configuration,
/// simulation execution dispatch, clinical metric displays (TTR %, burst phase, steady-state rate, re-dosing alert),
/// longitudinal serum assay calibration, observed SVG markers, safety alerts, and reset actions.
/// </summary>
public sealed class DepotStudioComponentTests : TestContext
{
    /// <summary>
    /// Thread-safe localizer service for UI text.
    /// </summary>
    private readonly WeaverLocalizer _localizer = new();

    /// <summary>
    /// Computational time-release depot service.
    /// </summary>
    private readonly TimeReleaseDepotService _depotService = new();

    /// <summary>
    /// Longitudinal Bayesian depot calibration service.
    /// </summary>
    private readonly DepotCalibrationService _calibrationService;

    /// <summary>
    /// Mocked depot formulation repository for studio persistence actions.
    /// </summary>
    private readonly Mock<IDepotFormulationRepository> _depotRepoMock = new();

    /// <summary>
    /// Mocked longitudinal depot assay repository for studio persistence actions.
    /// </summary>
    private readonly Mock<IDepotAssayRepository> _assayRepoMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotStudioComponentTests"/> class.
    /// </summary>
    public DepotStudioComponentTests()
    {
        // WHAT: Configures dependency injection container for bUnit component testing.
        // WHY:  DepotStudio depends on ITimeReleaseDepotService, IDepotCalibrationService, repositories, and IWeaverLocalizer.
        // HOW:  Registers real computational services and mocked repositories into TestContext.Services.
        _calibrationService = new DepotCalibrationService(_depotService);

        _depotRepoMock
            .Setup(r => r.UpsertDepotFormulationAsync(It.IsAny<DepotFormulationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _assayRepoMock
            .Setup(r => r.RecordObservationAsync(It.IsAny<DepotAssayObservationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepotAssayObservationRecord obs, CancellationToken _) => obs with { ObservationId = "assay_test_1" });

        _assayRepoMock
            .Setup(r => r.SaveCalibrationSnapshotAsync(It.IsAny<DepotCalibrationSnapshotRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepotCalibrationSnapshotRecord snap, CancellationToken _) => snap with { SnapshotId = "calib_test_1" });

        Services.AddSingleton<IWeaverLocalizer>(_localizer);
        Services.AddSingleton<ITimeReleaseDepotService>(_depotService);
        Services.AddSingleton<IDepotCalibrationService>(_calibrationService);
        Services.AddSingleton(_depotRepoMock.Object);
        Services.AddSingleton(_assayRepoMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="DepotStudio"/> renders the form controls and computes default baseline simulation metrics on initial render.
    /// </summary>
    [Fact]
    public void DepotStudio_InitialRender_RendersFormAndDefaultSimulationMetrics()
    {
        // WHAT: Renders DepotStudio and inspects initial DOM structure.
        // WHY:  Guarantees that clinicians receive immediate visual feedback and default 180-day simulation metrics upon loading the page.
        // HOW:  Renders component and asserts on header, inputs, buttons, metric cards, SVG chart, and longitudinal calibration panel.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        Assert.Contains("180-Day Semi-Annual Time-Release Depot Studio", cut.Markup);
        Assert.NotNull(cut.Find("#select-regimen"));
        Assert.NotNull(cut.Find("#select-matrix"));
        Assert.NotNull(cut.Find("#input-initial-mass"));
        Assert.NotNull(cut.Find("#input-erosion-rate"));
        Assert.NotNull(cut.Find("#btn-run-depot-simulation"));
        Assert.NotNull(cut.Find("#btn-reset-depot-defaults"));
        Assert.NotNull(cut.Find("#longitudinal-calibration-panel"));
        Assert.NotNull(cut.Find("#input-assay-day"));
        Assert.NotNull(cut.Find("#btn-record-assay-recalibrate"));

        // Asserts clinical metric cards rendered
        Assert.Contains("TIME IN TARGET RANGE", cut.Markup);
        Assert.Contains("PHASE I BURST FRACTION", cut.Markup);
        Assert.Contains("STEADY-STATE RATE", cut.Markup);
        Assert.Contains("TERMINAL RE-DOSING DAY", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);

        // Asserts SVG trajectory and table
        Assert.NotNull(cut.Find("svg"));
        Assert.NotNull(cut.Find("table.data-table"));
    }

    /// <summary>
    /// Verifies that modifying formulation parameters and clicking "Run 180-Day Simulation" updates the simulation result.
    /// </summary>
    [Fact]
    public void DepotStudio_RunSimulation_UpdatesMetricsOnFormChanges()
    {
        // WHAT: Changes initial mass and triggers the simulation button.
        // WHY:  Ensures interactive parameter explorations trigger computational re-evaluations accurately.
        // HOW:  Changes input values, clicks the execute button, and verifies updated markup values.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        cut.Find("#input-initial-mass").Change("25.0");
        cut.Find("#btn-run-depot-simulation").Click();

        Assert.Contains("25.0 mg", cut.Markup);
    }

    /// <summary>
    /// Verifies that changing the therapeutic regimen dropdown automatically re-executes the simulation with regimen-specific setpoints.
    /// </summary>
    [Fact]
    public void DepotStudio_HandleRegimenChanged_ReExecutesSimulationWithNewRegimen()
    {
        // WHAT: Selects FemaleToMale in regimen dropdown and then switches back to MaleToFemale.
        // WHY:  Verifies that switching between estrogenic and androgenic therapies updates default loaded mass (1050.0 mg FtM, 16.0 mg MtF).
        // HOW:  Fires change event on #select-regimen for both regimens using fresh cut.Find queries.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        cut.Find("#select-regimen").Change(TherapeuticRegimen.FemaleToMale.ToString());
        Assert.Contains("1050.0 mg", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);

        cut.Find("#select-regimen").Change(TherapeuticRegimen.MaleToFemale.ToString());
        Assert.Contains("16.0 mg", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);
    }

    /// <summary>
    /// Verifies that clicking "Reset Defaults" restores initial clinical formulation values.
    /// </summary>
    [Fact]
    public void DepotStudio_ResetDefaults_RestoresInitialParameters()
    {
        // WHAT: Modifies input values and then clicks the reset defaults button.
        // WHY:  Allows clinicians to restore standard baseline values with a single action.
        // HOW:  Alters mass input, clicks reset button, and verifies fields reset to 16.0 mg.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        cut.Find("#input-initial-mass").Change("50.0");
        cut.Find("#btn-reset-depot-defaults").Click();

        Assert.Contains("16.0", cut.Markup);
    }

    /// <summary>
    /// Verifies that a severely under-dosed formulation triggers the suboptimal clinical verdict badge.
    /// </summary>
    [Fact]
    public void DepotStudio_SuboptimalFormulation_RendersSuboptimalVerdict()
    {
        // WHAT: Sets initial mass to an extremely low value (1.0 mg) causing premature depletion and inadequate TTR.
        // WHY:  Ensures that dangerous or ineffective depot formulations are flagged with warning/danger indicators.
        // HOW:  Sets initial mass to 1.0, clicks run, and asserts on suboptimal verdict text.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        cut.Find("#input-initial-mass").Change("1.0");
        cut.Find("#btn-run-depot-simulation").Click();

        Assert.Contains("SUBOPTIMAL FORMULATION", cut.Markup);
    }

    /// <summary>
    /// Verifies recording longitudinal serum assays, rendering observed SVG markers and recalibrated trajectory,
    /// triggering clinical safety alerts (e.g. FtM critical hematocrit), validating invalid inputs, and clearing assays.
    /// </summary>
    [Fact]
    public void DepotStudio_LongitudinalAssayCalibration_RendersOverlayAlertsAndValidation()
    {
        // WHAT: Exercises the Milestone 34 longitudinal calibration panel end-to-end in bUnit.
        // WHY:  Verifies assay recording, SVG marker projection, recalibrated curve overlay, safety alert callouts, and input validation.
        // HOW:  Uses fresh cut.Find(...) queries after each dispatch to test validation error, MtF optimal calibration,
        //       FtM critical hematocrit alert, and Clear Assays reset.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        // 1. Invalid day triggers validation banner
        cut.Find("#input-assay-day").Change("0");
        cut.Find("#btn-record-assay-recalibrate").Click();
        Assert.Contains("Day since implant must be between 1 and 365.", cut.Find("#assay-validation-error").TextContent);

        // 2. Valid MtF Day 30 and Day 60 assays render SVG recalibrated overlay, markers, and adaptive telemetry card
        cut.Find("#input-assay-day").Change("30");
        cut.Find("#input-assay-e2").Change("515");
        cut.Find("#btn-record-assay-recalibrate").Click();

        Assert.NotNull(cut.Find("#adaptive-calibration-card"));
        Assert.NotNull(cut.Find("#calibration-safety-badge"));
        Assert.NotNull(cut.Find("path[data-series='recalibrated']"));
        Assert.Single(cut.FindAll("circle.assay-marker"));
        Assert.Contains("Day 30", cut.Find("#recorded-assays-table").TextContent);

        cut.Find("#input-assay-day").Change("60");
        cut.Find("#input-assay-e2").Change("490");
        cut.Find("#btn-record-assay-recalibrate").Click();
        Assert.Equal(2, cut.FindAll("circle.assay-marker").Count);

        // 3. Clear Assays removes recalibration overlay and table
        cut.Find("#btn-clear-assays").Click();
        Assert.Empty(cut.FindAll("#adaptive-calibration-card"));
        Assert.Empty(cut.FindAll("circle.assay-marker"));

        // 4. Switch to FtM regimen and record critical hematocrit (>= 54.0%) -> CriticalSafety alert rendered
        cut.Find("#select-regimen").Change(TherapeuticRegimen.FemaleToMale.ToString());
        cut.Find("#input-assay-day").Change("60");
        cut.Find("#input-assay-t").Change("24.5");
        cut.Find("#input-assay-hct").Change("55.2");
        cut.Find("#btn-record-assay-recalibrate").Click();

        Assert.Contains("FTM_HEMATOCRIT_CRITICAL", cut.Find("#calibration-safety-alerts").TextContent);
        Assert.Contains("Critical Safety Ceiling", cut.Find("#calibration-safety-badge").TextContent);
    }
}
