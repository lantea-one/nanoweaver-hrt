using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// User interface request model for generating an automated Opentrons OT-2 or Flex liquid handler script.
/// </summary>
/// <param name="SequenceOrSampleId">Raw scaffold DNA sequence or built-in sample identifier.</param>
/// <param name="Regimen">Target endocrine regimen.</param>
/// <param name="Geometry">Nanocarrier geometric lattice (e.g. Nanotube, Cuboid, Cage).</param>
/// <param name="TargetKd">Target aptamer dissociation constant in nanomolar.</param>
/// <param name="Options">Optional protocol synthesis and deck configuration options.</param>
public sealed record OpentronsGenerationRequest(
    string SequenceOrSampleId,
    TherapeuticRegimen Regimen = TherapeuticRegimen.MaleToFemale,
    string Geometry = "Nanotube",
    double TargetKd = 4.8,
    OpentronsProtocolOptions? Options = null)
{
    /// <summary>
    /// Converts this request into the server-side <see cref="GenerateOpentronsScriptRequest"/> payload.
    /// </summary>
    /// <returns>A populated Opentrons script generation request instance.</returns>
    public GenerateOpentronsScriptRequest ToScriptRequest()
    {
        // WHAT: Projects UI parameters into the Opentrons protocol generator request model.
        // WHY:  Encapsulates default protocol options and aligns desktop inputs with API endpoints.
        // HOW:  Instantiates GenerateOpentronsScriptRequest copying properties and defaulting Options.
        return new GenerateOpentronsScriptRequest
        {
            SequenceOrSampleId = SequenceOrSampleId,
            Regimen = Regimen,
            Geometry = Geometry,
            TargetKd = TargetKd,
            Options = Options ?? new OpentronsProtocolOptions()
        };
    }
}

