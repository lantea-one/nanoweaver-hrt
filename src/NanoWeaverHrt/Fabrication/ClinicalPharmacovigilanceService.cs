using System.Globalization;
using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Deterministic clinical pharmacovigilance surveillance, CARPA/TLR9 immunogenicity detection,
/// and automated fail-closed lot quarantine escalation engine implementing <see cref="IClinicalPharmacovigilanceService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Zero-LINQ post-implantation safety surveillance engine evaluating CTCAE v5.0
/// clinical grades alongside complement (CH50, C3a, C5a), cytokine (IL-6, TNF-α), anti-PEG IgM, anti-dsDNA IgG,
/// and TLR9 innate signaling biomarkers.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P216): Automatically quarantines implicated wet-lab lots and
/// invalidates Certificate of Analysis trust before severe CARPA hypersensitivity or cytokine storm spreads across patients.
/// </para>
/// <para>
/// <strong>How it works:</strong> Encrypts clinical notes at rest via <see cref="ProfileDataProtector"/>, runs
/// allocation-free threshold passes over persisted logs, transitions affected lots to
/// <see cref="FabricationLotStatus.Quarantined"/>, and records hash-chained 21 CFR Part 11 audit events.
/// </para>
/// </remarks>
public sealed class ClinicalPharmacovigilanceService : IClinicalPharmacovigilanceService
{
    /// <summary>
    /// Repository storing fabrication lots, QC checks, and revocation tombstones.
    /// </summary>
    private readonly IFabricationLotRepository _lotRepository;

    /// <summary>
    /// Repository persisting CTCAE adverse events and CARPA/TLR9 immunogenicity panels.
    /// </summary>
    private readonly IClinicalPharmacovigilanceRepository _pvRepository;

