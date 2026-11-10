namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Computational service simulating 180-day semi-annual time-release depot kinetics coupled with HPG axis feedback.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Simulates how a subcutaneous polymer depot delivers hormone micro-doses
/// continuously across a six-month (180-day) therapeutic window. Models three distinct phases (burst suppression,
/// steady-state plateau, and terminal taper) while calculating pituitary gonadotropin suppression (LH &lt; 1.0 IU/L,
/// FSH &lt; 1.5 IU/L) and time in target range (TTR &gt;= 95%).
/// </para>
/// </remarks>
public interface ITimeReleaseDepotService
{
    /// <summary>
    /// Simulates the complete 180-day time-release depot trajectory and coupled endocrine response.
    /// </summary>
    /// <param name="parameters">The depot formulation parameters including matrix, regimen, and pharmacogenomic scaling.</param>
    /// <returns>A <see cref="TimeReleaseSimulationResult"/> containing daily trajectory points, burst fraction, and clinical metrics.</returns>
    TimeReleaseSimulationResult Simulate180DayDepot(TimeReleaseDepotParameters parameters);

    /// <summary>
    /// Calculates the instantaneous daily payload release rate in milligrams per day for a given elapsed day.
    /// </summary>
    /// <param name="day">The elapsed simulation time in days.</param>
    /// <param name="matrix">The polymer matrix carrier type.</param>
    /// <param name="initialMassMg">The initial payload mass loaded into the depot in milligrams.</param>
    /// <param name="clearanceMultiplier">The patient-specific pharmacogenomic clearance multiplier.</param>
    /// <returns>The calculated instantaneous daily release rate in milligrams per day (mg/day).</returns>
    double CalculateInstantaneousDailyRelease(double day, PolymerMatrixType matrix, double initialMassMg, double clearanceMultiplier);
}
