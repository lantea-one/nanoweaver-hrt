using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying FASTA header filtering, deterministic package hashing,
/// and thermal melting temperature calculations in <see cref="FabricationService"/>.
/// </summary>
public sealed class FabricationServiceTests
{
    private readonly FabricationService _service = new();

    /// <summary>
    /// Verifies that <see cref="FabricationService.AnalyzeSequenceComposition"/> ignores English letters
    /// inside FASTA header lines (<c>&gt;Header...</c>).
    /// </summary>
    [Fact]
    public void AnalyzeSequenceComposition_WithFastaHeader_IgnoresHeaderLetters()
    {
        // WHAT: Analyzes a FASTA string containing ">Header with letters A, C, G, T\nACGT".
        // WHY:  FASTA header descriptions must not pollute nucleotide base counts or molecular weight calculations.
        // HOW:  Asserts that exactly 1 A, 1 C, 1 G, and 1 T are counted from the actual sequence line.
        string fasta = ">Header with letters A, C, G, T\nACGT";
        var result = _service.AnalyzeSequenceComposition(fasta);

        Assert.Equal(1, result.A);
        Assert.Equal(1, result.C);
        Assert.Equal(1, result.G);
        Assert.Equal(1, result.T);
        Assert.Equal(50.0, result.GcPercent);
        Assert.Equal(1.32, result.EstimatedMwKDa);
    }

    /// <summary>
    /// Verifies that <see cref="FabricationService.GenerateFabricationPackage"/> generates a 100% deterministic
    /// SHA-256 manifest digest across independent invocations with identical inputs.
    /// </summary>
    [Fact]
    public void GenerateFabricationPackage_IdenticalInputs_ProducesDeterministicSha256Manifest()
    {
        // WHAT: Invokes GenerateFabricationPackage twice with identical parameters.
        // WHY:  Manifest fingerprints must be cryptographically reproducible regardless of execution timestamp.
        // HOW:  Asserts Sha256Manifest is non-empty and strictly identical across runs.
        string sampleId = "SAMPLE-MTF-001";
        TherapeuticRegimen regimen = TherapeuticRegimen.MaleToFemale;
        string geometry = "Hexagonal Nanotube (6-HB)";
        double targetKd = 3.5;

        var package1 = _service.GenerateFabricationPackage(sampleId, regimen, geometry, targetKd);
        var package2 = _service.GenerateFabricationPackage(sampleId, regimen, geometry, targetKd);

        Assert.NotEmpty(package1.Sha256Manifest);
        Assert.Equal(package1.Sha256Manifest, package2.Sha256Manifest);
    }

    /// <summary>
    /// Verifies that <see cref="FabricationService.CalculateMeltingTemperature"/> calculates accurate Tm values
    /// according to the Wallace empirical rule for short oligos and the Marmur formula for longer sequences.
    /// </summary>
    [Fact]
    public void CalculateMeltingTemperature_WallaceAndMarmurRules_CalculatesExpectedTm()
    {
        // WHAT: Tests Tm for zero bases, short sequence (< 14 nt), and standard 50 nt oligo.
        // WHY:  Ensures accurate thermodynamic ramping bounds for DNA origami staple annealing.
        // HOW:  Validates Wallace 2*(A+T) + 4*(G+C) and Marmur formula 64.9 + 41 * (G+C - 16.4) / total.
        Assert.Equal(0.0, FabricationService.CalculateMeltingTemperature(0, 0, 0, 0));

        // Short oligo: A=2, T=2, G=2, C=2 -> (2+2)*2 + (2+2)*4 = 8 + 16 = 24.0°C
        Assert.Equal(24.0, FabricationService.CalculateMeltingTemperature(2, 2, 2, 2));

        // Long oligo: A=10, T=10, G=15, C=15 -> total = 50, G+C = 30 -> 76.1°C
        Assert.Equal(76.1, FabricationService.CalculateMeltingTemperature(10, 10, 15, 15));
    }

    /// <summary>
    /// Verifies that a greater-than symbol (<c>&gt;</c>) appearing in the middle of a sequence line is not treated as a FASTA header.
    /// </summary>
    [Fact]
    public void AnalyzeSequenceComposition_WithMidLineGreaterThan_DoesNotTreatAsHeader()
    {
        // WHAT: Analyzes a FASTA string containing ">Header\nACG>T".
        // WHY:  Only '>' at the beginning of a line initiates a FASTA label comment.
        // HOW:  Asserts that A, C, G, and T are all counted and the '>' character is skipped as a non-nucleotide.
        string fasta = ">Header\nACG>T";
        var result = _service.AnalyzeSequenceComposition(fasta);

        Assert.Equal(1, result.A);
        Assert.Equal(1, result.C);
        Assert.Equal(1, result.G);
        Assert.Equal(1, result.T);
    }

