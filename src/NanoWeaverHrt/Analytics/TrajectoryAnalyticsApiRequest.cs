namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Represents an analytical query API request targeted at a trajectory Parquet dataset.
/// </summary>
/// <param name="DatasetName">The name or identifier of the trajectory dataset to query.</param>
/// <param name="Query">The read-only SQL query to execute against the trajectory dataset.</param>
/// <param name="MaxRows">The optional maximum number of rows to return from the analytical query (default is 1000).</param>
public sealed record TrajectoryAnalyticsApiRequest(
    string DatasetName,
    string Query,
    int? MaxRows = 1000);
