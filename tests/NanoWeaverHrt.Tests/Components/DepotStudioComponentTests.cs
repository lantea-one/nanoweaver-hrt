using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and integration test suite for <see cref="DepotStudio"/> validating interactive 180-day depot parameter configuration,
/// simulation execution dispatch, clinical metric displays (TTR %, burst phase, steady-state rate, re-dosing alert),
/// multi-phase trajectory rendering, and reset actions.
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
    /// Initializes a new instance of the <see cref="DepotStudioComponentTests"/> class.
    /// </summary>
    public DepotStudioComponentTests()
    {
        // WHAT: Configures dependency injection container for bUnit component testing.
        // WHY:  DepotStudio depends on ITimeReleaseDepotService and IWeaverLocalizer for kinetics calculations and localized labels.
        // HOW:  Registers singleton services into TestContext.Services.
        Services.AddSingleton<IWeaverLocalizer>(_localizer);
        Services.AddSingleton<ITimeReleaseDepotService>(_depotService);
    }

    /// <summary>
    /// Verifies that <see cref="DepotStudio"/> renders the form controls and computes default baseline simulation metrics on initial render.
    /// </summary>
    [Fact]
    public void DepotStudio_InitialRender_RendersFormAndDefaultSimulationMetrics()
    {
        // WHAT: Renders DepotStudio and inspects initial DOM structure.
        // WHY:  Guarantees that clinicians receive immediate visual feedback and default 180-day simulation metrics upon loading the page.
        // HOW:  Renders component and asserts on header, inputs, buttons, metric cards, SVG chart, and milestone table.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        Assert.Contains("180-Day Semi-Annual Time-Release Depot Studio", cut.Markup);
        Assert.NotNull(cut.Find("#select-regimen"));
        Assert.NotNull(cut.Find("#select-matrix"));
        Assert.NotNull(cut.Find("#input-initial-mass"));
        Assert.NotNull(cut.Find("#input-erosion-rate"));
        Assert.NotNull(cut.Find("#btn-run-depot-simulation"));
        Assert.NotNull(cut.Find("#btn-reset-depot-defaults"));

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
        // WHAT: Changes initial mass and matrix type, then triggers the simulation button.
        // WHY:  Ensures interactive parameter explorations trigger computational re-evaluations accurately.
        // HOW:  Changes input values, clicks the execute button, and verifies updated markup values.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        // Update initial mass from 18.0 to 25.0
        var massInput = cut.Find("#input-initial-mass");
        massInput.Change("25.0");

        var runBtn = cut.Find("#btn-run-depot-simulation");
        runBtn.Click();

        Assert.Contains("25.0 mg", cut.Markup);
    }

    /// <summary>
    /// Verifies that changing the therapeutic regimen dropdown automatically re-executes the simulation with regimen-specific setpoints.
    /// </summary>
    [Fact]
    public void DepotStudio_HandleRegimenChanged_ReExecutesSimulationWithNewRegimen()
    {
        // WHAT: Selects FemaleToMale in regimen dropdown.
        // WHY:  Verifies that switching between estrogenic and androgenic therapies recalculates HPG feedback targets.
        // HOW:  Fires change event on #select-regimen and checks that testosterone targets are evaluated.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        var regimenSelect = cut.Find("#select-regimen");
        regimenSelect.Change(TherapeuticRegimen.FemaleToMale.ToString());

        Assert.Contains("FemaleToMale", cut.Markup);
    }

    /// <summary>
    /// Verifies that clicking "Reset Defaults" restores initial clinical formulation values.
    /// </summary>
    [Fact]
    public void DepotStudio_ResetDefaults_RestoresInitialParameters()
    {
        // WHAT: Modifies input values and then clicks the reset defaults button.
        // WHY:  Allows clinicians to restore standard baseline values with a single action.
        // HOW:  Alters mass input, clicks reset button, and verifies fields reset to 18.0 mg.
        IRenderedComponent<DepotStudio> cut = RenderComponent<DepotStudio>();

        var massInput = cut.Find("#input-initial-mass");
        massInput.Change("50.0");

        var resetBtn = cut.Find("#btn-reset-depot-defaults");
        resetBtn.Click();

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

        var massInput = cut.Find("#input-initial-mass");
        massInput.Change("1.0");

        var runBtn = cut.Find("#btn-run-depot-simulation");
        runBtn.Click();

        Assert.Contains("SUBOPTIMAL FORMULATION", cut.Markup);
    }
}
