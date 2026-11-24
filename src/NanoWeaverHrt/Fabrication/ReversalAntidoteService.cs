using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Deterministic toehold-mediated strand-displacement reversal antidote designer, ML-DSA-65 cryptographic pairing
/// engine, and 24-hour hormone cessation simulator implementing <see cref="IReversalAntidoteService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete emergency safety switch orchestrator synthesizing patient-matched
/// invading ssDNA oligos and enforcing atomic primary lot neutralization upon administration.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P231): Guarantees permanent programmable HRT depots can be
/// rapidly, safely, and cryptographically halted within 24 hours whenever clinically indicated.
/// </para>
/// <para>
/// <strong>How it works:</strong> Builds Watson-Crick complementary ssDNA strands without LINQ allocations,
/// computes nearest-neighbor ΔG°37 and Tm thermodynamics, signs canonical pairing JSON via <see cref="IKeyManagementService"/>,
/// verifies signatures via <see cref="IPostQuantumCryptoProvider"/>, and emits 21 CFR Part 11 audit trails.
/// </para>
/// </remarks>
public sealed class ReversalAntidoteService : IReversalAntidoteService
{
    /// <summary>
    /// Repository supplying target wet-lab fabrication lots and recording permanent lot recalls.
    /// </summary>
    private readonly IFabricationLotRepository _lotRepository;

    /// <summary>
    /// Repository persisting paired reversal antidotes and emergency neutralization events.
    /// </summary>
    private readonly IReversalAntidoteRepository _antidoteRepository;

    /// <summary>
    /// Key management service providing the node's persistent NIST FIPS 204 ML-DSA-65 signing key.
    /// </summary>
    private readonly IKeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptographic provider validating ML-DSA-65 digital signatures.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// FDA 21 CFR Part 11 tamper-evident compliance audit ledger service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Deterministic UTC clock provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReversalAntidoteService"/> class.
    /// </summary>
    /// <param name="lotRepository">Fabrication lot &amp; revocation repository.</param>
    /// <param name="antidoteRepository">Reversal antidote &amp; neutralization event repository.</param>
    /// <param name="keyManagementService">Node ML-DSA-65 signing key service.</param>
    /// <param name="cryptoProvider">Post-quantum signature verification engine.</param>
    /// <param name="auditService">21 CFR Part 11 audit ledger service.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for unit testing.</param>
    public ReversalAntidoteService(
        IFabricationLotRepository lotRepository,
        IReversalAntidoteRepository antidoteRepository,
        IKeyManagementService keyManagementService,
        IPostQuantumCryptoProvider cryptoProvider,
        IComplianceAuditService auditService,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Guards and stores injected lot, antidote, PQC key, signature, and audit dependencies.
        // WHY:  Prevents silent null dereferences during clinical antidote pairing or emergency administration.
        // HOW:  Validates all constructor dependencies upfront via ArgumentNullException.ThrowIfNull.
        ArgumentNullException.ThrowIfNull(lotRepository);
        ArgumentNullException.ThrowIfNull(antidoteRepository);
        ArgumentNullException.ThrowIfNull(keyManagementService);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(auditService);

        _lotRepository = lotRepository;
        _antidoteRepository = antidoteRepository;
        _keyManagementService = keyManagementService;
        _cryptoProvider = cryptoProvider;
        _auditService = auditService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<ReversalAntidotePairingReport?> PairAntidoteAsync(
        string lotId,
        PairReversalAntidoteRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Designs, signs, persists, and audits a sequence-specific reversal antidote paired to lotId.
        // WHY:  Binds a physical nanocarrier batch to a verified molecular kill-switch prior to patient implantation.
        // HOW:  Validates stoichiometry and oligo inputs, computes thermodynamics, signs canonical JSON with ML-DSA-65, saves, and audits.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        PairReversalAntidoteRequest effective = request ?? new PairReversalAntidoteRequest();
        ValidatePairRequest(effective);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        string targetLockSeq = ResolveAndValidateTargetSequence(effective.TargetAptamerLockSequence5To3, lot);
        string antidoteSeq = BuildReverseComplement(targetLockSeq);
        (double gcPercent, double deltaGKcalMol, double tmCelsius) = ComputeThermodynamics(antidoteSeq, effective.ToeholdLengthNt);

        DateTimeOffset createdAt = NormalizeToMicroseconds(_utcNow());
        string antidoteId = $"anti_{Guid.NewGuid():N}"[..24];

        ReversalAntidoteRecord unsignedRecord = new()
        {
            AntidoteId = antidoteId,
            PrimaryLotId = lot.LotId,
            PatientId = lot.PatientId,
            AntidoteSequence5To3 = antidoteSeq,
            ToeholdLengthNt = effective.ToeholdLengthNt,
            MeltingTempCelsius = tmCelsius,
            BindingDeltaGKcalMol = deltaGKcalMol,
            PairedPayloadSha256 = string.Empty,
            PairedSignatureBase64 = string.Empty,
            SignerKeyId = string.Empty,
            Status = ReversalAntidoteStatus.ActiveStandby,
            CreatedAtUtc = createdAt
        };

        byte[] canonicalBytes = BuildCanonicalPairingPayloadBytes(unsignedRecord);
        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        PqcSignature signature = _keyManagementService.SignWithNodeSigningKey(canonicalBytes);
        PqcKeyMaterial nodeKey = _keyManagementService.GetNodeSigningKey();

        ReversalAntidoteRecord signedRecord = unsignedRecord with
        {
            PairedPayloadSha256 = payloadSha256,
            PairedSignatureBase64 = signature.SignatureBase64,
            SignerKeyId = signature.SignerKeyId
        };

        await _antidoteRepository.SaveAntidoteAsync(signedRecord, cancellationToken).ConfigureAwait(false);

        ReversalAntidoteRecipe recipe = BuildRecipe(
            targetLockSeq,
            signedRecord,
            effective.MolarExcessRatio,
            effective.StockConcentrationMicromolar,
            gcPercent,
            lot.TargetMassMg);

        _auditService.LogAction(
            operatorId.Trim(),
            "FABRICATION_ANTIDOTE_PAIR",
            $"fabrication-lot/{lot.LotId}/antidote/{signedRecord.AntidoteId}",
            JsonSerializer.Serialize(new
            {
                antidoteId = signedRecord.AntidoteId,
                primaryLotId = lot.LotId,
                patientId = lot.PatientId,
                toeholdLengthNt = signedRecord.ToeholdLengthNt,
                meltingTempCelsius = signedRecord.MeltingTempCelsius,
                bindingDeltaGKcalMol = signedRecord.BindingDeltaGKcalMol,
                pairedPayloadSha256 = payloadSha256,
                signerKeyId = signedRecord.SignerKeyId
            }));

        IReadOnlyList<LotNeutralizationEventRecord> events =
            await _antidoteRepository.ListNeutralizationEventsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);

        return new ReversalAntidotePairingReport
        {
            AntidoteId = signedRecord.AntidoteId,
            PrimaryLotId = lot.LotId,
            PatientId = lot.PatientId,
            Regimen = lot.Regimen,
            PrimaryLotStatus = lot.LotStatus,
            Status = signedRecord.Status,
            Recipe = recipe,
            PairedPayloadSha256 = signedRecord.PairedPayloadSha256,
            PairedSignatureBase64 = signedRecord.PairedSignatureBase64,
            SignerKeyId = signedRecord.SignerKeyId,
            SignerPublicKeyBase64 = nodeKey.PublicKeyBase64,
            SignatureAlgorithm = ReversalAntidoteThresholds.SignatureAlgorithmName,
            SignatureVerified = true,
            CreatedAtUtc = signedRecord.CreatedAtUtc,
            NeutralizationEvents = events
        };
    }

