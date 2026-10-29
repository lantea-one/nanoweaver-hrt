using System.Data;
using System.Data.Common;
using System.Diagnostics;
using DuckDB.NET.Data;
using Microsoft.Extensions.Configuration;

namespace NanoWeaverHrt.Analytics;

/// <summary>
/// Columnar trajectory analytical engine executing in-process SQL queries directly over Apache Parquet datasets
/// and memory tables via DuckDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Architecture Overview:</strong> Integrates DuckDB.NET in-process execution with zero-copy columnar scanning.
/// Trajectory coordinates serialized into Parquet by <see cref="Storage.DuckDbParquetWriter"/> can be queried
/// interactively with full ANSI SQL support, CTEs, and mathematical aggregation functions.
/// </para>
/// <para>
/// <strong>Security &amp; Gating:</strong> Enforces strict read-only execution by validating all incoming SQL statements
/// against mutation and administrative keywords (e.g., DROP, DELETE, INSERT, UPDATE, ALTER, ATTACH, COPY, PRAGMA, INSTALL, LOAD).
/// </para>
/// </remarks>
public sealed class DuckDbTrajectoryAnalyticsService : ITrajectoryAnalyticsService, IDisposable
{
    /// <summary>
    /// Configuration key for overriding the directory hosting trajectory Parquet files.
    /// </summary>
    private const string ParquetDirConfigKey = "TRAJECTORY_PARQUET_DIR";

    /// <summary>
    /// Default relative directory path where trajectory Parquet files are stored.
    /// </summary>
    private const string DefaultParquetDirectory = "simulations/parquet";

    /// <summary>
    /// Default row limit applied when the requested row count is unassigned or non-positive.
    /// </summary>
    private const int DefaultMaxRows = 1000;

    /// <summary>
    /// Upper safety ceiling applied to analytical query row limits.
    /// </summary>
    private const int AbsoluteMaxRows = 10000;

