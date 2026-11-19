using NanoWeaverHrt.Endocrine;
using Xunit;

namespace NanoWeaverHrt.Tests.Endocrine;

/// <summary>
/// Comprehensive unit tests for <see cref="DepotCalibrationService"/> and Phase 2 calibration domain models.
/// </summary>
public sealed class DepotCalibrationServiceTests
{
    /// <summary>
    /// Forward time-release depot simulator used to generate synthetic ground truth and run calibration.
    /// </summary>
    private readonly TimeReleaseDepotService _depotService = new();

    /// <summary>
    /// Longitudinal Bayesian depot calibration service under test.
    /// </summary>
    private readonly DepotCalibrationService _calibrationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotCalibrationServiceTests"/> class.
    /// </summary>
    public DepotCalibrationServiceTests()
    {
        _calibrationService = new DepotCalibrationService(_depotService);
    }

    /// <summary>
    /// Verifies default values, init accessors, and OpenAPI example generators across all Phase 2 calibration domain records.
    /// </summary>
    [Fact]
    public void DomainModels_DefaultsAndExamples()
    {
        // WHAT: Instantiates default and example instances of DepotAssayObservation, DepotSafetyAlert,
        //       DepotCalibrationRequest, and DepotCalibrationResult.
        // WHY:  Guarantees 100% coverage of DTO properties and Swagger IExamplesProvider implementations.
        // HOW:  Constructs each record, validates default properties, and asserts non-empty example payloads.
        DepotAssayObservation obs = new();
        Assert.Equal(0, obs.DaySinceImplant);
        DepotAssayObservation obsEx = obs.GetExamples();
        Assert.Equal(60, obsEx.DaySinceImplant);
        Assert.True(obsEx.ObservedE2PmolL > 0);

        DepotSafetyAlert alert = new();
        Assert.Equal(string.Empty, alert.Code);
        Assert.Equal(DepotSafetyStatus.Optimal, alert.Severity);
        DepotSafetyAlert alertEx = alert.GetExamples();
        Assert.False(string.IsNullOrWhiteSpace(alertEx.Code));
        Assert.Equal(DepotSafetyStatus.AdvisoryDrift, alertEx.Severity);

        DepotCalibrationRequest req = new();
        Assert.Equal(string.Empty, req.DepotId);
        Assert.Empty(req.Observations);
        DepotCalibrationRequest reqEx = req.GetExamples();
        Assert.False(string.IsNullOrWhiteSpace(reqEx.DepotId));
        Assert.Single(reqEx.Observations);

        DepotCalibrationResult res = new();
        Assert.Equal(1.0, res.InferredClearanceMultiplier);
        Assert.Equal(0.55, res.InferredDailyErosionRatePercent);
        Assert.Equal(180, res.AdjustedRedoseDay);
        Assert.Equal(18.0, res.RecommendedNextMassMg);
        Assert.Equal(0.55, res.RecommendedNextErosionRatePercent);
        Assert.Equal(DepotSafetyStatus.Optimal, res.OverallSafetyStatus);
        DepotCalibrationResult resEx = res.GetExamples();
        Assert.Equal(164, resEx.AdjustedRedoseDay);
        Assert.NotNull(resEx.RecalibratedTrajectory);
    }

