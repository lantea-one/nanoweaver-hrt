using System.Text.Json.Serialization;

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
        // WHAT: Initializes a default target profile tuned for Estrogenic Dominance (Feminizing HRT).
        // WHY:  Provides a valid baseline for immediate clinical simulation upon service boot.
        // HOW:  Sets E2 to 550 pmol/L (approx 150 pg/mL) and T to 1.2 nmol/L (suppressed).
        _currentProfile = new EndocrineTargetProfile
        {
            PatientId = "ANON-PATIENT-001",
            TargetState = ToggleState.EstrogenicDominant,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetProgesteroneNmolL = 5.0,
            BindingAffinityKdTargetNanoMolar = 4.2,
            UpdatedUtc = DateTime.UtcNow.ToString("o")
        };
    }

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

            _currentProfile = new EndocrineTargetProfile
            {
                PatientId = _currentProfile.PatientId,
                TargetState = request.TargetState,
                TargetEstradiolPmolL = Math.Clamp(estradiolTarget, 50.0, 2000.0),
                TargetTestosteroneNmolL = Math.Clamp(testosteroneTarget, 0.2, 50.0),
                TargetProgesteroneNmolL = _currentProfile.TargetProgesteroneNmolL,
                BindingAffinityKdTargetNanoMolar = _currentProfile.BindingAffinityKdTargetNanoMolar,
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };

            return _currentProfile;
        }
    }

    /// <summary>
    /// Simulates pharmacodynamic hormone concentration stabilization over a given time horizon.
    /// </summary>
    /// <param name="totalHours">Total simulation duration in hours (e.g. 72 hours).</param>
    /// <param name="stepHours">Time interval between discrete curve sample points.</param>
    /// <returns>A collection of simulated serum hormone concentration data points.</returns>
    public IReadOnlyList<HormoneCurvePoint> SimulateKinetics(int totalHours = 72, int stepHours = 4)
    {
        EndocrineTargetProfile profile = GetActiveProfile();
        List<HormoneCurvePoint> points = new();

        // Starting baseline concentrations
        double currentE2 = profile.TargetState == ToggleState.EstrogenicDominant ? 120.0 : 600.0;
        double currentT = profile.TargetState == ToggleState.EstrogenicDominant ? 18.0 : 1.5;

        // Exponential approach rate toward target setpoints (representing closed-loop aptamer/feedback kinetics)
        double rateConstant = 0.08;

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
public sealed record HormoneCurvePoint
{
    /// <summary>Elapsed hours from the initiation of the toggle transition.</summary>
    [JsonPropertyName("hour")]
    public required int Hour { get; init; }

    /// <summary>Estradiol concentration in pmol/L.</summary>
    [JsonPropertyName("estradiol_pmol_l")]
    public required double EstradiolPmolL { get; init; }

    /// <summary>Testosterone concentration in nmol/L.</summary>
    [JsonPropertyName("testosterone_nmol_l")]
    public required double TestosteroneNmolL { get; init; }
}
