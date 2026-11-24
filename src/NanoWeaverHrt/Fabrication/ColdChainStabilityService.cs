using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Deterministic biophysical Arrhenius thermal degradation engine and tamper-evident SHA-256 chain-of-custody
/// service implementing <see cref="IColdChainStabilityService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Zero-LINQ cold-chain stability calculator and custody provenance guardian
/// for released DNA origami HRT depot batches.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P211): Prevents thermally compromised, expired, or custody-tampered
/// batches from passing clinical CoA verification or patient administration checks.
/// </para>
/// <para>
/// <strong>How it works:</strong> Integrates Arrhenius rate constants with dsDNA melting-cliff penalties over
/// chronological sensor observations, encrypts confidential notes with <see cref="ProfileDataProtector"/>,
/// links custody records via microsecond-normalized SHA-256 chains, and emits 21 CFR Part 11 audit logs.
/// </para>
/// </remarks>
public sealed class ColdChainStabilityService : IColdChainStabilityService
{
    /// <summary>
    /// Configuration catalog key controlling whether <see cref="CustodyStage.PatientAdministration"/> requires an active paired reversal antidote.
    /// </summary>
    public const string RequirePairedAntidoteConfigKey = "Fabrication:RequirePairedAntidote";

    /// <summary>
    /// Maximum allowed plaintext character length for optional excursion operator notes.
    /// </summary>
    public const int MaxNotesLength = 2_000;

    /// <summary>
    /// Repository storing fabrication lots, QC checks, and local/federated revocation tombstones.
    /// </summary>
    private readonly IFabricationLotRepository _lotRepository;

    /// <summary>
    /// Repository persisting cold-chain thermal excursions and hash-chained custody handoffs.
    /// </summary>
    private readonly IColdChainCustodyRepository _coldChainRepository;

    /// <summary>
    /// AES-256-GCM field protector encrypting optional excursion notes at rest.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// FDA 21 CFR Part 11 hash-chained audit logging service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Injectable deterministic UTC clock provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Optional Milestone 44 reversal antidote verifier enforcing <see cref="RequirePairedAntidoteConfigKey"/>.
    /// </summary>
    private readonly IReversalAntidoteService? _reversalAntidoteService;

    /// <summary>
    /// Optional host configuration supplying static overrides for <see cref="RequirePairedAntidoteConfigKey"/>.
    /// </summary>
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// Optional runtime configuration store supplying database overrides for <see cref="RequirePairedAntidoteConfigKey"/>.
    /// </summary>
    private readonly IConfigurationRepository? _configurationRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColdChainStabilityService"/> class.
    /// </summary>
    /// <param name="lotRepository">Fabrication lot &amp; revocation repository.</param>
    /// <param name="coldChainRepository">Cold-chain thermal excursion &amp; custody handoff repository.</param>
    /// <param name="protector">AES-256-GCM field protector for optional clinical notes.</param>
    /// <param name="auditService">21 CFR Part 11 compliance audit service.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for testing.</param>
    /// <param name="reversalAntidoteService">Optional Milestone 44 antidote readiness verifier.</param>
    /// <param name="configuration">Optional host configuration provider.</param>
    /// <param name="configurationRepository">Optional runtime configuration repository.</param>
    public ColdChainStabilityService(
        IFabricationLotRepository lotRepository,
        IColdChainCustodyRepository coldChainRepository,
        ProfileDataProtector protector,
        IComplianceAuditService auditService,
        Func<DateTimeOffset>? utcNow = null,
        IReversalAntidoteService? reversalAntidoteService = null,
        IConfiguration? configuration = null,
        IConfigurationRepository? configurationRepository = null)
    {
        // WHAT: Validates and stores all required domain dependencies and optional Milestone 44 gate dependencies.
        // WHY:  Guarantees fail-fast DI initialization with zero null dereferences during clinical calculations.
        // HOW:  Throws ArgumentNullException on any missing required dependency and stores optional antidote/config collaborators.
        ArgumentNullException.ThrowIfNull(lotRepository);
        ArgumentNullException.ThrowIfNull(coldChainRepository);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(auditService);

        _lotRepository = lotRepository;
        _coldChainRepository = coldChainRepository;
        _protector = protector;
        _auditService = auditService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _reversalAntidoteService = reversalAntidoteService;
        _configuration = configuration;
        _configurationRepository = configurationRepository;
    }

