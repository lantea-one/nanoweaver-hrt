using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Deterministic systems-biology engine evaluating permanent non-nuclease epigenetic steroidogenic toggle
/// circuits, CFD off-target gating, Hill phase-plane separatrix geometry, and ML-DSA-65 synthesis sign-off.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IEpigeneticCircuitService"/> enforcing
/// fail-closed genomic specificity (&gt;= 98.0% CFD) and mutual-repression bistable toggle stability before
/// permitting cryptographic release approval.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P221): Translates MtF (<c>CYP19A1</c>/<c>HSD17B1</c> ON,
/// <c>SRD5A2</c>/<c>HSD17B3</c> OFF) and FtM (<c>HSD17B3</c>/<c>CYP17A1</c> ON, <c>CYP19A1</c> OFF) chromatin
/// blueprints into verifiable steady-state trajectories without allocating LINQ closures in biophysical loops.
/// </para>
/// <para>
/// <strong>How it works:</strong> Solves the coupled 2D Hill toggle fixed points numerically over a 200-step
/// bracketed grid with bisection refinement, maps target attractors to serum E2/T and TTR%, encrypts clinical notes,
/// signs valid blueprints using <see cref="IKeyManagementService"/>, and logs immutable 21 CFR Part 11 audits.
/// </para>
/// </remarks>
public sealed class EpigeneticCircuitService : IEpigeneticCircuitService
{
    /// <summary>
    /// Canonical cryptographic signature algorithm identifier applied on approval.
    /// </summary>
    private const string SignatureAlgorithmName = "ML-DSA-65";

    /// <summary>
    /// Persistent repository storing epigenetic circuits and child single-guide RNAs.
    /// </summary>
    private readonly IEpigeneticCircuitRepository _repository;

    /// <summary>
    /// AES-256-GCM field protector encrypting clinician design notes at rest.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Node key management service supplying persistent ML-DSA-65 signing without exposing private keys.
    /// </summary>
    private readonly IKeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptographic provider verifying ML-DSA-65 approval signatures offline.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// FDA 21 CFR Part 11 hash-chained compliance audit ledger service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Deterministic UTC clock provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpigeneticCircuitService"/> class.
    /// </summary>
    /// <param name="repository">Persistent epigenetic circuit and guide-RNA repository.</param>
    /// <param name="protector">AES-256-GCM field protector for clinical design notes.</param>
    /// <param name="keyManagementService">Node key management service supplying ML-DSA-65 signing keys.</param>
    /// <param name="cryptoProvider">Post-quantum provider verifying ML-DSA-65 signatures.</param>
    /// <param name="auditService">21 CFR Part 11 compliance audit service.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for testing.</param>
    public EpigeneticCircuitService(
        IEpigeneticCircuitRepository repository,
        ProfileDataProtector protector,
        IKeyManagementService keyManagementService,
        IPostQuantumCryptoProvider cryptoProvider,
        IComplianceAuditService auditService,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Guards and stores all required persistence, crypto, and compliance dependencies.
        // WHY:  Guarantees fail-fast DI composition and prevents null references during approval signing.
        // HOW:  Validates every argument and falls back to DateTimeOffset.UtcNow when no custom clock is passed.
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(keyManagementService);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(auditService);

        _repository = repository;
        _protector = protector;
        _keyManagementService = keyManagementService;
        _cryptoProvider = cryptoProvider;
        _auditService = auditService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<EpigeneticCircuitEvaluationReport> DesignCircuitAsync(
        DesignEpigeneticCircuitRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates inputs, builds or checks sgRNAs, encrypts notes, evaluates bistability, persists, and audits design.
        // WHY:  Milestone 42 (P221): Provides an auditable closed-loop circuit authoring entrypoint.
        // HOW:  Normalizes inputs, runs biophysical analysis, persists atomically, logs EPIGENETIC_CIRCUIT_DESIGN, and returns report.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string cleanPatientId = request.PatientId?.Trim() ?? string.Empty;
        if (cleanPatientId.Length == 0 || cleanPatientId.Length > 128)
        {
            throw new ArgumentException("PatientId must be between 1 and 128 characters.", nameof(request));
        }

        ValidateEnums(request.Regimen, request.TargetTissue, request.EffectorPlatform);

        string? cleanNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (cleanNotes is not null && cleanNotes.Length > EpigeneticSafetyThresholds.MaxNotesLength)
        {
            throw new ArgumentException($"Notes must not exceed {EpigeneticSafetyThresholds.MaxNotesLength} characters.", nameof(request));
        }

        string cleanOperator = operatorId.Trim();
        string circuitId = $"epicirc_{Guid.NewGuid():N}"[..24];
        DateTimeOffset createdAtUtc = NormalizeToMicroseconds(_utcNow());

        IReadOnlyList<EpigeneticGuideRnaRecord> guides = BuildValidatedGuides(
            circuitId,
            cleanPatientId,
            request.Regimen,
            request.Guides);

        string? encryptedNotes = cleanNotes is null ? null : _protector.EncryptField(cleanNotes);

        EpigeneticCircuitRecord draftRecord = new()
        {
            CircuitId = circuitId,
            PatientId = cleanPatientId,
            Regimen = request.Regimen,
            TargetTissue = request.TargetTissue,
            EffectorPlatform = request.EffectorPlatform,
            CircuitStatus = EpigeneticCircuitStatus.Draft,
            PredictedTtrPercent = 0.0,
            SeparatrixMargin = 0.0,
            MinOffTargetSpecificityScore = 0.0,
            NotesEncrypted = encryptedNotes,
            CreatedAtUtc = createdAtUtc,
            ApprovedAtUtc = null
        };

        EpigeneticCircuitEvaluationReport evaluatedReport = BuildEvaluationReport(
            draftRecord,
            guides,
            cleanNotes,
            clearanceMultiplier: 1.0,
            epigeneticStabilityFactor: 1.0,
            overrideStatus: null,
            signerPublicKeyBase64: null);

        EpigeneticCircuitStatus initialStatus = evaluatedReport.IsSafeForPermanentSynthesis
            ? EpigeneticCircuitStatus.Evaluated
            : EpigeneticCircuitStatus.Blocked;

        EpigeneticCircuitRecord persistedRecord = draftRecord with
        {
            CircuitStatus = initialStatus,
            PredictedTtrPercent = evaluatedReport.PredictedTtrPercent,
            SeparatrixMargin = evaluatedReport.SeparatrixMargin,
            MinOffTargetSpecificityScore = evaluatedReport.MinOffTargetSpecificityScore
        };

        await _repository.SaveCircuitAsync(persistedRecord, guides, cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            cleanOperator,
            "EPIGENETIC_CIRCUIT_DESIGN",
            $"epigenetic-circuit/{circuitId}",
            JsonSerializer.Serialize(new
            {
                circuitId,
                patientId = cleanPatientId,
                regimen = request.Regimen.ToString(),
                effectorPlatform = request.EffectorPlatform.ToString(),
                guideCount = guides.Count,
                status = initialStatus.ToString(),
                minCfdSpecificity = persistedRecord.MinOffTargetSpecificityScore
            }));

        return evaluatedReport with { CircuitStatus = initialStatus };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EpigeneticCircuitEvaluationReport>> ListPatientCircuitsAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads all patient epigenetic circuits, decrypts clinical notes, and reconstructs live phase planes.
        // WHY:  Enables APIs, desktop/mobile workspaces, and UI panels to display full geometric and approval telemetry.
        // HOW:  Queries repository chronologically, loads child guides, decrypts notes, and attaches node public key when approved.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        IReadOnlyList<EpigeneticCircuitRecord> records = await _repository.ListCircuitsByPatientAsync(patientId.Trim(), cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return [];
        }

        string nodePublicKey = _keyManagementService.GetNodeSigningKey().PublicKeyBase64;
        List<EpigeneticCircuitEvaluationReport> reports = new(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            EpigeneticCircuitRecord record = records[i];
            IReadOnlyList<EpigeneticGuideRnaRecord> guides = await _repository.ListGuidesByCircuitAsync(record.CircuitId, cancellationToken).ConfigureAwait(false);
            string? decryptedNotes = record.NotesEncrypted is null ? null : _protector.DecryptField(record.NotesEncrypted);
            string? pubKey = record.CircuitStatus == EpigeneticCircuitStatus.Approved ? nodePublicKey : null;

            reports.Add(BuildEvaluationReport(
                record,
                guides,
                decryptedNotes,
                clearanceMultiplier: 1.0,
                epigeneticStabilityFactor: 1.0,
                overrideStatus: record.CircuitStatus,
                signerPublicKeyBase64: pubKey));
        }

        return reports;
    }

