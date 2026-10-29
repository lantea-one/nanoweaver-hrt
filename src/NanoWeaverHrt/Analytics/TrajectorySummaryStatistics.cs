namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Encapsulates high-level geometrical and structural summary metrics for a molecular trajectory dataset.
/// </summary>
/// <param name="DatasetName">The identifier of the analyzed trajectory dataset.</param>
/// <param name="AtomCount">The total number of unique atoms or beads per frame in the trajectory.</param>
/// <param name="FrameCount">The total number of simulation time frames recorded in the trajectory.</param>
/// <param name="BoundingBoxVolume">The enclosing bounding box volume (DX × DY × DZ) across all coordinates in cubic Angstroms (Å³).</param>
/// <param name="MeanRadiusOfGyration">The mean radius of gyration across frames in Angstroms (Å).</param>
public sealed record TrajectorySummaryStatistics(
    string DatasetName,
    long AtomCount,
    long FrameCount,
    double BoundingBoxVolume,
    double MeanRadiusOfGyration);
