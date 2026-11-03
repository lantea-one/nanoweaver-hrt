namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Computational implementation of dynamic closed-loop endocrine feedback and pituitary gonadotropin suppression.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This engine solves a system of four coupled differential equations
/// modeling the human Hypothalamic-Pituitary-Gonadal (HPG) axis. It predicts how circulating 17β-estradiol and
/// testosterone suppress pituitary gonadotropins (LH and FSH) via negative feedback, while autonomous aptamer-gated
/// nanocarriers dynamically modulate payload release to achieve and sustain target hormone setpoints.
/// </para>
/// </remarks>
public sealed class EndocrineClosedLoopEngine : IEndocrineClosedLoopEngine
{
    /// <summary>
    /// Maximum pituitary LH secretion velocity in IU/(L·h).
    /// </summary>
    private const double VmaxLh = 2.2;

    /// <summary>
    /// Inhibitory constant for 17β-estradiol negative feedback on pituitary LH release in pmol/L.
    /// </summary>
    private const double KiELh = 180.0;

    /// <summary>
    /// Inhibitory constant for testosterone negative feedback on pituitary LH release in nmol/L.
    /// </summary>
    private const double KiTLh = 3.5;

    /// <summary>
    /// Hill coefficient n1 for estradiol feedback inhibition on LH secretion.
    /// </summary>
    private const double N1 = 2.0;

    /// <summary>
    /// Hill coefficient n2 for testosterone feedback inhibition on LH secretion.
    /// </summary>
    private const double N2 = 2.0;

    /// <summary>
    /// First-order linear systemic clearance rate for luteinizing hormone in h⁻¹.
    /// </summary>
    private const double KClearLh = 0.35;

    /// <summary>
    /// Maximum pituitary FSH secretion velocity in IU/(L·h).
    /// </summary>
    private const double VmaxFsh = 1.6;

    /// <summary>
    /// Inhibitory constant for 17β-estradiol negative feedback on pituitary FSH release in pmol/L.
    /// </summary>
    private const double KiEFsh = 220.0;

    /// <summary>
    /// Inhibitory constant for testosterone negative feedback on pituitary FSH release in nmol/L.
    /// </summary>
    private const double KiTFsh = 5.0;

    /// <summary>
    /// Hill coefficient m1 for estradiol feedback inhibition on FSH secretion.
    /// </summary>
    private const double M1 = 1.5;

    /// <summary>
    /// Hill coefficient m2 for testosterone feedback inhibition on FSH secretion.
    /// </summary>
    private const double M2 = 1.5;

    /// <summary>
    /// First-order linear systemic clearance rate for follicle-stimulating hormone in h⁻¹.
    /// </summary>
    private const double KClearFsh = 0.28;

    /// <summary>
    /// Endogenous gonadal 17β-estradiol production rate constant stimulated by LH in pmol/(IU·h).
    /// </summary>
    private const double KEndoE = 12.0;

    /// <summary>
    /// Endogenous gonadal testosterone production rate constant stimulated by LH in nmol/(IU·h).
    /// </summary>
    private const double KEndoT = 1.8;

    /// <summary>
    /// Baseline systemic first-order clearance rate for 17β-estradiol in h⁻¹.
    /// </summary>
    private const double BaseKClearE = 0.08;

    /// <summary>
    /// Baseline systemic first-order clearance rate for testosterone in h⁻¹.
    /// </summary>
    private const double BaseKClearT = 0.09;

    /// <summary>
    /// Reference physiological LH baseline in IU/L used for aptamer feedback normalization.
    /// </summary>
    private const double ReferenceLhBaseline = 6.5;

    /// <summary>
    /// Maximum internal numerical integration sub-step in hours to ensure RK4 stability.
    /// </summary>
    private const double MaxInternalDt = 0.05;