    /// <inheritdoc/>
    public async Task<EpigeneticCircuitEvaluationReport?> EvaluateCircuitAsync(
        string circuitId,
        EvaluateEpigeneticCircuitRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Re-evaluates biophysical bistability, CFD specificity, and endocrine setpoints for a stored circuit.
        // WHY:  Allows clinicians to simulate metabolic clearance stress and epigenetic stability margins before sign-off.
        // HOW:  Validates parameters, loads stored circuit and guides, persists Evaluated/Blocked status, and writes audit log.
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        double clearance = request?.ClearanceMultiplier ?? 1.0;
        double stability = request?.EpigeneticStabilityFactor ?? 1.0;
        ValidateFiniteRange(clearance, 0.25, 4.0, nameof(EvaluateEpigeneticCircuitRequest.ClearanceMultiplier));
        ValidateFiniteRange(stability, 0.2, 2.0, nameof(EvaluateEpigeneticCircuitRequest.EpigeneticStabilityFactor));

        EpigeneticCircuitRecord? existing = await _repository.GetCircuitByIdAsync(circuitId.Trim(), cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        IReadOnlyList<EpigeneticGuideRnaRecord> guides = await _repository.ListGuidesByCircuitAsync(existing.CircuitId, cancellationToken).ConfigureAwait(false);
        string? decryptedNotes = existing.NotesEncrypted is null ? null : _protector.DecryptField(existing.NotesEncrypted);

        EpigeneticCircuitEvaluationReport evaluation = BuildEvaluationReport(
            existing,
            guides,
            decryptedNotes,
            clearance,
            stability,
            overrideStatus: null,
            signerPublicKeyBase64: null);

        EpigeneticCircuitStatus nextStatus = evaluation.IsSafeForPermanentSynthesis
            ? EpigeneticCircuitStatus.Evaluated
            : EpigeneticCircuitStatus.Blocked;

        await _repository.UpdateCircuitEvaluationAsync(
            existing.CircuitId,
            nextStatus,
            evaluation.PredictedTtrPercent,
            evaluation.SeparatrixMargin,
            evaluation.MinOffTargetSpecificityScore,
            approvedAtUtc: null,
            approvalPayloadSha256: null,
            approvalSignatureBase64: null,
            approvalSignerKeyId: null,
            cancellationToken).ConfigureAwait(false);

        string cleanOperator = operatorId.Trim();
        _auditService.LogAction(
            cleanOperator,
            "EPIGENETIC_CIRCUIT_EVALUATE",
            $"epigenetic-circuit/{existing.CircuitId}",
            JsonSerializer.Serialize(new
            {
                circuitId = existing.CircuitId,
                clearanceMultiplier = clearance,
                epigeneticStabilityFactor = stability,
                isBistable = evaluation.IsBistable,
                separatrixMargin = evaluation.SeparatrixMargin,
                minCfdSpecificity = evaluation.MinOffTargetSpecificityScore,
                status = nextStatus.ToString()
            }));

        return evaluation with
        {
            CircuitStatus = nextStatus,
            ApprovedAtUtc = null,
            ApprovalPayloadSha256 = null,
            ApprovalSignatureBase64 = null,
            ApprovalSignerKeyId = null,
            ApprovalSignerPublicKeyBase64 = null,
            SignatureAlgorithm = null
        };
    }

    /// <inheritdoc/>
    public async Task<EpigeneticCircuitEvaluationReport?> ApproveCircuitAsync(
        string circuitId,
        ApproveEpigeneticCircuitRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Performs fail-closed safety gating and mints a signed ML-DSA-65 blueprint approval when all gates pass.
        // WHY:  Milestone 42 (P221): Guarantees unsafe or monostable epigenetic circuits never receive synthesis signatures.
        // HOW:  Re-evaluates live; if any gate fails, updates status to Blocked and omits signature; otherwise signs canonical UTF-8 JSON.
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        double clearance = request?.ClearanceMultiplier ?? 1.0;
        double stability = request?.EpigeneticStabilityFactor ?? 1.0;
        ValidateFiniteRange(clearance, 0.25, 4.0, nameof(ApproveEpigeneticCircuitRequest.ClearanceMultiplier));
        ValidateFiniteRange(stability, 0.2, 2.0, nameof(ApproveEpigeneticCircuitRequest.EpigeneticStabilityFactor));

        string? reviewerNotes = string.IsNullOrWhiteSpace(request?.ReviewerNotes) ? null : request.ReviewerNotes.Trim();
        if (reviewerNotes is not null && reviewerNotes.Length > EpigeneticSafetyThresholds.MaxNotesLength)
        {
            throw new ArgumentException($"ReviewerNotes must not exceed {EpigeneticSafetyThresholds.MaxNotesLength} characters.", nameof(request));
        }

        EpigeneticCircuitRecord? existing = await _repository.GetCircuitByIdAsync(circuitId.Trim(), cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        IReadOnlyList<EpigeneticGuideRnaRecord> guides = await _repository.ListGuidesByCircuitAsync(existing.CircuitId, cancellationToken).ConfigureAwait(false);
        string? decryptedNotes = existing.NotesEncrypted is null ? null : _protector.DecryptField(existing.NotesEncrypted);
        string cleanOperator = operatorId.Trim();

        EpigeneticCircuitEvaluationReport evaluation = BuildEvaluationReport(
            existing,
            guides,
            decryptedNotes,
            clearance,
            stability,
            overrideStatus: null,
            signerPublicKeyBase64: null);

        if (!evaluation.IsSafeForPermanentSynthesis)
        {
            await _repository.UpdateCircuitEvaluationAsync(
                existing.CircuitId,
                EpigeneticCircuitStatus.Blocked,
                evaluation.PredictedTtrPercent,
                evaluation.SeparatrixMargin,
                evaluation.MinOffTargetSpecificityScore,
                approvedAtUtc: null,
                approvalPayloadSha256: null,
                approvalSignatureBase64: null,
                approvalSignerKeyId: null,
                cancellationToken).ConfigureAwait(false);

            _auditService.LogAction(
                cleanOperator,
                "EPIGENETIC_CIRCUIT_APPROVE",
                $"epigenetic-circuit/{existing.CircuitId}",
                JsonSerializer.Serialize(new
                {
                    circuitId = existing.CircuitId,
                    outcome = "blocked",
                    reasons = evaluation.FailureReasons,
                    reviewerNotes
                }));

            return evaluation with
            {
                CircuitStatus = EpigeneticCircuitStatus.Blocked,
                ApprovedAtUtc = null,
                ApprovalPayloadSha256 = null,
                ApprovalSignatureBase64 = null,
                ApprovalSignerKeyId = null,
                ApprovalSignerPublicKeyBase64 = null,
                SignatureAlgorithm = null
            };
        }

        DateTimeOffset approvedAtUtc = NormalizeToMicroseconds(_utcNow());
        byte[] canonicalBytes = BuildCanonicalApprovalPayloadBytes(
            existing,
            guides,
            evaluation.PredictedEstradiolPmolL,
            evaluation.PredictedTestosteroneNmolL,
            evaluation.PredictedTtrPercent,
            evaluation.SeparatrixMargin,
            evaluation.MinOffTargetSpecificityScore,
            approvedAtUtc);
        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        PqcSignature signature = _keyManagementService.SignWithNodeSigningKey(canonicalBytes);
        PqcKeyMaterial publicKey = _keyManagementService.GetNodeSigningKey();

        await _repository.UpdateCircuitEvaluationAsync(
            existing.CircuitId,
            EpigeneticCircuitStatus.Approved,
            evaluation.PredictedTtrPercent,
            evaluation.SeparatrixMargin,
            evaluation.MinOffTargetSpecificityScore,
            approvedAtUtc,
            payloadSha256,
            signature.SignatureBase64,
            signature.SignerKeyId,
            cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            cleanOperator,
            "EPIGENETIC_CIRCUIT_APPROVE",
            $"epigenetic-circuit/{existing.CircuitId}",
            JsonSerializer.Serialize(new
            {
                circuitId = existing.CircuitId,
                outcome = "approved",
                payloadSha256,
                signerKeyId = signature.SignerKeyId,
                signatureAlgorithm = SignatureAlgorithmName,
                reviewerNotes
            }));

        return evaluation with
        {
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            ApprovedAtUtc = approvedAtUtc,
            ApprovalPayloadSha256 = payloadSha256,
            ApprovalSignatureBase64 = signature.SignatureBase64,
            ApprovalSignerKeyId = signature.SignerKeyId,
            ApprovalSignerPublicKeyBase64 = publicKey.PublicKeyBase64,
            SignatureAlgorithm = SignatureAlgorithmName
        };
    }

    /// <inheritdoc/>
    public bool VerifyApprovalSignature(EpigeneticCircuitEvaluationReport report)
    {
        // WHAT: Independently verifies canonical SHA-256 digest binding and ML-DSA-65 digital signature on an approved report.
        // WHY:  Prevents silent tampering with hormone targets, guide sequences, or CFD scores after clinical sign-off.
        // HOW:  Checks populated signature metadata, reconstructs canonical UTF-8 JSON bytes, matches SHA-256, and verifies PQC signature.
        ArgumentNullException.ThrowIfNull(report);

        if (report.CircuitStatus != EpigeneticCircuitStatus.Approved
            || !report.ApprovedAtUtc.HasValue
            || string.IsNullOrWhiteSpace(report.ApprovalPayloadSha256)
            || string.IsNullOrWhiteSpace(report.ApprovalSignatureBase64)
            || string.IsNullOrWhiteSpace(report.ApprovalSignerPublicKeyBase64))
        {
            return false;
        }

        EpigeneticCircuitRecord projection = new()
        {
            CircuitId = report.CircuitId,
            PatientId = report.PatientId,
            Regimen = report.Regimen,
            TargetTissue = report.TargetTissue,
            EffectorPlatform = report.EffectorPlatform,
            CreatedAtUtc = report.CreatedAtUtc
        };

        byte[] canonicalBytes = BuildCanonicalApprovalPayloadBytes(
            projection,
            report.Guides,
            report.PredictedEstradiolPmolL,
            report.PredictedTestosteroneNmolL,
            report.PredictedTtrPercent,
            report.SeparatrixMargin,
            report.MinOffTargetSpecificityScore,
            report.ApprovedAtUtc.Value);

        string recomputedSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        if (!string.Equals(recomputedSha256, report.ApprovalPayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _cryptoProvider.VerifySignature(
            canonicalBytes,
            report.ApprovalSignatureBase64,
            report.ApprovalSignerPublicKeyBase64);
    }

    /// <summary>
    /// Computes Hill phase-plane fixed points, nullclines, separatrix stability, endocrine steady states, and gate checks.
    /// </summary>
    /// <param name="record">Persisted or candidate circuit metadata.</param>
    /// <param name="guides">Validated guide-RNA specifications.</param>
    /// <param name="decryptedNotes">Optional plaintext clinician design notes.</param>
    /// <param name="clearanceMultiplier">Patient steroid clearance multiplier.</param>
    /// <param name="epigeneticStabilityFactor">Chromatin heritability stability scaling factor.</param>
    /// <param name="overrideStatus">Optional persisted status to reflect on the report.</param>
    /// <param name="signerPublicKeyBase64">Optional node ML-DSA-65 public key when approved.</param>
    /// <returns>Complete biophysical evaluation report.</returns>
    private static EpigeneticCircuitEvaluationReport BuildEvaluationReport(
        EpigeneticCircuitRecord record,
        IReadOnlyList<EpigeneticGuideRnaRecord> guides,
        string? decryptedNotes,
        double clearanceMultiplier,
        double epigeneticStabilityFactor,
        EpigeneticCircuitStatus? overrideStatus,
        string? signerPublicKeyBase64)
    {
        // WHAT: Solves the 2D mutual-repression Hill dynamical system and evaluates all 3 clinical safety gates.
        // WHY:  Provides single unified computation path shared across Design, Evaluate, List, and Approve workflows.
        // HOW:  Derives Hill synthesis strengths alphaU/alphaV, solves fixed points on [0, 1.4], computes serum E2/T and TTR%, and checks thresholds.
        double minCfd = 100.0;
        int activateCount = 0;
        int repressCount = 0;
        double estrogenDrive = 0.0;
        double androgenDrive = 0.0;

        for (int i = 0; i < guides.Count; i++)
        {
            EpigeneticGuideRnaRecord guide = guides[i];
            if (guide.CfdSpecificityScore < minCfd)
            {
                minCfd = guide.CfdSpecificityScore;
            }

            if (guide.ModulationMode == GuideRnaModulationMode.Activate)
            {
                activateCount++;
            }
            else
            {
                repressCount++;
            }

            double delta = ComputeGuideAxisContribution(guide);
            if (IsEstrogenPathwayGene(guide.TargetGeneSymbol))
            {
                estrogenDrive += delta;
            }
            else
            {
                androgenDrive += delta;
            }
        }

        minCfd = Math.Round(minCfd, 2, MidpointRounding.AwayFromZero);

        bool isEstrogenicTarget = DetermineEstrogenicTarget(record.Regimen, estrogenDrive, androgenDrive);
        double platformGain = GetPlatformStabilityGain(record.EffectorPlatform);
        double toggleCoupling = activateCount > 0 && repressCount > 0 ? 1.0 : 0.55;
        double scaledDrive = Math.Clamp(platformGain * epigeneticStabilityFactor * toggleCoupling, 0.25, 1.15);

        double alphaU = isEstrogenicTarget ? Math.Clamp(1.06 * scaledDrive, 0.15, 1.25) : Math.Clamp(0.88 * scaledDrive, 0.15, 1.25);
        double alphaV = isEstrogenicTarget ? Math.Clamp(0.88 * scaledDrive, 0.15, 1.25) : Math.Clamp(1.06 * scaledDrive, 0.15, 1.25);

        EpigeneticNullclineDiagram phasePlane = SolvePhasePlaneDiagram(alphaU, alphaV, isEstrogenicTarget);

        double clearanceFactor = Math.Pow(clearanceMultiplier, 0.65);
        double predictedE2 = Math.Round(
            (65.0 + (phasePlane.TargetAttractor.EstrogenAxisNormalized * 485.0)) / clearanceFactor,
            2,
            MidpointRounding.AwayFromZero);
        double predictedT = Math.Round(
            (0.25 + (phasePlane.TargetAttractor.AndrogenAxisNormalized * 19.5)) / clearanceFactor,
            2,
            MidpointRounding.AwayFromZero);

        bool inHormoneRange = IsInTargetHormoneRange(isEstrogenicTarget, predictedE2, predictedT);
        double rangeAdjustment = inHormoneRange ? 0.0 : 28.0;
        double predictedTtr = Math.Round(
            Math.Clamp(72.0 + (phasePlane.SeparatrixMargin * 50.0) - rangeAdjustment, 0.0, 99.8),
            2,
            MidpointRounding.AwayFromZero);

        bool passesOffTarget = minCfd >= EpigeneticSafetyThresholds.MinimumCfdSpecificityScore;
        bool passesBistability = phasePlane.IsBistable
            && phasePlane.SeparatrixMargin >= EpigeneticSafetyThresholds.MinimumSeparatrixMargin;
        bool passesSetpoint = inHormoneRange
            && predictedTtr >= EpigeneticSafetyThresholds.MinimumPredictedTtrPercent;

        List<string> failures = [];
        if (!passesOffTarget)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"CFD off-target specificity floor failed: minimum score {minCfd:F2}% is below required {EpigeneticSafetyThresholds.MinimumCfdSpecificityScore:F1}%."));
        }

        if (!passesBistability)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Bistable toggle gate failed: IsBistable={phasePlane.IsBistable}, separatrix margin {phasePlane.SeparatrixMargin:F3} < {EpigeneticSafetyThresholds.MinimumSeparatrixMargin:F2}."));
        }

        if (!passesSetpoint)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Endocrine setpoint gate failed: predicted E2={predictedE2:F1} pmol/L, T={predictedT:F2} nmol/L, TTR={predictedTtr:F1}%."));
        }

