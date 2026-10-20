using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Endocrine;

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
public sealed record EndocrineTargetProfile
{
    /// <summary>
    /// Unique anonymized identifier for the patient or experimental trial subject.
    /// </summary>
    [JsonPropertyName("patient_id")]
    public required string PatientId { get; init; }

    /// <summary>
    /// The primary clinical objective: Male-to-Female (MtF) or Female-to-Male (FtM) permanent HRT.
    /// </summary>
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// The desired dominant hormonal state (Estrogenic vs. Androgenic).
    /// </summary>
    [JsonPropertyName("target_state")]
    public required ToggleState TargetState { get; init; }

    /// <summary>
    /// The specific cellular tissue targeted by the delivery nanocarrier.
    /// </summary>
    [JsonPropertyName("target_tissue")]
    public required TargetTissueType TargetTissue { get; init; }

    /// <summary>
    /// Target 17β-estradiol serum concentration in picomoles per liter (pmol/L).
    /// Standard therapeutic feminizing range: 350 - 750 pmol/L.
    /// </summary>
    [JsonPropertyName("target_estradiol_pmol_l")]
    public required double TargetEstradiolPmolL { get; init; }

    /// <summary>
    /// Target total testosterone serum concentration in nanomoles per liter (nmol/L).
    /// Standard therapeutic masculinizing range: 12 - 28 nmol/L; feminizing suppression target: &lt; 1.8 nmol/L.
    /// </summary>
    [JsonPropertyName("target_testosterone_nmol_l")]
    public required double TargetTestosteroneNmolL { get; init; }

    /// <summary>
    /// Target progesterone concentration in nanomoles per liter (nmol/L), if applicable.
    /// </summary>
    [JsonPropertyName("target_progesterone_nmol_l")]
    public double TargetProgesteroneNmolL { get; init; } = 0.0;

    /// <summary>
    /// Target dissociation constant (Kd) in nanomolar (nM) for aptamer-steroid complexation.
    /// Lower values indicate stronger, more selective binding affinity.
    /// </summary>
    [JsonPropertyName("kd_target_nanomolar")]
    public double BindingAffinityKdTargetNanoMolar { get; init; } = 5.0;

    /// <summary>
    /// Structural parameters of the patient-targeted DNA origami delivery vehicle.
    /// </summary>
    [JsonPropertyName("delivery_vehicle")]
    public NanocarrierDeliveryVehicle DeliveryVehicle { get; init; } = NanocarrierDeliveryVehicle.Default;

    /// <summary>
    /// Timestamp when this hormonal profile was established or modified.
    /// </summary>
    [JsonPropertyName("updated_utc")]
    public required string UpdatedUtc { get; init; }
}

/// <summary>
/// Physical parameters of the patient-targeted DNA origami nanocarrier modeled and fabricated for HRT delivery.
/// </summary>
public sealed record NanocarrierDeliveryVehicle
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
    [JsonPropertyName("geometry")]
    public required string Geometry { get; init; }

    /// <summary>Nucleotide aptamer sequence identifier responsible for cell-surface receptor targeting.</summary>
    [JsonPropertyName("targeting_ligand_aptamer")]
    public required string TargetingLigandAptamer { get; init; }

    /// <summary>Estimated molecular payload capacity per nanocarrier.</summary>
    [JsonPropertyName("payload_capacity_molecules")]
    public required int PayloadCapacityMolecules { get; init; }

    /// <summary>Estimated enzymatic stability half-life in human serum at 37°C in hours.</summary>
    [JsonPropertyName("estimated_stability_half_life_hours")]
    public required double EstimatedStabilityHalfLifeHours { get; init; }
}

/// <summary>
/// Request payload for transitioning the synthetic endocrine toggle switch.
/// </summary>
public sealed record ToggleSwitchRequest
{
    /// <summary>
    /// Target operational state to transition toward.
    /// </summary>
    [JsonPropertyName("target_state")]
    public required ToggleState TargetState { get; init; }

    /// <summary>
    /// Target HRT regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen? Regimen { get; init; }

    /// <summary>
    /// Target tissue type for the delivery vehicle.
    /// </summary>
    [JsonPropertyName("target_tissue")]
    public TargetTissueType? TargetTissue { get; init; }

    /// <summary>
    /// Optional override for target estradiol concentration (pmol/L).
    /// </summary>
    [JsonPropertyName("estradiol_pmol_l")]
    public double? EstradiolPmolL { get; init; }

    /// <summary>
    /// Optional override for target testosterone concentration (nmol/L).
    /// </summary>
    [JsonPropertyName("testosterone_nmol_l")]
    public double? TestosteroneNmolL { get; init; }
}
