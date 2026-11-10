namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Computational implementation of 180-day semi-annual time-release depot kinetics coupled with HPG axis feedback.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This engine simulates how long-acting subcutaneous nanocarrier formulations
/// elute therapeutic hormones across a six-month (180-day) horizon. It enforces three distinct pharmacokinetic phases:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Phase I (Days 1–7):</strong> Burst suppression holding cumulative elution strictly &lt;= 5.0%.</description></item>
/// <item><description><strong>Phase II (Days 8–150):</strong> Zero-order steady-state plateau delivering constant micro-aliquots.</description></item>
/// <item><description><strong>Phase III (Days 151–180):</strong> First-order terminal taper down to ~5% reserve with re-dosing alert.</description></item>
/// </list>
/// <para>
/// Throughout the simulation, reciprocal negative feedback across the Hypothalamic-Pituitary-Gonadal (HPG) axis is modeled
/// to evaluate pituitary gonadotropin suppression (LH &lt; 1.0 IU/L, FSH &lt; 1.5 IU/L) and time in target range (TTR &gt;= 95.0%).
/// </para>
/// </remarks>
public sealed class TimeReleaseDepotService : ITimeReleaseDepotService
{
    /// <summary>
    /// Nominal daily payload fractional elution rate during the zero-order steady-state plateau.
    /// </summary>
    private const double BasePlateauFractionPerDay = 0.0055;

    /// <summary>
    /// Duration of Phase I burst suppression in days.
    /// </summary>
    private const int BurstSuppressionDays = 7;

    /// <summary>
    /// Duration of Phase II steady-state plateau horizon in days.
    /// </summary>
    private const int SteadyStatePlateauDays = 150;

    /// <summary>
    /// First-order exponential decay constant lambda in day⁻¹ for Phase III terminal reserve tapering.
    /// </summary>
    private const double TerminalTaperDecayRate = 0.012;

    /// <summary>
    /// Molecular weight of 17β-estradiol in grams per mole (g/mol).
    /// </summary>
    private const double EstradiolMolecularWeight = 272.38;

    /// <summary>
    /// Molecular weight of testosterone in grams per mole (g/mol).
    /// </summary>
    private const double TestosteroneMolecularWeight = 288.42;

    /// <summary>
    /// Reference physiological systemic clearance for 17β-estradiol in liters per day (L/day).
    /// </summary>
    private const double EstradiolDailyClearanceL = 500.0;

    /// <summary>
    /// Reference physiological systemic clearance for testosterone in liters per day (L/day).
    /// </summary>
    private const double TestosteroneDailyClearanceL = 850.0;

    /// <summary>
    /// Inhibitory dissociation constant for 17β-estradiol negative feedback on pituitary LH secretion in pmol/L.
    /// </summary>
    private const double KiEstradiolLh = 180.0;

    /// <summary>
    /// Inhibitory dissociation constant for testosterone negative feedback on pituitary LH secretion in nmol/L.
    /// </summary>
    private const double KiTestosteroneLh = 3.5;

    /// <summary>
    /// Inhibitory dissociation constant for 17β-estradiol negative feedback on pituitary FSH secretion in pmol/L.
    /// </summary>
    private const double KiEstradiolFsh = 220.0;

    /// <summary>
    /// Inhibitory dissociation constant for testosterone negative feedback on pituitary FSH secretion in nmol/L.
    /// </summary>
    private const double KiTestosteroneFsh = 5.0;

    /// <summary>
    /// Hill coefficient n1 for estradiol-mediated LH negative feedback inhibition.
    /// </summary>
    private const double HillN1 = 2.0;

    /// <summary>
    /// Hill coefficient n2 for testosterone-mediated LH negative feedback inhibition.
    /// </summary>
    private const double HillN2 = 2.0;

    /// <summary>
    /// Hill coefficient m1 for estradiol-mediated FSH negative feedback inhibition.
    /// </summary>
    private const double HillM1 = 1.5;

    /// <summary>
    /// Hill coefficient m2 for testosterone-mediated FSH negative feedback inhibition.
    /// </summary>
    private const double HillM2 = 1.5;