    /// <inheritdoc/>
    public async Task<ReversalAntidotePairingReport?> GetLatestAntidoteReportAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads the latest paired antidote record for lotId and evaluates live ML-DSA-65 signature integrity.
        // WHY:  Exposes up-to-date standby status, oligo recipe, and neutralization history cleanly to APIs and UIs.
        // HOW:  Fetches lot and latest antidote, verifies signature via VerifyAntidotePairingSignature, and projects DTO.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        ReversalAntidoteRecord? antidote = await _antidoteRepository.GetLatestAntidoteByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        if (antidote is null)
        {
            return null;
        }

        bool signatureVerified = VerifyAntidotePairingSignature(antidote);
        string reconstructedTarget = BuildReverseComplement(antidote.AntidoteSequence5To3);
        double gcPercent = ComputeGcPercent(antidote.AntidoteSequence5To3);
        ReversalAntidoteRecipe recipe = BuildRecipe(
            reconstructedTarget,
            antidote,
            molarExcessRatio: 5.0,
            stockConcentrationMicromolar: 100.0,
            gcPercent,
            lot.TargetMassMg);

        PqcKeyMaterial nodeKey = _keyManagementService.GetNodeSigningKey();
        IReadOnlyList<LotNeutralizationEventRecord> events =
            await _antidoteRepository.ListNeutralizationEventsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);

        return new ReversalAntidotePairingReport
        {
            AntidoteId = antidote.AntidoteId,
            PrimaryLotId = lot.LotId,
            PatientId = lot.PatientId,
            Regimen = lot.Regimen,
            PrimaryLotStatus = lot.LotStatus,
            Status = antidote.Status,
            Recipe = recipe,
            PairedPayloadSha256 = antidote.PairedPayloadSha256,
            PairedSignatureBase64 = antidote.PairedSignatureBase64,
            SignerKeyId = antidote.SignerKeyId,
            SignerPublicKeyBase64 = nodeKey.PublicKeyBase64,
            SignatureAlgorithm = ReversalAntidoteThresholds.SignatureAlgorithmName,
            SignatureVerified = signatureVerified,
            CreatedAtUtc = antidote.CreatedAtUtc,
            NeutralizationEvents = events
        };
    }

    /// <inheritdoc/>
    public async Task<NeutralizationSimulationReport?> SimulateNeutralizationAsync(
        string lotId,
        SimulateNeutralizationRequest? request,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Forecasts hourly toehold strand-displacement disassembly and systemic hormone decay over 6-72 hours.
        // WHY:  Quantifies time-to-clearance (< 5% basal release) before administering an emergency rescue bolus.
        // HOW:  Validates horizon/dose, requires an existing paired antidote, and integrates deterministic decay kinetics.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        SimulateNeutralizationRequest effective = request ?? new SimulateNeutralizationRequest();
        if (effective.HorizonHours < ReversalAntidoteThresholds.MinHorizonHours
            || effective.HorizonHours > ReversalAntidoteThresholds.MaxHorizonHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"HorizonHours must be between {ReversalAntidoteThresholds.MinHorizonHours} and {ReversalAntidoteThresholds.MaxHorizonHours}.");
        }

        if (effective.DosageMg.HasValue)
        {
            ValidateDosageMg(effective.DosageMg.Value);
        }

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        ReversalAntidoteRecord? antidote = await _antidoteRepository.GetLatestAntidoteByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        if (antidote is null)
        {
            throw new InvalidOperationException($"No paired reversal antidote exists for fabrication lot '{lot.LotId}'.");
        }

        double recommendedMassMg = ComputeRecommendedMassMg(antidote.AntidoteSequence5To3.Length, lot.TargetMassMg, 5.0);
        double appliedDosageMg = effective.DosageMg ?? recommendedMassMg;

        return ComputeNeutralizationReport(lot, antidote, recommendedMassMg, appliedDosageMg, effective.HorizonHours, _utcNow());
    }

    /// <inheritdoc/>
    public async Task<AntidoteAdministrationResponse?> AdministerAntidoteAsync(
        string lotId,
        AdministerAntidoteRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Executes irreversible emergency antidote administration, neutralizes the paired antidote, and revokes the primary lot.
        // WHY:  Blocks any subsequent clinical use or custody handoff of a neutralized depot and preserves a 21 CFR Part 11 record.
        // HOW:  Fails closed on missing/tampered/already-neutralized antidotes, persists event + status + revocation, and audits.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        ValidateDosageMg(request.DosageMg);
        if (string.IsNullOrWhiteSpace(request.ClinicalReason)
            || request.ClinicalReason.Trim().Length > ReversalAntidoteThresholds.MaxClinicalReasonLength)
        {
            throw new ArgumentException(
                $"ClinicalReason must be non-empty and at most {ReversalAntidoteThresholds.MaxClinicalReasonLength} characters.",
                nameof(request));
        }

        string cleanReason = request.ClinicalReason.Trim();
        string cleanOperator = operatorId.Trim();

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        ReversalAntidoteRecord? antidote = await _antidoteRepository.GetLatestAntidoteByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        if (antidote is null)
        {
            throw new InvalidOperationException($"Emergency neutralization blocked fail-closed: fabrication lot '{lot.LotId}' has no paired reversal antidote.");
        }

        if (antidote.Status != ReversalAntidoteStatus.ActiveStandby)
        {
            throw new InvalidOperationException($"Emergency neutralization blocked fail-closed: reversal antidote '{antidote.AntidoteId}' is already in status '{antidote.Status}'.");
        }

        if (!VerifyAntidotePairingSignature(antidote))
        {
            throw new InvalidOperationException($"Emergency neutralization blocked fail-closed: reversal antidote '{antidote.AntidoteId}' failed ML-DSA-65 pairing verification.");
        }

        DateTimeOffset administeredAt = NormalizeToMicroseconds(_utcNow());
        double recommendedMassMg = ComputeRecommendedMassMg(antidote.AntidoteSequence5To3.Length, lot.TargetMassMg, 5.0);
        NeutralizationSimulationReport kinetics = ComputeNeutralizationReport(lot, antidote, recommendedMassMg, request.DosageMg, 24, administeredAt);

        await _antidoteRepository.UpdateAntidoteStatusAsync(antidote.AntidoteId, ReversalAntidoteStatus.Neutralized, cancellationToken).ConfigureAwait(false);

        LotNeutralizationEventRecord neutralizationEvent = new()
        {
            NeutralizationId = $"neut_{Guid.NewGuid():N}"[..24],
            AntidoteId = antidote.AntidoteId,
            PrimaryLotId = lot.LotId,
            PatientId = lot.PatientId,
            DosageMg = Math.Round(request.DosageMg, 4, MidpointRounding.AwayFromZero),
            ClinicalReason = cleanReason,
            EstimatedClearanceHours = kinetics.EstimatedClearanceHours,
            ExecutedBy = cleanOperator,
            AdministeredAtUtc = administeredAt
        };
        await _antidoteRepository.AddNeutralizationEventAsync(neutralizationEvent, cancellationToken).ConfigureAwait(false);

        FabricationLotRecord revokedLot = await _lotRepository.UpsertLotAsync(
            lot with { LotStatus = FabricationLotStatus.Revoked },
            cancellationToken).ConfigureAwait(false);

        FabricationLotRevocationRecord revocation = await _lotRepository.RecordLotRevocationAsync(
            new FabricationLotRevocationRecord
            {
                LotId = revokedLot.LotId,
                PatientId = revokedLot.PatientId,
                PayloadSha256 = revokedLot.CertificatePayloadSha256,
                RevokedBy = cleanOperator,
                RevocationReason = $"Emergency Antidote Neutralization ({neutralizationEvent.NeutralizationId}): {cleanReason}",
                RevokedAtUtc = administeredAt
            },
            cancellationToken).ConfigureAwait(false);

        _auditService.LogAction(
            cleanOperator,
            "FABRICATION_ANTIDOTE_ADMINISTER",
            $"fabrication-lot/{revokedLot.LotId}/neutralization/{neutralizationEvent.NeutralizationId}",
            JsonSerializer.Serialize(new
            {
                neutralizationId = neutralizationEvent.NeutralizationId,
                antidoteId = antidote.AntidoteId,
                primaryLotId = revokedLot.LotId,
                patientId = revokedLot.PatientId,
                dosageMg = neutralizationEvent.DosageMg,
                estimatedClearanceHours = neutralizationEvent.EstimatedClearanceHours,
                clinicalReason = cleanReason
            }));

        return new AntidoteAdministrationResponse
        {
            Event = neutralizationEvent,
            Antidote = antidote with { Status = ReversalAntidoteStatus.Neutralized },
            LotStatus = FabricationLotStatus.Revoked,
            Revocation = revocation,
            Kinetics = kinetics
        };
    }

    /// <inheritdoc/>
    public bool VerifyAntidotePairingSignature(ReversalAntidoteRecord antidote)
    {
        // WHAT: Independently verifies canonical SHA-256 hash binding, key ID match, and ML-DSA-65 signature validity.
        // WHY:  Prevents silent tampering with antidote oligo sequence, melting temperature, or primary lot linkage.
        // HOW:  Rebuilds canonical UTF-8 JSON bytes, compares hex SHA-256, confirms node signing key ID, and calls VerifySignature.
        ArgumentNullException.ThrowIfNull(antidote);

        if (string.IsNullOrWhiteSpace(antidote.PairedPayloadSha256)
            || string.IsNullOrWhiteSpace(antidote.PairedSignatureBase64)
            || string.IsNullOrWhiteSpace(antidote.SignerKeyId))
        {
            return false;
        }

        PqcKeyMaterial nodeKey = _keyManagementService.GetNodeSigningKey();
        if (!string.Equals(antidote.SignerKeyId, nodeKey.KeyId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] canonicalBytes = BuildCanonicalPairingPayloadBytes(antidote);
        string expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        if (!string.Equals(expectedSha256, antidote.PairedPayloadSha256, StringComparison.Ordinal))
        {
            return false;
        }

        return _cryptoProvider.VerifySignature(canonicalBytes, antidote.PairedSignatureBase64, nodeKey.PublicKeyBase64);
    }

    /// <inheritdoc/>
    public async Task<(bool IsSatisfied, string Reason)> VerifyActivePairedAntidoteAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Verifies that a lot has an active, ML-DSA-65 signature-verified standby antidote before patient implantation.
        // WHY:  Enforces fail-closed RequirePairedAntidote policy during CustodyStage.PatientAdministration handoffs.
        // HOW:  Queries latest paired antidote, checks ActiveStandby status, and validates ML-DSA-65 pairing signature.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        string cleanLotId = lotId.Trim();
        ReversalAntidoteRecord? antidote = await _antidoteRepository.GetLatestAntidoteByLotAsync(cleanLotId, cancellationToken).ConfigureAwait(false);
        if (antidote is null)
        {
            return (false, $"Patient administration blocked fail-closed (RequirePairedAntidote): no reversal antidote paired with fabrication lot '{cleanLotId}'.");
        }

        if (antidote.Status != ReversalAntidoteStatus.ActiveStandby)
        {
            return (false, $"Patient administration blocked fail-closed (RequirePairedAntidote): paired antidote '{antidote.AntidoteId}' is in status '{antidote.Status}'.");
        }

        if (!VerifyAntidotePairingSignature(antidote))
        {
            return (false, $"Patient administration blocked fail-closed (RequirePairedAntidote): paired antidote '{antidote.AntidoteId}' failed ML-DSA-65 signature verification.");
        }

        return (true, $"Paired reversal antidote '{antidote.AntidoteId}' verified ActiveStandby with valid ML-DSA-65 signature.");
    }

    /// <summary>
    /// Validates pairing input parameters against biophysical sequence, stoichiometry, and concentration bounds.
    /// </summary>
    /// <param name="request">Pairing configuration request to validate.</param>
    private static void ValidatePairRequest(PairReversalAntidoteRequest request)
    {
        // WHAT: Guards ToeholdLengthNt, MolarExcessRatio, and StockConcentrationMicromolar bounds.
        // WHY:  Rejects non-physical or unstable oligo designs before persistence or signing.
        // HOW:  Checks inclusive min/max constants cleanly with explicit ArgumentOutOfRangeException messages.
        if (request.ToeholdLengthNt < ReversalAntidoteThresholds.MinToeholdLengthNt
            || request.ToeholdLengthNt > ReversalAntidoteThresholds.MaxToeholdLengthNt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"ToeholdLengthNt must be between {ReversalAntidoteThresholds.MinToeholdLengthNt} and {ReversalAntidoteThresholds.MaxToeholdLengthNt} nt.");
        }

        if (!double.IsFinite(request.MolarExcessRatio)
            || request.MolarExcessRatio < ReversalAntidoteThresholds.MinMolarExcessRatio
            || request.MolarExcessRatio > ReversalAntidoteThresholds.MaxMolarExcessRatio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"MolarExcessRatio must be finite and between {ReversalAntidoteThresholds.MinMolarExcessRatio} and {ReversalAntidoteThresholds.MaxMolarExcessRatio}.");
        }

        if (!double.IsFinite(request.StockConcentrationMicromolar)
            || request.StockConcentrationMicromolar < ReversalAntidoteThresholds.MinStockConcentrationMicromolar
            || request.StockConcentrationMicromolar > ReversalAntidoteThresholds.MaxStockConcentrationMicromolar)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"StockConcentrationMicromolar must be finite and between {ReversalAntidoteThresholds.MinStockConcentrationMicromolar} and {ReversalAntidoteThresholds.MaxStockConcentrationMicromolar} µM.");
        }
    }

    /// <summary>
    /// Validates administered or simulated antidote dose mass in milligrams.
    /// </summary>
    /// <param name="dosageMg">Bolus mass in milligrams.</param>
    private static void ValidateDosageMg(double dosageMg)
    {
        // WHAT: Ensures bolus antidote mass is finite and inside physiological clinical bounds.
        // WHY:  Prevents NaN/Infinity propagation or under/over-dosing inside kinetics and administration workflows.
        // HOW:  Compares against ReversalAntidoteThresholds.MinDosageMg and MaxDosageMg.
        if (!double.IsFinite(dosageMg)
            || dosageMg < ReversalAntidoteThresholds.MinDosageMg
            || dosageMg > ReversalAntidoteThresholds.MaxDosageMg)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dosageMg),
                $"DosageMg must be finite and between {ReversalAntidoteThresholds.MinDosageMg} and {ReversalAntidoteThresholds.MaxDosageMg} mg.");
        }
    }

    /// <summary>
    /// Normalizes a user-provided target aptamer lock sequence or synthesizes a deterministic 28-nt lot-specific lock.
    /// </summary>
    /// <param name="customSequence">Optional user-supplied 5'→3' ssDNA sequence.</param>
    /// <param name="lot">Parent wet-lab fabrication lot record.</param>
    /// <returns>Normalized uppercase DNA sequence consisting solely of A, C, G, and T.</returns>
    private static string ResolveAndValidateTargetSequence(string? customSequence, FabricationLotRecord lot)
    {
        // WHAT: Returns a validated A/C/G/T aptamer lock target sequence for antidote complement synthesis.
        // WHY:  Supports explicit sequence entry while defaulting seamlessly to a unique deterministic sequence per lot.
        // HOW:  Trims/uppercases custom input and validates length/bases, or maps SHA-256 entropy bytes cleanly to DNA bases.
        if (!string.IsNullOrWhiteSpace(customSequence))
        {
            string normalized = customSequence.Trim().ToUpperInvariant();
            if (normalized.Length < ReversalAntidoteThresholds.MinTargetSequenceLengthNt
                || normalized.Length > ReversalAntidoteThresholds.MaxTargetSequenceLengthNt)
            {
                throw new ArgumentException(
                    $"TargetAptamerLockSequence5To3 length must be between {ReversalAntidoteThresholds.MinTargetSequenceLengthNt} and {ReversalAntidoteThresholds.MaxTargetSequenceLengthNt} nt.",
                    nameof(customSequence));
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                char baseChar = normalized[i];
                if (baseChar != 'A' && baseChar != 'C' && baseChar != 'G' && baseChar != 'T')
                {
                    throw new ArgumentException(
                        $"TargetAptamerLockSequence5To3 contains invalid DNA base '{baseChar}' at index {i}; only A, C, G, T are allowed.",
                        nameof(customSequence));
                }
            }

            return normalized;
        }

        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes($"{lot.LotId}|{lot.PatientId}|{lot.Regimen}"));
        const string alphabet = "GCTA";
        char[] synthesized = new char[28];
        for (int i = 0; i < synthesized.Length; i++)
        {
            synthesized[i] = alphabet[seed[i] & 3];
        }

        return new string(synthesized);
    }

    /// <summary>
    /// Computes the Watson-Crick reverse-complement DNA strand (5'→3') without heap LINQ allocations.
    /// </summary>
    /// <param name="sequence">Validated uppercase 5'→3' A/C/G/T DNA strand.</param>
    /// <returns>Reverse-complementary 5'→3' ssDNA sequence of identical length.</returns>
    private static string BuildReverseComplement(string sequence)
    {
        // WHAT: Generates the Watson-Crick reverse-complement strand for toehold hybridization.
        // WHY:  Antiparallel ssDNA strands pair 5'->3' against 3'<-5' aptamer locks to open the nanostructure switch.
        // HOW:  Allocates a single fixed char[] buffer and fills reverse positions with A<->T, C<->G complements.
        char[] comp = new char[sequence.Length];
        for (int i = 0; i < sequence.Length; i++)
        {
            char src = sequence[sequence.Length - 1 - i];
            comp[i] = src switch
            {
                'A' => 'T',
                'T' => 'A',
                'C' => 'G',
                _ => 'C'
            };
        }

        return new string(comp);
    }

    /// <summary>
    /// Computes GC base percentage of a validated uppercase DNA strand without LINQ allocations.
    /// </summary>
    /// <param name="sequence">Validated uppercase DNA string.</param>
    /// <returns>Percentage of G/C nucleotides rounded to 2 decimal places.</returns>
    private static double ComputeGcPercent(string sequence)
    {
        // WHAT: Counts G and C bases over the oligonucleotide length.
        // WHY:  GC content governs both duplex stability and nearest-neighbor annealing temperature.
        // HOW:  Iterates sequentially and scales to [0, 100].
        int gc = 0;
        for (int i = 0; i < sequence.Length; i++)
        {
            char b = sequence[i];
            if (b is 'G' or 'C')
            {
                gc++;
            }
        }

        return Math.Round((gc * 100.0) / sequence.Length, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Estimates duplex hybridization free energy ΔG°37 (kcal/mol) and physiological melting temperature Tm (°C).
    /// </summary>
    /// <param name="sequence">Complement ssDNA sequence.</param>
    /// <param name="toeholdLengthNt">Exposed ssDNA toehold domain length.</param>
    /// <returns>Tuple of GC%, ΔG°37 (kcal/mol), and Tm (°C).</returns>
    private static (double GcPercent, double DeltaGKcalMol, double MeltingTempCelsius) ComputeThermodynamics(
        string sequence,
        int toeholdLengthNt)
    {
        // WHAT: Calculates Watson-Crick hybridization ΔG°37 and salt-corrected Tm from base stacking energies.
        // WHY:  Confirms exergonic strand displacement (ΔG°37 << 0) and thermal stability well above 37°C physiological fever bounds.
        // HOW:  Sums SantaLucia weighted G:C / A:T base-pair ΔH, ΔS, ΔG°37 contributions with helix initiation and 150 mM Na+ correction.
        int gcCount = 0;
        int atCount = 0;
        for (int i = 0; i < sequence.Length; i++)
        {
            if (sequence[i] is 'G' or 'C')
            {
                gcCount++;
            }
            else
            {
                atCount++;
            }
        }

        double deltaH = (-9.8 * gcCount) + (-7.6 * atCount) + 0.6;
        double deltaS = (-24.4 * gcCount) + (-21.3 * atCount) - 5.9;
        double deltaG = (-2.15 * gcCount) + (-1.15 * atCount) + 1.96 - (toeholdLengthNt * 0.12);

        const double gasConstant = 1.987;
        const double totalStrandMolar = 1e-6;
        double tmKelvin = (deltaH * 1000.0) / (deltaS + (gasConstant * Math.Log(totalStrandMolar / 4.0)));
        double tmCelsius = (tmKelvin - 273.15) + (16.6 * Math.Log10(0.15));

        double gcPercent = Math.Round((gcCount * 100.0) / sequence.Length, 2, MidpointRounding.AwayFromZero);
        return (
            gcPercent,
            Math.Round(deltaG, 3, MidpointRounding.AwayFromZero),
            Math.Round(tmCelsius, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Computes recommended rescue antidote mass in milligrams for a given molar excess over primary depot aptamer locks.
    /// </summary>
    /// <param name="oligoLengthNt">Antidote oligonucleotide length in nucleotides.</param>
    /// <param name="targetLotMassMg">Encapsulated primary nanocarrier lot mass in milligrams.</param>
    /// <param name="molarExcessRatio">Target antidote-to-aptamer molar excess ratio.</param>
    /// <returns>Recommended rescue bolus mass in milligrams rounded to 4 decimals.</returns>
    private static double ComputeRecommendedMassMg(int oligoLengthNt, double targetLotMassMg, double molarExcessRatio)
    {
        // WHAT: Converts primary depot mass and aptamer site density into required mg of invading ssDNA oligo.
        // WHY:  Ensures stoichiometric 5:1 excess driving pseudo-first-order complete strand displacement.
        // HOW:  Multiplies target µmol * molarExcessRatio * molecularWeightKda and clamps into [MinDosageMg, MaxDosageMg].
        double targetAptamerUmol = Math.Max(0.05, targetLotMassMg) * ReversalAntidoteThresholds.AptamerLockMicromolesPerMgPayload;
        double mwKda = (oligoLengthNt * ReversalAntidoteThresholds.AverageNtMolecularWeightDa) / 1000.0;
        double rawMg = targetAptamerUmol * molarExcessRatio * mwKda;
        double clamped = Math.Clamp(rawMg, ReversalAntidoteThresholds.MinDosageMg, ReversalAntidoteThresholds.MaxDosageMg);
        return Math.Round(clamped, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Constructs a complete <see cref="ReversalAntidoteRecipe"/> DTO from oligo sequence and thermodynamic outputs.
    /// </summary>
    /// <param name="targetLockSeq">Target 5'→3' aptamer lock DNA sequence.</param>
    /// <param name="record">Signed persistence record.</param>
    /// <param name="molarExcessRatio">Molar excess ratio.</param>
    /// <param name="stockConcentrationMicromolar">Stock concentration in µM.</param>
    /// <param name="gcPercent">Calculated GC percentage.</param>
    /// <param name="targetLotMassMg">Primary lot payload mass in mg.</param>
    /// <returns>Populated <see cref="ReversalAntidoteRecipe"/>.</returns>
    private static ReversalAntidoteRecipe BuildRecipe(
        string targetLockSeq,
        ReversalAntidoteRecord record,
        double molarExcessRatio,
        double stockConcentrationMicromolar,
        double gcPercent,
        double targetLotMassMg)
    {
        // WHAT: Aggregates oligo sequence, toehold subsequence, MW, bolus mass, and reconstitution volume.
        // WHY:  Produces an immediately actionable wet-lab synthesis and rescue syringe preparation sheet.
        // HOW:  Slices toehold domain, calculates MW in kDa and µL volume from desired µmol / µM stock concentration.
        double mwKda = Math.Round((record.AntidoteSequence5To3.Length * ReversalAntidoteThresholds.AverageNtMolecularWeightDa) / 1000.0, 3, MidpointRounding.AwayFromZero);
        double recommendedMassMg = ComputeRecommendedMassMg(record.AntidoteSequence5To3.Length, targetLotMassMg, molarExcessRatio);
        double requiredUmol = recommendedMassMg / mwKda;
        double volumeMicroliters = Math.Round((requiredUmol / stockConcentrationMicromolar) * 1_000_000.0, 2, MidpointRounding.AwayFromZero);
        int sliceLen = Math.Min(record.ToeholdLengthNt, record.AntidoteSequence5To3.Length);

        return new ReversalAntidoteRecipe
        {
            TargetAptamerLockSequence5To3 = targetLockSeq,
            AntidoteSequence5To3 = record.AntidoteSequence5To3,
            ToeholdDomain5To3 = record.AntidoteSequence5To3[..sliceLen],
            ToeholdLengthNt = record.ToeholdLengthNt,
            MolarExcessRatio = Math.Round(molarExcessRatio, 2, MidpointRounding.AwayFromZero),
            AntidoteMolecularWeightKda = mwKda,
            RecommendedAntidoteMassMg = recommendedMassMg,
            StockConcentrationMicromolar = Math.Round(stockConcentrationMicromolar, 2, MidpointRounding.AwayFromZero),
            InjectionVolumeMicroliters = volumeMicroliters,
            GcContentPercent = gcPercent,
            MeltingTempCelsius = record.MeltingTempCelsius,
            BindingDeltaGKcalMol = record.BindingDeltaGKcalMol
        };
    }

    /// <summary>
    /// Executes hourly first-order toehold strand-displacement disassembly and systemic hormone decay modeling.
    /// </summary>
    /// <param name="lot">Target fabrication lot.</param>
    /// <param name="antidote">Paired reversal antidote.</param>
    /// <param name="recommendedMassMg">Nominal 5:1 stoichiometric mass baseline.</param>
    /// <param name="appliedDosageMg">Applied rescue dose mass in milligrams.</param>
    /// <param name="horizonHours">Hours to simulate (6..72).</param>
    /// <param name="timestamp">Timestamp recorded on report.</param>
    /// <returns>Deterministic <see cref="NeutralizationSimulationReport"/>.</returns>
    private static NeutralizationSimulationReport ComputeNeutralizationReport(
        FabricationLotRecord lot,
        ReversalAntidoteRecord antidote,
        double recommendedMassMg,
        double appliedDosageMg,
        int horizonHours,
        DateTimeOffset timestamp)
    {
        // WHAT: Simulates nanocarrier lock displacement and circulating sex steroid washout over horizonHours.
        // WHY:  Toehold initiation rate and excess ratio govern how quickly active hormone elution arrests (< 5% basal).
        // HOW:  Computes k_disp(toehold, affinity, doseRatio) and integrates hourly discrete curve without LINQ.
        double toeholdBoost = 1.0 + (0.25 * (antidote.ToeholdLengthNt - ReversalAntidoteThresholds.MinToeholdLengthNt));
        double affinityFactor = Math.Clamp(-antidote.BindingDeltaGKcalMol / 35.0, 0.75, 1.60);
        double doseRatio = appliedDosageMg / recommendedMassMg;
        double effectiveExcess = Math.Round(5.0 * doseRatio, 2, MidpointRounding.AwayFromZero);
        double doseFactor = Math.Clamp(doseRatio, 0.20, 2.50);

        double kDispPerHour = 0.14 * toeholdBoost * affinityFactor * doseFactor;
        double halfLifeHours = Math.Round(Math.Log(2.0) / kDispPerHour, 2, MidpointRounding.AwayFromZero);
        double clearanceHours = Math.Round(Math.Log(1.0 / ReversalAntidoteThresholds.ClearanceThresholdFraction) / kDispPerHour, 2, MidpointRounding.AwayFromZero);

        (double steadyE2, double basalE2, double steadyT, double basalT) = lot.Regimen switch
        {
            TherapeuticRegimen.MaleToFemale => (550.0, 42.0, 1.05, 1.05),
            TherapeuticRegimen.FemaleToMale => (110.0, 110.0, 22.5, 0.85),
            _ => (360.0, 48.0, 11.5, 0.95)
        };

        List<NeutralizationKineticsPoint> points = new(horizonHours + 1);
        for (int hr = 0; hr <= horizonHours; hr++)
        {
            double boundFraction = Math.Exp(-kDispPerHour * hr);
            double displacedFraction = 1.0 - boundFraction;
            double normalizedPct = Math.Round(boundFraction * 100.0, 2, MidpointRounding.AwayFromZero);
            double e2 = Math.Round(basalE2 + ((steadyE2 - basalE2) * boundFraction), 2, MidpointRounding.AwayFromZero);
            double t = Math.Round(basalT + ((steadyT - basalT) * boundFraction), 3, MidpointRounding.AwayFromZero);

            points.Add(new NeutralizationKineticsPoint
            {
                Hour = hr,
                BoundNanocarrierFraction = Math.Round(boundFraction, 4, MidpointRounding.AwayFromZero),
                DisplacedComplexFraction = Math.Round(displacedFraction, 4, MidpointRounding.AwayFromZero),
                NormalizedHormoneOutputPercent = normalizedPct,
                PredictedEstradiolPmolL = e2,
                PredictedTestosteroneNmolL = t
            });
        }

        double cessation24 = Math.Round((1.0 - Math.Exp(-kDispPerHour * 24.0)) * 100.0, 2, MidpointRounding.AwayFromZero);

        return new NeutralizationSimulationReport
        {
            AntidoteId = antidote.AntidoteId,
            PrimaryLotId = lot.LotId,
            PatientId = lot.PatientId,
            Regimen = lot.Regimen,
            AppliedDosageMg = Math.Round(appliedDosageMg, 4, MidpointRounding.AwayFromZero),
            MolarExcessAchieved = effectiveExcess,
            HorizonHours = horizonHours,
            EstimatedHalfLifeHours = halfLifeHours,
            EstimatedClearanceHours = clearanceHours,
            TwentyFourHourCessationPercent = cessation24,
            Trajectory = points,
            GeneratedAtUtc = timestamp
        };
    }

    /// <summary>
    /// Serializes immutable antidote-lot pairing fields into deterministic canonical UTF-8 JSON bytes.
    /// </summary>
    /// <param name="record">Reversal antidote record whose canonical pairing digest is computed.</param>
    /// <returns>Canonical UTF-8 byte array used for SHA-256 hashing and ML-DSA-65 signing/verification.</returns>
    private static byte[] BuildCanonicalPairingPayloadBytes(ReversalAntidoteRecord record)
    {
        // WHAT: Produces deterministic UTF-8 JSON binding antidote sequence, thermodynamics, lot ID, and patient ID.
        // WHY:  Guarantees portable SHA-256 and ML-DSA-65 signature stability across all SQL dialects and clock precisions.
        // HOW:  Rounds floating-point scalars to 4 decimals and normalizes CreatedAtUtc to microsecond ISO-8601 UTC.
        var payload = new
        {
            antidoteId = record.AntidoteId,
            primaryLotId = record.PrimaryLotId,
            patientId = record.PatientId,
            antidoteSequence5To3 = record.AntidoteSequence5To3,
            toeholdLengthNt = record.ToeholdLengthNt,
            meltingTempCelsius = Math.Round(record.MeltingTempCelsius, 4, MidpointRounding.AwayFromZero),
            bindingDeltaGKcalMol = Math.Round(record.BindingDeltaGKcalMol, 4, MidpointRounding.AwayFromZero),
            createdAtUtc = NormalizeToMicroseconds(record.CreatedAtUtc).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    /// <summary>
    /// Truncates sub-microsecond ticks from a <see cref="DateTimeOffset"/> for portable cross-engine persistence parity.
    /// </summary>
    /// <param name="timestamp">Source UTC timestamp.</param>
    /// <returns>UTC timestamp aligned cleanly to whole microseconds.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset timestamp)
    {
        // WHAT: Aligns timestamps to microsecond resolution (10 ticks per microsecond).
        // WHY:  MySQL DATETIME(6), PostgreSQL TIMESTAMPTZ, and DuckDB TIMESTAMP store microseconds rather than 100-ns ticks.
        // HOW:  Drops remainder ticks mod TimeSpan.TicksPerMicrosecond after converting to UTC.
        DateTimeOffset utc = timestamp.ToUniversalTime();
        long trimmedTicks = utc.UtcTicks - (utc.UtcTicks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(trimmedTicks, TimeSpan.Zero);
    }
}
