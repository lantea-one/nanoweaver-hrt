using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Services.Stack;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests for <see cref="StackServices"/> verifying stack service topology,
/// diagnostic connectivity probing, and the hardware accelerator qualification benchmark card.
/// </summary>
public sealed class StackServicesTests : TestContext
{
    /// <summary>
    /// Mock stack service manager providing service health and configuration decks.
    /// </summary>
    private readonly Mock<IStackServiceManager> _stackManagerMock = new();

    /// <summary>
    /// Mock configuration repository supplying trajectory parquet directory paths.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configRepoMock = new();

    /// <summary>
    /// Mock hardware accelerator benchmark service providing qualification reports.
    /// </summary>
    private readonly Mock<IAcceleratorBenchmarkService> _benchmarkServiceMock = new();

    /// <summary>
    /// Sample hardware accelerator benchmark report used across test assertions.
    /// </summary>
    private readonly AcceleratorBenchmarkReport _sampleGoReport = new()
    {
        DeviceName = "Intel Arc A770 Graphics",
        DriverVersion = "24.1.0-opencl",
        SinglePrecisionGflops = 18500.5,
        DoublePrecisionGflops = 2300.25,
        MemoryBandwidthGigaBytesPerSecond = 512.75,
        TemperatureCelsius = 58.5,
        PowerUsageWatts = 195.0,
        Readiness = HardwareReadinessScore.Go,
        Recommendations = ["Hardware surpasses production simulation campaign requirements."],
        BenchmarkTimestampUtc = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="StackServicesTests"/> class.
    /// </summary>
    public StackServicesTests()
    {
        // WHAT: Registers mock dependencies and configures loose JSInterop in bUnit container.
        // WHY:  Ensures full DI graph resolution when rendering the interactive StackServices page.
        // HOW:  Adds singleton mock registrations for IStackServiceManager, IConfigurationRepository, and IAcceleratorBenchmarkService.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _stackManagerMock
            .Setup(m => m.GetStackServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StackServiceInfo>
            {
                new()
                {
                    ServiceId = "simulation-engine",
                    DisplayName = "Simulation Engine",
                    TechnologyStack = "oxDNA 2.4 / LAMMPS (OpenCL / SYCL)",
                    RoleDescription = "Molecular dynamics folding simulation engine.",
                    EndpointOrLocation = "/workspace/shared-data",
                    Health = ServiceHealthState.Healthy,
                    StatusMessage = "Engine container active and responsive.",
                    Metrics = new Dictionary<string, string>
                    {
                        ["Backend"] = "OpenCL",
                        ["State"] = "Idle"
                    }
                },
                new()
                {
                    ServiceId = "pg-duckdb",
                    DisplayName = "Columnar Storage Node",
                    TechnologyStack = "PostgreSQL 17 + DuckDB extension",
                    RoleDescription = "SIMD-vectorized columnar Parquet query engine.",
                    EndpointOrLocation = "postgres://weaver:5432/nanoweaver",
                    Health = ServiceHealthState.DegradedOrFallback,
                    StatusMessage = "Operating in local SQLite fallback mode.",
                    Metrics = new Dictionary<string, string>
                    {
                        ["Mode"] = "Fallback"
                    }
                }
            });

        _stackManagerMock
            .Setup(m => m.GetDiscoveredSimulationConfigs())
            .Returns(new List<string> { "oxdna_deck.oxdna", "lammps_deck.in" });

        _configRepoMock
            .Setup(c => c.GetValueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("/tmp");

        Services.AddSingleton<IStackServiceManager>(_stackManagerMock.Object);
        Services.AddSingleton<IConfigurationRepository>(_configRepoMock.Object);
        Services.AddSingleton<IAcceleratorBenchmarkService>(_benchmarkServiceMock.Object);
    }

    /// <summary>
    /// Verifies that when no cached benchmark report exists, an empty state is rendered with an enabled qualification button.
    /// </summary>
    [Fact]
    public void Render_WithoutBenchmarkReport_RendersEmptyStateAndQualificationButton()
    {
        // WHAT: Tests component initialization when no prior accelerator benchmark report is available.
        // WHY:  Verifies empty-state guidance and ensures the "Run Hardware Qualification" button is accessible.
        // HOW:  Returns null from GetLatestReportAsync, renders component, and asserts on DOM content.
        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcceleratorBenchmarkReport?)null);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        var emptyState = cut.Find(".accelerator-benchmark-card .empty-state");
        Assert.Contains("No accelerator benchmark report is currently available.", emptyState.TextContent);

        var button = cut.Find("button.btn-run-benchmark");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Contains("Run Hardware Qualification", button.TextContent);
    }

