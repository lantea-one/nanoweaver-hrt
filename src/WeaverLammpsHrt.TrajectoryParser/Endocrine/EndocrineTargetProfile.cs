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
    /// The desired dominant hormonal state (Estrogenic vs. Androgenic).
    /// </summary>
    [JsonPropertyName("target_state")]
    public required ToggleState TargetState { get; init; }

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
    /// Timestamp when this hormonal profile was established or modified.
    /// </summary>
    [JsonPropertyName("updated_utc")]
    public required string UpdatedUtc { get; init; }
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
