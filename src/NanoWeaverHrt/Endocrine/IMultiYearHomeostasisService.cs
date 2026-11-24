using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Longitudinal systems-biology contract simulating and persisting 1-to-10-year coupled hydrogel-to-epigenetic
/// endocrine homeostasis across quiescent and proliferating tissue turnover cycles.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Domain service orchestrating multi-year durability forecasts bridging the initial
/// 180-day polymer depot (<see cref="ITimeReleaseDepotService"/>) into permanent epigenetic toggle circuits
/// (<see cref="IEpigeneticCircuitService"/>).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P226): Verifies whether Day 90–180 exogenous-to-endogenous handoff
/// avoids sub-therapeutic troughs and whether DNMT1 maintenance fidelity preserves endocrine target range over 10 years.
/// </para>
/// <para>
/// <strong>How it works:</strong> Integrates coupled stem/progenitor epigenetic memory ODEs with 4th-order Runge-Kutta
/// (RK4), saves runs/checkpoints via <see cref="NanoWeaverHrt.Storage.IMultiYearHomeostasisRepository"/>, and emits
/// FDA 21 CFR Part 11 <c>DEPOT_MULTIYEAR_SIMULATION_RUN</c> audit records.
/// </para>
/// </remarks>
public interface IMultiYearHomeostasisService
{
    /// <summary>
    /// Simulates 1-to-10-year coupled hydrogel handoff and mitotic epigenetic durability for a stored depot formulation
    /// and persists the run and annual checkpoints atomically.
    /// </summary>
    /// <param name="depotId">Parent semi-annual depot formulation identifier.</param>
    /// <param name="request">Optional cellular turnover, mitotic fidelity, and linked circuit parameters.</param>
    /// <param name="operatorId">Authenticated operator username recorded in the 21 CFR Part 11 audit ledger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Complete <see cref="MultiYearHomeostasisForecastReport"/>, or <see langword="null"/> if <paramref name="depotId"/> does not exist.</returns>
    Task<MultiYearHomeostasisForecastReport?> SimulateAndPersistAsync(
        string depotId,
        SimulateMultiYearHomeostasisRequest? request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all persisted multi-year homeostasis forecasts for a depot in newest-first order with reconstructed curves.
    /// </summary>
    /// <param name="depotId">Parent semi-annual depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest-first read-only list of <see cref="MultiYearHomeostasisForecastReport"/> items.</returns>
    Task<IReadOnlyList<MultiYearHomeostasisForecastReport>> ListForecastsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent persisted multi-year homeostasis forecast for a depot with reconstructed trajectory curves.
    /// </summary>
    /// <param name="depotId">Parent semi-annual depot formulation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest <see cref="MultiYearHomeostasisForecastReport"/>, or <see langword="null"/> if none exists.</returns>
    Task<MultiYearHomeostasisForecastReport?> GetLatestForecastByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default);
}
