namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Computational engine for simulating dynamic closed-loop endocrine feedback and pituitary gonadotropin suppression.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Simulates how autonomous, aptamer-gated DNA origami nanocarriers interact with
/// the human Hypothalamic-Pituitary-Gonadal (HPG) axis. As circulating hormones deviate from clinical targets, the nanocarriers
/// modulate payload micro-release while reciprocal negative feedback suppresses endogenous pituitary gonadotropins (LH and FSH).
/// </para>
/// </remarks>
public interface IEndocrineClosedLoopEngine
{
    /// <summary>
    /// Executes a dynamic closed-loop simulation using 4th-order Runge-Kutta numerical integration.
    /// </summary>
    /// <param name="request">The simulation parameters including baseline concentrations, targets, and time horizon.</param>
    /// <returns>A <see cref="ClosedLoopSimulationResult"/> containing discrete trajectory points, final values, and suppression metrics.</returns>
    ClosedLoopSimulationResult Simulate(ClosedLoopSimulationRequest request);
}
