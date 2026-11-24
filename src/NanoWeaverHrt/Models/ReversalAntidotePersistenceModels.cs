using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Lifecycle status of a sequence-specific toehold strand-displacement antidote paired with a wet-lab fabrication lot.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReversalAntidoteStatus
{
    /// <summary>
    /// Antidote is cryptographically paired, verified, and held in clinical standby alongside the implanted depot lot.
    /// </summary>
    ActiveStandby = 0,

    /// <summary>
    /// Emergency clinical neutralization has been executed using this antidote to disassemble the paired depot nanostructures.
    /// </summary>
    Neutralized = 1,

    /// <summary>
    /// Antidote pairing was superseded or administratively invalidated prior to neutralization.
    /// </summary>
    Revoked = 2
}

/// <summary>
/// Persistent database record representing a patient-specific toehold strand-displacement antidote oligo
/// cryptographically bound to a physical wet-lab fabrication lot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Durable record stored in <c>weaver_lot_reversal_antidotes</c> holding the
/// complementary invading ssDNA 5'→3' sequence, toehold overhang length, hybridization thermodynamics, and NIST FIPS 204
/// ML-DSA-65 pairing signature binding the antidote to its target <see cref="FabricationLotRecord"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P230): Provides a verifiable biophysical and cryptographic kill-switch
/// pairing guarantee before high-risk clinical depot implantation.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and updated via <see cref="NanoWeaverHrt.Storage.IReversalAntidoteRepository"/>
/// with atomic GDPR Article 17 patient erasure support across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ReversalAntidoteRecord", Description = "Persisted toehold strand-displacement reversal antidote paired via ML-DSA-65 to a fabrication lot.")]
public sealed record ReversalAntidoteRecord : IExamplesProvider<ReversalAntidoteRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this paired reversal antidote protocol.
    /// </summary>
    [Description("Unique identifier of the reversal antidote record.")]
    [JsonPropertyName("antidoteId")]
    public string AntidoteId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the identifier of the primary nanocarrier fabrication lot that this antidote neutralizes.
    /// </summary>
    [Description("Identifier of the paired primary wet-lab fabrication lot.")]
    [JsonPropertyName("primaryLotId")]
    public string PrimaryLotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning anonymized patient identifier.
    /// </summary>
    [Description("Patient identifier associated with both the primary lot and paired antidote.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Watson-Crick complementary invading ssDNA antidote oligo sequence in 5'→3' orientation.
    /// </summary>
    [Description("5'-to-3' single-stranded DNA antidote oligo sequence containing the toehold and branch-migration domain.")]
    [JsonPropertyName("antidoteSequence5To3")]
    public string AntidoteSequence5To3 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exposed single-stranded toehold initiation domain length in nucleotides (6 to 12 nt).
    /// </summary>
    [Description("Toehold overhang length in nucleotides initiating rapid strand displacement.")]
    [JsonPropertyName("toeholdLengthNt")]
    public int ToeholdLengthNt { get; init; } = 8;

    /// <summary>
    /// Gets or initializes the predicted DNA duplex melting temperature Tm in degrees Celsius.
    /// </summary>
    [Description("Calculated antidote-target duplex melting temperature in °C.")]
    [JsonPropertyName("meltingTempCelsius")]
    public double MeltingTempCelsius { get; init; }

    /// <summary>
    /// Gets or initializes the predicted standard Gibbs free energy of hybridization ΔG°37 in kcal/mol.
    /// </summary>
    [Description("Calculated hybridization free energy ΔG°37 in kcal/mol (negative values indicate spontaneous binding).")]
    [JsonPropertyName("bindingDeltaGKcalMol")]
    public double BindingDeltaGKcalMol { get; init; }

    /// <summary>
    /// Gets or initializes the lowercase 64-character SHA-256 hex digest of the canonical antidote-lot pairing payload.
    /// </summary>
    [Description("Canonical SHA-256 hex digest binding antidote sequence, thermodynamics, lot ID, and patient ID.")]
    [JsonPropertyName("pairedPayloadSha256")]
    public string PairedPayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 digital signature attesting this pairing.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 digital signature generated over the canonical pairing payload.")]
    [JsonPropertyName("pairedSignatureBase64")]
    public string PairedSignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the node signing key identifier used to sign the pairing attestation.
    /// </summary>
    [Description("Node ML-DSA-65 public signing key identifier (SHA-256 hex key ID).")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current lifecycle status of this paired antidote.
    /// </summary>
    [Description("Current operational status of the reversal antidote (ActiveStandby, Neutralized, or Revoked).")]
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReversalAntidoteStatus Status { get; init; } = ReversalAntidoteStatus.ActiveStandby;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when the antidote pairing was created.
    /// </summary>
    [Description("UTC timestamp when the antidote was designed, signed, and persisted.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="ReversalAntidoteRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated reversal antidote pairing record example.</returns>
    public ReversalAntidoteRecord GetExamples() => new()
    {
        AntidoteId = "anti_20261124_001",
        PrimaryLotId = "lot_sample_001",
        PatientId = "pat_001",
        AntidoteSequence5To3 = "GCTAGCATGCTAGCATGCTAGCATGCTAGCATGCAT",
        ToeholdLengthNt = 8,
        MeltingTempCelsius = 71.4,
        BindingDeltaGKcalMol = -42.6,
        PairedPayloadSha256 = "aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff6666aaaa1111bbbb2222",
        PairedSignatureBase64 = "TUwtRFNBLTY1LVNJR05BVFVSRS1FWEFNUExF",
        SignerKeyId = "key_node_mldsa65_001",
        Status = ReversalAntidoteStatus.ActiveStandby,
        CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)
    };
}

/// <summary>
/// Persistent audit record capturing an emergency clinical depot neutralization event executed with a paired antidote.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Immutable administration row stored in <c>weaver_lot_neutralization_events</c>
/// documenting administered antidote mass, plain-English clinical justification, modeled 24-hour hormone clearance,
/// and operator attribution.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P230): Guarantees tamper-evident clinical traceability whenever a permanent
/// depot nanocarrier batch is disassembled in vivo.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inserted alongside lot revocation during <c>AdministerAntidoteAsync</c> and listed in
/// ascending chronological order by <see cref="NanoWeaverHrt.Storage.IReversalAntidoteRepository"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "LotNeutralizationEventRecord", Description = "Persisted emergency clinical depot neutralization administration event.")]
public sealed record LotNeutralizationEventRecord : IExamplesProvider<LotNeutralizationEventRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this clinical neutralization administration event.
    /// </summary>
    [Description("Unique identifier of the neutralization event record.")]
    [JsonPropertyName("neutralizationId")]
    public string NeutralizationId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the paired antidote identifier administered to the patient.
    /// </summary>
    [Description("Identifier of the paired reversal antidote injected to neutralize the depot.")]
    [JsonPropertyName("antidoteId")]
    public string AntidoteId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the neutralized primary fabrication lot identifier.
    /// </summary>
    [Description("Identifier of the primary fabrication lot halted and permanently recalled.")]
    [JsonPropertyName("primaryLotId")]
    public string PrimaryLotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the anonymized patient identifier receiving emergency neutralization.
    /// </summary>
    [Description("Patient identifier receiving the emergency antidote intervention.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the administered antidote oligo dose mass in milligrams.
    /// </summary>
    [Description("Administered invading ssDNA antidote dose in milligrams.")]
    [JsonPropertyName("dosageMg")]
    public double DosageMg { get; init; }

    /// <summary>
    /// Gets or initializes the documented clinical reason triggering emergency depot neutralization.
    /// </summary>
    [Description("Plain-English clinical justification for executing irreversible depot neutralization.")]
    [JsonPropertyName("clinicalReason")]
    public string ClinicalReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the modeled time in hours required to reach systemic endocrine clearance threshold.
    /// </summary>
    [Description("Projected hours required to suppress residual depot hormone release below 5% baseline.")]
    [JsonPropertyName("estimatedClearanceHours")]
    public double EstimatedClearanceHours { get; init; }

    /// <summary>
    /// Gets or initializes the authenticated operator username executing the emergency administration.
    /// </summary>
    [Description("Authenticated clinician or researcher username who executed the neutralization.")]
    [JsonPropertyName("executedBy")]
    public string ExecutedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when emergency neutralization was administered.
    /// </summary>
    [Description("UTC timestamp when emergency neutralization was recorded.")]
    [JsonPropertyName("administeredAtUtc")]
    public DateTimeOffset AdministeredAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="LotNeutralizationEventRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated neutralization event record example.</returns>
    public LotNeutralizationEventRecord GetExamples() => new()
    {
        NeutralizationId = "neut_20261124_001",
        AntidoteId = "anti_20261124_001",
        PrimaryLotId = "lot_sample_001",
        PatientId = "pat_001",
        DosageMg = 6.25,
        ClinicalReason = "Acute CARPA hypersensitivity requiring immediate hydrogel nanocarrier disassembly.",
        EstimatedClearanceHours = 14.5,
        ExecutedBy = "clinician_ada",
        AdministeredAtUtc = new DateTimeOffset(2026, 11, 24, 15, 30, 0, TimeSpan.Zero)
    };
}
