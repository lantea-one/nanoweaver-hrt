using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Simulations.Models;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel coordinating in-silico simulation workflows including physiological serum stability,
/// aptamer gating dynamics, payload leakage retention, PBPK endocrine kinetics, and analytical SQL querying.
/// </summary>
/// <remarks>
/// // WHAT: Provides headless, reactive presentation logic for computational nanocarrier simulation campaigns.
/// // WHY:  Issue #159 (P156): Enables desktop and mobile frontends to trigger simulations and inspect live results.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, records results in observable properties, and manages busy states.
/// </remarks>
public sealed class SimulationWorkspaceViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// REST client communicating with the target sovereign nanoweaver-hrt node.
    /// </summary>
    private readonly INanoWeaverNodeClient _client;

    /// <summary>
    /// Callback delegate resolving the currently active instance connection profile.
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
    /// Default dataset identifier for analytical SQL queries.
    /// </summary>
    private string _activeDatasetName = "default";

    /// <summary>
    /// Indicates whether a simulation campaign or analytical query is currently running.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during simulation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Result of the most recent physiological serum stability simulation campaign.
    /// </summary>
    private SerumStabilityAnalysisResult? _lastSerumStabilityResult;

    /// <summary>
    /// Result of the most recent aptamer gating thermodynamic simulation campaign.
    /// </summary>
    private AptamerGatingAnalysisResult? _lastAptamerGatingResult;

    /// <summary>
    /// Result of the most recent payload leakage retention simulation campaign.
    /// </summary>
    private PayloadLeakageAnalysisResult? _lastPayloadLeakageResult;

    /// <summary>
    /// Result of the most recent PBPK endocrine pharmacokinetic simulation campaign.
    /// </summary>
    private PbpkEndocrineSimulationResult? _lastPbpkSimulationResult;

    /// <summary>
    /// Result of the most recent analytical SQL query executed against trajectory datasets.
    /// </summary>
    private TrajectoryAnalyticsApiResponse? _lastTrajectoryAnalyticsResult;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing simulation and query requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public SimulationWorkspaceViewModel(
        INanoWeaverNodeClient client,
        Func<InstanceProfile?>? profileProvider = null,
        Func<string?>? tokenProvider = null)
    {
        // WHAT: Injects mandatory client and optional ambient session providers.
        // WHY:  Permits standalone headless test execution or shell-integrated credential resolution.
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
    /// Gets or sets the target dataset name used for analytical SQL trajectory queries.
    /// </summary>
    public string ActiveDatasetName
    {
        get => _activeDatasetName;
        set => SetField(ref _activeDatasetName, value);
    }

    /// <summary>
    /// Gets a value indicating whether an asynchronous simulation operation is currently in flight.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// Gets the most recent operational error message, or <see langword="null"/> if the last operation succeeded.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets the result of the most recent serum stability simulation campaign.
    /// </summary>
    public SerumStabilityAnalysisResult? LastSerumStabilityResult
    {
        get => _lastSerumStabilityResult;
        private set => SetField(ref _lastSerumStabilityResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent aptamer gating simulation campaign.
    /// </summary>
    public AptamerGatingAnalysisResult? LastAptamerGatingResult
    {
        get => _lastAptamerGatingResult;
        private set => SetField(ref _lastAptamerGatingResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent payload leakage simulation campaign.
    /// </summary>
    public PayloadLeakageAnalysisResult? LastPayloadLeakageResult
    {
        get => _lastPayloadLeakageResult;
        private set => SetField(ref _lastPayloadLeakageResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent PBPK endocrine simulation campaign.
    /// </summary>
    public PbpkEndocrineSimulationResult? LastPbpkSimulationResult
    {
        get => _lastPbpkSimulationResult;
        private set => SetField(ref _lastPbpkSimulationResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent trajectory analytical query.
    /// </summary>
    public TrajectoryAnalyticsApiResponse? LastTrajectoryAnalyticsResult
    {
        get => _lastTrajectoryAnalyticsResult;
        private set => SetField(ref _lastTrajectoryAnalyticsResult, value);
    }

    /// <summary>
    /// Executes a serum stability simulation campaign against the active instance.
    /// </summary>
    /// <param name="request">Serum stability simulation parameters and environmental conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including degradation kinetics and half-life projections.</returns>
    public async Task<SerumStabilityAnalysisResult> RunSerumStabilityAsync(
        SerumStabilityRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a serum stability simulation request to the node client.
        // WHY:  Calculates nanocarrier nuclease degradation kinetics in simulated physiological serum.
        // HOW:  Validates inputs, obtains active profile and token, sets busy state, awaits client, and records result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.RunSerumStabilityAsync(profile, token, request, ct).ConfigureAwait(false);
            LastSerumStabilityResult = result;
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
    /// Executes an aptamer gating simulation campaign against the active instance.
    /// </summary>
    /// <param name="request">Aptamer gating simulation parameters and target ligand conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including opening probability and gating thermodynamics.</returns>
    public async Task<AptamerGatingAnalysisResult> RunAptamerGatingAsync(
        AptamerGatingRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches an aptamer gating thermodynamic simulation request to the node client.
        // WHY:  Models ligand-dependent bistable gating conformational transitions for targeted nanocarriers.
        // HOW:  Validates inputs, resolves session credentials, manages busy state, invokes client, and updates observable result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.RunAptamerGatingAsync(profile, token, request, ct).ConfigureAwait(false);
            LastAptamerGatingResult = result;
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
    /// Executes a payload leakage simulation campaign against the active instance.
    /// </summary>
    /// <param name="request">Payload retention parameters and membrane stability conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including premature leakage rates and retention efficiency.</returns>
    public async Task<PayloadLeakageAnalysisResult> RunPayloadLeakageAsync(
        PayloadLeakageRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a payload retention leakage simulation request to the node client.
        // WHY:  Quantifies steroidal payload containment efficiency inside DNA origami hollow cavities.
        // HOW:  Validates inputs, retrieves active credentials, coordinates busy state, and captures result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.RunPayloadLeakageAsync(profile, token, request, ct).ConfigureAwait(false);
            LastPayloadLeakageResult = result;
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
    /// Executes a PBPK endocrine pharmacokinetic simulation campaign against the active instance.
    /// </summary>
    /// <param name="request">PBPK endocrine dosing, receptor kinetics, and patient demographic parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Simulation campaign results including systemic bioavailability and compartmental kinetics.</returns>
    public async Task<PbpkEndocrineSimulationResult> RunPbpkSimulationAsync(
        PbpkSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a physiologically based pharmacokinetic (PBPK) simulation request to the node client.
        // WHY:  Forecasts multi-compartment endocrine hormone distributions and tissue receptor kinetics.
        // HOW:  Validates inputs, retrieves active session credentials, sets busy indicator, and updates state.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.RunPbpkSimulationAsync(profile, token, request, ct).ConfigureAwait(false);
            LastPbpkSimulationResult = result;
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
    /// Executes a read-only analytical SQL query against the active trajectory dataset.
    /// </summary>
    /// <param name="sqlQuery">The read-only SQL query to execute.</param>
    /// <param name="maxRows">The maximum number of rows to return (default is 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tabular query results containing column schema and row data arrays.</returns>
    public async Task<TrajectoryAnalyticsApiResponse> QueryTrajectoryAnalyticsAsync(
        string sqlQuery,
        int maxRows = 100,
        CancellationToken ct = default)
    {
        // WHAT: Constructs a trajectory query request using ActiveDatasetName and dispatches it.
        // WHY:  Permits ad-hoc SQL inspection of columnar trajectory datasets from the desktop GUI.
        // HOW:  Validates query text, builds TrajectoryAnalyticsApiRequest, and delegates to the overload.
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlQuery);
        var request = new TrajectoryAnalyticsApiRequest(ActiveDatasetName, sqlQuery, maxRows);
        return await QueryTrajectoryAnalyticsAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a read-only analytical SQL query against a trajectory dataset using an explicit request model.
    /// </summary>
    /// <param name="request">Analytical query request specifying dataset name, query text, and row limits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tabular query results containing column schema and row data arrays.</returns>
    public async Task<TrajectoryAnalyticsApiResponse> QueryTrajectoryAnalyticsAsync(
        TrajectoryAnalyticsApiRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches an analytical SQL query to the connected node client.
        // WHY:  Retrieves analytical telemetry and frame statistics for visualization in the desktop workspace.
        // HOW:  Validates request, resolves session credentials, activates busy state, awaits client, and records result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.QueryTrajectoryAnalyticsAsync(profile, token, request, ct).ConfigureAwait(false);
            LastTrajectoryAnalyticsResult = result;
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
    /// Resolves the effective instance connection profile and Bearer authentication token.
    /// </summary>
    /// <returns>A tuple containing the active profile and Bearer token string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active profile or bearer token is available.</exception>
    private (InstanceProfile Profile, string BearerToken) ResolveEffectiveSession()
    {
        // WHAT: Obtains active instance profile and Bearer token from local properties or ambient delegates.
        // WHY:  Guarantees authenticated REST requests cannot be issued without complete session credentials.
        // HOW:  Evaluates explicit property overrides with fallback to injected provider callbacks.
        var profile = ActiveProfile ?? _profileProvider?.Invoke();
        if (profile == null)
        {
            throw new InvalidOperationException("An active instance profile is required to execute simulation operations.");
        }

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute simulation operations.");
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