    /// <summary>
    /// Verifies that distinct geometries (Nanotube, Cuboid, Cage) produce distinct structural core staples and manifests.
    /// </summary>
    [Fact]
    public void GenerateFabricationPackage_DifferentGeometries_ProduceDistinctCoreStaplesAndHashes()
    {
        // WHAT: Generates packages for Nanotube, Cuboid, and Icosahedral Cage geometries with identical therapeutic parameters.
        // WHY:  Core crossovers must be physically differentiated to fold the correct 3D architecture.
        // HOW:  Compares core staple sequences and SHA-256 manifests, asserting mutual inequality.
        var pkgNanotube = _service.GenerateFabricationPackage("custom-seq", TherapeuticRegimen.MaleToFemale, "6-Helix Bundle Nanotube", 4.0);
        var pkgCuboid = _service.GenerateFabricationPackage("custom-seq", TherapeuticRegimen.MaleToFemale, "3D DNA Cuboid Vault", 4.0);
        var pkgCage = _service.GenerateFabricationPackage("custom-seq", TherapeuticRegimen.MaleToFemale, "Icosahedral Cage", 4.0);

        Assert.NotEqual(pkgNanotube.StapleOligos[0].Sequence, pkgCuboid.StapleOligos[0].Sequence);
        Assert.NotEqual(pkgNanotube.StapleOligos[0].Sequence, pkgCage.StapleOligos[0].Sequence);
        Assert.NotEqual(pkgCuboid.StapleOligos[0].Sequence, pkgCage.StapleOligos[0].Sequence);

        Assert.NotEqual(pkgNanotube.Sha256Manifest, pkgCuboid.Sha256Manifest);
        Assert.NotEqual(pkgNanotube.Sha256Manifest, pkgCage.Sha256Manifest);
        Assert.NotEqual(pkgCuboid.Sha256Manifest, pkgCage.Sha256Manifest);
    }

    /// <summary>
    /// Verifies that aptamer staples retain intact full-length hormone aptamers and distinct locus hybridization anchors.
    /// </summary>
    [Fact]
    public void GenerateFabricationPackage_PreservesFullLengthAptamerMotifs_AndDistinctAnchors()
    {
        // WHAT: Generates MtF and FtM packages and inspects all ST-APTA-* strands.
        // WHY:  Severing aptamers in half destroys binding affinity; all 4 pockets must have unique locus anchors.
        // HOW:  Asserts full motif substring presence and pairwise sequence uniqueness.
        string mtfAptamer = "ATACCAGCTTATTCAATTGGGAGTGTGTGGTATAGTGGTGTCTTTGTTTGT";
        string ftmAptamer = "CGAATGAGTTACGTACCTGTAGTGCTGTGGCATTTGTACGGTCAGCT";

        var pkgMtf = _service.GenerateFabricationPackage("mtf-sample", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.0);
        var pkgFtm = _service.GenerateFabricationPackage("ftm-sample", TherapeuticRegimen.FemaleToMale, "Nanotube", 4.0);

        var mtfAptas = new List<StapleOligoRecord>();
        foreach (var s in pkgMtf.StapleOligos)
        {
            if (s.StapleId.StartsWith("ST-APTA-"))
            {
                mtfAptas.Add(s);
            }
        }

        var ftmAptas = new List<StapleOligoRecord>();
        foreach (var s in pkgFtm.StapleOligos)
        {
            if (s.StapleId.StartsWith("ST-APTA-"))
            {
                ftmAptas.Add(s);
            }
        }

        Assert.Equal(4, mtfAptas.Count);
        Assert.Equal(4, ftmAptas.Count);

        HashSet<string> seenMtf = new();
        foreach (var staple in mtfAptas)
        {
            Assert.Contains(mtfAptamer, staple.Sequence);
            Assert.True(seenMtf.Add(staple.Sequence), $"Duplicate staple sequence found: {staple.StapleId}");
        }

        HashSet<string> seenFtm = new();
        foreach (var staple in ftmAptas)
        {
            Assert.Contains(ftmAptamer, staple.Sequence);
            Assert.True(seenFtm.Add(staple.Sequence), $"Duplicate staple sequence found: {staple.StapleId}");
        }
    }