    /// <summary>
    /// First-order daily pharmacokinetic smoothing rate alpha for circulating sex steroids.
    /// </summary>
    private const double HormoneSmoothingAlpha = 0.65;

    /// <summary>
    /// First-order daily pharmacokinetic smoothing rate beta for circulating pituitary gonadotropins.
    /// </summary>
    private const double GonadotropinSmoothingBeta = 0.50;

    /// <summary>
    /// Minimum therapeutic target serum 17β-estradiol concentration for feminizing HRT in pmol/L.
    /// </summary>
    private const double MtfEstradiolTargetMinPmolL = 400.0;

    /// <summary>
    /// Maximum therapeutic target serum 17β-estradiol concentration for feminizing HRT in pmol/L.
    /// </summary>
    private const double MtfEstradiolTargetMaxPmolL = 700.0;

    /// <summary>
    /// Minimum therapeutic target serum total testosterone concentration for masculinizing HRT in nmol/L.
    /// </summary>
    private const double FtmTestosteroneTargetMinNmolL = 15.0;

    /// <summary>
    /// Maximum therapeutic target serum total testosterone concentration for masculinizing HRT in nmol/L.
    /// </summary>
    private const double FtmTestosteroneTargetMaxNmolL = 30.0;

    /// <summary>
    /// Phase I descriptive label.
    /// </summary>
    private const string Phase1Label = "Phase I: Burst Suppression";

    /// <summary>
    /// Phase II descriptive label.
    /// </summary>
    private const string Phase2Label = "Phase II: Steady-State Plateau";

    /// <summary>
    /// Phase III descriptive label.
    /// </summary>
    private const string Phase3Label = "Phase III: Terminal Taper";