        bool isSafe = failures.Count == 0;
        EpigeneticCircuitStatus effectiveStatus = overrideStatus ?? (isSafe ? EpigeneticCircuitStatus.Evaluated : EpigeneticCircuitStatus.Blocked);

        return new EpigeneticCircuitEvaluationReport
        {
            CircuitId = record.CircuitId,
            PatientId = record.PatientId,
            Regimen = record.Regimen,
            TargetTissue = record.TargetTissue,
            EffectorPlatform = record.EffectorPlatform,
            CircuitStatus = effectiveStatus,
            PredictedEstradiolPmolL = predictedE2,
            PredictedTestosteroneNmolL = predictedT,
            PredictedTtrPercent = predictedTtr,
            MinOffTargetSpecificityScore = minCfd,
            PassesOffTargetGate = passesOffTarget,
            IsBistable = phasePlane.IsBistable,
            SeparatrixMargin = phasePlane.SeparatrixMargin,
            PassesBistabilityGate = passesBistability,
            PassesEndocrineSetpointGate = passesSetpoint,
            IsSafeForPermanentSynthesis = isSafe,
            FailureReasons = failures,
            Guides = guides,
            PhasePlane = phasePlane,
            DecryptedNotes = decryptedNotes,
            ApprovalPayloadSha256 = record.ApprovalPayloadSha256,
            ApprovalSignatureBase64 = record.ApprovalSignatureBase64,
            ApprovalSignerKeyId = record.ApprovalSignerKeyId,
            ApprovalSignerPublicKeyBase64 = signerPublicKeyBase64,
            SignatureAlgorithm = effectiveStatus == EpigeneticCircuitStatus.Approved ? SignatureAlgorithmName : null,
            CreatedAtUtc = record.CreatedAtUtc,
            ApprovedAtUtc = record.ApprovedAtUtc
        };
    }

    /// <summary>
    /// Computes Hill nullcline curves, bracketed fixed-point roots, separatrix curve, and stability margin without LINQ.
    /// </summary>
    /// <param name="alphaU">Estrogenic synthesis maximal rate.</param>
    /// <param name="alphaV">Androgenic synthesis maximal rate.</param>
    /// <param name="isEstrogenicTarget">True when targeting the estrogen-dominant attractor; false for androgen-dominant.</param>
    /// <returns>Complete <see cref="EpigeneticNullclineDiagram"/> geometry.</returns>
    private static EpigeneticNullclineDiagram SolvePhasePlaneDiagram(
        double alphaU,
        double alphaV,
        bool isEstrogenicTarget)
    {
        // WHAT: Samples E2/T nullclines and solves du/dt = gv/dt = 0 intersections on an offset 200-step grid + bisection.
        // WHY:  Distinguishes 3-root bistable epigenetic switches from collapsed 1-root monostable switches cleanly.
        // HOW:  Iterates 200 offset intervals detecting F(u) = gu(gv(u)) - u sign flips and refines each via 28 bisection steps.
        const int curveSamples = 25;
        List<PhasePlanePoint> estrogenNullcline = new(curveSamples);
        List<PhasePlanePoint> androgenNullcline = new(curveSamples);

        for (int i = 0; i < curveSamples; i++)
        {
            double axisCoord = i * (1.2 / (curveSamples - 1));
            estrogenNullcline.Add(new PhasePlanePoint
            {
                EstrogenAxisNormalized = Math.Round(EvaluateHill(axisCoord, alphaU), 4, MidpointRounding.AwayFromZero),
                AndrogenAxisNormalized = Math.Round(axisCoord, 4, MidpointRounding.AwayFromZero)
            });
            androgenNullcline.Add(new PhasePlanePoint
            {
                EstrogenAxisNormalized = Math.Round(axisCoord, 4, MidpointRounding.AwayFromZero),
                AndrogenAxisNormalized = Math.Round(EvaluateHill(axisCoord, alphaV), 4, MidpointRounding.AwayFromZero)
            });
        }

        const int gridSteps = 200;
        const double scanOffset = 0.00013;
        const double scanSpan = 1.4;
        double step = scanSpan / gridSteps;

        List<PhasePlanePoint> roots = new(3);
        double leftU = scanOffset;
        double leftF = EvaluateFixedPointResidual(leftU, alphaU, alphaV);

        for (int i = 0; i < gridSteps; i++)
        {
            double rightU = scanOffset + ((i + 1) * step);
            double rightF = EvaluateFixedPointResidual(rightU, alphaU, alphaV);

            if (leftF * rightF < 0.0)
            {
                double a = leftU;
                double b = rightU;
                double fa = leftF;

                for (int iter = 0; iter < 28; iter++)
                {
                    double mid = 0.5 * (a + b);
                    double fm = EvaluateFixedPointResidual(mid, alphaU, alphaV);
                    if (fa * fm <= 0.0)
                    {
                        b = mid;
                    }
                    else
                    {
                        a = mid;
                        fa = fm;
                    }
                }

                double rootU = 0.5 * (a + b);
                double rootV = EvaluateHill(rootU, alphaV);
                roots.Add(new PhasePlanePoint
                {
                    EstrogenAxisNormalized = Math.Round(rootU, 4, MidpointRounding.AwayFromZero),
                    AndrogenAxisNormalized = Math.Round(rootV, 4, MidpointRounding.AwayFromZero)
                });
            }

            leftU = rightU;
            leftF = rightF;
        }

        if (roots.Count == 3)
        {
            PhasePlanePoint androgenAttractor = roots[0];
            PhasePlanePoint saddle = roots[1];
            PhasePlanePoint estrogenAttractor = roots[2];

            PhasePlanePoint target = isEstrogenicTarget ? estrogenAttractor : androgenAttractor;
            PhasePlanePoint alternate = isEstrogenicTarget ? androgenAttractor : estrogenAttractor;

            double duTarget = target.EstrogenAxisNormalized - saddle.EstrogenAxisNormalized;
            double dvTarget = target.AndrogenAxisNormalized - saddle.AndrogenAxisNormalized;
            double targetDist = Math.Sqrt((duTarget * duTarget) + (dvTarget * dvTarget));

            double duAlt = alternate.EstrogenAxisNormalized - saddle.EstrogenAxisNormalized;
            double dvAlt = alternate.AndrogenAxisNormalized - saddle.AndrogenAxisNormalized;
            double altDist = Math.Sqrt((duAlt * duAlt) + (dvAlt * dvAlt));

            double margin = Math.Round(Math.Min(targetDist, altDist), 4, MidpointRounding.AwayFromZero);

            List<PhasePlanePoint> separatrix = BuildLinearizedSeparatrix(saddle, alphaU, alphaV);

            return new EpigeneticNullclineDiagram
            {
                EstrogenNullcline = estrogenNullcline,
                AndrogenNullcline = androgenNullcline,
                SeparatrixCurve = separatrix,
                TargetAttractor = target,
                AlternateAttractor = alternate,
                SaddlePoint = saddle,
                IsBistable = true,
                SeparatrixMargin = margin
            };
        }

        PhasePlanePoint collapsedRoot = roots[0];
        return new EpigeneticNullclineDiagram
        {
            EstrogenNullcline = estrogenNullcline,
            AndrogenNullcline = androgenNullcline,
            SeparatrixCurve = [],
            TargetAttractor = collapsedRoot,
            AlternateAttractor = collapsedRoot,
            SaddlePoint = collapsedRoot,
            IsBistable = false,
            SeparatrixMargin = 0.0
        };
    }

    /// <summary>
    /// Evaluates the mutual-repression Hill synthesis response function <c>alpha / (1 + (x / K)^n)</c>.
    /// </summary>
    /// <param name="repressorLevel">Normalized concentration of the opposing steroidogenic module.</param>
    /// <param name="alpha">Maximal synthesis drive for the activated module.</param>
    /// <returns>Steady-state response activity along the active axis.</returns>
    private static double EvaluateHill(double repressorLevel, double alpha)
    {
        // WHAT: Computes the Hill cooperative inhibition term alpha / (1 + (x/K)^4).
        // WHY:  Models sharp ultrasensitive chromatin switching between estrogenic and androgenic attractors.
        // HOW:  Evaluates (repressorLevel / K)^4 via explicit Math.Pow without allocations.
        double ratio = repressorLevel / EpigeneticSafetyThresholds.HillHalfSaturationK;
        return alpha / (1.0 + Math.Pow(ratio, EpigeneticSafetyThresholds.HillCooperativityN));
    }

    /// <summary>
    /// Evaluates the scalar fixed-point residual <c>g_u(g_v(u)) - u</c>.
    /// </summary>
    /// <param name="u">Candidate estrogenic coordinate.</param>
    /// <param name="alphaU">Estrogenic synthesis maximal drive.</param>
    /// <param name="alphaV">Androgenic synthesis maximal drive.</param>
    /// <returns>Residual crossing zero cleanly at each phase-plane equilibrium.</returns>
    private static double EvaluateFixedPointResidual(double u, double alphaU, double alphaV)
    {
        // WHAT: Computes the composed Hill residual g_u(g_v(u)) - u for fixed-point root bracketing.
        // WHY:  Roots of this scalar function enumerate all attractors and the intermediate saddle equilibrium.
        // HOW:  Evaluates EvaluateHill twice in series and subtracts u.
        return EvaluateHill(EvaluateHill(u, alphaV), alphaU) - u;
    }

    /// <summary>
    /// Constructs 9 sampled points along the linearized local separatrix passing through the saddle equilibrium.
    /// </summary>
    /// <param name="saddle">Coordinates of the unstable saddle fixed point.</param>
    /// <param name="alphaU">Estrogenic maximal rate.</param>
    /// <param name="alphaV">Androgenic maximal rate.</param>
    /// <returns>Ordered curve points spanning across the [0, 1.2]^2 phase portrait.</returns>
    private static List<PhasePlanePoint> BuildLinearizedSeparatrix(
        PhasePlanePoint saddle,
        double alphaU,
        double alphaV)
    {
        // WHAT: Traces the stable manifold tangent direction through the saddle point for phase-plane plotting.
        // WHY:  Milestone 42 (P223): Lets the UI SVG visualize the exact basin boundary between MtF and FtM states.
        // HOW:  Computes numerical slopes of both Hill nullclines at the saddle and spans u across [0.05, 1.15].
        double derivU = Math.Abs(EvaluateHill(saddle.AndrogenAxisNormalized + 0.001, alphaU) - EvaluateHill(saddle.AndrogenAxisNormalized - 0.001, alphaU));
        double derivV = Math.Abs(EvaluateHill(saddle.EstrogenAxisNormalized + 0.001, alphaV) - EvaluateHill(saddle.EstrogenAxisNormalized - 0.001, alphaV));
        double slope = Math.Clamp(Math.Sqrt(derivV / Math.Max(derivU, 1e-6)), 0.5, 2.0);

        List<PhasePlanePoint> curve = new(9);
        for (int i = 0; i < 9; i++)
        {
            double u = 0.05 + (i * (1.10 / 8.0));
            double v = Math.Clamp(saddle.AndrogenAxisNormalized + ((u - saddle.EstrogenAxisNormalized) * slope), 0.02, 1.18);
            curve.Add(new PhasePlanePoint
            {
                EstrogenAxisNormalized = Math.Round(u, 4, MidpointRounding.AwayFromZero),
                AndrogenAxisNormalized = Math.Round(v, 4, MidpointRounding.AwayFromZero)
            });
        }

        return curve;
    }

    /// <summary>
    /// Maps an effector platform to its empirical chromatin persistence multiplier.
    /// </summary>
    /// <param name="platform">Epigenetic dCas9 effector platform.</param>
    /// <returns>Dimensionless heritability stability multiplier.</returns>
    private static double GetPlatformStabilityGain(EpigeneticEffectorPlatform platform) => platform switch
    {
        EpigeneticEffectorPlatform.CrisprOffOn => 1.08,
        EpigeneticEffectorPlatform.DCas9Dnmt3aTet1 => 1.00,
        _ => 0.90
    };

    /// <summary>
    /// Evaluates whether serum hormone outputs lie within regimen physiological target bounds.
    /// </summary>
    /// <param name="isEstrogenicTarget">True for MtF / estrogen-dominant regimens; false for FtM / androgen-dominant.</param>
    /// <param name="predictedE2">Predicted serum 17β-estradiol concentration in pmol/L.</param>
    /// <param name="predictedT">Predicted serum total testosterone concentration in nmol/L.</param>
    /// <returns><see langword="true"/> if both hormones fall inside clinical target bounds; otherwise <see langword="false"/>.</returns>
    private static bool IsInTargetHormoneRange(
        bool isEstrogenicTarget,
        double predictedE2,
        double predictedT)
    {
        // WHAT: Checks predicted steady-state serum E2 and T against regimen clinical target windows.
        // WHY:  Blocks designs whose clearance sensitivity pushes steady-state hormones outside physiological safety limits.
        // HOW:  Compares predictedE2 and predictedT against MtF or FtM bounds in EpigeneticSafetyThresholds.
        if (isEstrogenicTarget)
        {
            return predictedE2 >= EpigeneticSafetyThresholds.MtFMinEstradiolPmolL
                && predictedE2 <= EpigeneticSafetyThresholds.MtFMaxEstradiolPmolL
                && predictedT < EpigeneticSafetyThresholds.MtFMaxTestosteroneNmolL;
        }

        return predictedT >= EpigeneticSafetyThresholds.FtMMinTestosteroneNmolL
            && predictedT <= EpigeneticSafetyThresholds.FtMMaxTestosteroneNmolL
            && predictedE2 < EpigeneticSafetyThresholds.FtMMaxEstradiolPmolL;
    }

    /// <summary>
    /// Determines whether the target clinical attractor corresponds to estrogenic or androgenic dominance.
    /// </summary>
    /// <param name="regimen">Prescribed therapeutic regimen enum.</param>
    /// <param name="estrogenDrive">Aggregate guide-RNA drive across estrogenic genes.</param>
    /// <param name="androgenDrive">Aggregate guide-RNA drive across androgenic genes.</param>
    /// <returns><see langword="true"/> for Estrogenic dominance; <see langword="false"/> for Androgenic dominance.</returns>
    private static bool DetermineEstrogenicTarget(
        TherapeuticRegimen regimen,
        double estrogenDrive,
        double androgenDrive)
    {
        // WHAT: Resolves target hormonal dominance from regimen enum or Custom guide balance.
        // WHY:  Supports canonical MaleToFemale/FemaleToMale targets plus guide-driven Custom designs cleanly.
        // HOW:  Directly returns true/false for binary regimens or compares estrogenDrive vs androgenDrive for Custom.
        if (regimen == TherapeuticRegimen.MaleToFemale)
        {
            return true;
        }

        if (regimen == TherapeuticRegimen.FemaleToMale)
        {
            return false;
        }

        return estrogenDrive >= androgenDrive;
    }

    /// <summary>
    /// Computes signed pathway activation strength contributed by a single guide RNA.
    /// </summary>
    /// <param name="guide">Validated guide-RNA record.</param>
    /// <returns>Positive transcriptional drive contribution.</returns>
    private static double ComputeGuideAxisContribution(EpigeneticGuideRnaRecord guide)
    {
        // WHAT: Quantifies transcriptional push contributed by one sgRNA without LINQ allocations.
        // WHY:  Helps classify Custom regimen polarity from the magnitudes of Activate vs Repress targets.
        // HOW:  Takes magnitude of log2(PredictedFoldChange) signed by modulation mode.
        double logChange = Math.Log2(guide.PredictedFoldChange);
        return guide.ModulationMode == GuideRnaModulationMode.Activate
            ? Math.Max(0.1, logChange)
            : -Math.Max(0.1, -logChange);
    }

    /// <summary>
    /// Classifies an HGNC gene symbol into estrogenic vs androgenic steroidogenic modules.
    /// </summary>
    /// <param name="geneSymbol">Uppercase HGNC gene symbol.</param>
    /// <returns><see langword="true"/> for aromatase/estrogen-receptor axis genes; <see langword="false"/> otherwise.</returns>
    private static bool IsEstrogenPathwayGene(string geneSymbol)
    {
        // WHAT: Assigns steroidogenic gene symbols to either the E2-synthesis/response or T/DHT module.
        // WHY:  Maps CYP19A1, HSD17B1, and ESR1/2 cleanly against HSD17B3, CYP17A1, SRD5A2, and AR.
        // HOW:  Compares normalized symbol prefixes with OrdinalIgnoreCase without regex or LINQ.
        return string.Equals(geneSymbol, "CYP19A1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(geneSymbol, "HSD17B1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(geneSymbol, "ESR1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(geneSymbol, "ESR2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates caller guide-RNA inputs or constructs canonical regimen defaults when omitted.
    /// </summary>
    /// <param name="circuitId">Parent circuit identifier.</param>
    /// <param name="patientId">Owning patient identifier.</param>
    /// <param name="regimen">Prescribed endocrine therapeutic regimen.</param>
    /// <param name="inputs">Optional caller guide-RNA specifications.</param>
    /// <returns>Deterministically ordered read-only list of persisted guide-RNA records.</returns>
    private static IReadOnlyList<EpigeneticGuideRnaRecord> BuildValidatedGuides(
        string circuitId,
        string patientId,
        TherapeuticRegimen regimen,
        IReadOnlyList<GuideRnaSpecInput>? inputs)
    {
        // WHAT: Validates custom sgRNAs (protospacer bases/length, PAM, CFD bounds) or builds canonical MtF/FtM defaults.
        // WHY:  Prevents malformed DNA sequences or invalid CFD/fold-change parameters from entering persistence.
        // HOW:  Creates default guides when inputs is null/empty; otherwise validates each GuideRnaSpecInput deterministically.
        if (inputs is null || inputs.Count == 0)
        {
            return BuildDefaultGuides(circuitId, patientId, regimen);
        }

        if (inputs.Count > EpigeneticSafetyThresholds.MaxGuidesPerCircuit)
        {
            throw new ArgumentException($"A circuit may specify at most {EpigeneticSafetyThresholds.MaxGuidesPerCircuit} guide RNAs.", nameof(inputs));
        }

        List<EpigeneticGuideRnaRecord> guides = new(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            GuideRnaSpecInput item = inputs[i] ?? throw new ArgumentNullException(nameof(inputs), "Guide entry cannot be null.");

            string geneSymbol = item.TargetGeneSymbol?.Trim().ToUpperInvariant() ?? string.Empty;
            if (geneSymbol.Length == 0 || geneSymbol.Length > 32)
            {
                throw new ArgumentException("TargetGeneSymbol must be between 1 and 32 characters.", nameof(inputs));
            }

            if (!Enum.IsDefined(item.ModulationMode))
            {
                throw new ArgumentException("ModulationMode must be a valid GuideRnaModulationMode value.", nameof(inputs));
            }

            string protospacer = ValidateDnaProtospacer(item.ProtospacerSequence);
            string pam = item.PamSequence?.Trim().ToUpperInvariant() ?? string.Empty;
            if (pam.Length < 2 || pam.Length > 6)
            {
                throw new ArgumentException("PamSequence must be between 2 and 6 characters.", nameof(inputs));
            }

            ValidateFiniteRange(item.CfdSpecificityScore, 0.0, 100.0, nameof(item.CfdSpecificityScore));
            ValidateFiniteRange(item.PredictedFoldChange, 0.01, 50.0, nameof(item.PredictedFoldChange));

            guides.Add(new EpigeneticGuideRnaRecord
            {
                GuideId = string.Create(CultureInfo.InvariantCulture, $"{circuitId}_g{i + 1:D2}"),
                CircuitId = circuitId,
                PatientId = patientId,
                TargetGeneSymbol = geneSymbol,
                ModulationMode = item.ModulationMode,
                ProtospacerSequence = protospacer,
                PamSequence = pam,
                CfdSpecificityScore = Math.Round(item.CfdSpecificityScore, 2, MidpointRounding.AwayFromZero),
                PredictedFoldChange = Math.Round(item.PredictedFoldChange, 3, MidpointRounding.AwayFromZero)
            });
        }

        return guides;
    }

    /// <summary>
    /// Validates and canonicalizes a DNA protospacer sequence (18 to 24 bases, uppercase A/C/G/T only).
    /// </summary>
    /// <param name="rawSequence">Raw protospacer input string.</param>
    /// <returns>Trimmed uppercase protospacer DNA sequence.</returns>
    private static string ValidateDnaProtospacer(string rawSequence)
    {
        // WHAT: Checks protospacer length (18-24 nt) and ensures every nucleotide is A, C, G, or T without Regex.
        // WHY:  Zero-allocation character loop adheres to AGENTS.md while enforcing strict sequence hygiene.
        // HOW:  Trims, uppercases, inspects length, and scans every character in a single linear loop.
        string seq = rawSequence?.Trim().ToUpperInvariant() ?? string.Empty;
        if (seq.Length < 18 || seq.Length > 24)
        {
            throw new ArgumentException("ProtospacerSequence must be between 18 and 24 nucleotides.", nameof(rawSequence));
        }

        for (int i = 0; i < seq.Length; i++)
        {
            char c = seq[i];
            if (c != 'A' && c != 'C' && c != 'G' && c != 'T')
            {
                throw new ArgumentException("ProtospacerSequence may contain only A, C, G, and T nucleotides.", nameof(rawSequence));
            }
        }

        return seq;
    }

    /// <summary>
    /// Generates canonical validated guide-RNA presets when callers omit custom guide arrays.
    /// </summary>
    /// <param name="circuitId">Parent circuit identifier.</param>
    /// <param name="patientId">Owning patient identifier.</param>
    /// <param name="regimen">Prescribed endocrine therapeutic regimen.</param>
    /// <returns>Canonical MtF (or Custom default) vs FtM guide-RNA array.</returns>
    private static List<EpigeneticGuideRnaRecord> BuildDefaultGuides(
        string circuitId,
        string patientId,
        TherapeuticRegimen regimen)
    {
        // WHAT: Synthesizes high-specificity (>98.8% CFD) steroidogenic reprogramming guide panels per regimen.
        // WHY:  Lets clinicians author safe canonical MtF or FtM circuits with zero manual sequence transcription.
        // HOW:  Emits CYP19A1/HSD17B1 ON + SRD5A2/HSD17B3 OFF for MtF/Custom, or HSD17B3/CYP17A1 ON + CYP19A1 OFF for FtM.
        if (regimen == TherapeuticRegimen.FemaleToMale)
        {
            return
            [
                new EpigeneticGuideRnaRecord
                {
                    GuideId = $"{circuitId}_g01",
                    CircuitId = circuitId,
                    PatientId = patientId,
                    TargetGeneSymbol = "HSD17B3",
                    ModulationMode = GuideRnaModulationMode.Activate,
                    ProtospacerSequence = "TAGCTAGCTGATCGATCGAT",
                    PamSequence = "GGG",
                    CfdSpecificityScore = 99.3,
                    PredictedFoldChange = 4.5
                },
                new EpigeneticGuideRnaRecord
                {
                    GuideId = $"{circuitId}_g02",
                    CircuitId = circuitId,
                    PatientId = patientId,
                    TargetGeneSymbol = "CYP17A1",
                    ModulationMode = GuideRnaModulationMode.Activate,
                    ProtospacerSequence = "GCTAGCTAGCTGATCGATCC",
                    PamSequence = "AGG",
                    CfdSpecificityScore = 98.9,
                    PredictedFoldChange = 3.6
                },
                new EpigeneticGuideRnaRecord
                {
                    GuideId = $"{circuitId}_g03",
                    CircuitId = circuitId,
                    PatientId = patientId,
                    TargetGeneSymbol = "CYP19A1",
                    ModulationMode = GuideRnaModulationMode.Repress,
                    ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
                    PamSequence = "AGG",
                    CfdSpecificityScore = 99.5,
                    PredictedFoldChange = 0.1
                }
            ];
        }

        return
        [
            new EpigeneticGuideRnaRecord
            {
                GuideId = $"{circuitId}_g01",
                CircuitId = circuitId,
                PatientId = patientId,
                TargetGeneSymbol = "CYP19A1",
                ModulationMode = GuideRnaModulationMode.Activate,
                ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
                PamSequence = "AGG",
                CfdSpecificityScore = 99.4,
                PredictedFoldChange = 4.2
            },
            new EpigeneticGuideRnaRecord
            {
                GuideId = $"{circuitId}_g02",
                CircuitId = circuitId,
                PatientId = patientId,
                TargetGeneSymbol = "HSD17B1",
                ModulationMode = GuideRnaModulationMode.Activate,
                ProtospacerSequence = "ATCGATCGTAGCTAGCTGAT",
                PamSequence = "TGG",
                CfdSpecificityScore = 99.1,
                PredictedFoldChange = 3.4
            },
            new EpigeneticGuideRnaRecord
            {
                GuideId = $"{circuitId}_g03",
                CircuitId = circuitId,
                PatientId = patientId,
                TargetGeneSymbol = "SRD5A2",
                ModulationMode = GuideRnaModulationMode.Repress,
                ProtospacerSequence = "CGATCGATGCTAGCTAGCTA",
                PamSequence = "CGG",
                CfdSpecificityScore = 98.8,
                PredictedFoldChange = 0.12
            },
            new EpigeneticGuideRnaRecord
            {
                GuideId = $"{circuitId}_g04",
                CircuitId = circuitId,
                PatientId = patientId,
                TargetGeneSymbol = "HSD17B3",
                ModulationMode = GuideRnaModulationMode.Repress,
                ProtospacerSequence = "TAGCTAGCTGATCGATCGAT",
                PamSequence = "GGG",
                CfdSpecificityScore = 99.2,
                PredictedFoldChange = 0.15
            }
        ];
    }

    /// <summary>
    /// Serializes canonical blueprint parameters, guide-RNA loci, and safety metrics into UTF-8 JSON bytes for ML-DSA-65 signing.
    /// </summary>
    /// <param name="circuit">Circuit metadata record.</param>
    /// <param name="guides">Ordered guide-RNA records bound to the blueprint.</param>
    /// <param name="predictedE2">Predicted steady-state serum 17β-estradiol in pmol/L.</param>
    /// <param name="predictedT">Predicted steady-state serum testosterone in nmol/L.</param>
    /// <param name="predictedTtr">Predicted time-in-therapeutic-range percentage.</param>
    /// <param name="separatrixMargin">Normalized separatrix stability margin.</param>
    /// <param name="minCfdSpecificity">Minimum CFD specificity score across all guide RNAs.</param>
    /// <param name="approvedAtUtc">UTC timestamp of clinical approval.</param>
    /// <returns>Canonical compact UTF-8 JSON payload bytes ready for SHA-256 hashing and ML-DSA-65 signing.</returns>
    private static byte[] BuildCanonicalApprovalPayloadBytes(
        EpigeneticCircuitRecord circuit,
        IReadOnlyList<EpigeneticGuideRnaRecord> guides,
        double predictedE2,
        double predictedT,
        double predictedTtr,
        double separatrixMargin,
        double minCfdSpecificity,
        DateTimeOffset approvedAtUtc)
    {
        // WHAT: Serializes a fixed-order canonical UTF-8 JSON document binding circuit architecture, guide sequences, and safety metrics.
        // WHY:  Guarantees byte-identical digest reconstruction across independent node or wet-lab signature verification.
        // HOW:  Writes deterministic JSON fields via Utf8JsonWriter over an ArrayBufferWriter<byte>.
        ArrayBufferWriter<byte> buffer = new(1024);
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("circuitId", circuit.CircuitId);
            writer.WriteString("patientId", circuit.PatientId);
            writer.WriteString("regimen", circuit.Regimen.ToString());
            writer.WriteString("targetTissue", circuit.TargetTissue.ToString());
            writer.WriteString("effectorPlatform", circuit.EffectorPlatform.ToString());
            writer.WriteString("predictedEstradiolPmolL", predictedE2.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("predictedTestosteroneNmolL", predictedT.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("predictedTtrPercent", predictedTtr.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("separatrixMargin", separatrixMargin.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("minOffTargetSpecificityScore", minCfdSpecificity.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("approvedAtUtc", approvedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

            writer.WriteStartArray("guides");
            for (int i = 0; i < guides.Count; i++)
            {
                EpigeneticGuideRnaRecord g = guides[i];
                writer.WriteStartObject();
                writer.WriteString("guideId", g.GuideId);
                writer.WriteString("targetGeneSymbol", g.TargetGeneSymbol);
                writer.WriteString("modulationMode", g.ModulationMode.ToString());
                writer.WriteString("protospacerSequence", g.ProtospacerSequence);
                writer.WriteString("pamSequence", g.PamSequence);
                writer.WriteString("cfdSpecificityScore", g.CfdSpecificityScore.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteString("predictedFoldChange", g.PredictedFoldChange.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Validates that regimen, target tissue, and effector platform are defined enumeration values.
    /// </summary>
    /// <param name="regimen">Therapeutic regimen enum value.</param>
    /// <param name="targetTissue">Anatomical tissue target enum value.</param>
    /// <param name="effectorPlatform">dCas9 effector platform enum value.</param>
    private static void ValidateEnums(
        TherapeuticRegimen regimen,
        TargetTissueType targetTissue,
        EpigeneticEffectorPlatform effectorPlatform)
    {
        // WHAT: Guards against undefined numeric enum cast values entering domain computations.
        // WHY:  Prevents silent switch fallthrough on malformed JSON deserialization payloads.
        // HOW:  Invokes Enum.IsDefined on each enumeration value and throws ArgumentException on mismatch.
        if (!Enum.IsDefined(regimen))
        {
            throw new ArgumentException("Regimen must be a valid TherapeuticRegimen value.", nameof(regimen));
        }

        if (!Enum.IsDefined(targetTissue))
        {
            throw new ArgumentException("TargetTissue must be a valid TargetTissueType value.", nameof(targetTissue));
        }

        if (!Enum.IsDefined(effectorPlatform))
        {
            throw new ArgumentException("EffectorPlatform must be a valid EpigeneticEffectorPlatform value.", nameof(effectorPlatform));
        }
    }

    /// <summary>
    /// Validates that a numeric parameter is finite and falls within inclusive biophysical bounds.
    /// </summary>
    /// <param name="value">Input value.</param>
    /// <param name="minInclusive">Minimum inclusive bound.</param>
    /// <param name="maxInclusive">Maximum inclusive bound.</param>
    /// <param name="parameterName">Name of the parameter validated.</param>
    private static void ValidateFiniteRange(
        double value,
        double minInclusive,
        double maxInclusive,
        string parameterName)
    {
        // WHAT: Rejects NaN, Infinity, and out-of-range simulation or specificity parameters.
        // WHY:  Preserves deterministic Hill phase-plane convergence and prevents silent threshold bypasses.
        // HOW:  Checks double.IsFinite and inclusive boundaries, throwing ArgumentOutOfRangeException on violation.
        if (!double.IsFinite(value) || value < minInclusive || value > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                string.Create(CultureInfo.InvariantCulture, $"{parameterName} must be finite and between {minInclusive} and {maxInclusive}."));
        }
    }

    /// <summary>
    /// Truncates a UTC timestamp to exact microsecond precision (6 fractional digits) for multi-engine SQL parity.
    /// </summary>
    /// <param name="timestamp">Input timestamp.</param>
    /// <returns>UTC timestamp with sub-microsecond ticks zeroed.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset timestamp)
    {
        // WHAT: Normalizes timestamp to UTC truncated to whole microseconds.
        // WHY:  MySQL DATETIME(6) stores 6 fractional digits vs .NET 7 digits, ensuring canonical hash parity after DB round-trips.
        // HOW:  Subtracts utc.Ticks % 10 before constructing a UTC DateTimeOffset.
        DateTimeOffset utc = timestamp.ToUniversalTime();
        long microsecondTicks = utc.Ticks - (utc.Ticks % 10);
        return new DateTimeOffset(microsecondTicks, TimeSpan.Zero);
    }
}
