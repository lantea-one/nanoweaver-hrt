namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Service contract for executing in-process columnar SQL analytical queries and extracting summary
/// statistics over molecular dynamics trajectory Parquet datasets.
/// </summary>
public interface ITrajectoryAnalyticsService
{
    /// <summary>
    /// Executes a read-only analytical SQL query against the specified trajectory dataset.
    /// </summary>
    /// <param name="query">The query specification defining the dataset, SQL statement, row limit, and optional timeout.</param>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the tabular query result.</returns>
    Task<TrajectoryAnalyticsResult> ExecuteQueryAsync(TrajectoryAnalyticsQuery query, CancellationToken ct = default);

    /// <summary>
    /// Computes geometrical summary statistics (atom count, frame count, bounding box volume, mean radius of gyration)
    /// for the specified trajectory dataset.
    /// </summary>
    /// <param name="datasetName">The identifier of the dataset to analyze.</param>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the calculated trajectory summary statistics.</returns>
    Task<TrajectorySummaryStatistics> GetSummaryStatisticsAsync(string datasetName, CancellationToken ct = default);
}