/// <summary>
/// ViewModel coordinating wet-lab benchtop automation, agarose gel electrophoresis image analysis,
/// FDA 21 CFR Part 11 audit ledger verification, and trajectory federation manifests.
/// </summary>
/// <remarks>
/// // WHAT: Provides presentation state and asynchronous commands for wet-lab fabrication and compliance oversight.
/// // WHY:  Issue #159 (P156): Enables laboratory operators to automate robotic pipetting and verify ledger integrity.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, tracks operational status, and exposes observable artifacts.
/// </remarks>
public sealed class WetLabComplianceWorkspaceViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// REST client communicating with the connected nanoweaver-hrt node.
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
    /// Indicates whether an asynchronous wet-lab or compliance operation is currently running.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during operation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Result of the most recent Opentrons liquid handler protocol synthesis.
    /// </summary>
    private OpentronsProtocolResult? _lastOpentronsResult;

    /// <summary>
    /// Result of the most recent agarose gel image analysis and yield calculation.
    /// </summary>
    private GelAnalysisResult? _lastGelAnalysisResult;

    /// <summary>
    /// Result of the most recent audit ledger cryptographic verification.
    /// </summary>
    private AuditLedgerVerificationResult? _lastAuditLedgerVerification;

    /// <summary>
    /// Result of the most recently generated trajectory federation manifest.
    /// </summary>
    private TrajectoryFederationManifest? _lastFederationManifest;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="WetLabComplianceWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing fabrication and compliance requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public WetLabComplianceWorkspaceViewModel(
        INanoWeaverNodeClient client,
        Func<InstanceProfile?>? profileProvider = null,
        Func<string?>? tokenProvider = null)
    {
        // WHAT: Injects mandatory node client and optional session credential providers.
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
    /// Gets the result of the most recent Opentrons protocol generation run.
    /// </summary>
    public OpentronsProtocolResult? LastOpentronsResult
    {
        get => _lastOpentronsResult;
        private set => SetField(ref _lastOpentronsResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent agarose gel image analysis run.
    /// </summary>
    public GelAnalysisResult? LastGelAnalysisResult
    {
        get => _lastGelAnalysisResult;
        private set => SetField(ref _lastGelAnalysisResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent audit ledger cryptographic verification run.
    /// </summary>
    public AuditLedgerVerificationResult? LastAuditLedgerVerification
    {
        get => _lastAuditLedgerVerification;
        private set => SetField(ref _lastAuditLedgerVerification, value);
    }

    /// <summary>
    /// Gets the result of the most recently built trajectory federation manifest.
    /// </summary>
    public TrajectoryFederationManifest? LastFederationManifest
    {
        get => _lastFederationManifest;
        private set => SetField(ref _lastFederationManifest, value);
    }

    /// <summary>
    /// Generates an automated Opentrons OT-2 or Flex Python protocol script using a UI request model.
    /// </summary>
    /// <param name="request">Liquid handler script generation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Synthesized Opentrons protocol script and deck layout summary.</returns>
    public async Task<OpentronsProtocolResult> GenerateOpentronsProtocolAsync(
        OpentronsGenerationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Converts UI request parameters and delegates to script generator overload.
        // WHY:  Provides clean parameter binding for the wet-lab protocol synthesis UI.
        // HOW:  Validates non-null request, converts via ToScriptRequest(), and calls overload.
        ArgumentNullException.ThrowIfNull(request);
        return await GenerateOpentronsProtocolAsync(request.ToScriptRequest(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates an automated Opentrons OT-2 or Flex Python protocol script using the API request model.
    /// </summary>
    /// <param name="request">Liquid handler script generation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Synthesized Opentrons protocol script and deck layout summary.</returns>
    public async Task<OpentronsProtocolResult> GenerateOpentronsProtocolAsync(
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches an Opentrons script synthesis request to the node client.
        // WHY:  Synthesizes Python automation code for multi-channel pipetting and thermal ramps.
        // HOW:  Validates inputs, retrieves active credentials, activates busy state, awaits client, and records result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GenerateOpentronsProtocolAsync(profile, token, request, ct).ConfigureAwait(false);
            LastOpentronsResult = result;
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
    /// Analyzes an agarose gel electrophoresis image for DNA origami nanocarrier assembly yield.
    /// </summary>
    /// <param name="request">Gel image analysis request containing pixel data and lane counts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Analytical gel electrophoresis segmentation and folding yield result.</returns>
    public async Task<GelAnalysisResult> AnalyzeGelImageAsync(
        GelAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches an agarose gel densitometry analysis request to the node client.
        // WHY:  Calculates nanocarrier folding yield, lane migration profiles, and smearing metrics.
        // HOW:  Validates inputs, retrieves active credentials, coordinates busy indicator, and stores result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.AnalyzeGelAsync(profile, token, request, ct).ConfigureAwait(false);
            LastGelAnalysisResult = result;
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
    /// Cryptographically verifies unbroken hash chaining and post-quantum signatures across the audit ledger.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result detailing ledger integrity status and entry count.</returns>
    public async Task<AuditLedgerVerificationResult> VerifyAuditLedgerAsync(CancellationToken ct = default)
    {
        // WHAT: Triggers full hash-chain verification of the FDA 21 CFR Part 11 audit ledger.
        // WHY:  Assures researchers that historical records, calibrations, and access events remain tamper-evident.
        // HOW:  Resolves credentials, sets busy indicator, queries client, and captures verification result.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.VerifyAuditLedgerAsync(profile, token, ct).ConfigureAwait(false);
            LastAuditLedgerVerification = result;
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
    /// Builds and cryptographically signs a trajectory federation chunk manifest for P2P swarm distribution.
    /// </summary>
    /// <param name="datasetName">Target trajectory file name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated trajectory federation manifest.</returns>
    public async Task<TrajectoryFederationManifest> BuildFederatedManifestAsync(
        string datasetName,
        CancellationToken ct = default)
    {
        // WHAT: Constructs a FederationBuildManifestRequest for the specified file and dispatches manifest creation.
        // WHY:  Permits sharing of verified simulation trajectories across decentralized peer laboratories.
        // HOW:  Validates non-empty file name, builds request model, and delegates to the request overload.
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);
        var request = new FederationBuildManifestRequest { TrajectoryFileName = datasetName };
        return await BuildFederatedManifestAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds and cryptographically signs a trajectory federation chunk manifest using an explicit request model.
    /// </summary>
    /// <param name="request">Parameters specifying the trajectory file name and node metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated trajectory federation manifest.</returns>
    public async Task<TrajectoryFederationManifest> BuildFederatedManifestAsync(
        FederationBuildManifestRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a trajectory federation manifest synthesis request to the node client.
        // WHY:  Computes Merkle chunk hashes and generates post-quantum signatures for trajectory distribution.
        // HOW:  Validates inputs, resolves credentials, sets busy indicator, awaits client, and records result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.BuildFederationManifestAsync(profile, token, request, ct).ConfigureAwait(false);
            LastFederationManifest = result;
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
            throw new InvalidOperationException("An active instance profile is required to execute wet-lab compliance operations.");
        }

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute wet-lab compliance operations.");
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
