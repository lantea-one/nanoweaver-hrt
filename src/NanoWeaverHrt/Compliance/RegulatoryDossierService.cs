using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Concrete Regulatory IND / IMPD dossier compiler and cross-node cryptographic verifier implementing
/// <see cref="IRegulatoryDossierService"/> across all DNA, fabrication, cold-chain, QSP, and compliance subsystems.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> End-to-end evidence aggregator, 10-gate fail-closed readiness evaluator,
/// ML-DSA-65 canonical bundle signer, AES-256-GCM at-rest archiver, and cross-node verifier.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P236): Delivers reproducible multi-center clinical trial submission
/// packages stripped of raw patient PII while binding complete CMC and QSP provenance to post-quantum signatures.
/// </para>
/// <para>
/// <strong>How it works:</strong> Aggregates patient DNA blueprints, lot CoAs, reversal antidotes, cold-chain custody,
/// pharmacogenomics, 180-day depot simulations, epigenetic specificity, 10-year durability, pharmacovigilance,
/// and 21 CFR Part 11 ledger integrity without LINQ allocations.
/// </para>
/// </remarks>
public sealed class RegulatoryDossierService : IRegulatoryDossierService
{
    /// <summary>
    /// Default clinical trial sponsor label used when omitted from compilation requests.
    /// </summary>
    private const string DefaultSponsorName = "NanoWeaverHrt Sovereign Research Consortium";

    /// <summary>
    /// Default clinical trial site identifier used when omitted from compilation requests.
    /// </summary>
    private const string DefaultSiteCode = "SITE-SOVEREIGN-001";

    /// <summary>
    /// Canonical redacted placeholder value substituted into patient-facing summary projections.
    /// </summary>
    private const string RedactedLabel = "[REDACTED]";

    /// <summary>
    /// Expected post-quantum digital signature suite identifier sealing dossier bundles.
    /// </summary>
    private const string ExpectedSignatureAlgorithm = "ML-DSA-65";

    /// <summary>
    /// Canonical JSON serialization options ensuring deterministic key casing and compact formatting for SHA-256 signing.
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Persistence repository storing AES-256-GCM encrypted dossier snapshots.
    /// </summary>
    private readonly IRegulatoryDossierRepository _repository;

    /// <summary>
    /// Repository supplying patient digitized DNA blueprints and pharmacogenomic screening results.
    /// </summary>
    private readonly IPatientDnaRepository _patientDnaRepository;

    /// <summary>
    /// Repository supplying patient semi-annual hydrogel depot formulations.
    /// </summary>
    private readonly IDepotFormulationRepository _depotRepository;

    /// <summary>
    /// Deterministic 180-day pharmacokinetic time-release depot simulation engine.
    /// </summary>
    private readonly ITimeReleaseDepotService _depotService;

    /// <summary>
    /// Repository supplying wet-lab fabrication lots, QC checks, and local/federated recalls.
    /// </summary>
    private readonly IFabricationLotRepository _lotRepository;

    /// <summary>
    /// Fabrication service reconstructing and verifying ML-DSA-65 Certificates of Analysis.
    /// </summary>
    private readonly IFabricationLotService _lotService;

    /// <summary>
    /// Reversal antidote service reporting standby pairing status and ML-DSA-65 signature validity.
    /// </summary>
    private readonly IReversalAntidoteService _antidoteService;

    /// <summary>
    /// Cold-chain stability and SHA-256 custody ledger evaluator.
    /// </summary>
    private readonly IColdChainStabilityService _coldChainService;

    /// <summary>
    /// Post-implantation clinical pharmacovigilance and CARPA/TLR9 surveillance evaluator.
    /// </summary>
    private readonly IClinicalPharmacovigilanceService _pharmacovigilanceService;

    /// <summary>
    /// Repository supplying epigenetic toggle circuits and off-target CFD specificity metrics.
    /// </summary>
    private readonly IEpigeneticCircuitRepository _circuitRepository;

    /// <summary>
    /// Repository supplying multi-year coupled durability forecast runs.
    /// </summary>
    private readonly IMultiYearHomeostasisRepository _multiYearRepository;

    /// <summary>
    /// FDA 21 CFR Part 11 audit ledger and HIPAA Safe Harbor pseudonymization service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Key management service providing the node's persistent ML-DSA-65 signing key.
    /// </summary>
    private readonly IKeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum signature verification engine.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Registry of active and revoked trusted peer laboratory signing keys.
    /// </summary>
    private readonly IPeerKeyRegistry _peerKeyRegistry;

