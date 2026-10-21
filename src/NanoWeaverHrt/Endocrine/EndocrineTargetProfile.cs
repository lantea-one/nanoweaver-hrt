using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Defines patient-specific target hormonal setpoints for closed-loop endocrine regulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Every individual's endocrine system has personalized 
/// target concentrations for optimal bone density, cardiovascular health, mood stability, and physical expression. 
/// This record stores those specific numerical targets.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "EndocrineTargetProfile", Description = "Patient-specific target hormonal setpoints, bistable dominance states, and nanocarrier parameters.")]
public sealed record EndocrineTargetProfile : IExamplesProvider<EndocrineTargetProfile>
{
    /// <summary>
    /// Unique anonymized identifier for the patient or experimental trial subject.
    /// </summary>
    [Description("Unique anonymized identifier for the patient or experimental trial subject.")]
    [JsonPropertyName("patientId")]
    public required string PatientId { get; init; }

    /// <summary>
    /// The primary clinical objective: Male-to-Female (MtF) or Female-to-Male (FtM) permanent HRT.
    /// </summary>
    [Description("The primary clinical objective: Male-to-Female (MtF) or Female-to-Male (FtM) permanent HRT.")]
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// The desired dominant hormonal state (Estrogenic vs. Androgenic).
    /// </summary>
    [Description("The desired dominant hormonal state (Estrogenic vs. Androgenic).")]
    [JsonPropertyName("targetState")]
    public required ToggleState TargetState { get; init; }

    /// <summary>
    /// The specific cellular tissue targeted by the delivery nanocarrier.
    /// </summary>
    [Description("The specific cellular tissue targeted by the delivery nanocarrier.")]
    [JsonPropertyName("targetTissue")]
    public required TargetTissueType TargetTissue { get; init; }

    /// <summary>
    /// Target 17β-estradiol serum concentration in picomoles per liter (pmol/L).
    /// Standard therapeutic feminizing range: 350 - 750 pmol/L.
    /// </summary>
    [Description("Target 17β-estradiol serum concentration in pmol/L.")]
    [JsonPropertyName("targetEstradiolPmolL")]
    public required double TargetEstradiolPmolL { get; init; }

    /// <summary>
    /// Target total testosterone serum concentration in nanomoles per liter (nmol/L).
    /// Standard therapeutic masculinizing range: 12 - 28 nmol/L; feminizing suppression target: &lt; 1.8 nmol/L.
    /// </summary>
    [Description("Target total testosterone serum concentration in nmol/L.")]
    [JsonPropertyName("targetTestosteroneNmolL")]
    public required double TargetTestosteroneNmolL { get; init; }

    /// <summary>
    /// Target progesterone concentration in nanomoles per liter (nmol/L), if applicable.
    /// </summary>
    [Description("Target progesterone concentration in nmol/L, if applicable.")]
    [JsonPropertyName("targetProgesteroneNmolL")]
    public double TargetProgesteroneNmolL { get; init; } = 0.0;

    /// <summary>
    /// Target dissociation constant (Kd) in nanomolar (nM) for aptamer-steroid complexation.
    /// Lower values indicate stronger, more selective binding affinity.
    /// </summary>
    [Description("Target dissociation constant (Kd) in nM for aptamer-steroid complexation.")]
    [JsonPropertyName("kdTargetNanomolar")]
    public double BindingAffinityKdTargetNanoMolar { get; init; } = 5.0;

    /// <summary>
    /// Structural parameters of the patient-targeted DNA origami delivery vehicle.
    /// </summary>
    [Description("Structural parameters of the patient-targeted DNA origami delivery vehicle.")]
    [JsonPropertyName("deliveryVehicle")]
    public NanocarrierDeliveryVehicle DeliveryVehicle { get; init; } = NanocarrierDeliveryVehicle.Default;

    /// <summary>
    /// Timestamp when this hormonal profile was established or modified.
    /// </summary>
    [Description("Timestamp when this hormonal profile was established or modified.")]
    [JsonPropertyName("updatedUtc")]
    public required string UpdatedUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EndocrineTargetProfile"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated endocrine profile example.</returns>
    public EndocrineTargetProfile GetExamples() => new()
    {
        PatientId = "sub_patient_8849c",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetState = ToggleState.EstrogenicDominant,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        TargetEstradiolPmolL = 550.0,
        TargetTestosteroneNmolL = 0.95,
        TargetProgesteroneNmolL = 2.4,
        BindingAffinityKdTargetNanoMolar = 2.5,
        DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
        UpdatedUtc = "2026-10-20T18:00:00Z"
    };
}

