using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload for minting a new patient-specific wet-lab nanocarrier fabrication lot
/// linked to an active semi-annual depot formulation.
/// </summary>
[SwaggerSchema(Title = "CreateFabricationLotRequest", Description = "Request payload for minting a patient-specific wet-lab fabrication lot from a depot formulation.")]
public sealed record CreateFabricationLotRequest : IExamplesProvider<CreateFabricationLotRequest>
{
    /// <summary>
    /// Gets or initializes the parent semi-annual depot formulation identifier.
    /// </summary>
    [Description("Parent semi-annual depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether the lot should inherit adaptive mass and erosion targets
    /// from the latest Milestone 34 longitudinal calibration snapshot when available.
    /// </summary>
    [Description("When true, automatically inherits recommended next-cycle mass and erosion rate from the latest calibration snapshot.")]
    [JsonPropertyName("useLatestCalibration")]
    public bool UseLatestCalibration { get; init; } = true;

    /// <summary>
    /// Gets or initializes a value indicating whether M.SssI enzymatic CpG methylation verification (&gt;= 95%) is mandatory.
    /// </summary>
    [Description("When true, requires passing M.SssI CpG enzymatic methylation (>= 95.0%) prior to cryptographic release.")]
    [JsonPropertyName("cpgMethylationRequired")]
    public bool CpGMethylationRequired { get; init; } = true;

    /// <summary>
    /// Gets or initializes an optional SHA-256 hex digest of a pre-generated Opentrons liquid-handler script.
    /// When omitted, the node computes a deterministic digest from the batch formulation parameters.
    /// </summary>
    [Description("Optional SHA-256 hex digest of the Opentrons synthesis script; computed deterministically when omitted.")]
    [JsonPropertyName("opentronsProtocolSha256")]
    public string? OpentronsProtocolSha256 { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreateFabricationLotRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated fabrication lot creation request.</returns>
    public CreateFabricationLotRequest GetExamples() => new()
    {
        DepotId = "depot_sample_001",
        UseLatestCalibration = true,
        CpGMethylationRequired = true,
        OpentronsProtocolSha256 = null
    };
}

/// <summary>
/// Request payload for recording a single pharmaceutical wet-lab quality control measurement against a fabrication lot.
/// </summary>
[SwaggerSchema(Title = "RecordFabricationQcRequest", Description = "Request payload for recording a wet-lab pharmaceutical QC measurement against a fabrication lot.")]
public sealed record RecordFabricationQcRequest : IExamplesProvider<RecordFabricationQcRequest>
{
    /// <summary>
    /// Gets or initializes the wet-lab QC gate type measured.
    /// </summary>
    [Description("Wet-lab pharmaceutical QC gate measured (GelFoldingYield, EndotoxinSterility, HydrodynamicSizePdi, PayloadEncapsulation, or CpGMethylation).")]
    [JsonPropertyName("gateType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationQcGateType GateType { get; init; } = FabricationQcGateType.GelFoldingYield;

    /// <summary>
    /// Gets or initializes the primary measured value in standard laboratory units.
    /// </summary>
    [Description("Primary measured assay value in standard wet-lab units.")]
    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; init; }

    /// <summary>
    /// Gets or initializes an optional secondary measurement (DLS PDI or gravimetric mass deviation %).
    /// </summary>
    [Description("Optional secondary assay value (DLS polydispersity index or gravimetric mass deviation %).")]
    [JsonPropertyName("secondaryValue")]
    public double? SecondaryValue { get; init; }

    /// <summary>
    /// Gets or initializes optional plaintext technician notes encrypted at rest using AES-256-GCM.
    /// </summary>
    [Description("Optional wet-lab technician notes (encrypted at rest via AES-256-GCM).")]
    [JsonPropertyName("technicianNotes")]
    public string? TechnicianNotes { get; init; }

    /// <summary>
    /// Gets or initializes an optional UTC timestamp when the benchtop assay completed.
    /// </summary>
    [Description("Optional UTC timestamp when the wet-lab QC measurement completed; defaults to current UTC time.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset? RecordedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RecordFabricationQcRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated wet-lab QC recording request.</returns>
    public RecordFabricationQcRequest GetExamples() => new()
    {
        GateType = FabricationQcGateType.GelFoldingYield,
        MeasuredValue = 92.4,
        SecondaryValue = null,
        TechnicianNotes = "2% agarose gel stained with SYBR Safe; single sharp monomeric band.",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T13:30:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Response payload representing a recorded pharmaceutical wet-lab QC check measurement.
/// </summary>
[SwaggerSchema(Title = "FabricationQcCheckResponse", Description = "Response payload for a recorded wet-lab pharmaceutical QC measurement.")]
public sealed record FabricationQcCheckResponse : IExamplesProvider<FabricationQcCheckResponse>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this QC check measurement.
    /// </summary>
    [Description("Unique identifier of this wet-lab QC check.")]
    [JsonPropertyName("checkId")]
    public string CheckId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent fabrication lot identifier.
    /// </summary>
    [Description("Parent fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this lot.
    /// </summary>
    [Description("Patient identifier associated with this lot.")]
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
    /// Gets or initializes the primary numerical assay value.
    /// </summary>
    [Description("Primary numerical assay value.")]
    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; init; }

    /// <summary>
    /// Gets or initializes an optional secondary numerical assay value.
    /// </summary>
    [Description("Optional secondary numerical assay value (DLS PDI or gravimetric mass deviation %).")]
    [JsonPropertyName("secondaryValue")]
    public double? SecondaryValue { get; init; }

    /// <summary>
    /// Gets or initializes the normative numeric threshold applied to this gate.
    /// </summary>
    [Description("Normative numeric acceptance threshold applied to this gate.")]
    [JsonPropertyName("thresholdValue")]
    public double ThresholdValue { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the check passed its gate acceptance threshold.
    /// </summary>
    [Description("True when the measurement passed its pharmaceutical acceptance criterion.")]
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the QC check was recorded.
    /// </summary>
    [Description("UTC timestamp when the QC check was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes optional decrypted technician notes for authorized callers.
    /// </summary>
    [Description("Optional decrypted wet-lab technician notes.")]
    [JsonPropertyName("technicianNotes")]
    public string? TechnicianNotes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationQcCheckResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated QC check response.</returns>
    public FabricationQcCheckResponse GetExamples() => new()
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
        TechnicianNotes = "2% agarose gel stained with SYBR Safe; single sharp monomeric band."
    };
}

/// <summary>
/// Response payload representing a wet-lab fabrication lot together with its recorded QC check history.
/// </summary>
[SwaggerSchema(Title = "FabricationLotResponse", Description = "Response payload for a patient-specific wet-lab fabrication lot and its QC measurements.")]
public sealed record FabricationLotResponse : IExamplesProvider<FabricationLotResponse>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the fabrication lot.
    /// </summary>
    [Description("Unique identifier of the wet-lab fabrication lot.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent semi-annual depot formulation identifier.
    /// </summary>
    [Description("Parent semi-annual depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional Milestone 34 longitudinal calibration snapshot identifier used to size this lot.
    /// </summary>
    [Description("Optional longitudinal calibration snapshot identifier used to size this batch.")]
    [JsonPropertyName("calibrationSnapshotId")]
    public string? CalibrationSnapshotId { get; init; }

    /// <summary>
    /// Gets or initializes the patient identifier associated with this lot.
    /// </summary>
    [Description("Patient identifier associated with this fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic hormone replacement regimen.
    /// </summary>
    [Description("Therapeutic hormone replacement regimen.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the polymer matrix type encapsulating the nanocarriers.
    /// </summary>
    [Description("Polymer matrix composition encapsulating the nanocarriers.")]
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
    /// Gets or initializes a value indicating whether CpG enzymatic methylation verification is mandatory.
    /// </summary>
    [Description("True when CpG methylation verification (>= 95.0%) is required before release.")]
    [JsonPropertyName("cpgMethylationRequired")]
    public bool CpGMethylationRequired { get; init; } = true;

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the Opentrons liquid-handler synthesis protocol.
    /// </summary>
    [Description("SHA-256 hex digest of the Opentrons liquid-handler protocol executed for this lot.")]
    [JsonPropertyName("opentronsProtocolSha256")]
    public string OpentronsProtocolSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current lifecycle status of the lot.
    /// </summary>
    [Description("Current lifecycle status of the fabrication lot.")]
    [JsonPropertyName("lotStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus LotStatus { get; init; } = FabricationLotStatus.Draft;

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the signed Certificate of Analysis payload once released.
    /// </summary>
    [Description("SHA-256 hex digest of the signed Certificate of Analysis payload once released.")]
    [JsonPropertyName("certificatePayloadSha256")]
    public string? CertificatePayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 digital signature once released.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 digital signature once released.")]
    [JsonPropertyName("certificateSignatureBase64")]
    public string? CertificateSignatureBase64 { get; init; }

    /// <summary>
    /// Gets or initializes the signing key identifier of the fabricating node once released.
    /// </summary>
    [Description("Key identifier of the fabricating node's ML-DSA-65 signing key.")]
    [JsonPropertyName("certificateSignerKeyId")]
    public string? CertificateSignerKeyId { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the lot was created.
    /// </summary>
    [Description("UTC timestamp when the fabrication lot was created.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the lot was cryptographically released, or <see langword="null"/>.
    /// </summary>
    [Description("UTC timestamp when the lot was cryptographically released, or null.")]
    [JsonPropertyName("releasedAtUtc")]
    public DateTimeOffset? ReleasedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the chronological list of wet-lab QC checks recorded against this lot.
    /// </summary>
    [Description("Chronological list of wet-lab QC checks recorded against this lot.")]
    [JsonPropertyName("qcChecks")]
    public IReadOnlyList<FabricationQcCheckResponse> QcChecks { get; init; } = [];

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated fabrication lot response.</returns>
    public FabricationLotResponse GetExamples() => new()
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
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-19T13:00:00Z", CultureInfo.InvariantCulture),
        QcChecks = [new FabricationQcCheckResponse().GetExamples()]
    };
}

/// <summary>
/// Response payload returned after executing 5-gate wet-lab release verification and ML-DSA-65 signing on a lot.
/// </summary>
[SwaggerSchema(Title = "FabricationLotReleaseResponse", Description = "Response payload containing the updated fabrication lot, 5-gate evaluation, and cryptographic verification verdict.")]
public sealed record FabricationLotReleaseResponse : IExamplesProvider<FabricationLotReleaseResponse>
{
    /// <summary>
    /// Gets or initializes the updated fabrication lot record with its new status and signature metadata.
    /// </summary>
    [Description("Updated fabrication lot record after release or quarantine evaluation.")]
    [JsonPropertyName("lot")]
    public FabricationLotResponse Lot { get; init; } = new();

    /// <summary>
    /// Gets or initializes the detailed 5-gate release evaluation and optional signed Certificate of Analysis.
    /// </summary>
    [Description("Detailed 5-gate pharmaceutical evaluation and optional ML-DSA-65 Certificate of Analysis.")]
    [JsonPropertyName("evaluation")]
    public FabricationLotReleaseEvaluation Evaluation { get; init; } = new();

    /// <summary>
    /// Gets or initializes a value indicating whether the issued Certificate of Analysis passed live ML-DSA-65 verification.
    /// </summary>
    [Description("True when the issued Certificate of Analysis passed live ML-DSA-65 signature verification.")]
    [JsonPropertyName("certificateVerified")]
    public bool CertificateVerified { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotReleaseResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated lot release response.</returns>
    public FabricationLotReleaseResponse GetExamples() => new()
    {
        Lot = new FabricationLotResponse().GetExamples() with { LotStatus = FabricationLotStatus.Released },
        Evaluation = new FabricationLotReleaseEvaluation().GetExamples(),
        CertificateVerified = true
    };
}

/// <summary>
/// Response payload containing a released fabrication lot's reconstructed Certificate of Analysis
/// alongside its live NIST FIPS 204 ML-DSA-65 verification status.
/// </summary>
[SwaggerSchema(Title = "FabricationLotCertificateResponse", Description = "Signed ML-DSA-65 Certificate of Analysis with live cryptographic verification status.")]
public sealed record FabricationLotCertificateResponse : IExamplesProvider<FabricationLotCertificateResponse>
{
    /// <summary>
    /// Gets or initializes the signed Certificate of Analysis for the released fabrication lot.
    /// </summary>
    [Description("Signed NIST FIPS 204 ML-DSA-65 Certificate of Analysis.")]
    [JsonPropertyName("certificate")]
    public FabricationLotCertificate Certificate { get; init; } = new();

    /// <summary>
    /// Gets or initializes a value indicating whether the certificate passed live SHA-256 and ML-DSA-65 verification.
    /// </summary>
    [Description("True when canonical SHA-256 hash and ML-DSA-65 signature both verify cleanly.")]
    [JsonPropertyName("signatureVerified")]
    public bool SignatureVerified { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotCertificateResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated verified certificate response.</returns>
    public FabricationLotCertificateResponse GetExamples() => new()
    {
        Certificate = new FabricationLotCertificate().GetExamples(),
        SignatureVerified = true
    };
}

/// <summary>
/// Pure static mapping helpers bridging persisted fabrication lot records, AES-256-GCM technician note decryption,
/// and REST API DTOs.
/// </summary>
public static class FabricationLotMapper
{
    /// <summary>
    /// Maps a persisted <see cref="FabricationQcCheckRecord"/> into an API <see cref="FabricationQcCheckResponse"/>,
    /// decrypting any stored technician notes via <see cref="ProfileDataProtector"/>.
    /// </summary>
    /// <param name="record">Persisted wet-lab QC check record.</param>
    /// <param name="protector">Node PHI data protector.</param>
    /// <returns>A populated <see cref="FabricationQcCheckResponse"/>.</returns>
    public static FabricationQcCheckResponse ToQcResponse(
        FabricationQcCheckRecord record,
        ProfileDataProtector protector)
    {
        // WHAT: Converts a database QC check row to its API DTO and decrypts optional technician notes.
        // WHY:  Ensures notes remain AES-256-GCM encrypted at rest while surfacing cleanly to authorized viewers.
        // HOW:  Decrypts NotesEncrypted when non-blank via ProfileDataProtector.DecryptDataUri.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(protector);

        string? decryptedNotes = string.IsNullOrWhiteSpace(record.NotesEncrypted)
            ? null
            : protector.DecryptDataUri(record.NotesEncrypted);

        return new FabricationQcCheckResponse
        {
            CheckId = record.CheckId,
            LotId = record.LotId,
            PatientId = record.PatientId,
            GateType = record.GateType,
            MeasuredValue = record.MeasuredValue,
            SecondaryValue = record.SecondaryValue,
            ThresholdValue = record.ThresholdValue,
            Passed = record.Passed,
            RecordedAtUtc = record.RecordedAtUtc,
            TechnicianNotes = decryptedNotes
        };
    }

    /// <summary>
    /// Maps a persisted <see cref="FabricationLotRecord"/> and its QC checks into a <see cref="FabricationLotResponse"/>.
    /// </summary>
    /// <param name="lot">Persisted fabrication lot record.</param>
    /// <param name="checks">Chronological wet-lab QC checks associated with the lot.</param>
    /// <param name="protector">Node PHI data protector for technician note decryption.</param>
    /// <returns>A populated <see cref="FabricationLotResponse"/>.</returns>
    public static FabricationLotResponse ToLotResponse(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks,
        ProfileDataProtector protector)
    {
        // WHAT: Projects a fabrication lot and its recorded QC checks into a REST response payload without LINQ.
        // WHY:  Centralizes DTO mapping and maintains zero-LINQ performance discipline across endpoint handlers.
        // HOW:  Iterates checks with a pre-sized list and invokes ToQcResponse for each entry.
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(protector);

        List<FabricationQcCheckResponse> qcResponses = new(checks.Count);
        for (int i = 0; i < checks.Count; i++)
        {
            qcResponses.Add(ToQcResponse(checks[i], protector));
        }

        return new FabricationLotResponse
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
            LotStatus = lot.LotStatus,
            CertificatePayloadSha256 = lot.CertificatePayloadSha256,
            CertificateSignatureBase64 = lot.CertificateSignatureBase64,
            CertificateSignerKeyId = lot.CertificateSignerKeyId,
            CreatedAtUtc = lot.CreatedAtUtc,
            ReleasedAtUtc = lot.ReleasedAtUtc,
            QcChecks = qcResponses
        };
    }
}

/// <summary>
/// Request payload for permanently revoking a wet-lab fabrication lot and its Certificate of Analysis.
/// </summary>
[SwaggerSchema(Title = "RevokeFabricationLotRequest", Description = "Request payload for administratively revoking a wet-lab fabrication lot.")]
public sealed record RevokeFabricationLotRequest : IExamplesProvider<RevokeFabricationLotRequest>
{
    /// <summary>
    /// Gets or initializes the plain-English clinical or quality-control reason for revoking this lot.
    /// </summary>
    [Description("Plain-English clinical or quality-control reason for permanent lot revocation.")]
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="RevokeFabricationLotRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated lot revocation request.</returns>
    public RevokeFabricationLotRequest GetExamples() => new()
    {
        Reason = "Cold-chain excursion detected during courier transit (temperature exceeded 8°C)."
    };
}

/// <summary>
/// Response payload returned after permanently revoking a wet-lab fabrication lot.
/// </summary>
[SwaggerSchema(Title = "FabricationLotRevocationResponse", Description = "Response payload containing the revoked fabrication lot and permanent revocation record.")]
public sealed record FabricationLotRevocationResponse : IExamplesProvider<FabricationLotRevocationResponse>
{
    /// <summary>
    /// Gets or initializes the updated fabrication lot record transitioned to <see cref="FabricationLotStatus.Revoked"/>.
    /// </summary>
    [Description("Updated fabrication lot record transitioned to Revoked status.")]
    [JsonPropertyName("lot")]
    public FabricationLotResponse Lot { get; init; } = new();

    /// <summary>
    /// Gets or initializes the persisted revocation tombstone record.
    /// </summary>
    [Description("Persisted revocation tombstone containing administrator identity, reason, and timestamp.")]
    [JsonPropertyName("revocation")]
    public FabricationLotRevocationRecord Revocation { get; init; } = new();

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationLotRevocationResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated lot revocation response.</returns>
    public FabricationLotRevocationResponse GetExamples() => new()
    {
        Lot = new FabricationLotResponse().GetExamples() with { LotStatus = FabricationLotStatus.Revoked },
        Revocation = new FabricationLotRevocationRecord().GetExamples()
    };
}

/// <summary>
/// Single de-identified lot revocation entry included inside a portable ML-DSA-65 signed peer revocation feed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The non-PHI subset of a permanent lot revocation record safe for federated sharing
/// across peer NanoWeaver laboratories (omits patient identifiers while preserving lot ID, payload hash, actor, reason, and timestamp).
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SignedLotRevocationEntry", Description = "De-identified permanent lot recall entry carried inside a signed ML-DSA-65 peer revocation feed.")]
public sealed record SignedLotRevocationEntry : IExamplesProvider<SignedLotRevocationEntry>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the revoked fabrication lot.
    /// </summary>
    [Description("Unique identifier of the revoked wet-lab fabrication lot.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional SHA-256 hex digest of the revoked Certificate of Analysis payload.
    /// </summary>
    [Description("Optional SHA-256 hex digest of the revoked Certificate of Analysis payload.")]
    [JsonPropertyName("payloadSha256")]
    public string? PayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the revoking Administrator username on the issuing laboratory node.
    /// </summary>
    [Description("Administrator username who issued the recall on the fabricating node.")]
    [JsonPropertyName("revokedBy")]
    public string RevokedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the plain-English clinical or cold-chain reason for permanent lot revocation.
    /// </summary>
    [Description("Plain-English clinical or quality-control reason for permanent lot revocation.")]
    [JsonPropertyName("revocationReason")]
    public string RevocationReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the lot was permanently revoked.
    /// </summary>
    [Description("UTC timestamp when the lot was permanently revoked.")]
    [JsonPropertyName("revokedAtUtc")]
    public DateTimeOffset RevokedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="SignedLotRevocationEntry"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated signed revocation entry.</returns>
    public SignedLotRevocationEntry GetExamples() => new()
    {
        LotId = "lot_sample_001",
        PayloadSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
        RevokedBy = "admin",
        RevocationReason = "Cold-chain excursion detected during courier transit (temperature exceeded 8°C).",
        RevokedAtUtc = DateTimeOffset.Parse("2026-11-19T16:00:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Portable, NIST FIPS 204 ML-DSA-65 signed Certificate Revocation Feed exported by a sovereign NanoWeaver node
/// for cross-node clinical recall synchronization.
/// </summary>
[SwaggerSchema(Title = "SignedLotRevocationFeed", Description = "Portable NIST FIPS 204 ML-DSA-65 signed lot revocation feed for cross-node peer recall synchronization.")]
public sealed record SignedLotRevocationFeed : IExamplesProvider<SignedLotRevocationFeed>
{
    /// <summary>
    /// Gets or initializes the schema version identifier for canonical revocation feed hashing.
    /// </summary>
    [Description("Canonical feed format version identifier.")]
    [JsonPropertyName("feedVersion")]
    public string FeedVersion { get; init; } = "nanoweaver-revocation-feed-v1";

    /// <summary>
    /// Gets or initializes the UTC timestamp when this revocation feed snapshot was generated and signed.
    /// </summary>
    [Description("UTC timestamp when this signed revocation feed was generated.")]
    [JsonPropertyName("issuedAtUtc")]
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes the deterministically ordered list of de-identified lot recall entries.
    /// </summary>
    [Description("Deterministically ordered list of de-identified permanent lot recall entries.")]
    [JsonPropertyName("entries")]
    public IReadOnlyList<SignedLotRevocationEntry> Entries { get; init; } = [];

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the canonical UTF-8 JSON feed payload.
    /// </summary>
    [Description("SHA-256 hex digest of the canonical UTF-8 JSON revocation feed payload.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 signature over the canonical feed payload.
    /// </summary>
    [Description("Base64-encoded NIST FIPS 204 ML-DSA-65 signature over the canonical feed payload.")]
    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the signing key identifier of the exporting laboratory node.
    /// </summary>
    [Description("ML-DSA-65 signing key identifier of the exporting laboratory node.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded ML-DSA-65 public key of the exporting laboratory node.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 public key of the exporting laboratory node.")]
    [JsonPropertyName("signerPublicKeyBase64")]
    public string SignerPublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the post-quantum digital signature algorithm name (<c>ML-DSA-65</c>).
    /// </summary>
    [Description("Post-quantum digital signature algorithm name (ML-DSA-65).")]
    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; init; } = "ML-DSA-65";

    /// <summary>
    /// Generates a representative example instance of <see cref="SignedLotRevocationFeed"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated signed revocation feed.</returns>
    public SignedLotRevocationFeed GetExamples() => new()
    {
        FeedVersion = "nanoweaver-revocation-feed-v1",
        IssuedAtUtc = DateTimeOffset.Parse("2026-11-19T16:15:00Z", CultureInfo.InvariantCulture),
        Entries = [new SignedLotRevocationEntry().GetExamples()],
        PayloadSha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        SignatureBase64 = "TUwtRFNBLTY1LVNJRw==",
        SignerKeyId = "node-mldsa65-primary",
        SignerPublicKeyBase64 = "UExBQ0VIT0xERVI=",
        SignatureAlgorithm = "ML-DSA-65"
    };
}

/// <summary>
/// Verification verdict produced when evaluating an imported <see cref="SignedLotRevocationFeed"/>
/// against mathematical ML-DSA-65 integrity and local/peer trust anchors.
/// </summary>
public sealed record RevocationFeedVerificationResult
{
    /// <summary>
    /// Gets or initializes a value indicating whether the feed canonical SHA-256 digest and ML-DSA-65 signature verified cleanly.
    /// </summary>
    public bool SignatureValid { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the feed is trusted for ingestion (valid signature and active local or peer key).
    /// </summary>
    public bool IsTrusted { get; init; }

    /// <summary>
    /// Gets or initializes the resolved trust anchor classification.
    /// </summary>
    public FabricationCertificateTrustSource TrustSource { get; init; } = FabricationCertificateTrustSource.InvalidSignature;

    /// <summary>
    /// Gets or initializes the human-readable peer laboratory label when matched in <c>weaver_peer_keys</c>.
    /// </summary>
    public string? TrustedPeerLabel { get; init; }

    /// <summary>
    /// Gets or initializes a plain-English explanation of the verification verdict.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Response payload returned after verifying and importing a peer node's <see cref="SignedLotRevocationFeed"/>.
/// </summary>
[SwaggerSchema(Title = "ImportRevocationFeedResponse", Description = "Response payload summarizing the verified import of a signed ML-DSA-65 peer revocation feed.")]
public sealed record ImportRevocationFeedResponse : IExamplesProvider<ImportRevocationFeedResponse>
{
    /// <summary>
    /// Gets or initializes the number of verified recall tombstones persisted into <c>weaver_federated_lot_revocations</c>.
    /// </summary>
    [Description("Number of verified recall tombstones imported and persisted.")]
    [JsonPropertyName("importedCount")]
    public int ImportedCount { get; init; }

    /// <summary>
    /// Gets or initializes the resolved cryptographic trust source classification of the imported feed.
    /// </summary>
    [Description("Resolved trust anchor classification (LocalNode or TrustedPeer).")]
    [JsonPropertyName("trustSource")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationCertificateTrustSource TrustSource { get; init; } = FabricationCertificateTrustSource.TrustedPeer;

    /// <summary>
    /// Gets or initializes the ML-DSA-65 signing key identifier of the peer laboratory.
    /// </summary>
    [Description("ML-DSA-65 signing key identifier of the peer laboratory.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the human-readable peer laboratory label if imported from a registered peer key.
    /// </summary>
    [Description("Human-readable label of the trusted peer laboratory, if applicable.")]
    [JsonPropertyName("trustedPeerLabel")]
    public string? TrustedPeerLabel { get; init; }

    /// <summary>
    /// Gets or initializes the canonical SHA-256 hex digest of the verified feed payload.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the verified revocation feed.")]
    [JsonPropertyName("feedPayloadSha256")]
    public string FeedPayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a plain-English summary of the import result.
    /// </summary>
    [Description("Plain-English summary of the verified revocation feed import.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the feed import completed.
    /// </summary>
    [Description("UTC timestamp when the verified feed import completed.")]
    [JsonPropertyName("importedAtUtc")]
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="ImportRevocationFeedResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated import revocation feed response.</returns>
    public ImportRevocationFeedResponse GetExamples() => new()
    {
        ImportedCount = 1,
        TrustSource = FabricationCertificateTrustSource.TrustedPeer,
        SignerKeyId = "node-mldsa65-peer-zurich",
        TrustedPeerLabel = "Zurich Sovereign Wet-Lab",
        FeedPayloadSha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        Summary = "Verified and imported 1 federated recall tombstone(s) from trusted peer laboratory 'Zurich Sovereign Wet-Lab'.",
        ImportedAtUtc = DateTimeOffset.Parse("2026-11-19T16:15:00Z", CultureInfo.InvariantCulture)
    };
}