    /// <summary>
    /// Authenticated AES-256-GCM envelope protector encrypting serialized bundles at rest.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Deterministic UTC clock source (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegulatoryDossierService"/> class.
    /// </summary>
    /// <param name="repository">Encrypted regulatory dossier repository.</param>
    /// <param name="patientDnaRepository">Patient DNA and clinical result repository.</param>
    /// <param name="depotRepository">Depot formulation repository.</param>
    /// <param name="depotService">180-day depot pharmacokinetic simulator.</param>
    /// <param name="lotRepository">Wet-lab fabrication lot and recall repository.</param>
    /// <param name="lotService">Wet-lab Certificate of Analysis reconstruction and verification service.</param>
    /// <param name="antidoteService">Paired reversal antidote verification service.</param>
    /// <param name="coldChainService">Cold-chain Arrhenius stability and custody ledger service.</param>
    /// <param name="pharmacovigilanceService">Clinical pharmacovigilance evaluator.</param>
    /// <param name="circuitRepository">Epigenetic circuit persistence repository.</param>
    /// <param name="multiYearRepository">Multi-year homeostasis forecast repository.</param>
    /// <param name="auditService">HIPAA Safe Harbor and 21 CFR Part 11 audit ledger service.</param>
    /// <param name="keyManagementService">Node ML-DSA-65 key management service.</param>
    /// <param name="cryptoProvider">Post-quantum signature verification engine.</param>
    /// <param name="peerKeyRegistry">Trusted peer laboratory key registry.</param>
    /// <param name="protector">AES-256-GCM PHI storage protector.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for unit testing.</param>
    public RegulatoryDossierService(
        IRegulatoryDossierRepository repository,
        IPatientDnaRepository patientDnaRepository,
        IDepotFormulationRepository depotRepository,
        ITimeReleaseDepotService depotService,
        IFabricationLotRepository lotRepository,
        IFabricationLotService lotService,
        IReversalAntidoteService antidoteService,
        IColdChainStabilityService coldChainService,
        IClinicalPharmacovigilanceService pharmacovigilanceService,
        IEpigeneticCircuitRepository circuitRepository,
        IMultiYearHomeostasisRepository multiYearRepository,
        IComplianceAuditService auditService,
        IKeyManagementService keyManagementService,
        IPostQuantumCryptoProvider cryptoProvider,
        IPeerKeyRegistry peerKeyRegistry,
        ProfileDataProtector protector,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Validates and stores all repository, simulation, cryptographic, and compliance dependencies.
        // WHY:  Prevents silent null dereferences during regulatory compilation or cross-node verification.
        // HOW:  Guards every constructor parameter with ArgumentNullException.ThrowIfNull and retains references.
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(patientDnaRepository);
        ArgumentNullException.ThrowIfNull(depotRepository);
        ArgumentNullException.ThrowIfNull(depotService);
        ArgumentNullException.ThrowIfNull(lotRepository);
        ArgumentNullException.ThrowIfNull(lotService);
        ArgumentNullException.ThrowIfNull(antidoteService);
        ArgumentNullException.ThrowIfNull(coldChainService);
        ArgumentNullException.ThrowIfNull(pharmacovigilanceService);
        ArgumentNullException.ThrowIfNull(circuitRepository);
        ArgumentNullException.ThrowIfNull(multiYearRepository);
        ArgumentNullException.ThrowIfNull(auditService);
        ArgumentNullException.ThrowIfNull(keyManagementService);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(peerKeyRegistry);
        ArgumentNullException.ThrowIfNull(protector);

        _repository = repository;
        _patientDnaRepository = patientDnaRepository;
        _depotRepository = depotRepository;
        _depotService = depotService;
        _lotRepository = lotRepository;
        _lotService = lotService;
        _antidoteService = antidoteService;
        _coldChainService = coldChainService;
        _pharmacovigilanceService = pharmacovigilanceService;
        _circuitRepository = circuitRepository;
        _multiYearRepository = multiYearRepository;
        _auditService = auditService;
        _keyManagementService = keyManagementService;
        _cryptoProvider = cryptoProvider;
        _peerKeyRegistry = peerKeyRegistry;
        _protector = protector;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<SignedRegulatoryDossierBundle> CompileDossierAsync(
        string patientId,
        CompileRegulatoryDossierRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Aggregates CMC, QSP, and compliance evidence, scores 10 fail-closed gates, seals with ML-DSA-65, encrypts, and audits.
        // WHY:  Produces an immutable, de-identified FDA IND / EMA IMPD bundle safe for multi-center regulatory audit.
        // HOW:  Resolves patient entities cleanly, runs 10 gate checks, hashes canonical manifest JSON, signs, encrypts, saves, and audits.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string trimmedPatientId = patientId.Trim();
        string trimmedOperatorId = operatorId.Trim();
        CompileRegulatoryDossierRequest effective = request ?? new CompileRegulatoryDossierRequest();

        string sponsorName = string.IsNullOrWhiteSpace(effective.SponsorName)
            ? DefaultSponsorName
            : effective.SponsorName.Trim();
        string siteCode = string.IsNullOrWhiteSpace(effective.ClinicalTrialSiteCode)
            ? DefaultSiteCode
            : effective.ClinicalTrialSiteCode.Trim();

        DateTimeOffset compiledAtUtc = NormalizeToMicroseconds(_utcNow());
        string prefix = effective.SubmissionFramework == RegulatorySubmissionFramework.EmaImpd ? "impd" : "ind";
        string dossierId = $"{prefix}_{Guid.NewGuid():N}"[..24];
        string pseudonym = _auditService.PseudonymizeSubjectId(trimmedPatientId);

        List<DossierReadinessGate> gates = new(10);
        List<string> blockers = [];

        // -----------------------------------------------------------------
        // Resolve primary linked clinical & fabrication records (patient-scoped)
        // -----------------------------------------------------------------
        IReadOnlyList<PatientDigitalDnaRecord> dnaRecords =
            await _patientDnaRepository.GetDnaRecordsAsync(trimmedPatientId, cancellationToken).ConfigureAwait(false);
        PatientDigitalDnaRecord? dna = dnaRecords.Count > 0 ? dnaRecords[0] : null;

        FabricationLotRecord? lot = await ResolvePatientLotAsync(trimmedPatientId, effective.LotId, cancellationToken).ConfigureAwait(false);
        DepotFormulationRecord? depot = await ResolvePatientDepotAsync(trimmedPatientId, effective.DepotId, lot, cancellationToken).ConfigureAwait(false);
        EpigeneticCircuitRecord? circuit = await ResolvePatientCircuitAsync(trimmedPatientId, effective.CircuitId, cancellationToken).ConfigureAwait(false);

        // -----------------------------------------------------------------
        // Section A: Quality / Chemistry, Manufacturing, and Controls (CMC)
        // -----------------------------------------------------------------
        (string? scaffoldSha256, double? kdNm, double? gcPct) = EvaluateDnaBlueprintGate(dna, gates, blockers);

        (bool lotCoaPassed,
         string? lotCoaSha,
         bool lotRevoked,
         ReversalAntidotePairingReport? antidoteReport,
         LotStabilityReport? stabilityReport) = await EvaluateLotAntidoteAndColdChainAsync(lot, gates, blockers, cancellationToken).ConfigureAwait(false);

        DossierQualityCmcSection qualityCmc = new()
        {
            DnaBlueprintId = dna?.Id,
            TargetTissue = dna?.TargetTissue,
            ActiveLigand = dna?.ActiveLigand,
            NanocarrierGeometry = dna?.NanocarrierGeometry,
            TargetAptamerKdNm = kdNm,
            ScaffoldFastaSha256 = scaffoldSha256,
            ScaffoldGcPercentage = gcPct,
            LotId = lot?.LotId,
            LotStatus = lot?.LotStatus.ToString(),
            OpentronsProtocolSha256 = lot?.OpentronsProtocolSha256,
            LotCertificatePayloadSha256 = lotCoaSha,
            LotCertificateVerified = lotCoaPassed,
            LotRevoked = lotRevoked,
            AntidoteId = antidoteReport?.AntidoteId,
            AntidoteStatus = antidoteReport?.Status.ToString(),
            AntidoteSignatureVerified = antidoteReport is not null && antidoteReport.SignatureVerified,
            AntidotePayloadSha256 = antidoteReport?.PairedPayloadSha256,
            ColdChainRemainingPotencyPercent = stabilityReport?.RemainingPotencyPercent,
            ColdChainSafeForImplantation = stabilityReport is not null && stabilityReport.IsSafeForImplantation,
            CustodyChainValid = stabilityReport is not null && stabilityReport.CustodyChainValid,
            CustodyHandoffCount = stabilityReport?.CustodyHandoffCount ?? 0
        };

        // -----------------------------------------------------------------
        // Section B: Preclinical & Quantitative Systems Pharmacology (QSP)
        // -----------------------------------------------------------------
        PatientResultRecord? pgxRecord = await ResolveLatestPharmacogenomicsResultAsync(trimmedPatientId, cancellationToken).ConfigureAwait(false);
        bool pgxCleared = EvaluatePharmacogenomicsGate(pgxRecord, gates, blockers);

        TimeReleaseSimulationResult? depotSim = EvaluateDepot180DayGate(depot, gates, blockers);
        EvaluateEpigeneticCircuitGate(circuit, gates, blockers);
        MultiYearHomeostasisRunRecord? multiYearRun = await EvaluateMultiYearGateAsync(depot, gates, blockers, cancellationToken).ConfigureAwait(false);

        DossierPreclinicalQspSection preclinicalQsp = new()
        {
            PharmacogenomicsResultId = pgxRecord?.Id,
            PharmacogenomicsStatus = pgxRecord?.Status,
            PharmacogenomicsCleared = pgxCleared,
            DepotId = depot?.DepotId,
            DepotPolymerMatrix = depot?.PolymerMatrixType,
            Depot180DayTtrPercent = depotSim?.TimeInTargetRangePercent,
            DepotBurstSuppressed = depotSim is not null && depotSim.BurstSuppressed,
            EpigeneticCircuitId = circuit?.CircuitId,
            EpigeneticCircuitStatus = circuit?.CircuitStatus.ToString(),
            EpigeneticMinSpecificityPercent = circuit?.MinOffTargetSpecificityScore,
            EpigeneticSeparatrixMargin = circuit?.SeparatrixMargin,
            MultiYearRunId = multiYearRun?.RunId,
            MultiYearDurabilityStatus = multiYearRun?.DurabilityStatus.ToString(),
            MultiYearProjectedTtrPercent = multiYearRun?.ProjectedTenYearTtrPercent,
            MultiYearHorizonYears = multiYearRun?.HorizonYears
        };

        // -----------------------------------------------------------------
        // Section C: HIPAA, Pharmacovigilance & 21 CFR Part 11 Compliance
        // -----------------------------------------------------------------
        LotPharmacovigilanceReport? pvReport = await EvaluatePharmacovigilanceGateAsync(lot, gates, blockers, cancellationToken).ConfigureAwait(false);
        AuditLedgerVerificationResult ledger = _auditService.VerifyLedgerIntegrity();
        EvaluateAuditLedgerGate(ledger, gates, blockers);

        DossierComplianceSection complianceSection = new()
        {
            HipaaSafeHarborPseudonym = pseudonym,
            HipaaDeIdentificationVerified = !string.IsNullOrWhiteSpace(pseudonym),
            PharmacovigilancePosture = pvReport?.Posture.ToString(),
            PharmacovigilanceQuarantineTriggered = pvReport is not null && pvReport.IsQuarantineTriggered,
            PharmacovigilanceEmergencyRecallRecommended = pvReport is not null && pvReport.EmergencyRecallRecommended,
            AuditLedgerIntact = ledger.IsValid,
            AuditLedgerRecordsVerified = ledger.TotalRecordsVerified,
            AuditLedgerSigningKeyId = ledger.SigningKeyId
        };

        // Compute score deterministically without LINQ allocations.
        int passedCount = 0;
        for (int i = 0; i < gates.Count; i++)
        {
            if (gates[i].Passed)
            {
                passedCount++;
            }
        }

        double completenessScore = passedCount * 10.0;
        bool isSubmissionReady = passedCount == 10;

        RegulatoryDossierManifest manifest = new()
        {
            DossierId = dossierId,
            SubmissionFramework = effective.SubmissionFramework,
            PseudonymizedPatientId = pseudonym,
            SponsorName = sponsorName,
            ClinicalTrialSiteCode = siteCode,
            CompletenessScore = completenessScore,
            IsSubmissionReady = isSubmissionReady,
            CompiledAtUtc = compiledAtUtc,
            QualityCmc = qualityCmc,
            PreclinicalQsp = preclinicalQsp,
            Compliance = complianceSection,
            ReadinessGates = gates,
            BlockingReasons = blockers
        };

        byte[] canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, CanonicalJsonOptions);
        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        PqcSignature signature = _keyManagementService.SignWithNodeSigningKey(canonicalBytes);
        PqcKeyMaterial nodeKey = _keyManagementService.GetNodeSigningKey();

        SignedRegulatoryDossierBundle bundle = new()
        {
            BundleVersion = "nanoweaver-ind-dossier-v1",
            Manifest = manifest,
            PayloadSha256 = payloadSha256,
            SignatureAlgorithm = ExpectedSignatureAlgorithm,
            SignatureBase64 = signature.SignatureBase64,
            SignerKeyId = signature.SignerKeyId,
            SignerPublicKeyBase64 = nodeKey.PublicKeyBase64
        };

        string bundleJson = JsonSerializer.Serialize(bundle, CanonicalJsonOptions);
        string encryptedBundle = _protector.EncryptRequiredField(bundleJson);

        RegulatoryDossierRecord archiveRecord = new()
        {
            DossierId = dossierId,
            PatientId = trimmedPatientId,
            LotId = lot?.LotId,
            SubmissionFramework = effective.SubmissionFramework,
            PayloadSha256 = payloadSha256,
            SignatureBase64 = signature.SignatureBase64,
            SignerKeyId = signature.SignerKeyId,
            BundleJsonEncrypted = encryptedBundle,
            CompletenessScore = completenessScore,
            IsSubmissionReady = isSubmissionReady,
            CompiledBy = trimmedOperatorId,
            CompiledAtUtc = compiledAtUtc
        };

        await _repository.SaveDossierAsync(archiveRecord, cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            trimmedOperatorId,
            "COMPLIANCE_IND_DOSSIER_COMPILE",
            $"regulatory-dossier:{dossierId}",
            JsonSerializer.Serialize(new
            {
                dossierId,
                pseudonymizedPatientId = pseudonym,
                framework = effective.SubmissionFramework.ToString(),
                completenessScore,
                isSubmissionReady,
                blockerCount = blockers.Count,
                payloadSha256,
                signerKeyId = signature.SignerKeyId
            }));

        return bundle;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RegulatoryDossierSummary>> ListDossiersForPatientAsync(
        string patientId,
        bool redactForPatient = false,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries stored dossier archive rows for patientId and projects API summaries with optional PII/key redaction.
        // WHY:  Allows Administrators full audit visibility while shielding operator names and key IDs in Patient self-queries.
        // HOW:  Fetches newest-first rows from repository and iterates sequentially without LINQ allocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        IReadOnlyList<RegulatoryDossierRecord> records =
            await _repository.ListDossiersByPatientAsync(patientId.Trim(), cancellationToken).ConfigureAwait(false);

        List<RegulatoryDossierSummary> summaries = new(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            RegulatoryDossierRecord r = records[i];
            summaries.Add(new RegulatoryDossierSummary
            {
                DossierId = r.DossierId,
                PatientId = r.PatientId,
                LotId = r.LotId,
                SubmissionFramework = r.SubmissionFramework,
                PayloadSha256 = r.PayloadSha256,
                SignerKeyId = redactForPatient ? RedactedLabel : r.SignerKeyId,
                CompletenessScore = r.CompletenessScore,
                IsSubmissionReady = r.IsSubmissionReady,
                CompiledBy = redactForPatient ? RedactedLabel : r.CompiledBy,
                CompiledAtUtc = r.CompiledAtUtc,
                IsRedactedForPatient = redactForPatient
            });
        }

        return summaries;
    }

    /// <inheritdoc/>
    public async Task<SignedRegulatoryDossierBundle?> GetDecryptedBundleAsync(
        string dossierId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads and AES-256-GCM decrypts an archived SignedRegulatoryDossierBundle by primary key.
        // WHY:  Powers Administrator-authorized portable electronic submission bundle export without storing plaintext JSON in SQL.
        // HOW:  Fetches record via repository, decrypts BundleJsonEncrypted via ProfileDataProtector, and deserializes JSON.
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierId);

        RegulatoryDossierRecord? record =
            await _repository.GetDossierByIdAsync(dossierId.Trim(), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        string bundleJson = _protector.DecryptRequiredField(record.BundleJsonEncrypted);
        return JsonSerializer.Deserialize<SignedRegulatoryDossierBundle>(bundleJson, CanonicalJsonOptions);
    }

    /// <inheritdoc/>
    public async Task<RegulatoryDossierVerificationReport> VerifyDossierBundleAsync(
        SignedRegulatoryDossierBundle bundle,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Recomputes canonical manifest SHA-256 digest, verifies ML-DSA-65 signature, resolves trust anchor, and emits audit log.
        // WHY:  Enables independent regulatory auditors and multi-center trial clinics to validate downloaded dossier bundles fail-closed.
        // HOW:  Validates inputs, hashes canonical manifest JSON, checks PQC signature and node/peer key registry, logs COMPLIANCE_IND_DOSSIER_VERIFY.
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(bundle.Manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string trimmedOperatorId = operatorId.Trim();
        DateTimeOffset verifiedAtUtc = NormalizeToMicroseconds(_utcNow());

        byte[] canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(bundle.Manifest, CanonicalJsonOptions);
        string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        bool hashValid = string.Equals(computedSha256, bundle.PayloadSha256, StringComparison.OrdinalIgnoreCase);

        bool signatureValid = string.Equals(bundle.SignatureAlgorithm, ExpectedSignatureAlgorithm, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(bundle.SignatureBase64)
            && !string.IsNullOrWhiteSpace(bundle.SignerPublicKeyBase64)
            && _cryptoProvider.VerifySignature(canonicalBytes, bundle.SignatureBase64, bundle.SignerPublicKeyBase64);

        RegulatoryDossierVerificationReport report;
        if (!hashValid || !signatureValid)
        {
            string failureReason = !hashValid
                ? "Cryptographic verification failed: canonical manifest SHA-256 digest does not match payloadSha256."
                : "Cryptographic verification failed: NIST FIPS 204 ML-DSA-65 digital signature is invalid.";

            report = new RegulatoryDossierVerificationReport
            {
                DossierId = bundle.Manifest.DossierId,
                SubmissionFramework = bundle.Manifest.SubmissionFramework,
                PayloadSha256 = computedSha256,
                HashValid = hashValid,
                SignatureValid = signatureValid,
                IsTrusted = false,
                IsSubmissionReady = bundle.Manifest.IsSubmissionReady,
                TrustSource = RegulatoryDossierTrustSource.InvalidSignature,
                TrustedPeerLabel = null,
                SignerKeyId = bundle.SignerKeyId,
                Summary = failureReason,
                VerifiedAtUtc = verifiedAtUtc
            };
        }
        else
        {
            report = await ResolveTrustAnchorReportAsync(bundle, computedSha256, verifiedAtUtc, cancellationToken).ConfigureAwait(false);
        }

        _auditService.LogAction(
            trimmedOperatorId,
            "COMPLIANCE_IND_DOSSIER_VERIFY",
            $"regulatory-dossier:{bundle.Manifest.DossierId}",
            JsonSerializer.Serialize(new
            {
                dossierId = report.DossierId,
                hashValid = report.HashValid,
                signatureValid = report.SignatureValid,
                isTrusted = report.IsTrusted,
                trustSource = report.TrustSource.ToString(),
                signerKeyId = report.SignerKeyId
            }));

        return report;
    }

    /// <summary>
    /// Resolves whether a mathematically valid dossier signature originates from this sovereign node,
    /// an active peer laboratory key, a revoked peer key, or an untrusted signer.
    /// </summary>
    /// <param name="bundle">Canonically verified dossier bundle.</param>
    /// <param name="computedSha256">Recomputed lowercase SHA-256 hex digest.</param>
    /// <param name="verifiedAtUtc">Timestamp assigned to the verification report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete <see cref="RegulatoryDossierVerificationReport"/> classifying trust status.</returns>
    private async Task<RegulatoryDossierVerificationReport> ResolveTrustAnchorReportAsync(
        SignedRegulatoryDossierBundle bundle,
        string computedSha256,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        // WHAT: Matches the validated signer key against the local node ML-DSA-65 key and IPeerKeyRegistry without LINQ.
        // WHY:  Distinguishes self-sealed local dossiers from trusted peer trials and fails closed on revoked/unknown signers.
        // HOW:  Compares KeyId and PublicKeyBase64 against local key first, then scans registered peer entries sequentially.
        PqcKeyMaterial localKey = _keyManagementService.GetNodeSigningKey();
        if (string.Equals(bundle.SignerKeyId, localKey.KeyId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(bundle.SignerPublicKeyBase64, localKey.PublicKeyBase64, StringComparison.Ordinal))
        {
            return new RegulatoryDossierVerificationReport
            {
                DossierId = bundle.Manifest.DossierId,
                SubmissionFramework = bundle.Manifest.SubmissionFramework,
                PayloadSha256 = computedSha256,
                HashValid = true,
                SignatureValid = true,
                IsTrusted = true,
                IsSubmissionReady = bundle.Manifest.IsSubmissionReady,
                TrustSource = RegulatoryDossierTrustSource.LocalNode,
                TrustedPeerLabel = null,
                SignerKeyId = bundle.SignerKeyId,
                Summary = "Verified ML-DSA-65 regulatory dossier bundle sealed by this sovereign node.",
                VerifiedAtUtc = verifiedAtUtc
            };
        }

        IReadOnlyList<PeerKeyRecord> peers = await _peerKeyRegistry.ListAsync(cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < peers.Count; i++)
        {
            PeerKeyRecord peer = peers[i];
            if (peer.Algorithm == KeyAlgorithm.MlDsa65
                && string.Equals(peer.KeyId, bundle.SignerKeyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(peer.PublicKeyBase64, bundle.SignerPublicKeyBase64, StringComparison.Ordinal))
            {
                bool active = peer.IsActive;
                return new RegulatoryDossierVerificationReport
                {
                    DossierId = bundle.Manifest.DossierId,
                    SubmissionFramework = bundle.Manifest.SubmissionFramework,
                    PayloadSha256 = computedSha256,
                    HashValid = true,
                    SignatureValid = true,
                    IsTrusted = active,
                    IsSubmissionReady = bundle.Manifest.IsSubmissionReady,
                    TrustSource = active ? RegulatoryDossierTrustSource.TrustedPeer : RegulatoryDossierTrustSource.RevokedSigner,
                    TrustedPeerLabel = peer.Label,
                    SignerKeyId = bundle.SignerKeyId,
                    Summary = active
                        ? $"Verified ML-DSA-65 regulatory dossier bundle issued by trusted peer laboratory '{peer.Label}'."
                        : $"Untrusted: peer laboratory key '{peer.Label}' has been permanently revoked.",
                    VerifiedAtUtc = verifiedAtUtc
                };
            }
        }

        return new RegulatoryDossierVerificationReport
        {
            DossierId = bundle.Manifest.DossierId,
            SubmissionFramework = bundle.Manifest.SubmissionFramework,
            PayloadSha256 = computedSha256,
            HashValid = true,
            SignatureValid = true,
            IsTrusted = false,
            IsSubmissionReady = bundle.Manifest.IsSubmissionReady,
            TrustSource = RegulatoryDossierTrustSource.UntrustedSigner,
            TrustedPeerLabel = null,
            SignerKeyId = bundle.SignerKeyId,
            Summary = "Signature is mathematically valid, but the signer ML-DSA-65 key is not registered in this node's trusted peer key registry.",
            VerifiedAtUtc = verifiedAtUtc
        };
    }

    /// <summary>
    /// Resolves the target fabrication lot belonging strictly to <paramref name="patientId"/>.
    /// </summary>
    /// <param name="patientId">Normalized owning patient identifier.</param>
    /// <param name="requestedLotId">Optional explicit fabrication lot identifier from request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The patient-owned <see cref="FabricationLotRecord"/>, or <see langword="null"/> when missing or mismatched.</returns>
    private async Task<FabricationLotRecord?> ResolvePatientLotAsync(
        string patientId,
        string? requestedLotId,
        CancellationToken cancellationToken)
    {
        // WHAT: Resolves the patient-owned fabrication lot using explicit LotId override or newest lot lookup.
        // WHY:  Enforces strict patient ownership so cross-patient lot references fail closed cleanly.
        // HOW:  Validates PatientId equality when LotId is explicitly specified; otherwise picks index 0 from GetLotsByPatientIdAsync.
        if (!string.IsNullOrWhiteSpace(requestedLotId))
        {
            FabricationLotRecord? candidate = await _lotRepository.GetLotByIdAsync(requestedLotId.Trim(), cancellationToken).ConfigureAwait(false);
            if (candidate is not null && string.Equals(candidate.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            return null;
        }

        IReadOnlyList<FabricationLotRecord> patientLots =
            await _lotRepository.GetLotsByPatientIdAsync(patientId, cancellationToken).ConfigureAwait(false);
        return patientLots.Count > 0 ? patientLots[0] : null;
    }

    /// <summary>
    /// Resolves the target semi-annual depot formulation belonging strictly to <paramref name="patientId"/>.
    /// </summary>
    /// <param name="patientId">Normalized owning patient identifier.</param>
    /// <param name="requestedDepotId">Optional explicit depot identifier from request.</param>
    /// <param name="resolvedLot">Optional resolved fabrication lot supplying a parent depot link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The patient-owned <see cref="DepotFormulationRecord"/>, or <see langword="null"/> when missing or mismatched.</returns>
    private async Task<DepotFormulationRecord?> ResolvePatientDepotAsync(
        string patientId,
        string? requestedDepotId,
        FabricationLotRecord? resolvedLot,
        CancellationToken cancellationToken)
    {
        // WHAT: Resolves the patient-owned depot formulation from explicit DepotId, linked lot DepotId, or newest patient depot.
        // WHY:  Prevents linking a foreign patient's depot simulation into another subject's clinical dossier.
        // HOW:  Checks PatientId equality on explicit or lot-linked lookups before falling back to GetDepotsByPatientIdAsync.
        if (!string.IsNullOrWhiteSpace(requestedDepotId))
        {
            DepotFormulationRecord? candidate =
                await _depotRepository.GetDepotFormulationByIdAsync(requestedDepotId.Trim(), cancellationToken).ConfigureAwait(false);
            if (candidate is not null && string.Equals(candidate.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            return null;
        }

        if (resolvedLot is not null)
        {
            DepotFormulationRecord? linked =
                await _depotRepository.GetDepotFormulationByIdAsync(resolvedLot.DepotId, cancellationToken).ConfigureAwait(false);
            if (linked is not null && string.Equals(linked.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            {
                return linked;
            }
        }

        IReadOnlyList<DepotFormulationRecord> depots =
            await _depotRepository.GetDepotsByPatientIdAsync(patientId, cancellationToken).ConfigureAwait(false);
        return depots.Count > 0 ? depots[0] : null;
    }

    /// <summary>
    /// Resolves the target epigenetic reprogramming circuit belonging strictly to <paramref name="patientId"/>.
    /// </summary>
    /// <param name="patientId">Normalized owning patient identifier.</param>
    /// <param name="requestedCircuitId">Optional explicit circuit identifier from request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The patient-owned <see cref="EpigeneticCircuitRecord"/>, or <see langword="null"/> when missing or mismatched.</returns>
    private async Task<EpigeneticCircuitRecord?> ResolvePatientCircuitAsync(
        string patientId,
        string? requestedCircuitId,
        CancellationToken cancellationToken)
    {
        // WHAT: Resolves a patient-owned epigenetic circuit using explicit CircuitId or newest approved/recorded circuit.
        // WHY:  Prefers an Approved circuit deterministically while preserving fail-closed visibility of Draft/Blocked circuits.
        // HOW:  Scans ListCircuitsByPatientAsync in reverse chronological order without LINQ allocations.
        if (!string.IsNullOrWhiteSpace(requestedCircuitId))
        {
            EpigeneticCircuitRecord? candidate =
                await _circuitRepository.GetCircuitByIdAsync(requestedCircuitId.Trim(), cancellationToken).ConfigureAwait(false);
            if (candidate is not null && string.Equals(candidate.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            return null;
        }

        IReadOnlyList<EpigeneticCircuitRecord> circuits =
            await _circuitRepository.ListCircuitsByPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
        for (int i = circuits.Count - 1; i >= 0; i--)
        {
            if (circuits[i].CircuitStatus == EpigeneticCircuitStatus.Approved)
            {
                return circuits[i];
            }
        }

        return circuits.Count > 0 ? circuits[^1] : null;
    }

    /// <summary>
    /// Evaluates Gate 1 (<c>CMC_DNA_BLUEPRINT</c>) verifying digitized DNA origami scaffold sequence and aptamer Kd.
    /// </summary>
    /// <param name="dna">Resolved patient DNA blueprint record, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <returns>Tuple of scaffold SHA-256 digest, target Kd (nM), and GC%.</returns>
    private static (string? ScaffoldSha256, double? TargetKdNm, double? GcPercent) EvaluateDnaBlueprintGate(
        PatientDigitalDnaRecord? dna,
        List<DossierReadinessGate> gates,
        List<string> blockers)
    {
        // WHAT: Checks presence of a patient DNA origami scaffold FASTA sequence and strictly positive aptamer Kd.
        // WHY:  IND CMC Quality mandates reproducible sequence provenance and receptor-gating affinity parameters.
        // HOW:  Computes SHA-256 of UTF-8 ScaffoldFasta when present and appends gate evaluation and optional blocker.
        if (dna is null || string.IsNullOrWhiteSpace(dna.ScaffoldFasta) || dna.TargetAptamerKdNm <= 0.0)
        {
            const string reason = "Missing or invalid digitized DNA origami blueprint (scaffold FASTA sequence and Kd > 0 nM required).";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_DNA_BLUEPRINT",
                Section = "QualityCmc",
                Passed = false,
                Summary = reason
            });
            blockers.Add(reason);
            return (null, dna?.TargetAptamerKdNm, dna?.GcPercentage);
        }

        string scaffoldSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dna.ScaffoldFasta)));
        gates.Add(new DossierReadinessGate
        {
            GateCode = "CMC_DNA_BLUEPRINT",
            Section = "QualityCmc",
            Passed = true,
            Summary = string.Format(
                CultureInfo.InvariantCulture,
                "DNA blueprint '{0}' verified ({1}, Kd {2:F2} nM, GC {3:F1}%).",
                dna.Id,
                dna.NanocarrierGeometry,
                dna.TargetAptamerKdNm,
                dna.GcPercentage)
        });
        return (scaffoldSha256, dna.TargetAptamerKdNm, dna.GcPercentage);
    }

    /// <summary>
    /// Evaluates Gates 2, 3, and 4 covering wet-lab 5-gate CoA integrity, paired reversal antidote standby,
    /// and cold-chain Arrhenius stability + custody hash chain.
    /// </summary>
    /// <param name="lot">Resolved patient fabrication lot, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple capturing CoA pass/digest/revocation state alongside resolved antidote and stability reports.</returns>
    private async Task<(bool CoaPassed, string? CoaSha, bool Revoked, ReversalAntidotePairingReport? Antidote, LotStabilityReport? Stability)>
        EvaluateLotAntidoteAndColdChainAsync(
            FabricationLotRecord? lot,
            List<DossierReadinessGate> gates,
            List<string> blockers,
            CancellationToken cancellationToken)
    {
        // WHAT: Evaluates CMC lot CoA, reversal antidote pairing, and cold-chain thermal/custody gates fail-closed.
        // WHY:  All three physical batch gates depend on a patient-owned wet-lab FabricationLotRecord.
        // HOW:  Short-circuits all three gates with explicit blockers when no lot exists; otherwise validates each independently.
        if (lot is null)
        {
            const string coaMissing = "No patient-owned wet-lab fabrication lot exists for CMC CoA verification.";
            const string antiMissing = "No fabrication lot exists to verify an active paired ssDNA reversal antidote.";
            const string coldMissing = "No fabrication lot exists to verify Arrhenius cold-chain potency and custody chain.";
            gates.Add(new DossierReadinessGate { GateCode = "CMC_LOT_COA", Section = "QualityCmc", Passed = false, Summary = coaMissing });
            gates.Add(new DossierReadinessGate { GateCode = "CMC_REVERSAL_ANTIDOTE", Section = "QualityCmc", Passed = false, Summary = antiMissing });
            gates.Add(new DossierReadinessGate { GateCode = "CMC_COLD_CHAIN_CUSTODY", Section = "QualityCmc", Passed = false, Summary = coldMissing });
            blockers.Add(coaMissing);
            blockers.Add(antiMissing);
            blockers.Add(coldMissing);
            return (false, null, false, null, null);
        }

        // Gate 2: Wet-Lab CoA & Non-Revocation
        IReadOnlyList<FabricationQcCheckRecord> checks =
            await _lotRepository.GetQcChecksByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        FabricationLotRevocationRecord? localRevocation =
            await _lotRepository.GetLotRevocationAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        FederatedLotRevocationRecord? federatedRevocation =
            await _lotRepository.GetFederatedRevocationAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        bool lotRevoked = localRevocation is not null || federatedRevocation is not null;

        FabricationLotCertificate? cert = _lotService.ReconstructCertificate(lot, checks);
        bool certValid = cert is not null && _lotService.VerifyLotCertificate(cert);
        bool coaPassed = lot.LotStatus == FabricationLotStatus.Released
            && !string.IsNullOrWhiteSpace(lot.OpentronsProtocolSha256)
            && !lotRevoked
            && certValid;

        if (coaPassed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_LOT_COA",
                Section = "QualityCmc",
                Passed = true,
                Summary = $"Fabrication lot '{lot.LotId}' passed 5-gate QC release with verified ML-DSA-65 CoA and zero recalls."
            });
        }
        else
        {
            string coaReason = $"Fabrication lot '{lot.LotId}' failed CMC CoA gate (status {lot.LotStatus}, revoked={lotRevoked}, certVerified={certValid}).";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_LOT_COA",
                Section = "QualityCmc",
                Passed = false,
                Summary = coaReason
            });
            blockers.Add(coaReason);
        }

        // Gate 3: Paired Reversal Antidote
        ReversalAntidotePairingReport? antidote =
            await _antidoteService.GetLatestAntidoteReportAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        bool antidotePassed = antidote is not null
            && antidote.Status == ReversalAntidoteStatus.ActiveStandby
            && antidote.SignatureVerified;

        if (antidotePassed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_REVERSAL_ANTIDOTE",
                Section = "QualityCmc",
                Passed = true,
                Summary = $"Reversal antidote '{antidote!.AntidoteId}' is ActiveStandby with verified ML-DSA-65 binding." // WHY `!`: guarded by antidotePassed check above.
            });
        }
        else
        {
            string antiReason = antidote is null
                ? $"No paired sequence-specific ssDNA reversal antidote found for lot '{lot.LotId}'."
                : $"Paired reversal antidote '{antidote.AntidoteId}' failed standby gate (status={antidote.Status}, sigValid={antidote.SignatureVerified}).";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_REVERSAL_ANTIDOTE",
                Section = "QualityCmc",
                Passed = false,
                Summary = antiReason
            });
            blockers.Add(antiReason);
        }

        // Gate 4: Cold-Chain Potency & SHA-256 Custody Chain
        LotStabilityReport? stability =
            await _coldChainService.EvaluateLotStabilityAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        bool coldPassed = stability is not null
            && stability.IsSafeForImplantation
            && stability.CustodyChainValid;

        if (coldPassed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_COLD_CHAIN_CUSTODY",
                Section = "QualityCmc",
                Passed = true,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Cold-chain stability safe ({0:F2}% potency remaining, custody chain valid across {1} handoff(s)).",
                    stability!.RemainingPotencyPercent, // WHY `!`: guarded by coldPassed check above.
                    stability.CustodyHandoffCount)
            });
        }
        else
        {
            string coldReason = stability is null
                ? $"Cold-chain stability telemetry missing for lot '{lot.LotId}'."
                : $"Lot '{lot.LotId}' failed cold-chain/custody gate (safe={stability.IsSafeForImplantation}, custodyValid={stability.CustodyChainValid}).";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "CMC_COLD_CHAIN_CUSTODY",
                Section = "QualityCmc",
                Passed = false,
                Summary = coldReason
            });
            blockers.Add(coldReason);
        }

        return (coaPassed, cert?.PayloadSha256, lotRevoked, antidote, stability);
    }

    /// <summary>
    /// Scans patient clinical results without LINQ for the newest pharmacogenomics assessment record.
    /// </summary>
    /// <param name="patientId">Patient identifier whose results are queried.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest <see cref="PatientResultRecord"/> with <c>ResultType == "Pharmacogenomics"</c>, or <see langword="null"/>.</returns>
    private async Task<PatientResultRecord?> ResolveLatestPharmacogenomicsResultAsync(
        string patientId,
        CancellationToken cancellationToken)
    {
        // WHAT: Locates the newest pharmacogenomics screening result for patientId.
        // WHY:  GetResultsAsync returns newest-first records across all result categories.
        // HOW:  Iterates sequentially and returns the first record matching ResultType "Pharmacogenomics".
        IReadOnlyList<PatientResultRecord> results =
            await _patientDnaRepository.GetResultsAsync(patientId, cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < results.Count; i++)
        {
            if (string.Equals(results[i].ResultType, "Pharmacogenomics", StringComparison.OrdinalIgnoreCase))
            {
                return results[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates Gate 5 (<c>QSP_PHARMACOGENOMICS</c>) verifying clearance against thrombosis and CYP alerts.
    /// </summary>
    /// <param name="pgxRecord">Resolved latest pharmacogenomic screening result, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <returns><see langword="true"/> when cleared; otherwise <see langword="false"/>.</returns>
    private static bool EvaluatePharmacogenomicsGate(
        PatientResultRecord? pgxRecord,
        List<DossierReadinessGate> gates,
        List<string> blockers)
    {
        // WHAT: Enforces pharmacogenomic safety clearance before permitting clinical trial dossier submission.
        // WHY:  Blocks submission when CYP450 or Factor V Leiden / thrombosis alerts are unresolved.
        // HOW:  Accepts only Optimal, Complete, or Verified status labels; records blocker on ElevatedRisk/Caution/missing.
        bool passed = pgxRecord is not null &&
            (string.Equals(pgxRecord.Status, "Optimal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pgxRecord.Status, "Complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pgxRecord.Status, "Verified", StringComparison.OrdinalIgnoreCase));

        if (passed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_PHARMACOGENOMICS",
                Section = "PreclinicalQsp",
                Passed = true,
                Summary = $"Pharmacogenomics safety screening cleared ({pgxRecord!.Status})." // WHY `!`: guarded by passed above.
            });
            return true;
        }

        string reason = pgxRecord is null
            ? "Missing mandatory Pharmacogenomics clinical safety assessment record."
            : $"Pharmacogenomics safety screen '{pgxRecord.Id}' failed clearance with status '{pgxRecord.Status}'.";
        gates.Add(new DossierReadinessGate
        {
            GateCode = "QSP_PHARMACOGENOMICS",
            Section = "PreclinicalQsp",
            Passed = false,
            Summary = reason
        });
        blockers.Add(reason);
        return false;
    }

    /// <summary>
    /// Evaluates Gate 6 (<c>QSP_DEPOT_180D</c>) by running 180-day hydrogel release and HPG kinetics modeling.
    /// </summary>
    /// <param name="depot">Resolved patient depot formulation, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <returns>Computed <see cref="TimeReleaseSimulationResult"/> when a depot exists; otherwise <see langword="null"/>.</returns>
    private TimeReleaseSimulationResult? EvaluateDepot180DayGate(
        DepotFormulationRecord? depot,
        List<DossierReadinessGate> gates,
        List<string> blockers)
    {
        // WHAT: Re-simulates 180-day hydrogel depot pharmacokinetics to verify TTR >= 95% and Day 7 burst suppression.
        // WHY:  Ensures reproducible in-silico PK evidence using normalized formulation parameters.
        // HOW:  Maps DepotFormulationRecord via DepotCalibrationMapper.ToBaselineParameters and checks TTR + BurstSuppressed.
        if (depot is null)
        {
            const string missing = "No patient-owned semi-annual depot formulation exists for 180-day QSP validation.";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_DEPOT_180D",
                Section = "PreclinicalQsp",
                Passed = false,
                Summary = missing
            });
            blockers.Add(missing);
            return null;
        }

        TimeReleaseDepotParameters parameters = DepotCalibrationMapper.ToBaselineParameters(depot);
        TimeReleaseSimulationResult sim = _depotService.Simulate180DayDepot(parameters);
        bool passed = sim.TimeInTargetRangePercent >= 95.0 && sim.BurstSuppressed;

        if (passed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_DEPOT_180D",
                Section = "PreclinicalQsp",
                Passed = true,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "180-day depot '{0}' verified ({1:F1}% TTR, Day 7 burst suppressed).",
                    depot.DepotId,
                    sim.TimeInTargetRangePercent)
            });
        }
        else
        {
            string reason = string.Format(
                CultureInfo.InvariantCulture,
                "180-day depot '{0}' failed QSP gate (TTR {1:F1}% < 95.0% or burstSuppressed={2}).",
                depot.DepotId,
                sim.TimeInTargetRangePercent,
                sim.BurstSuppressed);
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_DEPOT_180D",
                Section = "PreclinicalQsp",
                Passed = false,
                Summary = reason
            });
            blockers.Add(reason);
        }

        return sim;
    }

    /// <summary>
    /// Evaluates Gate 7 (<c>QSP_EPIGENETIC_CIRCUIT</c>) verifying ML-DSA-65 approval and >= 98.0% CFD specificity.
    /// </summary>
    /// <param name="circuit">Resolved patient epigenetic toggle circuit, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    private static void EvaluateEpigeneticCircuitGate(
        EpigeneticCircuitRecord? circuit,
        List<DossierReadinessGate> gates,
        List<string> blockers)
    {
        // WHAT: Enforces permanent circuit approval status and genome-wide CFD off-target specificity >= 98.0%.
        // WHY:  Blocks IND/IMPD submission when guide-RNA off-target specificity or bistable approval is unmet.
        // HOW:  Validates CircuitStatus == Approved and MinOffTargetSpecificityScore >= 98.0.
        bool passed = circuit is not null
            && circuit.CircuitStatus == EpigeneticCircuitStatus.Approved
            && circuit.MinOffTargetSpecificityScore >= 98.0;

        if (passed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_EPIGENETIC_CIRCUIT",
                Section = "PreclinicalQsp",
                Passed = true,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Epigenetic circuit '{0}' Approved with {1:F2}% min CFD specificity.",
                    circuit!.CircuitId, // WHY `!`: guarded by passed above.
                    circuit.MinOffTargetSpecificityScore)
            });
            return;
        }

        string reason = circuit is null
            ? "Missing approved epigenetic steroidogenic toggle circuit blueprint."
            : string.Format(
                CultureInfo.InvariantCulture,
                "Epigenetic circuit '{0}' failed QSP specificity/approval gate (status={1}, minCfd={2:F2}%).",
                circuit.CircuitId,
                circuit.CircuitStatus,
                circuit.MinOffTargetSpecificityScore);
        gates.Add(new DossierReadinessGate
        {
            GateCode = "QSP_EPIGENETIC_CIRCUIT",
            Section = "PreclinicalQsp",
            Passed = false,
            Summary = reason
        });
        blockers.Add(reason);
    }

    /// <summary>
    /// Evaluates Gate 8 (<c>QSP_MULTIYEAR_HOMEOSTASIS</c>) checking 1-to-10-year mitotic durability persistence.
    /// </summary>
    /// <param name="depot">Resolved patient depot formulation, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest <see cref="MultiYearHomeostasisRunRecord"/> when present; otherwise <see langword="null"/>.</returns>
    private async Task<MultiYearHomeostasisRunRecord?> EvaluateMultiYearGateAsync(
        DepotFormulationRecord? depot,
        List<DossierReadinessGate> gates,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        // WHAT: Inspects the newest multi-year durability forecast for depot and enforces >= 95% projected TTR.
        // WHY:  Mitotic chromatin drift or untested long-term stability must fail closed prior to trial submission.
        // HOW:  Queries IMultiYearHomeostasisRepository.GetLatestRunByDepotAsync and validates status and TTR threshold.
        if (depot is null)
        {
            const string missingDepot = "No depot formulation exists to evaluate multi-year homeostasis durability.";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_MULTIYEAR_HOMEOSTASIS",
                Section = "PreclinicalQsp",
                Passed = false,
                Summary = missingDepot
            });
            blockers.Add(missingDepot);
            return null;
        }

        MultiYearHomeostasisRunRecord? run =
            await _multiYearRepository.GetLatestRunByDepotAsync(depot.DepotId, cancellationToken).ConfigureAwait(false);
        bool passed = run is not null
            && run.DurabilityStatus == MultiYearDurabilityStatus.PermanentHomeostasisVerified
            && run.ProjectedTenYearTtrPercent >= 95.0;

        if (passed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_MULTIYEAR_HOMEOSTASIS",
                Section = "PreclinicalQsp",
                Passed = true,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}-year homeostasis durability verified ({1:F1}% projected TTR).",
                    run!.HorizonYears, // WHY `!`: guarded by passed above.
                    run.ProjectedTenYearTtrPercent)
            });
        }
        else
        {
            string reason = run is null
                ? $"No multi-year homeostasis forecast run recorded for depot '{depot.DepotId}'."
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Multi-year durability run '{0}' failed stability gate (status={1}, TTR={2:F1}%).",
                    run.RunId,
                    run.DurabilityStatus,
                    run.ProjectedTenYearTtrPercent);
            gates.Add(new DossierReadinessGate
            {
                GateCode = "QSP_MULTIYEAR_HOMEOSTASIS",
                Section = "PreclinicalQsp",
                Passed = false,
                Summary = reason
            });
            blockers.Add(reason);
        }

        return run;
    }

    /// <summary>
    /// Evaluates Gate 9 (<c>COMP_PHARMACOVIGILANCE</c>) ensuring zero active CARPA/CTCAE quarantine or recall flags.
    /// </summary>
    /// <param name="lot">Resolved patient fabrication lot, or <see langword="null"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluated <see cref="LotPharmacovigilanceReport"/> when a lot exists; otherwise <see langword="null"/>.</returns>
    private async Task<LotPharmacovigilanceReport?> EvaluatePharmacovigilanceGateAsync(
        FabricationLotRecord? lot,
        List<DossierReadinessGate> gates,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        // WHAT: Queries pharmacovigilance posture on the lot and blocks dossier readiness on quarantine/recall triggers.
        // WHY:  Prevents submitting trial dossiers for lots exhibiting CARPA hypersensitivity, TLR9 storms, or Grade >= 3 AEs.
        // HOW:  Calls EvaluateLotPharmacovigilanceAsync and verifies both IsQuarantineTriggered and EmergencyRecallRecommended are false.
        if (lot is null)
        {
            const string missingLot = "No fabrication lot exists to evaluate clinical pharmacovigilance clearance.";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "COMP_PHARMACOVIGILANCE",
                Section = "Compliance",
                Passed = false,
                Summary = missingLot
            });
            blockers.Add(missingLot);
            return null;
        }

        LotPharmacovigilanceReport? pv =
            await _pharmacovigilanceService.EvaluateLotPharmacovigilanceAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        bool passed = pv is not null && !pv.IsQuarantineTriggered && !pv.EmergencyRecallRecommended;

        if (passed)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "COMP_PHARMACOVIGILANCE",
                Section = "Compliance",
                Passed = true,
                Summary = $"Pharmacovigilance cleared on lot '{lot.LotId}' (posture {pv!.Posture})." // WHY `!`: guarded by passed above.
            });
        }
        else
        {
            string reason = pv is null
                ? $"Pharmacovigilance evaluation unavailable for lot '{lot.LotId}'."
                : $"Lot '{lot.LotId}' blocked by pharmacovigilance (quarantine={pv.IsQuarantineTriggered}, recallRecommended={pv.EmergencyRecallRecommended}).";
            gates.Add(new DossierReadinessGate
            {
                GateCode = "COMP_PHARMACOVIGILANCE",
                Section = "Compliance",
                Passed = false,
                Summary = reason
            });
            blockers.Add(reason);
        }

        return pv;
    }

    /// <summary>
    /// Evaluates Gate 10 (<c>COMP_AUDIT_LEDGER</c>) confirming unbroken FDA 21 CFR Part 11 SHA-384 Merkle ledger integrity.
    /// </summary>
    /// <param name="ledger">Result of <see cref="IComplianceAuditService.VerifyLedgerIntegrity"/>.</param>
    /// <param name="gates">Mutable ordered gate output list.</param>
    /// <param name="blockers">Mutable ordered blocking reason list.</param>
    private static void EvaluateAuditLedgerGate(
        AuditLedgerVerificationResult ledger,
        List<DossierReadinessGate> gates,
        List<string> blockers)
    {
        // WHAT: Records pass or fail-closed blocker for full 21 CFR Part 11 audit ledger hash-chain verification.
        // WHY:  Tampered or corrupted audit logs invalidate clinical trial reproducibility across regulatory centers.
        // HOW:  Inspects ledger.IsValid and appends gate summary or blocking diagnostic reason.
        if (ledger.IsValid)
        {
            gates.Add(new DossierReadinessGate
            {
                GateCode = "COMP_AUDIT_LEDGER",
                Section = "Compliance",
                Passed = true,
                Summary = $"21 CFR Part 11 audit ledger verified ({ledger.TotalRecordsVerified} records intact)."
            });
            return;
        }

        string reason = $"21 CFR Part 11 audit ledger integrity failed: {ledger.Message}";
        gates.Add(new DossierReadinessGate
        {
            GateCode = "COMP_AUDIT_LEDGER",
            Section = "Compliance",
            Passed = false,
            Summary = reason
        });
        blockers.Add(reason);
    }

    /// <summary>
    /// Truncates sub-microsecond ticks from a UTC timestamp for portable canonical hashing across JSON and SQL.
    /// </summary>
    /// <param name="timestamp">Source timestamp.</param>
    /// <returns>UTC timestamp aligned cleanly to whole microseconds with zero offset.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset timestamp)
    {
        // WHAT: Strips sub-microsecond ticks and enforces UTC offset TimeSpan.Zero.
        // WHY:  Ensures identical JSON round-trip timestamps and SHA-256 digests across nodes and SQL dialects.
        // HOW:  Truncates UtcTicks modulo TimeSpan.TicksPerMicrosecond and constructs a UTC DateTimeOffset.
        DateTimeOffset utc = timestamp.ToUniversalTime();
        long trimmedTicks = utc.UtcTicks - (utc.UtcTicks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(trimmedTicks, TimeSpan.Zero);
    }
}
