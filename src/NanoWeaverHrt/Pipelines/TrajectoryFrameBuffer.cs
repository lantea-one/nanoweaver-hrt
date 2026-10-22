using System.Buffers;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Pipelines;

/// <summary>
/// Shared allocation policy for trajectory frame coordinate buffers: the hard ceiling on how many atoms a single
/// simulation frame may declare, and the incremental growth strategy that keeps a hostile header from steering
/// memory consumption.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What a frame is, in plain English:</strong> a molecular dynamics engine such as oxDNA or LAMMPS writes out
/// a "frame" — one snapshot of where every atom in the simulated structure was at a particular instant — thousands of
/// times during a run. A DNA origami nanocarrier made of a few thousand bases is a few tens of thousands of atoms, so
/// a frame is normally small; the trajectory is long, not wide.
/// </para>
/// <para>
/// <strong>Why this class exists:</strong> both text trajectory formats announce the frame width up front (the XYZ
/// format's first line is the atom count; LAMMPS writes it under <c>ITEM: NUMBER OF ATOMS</c>), and the binary DCD
/// format carries it in the file header. Those numbers come from a file an attacker may have uploaded through
/// <c>/api/datasets/import</c> or dropped into the shared volume. Passing them straight to
/// <see cref="ArrayPool{T}.Shared"/>'s <c>Rent</c> hands the attacker control of a single allocation: once the request
/// exceeds the pool's largest bucket, <c>Rent</c> falls back to a plain <c>new T[n]</c>, so a twenty-byte file whose
/// header reads <c>2147483647</c> would ask for a 24 GB array and take the node down.
/// </para>
/// <para>
/// <strong>The two-part defence:</strong> first, a declared count above <see cref="MaxAtomsPerFrame"/> is rejected
/// outright, exactly as a malformed or non-numeric count already was. Second, the buffer is never sized from the
/// declared count alone — it starts at <see cref="InitialFrameCapacity"/> and doubles only as real atoms actually
/// arrive, so the memory a file can cause to be reserved is bounded by the data it genuinely contains rather than by
/// the number it claims. A tiny file therefore cannot induce a large allocation even while its declared count sits
/// under the ceiling.
/// </para>
/// </remarks>
internal static class TrajectoryFrameBuffer
{
    /// <summary>
    /// Maximum number of atoms a single trajectory frame may declare before the frame header is rejected as
    /// implausible.
    /// </summary>
    /// <remarks>
    /// Ten million atoms is roughly two orders of magnitude above the largest structure this platform simulates (a
    /// fully solvated DNA origami nanocarrier with its hormone payload runs to the low hundreds of thousands of
    /// atoms), so the ceiling cannot reject legitimate scientific data. At the limit the coordinate buffer for one
    /// frame is 10 000 000 x 12 bytes = 120 MB, which is survivable; the value above it, 2 147 483 647, is not.
    /// </remarks>
    internal const int MaxAtomsPerFrame = 10_000_000;

    /// <summary>
    /// Number of atoms the coordinate buffer is first sized for, before any growth.
    /// </summary>
    /// <remarks>
    /// 4096 atoms is 48 KB, which stays inside <see cref="ArrayPool{T}.Shared"/>'s pooled buckets and covers a typical
    /// single-strand DNA origami frame in one rental, while being small enough that a file lying about its width wastes
    /// nothing worth measuring.
    /// </remarks>
    internal const int InitialFrameCapacity = 4096;

    /// <summary>
    /// Determines whether a declared per-frame atom count is usable.
    /// </summary>
    /// <param name="declaredAtomCount">The atom count read from the trajectory header or frame section.</param>
    /// <returns>
    /// True when the count is strictly positive and does not exceed <see cref="MaxAtomsPerFrame"/>; otherwise false.
    /// </returns>
    /// <remarks>
    /// Zero and negative counts were already treated as malformed headers by every reader, and an over-ceiling count is
    /// now treated identically: the frame is simply not parsed. Callers that reach this method have not yet rented any
    /// memory, which is the point — validation happens strictly before allocation.
    /// </remarks>
    internal static bool IsPlausibleAtomCount(int declaredAtomCount)
    {
        return declaredAtomCount > 0 && declaredAtomCount <= MaxAtomsPerFrame;
    }

    /// <summary>
    /// Returns a larger coordinate buffer, copying any atoms already accumulated and recycling the previous rental.
    /// </summary>
    /// <param name="currentBuffer">
    /// The buffer currently in use, which must be either <see cref="Array.Empty{T}"/> or an active
    /// <see cref="ArrayPool{T}.Shared"/> rental.
    /// </param>
    /// <param name="declaredAtomCount">
    /// The atom count declared for the frame being filled, already validated by <see cref="IsPlausibleAtomCount"/>.
    /// This caps the growth so the buffer never exceeds what the frame could possibly need.
    /// </param>
    /// <param name="atomsInUse">How many leading entries of <paramref name="currentBuffer"/> hold live coordinates.</param>
    /// <returns>A rented buffer strictly larger than <paramref name="currentBuffer"/> holding the same leading atoms.</returns>
    /// <remarks>
    /// The new rental is taken before the old one is handed back, so the copy is always made between two buffers the
    /// caller owns. The caller keeps exactly one live rental at all times, which is what lets each reader discharge its
    /// Prime Directive 1 obligation with a single <c>ArrayPool.Return</c> in its <c>finally</c> block.
    /// </remarks>
    internal static AtomCoordinate[] Grow(AtomCoordinate[] currentBuffer, int declaredAtomCount, int atomsInUse)
    {
        // WHAT: Picks the next capacity and moves the already-parsed atoms into a fresh rental.
        // WHY:  Growing on demand means the memory a trajectory can reserve tracks the coordinates it actually
        //       supplies, not the width its header claims, which is the core of the H4 hardening.
        // HOW:  The first growth jumps to InitialFrameCapacity; later growths double. Both are clamped to the declared
        //       count so a 5-atom frame never reserves 4096 slots and a 10 000 000-atom frame never overshoots.
        if (currentBuffer.Length == 0)
        {
            int firstCapacity = Math.Min(declaredAtomCount, InitialFrameCapacity);
            return ArrayPool<AtomCoordinate>.Shared.Rent(firstCapacity);
        }

        int grownCapacity = Math.Min(declaredAtomCount, currentBuffer.Length * 2);
        AtomCoordinate[] grownBuffer = ArrayPool<AtomCoordinate>.Shared.Rent(grownCapacity);

        // WHAT: Copies the live prefix of the old rental into the new one.
        // WHY:  Atoms already parsed for the current frame must survive the resize or the emitted frame would be
        //       silently truncated, violating Prime Directive 6's accuracy requirement.
        // HOW:  A span-to-span copy of exactly the occupied entries; the remainder of the new buffer stays untouched
        //       because readers only ever expose the slice they have filled.
        currentBuffer.AsSpan(0, atomsInUse).CopyTo(grownBuffer);
        ArrayPool<AtomCoordinate>.Shared.Return(currentBuffer);

        return grownBuffer;
    }
}
