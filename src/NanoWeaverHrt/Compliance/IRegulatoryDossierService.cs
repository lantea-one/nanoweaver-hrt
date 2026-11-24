using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Domain service contract compiling de-identified FDA IND / EMA IMPD electronic submission dossiers,
/// enforcing 10 fail-closed CMC/QSP/Compliance gates, sealing canonical manifests with ML-DSA-65,
/// encrypting archives at rest via AES-256-GCM, and verifying portable cross-node dossier bundles.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Multi-center regulatory dossier compiler and cryptographic verifier for permanent HRT trials.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P236): Produces reproducible, HIPAA-pseudonymized, post-quantum
/// signed IND/IMPD evidence bundles spanning scaffold design through 10-year homeostasis and reversal antidotes.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="RegulatoryDossierService"/> using zero-LINQ aggregation,
/// canonical JSON SHA-256 digests, ML-DSA-65 signatures, AES-256-GCM storage encryption, and 21 CFR Part 11 audit events.
/// </para>
/// </remarks>
public interface IRegulatoryDossierService
{
    /// <summary>
    /// Compiles, evaluates 10 fail-closed gates, signs with NIST FIPS 204 ML-DSA-65, encrypts at rest, persists,
    /// and audits an FDA IND or EMA IMPD regulatory dossier bundle for <paramref name="patientId"/>.
    /// </summary>
    /// <param name="patientId">Patient identifier whose clinical and fabrication evidence is aggregated.</param>
    /// <param name="request">Optional submission framework, lot/depot/circuit bindings, sponsor, and site code.</param>
    /// <param name="operatorId">Authenticated administrator username initiating compilation.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The canonically hashed and ML-DSA-65 signed <see cref="SignedRegulatoryDossierBundle"/>.</returns>
    Task<SignedRegulatoryDossierBundle> CompileDossierAsync(
        string patientId,
        CompileRegulatoryDossierRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists compiled regulatory dossier summaries belonging to <paramref name="patientId"/> ordered newest first,
    /// redacting operator and node key metadata when <paramref name="redactForPatient"/> is enabled.
    /// </summary>
    /// <param name="patientId">Patient identifier whose archived dossiers are listed.</param>
    /// <param name="redactForPatient">When <see langword="true"/>, replaces operator username and signer key ID with <c>[REDACTED]</c>.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Newest-first read-only list of <see cref="RegulatoryDossierSummary"/> projections.</returns>
    Task<IReadOnlyList<RegulatoryDossierSummary>> ListDossiersForPatientAsync(
        string patientId,
        bool redactForPatient = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and AES-256-GCM decrypts an archived <see cref="SignedRegulatoryDossierBundle"/> by dossier identifier.
    /// </summary>
    /// <param name="dossierId">Unique regulatory dossier snapshot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The decrypted <see cref="SignedRegulatoryDossierBundle"/>, or <see langword="null"/> when absent.</returns>
    Task<SignedRegulatoryDossierBundle?> GetDecryptedBundleAsync(
        string dossierId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cryptographically verifies a portable <see cref="SignedRegulatoryDossierBundle"/> against canonical SHA-256,
    /// ML-DSA-65 signature validity, and local or trusted peer laboratory key anchors, logging an audit event.
    /// </summary>
    /// <param name="bundle">Portable signed regulatory dossier bundle to verify.</param>
    /// <param name="operatorId">Authenticated operator username initiating cross-node verification.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Fail-closed <see cref="RegulatoryDossierVerificationReport"/> diagnosing hash, signature, and trust status.</returns>
    Task<RegulatoryDossierVerificationReport> VerifyDossierBundleAsync(
        SignedRegulatoryDossierBundle bundle,
        string operatorId,
        CancellationToken cancellationToken = default);
}
