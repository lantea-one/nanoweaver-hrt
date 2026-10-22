using System.Buffers;
using System.Text;

namespace NanoWeaverHrt.Genomics;

/// <summary>
/// Zero-allocation, single-pass scanner that measures the two clinically actionable statistics of a FASTA or bare
/// nucleotide sequence: CpG dinucleotide frequency and the longest tandem CAG trinucleotide repeat run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary (no biology background required):</strong> DNA is written down as a long string of
/// four letters — A, C, G and T — each standing for one chemical building block ("base"). FASTA is the plain-text
/// format researchers use to exchange such strings. A FASTA file is a list of records; each record begins with a
/// single <em>label line</em> that starts with a <c>&gt;</c> character (for example <c>&gt;androgen receptor exon 1</c>)
/// and is followed by one or more lines of letters. Those letter lines are frequently wrapped at 60 or 80 columns,
/// and may also contain "ambiguity" letters such as <c>N</c> which mean "the sequencing machine could not tell which
/// base this was". This scanner throws label lines away, keeps only ASCII letters, treats lower case as upper case,
/// and measures two properties of the resulting letter stream:
/// </para>
/// <list type="number">
/// <item><description>
/// <strong>CpG dinucleotides</strong> — a <c>C</c> immediately followed by a <c>G</c>. Bacterial and viral DNA carries
/// many of these in an unmethylated ("unmarked") state, and the human innate immune system has a dedicated sensor,
/// Toll-Like Receptor 9 (TLR9), that treats them as an infection signature. A DNA origami nanocarrier built from a
/// CpG-rich scaffold is therefore liable to be destroyed by immune cells before it can release its hormone payload,
/// so a CpG-rich design must be chemically methylated during fabrication.
/// </description></item>
/// <item><description>
/// <strong>Tandem CAG repeats</strong> — the <c>CAG CAG CAG …</c> stretch near the start of the androgen receptor gene.
/// The receptor is the protein that testosterone docks into, and the number of repeats tunes how strongly it responds:
/// fewer repeats means a more sensitive receptor, more repeats means a less sensitive one. The repeat count therefore
/// changes the testosterone dose a patient needs, which is why it is measured here.
/// </description></item>
/// </list>
/// <para>
/// <strong>Why it is written this way:</strong> AGENTS.md Prime Directive 1 forbids <c>Regex</c>, <c>string.Split()</c>,
/// <c>new byte[]</c> and LINQ inside parsers, and mandates <see cref="ReadOnlySpan{T}"/> over raw UTF-8 bytes with
/// coordinate buffers rented from <see cref="ArrayPool{T}"/> and returned in a <c>finally</c> block. The previous
/// implementation ran three regular expressions, each materialising a full copy of the submitted sequence, which made
/// a 100 MB FASTA upload cost several hundred megabytes of Large Object Heap traffic per request. This scanner makes a
/// single forward pass with a handful of integer state variables and allocates nothing on the managed heap.
/// </para>
/// <para>
/// <strong>Deliberately preserved quirks:</strong> to satisfy Prime Directive 6 the scanner reproduces the historical
/// behaviour exactly, including two edge cases that are arguably not strict FASTA:
/// a <c>&gt;</c> only starts a label line when it is the first character of the stream or directly follows a line feed
/// (so a bare carriage return does not begin a new line, and a mid-line <c>&gt;</c> is just a discarded character);
/// and legacy semicolon (<c>;</c>) comment lines are <em>not</em> recognised, so their letters are measured as if they
/// were sequence data.
/// </para>
/// </remarks>
internal static class FastaSequenceScanner
{
    /// <summary>
    /// ASCII byte value of the line feed character ('\n'), the only character treated as a line boundary.
    /// </summary>
    private const byte LineFeed = (byte)'\n';

    /// <summary>
    /// ASCII byte value of the FASTA label marker ('&gt;') that introduces a record description line.
    /// </summary>
    private const byte LabelMarker = (byte)'>';

    /// <summary>
    /// ASCII byte value of upper-case 'A', the lower bound of the upper-case letter range.
    /// </summary>
    private const byte UpperA = (byte)'A';

    /// <summary>
    /// ASCII byte value of upper-case 'Z', the upper bound of the upper-case letter range.
    /// </summary>
    private const byte UpperZ = (byte)'Z';

    /// <summary>
    /// ASCII byte value of lower-case 'a', the lower bound of the lower-case letter range.
    /// </summary>
    private const byte LowerA = (byte)'a';

    /// <summary>
    /// ASCII byte value of lower-case 'z', the upper bound of the lower-case letter range.
    /// </summary>
    private const byte LowerZ = (byte)'z';

