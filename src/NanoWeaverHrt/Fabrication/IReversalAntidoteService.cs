using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Domain service contract designing sequence-specific Watson-Crick toehold strand-displacement reversal
/// antidotes, binding them to fabrication lots via NIST FIPS 204 ML-DSA-65, modeling 24-hour hormone cessation
/// kinetics, and enforcing pre-implantation standby verification.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Emergency nanocarrier kill-switch protocol engine for permanent reversible HRT batches.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P231): Ensures every implanted DNA origami depot carries a verified,
/// patient-matched molecular reversal oligo capable of rapid systemic hormone arrest upon clinical need.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="ReversalAntidoteService"/> combining thermodynamic oligo
/// design, canonical ML-DSA-65 attestation, atomic lot revocation on administration, and 21 CFR Part 11 audits.
/// </para>
/// </remarks>
public interface IReversalAntidoteService
{
    /// <summary>
    /// Designs a patient-specific toehold strand-displacement ssDNA antidote oligo paired to <paramref name="lotId"/>,
    /// signs the canonical binding payload with NIST FIPS 204 ML-DSA-65, persists the record, and emits an audit log.
    /// </summary>
    /// <param name="lotId">Target primary wet-lab fabrication lot identifier.</param>
    /// <param name="request">Optional oligo length, sequence, stoichiometric excess, and formulation parameters.</param>
    /// <param name="operatorId">Authenticated clinician or administrator username issuing the pairing.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The signed <see cref="ReversalAntidotePairingReport"/>, or <see langword="null"/> if <paramref name="lotId"/> does not exist.</returns>
    Task<ReversalAntidotePairingReport?> PairAntidoteAsync(
        string lotId,
        PairReversalAntidoteRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the latest paired reversal antidote report for <paramref name="lotId"/> alongside live ML-DSA-65 verification status.
    /// </summary>
    /// <param name="lotId">Target primary wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The <see cref="ReversalAntidotePairingReport"/> if both lot and antidote exist; otherwise <see langword="null"/>.</returns>
    Task<ReversalAntidotePairingReport?> GetLatestAntidoteReportAsync(
        string lotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Simulates hourly 24-hour strand-displacement nanocarrier disassembly and systemic hormone cessation kinetics.
    /// </summary>
    /// <param name="lotId">Target primary wet-lab fabrication lot identifier.</param>
    /// <param name="request">Optional rescue dose mass and kinetics time horizon.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The computed <see cref="NeutralizationSimulationReport"/>, or <see langword="null"/> if <paramref name="lotId"/> does not exist.</returns>
    Task<NeutralizationSimulationReport?> SimulateNeutralizationAsync(
        string lotId,
        SimulateNeutralizationRequest? request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes irreversible emergency antidote administration, transitions the paired antidote to <see cref="ReversalAntidoteStatus.Neutralized"/>,
    /// permanently revokes the primary lot, and records a 21 CFR Part 11 clinical neutralization event.
    /// </summary>
    /// <param name="lotId">Target primary wet-lab fabrication lot identifier.</param>
    /// <param name="request">Administered antidote bolus mass and clinical justification.</param>
    /// <param name="operatorId">Authenticated clinician username executing emergency reversal.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The <see cref="AntidoteAdministrationResponse"/>, or <see langword="null"/> if <paramref name="lotId"/> does not exist.</returns>
    Task<AntidoteAdministrationResponse?> AdministerAntidoteAsync(
        string lotId,
        AdministerAntidoteRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Independently recomputes the canonical SHA-256 digest and verifies the NIST FIPS 204 ML-DSA-65 signature
    /// binding a persisted <see cref="ReversalAntidoteRecord"/> to its primary fabrication lot.
    /// </summary>
    /// <param name="antidote">Persisted reversal antidote record to verify.</param>
    /// <returns><see langword="true"/> when SHA-256 hash, node key ID, and ML-DSA-65 signature verify cleanly; otherwise <see langword="false"/>.</returns>
    bool VerifyAntidotePairingSignature(ReversalAntidoteRecord antidote);

    /// <summary>
    /// Evaluates whether <paramref name="lotId"/> possesses an active, ML-DSA-65 verified standby reversal antidote
    /// prior to permitting patient implantation (<c>CustodyStage.PatientAdministration</c>).
    /// </summary>
    /// <param name="lotId">Target primary wet-lab fabrication lot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Tuple indicating readiness (<c>IsSatisfied</c>) and an explanatory clinical gate message.</returns>
    Task<(bool IsSatisfied, string Reason)> VerifyActivePairedAntidoteAsync(
        string lotId,
        CancellationToken cancellationToken = default);
}