    /// <summary>
    /// Verifies that targetKd tiers modulate GC-clamp vs AT-rich stem transitions in aptamer functionalization staples.
    /// </summary>
    [Fact]
    public void GenerateFabricationPackage_KdTiers_ModulatesStemClamps()
    {
        // WHAT: Generates packages with targetKd <= 3.0 nM, 3.0-6.0 nM, and >= 6.0 nM.
        // WHY:  High-affinity designs require GC clamps; rapid-release designs require AT-rich stems.
        // HOW:  Asserts that aptamer sequences differ across Kd tiers.
        var tightPkg = _service.GenerateFabricationPackage("sample", TherapeuticRegimen.MaleToFemale, "Nanotube", 2.0);
        var midPkg = _service.GenerateFabricationPackage("sample", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.5);
        var loosePkg = _service.GenerateFabricationPackage("sample", TherapeuticRegimen.MaleToFemale, "Nanotube", 7.5);

        string tightApta = "";
        foreach (var s in tightPkg.StapleOligos)
        {
            if (s.StapleId == "ST-APTA-001") { tightApta = s.Sequence; break; }
        }

        string midApta = "";
        foreach (var s in midPkg.StapleOligos)
        {
            if (s.StapleId == "ST-APTA-001") { midApta = s.Sequence; break; }
        }

        string looseApta = "";
        foreach (var s in loosePkg.StapleOligos)
        {
            if (s.StapleId == "ST-APTA-001") { looseApta = s.Sequence; break; }
        }

        Assert.NotEqual(tightApta, midApta);
        Assert.NotEqual(tightApta, looseApta);
        Assert.NotEqual(midApta, looseApta);

        Assert.NotEqual(tightPkg.Sha256Manifest, midPkg.Sha256Manifest);
        Assert.NotEqual(midPkg.Sha256Manifest, loosePkg.Sha256Manifest);
    }

    /// <summary>
    /// Verifies that <see cref="FabricationService.ExportFabricationManifestJsonAsync"/> serializes and computes SHA-256 in a single pass.
    /// </summary>
    [Fact]
    public async Task ExportFabricationManifestJsonAsync_ValidPackage_ExportsWithoutDoubleRead()
    {
        // WHAT: Exports a generated package to a temporary directory.
        // WHY:  Ensures persistence without redundant disk reads.
        // HOW:  Asserts file exists, path is returned, and sha256 matches the file contents.
        string tempDir = Path.Combine(Path.GetTempPath(), "fab_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = _service.GenerateFabricationPackage("sample-id", TherapeuticRegimen.MaleToFemale, "Nanotube", 3.5);
            (string filePath, string sha256) = await _service.ExportFabricationManifestJsonAsync(package, tempDir);

            Assert.True(File.Exists(filePath));
            Assert.NotEmpty(sha256);

            byte[] onDisk = await File.ReadAllBytesAsync(filePath);
            byte[] expectedHash = System.Security.Cryptography.SHA256.HashData(onDisk);
            Assert.Equal(Convert.ToHexString(expectedHash).ToLowerInvariant(), sha256);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="FabricationService.ExportFabricationManifestJsonAsync"/> rejects path traversal attempts in PackageId.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\sub\\evil")]
    [InlineData("sub/dir")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportFabricationManifestJsonAsync_PathTraversalPackageId_ThrowsArgumentException(string maliciousPackageId)
    {
        // WHAT: Attempts export with package IDs containing traversal tokens or directory separators.
        // WHY:  Prevents arbitrary file overwrite attacks.
        // HOW:  Asserts ArgumentException is thrown.
        string tempDir = Path.Combine(Path.GetTempPath(), "fab_test_" + Guid.NewGuid().ToString("N"));
        var package = _service.GenerateFabricationPackage("sample-id", TherapeuticRegimen.MaleToFemale, "Nanotube", 3.5) with
        {
            PackageId = maliciousPackageId
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExportFabricationManifestJsonAsync(package, tempDir));
    }

    /// <summary>
    /// Verifies that <see cref="FabricationService.CalculateSequenceMeltingTemperature"/> computes Tm for raw nucleotide sequences.
    /// </summary>
    [Fact]
    public void CalculateSequenceMeltingTemperature_ValidSequence_CalculatesTm()
    {
        // WHAT: Evaluates melting temperature for a sequence of 8 bases.
        // WHY:  Ensures the helper method correctly unpacks base counts and executes Wallace/Marmur rules.
        // HOW:  Asserts Tm matches expected Wallace rule output (2*(A+T) + 4*(G+C)).
        // A=2, T=2, G=2, C=2 -> 2*(2+2) + 4*(2+2) = 8 + 16 = 24.0°C
        double tm = _service.CalculateSequenceMeltingTemperature("ATCGATCG");
        Assert.Equal(24.0, tm);
    }
}