    /// <summary>
    /// Numeric distance between a lower-case ASCII letter and its upper-case counterpart (0x20).
    /// </summary>
    private const byte AsciiCaseOffset = LowerA - UpperA;

    /// <summary>
    /// Sentinel returned by <see cref="ToUpperAsciiLetter"/> for any byte that is not an ASCII letter.
    /// </summary>
    /// <remarks>
    /// Zero is safe as a sentinel because the NUL byte is itself not a letter, so no real nucleotide code collides
    /// with it.
    /// </remarks>
    private const byte NotALetter = 0;

    /// <summary>
    /// ASCII byte value of the cytosine base code ('C').
    /// </summary>
    private const byte Cytosine = (byte)'C';

    /// <summary>
    /// ASCII byte value of the adenine base code ('A').
    /// </summary>
    private const byte Adenine = (byte)'A';

    /// <summary>
    /// ASCII byte value of the guanine base code ('G').
    /// </summary>
    private const byte Guanine = (byte)'G';

    /// <summary>
    /// Smallest number of consecutive CAG triplets that counts as a tandem repeat run.
    /// </summary>
    /// <remarks>
    /// Mirrors the historical <c>(CAG){4,}</c> regular expression: anything shorter than four consecutive triplets is
    /// statistical noise in a random sequence and is reported as no run at all.
    /// </remarks>
    internal const int MinimumTandemCagRun = 4;

