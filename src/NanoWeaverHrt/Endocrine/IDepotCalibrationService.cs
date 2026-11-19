namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Domain service contract for longitudinal Bayesian calibration and adaptive re-dosing of active
/// 180-day subcutaneous time-release hormone depots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Compares a patient's real-world blood test results against the
/// theoretical 180-day depot release curve to infer how fast their body actually clears the hormone and how
/// quickly the subcutaneous hydrogel matrix erodes. Produces an updated trajectory, clinical safety alerts,
/// an adjusted re-dosing day, and next-cycle formulation recommendations.
/// </para>
/// </remarks>
public interface IDepotCalibrationService
{
    /// <summary>
    /// Assimilates one or more longitudinal serum assay observations into the depot pharmacokinetic model
    /// via deterministic regularized weighted least squares and computes adaptive re-dosing guidance.
    /// </summary>
    /// <param name="request">The calibration request bundling baseline depot parameters and longitudinal serum assays.</param>
    /// <returns>A <see cref="DepotCalibrationResult"/> containing inferred kinetics, safety alerts, and the recalibrated trajectory.</returns>
    DepotCalibrationResult CalibrateDepot(DepotCalibrationRequest request);

    /// <summary>
    /// Evaluates clinical endocrine and hematology safety guardrails across a collection of serum assay observations.
    /// </summary>
    /// <param name="regimen">The patient's active therapeutic endocrine regimen.</param>
    /// <param name="observations">The longitudinal serum assay observations to evaluate.</param>
    /// <returns>A read-only list of triggered <see cref="DepotSafetyAlert"/> findings (empty if all biomarkers are optimal).</returns>
    IReadOnlyList<DepotSafetyAlert> EvaluateSafetyGuardrails(
        TherapeuticRegimen regimen,
        IReadOnlyList<DepotAssayObservation> observations);
}
