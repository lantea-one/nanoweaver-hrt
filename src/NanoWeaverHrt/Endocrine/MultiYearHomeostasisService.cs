using System.Globalization;
using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Deterministic systems-biology engine coupling 180-day polymer depot elution with permanent epigenetic
/// steroidogenic reprogramming across multi-year mitotic tissue turnover cycles.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IMultiYearHomeostasisService"/> performing
/// zero-LINQ 4th-order Runge-Kutta (RK4) daily integration over 1 to 10 years (365 to 3,650 days).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P226): Verifies that exogenous nanocarrier taper seamlessly hands off
/// hormone maintenance to epigenetically reprogrammed cells around Days 90–180 and flags mitotic drift early.
/// </para>
/// <para>
/// <strong>How it works:</strong> Models quiescent and active proliferating progenitor methylation compartments,
/// cooperative Hill chromatin self-reinforcement, Day 90–180 aptamer-attenuated hydrogel handoff, HPG gonadotropin
/// feedback, atomic repository persistence, and 21 CFR Part 11 audit ledger recording.
/// </para>
/// </remarks>
public sealed class MultiYearHomeostasisService : IMultiYearHomeostasisService
{
    /// <summary>
    /// Repository supplying parent semi-annual depot formulations.
    /// </summary>
    private readonly IDepotFormulationRepository _depotRepository;

    /// <summary>
    /// Repository supplying optional patient-linked epigenetic toggle circuits.
    /// </summary>
    private readonly IEpigeneticCircuitRepository _circuitRepository;

    /// <summary>
    /// Repository persisting multi-year homeostasis runs and annual checkpoints.
    /// </summary>
    private readonly IMultiYearHomeostasisRepository _homeostasisRepository;

    /// <summary>
    /// 180-day time-release depot kinetic simulator providing exogenous hydrogel baseline drive.
    /// </summary>
    private readonly ITimeReleaseDepotService _depotService;

