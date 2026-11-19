using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Persistent database record representing a patient-specific wet-lab nanocarrier fabrication lot
/// linked to an active semi-annual depot formulation and optional longitudinal calibration snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational storage record persisted in <c>weaver_fabrication_lots</c>
/// tracking target payload mass, polymer erosion parameters, Opentrons protocol digest, lifecycle status,
/// and ML-DSA-65 Certificate of Analysis signature metadata.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Creates a permanent, tamper-evident chain of custody bridging digital
/// endocrine calibration to physical wet-lab synthesis and pharmaceutical lot release across all 4 SQL dialects.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and queried via <see cref="NanoWeaverHrt.Storage.IFabricationLotRepository"/>
/// with foreign key linkage to <c>weaver_depot_formulations</c> and GDPR Article 17 erasure cascade support.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FabricationLotRecord", Description = "Persisted patient-specific wet-lab nanocarrier fabrication lot record.")]
public sealed record FabricationLotRecord : IExamplesProvider<FabricationLotRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this wet-lab fabrication lot.
    /// </summary>
    [Description("Unique identifier of this wet-lab fabrication lot.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent semi-annual depot formulation identifier.
    /// </summary>
    [Description("Parent semi-annual depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional Milestone 34 calibration snapshot identifier used to size this lot.
    /// </summary>
    [Description("Optional longitudinal calibration snapshot identifier used to size this lot.")]
    [JsonPropertyName("calibrationSnapshotId")]
    public string? CalibrationSnapshotId { get; init; }

    /// <summary>
    /// Gets or initializes the patient identifier associated with this fabrication lot.
    /// </summary>
    [Description("Patient identifier associated with this fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the therapeutic hormone replacement regimen for this lot.
    /// </summary>
    [Description("Therapeutic hormone replacement regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the polymer matrix composition encapsulating the DNA origami nanocarriers.
    /// </summary>
    [Description("Polymer matrix composition encapsulating the nanocarriers (e.g. PLGA-PEG-PLGA).")]
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
    /// Gets or initializes a value indicating whether M.SssI CpG enzymatic methylation verification is mandatory.
    /// </summary>
    [Description("True when CpG methylation verification (>= 95%) is required prior to clinical release.")]
    [JsonPropertyName("cpgMethylationRequired")]
    public bool CpGMethylationRequired { get; init; } = true;

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the Opentrons Protocol API v2 synthesis script.
    /// </summary>
    [Description("SHA-256 hex digest of the Opentrons liquid-handler protocol generated for this lot.")]
    [JsonPropertyName("opentronsProtocolSha256")]
    public string OpentronsProtocolSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current lifecycle status of this fabrication lot.
    /// </summary>
    [Description("Current lifecycle status of this fabrication lot.")]
    [JsonPropertyName("lotStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus LotStatus { get; init; } = FabricationLotStatus.Draft;

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the canonical Certificate of Analysis payload once released.
    /// </summary>
    [Description("SHA-256 hex digest of the signed Certificate of Analysis payload once released.")]
    [JsonPropertyName("certificatePayloadSha256")]
    public string? CertificatePayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 digital signature once released.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 digital signature of the Certificate of Analysis once released.")]
    [JsonPropertyName("certificateSignatureBase64")]
    public string? CertificateSignatureBase64 { get; init; }

    /// <summary>
    /// Gets or initializes the signing key identifier of the fabricating node once released.
    /// </summary>
    [Description("Key identifier of the node ML-DSA-65 signing key that signed the Certificate of Analysis.")]
    [JsonPropertyName("certificateSignerKeyId")]
    public string? CertificateSignerKeyId { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the fabrication lot was created.
    /// </summary>
    [Description("UTC timestamp when the fabrication lot was created.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the lot was cryptographically released, or <see langword="null"/> if not released.
    /// </summary>
    [Description("UTC timestamp when the lot was cryptographically released, or null if not released.")]
    [JsonPropertyName("releasedAtUtc")]
    public DateTimeOffset? ReleasedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated fabrication lot persistence record.</returns>
    public FabricationLotRecord GetExamples() => new()
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
        LotStatus = FabricationLotStatus.QcPending,
        CertificatePayloadSha256 = null,
        CertificateSignatureBase64 = null,
        CertificateSignerKeyId = null,
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-19T13:00:00Z", CultureInfo.InvariantCulture),
        ReleasedAtUtc = null
    };
}

/// <summary>
/// Persistent database record representing a single wet-lab pharmaceutical quality control measurement
/// recorded against a specific fabrication lot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational storage record stored in <c>weaver_fabrication_qc_checks</c>
/// capturing a wet-lab gate reading (gel folding yield, LAL endotoxin, DLS size/PDI, HPLC encapsulation, or CpG methylation),
/// its threshold, pass verdict, timestamp, and optional PHI-encrypted technician notes.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides an immutable, chronologically ordered laboratory log supporting
/// 21 CFR Part 11 traceability and deterministic Certificate of Analysis signing.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and queried via <see cref="NanoWeaverHrt.Storage.IFabricationLotRepository"/>
/// with optional technician notes encrypted at rest via <see cref="NanoWeaverHrt.Security.Authentication.ProfileDataProtector"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FabricationQcCheckRecord", Description = "Persisted pharmaceutical wet-lab QC measurement recorded against a fabrication lot.")]
public sealed record FabricationQcCheckRecord : IExamplesProvider<FabricationQcCheckRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this QC check measurement.
    /// </summary>
    [Description("Unique identifier of this wet-lab QC measurement.")]
    [JsonPropertyName("checkId")]
    public string CheckId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent fabrication lot identifier.
    /// </summary>
    [Description("Parent fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with the parent lot.
    /// </summary>
    [Description("Patient identifier associated with the parent fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the pharmaceutical wet-lab QC gate type measured.
    /// </summary>
    [Description("Pharmaceutical wet-lab QC gate type measured.")]
    [JsonPropertyName("gateType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationQcGateType GateType { get; init; } = FabricationQcGateType.GelFoldingYield;

    /// <summary>
    /// Gets or initializes the primary numerical measurement value in standard wet-lab units.
    /// </summary>
    [Description("Primary numerical measurement value in standard wet-lab units.")]
    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; init; }

    /// <summary>
    /// Gets or initializes an optional secondary numerical measurement (e.g. DLS PDI or gravimetric mass deviation %).
    /// </summary>
    [Description("Optional secondary numerical measurement (such as DLS PDI or gravimetric mass deviation %).")]
    [JsonPropertyName("secondaryValue")]
    public double? SecondaryValue { get; init; }

    /// <summary>
    /// Gets or initializes the normative numeric acceptance threshold applied during evaluation.
    /// </summary>
    [Description("Normative numeric acceptance threshold applied during evaluation.")]
    [JsonPropertyName("thresholdValue")]
    public double ThresholdValue { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether this measurement passed its gate criterion.
    /// </summary>
    [Description("True when the measurement passed its pharmaceutical acceptance threshold.")]
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this QC measurement was recorded.
    /// </summary>
    [Description("UTC timestamp when this QC measurement was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes optional PHI-encrypted wet-lab technician notes stored at rest.
    /// </summary>
    [Description("Optional PHI-encrypted wet-lab technician notes stored at rest.")]
    [JsonPropertyName("notesEncrypted")]
    public string? NotesEncrypted { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationQcCheckRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated wet-lab QC check record.</returns>
    public FabricationQcCheckRecord GetExamples() => new()
    {
        CheckId = "qc_sample_001",
        LotId = "lot_sample_001",
        PatientId = "patient_alice",
        GateType = FabricationQcGateType.GelFoldingYield,
        MeasuredValue = 92.4,
        SecondaryValue = null,
        ThresholdValue = 85.0,
        Passed = true,
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T13:30:00Z", CultureInfo.InvariantCulture),
        NotesEncrypted = null
    };
}