    /// <inheritdoc/>
    public double ComputeDegradationRatePerHour(double temperatureCelsius)
    {
        // WHAT: Computes first-order Arrhenius thermal degradation rate k(T) in h^-1 with dsDNA melting-cliff multipliers.
        // WHY:  DNA origami staples dissociate cooperatively near physiological/fever temperatures (>= 37°C) and melt catastrophically above 42°C.
        // HOW:  Evaluates k_ref * exp(-(Ea/R) * (1/T_K - 1/T_ref_K)) multiplied by 1.0, 10.0 (37-42°C), or 100.0 (> 42°C).
        if (!double.IsFinite(temperatureCelsius)
            || temperatureCelsius < ArrheniusColdChainConstants.MinValidTemperatureCelsius
            || temperatureCelsius > ArrheniusColdChainConstants.MaxValidTemperatureCelsius)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperatureCelsius),
                $"Temperature must be finite and between {ArrheniusColdChainConstants.MinValidTemperatureCelsius}°C and {ArrheniusColdChainConstants.MaxValidTemperatureCelsius}°C.");
        }

        double tempKelvin = temperatureCelsius + ArrheniusColdChainConstants.AbsoluteZeroOffsetKelvin;
        double exponent = -(ArrheniusColdChainConstants.ActivationEnergyJPerMol / ArrheniusColdChainConstants.UniversalGasConstantJPerMolK)
            * ((1.0 / tempKelvin) - (1.0 / ArrheniusColdChainConstants.ReferenceStorageKelvin));
        double arrheniusRate = ArrheniusColdChainConstants.ReferenceRateConstantPerHour * Math.Exp(exponent);

        double multiplier = temperatureCelsius > ArrheniusColdChainConstants.CatastrophicDenaturationCelsius
            ? ArrheniusColdChainConstants.CatastrophicDenaturationMultiplier
            : temperatureCelsius >= ArrheniusColdChainConstants.CriticalMeltingWarningCelsius
                ? ArrheniusColdChainConstants.TransitionMeltingPenaltyMultiplier
                : 1.0;

        return arrheniusRate * multiplier;
    }

    /// <inheritdoc/>
    public string ComputeCustodyEntrySha256(CustodyHandoffRecord record)
    {
        // WHAT: Produces a deterministic lowercase SHA-256 hex digest committing all immutable custody handoff fields.
        // WHY:  Detects any post-hoc database modification of actor, stage, temperature, potency, timestamp, or chain link.
        // HOW:  Formats microsecond-normalized UTC timestamp and 4-decimal numbers with '|' delimiters into UTF-8 bytes and hashes with SHA-256.
        ArgumentNullException.ThrowIfNull(record);

        DateTimeOffset normalizedUtc = NormalizeToMicroseconds(record.RecordedAtUtc);
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{record.ChainPrevSha256}|{record.HandoffId}|{record.LotId}|{record.PatientId}|{record.Stage}|{record.FromActor}|{record.ToActor}|{record.FacilityCode}|{record.TemperatureCelsius:F4}|{record.PotencyPercentAtHandoff:F4}|{normalizedUtc:yyyy-MM-ddTHH:mm:ss.ffffffZ}");

        byte[] utf8 = Encoding.UTF8.GetBytes(canonical);
        byte[] hash = SHA256.HashData(utf8);
        return Convert.ToHexStringLower(hash);
    }

    /// <inheritdoc/>
    public CustodyChainVerificationResult VerifyCustodyChain(IReadOnlyList<CustodyHandoffRecord> handoffs)
    {
        // WHAT: Walks a lot's chronological custody handoffs verifying genesis linkage, prev-hash continuity, and SHA-256 digests.
        // WHY:  Guarantees 21 CFR Part 11 tamper-evidence across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
        // HOW:  Iterates with a zero-LINQ for-loop comparing expected ChainPrevSha256 and recomputed EntrySha256 in fixed time.
        ArgumentNullException.ThrowIfNull(handoffs);

        string expectedPrev = ArrheniusColdChainConstants.GenesisCustodyPrevSha256;
        for (int i = 0; i < handoffs.Count; i++)
        {
            CustodyHandoffRecord entry = handoffs[i];
            if (!string.Equals(entry.ChainPrevSha256, expectedPrev, StringComparison.Ordinal))
            {
                return new CustodyChainVerificationResult
                {
                    IsValid = false,
                    FailureReason = $"Custody chain linkage mismatch at index {i} (handoff '{entry.HandoffId}').",
                    EvaluatedCount = handoffs.Count
                };
            }

            string recomputed = ComputeCustodyEntrySha256(entry);
            byte[] expectedBytes = Encoding.ASCII.GetBytes(recomputed);
            byte[] actualBytes = Encoding.ASCII.GetBytes(entry.EntrySha256);
            if (expectedBytes.Length != actualBytes.Length
                || !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                return new CustodyChainVerificationResult
                {
                    IsValid = false,
                    FailureReason = $"Custody entry SHA-256 digest tampered at index {i} (handoff '{entry.HandoffId}').",
                    EvaluatedCount = handoffs.Count
                };
            }

            expectedPrev = entry.EntrySha256;
        }

        return new CustodyChainVerificationResult
        {
            IsValid = true,
            FailureReason = null,
            EvaluatedCount = handoffs.Count
        };
    }

    /// <inheritdoc/>
    public async Task<ThermalExcursionResponse?> RecordThermalExcursionAsync(
        string lotId,
        RecordThermalExcursionRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates and persists a new cold-chain thermal observation, audits it, and returns updated lot stability.
        // WHY:  Milestone 40 (P211): Tracks every temperature excursion and updates implantation safety in real time.
        // HOW:  Validates numeric ranges and sensor fields, encrypts optional notes via ProfileDataProtector, inserts row, logs audit, and evaluates stability.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        ValidateTemperature(request.TemperatureCelsius);
        if (!double.IsFinite(request.DurationHours)
            || request.DurationHours <= 0.0
            || request.DurationHours > ArrheniusColdChainConstants.MaxSingleDurationHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"DurationHours must be finite and between > 0 and {ArrheniusColdChainConstants.MaxSingleDurationHours} hours.");
        }

        if (!Enum.IsDefined(request.StorageTier))
        {
            throw new ArgumentException("StorageTier must be a valid ThermalStorageTier value.", nameof(request));
        }

        string sensorId = request.SensorId?.Trim() ?? string.Empty;
        if (sensorId.Length == 0 || sensorId.Length > 128)
        {
            throw new ArgumentException("SensorId must be between 1 and 128 characters.", nameof(request));
        }

        string? cleanNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (cleanNotes is not null && cleanNotes.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Notes must not exceed {MaxNotesLength} characters.", nameof(request));
        }

        string trimmedLotId = lotId.Trim();
        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(trimmedLotId, cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        DateTimeOffset recordedAtUtc = NormalizeToMicroseconds(request.RecordedAtUtc ?? _utcNow());
        double roundedTemp = Math.Round(request.TemperatureCelsius, 4, MidpointRounding.AwayFromZero);
        double roundedDuration = Math.Round(request.DurationHours, 4, MidpointRounding.AwayFromZero);
        string? encryptedNotes = cleanNotes is null ? null : _protector.EncryptField(cleanNotes);
        string excursionId = $"texc_{Guid.NewGuid():N}"[..24];

        ThermalExcursionRecord record = new()
        {
            ExcursionId = excursionId,
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            RecordedAtUtc = recordedAtUtc,
            TemperatureCelsius = roundedTemp,
            DurationHours = roundedDuration,
            StorageTier = request.StorageTier,
            SensorId = sensorId,
            NotesEncrypted = encryptedNotes,
            RecordedBy = operatorId.Trim()
        };

        await _coldChainRepository.AddThermalExcursionAsync(record, cancellationToken).ConfigureAwait(false);

        LotStabilityReport stability = await EvaluateStabilityForLotAsync(lot, cancellationToken).ConfigureAwait(false);

        string auditDetails = JsonSerializer.Serialize(new
        {
            excursionId = record.ExcursionId,
            lotId = lot.LotId,
            temperatureCelsius = roundedTemp,
            durationHours = roundedDuration,
            storageTier = record.StorageTier.ToString(),
            sensorId = record.SensorId,
            remainingPotencyPercent = stability.RemainingPotencyPercent,
            status = stability.Status.ToString()
        });
        _auditService.LogAction(operatorId.Trim(), "FABRICATION_THERMAL_LOG_RECORD", $"fabrication-lot/{lot.LotId}", auditDetails);

        return new ThermalExcursionResponse
        {
            ExcursionId = record.ExcursionId,
            LotId = record.LotId,
            PatientId = record.PatientId,
            RecordedAtUtc = record.RecordedAtUtc,
            TemperatureCelsius = record.TemperatureCelsius,
            DurationHours = record.DurationHours,
            StorageTier = record.StorageTier,
            SensorId = record.SensorId,
            Notes = cleanNotes,
            RecordedBy = record.RecordedBy,
            StabilityAfterExcursion = stability
        };
    }

    /// <inheritdoc/>
    public async Task<LotStabilityReport?> EvaluateLotStabilityAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads a fabrication lot and computes its current Arrhenius thermal stability and custody-chain verdict.
        // WHY:  Provides a live, deterministic safety assessment prior to shipping, inspection, or patient administration.
        // HOW:  Fetches lot by ID, returns null when absent, otherwise delegates to EvaluateStabilityForLotAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        return await EvaluateStabilityForLotAsync(lot, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CustodyHandoffRecord?> RecordCustodyHandoffAsync(
        string lotId,
        RecordCustodyHandoffRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Appends a SHA-256 hash-chained physical custody handoff while enforcing fail-closed administration and disposal rules.
        // WHY:  Milestone 40 (P211): Blocks clinical administration of revoked, quarantined, thermally compromised, or chain-tampered lots.
        // HOW:  Validates inputs, verifies prior chain & disposal state, checks revocation/quarantine/stability for PatientAdministration, hashes, persists, and audits.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        if (!Enum.IsDefined(request.Stage))
        {
            throw new ArgumentException("Stage must be a valid CustodyStage value.", nameof(request));
        }

        string fromActor = request.FromActor?.Trim() ?? string.Empty;
        if (fromActor.Length == 0 || fromActor.Length > 128)
        {
            throw new ArgumentException("FromActor must be between 1 and 128 characters.", nameof(request));
        }

        string toActor = request.ToActor?.Trim() ?? string.Empty;
        if (toActor.Length == 0 || toActor.Length > 128)
        {
            throw new ArgumentException("ToActor must be between 1 and 128 characters.", nameof(request));
        }

        string facilityCode = request.FacilityCode?.Trim() ?? string.Empty;
        if (facilityCode.Length == 0 || facilityCode.Length > 64)
        {
            throw new ArgumentException("FacilityCode must be between 1 and 64 characters.", nameof(request));
        }

        ValidateTemperature(request.TemperatureCelsius);

        string trimmedLotId = lotId.Trim();
        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(trimmedLotId, cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        IReadOnlyList<CustodyHandoffRecord> existingHandoffs = await _coldChainRepository.ListCustodyHandoffsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        CustodyChainVerificationResult chainCheck = VerifyCustodyChain(existingHandoffs);
        if (!chainCheck.IsValid)
        {
            throw new InvalidOperationException($"Cannot append custody handoff because existing custody hash chain is invalid: {chainCheck.FailureReason}");
        }

        for (int i = 0; i < existingHandoffs.Count; i++)
        {
            if (existingHandoffs[i].Stage == CustodyStage.QuarantineDisposal)
            {
                throw new InvalidOperationException($"Lot '{lot.LotId}' has already reached terminal QuarantineDisposal; no further custody transitions are permitted.");
            }
        }

        LotStabilityReport stability = await EvaluateStabilityForLotAsync(lot, cancellationToken).ConfigureAwait(false);

        if (request.Stage == CustodyStage.PatientAdministration)
        {
            FabricationLotRevocationRecord? localRev = await _lotRepository.GetLotRevocationAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
            FederatedLotRevocationRecord? fedRev = await _lotRepository.GetFederatedRevocationAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
            if (localRev is not null || fedRev is not null || lot.LotStatus == FabricationLotStatus.Revoked)
            {
                throw new InvalidOperationException($"Patient administration blocked fail-closed: fabrication lot '{lot.LotId}' has been permanently recalled/revoked.");
            }

            if (lot.LotStatus != FabricationLotStatus.Released)
            {
                throw new InvalidOperationException($"Patient administration blocked fail-closed: fabrication lot '{lot.LotId}' status is '{lot.LotStatus}' rather than Released.");
            }

            if (!stability.IsSafeForImplantation)
            {
                throw new InvalidOperationException($"Patient administration blocked fail-closed: {stability.Summary}");
            }

            if (await IsPairedAntidoteRequiredAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_reversalAntidoteService is null)
                {
                    throw new InvalidOperationException("Patient administration blocked fail-closed (RequirePairedAntidote): reversal antidote verification service is unavailable.");
                }

                (bool antidoteReady, string antidoteReason) = await _reversalAntidoteService
                    .VerifyActivePairedAntidoteAsync(lot.LotId, cancellationToken)
                    .ConfigureAwait(false);
                if (!antidoteReady)
                {
                    throw new InvalidOperationException(antidoteReason);
                }
            }
        }

        string prevSha = existingHandoffs.Count == 0
            ? ArrheniusColdChainConstants.GenesisCustodyPrevSha256
            : existingHandoffs[existingHandoffs.Count - 1].EntrySha256;

        DateTimeOffset recordedAtUtc = NormalizeToMicroseconds(request.RecordedAtUtc ?? _utcNow());
        double roundedTemp = Math.Round(request.TemperatureCelsius, 4, MidpointRounding.AwayFromZero);
        double roundedPotency = Math.Round(stability.RemainingPotencyPercent, 4, MidpointRounding.AwayFromZero);
        string handoffId = $"chnd_{Guid.NewGuid():N}"[..24];

        CustodyHandoffRecord unsignedRecord = new()
        {
            HandoffId = handoffId,
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            Stage = request.Stage,
            FromActor = fromActor,
            ToActor = toActor,
            FacilityCode = facilityCode,
            TemperatureCelsius = roundedTemp,
            PotencyPercentAtHandoff = roundedPotency,
            ChainPrevSha256 = prevSha,
            EntrySha256 = string.Empty,
            RecordedAtUtc = recordedAtUtc
        };

        string entrySha = ComputeCustodyEntrySha256(unsignedRecord);
        CustodyHandoffRecord committed = unsignedRecord with { EntrySha256 = entrySha };

        await _coldChainRepository.AddCustodyHandoffAsync(committed, cancellationToken).ConfigureAwait(false);

        string auditDetails = JsonSerializer.Serialize(new
        {
            handoffId = committed.HandoffId,
            lotId = committed.LotId,
            stage = committed.Stage.ToString(),
            fromActor = committed.FromActor,
            toActor = committed.ToActor,
            facilityCode = committed.FacilityCode,
            potencyPercentAtHandoff = committed.PotencyPercentAtHandoff,
            entrySha256 = committed.EntrySha256
        });
        _auditService.LogAction(operatorId.Trim(), "FABRICATION_CUSTODY_HANDOFF_RECORD", $"fabrication-lot/{committed.LotId}", auditDetails);

        return committed;
    }

    /// <inheritdoc/>
    public async Task<CustodyHandoffLedgerResponse?> GetCustodyLedgerAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads all custody handoffs for a lot and evaluates SHA-256 hash-chain continuity.
        // WHY:  Lets clinicians, patients, and auditors inspect verified chain-of-custody history.
        // HOW:  Confirms lot existence, lists chronological handoffs, runs VerifyCustodyChain, and wraps in response DTO.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = await _lotRepository.GetLotByIdAsync(lotId.Trim(), cancellationToken).ConfigureAwait(false);
        if (lot is null)
        {
            return null;
        }

        IReadOnlyList<CustodyHandoffRecord> handoffs = await _coldChainRepository.ListCustodyHandoffsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        CustodyChainVerificationResult verification = VerifyCustodyChain(handoffs);

        return new CustodyHandoffLedgerResponse
        {
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            CustodyChainValid = verification.IsValid,
            ChainValidationSummary = verification.IsValid
                ? $"Verified {handoffs.Count} hash-chained custody handoff(s) cleanly from genesis."
                : verification.FailureReason!,
            Handoffs = handoffs
        };
    }

    /// <inheritdoc/>
    public async Task<FabricationCertificateVerificationReport> ApplyColdChainStabilityGateAsync(
        FabricationCertificateVerificationReport baseReport,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Evaluates local cold-chain stability and custody integrity and overrides IsTrusted fail-closed when compromised.
        // WHY:  Milestone 40 (P211): A mathematically valid ML-DSA-65 CoA must still be blocked if the vial melted, expired, or lost custody integrity.
        // HOW:  Queries local lot by baseReport.LotId; if absent (foreign peer CoA), returns baseReport untouched; otherwise enriches and gates IsTrusted.
        ArgumentNullException.ThrowIfNull(baseReport);

        if (string.IsNullOrWhiteSpace(baseReport.LotId))
        {
            return baseReport;
        }

        LotStabilityReport? stability = await EvaluateLotStabilityAsync(baseReport.LotId, cancellationToken).ConfigureAwait(false);
        if (stability is null)
        {
            return baseReport;
        }

        bool coldChainBlocksTrust = !stability.IsSafeForImplantation;
        bool effectiveTrusted = baseReport.IsTrusted && !coldChainBlocksTrust;
        string effectiveSummary = coldChainBlocksTrust
            ? $"{baseReport.Summary} COLD-CHAIN BLOCK: {stability.Summary}"
            : baseReport.Summary;

        return baseReport with
        {
            IsTrusted = effectiveTrusted,
            Summary = effectiveSummary,
            ColdChainStatus = stability.Status,
            RemainingPotencyPercent = stability.RemainingPotencyPercent,
            CustodyChainValid = stability.CustodyChainValid,
            IsSafeForImplantation = stability.IsSafeForImplantation
        };
    }

    /// <summary>
    /// Computes the complete <see cref="LotStabilityReport"/> for an already-loaded <see cref="FabricationLotRecord"/>.
    /// </summary>
    /// <param name="lot">Loaded wet-lab fabrication lot record.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The calculated <see cref="LotStabilityReport"/>.</returns>
    private async Task<LotStabilityReport> EvaluateStabilityForLotAsync(
        FabricationLotRecord lot,
        CancellationToken cancellationToken)
    {
        // WHAT: Integrates Arrhenius kinetics over logged thermal excursions plus unlogged baseline -20°C storage time.
        // WHY:  Produces physically accurate remaining potency, thermal budget utilization, shelf-life days, and safety status.
        // HOW:  Loads excursions and custody handoffs, accumulates decay exponents in a zero-LINQ loop, and classifies priority status.
        IReadOnlyList<ThermalExcursionRecord> excursions = await _coldChainRepository.ListThermalExcursionsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CustodyHandoffRecord> handoffs = await _coldChainRepository.ListCustodyHandoffsByLotAsync(lot.LotId, cancellationToken).ConfigureAwait(false);
        CustodyChainVerificationResult chainCheck = VerifyCustodyChain(handoffs);

        DateTimeOffset evaluationUtc = NormalizeToMicroseconds(_utcNow());
        DateTimeOffset originUtc = NormalizeToMicroseconds(lot.ReleasedAtUtc ?? lot.CreatedAtUtc);
        DateTimeOffset nominalExpiryUtc = originUtc.AddDays(ArrheniusColdChainConstants.NominalShelfLifeDays);

        double totalExcursionHours = 0.0;
        double totalExcursionDecay = 0.0;
        bool hasCatastrophic = false;
        bool hasAdvisoryTemp = false;

        for (int i = 0; i < excursions.Count; i++)
        {
            ThermalExcursionRecord ex = excursions[i];
            totalExcursionHours += ex.DurationHours;
            totalExcursionDecay += ComputeDegradationRatePerHour(ex.TemperatureCelsius) * ex.DurationHours;

            if (ex.TemperatureCelsius > ArrheniusColdChainConstants.CatastrophicDenaturationCelsius)
            {
                hasCatastrophic = true;
            }

            if (ex.TemperatureCelsius > ArrheniusColdChainConstants.AdvisoryTemperatureCelsius
                || ex.StorageTier == ThermalStorageTier.AmbientControlled
                || ex.StorageTier == ThermalStorageTier.ThermalExcursion)
            {
                hasAdvisoryTemp = true;
            }
        }

        double elapsedHours = Math.Max(0.0, (evaluationUtc - originUtc).TotalHours);
        double baselineStorageHours = Math.Max(0.0, elapsedHours - totalExcursionHours);
        double rateMinus20 = ComputeDegradationRatePerHour(ArrheniusColdChainConstants.ReferenceStorageCelsius);
        double rate4C = ComputeDegradationRatePerHour(ArrheniusColdChainConstants.RefrigeratedReferenceCelsius);

        double totalDecay = totalExcursionDecay + (baselineStorageHours * rateMinus20);
        double potency = Math.Clamp(100.0 * Math.Exp(-totalDecay), 0.0, 100.0);
        double budgetUsedPercent = (totalDecay / ArrheniusColdChainConstants.MaxAllowableDecayExponent) * 100.0;
        bool isPastNominalExpiry = evaluationUtc > nominalExpiryUtc;

        double remainingBudget = Math.Max(0.0, ArrheniusColdChainConstants.MaxAllowableDecayExponent - totalDecay);
        double remainingDaysMinus20 = (hasCatastrophic || isPastNominalExpiry) ? 0.0 : remainingBudget / (rateMinus20 * 24.0);
        double remainingDays4C = (hasCatastrophic || isPastNominalExpiry) ? 0.0 : remainingBudget / (rate4C * 24.0);

        ThermalStabilityStatus status;
        if (hasCatastrophic)
        {
            status = ThermalStabilityStatus.CatastrophicDenaturation;
        }
        else if (potency < ArrheniusColdChainConstants.MinimumSafePotencyPercent || isPastNominalExpiry)
        {
            status = ThermalStabilityStatus.ExpiredOrCompromised;
        }
        else if (budgetUsedPercent >= ArrheniusColdChainConstants.AdvisoryBudgetPercent || hasAdvisoryTemp)
        {
            status = ThermalStabilityStatus.AdvisoryExcursion;
        }
        else
        {
            status = ThermalStabilityStatus.Optimal;
        }

        bool isSafeForImplantation = potency >= ArrheniusColdChainConstants.MinimumSafePotencyPercent
            && !hasCatastrophic
            && !isPastNominalExpiry
            && chainCheck.IsValid;

        double roundedPotency = Math.Round(potency, 2, MidpointRounding.AwayFromZero);
        double roundedBudget = Math.Round(budgetUsedPercent, 2, MidpointRounding.AwayFromZero);
        double roundedDaysMinus20 = Math.Round(remainingDaysMinus20, 2, MidpointRounding.AwayFromZero);
        double roundedDays4C = Math.Round(remainingDays4C, 2, MidpointRounding.AwayFromZero);

        string summary = BuildSummaryText(status, roundedPotency, roundedBudget, isPastNominalExpiry, chainCheck);

        return new LotStabilityReport
        {
            LotId = lot.LotId,
            PatientId = lot.PatientId,
            EvaluatedAtUtc = evaluationUtc,
            NominalExpiryAtUtc = nominalExpiryUtc,
            RemainingPotencyPercent = roundedPotency,
            CumulativeThermalBudgetUsedPercent = roundedBudget,
            EstimatedRemainingShelfLifeDaysAtMinus20C = roundedDaysMinus20,
            EstimatedRemainingShelfLifeDaysAt4C = roundedDays4C,
            Status = status,
            HasCatastrophicExcursion = hasCatastrophic,
            IsPastNominalExpiry = isPastNominalExpiry,
            CustodyChainValid = chainCheck.IsValid,
            CustodyChainFailureReason = chainCheck.FailureReason,
            IsSafeForImplantation = isSafeForImplantation,
            ExcursionCount = excursions.Count,
            CustodyHandoffCount = handoffs.Count,
            Summary = summary
        };
    }

    /// <summary>
    /// Formats a plain-English clinical summary explaining cold-chain stability and custody-chain status.
    /// </summary>
    /// <param name="status">Evaluated thermal stability status.</param>
    /// <param name="potencyPercent">Rounded remaining potency percentage.</param>
    /// <param name="budgetPercent">Rounded cumulative thermal budget percentage used.</param>
    /// <param name="isPastNominalExpiry">Whether the 365-day nominal expiry date has passed.</param>
    /// <param name="chainCheck">Custody hash-chain integrity verification verdict.</param>
    /// <returns>Human-readable clinical diagnostic summary.</returns>
    private static string BuildSummaryText(
        ThermalStabilityStatus status,
        double potencyPercent,
        double budgetPercent,
        bool isPastNominalExpiry,
        CustodyChainVerificationResult chainCheck)
    {
        // WHAT: Builds a concise clinical diagnostic summary prioritizing tamper and catastrophic melting alerts.
        // WHY:  Gives clinicians an unambiguous go/no-go explanation in UI badges, API responses, and CoA verification.
        // HOW:  Evaluates custody tamper first, then thermal status branches using invariant formatting.
        if (!chainCheck.IsValid)
        {
            return $"Custody chain integrity compromised: {chainCheck.FailureReason}";
        }

        if (status == ThermalStabilityStatus.CatastrophicDenaturation)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Catastrophic dsDNA origami denaturation (> 42.0°C excursion): {potencyPercent:F2}% potency remaining. Permanently unsafe for implantation.");
        }

        if (status == ThermalStabilityStatus.ExpiredOrCompromised)
        {
            return isPastNominalExpiry
                ? string.Create(CultureInfo.InvariantCulture, $"Lot passed nominal 365-day shelf-life expiry ({potencyPercent:F2}% potency). Unsafe for implantation.")
                : string.Create(CultureInfo.InvariantCulture, $"Thermal degradation exceeded safe threshold ({potencyPercent:F2}% potency remaining, {budgetPercent:F2}% budget used). Unsafe for implantation.");
        }

        if (status == ThermalStabilityStatus.AdvisoryExcursion)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Advisory thermal exposure recorded: {potencyPercent:F2}% potency remaining ({budgetPercent:F2}% thermal budget used); custody chain verified.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Optimal cold-chain stability: {potencyPercent:F2}% potency remaining ({budgetPercent:F2}% thermal budget used); custody chain verified.");
    }

    /// <summary>
    /// Validates that a temperature value is finite and within physical cold-chain bounds (-196°C to +121°C).
    /// </summary>
    /// <param name="temperatureCelsius">Candidate temperature in degrees Celsius.</param>
    private static void ValidateTemperature(double temperatureCelsius)
    {
        // WHAT: Guards against NaN, Infinity, or physically impossible temperature entries.
        // WHY:  Prevents arithmetic overflow in Arrhenius exp(-Ea/(RT)) and protects data integrity.
        // HOW:  Checks double.IsFinite and [MinValidTemperatureCelsius, MaxValidTemperatureCelsius] bounds.
        if (!double.IsFinite(temperatureCelsius)
            || temperatureCelsius < ArrheniusColdChainConstants.MinValidTemperatureCelsius
            || temperatureCelsius > ArrheniusColdChainConstants.MaxValidTemperatureCelsius)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperatureCelsius),
                $"TemperatureCelsius must be finite and between {ArrheniusColdChainConstants.MinValidTemperatureCelsius}°C and {ArrheniusColdChainConstants.MaxValidTemperatureCelsius}°C.");
        }
    }

    /// <summary>
    /// Normalizes a <see cref="DateTimeOffset"/> to UTC truncated to exact microsecond precision (6 fractional digits).
    /// </summary>
    /// <param name="timestamp">Input timestamp.</param>
    /// <returns>UTC timestamp with sub-microsecond ticks removed for MySQL <c>DATETIME(6)</c> SHA-256 parity.</returns>
    private static DateTimeOffset NormalizeToMicroseconds(DateTimeOffset timestamp)
    {
        // WHAT: Converts timestamp to UTC and clears the final 100-nanosecond digit.
        // WHY:  MySQL/MariaDB DATETIME(6) stores 6 fractional digits while .NET ticks have 7; truncating ensures identical SHA-256 hashes across all 4 engines.
        // HOW:  Computes utc.Ticks - (utc.Ticks % 10) and constructs a UTC DateTimeOffset.
        DateTimeOffset utc = timestamp.ToUniversalTime();
        long microsecondTicks = utc.Ticks - (utc.Ticks % 10);
        return new DateTimeOffset(microsecondTicks, TimeSpan.Zero);
    }

    /// <summary>
    /// Resolves whether <see cref="RequirePairedAntidoteConfigKey"/> is currently enabled in host or runtime configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns><see langword="true"/> when active paired reversal antidotes are mandatory before patient administration.</returns>
    private async Task<bool> IsPairedAntidoteRequiredAsync(CancellationToken cancellationToken)
    {
        // WHAT: Evaluates Fabrication:RequirePairedAntidote across IConfiguration and IConfigurationRepository.
        // WHY:  Milestone 44 (P231): Supports both static host config overrides and live database config toggles without restarts.
        // HOW:  Checks _configuration first and falls back to _configurationRepository.GetValueAsync when present.
        if (string.Equals(_configuration?[RequirePairedAntidoteConfigKey], "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_configurationRepository is not null)
        {
            string value = await _configurationRepository
                .GetValueAsync(RequirePairedAntidoteConfigKey, "false", cancellationToken)
                .ConfigureAwait(false);
            return string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
