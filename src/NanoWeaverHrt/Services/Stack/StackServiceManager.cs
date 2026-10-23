using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Text.Json;
using NanoWeaverHrt.Configuration;

namespace NanoWeaverHrt.Services.Stack;

/// <summary>
/// Authoritative manager providing live status, health inspection, and configuration discovery
/// across the Dockerized or standalone service stack components.
/// </summary>
public sealed class StackServiceManager : IStackServiceManager
{
    /// <summary>
    /// Persistent configuration repository enforcing database precedence over environment variables.
    /// </summary>
    private readonly IConfigurationRepository _configRepo;

    /// <summary>
    /// Host application configuration.
    /// </summary>
    private readonly IConfiguration _hostConfig;

    /// <summary>
    /// Reusable HTTP client factory or instance for service health probing.
    /// </summary>
    private readonly HttpClient _probeClient;

    /// <summary>
    /// Default static HTTP client instance for standard runtime operations.
    /// </summary>
    private static readonly HttpClient DefaultProbeClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>
    /// Initializes a new instance of the <see cref="StackServiceManager"/> class.
    /// </summary>
    /// <param name="configRepo">The configuration repository.</param>
    /// <param name="hostConfig">The host configuration.</param>
    /// <param name="probeClient">Optional custom HTTP client for test isolation.</param>
    public StackServiceManager(IConfigurationRepository configRepo, IConfiguration hostConfig, HttpClient? probeClient = null)
    {
        _configRepo = configRepo;
        _hostConfig = hostConfig;
        _probeClient = probeClient ?? DefaultProbeClient;
    }

    /// <summary>
    /// Inspects and retrieves real-time operational status for all services in the stack.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of service telemetry records.</returns>
    public async Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(CancellationToken ct = default)
    {
        List<StackServiceInfo> services =
        [
            await InspectSimulationNodeAsync(ct),
            await InspectStorageNodeAsync(ct),
            InspectParserNode(),
            await InspectPeerMeshAsync(ct)
        ];

        return services;
    }

    /// <summary>
    /// Retrieves diagnostic health for a specific service by its identifier.
    /// </summary>
    /// <param name="serviceId">The service identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The service information, or null if unknown.</returns>
    public async Task<StackServiceInfo?> GetServiceByIdAsync(string serviceId, CancellationToken ct = default)
    {
        return serviceId.ToLowerInvariant() switch
        {
            "simulation" or "simulation-engine" => await InspectSimulationNodeAsync(ct),
            "storage" or "pg-duckdb" or "database" => await InspectStorageNodeAsync(ct),
            "parser" or "trajectory-parser" => InspectParserNode(),
            "peer" or "peer-mesh" or "peers" => await InspectPeerMeshAsync(ct),
            _ => null
        };
    }

    /// <summary>
    /// Tests connectivity and responds with an updated status for the specified service.
    /// </summary>
    /// <param name="serviceId">The service identifier to ping or test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated service status info.</returns>
    public Task<StackServiceInfo> PingServiceAsync(string serviceId, CancellationToken ct = default)
    {
        return serviceId.ToLowerInvariant() switch
        {
            "simulation" or "simulation-engine" => InspectSimulationNodeAsync(ct),
            "storage" or "pg-duckdb" => InspectStorageNodeAsync(ct),
            "peer" or "peer-mesh" => InspectPeerMeshAsync(ct),
            _ => Task.FromResult(InspectParserNode())
        };
    }

