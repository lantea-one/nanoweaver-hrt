using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Domain service contract for evaluating 5-gate pharmaceutical wet-lab quality control assays,
/// issuing NIST FIPS 204 ML-DSA-65 signed Certificates of Analysis, and verifying batch provenance.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The wet-lab pharmaceutical release engine governing physical DNA origami
/// nanocarrier batches prior to subcutaneous depot implantation.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enforces strict, fail-closed biochemical safety thresholds (gel folding yield,
/// USP &lt;85&gt; endotoxin sterility, DLS hydrodynamic size/PDI, HPLC encapsulation, and CpG methylation) before
/// minting a post-quantum digital signature attesting that a lot is safe for clinical use.
/// </para>
/// <para>
/// <strong>How it works:</strong> Evaluates raw assay measurements deterministically without LINQ allocations,
/// builds canonical UTF-8 JSON certificate payloads, signs via <c>IKeyManagementService</c>, and verifies signatures
/// via <c>IPostQuantumCryptoProvider</c>.
/// </para>
/// </remarks>
public interface IFabricationLotService
{
    /// <summary>
    /// Evaluates a single pharmaceutical wet-lab quality control measurement against its normative release threshold.
    /// </summary>
    /// <param name="gateType">The pharmaceutical wet-lab QC gate being evaluated.</param>
    /// <param name="measuredValue">Primary measured laboratory value in standard units.</param>
    /// <param name="secondaryValue">Optional secondary measurement (DLS PDI or gravimetric mass deviation %).</param>
    /// <returns>A deterministic <see cref="FabricationQcGateEvaluation"/> describing pass/fail status and threshold details.</returns>
    FabricationQcGateEvaluation EvaluateSingleGate(
        FabricationQcGateType gateType,
        double measuredValue,
        double? secondaryValue = null);

    /// <summary>
    /// Evaluates all required wet-lab QC gates for a fabrication lot and signs an ML-DSA-65 Certificate of Analysis
    /// if and only if every required gate is present and passes.
    /// </summary>
    /// <param name="lot">The persisted fabrication lot specification.</param>
    /// <param name="checks">All wet-lab QC check records logged against the lot.</param>
    /// <returns>
    /// A <see cref="FabricationLotReleaseEvaluation"/> with <see cref="FabricationLotStatus.Released"/> and a signed
    /// <see cref="FabricationLotCertificate"/> on complete pass, or <see cref="FabricationLotStatus.Quarantined"/>
    /// with failure reasons and a <see langword="null"/> certificate if any required gate is missing or failed.
    /// </returns>
    FabricationLotReleaseEvaluation EvaluateAndSignLotRelease(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks);

    /// <summary>
    /// Reconstructs the <see cref="FabricationLotCertificate"/> from a persisted released lot and its recorded QC checks.
    /// </summary>
    /// <param name="lot">The persisted fabrication lot record.</param>
    /// <param name="checks">All wet-lab QC checks associated with the lot.</param>
    /// <returns>
    /// The reconstructed <see cref="FabricationLotCertificate"/> if the lot is released and carries signature metadata;
    /// otherwise, <see langword="null"/>.
    /// </returns>
    FabricationLotCertificate? ReconstructCertificate(
        FabricationLotRecord lot,
        IReadOnlyList<FabricationQcCheckRecord> checks);

    /// <summary>
    /// Cryptographically verifies a <see cref="FabricationLotCertificate"/> by recomputing its canonical SHA-256 digest
    /// and validating its NIST FIPS 204 ML-DSA-65 digital signature.
    /// </summary>
    /// <param name="certificate">The Certificate of Analysis to verify.</param>
    /// <returns><see langword="true"/> if both the canonical SHA-256 digest and ML-DSA-65 signature are valid; otherwise, <see langword="false"/>.</returns>
    bool VerifyLotCertificate(FabricationLotCertificate certificate);

    /// <summary>
    /// Computes a deterministic SHA-256 hex digest representing the Opentrons liquid-handler protocol specification
    /// for a given patient depot batch.
    /// </summary>
    /// <param name="depotId">Parent depot formulation identifier.</param>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="regimen">Therapeutic hormone replacement regimen.</param>
    /// <param name="polymerMatrixType">Biocompatible polymer matrix composition.</param>
    /// <param name="targetMassMg">Target nanocarrier payload mass in milligrams.</param>
    /// <param name="targetErosionRatePercent">Target daily erosion rate in percent per day.</param>
    /// <returns>Lowercase 64-character SHA-256 hexadecimal digest.</returns>
    string ComputeOpentronsProtocolSha256(
        string depotId,
        string patientId,
        TherapeuticRegimen regimen,
        string polymerMatrixType,
        double targetMassMg,
        double targetErosionRatePercent);

    /// <summary>
    /// Evaluates a Certificate of Analysis against mathematical ML-DSA-65 integrity, local or peer key trust anchors,
    /// and any permanent lot revocation tombstone.
    /// </summary>
    /// <param name="certificate">The signed Certificate of Analysis to verify.</param>
    /// <param name="peerKey">Optional matching peer key entry resolved from <c>IPeerKeyRegistry</c> (active or revoked).</param>
    /// <param name="revocation">Optional lot revocation record resolved from <c>IFabricationLotRepository</c>.</param>
    /// <returns>A complete <see cref="FabricationCertificateVerificationReport"/> classifying trust and revocation status.</returns>
    FabricationCertificateVerificationReport VerifyCertificateAgainstTrustAnchor(
        FabricationLotCertificate certificate,
        PeerKeyRecord? peerKey,
        FabricationLotRevocationRecord? revocation);
}
