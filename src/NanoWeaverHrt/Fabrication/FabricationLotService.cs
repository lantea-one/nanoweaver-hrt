using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Deterministic 5-gate pharmaceutical wet-lab quality control verification and NIST FIPS 204 ML-DSA-65
/// Certificate of Analysis signing engine.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The authoritative implementation of <see cref="IFabricationLotService"/>
/// enforcing biochemical quality thresholds on fabricated DNA origami nanocarrier batches.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Prevents mis-folded origami, bacterial endotoxin contamination, aggregated
/// nanoparticles, under-loaded payloads, or unmethylated TLR9-immunogenic CpG motifs from ever receiving a
/// cryptographically signed clinical release certificate.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses zero-LINQ fixed-array gate indexing, deterministic <see cref="Utf8JsonWriter"/>
/// canonical serialization, SHA-256 digest binding, and post-quantum ML-DSA-65 signing via <see cref="IKeyManagementService"/>.
/// </para>
/// </remarks>
public sealed class FabricationLotService : IFabricationLotService
{
    /// <summary>
    /// Minimum agarose gel electrophoresis monomeric folding yield percentage (85.0%).
    /// </summary>
    public const double MinGelFoldingYieldPercent = 85.0;

    /// <summary>
    /// Maximum allowable bacterial endotoxin concentration per USP &lt;85&gt; (0.50 EU/mL).
    /// </summary>
    public const double MaxEndotoxinEuPerMl = 0.50;

    /// <summary>
    /// Minimum acceptable DLS hydrodynamic diameter in nanometers (15.0 nm).
    /// </summary>
    public const double MinHydrodynamicDiameterNm = 15.0;

    /// <summary>
    /// Maximum acceptable DLS hydrodynamic diameter in nanometers (120.0 nm).
    /// </summary>
    public const double MaxHydrodynamicDiameterNm = 120.0;

    /// <summary>
    /// Maximum acceptable DLS polydispersity index (PDI &lt;= 0.20).
    /// </summary>
    public const double MaxPolydispersityIndex = 0.20;

    /// <summary>
    /// Minimum acceptable HPLC payload encapsulation efficiency percentage (80.0%).
    /// </summary>
    public const double MinPayloadEncapsulationPercent = 80.0;

    /// <summary>
    /// Maximum acceptable absolute gravimetric payload mass deviation percentage (5.0%).
    /// </summary>
    public const double MaxMassDeviationPercent = 5.0;

    /// <summary>
    /// Minimum acceptable M.SssI enzymatic CpG motif methylation percentage (95.0%).
    /// </summary>
    public const double MinCpGMethylationPercent = 95.0;

    /// <summary>
    /// Total number of enum values in <see cref="FabricationQcGateType"/>.
    /// </summary>
    private const int GateTypeCount = 5;

    /// <summary>
    /// Post-quantum signature algorithm identifier embedded in Certificates of Analysis.
    /// </summary>
    private const string SignatureAlgorithmName = "ML-DSA-65";

    /// <summary>
    /// Node key management service providing persistent ML-DSA-65 signing without exposing private key material.
    /// </summary>
    private readonly IKeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptographic provider performing independent ML-DSA-65 signature verification.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FabricationLotService"/> class.
    /// </summary>
    /// <param name="keyManagementService">Node key management service supplying persistent ML-DSA-65 signing.</param>
    /// <param name="cryptoProvider">Post-quantum cryptographic provider for ML-DSA-65 signature verification.</param>
    /// <exception cref="ArgumentNullException">Thrown when either dependency is <see langword="null"/>.</exception>
    public FabricationLotService(
        IKeyManagementService keyManagementService,
        IPostQuantumCryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(keyManagementService);
        ArgumentNullException.ThrowIfNull(cryptoProvider);

        _keyManagementService = keyManagementService;
        _cryptoProvider = cryptoProvider;
    }

