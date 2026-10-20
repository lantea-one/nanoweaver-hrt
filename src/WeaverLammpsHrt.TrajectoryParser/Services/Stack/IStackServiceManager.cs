namespace WeaverLammpsHrt.TrajectoryParser.Services.Stack;

/// <summary>
/// Provides unified diagnostic monitoring, control, and management over all core services in the sovereign stack:
/// molecular simulation engine, columnar storage node, SIMD trajectory parser, and collaborative peers.
/// </summary>
public interface IStackServiceManager
{
    /// <summary>
    /// Inspects and retrieves real-time operational status for all services in the stack.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of service telemetry records.</returns>
    Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves diagnostic health for a specific service by its identifier.
    /// </summary>
    /// <param name="serviceId">The service identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The service information, or null if unknown.</returns>
    Task<StackServiceInfo?> GetServiceByIdAsync(string serviceId, CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity and responds with an updated status for the specified service.
    /// </summary>
    /// <param name="serviceId">The service identifier to ping or test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated service status info.</returns>
    Task<StackServiceInfo> PingServiceAsync(string serviceId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves simulation configuration files discovered on disk for the oxDNA/LAMMPS node.
    /// </summary>
    /// <returns>List of config file paths and names.</returns>
    IReadOnlyList<string> GetDiscoveredSimulationConfigs();
}
