using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying the endocrine state engine, preset alignment, and pharmacogenomic kinetics simulation.
/// </summary>
public sealed class EndocrineStateEngineTests
{
    /// <summary>
    /// Verifies that the default constructor of <see cref="EndocrineStateEngine"/> initializes an active profile
    /// strictly aligned with <see cref="EndocrineStateEngine.CreateMtfPreset"/>.
    /// </summary>
    [Fact]
    public void Constructor_DefaultProfile_AlignsWithCreateMtfPreset()
    {
        // WHAT: Compares default constructor profile with CreateMtfPreset profile.
        // WHY:  Guarantees consistent default clinical setpoints across service boot and preset generation.
        // HOW:  Asserts matching TargetEstradiolPmolL, TargetTestosteroneNmolL, and BindingAffinityKdTargetNanoMolar.
        EndocrineStateEngine engine = new();
        EndocrineTargetProfile active = engine.GetActiveProfile();
        EndocrineTargetProfile preset = EndocrineStateEngine.CreateMtfPreset("ANON-PATIENT-001");

        Assert.Equal(preset.Regimen, active.Regimen);
        Assert.Equal(preset.TargetState, active.TargetState);
        Assert.Equal(preset.TargetEstradiolPmolL, active.TargetEstradiolPmolL);
        Assert.Equal(preset.TargetTestosteroneNmolL, active.TargetTestosteroneNmolL);
        Assert.Equal(preset.BindingAffinityKdTargetNanoMolar, active.BindingAffinityKdTargetNanoMolar);
    }

    /// <summary>
    /// Verifies that <see cref="EndocrineStateEngine.SimulateKinetics"/> reduces the effective steady-state
    /// hormone concentration for rapid metabolizers (clearance multiplier > 1.0).
    /// </summary>
    [Fact]
    public void SimulateKinetics_RapidMetabolizer_ReducesEffectiveSteadyState()
    {
        // WHAT: Simulates kinetics for baseline clearance vs accelerated clearance (1.5x).
        // WHY:  Rapid clearance in vivo depletes circulating hormone levels faster than baseline depot infusion.
        // HOW:  Compares the 72-hour plateau concentration between standard and rapid clearance profiles.
        EndocrineStateEngine engine = new();

        var baselineKinetics = engine.SimulateKinetics(totalHours: 72, stepHours: 4);

        PatientGenomicProfile rapidProfile = new()
        {
            PatientId = "P-RAPID",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [],
            Alerts = [],
            ThrombosisRiskIndex = 1.0,
            OverallClearanceMultiplier = 1.5,
            CpGMethylationMandatory = false,
            CalculatedAtUtc = DateTime.UtcNow.ToString("o")
        };

        var rapidKinetics = engine.SimulateKinetics(totalHours: 72, stepHours: 4, rapidProfile);

        // At hour 72 (final index), rapid metabolizer steady-state estradiol must be lower than baseline
        double baselineFinalE2 = baselineKinetics[^1].EstradiolPmolL;
        double rapidFinalE2 = rapidKinetics[^1].EstradiolPmolL;

        Assert.True(rapidFinalE2 < baselineFinalE2,
            $"Expected rapid metabolizer steady-state ({rapidFinalE2}) < baseline ({baselineFinalE2})");
    }
}
