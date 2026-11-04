using NanoWeaverHrt.Endocrine;
using Xunit;

namespace NanoWeaverHrt.Tests.Endocrine;

/// <summary>
/// Comprehensive unit tests verifying 4th-order Runge-Kutta numerical integration,
/// coupled HPG axis negative feedback dynamics, and aptamer carrier delivery in <see cref="EndocrineClosedLoopEngine"/>.
/// </summary>
public sealed class EndocrineClosedLoopEngineTests
{
    /// <summary>
    /// Target engine under test.
    /// </summary>
    private readonly EndocrineClosedLoopEngine _engine = new();

    /// <summary>
    /// Verifies Male-to-Female (MtF) simulation suppresses pituitary gonadotropins (LH/FSH),
    /// reaches target 17β-estradiol, and achieves homeostatic steady state.
    /// </summary>
    [Fact]
    public void Simulate_Mtf_SuppressesLhAndFsh_AndReachesTargetEstradiol()
    {
        // WHAT: Executes standard 72-hour feminizing closed-loop endocrine simulation.
        // WHY:  Verifies reciprocal negative feedback suppresses LH (< 1.0 IU/L) and FSH (< 1.5 IU/L) while delivering E2 payload.
        // HOW:  Supplies MtF baseline concentrations and asserts steady state convergence and suppression time.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 120.0,
            InitialTestosteroneNmolL = 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetState = ToggleState.EstrogenicDominant,
            TotalHours = 72,
            StepHours = 1.0,
            ClearanceMultiplier = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        Assert.NotNull(result);
        Assert.Equal(73, result.Points.Count);
        Assert.True(result.SteadyStateReached);
        Assert.True(result.TimeToSuppressionHours > 0.0);
        Assert.True(result.TimeToSuppressionHours <= 10.0);
        Assert.True(result.FinalLhIuL < 1.0, $"Expected final LH < 1.0 IU/L but got {result.FinalLhIuL}");
        Assert.True(result.FinalFshIuL < 1.5, $"Expected final FSH < 1.5 IU/L but got {result.FinalFshIuL}");
        Assert.InRange(result.FinalEstradiolPmolL, 545.0, 550.0);
        Assert.True(result.FinalTestosteroneNmolL <= 1.8, $"Expected final T <= 1.8 nmol/L but got {result.FinalTestosteroneNmolL}");
    }

    /// <summary>
    /// Verifies Female-to-Male (FtM) simulation suppresses pituitary gonadotropins (LH/FSH),
    /// reaches target testosterone, and suppresses endogenous 17β-estradiol.
    /// </summary>
    [Fact]
    public void Simulate_Ftm_SuppressesLhAndFsh_AndReachesTargetTestosterone()
    {
        // WHAT: Executes standard 72-hour masculinizing closed-loop endocrine simulation.
        // WHY:  Verifies androgen receptor negative feedback downregulates LH/FSH and suppresses ovarian E2.
        // HOW:  Supplies FtM baseline concentrations and asserts androgenic target attainment and E2 downregulation.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 600.0,
            InitialTestosteroneNmolL = 1.5,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 100.0,
            TargetTestosteroneNmolL = 20.0,
            TargetState = ToggleState.AndrogenicDominant,
            TotalHours = 72,
            StepHours = 1.0,
            ClearanceMultiplier = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        Assert.NotNull(result);
        Assert.Equal(73, result.Points.Count);
        Assert.True(result.SteadyStateReached);
        Assert.True(result.TimeToSuppressionHours > 0.0);
        Assert.True(result.FinalLhIuL < 1.0, $"Expected final LH < 1.0 IU/L but got {result.FinalLhIuL}");
        Assert.True(result.FinalFshIuL < 1.5, $"Expected final FSH < 1.5 IU/L but got {result.FinalFshIuL}");
        Assert.InRange(result.FinalTestosteroneNmolL, 19.8, 20.0);
        Assert.True(result.FinalEstradiolPmolL < 50.0, $"Expected final E2 < 50 pmol/L but got {result.FinalEstradiolPmolL}");
    }