    /// <inheritdoc/>
    public TimeReleaseSimulationResult Simulate180DayDepot(TimeReleaseDepotParameters parameters)
    {
        // WHAT: Validates depot simulation request boundaries and pharmacogenomic parameters.
        // WHY:  Eliminates numerical instability, non-physical negative concentrations, or out-of-range horizons.
        // HOW:  Invokes ValidateParameters helper throwing typed ArgumentNullException and ArgumentOutOfRangeException.
        ValidateParameters(parameters);

        int horizon = parameters.TargetHorizonDays;

        // WHAT: Pre-allocates trajectory point list with exact capacity.
        // WHY:  Upholds zero-allocation policy by eliminating dynamic array resizing in hot loops.
        // HOW:  Initializes List with capacity horizon + 1.
        List<TimeReleaseDailyPoint> points = new(horizon + 1);

        double cumulativeReleasedMg = 0.0;
        double currentEstradiol = parameters.InitialEstradiolPmolL;
        double currentTestosterone = parameters.InitialTestosteroneNmolL;
        double currentLh = parameters.InitialLhIuL;
        double currentFsh = parameters.InitialFshIuL;

        int daysInTarget = 0;
        double sumPhase2DailyRelease = 0.0;
        int countPhase2Days = 0;
        double sumPhase2Lh = 0.0;
        double sumPhase2Fsh = 0.0;
        int reDosingAlertDay = -1;
        double day7CumulativeFraction = 0.0;

        // WHAT: Constructs baseline Day 0 point.
        // WHY:  Provides initial clinical state prior to active payload elution.
        // HOW:  Evaluates target range compliance and appends point 0.
        bool initialInTarget = IsInTargetRange(parameters.Regimen, currentEstradiol, currentTestosterone);
        points.Add(new TimeReleaseDailyPoint
        {
            Day = 0,
            CumulativeFraction = 0.0,
            DailyReleaseMg = 0.0,
            RemainingReserveMg = parameters.InitialPayloadMassMg,
            EstradiolPmolL = currentEstradiol,
            TestosteroneNmolL = currentTestosterone,
            LhIuL = currentLh,
            FshIuL = currentFsh,
            InTargetRange = initialInTarget,
            Phase = Phase1Label,
            ReDosingAlert = false
        });

        // WHAT: Executes discrete daily time-stepping integration across the depot horizon.
        // WHY:  Computes drug elution, updates remaining reserve, adjusts circulating hormones, and tracks HPG feedback.
        // HOW:  Deterministic zero-allocation loop without LINQ or dynamic allocations.
        for (int d = 1; d <= horizon; d++)
        {
            double dailyReleaseMg = CalculateDailyRelease(d, parameters);

            // WHAT: Guards against over-releasing beyond the remaining depot reserve.
            // WHY:  Conserves total mass balance inside the physical polymer depot.
            // HOW:  Caps daily release to remaining reserve mass.
            double remainingBefore = Math.Max(0.0, parameters.InitialPayloadMassMg - cumulativeReleasedMg);
            if (dailyReleaseMg > remainingBefore)
            {
                dailyReleaseMg = remainingBefore;
            }

            cumulativeReleasedMg += dailyReleaseMg;
            double cumulativeFraction = Math.Min(1.0, cumulativeReleasedMg / parameters.InitialPayloadMassMg);
            double remainingReserveMg = Math.Max(0.0, parameters.InitialPayloadMassMg - cumulativeReleasedMg);
            double reserveFraction = remainingReserveMg / parameters.InitialPayloadMassMg;

            if (d == BurstSuppressionDays)
            {
                day7CumulativeFraction = cumulativeFraction;
            }

            string phase;
            if (d <= BurstSuppressionDays)
            {
                phase = Phase1Label;
            }
            else if (d <= SteadyStatePlateauDays)
            {
                phase = Phase2Label;
                sumPhase2DailyRelease += dailyReleaseMg;
                countPhase2Days++;
            }
            else
            {
                phase = Phase3Label;
            }

            // WHAT: Triggers clinical re-dosing alert when entering Phase III terminal taper or reserve drops <= 5%.
            // WHY:  Alerts clinicians and patient that depot payload is near exhaustion and requires scheduled replacement.
            // HOW:  Sets reDosingAlert and records first day triggered.
            bool reDosingAlert = d > SteadyStatePlateauDays || reserveFraction <= 0.05;
            if (reDosingAlert && reDosingAlertDay == -1)
            {
                reDosingAlertDay = d;
            }

            // WHAT: Updates circulating steroid hormone concentrations based on daily release and systemic clearance.
            // WHY:  Couples depot delivery to clinical target levels (400-700 pmol/L E2 for MtF, 15-30 nmol/L T for FtM).
            // HOW:  Converts daily mass to molar amount, calculates steady-state target, and smooths via first-order kinetics.
            UpdateCirculatingHormones(
                parameters,
                dailyReleaseMg,
                currentLh,
                ref currentEstradiol,
                ref currentTestosterone);

            // WHAT: Computes coupled HPG axis negative feedback on pituitary LH and FSH secretion.
            // WHY:  Elevated sex steroids inhibit hypothalamic GnRH pulses and pituitary gonadotropes.
            // HOW:  Applies Hill equations with cooperative inhibitory constants and smooths via first-order kinetics.
            UpdateGonadotropins(
                parameters,
                currentEstradiol,
                currentTestosterone,
                ref currentLh,
                ref currentFsh);

            if (d > BurstSuppressionDays && d <= SteadyStatePlateauDays)
            {
                sumPhase2Lh += currentLh;
                sumPhase2Fsh += currentFsh;
            }

            bool inTarget = IsInTargetRange(parameters.Regimen, currentEstradiol, currentTestosterone);
            if (inTarget)
            {
                daysInTarget++;
            }

            points.Add(new TimeReleaseDailyPoint
            {
                Day = d,
                CumulativeFraction = cumulativeFraction,
                DailyReleaseMg = dailyReleaseMg,
                RemainingReserveMg = remainingReserveMg,
                EstradiolPmolL = currentEstradiol,
                TestosteroneNmolL = currentTestosterone,
                LhIuL = currentLh,
                FshIuL = currentFsh,
                InTargetRange = inTarget,
                Phase = phase,
                ReDosingAlert = reDosingAlert
            });
        }

        // If simulation horizon was shorter than Day 7, use cumulative fraction at horizon
        if (horizon < BurstSuppressionDays)
        {
            day7CumulativeFraction = points[^1].CumulativeFraction;
        }

        double avgPhase2Release = countPhase2Days > 0 ? (sumPhase2DailyRelease / countPhase2Days) : 0.0;
        double timeInTargetPercent = (daysInTarget / (double)horizon) * 100.0;
        double terminalReserveFraction = points[^1].RemainingReserveMg / parameters.InitialPayloadMassMg;
        bool burstSuppressed = day7CumulativeFraction <= 0.05;
        bool lhSuppressionAchieved = countPhase2Days > 0 && (sumPhase2Lh / countPhase2Days) < 1.0;
        bool fshSuppressionAchieved = countPhase2Days > 0 && (sumPhase2Fsh / countPhase2Days) < 1.5;

        return new TimeReleaseSimulationResult
        {
            Points = points,
            Day7BurstFraction = day7CumulativeFraction,
            BurstSuppressed = burstSuppressed,
            AverageDailyReleaseMg = avgPhase2Release,
            TimeInTargetRangePercent = timeInTargetPercent,
            TerminalReserveFraction = terminalReserveFraction,
            ReDosingAlertDay = reDosingAlertDay,
            LhSuppressionAchieved = lhSuppressionAchieved,
            FshSuppressionAchieved = fshSuppressionAchieved,
            FinalEstradiolPmolL = points[^1].EstradiolPmolL,
            FinalTestosteroneNmolL = points[^1].TestosteroneNmolL,
            FinalLhIuL = points[^1].LhIuL,
            FinalFshIuL = points[^1].FshIuL
        };
    }