    /// <summary>
    /// Measures a sequence held as UTF-16 text by transcoding it into a pooled UTF-8 buffer and scanning the bytes.
    /// </summary>
    /// <param name="sequence">Raw FASTA or bare nucleotide text exactly as submitted by a caller.</param>
    /// <param name="nucleotideCount">Receives the number of ASCII letters retained after label lines are discarded.</param>
    /// <param name="cpgDinucleotideCount">Receives the number of positions where a 'C' is immediately followed by a 'G'.</param>
    /// <param name="longestCagRepeatRun">
    /// Receives the longest run of consecutive CAG triplets expressed as a triplet count, or zero when no run reached
    /// <see cref="MinimumTandemCagRun"/>.
    /// </param>
    /// <remarks>
    /// The sequence arrives as a <see cref="string"/> because it is posted as a JSON field, but Prime Directive 1
    /// requires the scan itself to run over raw UTF-8 bytes. The transcode buffer is therefore rented from
    /// <see cref="ArrayPool{T}.Shared"/> and returned in a <c>finally</c> block, so a steady stream of requests
    /// performs no managed heap allocation at all.
    /// </remarks>
    internal static void ScanUtf16(
        ReadOnlySpan<char> sequence,
        out int nucleotideCount,
        out int cpgDinucleotideCount,
        out int longestCagRepeatRun)
    {
        // WHAT: Sizes a pooled UTF-8 staging buffer exactly, transcodes into it, and scans the bytes.
        // WHY:  Prime Directive 1 bans "new byte[]" in parsers and mandates ArrayPool with a finally-block return;
        //       GetByteCount gives the exact length so no oversized Large Object Heap rental is ever requested.
        // HOW:  Encoding.UTF8.GetBytes writes span-to-span (no intermediate array), then Scan walks the written slice.
        int utf8ByteCount = Encoding.UTF8.GetByteCount(sequence);
        byte[] rentedUtf8 = ArrayPool<byte>.Shared.Rent(utf8ByteCount);

        try
        {
            int bytesWritten = Encoding.UTF8.GetBytes(sequence, rentedUtf8);
            Scan(rentedUtf8.AsSpan(0, bytesWritten), out nucleotideCount, out cpgDinucleotideCount, out longestCagRepeatRun);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedUtf8);
        }
    }

    /// <summary>
    /// Performs the single forward pass over a UTF-8 encoded FASTA or bare nucleotide byte sequence.
    /// </summary>
    /// <param name="utf8Sequence">The UTF-8 encoded sequence bytes to measure.</param>
    /// <param name="nucleotideCount">Receives the number of ASCII letters retained after label lines are discarded.</param>
    /// <param name="cpgDinucleotideCount">Receives the number of positions where a 'C' is immediately followed by a 'G'.</param>
    /// <param name="longestCagRepeatRun">
    /// Receives the longest run of consecutive CAG triplets expressed as a triplet count, or zero when no run reached
    /// <see cref="MinimumTandemCagRun"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Operating on bytes rather than UTF-16 characters is safe and exactly equivalent here: in UTF-8 every byte of a
    /// multi-byte character is 0x80 or higher, so no byte inside a non-ASCII character can ever be mistaken for a
    /// line feed, a '&gt;' or an A-Z/a-z letter. Non-ASCII text is therefore discarded wholesale, which is precisely
    /// what the historical <c>[^A-Za-z]</c> filter did.
    /// </para>
    /// <para>
    /// CpG counting compares each retained letter against the previously retained letter rather than against the
    /// preceding byte, so a C and G separated by a line wrap (<c>…AC</c> newline <c>GT…</c>) is still counted, matching
    /// the old behaviour of concatenating all lines before measuring.
    /// </para>
    /// </remarks>
    internal static void Scan(
        ReadOnlySpan<byte> utf8Sequence,
        out int nucleotideCount,
        out int cpgDinucleotideCount,
        out int longestCagRepeatRun)
    {
        nucleotideCount = 0;
        cpgDinucleotideCount = 0;
        longestCagRepeatRun = 0;

        // WHAT: Tracks whether the cursor sits on the first character of a line and whether that line is a label.
        // WHY:  A '>' only introduces a FASTA label when it is the very first character of a line; anywhere else it is
        //       just a character to discard. Carrying the flag forward lets the whole label be skipped without
        //       materialising it as a string.
        // HOW:  atLineStart begins true (byte 0 is a line start) and is re-armed by every line feed encountered.
        bool atLineStart = true;
        bool insideLabelLine = false;

        // WHAT: Remembers the previous retained letter so C-followed-by-G can be detected.
        // WHY:  CpG motifs are a property of adjacent bases, not of individual bases.
        // HOW:  Holds NotALetter until the first letter is retained, which cannot equal Cytosine so no phantom motif
        //       is counted at the start of the stream.
        byte previousBase = NotALetter;

        // WHAT: A three-state machine recognising the repeating triplet C -> A -> G, plus the run length so far.
        // WHY:  Replaces the forbidden "(CAG){4,}" regular expression with constant-memory state.
        // HOW:  cagTripletProgress counts how many letters of the current candidate triplet have matched (0, 1 or 2);
        //       completing a triplet extends currentCagRun, and any mismatch resets the run, re-seeding the machine
        //       when the offending letter is itself a 'C' so an immediately following CAG tract is not missed.
        int cagTripletProgress = 0;
        int currentCagRun = 0;

        for (int i = 0; i < utf8Sequence.Length; i++)
        {
            byte raw = utf8Sequence[i];

            if (raw == LineFeed)
            {
                atLineStart = true;
                insideLabelLine = false;
                continue;
            }

            if (atLineStart)
            {
                atLineStart = false;
                insideLabelLine = raw == LabelMarker;
            }

            if (insideLabelLine)
            {
                continue;
            }

            byte nucleotide = ToUpperAsciiLetter(raw);
            if (nucleotide == NotALetter)
            {
                continue;
            }

            nucleotideCount++;

            if (previousBase == Cytosine && nucleotide == Guanine)
            {
                cpgDinucleotideCount++;
            }

            previousBase = nucleotide;

            if (cagTripletProgress == 0 && nucleotide == Cytosine)
            {
                cagTripletProgress = 1;
            }
            else if (cagTripletProgress == 1 && nucleotide == Adenine)
            {
                cagTripletProgress = 2;
            }
            else if (cagTripletProgress == 2 && nucleotide == Guanine)
            {
                cagTripletProgress = 0;
                currentCagRun++;

                if (currentCagRun >= MinimumTandemCagRun && currentCagRun > longestCagRepeatRun)
                {
                    longestCagRepeatRun = currentCagRun;
                }
            }
            else
            {
                currentCagRun = 0;
                cagTripletProgress = nucleotide == Cytosine ? 1 : 0;
            }
        }
    }

    /// <summary>
    /// Converts an ASCII letter byte to its upper-case form, or reports that the byte is not a letter.
    /// </summary>
    /// <param name="value">The raw UTF-8 byte to classify.</param>
    /// <returns>
    /// The upper-case ASCII letter byte when <paramref name="value"/> is in the ranges 'A'-'Z' or 'a'-'z';
    /// otherwise <see cref="NotALetter"/>.
    /// </returns>
    /// <remarks>
    /// Case folding is a plain subtraction rather than a culture-aware call: nucleotide and ambiguity codes are always
    /// ASCII, and <c>InvariantGlobalization</c> is enabled for this project, so a locale-sensitive upper-casing routine
    /// would add cost without changing any result.
    /// </remarks>
    private static byte ToUpperAsciiLetter(byte value)
    {
        if (value >= LowerA && value <= LowerZ)
        {
            return (byte)(value - AsciiCaseOffset);
        }

        if (value >= UpperA && value <= UpperZ)
        {
            return value;
        }

        return NotALetter;
    }
}
