namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Operational qualification readiness rating for physical hardware accelerators executing DNA origami and endocrine simulations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Hardware qualification categorizes a GPU or accelerator into three readiness tiers:
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Go</term>
/// <description>The accelerator surpasses high-throughput production thresholds (FP32 &gt;= 5,000 GFLOPS and memory bandwidth &gt;= 150 GB/s) and can run full oxDNA 2.4 and LAMMPS trajectories.</description>
/// </item>
/// <item>
/// <term>Marginal</term>
/// <description>The accelerator meets minimum computational or bandwidth limits (FP32 &gt;= 1,500 GFLOPS or bandwidth &gt;= 50 GB/s) suitable for low-atom kinetic runs, but may bottleneck large origami folding.</description>
/// </item>
/// <item>
/// <term>Insufficient</term>
/// <description>The hardware fails minimum performance thresholds and is unsuited for production molecular dynamics simulations.</description>
/// </item>
/// </list>
/// </remarks>
public enum HardwareReadinessScore
{
    /// <summary>
    /// Hardware accelerator meets or exceeds all performance thresholds for production simulation campaigns.
    /// </summary>
    Go = 0,

    /// <summary>
    /// Hardware accelerator provides baseline performance suitable for small kinetic models, but may bottleneck large assemblies.
    /// </summary>
    Marginal = 1,

    /// <summary>
    /// Hardware accelerator fails minimum compute or memory bandwidth requirements for molecular dynamics workloads.
    /// </summary>
    Insufficient = 2
}