    /// <inheritdoc/>
    public ClosedLoopSimulationResult Simulate(ClosedLoopSimulationRequest request)
    {
        // WHAT: Validates simulation input boundaries.
        // WHY:  Protects against nonsensical biological ranges, negative concentrations, and numerical divergences.
        // HOW:  Throws typed ArgumentNullException and ArgumentOutOfRangeException.
        ArgumentNullException.ThrowIfNull(request);

        if (request.TotalHours < 1 || request.TotalHours > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TotalHours), "TotalHours must be between 1 and 720.");
        }

        if (request.StepHours < 0.1 || request.StepHours > 24.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StepHours), "StepHours must be between 0.1 and 24.0.");
        }

        if (request.StepHours > request.TotalHours)
        {
            throw new ArgumentOutOfRangeException(nameof(request.StepHours), "StepHours cannot exceed TotalHours.");
        }

        if (request.ClearanceMultiplier < 0.1 || request.ClearanceMultiplier > 5.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ClearanceMultiplier), "ClearanceMultiplier must be between 0.1 and 5.0.");
        }

        if (request.InitialEstradiolPmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InitialEstradiolPmolL), "Initial estradiol cannot be negative.");
        }

        if (request.InitialTestosteroneNmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InitialTestosteroneNmolL), "Initial testosterone cannot be negative.");
        }

        if (request.InitialLhIuL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InitialLhIuL), "Initial LH cannot be negative.");
        }

        if (request.InitialFshIuL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.InitialFshIuL), "Initial FSH cannot be negative.");
        }

        if (request.TargetEstradiolPmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TargetEstradiolPmolL), "Target estradiol cannot be negative.");
        }

        if (request.TargetTestosteroneNmolL < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TargetTestosteroneNmolL), "Target testosterone cannot be negative.");
        }

        // WHAT: Scales clearance rates by patient pharmacogenomic clearance multiplier.
        // WHY:  Rapid metabolizers (e.g. CYP1B1*3) clear faster, whereas poor metabolizers clear slower.
        // HOW:  Multiplies baseline rate constants by ClearanceMultiplier.
        double kClearE = BaseKClearE * request.ClearanceMultiplier;
        double kClearT = BaseKClearT * request.ClearanceMultiplier;

        // Pre-allocate trajectory point list to avoid dynamic re-allocations in hot loop
        int estimatedPoints = (int)Math.Ceiling(request.TotalHours / request.StepHours) + 2;
        List<ClosedLoopPoint> points = new(estimatedPoints);

        // State variables initialized to baseline
        double currentLh = request.InitialLhIuL;
        double currentFsh = request.InitialFshIuL;
        double currentE = request.InitialEstradiolPmolL;
        double currentT = request.InitialTestosteroneNmolL;

        double timeToSuppression = -1.0;

        // Record initial sample at t = 0
        ComputeDerivatives(currentLh, currentFsh, currentE, currentT, request, kClearE, kClearT, out _, out _, out _, out _, out double initialCarrierRate);
        points.Add(new ClosedLoopPoint
        {
            Hour = 0.0,
            EstradiolPmolL = currentE,
            TestosteroneNmolL = currentT,
            LhIuL = currentLh,
            FshIuL = currentFsh,
            CarrierReleaseRate = initialCarrierRate
        });

        if (currentLh < 1.0 || currentFsh < 1.5)
        {
            timeToSuppression = 0.0;
        }

        // WHAT: 4th-order Runge-Kutta (RK4) numerical integration over the requested duration.
        // WHY:  Provides high-order numerical stability and accurate coupled ODE trajectory solving.
        // HOW:  Advances time step-by-step with internal sub-stepping (dt <= 0.05 h) to eliminate stiffness issues.
        double currentHour = 0.0;
        while (currentHour < request.TotalHours - 1e-9)
        {
            double stepSpan = Math.Min(request.StepHours, request.TotalHours - currentHour);
            double elapsedInSpan = 0.0;

            while (elapsedInSpan < stepSpan - 1e-9)
            {
                double subDt = Math.Min(MaxInternalDt, stepSpan - elapsedInSpan);

                // Stage 1 (k1)
                ComputeDerivatives(currentLh, currentFsh, currentE, currentT, request, kClearE, kClearT,
                    out double k1Lh, out double k1Fsh, out double k1E, out double k1T, out _);

                // Stage 2 (k2 at half step)
                double lh2 = Math.Max(0.0, currentLh + 0.5 * subDt * k1Lh);
                double fsh2 = Math.Max(0.0, currentFsh + 0.5 * subDt * k1Fsh);
                double e2 = Math.Max(0.0, currentE + 0.5 * subDt * k1E);
                double t2 = Math.Max(0.0, currentT + 0.5 * subDt * k1T);
                ComputeDerivatives(lh2, fsh2, e2, t2, request, kClearE, kClearT,
                    out double k2Lh, out double k2Fsh, out double k2E, out double k2T, out _);

                // Stage 3 (k3 at half step)
                double lh3 = Math.Max(0.0, currentLh + 0.5 * subDt * k2Lh);
                double fsh3 = Math.Max(0.0, currentFsh + 0.5 * subDt * k2Fsh);
                double e3 = Math.Max(0.0, currentE + 0.5 * subDt * k2E);
                double t3 = Math.Max(0.0, currentT + 0.5 * subDt * k2T);
                ComputeDerivatives(lh3, fsh3, e3, t3, request, kClearE, kClearT,
                    out double k3Lh, out double k3Fsh, out double k3E, out double k3T, out _);

                // Stage 4 (k4 at full step)
                double lh4 = Math.Max(0.0, currentLh + subDt * k3Lh);
                double fsh4 = Math.Max(0.0, currentFsh + subDt * k3Fsh);
                double e4 = Math.Max(0.0, currentE + subDt * k3E);
                double t4 = Math.Max(0.0, currentT + subDt * k3T);
                ComputeDerivatives(lh4, fsh4, e4, t4, request, kClearE, kClearT,
                    out double k4Lh, out double k4Fsh, out double k4E, out double k4T, out _);

                // Weighted combination
                currentLh = Math.Max(0.0, currentLh + (subDt / 6.0) * (k1Lh + 2.0 * k2Lh + 2.0 * k3Lh + k4Lh));
                currentFsh = Math.Max(0.0, currentFsh + (subDt / 6.0) * (k1Fsh + 2.0 * k2Fsh + 2.0 * k3Fsh + k4Fsh));
                currentE = Math.Max(0.0, currentE + (subDt / 6.0) * (k1E + 2.0 * k2E + 2.0 * k3E + k4E));
                currentT = Math.Max(0.0, currentT + (subDt / 6.0) * (k1T + 2.0 * k2T + 2.0 * k3T + k4T));

                elapsedInSpan += subDt;
            }

            currentHour = Math.Round(currentHour + stepSpan, 6);

            ComputeDerivatives(currentLh, currentFsh, currentE, currentT, request, kClearE, kClearT, out _, out _, out _, out _, out double carrierRate);

            points.Add(new ClosedLoopPoint
            {
                Hour = currentHour,
                EstradiolPmolL = currentE,
                TestosteroneNmolL = currentT,
                LhIuL = currentLh,
                FshIuL = currentFsh,
                CarrierReleaseRate = carrierRate
            });

            if (timeToSuppression < 0.0 && (currentLh < 1.0 || currentFsh < 1.5))
            {
                timeToSuppression = currentHour;
            }
        }

        // WHAT: Evaluates whether the system has reached homeostatic steady state.
        // WHY:  Provides a clinical stability verdict indicating complete stabilization.
        // HOW:  Checks that the rate of change of primary variables across the final sample interval is below threshold.
        bool steadyStateReached = false;
        if (points.Count >= 2)
        {
            double deltaE = Math.Abs(points[^1].EstradiolPmolL - points[^2].EstradiolPmolL);
            double deltaT = Math.Abs(points[^1].TestosteroneNmolL - points[^2].TestosteroneNmolL);
            double deltaLh = Math.Abs(points[^1].LhIuL - points[^2].LhIuL);
            double rateE = deltaE / request.StepHours;
            double rateT = deltaT / request.StepHours;
            double rateLh = deltaLh / request.StepHours;

            steadyStateReached = rateE < 0.5 && rateT < 0.05 && rateLh < 0.05;
        }

        return new ClosedLoopSimulationResult
        {
            Points = points,
            FinalEstradiolPmolL = currentE,
            FinalTestosteroneNmolL = currentT,
            FinalLhIuL = currentLh,
            FinalFshIuL = currentFsh,
            SteadyStateReached = steadyStateReached,
            TimeToSuppressionHours = timeToSuppression
        };
    }

    /// <summary>
    /// Computes instantaneous derivatives for the coupled HPG axis ODE system.
    /// </summary>
    /// <param name="lh">Current circulating luteinizing hormone concentration in IU/L.</param>
    /// <param name="fsh">Current circulating follicle-stimulating hormone concentration in IU/L.</param>
    /// <param name="e">Current circulating 17β-estradiol concentration in pmol/L.</param>
    /// <param name="t">Current circulating total testosterone concentration in nmol/L.</param>
    /// <param name="request">The active simulation request parameters.</param>
    /// <param name="kClearE">Active clearance rate for 17β-estradiol in h⁻¹.</param>
    /// <param name="kClearT">Active clearance rate for testosterone in h⁻¹.</param>
    /// <param name="dlh">Output rate of change of LH in IU/(L·h).</param>
    /// <param name="dfsh">Output rate of change of FSH in IU/(L·h).</param>
    /// <param name="de">Output rate of change of estradiol in pmol/(L·h).</param>
    /// <param name="dt">Output rate of change of testosterone in nmol/(L·h).</param>
    /// <param name="carrierRate">Output instantaneous aptamer-gated nanocarrier release rate.</param>
    private static void ComputeDerivatives(
        double lh,
        double fsh,
        double e,
        double t,
        ClosedLoopSimulationRequest request,
        double kClearE,
        double kClearT,
        out double dlh,
        out double dfsh,
        out double de,
        out double dt,
        out double carrierRate)
    {
        // WHAT: Non-linear Hill-type reciprocal negative feedback on pituitary gonadotropins.
        // WHY:  Elevated circulating estrogens and androgens suppress pituitary LH and FSH synthesis.
        // HOW:  Evaluates saturation denominators with Hill exponents n = 2 and m = 1.5.
        double safeLh = Math.Max(0.0, lh);
        double safeFsh = Math.Max(0.0, fsh);
        double safeE = Math.Max(0.0, e);
        double safeT = Math.Max(0.0, t);

        double eOverKiLh = safeE / KiELh;
        double tOverKiLh = safeT / KiTLh;
        double denomLh = 1.0 + Math.Pow(eOverKiLh, N1) + Math.Pow(tOverKiLh, N2);
        dlh = (VmaxLh / denomLh) - (KClearLh * safeLh);

        double eOverKiFsh = safeE / KiEFsh;
        double tOverKiFsh = safeT / KiTFsh;
        double denomFsh = 1.0 + Math.Pow(eOverKiFsh, M1) + Math.Pow(tOverKiFsh, M2);
        dfsh = (VmaxFsh / denomFsh) - (KClearFsh * safeFsh);

        // WHAT: Aptamer-gated nanocarrier payload release rate with gonadotropin feedback modulation.
        // WHY:  When circulating hormone is below target, aptamer latches open in response to elevated LH,
        //       releasing payload until homeostasis is re-established.
        // HOW:  Deficit multiplied by (1.0 + 0.3 * (LH / 6.5)).
        double lhModulation = 1.0 + (0.3 * (safeLh / ReferenceLhBaseline));
        double rCarrierE = 0.0;
        double rCarrierT = 0.0;

        if (request.TargetState == ToggleState.EstrogenicDominant)
        {
            double deficitE = Math.Max(0.0, request.TargetEstradiolPmolL - safeE);
            rCarrierE = deficitE * lhModulation;
            carrierRate = rCarrierE;
        }
        else if (request.TargetState == ToggleState.AndrogenicDominant)
        {
            double deficitT = Math.Max(0.0, request.TargetTestosteroneNmolL - safeT);
            rCarrierT = deficitT * lhModulation;
            carrierRate = rCarrierT;
        }
        else
        {
            carrierRate = 0.0;
        }

        // Coupled hormone dynamics: endogenous LH stimulation + nanocarrier release - metabolic clearance
        de = (KEndoE * safeLh) + rCarrierE - (kClearE * safeE);
        dt = (KEndoT * safeLh) + rCarrierT - (kClearT * safeT);
    }
}