    /// <inheritdoc/>
    public FabricationQcGateEvaluation EvaluateSingleGate(
        FabricationQcGateType gateType,
        double measuredValue,
        double? secondaryValue = null)
    {
        // WHAT: Evaluates a single wet-lab QC reading against pharmaceutical release limits.
        // WHY:  Provides consistent, deterministic verdicts during both individual check recording and final lot release.
        // HOW:  Validates numeric finiteness then dispatches across the 5 biochemical QC gates.
        if (double.IsNaN(measuredValue) || double.IsInfinity(measuredValue))
        {
            throw new ArgumentOutOfRangeException(nameof(measuredValue), measuredValue, "Measured QC value must be a finite number.");
        }

        if (secondaryValue.HasValue && (double.IsNaN(secondaryValue.Value) || double.IsInfinity(secondaryValue.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(secondaryValue), secondaryValue, "Secondary QC value must be a finite number.");
        }

        switch (gateType)
        {
            case FabricationQcGateType.GelFoldingYield:
                {
                    bool passed = measuredValue >= MinGelFoldingYieldPercent;
                    string summary = passed
                        ? string.Format(CultureInfo.InvariantCulture, "Agarose gel folding yield {0:F2}% meets minimum threshold (>= {1:F1}%).", measuredValue, MinGelFoldingYieldPercent)
                        : string.Format(CultureInfo.InvariantCulture, "Agarose gel folding yield {0:F2}% is below minimum threshold (>= {1:F1}%).", measuredValue, MinGelFoldingYieldPercent);

                    return new FabricationQcGateEvaluation
                    {
                        GateType = gateType,
                        MeasuredValue = measuredValue,
                        SecondaryValue = secondaryValue,
                        ThresholdValue = MinGelFoldingYieldPercent,
                        ThresholdDescription = ">= 85.0% monomeric origami folding yield",
                        IsPresent = true,
                        Passed = passed,
                        Summary = summary
                    };
                }

            case FabricationQcGateType.EndotoxinSterility:
                {
                    bool passed = measuredValue >= 0.0 && measuredValue <= MaxEndotoxinEuPerMl;
                    string summary = passed
                        ? string.Format(CultureInfo.InvariantCulture, "LAL bacterial endotoxin {0:F3} EU/mL passes USP <85> sterility limit (<= {1:F2} EU/mL).", measuredValue, MaxEndotoxinEuPerMl)
                        : string.Format(CultureInfo.InvariantCulture, "LAL bacterial endotoxin {0:F3} EU/mL fails USP <85> sterility limit (0.00 to {1:F2} EU/mL).", measuredValue, MaxEndotoxinEuPerMl);

                    return new FabricationQcGateEvaluation
                    {
                        GateType = gateType,
                        MeasuredValue = measuredValue,
                        SecondaryValue = secondaryValue,
                        ThresholdValue = MaxEndotoxinEuPerMl,
                        ThresholdDescription = "<= 0.50 EU/mL LAL endotoxin (USP <85>)",
                        IsPresent = true,
                        Passed = passed,
                        Summary = summary
                    };
                }

            case FabricationQcGateType.HydrodynamicSizePdi:
                {
                    double pdi = secondaryValue ?? 0.0;
                    bool sizeOk = measuredValue >= MinHydrodynamicDiameterNm && measuredValue <= MaxHydrodynamicDiameterNm;
                    bool pdiOk = pdi >= 0.0 && pdi <= MaxPolydispersityIndex;
                    bool passed = sizeOk && pdiOk;
                    string summary = passed
                        ? string.Format(CultureInfo.InvariantCulture, "DLS hydrodynamic diameter {0:F1} nm (PDI {1:F3}) passes nanoparticle uniformity envelope.", measuredValue, pdi)
                        : string.Format(CultureInfo.InvariantCulture, "DLS hydrodynamic diameter {0:F1} nm (PDI {1:F3}) fails uniformity limits (15.0-120.0 nm, PDI <= 0.20).", measuredValue, pdi);

                    return new FabricationQcGateEvaluation
                    {
                        GateType = gateType,
                        MeasuredValue = measuredValue,
                        SecondaryValue = pdi,
                        ThresholdValue = MaxHydrodynamicDiameterNm,
                        ThresholdDescription = "15.0-120.0 nm DLS diameter and PDI <= 0.20",
                        IsPresent = true,
                        Passed = passed,
                        Summary = summary
                    };
                }

            case FabricationQcGateType.PayloadEncapsulation:
                {
                    double massDeviationPct = secondaryValue ?? 0.0;
                    bool encapOk = measuredValue >= MinPayloadEncapsulationPercent;
                    bool massOk = Math.Abs(massDeviationPct) <= MaxMassDeviationPercent;
                    bool passed = encapOk && massOk;
                    string summary = passed
                        ? string.Format(CultureInfo.InvariantCulture, "HPLC payload encapsulation {0:F2}% (mass deviation {1:F2}%) meets loading specification.", measuredValue, massDeviationPct)
                        : string.Format(CultureInfo.InvariantCulture, "HPLC payload encapsulation {0:F2}% (mass deviation {1:F2}%) fails loading limits (>= 80.0%, |deviation| <= 5.0%).", measuredValue, massDeviationPct);

                    return new FabricationQcGateEvaluation
                    {
                        GateType = gateType,
                        MeasuredValue = measuredValue,
                        SecondaryValue = massDeviationPct,
                        ThresholdValue = MinPayloadEncapsulationPercent,
                        ThresholdDescription = ">= 80.0% HPLC encapsulation and |mass deviation| <= 5.0%",
                        IsPresent = true,
                        Passed = passed,
                        Summary = summary
                    };
                }

            case FabricationQcGateType.CpGMethylation:
                {
                    bool passed = measuredValue >= MinCpGMethylationPercent;
                    string summary = passed
                        ? string.Format(CultureInfo.InvariantCulture, "M.SssI CpG methylation {0:F2}% passes TLR9 immune-evasion threshold (>= {1:F1}%).", measuredValue, MinCpGMethylationPercent)
                        : string.Format(CultureInfo.InvariantCulture, "M.SssI CpG methylation {0:F2}% is below TLR9 immune-evasion threshold (>= {1:F1}%).", measuredValue, MinCpGMethylationPercent);

                    return new FabricationQcGateEvaluation
                    {
                        GateType = gateType,
                        MeasuredValue = measuredValue,
                        SecondaryValue = secondaryValue,
                        ThresholdValue = MinCpGMethylationPercent,
                        ThresholdDescription = ">= 95.0% M.SssI CpG enzymatic methylation",
                        IsPresent = true,
                        Passed = passed,
                        Summary = summary
                    };
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(gateType), gateType, "Unsupported wet-lab QC gate type.");
        }
    }

    /// <inheritdoc/>
    public FabricationLotReleaseEvaluation EvaluateAndSignLotRelease(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks)
    {
        // WHAT: Evaluates all mandatory pharmaceutical QC gates and mints an ML-DSA-65 Certificate of Analysis on 100% pass.
        // WHY:  Guarantees fail-closed release safety so incomplete or out-of-spec wet-lab lots are automatically quarantined.
        // HOW:  Indexes latest check per gate via a fixed-size array without LINQ, evaluates required gates, and signs canonical UTF-8 JSON.
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(checks);

        DateTimeOffset evaluatedAtUtc = DateTimeOffset.UtcNow;
        (List<FabricationQcGateEvaluation> evaluations, List<string> failureReasons) = EvaluateRequiredGates(lot, checks);

        if (failureReasons.Count > 0)
        {
            return new FabricationLotReleaseEvaluation
            {
                LotId = lot.LotId,
                ResultStatus = FabricationLotStatus.Quarantined,
                AllRequiredGatesPassed = false,
                GateEvaluations = evaluations,
                FailureReasons = failureReasons,
                Certificate = null,
                EvaluatedAtUtc = evaluatedAtUtc
            };
        }

        byte[] canonicalBytes = BuildCanonicalCertificatePayloadBytes(
            lot,
            evaluations,
            evaluatedAtUtc);
        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        PqcSignature signature = _keyManagementService.SignWithNodeSigningKey(canonicalBytes);
        PqcKeyMaterial publicKey = _keyManagementService.GetNodeSigningKey();

        FabricationLotCertificate certificate = new()
        {
            LotId = lot.LotId,
            DepotId = lot.DepotId,
            CalibrationSnapshotId = lot.CalibrationSnapshotId,
            PatientId = lot.PatientId,
            Regimen = lot.Regimen,
            PolymerMatrixType = lot.PolymerMatrixType,
            TargetMassMg = lot.TargetMassMg,
            TargetErosionRatePercent = lot.TargetErosionRatePercent,
            CpGMethylationRequired = lot.CpGMethylationRequired,
            OpentronsProtocolSha256 = lot.OpentronsProtocolSha256,
            GateEvaluations = evaluations,
            IssuedAtUtc = evaluatedAtUtc,
            PayloadSha256 = payloadSha256,
            SignatureBase64 = signature.SignatureBase64,
            SignerKeyId = signature.SignerKeyId,
            SignerPublicKeyBase64 = publicKey.PublicKeyBase64,
            SignatureAlgorithm = SignatureAlgorithmName
        };

        return new FabricationLotReleaseEvaluation
        {
            LotId = lot.LotId,
            ResultStatus = FabricationLotStatus.Released,
            AllRequiredGatesPassed = true,
            GateEvaluations = evaluations,
            FailureReasons = [],
            Certificate = certificate,
            EvaluatedAtUtc = evaluatedAtUtc
        };
    }

    /// <inheritdoc/>
    public FabricationLotCertificate? ReconstructCertificate(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks)
    {
        // WHAT: Rebuilds a FabricationLotCertificate from persisted database lot and QC check records.
        // WHY:  Enables live tamper-evident cryptographic re-verification whenever the certificate endpoint is queried.
        // HOW:  Returns null unless the lot is Released with stored signature fields, then re-evaluates gates and attaches node public key.
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(checks);

        if (lot.LotStatus != FabricationLotStatus.Released
            || !lot.ReleasedAtUtc.HasValue
            || string.IsNullOrWhiteSpace(lot.CertificatePayloadSha256)
            || string.IsNullOrWhiteSpace(lot.CertificateSignatureBase64)
            || string.IsNullOrWhiteSpace(lot.CertificateSignerKeyId))
        {
            return null;
        }

        (List<FabricationQcGateEvaluation> evaluations, _) = EvaluateRequiredGates(lot, checks);
        PqcKeyMaterial publicKey = _keyManagementService.GetNodeSigningKey();

        return new FabricationLotCertificate
        {
            LotId = lot.LotId,
            DepotId = lot.DepotId,
            CalibrationSnapshotId = lot.CalibrationSnapshotId,
            PatientId = lot.PatientId,
            Regimen = lot.Regimen,
            PolymerMatrixType = lot.PolymerMatrixType,
            TargetMassMg = lot.TargetMassMg,
            TargetErosionRatePercent = lot.TargetErosionRatePercent,
            CpGMethylationRequired = lot.CpGMethylationRequired,
            OpentronsProtocolSha256 = lot.OpentronsProtocolSha256,
            GateEvaluations = evaluations,
            IssuedAtUtc = lot.ReleasedAtUtc.Value,
            PayloadSha256 = lot.CertificatePayloadSha256,
            SignatureBase64 = lot.CertificateSignatureBase64,
            SignerKeyId = lot.CertificateSignerKeyId,
            SignerPublicKeyBase64 = publicKey.PublicKeyBase64,
            SignatureAlgorithm = SignatureAlgorithmName
        };
    }

    /// <inheritdoc/>
    public bool VerifyLotCertificate(FabricationLotCertificate certificate)
    {
        // WHAT: Independently validates a Certificate of Analysis's canonical SHA-256 hash and ML-DSA-65 signature.
        // WHY:  Detects any post-release tampering of target mass, Opentrons protocol digest, or wet-lab QC metrics.
        // HOW:  Reconstructs the canonical UTF-8 payload, compares SHA-256 hex digest, and invokes ML-DSA-65 verification.
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrWhiteSpace(certificate.PayloadSha256)
            || string.IsNullOrWhiteSpace(certificate.SignatureBase64)
            || string.IsNullOrWhiteSpace(certificate.SignerPublicKeyBase64))
        {
            return false;
        }

        FabricationLotRecord lotProjection = new()
        {
            LotId = certificate.LotId,
            DepotId = certificate.DepotId,
            CalibrationSnapshotId = certificate.CalibrationSnapshotId,
            PatientId = certificate.PatientId,
            Regimen = certificate.Regimen,
            PolymerMatrixType = certificate.PolymerMatrixType,
            TargetMassMg = certificate.TargetMassMg,
            TargetErosionRatePercent = certificate.TargetErosionRatePercent,
            CpGMethylationRequired = certificate.CpGMethylationRequired,
            OpentronsProtocolSha256 = certificate.OpentronsProtocolSha256
        };

        byte[] canonicalBytes = BuildCanonicalCertificatePayloadBytes(
            lotProjection,
            certificate.GateEvaluations,
            certificate.IssuedAtUtc);
        string recomputedSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));

        if (!string.Equals(recomputedSha256, certificate.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _cryptoProvider.VerifySignature(
            canonicalBytes,
            certificate.SignatureBase64,
            certificate.SignerPublicKeyBase64);
    }

    /// <inheritdoc/>
    public string ComputeOpentronsProtocolSha256(
        string depotId,
        string patientId,
        TherapeuticRegimen regimen,
        string polymerMatrixType,
        double targetMassMg,
        double targetErosionRatePercent)
    {
        // WHAT: Computes a deterministic SHA-256 hex fingerprint for an adaptive Opentrons synthesis specification.
        // WHY:  Binds every minted wet-lab lot to its exact dispensing parameters when no custom script digest is supplied.
        // HOW:  Formats invariant canonical parameters into UTF-8 bytes and computes lowercase SHA-256 hex.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(polymerMatrixType);

        string canonicalSpec = string.Format(
            CultureInfo.InvariantCulture,
            "nanoweaver-opentrons-v2|depot={0}|patient={1}|regimen={2}|matrix={3}|mass_mg={4:F4}|erosion_pct={5:F4}",
            depotId.Trim(),
            patientId.Trim(),
            regimen,
            polymerMatrixType.Trim(),
            targetMassMg,
            targetErosionRatePercent);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSpec));
        return Convert.ToHexStringLower(hash);
    }

    /// <inheritdoc/>
    public FabricationCertificateVerificationReport VerifyCertificateAgainstTrustAnchor(
        FabricationLotCertificate certificate,
        PeerKeyRecord? peerKey,
        FabricationLotRevocationRecord? revocation)
    {
        // WHAT: Evaluates mathematical ML-DSA-65 validity, signer trust provenance, and lot-level recall revocation.
        // WHY:  Milestone 36 (P191): Enables cross-node federated verification where a receiving clinic trusts only
        //       local or registered unrevoked ML-DSA-65 peer laboratories and blocks recalled lots fail-closed.
        // HOW:  Verifies canonical SHA-256 + ML-DSA-65 signature first, then resolves LocalNode vs TrustedPeer vs
        //       RevokedSigner vs UntrustedSigner and overlays any permanent lot revocation tombstone.
        ArgumentNullException.ThrowIfNull(certificate);

        DateTimeOffset verifiedAtUtc = DateTimeOffset.UtcNow;
        bool isRevoked = revocation is not null;
        string? revocationReason = isRevoked ? revocation!.RevocationReason : null; // WHY `!`: guarded by isRevoked (revocation is not null).
        string? revokedBy = isRevoked ? revocation!.RevokedBy : null; // WHY `!`: guarded by isRevoked (revocation is not null).
        DateTimeOffset? revokedAtUtc = isRevoked ? revocation!.RevokedAtUtc : null; // WHY `!`: guarded by isRevoked (revocation is not null).

        if (!VerifyLotCertificate(certificate))
        {
            return new FabricationCertificateVerificationReport
            {
                LotId = certificate.LotId,
                PayloadSha256 = certificate.PayloadSha256,
                SignatureValid = false,
                IsTrusted = false,
                IsRevoked = isRevoked,
                TrustSource = FabricationCertificateTrustSource.InvalidSignature,
                TrustedPeerLabel = null,
                SignerKeyId = certificate.SignerKeyId,
                RevocationReason = revocationReason,
                RevokedBy = revokedBy,
                RevokedAtUtc = revokedAtUtc,
                Summary = "Cryptographic verification failed: canonical SHA-256 digest or ML-DSA-65 signature is invalid.",
                VerifiedAtUtc = verifiedAtUtc
            };
        }

        PqcKeyMaterial localKey = _keyManagementService.GetNodeSigningKey();
        bool matchesLocalNode = string.Equals(certificate.SignerKeyId, localKey.KeyId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(certificate.SignerPublicKeyBase64, localKey.PublicKeyBase64, StringComparison.Ordinal);

        if (matchesLocalNode)
        {
            bool trusted = !isRevoked;
            string summary = trusted
                ? "Verified ML-DSA-65 Certificate of Analysis issued by this sovereign node."
                : $"Signature is valid for this local node, but lot '{certificate.LotId}' was permanently revoked: {revocationReason}";

            return new FabricationCertificateVerificationReport
            {
                LotId = certificate.LotId,
                PayloadSha256 = certificate.PayloadSha256,
                SignatureValid = true,
                IsTrusted = trusted,
                IsRevoked = isRevoked,
                TrustSource = FabricationCertificateTrustSource.LocalNode,
                TrustedPeerLabel = null,
                SignerKeyId = certificate.SignerKeyId,
                RevocationReason = revocationReason,
                RevokedBy = revokedBy,
                RevokedAtUtc = revokedAtUtc,
                Summary = summary,
                VerifiedAtUtc = verifiedAtUtc
            };
        }

        if (peerKey is not null
            && peerKey.Algorithm == KeyAlgorithm.MlDsa65
            && string.Equals(peerKey.KeyId, certificate.SignerKeyId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(peerKey.PublicKeyBase64, certificate.SignerPublicKeyBase64, StringComparison.Ordinal))
        {
            if (!peerKey.IsActive)
            {
                return new FabricationCertificateVerificationReport
                {
                    LotId = certificate.LotId,
                    PayloadSha256 = certificate.PayloadSha256,
                    SignatureValid = true,
                    IsTrusted = false,
                    IsRevoked = isRevoked,
                    TrustSource = FabricationCertificateTrustSource.RevokedSigner,
                    TrustedPeerLabel = peerKey.Label,
                    SignerKeyId = certificate.SignerKeyId,
                    RevocationReason = revocationReason,
                    RevokedBy = revokedBy,
                    RevokedAtUtc = revokedAtUtc,
                    Summary = $"Untrusted: peer laboratory key '{peerKey.Label}' has been permanently revoked.",
                    VerifiedAtUtc = verifiedAtUtc
                };
            }

            bool trusted = !isRevoked;
            string summary = trusted
                ? $"Verified ML-DSA-65 Certificate of Analysis issued by trusted peer laboratory '{peerKey.Label}'."
                : $"Peer signature from '{peerKey.Label}' is valid, but lot '{certificate.LotId}' was permanently revoked: {revocationReason}";

            return new FabricationCertificateVerificationReport
            {
                LotId = certificate.LotId,
                PayloadSha256 = certificate.PayloadSha256,
                SignatureValid = true,
                IsTrusted = trusted,
                IsRevoked = isRevoked,
                TrustSource = FabricationCertificateTrustSource.TrustedPeer,
                TrustedPeerLabel = peerKey.Label,
                SignerKeyId = certificate.SignerKeyId,
                RevocationReason = revocationReason,
                RevokedBy = revokedBy,
                RevokedAtUtc = revokedAtUtc,
                Summary = summary,
                VerifiedAtUtc = verifiedAtUtc
            };
        }

        return new FabricationCertificateVerificationReport
        {
            LotId = certificate.LotId,
            PayloadSha256 = certificate.PayloadSha256,
            SignatureValid = true,
            IsTrusted = false,
            IsRevoked = isRevoked,
            TrustSource = FabricationCertificateTrustSource.UntrustedSigner,
            TrustedPeerLabel = null,
            SignerKeyId = certificate.SignerKeyId,
            RevocationReason = revocationReason,
            RevokedBy = revokedBy,
            RevokedAtUtc = revokedAtUtc,
            Summary = "Signature is mathematically valid, but the signer ML-DSA-65 key is not registered in this node's trusted peer key registry.",
            VerifiedAtUtc = verifiedAtUtc
        };
    }

    /// <summary>
    /// Evaluates all required wet-lab QC gates for the given lot using the latest recorded check per gate.
    /// </summary>
    /// <param name="lot">The fabrication lot specification.</param>
    /// <param name="checks">All wet-lab QC checks associated with the lot.</param>
    /// <returns>A tuple containing the ordered gate evaluations and any quarantine failure reasons.</returns>
    private (List<FabricationQcGateEvaluation> Evaluations, List<string> FailureReasons) EvaluateRequiredGates(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks)
    {
        // WHAT: Selects the most recent check for each QC gate without LINQ and evaluates all required gates.
        // WHY:  Supports re-testing a failed assay while ensuring every required gate has a passing latest reading.
        // HOW:  Scans checks once into a 5-element array indexed by enum value, then evaluates required gates in order.
        FabricationQcCheckRecord?[] latestByGate = new FabricationQcCheckRecord?[GateTypeCount];
        for (int i = 0; i < checks.Count; i++)
        {
            FabricationQcCheckRecord check = checks[i];
            int gateIndex = (int)check.GateType;
            if (gateIndex < 0 || gateIndex >= GateTypeCount)
            {
                continue;
            }

            FabricationQcCheckRecord? existing = latestByGate[gateIndex];
            if (existing is null || check.RecordedAtUtc >= existing.RecordedAtUtc)
            {
                latestByGate[gateIndex] = check;
            }
        }

        int requiredCount = lot.CpGMethylationRequired ? 5 : 4;
        List<FabricationQcGateEvaluation> evaluations = new(requiredCount);
        List<string> failureReasons = [];

        for (int gateIndex = 0; gateIndex < requiredCount; gateIndex++)
        {
            FabricationQcGateType gateType = (FabricationQcGateType)gateIndex;
            FabricationQcCheckRecord? latest = latestByGate[gateIndex];

            if (latest is null)
            {
                FabricationQcGateEvaluation missingEval = BuildMissingGateEvaluation(gateType);
                evaluations.Add(missingEval);
                failureReasons.Add(missingEval.Summary);
                continue;
            }

            FabricationQcGateEvaluation eval = EvaluateSingleGate(gateType, latest.MeasuredValue, latest.SecondaryValue);
            evaluations.Add(eval);
            if (!eval.Passed)
            {
                failureReasons.Add(eval.Summary);
            }
        }

        return (evaluations, failureReasons);
    }

    /// <summary>
    /// Creates a standardized missing-measurement evaluation record for a required wet-lab QC gate.
    /// </summary>
    /// <param name="gateType">The required gate that has no recorded measurements.</param>
    /// <returns>A failing <see cref="FabricationQcGateEvaluation"/> marked with <c>IsPresent = false</c>.</returns>
    private static FabricationQcGateEvaluation BuildMissingGateEvaluation(FabricationQcGateType gateType)
    {
        // WHAT: Constructs a clear quarantine evaluation when a mandatory wet-lab QC gate has never been recorded.
        // WHY:  Prevents silent omission of sterility, folding, size, encapsulation, or methylation assays.
        // HOW:  Maps each gate to its normative numeric threshold and description with Passed = false and IsPresent = false.
        (double threshold, string description) = gateType switch
        {
            FabricationQcGateType.GelFoldingYield => (MinGelFoldingYieldPercent, ">= 85.0% monomeric origami folding yield"),
            FabricationQcGateType.EndotoxinSterility => (MaxEndotoxinEuPerMl, "<= 0.50 EU/mL LAL endotoxin (USP <85>)"),
            FabricationQcGateType.HydrodynamicSizePdi => (MaxHydrodynamicDiameterNm, "15.0-120.0 nm DLS diameter and PDI <= 0.20"),
            FabricationQcGateType.PayloadEncapsulation => (MinPayloadEncapsulationPercent, ">= 80.0% HPLC encapsulation and |mass deviation| <= 5.0%"),
            _ => (MinCpGMethylationPercent, ">= 95.0% M.SssI CpG enzymatic methylation")
        };

        return new FabricationQcGateEvaluation
        {
            GateType = gateType,
            MeasuredValue = 0.0,
            SecondaryValue = null,
            ThresholdValue = threshold,
            ThresholdDescription = description,
            IsPresent = false,
            Passed = false,
            Summary = $"Missing required wet-lab QC measurement for gate {gateType}."
        };
    }

    /// <summary>
    /// Serializes the canonical certificate signing payload into deterministic UTF-8 JSON bytes.
    /// </summary>
    /// <param name="lot">The fabrication lot metadata bound by the certificate.</param>
    /// <param name="evaluations">The ordered wet-lab QC gate evaluations bound by the certificate.</param>
    /// <param name="issuedAtUtc">The exact UTC timestamp when the certificate is issued.</param>
    /// <returns>Deterministic UTF-8 JSON payload bytes ready for SHA-256 hashing and ML-DSA-65 signing.</returns>
    private static byte[] BuildCanonicalCertificatePayloadBytes(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcGateEvaluation> evaluations,
        DateTimeOffset issuedAtUtc)
    {
        // WHAT: Writes a strictly ordered canonical UTF-8 JSON document binding lot parameters and QC measurements.
        // WHY:  Ensures identical byte-for-byte reproduction across signing and independent post-release verification.
        // HOW:  Uses ArrayBufferWriter<byte> and Utf8JsonWriter with fixed field ordering and round-trip numeric strings.
        ArrayBufferWriter<byte> buffer = new(1024);
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("lotId", lot.LotId);
            writer.WriteString("depotId", lot.DepotId);
            writer.WriteString("calibrationSnapshotId", lot.CalibrationSnapshotId ?? string.Empty);
            writer.WriteString("patientId", lot.PatientId);
            writer.WriteString("regimen", lot.Regimen.ToString());
            writer.WriteString("polymerMatrixType", lot.PolymerMatrixType);
            writer.WriteString("targetMassMg", lot.TargetMassMg.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteString("targetErosionRatePercent", lot.TargetErosionRatePercent.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteBoolean("cpgMethylationRequired", lot.CpGMethylationRequired);
            writer.WriteString("opentronsProtocolSha256", lot.OpentronsProtocolSha256);
            writer.WriteString("issuedAtUtc", issuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

            writer.WriteStartArray("gateEvaluations");
            for (int i = 0; i < evaluations.Count; i++)
            {
                FabricationQcGateEvaluation eval = evaluations[i];
                writer.WriteStartObject();
                writer.WriteString("gateType", eval.GateType.ToString());
                writer.WriteString("measuredValue", eval.MeasuredValue.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteString("secondaryValue", eval.SecondaryValue.HasValue
                    ? eval.SecondaryValue.Value.ToString("R", CultureInfo.InvariantCulture)
                    : string.Empty);
                writer.WriteString("thresholdValue", eval.ThresholdValue.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteBoolean("isPresent", eval.IsPresent);
                writer.WriteBoolean("passed", eval.Passed);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Canonical schema version stamped on signed ML-DSA-65 lot revocation feeds.
    /// </summary>
    private const string RevocationFeedVersion = "nanoweaver-revocation-feed-v1";

    /// <inheritdoc/>
    public SignedLotRevocationFeed BuildSignedRevocationFeed(
        IReadOnlyList<FabricationLotRevocationRecord> revocations,
        DateTimeOffset? issuedAtUtc = null)
    {
        // WHAT: Projects local revocation tombstones into de-identified entries, hashes canonically, and signs with ML-DSA-65.
        // WHY:  Milestone 38 (P201): Lets peer laboratories pull and verify upstream batch recalls without leaking patient IDs.
        // HOW:  Copies entries without LINQ, sorts deterministically by LotId, hashes UTF-8 JSON with SHA-256, and signs via node key.
        ArgumentNullException.ThrowIfNull(revocations);

        DateTimeOffset effectiveIssuedAt = (issuedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        List<SignedLotRevocationEntry> entries = new(revocations.Count);
        for (int i = 0; i < revocations.Count; i++)
        {
            FabricationLotRevocationRecord rev = revocations[i];
            entries.Add(new SignedLotRevocationEntry
            {
                LotId = rev.LotId,
                PayloadSha256 = rev.PayloadSha256,
                RevokedBy = rev.RevokedBy,
                RevocationReason = rev.RevocationReason,
                RevokedAtUtc = rev.RevokedAtUtc.ToUniversalTime()
            });
        }

        entries.Sort(static (a, b) => string.Compare(a.LotId, b.LotId, StringComparison.Ordinal));

        byte[] canonicalBytes = BuildCanonicalRevocationFeedBytes(RevocationFeedVersion, effectiveIssuedAt, entries);
        string payloadSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        PqcKeyMaterial nodeKey = _keyManagementService.GetNodeSigningKey();
        PqcSignature signature = _keyManagementService.SignWithNodeSigningKey(canonicalBytes);

        return new SignedLotRevocationFeed
        {
            FeedVersion = RevocationFeedVersion,
            IssuedAtUtc = effectiveIssuedAt,
            Entries = entries,
            PayloadSha256 = payloadSha256,
            SignatureBase64 = signature.SignatureBase64,
            SignerKeyId = nodeKey.KeyId,
            SignerPublicKeyBase64 = nodeKey.PublicKeyBase64,
            SignatureAlgorithm = SignatureAlgorithmName
        };
    }

    /// <inheritdoc/>
    public RevocationFeedVerificationResult VerifySignedRevocationFeed(
        SignedLotRevocationFeed feed,
        PeerKeyRecord? peerKey)
    {
        // WHAT: Independently verifies a peer revocation feed's canonical SHA-256 hash, ML-DSA-65 signature, and signer trust anchor.
        // WHY:  Milestone 38 (P201): Prevents malicious or untrusted nodes from injecting spoofed clinical lot recalls.
        // HOW:  Recomputes canonical UTF-8 payload, validates SHA-256 + ML-DSA-65, and resolves LocalNode/TrustedPeer/RevokedSigner/UntrustedSigner.
        ArgumentNullException.ThrowIfNull(feed);

        if (string.IsNullOrWhiteSpace(feed.FeedVersion)
            || string.IsNullOrWhiteSpace(feed.PayloadSha256)
            || string.IsNullOrWhiteSpace(feed.SignatureBase64)
            || string.IsNullOrWhiteSpace(feed.SignerKeyId)
            || string.IsNullOrWhiteSpace(feed.SignerPublicKeyBase64))
        {
            return new RevocationFeedVerificationResult
            {
                SignatureValid = false,
                IsTrusted = false,
                TrustSource = FabricationCertificateTrustSource.InvalidSignature,
                TrustedPeerLabel = null,
                Summary = "Revocation feed rejected: mandatory signature or signer metadata fields are missing."
            };
        }

        byte[] canonicalBytes = BuildCanonicalRevocationFeedBytes(
            feed.FeedVersion,
            feed.IssuedAtUtc.ToUniversalTime(),
            feed.Entries);
        string recomputedSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));

        if (!string.Equals(recomputedSha256, feed.PayloadSha256, StringComparison.OrdinalIgnoreCase)
            || !_cryptoProvider.VerifySignature(canonicalBytes, feed.SignatureBase64, feed.SignerPublicKeyBase64))
        {
            return new RevocationFeedVerificationResult
            {
                SignatureValid = false,
                IsTrusted = false,
                TrustSource = FabricationCertificateTrustSource.InvalidSignature,
                TrustedPeerLabel = null,
                Summary = "Revocation feed rejected: canonical SHA-256 digest or ML-DSA-65 signature verification failed."
            };
        }

        PqcKeyMaterial localKey = _keyManagementService.GetNodeSigningKey();
        if (string.Equals(feed.SignerKeyId, localKey.KeyId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(feed.SignerPublicKeyBase64, localKey.PublicKeyBase64, StringComparison.Ordinal))
        {
            return new RevocationFeedVerificationResult
            {
                SignatureValid = true,
                IsTrusted = true,
                TrustSource = FabricationCertificateTrustSource.LocalNode,
                TrustedPeerLabel = null,
                Summary = "Verified ML-DSA-65 revocation feed signed by this local sovereign node."
            };
        }

        if (peerKey is not null
            && peerKey.Algorithm == KeyAlgorithm.MlDsa65
            && string.Equals(peerKey.KeyId, feed.SignerKeyId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(peerKey.PublicKeyBase64, feed.SignerPublicKeyBase64, StringComparison.Ordinal))
        {
            if (!peerKey.IsActive)
            {
                return new RevocationFeedVerificationResult
                {
                    SignatureValid = true,
                    IsTrusted = false,
                    TrustSource = FabricationCertificateTrustSource.RevokedSigner,
                    TrustedPeerLabel = peerKey.Label,
                    Summary = $"Revocation feed rejected: peer laboratory key '{peerKey.Label}' has been permanently revoked."
                };
            }

            return new RevocationFeedVerificationResult
            {
                SignatureValid = true,
                IsTrusted = true,
                TrustSource = FabricationCertificateTrustSource.TrustedPeer,
                TrustedPeerLabel = peerKey.Label,
                Summary = $"Verified ML-DSA-65 revocation feed issued by trusted peer laboratory '{peerKey.Label}'."
            };
        }

        return new RevocationFeedVerificationResult
        {
            SignatureValid = true,
            IsTrusted = false,
            TrustSource = FabricationCertificateTrustSource.UntrustedSigner,
            TrustedPeerLabel = null,
            Summary = "Revocation feed rejected: signer ML-DSA-65 key is not registered in this node's trusted peer key registry."
        };
    }

    /// <summary>
    /// Converts a verified <see cref="FederatedLotRevocationRecord"/> into a <see cref="FabricationLotRevocationRecord"/>
    /// suitable for unified fail-closed evaluation inside <see cref="VerifyCertificateAgainstTrustAnchor"/>.
    /// </summary>
    /// <param name="federated">The imported peer recall tombstone.</param>
    /// <returns>A projected <see cref="FabricationLotRevocationRecord"/> carrying peer provenance in <c>RevokedBy</c> and reason.</returns>
    public static FabricationLotRevocationRecord ToEffectiveRevocation(FederatedLotRevocationRecord federated)
    {
        // WHAT: Adapts an upstream federated recall record into the unified revocation type consumed by CoA verification.
        // WHY:  Allows VerifyCertificateAgainstTrustAnchor to enforce local and federated recalls with identical fail-closed logic.
        // HOW:  Formats issuer attribution into RevokedBy and preserves exact reason, hash, and timestamp.
        ArgumentNullException.ThrowIfNull(federated);

        string sourceLabel = string.IsNullOrWhiteSpace(federated.IssuerPeerLabel)
            ? federated.IssuerKeyId
            : federated.IssuerPeerLabel;

        return new FabricationLotRevocationRecord
        {
            LotId = federated.LotId,
            PatientId = string.Empty,
            PayloadSha256 = federated.PayloadSha256,
            RevokedBy = $"{federated.RevokedBy}@{sourceLabel}",
            RevocationReason = federated.RevocationReason,
            RevokedAtUtc = federated.RevokedAtUtc
        };
    }

    /// <summary>
    /// Serializes a revocation feed header and entry array into deterministic UTF-8 JSON bytes for SHA-256 and ML-DSA-65 signing.
    /// </summary>
    /// <param name="feedVersion">Feed schema version string.</param>
    /// <param name="issuedAtUtc">Canonical UTC issuance timestamp.</param>
    /// <param name="entries">Ordered list of de-identified revocation entries.</param>
    /// <returns>Deterministic UTF-8 JSON bytes.</returns>
    private static byte[] BuildCanonicalRevocationFeedBytes(
        string feedVersion,
        DateTimeOffset issuedAtUtc,
        IReadOnlyList<SignedLotRevocationEntry> entries)
    {
        // WHAT: Produces byte-for-byte reproducible UTF-8 JSON bytes binding feed metadata and all recall entries.
        // WHY:  Ensures SHA-256 digest and ML-DSA-65 signature verification succeed across independent sovereign nodes.
        // HOW:  Writes fixed-order JSON properties with Utf8JsonWriter over an ArrayBufferWriter<byte>.
        ArrayBufferWriter<byte> buffer = new(1024);
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("feedVersion", feedVersion);
            writer.WriteString("issuedAtUtc", issuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartArray("entries");
            for (int i = 0; i < entries.Count; i++)
            {
                SignedLotRevocationEntry entry = entries[i];
                writer.WriteStartObject();
                writer.WriteString("lotId", entry.LotId);
                writer.WriteString("payloadSha256", entry.PayloadSha256 ?? string.Empty);
                writer.WriteString("revokedBy", entry.RevokedBy);
                writer.WriteString("revocationReason", entry.RevocationReason);
                writer.WriteString("revokedAtUtc", entry.RevokedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