    /// <inheritdoc/>
    public double CalculateInstantaneousDailyRelease(double day, PolymerMatrixType matrix, double initialMassMg, double clearanceMultiplier)
    {
        // WHAT: Validates mass and pharmacogenomic clearance multiplier.
        // WHY:  Non-positive mass cannot elute drug; clearance multiplier must fall within physiological genomic bounds.
        // HOW:  Throws ArgumentOutOfRangeException if out of range.
        if (initialMassMg <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialMassMg), "Initial payload mass must be greater than zero.");
        }

        if (clearanceMultiplier < 0.1 || clearanceMultiplier > 5.0)
        {
            throw new ArgumentOutOfRangeException(nameof(clearanceMultiplier), "ClearanceMultiplier must be between 0.1 and 5.0.");
        }

        if (day <= 0.0)
        {
            return 0.0;
        }

        (double n, double matrixScale) = GetMatrixProperties(matrix);

        // WHAT: Computes pharmacogenomic clearance adaptation factor.
        // WHY:  Aptamer-gated DNA origami nanocarriers adapt micro-release to patient metabolizer phenotype.
        // HOW:  Applies a mild linear genomic clearance scaling centered at 1.0.
        double clearanceFactor = 1.0 + 0.05 * (clearanceMultiplier - 1.0);

        // WHAT: Calculates the daily release rate fraction across the three biophysical phases.
        // WHY:  Enforces Phase I burst suppression (<= 5% on Day 7), Phase II zero-order plateau, and Phase III terminal taper.
        // HOW:  Piecewise biophysical function with continuous phase boundaries.
        double dailyFraction;
        if (day <= BurstSuppressionDays)
        {
            dailyFraction = BasePlateauFractionPerDay * (0.35 + 0.65 * (day / BurstSuppressionDays)) * matrixScale;
        }
        else if (day <= SteadyStatePlateauDays)
        {
            dailyFraction = BasePlateauFractionPerDay * matrixScale * (0.70 + 0.30 * Math.Pow(day / SteadyStatePlateauDays, n - 0.50));
        }
        else
        {
            double deltaDays = day - SteadyStatePlateauDays;
            double plateauFraction = BasePlateauFractionPerDay * matrixScale;
            dailyFraction = plateauFraction * Math.Exp(-TerminalTaperDecayRate * deltaDays);
        }

        return initialMassMg * dailyFraction * clearanceFactor;
    }

    /// <summary>
    /// Calculates the instantaneous daily payload release rate using the specific kinetics model in the simulation parameters.
    /// </summary>
    /// <param name="day">The elapsed simulation time in days.</param>
    /// <param name="parameters">The full simulation request parameters.</param>
    /// <returns>The calculated daily payload release rate in milligrams per day (mg/day).</returns>
    private static double CalculateDailyRelease(double day, TimeReleaseDepotParameters parameters)
    {
        // WHAT: Resolves matrix biophysical constants and kinetics model scaling.
        // WHY:  Different polymer matrices (PLGA, Alginate, Cyclodextrin) and kinetics models modulate daily elution.
        // HOW:  Applies matrix scale, kinetics multiplier, and pharmacogenomic clearance scaling.
        (double n, double matrixScale) = GetMatrixProperties(parameters.MatrixType);
        double clearanceFactor = 1.0 + 0.05 * (parameters.ClearanceMultiplier - 1.0);

        double dailyFraction;
        if (day <= BurstSuppressionDays)
        {
            dailyFraction = BasePlateauFractionPerDay * (0.35 + 0.65 * (day / BurstSuppressionDays)) * matrixScale;
        }
        else if (day <= SteadyStatePlateauDays)
        {
            double modelMultiplier = GetKineticsModelMultiplier(parameters.KineticsModel, day, n);
            dailyFraction = BasePlateauFractionPerDay * matrixScale * modelMultiplier;
        }
        else
        {
            double deltaDays = day - SteadyStatePlateauDays;
            double plateauFraction = BasePlateauFractionPerDay * matrixScale;
            dailyFraction = plateauFraction * Math.Exp(-TerminalTaperDecayRate * deltaDays);
        }

        return parameters.InitialPayloadMassMg * dailyFraction * clearanceFactor;
    }

    /// <summary>
    /// Resolves the diffusion exponent and matrix-specific scaling constant for a given polymer matrix type.
    /// </summary>
    /// <param name="matrix">The polymer matrix carrier type.</param>
    /// <returns>A tuple containing the Korsmeyer-Peppas exponent n and the matrix scale factor.</returns>
    private static (double ExponentN, double MatrixScale) GetMatrixProperties(PolymerMatrixType matrix) => matrix switch
    {
        PolymerMatrixType.PlgaPegPlga => (0.55, 1.00),
        PolymerMatrixType.AlginateChitosan => (0.60, 0.98),
        PolymerMatrixType.CyclodextrinScaffold => (0.50, 1.03),
        PolymerMatrixType.CrosslinkedHydrogel => (0.65, 1.01),
        _ => throw new ArgumentOutOfRangeException(nameof(matrix), "Unsupported polymer matrix type.")
    };

    /// <summary>
    /// Resolves the kinetics model multiplier across Phase II steady-state plateau.
    /// </summary>
    /// <param name="model">The mathematical release kinetics model.</param>
    /// <param name="day">The elapsed simulation time in days.</param>
    /// <param name="n">The polymer matrix diffusion exponent.</param>
    /// <returns>The normalized kinetics model multiplier.</returns>
    private static double GetKineticsModelMultiplier(ReleaseKineticsModel model, double day, double n) => model switch
    {
        ReleaseKineticsModel.KorsmeyerPeppas => 0.70 + 0.30 * Math.Pow(day / SteadyStatePlateauDays, n - 0.50),
        ReleaseKineticsModel.Higuchi => 0.75 + 0.25 * Math.Pow(day / SteadyStatePlateauDays, 0.0),
        ReleaseKineticsModel.CoupledAnomalousFickian => 0.65 + 0.35 * Math.Pow(day / SteadyStatePlateauDays, 0.10),
        ReleaseKineticsModel.ZeroOrderErosion => 1.00,
        _ => throw new ArgumentOutOfRangeException(nameof(model), "Unsupported release kinetics model.")
    };

    /// <summary>
    /// Updates circulating sex steroid concentrations based on delivered payload micro-aliquots and metabolic clearance.
    /// </summary>
    /// <param name="parameters">The depot formulation parameters.</param>
    /// <param name="dailyReleaseMg">Instantaneous payload released today in milligrams.</param>
    /// <param name="currentLh">Current circulating LH concentration in IU/L.</param>
    /// <param name="currentEstradiol">Reference to current circulating 17β-estradiol concentration in pmol/L.</param>
    /// <param name="currentTestosterone">Reference to current circulating total testosterone concentration in nmol/L.</param>
    private static void UpdateCirculatingHormones(
        TimeReleaseDepotParameters parameters,
        double dailyReleaseMg,
        double currentLh,
        ref double currentEstradiol,
        ref double currentTestosterone)
    {
        // WHAT: Modulates circulating sex steroids according to the active therapeutic regimen.
        // WHY:  MtF regimens deliver E2 while clearing testosterone; FtM regimens deliver T while clearing E2.
        // HOW:  Converts daily mg dose to molar amount, calculates steady-state target, and smooths with first-order kinetics.
        switch (parameters.Regimen)
        {
            case TherapeuticRegimen.MaleToFemale:
                {
                    // Estradiol delivery: 1 mg = 3,671,341 pmol E2
                    double pmolDelivered = dailyReleaseMg * (1_000_000_000.0 / EstradiolMolecularWeight);
                    double dailyClearanceL = EstradiolDailyClearanceL * parameters.ClearanceMultiplier;
                    double targetE2 = pmolDelivered / dailyClearanceL;

                    currentEstradiol += HormoneSmoothingAlpha * (targetE2 - currentEstradiol);

                    // Endogenous gonadal testosterone suppression via low LH (< 1.5 IU/L)
                    double tProductionFraction = (currentLh >= 1.5) ? (currentLh / 6.5) : 0.05;
                    double targetT = Math.Min(parameters.TargetTestosteroneNmolL, 1.2) + (tProductionFraction * 0.1);
                    currentTestosterone += HormoneSmoothingAlpha * (targetT - currentTestosterone);
                    break;
                }

            case TherapeuticRegimen.FemaleToMale:
                {
                    // Testosterone delivery: 1 mg = 3,467.17 nmol T
                    double nmolDelivered = dailyReleaseMg * (1_000_000.0 / TestosteroneMolecularWeight);
                    double dailyClearanceL = TestosteroneDailyClearanceL * parameters.ClearanceMultiplier;
                    double targetT = nmolDelivered / dailyClearanceL;

                    currentTestosterone += HormoneSmoothingAlpha * (targetT - currentTestosterone);

                    // Endogenous cyclic estradiol suppression via low LH (< 1.5 IU/L)
                    double eProductionFraction = (currentLh >= 1.5) ? (currentLh / 6.5) : 0.05;
                    double targetE = Math.Min(parameters.TargetEstradiolPmolL, 100.0) + (eProductionFraction * 10.0);
                    currentEstradiol += HormoneSmoothingAlpha * (targetE - currentEstradiol);
                    break;
                }

            case TherapeuticRegimen.Custom:
            default:
                {
                    double targetE2 = parameters.TargetEstradiolPmolL;
                    double targetT = parameters.TargetTestosteroneNmolL;

                    currentEstradiol += HormoneSmoothingAlpha * (targetE2 - currentEstradiol);
                    currentTestosterone += HormoneSmoothingAlpha * (targetT - currentTestosterone);
                    break;
                }
        }
    }

    /// <summary>
    /// Updates circulating pituitary gonadotropin concentrations (LH and FSH) using Hill negative feedback dynamics.
    /// </summary>
    /// <param name="parameters">The depot formulation parameters containing baseline gonadotropins.</param>
    /// <param name="estradiol">Current circulating 17β-estradiol concentration in pmol/L.</param>
    /// <param name="testosterone">Current circulating total testosterone concentration in nmol/L.</param>
    /// <param name="currentLh">Reference to current circulating LH concentration in IU/L.</param>
    /// <param name="currentFsh">Reference to current circulating FSH concentration in IU/L.</param>
    private static void UpdateGonadotropins(
        TimeReleaseDepotParameters parameters,
        double estradiol,
        double testosterone,
        ref double currentLh,
        ref double currentFsh)
    {
        // WHAT: Computes cooperative Hill-equation feedback inhibition for pituitary LH and FSH.
        // WHY:  High sex steroids exert reciprocal negative feedback on the hypothalamus and anterior pituitary.
        // HOW:  Calculates normalized ratios to dissociation constants Ki, evaluates Hill denominator, and smooths daily changes.
        double eRatioLh = estradiol / KiEstradiolLh;
        double tRatioLh = testosterone / KiTestosteroneLh;
        double lhInhibition = 1.0 / (1.0 + Math.Pow(eRatioLh, HillN1) + Math.Pow(tRatioLh, HillN2));
        double targetLh = parameters.InitialLhIuL * lhInhibition;

        double eRatioFsh = estradiol / KiEstradiolFsh;
        double tRatioFsh = testosterone / KiTestosteroneFsh;
        double fshInhibition = 1.0 / (1.0 + Math.Pow(eRatioFsh, HillM1) + Math.Pow(tRatioFsh, HillM2));
        double targetFsh = parameters.InitialFshIuL * fshInhibition;

        currentLh += GonadotropinSmoothingBeta * (targetLh - currentLh);
        currentFsh += GonadotropinSmoothingBeta * (targetFsh - currentFsh);
    }

    /// <summary>
    /// Evaluates whether current circulating hormone levels are within physiological clinical target range for the active regimen.
    /// </summary>
    /// <param name="regimen">The target therapeutic endocrine regimen.</param>
    /// <param name="estradiol">Current 17β-estradiol concentration in pmol/L.</param>
    /// <param name="testosterone">Current total testosterone concentration in nmol/L.</param>
    /// <returns><c>true</c> if within target range; otherwise, <c>false</c>.</returns>
    private static bool IsInTargetRange(TherapeuticRegimen regimen, double estradiol, double testosterone) => regimen switch
    {
        TherapeuticRegimen.MaleToFemale => estradiol >= MtfEstradiolTargetMinPmolL && estradiol <= MtfEstradiolTargetMaxPmolL,
        TherapeuticRegimen.FemaleToMale => testosterone >= FtmTestosteroneTargetMinNmolL && testosterone <= FtmTestosteroneTargetMaxNmolL,
        _ => estradiol >= 200.0 || testosterone >= 10.0
    };

    /// <summary>
    /// Validates the numerical boundaries of all parameters within a depot simulation request.
    /// </summary>
    /// <param name="parameters">The parameters instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="parameters"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if any parameter is outside valid physical or clinical bounds.</exception>
    private static void ValidateParameters(TimeReleaseDepotParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.InitialPayloadMassMg <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.InitialPayloadMassMg), "Initial payload mass must be greater than zero.");
        }

        if (parameters.TargetHorizonDays < 1 || parameters.TargetHorizonDays > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.TargetHorizonDays), "Target horizon days must be between 1 and 365.");
        }

        if (parameters.ClearanceMultiplier < 0.1 || parameters.ClearanceMultiplier > 5.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.ClearanceMultiplier), "ClearanceMultiplier must be between 0.1 and 5.0.");
        }

        if (parameters.InitialEstradiolPmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.InitialEstradiolPmolL), "Initial estradiol cannot be negative.");
        }

        if (parameters.InitialTestosteroneNmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.InitialTestosteroneNmolL), "Initial testosterone cannot be negative.");
        }

        if (parameters.InitialLhIuL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.InitialLhIuL), "Initial LH cannot be negative.");
        }

        if (parameters.InitialFshIuL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.InitialFshIuL), "Initial FSH cannot be negative.");
        }

        if (parameters.TargetEstradiolPmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.TargetEstradiolPmolL), "Target estradiol cannot be negative.");
        }

        if (parameters.TargetTestosteroneNmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.TargetTestosteroneNmolL), "Target testosterone cannot be negative.");
        }
    }
}
