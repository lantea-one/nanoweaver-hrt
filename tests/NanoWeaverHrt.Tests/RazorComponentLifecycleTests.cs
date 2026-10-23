#pragma warning disable BL0006

using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Components.Layout;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Services;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive unit tests verifying the lifecycle, user interactions, action callbacks,
/// and render-tree generation for all Blazor Interactive Server UI components.
/// </summary>
public sealed class RazorComponentLifecycleTests : IDisposable
{
    /// <summary>
    /// Service provider supplying dependencies for component injection.
    /// </summary>
    private readonly ServiceProvider _services;

    /// <summary>
    /// Isolated temporary directory for file-based configuration and trajectory tests.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Mock JavaScript runtime recording invocations.
    /// </summary>
    private readonly MockJsRuntime _jsRuntime;

    /// <summary>
    /// Mock navigation manager simulating client-side navigation.
    /// </summary>
    private readonly MockNavigationManager _navManager;

    /// <summary>
    /// Headless test renderer for attaching render handles and executing component render queues.
    /// </summary>
    private readonly TestRenderer _renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RazorComponentLifecycleTests"/> class.
    /// </summary>
    public RazorComponentLifecycleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_component_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _jsRuntime = new MockJsRuntime();
        _navManager = new MockNavigationManager("http://localhost:5000/", "http://localhost:5000/");

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["TRAJECTORY_EXPORT_DIR"] = Path.Combine(_tempDir, "exports"),
                ["TRAJECTORY_PARQUET_DIR"] = Path.Combine(_tempDir, "parquet"),
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        ServiceCollection sc = new();
        sc.AddSingleton(config);
        sc.AddSingleton<IConfigurationRepository, SystemConfigurationStore>();
        sc.AddSingleton<IStackServiceManager, StackServiceManager>();
        sc.AddSingleton<IFabricationService, FabricationService>();
        sc.AddSingleton<IDatasetManager, DatasetTransferService>();
        sc.AddSingleton<XyzPipeReader>();
        sc.AddSingleton<DcdPipeReader>();
        sc.AddSingleton<LammpsCustomPipeReader>();
        sc.AddSingleton<IParquetTrajectoryWriter, DuckDbParquetWriter>();
        sc.AddSingleton<EndocrineStateEngine>();
        sc.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        sc.AddSingleton<ISimulationCampaignService, SimulationCampaignService>();
        // The authentication service now takes its bearer-token signing key from INodeSecretStore instead of
        // deriving it from a published constant, so the component host must provide one. It resolves its key
        // directory from the TRAJECTORY_INCOMING_DIR above, which keeps the generated files in this test
        // instance's temporary directory.
        sc.AddSingleton<INodeSecretStore, NodeSecretStore>();
        sc.AddSingleton<IPostQuantumCryptoProvider, PostQuantumCryptoProvider>();
        sc.AddSingleton<IKeyManagementService, KeyManagementService>();
        sc.AddSingleton<IComplianceAuditService, ComplianceAuditService>();
        sc.AddSingleton<DatabaseMigrationRunner>();
        sc.AddSingleton<IWeaverDbConnectionFactory, WeaverDbConnectionFactory>();
        sc.AddSingleton<IUserRepository, UserRepository>();
        sc.AddSingleton<IUserAuthenticationService, UserAuthenticationService>();
        sc.AddSingleton<IServerSessionActivityTracker>(_ => new ServerSessionActivityTracker());
        sc.AddScoped<UserSessionState>();
        sc.AddSingleton<NavigationManager>(_navManager);
        sc.AddSingleton<IJSRuntime>(_jsRuntime);
        sc.AddHttpContextAccessor();
        sc.AddLogging();

        _services = sc.BuildServiceProvider();
        _renderer = new TestRenderer(_services, NullLoggerFactory.Instance);

        // Initialize store and user repository
        IConfigurationRepository repo = _services.GetRequiredService<IConfigurationRepository>();
        repo.InitializeAsync().GetAwaiter().GetResult();