    /// <summary>
    /// AES-256-GCM field protector encrypting optional clinical notes at rest.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// FDA 21 CFR Part 11 hash-chained compliance audit service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Injectable deterministic UTC clock provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClinicalPharmacovigilanceService"/> class.
    /// </summary>
    /// <param name="lotRepository">Wet-lab fabrication lot repository.</param>
    /// <param name="pvRepository">Adverse event and immunogenicity assay repository.</param>
    /// <param name="protector">AES-256-GCM field protector for clinical notes.</param>
    /// <param name="auditService">21 CFR Part 11 compliance audit service.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for testing.</param>
    public ClinicalPharmacovigilanceService(
        IFabricationLotRepository lotRepository,
        IClinicalPharmacovigilanceRepository pvRepository,
        ProfileDataProtector protector,
        IComplianceAuditService auditService,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Validates and stores all required pharmacovigilance dependencies.
        // WHY:  Guarantees fail-fast DI wiring with zero null dereferences during safety escalation.
        // HOW:  Guards every dependency against null and falls back to DateTimeOffset.UtcNow when no test clock is supplied.
        ArgumentNullException.ThrowIfNull(lotRepository);
        ArgumentNullException.ThrowIfNull(pvRepository);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(auditService);

        _lotRepository = lotRepository;
        _pvRepository = pvRepository;
        _protector = protector;
        _auditService = auditService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<AdverseEventResponse?> RecordAdverseEventAsync(
        string lotId,
        RecordAdverseEventRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates and persists a post-implantation CTCAE v5.0 adverse event and evaluates quarantine escalation.
        // WHY:  Milestone 41 (P216): Encrypts sensitive clinician notes at rest and quarantines lots upon Grade >= 3 toxicity.
        // HOW:  Validates grade, symptom, onset, and notes; encrypts notes; persists row; logs audit; runs quarantine escalation.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        if (!Enum.IsDefined(request.SeverityGrade))
        {
            throw new ArgumentException("SeverityGrade must be a valid AdverseEventSeverityGrade value.", nameof(request));
        }

        string symptomCategory = request.SymptomCategory?.Trim() ?? string.Empty;
        if (symptomCategory.Length == 0 || symptomCategory.Length > PharmacovigilanceThresholds.MaxSymptomCategoryLength)
        {
            throw new ArgumentException($"SymptomCategory must be between 1 and {PharmacovigilanceThresholds.MaxSymptomCategoryLength} characters.", nameof(request));
        }

        ValidateFiniteRange(
            request.OnsetHoursPostImplant,
            0.0,
            PharmacovigilanceThresholds.MaxOnsetHoursPostImplant,
            nameof(request.OnsetHoursPostImplant));

        string? cleanNotes = string.IsNullOrWhiteSpace(request.ClinicalNotes) ? null : request.ClinicalNotes.Trim();
        if (cleanNotes is not null && cleanNotes.Length > PharmacovigilanceThresholds.MaxClinicalNotesLength)
        {
            throw new ArgumentException($"ClinicalNotes must not exceed {PharmacovigilanceThresholds.MaxClinicalNotesLength} characters.", nameof(request));
        }

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        string cleanOperator = operatorId.Trim();
        DateTimeOffset recordedAtUtc = NormalizeToMicroseconds(request.RecordedAtUtc ?? _utcNow());
        double roundedOnset = Math.Round(request.OnsetHoursPostImplant, 4, MidpointRounding.AwayFromZero);
        string? encryptedNotes = cleanNotes is null ? null : _protector.EncryptField(cleanNotes);
        string eventId = $"aevt_{Guid.NewGuid():N}"[..24];

        LotAdverseEventRecord record = new()
        {
            EventId = eventId,
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            SeverityGrade = request.SeverityGrade,
            SymptomCategory = symptomCategory,
            OnsetHoursPostImplant = roundedOnset,
            ClinicalNotesEncrypted = encryptedNotes,
            ReportedBy = cleanOperator,
            RecordedAtUtc = recordedAtUtc
        };

        await _pvRepository.AddAdverseEventAsync(record, cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            cleanOperator,
            "FABRICATION_ADVERSE_EVENT_RECORD",
            $"fabrication-lot/{lot.LotId}",
            JsonSerializer.Serialize(new
            {
                eventId = record.EventId,
                lotId = lot.LotId,
                severityGrade = record.SeverityGrade.ToString(),
                symptomCategory = record.SymptomCategory,
                onsetHoursPostImplant = record.OnsetHoursPostImplant
            }));

        LotPharmacovigilanceReport report = await EvaluateAndEscalateLotAsync(lot, cleanOperator, cancellationToken).ConfigureAwait(false);

        LotAdverseEventSummary summary = new()
        {
            EventId = record.EventId,
            LotId = record.LotId,
            PatientId = record.PatientId,
            SeverityGrade = record.SeverityGrade,
            SymptomCategory = record.SymptomCategory,
            OnsetHoursPostImplant = record.OnsetHoursPostImplant,
            ClinicalNotes = cleanNotes,
            ReportedBy = record.ReportedBy,
            RecordedAtUtc = record.RecordedAtUtc
        };

        return new AdverseEventResponse
        {
            Event = summary,
            ReportAfterEvent = report
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LotAdverseEventSummary>?> ListAdverseEventsAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads all chronological post-implantation adverse events for a lot and decrypts clinical notes.
        // WHY:  Authorizes clinicians and patients to audit adverse event timelines without exposing raw AES-256-GCM envelopes.
        // HOW:  Verifies lot existence, queries repository, and decrypts non-null clinical notes via MapAdverseEventSummaries.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        IReadOnlyList<LotAdverseEventRecord> records = await _pvRepository.ListAdverseEventsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        return DecryptAdverseEventSummaries(records);
    }

    /// <inheritdoc/>
    public async Task<ImmunogenicityAssayResponse?> RecordImmunogenicityAssayAsync(
        string lotId,
        RecordImmunogenicityAssayRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates and persists a quantitative CARPA/TLR9 immunogenicity panel and triggers auto-quarantine if critical.
        // WHY:  Milestone 41 (P216): Detects subclinical complement activation, cytokine release, or anti-DNA immunity early.
        // HOW:  Validates biomarker physical bounds, rounds values, inserts row, audits, and executes quarantine escalation.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        ValidateFiniteRange(request.Ch50Percent, PharmacovigilanceThresholds.MinCh50Percent, PharmacovigilanceThresholds.MaxCh50Percent, nameof(request.Ch50Percent));
        ValidateFiniteRange(request.C3aNgMl, 0.0, PharmacovigilanceThresholds.MaxC3aNgMl, nameof(request.C3aNgMl));
        ValidateFiniteRange(request.C5aNgMl, 0.0, PharmacovigilanceThresholds.MaxC5aNgMl, nameof(request.C5aNgMl));
        ValidateFiniteRange(request.Il6PgMl, 0.0, PharmacovigilanceThresholds.MaxIl6PgMl, nameof(request.Il6PgMl));
        ValidateFiniteRange(request.TnfaPgMl, 0.0, PharmacovigilanceThresholds.MaxTnfaPgMl, nameof(request.TnfaPgMl));
        ValidateFiniteRange(request.AntiPegIgmTiter, 0.0, PharmacovigilanceThresholds.MaxAntiPegIgmTiter, nameof(request.AntiPegIgmTiter));
        ValidateFiniteRange(request.AntiDsDnaIgGIuMl, 0.0, PharmacovigilanceThresholds.MaxAntiDsDnaIgGIuMl, nameof(request.AntiDsDnaIgGIuMl));
        ValidateFiniteRange(request.Tlr9ActivationIndex, 0.0, PharmacovigilanceThresholds.MaxTlr9ActivationIndex, nameof(request.Tlr9ActivationIndex));

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        string cleanOperator = operatorId.Trim();
        DateTimeOffset recordedAtUtc = NormalizeToMicroseconds(request.RecordedAtUtc ?? _utcNow());
        string assayId = $"immu_{Guid.NewGuid():N}"[..24];

        LotImmunogenicityAssayRecord record = new()
        {
            AssayId = assayId,
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            Ch50Percent = Math.Round(request.Ch50Percent, 4, MidpointRounding.AwayFromZero),
            C3aNgMl = Math.Round(request.C3aNgMl, 4, MidpointRounding.AwayFromZero),
            C5aNgMl = Math.Round(request.C5aNgMl, 4, MidpointRounding.AwayFromZero),
            Il6PgMl = Math.Round(request.Il6PgMl, 4, MidpointRounding.AwayFromZero),
            TnfaPgMl = Math.Round(request.TnfaPgMl, 4, MidpointRounding.AwayFromZero),
            AntiPegIgmTiter = Math.Round(request.AntiPegIgmTiter, 4, MidpointRounding.AwayFromZero),
            AntiDsDnaIgGIuMl = Math.Round(request.AntiDsDnaIgGIuMl, 4, MidpointRounding.AwayFromZero),
            Tlr9ActivationIndex = Math.Round(request.Tlr9ActivationIndex, 4, MidpointRounding.AwayFromZero),
            RecordedAtUtc = recordedAtUtc
        };

        await _pvRepository.AddImmunogenicityAssayAsync(record, cancellationToken).ConfigureAwait(false);

        LotPharmacovigilanceReport report = await EvaluateAndEscalateLotAsync(lot, cleanOperator, cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            cleanOperator,
            "FABRICATION_IMMUNOGENICITY_RECORD",
            $"fabrication-lot/{lot.LotId}",
            JsonSerializer.Serialize(new
            {
                assayId = record.AssayId,
                lotId = lot.LotId,
                ch50Percent = record.Ch50Percent,
                c3aNgMl = record.C3aNgMl,
                c5aNgMl = record.C5aNgMl,
                il6PgMl = record.Il6PgMl,
                tnfaPgMl = record.TnfaPgMl,
                antiPegIgmTiter = record.AntiPegIgmTiter,
                antiDsDnaIgGIuMl = record.AntiDsDnaIgGIuMl,
                tlr9ActivationIndex = record.Tlr9ActivationIndex,
                posture = report.Posture.ToString()
            }));

        return new ImmunogenicityAssayResponse
        {
            Assay = record,
            ReportAfterAssay = report
        };
    }

    /// <inheritdoc/>
    public async Task<LotPharmacovigilanceReport?> EvaluateLotPharmacovigilanceAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads a fabrication lot and computes its live post-implantation pharmacovigilance report.
        // WHY:  Enables clinicians, patients, and CoA verification gates to inspect real-time immunogenicity posture.
        // HOW:  Queries lot by ID, returns null if missing, otherwise evaluates via EvaluateForLotAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        return await EvaluateForLotAsync(lot, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<FabricationCertificateVerificationReport> ApplyPharmacovigilanceGateAsync(
        FabricationCertificateVerificationReport baseReport,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Evaluates local pharmacovigilance posture and overrides IsTrusted fail-closed when quarantined.
        // WHY:  Milestone 41 (P216): Prevents clinically reactive or CARPA-flagged lots from passing CoA verification.
        // HOW:  Queries local lot pharmacovigilance; returns baseReport unchanged for blank or foreign CoAs; else enforces gate.
        ArgumentNullException.ThrowIfNull(baseReport);

        if (string.IsNullOrWhiteSpace(baseReport.LotId))
        {
            return baseReport;
        }

        LotPharmacovigilanceReport? report = await EvaluateLotPharmacovigilanceAsync(baseReport.LotId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return baseReport;
        }

        bool pvBlocksTrust = report.IsQuarantineTriggered;
        bool effectiveTrusted = baseReport.IsTrusted && !pvBlocksTrust;
        string effectiveSummary = pvBlocksTrust
            ? $"{baseReport.Summary} PHARMACOVIGILANCE BLOCK: {report.Summary}"
            : baseReport.Summary;

        return baseReport with
        {
            IsTrusted = effectiveTrusted,
            Summary = effectiveSummary,
            PharmacovigilancePosture = report.Posture,
            IsPharmacovigilanceFlagged = pvBlocksTrust
        };
    }

    /// <summary>
    /// Re-evaluates pharmacovigilance posture after an event or assay write, transitioning non-revoked lots to
    /// <see cref="FabricationLotStatus.Quarantined"/> and logging a 21 CFR Part 11 quarantine audit entry when triggered.
    /// </summary>
    /// <param name="lot">Loaded parent fabrication lot record.</param>
    /// <param name="cleanOperator">Trimmed authenticated operator username.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated <see cref="LotPharmacovigilanceReport"/> reflecting any quarantine transition.</returns>
    private async Task<LotPharmacovigilanceReport> EvaluateAndEscalateLotAsync(
        FabricationLotRecord lot,
        string cleanOperator,
        CancellationToken cancellationToken)
    {
        // WHAT: Evaluates live safety signals and performs fail-closed lot quarantine transition when warranted.
        // WHY:  Centralizes escalation and audit logging so adverse event and immunogenicity paths behave identically.
        // HOW:  Transitions Released/Draft/InSynthesis/QcPending lots to Quarantined (preserving Revoked) and emits audit entry.
        LotPharmacovigilanceReport report = await EvaluateForLotAsync(lot, cancellationToken).ConfigureAwait(false);
        if (report.IsQuarantineTriggered)
        {
            if (lot.LotStatus != FabricationLotStatus.Quarantined && lot.LotStatus != FabricationLotStatus.Revoked)
            {
                FabricationLotRecord quarantinedLot = lot with { LotStatus = FabricationLotStatus.Quarantined };
                await _lotRepository.UpsertLotAsync(quarantinedLot, cancellationToken).ConfigureAwait(false);
                report = report with { LotStatus = FabricationLotStatus.Quarantined };
            }

            _auditService.LogAction(
                cleanOperator,
                "FABRICATION_PHARMACOVIGILANCE_QUARANTINE",
                $"fabrication-lot/{lot.LotId}",
                JsonSerializer.Serialize(new
                {
                    lotId = lot.LotId,
                    lotStatus = report.LotStatus.ToString(),
                    posture = report.Posture.ToString(),
                    carpaSignalDetected = report.CarpaSignalDetected,
                    tlr9CytokineStormDetected = report.Tlr9CytokineStormDetected,
                    humoralImmunogenicityDetected = report.HumoralImmunogenicityDetected,
                    triggeredSignals = report.TriggeredSignals
                }));
        }

        return report;
    }

    /// <summary>
    /// Evaluates adverse events and immunogenicity panels for an already-loaded <see cref="FabricationLotRecord"/>.
    /// </summary>
    /// <param name="lot">Loaded fabrication lot record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deterministic <see cref="LotPharmacovigilanceReport"/>.</returns>
    private async Task<LotPharmacovigilanceReport> EvaluateForLotAsync(
        FabricationLotRecord lot,
        CancellationToken cancellationToken)
    {
        // WHAT: Performs allocation-free CTCAE v5.0, CARPA, TLR9 cytokine storm, and humoral threshold evaluation.
        // WHY:  Produces deterministic safety classification, signal diagnostics, and decrypted summaries.
        // HOW:  Loads chronological events & assays, scans with simple for-loops, and synthesizes summary text.
        IReadOnlyList<LotAdverseEventRecord> rawEvents = await _pvRepository.ListAdverseEventsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LotImmunogenicityAssayRecord> assays = await _pvRepository.ListImmunogenicityAssaysByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LotAdverseEventSummary> events = DecryptAdverseEventSummaries(rawEvents);

        DateTimeOffset evaluatedAtUtc = NormalizeToMicroseconds(_utcNow());
        AdverseEventSeverityGrade? highestGrade = null;
        bool hasCriticalCtcae = false;
        bool carpaCritical = false;
        bool tlr9Critical = false;
        bool humoralCritical = false;
        bool monitorSignal = false;
        List<string> signals = [];

        for (int i = 0; i < events.Count; i++)
        {
            LotAdverseEventSummary ev = events[i];
            if (highestGrade is null || ev.SeverityGrade > highestGrade.Value)
            {
                highestGrade = ev.SeverityGrade;
            }

            if (ev.SeverityGrade >= AdverseEventSeverityGrade.Grade3Severe)
            {
                hasCriticalCtcae = true;
                AddUniqueSignal(signals, $"CTCAE {ev.SeverityGrade}: {ev.SymptomCategory}");
            }
            else
            {
                monitorSignal = true;
                AddUniqueSignal(signals, $"CTCAE {ev.SeverityGrade}: {ev.SymptomCategory}");
            }
        }

        for (int i = 0; i < assays.Count; i++)
        {
            LotImmunogenicityAssayRecord assay = assays[i];
            EvaluateCh50(assay.Ch50Percent, ref carpaCritical, ref monitorSignal, signals);
            EvaluateUpperBiomarker(assay.C3aNgMl, PharmacovigilanceThresholds.C3aMonitorNgMl, PharmacovigilanceThresholds.C3aCarpaCriticalNgMl, "CARPA critical C3a elevation", "Sub-critical C3a elevation", ref carpaCritical, ref monitorSignal, signals);
            EvaluateUpperBiomarker(assay.C5aNgMl, PharmacovigilanceThresholds.C5aMonitorNgMl, PharmacovigilanceThresholds.C5aCarpaCriticalNgMl, "CARPA critical C5a elevation", "Sub-critical C5a elevation", ref carpaCritical, ref monitorSignal, signals);
            EvaluateUpperBiomarker(assay.Il6PgMl, PharmacovigilanceThresholds.Il6MonitorPgMl, PharmacovigilanceThresholds.Il6CytokineStormCriticalPgMl, "Cytokine storm critical IL-6 surge", "Elevated serum IL-6", ref tlr9Critical, ref monitorSignal, signals);
            EvaluateUpperBiomarker(assay.TnfaPgMl, PharmacovigilanceThresholds.TnfaMonitorPgMl, PharmacovigilanceThresholds.TnfaCytokineStormCriticalPgMl, "Cytokine storm critical TNF-alpha surge", "Elevated serum TNF-alpha", ref tlr9Critical, ref monitorSignal, signals);

            bool antiPegCritical = false;
            EvaluateUpperBiomarker(assay.AntiPegIgmTiter, PharmacovigilanceThresholds.AntiPegIgmMonitorTiter, PharmacovigilanceThresholds.AntiPegIgmCriticalTiter, "Critical anti-PEG IgM CARPA titer", "Elevated anti-PEG IgM titer", ref antiPegCritical, ref monitorSignal, signals);
            if (antiPegCritical)
            {
                carpaCritical = true;
                humoralCritical = true;
            }

            EvaluateUpperBiomarker(assay.AntiDsDnaIgGIuMl, PharmacovigilanceThresholds.AntiDsDnaIgGMonitorIuMl, PharmacovigilanceThresholds.AntiDsDnaIgGCriticalIuMl, "Critical anti-dsDNA IgG autoimmune titer", "Elevated anti-dsDNA IgG titer", ref humoralCritical, ref monitorSignal, signals);
            EvaluateUpperBiomarker(assay.Tlr9ActivationIndex, PharmacovigilanceThresholds.Tlr9MonitorIndex, PharmacovigilanceThresholds.Tlr9CriticalIndex, "Critical TLR9 innate immune hyperactivation", "Elevated TLR9 innate activation", ref tlr9Critical, ref monitorSignal, signals);
        }

        bool isQuarantineTriggered = hasCriticalCtcae || carpaCritical || tlr9Critical || humoralCritical;
        PharmacovigilancePosture posture = isQuarantineTriggered
            ? PharmacovigilancePosture.CarpaQuarantine
            : monitorSignal
                ? PharmacovigilancePosture.Monitor
                : PharmacovigilancePosture.Nominal;

        string summary = BuildSummaryText(posture, events.Count, assays.Count, signals);

        return new LotPharmacovigilanceReport
        {
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            LotStatus = lot.LotStatus,
            EvaluatedAtUtc = evaluatedAtUtc,
            Posture = posture,
            HighestSeverityGrade = highestGrade,
            IsQuarantineTriggered = isQuarantineTriggered,
            EmergencyRecallRecommended = isQuarantineTriggered,
            CarpaSignalDetected = carpaCritical,
            Tlr9CytokineStormDetected = tlr9Critical,
            HumoralImmunogenicityDetected = humoralCritical,
            AdverseEventCount = events.Count,
            ImmunogenicityAssayCount = assays.Count,
            TriggeredSignals = signals,
            Summary = summary,
            AdverseEvents = events,
            ImmunogenicityAssays = assays
        };
    }

    /// <summary>
    /// Decrypts optional encrypted clinical notes across a chronological sequence of persisted adverse events.
    /// </summary>
    /// <param name="records">Persisted adverse event database records.</param>
    /// <returns>Authorized clinical summary records with plaintext notes restored.</returns>
    private IReadOnlyList<LotAdverseEventSummary> DecryptAdverseEventSummaries(IReadOnlyList<LotAdverseEventRecord> records)
    {
        // WHAT: Converts persisted adverse event rows into decrypted LotAdverseEventSummary DTOs without LINQ.
        // WHY:  Shared by both ListAdverseEventsAsync and EvaluateForLotAsync to avoid duplicating decryption loops.
        // HOW:  Iterates with a pre-sized List and invokes ProfileDataProtector.DecryptField on each note payload.
        List<LotAdverseEventSummary> summaries = new(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            LotAdverseEventRecord row = records[i];
            summaries.Add(new LotAdverseEventSummary
            {
                EventId = row.EventId,
                LotId = row.LotId,
                PatientId = row.PatientId,
                SeverityGrade = row.SeverityGrade,
                SymptomCategory = row.SymptomCategory,
                OnsetHoursPostImplant = row.OnsetHoursPostImplant,
                ClinicalNotes = _protector.DecryptField(row.ClinicalNotesEncrypted),
                ReportedBy = row.ReportedBy,
                RecordedAtUtc = row.RecordedAtUtc
            });
        }

        return summaries;
    }

    /// <summary>
    /// Evaluates lower-bound CH50 hemolytic complement activity against CARPA and monitoring thresholds.
    /// </summary>
    /// <param name="ch50Percent">Measured CH50 complement activity percentage.</param>
    /// <param name="carpaCritical">Set to <see langword="true"/> when CH50 falls to or below the critical depletion threshold.</param>
    /// <param name="monitorSignal">Set to <see langword="true"/> when CH50 drops into the sub-critical monitoring band.</param>
    /// <param name="signals">Diagnostic signal list receiving unique alerts.</param>
    private static void EvaluateCh50(
        double ch50Percent,
        ref bool carpaCritical,
        ref bool monitorSignal,
        List<string> signals)
    {
        // WHAT: Evaluates CH50 complement consumption where lower activity indicates acute classical-pathway activation.
        // WHY:  CARPA pseudoallergy consumes circulating complement proteins, driving CH50 precipitously downward.
        // HOW:  Compares against Ch50CarpaCriticalLowPercent first, then Ch50MonitorLowPercent.
        if (ch50Percent <= PharmacovigilanceThresholds.Ch50CarpaCriticalLowPercent)
        {
            carpaCritical = true;
            AddUniqueSignal(signals, "CARPA critical CH50 complement depletion");
        }
        else if (ch50Percent < PharmacovigilanceThresholds.Ch50MonitorLowPercent)
        {
            monitorSignal = true;
            AddUniqueSignal(signals, "Sub-critical CH50 complement consumption");
        }
    }

    /// <summary>
    /// Evaluates an upper-bound biomarker concentration or fold-change index against monitoring and critical thresholds.
    /// </summary>
    /// <param name="value">Measured biomarker assay value.</param>
    /// <param name="monitorThreshold">Lower monitoring threshold.</param>
    /// <param name="criticalThreshold">Critical quarantine escalation threshold.</param>
    /// <param name="criticalSignal">Diagnostic signal string recorded when <paramref name="criticalThreshold"/> is met.</param>
    /// <param name="monitorSignalText">Diagnostic signal string recorded when <paramref name="monitorThreshold"/> is met.</param>
    /// <param name="criticalFlag">Reference flag set to <see langword="true"/> upon critical threshold excursion.</param>
    /// <param name="monitorFlag">Reference flag set to <see langword="true"/> upon monitoring threshold excursion.</param>
    /// <param name="signals">Diagnostic signal list receiving unique alerts.</param>
    private static void EvaluateUpperBiomarker(
        double value,
        double monitorThreshold,
        double criticalThreshold,
        string criticalSignal,
        string monitorSignalText,
        ref bool criticalFlag,
        ref bool monitorFlag,
        List<string> signals)
    {
        // WHAT: Applies uniform upper-bound critical and monitoring thresholds for an immunogenicity biomarker.
        // WHY:  Eliminates repeated threshold branching across C3a, C5a, IL-6, TNF-α, anti-PEG IgM, anti-dsDNA IgG, and TLR9.
        // HOW:  Tests criticalThreshold first, falling back to monitorThreshold, and records non-duplicate signal labels.
        if (value >= criticalThreshold)
        {
            criticalFlag = true;
            AddUniqueSignal(signals, criticalSignal);
        }
        else if (value >= monitorThreshold)
        {
            monitorFlag = true;
            AddUniqueSignal(signals, monitorSignalText);
        }
    }

    /// <summary>
    /// Appends a diagnostic signal description to <paramref name="signals"/> only if not already present.
    /// </summary>
    /// <param name="signals">Target diagnostic signal list.</param>
    /// <param name="signal">Signal label to append.</param>
    private static void AddUniqueSignal(List<string> signals, string signal)
    {
        // WHAT: Deduplicates diagnostic signal labels without LINQ or HashSet overhead.
        // WHY:  Repeated longitudinal draws with the same persistent elevation should appear once cleanly in reports.
        // HOW:  Performs an allocation-free linear scan over the small signal list using Ordinal comparison.
        for (int i = 0; i < signals.Count; i++)
        {
            if (string.Equals(signals[i], signal, StringComparison.Ordinal))
            {
                return;
            }
        }

        signals.Add(signal);
    }

    /// <summary>
    /// Formats a plain-English clinical summary describing evaluated pharmacovigilance posture and signal count.
    /// </summary>
    /// <param name="posture">Resolved aggregate pharmacovigilance posture.</param>
    /// <param name="eventCount">Count of CTCAE adverse events evaluated.</param>
    /// <param name="assayCount">Count of immunogenicity assay panels evaluated.</param>
    /// <param name="signals">Detected clinical/biomarker diagnostic signals.</param>
    /// <returns>Human-readable clinical diagnostic summary.</returns>
    private static string BuildSummaryText(
        PharmacovigilancePosture posture,
        int eventCount,
        int assayCount,
        List<string> signals)
    {
        // WHAT: Synthesizes an unambiguous clinical summary for clinicians, APIs, and UI banners.
        // WHY:  Communicates quarantine escalations and active biomarker alerts clearly without chemical jargon.
        // HOW:  Branches on PharmacovigilancePosture and formats counts and comma-joined triggered signals.
        if (posture == PharmacovigilancePosture.CarpaQuarantine)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"CARPA / SEVERE TOXICITY QUARANTINE: {eventCount} adverse event(s), {assayCount} assay(s); emergency recall recommended ({string.Join(", ", signals)}).");
        }

