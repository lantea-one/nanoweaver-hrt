using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying deterministic alert generation, multi-record FASTA boundary isolation,
/// regimen relevance gating, and genetic dose adjustments in the genomics subsystem.
/// </summary>
public sealed class GenomicsTests
{
    private readonly PharmacogenomicsService _service = new();

    /// <summary>
    /// Verifies that <see cref="FastaSequenceScanner.Scan"/> accurately counts nucleotides,
    /// CpG dinucleotides, and CAG repeat runs on standard FASTA input.
    /// </summary>
    [Fact]
    public void Scan_StandardFastaSequence_AccuratelyMeasuresMetrics()
    {
        // WHAT: Scans a standard FASTA sequence with headers, CpG motifs, and CAG triplets.
        // WHY:  Ensures that valid bases are counted and motifs correctly quantified.
        // HOW:  Provides FASTA text with known metrics and asserts output parameters.
        string fasta = ">record1\nACGT\n>record2\nCAGCAGCAGCAG\n";
        byte[] bytes = Encoding.UTF8.GetBytes(fasta);

        FastaSequenceScanner.Scan(bytes, out int count, out int cpgCount, out int cagRun);

        Assert.Equal(16, count);
        Assert.Equal(1, cpgCount);
        Assert.Equal(4, cagRun);
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.AnalyzeProfile"/> generates 100% deterministic
    /// alert identifiers using SHA-256 rather than non-deterministic random GUIDs.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_AlertIds_AreDeterministicAcrossInvocations()
    {
        // WHAT: Calls AnalyzeProfile twice with identical parameters.
        // WHY:  Deterministic alert IDs are mandatory for persistent clinical auditing and cache correlation.
        // HOW:  Asserts that alert IDs from both calls match character-for-character.
        string patientId = "patient-deterministic-01";
        List<string> markers = ["F5_LEIDEN", "AR_CAG_LONG"];
        TherapeuticRegimen regimen = TherapeuticRegimen.MaleToFemale;

        var profile1 = _service.AnalyzeProfile(patientId, markers, regimen);
        var profile2 = _service.AnalyzeProfile(patientId, markers, regimen);

        Assert.Equal(profile1.Alerts.Count, profile2.Alerts.Count);
        for (int i = 0; i < profile1.Alerts.Count; i++)
        {
            Assert.Equal(profile1.Alerts[i].AlertId, profile2.Alerts[i].AlertId);
            Assert.StartsWith("ALERT_", profile1.Alerts[i].AlertId);
        }
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.AnalyzeProfile"/> only applies
    /// estradiol and testosterone threshold limits when the marker is relevant to the target regimen.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_RegimenRelevanceGating_OnlyAppliesRelevantLimits()
    {
        // WHAT: Evaluates an FtM subject carrying F5_LEIDEN (an MtF-associated thrombotic marker with MaxSafeEstradiolPmolL = 450.0).
        // WHY:  Estradiol caps must not constrain an FtM profile where estrogen is already suppressed.
        // HOW:  Asserts RecommendedMaxEstradiolPmolL is null when evaluated under FemaleToMale regimen.
        string patientId = "patient-ftm-carrier";
        List<string> markers = ["F5_LEIDEN"];

        var ftmProfile = _service.AnalyzeProfile(patientId, markers, TherapeuticRegimen.FemaleToMale);
        Assert.Null(ftmProfile.RecommendedMaxEstradiolPmolL);

        // When evaluated under MaleToFemale regimen, F5_LEIDEN estradiol limit must be actively applied
        var mtfProfile = _service.AnalyzeProfile(patientId, markers, TherapeuticRegimen.MaleToFemale);
        Assert.NotNull(mtfProfile.RecommendedMaxEstradiolPmolL);
        Assert.Equal(500.0, mtfProfile.RecommendedMaxEstradiolPmolL);
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.ApplyGeneticAdjustments"/> clamps testosterone targets
    /// to <see cref="PatientGenomicProfile.RecommendedMaxTestosteroneNmolL"/> when <c>AR_CAG_SHORT</c> is present in FtM.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_ArCagShort_ClampsTestosteroneTarget()
    {
        // WHAT: Tests AR_CAG_SHORT hypersensitivity marker which recommends capping testosterone <= 18.0 nmol/L.
        // WHY:  Prevents hyperandrogenic adverse effects in receptor-hypersensitive FtM patients.
        // HOW:  Applies adjustments to a base profile with target T = 22.0 nmol/L and verifies it is clamped to 18.0.
        string patientId = "patient-cag-short";
        var genomicProfile = _service.AnalyzeProfile(patientId, ["AR_CAG_SHORT"], TherapeuticRegimen.FemaleToMale);

        Assert.Equal(18.0, genomicProfile.RecommendedMaxTestosteroneNmolL);

        EndocrineTargetProfile baseProfile = EndocrineStateEngine.CreateFtmPreset(patientId) with
        {
            TargetTestosteroneNmolL = 22.0
        };

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, genomicProfile);

        Assert.Equal(18.0, adjusted.TargetTestosteroneNmolL);

        // Also test duplicate marker to cover the compound Math.Min branch
        var duplicateProfile = _service.AnalyzeProfile(patientId, ["AR_CAG_SHORT", "AR_CAG_SHORT"], TherapeuticRegimen.FemaleToMale);
        Assert.Equal(18.0, duplicateProfile.RecommendedMaxTestosteroneNmolL);
    }
}
