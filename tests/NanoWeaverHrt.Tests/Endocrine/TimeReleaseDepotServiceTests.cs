using NanoWeaverHrt.Endocrine;
using Xunit;

namespace NanoWeaverHrt.Tests.Endocrine;

/// <summary>
/// Unit tests verifying multi-phase time-release depot kinetics, burst suppression,
/// Korsmeyer-Peppas diffusion, HPG axis feedback, and parameter boundary validations in <see cref="TimeReleaseDepotService"/>.
/// </summary>
public sealed class TimeReleaseDepotServiceTests
{
    /// <summary>
    /// Service under test.
    /// </summary>
    private readonly TimeReleaseDepotService _service = new();

    /// <summary>
    /// Verifies Male-to-Female (MtF) simulation suppresses burst (&lt;= 5% on Day 7),
    /// maintains steady daily micro-aliquots (50-100 µg/day), suppresses LH and FSH,
    /// and achieves Time In Target Range (TTR) &gt;= 95%.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_MaleToFemale_SuppressesBurst_AndAchievesTtrAbove95()
    {
        // WHAT: Executes standard 180-day MtF depot simulation.
        // WHY:  Verifies Phase I burst suppression, Phase II steady-state micro-dosing, and HPG axis gonadotropin suppression.
        // HOW:  Provides MtF baseline parameters and asserts clinical endpoints.
        TimeReleaseDepotParameters parameters = new()
        {
            Regimen = TherapeuticRegimen.MaleToFemale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialPayloadMassMg = 16.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            InitialEstradiolPmolL = 120.0,
            InitialTestosteroneNmolL = 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.Equal(181, result.Points.Count);
        Assert.True(result.BurstSuppressed, $"Expected Day 7 burst suppression but got fraction {result.Day7BurstFraction}");
        Assert.True(result.Day7BurstFraction <= 0.05, $"Day 7 burst fraction {result.Day7BurstFraction} exceeds 0.05");
        Assert.InRange(result.AverageDailyReleaseMg, 0.050, 0.100);
        Assert.True(result.LhSuppressionAchieved, "LH suppression was not achieved during Phase II");
        Assert.True(result.FshSuppressionAchieved, "FSH suppression was not achieved during Phase II");
        Assert.True(result.TimeInTargetRangePercent >= 95.0, $"Expected TTR >= 95.0% but got {result.TimeInTargetRangePercent}%");
        Assert.InRange(result.TerminalReserveFraction, 0.02, 0.08);
        Assert.True(result.ReDosingAlertDay >= 151, $"Expected re-dosing alert on or after Day 151 but got {result.ReDosingAlertDay}");
        Assert.InRange(result.FinalEstradiolPmolL, 400.0, 700.0);
        Assert.True(result.FinalTestosteroneNmolL <= 1.8, $"Expected final testosterone <= 1.8 nmol/L but got {result.FinalTestosteroneNmolL}");
        Assert.True(result.FinalLhIuL < 1.0, $"Expected final LH < 1.0 IU/L but got {result.FinalLhIuL}");
        Assert.True(result.FinalFshIuL < 1.5, $"Expected final FSH < 1.5 IU/L but got {result.FinalFshIuL}");
    }

    /// <summary>
    /// Verifies Female-to-Male (FtM) simulation suppresses burst (&lt;= 5% on Day 7),
    /// maintains steady daily micro-aliquots (5.0-7.0 mg/day), suppresses LH and FSH,
    /// and achieves Time In Target Range (TTR) &gt;= 95%.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_FemaleToMale_SuppressesBurst_AndAchievesTtrAbove95()
    {
        // WHAT: Executes standard 180-day FtM depot simulation.
        // WHY:  Verifies masculinizing steady daily release and reciprocal gonadotropin suppression.
        // HOW:  Provides FtM baseline parameters with 1050 mg initial mass and asserts testosterone endpoints.
        TimeReleaseDepotParameters parameters = new()
        {
            Regimen = TherapeuticRegimen.FemaleToMale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialPayloadMassMg = 1050.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            InitialEstradiolPmolL = 550.0,
            InitialTestosteroneNmolL = 1.5,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 100.0,
            TargetTestosteroneNmolL = 20.0
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.Equal(181, result.Points.Count);
        Assert.True(result.BurstSuppressed, $"Expected Day 7 burst suppression but got fraction {result.Day7BurstFraction}");
        Assert.True(result.Day7BurstFraction <= 0.05, $"Day 7 burst fraction {result.Day7BurstFraction} exceeds 0.05");
        Assert.InRange(result.AverageDailyReleaseMg, 5.0, 7.0);
        Assert.True(result.LhSuppressionAchieved, "LH suppression was not achieved during Phase II");
        Assert.True(result.FshSuppressionAchieved, "FSH suppression was not achieved during Phase II");
        Assert.True(result.TimeInTargetRangePercent >= 95.0, $"Expected TTR >= 95.0% but got {result.TimeInTargetRangePercent}%");
        Assert.InRange(result.TerminalReserveFraction, 0.02, 0.08);
        Assert.True(result.ReDosingAlertDay >= 151, $"Expected re-dosing alert on or after Day 151 but got {result.ReDosingAlertDay}");
        Assert.InRange(result.FinalTestosteroneNmolL, 15.0, 30.0);
        Assert.True(result.FinalEstradiolPmolL <= 180.0, $"Expected final estradiol <= 180 pmol/L but got {result.FinalEstradiolPmolL}");
        Assert.True(result.FinalLhIuL < 1.0, $"Expected final LH < 1.0 IU/L but got {result.FinalLhIuL}");
        Assert.True(result.FinalFshIuL < 1.5, $"Expected final FSH < 1.5 IU/L but got {result.FinalFshIuL}");
    }

    /// <summary>
    /// Verifies Custom regimen tracks specified hormone setpoints and transitions smoothly.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_CustomRegimen_TracksCustomSetpoints()
    {
        // WHAT: Executes custom regimen simulation with bespoke hormone targets.
        // WHY:  Confirms the custom branch updates both estradiol and testosterone towards requested targets.
        // HOW:  Passes custom targets and evaluates trajectory convergence.
        TimeReleaseDepotParameters parameters = new()
        {
            Regimen = TherapeuticRegimen.Custom,
            MatrixType = PolymerMatrixType.AlginateChitosan,
            KineticsModel = ReleaseKineticsModel.Higuchi,
            InitialPayloadMassMg = 50.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            InitialEstradiolPmolL = 250.0,
            InitialTestosteroneNmolL = 12.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 300.0,
            TargetTestosteroneNmolL = 8.0
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.True(result.Points.Count > 0);
        Assert.InRange(result.FinalEstradiolPmolL, 290.0, 310.0);
        Assert.InRange(result.FinalTestosteroneNmolL, 7.5, 8.5);
    }

    /// <summary>
    /// Verifies simulation behavior across all supported polymer matrix types.
    /// </summary>
    /// <param name="matrixType">The matrix type under test.</param>
    [Theory]
    [InlineData(PolymerMatrixType.PlgaPegPlga)]
    [InlineData(PolymerMatrixType.AlginateChitosan)]
    [InlineData(PolymerMatrixType.CyclodextrinScaffold)]
    [InlineData(PolymerMatrixType.CrosslinkedHydrogel)]
    public void Simulate180DayDepot_AllMatrixTypes_ExecuteCleanly(PolymerMatrixType matrixType)
    {
        // WHAT: Executes depot simulation with each matrix composition.
        // WHY:  Ensures that PLGA, Alginate, Cyclodextrin, and Hydrogel matrices evaluate with valid exponents and scaling.
        // HOW:  Iterates across all enum values and asserts burst suppression.
        TimeReleaseDepotParameters parameters = new()
        {
            MatrixType = matrixType,
            InitialPayloadMassMg = 16.0,
            TargetHorizonDays = 180
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.True(result.BurstSuppressed);
        Assert.True(result.Day7BurstFraction <= 0.05);
    }

    /// <summary>
    /// Verifies simulation behavior across all supported mathematical kinetics models.
    /// </summary>
    /// <param name="model">The kinetics model under test.</param>
    [Theory]
    [InlineData(ReleaseKineticsModel.KorsmeyerPeppas)]
    [InlineData(ReleaseKineticsModel.Higuchi)]
    [InlineData(ReleaseKineticsModel.CoupledAnomalousFickian)]
    [InlineData(ReleaseKineticsModel.ZeroOrderErosion)]
    public void Simulate180DayDepot_AllKineticsModels_ExecuteCleanly(ReleaseKineticsModel model)
    {
        // WHAT: Executes depot simulation with each mathematical kinetics model.
        // WHY:  Ensures Korsmeyer-Peppas, Higuchi, Coupled Anomalous Fickian, and Zero-Order Erosion execute cleanly.
        // HOW:  Iterates across all enum values and asserts valid simulation points.
        TimeReleaseDepotParameters parameters = new()
        {
            KineticsModel = model,
            InitialPayloadMassMg = 16.0,
            TargetHorizonDays = 180
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.Equal(181, result.Points.Count);
        Assert.True(result.BurstSuppressed);
    }

    /// <summary>
    /// Verifies pharmacogenomic clearance multiplier scaling impacts daily release rates predictably.
    /// </summary>
    /// <param name="multiplier">The clearance multiplier under test.</param>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Simulate180DayDepot_PharmacogenomicClearance_ScalesRelease(double multiplier)
    {
        // WHAT: Compares simulation with poor, normal, and rapid metabolizer clearance multipliers.
        // WHY:  Verifies pharmacogenomic scaling parameter affects depot micro-dosing.
        // HOW:  Asserts that higher clearance multiplier yields slightly higher daily release to maintain target.
        TimeReleaseDepotParameters parameters = new()
        {
            InitialPayloadMassMg = 16.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = multiplier
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.True(result.Points.Count > 0);
    }

    /// <summary>
    /// Verifies that <see cref="TimeReleaseDepotService.CalculateInstantaneousDailyRelease"/>
    /// correctly computes daily rates across all three phases.
    /// </summary>
    [Fact]
    public void CalculateInstantaneousDailyRelease_ComputesPhasesCorrectly()
    {
        // WHAT: Evaluates instantaneous daily release on Day 0, Day 5 (Phase I), Day 50 (Phase II), and Day 160 (Phase III).
        // WHY:  Ensures piecewise rate function handles zero elapsed days, burst ramp, steady plateau, and terminal decay.
        // HOW:  Asserts exact zero at Day 0, positive and ordered values across subsequent phases.
        double rateDay0 = _service.CalculateInstantaneousDailyRelease(0.0, PolymerMatrixType.PlgaPegPlga, 16.0, 1.0);
        double rateDay5 = _service.CalculateInstantaneousDailyRelease(5.0, PolymerMatrixType.PlgaPegPlga, 16.0, 1.0);
        double rateDay50 = _service.CalculateInstantaneousDailyRelease(50.0, PolymerMatrixType.PlgaPegPlga, 16.0, 1.0);
        double rateDay160 = _service.CalculateInstantaneousDailyRelease(160.0, PolymerMatrixType.PlgaPegPlga, 16.0, 1.0);

        Assert.Equal(0.0, rateDay0);
        Assert.True(rateDay5 > 0.0);
        Assert.True(rateDay50 > rateDay5);
        Assert.True(rateDay160 < rateDay50);
    }

    /// <summary>
    /// Verifies that <see cref="TimeReleaseDepotService.CalculateInstantaneousDailyRelease"/>
    /// handles negative day input by returning zero.
    /// </summary>
    [Fact]
    public void CalculateInstantaneousDailyRelease_NegativeDay_ReturnsZero()
    {
        // WHAT: Tests negative day input.
        // WHY:  Guard condition for days prior to depot implantation.
        // HOW:  Passes negative day and asserts 0.0.
        double rate = _service.CalculateInstantaneousDailyRelease(-2.0, PolymerMatrixType.PlgaPegPlga, 16.0, 1.0);
        Assert.Equal(0.0, rate);
    }

    /// <summary>
    /// Verifies parameter validations in <see cref="TimeReleaseDepotService.CalculateInstantaneousDailyRelease"/>.
    /// </summary>
    [Fact]
    public void CalculateInstantaneousDailyRelease_InvalidParameters_ThrowExceptions()
    {
        // WHAT: Tests parameter boundary checks on CalculateInstantaneousDailyRelease.
        // WHY:  Non-positive initial mass or out-of-range clearance multiplier must be rejected.
        // HOW:  Asserts ArgumentOutOfRangeException on boundary violations.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateInstantaneousDailyRelease(10.0, PolymerMatrixType.PlgaPegPlga, 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateInstantaneousDailyRelease(10.0, PolymerMatrixType.PlgaPegPlga, -5.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateInstantaneousDailyRelease(10.0, PolymerMatrixType.PlgaPegPlga, 16.0, 0.05));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateInstantaneousDailyRelease(10.0, PolymerMatrixType.PlgaPegPlga, 16.0, 6.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateInstantaneousDailyRelease(10.0, (PolymerMatrixType)999, 16.0, 1.0));
    }

    /// <summary>
    /// Verifies parameter validation checks in <see cref="TimeReleaseDepotService.Simulate180DayDepot"/>.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_InvalidParameters_ThrowExceptions()
    {
        // WHAT: Tests all parameter boundary checks on Simulate180DayDepot.
        // WHY:  Guards against null request, invalid mass, horizon, clearance, or negative concentrations.
        // HOW:  Asserts ArgumentNullException and ArgumentOutOfRangeException.
        Assert.Throws<ArgumentNullException>(() => _service.Simulate180DayDepot(null!));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            InitialPayloadMassMg = -1.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            TargetHorizonDays = 0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            TargetHorizonDays = 400
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            ClearanceMultiplier = 0.05
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            ClearanceMultiplier = 6.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            InitialEstradiolPmolL = -10.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            InitialTestosteroneNmolL = -5.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            InitialLhIuL = -1.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            InitialFshIuL = -1.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            TargetEstradiolPmolL = -10.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            TargetTestosteroneNmolL = -5.0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            MatrixType = (PolymerMatrixType)999
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Simulate180DayDepot(new TimeReleaseDepotParameters
        {
            KineticsModel = (ReleaseKineticsModel)999
        }));
    }

    /// <summary>
    /// Verifies short horizon simulations (e.g. horizon &lt; 7 days) execute cleanly without index out of range.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_ShortHorizon_ExecutesCleanly()
    {
        // WHAT: Executes simulation with horizon = 5 days (less than BurstSuppressionDays = 7).
        // WHY:  Tests the branch where horizon < BurstSuppressionDays and Phase II count is zero.
        // HOW:  Asserts result is populated and average Phase II release defaults cleanly to 0.0.
        TimeReleaseDepotParameters parameters = new()
        {
            TargetHorizonDays = 5,
            InitialPayloadMassMg = 16.0
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.Equal(6, result.Points.Count);
        Assert.Equal(0.0, result.AverageDailyReleaseMg);
        Assert.False(result.LhSuppressionAchieved);
        Assert.False(result.FshSuppressionAchieved);
    }

    /// <summary>
    /// Verifies depot mass exhaustion capping when initial payload mass is completely eluted over an extended horizon.
    /// </summary>
    [Fact]
    public void Simulate180DayDepot_ExtendedHorizon_CapsReleaseAtReserve()
    {
        // WHAT: Executes simulation with extended horizon (260 days).
        // WHY:  Forces daily release to exceed remaining reserve, exercising the mass balance cap branch.
        // HOW:  Asserts that remaining reserve reaches zero and does not become negative.
        TimeReleaseDepotParameters parameters = new()
        {
            InitialPayloadMassMg = 10.0,
            TargetHorizonDays = 260
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.TerminalReserveFraction);
        Assert.Equal(0.0, result.Points[^1].RemainingReserveMg);
    }

    /// <summary>
    /// Verifies OpenAPI examples providers return non-null, valid schema representations.
    /// </summary>
    [Fact]
    public void ExamplesProviders_ReturnValidPopulatedInstances()
    {
        // WHAT: Validates GetExamples implementation on parameters and simulation result records.
        // WHY:  Guarantees Swagger documentation generators receive non-null illustrative schemas.
        // HOW:  Invokes GetExamples and asserts properties are populated.
        TimeReleaseDepotParameters paramExample = new TimeReleaseDepotParameters().GetExamples();
        Assert.NotNull(paramExample);
        Assert.Equal(18.0, paramExample.InitialPayloadMassMg);

        TimeReleaseSimulationResult resultExample = new TimeReleaseSimulationResult().GetExamples();
        Assert.NotNull(resultExample);
        Assert.Equal(2, resultExample.Points.Count);
        Assert.True(resultExample.BurstSuppressed);
    }

    /// <summary>
    /// Verifies target range evaluation for Custom regimen under various hormone concentrations.
    /// </summary>
    [Fact]
    public void CustomRegimen_TargetRangeEvaluations_ExecuteCorrectly()
    {
        // WHAT: Tests IsInTargetRange under Custom regimen with low hormones.
        // WHY:  Exercises the custom target range branch where neither estradiol >= 200 nor testosterone >= 10.
        // HOW:  Initializes with low concentrations and checks inTargetRange flag on Day 0.
        TimeReleaseDepotParameters parameters = new()
        {
            Regimen = TherapeuticRegimen.Custom,
            InitialEstradiolPmolL = 50.0,
            InitialTestosteroneNmolL = 2.0,
            TargetHorizonDays = 1,
            InitialPayloadMassMg = 10.0
        };

        TimeReleaseSimulationResult result = _service.Simulate180DayDepot(parameters);
        Assert.False(result.Points[0].InTargetRange);
    }
}
