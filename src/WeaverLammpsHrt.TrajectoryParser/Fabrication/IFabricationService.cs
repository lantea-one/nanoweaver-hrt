using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Fabrication;

/// <summary>
/// Service generating complete bench fabrication specifications, oligonucleotide staple sequences,
/// stoichiometric annealing protocols, and cryptographic manifests for DNA origami nanocarriers.
/// </summary>
public interface IFabricationService
{
    /// <summary>
    /// Retrieves all built-in sample digitized DNA designs available for immediate testing.
    /// </summary>
    /// <returns>Collection of pre-configured DNA origami samples.</returns>
    IReadOnlyList<DnaOrigamiSample> GetPresetSamples();

    /// <summary>
    /// Generates a complete synthesis specification and staple routing manifest for the specified parameters.
    /// </summary>
    /// <param name="sequenceOrSampleId">Raw nucleotide sequence or preset sample ID.</param>
    /// <param name="regimen">Target endocrine regimen (MtF or FtM).</param>
    /// <param name="geometry">Selected nanocarrier geometry (e.g., Nanotube, Cuboid, Cage).</param>
    /// <param name="targetKd">Target dissociation constant in nanomolar.</param>
    /// <returns>A comprehensive fabrication package ready for laboratory synthesis.</returns>
    FabricationPackage GenerateFabricationPackage(
        string sequenceOrSampleId,
        TherapeuticRegimen regimen,
        string geometry,
        double targetKd);

    /// <summary>
    /// Analyzes the base-pair composition, GC content, and molecular weight of an uploaded or input DNA sequence.
    /// </summary>
    /// <param name="sequence">Raw nucleotide sequence string.</param>
    /// <returns>Base counts, GC percentage, and molecular weight in kilodaltons.</returns>
    (int A, int T, int G, int C, double GcPercent, double EstimatedMwKDa) AnalyzeSequenceComposition(string sequence);

    /// <summary>
    /// Saves the fabrication package as a standardized JSON manifest with SHA-256 integrity verification.
    /// </summary>
    /// <param name="package">The fabrication package to export.</param>
    /// <param name="exportDirectory">Target directory for the export bundle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated manifest file path and SHA-256 hash.</returns>
    Task<(string FilePath, string Sha256Hash)> ExportFabricationManifestJsonAsync(
        FabricationPackage package,
        string exportDirectory,
        CancellationToken ct = default);
}