    /// <summary>
    /// Set of prohibited mutation and administrative SQL keywords rejected by the read-only query guard.
    /// </summary>
    private static readonly HashSet<string> ProhibitedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP",
        "DELETE",
        "INSERT",
        "UPDATE",
        "ALTER",
        "ATTACH",
        "COPY",
        "PRAGMA",
        "INSTALL",
        "LOAD"
    };

    /// <summary>
    /// Host configuration provider used for resolving trajectory storage directories.
    /// </summary>
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// In-process DuckDB database connection hosting the ephemeral analytical session.
    /// </summary>
    private readonly DuckDBConnection _connection;

    /// <summary>
    /// Synchronization gate ensuring serial execution across the shared in-process DuckDB connection.
    /// </summary>
    private readonly SemaphoreSlim _gate;

    /// <summary>
    /// Flag indicating whether the service and its underlying unmanaged DuckDB connection have been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Flag tracking whether the in-memory sample trajectory table and views have been initialized.
    /// </summary>
    private bool _sampleTableInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDbTrajectoryAnalyticsService"/> class.
    /// </summary>
    /// <param name="configuration">The optional configuration provider used to resolve trajectory parquet directories.</param>
    public DuckDbTrajectoryAnalyticsService(IConfiguration? configuration = null)
    {
        _configuration = configuration;
        _connection = new DuckDBConnection("DataSource=:memory:");
        _gate = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc/>
    public async Task<TrajectoryAnalyticsResult> ExecuteQueryAsync(
        TrajectoryAnalyticsQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.DatasetName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // WHAT: Validates query against prohibited mutation keywords.
        // WHY:  Guarantees read-only analytical execution and protects against arbitrary statement execution.
        // HOW:  Extracts tokens and compares against the ProhibitedKeywords set.
        ValidateReadOnlyQuery(query.Query);

        int requestedMax = query.MaxRows <= 0 ? DefaultMaxRows : query.MaxRows;
        int effectiveMaxRows = Math.Clamp(requestedMax, 1, AbsoluteMaxRows);

        using CancellationTokenSource? linkedCts = query.Timeout.HasValue && query.Timeout.Value > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (linkedCts != null && query.Timeout.HasValue)
        {
            linkedCts.CancelAfter(query.Timeout.Value);
        }

        CancellationToken effectiveCt = linkedCts?.Token ?? ct;

        // WHAT: Acquires mutual exclusion lock before using the shared DuckDB in-process connection.
        // WHY:  DuckDB native C connection handles require serialized command execution.
        // HOW:  Waits on SemaphoreSlim using effective cancellation token.
        await _gate.WaitAsync(effectiveCt);
        try
        {
            await EnsureConnectionOpenAsync(effectiveCt);

            // WHAT: Prepares view or table bindings for the target dataset.
            // WHY:  Allows queries referencing 'trajectory' or the dataset name to execute seamlessly.
            // HOW:  If the target is an in-memory dataset, seeds sample rows; otherwise points to read_parquet.
            await PrepareDatasetBindingAsync(query.DatasetName, effectiveCt);

            Stopwatch stopwatch = Stopwatch.StartNew();

            using DuckDBCommand command = _connection.CreateCommand();
            command.CommandText = query.Query;
            if (query.Timeout.HasValue && query.Timeout.Value > TimeSpan.Zero)
            {
                command.CommandTimeout = (int)Math.Ceiling(query.Timeout.Value.TotalSeconds);
            }

            using DbDataReader reader = await command.ExecuteReaderAsync(effectiveCt);

            List<string> columns = new(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            List<IReadOnlyList<object?>> rows = new();
            while (rows.Count < effectiveMaxRows && await reader.ReadAsync(effectiveCt))
            {
                object?[] rowValues = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    rowValues[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(rowValues);
            }

            stopwatch.Stop();
            return new TrajectoryAnalyticsResult(
                columns,
                rows,
                rows.Count,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TrajectorySummaryStatistics> GetSummaryStatisticsAsync(
        string datasetName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // WHAT: Acquires connection lock and prepares table source expression.
        // WHY:  Summary calculation requires access to the in-process DuckDB session.
        // HOW:  Resolves parquet or in-memory source, then queries atom count, frame count, volume, and mean Rg.
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureConnectionOpenAsync(ct);

            string tableSource = await ResolveTableSourceAsync(datasetName, ct);

            string sql = $@"
                WITH frame_rg AS (
                    SELECT
                        frame_index,
                        SQRT(AVG(CAST(distance_from_center AS DOUBLE) * CAST(distance_from_center AS DOUBLE))) AS rg
                    FROM {tableSource}
                    GROUP BY frame_index
                ),
                summary AS (
                    SELECT
                        COUNT(DISTINCT atom_index) AS atom_count,
                        COUNT(DISTINCT frame_index) AS frame_count,
                        CAST((MAX(x) - MIN(x)) AS DOUBLE) * CAST((MAX(y) - MIN(y)) AS DOUBLE) * CAST((MAX(z) - MIN(z)) AS DOUBLE) AS bb_volume
                    FROM {tableSource}
                )
                SELECT
                    COALESCE(s.atom_count, 0) AS atom_count,
                    COALESCE(s.frame_count, 0) AS frame_count,
                    COALESCE(s.bb_volume, 0.0) AS bounding_box_volume,
                    COALESCE((SELECT AVG(rg) FROM frame_rg), 0.0) AS mean_rg
                FROM summary s;
            ";

            using DuckDBCommand command = _connection.CreateCommand();
            command.CommandText = sql;

            using DbDataReader reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            long atomCount = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0));
            long frameCount = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1));
            double bbVolume = reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetValue(2));
            double meanRg = reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3));

            return new TrajectorySummaryStatistics(
                datasetName,
                atomCount,
                frameCount,
                bbVolume,
                meanRg);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases all managed and unmanaged resources held by the analytical engine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Validates that the provided SQL query contains only read-only statements and does not execute mutations or administrative commands.
    /// </summary>
    /// <param name="sql">The SQL query string to inspect.</param>
    /// <exception cref="ArgumentException">Thrown when the query is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the query contains prohibited mutation or administrative keywords.</exception>
    private static void ValidateReadOnlyQuery(string sql)
    {
        // WHAT: Inspects incoming SQL statement tokens to enforce read-only execution.
        // WHY:  Protects the analytical database session from data destruction, privilege escalation, and file system mutations.
        // HOW:  Extracts alphanumeric tokens and verifies that none match the ProhibitedKeywords set.
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        ReadOnlySpan<char> span = sql.AsSpan();
        int i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && !char.IsLetterOrDigit(span[i]) && span[i] != '_')
            {
                i++;
            }

            if (i >= span.Length)
            {
                break;
            }

            int start = i;
            while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_'))
            {
                i++;
            }

            string token = span.Slice(start, i - start).ToString();
            if (ProhibitedKeywords.Contains(token))
            {
                throw new InvalidOperationException($"Query contains prohibited mutation or administrative keyword '{token}'. Only read-only analytical queries are permitted.");
            }
        }
    }

    /// <summary>
    /// Ensures that the underlying in-process DuckDB database connection is open and ready for execution.
    /// </summary>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the connection is open.</returns>
    private async Task EnsureConnectionOpenAsync(CancellationToken ct)
    {
        // WHAT: Ensures the underlying DuckDB in-memory connection is opened.
        // WHY:  DuckDB commands require an open connection state before execution.
        // HOW:  Checks ConnectionState.Open and opens asynchronously if closed.
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct);
        }
    }

    /// <summary>
    /// Populates an in-memory synthetic sample trajectory table and associated views when no on-disk dataset is referenced.
    /// </summary>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the sample tables and views are initialized.</returns>
    private async Task EnsureSampleTableCreatedAsync(CancellationToken ct)
    {
        // WHAT: Initializes a synthetic in-memory sample trajectory table and view.
        // WHY:  Allows queries and summary statistics against sample datasets without requiring on-disk Parquet files.
        // HOW:  Creates sample_trajectory table, populates standard origami beads, and exposes trajectory view.
        if (_sampleTableInitialized)
        {
            return;
        }

        using DuckDBCommand createCmd = _connection.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sample_trajectory (
                frame_index BIGINT,
                atom_index INTEGER,
                x FLOAT,
                y FLOAT,
                z FLOAT,
                distance_from_center FLOAT,
                target_region VARCHAR
            );
            DELETE FROM sample_trajectory;
            INSERT INTO sample_trajectory VALUES
                (0, 0, 10.0, 20.0, 30.0, 5.0, 'aptamer_pocket'),
                (0, 1, 15.0, 25.0, 35.0, 0.0, 'scaffold'),
                (0, 2, 18.0, 28.0, 38.0, 5.0, 'hormone_payload'),
                (1, 0, 10.5, 20.5, 30.5, 5.1, 'aptamer_pocket'),
                (1, 1, 15.5, 25.5, 35.5, 0.1, 'scaffold'),
                (1, 2, 18.5, 28.5, 38.5, 5.1, 'hormone_payload');
            CREATE OR REPLACE TEMPORARY VIEW trajectory AS SELECT * FROM sample_trajectory;
            CREATE OR REPLACE TEMPORARY VIEW sample AS SELECT * FROM sample_trajectory;
            CREATE OR REPLACE TEMPORARY VIEW samples AS SELECT * FROM sample_trajectory;
            CREATE OR REPLACE TEMPORARY VIEW synthetic AS SELECT * FROM sample_trajectory;
        ";
        await createCmd.ExecuteNonQueryAsync(ct);

        _sampleTableInitialized = true;
    }

    /// <summary>
    /// Prepares view bindings for the specified dataset, enabling user queries to reference 'trajectory' or the dataset name.
    /// </summary>
    /// <param name="datasetName">The identifier of the trajectory dataset to bind.</param>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the view bindings have been established.</returns>
    private async Task PrepareDatasetBindingAsync(string datasetName, CancellationToken ct)
    {
        // WHAT: Prepares DuckDB view bindings for analytical queries.
        // WHY:  Allows queries referencing 'trajectory' or the dataset name to execute correctly.
        // HOW:  If the dataset matches in-memory sample tokens, populates the sample table; otherwise creates views over read_parquet.
        if (IsMemoryOnlyToken(datasetName))
        {
            return;
        }

        string parquetPath = ResolveParquetPath(datasetName);
        if (File.Exists(parquetPath))
        {
            string normalizedPath = parquetPath.Replace('\\', '/').Replace("'", "''");
            string viewName = Path.GetFileNameWithoutExtension(datasetName).Replace("\"", "\"\"");

            using DuckDBCommand viewCmd = _connection.CreateCommand();
            viewCmd.CommandText = $@"
                CREATE OR REPLACE TEMPORARY VIEW trajectory AS SELECT * FROM read_parquet('{normalizedPath}');
                CREATE OR REPLACE TEMPORARY VIEW ""{viewName}"" AS SELECT * FROM read_parquet('{normalizedPath}');
            ";
            await viewCmd.ExecuteNonQueryAsync(ct);
            return;
        }

        if (IsSampleToken(datasetName))
        {
            await EnsureSampleTableCreatedAsync(ct);
            return;
        }

        throw new FileNotFoundException($"Trajectory Parquet file for dataset '{datasetName}' does not exist at '{parquetPath}'.", parquetPath);
    }

    /// <summary>
    /// Resolves the SQL table source expression for summary statistics calculations.
    /// </summary>
    /// <param name="datasetName">The identifier of the trajectory dataset.</param>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>A task completing with the SQL table source expression (e.g. read_parquet or sample_trajectory).</returns>
    private async Task<string> ResolveTableSourceAsync(string datasetName, CancellationToken ct)
    {
        // WHAT: Resolves the SQL table source expression for summary statistics calculations.
        // WHY:  Abstracts over read_parquet calls for disk files and in-memory sample tables.
        // HOW:  Checks memory tokens, then on-disk parquet files, then sample fallback, or throws FileNotFoundException.
        if (IsMemoryOnlyToken(datasetName))
        {
            await EnsureSampleTableCreatedAsync(ct);
            return "trajectory";
        }

        string parquetPath = ResolveParquetPath(datasetName);
        if (File.Exists(parquetPath))
        {
            string normalizedPath = parquetPath.Replace('\\', '/').Replace("'", "''");
            return $"read_parquet('{normalizedPath}')";
        }

        if (IsSampleToken(datasetName))
        {
            await EnsureSampleTableCreatedAsync(ct);
            return "sample_trajectory";
        }

        throw new FileNotFoundException($"Trajectory Parquet file for dataset '{datasetName}' does not exist at '{parquetPath}'.", parquetPath);
    }

    /// <summary>
    /// Resolves the physical path of the trajectory parquet file on disk.
    /// </summary>
    /// <param name="datasetName">The name or relative path of the trajectory dataset.</param>
    /// <returns>The resolved absolute path to the Parquet file.</returns>
    private string ResolveParquetPath(string datasetName)
    {
        // WHAT: Resolves the physical path of the trajectory parquet file.
        // WHY:  Allows datasets to be stored in a configured directory or default simulations/parquet folder.
        // HOW:  Checks if datasetName is an existing path; otherwise combines with configured or default directory.
        if (File.Exists(datasetName))
        {
            return Path.GetFullPath(datasetName);
        }

        string fileName = datasetName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
            ? datasetName
            : $"{datasetName}.parquet";

        string? configuredDir = _configuration?[ParquetDirConfigKey];
        string dir = string.IsNullOrWhiteSpace(configuredDir)
            ? DefaultParquetDirectory
            : configuredDir.Trim();

        return Path.GetFullPath(Path.Combine(dir, fileName));
    }

    /// <summary>
    /// Determines whether the dataset name refers strictly to the in-memory session.
    /// </summary>
    /// <param name="datasetName">The identifier to inspect.</param>
    /// <returns><c>true</c> if the dataset name refers to the in-memory session; otherwise, <c>false</c>.</returns>
    private static bool IsMemoryOnlyToken(string datasetName)
    {
        // WHAT: Determines whether the dataset name refers strictly to the in-memory session.
        // WHY:  Allows raw mathematical or ephemeral SQL expressions without backing tables.
        // HOW:  Compares dataset identifier against memory tokens.
        return string.Equals(datasetName, ":memory:", StringComparison.OrdinalIgnoreCase)
            || string.Equals(datasetName, "memory", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the dataset name refers to synthetic sample datasets.
    /// </summary>
    /// <param name="datasetName">The identifier to inspect.</param>
    /// <returns><c>true</c> if the dataset name refers to synthetic sample datasets; otherwise, <c>false</c>.</returns>
    private static bool IsSampleToken(string datasetName)
    {
        // WHAT: Determines whether the dataset name refers to synthetic sample datasets.
        // WHY:  Allows testing and demonstrations without requiring physical Parquet files on disk.
        // HOW:  Compares dataset identifier against sample keywords.
        return string.Equals(datasetName, "sample", StringComparison.OrdinalIgnoreCase)
            || string.Equals(datasetName, "samples", StringComparison.OrdinalIgnoreCase)
            || string.Equals(datasetName, "synthetic", StringComparison.OrdinalIgnoreCase);
    }
}