    /// <summary>
    /// FDA 21 CFR Part 11 tamper-evident compliance audit ledger service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Deterministic UTC timestamp provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiYearHomeostasisService"/> class.
    /// </summary>
    /// <param name="depotRepository">Repository reading semi-annual depot formulations.</param>
    /// <param name="circuitRepository">Repository reading linked epigenetic toggle circuits.</param>
    /// <param name="homeostasisRepository">Repository persisting multi-year forecast runs and checkpoints.</param>
    /// <param name="depotService">180-day time-release depot simulator.</param>
    /// <param name="auditService">21 CFR Part 11 compliance audit service.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for unit testing.</param>
    public MultiYearHomeostasisService(
        IDepotFormulationRepository depotRepository,
        IEpigeneticCircuitRepository circuitRepository,
        IMultiYearHomeostasisRepository homeostasisRepository,
        ITimeReleaseDepotService depotService,
        IComplianceAuditService auditService,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Guards and stores injected depot, circuit, persistence, simulation, and compliance dependencies.
        // WHY:  Prevents silent null dereferences across multi-year longitudinal forecasting workflows.
        // HOW:  Validates all constructor parameters upfront with ArgumentNullException.ThrowIfNull.
        ArgumentNullException.ThrowIfNull(depotRepository);
        ArgumentNullException.ThrowIfNull(circuitRepository);
        ArgumentNullException.ThrowIfNull(homeostasisRepository);
        ArgumentNullException.ThrowIfNull(depotService);
        ArgumentNullException.ThrowIfNull(auditService);

        _depotRepository = depotRepository;
        _circuitRepository = circuitRepository;
        _homeostasisRepository = homeostasisRepository;
        _depotService = depotService;
        _auditService = auditService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<MultiYearHomeostasisForecastReport?> SimulateAndPersistAsync(
        string depotId,
        SimulateMultiYearHomeostasisRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates request boundaries, couples 180-day depot kinetics with 1–10y RK4 chromatin memory, persists, and audits.
        // WHY:  Milestone 43 (P226): Produces deterministic permanent homeostasis verification and mitotic washout advisories.
        // HOW:  Loads depot & optional circuit, executes ExecuteCoupledSimulation, writes run+checkpoints, and logs audit event.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        SimulateMultiYearHomeostasisRequest effective = request ?? new SimulateMultiYearHomeostasisRequest();
        ValidateRequest(effective);

        string cleanDepotId = depotId.Trim();
        DepotFormulationRecord? depot = await _depotRepository.GetDepotFormulationByIdAsync(cleanDepotId, cancellationToken).ConfigureAwait(false);
        if (depot is null)
        {
            return null;
        }

        string? cleanCircuitId = string.IsNullOrWhiteSpace(effective.CircuitId) ? null : effective.CircuitId.Trim();
        double circuitDriveFactor = 1.0;
        if (cleanCircuitId is not null)
        {
            EpigeneticCircuitRecord? circuit = await _circuitRepository.GetCircuitByIdAsync(cleanCircuitId, cancellationToken).ConfigureAwait(false);
            if (circuit is null)
            {
                throw new ArgumentException($"Linked epigenetic circuit '{cleanCircuitId}' was not found.", nameof(request));
            }

            if (!string.Equals(circuit.PatientId, depot.PatientId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Linked epigenetic circuit '{cleanCircuitId}' belongs to a different patient.", nameof(request));
            }

            if (circuit.CircuitStatus == EpigeneticCircuitStatus.Blocked)
            {
                throw new ArgumentException($"Linked epigenetic circuit '{cleanCircuitId}' is Blocked and cannot drive permanent homeostasis.", nameof(request));
            }

            circuitDriveFactor = ComputeCircuitDriveFactor(circuit.SeparatrixMargin, circuit.EffectorPlatform);
        }

        string runId = $"myh_{Guid.NewGuid():N}"[..20];
        DateTimeOffset createdAt = NormalizeToMicroseconds(_utcNow());

        SimulatedTrajectoryBundle bundle = ExecuteCoupledSimulation(
            runId,
            depot,
            effective,
            circuitDriveFactor);

        MultiYearHomeostasisRunRecord runRecord = new()
        {
            RunId = runId,
            DepotId = depot.DepotId,
            CircuitId = cleanCircuitId,
            PatientId = depot.PatientId,
            Regimen = depot.Regimen,
            HorizonYears = effective.HorizonYears,
            MitoticFidelityRate = effective.MitoticFidelityRate,
            TransducedProgenitorFraction = effective.TransducedProgenitorFraction,
            ProjectedTenYearTtrPercent = bundle.ProjectedTenYearTtrPercent,
            DurabilityStatus = bundle.DurabilityStatus,
            CreatedAtUtc = createdAt
        };

        await _homeostasisRepository.SaveForecastRunAsync(runRecord, bundle.Checkpoints, cancellationToken).ConfigureAwait(false);

        string cleanOperator = operatorId.Trim();
        _auditService.LogAction(
            cleanOperator,
            "DEPOT_MULTIYEAR_SIMULATION_RUN",
            $"depot/{depot.DepotId}/multiyear/{runId}",
            JsonSerializer.Serialize(new
            {
                runId,
                depotId = depot.DepotId,
                circuitId = cleanCircuitId,
                patientId = depot.PatientId,
                horizonYears = effective.HorizonYears,
                mitoticFidelityRate = effective.MitoticFidelityRate,
                transducedProgenitorFraction = effective.TransducedProgenitorFraction,
                projectedTenYearTtrPercent = bundle.ProjectedTenYearTtrPercent,
                durabilityStatus = bundle.DurabilityStatus.ToString(),
                mitoticDriftAlert = bundle.MitoticDriftAlert
            }));

        return new MultiYearHomeostasisForecastReport
        {
            RunId = runId,
            DepotId = depot.DepotId,
            CircuitId = cleanCircuitId,
            PatientId = depot.PatientId,
            Regimen = depot.Regimen,
            HorizonYears = effective.HorizonYears,
            SimulatedDays = effective.HorizonYears * 365,
            MitoticFidelityRate = effective.MitoticFidelityRate,
            TransducedProgenitorFraction = effective.TransducedProgenitorFraction,
            ProjectedTenYearTtrPercent = bundle.ProjectedTenYearTtrPercent,
            HandoffWindowTtrPercent = bundle.HandoffWindowTtrPercent,
            HandoffTroughDropDetected = bundle.HandoffTroughDropDetected,
            FinalMethylatedProgenitorFraction = bundle.FinalMethylatedProgenitorFraction,
            WashoutOnsetYear = bundle.WashoutOnsetYear,
            DurabilityStatus = bundle.DurabilityStatus,
            MitoticDriftAlert = bundle.MitoticDriftAlert,
            RequiresBoosterDose = bundle.RequiresBoosterDose,
            RecommendedIntervention = bundle.RecommendedIntervention,
            AnnualCheckpoints = bundle.Checkpoints,
            TrajectorySamples = bundle.TrajectorySamples,
            CreatedAtUtc = createdAt
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MultiYearHomeostasisForecastReport>> ListForecastsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads all multi-year forecast runs for a depot and reconstructs full trajectory reports.
        // WHY:  Powers chronological durability history queries without bloating database checkpoint storage.
        // HOW:  Fetches runs and depot formulation once, then hydrates each run via HydratePersistedRunAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        string cleanDepotId = depotId.Trim();
        IReadOnlyList<MultiYearHomeostasisRunRecord> runs = await _homeostasisRepository.ListRunsByDepotAsync(cleanDepotId, cancellationToken).ConfigureAwait(false);
        if (runs.Count == 0)
        {
            return [];
        }

        DepotFormulationRecord? depot = await _depotRepository.GetDepotFormulationByIdAsync(cleanDepotId, cancellationToken).ConfigureAwait(false);
        List<MultiYearHomeostasisForecastReport> reports = new(runs.Count);
        for (int i = 0; i < runs.Count; i++)
        {
            reports.Add(await HydratePersistedRunAsync(runs[i], depot, cancellationToken).ConfigureAwait(false));
        }

        return reports;
    }

    /// <inheritdoc/>
    public async Task<MultiYearHomeostasisForecastReport?> GetLatestForecastByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads the single most recent persisted multi-year forecast report for a depot.
        // WHY:  Powers GET /api/depots/{depotId}/multiyear-forecasts/latest and native ViewModel refresh.
        // HOW:  Queries GetLatestRunByDepotAsync, returns null if missing, otherwise hydrates via HydratePersistedRunAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        string cleanDepotId = depotId.Trim();
        MultiYearHomeostasisRunRecord? latest = await _homeostasisRepository.GetLatestRunByDepotAsync(cleanDepotId, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return null;
        }

        DepotFormulationRecord? depot = await _depotRepository.GetDepotFormulationByIdAsync(cleanDepotId, cancellationToken).ConfigureAwait(false);
        return await HydratePersistedRunAsync(latest, depot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hydrates a complete <see cref="MultiYearHomeostasisForecastReport"/> from a stored run header and checkpoints.
    /// </summary>
    /// <param name="run">Persisted forecast run record.</param>
    /// <param name="depot">Parent depot formulation if still present; otherwise fallback synthetic formulation matching <paramref name="run"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reconstructed <see cref="MultiYearHomeostasisForecastReport"/>.</returns>
    private async Task<MultiYearHomeostasisForecastReport> HydratePersistedRunAsync(
        MultiYearHomeostasisRunRecord run,
        DepotFormulationRecord? depot,
        CancellationToken cancellationToken)
    {
        // WHAT: Rebuilds handoff metrics and chart trajectory samples deterministically from persisted run metadata and checkpoints.
        // WHY:  Ensures historical GET requests return complete SVG curves, advisories, and checkpoint tables.
        // HOW:  Re-runs deterministic RK4 model using stored scalar parameters while preserving stored checkpoints and verdict.
        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> storedCheckpoints =
            await _homeostasisRepository.GetCheckpointsByRunAsync(run.RunId, cancellationToken).ConfigureAwait(false);

        DepotFormulationRecord effectiveDepot = depot ?? new DepotFormulationRecord
        {
            DepotId = run.DepotId,
            PatientId = run.PatientId,
            Regimen = run.Regimen,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = run.Regimen == TherapeuticRegimen.FemaleToMale ? 1025.0 : 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = run.CreatedAtUtc
        };

        double circuitDrive = 1.0;
        if (!string.IsNullOrWhiteSpace(run.CircuitId))
        {
            EpigeneticCircuitRecord? circuit = await _circuitRepository.GetCircuitByIdAsync(run.CircuitId, cancellationToken).ConfigureAwait(false);
            if (circuit is not null && circuit.CircuitStatus != EpigeneticCircuitStatus.Blocked)
            {
                circuitDrive = ComputeCircuitDriveFactor(circuit.SeparatrixMargin, circuit.EffectorPlatform);
            }
        }

        SimulateMultiYearHomeostasisRequest reconstructedReq = new()
        {
            CircuitId = run.CircuitId,
            HorizonYears = Math.Clamp(run.HorizonYears, MultiYearHomeostasisThresholds.MinHorizonYears, MultiYearHomeostasisThresholds.MaxHorizonYears),
            MitoticFidelityRate = Math.Clamp(run.MitoticFidelityRate, MultiYearHomeostasisThresholds.MinMitoticFidelityRate, MultiYearHomeostasisThresholds.MaxMitoticFidelityRate),
            TransducedProgenitorFraction = Math.Clamp(run.TransducedProgenitorFraction, 0.10, 1.0)
        };

        SimulatedTrajectoryBundle hydrated = ExecuteCoupledSimulation(
            run.RunId,
            effectiveDepot,
            reconstructedReq,
            circuitDrive);

        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> checkpointsToUse = storedCheckpoints.Count > 0
            ? storedCheckpoints
            : hydrated.Checkpoints;
        double finalProgenitor = storedCheckpoints.Count > 0
            ? storedCheckpoints[^1].MethylatedProgenitorFraction
            : hydrated.FinalMethylatedProgenitorFraction;
        bool driftAlert = run.DurabilityStatus == MultiYearDurabilityStatus.MitoticDriftAdvisory || hydrated.MitoticDriftAlert;

        return new MultiYearHomeostasisForecastReport
        {
            RunId = run.RunId,
            DepotId = run.DepotId,
            CircuitId = run.CircuitId,
            PatientId = run.PatientId,
            Regimen = run.Regimen,
            HorizonYears = run.HorizonYears,
            SimulatedDays = run.HorizonYears * 365,
            MitoticFidelityRate = run.MitoticFidelityRate,
            TransducedProgenitorFraction = run.TransducedProgenitorFraction,
            ProjectedTenYearTtrPercent = run.ProjectedTenYearTtrPercent,
            HandoffWindowTtrPercent = hydrated.HandoffWindowTtrPercent,
            HandoffTroughDropDetected = hydrated.HandoffTroughDropDetected,
            FinalMethylatedProgenitorFraction = finalProgenitor,
            WashoutOnsetYear = hydrated.WashoutOnsetYear,
            DurabilityStatus = run.DurabilityStatus,
            MitoticDriftAlert = driftAlert,
            RequiresBoosterDose = driftAlert,
            RecommendedIntervention = hydrated.RecommendedIntervention,
            AnnualCheckpoints = checkpointsToUse,
            TrajectorySamples = hydrated.TrajectorySamples,
            CreatedAtUtc = run.CreatedAtUtc
        };
    }

    /// <summary>
    /// Executes the deterministic zero-LINQ coupled 180-day hydrogel + multi-year RK4 mitotic durability solver.
    /// </summary>
    /// <param name="runId">Identifier assigned to this simulation run for checkpoint keys.</param>
    /// <param name="depot">Baseline semi-annual polymer depot formulation.</param>
    /// <param name="request">Validated cellular turnover, fidelity, and clearance parameters.</param>
    /// <param name="circuitDriveFactor">Endogenous promoter/circuit stability multiplier derived from linked epigenetic circuit.</param>
    /// <returns>Complete computed checkpoints, trajectory samples, and clinical durability classifications.</returns>
    private SimulatedTrajectoryBundle ExecuteCoupledSimulation(
        string runId,
        DepotFormulationRecord depot,
        SimulateMultiYearHomeostasisRequest request,
        double circuitDriveFactor)
    {
        // WHAT: Couples Days 0-180 TimeReleaseDepotService kinetics with 1-10y RK4 quiescent+proliferating chromatin dynamics.
        // WHY:  Captures seamless hydrogel-to-endogenous handoff, mitotic dilution loss, and HPG feedback without LINQ allocations.
        // HOW:  Integrates RK4 step daily over totalDays, records annual checkpoints and downsampled curve points, and evaluates durability.
        TimeReleaseDepotParameters baselineParams = DepotCalibrationMapper.ToBaselineParameters(depot) with
        {
            TargetHorizonDays = 180,
            ClearanceMultiplier = request.ClearanceMultiplier
        };
        TimeReleaseSimulationResult baseline180 = _depotService.Simulate180DayDepot(baselineParams);

        int totalDays = request.HorizonYears * 365;
        double qFrac = request.QuiescentStemCellFraction;
        double q = request.TransducedProgenitorFraction;
        double p = request.TransducedProgenitorFraction;

        double lambdaP = 1.0 / request.TissueDivisionIntervalDays;
        double lambdaQ = 0.35 / request.TissueDivisionIntervalDays;
        double gamma = 0.00055 * request.PromoterReinforcementStrength * circuitDriveFactor;
        double cap = Math.Min(0.95, request.TransducedProgenitorFraction + 0.02);

        List<MultiYearHomeostasisCheckpointRecord> checkpoints = new(request.HorizonYears + 1);
        List<MultiYearTrajectoryPoint> samples = new(32);

        double estradiol = baselineParams.InitialEstradiolPmolL;
        double testosterone = baselineParams.InitialTestosteroneNmolL;
        double lh = baselineParams.InitialLhIuL;
        double fsh = baselineParams.InitialFshIuL;

        double weightedM0 = (qFrac * q) + ((1.0 - qFrac) * p);
        checkpoints.Add(BuildCheckpoint(runId, depot.PatientId, 0, estradiol, testosterone, lh, fsh, weightedM0, 1.0));
        samples.Add(new MultiYearTrajectoryPoint
        {
            Day = 0,
            YearFraction = 0.0,
            EstradiolPmolL = Round2(estradiol),
            TestosteroneNmolL = Round2(testosterone),
            LhIuL = Round2(lh),
            FshIuL = Round2(fsh),
            HydrogelContributionFraction = 1.0,
            EndogenousContributionFraction = 0.0,
            MethylatedProgenitorFraction = Round4(weightedM0),
            InTargetRange = IsHormoneInTargetRange(depot.Regimen, estradiol, testosterone)
        });

        int daysInTarget = 0;
        int handoffDaysTotal = 0;
        int handoffDaysInTarget = 0;
        double? washoutOnsetYear = null;
        double lastExo180 = 0.0;
        double clearanceHormoneScale = 1.0 / (1.0 + (0.06 * (request.ClearanceMultiplier - 1.0)));

        // WHAT: Steps daily over [1..totalDays] using 4th-order Runge-Kutta for epigenetic chromatin compartments.
        // WHY:  Maintains numerical accuracy across thousands of days of coupled quiescent/proliferating cell divisions.
        // HOW:  Computes 4 RK4 slopes for (q, p), couples normalized hydrogel and endogenous steroid drive, and samples outputs.
        for (int day = 1; day <= totalDays; day++)
        {
            AdvanceProgenitorStateRk4(
                ref q,
                ref p,
                qFrac,
                lambdaQ,
                lambdaP,
                request.MitoticFidelityRate,
                gamma,
                cap);

            double weightedM = (qFrac * q) + ((1.0 - qFrac) * p);

            // Smooth logistic chromatin activation from Day 0 through Day 180
            double engraftment = 1.0 / (1.0 + Math.Exp(-0.075 * (day - 115.0)));
            double progenitorCompetence = Math.Min(1.0, weightedM / 0.80);
            double endoDrive = engraftment * progenitorCompetence * Math.Min(1.12, circuitDriveFactor) * clearanceHormoneScale;

            double exoEffective;
            if (day <= 180)
            {
                // Aptamer-mediated self-limiting hydrogel handoff attenuates exogenous contribution as endogenous drive rises
                double rawHydrogelDrive = ComputeNormalizedHydrogelDrive(depot.Regimen, baseline180.Points[day]);
                exoEffective = rawHydrogelDrive * Math.Max(0.0, 1.0 - Math.Min(1.0, endoDrive));
                if (day == 180)
                {
                    lastExo180 = exoEffective;
                }
            }
            else
            {
                exoEffective = lastExo180 * Math.Exp(-0.08 * (day - 180));
            }

            double totalDrive = exoEffective + endoDrive;
            double safeTotalDrive = Math.Max(totalDrive, 1e-9);
            double hydrogelFraction = Math.Clamp(exoEffective / safeTotalDrive, 0.0, 1.0);
            double endogenousFraction = Math.Clamp(1.0 - hydrogelFraction, 0.0, 1.0);

            ComputeSteroidsAndGonadotropins(
                depot.Regimen,
                baselineParams,
                totalDrive,
                ref estradiol,
                ref testosterone,
                ref lh,
                ref fsh);

            bool inTarget = IsHormoneInTargetRange(depot.Regimen, estradiol, testosterone);
            if (inTarget)
            {
                daysInTarget++;
            }

            if (day >= MultiYearHomeostasisThresholds.HandoffStartDay && day <= MultiYearHomeostasisThresholds.HandoffEndDay)
            {
                handoffDaysTotal++;
                if (inTarget)
                {
                    handoffDaysInTarget++;
                }
            }

            if (!washoutOnsetYear.HasValue && day > 180 && (weightedM < MultiYearHomeostasisThresholds.MinimumProgenitorFractionFloor || !inTarget))
            {
                washoutOnsetYear = Round2(day / 365.0);
            }

            if (day % 365 == 0)
            {
                int yearIndex = day / 365;
                checkpoints.Add(BuildCheckpoint(runId, depot.PatientId, yearIndex, estradiol, testosterone, lh, fsh, weightedM, hydrogelFraction));
            }

            if (IsChartSampleDay(day, totalDays))
            {
                samples.Add(new MultiYearTrajectoryPoint
                {
                    Day = day,
                    YearFraction = Round2(day / 365.0),
                    EstradiolPmolL = Round2(estradiol),
                    TestosteroneNmolL = Round2(testosterone),
                    LhIuL = Round2(lh),
                    FshIuL = Round2(fsh),
                    HydrogelContributionFraction = Round4(hydrogelFraction),
                    EndogenousContributionFraction = Round4(endogenousFraction),
                    MethylatedProgenitorFraction = Round4(weightedM),
                    InTargetRange = inTarget
                });
            }
        }

        double projectedTtr = Round2((daysInTarget / (double)totalDays) * 100.0);
        double handoffTtr = Round2((handoffDaysInTarget / (double)handoffDaysTotal) * 100.0);
        double finalProgenitor = Round4((qFrac * q) + ((1.0 - qFrac) * p));

        bool handoffTroughDrop = handoffTtr < MultiYearHomeostasisThresholds.RequiredTenYearTtrPercent;
        bool driftAlert =
            projectedTtr < MultiYearHomeostasisThresholds.RequiredTenYearTtrPercent ||
            finalProgenitor < MultiYearHomeostasisThresholds.MinimumProgenitorFractionFloor ||
            handoffTroughDrop ||
            washoutOnsetYear.HasValue;

        MultiYearDurabilityStatus status = driftAlert
            ? MultiYearDurabilityStatus.MitoticDriftAdvisory
            : MultiYearDurabilityStatus.PermanentHomeostasisVerified;

        string intervention = BuildClinicalRecommendation(
            driftAlert,
            handoffTroughDrop,
            washoutOnsetYear,
            finalProgenitor,
            projectedTtr,
            request.HorizonYears);

        return new SimulatedTrajectoryBundle(
            projectedTtr,
            handoffTtr,
            handoffTroughDrop,
            finalProgenitor,
            washoutOnsetYear,
            status,
            driftAlert,
            driftAlert,
            intervention,
            checkpoints,
            samples);
    }

    /// <summary>
    /// Advances quiescent (<paramref name="q"/>) and proliferating (<paramref name="p"/>) methylated progenitor fractions
    /// by one discrete day using 4th-order Runge-Kutta integration.
    /// </summary>
    /// <param name="q">Quiescent stem-niche progenitor fraction (updated in place).</param>
    /// <param name="p">Actively dividing steroidogenic progenitor fraction (updated in place).</param>
    /// <param name="qFrac">Stem-niche weighting fraction.</param>
    /// <param name="lambdaQ">Daily cell-division rate in the quiescent niche.</param>
    /// <param name="lambdaP">Daily cell-division rate in the proliferating pool.</param>
    /// <param name="fidelity">DNMT1 maintenance methylation fidelity per division.</param>
    /// <param name="gamma">Effective cooperative promoter chromatin self-reinforcement rate.</param>
    /// <param name="cap">Maximum achievable transduced chromatin ceiling.</param>
    private static void AdvanceProgenitorStateRk4(
        ref double q,
        ref double p,
        double qFrac,
        double lambdaQ,
        double lambdaP,
        double fidelity,
        double gamma,
        double cap)
    {
        // WHAT: Performs one classical 4-stage RK4 time step on the coupled (q, p) chromatin memory ODEs.
        // WHY:  Prevents integration truncation error over multi-year 3,650-day simulations.
        // HOW:  Evaluates EvaluateDerivatives at k1, k2, k3, k4 stages and clamps resulting state fractions to [0, 1].
        (double k1q, double k1p) = EvaluateDerivatives(q, p, qFrac, lambdaQ, lambdaP, fidelity, gamma, cap);
        (double k2q, double k2p) = EvaluateDerivatives(q + (0.5 * k1q), p + (0.5 * k1p), qFrac, lambdaQ, lambdaP, fidelity, gamma, cap);
        (double k3q, double k3p) = EvaluateDerivatives(q + (0.5 * k2q), p + (0.5 * k2p), qFrac, lambdaQ, lambdaP, fidelity, gamma, cap);
        (double k4q, double k4p) = EvaluateDerivatives(q + k3q, p + k3p, qFrac, lambdaQ, lambdaP, fidelity, gamma, cap);

        q = Math.Clamp(q + ((k1q + (2.0 * k2q) + (2.0 * k3q) + k4q) / 6.0), 0.0, 1.0);
        p = Math.Clamp(p + ((k1p + (2.0 * k2p) + (2.0 * k3p) + k4p) / 6.0), 0.0, 1.0);
    }

    /// <summary>
    /// Evaluates time derivatives <c>dq/dt</c> and <c>dp/dt</c> balancing mitotic DNMT1 dilution loss
    /// against Hill-cooperative chromatin self-reinforcement and asymmetric stem replenishment.
    /// </summary>
    /// <param name="q">Current quiescent progenitor methylation fraction.</param>
    /// <param name="p">Current proliferating progenitor methylation fraction.</param>
    /// <param name="qFrac">Quiescent stem niche coupling weight.</param>
    /// <param name="lambdaQ">Quiescent daily cell division frequency.</param>
    /// <param name="lambdaP">Proliferating daily cell division frequency.</param>
    /// <param name="fidelity">DNMT1 maintenance fidelity per mitosis.</param>
    /// <param name="gamma">Effective cooperative chromatin reinforcement constant.</param>
    /// <param name="cap">Transduction chromatin ceiling.</param>
    /// <returns>Pair <c>(dq, dp)</c> representing instantaneous daily rates of change.</returns>
    private static (double Dq, double Dp) EvaluateDerivatives(
        double q,
        double p,
        double qFrac,
        double lambdaQ,
        double lambdaP,
        double fidelity,
        double gamma,
        double cap)
    {
        // WHAT: Computes instantaneous daily rates of change for quiescent (q) and active proliferating (p) chromatin pools.
        // WHY:  Captures the bifurcation between self-reinforcing memory (high F) and mitotic washout (low F).
        // HOW:  Subtracts division-rate * (1 - fidelity) loss from cooperative cubic-Hill reinforcement toward cap.
        double dilution = 1.0 - fidelity;
        double lossQ = lambdaQ * dilution * q;
        double lossP = lambdaP * dilution * p;

        const double K3 = 0.62 * 0.62 * 0.62;
        double q3 = q * q * q;
        double p3 = p * p * p;
        double hillQ = q3 / (K3 + q3);
        double hillP = p3 / (K3 + p3);

        double reinforceQ = gamma * hillQ * Math.Max(0.0, cap - q);
        double reinforceP = gamma * hillP * Math.Max(0.0, cap - p);

        double dq = reinforceQ - lossQ;
        double dp = reinforceP - lossP + (0.00020 * qFrac * (q - p));
        return (dq, dp);
    }

    /// <summary>
    /// Derives a circuit stability multiplier from an approved or evaluated linked epigenetic toggle circuit.
    /// </summary>
    /// <param name="separatrixMargin">Bistable phase-plane separatrix margin computed in Milestone 42.</param>
    /// <param name="effector">dCas9 epigenetic effector platform architecture.</param>
    /// <returns>Clamped stability multiplier in [0.85, 1.25].</returns>
    private static double ComputeCircuitDriveFactor(double separatrixMargin, EpigeneticEffectorPlatform effector)
    {
        // WHAT: Scales endogenous chromatin durability by effector persistence class and bistable separatrix depth.
        // WHY:  CRISPRoff/on hit-and-run memory outperforms transient KRAB complexes in multi-year turnover simulations.
        // HOW:  Adds normalized margin bonus to effector baseline factor and clamps to [0.85, 1.25].
        double platformBase = effector switch
        {
            EpigeneticEffectorPlatform.CrisprOffOn => 1.05,
            EpigeneticEffectorPlatform.DCas9Dnmt3aTet1 => 1.00,
            _ => 0.90
        };
        double bonus = Math.Clamp(separatrixMargin, 0.0, 0.8) * 0.15;
        return Math.Clamp(platformBase + bonus, 0.85, 1.25);
    }

    /// <summary>
    /// Normalizes a 180-day exogenous depot daily hormone state to a dimensionless drive ratio around target setpoint.
    /// </summary>
    /// <param name="regimen">Target endocrine regimen.</param>
    /// <param name="point">180-day baseline daily state from <see cref="ITimeReleaseDepotService"/>.</param>
    /// <returns>Normalized drive ratio (approximately 1.0 during steady-state plateau).</returns>
    private static double ComputeNormalizedHydrogelDrive(TherapeuticRegimen regimen, TimeReleaseDailyPoint point) => regimen switch
    {
        TherapeuticRegimen.MaleToFemale => point.EstradiolPmolL / 550.0,
        TherapeuticRegimen.FemaleToMale => point.TestosteroneNmolL / 22.5,
        _ => Math.Max(point.EstradiolPmolL / 360.0, point.TestosteroneNmolL / 12.0)
    };

    /// <summary>
    /// Maps total coupled (exogenous + endogenous) normalized steroidogenic drive into smoothed circulating E2, T, LH, and FSH.
    /// </summary>
    /// <param name="regimen">Target therapeutic endocrine regimen.</param>
    /// <param name="baselineParams">Baseline depot pharmacokinetic parameters.</param>
    /// <param name="totalDrive">Normalized combined exogenous + endogenous steroidogenic drive.</param>
    /// <param name="estradiol">Circulating 17β-estradiol in pmol/L (updated in place).</param>
    /// <param name="testosterone">Circulating total testosterone in nmol/L (updated in place).</param>
    /// <param name="lh">Circulating LH in IU/L (updated in place).</param>
    /// <param name="fsh">Circulating FSH in IU/L (updated in place).</param>
    private static void ComputeSteroidsAndGonadotropins(
        TherapeuticRegimen regimen,
        TimeReleaseDepotParameters baselineParams,
        double totalDrive,
        ref double estradiol,
        ref double testosterone,
        ref double lh,
        ref double fsh)
    {
        // WHAT: Updates daily serum steroids and Hill HPG gonadotropins from total coupled drive.
        // WHY:  Reflects physiological smoothing (~1.5 day steroid half-life) and reciprocal LH/FSH suppression.
        // HOW:  Resolves regimen setpoint targets, applies alpha/beta smoothing, and computes Hill inhibition ratios.
        double clampedDrive = Math.Clamp(totalDrive, 0.0, 1.4);
        double targetE2;
        double targetT;

        if (regimen == TherapeuticRegimen.MaleToFemale)
        {
            targetE2 = clampedDrive * 550.0;
            targetT = 0.9 + (0.6 * Math.Max(0.0, 1.0 - clampedDrive));
        }
        else if (regimen == TherapeuticRegimen.FemaleToMale)
        {
            targetT = clampedDrive * 22.5;
            targetE2 = 75.0 + (45.0 * Math.Max(0.0, 1.0 - clampedDrive));
        }
        else
        {
            targetE2 = clampedDrive * 360.0;
            targetT = clampedDrive * 12.0;
        }

        estradiol += 0.55 * (targetE2 - estradiol);
        testosterone += 0.55 * (targetT - testosterone);

        double lhDenom = 1.0 + Math.Pow(estradiol / 180.0, 2.0) + Math.Pow(testosterone / 3.5, 2.0);
        double fshDenom = 1.0 + Math.Pow(estradiol / 220.0, 1.5) + Math.Pow(testosterone / 5.0, 1.5);
        double targetLh = baselineParams.InitialLhIuL / lhDenom;
        double targetFsh = baselineParams.InitialFshIuL / fshDenom;

        lh += 0.45 * (targetLh - lh);
        fsh += 0.45 * (targetFsh - fsh);
    }

    /// <summary>
    /// Evaluates whether systemic serum hormone levels fall inside clinical physiological target ranges.
    /// </summary>
    /// <param name="regimen">Target therapeutic endocrine regimen.</param>
    /// <param name="estradiol">Serum 17β-estradiol in pmol/L.</param>
    /// <param name="testosterone">Serum total testosterone in nmol/L.</param>
    /// <returns><see langword="true"/> when inside target clinical range; otherwise <see langword="false"/>.</returns>
    private static bool IsHormoneInTargetRange(TherapeuticRegimen regimen, double estradiol, double testosterone) => regimen switch
    {
        TherapeuticRegimen.MaleToFemale => estradiol >= 400.0 && estradiol <= 700.0,
        TherapeuticRegimen.FemaleToMale => testosterone >= 15.0 && testosterone <= 30.0,
        _ => estradiol >= 200.0 || testosterone >= 10.0
    };

    /// <summary>
    /// Determines whether an elapsed simulation day should be retained in the SVG downsampled chart series.
    /// </summary>
    /// <param name="day">Elapsed simulation day (1..totalDays).</param>
    /// <param name="totalDays">Total simulated days.</param>
    /// <returns><see langword="true"/> when this day captures key handoff or semi-annual curvature.</returns>
    private static bool IsChartSampleDay(int day, int totalDays)
    {
        // WHAT: Retains dense samples across Day 0-180 hydrogel handoff and semi-annual samples through Year 10.
        // WHY:  Keeps JSON payloads and browser SVG DOM compact (~40 points) while showing exact Day 90-180 handoff inflection.
        // HOW:  Matches fixed handoff milestones (15, 30, 60, 90, 120, 150, 180) and every ~182/365 days plus final day.
        if (day is 15 or 30 or 60 or 90 or 120 or 150 or 180)
        {
            return true;
        }

        return day > 180 && (day % 365 == 0 || day % 365 == 182 || day == totalDays);
    }

    /// <summary>
    /// Constructs a rounded annual checkpoint record for database persistence.
    /// </summary>
    /// <param name="runId">Parent forecast run identifier.</param>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="yearIndex">Elapsed year index (0 through HorizonYears).</param>
    /// <param name="estradiol">Serum 17β-estradiol in pmol/L.</param>
    /// <param name="testosterone">Serum total testosterone in nmol/L.</param>
    /// <param name="lh">Serum LH in IU/L.</param>
    /// <param name="fsh">Serum FSH in IU/L.</param>
    /// <param name="methylatedFraction">Retained methylated progenitor fraction.</param>
    /// <param name="hydrogelFraction">Residual exogenous hydrogel contribution fraction.</param>
    /// <returns>Populated <see cref="MultiYearHomeostasisCheckpointRecord"/>.</returns>
    private static MultiYearHomeostasisCheckpointRecord BuildCheckpoint(
        string runId,
        string patientId,
        int yearIndex,
        double estradiol,
        double testosterone,
        double lh,
        double fsh,
        double methylatedFraction,
        double hydrogelFraction)
    {
        // WHAT: Allocates an annual checkpoint record with deterministic ID and rounded metrics.
        // WHY:  Provides consistent multi-year summary snapshots for persistence and UI table rendering.
        // HOW:  Formats CheckpointId from runId and yearIndex and rounds floating-point columns deterministically.
        return new MultiYearHomeostasisCheckpointRecord
        {
            CheckpointId = $"{runId}_y{yearIndex:D2}",
            RunId = runId,
            PatientId = patientId,
            YearIndex = yearIndex,
            EstradiolPmolL = Round2(estradiol),
            TestosteroneNmolL = Round2(testosterone),
            LhIuL = Round2(lh),
            FshIuL = Round2(fsh),
            MethylatedProgenitorFraction = Round4(methylatedFraction),
            HydrogelContributionFraction = Round4(hydrogelFraction)
        };
    }

    /// <summary>
    /// Synthesizes a plain-English clinical intervention summary from multi-year simulation outcomes.
    /// </summary>
    /// <param name="driftAlert">True when any durability gate failed.</param>
    /// <param name="handoffTrough">True when Day 90–180 handoff showed sub-therapeutic hormone levels.</param>
    /// <param name="washoutOnsetYear">Optional year where drift began.</param>
    /// <param name="finalProgenitor">Retained methylated progenitor fraction at horizon end.</param>
    /// <param name="projectedTtr">Overall TTR percentage across the horizon.</param>
    /// <param name="horizonYears">Total simulated horizon in years.</param>
    /// <returns>Clinician-readable summary and booster recommendation.</returns>
    private static string BuildClinicalRecommendation(
        bool driftAlert,
        bool handoffTrough,
        double? washoutOnsetYear,
        double finalProgenitor,
        double projectedTtr,
        int horizonYears)
    {
        // WHAT: Formats an actionable recommendation identifying either conserved homeostasis or specific booster intervention.
        // WHY:  Guides clinicians when Day 90-180 handoff troughs or multi-year mitotic washout threaten efficacy.
        // HOW:  Branches cleanly on handoffTrough and washoutOnsetYear before falling back to conserved permanent verdict.
        if (!driftAlert)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Permanent endogenous homeostasis verified over {0} years (TTR {1:F1}%, retained chromatin memory {2:P1}); no booster dosing required.",
                horizonYears,
                projectedTtr,
                finalProgenitor);
        }

        if (handoffTrough)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Sub-therapeutic Day 90-180 hydrogel-to-epigenetic handoff detected (TTR {0:F1}%, retained memory {1:P1}); administer secondary LNP transducing boost at Day 90 or bridge depot elongation.",
                projectedTtr,
                finalProgenitor);
        }

        double yr = washoutOnsetYear.GetValueOrDefault(horizonYears);
        return string.Format(
            CultureInfo.InvariantCulture,
            "Mitotic chromatin washout advisory beginning Year {0:F2} (TTR {1:F1}%, final progenitor retention {2:P1}); schedule CRISPRoff/on promoter-reinforcement booster.",
            yr,
            projectedTtr,
            finalProgenitor);
    }