    /// <summary>
    /// Verifies constructor and request validation guards throw typed exceptions on invalid arguments.
    /// </summary>
    [Fact]
    public void ValidationGuards_ThrowOnInvalidArguments()
    {
        // WHAT: Exercises every defensive guard clause in DepotCalibrationService.
        // WHY:  Ensures invalid or non-physical inputs are rejected deterministically before optimization.
        // HOW:  Calls constructor, EvaluateSafetyGuardrails, and CalibrateDepot with invalid values.
        Assert.Throws<ArgumentNullException>(() => new DepotCalibrationService(null!));
        Assert.Throws<ArgumentNullException>(() => _calibrationService.EvaluateSafetyGuardrails(TherapeuticRegimen.MaleToFemale, null!));
        Assert.Throws<ArgumentNullException>(() => _calibrationService.CalibrateDepot(null!));

        Assert.Throws<ArgumentException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = " ",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30 }]
        }));

        Assert.Throws<ArgumentNullException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = null!,
            Observations = [new DepotAssayObservation { DaySinceImplant = 30 }]
        }));

        Assert.Throws<ArgumentNullException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = null!
        }));

        Assert.Throws<ArgumentException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = []
        }));

        Assert.Throws<ArgumentException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [null!]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 366 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedE2PmolL = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedTNmolL = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedLhIuL = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedFshIuL = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedShbgNmolL = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedHematocritPct = -1.0 }]
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "d1",
            PatientId = "p1",
            BaselineParameters = new TimeReleaseDepotParameters(),
            Observations = [new DepotAssayObservation { DaySinceImplant = 30, ObservedHematocritPct = 101.0 }]
        }));
    }

    /// <summary>
    /// Verifies MtF multi-point WLS calibration recovers synthetic ground-truth clearance and erosion within 2%
    /// and is 100% deterministic across repeated runs.
    /// </summary>
    [Fact]
    public void CalibrateDepot_MtfMultiPoint_RecoversSyntheticGroundTruthWithin2Percent()
    {
        // WHAT: Generates synthetic Day 30, 90, 145, 165 assays from a shifted MtF patient (clearance 1.35, erosion 0.78)
        //       and verifies the calibration engine recovers both parameters within 2% with near-zero RMSE.
        // WHY:  Validates biophysical parameter identifiability and deterministic convergence of the coarse-to-fine optimizer.
        // HOW:  Simulates ground truth via TimeReleaseDepotService, passes observations with nominal baseline, asserts error <= 2%.
        TimeReleaseDepotParameters baseline = new()
        {
            Regimen = TherapeuticRegimen.MaleToFemale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialPayloadMassMg = 18.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = 0.55
        };

        const double trueClearance = 1.35;
        const double trueErosion = 0.78;
        TimeReleaseSimulationResult truth = _depotService.Simulate180DayDepot(baseline with
        {
            ClearanceMultiplier = trueClearance,
            DailyErosionRatePercent = trueErosion
        });

        int[] sampleDays = [30, 90, 125, 138, 160];
        List<DepotAssayObservation> observations = [];
        foreach (int day in sampleDays)
        {
            TimeReleaseDailyPoint pt = truth.Points[day];
            observations.Add(new DepotAssayObservation
            {
                DaySinceImplant = day,
                ObservedE2PmolL = pt.EstradiolPmolL,
                ObservedTNmolL = pt.TestosteroneNmolL,
                ObservedLhIuL = pt.LhIuL,
                ObservedFshIuL = pt.FshIuL,
                ObservedShbgNmolL = 75.0,
                ObservedHematocritPct = 41.0
            });
        }

        DepotCalibrationRequest request = new()
        {
            DepotId = "depot_mtf_truth",
            PatientId = "patient_mtf_truth",
            BaselineParameters = baseline,
            Observations = observations
        };

        DepotCalibrationResult result1 = _calibrationService.CalibrateDepot(request);
        DepotCalibrationResult result2 = _calibrationService.CalibrateDepot(request);

        Assert.Equal(result1.InferredClearanceMultiplier, result2.InferredClearanceMultiplier);
        Assert.Equal(result1.InferredDailyErosionRatePercent, result2.InferredDailyErosionRatePercent);
        Assert.Equal(result1.AdjustedRedoseDay, result2.AdjustedRedoseDay);

        double clearanceRelErr = Math.Abs(result1.InferredClearanceMultiplier - trueClearance) / trueClearance;
        double erosionRelErr = Math.Abs(result1.InferredDailyErosionRatePercent - trueErosion) / trueErosion;

        Assert.True(clearanceRelErr <= 0.02, $"Clearance error {clearanceRelErr:P2} exceeded 2% (inferred {result1.InferredClearanceMultiplier}).");
        Assert.True(erosionRelErr <= 0.02, $"Erosion error {erosionRelErr:P2} exceeded 2% (inferred {result1.InferredDailyErosionRatePercent}).");
        Assert.True(result1.ResidualRmse < 0.02);
        Assert.True(result1.AdjustedRedoseDay < 180);
        Assert.True(result1.RecommendedNextMassMg > baseline.InitialPayloadMassMg);
        Assert.True(result1.RecommendedNextErosionRatePercent < baseline.DailyErosionRatePercent);
    }

    /// <summary>
    /// Verifies FtM multi-point WLS calibration recovers synthetic ground-truth clearance and erosion within 2%.
    /// </summary>
    [Fact]
    public void CalibrateDepot_FtmMultiPoint_RecoversSyntheticGroundTruthWithin2Percent()
    {
        // WHAT: Generates synthetic Day 30, 90, 140, 155 assays from a shifted FtM patient (clearance 0.82, erosion 0.68)
        //       and verifies the calibration engine recovers both parameters within 2%.
        // WHY:  Confirms FtM testosterone-primary objective weighting and adaptive redose calculation work identically to MtF.
        // HOW:  Simulates ground truth via TimeReleaseDepotService, runs CalibrateDepot, asserts convergence <= 2%.
        TimeReleaseDepotParameters baseline = new()
        {
            Regimen = TherapeuticRegimen.FemaleToMale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialPayloadMassMg = 1000.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = 0.55,
            InitialEstradiolPmolL = 500.0,
            InitialTestosteroneNmolL = 1.5,
            TargetEstradiolPmolL = 100.0,
            TargetTestosteroneNmolL = 20.0
        };

        const double trueClearance = 0.82;
        const double trueErosion = 0.68;
        TimeReleaseSimulationResult truth = _depotService.Simulate180DayDepot(baseline with
        {
            ClearanceMultiplier = trueClearance,
            DailyErosionRatePercent = trueErosion
        });

        int[] sampleDays = [30, 90, 145, 155, 165];
        List<DepotAssayObservation> observations = [];
        foreach (int day in sampleDays)
        {
            TimeReleaseDailyPoint pt = truth.Points[day];
            observations.Add(new DepotAssayObservation
            {
                DaySinceImplant = day,
                ObservedE2PmolL = pt.EstradiolPmolL,
                ObservedTNmolL = pt.TestosteroneNmolL,
                ObservedLhIuL = pt.LhIuL,
                ObservedFshIuL = pt.FshIuL,
                ObservedShbgNmolL = 32.0,
                ObservedHematocritPct = 46.5
            });
        }

        DepotCalibrationResult result = _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "depot_ftm_truth",
            PatientId = "patient_ftm_truth",
            BaselineParameters = baseline,
            Observations = observations
        });

        double clearanceRelErr = Math.Abs(result.InferredClearanceMultiplier - trueClearance) / trueClearance;
        double erosionRelErr = Math.Abs(result.InferredDailyErosionRatePercent - trueErosion) / trueErosion;

        Assert.True(clearanceRelErr <= 0.02, $"Clearance error {clearanceRelErr:P2} exceeded 2% (inferred {result.InferredClearanceMultiplier}).");
        Assert.True(erosionRelErr <= 0.02, $"Erosion error {erosionRelErr:P2} exceeded 2% (inferred {result.InferredDailyErosionRatePercent}).");
        Assert.True(result.ResidualRmse < 0.02);
        Assert.True(result.AdjustedRedoseDay < 180);
    }

    /// <summary>
    /// Verifies single-point Bayesian prior shrinkage keeps estimates anchored between baseline and extreme shift,
    /// and exercises short-horizon and Custom regimen branches.
    /// </summary>
    [Fact]
    public void CalibrateDepot_SinglePointShrinkageAndCustomRegimen()
    {
        // WHAT: Tests single-point observation prior shrinkage on MtF and exercises Custom regimen + short horizon (< 8 days).
        // WHY:  Prevents overfitting from a single lab visit and ensures 100% branch coverage across Custom regimen and short horizons.
        // HOW:  Runs CalibrateDepot with a single shifted MtF observation, then with a short-horizon Custom regimen depot.
        TimeReleaseDepotParameters mtfBaseline = new()
        {
            Regimen = TherapeuticRegimen.MaleToFemale,
            InitialPayloadMassMg = 18.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = 0.55
        };

        DepotCalibrationResult singleResult = _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "depot_single",
            PatientId = "patient_single",
            BaselineParameters = mtfBaseline,
            Observations =
            [
                new DepotAssayObservation
                {
                    DaySinceImplant = 45,
                    ObservedE2PmolL = 420.0,
                    ObservedTNmolL = 1.1,
                    ObservedLhIuL = 0.6,
                    ObservedFshIuL = 0.9,
                    ObservedShbgNmolL = 70.0,
                    ObservedHematocritPct = 41.0
                }
            ]
        });

        Assert.True(singleResult.InferredClearanceMultiplier > 1.0);
        Assert.True(singleResult.InferredClearanceMultiplier < 1.8);

        TimeReleaseDepotParameters customShortBaseline = new()
        {
            Regimen = TherapeuticRegimen.Custom,
            InitialPayloadMassMg = 50.0,
            TargetHorizonDays = 5,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = 0.55,
            TargetEstradiolPmolL = 450.0,
            TargetTestosteroneNmolL = 5.0
        };

        DepotCalibrationResult customResult = _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "depot_custom_short",
            PatientId = "patient_custom",
            BaselineParameters = customShortBaseline,
            Observations =
            [
                new DepotAssayObservation
                {
                    DaySinceImplant = 4,
                    ObservedE2PmolL = 440.0,
                    ObservedTNmolL = 5.1,
                    ObservedLhIuL = 1.2,
                    ObservedFshIuL = 1.4,
                    ObservedShbgNmolL = 55.0,
                    ObservedHematocritPct = 43.0
                }
            ]
        });

        Assert.Equal(5, customResult.AdjustedRedoseDay);
        Assert.Equal(DepotSafetyStatus.Optimal, customResult.OverallSafetyStatus);
    }

    /// <summary>
    /// Verifies every clinical safety guardrail branch across MtF, FtM, and HPG escape conditions,
    /// including automated 15% next-cycle mass down-titration on CriticalSafety.
    /// </summary>
    [Fact]
    public void EvaluateSafetyGuardrails_AllClinicalThresholdsAndCriticalDownTitration()
    {
        // WHAT: Tests HPG LH/FSH escape (> Day 14 vs <= Day 14), FtM hematocrit warning & critical, FtM low T trough,
        //       MtF E2 warning & critical, MtF low E2 trough, and CriticalSafety 15% next-mass down-titration.
        // WHY:  Guarantees 100% branch coverage of every clinical patient-safety guardrail.
        // HOW:  Constructs targeted observations for each threshold boundary and asserts alert codes, severities, and down-titration.

        // 1. Day 10 elevated LH/FSH should NOT trigger HPG escape (still in early ramp <= Day 14)
        IReadOnlyList<DepotSafetyAlert> earlyAlerts = _calibrationService.EvaluateSafetyGuardrails(
            TherapeuticRegimen.MaleToFemale,
            [
                new DepotAssayObservation
                {
                    DaySinceImplant = 10,
                    ObservedE2PmolL = 520.0,
                    ObservedTNmolL = 1.0,
                    ObservedLhIuL = 4.5,
                    ObservedFshIuL = 4.2,
                    ObservedHematocritPct = 40.0
                }
            ]);
        Assert.Empty(earlyAlerts);

        // 2. FtM Warning hematocrit (52.5%) + HPG LH/FSH escape (Day 30) + sub-therapeutic T trough (< 10 nmol/L)
        IReadOnlyList<DepotSafetyAlert> ftmWarnAlerts = _calibrationService.EvaluateSafetyGuardrails(
            TherapeuticRegimen.FemaleToMale,
            [
                new DepotAssayObservation
                {
                    DaySinceImplant = 30,
                    ObservedE2PmolL = 120.0,
                    ObservedTNmolL = 8.5,
                    ObservedLhIuL = 2.4,
                    ObservedFshIuL = 3.5,
                    ObservedHematocritPct = 52.5
                }
            ]);
        Assert.Contains(ftmWarnAlerts, a => a.Code == "HPG_LH_ESCAPE" && a.Severity == DepotSafetyStatus.WarningEscape);
        Assert.Contains(ftmWarnAlerts, a => a.Code == "HPG_FSH_ESCAPE" && a.Severity == DepotSafetyStatus.WarningEscape);
        Assert.Contains(ftmWarnAlerts, a => a.Code == "FTM_HEMATOCRIT_WARNING" && a.Severity == DepotSafetyStatus.WarningEscape);
        Assert.Contains(ftmWarnAlerts, a => a.Code == "FTM_T_SUBTHERAPEUTIC_TROUGH" && a.Severity == DepotSafetyStatus.AdvisoryDrift);

        // 3. FtM Critical hematocrit (>= 54.0%) triggers CriticalSafety and 15% mass down-titration
        TimeReleaseDepotParameters ftmBase = new()
        {
            Regimen = TherapeuticRegimen.FemaleToMale,
            InitialPayloadMassMg = 1000.0,
            TargetHorizonDays = 180,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = 0.55,
            InitialEstradiolPmolL = 500.0,
            InitialTestosteroneNmolL = 1.5,
            TargetEstradiolPmolL = 100.0,
            TargetTestosteroneNmolL = 20.0
        };
        DepotCalibrationResult ftmCritResult = _calibrationService.CalibrateDepot(new DepotCalibrationRequest
        {
            DepotId = "depot_ftm_crit",
            PatientId = "patient_ftm_crit",
            BaselineParameters = ftmBase,
            Observations =
            [
                new DepotAssayObservation
                {
                    DaySinceImplant = 60,
                    ObservedE2PmolL = 95.0,
                    ObservedTNmolL = 24.0,
                    ObservedLhIuL = 0.4,
                    ObservedFshIuL = 0.6,
                    ObservedHematocritPct = 55.2
                }
            ]
        });
        Assert.Equal(DepotSafetyStatus.CriticalSafety, ftmCritResult.OverallSafetyStatus);
        Assert.Contains(ftmCritResult.Alerts, a => a.Code == "FTM_HEMATOCRIT_CRITICAL");
        Assert.True(ftmCritResult.RecommendedNextMassMg < ftmBase.InitialPayloadMassMg);

        // 4. MtF E2 warning (>= 1100), E2 critical (>= 1500), and E2 sub-therapeutic trough (< 350)
        IReadOnlyList<DepotSafetyAlert> mtfAlerts = _calibrationService.EvaluateSafetyGuardrails(
            TherapeuticRegimen.MaleToFemale,
            [
                new DepotAssayObservation { DaySinceImplant = 30, ObservedE2PmolL = 1200.0, ObservedLhIuL = 0.3, ObservedFshIuL = 0.5, ObservedHematocritPct = 40.0 },
                new DepotAssayObservation { DaySinceImplant = 45, ObservedE2PmolL = 1600.0, ObservedLhIuL = 0.2, ObservedFshIuL = 0.4, ObservedHematocritPct = 40.0 },
                new DepotAssayObservation { DaySinceImplant = 150, ObservedE2PmolL = 310.0, ObservedLhIuL = 0.9, ObservedFshIuL = 1.1, ObservedHematocritPct = 40.0 }
            ]);
        Assert.Contains(mtfAlerts, a => a.Code == "MTF_E2_WARNING" && a.Severity == DepotSafetyStatus.WarningEscape);
        Assert.Contains(mtfAlerts, a => a.Code == "MTF_E2_CRITICAL" && a.Severity == DepotSafetyStatus.CriticalSafety);
        Assert.Contains(mtfAlerts, a => a.Code == "MTF_E2_SUBTHERAPEUTIC_TROUGH" && a.Severity == DepotSafetyStatus.AdvisoryDrift);
    }
}