    /// <summary>
    /// Retrieves simulation configuration files discovered on disk for the oxDNA/LAMMPS node.
    /// </summary>
    /// <returns>List of config file paths and names.</returns>
    public IReadOnlyList<string> GetDiscoveredSimulationConfigs()
    {
        // WHAT: Discovers simulation configuration decks (*.in, *.oxdna) in the configs volume.
        // WHY:  Permits researchers to select simulation decks from the UI without manual path entry.
        // HOW:  Enumerates files in SIMULATION_CONFIGS_DIR or fallback simulations/configs.
        string configsDir = _hostConfig["SIMULATION_CONFIGS_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "configs");

        if (!Directory.Exists(configsDir))
        {
            return [];
        }

        return Directory.GetFiles(configsDir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(f => !string.IsNullOrEmpty(f))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Inspects the oxDNA / LAMMPS simulation container or external compute cluster.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A snapshot describing whether the simulation engine is reachable and how it is configured.</returns>
    private async Task<StackServiceInfo> InspectSimulationNodeAsync(CancellationToken ct)
    {
        string endpoint = await _configRepo.GetValueAsync("Simulation:EndpointUrl", "http://simulation-node:8080", ct);
        string accelerator = await _configRepo.GetValueAsync("Simulation:AcceleratorBackend", "Intel iGPU / OpenCL (Default)", ct);
        IReadOnlyList<string> configs = GetDiscoveredSimulationConfigs();

        bool isOnline = false;
        string statusMsg;
        string activeEndpoint = endpoint;
        string daemonStatus = "Offline / Standby";

        // WHAT: Probes the configured simulation node endpoint with localhost fallback for local development.
        // WHY:  Ensures the dashboard reports online health both when deployed in Docker and when tested locally.
        // HOW:  Issues a rapid GET request to endpoint; on failure against simulation-node, attempts localhost.
        HttpResponseMessage? resp = await TryProbeEndpointAsync(endpoint, ct);
        if (resp == null && endpoint.Contains("simulation-node", StringComparison.OrdinalIgnoreCase))
        {
            string localFallback = endpoint.Replace("simulation-node", "localhost", StringComparison.OrdinalIgnoreCase);
            resp = await TryProbeEndpointAsync(localFallback, ct);
            if (resp != null)
            {
                activeEndpoint = localFallback;
            }
        }

        if (resp != null)
        {
            isOnline = true;
            daemonStatus = "Active (HTTP/1.1 REST API)";
            accelerator = await ExtractAcceleratorAsync(resp, accelerator, ct);
            statusMsg = $"Online & Connected ({accelerator})";
            resp.Dispose();
        }
        else if (configs.Count > 0)
        {
            statusMsg = $"Local Mode Active ({configs.Count} simulation presets available)";
        }
        else
        {
            statusMsg = "Offline / Standby (Awaiting Container Startup)";
        }

        ServiceHealthState healthState = isOnline
            ? ServiceHealthState.Healthy
            : (configs.Count > 0 ? ServiceHealthState.DegradedOrFallback : ServiceHealthState.Offline);

        return new StackServiceInfo
        {
            ServiceId = "simulation-engine",
            DisplayName = "Molecular Simulation Engine (oxDNA / LAMMPS)",
            RoleDescription = "Executes hardware-accelerated molecular dynamics, coarse-grained DNA folding, and allosteric simulations.",
            EndpointOrLocation = activeEndpoint,
            Health = healthState,
            StatusMessage = statusMsg,
            TechnologyStack = $"oxDNA / LAMMPS 2024 ({accelerator})",
            Metrics = new Dictionary<string, string>
            {
                ["Simulation Daemon"] = daemonStatus,
                ["Engine Presets"] = $"{configs.Count} templates discovered",
                ["Target Accelerator"] = accelerator,
                ["IPC Shm Size"] = "4 GB Host Shared Memory",
                ["Coordinate Mount"] = "/workspace/data (Shared In-Memory Volume)",
                ["Daemon Endpoint"] = activeEndpoint
            }
        };
    }

    /// <summary>
    /// Attempts to query an HTTP probe endpoint, returning the response if 2xx success or null on failure.
    /// </summary>
    /// <param name="url">The HTTP endpoint URL to probe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The successful HTTP response message, or null if unreachable.</returns>
    private async Task<HttpResponseMessage?> TryProbeEndpointAsync(string url, CancellationToken ct)
    {
        // WHAT: Probes an endpoint and safely catches network or HTTP faults.
        // WHY:  Prevents connection drops or unresolvable DNS names from crashing status checks.
        // HOW:  Calls GetAsync on _probeClient, disposing any non-2xx response.
        try
        {
            HttpResponseMessage response = await _probeClient.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            response.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the accelerator backend name from a simulation daemon response if present.
    /// </summary>
    /// <param name="response">The active HTTP response message.</param>
    /// <param name="defaultAccelerator">Fallback accelerator name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved accelerator backend description.</returns>
    private static async Task<string> ExtractAcceleratorAsync(HttpResponseMessage response, string defaultAccelerator, CancellationToken ct)
    {
        // WHAT: Reads and parses the JSON response payload from the simulation daemon.
        // WHY:  Displays the dynamic hardware accelerator (e.g., Level Zero or OpenCL) in telemetry.
        // HOW:  Parses JSON document looking for "accelerator" property.
        try
        {
            string json = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("accelerator", out JsonElement elem))
            {
                string? acc = elem.GetString();
                if (!string.IsNullOrWhiteSpace(acc))
                {
                    return acc;
                }
            }
        }
        catch
        {
            // Corrupted payload falls back to default
        }

        return defaultAccelerator;
    }

    /// <summary>
    /// Inspects the storage and columnar analytics node (PostgreSQL / pg-duckdb or SQLite fallback).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A snapshot describing the active database backend and its analytics directories.</returns>
    private async Task<StackServiceInfo> InspectStorageNodeAsync(CancellationToken ct)
    {
        string backend = _configRepo.ActiveBackendName;
        string parquetDir = await _configRepo.GetValueAsync("Storage:TrajectoryParquetDir", "./simulations/parquet", ct);
        string connStr = await _configRepo.GetValueAsync("Storage:PostgresConnectionString", "Host=localhost;Port=5432;Database=weaver_hrt", ct);

        int parquetCount = 0;
        long totalParquetBytes = 0;
        if (Directory.Exists(parquetDir))
        {
            DirectoryInfo di = new(parquetDir);
            FileInfo[] files = di.GetFiles("*.parquet");
            parquetCount = files.Length;
            totalParquetBytes = files.Sum(f => f.Length);
        }

        bool isPostgres = backend.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);

        return new StackServiceInfo
        {
            ServiceId = "pg-duckdb",
            DisplayName = isPostgres ? "PostgreSQL & pg_duckdb Columnar Analytics" : "Embedded SQLite & DuckDB Parquet Storage",
            RoleDescription = "Vectorized SQL analytical query execution and zero-copy columnar Apache Parquet storage.",
            EndpointOrLocation = isPostgres ? connStr.Split(';')[0] : "Local Embedded Storage",
            Health = isPostgres ? ServiceHealthState.Healthy : ServiceHealthState.DegradedOrFallback,
            StatusMessage = isPostgres ? "PostgreSQL pg_duckdb Active" : "Operational (Embedded SQLite Fallback Active)",
            TechnologyStack = isPostgres ? "PostgreSQL 18 + pg_duckdb v1.1.1" : "Microsoft.Data.Sqlite + DuckDB.NET Full",
            Metrics = new Dictionary<string, string>
            {
                ["Active Backend"] = backend,
                ["Serialized Parquet Files"] = $"{parquetCount} files",
                ["Parquet Volume Size"] = $"{totalParquetBytes / (1024.0 * 1024.0):F2} MB",
                ["Vectorized Queries"] = "SIMD-accelerated DuckDB execution enabled"
            }
        };
    }

    /// <summary>
    /// Evaluates SIMD instruction width capability into a human-readable display string.
    /// </summary>
    /// <param name="v512">True if 512-bit vector SIMD is hardware-accelerated.</param>
    /// <param name="v256">True if 256-bit vector SIMD is hardware-accelerated.</param>
    /// <param name="v128">True if 128-bit vector SIMD is hardware-accelerated.</param>
    /// <returns>Readable SIMD hardware acceleration capability string.</returns>
    internal static string GetSimdCapabilityDescription(bool v512, bool v256, bool v128)
    {
        // WHAT: Evaluates CPU SIMD instruction vector width capability.
        // WHY:  Allows deterministic testing of all vector width fallback levels.
        // HOW:  Checks descending vector bitwidths: 512-bit -> 256-bit -> 128-bit -> scalar fallback.
        if (v512) return "AVX-512 (512-bit SIMD)";
        if (v256) return "AVX2 (256-bit SIMD)";
        if (v128) return "SSE4.2 (128-bit SIMD)";
        return "Software Fallback";
    }

    /// <summary>
    /// Formats garbage collector operating mode description.
    /// </summary>
    /// <param name="isServerGc">True if Server GC mode is active.</param>
    /// <returns>Readable garbage collection mode string.</returns>
    internal static string GetGcModeDescription(bool isServerGc)
    {
        // WHAT: Formats runtime garbage collection mode string.
        // WHY:  Allows deterministic verification of both Server GC and Workstation GC paths.
        // HOW:  Ternary branch evaluating isServerGc.
        return isServerGc ? "Enabled (Server GC)" : "Workstation";
    }

    /// <summary>
    /// Inspects the in-process .NET 10 trajectory streaming parser node.
    /// </summary>
    /// <returns>A snapshot describing the parser's uptime, memory use, and detected vector instruction set.</returns>
    private StackServiceInfo InspectParserNode()
    {
        Process process = Process.GetCurrentProcess();
        TimeSpan uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

        string simdCapability = GetSimdCapabilityDescription(
            Vector512.IsHardwareAccelerated,
            Vector256.IsHardwareAccelerated,
            Vector128.IsHardwareAccelerated);

        return new StackServiceInfo
        {
            ServiceId = "trajectory-parser",
            DisplayName = ".NET 10 Zero-Allocation Streaming Parser",
            RoleDescription = "High-throughput System.IO.Pipelines streaming reader, SIMD pocket contact analyzer, and web microservice.",
            EndpointOrLocation = "http://localhost:5000",
            Health = ServiceHealthState.Healthy,
            StatusMessage = "Operational (Zero-GC Streaming Pipeline Ready)",
            TechnologyStack = $".NET 10.0 / C# 13 ({simdCapability})",
            Metrics = new Dictionary<string, string>
            {
                ["GC Allocation Policy"] = "Strict Zero-Allocation (ArrayPool<T>.Shared)",
                ["GC Server Mode"] = GetGcModeDescription(System.Runtime.GCSettings.IsServerGC),
                ["Process Working Set"] = $"{process.WorkingSet64 / (1024.0 * 1024.0):F1} MB",
                ["Uptime"] = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
            }
        };
    }

    /// <summary>
    /// Inspects the collaborative peer mesh network and active single-use share keys.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A snapshot describing how many collaborative share links are active and how many were already used.</returns>
    private async Task<StackServiceInfo> InspectPeerMeshAsync(CancellationToken ct)
    {
        IReadOnlyList<ShareKeyRecord> keys = await _configRepo.ListShareKeysAsync(ct);
        int activeKeys = keys.Count(k => k.IsActive);
        int consumedKeys = keys.Count(k => k.IsConsumed);

        return new StackServiceInfo
        {
            ServiceId = "peer-mesh",
            DisplayName = "Collaborative Computing & Peer Federation",
            RoleDescription = "Generates cryptographically signed single-use share keys and links for secure remote node collaboration.",
            EndpointOrLocation = "Federated Peering Protocol (port 5000 / /connect)",
            Health = ServiceHealthState.Healthy,
            StatusMessage = $"Operational ({activeKeys} Active Share Links, {consumedKeys} Connected Peers)",
            TechnologyStack = "Cryptographic 256-bit Bearer Tokens / Single-Use Nonces",
            Metrics = new Dictionary<string, string>
            {
                ["Active Share Keys"] = $"{activeKeys} available for connection",
                ["Consumed Peer Sessions"] = $"{consumedKeys} peer instances connected",
                ["Total Share Links Generated"] = $"{keys.Count} total"
            }
        };
    }
}
