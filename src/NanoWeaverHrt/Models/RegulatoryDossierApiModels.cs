using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload instructing the regulatory dossier compiler to assemble and seal an FDA IND or EMA IMPD
/// evidence bundle for a patient cohort subject.
/// </summary>
[SwaggerSchema(Title = "CompileRegulatoryDossierRequest", Description = "Parameters specifying regulatory submission framework and optional target lot/depot/circuit bindings.")]
public sealed record CompileRegulatoryDossierRequest : IExamplesProvider<CompileRegulatoryDossierRequest>
{
    /// <summary>
    /// Gets or initializes the target international electronic trial framework (<see cref="RegulatorySubmissionFramework.FdaInd"/> or <see cref="RegulatorySubmissionFramework.EmaImpd"/>).
    /// </summary>
    [Description("Target international submission framework (FdaInd or EmaImpd).")]
    [JsonPropertyName("submissionFramework")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatorySubmissionFramework SubmissionFramework { get; init; } = RegulatorySubmissionFramework.FdaInd;

    /// <summary>
    /// Gets or initializes an optional fabrication lot identifier to evaluate; when omitted, the patient's newest lot is selected.
    /// </summary>
    [Description("Optional target fabrication lot identifier; defaults to the patient's newest lot.")]
    [JsonPropertyName("lotId")]
    public string? LotId { get; init; }

    /// <summary>
    /// Gets or initializes an optional semi-annual depot formulation identifier; when omitted, the linked lot depot or patient's newest depot is selected.
    /// </summary>
    [Description("Optional target depot formulation identifier; defaults to lot depot or newest patient depot.")]
    [JsonPropertyName("depotId")]
    public string? DepotId { get; init; }

    /// <summary>
    /// Gets or initializes an optional epigenetic toggle circuit identifier; when omitted, the newest approved (or newest) patient circuit is selected.
    /// </summary>
    [Description("Optional target epigenetic circuit identifier; defaults to patient's newest approved circuit.")]
    [JsonPropertyName("circuitId")]
    public string? CircuitId { get; init; }

    /// <summary>
    /// Gets or initializes the clinical trial sponsor organization name stamped on the dossier manifest.
    /// </summary>
    [Description("Clinical trial sponsor name stamped onto the regulatory manifest.")]
    [JsonPropertyName("sponsorName")]
    public string? SponsorName { get; init; } = "NanoWeaverHrt Sovereign Research Consortium";

    /// <summary>
    /// Gets or initializes the investigative trial site code recorded on the dossier manifest.
    /// </summary>
    [Description("Multi-center investigative trial site code.")]
    [JsonPropertyName("clinicalTrialSiteCode")]
    public string? ClinicalTrialSiteCode { get; init; } = "SITE-SOVEREIGN-001";

    /// <summary>
    /// Generates a representative example instance of <see cref="CompileRegulatoryDossierRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated regulatory dossier compilation request example.</returns>
    public CompileRegulatoryDossierRequest GetExamples() => new()
    {
        SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
        LotId = "lot_001",
        DepotId = "depot_001",
        CircuitId = "epicirc_001",
        SponsorName = "NanoWeaverHrt Sovereign Research Consortium",
        ClinicalTrialSiteCode = "SITE-SOVEREIGN-001"
    };
}

/// <summary>
/// Request payload wrapping a portable ML-DSA-65 <see cref="SignedRegulatoryDossierBundle"/> for offline
/// or cross-node clinical trial audit verification.
/// </summary>
[SwaggerSchema(Title = "VerifyRegulatoryDossierRequest", Description = "Request wrapper carrying a portable ML-DSA-65 signed IND / IMPD dossier bundle to verify.")]
public sealed record VerifyRegulatoryDossierRequest : IExamplesProvider<VerifyRegulatoryDossierRequest>
{
    /// <summary>
    /// Gets or initializes the signed FDA IND or EMA IMPD dossier bundle to verify.
    /// </summary>
    [Description("Signed portable regulatory dossier bundle to verify against canonical SHA-256 and trusted ML-DSA-65 keys.")]
    [JsonPropertyName("bundle")]
    public SignedRegulatoryDossierBundle? Bundle { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="VerifyRegulatoryDossierRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated regulatory dossier verification request example.</returns>
    public VerifyRegulatoryDossierRequest GetExamples() => new()
    {
        Bundle = new SignedRegulatoryDossierBundle().GetExamples()
    };
}
