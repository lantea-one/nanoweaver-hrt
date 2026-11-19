using System.Globalization;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Deterministic regularized weighted least-squares (WLS) Bayesian calibration engine and clinical safety
/// guardrail evaluator for longitudinal 180-day subcutaneous hormone depots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Real patients metabolize hormones and degrade polymer hydrogels at
/// slightly different speeds than population averages. This service takes measured blood tests recorded at
/// follow-up visits (such as Day 30, Day 60, and Day 90), searches for the individualized metabolic clearance
/// and daily erosion rates that best match those blood draws, checks clinical safety limits (pituitary LH/FSH
/// escape, FtM hematocrit elevation, MtF supraphysiological estradiol, and sub-therapeutic troughs), and
/// recommends when to re-dose and how much payload to load for the next 180-day depot cycle.
/// </para>
/// </remarks>
public sealed class DepotCalibrationService : IDepotCalibrationService
{
    /// <summary>
    /// Lower bound of the inferred metabolic clearance multiplier search domain.
    /// </summary>
    private const double MinClearanceMultiplier = 0.40;

    /// <summary>
    /// Upper bound of the inferred metabolic clearance multiplier search domain.
    /// </summary>
    private const double MaxClearanceMultiplier = 2.50;

    /// <summary>
    /// Lower bound of the inferred daily polymer erosion rate search domain in percent per day.
    /// </summary>
    private const double MinDailyErosionRatePercent = 0.20;

    /// <summary>
    /// Upper bound of the inferred daily polymer erosion rate search domain in percent per day.
    /// </summary>
    private const double MaxDailyErosionRatePercent = 2.00;

    /// <summary>
    /// Reference normalization scale for serum 17β-estradiol residuals in pmol/L.
    /// </summary>
    private const double EstradiolNormalizationScale = 550.0;

    /// <summary>
    /// Reference normalization scale for serum total testosterone residuals in nmol/L.
    /// </summary>
    private const double TestosteroneNormalizationScale = 20.0;

    /// <summary>
    /// Reference normalization scale for circulating luteinizing hormone residuals in IU/L.
    /// </summary>
    private const double LhNormalizationScale = 5.0;

    /// <summary>
    /// Number of coarse grid points along the clearance multiplier axis during Stage 1 search.
    /// </summary>
    private const int CoarseClearanceSteps = 25;

    /// <summary>
    /// Number of coarse grid points along the daily erosion rate axis during Stage 1 search.
    /// </summary>
    private const int CoarseErosionSteps = 25;

    /// <summary>
    /// Number of progressive local refinement rounds in Stage 2 coordinate optimization.
    /// </summary>
    private const int RefinementRounds = 5;

    /// <summary>
    /// Number of samples per axis in each Stage 2 local refinement window.
    /// </summary>
    private const int RefinementGridPoints = 7;

    /// <summary>
    /// Minimum elapsed day after which elevated pituitary LH or FSH triggers an HPG axis escape warning.
    /// </summary>
    private const int HpgSuppressionCheckMinDay = 14;

    /// <summary>
    /// Clinical warning threshold for circulating LH escape in IU/L.
    /// </summary>
    private const double LhEscapeThresholdIuL = 2.0;

    /// <summary>
    /// Clinical warning threshold for circulating FSH escape in IU/L.
    /// </summary>
    private const double FshEscapeThresholdIuL = 3.0;

    /// <summary>
    /// Clinical warning threshold for FtM blood hematocrit elevation in percent.
    /// </summary>
    private const double FtmHematocritWarningPct = 52.0;

    /// <summary>
    /// Clinical critical threshold for FtM blood hematocrit erythrocytosis in percent.
    /// </summary>
    private const double FtmHematocritCriticalPct = 54.0;

    /// <summary>
    /// Clinical warning threshold for MtF supraphysiological estradiol exposure in pmol/L.
    /// </summary>
    private const double MtfEstradiolWarningPmolL = 1100.0;

    /// <summary>
    /// Clinical critical threshold for MtF thrombotic-risk estradiol exposure in pmol/L.
    /// </summary>
    private const double MtfEstradiolCriticalPmolL = 1500.0;

    /// <summary>
    /// Minimum therapeutic trough floor for MtF estradiol in pmol/L.
    /// </summary>
    private const double MtfEstradiolTroughFloorPmolL = 350.0;

