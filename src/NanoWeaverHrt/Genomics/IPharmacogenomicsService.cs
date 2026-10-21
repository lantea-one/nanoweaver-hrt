using NanoWeaverHrt.Endocrine;

namespace NanoWeaverHrt.Genomics;

/// <summary>
/// Service contract for evaluating patient genetic markers, calculating pharmacogenomic kinetic modifiers,
/// generating clinical call-out alerts, and adjusting DNA origami chassis parameters.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This service acts as the genetic brain of the platform.
/// It receives genetic test results or DNA sequence data, checks if the patient has any known mutations that 
/// alter how they process estrogen, testosterone, or DNA nanocarriers, and outputs actionable alerts and 
/// dosage/chassis recommendations.
/// </para>
/// </remarks>
public interface IPharmacogenomicsService
{
    /// <summary>
    /// Retrieves the comprehensive catalog of known pharmacogenomic and hemostatic markers.
    /// </summary>
    /// <returns>A read-only collection of all available <see cref="GeneticMarker"/> definitions.</returns>
    IReadOnlyList<GeneticMarker> GetCatalog();

    /// <summary>
    /// Retrieves standard clinical genetic preset panels for immediate simulation and testing.
    /// </summary>
    /// <returns>A read-only collection of <see cref="GeneticMarkerPreset"/> definitions.</returns>
    IReadOnlyList<GeneticMarkerPreset> GetPresetProfiles();

    /// <summary>
    /// Evaluates a list of observed marker identifiers for a patient under the active HRT regimen.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <param name="markerIds">Collection of detected marker IDs.</param>
    /// <param name="regimen">Active therapeutic regimen (MtF or FtM).</param>
    /// <returns>A complete <see cref="PatientGenomicProfile"/> containing synthesized alerts and kinetic modifiers.</returns>
    PatientGenomicProfile AnalyzeProfile(string patientId, IEnumerable<string> markerIds, TherapeuticRegimen regimen);

    /// <summary>
    /// Analyzes a raw FASTA or DNA sequence to compute CpG motif density (TLR9 immunogenicity) and trinucleotide repeat lengths (AR CAG).
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <param name="fastaOrDnaSequence">The raw nucleotide sequence string.</param>
    /// <param name="regimen">Active therapeutic regimen.</param>
    /// <returns>A generated <see cref="PatientGenomicProfile"/> with sequence-derived alerts.</returns>
    PatientGenomicProfile AnalyzeSequence(string patientId, string fastaOrDnaSequence, TherapeuticRegimen regimen);

    /// <summary>
    /// Applies calculated pharmacogenomic modifiers to an existing endocrine target profile, producing a genetically-safe adjusted profile.
    /// </summary>
    /// <param name="baseProfile">The baseline physiological profile.</param>
    /// <param name="genomicProfile">The patient's evaluated pharmacogenomic profile.</param>
    /// <returns>A new <see cref="EndocrineTargetProfile"/> customized to the patient's genetic predispositions.</returns>
    EndocrineTargetProfile ApplyGeneticAdjustments(EndocrineTargetProfile baseProfile, PatientGenomicProfile genomicProfile);
}
