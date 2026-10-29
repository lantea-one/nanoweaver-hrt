namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Represents the tabular API response produced by executing a trajectory analytical query.
/// </summary>
/// <param name="DatasetName">The name or identifier of the queried trajectory dataset.</param>
/// <param name="Columns">The ordered list of column names produced by the query.</param>
/// <param name="Rows">The rows of data returned by the query, where each row is an ordered list of cell values.</param>
/// <param name="TotalRows">The total number of rows returned in this result set.</param>
/// <param name="ElapsedMilliseconds">The execution duration of the query in milliseconds.</param>
public sealed record TrajectoryAnalyticsApiResponse(
    string DatasetName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int TotalRows,
    double ElapsedMilliseconds);
