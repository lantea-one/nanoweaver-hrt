using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Endocrine;

/// <summary>
/// High-performance, zero-allocation spatial analyzer computing binding affinity and contact geometry 
/// between DNA origami aptamer pockets and steroid hormone ligands from raw trajectory frames.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> A DNA aptamer is a strand of DNA folded into a specific pocket shape 
/// that grabs onto a target molecule (like a glove catching a ball). This analyzer checks every frame of 
/// a molecular simulation to see if the steroid hormone (estrogen or testosterone) is staying firmly inside 
/// the aptamer pocket or slipping away.
/// </para>
/// <para>
/// <strong>Performance &amp; Memory Constraint:</strong> Operates entirely on raw stack and pooled memory slices 
/// (<see cref="ReadOnlySpan{T}"/>). Never allocates temporary objects or strings within the frame analysis loop.
/// </para>
/// </remarks>
public static class AptamerAffinityAnalyzer
{
    /// <summary>
    /// Default proximity threshold in Angstroms (5.0 Å) considered an active non-covalent binding contact.
    /// In molecular dynamics, atoms within 4.5 to 5.5 Å typically participate in van der Waals or hydrogen bonding.
    /// </summary>
    public const float DefaultContactThresholdAngstroms = 5.0f;

    /// <summary>
    /// Computes the Euclidean distance squared between two 3D atom coordinates.
    /// </summary>
    /// <param name="a">The first Cartesian coordinate.</param>
    /// <param name="b">The second Cartesian coordinate.</param>
    /// <returns>The squared distance between <paramref name="a"/> and <paramref name="b"/> in Angstroms squared.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Performance Rationale:</strong> Avoiding <see cref="Math.Sqrt(double)"/> in hot inner loops 
    /// significantly improves CPU throughput. We compare squared values directly against squared thresholds.
    /// </para>
    /// </remarks>
    public static float DistanceSquared(in AtomCoordinate a, in AtomCoordinate b)
    {
        // WHAT: Calculates component differences along X, Y, and Z axes.
        // WHY:  Standard 3D Pythagorean distance calculation.
        // HOW:  (dx*dx + dy*dy + dz*dz)
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>
    /// Counts how many aptamer pocket atoms reside within binding threshold of the target hormone center.
    /// </summary>
    /// <param name="frame">The contiguous memory span of atom coordinates for the current simulation frame.</param>
    /// <param name="pocketIndices">Indices corresponding to atoms composing the DNA aptamer binding pocket.</param>
    /// <param name="hormoneCenterIndex">Index corresponding to the center atom of the steroid hormone ligand.</param>
    /// <param name="thresholdAngstroms">Proximity distance threshold in Angstroms (defaults to 5.0 Å).</param>
    /// <returns>The count of aptamer atoms actively in contact with the hormone ligand.</returns>
    public static int CountBindingContacts(
        ReadOnlySpan<AtomCoordinate> frame,
        ReadOnlySpan<int> pocketIndices,
        int hormoneCenterIndex,
        float thresholdAngstroms = DefaultContactThresholdAngstroms)
    {
        if (frame.IsEmpty || pocketIndices.IsEmpty || (uint)hormoneCenterIndex >= (uint)frame.Length)
        {
            return 0;
        }

        // WHAT: Squares the threshold once before the loop to compare squared distances.
        // WHY:  Eliminates expensive square root operations inside the multi-thousand atom loop.
        // HOW:  Pre-computes threshold * threshold.
        float thresholdSq = thresholdAngstroms * thresholdAngstroms;
        AtomCoordinate hormoneCenter = frame[hormoneCenterIndex];
        int contactCount = 0;

        for (int i = 0; i < pocketIndices.Length; i++)
        {
            int atomIdx = pocketIndices[i];
            if ((uint)atomIdx < (uint)frame.Length)
            {
                if (DistanceSquared(frame[atomIdx], hormoneCenter) <= thresholdSq)
                {
                    contactCount++;
                }
            }
        }

        return contactCount;
    }

    /// <summary>
    /// Computes the geometric centroid (average center) of a specified subset of atoms.
    /// </summary>
    /// <param name="frame">The frame coordinate span.</param>
    /// <param name="atomIndices">Indices of atoms to include in the centroid calculation.</param>
    /// <returns>The computed geometric center as an <see cref="AtomCoordinate"/>.</returns>
    public static AtomCoordinate CalculateCentroid(
        ReadOnlySpan<AtomCoordinate> frame,
        ReadOnlySpan<int> atomIndices)
    {
        if (frame.IsEmpty || atomIndices.IsEmpty)
        {
            return default;
        }

        float sumX = 0f;
        float sumY = 0f;
        float sumZ = 0f;
        int validCount = 0;

        // WHAT: Accumulates spatial coordinates across all target atoms.
        // WHY:  Determines the physical center of mass/geometry for docking evaluation.
        // HOW:  Sums X, Y, Z components and divides by the valid atom count.
        for (int i = 0; i < atomIndices.Length; i++)
        {
            int idx = atomIndices[i];
            if ((uint)idx < (uint)frame.Length)
            {
                AtomCoordinate coord = frame[idx];
                sumX += coord.X;
                sumY += coord.Y;
                sumZ += coord.Z;
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return default;
        }

        return new AtomCoordinate(sumX / validCount, sumY / validCount, sumZ / validCount);
    }
}

/// <summary>
/// Telemetry metrics summarizing binding stability and contact dynamics across a molecular trajectory.
/// </summary>
[SwaggerSchema(Title = "AptamerTrajectoryMetrics", Description = "Telemetry metrics summarizing binding stability, contacts, and occupancy ratio across a molecular trajectory.")]
public sealed record AptamerTrajectoryMetrics : WeaverLammpsHrt.TrajectoryParser.Swagger.IExamplesProvider<AptamerTrajectoryMetrics>
{
    /// <summary>
    /// Name of the parsed simulation trajectory file.
    /// </summary>
    [Description("Name of the parsed simulation trajectory file.")]
    [JsonPropertyName("trajectoryFile")]
    public required string TrajectoryFileName { get; init; }

    /// <summary>
    /// Total number of coordinate frames evaluated.
    /// </summary>
    [Description("Total number of coordinate frames evaluated.")]
    [JsonPropertyName("framesAnalyzed")]
    public required long FramesAnalyzed { get; init; }

    /// <summary>
    /// Average number of active atomic contacts between the aptamer pocket and hormone ligand per frame.
    /// </summary>
    [Description("Average number of active atomic contacts between the aptamer pocket and hormone ligand per frame.")]
    [JsonPropertyName("averageContactsPerFrame")]
    public required double AverageContactsPerFrame { get; init; }

    /// <summary>
    /// Proportion of frames (between 0.0 and 1.0) where the ligand maintained at least 3 active contacts in the pocket.
    /// Values above 0.85 indicate stable thermodynamic binding.
    /// </summary>
    [Description("Proportion of frames (between 0.0 and 1.0) where the ligand maintained stable binding contacts.")]
    [JsonPropertyName("bindingOccupancyRatio")]
    public required double BindingOccupancyRatio { get; init; }

    /// <summary>
    /// Qualitative classification of binding stability based on trajectory occupancy.
    /// </summary>
    [Description("Qualitative classification of binding stability (e.g. 'Stable High-Affinity Complex').")]
    [JsonPropertyName("stabilityVerdict")]
    public required string StabilityVerdict { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="AptamerTrajectoryMetrics"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated aptamer trajectory metrics example.</returns>
    public AptamerTrajectoryMetrics GetExamples() => new()
    {
        TrajectoryFileName = "estradiol_e2_aptamer_docking.xyz",
        FramesAnalyzed = 5000,
        AverageContactsPerFrame = 6.42,
        BindingOccupancyRatio = 0.94,
        StabilityVerdict = "Stable High-Affinity Complex (Kd ≈ 1.8 nM)"
    };
}
