using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Supported international regulatory electronic clinical trial submission frameworks.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegulatorySubmissionFramework
{
    /// <summary>
    /// United States Food and Drug Administration Investigational New Drug (21 CFR Part 312) electronic dossier.
    /// </summary>
    FdaInd = 0,

    /// <summary>
    /// European Medicines Agency Investigational Medicinal Product Dossier (EU CTR 536/2014).
    /// </summary>
    EmaImpd = 1
}

/// <summary>
/// Persistent database record storing an AES-256-GCM encrypted, NIST FIPS 204 ML-DSA-65 signed
/// FDA IND or EMA IMPD regulatory dossier snapshot in <c>weaver_regulatory_dossiers</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Durable archive entity capturing dossier identity, optional primary lot linkage,
/// canonical SHA-256 digest, ML-DSA-65 signature metadata, AES-256-GCM encrypted bundle JSON, and readiness metrics.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P235): Guarantees reproducible multi-center clinical trial audit
/// preservation across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB with GDPR Article 17 erasure support.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and queried via <see cref="NanoWeaverHrt.Storage.IRegulatoryDossierRepository"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "RegulatoryDossierRecord", Description = "Persisted encrypted FDA IND / EMA IMPD regulatory dossier snapshot record.")]
public sealed record RegulatoryDossierRecord : IExamplesProvider<RegulatoryDossierRecord>
{
    /// <summary>
    /// Gets or initializes the unique regulatory dossier archive identifier.
    /// </summary>
    [Description("Unique identifier of the compiled regulatory dossier snapshot.")]
    [JsonPropertyName("dossierId")]
    public string DossierId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning patient identifier for cohort indexing and GDPR erasure.
    /// </summary>
    [Description("Owning patient identifier associated with this dossier snapshot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional primary wet-lab fabrication lot identifier compiled into CMC evidence.
    /// </summary>
    [Description("Optional primary fabrication lot identifier evaluated in the dossier.")]
    [JsonPropertyName("lotId")]
    public string? LotId { get; init; }

    /// <summary>
    /// Gets or initializes the target regulatory submission framework (<see cref="RegulatorySubmissionFramework.FdaInd"/> or <see cref="RegulatorySubmissionFramework.EmaImpd"/>).
    /// </summary>
    [Description("Target regulatory submission framework (FdaInd or EmaImpd).")]
    [JsonPropertyName("submissionFramework")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatorySubmissionFramework SubmissionFramework { get; init; } = RegulatorySubmissionFramework.FdaInd;

    /// <summary>
    /// Gets or initializes the lowercase 64-character SHA-256 hex digest of the canonical dossier manifest.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the compiled dossier manifest.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 signature over the canonical manifest.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 digital signature sealing the dossier manifest.")]
    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the signing node ML-DSA-65 public key identifier.
    /// </summary>
    [Description("Node ML-DSA-65 public key identifier that sealed the dossier.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the AES-256-GCM encrypted serialized JSON bundle stored at rest.
    /// </summary>
    [Description("AES-256-GCM encrypted serialized JSON payload of the complete signed dossier bundle.")]
    [JsonPropertyName("bundleJsonEncrypted")]
    public string BundleJsonEncrypted { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the deterministic 0–100 completeness score across the 10 regulatory readiness gates.
    /// </summary>
    [Description("Dossier readiness completeness score (0.0 to 100.0 percent).")]
    [JsonPropertyName("completenessScore")]
    public double CompletenessScore { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether all 10 fail-closed regulatory gates passed.
    /// </summary>
    [Description("True when all 10 mandatory CMC, QSP, and compliance gates pass cleanly.")]
    [JsonPropertyName("isSubmissionReady")]
    public bool IsSubmissionReady { get; init; }

    /// <summary>
    /// Gets or initializes the authenticated administrator username who compiled this dossier.
    /// </summary>
    [Description("Administrator username who triggered dossier compilation.")]
    [JsonPropertyName("compiledBy")]
    public string CompiledBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this dossier snapshot was compiled.
    /// </summary>
    [Description("UTC timestamp when the regulatory dossier was compiled and sealed.")]
    [JsonPropertyName("compiledAtUtc")]
    public DateTimeOffset CompiledAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RegulatoryDossierRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated regulatory dossier archive record example.</returns>
    public RegulatoryDossierRecord GetExamples() => new()
    {
        DossierId = "ind_20261124_001",
        PatientId = "pat_001",
        LotId = "lot_sample_001",
        SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
        PayloadSha256 = new string('a', 64),
        SignatureBase64 = "TUwtRFNBLTY1LURPU1NJRVI=",
        SignerKeyId = "key_node_mldsa65_001",
        BundleJsonEncrypted = "data:application/encrypted;base64,AAA=",
        CompletenessScore = 100.0,
        IsSubmissionReady = true,
        CompiledBy = "admin_lead",
        CompiledAtUtc = new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero)
    };
}