        if (posture == PharmacovigilancePosture.Monitor)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Pharmacovigilance monitoring advised: {eventCount} adverse event(s), {assayCount} assay(s) ({string.Join(", ", signals)}).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Nominal post-implantation pharmacovigilance: {eventCount} adverse event(s), {assayCount} assay(s), no CARPA or TLR9 signals detected.");
    }

    /// <summary>
    /// Validates that a numeric clinical measurement is finite and within physical inclusive bounds.
    /// </summary>
    /// <param name="value">Candidate measurement value.</param>
    /// <param name="minInclusive">Minimum valid inclusive bound.</param>
    /// <param name="maxInclusive">Maximum valid inclusive bound.</param>
    /// <param name="parameterName">Name of the input measurement parameter.</param>
    private static void ValidateFiniteRange(
        double value,
        double minInclusive,
        double maxInclusive,
        string parameterName)
    {
        // WHAT: Guards against NaN, Infinity, negative concentrations, or out-of-scale laboratory entries.
        // WHY:  Protects diagnostic threshold comparisons and prevents corrupted biomarker persistence.
        // HOW:  Checks double.IsFinite and inclusive range bounds, throwing ArgumentOutOfRangeException on violation.
        if (!double.IsFinite(value) || value < minInclusive || value > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                string.Create(CultureInfo.InvariantCulture, $"{parameterName} must be finite and between {minInclusive} and {maxInclusive}."));
        }
    }

    /// <summary>
    /// Normalizes a <see cref="DateTimeOffset"/> to UTC truncated to exact microsecond precision (6 fractional digits).
    /// </summary>
    /// <param name="timestamp">Input timestamp.</param>
    /// <returns>UTC timestamp with sub-microsecond ticks removed for MySQL <c>DATETIME(6)</c> parity.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset timestamp)
    {
        // WHAT: Converts timestamp to UTC and clears the final 100-nanosecond digit.
        // WHY:  MySQL/MariaDB DATETIME(6) stores 6 fractional digits while .NET ticks have 7; truncating ensures identical precision across all 4 engines.
        // HOW:  Computes utc.Ticks - (utc.Ticks % 10) and returns a UTC DateTimeOffset.
        DateTimeOffset utc = timestamp.ToUniversalTime();
        long microsecondTicks = utc.Ticks - (utc.Ticks % 10);
        return new DateTimeOffset(microsecondTicks, TimeSpan.Zero);
    }
}
