using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// User interface request model for configuring dynamic closed-loop endocrine feedback simulations.
/// </summary>
/// <param name="InitialEstradiolPmolL">Initial baseline serum 17β-estradiol concentration in pmol/L.</param>
/// <param name="InitialTestosteroneNmolL">Initial baseline serum total testosterone concentration in nmol/L.</param>
/// <param name="InitialLhIuL">Initial circulating luteinizing hormone (LH) concentration in IU/L.</param>
/// <param name="InitialFshIuL">Initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.</param>
/// <param name="TargetEstradiolPmolL">Target 17β-estradiol setpoint concentration in pmol/L.</param>
/// <param name="TargetTestosteroneNmolL">Target total testosterone setpoint concentration in nmol/L.</param>
/// <param name="TargetState">Target operational toggle dominance state.</param>
/// <param name="TotalHours">Total simulation duration in hours (1 to 720).</param>
/// <param name="StepHours">Numerical integration step size in hours (0.1 to 24.0).</param>
/// <param name="ClearanceMultiplier">Metabolic clearance rate scaling multiplier (0.1 to 5.0).</param>
public sealed record ClosedLoopRequest(
    double InitialEstradiolPmolL = 120.0,
    double InitialTestosteroneNmolL = 18.0,
    double InitialLhIuL = 6.5,
    double InitialFshIuL = 5.0,
    double TargetEstradiolPmolL = 550.0,
    double TargetTestosteroneNmolL = 1.2,
    ToggleState TargetState = ToggleState.EstrogenicDominant,
    int TotalHours = 72,
    double StepHours = 1.0,
    double ClearanceMultiplier = 1.0)
{
    /// <summary>
    /// Converts this request into the server-side <see cref="ClosedLoopSimulationRequest"/> payload.
    /// </summary>
    /// <returns>A populated closed-loop simulation request instance.</returns>
    public ClosedLoopSimulationRequest ToSimulationRequest()
    {
        // WHAT: Projects UI parameters into the numerical integration request model.
        // WHY:  Decouples GUI presentation bindings from backend API contracts.
        // HOW:  Instantiates ClosedLoopSimulationRequest copying field values.
        return new ClosedLoopSimulationRequest
        {
            InitialEstradiolPmolL = InitialEstradiolPmolL,
            InitialTestosteroneNmolL = InitialTestosteroneNmolL,
            InitialLhIuL = InitialLhIuL,
            InitialFshIuL = InitialFshIuL,
            TargetEstradiolPmolL = TargetEstradiolPmolL,
            TargetTestosteroneNmolL = TargetTestosteroneNmolL,
            TargetState = TargetState,
            TotalHours = TotalHours,
            StepHours = StepHours,
            ClearanceMultiplier = ClearanceMultiplier
        };
    }
}

/// <summary>
/// User interface request model for screening patient pharmacogenomic variants and adjusting active endocrine targets.
/// </summary>
/// <param name="PatientId">Patient or anonymous subject identifier.</param>
/// <param name="MarkerIds">Array of observed clinical genetic marker identifiers.</param>
/// <param name="Regimen">Target therapeutic endocrine regimen (MaleToFemale or FemaleToMale).</param>
public sealed record PharmacogenomicsAdjustmentRequest(
    string? PatientId = "ANON-PATIENT-001",
    string[]? MarkerIds = null,
    TherapeuticRegimen Regimen = TherapeuticRegimen.MaleToFemale)
{
    /// <summary>
    /// Converts this request into the server-side <see cref="GenomicAnalysisRequest"/> payload.
    /// </summary>
    /// <returns>A populated genomic analysis request instance.</returns>
    public GenomicAnalysisRequest ToGenomicAnalysisRequest()
    {
        // WHAT: Projects UI parameters into the genomic analysis request DTO.
        // WHY:  Bridges desktop ViewModel inputs to REST client schemas.
        // HOW:  Instantiates GenomicAnalysisRequest copying PatientId, MarkerIds, and Regimen.
        return new GenomicAnalysisRequest
        {
            PatientId = PatientId,
            MarkerIds = MarkerIds,
            Regimen = Regimen
        };
    }
}

