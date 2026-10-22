using System.Text;
using System.Text.RegularExpressions;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Behaviour-pinning and differential-equivalence tests for the FASTA / raw DNA sequence parser used by
/// <see cref="PharmacogenomicsService.AnalyzeSequence"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary:</strong> A FASTA file is the plain-text format biologists use to share DNA.
/// It is a list of records; each record starts with a one-line label beginning with <c>&gt;</c> (for example
/// <c>&gt;chromosome 1</c>) followed by one or more lines of letters standing for the four DNA building blocks
/// A, C, G and T (plus "ambiguity" letters such as <c>N</c>, meaning "could be any base"). The parser throws the
/// label lines away, keeps the letters, upper-cases them, and then measures two things that matter clinically:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>CpG density</strong> — how often a C is immediately followed by a G. Bacterial and viral DNA is rich in
/// these; the human innate immune system uses receptor TLR9 to spot them and attack. A DNA origami nanocarrier with
/// too many CpG motifs would be destroyed before it delivered its hormone payload.
/// </description></item>
/// <item><description>
/// <strong>Longest run of repeated CAG triplets</strong> — the androgen (testosterone) receptor gene contains a
/// stretch of <c>CAG CAG CAG …</c>. The number of repeats tunes how strongly the receptor responds to testosterone,
/// so it changes the dose a patient needs.
/// </description></item>
/// </list>
/// <para>
/// <strong>Why these tests exist:</strong> AGENTS.md Prime Directive 1 forbids <see cref="Regex"/> in parsers, so the
/// original regular-expression implementation was replaced with a single-pass span scanner. Prime Directive 6 demands
/// the numbers stay identical. These tests pin the observable results of the original implementation, and
/// <see cref="ReferenceCleanAndMeasure"/> re-implements the exact original regular expressions as an independent
/// oracle so the replacement can be proven byte-for-byte equivalent over exhaustive and pseudo-random inputs.
/// </para>
/// </remarks>
public sealed class FastaSequenceParserPinningTests
{
    /// <summary>
    /// The pharmacogenomics service under test.
    /// </summary>
    private readonly PharmacogenomicsService _service = new();

    /// <summary>
    /// Original regular expression that stripped FASTA label lines (anything from a line-leading '&gt;' to end of line).
    /// </summary>
    /// <remarks>
    /// Reproduced verbatim from the pre-refactor <c>PharmacogenomicsService.AnalyzeSequence</c> so it can act as an
    /// independent behavioural oracle. It must never be "improved" — its job is to encode the old behaviour exactly.
    /// </remarks>
    private static readonly Regex LegacyHeaderRegex = new(@"^>.*$", RegexOptions.Multiline);

    /// <summary>
    /// Original regular expression that removed every character which is not an ASCII letter.
    /// </summary>
    private static readonly Regex LegacyNonAlphaRegex = new(@"[^A-Za-z]");

    /// <summary>
    /// Original regular expression that located tandem runs of four or more CAG trinucleotides.
    /// </summary>
    private static readonly Regex LegacyCagRegex = new(@"(CAG){4,}");

    /// <summary>
    /// Re-implements the pre-refactor cleaning and measurement logic using the original regular expressions.
    /// </summary>
    /// <param name="input">Raw FASTA or bare nucleotide text exactly as submitted by a caller.</param>
    /// <returns>
    /// A tuple of the cleaned sequence length, the number of CG dinucleotides, and the longest tandem CAG repeat run
    /// (expressed as a triplet count, and reported as zero when no run reached the legacy four-repeat minimum).
    /// </returns>
    /// <remarks>
    /// This is deliberately the slow, allocating, <see cref="Regex"/>-based version. It lives in the test project only,
    /// where Prime Directive 1 does not apply, precisely so the fast production scanner has something to be compared to.
    /// </remarks>
    private static (int CleanLength, int CpgCount, int MaxCagRepeats) ReferenceCleanAndMeasure(string input)
    {
        string cleanSeq = LegacyHeaderRegex.Replace(input, "");
        cleanSeq = LegacyNonAlphaRegex.Replace(cleanSeq, "").ToUpperInvariant();

        int cpgCount = 0;
        for (int i = 0; i < cleanSeq.Length - 1; i++)
        {
            if (cleanSeq[i] == 'C' && cleanSeq[i + 1] == 'G')
            {
                cpgCount++;
            }
        }

        int maxCagRepeats = 0;
        foreach (Match m in LegacyCagRegex.Matches(cleanSeq))
        {
            int repeats = m.Length / 3;
            if (repeats > maxCagRepeats)
            {
                maxCagRepeats = repeats;
            }
        }

        return (cleanSeq.Length, cpgCount, maxCagRepeats);
    }

