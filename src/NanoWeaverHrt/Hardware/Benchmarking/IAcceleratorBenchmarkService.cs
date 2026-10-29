namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Hardware accelerator qualification and diagnostic benchmark engine.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This service measures the raw computational speed and memory transfer
/// rates of the node's graphics cards or accelerators. It helps operators verify that the machine is capable of
/// running complex molecular folding simulations before starting long-running jobs.
/// </para>
/// </remarks>
public interface IAcceleratorBenchmarkService
{
    /// <summary>
    /// Executes a deterministic compute and memory throughput benchmark on the hardware accelerator.
    /// </summary>
    /// <param name="ct">Cancellation token to abort the benchmark execution.</param>
    /// <returns>A completed <see cref="AcceleratorBenchmarkReport"/> containing throughput metrics and readiness evaluation.</returns>
    Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recently completed benchmark report, or null if no benchmark has been executed.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel retrieval.</param>
    /// <returns>The latest <see cref="AcceleratorBenchmarkReport"/>, or null if none is available.</returns>
    Task<AcceleratorBenchmarkReport?> GetLatestReportAsync(CancellationToken ct = default);
}
