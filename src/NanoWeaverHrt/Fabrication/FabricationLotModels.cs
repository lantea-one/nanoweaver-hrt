using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Lifecycle status of a patient-specific wet-lab nanocarrier fabrication lot.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FabricationLotStatus
{
    /// <summary>
    /// Lot specification has been created from a depot formulation and awaits synthesis initiation.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// DNA origami annealing and polymer matrix encapsulation are actively underway on the liquid handler.
    /// </summary>
    InSynthesis = 1,

    /// <summary>
    /// Physical synthesis is complete and 5-gate pharmaceutical quality control assays are being recorded.
    /// </summary>
    QcPending = 2,

    /// <summary>
    /// All required wet-lab quality control gates passed and an ML-DSA-65 Certificate of Analysis was issued.
    /// </summary>
    Released = 3,

    /// <summary>
    /// One or more required quality control gates failed or were missing, blocking clinical release.
    /// </summary>
    Quarantined = 4,

    /// <summary>
    /// Previously issued lot has been administratively revoked and must not be implanted.
    /// </summary>
    Revoked = 5
}

/// <summary>
/// Pharmaceutical wet-lab quality control verification gate executed prior to cryptographic lot release.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FabricationQcGateType
{
    /// <summary>
    /// Agarose gel electrophoresis folding yield densitometry (target >= 85.0%).
    /// </summary>
    GelFoldingYield = 0,

    /// <summary>
    /// Limulus Amebocyte Lysate (LAL) bacterial endotoxin sterility assay per USP &lt;85&gt; (target &lt;= 0.50 EU/mL).
    /// </summary>
    EndotoxinSterility = 1,

    /// <summary>
    /// Dynamic Light Scattering (DLS) hydrodynamic diameter (15.0 to 120.0 nm) and polydispersity index (PDI &lt;= 0.20).
    /// </summary>
    HydrodynamicSizePdi = 2,

    /// <summary>
    /// HPLC payload encapsulation efficiency (&gt;= 80.0%) and gravimetric mass deviation (&lt;= 5.0%).
    /// </summary>
    PayloadEncapsulation = 3,

    /// <summary>
    /// M.SssI enzymatic CpG motif methylation verification (&gt;= 95.0%) preventing TLR9 innate immune signaling.
    /// </summary>
    CpGMethylation = 4
}