    /// <summary>
    /// Minimum therapeutic trough floor for FtM total testosterone in nmol/L.
    /// </summary>
    private const double FtmTestosteroneTroughFloorNmolL = 10.0;

    /// <summary>
    /// Forward 180-day time-release depot simulator used to evaluate candidate parameter pairs.
    /// </summary>
    private readonly ITimeReleaseDepotService _depotService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotCalibrationService"/> class.
    /// </summary>
    /// <param name="depotService">The forward time-release depot simulation service.</param>
    /// <exception cref="ArgumentNullException"><paramref name="depotService"/> is <see langword="null"/>.</exception>
    public DepotCalibrationService(ITimeReleaseDepotService depotService)
    {
        ArgumentNullException.ThrowIfNull(depotService);
        _depotService = depotService;
    }

    /// <inheritdoc/>
    public DepotCalibrationResult CalibrateDepot(DepotCalibrationRequest request)
    {
        // WHAT: Validates calibration request inputs and fits individualized clearance and erosion kinetics via WLS.
        // WHY:  Assimilates real-world blood hormone levels to adaptively update re-dosing timing and next-cycle mass.
        // HOW:  Executes deterministic coarse-to-fine bounded 2D search with Bayesian prior shrinkage, then evaluates guardrails.
        ValidateCalibrationRequest(request);

        TimeReleaseDepotParameters baseline = request.BaselineParameters;
        IReadOnlyList<DepotAssayObservation> observations = request.Observations;

        double priorClearance = Math.Clamp(baseline.ClearanceMultiplier, MinClearanceMultiplier, MaxClearanceMultiplier);
        double priorErosion = Math.Clamp(baseline.DailyErosionRatePercent, MinDailyErosionRatePercent, MaxDailyErosionRatePercent);

        // WHAT: Selects Gaussian prior regularization weight based on observation count.
        // WHY:  A single lab draw cannot uniquely separate clearance from erosion without shrinking toward prior;
        //       multiple longitudinal draws identify both parameters tightly with minimal shrinkage bias.
        // HOW:  Uses 0.25 prior weight for single-point observations and 0.0002 ridge penalty for multi-point series.
        double priorWeight = observations.Count == 1 ? 0.25 : 0.0002;

        double bestClearance = priorClearance;
        double bestErosion = priorErosion;
        double bestLoss = double.PositiveInfinity;

        // WHAT: Stage 1 coarse global grid sweep across bounded clearance and erosion domains.
        // WHY:  Prevents convergence to local minima across non-linear reserve-depletion cliffs.
        // HOW:  Evaluates CoarseClearanceSteps x CoarseErosionSteps deterministic coordinates with zero LINQ allocations.
        double clearanceStep = (MaxClearanceMultiplier - MinClearanceMultiplier) / (CoarseClearanceSteps - 1);
        double erosionStep = (MaxDailyErosionRatePercent - MinDailyErosionRatePercent) / (CoarseErosionSteps - 1);

        for (int ci = 0; ci < CoarseClearanceSteps; ci++)
        {
            double candidateClearance = MinClearanceMultiplier + (ci * clearanceStep);
            for (int ei = 0; ei < CoarseErosionSteps; ei++)
            {
                double candidateErosion = MinDailyErosionRatePercent + (ei * erosionStep);
                double loss = EvaluateCandidateLoss(
                    baseline,
                    observations,
                    candidateClearance,
                    candidateErosion,
                    priorClearance,
                    priorErosion,
                    priorWeight);

                if (loss < bestLoss)
                {
                    bestLoss = loss;
                    bestClearance = candidateClearance;
                    bestErosion = candidateErosion;
                }
            }
        }

        // WHAT: Stage 2 progressive local coordinate refinement around the coarse optimum.
        // WHY:  Tightens parameter inference precision below 0.2% while remaining strictly deterministic and fast.
        // HOW:  Shrinks local window radii across RefinementRounds and evaluates a 7x7 bounded sub-grid each round.
        double clearanceRadius = clearanceStep * 1.5;
        double erosionRadius = erosionStep * 1.5;

        for (int round = 0; round < RefinementRounds; round++)
        {
            double cLow = Math.Max(MinClearanceMultiplier, bestClearance - clearanceRadius);
            double cHigh = Math.Min(MaxClearanceMultiplier, bestClearance + clearanceRadius);
            double eLow = Math.Max(MinDailyErosionRatePercent, bestErosion - erosionRadius);
            double eHigh = Math.Min(MaxDailyErosionRatePercent, bestErosion + erosionRadius);

            double localCStep = (cHigh - cLow) / (RefinementGridPoints - 1);
            double localEStep = (eHigh - eLow) / (RefinementGridPoints - 1);

            double roundBestClearance = bestClearance;
            double roundBestErosion = bestErosion;

            for (int ci = 0; ci < RefinementGridPoints; ci++)
            {
                double candidateClearance = cLow + (ci * localCStep);
                for (int ei = 0; ei < RefinementGridPoints; ei++)
                {
                    double candidateErosion = eLow + (ei * localEStep);
                    double loss = EvaluateCandidateLoss(
                        baseline,
                        observations,
                        candidateClearance,
                        candidateErosion,
                        priorClearance,
                        priorErosion,
                        priorWeight);

                    if (loss < bestLoss)
                    {
                        bestLoss = loss;
                        roundBestClearance = candidateClearance;
                        roundBestErosion = candidateErosion;
                    }
                }
            }

            bestClearance = roundBestClearance;
            bestErosion = roundBestErosion;
            clearanceRadius *= 0.50;
            erosionRadius *= 0.50;
        }

        double inferredClearance = Math.Round(bestClearance, 4);
        double inferredErosion = Math.Round(bestErosion, 4);

        TimeReleaseDepotParameters calibratedParameters = baseline with
        {
            ClearanceMultiplier = inferredClearance,
            DailyErosionRatePercent = inferredErosion
        };

        TimeReleaseSimulationResult recalibratedTrajectory = _depotService.Simulate180DayDepot(calibratedParameters);
        double residualRmse = Math.Round(ComputeResidualRmse(baseline.Regimen, observations, recalibratedTrajectory), 4);
        IReadOnlyList<DepotSafetyAlert> alerts = EvaluateSafetyGuardrails(baseline.Regimen, observations);
        DepotSafetyStatus overallStatus = DetermineOverallSafetyStatus(alerts);
        int adjustedRedoseDay = ComputeAdjustedRedoseDay(baseline, recalibratedTrajectory);
        double recommendedNextMassMg = ComputeRecommendedNextMassMg(baseline, inferredClearance, overallStatus);
        double recommendedNextErosionRate = ComputeRecommendedNextErosionRate(baseline, inferredErosion);

        return new DepotCalibrationResult
        {
            DepotId = request.DepotId,
            InferredClearanceMultiplier = inferredClearance,
            InferredDailyErosionRatePercent = inferredErosion,
            ResidualRmse = residualRmse,
            AdjustedRedoseDay = adjustedRedoseDay,
            RecommendedNextMassMg = recommendedNextMassMg,
            RecommendedNextErosionRatePercent = recommendedNextErosionRate,
            OverallSafetyStatus = overallStatus,
            Alerts = alerts,
            RecalibratedTrajectory = recalibratedTrajectory
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<DepotSafetyAlert> EvaluateSafetyGuardrails(
        TherapeuticRegimen regimen,
        IReadOnlyList<DepotAssayObservation> observations)
    {
        // WHAT: Evaluates clinical HPG escape, hematocrit, supraphysiological estradiol, and trough drift guardrails.
        // WHY:  Protects patients from erythrocytosis, thrombosis risk, pituitary escape, and premature hormone depletion.
        // HOW:  Iterates observations in a zero-LINQ loop and records typed DepotSafetyAlert instances for every threshold breach.
        ArgumentNullException.ThrowIfNull(observations);

        List<DepotSafetyAlert> alerts = [];

        for (int i = 0; i < observations.Count; i++)
        {
            DepotAssayObservation obs = observations[i];

            if (obs.DaySinceImplant > HpgSuppressionCheckMinDay)
            {
                if (obs.ObservedLhIuL >= LhEscapeThresholdIuL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "HPG_LH_ESCAPE",
                        Severity = DepotSafetyStatus.WarningEscape,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Measured LH ({1:F2} IU/L) indicates pituitary HPG axis escape (>= {2:F1} IU/L).",
                            obs.DaySinceImplant,
                            obs.ObservedLhIuL,
                            LhEscapeThresholdIuL),
                        MeasuredValue = obs.ObservedLhIuL,
                        ThresholdValue = LhEscapeThresholdIuL
                    });
                }

                if (obs.ObservedFshIuL >= FshEscapeThresholdIuL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "HPG_FSH_ESCAPE",
                        Severity = DepotSafetyStatus.WarningEscape,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Measured FSH ({1:F2} IU/L) indicates incomplete gonadotropin suppression (>= {2:F1} IU/L).",
                            obs.DaySinceImplant,
                            obs.ObservedFshIuL,
                            FshEscapeThresholdIuL),
                        MeasuredValue = obs.ObservedFshIuL,
                        ThresholdValue = FshEscapeThresholdIuL
                    });
                }
            }

            if (regimen == TherapeuticRegimen.FemaleToMale)
            {
                if (obs.ObservedHematocritPct >= FtmHematocritCriticalPct)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "FTM_HEMATOCRIT_CRITICAL",
                        Severity = DepotSafetyStatus.CriticalSafety,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Critical erythrocytosis risk — hematocrit {1:F1}% reached or exceeded {2:F1}% ceiling.",
                            obs.DaySinceImplant,
                            obs.ObservedHematocritPct,
                            FtmHematocritCriticalPct),
                        MeasuredValue = obs.ObservedHematocritPct,
                        ThresholdValue = FtmHematocritCriticalPct
                    });
                }
                else if (obs.ObservedHematocritPct >= FtmHematocritWarningPct)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "FTM_HEMATOCRIT_WARNING",
                        Severity = DepotSafetyStatus.WarningEscape,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Elevated hematocrit ({1:F1}%) reached the {2:F1}% monitoring threshold.",
                            obs.DaySinceImplant,
                            obs.ObservedHematocritPct,
                            FtmHematocritWarningPct),
                        MeasuredValue = obs.ObservedHematocritPct,
                        ThresholdValue = FtmHematocritWarningPct
                    });
                }

                if (obs.ObservedTNmolL < FtmTestosteroneTroughFloorNmolL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "FTM_T_SUBTHERAPEUTIC_TROUGH",
                        Severity = DepotSafetyStatus.AdvisoryDrift,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Measured total testosterone ({1:F2} nmol/L) dropped below the {2:F1} nmol/L trough floor.",
                            obs.DaySinceImplant,
                            obs.ObservedTNmolL,
                            FtmTestosteroneTroughFloorNmolL),
                        MeasuredValue = obs.ObservedTNmolL,
                        ThresholdValue = FtmTestosteroneTroughFloorNmolL
                    });
                }
            }
            else if (regimen == TherapeuticRegimen.MaleToFemale)
            {
                if (obs.ObservedE2PmolL >= MtfEstradiolCriticalPmolL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "MTF_E2_CRITICAL",
                        Severity = DepotSafetyStatus.CriticalSafety,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Critical supraphysiological estradiol ({1:F1} pmol/L) reached or exceeded {2:F1} pmol/L ceiling.",
                            obs.DaySinceImplant,
                            obs.ObservedE2PmolL,
                            MtfEstradiolCriticalPmolL),
                        MeasuredValue = obs.ObservedE2PmolL,
                        ThresholdValue = MtfEstradiolCriticalPmolL
                    });
                }
                else if (obs.ObservedE2PmolL >= MtfEstradiolWarningPmolL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "MTF_E2_WARNING",
                        Severity = DepotSafetyStatus.WarningEscape,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Supraphysiological estradiol ({1:F1} pmol/L) reached the {2:F1} pmol/L warning threshold.",
                            obs.DaySinceImplant,
                            obs.ObservedE2PmolL,
                            MtfEstradiolWarningPmolL),
                        MeasuredValue = obs.ObservedE2PmolL,
                        ThresholdValue = MtfEstradiolWarningPmolL
                    });
                }

                if (obs.ObservedE2PmolL < MtfEstradiolTroughFloorPmolL)
                {
                    alerts.Add(new DepotSafetyAlert
                    {
                        Code = "MTF_E2_SUBTHERAPEUTIC_TROUGH",
                        Severity = DepotSafetyStatus.AdvisoryDrift,
                        Message = string.Format(
                            CultureInfo.InvariantCulture,
                            "Day {0}: Measured estradiol ({1:F1} pmol/L) dropped below the {2:F1} pmol/L therapeutic trough floor.",
                            obs.DaySinceImplant,
                            obs.ObservedE2PmolL,
                            MtfEstradiolTroughFloorPmolL),
                        MeasuredValue = obs.ObservedE2PmolL,
                        ThresholdValue = MtfEstradiolTroughFloorPmolL
                    });
                }
            }
        }

        return alerts;
    }

    /// <summary>
    /// Evaluates the regularized weighted least-squares loss for a candidate clearance and erosion parameter pair.
    /// </summary>
    /// <param name="baseline">Baseline theoretical depot parameters.</param>
    /// <param name="observations">Longitudinal serum assay observations.</param>
    /// <param name="candidateClearance">Candidate metabolic clearance multiplier.</param>
    /// <param name="candidateErosion">Candidate daily polymer erosion rate percentage.</param>
    /// <param name="priorClearance">Baseline prior metabolic clearance multiplier.</param>
    /// <param name="priorErosion">Baseline prior daily polymer erosion rate percentage.</param>
    /// <param name="priorWeight">Bayesian Gaussian prior regularization weight.</param>
    /// <returns>The non-negative regularized objective value to minimize.</returns>
    private double EvaluateCandidateLoss(
        TimeReleaseDepotParameters baseline,
        IReadOnlyList<DepotAssayObservation> observations,
        double candidateClearance,
        double candidateErosion,
        double priorClearance,
        double priorErosion,
        double priorWeight)
    {
        // WHAT: Runs the forward simulator for (candidateClearance, candidateErosion) and scores residuals + prior penalty.
        // WHY:  Quantifies how well a candidate kinetic pair explains observed serum assays while regularizing uncertainty.
        // HOW:  Accumulates weighted normalized squared errors in a zero-allocation for loop and adds quadratic prior shrinkage.
        TimeReleaseDepotParameters candidateParams = baseline with
        {
            ClearanceMultiplier = candidateClearance,
            DailyErosionRatePercent = candidateErosion
        };

        TimeReleaseSimulationResult sim = _depotService.Simulate180DayDepot(candidateParams);
        IReadOnlyList<TimeReleaseDailyPoint> points = sim.Points;
        int maxIndex = points.Count - 1;

        double sumWeightedSqErr = 0.0;
        for (int i = 0; i < observations.Count; i++)
        {
            DepotAssayObservation obs = observations[i];
            int dayIndex = Math.Clamp(obs.DaySinceImplant, 0, maxIndex);
            TimeReleaseDailyPoint predicted = points[dayIndex];

            double e2Err = (obs.ObservedE2PmolL - predicted.EstradiolPmolL) / EstradiolNormalizationScale;
            double tErr = (obs.ObservedTNmolL - predicted.TestosteroneNmolL) / TestosteroneNormalizationScale;
            double lhErr = (obs.ObservedLhIuL - predicted.LhIuL) / LhNormalizationScale;

            sumWeightedSqErr += baseline.Regimen switch
            {
                TherapeuticRegimen.MaleToFemale => (e2Err * e2Err) + (0.10 * tErr * tErr) + (0.05 * lhErr * lhErr),
                TherapeuticRegimen.FemaleToMale => (tErr * tErr) + (0.10 * e2Err * e2Err) + (0.05 * lhErr * lhErr),
                _ => (0.50 * e2Err * e2Err) + (0.50 * tErr * tErr) + (0.05 * lhErr * lhErr)
            };
        }

        double dataLoss = sumWeightedSqErr / observations.Count;
        double clearanceDiff = (candidateClearance - priorClearance) / priorClearance;
        double erosionDiff = (candidateErosion - priorErosion) / priorErosion;
        double priorPenalty = priorWeight * ((clearanceDiff * clearanceDiff) + (erosionDiff * erosionDiff));

        return dataLoss + priorPenalty;
    }

    /// <summary>
    /// Computes the unregularized normalized root-mean-square residual error between observations and predictions.
    /// </summary>
    /// <param name="regimen">Active therapeutic endocrine regimen.</param>
    /// <param name="observations">Measured serum assay observations.</param>
    /// <param name="trajectory">Recalibrated forward simulation trajectory.</param>
    /// <returns>The normalized RMSE across all observed timepoints.</returns>
    private static double ComputeResidualRmse(
        TherapeuticRegimen regimen,
        IReadOnlyList<DepotAssayObservation> observations,
        TimeReleaseSimulationResult trajectory)
    {
        // WHAT: Calculates normalized RMSE of primary therapeutic hormone predictions against observed assays.
        // WHY:  Provides clinicians with an interpretable goodness-of-fit metric for the calibrated curve.
        // HOW:  Sums normalized squared errors over all observations in a zero-LINQ loop and returns the square root of the mean.
        IReadOnlyList<TimeReleaseDailyPoint> points = trajectory.Points;
        int maxIndex = points.Count - 1;
        double sumSq = 0.0;

        for (int i = 0; i < observations.Count; i++)
        {
            DepotAssayObservation obs = observations[i];
            int dayIndex = Math.Clamp(obs.DaySinceImplant, 0, maxIndex);
            TimeReleaseDailyPoint predicted = points[dayIndex];

            double err = regimen switch
            {
                TherapeuticRegimen.MaleToFemale => (obs.ObservedE2PmolL - predicted.EstradiolPmolL) / EstradiolNormalizationScale,
                TherapeuticRegimen.FemaleToMale => (obs.ObservedTNmolL - predicted.TestosteroneNmolL) / TestosteroneNormalizationScale,
                _ => (((obs.ObservedE2PmolL - predicted.EstradiolPmolL) / EstradiolNormalizationScale)
                    + ((obs.ObservedTNmolL - predicted.TestosteroneNmolL) / TestosteroneNormalizationScale)) * 0.5
            };

            sumSq += err * err;
        }

        return Math.Sqrt(sumSq / observations.Count);
    }

    /// <summary>
    /// Determines the highest clinical safety severity across all triggered guardrail alerts.
    /// </summary>
    /// <param name="alerts">Evaluated safety alerts.</param>
    /// <returns>The maximum <see cref="DepotSafetyStatus"/> present, or <see cref="DepotSafetyStatus.Optimal"/> if none.</returns>
    private static DepotSafetyStatus DetermineOverallSafetyStatus(IReadOnlyList<DepotSafetyAlert> alerts)
    {
        // WHAT: Reduces a list of alerts to the highest severity classification.
        // WHY:  Drives top-level clinical badge styling and automated safety down-titration rules.
        // HOW:  Compares enum ordinal values sequentially without LINQ allocations.
        DepotSafetyStatus highest = DepotSafetyStatus.Optimal;
        for (int i = 0; i < alerts.Count; i++)
        {
            if (alerts[i].Severity > highest)
            {
                highest = alerts[i].Severity;
            }
        }

        return highest;
    }

    /// <summary>
    /// Computes the earliest day on the recalibrated trajectory where re-dosing is clinically indicated.
    /// </summary>
    /// <param name="baseline">Baseline depot parameters.</param>
    /// <param name="recalibratedTrajectory">Recalibrated daily trajectory.</param>
    /// <returns>The adjusted clinical re-dosing day within the simulated horizon.</returns>
    private static int ComputeAdjustedRedoseDay(
        TimeReleaseDepotParameters baseline,
        TimeReleaseSimulationResult recalibratedTrajectory)
    {
        // WHAT: Identifies the earliest post-ramp day where depot reserve drops <= 5% or primary hormone falls below trough floor.
        // WHY:  Prevents patients from experiencing endocrine withdrawal symptoms when in-vivo erosion or clearance is faster than nominal.
        // HOW:  Scans recalibrated trajectory points starting after Phase I burst suppression (Day 8) through TargetHorizonDays.
        IReadOnlyList<TimeReleaseDailyPoint> points = recalibratedTrajectory.Points;
        int startDay = points.Count > 8 ? 8 : 1;

        for (int i = startDay; i < points.Count; i++)
        {
            TimeReleaseDailyPoint pt = points[i];
            double reserveFraction = pt.RemainingReserveMg / baseline.InitialPayloadMassMg;
            bool belowFloor = baseline.Regimen switch
            {
                TherapeuticRegimen.MaleToFemale => pt.EstradiolPmolL < MtfEstradiolTroughFloorPmolL,
                TherapeuticRegimen.FemaleToMale => pt.TestosteroneNmolL < FtmTestosteroneTroughFloorNmolL,
                _ => false
            };

            if (reserveFraction <= 0.05 || belowFloor)
            {
                return pt.Day;
            }
        }

        return baseline.TargetHorizonDays;
    }

    /// <summary>
    /// Calculates the recommended initial nanocarrier payload mass in milligrams for the next 180-day depot cycle.
    /// </summary>
    /// <param name="baseline">Baseline depot formulation parameters.</param>
    /// <param name="inferredClearance">Inferred patient metabolic clearance multiplier.</param>
    /// <param name="overallStatus">Overall clinical safety status from guardrail evaluation.</param>
    /// <returns>Recommended payload mass in milligrams rounded to two decimal places.</returns>
    private static double ComputeRecommendedNextMassMg(
        TimeReleaseDepotParameters baseline,
        double inferredClearance,
        DepotSafetyStatus overallStatus)
    {
        // WHAT: Scales next-cycle payload mass proportionally to inferred systemic clearance with safety ceiling protection.
        // WHY:  Rapid metabolizers need higher payload reserves to sustain 180 days, whereas critical safety alerts demand down-titration.
        // HOW:  Multiplies baseline mass by clearance ratio, clamps to [0.5x, 2.5x], and applies a 15% reduction if CriticalSafety.
        double clearanceRatio = inferredClearance / baseline.ClearanceMultiplier;
        double scaledMass = baseline.InitialPayloadMassMg * clearanceRatio;
        double minMass = baseline.InitialPayloadMassMg * 0.50;
        double maxMass = baseline.InitialPayloadMassMg * 2.50;
        double boundedMass = Math.Clamp(scaledMass, minMass, maxMass);

        if (overallStatus == DepotSafetyStatus.CriticalSafety)
        {
            boundedMass *= 0.85;
        }

        return Math.Round(boundedMass, 2);
    }

    /// <summary>
    /// Calculates the recommended target daily erosion rate percentage for the next 180-day hydrogel fabrication.
    /// </summary>
    /// <param name="baseline">Baseline depot formulation parameters.</param>
    /// <param name="inferredErosion">Inferred in-vivo daily hydrogel erosion rate percentage.</param>
    /// <returns>Recommended target daily erosion rate percentage bounded to [0.20, 2.00].</returns>
    private static double ComputeRecommendedNextErosionRate(
        TimeReleaseDepotParameters baseline,
        double inferredErosion)
    {
        // WHAT: Compensates next-cycle polymer crosslinking density for faster or slower in-vivo hydrogel erosion.
        // WHY:  If tissue esterases degraded the depot faster than nominal, tighter crosslinking restores a full 180-day duration.
        // HOW:  Applies inverse ratio scaling relative to baseline erosion rate and clamps to physical synthesis bounds.
        double compensated = baseline.DailyErosionRatePercent * (baseline.DailyErosionRatePercent / inferredErosion);
        return Math.Round(Math.Clamp(compensated, MinDailyErosionRatePercent, MaxDailyErosionRatePercent), 3);
    }

    /// <summary>
    /// Validates structural and biophysical bounds on a <see cref="DepotCalibrationRequest"/>.
    /// </summary>
    /// <param name="request">The calibration request to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when required objects are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <c>DepotId</c> is blank or no observations are supplied.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any assay biomarker falls outside physical bounds.</exception>
    private static void ValidateCalibrationRequest(DepotCalibrationRequest request)
    {
        // WHAT: Enforces fail-fast validation on depot ID, baseline parameters, and every assay measurement.
        // WHY:  Prevents non-physical negative lab values, invalid days, or empty observation lists from entering optimization.
        // HOW:  Checks nullability and numerical ranges with explicit ArgumentException / ArgumentOutOfRangeException throws.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DepotId);
        ArgumentNullException.ThrowIfNull(request.BaselineParameters);
        ArgumentNullException.ThrowIfNull(request.Observations);

        if (request.Observations.Count == 0)
        {
            throw new ArgumentException("At least one serum assay observation is required for calibration.", nameof(request));
        }

        for (int i = 0; i < request.Observations.Count; i++)
        {
            DepotAssayObservation obs = request.Observations[i];
            if (obs is null)
            {
                throw new ArgumentException("Assay observation entries cannot be null.", nameof(request));
            }

            if (obs.DaySinceImplant < 1 || obs.DaySinceImplant > 365)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "DaySinceImplant must be between 1 and 365.");
            }

            if (obs.ObservedE2PmolL < 0.0 || obs.ObservedTNmolL < 0.0 || obs.ObservedLhIuL < 0.0 ||
                obs.ObservedFshIuL < 0.0 || obs.ObservedShbgNmolL < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Observed hormone concentrations cannot be negative.");
            }

            if (obs.ObservedHematocritPct < 0.0 || obs.ObservedHematocritPct > 100.0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "ObservedHematocritPct must be between 0.0 and 100.0.");
            }
        }
    }
}