    /// <summary>
    /// Collects the sequence-derived marker identifiers that <see cref="PharmacogenomicsService.AnalyzeSequence"/>
    /// reports for a given input, sorted so the result is order-independent.
    /// </summary>
    /// <param name="input">Raw FASTA or bare nucleotide text.</param>
    /// <returns>A comma-separated, alphabetically sorted list of detected marker identifiers.</returns>
    private string DetectMarkerIds(string input)
    {
        PatientGenomicProfile profile = _service.AnalyzeSequence("PIN-PATIENT", input, TherapeuticRegimen.MaleToFemale);
        List<string> ids = profile.ObservedMarkers.Select(m => m.MarkerId).OrderBy(id => id, StringComparer.Ordinal).ToList();
        return string.Join(",", ids);
    }

    /// <summary>
    /// Builds a string by repeating a motif a fixed number of times.
    /// </summary>
    /// <param name="motif">The motif to repeat (for example <c>"CAG"</c>).</param>
    /// <param name="times">How many times to repeat the motif.</param>
    /// <returns>The concatenated repeated motif.</returns>
    private static string Repeat(string motif, int times)
    {
        StringBuilder builder = new(motif.Length * times);
        for (int i = 0; i < times; i++)
        {
            builder.Append(motif);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Pins the exact set of sequence-derived markers produced for a broad matrix of real-world FASTA shapes.
    /// </summary>
    /// <param name="input">Raw FASTA or bare nucleotide text.</param>
    /// <param name="expectedMarkerIds">The comma-separated, alphabetically sorted marker identifiers expected.</param>
    /// <remarks>
    /// Each case documents a shape a biologist can realistically paste into the web UI. The expectations were captured
    /// from the original <see cref="Regex"/> implementation before it was replaced, which is what makes this a
    /// regression pin rather than a restatement of the new code.
    /// </remarks>
    [Theory]
    // Two-record FASTA, Unix line endings: labels dropped, 8 bases kept, 2 CpG motifs -> 50% density.
    [InlineData(">h1\nACGT\n>h2\nACGT\n", "TLR9_HIGH_SENSITIVITY")]
    // Identical content with Windows CRLF line endings must give an identical answer.
    [InlineData(">h1\r\nACGT\r\n>h2\r\nACGT\r\n", "TLR9_HIGH_SENSITIVITY")]
    // Bare lowercase sequence with no label at all is upper-cased before measurement.
    [InlineData("acgtacgt", "TLR9_HIGH_SENSITIVITY")]
    // Missing trailing newline must not change the result.
    [InlineData(">h\nACGT", "TLR9_HIGH_SENSITIVITY")]
    // A record whose sequence body is blank contributes nothing.
    [InlineData(">h1\n\n>h2\nACGT\n", "TLR9_HIGH_SENSITIVITY")]
    // A label-only submission yields an empty sequence: no measurements, no markers.
    [InlineData(">only a header", "")]
    [InlineData(">only a header\n", "")]
    // Ambiguity codes (N = any base) are kept as letters but contain no CG and no CAG.
    [InlineData("NNNNNNNNNN", "")]
    [InlineData(">amb\nRYKMSWBDHVN\nryk mswbd hvn\n", "")]
    // A single base cannot form a dinucleotide, so density stays at zero.
    [InlineData("C", "")]
    // Digits, spaces and dashes are discarded; the four remaining bases include one CpG.
    [InlineData("A-T-C-G-1-2-3", "TLR9_HIGH_SENSITIVITY")]
    // Non-ASCII characters are discarded entirely.
    [InlineData("ACGTéü", "TLR9_HIGH_SENSITIVITY")]
    // 28 tandem CAG triplets: long androgen-receptor tract, and no CG motif anywhere.
    [InlineData(">ar\nCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAG\n", "AR_CAG_LONG")]
    // The same tract split across wrapped lines must be stitched together before counting.
    [InlineData(">ar\nCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAG\nCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAG\n", "AR_CAG_LONG")]
    // Mixed case must behave identically to upper case.
    [InlineData("cagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAGcagCAG", "AR_CAG_LONG")]
    // A label line that itself contains CAG repeats must be ignored.
    [InlineData(">CAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAGCAG\nNNNN\n", "")]
    // A '>' that is not at the start of a line is NOT a label: the letters after it are kept.
    [InlineData("ACGT>HDRACGT", "TLR9_HIGH_SENSITIVITY")]
    // A lone carriage return does not start a new line for label purposes, so 'HDR' is kept.
    [InlineData("ACGT\r>HDR\r", "TLR9_HIGH_SENSITIVITY")]
    // Legacy ';' comment lines are NOT treated as labels, so their letters are measured.
    [InlineData(">h1\nACGT\n;comment CG\nACGT", "TLR9_HIGH_SENSITIVITY")]
    public void AnalyzeSequence_PinsLegacyMarkerDetection(string input, string expectedMarkerIds)
    {
        // WHAT: Runs the production sequence analyser over a curated matrix of FASTA shapes.
        // WHY:  Locks in the exact clinical conclusions of the pre-refactor Regex parser (Prime Directive 6).
        // HOW:  Compares the sorted detected marker identifier list against a hard-coded expectation.
        Assert.Equal(expectedMarkerIds, DetectMarkerIds(input));
    }

    /// <summary>
    /// Pins the CAG repeat-count thresholds at every clinically meaningful boundary.
    /// </summary>
    /// <param name="repeatCount">Number of tandem CAG triplets to synthesise.</param>
    /// <param name="expectedMarkerIds">The marker identifiers expected for that repeat count.</param>
    [Theory]
    [InlineData(0, "")]
    [InlineData(3, "")]
    [InlineData(4, "")]
    [InlineData(9, "")]
    [InlineData(10, "AR_CAG_SHORT")]
    [InlineData(14, "AR_CAG_SHORT")]
    [InlineData(19, "AR_CAG_SHORT")]
    [InlineData(20, "")]
    [InlineData(25, "")]
    [InlineData(26, "AR_CAG_LONG")]
    [InlineData(28, "AR_CAG_LONG")]
    public void AnalyzeSequence_PinsCagRepeatThresholds(int repeatCount, string expectedMarkerIds)
    {
        // WHAT: Synthesises a sequence flanked by inert bases around an exact CAG repeat tract.
        // WHY:  The 10-19 (hypersensitive) and >=26 (insensitive) androgen receptor bands drive dosing, so the
        //       boundaries must not drift by even one repeat.
        // HOW:  Builds "AATT" + (CAG * n) + "AATT". The flanks deliberately contain no C-followed-by-G pair, so the
        //       CpG immunogenicity marker cannot fire and the assertion isolates the CAG repeat logic alone.
        string sequence = "AATT" + Repeat("CAG", repeatCount) + "AATT";

        Assert.Equal(expectedMarkerIds, DetectMarkerIds(sequence));
    }

    /// <summary>
    /// Pins the CpG immunogenicity threshold at and either side of the 3.0% trigger point.
    /// </summary>
    /// <param name="sequence">The bare nucleotide sequence to measure.</param>
    /// <param name="expectTlr9">True when the TLR9 immunogenicity marker is expected to fire.</param>
    [Theory]
    // 100 bases, 1 CpG motif => (1*2)/100 = 2.0%: below the 3.0% trigger.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACGTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT", false)]
    // 100 bases, 2 CpG motifs => (2*2)/100 = 4.0%: above the trigger.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAACGAAAAAAAAAAAAAAAAAAAAAAACGTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT", true)]
    // 200 bases, 3 CpG motifs => (3*2)/200 = 3.0%: exactly on the inclusive trigger.
    [InlineData(
        "CGAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACG" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACG" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    // No CpG motif at all.
    [InlineData("ATATATATATATATATATATATATATATATATATATATATATATATATAT", false)]
    public void AnalyzeSequence_PinsCpgDensityThreshold(string sequence, bool expectTlr9)
    {
        // WHAT: Measures CpG density on sequences straddling the 3.0% immunogenicity cut-off.
        // WHY:  Density is reported as (CG motif count x 2) / total bases, so an off-by-one in either term moves
        //       the clinical conclusion; Prime Directive 6 requires the arithmetic to be exact.
        // HOW:  Asserts the CpGMethylationMandatory flag and the TLR9 marker together.
        PatientGenomicProfile profile = _service.AnalyzeSequence("PIN-CPG", sequence, TherapeuticRegimen.MaleToFemale);

        Assert.Equal(expectTlr9, profile.CpGMethylationMandatory);
        Assert.Equal(expectTlr9 ? "TLR9_HIGH_SENSITIVITY" : "", DetectMarkerIds(sequence));
    }

    /// <summary>
    /// Confirms that the curated FASTA matrix produces exactly the marker set implied by the legacy regular
    /// expression oracle, tying the hard-coded pins above to an independently derived result.
    /// </summary>
    /// <param name="input">Raw FASTA or bare nucleotide text.</param>
    [Theory]
    [InlineData(">h1\nACGT\n>h2\nACGT\n")]
    [InlineData(">h1\r\nACGT\r\n>h2\r\nACGT\r\n")]
    [InlineData("acgtacgt")]
    [InlineData(">only a header")]
    [InlineData("NNNNNNNNNN")]
    [InlineData("ACGT>HDRACGT")]
    [InlineData("ACGT\r>HDR\r")]
    [InlineData(">h1\nACGT\n;comment CG\nACGT")]
    public void AnalyzeSequence_AgreesWithLegacyRegexOracle(string input)
    {
        // WHAT: Derives the expected marker set from the legacy Regex implementation and compares it to production.
        // WHY:  Proves the hard-coded pins are the old behaviour rather than the new behaviour restated.
        // HOW:  Applies the same thresholds the service applies to the oracle's cleaned-sequence measurements.
        (int cleanLength, int cpgCount, int maxCagRepeats) = ReferenceCleanAndMeasure(input);

        double density = cleanLength > 1 ? (double)(cpgCount * 2) / cleanLength * 100.0 : 0.0;
        List<string> expected = new();
        if (density >= 3.0)
        {
            expected.Add("TLR9_HIGH_SENSITIVITY");
        }
        if (maxCagRepeats >= 26)
        {
            expected.Add("AR_CAG_LONG");
        }
        else if (maxCagRepeats >= 10 && maxCagRepeats <= 19)
        {
            expected.Add("AR_CAG_SHORT");
        }
        expected.Sort(StringComparer.Ordinal);

        Assert.Equal(string.Join(",", expected), DetectMarkerIds(input));
    }

    /// <summary>
    /// Asserts that the production scanner reports exactly the same three measurements as the legacy regular
    /// expression oracle for a single input.
    /// </summary>
    /// <param name="input">Raw FASTA or bare nucleotide text.</param>
    private static void AssertScannerMatchesOracle(string input)
    {
        (int expectedLength, int expectedCpg, int expectedCag) = ReferenceCleanAndMeasure(input);

        FastaSequenceScanner.ScanUtf16(
            input.AsSpan(),
            out int actualLength,
            out int actualCpg,
            out int actualCag);

        Assert.Equal(expectedLength, actualLength);
        Assert.Equal(expectedCpg, actualCpg);
        Assert.Equal(expectedCag, actualCag);
    }

    /// <summary>
    /// Exhaustively proves scanner/oracle equivalence for every sequence of up to eight bases over the full
    /// four-letter DNA alphabet.
    /// </summary>
    /// <remarks>
    /// 87 381 distinct sequences are checked. This is a brute-force proof rather than a sample: for short inputs there
    /// is no sequence of A, C, G and T on which the span scanner and the retired regular expressions can disagree.
    /// </remarks>
    [Fact]
    public void Scanner_MatchesLegacyOracle_ExhaustivelyForShortDnaSequences()
    {
        // WHAT: Enumerates every string of length 0..8 over the alphabet {C, A, G, T}.
        // WHY:  Prime Directive 6 requires the refactor to be byte-identical, and exhaustive enumeration removes any
        //       doubt about the CpG adjacency rule and the CAG state machine's reset behaviour on short inputs.
        // HOW:  Treats the loop counter as a base-4 numeral, decoding each digit into a base letter.
        const string alphabet = "CAGT";
        char[] buffer = new char[8];
        int checkedSequences = 0;

        for (int length = 0; length <= 8; length++)
        {
            int combinations = 1;
            for (int p = 0; p < length; p++)
            {
                combinations *= alphabet.Length;
            }

            for (int combination = 0; combination < combinations; combination++)
            {
                int remainder = combination;
                for (int position = 0; position < length; position++)
                {
                    buffer[position] = alphabet[remainder % alphabet.Length];
                    remainder /= alphabet.Length;
                }

                AssertScannerMatchesOracle(new string(buffer, 0, length));
                checkedSequences++;
            }
        }

        Assert.Equal(87381, checkedSequences);
    }

    /// <summary>
    /// Proves scanner/oracle equivalence over a large deterministic corpus of messy, realistic FASTA text.
    /// </summary>
    /// <remarks>
    /// The corpus mixes bases, ambiguity codes, lower case, label markers, both line-ending conventions, digits,
    /// spaces and dashes, and is long enough to contain genuine tandem CAG tracts. The generator is a fixed-seed
    /// 64-bit linear congruential sequence rather than <c>Random</c>, so the corpus is byte-identical on every machine
    /// and every run, satisfying Prime Directive 6's zero-flakiness requirement.
    /// </remarks>
    [Fact]
    public void Scanner_MatchesLegacyOracle_OverDeterministicFuzzCorpus()
    {
        // WHAT: Generates 4000 pseudo-random FASTA-like strings and compares scanner output against the oracle.
        // WHY:  Exhaustive enumeration cannot reach the lengths needed for four-or-more tandem CAG triplets, nor the
        //       interaction between label lines, CRLF endings and wrapped sequence lines.
        // HOW:  A fixed-seed LCG drives both the length and the symbol choice; 'CAG' is included as a multi-character
        //       token so long repeat tracts arise naturally.
        string[] tokens = { "A", "C", "G", "T", "N", "a", "c", "g", "t", "CAG", "cag", ">", "\n", "\r\n", "\r", " ", "-", "7", "é" };
        ulong state = 0x5DEECE66D;
        StringBuilder builder = new();

        for (int sample = 0; sample < 4000; sample++)
        {
            builder.Clear();
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            int tokenCount = (int)((state >> 33) % 60UL);

            for (int t = 0; t < tokenCount; t++)
            {
                state = (state * 6364136223846793005UL) + 1442695040888963407UL;
                builder.Append(tokens[(int)((state >> 33) % (ulong)tokens.Length)]);
            }

            AssertScannerMatchesOracle(builder.ToString());
        }
    }

    /// <summary>
    /// Proves scanner/oracle equivalence on hand-picked sequences that target the CAG state machine's awkward cases.
    /// </summary>
    /// <param name="input">Raw nucleotide text crafted to stress a specific state transition.</param>
    [Theory]
    // A long tract followed by a shorter one: the shorter run must not lower the recorded maximum.
    [InlineData("CAGCAGCAGCAGCAGCAGTTTCAGCAGCAGCAG")]
    // The reverse order: a short tract first, then a longer one.
    [InlineData("CAGCAGCAGCAGTTTCAGCAGCAGCAGCAGCAG")]
    // A run broken by a stray 'C' mid-triplet, which must re-seed the machine on that same 'C'.
    [InlineData("CAGCAGCACAGCAGCAGCAGCAG")]
    // A leading duplicate 'C' must not shift the tract out of alignment.
    [InlineData("CCAGCAGCAGCAGCAG")]
    // Mismatch at the second position of a triplet.
    [InlineData("CAACAGCAGCAGCAGCAG")]
    // Mismatch at the third position of a triplet with a non-'C' letter.
    [InlineData("CATCAGCAGCAGCAGCAG")]
    // Two tracts of exactly equal length.
    [InlineData("CAGCAGCAGCAGTCAGCAGCAGCAG")]
    // Exactly the four-triplet minimum, and one below it.
    [InlineData("CAGCAGCAGCAG")]
    [InlineData("CAGCAGCAG")]
    // A tract wrapped across lines inside a labelled record.
    [InlineData(">ar exon 1\r\nCAGCAGCAGCAG\r\nCAGCAGCAGCAG\r\n>next\r\nTTTT\r\n")]
    // A tract that is interrupted only by a line feed, which the old code also stitched together.
    [InlineData("CAGCAG\nCAGCAG\nCAGCAG")]
    // CpG motifs straddling a line wrap.
    [InlineData("AAAC\nGAAAC\nGAAA")]
    public void Scanner_MatchesLegacyOracle_ForCraftedRepeatEdgeCases(string input)
    {
        // WHAT: Compares scanner output against the legacy Regex oracle for adversarial repeat layouts.
        // WHY:  The replacement uses a three-state machine instead of backtracking regex matching, so the cases where
        //       the two strategies could conceivably diverge must be named explicitly, not merely sampled.
        // HOW:  Delegates to AssertScannerMatchesOracle, which asserts all three measurements simultaneously.
        AssertScannerMatchesOracle(input);
    }
}
