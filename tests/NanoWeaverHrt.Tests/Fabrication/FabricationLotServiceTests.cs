using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit tests for <see cref="FabricationLotService"/> and Milestone 35 domain models verifying all 5 wet-lab QC gates,
/// fail-closed quarantine, NIST FIPS 204 ML-DSA-65 signing, certificate reconstruction, and tamper detection.
/// </summary>
public sealed class FabricationLotServiceTests : IDisposable
{
    /// <summary>
    /// Isolated key management host providing real ML-DSA-65 signing and verification primitives.
    /// </summary>
    private readonly SubjectKeyTestHost _host;

    /// <summary>
    /// Real NIST FIPS 204 ML-DSA-65 post-quantum cryptographic provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Isolated node key management service providing persistent ML-DSA-65 signing.
    /// </summary>
    private readonly KeyManagementService _keyManagementService;

    /// <summary>
    /// Fabrication lot service under test.
    /// </summary>
    private readonly FabricationLotService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="FabricationLotServiceTests"/> class.
    /// </summary>
    public FabricationLotServiceTests()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_fab_lot_svc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _host = new SubjectKeyTestHost(tempDir);
        _cryptoProvider = _host.CryptoProvider;
        _keyManagementService = _host.KeyManagement;
        _service = new FabricationLotService(_keyManagementService, _cryptoProvider);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        string rootDir = _host.RootDirectory;
        _host.Dispose();
        if (Directory.Exists(rootDir))
        {
            try
            {
                Directory.Delete(rootDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies constructor null guards, argument validation, and OpenAPI example providers on fabrication domain models.
    /// </summary>
    [Fact]
    public void Guards_AndDomainExamples_BehaveCorrectly()
    {
        // WHAT: Tests constructor null checks, NaN/Infinity guards, invalid enum checks, and GetExamples() methods.
        // WHY:  Guarantees 100% defensive coverage on public domain contracts and Swagger example providers.
        // HOW:  Invokes every error path and verifies example objects carry expected non-empty fields.
        Assert.Throws<ArgumentNullException>(() => new FabricationLotService(null!, _cryptoProvider));
        Assert.Throws<ArgumentNullException>(() => new FabricationLotService(_keyManagementService, null!));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EvaluateSingleGate(FabricationQcGateType.GelFoldingYield, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EvaluateSingleGate(FabricationQcGateType.GelFoldingYield, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 50.0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 50.0, double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.EvaluateSingleGate((FabricationQcGateType)99, 50.0));

        Assert.Throws<ArgumentNullException>(() => _service.EvaluateAndSignLotRelease(null!, []));
        Assert.Throws<ArgumentNullException>(() => _service.EvaluateAndSignLotRelease(CreateSampleLot(true), null!));
        Assert.Throws<ArgumentNullException>(() => _service.ReconstructCertificate(null!, []));
        Assert.Throws<ArgumentNullException>(() => _service.ReconstructCertificate(CreateSampleLot(true), null!));
        Assert.Throws<ArgumentNullException>(() => _service.VerifyLotCertificate(null!));

        Assert.Throws<ArgumentException>(() => _service.ComputeOpentronsProtocolSha256("", "p1", TherapeuticRegimen.MaleToFemale, "PLGA-PEG-PLGA", 18.0, 0.55));
        Assert.Throws<ArgumentException>(() => _service.ComputeOpentronsProtocolSha256("d1", " ", TherapeuticRegimen.MaleToFemale, "PLGA-PEG-PLGA", 18.0, 0.55));
        Assert.Throws<ArgumentException>(() => _service.ComputeOpentronsProtocolSha256("d1", "p1", TherapeuticRegimen.MaleToFemale, "", 18.0, 0.55));

        FabricationQcGateEvaluation gateEx = new FabricationQcGateEvaluation().GetExamples();
        Assert.True(gateEx.Passed);
        Assert.True(gateEx.IsPresent);

        FabricationLotCertificate certEx = new FabricationLotCertificate().GetExamples();
        Assert.Equal("ML-DSA-65", certEx.SignatureAlgorithm);
        Assert.Equal("lot_sample_001", certEx.LotId);

        FabricationLotReleaseEvaluation relEx = new FabricationLotReleaseEvaluation().GetExamples();
        Assert.Equal(FabricationLotStatus.Released, relEx.ResultStatus);
        Assert.NotNull(relEx.Certificate);
    }

    /// <summary>
    /// Verifies exact pass and fail boundary conditions across all 5 pharmaceutical wet-lab QC gates.
    /// </summary>
    [Fact]
    public void EvaluateSingleGate_AllFiveGates_RespectExactPharmaceuticalThresholds()
    {
        // WHAT: Exercises every pass and fail boundary across GelFoldingYield, EndotoxinSterility,
        //       HydrodynamicSizePdi, PayloadEncapsulation, and CpGMethylation.
        // WHY:  Ensures biochemical safety limits cannot drift or accept out-of-spec batches.
        // HOW:  Tests exact boundary, passing, and failing primary/secondary combinations for all 5 gates.
        // Gate 1: GelFoldingYield >= 85.0%
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.GelFoldingYield, 85.0).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.GelFoldingYield, 84.99).Passed);

        // Gate 2: EndotoxinSterility 0.0 <= EU/mL <= 0.50
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.EndotoxinSterility, 0.0).Passed);
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.EndotoxinSterility, 0.50).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.EndotoxinSterility, 0.51).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.EndotoxinSterility, -0.01).Passed);

        // Gate 3: HydrodynamicSizePdi 15.0 <= nm <= 120.0 AND 0.0 <= PDI <= 0.20
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 15.0, 0.20).Passed);
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 120.0, null).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 14.9, 0.10).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 120.1, 0.10).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 60.0, 0.21).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.HydrodynamicSizePdi, 60.0, -0.05).Passed);

        // Gate 4: PayloadEncapsulation >= 80.0% AND |mass deviation| <= 5.0%
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.PayloadEncapsulation, 80.0, -5.0).Passed);
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.PayloadEncapsulation, 90.0, null).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.PayloadEncapsulation, 79.9, 0.0).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.PayloadEncapsulation, 90.0, 5.1).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.PayloadEncapsulation, 90.0, -5.1).Passed);

        // Gate 5: CpGMethylation >= 95.0%
        Assert.True(_service.EvaluateSingleGate(FabricationQcGateType.CpGMethylation, 95.0).Passed);
        Assert.False(_service.EvaluateSingleGate(FabricationQcGateType.CpGMethylation, 94.9).Passed);
    }

    /// <summary>
    /// Verifies that a lot passing all 5 gates receives a valid ML-DSA-65 Certificate of Analysis that survives
    /// database reconstruction and detects tampering on payload fields, QC readings, or signature bytes.
    /// </summary>
    [Fact]
    public void EvaluateAndSignLotRelease_AllFiveGatesPass_SignsReconstructsAndDetectsTampering()
    {
        // WHAT: Tests full 5-gate release signing with real ML-DSA-65 cryptography, re-testing superseding older failures,
        //       certificate reconstruction, and tamper rejection.
        // WHY:  Proves end-to-end cryptographic provenance and post-quantum integrity of wet-lab batch certificates.
        // HOW:  Submits an early failing check superseded by a newer passing check plus out-of-range enum check, signs,
        //       reconstructs from persisted record fields, verifies true, then mutates fields and asserts false.
        FabricationLotRecord lot = CreateSampleLot(cpgRequired: true);
        DateTimeOffset baseTime = new(2026, 11, 19, 11, 0, 0, TimeSpan.Zero);

        List<FabricationQcCheckRecord> checks =
        [
            // Out-of-range enum check ignored cleanly
            new() { CheckId = "qc_bad_enum_low", LotId = lot.LotId, PatientId = lot.PatientId, GateType = (FabricationQcGateType)(-1), MeasuredValue = 1.0, RecordedAtUtc = baseTime },
            new() { CheckId = "qc_bad_enum_high", LotId = lot.LotId, PatientId = lot.PatientId, GateType = (FabricationQcGateType)99, MeasuredValue = 1.0, RecordedAtUtc = baseTime },
            // Early failing gel check, superseded by later passing re-test, followed by an even earlier check that must NOT overwrite the latest
            new() { CheckId = "qc_gel_fail", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 70.0, RecordedAtUtc = baseTime.AddMinutes(1) },
            new() { CheckId = "qc_gel_pass", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 93.2, RecordedAtUtc = baseTime.AddMinutes(5) },
            new() { CheckId = "qc_gel_older", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 60.0, RecordedAtUtc = baseTime.AddMinutes(2) },
            new() { CheckId = "qc_endo", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.12, RecordedAtUtc = baseTime.AddMinutes(6) },
            new() { CheckId = "qc_dls", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 62.5, SecondaryValue = 0.11, RecordedAtUtc = baseTime.AddMinutes(7) },
            new() { CheckId = "qc_hplc", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 89.4, SecondaryValue = 1.8, RecordedAtUtc = baseTime.AddMinutes(8) },
            new() { CheckId = "qc_cpg", LotId = lot.LotId, PatientId = lot.PatientId, GateType = FabricationQcGateType.CpGMethylation, MeasuredValue = 98.1, RecordedAtUtc = baseTime.AddMinutes(9) }
        ];

        FabricationLotReleaseEvaluation result = _service.EvaluateAndSignLotRelease(lot, checks);
        Assert.Equal(FabricationLotStatus.Released, result.ResultStatus);
        Assert.True(result.AllRequiredGatesPassed);
        Assert.Empty(result.FailureReasons);
        Assert.Equal(5, result.GateEvaluations.Count);
        Assert.NotNull(result.Certificate);
        Assert.Equal("ML-DSA-65", result.Certificate.SignatureAlgorithm);
        Assert.True(_service.VerifyLotCertificate(result.Certificate));

        // Reconstruct from persisted released lot metadata and verify live cryptographic integrity
        FabricationLotRecord releasedLot = lot with
        {
            LotStatus = FabricationLotStatus.Released,
            CertificatePayloadSha256 = result.Certificate.PayloadSha256,
            CertificateSignatureBase64 = result.Certificate.SignatureBase64,
            CertificateSignerKeyId = result.Certificate.SignerKeyId,
            ReleasedAtUtc = result.Certificate.IssuedAtUtc
        };

        FabricationLotCertificate? reconstructed = _service.ReconstructCertificate(releasedLot, checks);
        Assert.NotNull(reconstructed);
        Assert.True(_service.VerifyLotCertificate(reconstructed));

        // Tamper checks: mutated mass, mutated hash, mutated signature, blank fields
        Assert.False(_service.VerifyLotCertificate(reconstructed with { TargetMassMg = 99.9 }));
        Assert.False(_service.VerifyLotCertificate(reconstructed with { PayloadSha256 = "0000" }));
        Assert.False(_service.VerifyLotCertificate(reconstructed with { PayloadSha256 = "" }));
        Assert.False(_service.VerifyLotCertificate(reconstructed with { SignatureBase64 = "" }));
        Assert.False(_service.VerifyLotCertificate(reconstructed with { SignerPublicKeyBase64 = "" }));

        byte[] tamperedSigBytes = Convert.FromBase64String(reconstructed.SignatureBase64);
        tamperedSigBytes[0] ^= 0xFF;
        Assert.False(_service.VerifyLotCertificate(reconstructed with { SignatureBase64 = Convert.ToBase64String(tamperedSigBytes) }));

        // Non-released or incomplete metadata returns null from ReconstructCertificate
        Assert.Null(_service.ReconstructCertificate(releasedLot with { LotStatus = FabricationLotStatus.Quarantined }, checks));
        Assert.Null(_service.ReconstructCertificate(releasedLot with { ReleasedAtUtc = null }, checks));
        Assert.Null(_service.ReconstructCertificate(releasedLot with { CertificatePayloadSha256 = null }, checks));
        Assert.Null(_service.ReconstructCertificate(releasedLot with { CertificateSignatureBase64 = null }, checks));
        Assert.Null(_service.ReconstructCertificate(releasedLot with { CertificateSignerKeyId = null }, checks));
    }

    /// <summary>
    /// Verifies fail-closed quarantine when required gates are missing or fail, and 4-gate release when CpG is optional.
    /// </summary>
    [Fact]
    public void EvaluateAndSignLotRelease_MissingOrFailedGatesQuarantine_AndOptionalCpGAllowsFourGateRelease()
    {
        // WHAT: Tests missing-gate quarantine across all 5 gates, failed-gate quarantine, and 4-gate release when CpG is not required.
        // WHY:  Guarantees a certificate is never issued if any mandatory wet-lab assay is missing or out-of-spec.
        // HOW:  Evaluates an empty check list (5 missing), a failed endotoxin list, and a 4-gate lot with CpGMethylationRequired = false.
        FabricationLotRecord lotWithCpG = CreateSampleLot(cpgRequired: true);
        FabricationLotReleaseEvaluation allMissing = _service.EvaluateAndSignLotRelease(lotWithCpG, []);
        Assert.Equal(FabricationLotStatus.Quarantined, allMissing.ResultStatus);
        Assert.False(allMissing.AllRequiredGatesPassed);
        Assert.Null(allMissing.Certificate);
        Assert.Equal(5, allMissing.GateEvaluations.Count);
        Assert.Equal(5, allMissing.FailureReasons.Count);
        Assert.All(allMissing.GateEvaluations, e => Assert.False(e.IsPresent));

        DateTimeOffset t0 = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        List<FabricationQcCheckRecord> fourPassingChecks =
        [
            new() { CheckId = "qc_1", LotId = lotWithCpG.LotId, PatientId = lotWithCpG.PatientId, GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 90.0, RecordedAtUtc = t0 },
            new() { CheckId = "qc_2", LotId = lotWithCpG.LotId, PatientId = lotWithCpG.PatientId, GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.25, RecordedAtUtc = t0 },
            new() { CheckId = "qc_3", LotId = lotWithCpG.LotId, PatientId = lotWithCpG.PatientId, GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 55.0, SecondaryValue = 0.12, RecordedAtUtc = t0 },
            new() { CheckId = "qc_4", LotId = lotWithCpG.LotId, PatientId = lotWithCpG.PatientId, GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 86.0, SecondaryValue = 0.5, RecordedAtUtc = t0 }
        ];

        // With CpG required, missing CpG must quarantine
        FabricationLotReleaseEvaluation missingCpG = _service.EvaluateAndSignLotRelease(lotWithCpG, fourPassingChecks);
        Assert.Equal(FabricationLotStatus.Quarantined, missingCpG.ResultStatus);
        Assert.Null(missingCpG.Certificate);
        Assert.Single(missingCpG.FailureReasons);

        // With CpG optional, 4 passing gates release cleanly (also testing CalibrationSnapshotId == null path)
        FabricationLotRecord lotNoCpG = CreateSampleLot(cpgRequired: false) with { CalibrationSnapshotId = null };
        FabricationLotReleaseEvaluation releasedFour = _service.EvaluateAndSignLotRelease(lotNoCpG, fourPassingChecks);
        Assert.Equal(FabricationLotStatus.Released, releasedFour.ResultStatus);
        Assert.True(releasedFour.AllRequiredGatesPassed);
        Assert.Equal(4, releasedFour.GateEvaluations.Count);
        Assert.NotNull(releasedFour.Certificate);
        Assert.True(_service.VerifyLotCertificate(releasedFour.Certificate));

        // Failing endotoxin quarantines even when all 4 gates are present
        List<FabricationQcCheckRecord> failingEndo =
        [
            fourPassingChecks[0],
            fourPassingChecks[1] with { MeasuredValue = 1.25 },
            fourPassingChecks[2],
            fourPassingChecks[3]
        ];
        FabricationLotReleaseEvaluation quarantinedEndo = _service.EvaluateAndSignLotRelease(lotNoCpG, failingEndo);
        Assert.Equal(FabricationLotStatus.Quarantined, quarantinedEndo.ResultStatus);
        Assert.Null(quarantinedEndo.Certificate);
        Assert.Single(quarantinedEndo.FailureReasons);

        // Deterministic Opentrons SHA-256 helper
        string hash1 = _service.ComputeOpentronsProtocolSha256("depot_1", "patient_1", TherapeuticRegimen.FemaleToMale, "PLGA-PEG-PLGA", 21.5, 0.52);
        string hash2 = _service.ComputeOpentronsProtocolSha256("depot_1", "patient_1", TherapeuticRegimen.FemaleToMale, "PLGA-PEG-PLGA", 21.5, 0.52);
        string hash3 = _service.ComputeOpentronsProtocolSha256("depot_1", "patient_1", TherapeuticRegimen.FemaleToMale, "PLGA-PEG-PLGA", 22.0, 0.52);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }

    /// <summary>
    /// Creates a representative fabrication lot record for service test cases.
    /// </summary>
    /// <param name="cpgRequired">Whether CpG methylation verification is mandatory.</param>
    /// <returns>A populated <see cref="FabricationLotRecord"/>.</returns>
    private FabricationLotRecord CreateSampleLot(bool cpgRequired) => new()
    {
        LotId = "lot_unit_001",
        DepotId = "depot_unit_001",
        CalibrationSnapshotId = "calib_unit_001",
        PatientId = "patient_unit_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        PolymerMatrixType = "PLGA-PEG-PLGA",
        TargetMassMg = 19.4,
        TargetErosionRatePercent = 0.58,
        CpGMethylationRequired = cpgRequired,
        OpentronsProtocolSha256 = _service.ComputeOpentronsProtocolSha256(
            "depot_unit_001",
            "patient_unit_001",
            TherapeuticRegimen.MaleToFemale,
            "PLGA-PEG-PLGA",
            19.4,
            0.58),
        LotStatus = FabricationLotStatus.QcPending,
        CreatedAtUtc = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero)
    };
}