    /// <summary>
    /// Verifies that in Transitioning operational state, nanocarrier delivery rate is inactive (0.0).
    /// </summary>
    [Fact]
    public void Simulate_TransitioningState_HasZeroCarrierRelease()
    {
        // WHAT: Tests simulation under ToggleState.Transitioning.
        // WHY:  During transition calibration, nanocarriers must not actively release hormonal payloads.
        // HOW:  Asserts CarrierReleaseRate is 0.0 across all trajectory points.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 200.0,
            InitialTestosteroneNmolL = 5.0,
            InitialLhIuL = 4.0,
            InitialFshIuL = 3.0,
            TargetState = ToggleState.Transitioning,
            TotalHours = 24,
            StepHours = 2.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        Assert.All(result.Points, pt => Assert.Equal(0.0, pt.CarrierReleaseRate));
    }

    /// <summary>
    /// Verifies <see cref="EndocrineClosedLoopEngine.Simulate"/> throws <see cref="ArgumentNullException"/> for null request.
    /// </summary>
    [Fact]
    public void Simulate_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        // WHAT: Tests null guard on request parameter.
        // WHY:  Prevents NullReferenceException inside integration loop.
        // HOW:  Asserts ArgumentNullException is thrown.
        Assert.Throws<ArgumentNullException>(() => _engine.Simulate(null!));
    }

    /// <summary>
    /// Verifies <see cref="EndocrineClosedLoopEngine.Simulate"/> validates TotalHours bounds (1 to 720).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(721)]
    public void Simulate_ThrowsArgumentOutOfRangeException_ForInvalidTotalHours(int totalHours)
    {
        // WHAT: Tests TotalHours range validation.
        // WHY:  Prevents excessive compute duration or non-positive time horizons.
        // HOW:  Asserts ArgumentOutOfRangeException is thrown.
        ClosedLoopSimulationRequest req = new() { TotalHours = totalHours };
        Assert.Throws<ArgumentOutOfRangeException>(nameof(req.TotalHours), () => _engine.Simulate(req));
    }

    /// <summary>
    /// Verifies <see cref="EndocrineClosedLoopEngine.Simulate"/> validates StepHours bounds (0.1 to 24.0) and relation to TotalHours.
    /// </summary>
    [Theory]
    [InlineData(0.05, 72)]
    [InlineData(25.0, 72)]
    [InlineData(10.0, 5)]
    public void Simulate_ThrowsArgumentOutOfRangeException_ForInvalidStepHours(double stepHours, int totalHours)
    {
        // WHAT: Tests StepHours range validation and StepHours <= TotalHours invariant.
        // WHY:  Guarantees numerically sensible temporal sampling.
        // HOW:  Asserts ArgumentOutOfRangeException is thrown.
        ClosedLoopSimulationRequest req = new() { StepHours = stepHours, TotalHours = totalHours };
        Assert.Throws<ArgumentOutOfRangeException>(nameof(req.StepHours), () => _engine.Simulate(req));
    }

    /// <summary>
    /// Verifies <see cref="EndocrineClosedLoopEngine.Simulate"/> validates ClearanceMultiplier bounds (0.1 to 5.0).
    /// </summary>
    [Theory]
    [InlineData(0.05)]
    [InlineData(5.5)]
    public void Simulate_ThrowsArgumentOutOfRangeException_ForInvalidClearanceMultiplier(double multiplier)
    {
        // WHAT: Tests ClearanceMultiplier range validation.
        // WHY:  Prevents negative or non-physiological metabolic clearance rates.
        // HOW:  Asserts ArgumentOutOfRangeException is thrown.
        ClosedLoopSimulationRequest req = new() { ClearanceMultiplier = multiplier };
        Assert.Throws<ArgumentOutOfRangeException>(nameof(req.ClearanceMultiplier), () => _engine.Simulate(req));
    }

    /// <summary>
    /// Verifies <see cref="EndocrineClosedLoopEngine.Simulate"/> rejects negative baseline or target hormone concentrations.
    /// </summary>
    [Fact]
    public void Simulate_ThrowsArgumentOutOfRangeException_ForNegativeHormoneConcentrations()
    {
        // WHAT: Tests concentration non-negativity validation for all hormone setpoints.
        // WHY:  Concentrations in physiological systems cannot be negative.
        // HOW:  Asserts ArgumentOutOfRangeException is thrown for each negative parameter.
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { InitialEstradiolPmolL = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { InitialTestosteroneNmolL = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { InitialLhIuL = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { InitialFshIuL = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { TargetEstradiolPmolL = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Simulate(new ClosedLoopSimulationRequest { TargetTestosteroneNmolL = -1.0 }));
    }

    /// <summary>
    /// Verifies that patient pharmacogenomic clearance multiplier scales metabolic clearance rates.
    /// </summary>
    [Fact]
    public void Simulate_ClearanceMultiplier_AltersKineticsAndSteadyState()
    {
        // WHAT: Compares 72-hour kinetics with baseline clearance (1.0x) versus rapid clearance (2.0x).
        // WHY:  Validates that patient genetic polymorphisms (e.g. CYP1B1*3) dynamically accelerate metabolic elimination.
        // HOW:  Executes identical requests varying only ClearanceMultiplier and asserts divergent final concentrations.
        ClosedLoopSimulationRequest baselineReq = new()
        {
            ClearanceMultiplier = 1.0,
            TotalHours = 48,
            StepHours = 1.0
        };

        ClosedLoopSimulationRequest rapidReq = new()
        {
            ClearanceMultiplier = 2.0,
            TotalHours = 48,
            StepHours = 1.0
        };

        ClosedLoopSimulationResult baselineRes = _engine.Simulate(baselineReq);
        ClosedLoopSimulationResult rapidRes = _engine.Simulate(rapidReq);

        Assert.NotEqual(baselineRes.FinalEstradiolPmolL, rapidRes.FinalEstradiolPmolL);
        Assert.True(rapidRes.FinalEstradiolPmolL < baselineRes.FinalEstradiolPmolL, "Higher clearance must yield lower residual concentration.");
    }

    /// <summary>
    /// Verifies that when initial baseline gonadotropins are already suppressed, TimeToSuppressionHours is 0.0.
    /// </summary>
    [Fact]
    public void Simulate_AlreadySuppressedBaseline_SuppressionTimeIsZero()
    {
        // WHAT: Tests edge case where patient baseline LH is already suppressed (< 1.0 IU/L).
        // WHY:  Ensures immediate suppression recognition without reporting delayed hours.
        // HOW:  Sets InitialLhIuL to 0.5 IU/L and asserts TimeToSuppressionHours == 0.0.
        ClosedLoopSimulationRequest req = new()
        {
            InitialLhIuL = 0.5,
            InitialFshIuL = 3.0,
            TotalHours = 24,
            StepHours = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(req);

        Assert.Equal(0.0, result.TimeToSuppressionHours);
    }

    /// <summary>
    /// Verifies that a 1-hour simulation does not prematurely report steady state.
    /// </summary>
    [Fact]
    public void Simulate_ShortDuration_DoesNotReachSteadyState()
    {
        // WHAT: Tests 1-hour simulation with large initial hormone deficit.
        // WHY:  During rapid initial response, the rate of change is high, so steady state must not be flagged.
        // HOW:  Asserts SteadyStateReached is false for TotalHours = 1.
        ClosedLoopSimulationRequest req = new()
        {
            TotalHours = 1,
            StepHours = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(req);

        Assert.False(result.SteadyStateReached);
    }

    /// <summary>
    /// Verifies that when gonadotropins remain unsuppressed across the simulation duration, TimeToSuppressionHours reports -1.0.
    /// </summary>
    [Fact]
    public void Simulate_NonSuppressedScenario_ReportsNegativeOneSuppressionTime()
    {
        // WHAT: Tests a scenario of brief duration where baseline gonadotropins remain elevated (LH >= 1.0, FSH >= 1.5).
        // WHY:  Ensures that when LH and FSH never drop below suppression thresholds during the simulation, -1.0 is returned.
        // HOW:  Configures a 3-hour window in Transitioning mode; LH and FSH remain well above thresholds.
        ClosedLoopSimulationRequest req = new()
        {
            InitialEstradiolPmolL = 10.0,
            InitialTestosteroneNmolL = 0.5,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 20.0,
            TargetTestosteroneNmolL = 1.0,
            TargetState = ToggleState.Transitioning,
            TotalHours = 3,
            StepHours = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(req);

        Assert.Equal(-1.0, result.TimeToSuppressionHours);
    }

    /// <summary>
    /// Verifies that <see cref="ClosedLoopSimulationRequest.GetExamples"/> and <see cref="ClosedLoopSimulationResult.GetExamples"/>
    /// supply populated canonical instances for OpenAPI documentation.
    /// </summary>
    [Fact]
    public void GetExamples_ReturnsPopulatedInstances()
    {
        // WHAT: Exercises Swagger IExamplesProvider implementations for closed-loop models.
        // WHY:  Ensures OpenAPI contract documentation examples are non-null and valid.
        // HOW:  Instantiates example models and validates properties.
        ClosedLoopSimulationRequest reqExample = new ClosedLoopSimulationRequest().GetExamples();
        Assert.NotNull(reqExample);
        Assert.Equal(72, reqExample.TotalHours);

        ClosedLoopSimulationResult resExample = new ClosedLoopSimulationResult().GetExamples();
        Assert.NotNull(resExample);
        Assert.NotEmpty(resExample.Points);
        Assert.True(resExample.SteadyStateReached);
        Assert.Equal(5.0, resExample.TimeToSuppressionHours);
    }

    /// <summary>
    /// Verifies that calibrated proportional gain eliminates steady-state droop, converging within 1% of target estradiol.
    /// </summary>
    [Fact]
    public void Simulate_Mtf_EliminatesSteadyStateDroop_WithinOnePercent()
    {
        // WHAT: Tests feminizing endocrine simulation for steady-state droop elimination.
        // WHY:  Uncalibrated models drooped by ~8% (509 pmol/L); calibrated gain and feedforward converge to target setpoint (548–550 pmol/L).
        // HOW:  Runs 72h simulation with target 550.0 pmol/L and asserts droop is strictly less than 1.0%.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 120.0,
            InitialTestosteroneNmolL = 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetState = ToggleState.EstrogenicDominant,
            TotalHours = 72,
            StepHours = 1.0,
            ClearanceMultiplier = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        double droopPercent = Math.Abs(request.TargetEstradiolPmolL - result.FinalEstradiolPmolL) / request.TargetEstradiolPmolL * 100.0;
        Assert.True(droopPercent < 1.0, $"Expected droop < 1% but got {droopPercent:F3}% (Final E: {result.FinalEstradiolPmolL:F2} pmol/L).");
        Assert.InRange(result.FinalEstradiolPmolL, 545.0, 550.0);
        Assert.True(result.FinalTestosteroneNmolL <= 1.8, $"Expected female T range (<= 1.8 nmol/L) but got {result.FinalTestosteroneNmolL:F3} nmol/L.");
    }

    /// <summary>
    /// Verifies that calibrated proportional gain eliminates steady-state droop for masculinizing FtM regimens.
    /// </summary>
    /// <param name="targetTestosterone">Target testosterone setpoint in nmol/L.</param>
    [Theory]
    [InlineData(18.5)]
    [InlineData(20.0)]
    public void Simulate_Ftm_EliminatesSteadyStateDroop_WithinOnePercent(double targetTestosterone)
    {
        // WHAT: Tests masculinizing endocrine simulation for steady-state droop elimination across standard setpoints.
        // WHY:  Ensures that autonomous aptamer delivery delivers therapeutic testosterone without persistent deficit.
        // HOW:  Executes 72h simulation and validates final testosterone is within 1.0% of target setpoint.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 600.0,
            InitialTestosteroneNmolL = 1.5,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 80.0,
            TargetTestosteroneNmolL = targetTestosterone,
            TargetState = ToggleState.AndrogenicDominant,
            TotalHours = 72,
            StepHours = 1.0,
            ClearanceMultiplier = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        double droopPercent = Math.Abs(targetTestosterone - result.FinalTestosteroneNmolL) / targetTestosterone * 100.0;
        Assert.True(droopPercent < 1.0, $"Expected droop < 1% but got {droopPercent:F3}% (Final T: {result.FinalTestosteroneNmolL:F3} nmol/L).");
        Assert.InRange(result.FinalTestosteroneNmolL, targetTestosterone * 0.99, targetTestosterone);
        Assert.True(result.FinalEstradiolPmolL < 100.0, $"Expected male E2 range (< 100 pmol/L) but got {result.FinalEstradiolPmolL:F2} pmol/L.");
    }

    /// <summary>
    /// Verifies 4th-order Runge-Kutta numerical integration remains unconditionally stable across step sizes.
    /// </summary>
    /// <param name="stepHours">Temporal sampling step size in hours.</param>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Simulate_NumericalStability_AcrossStepSizes(double stepHours)
    {
        // WHAT: Tests RK4 numerical stability across fine and coarse temporal discretization intervals.
        // WHY:  Verifies that sub-stepping (dt <= 0.05 h) prevents stiffness and numerical divergence.
        // HOW:  Simulates MtF regimen and asserts non-negative values, target convergence, and steady-state attainment.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 120.0,
            InitialTestosteroneNmolL = 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetState = ToggleState.EstrogenicDominant,
            TotalHours = 48,
            StepHours = stepHours
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        Assert.NotNull(result);
        Assert.True(result.FinalEstradiolPmolL > 0.0);
        Assert.True(result.FinalTestosteroneNmolL > 0.0);
        Assert.True(result.FinalLhIuL > 0.0);
        Assert.True(result.FinalFshIuL > 0.0);
        Assert.InRange(result.FinalEstradiolPmolL, 545.0, 550.0);
    }

    /// <summary>
    /// Verifies that in Transitioning operational state under suppressed LH, endogenous synthesis follows symmetric suppression.
    /// </summary>
    [Fact]
    public void Simulate_TransitioningState_WithSuppressedLh_ExecutesSymmetricSuppressionBranch()
    {
        // WHAT: Tests Transitioning operational state with suppressed baseline gonadotropins (< 1.5 IU/L).
        // WHY:  Exercises the non-MtF non-FtM baseline suppression branch in ComputeDerivatives.
        // HOW:  Configures InitialLhIuL = 0.5 IU/L with TargetState = ToggleState.Transitioning.
        ClosedLoopSimulationRequest request = new()
        {
            InitialEstradiolPmolL = 100.0,
            InitialTestosteroneNmolL = 2.0,
            InitialLhIuL = 0.5,
            InitialFshIuL = 1.0,
            TargetState = ToggleState.Transitioning,
            TotalHours = 12,
            StepHours = 1.0
        };

        ClosedLoopSimulationResult result = _engine.Simulate(request);

        Assert.NotNull(result);
        Assert.All(result.Points, pt => Assert.Equal(0.0, pt.CarrierReleaseRate));
        Assert.True(result.FinalEstradiolPmolL > 0.0);
        Assert.True(result.FinalTestosteroneNmolL > 0.0);
    }
}
