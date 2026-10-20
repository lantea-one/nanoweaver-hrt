using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Genomics;

namespace WeaverLammpsHrt.TrajectoryParser.Endocrine;

/// <summary>
/// Simulates and coordinates the bistable endocrine state machine, controlling patient-targeted hormone setpoints 
/// and modeling serum concentration kinetics over time.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> In a biological system, toggling between Estrogen-dominant and 
/// Testosterone-dominant modes requires smooth transitions. If estrogen levels rise, the body naturally dampens 
/// testosterone production through negative feedback on gonadotropins (LH/FSH). This engine tracks those setpoints 
/// and ensures that transition requests adhere to safe clinical targets.
/// </para>
/// </remarks>
public sealed class EndocrineStateEngine
{
    /// <summary>
    /// Thread synchronization lock guarding modifications to the active profile.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// The currently active target endocrine profile.
    /// </summary>
    private EndocrineTargetProfile _currentProfile;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndocrineStateEngine"/> with standard physiological defaults.
    /// </summary>
    public EndocrineStateEngine()
    {
        // WHAT: Initializes a default target profile tuned for Male-to-Female (MtF) Feminizing HRT.
        // WHY:  Provides a valid baseline for immediate clinical simulation upon service boot.
        // HOW:  Sets E2 to 550 pmol/L (approx 150 pg/mL) and T to 1.2 nmol/L (suppressed).
        _currentProfile = new EndocrineTargetProfile
        {
            PatientId = "ANON-PATIENT-001",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetState = ToggleState.EstrogenicDominant,
            TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetProgesteroneNmolL = 5.0,
            BindingAffinityKdTargetNanoMolar = 4.2,
            DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
            UpdatedUtc = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Generates a standardized clinical preset profile for Male-to-Female (MtF) permanent HRT.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <returns>A target profile configured for estrogenic dominance and complete androgen suppression.</returns>
    public static EndocrineTargetProfile CreateMtfPreset(string patientId) => new()
    {
        PatientId = patientId,
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetState = ToggleState.EstrogenicDominant,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        TargetEstradiolPmolL = 550.0,
        TargetTestosteroneNmolL = 1.1,
        TargetProgesteroneNmolL = 5.0,
        BindingAffinityKdTargetNanoMolar = 3.8,
        DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
        UpdatedUtc = DateTime.UtcNow.ToString("o")
    };

    /// <summary>
    /// Generates a standardized clinical preset profile for Female-to-Male (FtM) permanent HRT.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <returns>A target profile configured for androgenic dominance and estrogen suppression.</returns>
    public static EndocrineTargetProfile CreateFtmPreset(string patientId) => new()
    {
        PatientId = patientId,
        Regimen = TherapeuticRegimen.FemaleToMale,
        TargetState = ToggleState.AndrogenicDominant,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        TargetEstradiolPmolL = 100.0,
        TargetTestosteroneNmolL = 20.0,
        TargetProgesteroneNmolL = 0.5,
        BindingAffinityKdTargetNanoMolar = 4.5,
        DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
        UpdatedUtc = DateTime.UtcNow.ToString("o")
    };

    /// <summary>
    /// Retrieves a snapshot of the currently active endocrine target profile.
    /// </summary>
    /// <returns>An immutable <see cref="EndocrineTargetProfile"/> instance.</returns>
    public EndocrineTargetProfile GetActiveProfile()
    {
        lock (_stateLock)
        {
            return _currentProfile;
        }
    }

    /// <summary>
    /// Explicitly replaces the active endocrine target profile with an adjusted or customized profile.
    /// </summary>
    /// <param name="profile">The new profile to set as active.</param>
    public void SetActiveProfile(EndocrineTargetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_stateLock)
        {
            _currentProfile = profile;
        }
    }

    /// <summary>
    /// Transitions the endocrine toggle switch to a new operational regime and updates target setpoints.
    /// </summary>
    /// <param name="request">The switch request containing target state and optional concentration overrides.</param>
    /// <returns>The newly updated <see cref="EndocrineTargetProfile"/>.</returns>
    public EndocrineTargetProfile UpdateToggleState(ToggleSwitchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            // WHAT: Derives safe physiological setpoints based on the requested toggle mode.
            // WHY:  Prevents hazardous hormone spikes or sub-therapeutic troughs.
            // HOW:  Selects established clinical guidelines unless explicit overrides are provided.
            double estradiolTarget;
            double testosteroneTarget;

            if (request.TargetState == ToggleState.EstrogenicDominant)
            {
                // Typical feminizing HRT setpoint: E2 ~ 550 pmol/L, T suppressed < 1.8 nmol/L
                estradiolTarget = request.EstradiolPmolL ?? 550.0;
                testosteroneTarget = request.TestosteroneNmolL ?? 1.2;
            }
            else if (request.TargetState == ToggleState.AndrogenicDominant)
            {
                // Typical masculinizing HRT setpoint: T ~ 20.0 nmol/L (575 ng/dL), E2 < 150 pmol/L
                estradiolTarget = request.EstradiolPmolL ?? 100.0;
                testosteroneTarget = request.TestosteroneNmolL ?? 20.0;
            }
            else
            {
                estradiolTarget = request.EstradiolPmolL ?? 300.0;
                testosteroneTarget = request.TestosteroneNmolL ?? 8.0;
            }

            TherapeuticRegimen regimen = request.Regimen ?? (request.TargetState == ToggleState.EstrogenicDominant
                ? TherapeuticRegimen.MaleToFemale
                : TherapeuticRegimen.FemaleToMale);

            TargetTissueType targetTissue = request.TargetTissue ?? _currentProfile.TargetTissue;

            _currentProfile = new EndocrineTargetProfile
            {
                PatientId = _currentProfile.PatientId,
                Regimen = regimen,
                TargetState = request.TargetState,
                TargetTissue = targetTissue,
                TargetEstradiolPmolL = Math.Clamp(estradiolTarget, 50.0, 2000.0),
                TargetTestosteroneNmolL = Math.Clamp(testosteroneTarget, 0.2, 50.0),
                TargetProgesteroneNmolL = _currentProfile.TargetProgesteroneNmolL,
                BindingAffinityKdTargetNanoMolar = _currentProfile.BindingAffinityKdTargetNanoMolar,
                DeliveryVehicle = _currentProfile.DeliveryVehicle,
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };

            return _currentProfile;
        }
    }

