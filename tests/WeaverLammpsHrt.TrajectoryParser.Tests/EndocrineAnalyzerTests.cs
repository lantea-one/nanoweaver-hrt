using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Models;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Unit tests verifying the mathematical accuracy and state transition logic of the endocrine simulation engine 
/// and zero-allocation aptamer affinity analyzer.
/// </summary>
public sealed class EndocrineAnalyzerTests
{
    /// <summary>
    /// Verifies that <see cref="AptamerAffinityAnalyzer.DistanceSquared"/> accurately calculates 3D Euclidean distance squared.
    /// </summary>
    [Fact]
    public void DistanceSquared_ShouldComputeAccurateSquaredDistance()
    {
        // WHAT: Tests standard 3D Pythagorean distance (3, 4, 0) -> distance squared should be 25.0.
        // WHY:  Guarantees precision for non-covalent binding pocket evaluations.
        // HOW:  Passes origin and point (3, 4, 0) to DistanceSquared.
        AtomCoordinate p1 = new(0f, 0f, 0f);
        AtomCoordinate p2 = new(3f, 4f, 0f);

        float distSq = AptamerAffinityAnalyzer.DistanceSquared(in p1, in p2);

        Assert.Equal(25.0f, distSq);
    }

    /// <summary>
    /// Verifies that <see cref="AptamerAffinityAnalyzer.CountBindingContacts"/> accurately counts only atoms within the proximity threshold.
    /// </summary>
    [Fact]
    public void CountBindingContacts_ShouldFilterBySpatialThreshold()
    {
        // WHAT: Creates a 4-atom coordinate span with 2 atoms close to ligand (3.0 Å and 4.0 Å) and 1 distant (10.0 Å).
        // WHY:  Ensures that steric contact counts correctly distinguish bound residues from bulk solvent.
        // HOW:  Evaluates contact count against a 5.0 Å threshold.
        AtomCoordinate[] frame = new AtomCoordinate[]
        {
            new(3.0f, 0f, 0f),   // Distance 3.0 Å (Bound)
            new(0f, 4.0f, 0f),   // Distance 4.0 Å (Bound)
            new(10.0f, 0f, 0f),  // Distance 10.0 Å (Unbound)
            new(0f, 0f, 0f)      // Ligand center (Index 3)
        };

        int[] pocketIndices = new int[] { 0, 1, 2 };
        int ligandCenterIndex = 3;

        int contacts = AptamerAffinityAnalyzer.CountBindingContacts(frame.AsSpan(), pocketIndices.AsSpan(), ligandCenterIndex, 5.0f);

        Assert.Equal(2, contacts);
    }

    /// <summary>
    /// Verifies that <see cref="AptamerAffinityAnalyzer.CalculateCentroid"/> computes the exact arithmetic mean of coordinates.
    /// </summary>
    [Fact]
    public void CalculateCentroid_ShouldReturnGeometricMean()
    {
        // WHAT: Computes centroid of (0, 0, 0) and (10, 20, 30).
        // WHY:  Used to track ligand center-of-mass during trajectory docking analysis.
        // HOW:  Asserts resulting coordinate equals (5, 10, 15).
        AtomCoordinate[] frame = new AtomCoordinate[]
        {
            new(0f, 0f, 0f),
            new(10f, 20f, 30f)
        };

        int[] indices = new int[] { 0, 1 };
        AtomCoordinate centroid = AptamerAffinityAnalyzer.CalculateCentroid(frame.AsSpan(), indices.AsSpan());

        Assert.Equal(5.0f, centroid.X);
        Assert.Equal(10.0f, centroid.Y);
        Assert.Equal(15.0f, centroid.Z);
    }

    /// <summary>
    /// Verifies that <see cref="EndocrineStateEngine"/> initializes in Estrogenic dominance and correctly transitions to Androgenic mode.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_ShouldTransitionBetweenBistableModes()
    {
        // WHAT: Verifies default Estrogenic setpoint, then requests transition to AndrogenicDominant.
        // WHY:  Validates that the synthetic endocrine toggle switch correctly recalibrates clinical targets.
        // HOW:  Calls UpdateToggleState and verifies target concentrations match masculine endocrine targets.
        EndocrineStateEngine engine = new();
        EndocrineTargetProfile initial = engine.GetActiveProfile();

        Assert.Equal(ToggleState.EstrogenicDominant, initial.TargetState);
        Assert.Equal(550.0, initial.TargetEstradiolPmolL);
        Assert.Equal(1.2, initial.TargetTestosteroneNmolL);

        // Transition to Androgenic
        ToggleSwitchRequest switchRequest = new()
        {
            TargetState = ToggleState.AndrogenicDominant,
            TestosteroneNmolL = 22.5
        };

        EndocrineTargetProfile updated = engine.UpdateToggleState(switchRequest);

        Assert.Equal(ToggleState.AndrogenicDominant, updated.TargetState);
        Assert.Equal(22.5, updated.TargetTestosteroneNmolL);
        Assert.Equal(100.0, updated.TargetEstradiolPmolL);
    }

    /// <summary>
    /// Verifies that pharmacodynamic kinetics simulation produces smooth exponential stabilization curves over 72 hours.
    /// </summary>
    [Fact]
    public void SimulateKinetics_ShouldProduceSmoothStabilizationCurve()
    {
        // WHAT: Runs 72-hour simulated kinetics trajectory.
        // WHY:  Ensures that generated data points provide continuous pharmacodynamic curves for the Web UI.
        // HOW:  Asserts curve point count and checks that concentration approaches the target profile.
        EndocrineStateEngine engine = new();
        IReadOnlyList<HormoneCurvePoint> curve = engine.SimulateKinetics(totalHours: 72, stepHours: 6);

        // 72 / 6 = 12 intervals -> 13 points (0, 6, 12, ... 72)
        Assert.Equal(13, curve.Count);
        Assert.Equal(0, curve[0].Hour);
        Assert.Equal(72, curve[^1].Hour);

        // At hour 72, concentration should be close to target setpoint (550.0 pmol/L)
        Assert.InRange(curve[^1].EstradiolPmolL, 500.0, 560.0);
    }
}
