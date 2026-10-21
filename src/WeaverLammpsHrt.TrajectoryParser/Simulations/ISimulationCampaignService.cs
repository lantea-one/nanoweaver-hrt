using WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations;

/// <summary>
/// Authoritative simulation orchestrator executing the 4 high-impact in-silico campaigns:
/// oxDNA serum stability, LAMMPS aptamer gating, steroid payload leakage, and whole-body PBPK modeling.
/// </summary>
public interface ISimulationCampaignService
{
    /// <summary>
    /// Retrieves metadata for all available in-silico simulation campaigns.
    /// </summary>
    /// <returns>Collection of simulation campaign metadata records.</returns>
    IReadOnlyList<SimulationCampaignInfo> GetCampaignCatalog();

    /// <summary>
    /// Executes Campaign 1: oxDNA Physiological Serum &amp; Low-Magnesium Stability Simulation.
    /// </summary>
    /// <param name="request">Input parameters specifying salt, temperature, and passivation.</param>
    /// <returns>Structural stability metrics, RMSF profile, and crossover fraying analysis.</returns>
    SerumStabilityAnalysisResult SimulateSerumStability(SerumStabilityRequest request);

    /// <summary>
    /// Executes Campaign 2: LAMMPS Aptamer Gating Free-Energy &amp; Receptor Tropism Simulation.
    /// </summary>
    /// <param name="request">Input parameters specifying target receptor and umbrella sampling windows.</param>
    /// <returns>Free-energy barriers (PMF), switching delta, and GnRH-R docking affinity.</returns>
    AptamerGatingAnalysisResult SimulateAptamerGating(AptamerGatingRequest request);

    /// <summary>
    /// Executes Campaign 3: Steroid Payload Encapsulation &amp; Micro-Leakage Dynamics.
    /// </summary>
    /// <param name="request">Input parameters specifying cavity geometry, steroid cargo, and functionalization.</param>
    /// <returns>Cargo retention percentage, passive leakage rate, and release turnover metrics.</returns>
    PayloadLeakageAnalysisResult SimulatePayloadLeakage(PayloadLeakageRequest request);

    /// <summary>
    /// Executes Campaign 4: Quantitative Systems Pharmacology (QSP) / PBPK Whole-Body Endocrine Simulation.
    /// </summary>
    /// <param name="request">Input parameters specifying subject, regimen, depot dose, and horizon.</param>
    /// <returns>Multi-day endocrine trajectory curves, steady-state setpoints, and dosing interval recommendations.</returns>
    PbpkEndocrineSimulationResult SimulatePbpkEndocrine(PbpkSimulationRequest request);

    /// <summary>
    /// Generates a customized oxDNA input deck configuration string for Campaign 1.
    /// </summary>
    /// <param name="request">Serum stability parameters.</param>
    /// <returns>Formatted oxDNA input file content.</returns>
    string GenerateOxDnaInputDeck(SerumStabilityRequest request);

    /// <summary>
    /// Generates a customized LAMMPS input script string for Campaign 2.
    /// </summary>
    /// <param name="request">Aptamer gating parameters.</param>
    /// <returns>Formatted LAMMPS input file content.</returns>
    string GenerateLammpsAptamerInputDeck(AptamerGatingRequest request);

    /// <summary>
    /// Generates a customized LAMMPS input script string for Campaign 3.
    /// </summary>
    /// <param name="request">Payload leakage parameters.</param>
    /// <returns>Formatted LAMMPS input file content.</returns>
    string GenerateLammpsPayloadInputDeck(PayloadLeakageRequest request);
}