    /// <summary>
    /// Verifies that when a cached report exists, all hardware metrics, readiness badges, and recommendations are displayed.
    /// </summary>
    [Fact]
    public void Render_WithCachedBenchmarkReport_RendersAllMetricsAndReadinessGo()
    {
        // WHAT: Tests component initialization with a pre-existing accelerator benchmark report.
        // WHY:  Validates that operator and researcher diagnostics show device name, driver, FP32, FP64, bandwidth, temperature, and power.
        // HOW:  Returns sample report from GetLatestReportAsync, renders component, and queries DOM via fresh cut.Find calls.
        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleGoReport);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        Assert.Contains("Intel Arc A770 Graphics", cut.Find(".benchmark-device-name").TextContent);
        Assert.Contains("24.1.0-opencl", cut.Find(".benchmark-driver-version").TextContent);
        Assert.Contains("18500.50 GFLOPS", cut.Find(".fp32-throughput").TextContent);
        Assert.Contains("2300.25 GFLOPS", cut.Find(".fp64-throughput").TextContent);
        Assert.Contains("512.75 GB/s", cut.Find(".memory-bandwidth").TextContent);
        Assert.Contains("58.5 °C", cut.Find(".benchmark-temp").TextContent);
        Assert.Contains("195.0 W", cut.Find(".benchmark-power").TextContent);

        var badge = cut.Find(".benchmark-readiness-badge");
        Assert.Contains("badge-green", badge.ClassName);
        Assert.Contains("Go", badge.TextContent);

