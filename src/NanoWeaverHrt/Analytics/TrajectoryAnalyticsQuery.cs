namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Represents an analytical query request targeted at a trajectory dataset.
/// </summary>
/// <param name="DatasetName">The name or identifier of the trajectory dataset to query.</param>
/// <param name="Query">The read-only SQL query to execute against the trajectory data.</param>
/// <param name="MaxRows">The maximum number of rows to return from the analytical query (default is 1000).</param>
/// <param name="Timeout">The optional execution timeout for the analytical query.</param>
public sealed record TrajectoryAnalyticsQuery(
    string DatasetName,
    string Query,
    int MaxRows = 1000,
    TimeSpan? Timeout = null);