    /// <summary>
    /// Simulates pharmacodynamic hormone concentration stabilization over a given time horizon, optionally modulating 
    /// clearance rate and burst damping according to a patient's pharmacogenomic profile.
    /// </summary>
    /// <param name="totalHours">Total simulation duration in hours (e.g. 72 hours).</param>
    /// <param name="stepHours">Time interval between discrete curve sample points.</param>
    /// <param name="genomicProfile">Optional patient genomic profile adjusting clearance and thrombosis damping.</param>
    /// <returns>A collection of simulated serum hormone concentration data points.</returns>
    public IReadOnlyList<HormoneCurvePoint> SimulateKinetics(int totalHours = 72, int stepHours = 4, PatientGenomicProfile? genomicProfile = null)
    {
        EndocrineTargetProfile profile = GetActiveProfile();
        List<HormoneCurvePoint> points = new();

        // Starting baseline concentrations
        double currentE2 = profile.TargetState == ToggleState.EstrogenicDominant ? 120.0 : 600.0;
        double currentT = profile.TargetState == ToggleState.EstrogenicDominant ? 18.0 : 1.5;

        // Exponential approach rate toward target setpoints (representing closed-loop aptamer/feedback kinetics)
        // WHAT: Modulates baseline rate constant (0.08 h⁻¹) by the patient's genetic clearance multiplier.
        // WHY:  Rapid metabolizers (e.g. CYP1B1*3) clear faster, whereas poor metabolizers (COMT/UGT2B17) clear slower.
        // HOW:  Scales 0.08 by OverallClearanceMultiplier and applies damping if thrombosis risk is elevated.
        double rateConstant = 0.08;
        if (genomicProfile != null)
        {
            double clearance = genomicProfile.OverallClearanceMultiplier;
            rateConstant *= clearance > 0 ? clearance : 1.0;

            // If thrombosis risk is elevated (> 1.5), dampen release rate to model continuous micro-infusion
            if (genomicProfile.ThrombosisRiskIndex > 1.5)
            {
                rateConstant *= 0.85;
            }
        }

        for (int h = 0; h <= totalHours; h += stepHours)
        {
            // WHAT: Models continuous feedback regulation using differential exponential relaxation.
            // WHY:  Approximates biologically realistic in-vivo transition curves without discontinuous step jumps.
            // HOW:  current = target + (initial - target) * exp(-rate * t)
            double e2AtHour = profile.TargetEstradiolPmolL + (currentE2 - profile.TargetEstradiolPmolL) * Math.Exp(-rateConstant * h);
            double tAtHour = profile.TargetTestosteroneNmolL + (currentT - profile.TargetTestosteroneNmolL) * Math.Exp(-rateConstant * h);

            points.Add(new HormoneCurvePoint
            {
                Hour = h,
                EstradiolPmolL = Math.Round(e2AtHour, 1),
                TestosteroneNmolL = Math.Round(tAtHour, 2)
            });
        }

        return points;
    }
}

/// <summary>
/// Represents a discrete sample point along a simulated hormone concentration timeline.
/// </summary>
[SwaggerSchema(Title = "HormoneCurvePoint", Description = "Discrete sample point along a simulated 72-hour hormone concentration timeline.")]
public sealed record HormoneCurvePoint : WeaverLammpsHrt.TrajectoryParser.Swagger.IExamplesProvider<HormoneCurvePoint>
{
    /// <summary>Elapsed hours from the initiation of the toggle transition.</summary>
    [Description("Elapsed hours from the initiation of the toggle transition.")]
    [JsonPropertyName("hour")]
    public required int Hour { get; init; }

    /// <summary>Estradiol concentration in pmol/L.</summary>
    [Description("17β-estradiol serum concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public required double EstradiolPmolL { get; init; }

    /// <summary>Testosterone concentration in nmol/L.</summary>
    [Description("Total testosterone serum concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public required double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="HormoneCurvePoint"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated hormone timeline sample point example.</returns>
    public HormoneCurvePoint GetExamples() => new()
    {
        Hour = 12,
        EstradiolPmolL = 485.5,
        TestosteroneNmolL = 0.82
    };
}