/// <summary>
/// Physical parameters of the patient-targeted DNA origami nanocarrier modeled and fabricated for HRT delivery.
/// </summary>
[SwaggerSchema(Title = "NanocarrierDeliveryVehicle", Description = "Physical parameters of the patient-targeted DNA origami nanocarrier modeled and fabricated for HRT delivery.")]
public sealed record NanocarrierDeliveryVehicle : IExamplesProvider<NanocarrierDeliveryVehicle>
{
    /// <summary>Default delivery vehicle template for anterior pituitary receptor targeting.</summary>
    public static readonly NanocarrierDeliveryVehicle Default = new()
    {
        Geometry = "Hexagonal Nanotube (6-helix bundle)",
        TargetingLigandAptamer = "GnRHR-Aptamer-42nt",
        PayloadCapacityMolecules = 48,
        EstimatedStabilityHalfLifeHours = 120.0
    };

    /// <summary>Physical geometry of the folded DNA origami container (e.g. Nanotube, Nanobox, Cage).</summary>
    [Description("Physical geometry of the folded DNA origami container.")]
    [JsonPropertyName("geometry")]
    public required string Geometry { get; init; }

    /// <summary>Nucleotide aptamer sequence identifier responsible for cell-surface receptor targeting.</summary>
    [Description("Nucleotide aptamer sequence identifier responsible for cell-surface receptor targeting.")]
    [JsonPropertyName("targetingLigandAptamer")]
    public required string TargetingLigandAptamer { get; init; }

    /// <summary>Estimated molecular payload capacity per nanocarrier.</summary>
    [Description("Estimated molecular payload capacity per nanocarrier.")]
    [JsonPropertyName("payloadCapacityMolecules")]
    public required int PayloadCapacityMolecules { get; init; }

    /// <summary>Estimated enzymatic stability half-life in human serum at 37°C in hours.</summary>
    [Description("Estimated enzymatic stability half-life in human serum at 37°C in hours.")]
    [JsonPropertyName("estimatedStabilityHalfLifeHours")]
    public required double EstimatedStabilityHalfLifeHours { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="NanocarrierDeliveryVehicle"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated nanocarrier parameter example.</returns>
    public NanocarrierDeliveryVehicle GetExamples() => Default;
}

/// <summary>
/// Request payload for transitioning the synthetic endocrine toggle switch.
/// </summary>
[SwaggerSchema(Title = "ToggleSwitchRequest", Description = "Request payload for transitioning the synthetic endocrine toggle switch.")]
public sealed record ToggleSwitchRequest : IExamplesProvider<ToggleSwitchRequest>
{
    /// <summary>
    /// Target operational state to transition toward.
    /// </summary>
    [Description("Target operational state to transition toward.")]
    [JsonPropertyName("targetState")]
    public required ToggleState TargetState { get; init; }

    /// <summary>
    /// Target HRT regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [Description("Target HRT regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen? Regimen { get; init; }

    /// <summary>
    /// Target tissue type for the delivery vehicle.
    /// </summary>
    [Description("Target tissue type for the delivery vehicle.")]
    [JsonPropertyName("targetTissue")]
    public TargetTissueType? TargetTissue { get; init; }

    /// <summary>
    /// Optional override for target estradiol concentration (pmol/L).
    /// </summary>
    [Description("Optional override for target estradiol concentration (pmol/L).")]
    [JsonPropertyName("estradiolPmolL")]
    public double? EstradiolPmolL { get; init; }

    /// <summary>
    /// Optional override for target testosterone concentration (nmol/L).
    /// </summary>
    [Description("Optional override for target testosterone concentration (nmol/L).")]
    [JsonPropertyName("testosteroneNmolL")]
    public double? TestosteroneNmolL { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="ToggleSwitchRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated toggle switch request example.</returns>
    public ToggleSwitchRequest GetExamples() => new()
    {
        TargetState = ToggleState.EstrogenicDominant,
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        EstradiolPmolL = 550.0,
        TestosteroneNmolL = 0.95
    };
}
