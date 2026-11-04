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
}