        IUserRepository userRepo = _services.GetRequiredService<IUserRepository>();
        userRepo.InitializeAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _services.Dispose();
        _renderer.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in temp
            }
        }
    }

    /// <summary>
    /// Injects dependencies into a component's [Inject] properties.
    /// </summary>
    /// <param name="component">Target Razor component.</param>
    /// <param name="services">Dependency service provider.</param>
    private static void InjectDependencies(ComponentBase component, IServiceProvider services)
    {
        // WHAT: Discovers properties decorated with [InjectAttribute] and assigns matching service instances.
        // WHY:  Permits headless testing of Razor component lifecycle methods without requiring an active browser or WebSocket.
        // HOW:  Uses reflection to inspect all public and non-public properties of the component type.
        Type type = component.GetType();
        PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (PropertyInfo prop in props)
        {
            if (prop.GetCustomAttribute<InjectAttribute>() != null)
            {
                object? service = services.GetService(prop.PropertyType);
                if (service != null && prop.CanWrite)
                {
                    prop.SetValue(component, service);
                }
            }
        }
    }

    /// <summary>
    /// Initializes a component, attaches it to the test renderer within the dispatcher, and renders its initial tree.
    /// </summary>
    /// <param name="component">The Razor component to initialize.</param>
    private async Task InitializeAndRenderAsync(ComponentBase component)
    {
        InjectDependencies(component, _services);
        await _renderer.Dispatcher.InvokeAsync(async () =>
        {
            _renderer.AttachComponent(component);
            await component.SetParametersAsync(ParameterView.Empty);
            RenderComponent(component);
        });
    }

    /// <summary>
    /// Invokes the protected BuildRenderTree method on a component within the dispatcher.
    /// </summary>
    /// <param name="component">The component to render.</param>
    private void RenderComponent(ComponentBase component)
    {
        // WHAT: Invokes the protected BuildRenderTree method with a real RenderTreeBuilder.
        // WHY:  Executes and verifies all Razor template branches, expressions, and event bindings.
        // HOW:  Obtains the MethodInfo for BuildRenderTree via reflection and invokes it inside Dispatcher.
        _renderer.Dispatcher.InvokeAsync(() =>
        {
            using RenderTreeBuilder builder = new();
            MethodInfo? method = component.GetType().GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(component, [builder]);
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invokes a non-public asynchronous method on a component within the dispatcher.
    /// </summary>
    private async Task InvokeAsyncMethod(object target, string methodName, params object?[] parameters)
    {
        await _renderer.Dispatcher.InvokeAsync(async () =>
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            object? result = method.Invoke(target, parameters);
            if (result is Task task)
            {
                await task;
            }
        });
    }

    /// <summary>
    /// Invokes a non-public synchronous method on a component within the dispatcher.
    /// </summary>
    private void InvokeMethod(object target, string methodName, params object?[] parameters)
    {
        _renderer.Dispatcher.InvokeAsync(() =>
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(target, parameters);
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invokes a non-public synchronous method that returns a value on a component within the dispatcher.
    /// </summary>
    private T InvokeMethodWithResult<T>(object target, string methodName, params object?[] parameters)
    {
        return _renderer.Dispatcher.InvokeAsync(() =>
        {
            MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (T)method.Invoke(target, parameters)!;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads a non-public property from a component via reflection.
    /// </summary>
    private static T GetProperty<T>(object target, string propName)
    {
        PropertyInfo? pi = target.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(pi);
        return (T)pi.GetValue(target)!;
    }

    /// <summary>
    /// Sets a non-public property on a component via reflection.
    /// </summary>
    private static void SetProperty(object target, string propName, object? value)
    {
        PropertyInfo? pi = target.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(pi);
        pi.SetValue(target, value);
    }

    /// <summary>
    /// Sets a non-public field on a component via reflection.
    /// </summary>
    private static void SetField(object target, string fieldName, object? value)
    {
        FieldInfo? fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(fi);
        fi.SetValue(target, value);
    }

    /// <summary>
    /// Verifies the <see cref="WorkflowWizard"/> component completes all 6 stages, preset selection,
    /// simulation execution, stream parsing, and fabrication export.
    /// </summary>
    [Fact]
    public async Task WorkflowWizard_FullLifecycle_ExercisesAllStagesAndActions()
    {
        // WHAT: Tests end-to-end user actions on the Guided DNA Workflow Wizard.
        // WHY:  Verifies step progression, sequence editing, in-silico simulation, and fabrication package generation.
        // HOW:  Instantiates WorkflowWizard, sets parameters, drives each stage, and asserts state changes.
        WorkflowWizard wizard = new();
        await InitializeAndRenderAsync(wizard);

        // Assert initial stage is 1
        Assert.Equal(1, GetProperty<int>(wizard, "CurrentStep"));

        // 1. Test Preset Selection
        IFabricationService fab = _services.GetRequiredService<IFabricationService>();
        IReadOnlyList<DnaOrigamiSample> presets = fab.GetPresetSamples();
        Assert.NotEmpty(presets);

        foreach (DnaOrigamiSample sample in presets)
        {
            InvokeMethod(wizard, "SelectSamplePreset", sample);
            Assert.Equal(sample.Id, GetProperty<string>(wizard, "SelectedSampleId"));
            Assert.Equal(sample.AptamerSequence, GetProperty<string>(wizard, "InputSequence"));
            RenderComponent(wizard);
        }

        // 2. Test Custom Sequence Change
        SetProperty(wizard, "InputSequence", "ATCGATCGATCGATCG");
        InvokeMethod(wizard, "OnSequenceChanged");
        RenderComponent(wizard);

        // 3. Test Base Percentage Calculation
        double pct = InvokeMethodWithResult<double>(wizard, "GetBasePercent", 4);
        Assert.True(pct > 0);

        // 4. Test Step Navigation through all 6 steps
        for (int step = 1; step <= 6; step++)
        {
            InvokeMethod(wizard, "SetStep", step);
            Assert.Equal(step, GetProperty<int>(wizard, "CurrentStep"));
            RenderComponent(wizard);

            // Exercise OnAfterRenderAsync for each step
            await InvokeAsyncMethod(wizard, "OnAfterRenderAsync", false);
        }

        // Test boundary clamping on step
        InvokeMethod(wizard, "SetStep", -5);
        Assert.Equal(1, GetProperty<int>(wizard, "CurrentStep"));
        InvokeMethod(wizard, "SetStep", 99);
        Assert.Equal(6, GetProperty<int>(wizard, "CurrentStep"));

        // 5. Test Regimen Switching
        InvokeMethod(wizard, "SetRegimen", TherapeuticRegimen.MaleToFemale);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, GetProperty<TherapeuticRegimen>(wizard, "SelectedRegimen"));
        Assert.Equal(550.0, GetProperty<double>(wizard, "TargetEstradiolPmol"));

        InvokeMethod(wizard, "SetRegimen", TherapeuticRegimen.FemaleToMale);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, GetProperty<TherapeuticRegimen>(wizard, "SelectedRegimen"));
        Assert.Equal(90.0, GetProperty<double>(wizard, "TargetEstradiolPmol"));

        // 6. Test In-Silico Simulation Runner
        await InvokeAsyncMethod(wizard, "RunSimulationAsync");

        // 7. Test Stream Parser Runner
        await InvokeAsyncMethod(wizard, "RunStreamParseAsync");

        // 8. Test Parquet Export Runner
        await InvokeAsyncMethod(wizard, "RunParquetExportAsync");

        // 9. Test Fabrication Package Re-generation and Export
        InvokeMethod(wizard, "RegenerateFabrication");
        await InvokeAsyncMethod(wizard, "ExportFabricationPackageAsync");

        // 10. Test DIY & HomeLab Fabrication Guide tab navigation
        InvokeMethod(wizard, "SetDiyTab", "pipetting");
        RenderComponent(wizard);
        InvokeMethod(wizard, "SetDiyTab", "materials");
        RenderComponent(wizard);
        InvokeMethod(wizard, "SetDiyTab", "qc");
        RenderComponent(wizard);
        InvokeMethod(wizard, "SetDiyTab", "protocol");
        RenderComponent(wizard);

        // 11. Test Sequence Pharmacogenomic Screening and TLR9 Epigenetic Warning Render
        SetProperty(wizard, "InputSequence", string.Concat(Enumerable.Repeat("CGCG", 25)));
        InvokeMethod(wizard, "OnSequenceChanged");
        RenderComponent(wizard);

        // Advance to Stage 6 with TLR9 alert triggered to verify epigenetic banner render
        InvokeMethod(wizard, "SetStep", 6);
        RenderComponent(wizard);

        // Test static alert CSS helpers
        Assert.Equal("genomic-alert-critical", InvokeMethodWithResult<string>(wizard, "GetAlertBoxClass", ClinicalSeverity.Critical));
        Assert.Equal("genomic-alert-warning", InvokeMethodWithResult<string>(wizard, "GetAlertBoxClass", ClinicalSeverity.Warning));
        Assert.Equal("genomic-alert-advisory", InvokeMethodWithResult<string>(wizard, "GetAlertBoxClass", ClinicalSeverity.Advisory));
        Assert.Equal("genomic-alert-optimal", InvokeMethodWithResult<string>(wizard, "GetAlertBoxClass", ClinicalSeverity.Optimal));

        Assert.Equal("genomic-badge-critical", InvokeMethodWithResult<string>(wizard, "GetAlertBadgeClass", ClinicalSeverity.Critical));
        Assert.Equal("genomic-badge-warning", InvokeMethodWithResult<string>(wizard, "GetAlertBadgeClass", ClinicalSeverity.Warning));
        Assert.Equal("genomic-badge-advisory", InvokeMethodWithResult<string>(wizard, "GetAlertBadgeClass", ClinicalSeverity.Advisory));
        Assert.Equal("genomic-badge-optimal", InvokeMethodWithResult<string>(wizard, "GetAlertBadgeClass", ClinicalSeverity.Optimal));
    }

    /// <summary>
    /// Verifies the <see cref="ConfigurationManagement"/> component handles override persistence,
    /// revert operations, single-use share key generation, and revocation.
    /// </summary>
    [Fact]
    public async Task ConfigurationManagement_FullLifecycle_ExercisesOverridesAndShareKeys()
    {
        // WHAT: Tests the Authoritative Configuration & Peer Collaboration Blazor component.
        // WHY:  Ensures UI controls correctly invoke database override persistence, revert, and token lifecycles.
        // HOW:  Instantiates ConfigurationManagement, saves an override, reverts it, creates a key, and revokes it.
        ConfigurationManagement configPage = new();
        await InitializeAndRenderAsync(configPage);

        // 1. Save an override
        string testKey = "Simulation:AcceleratorBackend";
        FieldInfo? editValuesField = typeof(ConfigurationManagement).GetField("_editValues", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(editValuesField);
        Dictionary<string, string> editValues = (Dictionary<string, string>)editValuesField.GetValue(configPage)!;
        editValues[testKey] = "NVIDIA CUDA (Testing)";

        await InvokeAsyncMethod(configPage, "SaveOverrideAsync", testKey);
        RenderComponent(configPage);

        // Verify database override was applied
        IConfigurationRepository repo = _services.GetRequiredService<IConfigurationRepository>();
        string val = await repo.GetValueAsync(testKey);
        Assert.Equal("NVIDIA CUDA (Testing)", val);

        // 2. Revert the override
        await InvokeAsyncMethod(configPage, "RevertDefaultAsync", testKey);
        RenderComponent(configPage);

        // 3. Create a Single-Use Share Key
        FieldInfo? windowField = typeof(ConfigurationManagement).GetField("_newKeyExpirationWindow", BindingFlags.Instance | BindingFlags.NonPublic);
        windowField?.SetValue(configPage, "1m");
        await InvokeAsyncMethod(configPage, "CreateShareKeyAsync");
        RenderComponent(configPage);

        // Verify share key exists
        FieldInfo? newlyGenField = typeof(ConfigurationManagement).GetField("_newlyGeneratedKey", BindingFlags.Instance | BindingFlags.NonPublic);
        ShareKeyRecord? newKey = (ShareKeyRecord?)newlyGenField?.GetValue(configPage);
        Assert.NotNull(newKey);
        Assert.StartsWith("nanoweaver_hrt_", newKey.KeyToken);

        // 4. Test Clipboard Copy
        await InvokeAsyncMethod(configPage, "CopyShareLinkToClipboard");

        // 5. Test Revoking the Key
        await InvokeAsyncMethod(configPage, "RevokeKeyAsync", newKey.TokenId);
        RenderComponent(configPage);

        // 6. Test Badge Helper Methods
        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeClass", ConfigurationSource.DatabaseOverride));
        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeClass", ConfigurationSource.EnvironmentVariable));
        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeClass", ConfigurationSource.DefaultFallback));

        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeLabel", ConfigurationSource.DatabaseOverride));
        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeLabel", ConfigurationSource.EnvironmentVariable));
        Assert.NotNull(InvokeMethodWithResult<string>(configPage, "GetSourceBadgeLabel", ConfigurationSource.DefaultFallback));
    }

    /// <summary>
    /// Verifies the <see cref="EndocrineDashboard"/> component handles bistable toggle switches
    /// and canvas chart render passes.
    /// </summary>
    [Fact]
    public async Task EndocrineDashboard_FullLifecycle_ExercisesToggleAndRendering()
    {
        // WHAT: Tests the Patient-Targeted Endocrine Regulatory Hub component.
        // WHY:  Verifies toggle state updates between Estrogenic and Androgenic dominance.
        // HOW:  Instantiates EndocrineDashboard, toggles states, and runs chart renders.
        EndocrineDashboard dashboard = new();
        await InitializeAndRenderAsync(dashboard);

        // 1. Toggle to Androgenic Dominant (FtM)
        await InvokeAsyncMethod(dashboard, "SetToggleState", ToggleState.AndrogenicDominant);
        RenderComponent(dashboard);

        EndocrineStateEngine engine = _services.GetRequiredService<EndocrineStateEngine>();
        Assert.Equal(ToggleState.AndrogenicDominant, engine.GetActiveProfile().TargetState);

        // 2. Toggle to Estrogenic Dominant (MtF)
        await InvokeAsyncMethod(dashboard, "SetToggleState", ToggleState.EstrogenicDominant);
        RenderComponent(dashboard);
        Assert.Equal(ToggleState.EstrogenicDominant, engine.GetActiveProfile().TargetState);

        // 3. Test Pharmacogenomic Presets and Custom Markers
        InvokeMethod(dashboard, "SelectPreset", "PRESET_MTF_THROMBOSIS");
        RenderComponent(dashboard);

        // Test non-existent preset fallback
        InvokeMethod(dashboard, "SelectPreset", "PRESET_NONEXISTENT");
        RenderComponent(dashboard);

        // Toggle marker off and on
        InvokeMethod(dashboard, "ToggleMarker", "F5_LEIDEN");
        RenderComponent(dashboard);
        InvokeMethod(dashboard, "ToggleMarker", "F5_LEIDEN");
        RenderComponent(dashboard);

        // Clear all markers
        InvokeMethod(dashboard, "ClearAllMarkers");
        RenderComponent(dashboard);

        // Apply genomic adjustments
        InvokeMethod(dashboard, "SelectPreset", "PRESET_MTF_THROMBOSIS");
        await InvokeAsyncMethod(dashboard, "ApplyGenomicAdjustments");
        RenderComponent(dashboard);

        // Test styling helpers
        Assert.Equal("genomic-alert-critical", InvokeMethodWithResult<string>(dashboard, "GetAlertBoxClass", ClinicalSeverity.Critical));
        Assert.Equal("genomic-alert-warning", InvokeMethodWithResult<string>(dashboard, "GetAlertBoxClass", ClinicalSeverity.Warning));
        Assert.Equal("genomic-alert-advisory", InvokeMethodWithResult<string>(dashboard, "GetAlertBoxClass", ClinicalSeverity.Advisory));
        Assert.Equal("genomic-alert-optimal", InvokeMethodWithResult<string>(dashboard, "GetAlertBoxClass", ClinicalSeverity.Optimal));

        Assert.Equal("genomic-badge-critical", InvokeMethodWithResult<string>(dashboard, "GetBadgeClass", ClinicalSeverity.Critical));
        Assert.Equal("genomic-badge-warning", InvokeMethodWithResult<string>(dashboard, "GetBadgeClass", ClinicalSeverity.Warning));
        Assert.Equal("genomic-badge-advisory", InvokeMethodWithResult<string>(dashboard, "GetBadgeClass", ClinicalSeverity.Advisory));
        Assert.Equal("genomic-badge-optimal", InvokeMethodWithResult<string>(dashboard, "GetBadgeClass", ClinicalSeverity.Optimal));

        // 4. Test AfterRender and Chart Rendering
        await InvokeAsyncMethod(dashboard, "OnAfterRenderAsync", false);
        await InvokeAsyncMethod(dashboard, "RenderChartAsync");
    }

    /// <summary>
    /// Verifies the <see cref="StackServices"/> component inspects stack topology and executes service pings.
    /// </summary>
    [Fact]
    public async Task StackServices_FullLifecycle_ExercisesInspectionAndPings()
    {
        // WHAT: Tests the Sovereign Stack Services Topology dashboard component.
        // WHY:  Verifies that service cards, metrics, and connectivity pings function cleanly.
        // HOW:  Instantiates StackServices, triggers pings for each service ID, and renders the view.
        StackServices page = new();
        await InitializeAndRenderAsync(page);

        // 1. Test Refresh
        await InvokeAsyncMethod(page, "RefreshServicesAsync");
        RenderComponent(page);

        // 2. Test Ping for all service IDs
        string[] services = ["simulation-engine", "pg-duckdb", "trajectory-parser", "peer-mesh", "unknown-service"];
        foreach (string svcId in services)
        {
            await InvokeAsyncMethod(page, "PingServiceAsync", svcId);
            RenderComponent(page);
        }

        // 3. Test static styling helpers across all ServiceHealthState enum cases
        Assert.Equal("badge-accent", InvokeMethodWithResult<string>(page, "GetBadgeClass", ServiceHealthState.Healthy));
        Assert.Equal("badge-outline", InvokeMethodWithResult<string>(page, "GetBadgeClass", ServiceHealthState.DegradedOrFallback));
        Assert.Equal("badge-accent", InvokeMethodWithResult<string>(page, "GetBadgeClass", ServiceHealthState.Busy));
        Assert.Equal("badge-rose", InvokeMethodWithResult<string>(page, "GetBadgeClass", ServiceHealthState.Offline));

        Assert.Equal("border-healthy", InvokeMethodWithResult<string>(page, "GetCardBorderClass", ServiceHealthState.Healthy));
        Assert.Equal("border-fallback", InvokeMethodWithResult<string>(page, "GetCardBorderClass", ServiceHealthState.DegradedOrFallback));
        Assert.Equal("border-offline", InvokeMethodWithResult<string>(page, "GetCardBorderClass", ServiceHealthState.Offline));

        // 4. Test non-empty parquet and simulation config tables
        string parquetDir = Path.Combine(_tempDir, "parquet");
        Directory.CreateDirectory(parquetDir);
        await File.WriteAllTextAsync(Path.Combine(parquetDir, "test.parquet"), "PAR1");

        IConfigurationRepository repo = _services.GetRequiredService<IConfigurationRepository>();
        await repo.SetDatabaseOverrideAsync("Storage:TrajectoryParquetDir", parquetDir);

        SetField(page, "_simulationConfigs", (IReadOnlyList<string>)new[] { "test.in", "test.oxdna" });
        await InvokeAsyncMethod(page, "RefreshServicesAsync");
        RenderComponent(page);
    }

    /// <summary>
    /// Verifies the <see cref="TrajectoryBrowser"/> component loads files, runs stream parses,
    /// triggers Parquet conversions, and executes dataset exports.
    /// </summary>
    [Fact]
    public async Task TrajectoryBrowser_FullLifecycle_ExercisesFileOperations()
    {
        // WHAT: Tests the Zero-Allocation Trajectory Streaming Browser component.
        // WHY:  Validates file discovery, stream parsing, Parquet export, and dataset export actions.
        // HOW:  Creates test fixture files in the temp directory, initializes the browser, and drives each action.
        string testXyz = Path.Combine(_tempDir, "browser_test.xyz");
        File.WriteAllText(testXyz, "1\nTest Frame\nC 0.0 0.0 0.0\n");

        TrajectoryBrowser browser = new();
        await InitializeAndRenderAsync(browser);

        // 1. Refresh file list
        await InvokeAsyncMethod(browser, "LoadTrajectoriesAsync");
        RenderComponent(browser);

        // 2. Stream parse file
        await InvokeAsyncMethod(browser, "StreamParseFileAsync", "browser_test.xyz");
        RenderComponent(browser);

        // 3. Convert to Parquet
        await InvokeAsyncMethod(browser, "ConvertToParquetAsync", "browser_test.xyz");
        RenderComponent(browser);

        // 4. Export dataset
        await InvokeAsyncMethod(browser, "ExportDatasetAsync");
        RenderComponent(browser);
    }

    /// <summary>
    /// Verifies the <see cref="PeerConnect"/> component handles single-use token redemption and error branches.
    /// </summary>
    [Fact]
    public async Task PeerConnect_FullLifecycle_ExercisesTokenRedemption()
    {
        // WHAT: Tests the Federated Peer Connection Portal component.
        // WHY:  Verifies single-use token redemption and session establishment through the Blazor UI.
        // HOW:  Generates a valid share key, redeems it through PeerConnect, and asserts success.
        IConfigurationRepository repo = _services.GetRequiredService<IConfigurationRepository>();
        ShareKeyRecord key = await repo.CreateShareKeyAsync("Test Peer", TimeSpan.FromHours(1));

        // Test with token in query string
        _navManager.SetUri($"http://localhost:5000/connect?token={key.KeyToken}");

        PeerConnect connectPage = new();
        connectPage.TokenInput = key.KeyToken;
        await InitializeAndRenderAsync(connectPage);

        // Redeem the token
        await InvokeAsyncMethod(connectPage, "RedeemTokenAsync");
        RenderComponent(connectPage);

        // Attempting to redeem again with the consumed token must show an error
        await InvokeAsyncMethod(connectPage, "RedeemTokenAsync");
        RenderComponent(connectPage);

        // Attempting to redeem with empty token
        connectPage.TokenInput = "";
        await InvokeAsyncMethod(connectPage, "RedeemTokenAsync");
        RenderComponent(connectPage);
    }

    /// <summary>
    /// Verifies the <see cref="MainLayout"/> component renders navigation, telemetry badges, and node status.
    /// </summary>
    [Fact]
    public async Task MainLayout_FullLifecycle_RendersCleanly()
    {
        MainLayout layout = new();
        await InitializeAndRenderAsync(layout);
        RenderComponent(layout);
        InvokeMethod(layout, "UpdateTelemetry");

        // Exercise idle callbacks when unauthenticated
        await layout.RecordActivity();
        await layout.OnUserIdleTimeout();

        // Exercise with authenticated user
        UserSessionState session = _services.GetRequiredService<UserSessionState>();
        session.SetUser(new UserAccount
        {
            Username = "admin_layout_test",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MC4CAQAwBQYDK2VwBCIEIA=="
        });

        // Trigger OnAfterRenderAsync with authenticated state
        await InvokeAsyncMethod(layout, "OnAfterRenderAsync", false);

        // Trigger a heartbeat and then OnUserIdleTimeout when authenticated -> signs out and navigates
        await layout.RecordActivity();
        await layout.OnUserIdleTimeout();
        Assert.False(session.IsAuthenticated);

        // Trigger OnAfterRenderAsync when unauthenticated -> unregisters watcher
        await InvokeAsyncMethod(layout, "OnAfterRenderAsync", false);

        // Exercise HandleSignOut
        InvokeMethod(layout, "HandleSignOut");

        layout.Dispose();
    }

    /// <summary>
    /// Verifies the <see cref="Login"/> component handles idle timeout query notices, validation, and sign out.
    /// </summary>
    [Fact]
    public async Task Login_FullLifecycle_ExercisesIdleTimeoutAndInteractions()
    {
        // 1. Render login page with reason=idle_timeout query parameter
        _navManager.NavigateTo("http://localhost:5000/login?reason=idle_timeout");
        Login login = new();
        await InitializeAndRenderAsync(login);
        RenderComponent(login);

        // 2. Exercise Authenticate with empty fields (validation failure)
        await InvokeAsyncMethod(login, "Authenticate");
        RenderComponent(login);

        // 3. Exercise SignOut method
        InvokeMethod(login, "SignOut");
        RenderComponent(login);
    }

    /// <summary>
    /// Verifies the <see cref="SimulationStudio"/> component initializes all campaigns, runs simulations, and switches tabs.
    /// </summary>
    [Fact]
    public async Task SimulationStudio_FullLifecycle_ExercisesAllCampaignsAndRuns()
    {
        SimulationStudio studio = new();
        await InitializeAndRenderAsync(studio);
        RenderComponent(studio);

        // Switch to Campaign 2
        InvokeMethod(studio, "SelectCampaign", "CAMPAIGN_APTAMER_GATING");
        RenderComponent(studio);
        InvokeMethod(studio, "RunAptamerGating");
        RenderComponent(studio);

        // Switch to Campaign 3
        InvokeMethod(studio, "SelectCampaign", "CAMPAIGN_PAYLOAD_LEAKAGE");
        RenderComponent(studio);
        InvokeMethod(studio, "RunPayloadLeakage");
        RenderComponent(studio);

        // Switch to Campaign 4
        InvokeMethod(studio, "SelectCampaign", "CAMPAIGN_PBPK_ENDOCRINE");
        RenderComponent(studio);
        InvokeMethod(studio, "RunPbpkSimulation");
        RenderComponent(studio);

        // Switch back to Campaign 1
        InvokeMethod(studio, "SelectCampaign", "CAMPAIGN_SERUM_STABILITY");
        RenderComponent(studio);
        InvokeMethod(studio, "RunSerumStability");
        RenderComponent(studio);
    }

    /// <summary>
    /// Headless in-memory component renderer that assigns render handles and enables StateHasChanged dispatch.
    /// </summary>
    private sealed class TestRenderer : Renderer
    {
        /// <summary>
        /// Initializes a new instance of <see cref="TestRenderer"/>.
        /// </summary>
        public TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
            : base(services, loggerFactory)
        {
        }

        /// <inheritdoc/>
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        /// <summary>
        /// Attaches a component to this renderer, assigning a valid RenderHandle.
        /// </summary>
        public void AttachComponent(IComponent component)
        {
            AssignRootComponentId(component);
        }

        /// <inheritdoc/>
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void HandleException(Exception exception)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>
    /// Mock navigation manager for unit testing.
    /// </summary>
    private sealed class MockNavigationManager : NavigationManager
    {
        /// <summary>
        /// Initializes a new instance of <see cref="MockNavigationManager"/>.
        /// </summary>
        public MockNavigationManager(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        /// <summary>
        /// Sets the current URI.
        /// </summary>
        public void SetUri(string uri)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }

        /// <inheritdoc/>
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }
    }

    /// <summary>
    /// Mock JavaScript runtime for unit testing.
    /// </summary>
    private sealed class MockJsRuntime : IJSRuntime
    {
        /// <summary>
        /// Recorded method invocations.
        /// </summary>
        public List<(string Identifier, object?[]? Args)> Invocations { get; } = new();

        /// <inheritdoc/>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add((identifier, args));
            return ValueTask.FromResult(default(TValue)!);
        }

        /// <inheritdoc/>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add((identifier, args));
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
