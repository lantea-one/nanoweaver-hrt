using System.IO.Pipelines;
using WeaverLammpsHrt.TrajectoryParser.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Abstractions;

/// <summary>
/// Delegate invoked whenever a complete molecular simulation frame has been parsed without heap allocation.
/// </summary>
/// <param name="frameIndex">The zero-based sequential frame number within the trajectory.</param>
/// <param name="coordinates">A contiguous memory slice containing 3D atom coordinates for the current frame.</param>
/// <returns>A <see cref="ValueTask"/> that completes once downstream frame processing finishes.</returns>
/// <remarks>
/// <para>
/// <strong>Memory Constraint:</strong> The <paramref name="coordinates"/> span is backed by rented memory 
/// and is only valid for the synchronous duration of the invocation. Downstream consumers must either 
/// process the data immediately or copy it to a persistent buffer.
/// </para>
/// </remarks>
public delegate ValueTask FrameParsedCallback(long frameIndex, ReadOnlySpan<AtomCoordinate> coordinates);

/// <summary>
/// Defines a streaming, zero-allocation parser contract for molecular dynamics trajectory files.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A standardized interface for parsing different simulation formats 
/// (e.g. .xyz text files, .dcd binary files, oxDNA configuration files).
/// </para>
/// <para>
/// <strong>Why this design:</strong> Using <see cref="PipeReader"/> allows high-throughput asynchronous 
/// reading directly from disk or network sockets without loading the entire multi-gigabyte trajectory into memory.
/// </para>
/// </remarks>
public interface ITrajectoryParser
{
    /// <summary>
    /// Gets the human-readable format identifier handled by this parser (e.g., "XYZ" or "DCD").
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Parses an incoming byte stream asynchronously using zero-allocation pipelines.
    /// </summary>
    /// <param name="reader">The pipeline reader streaming raw trajectory bytes.</param>
    /// <param name="onFrameAvailable">The callback invoked for each complete coordinate frame.</param>
    /// <param name="cancellationToken">Cancellation token to gracefully abort processing.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous parsing operation.</returns>
    Task ParseAsync(PipeReader reader, FrameParsedCallback onFrameAvailable, CancellationToken cancellationToken = default);
}
