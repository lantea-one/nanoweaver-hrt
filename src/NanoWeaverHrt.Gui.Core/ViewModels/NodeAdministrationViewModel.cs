using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel coordinating sovereign node administration, first-launch setup bootstrapping,
/// user account management, configuration overrides, container stack services, and hardware telemetry.
/// </summary>
/// <remarks>
/// // WHAT: Provides headless presentation state and asynchronous commands for node administration workflows.
/// // WHY:  Issue #169 (P166): Exposes comprehensive administration capabilities to desktop and mobile frontends.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, tracks busy states, and records observable result artifacts.
/// </remarks>
public sealed class NodeAdministrationViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// REST client communicating with the connected sovereign nanoweaver-hrt node.
    /// </summary>
    private readonly INanoWeaverNodeClient _client;

    /// <summary>
    /// Callback delegate resolving the active instance connection profile.
    /// </summary>
    private readonly Func<InstanceProfile?>? _profileProvider;

    /// <summary>
    /// Callback delegate resolving the active zeroizable Bearer session token.
    /// </summary>
    private readonly Func<string?>? _tokenProvider;

    /// <summary>
    /// Explicit override for active instance connection profile.
    /// </summary>
    private InstanceProfile? _activeProfile;

    /// <summary>
    /// Explicit override for active Bearer authentication token.
    /// </summary>
    private string? _activeBearerToken;

    /// <summary>
    /// Indicates whether an asynchronous node administration operation is currently running.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during operation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Result of the most recent first-launch setup status check.
    /// </summary>
    private SetupStatusResponse? _lastSetupStatus;

    /// <summary>
    /// Collection of sovereign user accounts retrieved from the node.
    /// </summary>
    private IReadOnlyList<UserAccount>? _users;

    /// <summary>
    /// Account details for the currently authenticated user.
    /// </summary>
    private UserAccount? _currentUser;

    /// <summary>
    /// Collection of configuration items and environment settings retrieved from the node.
    /// </summary>
    private IReadOnlyList<ConfigurationItem>? _configurations;

    /// <summary>
    /// Collection of container service status records retrieved from the node.
    /// </summary>
    private IReadOnlyList<StackServiceInfo>? _stackServices;

    /// <summary>
    /// Result of the most recent hardware qualification accelerator benchmark report.
    /// </summary>
    private AcceleratorBenchmarkReport? _lastBenchmarkReport;

    /// <summary>
    /// Result of the most recent granular multi-subsystem diagnostic health report.
    /// </summary>
    private GranularSystemHealth? _lastGranularHealth;

    /// <summary>
    /// Result of the most recent high-level system health overview or health probe.
    /// </summary>
    private SystemHealthOverview? _lastSystemHealth;

    /// <summary>
    /// Result of the most recent event bus and distributed broker telemetry query.
    /// </summary>
    private EventBusTelemetry? _lastEventBusMetrics;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeAdministrationViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing node administration requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public NodeAdministrationViewModel(
        INanoWeaverNodeClient client,
        Func<InstanceProfile?>? profileProvider = null,
        Func<string?>? tokenProvider = null)
    {
        // WHAT: Injects mandatory client and optional ambient session providers.
        // WHY:  Allows flexible usage in both integrated shell contexts and isolated headless unit tests.
        // HOW:  Validates non-null client and assigns internal delegate fields.
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _profileProvider = profileProvider;
        _tokenProvider = tokenProvider;
    }

    /// <summary>
    /// Gets or sets an explicit instance profile override.
    /// </summary>
    public InstanceProfile? ActiveProfile
    {
        get => _activeProfile;
        set => SetField(ref _activeProfile, value);
    }

    /// <summary>
    /// Gets or sets an explicit Bearer session token override.
    /// </summary>
    public string? ActiveBearerToken
    {
        get => _activeBearerToken;
        set => SetField(ref _activeBearerToken, value);
    }

    /// <summary>
    /// Gets a value indicating whether an asynchronous operation is currently in flight.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// Gets the most recent operational error message, or <see langword="null"/> if successful.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets the result of the most recent first-launch setup status check.
    /// </summary>
    public SetupStatusResponse? LastSetupStatus
    {
        get => _lastSetupStatus;
        private set => SetField(ref _lastSetupStatus, value);
    }

    /// <summary>
    /// Gets the list of registered sovereign user accounts retrieved from the node.
    /// </summary>
    public IReadOnlyList<UserAccount>? Users
    {
        get => _users;
        private set => SetField(ref _users, value);
    }

    /// <summary>
    /// Gets the account details for the currently authenticated user.
    /// </summary>
    public UserAccount? CurrentUser
    {
        get => _currentUser;
        private set => SetField(ref _currentUser, value);
    }

    /// <summary>
    /// Gets the collection of effective stack configuration items.
    /// </summary>
    public IReadOnlyList<ConfigurationItem>? Configurations
    {
        get => _configurations;
        private set => SetField(ref _configurations, value);
    }

    /// <summary>
    /// Gets the collection of stack container service runtime statuses.
    /// </summary>
    public IReadOnlyList<StackServiceInfo>? StackServices
    {
        get => _stackServices;
        private set => SetField(ref _stackServices, value);
    }

    /// <summary>
    /// Gets the result of the most recent hardware accelerator benchmark report.
    /// </summary>
    public AcceleratorBenchmarkReport? LastBenchmarkReport
    {
        get => _lastBenchmarkReport;
        private set => SetField(ref _lastBenchmarkReport, value);
    }

    /// <summary>
    /// Gets the result of the most recent granular diagnostic health report.
    /// </summary>
    public GranularSystemHealth? LastGranularHealth
    {
        get => _lastGranularHealth;
        private set => SetField(ref _lastGranularHealth, value);
    }

    /// <summary>
    /// Gets the result of the most recent high-level system health overview or health probe.
    /// </summary>
    public SystemHealthOverview? LastSystemHealth
    {
        get => _lastSystemHealth;
        private set => SetField(ref _lastSystemHealth, value);
    }

    /// <summary>
    /// Gets the result of the most recent event bus telemetry query.
    /// </summary>
    public EventBusTelemetry? LastEventBusMetrics
    {
        get => _lastEventBusMetrics;
        private set => SetField(ref _lastEventBusMetrics, value);
    }

    /// <summary>
    /// Evaluates whether the first-launch setup wizard is required via GET /api/setup/status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Setup status payload indicating if initial administrator provisioning is needed.</returns>
    public async Task<SetupStatusResponse> GetSetupStatusAsync(CancellationToken ct = default)
    {
        // WHAT: Dispatches a setup status query to the connected node client.
        // WHY:  Determines whether the sovereign node requires first-launch administrator initialization.
        // HOW:  Resolves effective profile, sets busy state, invokes client, and records result.
        var profile = ResolveEffectiveProfile();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetSetupStatusAsync(profile, ct).ConfigureAwait(false);
            LastSetupStatus = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Bootstraps the sovereign node by provisioning the master Administrator account and initial public key via POST /api/setup/initialize.
    /// </summary>
    /// <param name="request">Initial administrator credentials, public key, and access gate configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Authentication response containing initial Bearer JWT and administrator user profile.</returns>
    public async Task<AuthTokenResponse> InitializeSetupAdminAsync(
        InitialSetupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Initializes the master Administrator account on an uninitialized node.
        // WHY:  Fulfills first-launch cryptographic bootstrap requirements.
        // HOW:  Validates request, resolves effective profile, calls client, and captures error on failure.
        ArgumentNullException.ThrowIfNull(request);
        var profile = ResolveEffectiveProfile();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.InitializeSetupAdminAsync(profile, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lists all registered sovereign user accounts via GET /api/users.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of registered user accounts.</returns>
    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        // WHAT: Fetches registered sovereign user accounts from the node.
        // WHY:  Allows administrator inspection and user lifecycle oversight.
        // HOW:  Resolves effective session, calls client, updates Users property, and handles errors.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListUsersAsync(profile, token, ct).ConfigureAwait(false);
            Users = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Provisions a new sovereign user account with an existing public key via POST /api/users.
    /// </summary>
    /// <param name="request">User account creation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created user account record.</returns>
    public async Task<UserAccount> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Provisions a new sovereign user account with an existing public key.
        // WHY:  Enables administrators to enroll researchers, clinicians, and patients.
        // HOW:  Validates request, resolves credentials, invokes client, and reports errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreateUserAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Revokes and permanently deletes a user account via DELETE /api/users/{username}.
    /// </summary>
    /// <param name="username">Username of account to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeleteUserAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Deletes a user account from the sovereign node.
        // WHY:  Permits administrative account revocation and lifecycle cleanup.
        // HOW:  Validates username, resolves session credentials, delegates to client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteUserAsync(profile, token, username, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Executes cryptographic destruction of subject decryption keys via POST /api/compliance/crypto-shred.
    /// </summary>
    /// <param name="request">Crypto-shred payload specifying target subject ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task CryptoShredUserAsync(CryptoShredRequest request, CancellationToken ct = default)
    {
        // WHAT: Executes irreversible cryptographic destruction of subject decryption keys.
        // WHY:  Satisfies GDPR Article 17 Right to be Forgotten and clinical privacy compliance.
        // HOW:  Validates request, resolves credentials, dispatches to client, and manages busy state.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.CryptoShredUserAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves identity claims and account details for the authenticated user via GET /api/auth/me.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current authenticated user account.</returns>
    public async Task<UserAccount> GetCurrentUserAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves identity claims and account details for the authenticated user.
        // WHY:  Enables display of logged-in user profile, roles, and cryptographic algorithms.
        // HOW:  Resolves session, queries client, updates CurrentUser property, and handles errors.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetCurrentUserAsync(profile, token, ct).ConfigureAwait(false);
            CurrentUser = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Terminates the interactive session via POST /api/auth/logout.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        // WHAT: Terminates the interactive session on the sovereign node.
        // WHY:  Invalidates active session token on the remote instance.
        // HOW:  Resolves session, invokes client logout, clears CurrentUser, and handles busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.LogoutAsync(profile, token, ct).ConfigureAwait(false);
            CurrentUser = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves all stack configuration entries via GET /api/configuration (Administrator role required).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of effective configuration items.</returns>
    public async Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves all stack configuration entries from the connected instance.
        // WHY:  Provides visibility into operational node settings and environment variables.
        // HOW:  Resolves credentials, queries client, records Configurations property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetAllConfigurationsAsync(profile, token, ct).ConfigureAwait(false);
            Configurations = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists an authoritative configuration override to the database via POST /api/configuration/override (Administrator role required).
    /// </summary>
    /// <param name="request">Key-value override parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetConfigurationOverrideAsync(
        ConfigOverrideRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Persists an authoritative configuration override to the sovereign node database.
        // WHY:  Allows administrators to tune platform parameters dynamically without restarting services.
        // HOW:  Validates request, resolves session, calls client, and captures operational errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.SetConfigurationOverrideAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Reverts an authoritative configuration override to default/environment fallback via DELETE /api/configuration/override/{key} (Administrator role required).
    /// </summary>
    /// <param name="key">Configuration key to revert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteConfigurationOverrideAsync(
        string key,
        CancellationToken ct = default)
    {
        // WHAT: Reverts an authoritative configuration override back to default/environment settings.
        // WHY:  Restores default configuration behavior when custom overrides are no longer required.
        // HOW:  Validates key string, resolves session, invokes client, and captures errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteConfigurationOverrideAsync(profile, token, key, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves real-time status and health telemetry for all container services via GET /api/services.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of runtime statuses for database, cache, and simulation engines.</returns>
    public async Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves real-time status and health telemetry for all container services.
        // WHY:  Enables administrative monitoring of database, cache, and simulation engines.
        // HOW:  Resolves session, queries client, sets StackServices property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetStackServicesAsync(profile, token, ct).ConfigureAwait(false);
            StackServices = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tests connectivity and latency to a single stack dependency via POST /api/services/ping/{serviceId}.
    /// </summary>
    /// <param name="serviceId">Identifier of target service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refreshed health and latency metrics for the service.</returns>
    public async Task<StackServiceInfo> PingStackServiceAsync(
        string serviceId,
        CancellationToken ct = default)
    {
        // WHAT: Tests connectivity and latency to a single stack container dependency.
        // WHY:  Allows diagnostic probing of individual sub-services during troubleshooting.
        // HOW:  Validates serviceId, resolves session, awaits client, and records error if ping fails.
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.PingStackServiceAsync(profile, token, serviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Queries the most recent cached hardware accelerator benchmark report via GET /api/system/benchmark/latest.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Most recent benchmark report, or <see langword="null"/> if none exists.</returns>
    public async Task<AcceleratorBenchmarkReport?> GetLatestBenchmarkAsync(CancellationToken ct = default)
    {
        // WHAT: Queries the most recent cached hardware accelerator benchmark report.
        // WHY:  Allows inspecting compute and memory bandwidth qualification metrics without re-running benchmark.
        // HOW:  Resolves session, queries client, sets LastBenchmarkReport, and handles errors.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetLatestBenchmarkAsync(profile, token, ct).ConfigureAwait(false);
            LastBenchmarkReport = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Executes an on-demand hardware qualification benchmark via POST /api/system/benchmark/run.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Hardware accelerator compute and memory bandwidth report.</returns>
    public async Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(CancellationToken ct = default)
    {
        // WHAT: Executes an on-demand hardware qualification benchmark on the connected node.
        // WHY:  Measures real-time GPU/accelerator compute throughput and memory bandwidth.
        // HOW:  Resolves session, triggers client benchmark run, updates LastBenchmarkReport, and handles state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.RunBenchmarkAsync(profile, token, ct).ConfigureAwait(false);
            LastBenchmarkReport = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves detailed subsystem diagnostic telemetry via authenticated GET /api/system/health.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Granular multi-subsystem diagnostic health report.</returns>
    public async Task<GranularSystemHealth> GetGranularSystemHealthAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves detailed multi-subsystem diagnostic telemetry from the node.
        // WHY:  Provides comprehensive health metrics across hardware, database, storage, and networking.
        // HOW:  Resolves session, invokes client, updates LastGranularHealth, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetGranularSystemHealthAsync(profile, token, ct).ConfigureAwait(false);
            LastGranularHealth = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Queries the operational health status and diagnostic telemetry via GET /api/system/health.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>System health overview report.</returns>
    public async Task<SystemHealthOverview> GetSystemHealthAsync(CancellationToken ct = default)
    {
        // WHAT: Queries operational health status and diagnostic telemetry via GET /api/system/health.
        // WHY:  Provides a high-level overview of overall cluster runtime health.
        // HOW:  Resolves effective profile and optional token, invokes client, updates LastSystemHealth, and tracks state.
        var profile = ResolveEffectiveProfile();
        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetSystemHealthAsync(profile, token, ct).ConfigureAwait(false);
            LastSystemHealth = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Evaluates cluster container runtime operational health via the GET /healthz liveness probe.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>High-level system health overview.</returns>
    public async Task<SystemHealthOverview> GetHealthzAsync(CancellationToken ct = default)
    {
        // WHAT: Evaluates cluster container runtime operational health via the GET /healthz probe.
        // WHY:  Verifies container liveness and orchestrator readiness without authentication.
        // HOW:  Resolves effective profile, invokes client, updates LastSystemHealth, and manages busy state.
        var profile = ResolveEffectiveProfile();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetHealthzAsync(profile, ct).ConfigureAwait(false);
            LastSystemHealth = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Evaluates node operational health via the standard GET /health probe.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>High-level system health overview.</returns>
    public async Task<SystemHealthOverview> GetHealthAsync(CancellationToken ct = default)
    {
        // WHAT: Evaluates node operational health via the standard GET /health probe.
        // WHY:  Provides unauthenticated health check probe response for monitoring agents.
        // HOW:  Resolves effective profile, invokes client, updates LastSystemHealth, and manages busy state.
        var profile = ResolveEffectiveProfile();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetHealthAsync(profile, ct).ConfigureAwait(false);
            LastSystemHealth = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves real-time telemetry on the in-memory event bus and distributed Valkey bridge via GET /api/system/event-bus.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Event bus telemetry metrics.</returns>
    public async Task<EventBusTelemetry> GetEventBusMetricsAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieves real-time telemetry on the in-memory event bus and distributed message bridge.
        // WHY:  Exposes publication throughput, subscriber lag, and dropped message counts for monitoring.
        // HOW:  Resolves session credentials, invokes client, updates LastEventBusMetrics, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetEventBusMetricsAsync(profile, token, ct).ConfigureAwait(false);
            LastEventBusMetrics = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Resolves the effective instance connection profile.
    /// </summary>
    /// <returns>The active <see cref="InstanceProfile"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active profile is available.</exception>
    private InstanceProfile ResolveEffectiveProfile()
    {
        // WHAT: Obtains active instance profile from local property or ambient delegate.
        // WHY:  Guarantees REST requests cannot be issued without an endpoint profile.
        // HOW:  Evaluates explicit property override with fallback to injected provider callback.
        var profile = ActiveProfile ?? _profileProvider?.Invoke();
        if (profile == null)
        {
            throw new InvalidOperationException("An active instance profile is required to execute node administration operations.");
        }

        return profile;
    }

    /// <summary>
    /// Resolves the effective instance connection profile and Bearer authentication token.
    /// </summary>
    /// <returns>A tuple containing the active profile and Bearer token string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active profile or bearer token is available.</exception>
    private (InstanceProfile Profile, string BearerToken) ResolveEffectiveSession()
    {
        // WHAT: Obtains active instance profile and Bearer token from local properties or ambient delegates.
        // WHY:  Guarantees authenticated REST requests cannot be issued without complete session credentials.
        // HOW:  Resolves profile first, then evaluates token property with fallback to token provider delegate.
        var profile = ResolveEffectiveProfile();

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute node administration operations.");
        }

        return (profile, token);
    }

    /// <summary>
    /// Helper method to mutate property values and dispatch PropertyChanged events when altered.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">New value to assign.</param>
    /// <param name="propertyName">Name of the property being modified.</param>
    /// <returns>True if the property value changed; otherwise false.</returns>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // WHAT: Performs equality check and conditionally mutates backing field.
        // WHY:  Avoids redundant PropertyChanged event churn across data bindings.
        // HOW:  Compares values via EqualityComparer<T>.Default; if distinct, sets field and invokes OnPropertyChanged.
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">Name of the modified property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Dispatches PropertyChanged event to subscribers.
        // WHY:  Signals Avalonia UI binding engine to update presentation elements.
        // HOW:  Invokes PropertyChanged handler with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