    /// <summary>
    /// Validates all numeric and structural constraints on a multi-year homeostasis simulation request.
    /// </summary>
    /// <param name="request">Validated request parameters.</param>
    private static void ValidateRequest(SimulateMultiYearHomeostasisRequest request)
    {
        // WHAT: Enforces closed physical bounds on horizon, DNMT1 fidelity, transduction, stem pool, division interval, clearance, and reinforcement.
        // WHY:  Prevents non-physical probabilities or divergent ODE step sizes before entering the RK4 integrator.
        // HOW:  Checks each parameter with ValidateFiniteRange and throws ArgumentOutOfRangeException on violation.
        if (request.HorizonYears < MultiYearHomeostasisThresholds.MinHorizonYears ||
            request.HorizonYears > MultiYearHomeostasisThresholds.MaxHorizonYears)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.HorizonYears),
                $"HorizonYears must be between {MultiYearHomeostasisThresholds.MinHorizonYears} and {MultiYearHomeostasisThresholds.MaxHorizonYears}.");
        }

        ValidateFiniteRange(request.MitoticFidelityRate, MultiYearHomeostasisThresholds.MinMitoticFidelityRate, MultiYearHomeostasisThresholds.MaxMitoticFidelityRate, nameof(request.MitoticFidelityRate));
        ValidateFiniteRange(request.TransducedProgenitorFraction, 0.10, 1.0, nameof(request.TransducedProgenitorFraction));
        ValidateFiniteRange(request.QuiescentStemCellFraction, 0.0, 0.95, nameof(request.QuiescentStemCellFraction));
        ValidateFiniteRange(request.TissueDivisionIntervalDays, 7.0, 365.0, nameof(request.TissueDivisionIntervalDays));
        ValidateFiniteRange(request.ClearanceMultiplier, 0.1, 5.0, nameof(request.ClearanceMultiplier));
        ValidateFiniteRange(request.PromoterReinforcementStrength, 0.0, 2.0, nameof(request.PromoterReinforcementStrength));
    }

    /// <summary>
    /// Ensures a floating-point parameter is finite and lies inside an inclusive interval.
    /// </summary>
    /// <param name="value">Input value.</param>
    /// <param name="min">Inclusive minimum bound.</param>
    /// <param name="max">Inclusive maximum bound.</param>
    /// <param name="paramName">Parameter name reported on failure.</param>
    private static void ValidateFiniteRange(double value, double min, double max, string paramName)
    {
        // WHAT: Guards against NaN, Infinity, or out-of-range numeric simulation arguments.
        // WHY:  Maintains numerical stability and RFC 7807 400 validation determinism.
        // HOW:  Tests double.IsFinite and inclusive bounds before throwing ArgumentOutOfRangeException.
        if (!double.IsFinite(value) || value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be finite and between {min} and {max}.");
        }
    }

    /// <summary>
    /// Truncates a UTC timestamp to microsecond resolution matching SQLite/PostgreSQL/DuckDB/MySQL persistence.
    /// </summary>
    /// <param name="value">Raw timestamp.</param>
    /// <returns>Microsecond-aligned UTC <see cref="DateTimeOffset"/>.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset value)
    {
        // WHAT: Strips sub-microsecond tick remainder so persisted timestamps round-trip bit-exactly across SQL dialects.
        // WHY:  Prevents microsecond rounding mismatches across PostgreSQL TIMESTAMPTZ, MySQL DATETIME(6), and SQLite text.
        // HOW:  Subtracts modulo-10 ticks in UTC offset 0.
        DateTimeOffset utc = value.ToUniversalTime();
        long alignedTicks = utc.UtcTicks - (utc.UtcTicks % 10L);
        return new DateTimeOffset(alignedTicks, TimeSpan.Zero);
    }

    /// <summary>
    /// Rounds a double value cleanly to 2 decimal places using AwayFromZero midpoint rounding.
    /// </summary>
    /// <param name="value">Input floating-point value.</param>
    /// <returns>Rounded value to 2 decimals.</returns>
    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds a double value cleanly to 4 decimal places using AwayFromZero midpoint rounding.
    /// </summary>
    /// <param name="value">Input floating-point value.</param>
    /// <returns>Rounded value to 4 decimals.</returns>
    private static double Round4(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Internal container bundling raw multi-year simulation outputs prior to run persistence or report hydration.
    /// </summary>
    /// <param name="ProjectedTenYearTtrPercent">Overall TTR across totalDays.</param>
    /// <param name="HandoffWindowTtrPercent">TTR across Days 90–180.</param>
    /// <param name="HandoffTroughDropDetected">True when Day 90–180 fell sub-therapeutic.</param>
    /// <param name="FinalMethylatedProgenitorFraction">Final retained progenitor fraction.</param>
    /// <param name="WashoutOnsetYear">First washout year, if any.</param>
    /// <param name="DurabilityStatus">Aggregate durability classification.</param>
    /// <param name="MitoticDriftAlert">True when drift/trough alert triggers.</param>
    /// <param name="RequiresBoosterDose">True when booster dosing is advised.</param>
    /// <param name="RecommendedIntervention">Clinical guidance text.</param>
    /// <param name="Checkpoints">Persisted annual checkpoints Year 0..HorizonYears.</param>
    /// <param name="TrajectorySamples">Downsampled trajectory points for charts.</param>
    private sealed record SimulatedTrajectoryBundle(
        double ProjectedTenYearTtrPercent,
        double HandoffWindowTtrPercent,
        bool HandoffTroughDropDetected,
        double FinalMethylatedProgenitorFraction,
        double? WashoutOnsetYear,
        MultiYearDurabilityStatus DurabilityStatus,
        bool MitoticDriftAlert,
        bool RequiresBoosterDose,
        string RecommendedIntervention,
        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> Checkpoints,
        IReadOnlyList<MultiYearTrajectoryPoint> TrajectorySamples);
}