/// <summary>
/// ViewModel coordinating clinical pharmacogenomics, dynamic closed-loop endocrine feedback,
/// sequence immunogenicity screening, and personalized Digital DNA nanocarrier blueprints.
/// </summary>
/// <remarks>
/// // WHAT: Provides presentation state and asynchronous command logic for clinical genomics and endocrinology.
/// // WHY:  Issue #159 (P156): Enables clinicians and researchers to analyze genetic risk and tailor HRT blueprints.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, tracks operation lifecycle, and exposes observable results.
/// </remarks>
public sealed class ClinicalGenomicsWorkspaceViewModel : INotifyPropertyChanged
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
    /// Indicates whether an asynchronous clinical operation is currently in flight.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during operation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Result of the most recent closed-loop endocrine simulation.
    /// </summary>
    private ClosedLoopSimulationResult? _lastClosedLoopResult;

    /// <summary>
    /// Result of the most recent genomic profile or FASTA sequence analysis.
    /// </summary>
    private PatientGenomicProfile? _lastGenomicProfile;

    /// <summary>
    /// Result of the most recent pharmacogenomics adjustment evaluation.
    /// </summary>
    private GenomicAdjustmentResult? _lastAdjustmentResult;

    /// <summary>
    /// Result of the most recent patient Digital DNA blueprint query.
    /// </summary>
    private PatientDigitalDnaPackage? _lastPatientDnaPackage;

    /// <summary>
    /// Result of the most recent created patient Digital DNA database record.
    /// </summary>
    private PatientDigitalDnaRecord? _lastCreatedDnaRecord;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClinicalGenomicsWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing genomics and endocrine requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public ClinicalGenomicsWorkspaceViewModel(
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
    /// Gets the result of the most recent closed-loop endocrine simulation run.
    /// </summary>
    public ClosedLoopSimulationResult? LastClosedLoopResult
    {
        get => _lastClosedLoopResult;
        private set => SetField(ref _lastClosedLoopResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent genomic sequence or profile analysis.
    /// </summary>
    public PatientGenomicProfile? LastGenomicProfile
    {
        get => _lastGenomicProfile;
        private set => SetField(ref _lastGenomicProfile, value);
    }

    /// <summary>
    /// Gets the result of the most recent pharmacogenomic endocrine target adjustment.
    /// </summary>
    public GenomicAdjustmentResult? LastAdjustmentResult
    {
        get => _lastAdjustmentResult;
        private set => SetField(ref _lastAdjustmentResult, value);
    }

    /// <summary>
    /// Gets the result of the most recent patient Digital DNA blueprint query.
    /// </summary>
    public PatientDigitalDnaPackage? LastPatientDnaPackage
    {
        get => _lastPatientDnaPackage;
        private set => SetField(ref _lastPatientDnaPackage, value);
    }

    /// <summary>
    /// Gets the result of the most recently created patient Digital DNA blueprint record.
    /// </summary>
    public PatientDigitalDnaRecord? LastCreatedDnaRecord
    {
        get => _lastCreatedDnaRecord;
        private set => SetField(ref _lastCreatedDnaRecord, value);
    }

    /// <summary>
    /// Simulates dynamic closed-loop endocrine feedback and pituitary gonadotropin suppression using a UI request model.
    /// </summary>
    /// <param name="request">Closed-loop numerical integration parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Closed-loop numerical integration simulation result.</returns>
    public async Task<ClosedLoopSimulationResult> SimulateClosedLoopAsync(
        ClosedLoopRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Converts UI request model and invokes the underlying simulation overload.
        // WHY:  Exposes simplified parameter binding for the desktop presentation layer.
        // HOW:  Validates non-null request, converts to ClosedLoopSimulationRequest, and delegates.
        ArgumentNullException.ThrowIfNull(request);
        return await SimulateClosedLoopAsync(request.ToSimulationRequest(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Simulates dynamic closed-loop endocrine feedback and pituitary gonadotropin suppression using the API model.
    /// </summary>
    /// <param name="request">Closed-loop numerical integration parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Closed-loop numerical integration simulation result.</returns>
    public async Task<ClosedLoopSimulationResult> SimulateClosedLoopAsync(
        ClosedLoopSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a closed-loop endocrine simulation request to the node client.
        // WHY:  Solves coupled differential equations for hormone release, feedback, and gonadotropin suppression.
        // HOW:  Validates inputs, retrieves active credentials, activates busy state, awaits client, and records result.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.SimulateClosedLoopEndocrineAsync(profile, token, request, ct).ConfigureAwait(false);
            LastClosedLoopResult = result;
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
    /// Scans a raw FASTA sequence for CpG density immunogenicity and CAG repeat sensitivity under the given regimen.
    /// </summary>
    /// <param name="sequence">Raw FASTA or nucleotide sequence string.</param>
    /// <param name="regimen">Target therapeutic regimen string ("MaleToFemale", "FemaleToMale", "MtF", or "FtM").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sequence-derived patient genomic profile containing severity alerts and kinetic recommendations.</returns>
    public async Task<PatientGenomicProfile> AnalyzeGenomicProfileAsync(
        string sequence,
        string regimen,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a sequence analysis request for TLR9 CpG density and AR CAG repeat motifs.
        // WHY:  Detects immunogenicity risks and receptor sensitivity differences from patient FASTA sequences.
        // HOW:  Validates sequence, parses regimen string to enum, resolves credentials, calls client, and updates state.
        ArgumentException.ThrowIfNullOrWhiteSpace(sequence);
        var (profile, token) = ResolveEffectiveSession();

        TherapeuticRegimen parsedRegimen = TherapeuticRegimen.MaleToFemale;
        if (!string.IsNullOrWhiteSpace(regimen) &&
            (string.Equals(regimen, "FemaleToMale", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(regimen, "FtM", StringComparison.OrdinalIgnoreCase)))
        {
            parsedRegimen = TherapeuticRegimen.FemaleToMale;
        }

        var req = new SequenceAnalysisRequest
        {
            PatientId = "ANON-PATIENT-001",
            Sequence = sequence,
            Regimen = parsedRegimen
        };

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.AnalyzeGenomicSequenceAsync(profile, token, req, ct).ConfigureAwait(false);
            LastGenomicProfile = result;
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
    /// Evaluates patient pharmacogenomics and adjusts active endocrine setpoints using a UI request model.
    /// </summary>
    /// <param name="request">Genomic markers and target HRT regimen request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Evaluated genomic profile and adjusted endocrine target setpoints.</returns>
    public async Task<GenomicAdjustmentResult> ApplyGenomicAdjustmentsAsync(
        PharmacogenomicsAdjustmentRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Converts UI adjustment model and delegates to the underlying adjustment method.
        // WHY:  Provides clean property bindings for the clinical adjustment panel.
        // HOW:  Validates non-null request, converts to GenomicAnalysisRequest, and invokes overload.
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyGenomicAdjustmentsAsync(request.ToGenomicAnalysisRequest(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates patient pharmacogenomics and adjusts active endocrine setpoints using the API model.
    /// </summary>
    /// <param name="request">Genomic markers and target HRT regimen request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Evaluated genomic profile and adjusted endocrine target setpoints.</returns>
    public async Task<GenomicAdjustmentResult> ApplyGenomicAdjustmentsAsync(
        GenomicAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a pharmacogenomics evaluation request to adjust target endocrine setpoints.
        // WHY:  Modulates burst release rates and clamps target hormone levels when thrombophilic alleles are present.
        // HOW:  Validates request, resolves credentials, sets busy indicator, awaits client, and updates observable state.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ApplyGenomicAdjustmentsAsync(profile, token, request, ct).ConfigureAwait(false);
            LastAdjustmentResult = result;
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
    /// Retrieves digitized DNA nanocarrier fabrication regimens for a patient with optional filters.
    /// </summary>
    /// <param name="username">Target patient identifier.</param>
    /// <param name="regimen">Optional therapeutic regimen filter ("MaleToFemale", "FemaleToMale", "MtF", or "FtM").</param>
    /// <param name="geometry">Optional nanocarrier geometry filter.</param>
    /// <param name="targetKd">Optional target aptamer dissociation constant filter in nanomolar.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete patient Digital DNA package including staple oligos and annealing ramps.</returns>
    public async Task<PatientDigitalDnaPackage> ListPatientDnaAsync(
        string username,
        string? regimen = null,
        string? geometry = null,
        double? targetKd = null,
        CancellationToken ct = default)
    {
        // WHAT: Fetches personalized Digital DNA fabrication package matching specified criteria.
        // WHY:  Allows clinicians to inspect staple oligo routing, annealing schedules, and Kd targets.
        // HOW:  Validates username, parses regimen string, resolves session credentials, invokes client, and records package.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        TherapeuticRegimen? parsedRegimen = null;
        if (!string.IsNullOrWhiteSpace(regimen))
        {
            if (string.Equals(regimen, "FemaleToMale", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(regimen, "FtM", StringComparison.OrdinalIgnoreCase))
            {
                parsedRegimen = TherapeuticRegimen.FemaleToMale;
            }
            else if (string.Equals(regimen, "MaleToFemale", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(regimen, "MtF", StringComparison.OrdinalIgnoreCase))
            {
                parsedRegimen = TherapeuticRegimen.MaleToFemale;
            }
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetPatientDnaRegimensAsync(
                profile,
                token,
                username,
                parsedRegimen,
                geometry,
                targetKd,
                ct).ConfigureAwait(false);

            LastPatientDnaPackage = result;
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
    /// Registers a digitized DNA nanocarrier blueprint for a patient from a package model.
    /// </summary>
    /// <param name="username">Target patient identifier.</param>
    /// <param name="package">The Digital DNA blueprint package to register.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="PatientDigitalDnaRecord"/> record.</returns>
    public async Task<PatientDigitalDnaRecord> CreatePatientDnaAsync(
        string username,
        PatientDigitalDnaPackage package,
        CancellationToken ct = default)
    {
        // WHAT: Maps an incoming PatientDigitalDnaPackage to CreatePatientDnaRequest and dispatches registration.
        // WHY:  Allows client applications to commit finalized fabrication blueprints to the patient ledger.
        // HOW:  Validates inputs, builds CreatePatientDnaRequest, and delegates to the request overload.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(package);

        string title = !string.IsNullOrWhiteSpace(package.TargetTissue)
            ? $"{package.NanocarrierGeometry} ({package.TargetTissue})"
            : package.NanocarrierGeometry;

        var request = new CreatePatientDnaRequest
        {
            Title = title,
            Regimen = package.Regimen,
            TargetTissue = package.TargetTissue,
            ActiveLigand = package.ActiveLigand,
            NanocarrierGeometry = package.NanocarrierGeometry,
            TargetAptamerKdNm = package.TargetAptamerKdNm,
            ScaffoldFasta = package.ScaffoldFasta
        };

        return await CreatePatientDnaAsync(username, request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a digitized DNA nanocarrier blueprint for a patient from an explicit request model.
    /// </summary>
    /// <param name="username">Target patient identifier.</param>
    /// <param name="request">DNA blueprint synthesis parameters and nanocarrier specifications.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="PatientDigitalDnaRecord"/> record.</returns>
    public async Task<PatientDigitalDnaRecord> CreatePatientDnaAsync(
        string username,
        CreatePatientDnaRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a CreatePatientDnaRequest to the connected node client.
        // WHY:  Persists a digitized DNA nanocarrier architecture to the patient's record registry.
        // HOW:  Validates username and request, resolves credentials, sets busy indicator, and captures result.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.CreatePatientDnaAsync(profile, token, username, request, ct).ConfigureAwait(false);
            LastCreatedDnaRecord = result;
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
            throw new InvalidOperationException("An active instance profile is required to execute clinical genomics operations.");
        }

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute clinical genomics operations.");
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