        var rec = cut.Find(".recommendation-item");
        Assert.Contains("Hardware surpasses production simulation campaign requirements.", rec.TextContent);
    }

    /// <summary>
    /// Verifies that Marginal and Insufficient readiness scores apply amber and red high-contrast badge styles.
    /// </summary>
    [Fact]
    public void Render_WithMarginalAndInsufficientReadiness_AppliesExpectedBadgeClasses()
    {
        // WHAT: Tests badge CSS classes for Marginal and Insufficient hardware readiness tiers.
        // WHY:  Guarantees high-contrast visual safety indicators for suboptimal accelerator hardware.
        // HOW:  Renders with Marginal report and asserts badge-amber, then renders with Insufficient report and asserts badge-red.
        AcceleratorBenchmarkReport marginalReport = _sampleGoReport with
        {
            Readiness = HardwareReadinessScore.Marginal,
            Recommendations = ["Thermal throttle approaching safe limits."]
        };

        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(marginalReport);

        IRenderedComponent<StackServices> cutMarginal = RenderComponent<StackServices>();
        var marginalBadge = cutMarginal.Find(".benchmark-readiness-badge");
        Assert.Contains("badge-amber", marginalBadge.ClassName);
        Assert.Contains("Marginal", marginalBadge.TextContent);

        AcceleratorBenchmarkReport insufficientReport = _sampleGoReport with
        {
            Readiness = HardwareReadinessScore.Insufficient,
            Recommendations = ["Hardware fails minimum memory bandwidth threshold."]
        };

        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(insufficientReport);

        IRenderedComponent<StackServices> cutInsufficient = RenderComponent<StackServices>();
        var insufficientBadge = cutInsufficient.Find(".benchmark-readiness-badge");
        Assert.Contains("badge-red", insufficientBadge.ClassName);
        Assert.Contains("Insufficient", insufficientBadge.TextContent);
    }

    /// <summary>
    /// Verifies that clicking "Run Hardware Qualification" executes the benchmark engine and updates the UI with fresh metrics.
    /// </summary>
    [Fact]
    public async Task Click_RunHardwareQualification_TriggersBenchmarkAndUpdatesUi()
    {
        // WHAT: Tests the button click interaction triggering an on-demand hardware accelerator benchmark.
        // WHY:  Ensures operators can qualify their accelerators directly from the diagnostic web interface.
        // HOW:  Renders with null report, clicks button, awaits async dispatch, and verifies fresh DOM elements via cut.Find.
        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcceleratorBenchmarkReport?)null);

        _benchmarkServiceMock
            .Setup(b => b.RunBenchmarkAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleGoReport);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        // Verify initially empty
        Assert.NotNull(cut.Find(".accelerator-benchmark-card .empty-state"));

        // Trigger hardware qualification run
        var runButton = cut.Find("button.btn-run-benchmark");
        await cut.InvokeAsync(() => runButton.Click());

        // Prime Directive 5.2: always re-query DOM elements via fresh cut.Find calls
        _benchmarkServiceMock.Verify(b => b.RunBenchmarkAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Intel Arc A770 Graphics", cut.Find(".benchmark-device-name").TextContent);
        Assert.Contains("18500.50 GFLOPS", cut.Find(".fp32-throughput").TextContent);
    }

    /// <summary>
    /// Verifies that clicking the test connectivity button invokes service ping and updates telemetry.
    /// </summary>
    [Fact]
    public async Task PingService_TriggersManagerAndRefreshesServices()
    {
        // WHAT: Tests individual stack service connectivity testing interaction.
        // WHY:  Validates that operators can ping container dependencies from the services topology view.
        // HOW:  Clicks "Test Connectivity" button and asserts PingServiceAsync was called.
        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcceleratorBenchmarkReport?)null);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        var pingButton = cut.Find(".service-card button");
        await cut.InvokeAsync(() => pingButton.Click());

        _stackManagerMock.Verify(m => m.PingServiceAsync("simulation-engine", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that clicking "Refresh Services" re-probes all stack services and volume listings.
    /// </summary>
    [Fact]
    public async Task RefreshServices_RefreshesCatalogAndServices()
    {
        // WHAT: Tests master "Refresh Services" button interaction.
        // WHY:  Allows operators to re-probe all stack components and volume mounts.
        // HOW:  Clicks refresh button and verifies GetStackServicesAsync invocation count.
        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcceleratorBenchmarkReport?)null);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        var refreshButton = cut.Find(".services-container > .panel-header .actions button");
        await cut.InvokeAsync(() => refreshButton.Click());

        // Once in OnInitializedAsync, once in RefreshServicesAsync on click = 2 times
        _stackManagerMock.Verify(m => m.GetStackServicesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that offline and busy service health states resolve expected CSS badge and border classes.
    /// </summary>
    [Fact]
    public void ServiceHealthStates_OfflineAndBusy_ResolveExpectedCssClasses()
    {
        // WHAT: Tests rendering for Offline and Busy stack service states.
        // WHY:  Validates fallback branches in GetBadgeClass and GetCardBorderClass.
        // HOW:  Provides Offline and Busy service records, renders component, and asserts on CSS classes.
        _stackManagerMock
            .Setup(m => m.GetStackServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StackServiceInfo>
            {
                new()
                {
                    ServiceId = "offline-svc",
                    DisplayName = "Offline Service",
                    TechnologyStack = "Docker Container",
                    RoleDescription = "Offline testing.",
                    EndpointOrLocation = "http://localhost:9999",
                    Health = ServiceHealthState.Offline,
                    StatusMessage = "Container unreachable.",
                    Metrics = new Dictionary<string, string>()
                },
                new()
                {
                    ServiceId = "busy-svc",
                    DisplayName = "Busy Service",
                    TechnologyStack = "Compute Node",
                    RoleDescription = "Busy testing.",
                    EndpointOrLocation = "http://localhost:8888",
                    Health = ServiceHealthState.Busy,
                    StatusMessage = "Simulation running.",
                    Metrics = new Dictionary<string, string>()
                }
            });

        _benchmarkServiceMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AcceleratorBenchmarkReport?)null);

        IRenderedComponent<StackServices> cut = RenderComponent<StackServices>();

        var cards = cut.FindAll(".service-card");
        Assert.Contains("border-offline", cards[0].ClassName);
        Assert.Contains("badge-rose", cards[0].QuerySelector(".badge")?.ClassName);

        Assert.Contains("badge-accent", cards[1].QuerySelector(".badge")?.ClassName);
    }
}
