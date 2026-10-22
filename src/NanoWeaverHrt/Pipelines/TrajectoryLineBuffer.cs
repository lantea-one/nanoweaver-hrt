using System.Buffers;

namespace NanoWeaverHrt.Pipelines;

/// <summary>
/// Shared policy for copying one line of a text trajectory out of the pipeline into a contiguous, pooled byte buffer
/// that is always large enough to hold the whole line.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a copy is needed at all.</strong> The pipeline hands each line over as a
/// <see cref="ReadOnlySequence{T}"/>, which may be split across several non-adjacent memory segments depending on how
/// the operating system happened to deliver the bytes. The number parsers need one contiguous
/// <see cref="ReadOnlySpan{T}"/>, so the line is copied into a single rented array first.
/// </para>
/// <para>
/// <strong>The defect this closes (audit finding L5, escalated to HIGH).</strong> The readers used to copy
/// <c>Math.Min(lineSequence.Length, lineByteBuffer.Length)</c> bytes into a fixed 512-byte rental and then parse the
/// result as though it were a complete line. A coordinate line longer than 512 bytes — entirely legitimate in a
/// LAMMPS dump with many columns, or any file written with wide fixed-width padding — was therefore silently cut
/// short, and the parser read whatever truncated number remained. If the cut landed inside the Z coordinate,
/// <c>3.75</c> became <c>3.7</c>: a wrong atomic position, accepted as valid, with nothing logged and nothing thrown.
/// </para>
/// <para>
/// <strong>Why that is the worst defect class here.</strong> These coordinates feed the endocrine kinetics and DNA
/// origami fabrication decisions this platform exists to make, and AGENTS.md Prime Directive 6 requires 100%
/// deterministic accuracy. A disclosure bug is eventually noticed; a quietly wrong number in a hormone-dosing
/// pipeline is not. Truncation is therefore made structurally impossible rather than merely unlikely.
/// </para>
/// </remarks>
internal static class TrajectoryLineBuffer
{
    /// <summary>
    /// Size of the initial line buffer rental, in bytes.
    /// </summary>
    /// <remarks>
    /// 512 bytes is the historical size and comfortably holds an ordinary XYZ coordinate line, so the common case
    /// still completes in a single rental with no growth.
    /// </remarks>
    internal const int InitialLineCapacity = 512;

    /// <summary>
    /// Largest line, in bytes, that will be copied and parsed.
    /// </summary>
    /// <remarks>
    /// 64 KiB is far beyond any real coordinate line: a LAMMPS dump row carrying two hundred wide columns is a few
    /// kilobytes. The ceiling exists because the line length is attacker-controlled — a file consisting of one
    /// enormous run of bytes with no line feed in it would otherwise dictate the rental size, and a line longer than
    /// <see cref="int.MaxValue"/> would overflow the cast to <see cref="int"/>. Anything above the ceiling is treated
    /// as a corrupt line rather than being truncated into a plausible-looking wrong answer.
    /// </remarks>
    internal const int MaxLineBytes = 64 * 1024;

    /// <summary>
    /// Copies one complete line into the caller's pooled buffer, growing that buffer if the line does not fit.
    /// </summary>
    /// <param name="lineSequence">The line as delivered by the pipeline, excluding its newline delimiter.</param>
    /// <param name="lineByteBuffer">
    /// The caller's live <see cref="ArrayPool{T}.Shared"/> rental. Replaced with a larger rental when the line does
    /// not fit, in which case the previous rental is returned to the pool before this method exits.
    /// </param>
    /// <param name="lineLength">
    /// Receives the number of bytes copied, which is always the line's full length on success, and zero when the line
    /// was refused.
    /// </param>
    /// <returns>
    /// True when the whole line was copied and may be parsed; false when the line exceeds
    /// <see cref="MaxLineBytes"/> and must be treated as corrupt.
    /// </returns>
    /// <remarks>
    /// The contract is deliberately all-or-nothing: on success the caller holds the entire line, and on failure it
    /// holds nothing. There is no partial outcome, because a partial line is exactly the wrong answer this method
    /// exists to prevent. The caller keeps exactly one live rental throughout, so a single
    /// <c>ArrayPool.Return</c> in its <c>finally</c> block still discharges its Prime Directive 1 obligation.
    /// </remarks>
    internal static bool TryCopyLine(ReadOnlySequence<byte> lineSequence, ref byte[] lineByteBuffer, out int lineLength)
    {
        // WHAT: Refuses any line beyond the documented ceiling before measuring or copying anything.
        // WHY:  The length comes from the file, so it must not size a rental on its own, and a length above
        //       int.MaxValue would wrap when cast. Refusing is safe here only because the caller discards the frame
        //       it was building rather than carrying on with a hole in it.
        // HOW:  ReadOnlySequence<byte>.Length is already a long, so the comparison happens before any narrowing.
        if (lineSequence.Length > MaxLineBytes)
        {
            lineLength = 0;
            return false;
        }

        int requiredBytes = (int)lineSequence.Length;

        // WHAT: Grows the rental when the line is wider than the buffer currently held.
        // WHY:  This replaces the Math.Min clamp that silently discarded the tail of a long line. The old contents
        //       are not copied across because each line is parsed and finished with before the next one is read, so
        //       there is nothing in the old buffer worth preserving.
        // HOW:  The larger rental is taken first and the previous one returned immediately afterwards, so the caller
        //       never holds two rentals and never holds none.
        if (lineByteBuffer.Length < requiredBytes)
        {
            byte[] grownBuffer = ArrayPool<byte>.Shared.Rent(requiredBytes);
            ArrayPool<byte>.Shared.Return(lineByteBuffer);
            lineByteBuffer = grownBuffer;
        }

        lineSequence.CopyTo(lineByteBuffer);
        lineLength = requiredBytes;
        return true;
    }
}