/// <summary>
/// Deterministic evaluation result for a single pharmaceutical wet-lab quality control gate.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A structured evaluation record capturing the measured primary/secondary assay values,
/// normative acceptance threshold, pass/fail verdict, and human-readable diagnostic summary for one QC gate.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Ensures transparent, auditable pharmaceutical release decisions that both
/// wet-lab scientists and automated cryptographic signers can verify deterministically.
/// </para>
/// <para>
/// <strong>How it works:</strong> Produced by <see cref="IFabricationLotService.EvaluateSingleGate"/> and embedded
/// directly inside <see cref="FabricationLotReleaseEvaluation"/> and <see cref="FabricationLotCertificate"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FabricationQcGateEvaluation", Description = "Deterministic evaluation of a single pharmaceutical wet-lab QC gate.")]
public sealed record FabricationQcGateEvaluation : IExamplesProvider<FabricationQcGateEvaluation>
{
    /// <summary>
    /// Gets or initializes the wet-lab quality control gate evaluated.
    /// </summary>
    [Description("Wet-lab quality control gate evaluated.")]
    [JsonPropertyName("gateType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationQcGateType GateType { get; init; } = FabricationQcGateType.GelFoldingYield;

    /// <summary>
    /// Gets or initializes the primary measured assay value (e.g. % yield, EU/mL endotoxin, nm diameter, or % methylation).
    /// </summary>
    [Description("Primary measured assay value in standard laboratory units.")]
    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; init; }

    /// <summary>
    /// Gets or initializes an optional secondary assay value (e.g. DLS polydispersity index or gravimetric % deviation).
    /// </summary>
    [Description("Optional secondary assay value (such as DLS PDI or gravimetric mass deviation %).")]
    [JsonPropertyName("secondaryValue")]
    public double? SecondaryValue { get; init; }

    /// <summary>
    /// Gets or initializes the numeric normative threshold value used for this gate.
    /// </summary>
    [Description("Normative numeric acceptance threshold value for this gate.")]
    [JsonPropertyName("thresholdValue")]
    public double ThresholdValue { get; init; }

    /// <summary>
    /// Gets or initializes a human-readable description of the acceptance criterion.
    /// </summary>
    [Description("Human-readable pharmaceutical acceptance criterion for this gate.")]
    [JsonPropertyName("thresholdDescription")]
    public string ThresholdDescription { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether a measurement for this gate was present on the lot.
    /// </summary>
    [Description("True when at least one wet-lab measurement was recorded for this gate; false when missing.")]
    [JsonPropertyName("isPresent")]
    public bool IsPresent { get; init; } = true;

    /// <summary>
    /// Gets or initializes a value indicating whether the gate met its pharmaceutical release criterion.
    /// </summary>
    [Description("True when the measurement meets all acceptance thresholds for this gate.")]
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    /// <summary>
    /// Gets or initializes a plain-English diagnostic summary explaining why the gate passed or failed.
    /// </summary>
    [Description("Plain-English diagnostic explanation of the gate verdict.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationQcGateEvaluation"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample passing agarose gel folding yield evaluation.</returns>
    public FabricationQcGateEvaluation GetExamples() => new()
    {
        GateType = FabricationQcGateType.GelFoldingYield,
        MeasuredValue = 92.4,
        SecondaryValue = null,
        ThresholdValue = 85.0,
        ThresholdDescription = ">= 85.0% monomeric origami folding yield",
        IsPresent = true,
        Passed = true,
        Summary = "Agarose gel folding yield 92.40% meets minimum threshold (>= 85.0%)."
    };
}

/// <summary>
/// Post-quantum NIST FIPS 204 ML-DSA-65 signed Certificate of Analysis (CoA) attesting that a physical
/// patient-specific nanocarrier batch satisfied all required pharmaceutical wet-lab quality control gates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> An immutable, cryptographically signed batch release artifact binding
/// a patient's depot formulation, adaptive calibration parameters, Opentrons liquid-handler protocol hash,
/// and 5-gate wet-lab QC results to the fabricating node's ML-DSA-65 signing key.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Guarantees zero-trust provenance and tamper evidence between digital
/// pharmacokinetic design and physical subcutaneous implantation without relying on centralized third-party authorities.
/// </para>
/// <para>
/// <strong>How it works:</strong> Serializes deterministic canonical JSON fields, hashes them with SHA-256,
/// signs the canonical UTF-8 payload bytes using <c>IKeyManagementService.SignWithNodeSigningKey</c>, and exposes
/// the signer public key for offline verification via <c>IPostQuantumCryptoProvider.VerifySignature</c>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FabricationLotCertificate", Description = "NIST FIPS 204 ML-DSA-65 signed Certificate of Analysis for a released wet-lab fabrication lot.")]
public sealed record FabricationLotCertificate : IExamplesProvider<FabricationLotCertificate>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the certified fabrication lot.
    /// </summary>
    [Description("Unique identifier of the certified wet-lab fabrication lot.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent semi-annual depot formulation identifier.
    /// </summary>
    [Description("Parent semi-annual depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional Milestone 34 longitudinal calibration snapshot identifier used to size this batch.
    /// </summary>
    [Description("Optional longitudinal calibration snapshot identifier used to adaptively size this batch.")]
    [JsonPropertyName("calibrationSnapshotId")]
    public string? CalibrationSnapshotId { get; init; }

    /// <summary>
    /// Gets or initializes the patient identifier for whom this batch was fabricated.
    /// </summary>
    [Description("Patient identifier associated with this fabricated lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the therapeutic hormone replacement regimen of this lot.
    /// </summary>
    [Description("Therapeutic hormone replacement regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the biocompatible polymer matrix type encapsulating the nanocarriers.
    /// </summary>
    [Description("Biocompatible polymer matrix composition encapsulating the nanocarriers.")]
    [JsonPropertyName("polymerMatrixType")]
    public string PolymerMatrixType { get; init; } = "PLGA-PEG-PLGA";

    /// <summary>
    /// Gets or initializes the target loaded nanocarrier payload mass in milligrams.
    /// </summary>
    [Description("Target loaded nanocarrier payload mass in milligrams (mg).")]
    [JsonPropertyName("targetMassMg")]
    public double TargetMassMg { get; init; }

    /// <summary>
    /// Gets or initializes the target daily polymer matrix erosion rate in percent per day.
    /// </summary>
    [Description("Target daily polymer matrix erosion rate in percent per day (%/day).")]
    [JsonPropertyName("targetErosionRatePercent")]
    public double TargetErosionRatePercent { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether CpG enzymatic methylation verification was mandatory for this lot.
    /// </summary>
    [Description("True if M.SssI CpG methylation verification was required to suppress TLR9 innate immune signaling.")]
    [JsonPropertyName("cpgMethylationRequired")]
    public bool CpGMethylationRequired { get; init; }

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the Opentrons Protocol API v2 synthesis script.
    /// </summary>
    [Description("SHA-256 hex digest of the Opentrons liquid-handler protocol executed for this lot.")]
    [JsonPropertyName("opentronsProtocolSha256")]
    public string OpentronsProtocolSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the ordered list of evaluated wet-lab QC gates covered by this certificate.
    /// </summary>
    [Description("Evaluated pharmaceutical wet-lab QC gates certified by this Certificate of Analysis.")]
    [JsonPropertyName("gateEvaluations")]
    public IReadOnlyList<FabricationQcGateEvaluation> GateEvaluations { get; init; } = [];

    /// <summary>
    /// Gets or initializes the UTC timestamp when this Certificate of Analysis was cryptographically issued.
    /// </summary>
    [Description("UTC timestamp when the Certificate of Analysis was signed.")]
    [JsonPropertyName("issuedAtUtc")]
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the canonical certificate payload.
    /// </summary>
    [Description("SHA-256 hex digest of the canonical signed certificate payload.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 digital signature.
    /// </summary>
    [Description("Base64-encoded NIST FIPS 204 ML-DSA-65 digital signature over the canonical payload.")]
    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the key identifier of the fabricating node's signing key.
    /// </summary>
    [Description("Key identifier of the fabricating node's ML-DSA-65 signing key.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded public half of the signing key for independent verification.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 public key enabling offline certificate verification.")]
    [JsonPropertyName("signerPublicKeyBase64")]
    public string SignerPublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the post-quantum digital signature algorithm name.
    /// </summary>
    [Description("Post-quantum cryptographic signature algorithm identifier (ML-DSA-65).")]
    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; init; } = "ML-DSA-65";

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotCertificate"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample signed Certificate of Analysis.</returns>
    public FabricationLotCertificate GetExamples() => new()
    {
        LotId = "lot_sample_001",
        DepotId = "depot_sample_001",
        CalibrationSnapshotId = "calib_sample_001",
        PatientId = "patient_alice",
        Regimen = TherapeuticRegimen.MaleToFemale,
        PolymerMatrixType = "PLGA-PEG-PLGA",
        TargetMassMg = 19.4,
        TargetErosionRatePercent = 0.58,
        CpGMethylationRequired = true,
        OpentronsProtocolSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        GateEvaluations = [new FabricationQcGateEvaluation().GetExamples()],
        IssuedAtUtc = DateTimeOffset.Parse("2026-11-19T14:00:00Z", CultureInfo.InvariantCulture),
        PayloadSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
        SignatureBase64 = "BASE64_ML_DSA_65_SIGNATURE_SAMPLE",
        SignerKeyId = "node-mldsa65-key-01",
        SignerPublicKeyBase64 = "BASE64_ML_DSA_65_PUBLIC_KEY_SAMPLE",
        SignatureAlgorithm = "ML-DSA-65"
    };
}

/// <summary>
/// Complete result of executing 5-gate wet-lab quality control verification and cryptographic lot release signing.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The domain decision output returned by <see cref="IFabricationLotService.EvaluateAndSignLotRelease"/>
/// containing the resulting lot status (<see cref="FabricationLotStatus.Released"/> or <see cref="FabricationLotStatus.Quarantined"/>),
/// per-gate evaluations, failure reasons, and the signed certificate when all required gates pass.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Guarantees fail-closed clinical release behavior: a certificate is only minted
/// when every mandatory wet-lab gate is recorded and passes its threshold.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inspects all recorded QC checks for a lot, selects the latest reading per gate
/// without LINQ allocations, evaluates each required gate, and signs only on 100% pass.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FabricationLotReleaseEvaluation", Description = "Outcome of 5-gate pharmaceutical QC verification and cryptographic lot release signing.")]
public sealed record FabricationLotReleaseEvaluation : IExamplesProvider<FabricationLotReleaseEvaluation>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the evaluated fabrication lot.
    /// </summary>
    [Description("Unique identifier of the evaluated fabrication lot.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the resulting lifecycle status (<c>Released</c> or <c>Quarantined</c>).
    /// </summary>
    [Description("Resulting lifecycle status after 5-gate evaluation (Released or Quarantined).")]
    [JsonPropertyName("resultStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus ResultStatus { get; init; } = FabricationLotStatus.Quarantined;

    /// <summary>
    /// Gets or initializes a value indicating whether every required wet-lab QC gate passed.
    /// </summary>
    [Description("True when all required wet-lab QC gates are present and passed.")]
    [JsonPropertyName("allRequiredGatesPassed")]
    public bool AllRequiredGatesPassed { get; init; }

    /// <summary>
    /// Gets or initializes the deterministic evaluation list covering every required wet-lab gate.
    /// </summary>
    [Description("Ordered evaluation details for every required wet-lab QC gate.")]
    [JsonPropertyName("gateEvaluations")]
    public IReadOnlyList<FabricationQcGateEvaluation> GateEvaluations { get; init; } = [];

    /// <summary>
    /// Gets or initializes the list of plain-English reasons why the lot was quarantined, or empty when released.
    /// </summary>
    [Description("Plain-English failure reasons explaining quarantine; empty when released.")]
    [JsonPropertyName("failureReasons")]
    public IReadOnlyList<string> FailureReasons { get; init; } = [];

    /// <summary>
    /// Gets or initializes the signed ML-DSA-65 Certificate of Analysis when released; otherwise <see langword="null"/>.
    /// </summary>
    [Description("Signed ML-DSA-65 Certificate of Analysis if Released; null if Quarantined.")]
    [JsonPropertyName("certificate")]
    public FabricationLotCertificate? Certificate { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this release evaluation completed.
    /// </summary>
    [Description("UTC timestamp when the lot release evaluation completed.")]
    [JsonPropertyName("evaluatedAtUtc")]
    public DateTimeOffset EvaluatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotReleaseEvaluation"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample released lot evaluation containing a signed certificate.</returns>
    public FabricationLotReleaseEvaluation GetExamples()
    {
        FabricationLotCertificate cert = new FabricationLotCertificate().GetExamples();
        return new FabricationLotReleaseEvaluation
        {
            LotId = cert.LotId,
            ResultStatus = FabricationLotStatus.Released,
            AllRequiredGatesPassed = true,
            GateEvaluations = cert.GateEvaluations,
            FailureReasons = [],
            Certificate = cert,
            EvaluatedAtUtc = cert.IssuedAtUtc
        };
    }
}
