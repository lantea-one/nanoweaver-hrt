using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Gui.Core.Theme;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// Aggregate clinical safety severity classification derived from active depot calibration guardrails
/// and wet-lab fabrication lot quarantine or revocation states.
/// </summary>
public enum ClinicalWorkspaceSeverity
{
    /// <summary>
    /// No active formulations or lots have been loaded yet.
    /// </summary>
    Idle = 0,

    /// <summary>
    /// All evaluated longitudinal assays and fabrication lots are within physiological and QC guardrails.
    /// </summary>
    Optimal = 1,

    /// <summary>
    /// One or more advisory drift notices, warning guardrails, or quarantined lots require clinical review.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// A critical clinical safety ceiling, revoked lot, or untrusted/tampered certificate requires immediate attention.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Immutable clinical workspace summary snapshot projecting active depot formulation counts,
/// adaptive re-dosing guidance, wet-lab lot release status totals, and sovereign dark-mode palette accents.
/// </summary>
/// <param name="ActivePatientId">Active patient identifier currently loaded in the workspace.</param>
/// <param name="FormulationCount">Total persisted semi-annual depot formulations for the patient.</param>
/// <param name="AssayCount">Total longitudinal serum hormone assays recorded for the selected depot.</param>
/// <param name="TotalLotCount">Total wet-lab fabrication lots associated with the patient.</param>
/// <param name="ReleasedLotCount">Number of lots cryptographically released with ML-DSA-65 Certificates of Analysis.</param>
/// <param name="QuarantinedLotCount">Number of lots blocked in quarantine after failing a 5-gate wet-lab QC check.</param>
/// <param name="RevokedLotCount">Number of lots permanently revoked via clinical recall.</param>
/// <param name="ActiveSafetyAlertCount">Number of active clinical safety guardrail alerts in the latest calibration.</param>
/// <param name="AdjustedRedoseDay">Adjusted re-dosing day from the latest calibration, or <see langword="null"/> when uncalibrated.</param>
/// <param name="RecommendedNextMassMg">Recommended next-cycle nanocarrier payload mass in milligrams, or <see langword="null"/>.</param>
/// <param name="Severity">Aggregate clinical severity classification across calibration and lot statuses.</param>
/// <param name="SeverityAccentHex">Sovereign high-contrast dark-mode hex color code corresponding to <paramref name="Severity"/>.</param>
/// <param name="FailedPeerSyncCount">Number of subscribed federated peer revocation sync endpoints currently in <see cref="FederatedPeerSyncStatus.Failed"/> state.</param>
/// <param name="NewFederatedRecallCount">Number of newly discovered federated recall tombstones imported by the most recent pull-sync operation.</param>
/// <param name="CompromisedColdChainLotCount">Number of inspected lots flagged thermally compromised, expired, or custody-tampered (fail-closed unsafe for implantation).</param>
/// <param name="CriticalPharmacovigilanceLotCount">Number of inspected lots flagged for fail-closed quarantine by post-implantation pharmacovigilance or CARPA/TLR9 signals.</param>
public sealed record DepotWorkspaceSummary(
    string? ActivePatientId,
    int FormulationCount,
    int AssayCount,
    int TotalLotCount,
    int ReleasedLotCount,
    int QuarantinedLotCount,
    int RevokedLotCount,
    int ActiveSafetyAlertCount,
    int? AdjustedRedoseDay,
    double? RecommendedNextMassMg,
    ClinicalWorkspaceSeverity Severity,
    string SeverityAccentHex,
    int FailedPeerSyncCount = 0,
    int NewFederatedRecallCount = 0,
    int CompromisedColdChainLotCount = 0,
    int CriticalPharmacovigilanceLotCount = 0);

/// <summary>
/// Native cross-platform MVVM ViewModel unifying Milestones 33–36 clinical workflows:
/// semi-annual 180-day depot formulations, longitudinal serum assay calibration, wet-lab 5-gate QC lot release,
/// portable ML-DSA-65 Certificate of Analysis downloads, cross-node trust verification, and permanent lot revocation.
/// </summary>
/// <remarks>
/// // WHAT: Dedicated reactive presentation ViewModel powering native desktop and mobile Depot &amp; Lot Studio screens.
/// // WHY:  Milestone 37 (P195): Gives Avalonia desktop and mobile shells first-class clinical workflow coordination,
/// //       zero-LINQ KPI aggregation, localized summaries, and deterministic memory hygiene on idle/background lock.
/// // HOW:  Delegates authenticated REST operations to <see cref="INanoWeaverNodeClient"/> using the active session
/// //       profile and bearer token, tracks selected depot/lot state, and emits <see cref="INotifyPropertyChanged"/> updates.
/// </remarks>
public sealed class DepotAndLotWorkspaceViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// JSON serializer options matching NanoWeaver node camelCase REST contracts for portable CoA deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions s_certificateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Authenticated REST client targeting connected NanoWeaver HRT nodes.
    /// </summary>
    private readonly INanoWeaverNodeClient _client;

    /// <summary>
    /// Callback returning the currently selected <see cref="InstanceProfile"/> from the parent shell.
    /// </summary>
    private readonly Func<InstanceProfile?> _getActiveProfile;

    /// <summary>
    /// Callback returning the currently active unprotected bearer token from the parent shell.
    /// </summary>
    private readonly Func<string?> _getActiveToken;

    /// <summary>
    /// Backing field for <see cref="IsBusy"/>.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Backing field for <see cref="ErrorMessage"/>.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Backing field for <see cref="ActivePatientId"/>.
    /// </summary>
    private string? _activePatientId;

    /// <summary>
    /// Backing field for <see cref="SelectedDepotId"/>.
    /// </summary>
    private string? _selectedDepotId;

    /// <summary>
    /// Backing field for <see cref="SelectedLotId"/>.
    /// </summary>
    private string? _selectedLotId;

    /// <summary>
    /// Backing field for <see cref="Formulations"/>.
    /// </summary>
    private IReadOnlyList<DepotFormulationResponse> _formulations = [];

    /// <summary>
    /// Backing field for <see cref="ActiveSimulation"/>.
    /// </summary>
    private TimeReleaseSimulationResult? _activeSimulation;

    /// <summary>
    /// Backing field for <see cref="RecordedAssays"/>.
    /// </summary>
    private IReadOnlyList<DepotAssayObservationResponse> _recordedAssays = [];

    /// <summary>
    /// Backing field for <see cref="LatestCalibration"/>.
    /// </summary>
    private DepotCalibrationResponse? _latestCalibration;

    /// <summary>
    /// Backing field for <see cref="FabricationLots"/>.
    /// </summary>
    private IReadOnlyList<FabricationLotResponse> _fabricationLots = [];

    /// <summary>
    /// Backing field for <see cref="LatestLotRelease"/>.
    /// </summary>
    private FabricationLotReleaseResponse? _latestLotRelease;

    /// <summary>
    /// Backing field for <see cref="LatestLotCertificate"/>.
    /// </summary>
    private FabricationLotCertificateResponse? _latestLotCertificate;

    /// <summary>
    /// Backing field for <see cref="LastCertificateVerification"/>.
    /// </summary>
    private FabricationCertificateVerificationReport? _lastCertificateVerification;

    /// <summary>
    /// Backing field for <see cref="LastRevocation"/>.
    /// </summary>
    private FabricationLotRevocationResponse? _lastRevocation;

    /// <summary>
    /// Backing field for <see cref="LastDownloadedCertificateBytes"/>.
    /// </summary>
    private byte[]? _lastDownloadedCertificateBytes;

    /// <summary>
    /// Occurs when an observable ViewModel property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotAndLotWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">Strongly typed REST API client for node communication.</param>
    /// <param name="getActiveProfile">Delegate returning the currently selected instance profile.</param>
    /// <param name="getActiveToken">Delegate returning the active bearer authentication token.</param>
    public DepotAndLotWorkspaceViewModel(
        INanoWeaverNodeClient client,
        Func<InstanceProfile?> getActiveProfile,
        Func<string?> getActiveToken)
    {
        // WHAT: Stores REST client and active session resolver callbacks.
        // WHY:  Keeps this workspace synchronized with multi-instance selection and session lock state.
        // HOW:  Validates non-null constructor parameters with ArgumentNullException.ThrowIfNull.
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _getActiveProfile = getActiveProfile ?? throw new ArgumentNullException(nameof(getActiveProfile));
        _getActiveToken = getActiveToken ?? throw new ArgumentNullException(nameof(getActiveToken));
    }

    /// <summary>
    /// Gets a value indicating whether an asynchronous clinical operation is currently in flight.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the most recent error message captured from a failed clinical or cryptographic operation.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the patient identifier currently targeted by the native depot workspace.
    /// </summary>
    public string? ActivePatientId
    {
        get => _activePatientId;
        set
        {
            if (!string.Equals(_activePatientId, value, StringComparison.Ordinal))
            {
                _activePatientId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the currently selected semi-annual depot formulation identifier.
    /// </summary>
    public string? SelectedDepotId
    {
        get => _selectedDepotId;
        set
        {
            if (!string.Equals(_selectedDepotId, value, StringComparison.Ordinal))
            {
                _selectedDepotId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the currently selected wet-lab fabrication lot identifier.
    /// </summary>
    public string? SelectedLotId
    {
        get => _selectedLotId;
        set
        {
            if (!string.Equals(_selectedLotId, value, StringComparison.Ordinal))
            {
                _selectedLotId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the list of persisted semi-annual depot formulations loaded for <see cref="ActivePatientId"/>.
    /// </summary>
    public IReadOnlyList<DepotFormulationResponse> Formulations
    {
        get => _formulations;
        private set
        {
            _formulations = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the latest simulated 180-day multi-phase depot release trajectory.
    /// </summary>
    public TimeReleaseSimulationResult? ActiveSimulation
    {
        get => _activeSimulation;
        private set
        {
            _activeSimulation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the chronological list of longitudinal serum hormone assays loaded for <see cref="SelectedDepotId"/>.
    /// </summary>
    public IReadOnlyList<DepotAssayObservationResponse> RecordedAssays
    {
        get => _recordedAssays;
        private set
        {
            _recordedAssays = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the latest adaptive Bayesian longitudinal calibration snapshot for <see cref="SelectedDepotId"/>.
    /// </summary>
    public DepotCalibrationResponse? LatestCalibration
    {
        get => _latestCalibration;
        private set
        {
            _latestCalibration = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the wet-lab nanocarrier fabrication lots loaded for <see cref="ActivePatientId"/>.
    /// </summary>
    public IReadOnlyList<FabricationLotResponse> FabricationLots
    {
        get => _fabricationLots;
        private set
        {
            _fabricationLots = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recent 5-gate wet-lab lot release evaluation result.
    /// </summary>
    public FabricationLotReleaseResponse? LatestLotRelease
    {
        get => _latestLotRelease;
        private set
        {
            _latestLotRelease = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recently retrieved and cryptographically verified ML-DSA-65 Certificate of Analysis.
    /// </summary>
    public FabricationLotCertificateResponse? LatestLotCertificate
    {
        get => _latestLotCertificate;
        private set
        {
            _latestLotCertificate = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recent cross-node ML-DSA-65 trust-anchor and revocation verification verdict.
    /// </summary>
    public FabricationCertificateVerificationReport? LastCertificateVerification
    {
        get => _lastCertificateVerification;
        private set
        {
            _lastCertificateVerification = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recent permanent fabrication lot revocation response.
    /// </summary>
    public FabricationLotRevocationResponse? LastRevocation
    {
        get => _lastRevocation;
        private set
        {
            _lastRevocation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recently downloaded portable Certificate of Analysis UTF-8 JSON bytes, if cached.
    /// </summary>
    public byte[]? LastDownloadedCertificateBytes
    {
        get => _lastDownloadedCertificateBytes;
        private set
        {
            _lastDownloadedCertificateBytes = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Loads the complete patient clinical depot and fabrication lot workspace from the connected node,
    /// populating formulations, longitudinal assays, latest calibration, and wet-lab fabrication lots.
    /// </summary>
    /// <param name="patientId">Target patient username or identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed <see cref="DepotWorkspaceSummary"/> after synchronization.</returns>
    public async Task<DepotWorkspaceSummary> LoadPatientWorkspaceAsync(
        string patientId,
        CancellationToken ct = default)
    {
        // WHAT: Fetches formulations, fabrication lots, and selected depot longitudinal calibration state in one flow.
        // WHY:  Provides a single deterministic startup command when opening the native Depot & Lot Studio screen.
        // HOW:  Resolves session, queries formulations and lots, selects the newest depot/lot, and loads assays + calibration.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ActivePatientId = patientId;
            IReadOnlyList<DepotFormulationResponse> formulations = await _client.GetDepotFormulationsAsync(profile, token, patientId, ct).ConfigureAwait(false);
            IReadOnlyList<FabricationLotResponse> lots = await _client.GetFabricationLotsAsync(profile, token, patientId, ct).ConfigureAwait(false);

            Formulations = formulations;
            FabricationLots = lots;

            if (formulations.Count > 0)
            {
                SelectedDepotId = formulations[0].Formulation.DepotId;
                RecordedAssays = await _client.GetDepotAssaysAsync(profile, token, SelectedDepotId, ct).ConfigureAwait(false);
                LatestCalibration = await _client.GetLatestDepotCalibrationAsync(profile, token, SelectedDepotId, ct).ConfigureAwait(false);
            }
            else
            {
                SelectedDepotId = null;
                RecordedAssays = [];
                LatestCalibration = null;
            }

            SelectedLotId = lots.Count > 0 ? lots[0].LotId : null;
            return BuildSummary();
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
    /// Creates a new semi-annual depot formulation and immediately simulates its 180-day multi-phase release curve.
    /// </summary>
    /// <param name="createRequest">Formulation creation payload.</param>
    /// <param name="simulateRequest">Multi-phase release simulation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="DepotFormulationResponse"/>.</returns>
    public async Task<DepotFormulationResponse> CreateFormulationAndSimulateAsync(
        CreateDepotFormulationRequest createRequest,
        SimulateDepotReleaseRequest simulateRequest,
        CancellationToken ct = default)
    {
        // WHAT: Registers a new depot formulation and runs its companion 180-day pharmacokinetic simulation.
        // WHY:  Mirrors the web Depot Studio one-click design workflow in native desktop and mobile shells.
        // HOW:  Persists the formulation, refreshes the patient's formulation list, updates selection, and executes simulation.
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(simulateRequest);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            DepotFormulationResponse created = await _client.CreateDepotFormulationAsync(profile, token, createRequest, ct).ConfigureAwait(false);
            TimeReleaseSimulationResult simulation = await _client.SimulateDepotReleaseAsync(profile, token, simulateRequest, ct).ConfigureAwait(false);

            ActivePatientId = created.Formulation.PatientId;
            SelectedDepotId = created.Formulation.DepotId;
            ActiveSimulation = simulation;
            Formulations = await _client.GetDepotFormulationsAsync(profile, token, created.Formulation.PatientId, ct).ConfigureAwait(false);
            return created;
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
    /// Records a longitudinal serum hormone assay against <see cref="SelectedDepotId"/> and immediately runs
    /// adaptive Bayesian recalibration.
    /// </summary>
    /// <param name="request">Longitudinal blood hormone assay payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="DepotCalibrationResponse"/> snapshot.</returns>
    public async Task<DepotCalibrationResponse> RecordAssayAndCalibrateAsync(
        RecordDepotAssayRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Records a new follow-up blood draw and recalibrates patient clearance, erosion, and safety alerts.
        // WHY:  Milestone 34 closed-loop clinical cadence requires assay persistence followed by immediate trajectory update.
        // HOW:  Validates selected depot, posts assay, refreshes assay table, runs calibration, and updates ActiveSimulation.
        ArgumentNullException.ThrowIfNull(request);
        string depotId = RequireSelectedDepotId();
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RecordDepotAssayAsync(profile, token, depotId, request, ct).ConfigureAwait(false);
            RecordedAssays = await _client.GetDepotAssaysAsync(profile, token, depotId, ct).ConfigureAwait(false);
            DepotCalibrationResponse calibration = await _client.CalibrateDepotAsync(profile, token, depotId, ct).ConfigureAwait(false);
            LatestCalibration = calibration;
            ActiveSimulation = calibration.RecalibratedTrajectory;
            return calibration;
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
    /// Mints a new patient-specific wet-lab fabrication lot and selects it in the workspace.
    /// </summary>
    /// <param name="request">Wet-lab fabrication lot creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="FabricationLotResponse"/>.</returns>
    public async Task<FabricationLotResponse> MintFabricationLotAsync(
        CreateFabricationLotRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Mints a new physical wet-lab fabrication lot linked to an active depot formulation.
        // WHY:  Bridges digital endocrine calibration to benchtop Opentrons synthesis and 5-gate QC tracking.
        // HOW:  Calls CreateFabricationLotAsync, selects the newly minted lot, and refreshes the patient lot ledger.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FabricationLotResponse lot = await _client.CreateFabricationLotAsync(profile, token, request, ct).ConfigureAwait(false);
            ActivePatientId = lot.PatientId;
            SelectedDepotId = lot.DepotId;
            SelectedLotId = lot.LotId;
            FabricationLots = await _client.GetFabricationLotsAsync(profile, token, lot.PatientId, ct).ConfigureAwait(false);
            return lot;
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
    /// Records a single pharmaceutical wet-lab QC measurement against <see cref="SelectedLotId"/>.
    /// </summary>
    /// <param name="request">Wet-lab QC gate measurement payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="FabricationQcCheckResponse"/>.</returns>
    public async Task<FabricationQcCheckResponse> RecordQcMeasurementAsync(
        RecordFabricationQcRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Logs a wet-lab assay result (gel yield, LAL endotoxin, DLS size/PDI, encapsulation, or CpG methylation).
        // WHY:  Populates the 5-gate pharmaceutical verification checklist prior to cryptographic lot release.
        // HOW:  Validates SelectedLotId, calls RecordFabricationQcAsync, and refreshes the lot ledger when ActivePatientId is set.
        ArgumentNullException.ThrowIfNull(request);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FabricationQcCheckResponse check = await _client.RecordFabricationQcAsync(profile, token, lotId, request, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(_activePatientId))
            {
                FabricationLots = await _client.GetFabricationLotsAsync(profile, token, _activePatientId, ct).ConfigureAwait(false);
            }

            return check;
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
    /// Executes 5-gate wet-lab release verification and NIST FIPS 204 ML-DSA-65 signing on <see cref="SelectedLotId"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="FabricationLotReleaseResponse"/> indicating Released or Quarantined outcome.</returns>
    public async Task<FabricationLotReleaseResponse> ReleaseSelectedLotAsync(CancellationToken ct = default)
    {
        // WHAT: Evaluates all required wet-lab QC gates and signs an ML-DSA-65 Certificate of Analysis if all pass.
        // WHY:  Enforces fail-closed pharmaceutical safety gating before clinical implantation.
        // HOW:  Invokes ReleaseFabricationLotAsync, updates LatestLotRelease, fetches the verified certificate when released, and refreshes lots.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FabricationLotReleaseResponse release = await _client.ReleaseFabricationLotAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LatestLotRelease = release;

            if (release.Lot.LotStatus == FabricationLotStatus.Released)
            {
                LatestLotCertificate = await _client.GetFabricationLotCertificateAsync(profile, token, lotId, ct).ConfigureAwait(false);
            }
            else
            {
                LatestLotCertificate = null;
            }

            if (!string.IsNullOrWhiteSpace(_activePatientId))
            {
                FabricationLots = await _client.GetFabricationLotsAsync(profile, token, _activePatientId, ct).ConfigureAwait(false);
            }

            return release;
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
    /// Permanently revokes <see cref="SelectedLotId"/> with an auditable clinical recall reason.
    /// </summary>
    /// <param name="reason">Plain-English clinical or cold-chain recall reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The permanent <see cref="FabricationLotRevocationResponse"/>.</returns>
    public async Task<FabricationLotRevocationResponse> RevokeSelectedLotAsync(
        string reason,
        CancellationToken ct = default)
    {
        // WHAT: Permanently recalls the selected fabrication lot and invalidates future clinical verification.
        // WHY:  Supports emergency lot revocation directly from Administrator desktop and mobile consoles.
        // HOW:  Validates reason and lot selection, calls RevokeFabricationLotAsync, clears stale certificate cache, and refreshes lots.
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var req = new RevokeFabricationLotRequest { Reason = reason.Trim() };
            FabricationLotRevocationResponse revocation = await _client.RevokeFabricationLotAsync(profile, token, lotId, req, ct).ConfigureAwait(false);
            LastRevocation = revocation;
            LatestLotCertificate = null;
            if (!string.IsNullOrWhiteSpace(_activePatientId))
            {
                FabricationLots = await _client.GetFabricationLotsAsync(profile, token, _activePatientId, ct).ConfigureAwait(false);
            }

            return revocation;
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
    /// Downloads the portable UTF-8 JSON Certificate of Analysis bundle for <see cref="SelectedLotId"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw UTF-8 JSON bytes if the lot has a signed certificate; otherwise <see langword="null"/>.</returns>
    public async Task<byte[]?> DownloadSelectedCertificateBundleAsync(CancellationToken ct = default)
    {
        // WHAT: Fetches the signed portable CoA JSON bundle for offline export or cross-node handoff.
        // WHY:  Enables air-gapped clinical provenance sharing from native desktop and mobile file systems.
        // HOW:  Validates SelectedLotId, wipes any previously cached bytes, downloads fresh bytes, and stores them.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            WipeCertificateBytes();
            byte[]? bundle = await _client.DownloadFabricationCertificateBundleAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LastDownloadedCertificateBytes = bundle;
            return bundle;
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
    /// Parses a portable JSON Certificate of Analysis payload and verifies its ML-DSA-65 signature,
    /// peer trust anchor, and revocation status against the connected node.
    /// </summary>
    /// <param name="certificateJson">Raw JSON string representing a <see cref="FabricationLotCertificate"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authoritative <see cref="FabricationCertificateVerificationReport"/>.</returns>
    public async Task<FabricationCertificateVerificationReport> VerifyImportedCertificateJsonAsync(
        string certificateJson,
        CancellationToken ct = default)
    {
        // WHAT: Deserializes a portable CoA JSON string and submits it to the node's cross-node ML-DSA-65 verifier.
        // WHY:  Lets receiving clinicians verify imported peer laboratory certificates natively without a browser.
        // HOW:  Parses JSON with camelCase-insensitive options, validates non-null certificate, invokes VerifyFabricationCertificateAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateJson);
        FabricationLotCertificate? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FabricationLotCertificate>(certificateJson, s_certificateJsonOptions);
        }
        catch (JsonException ex)
        {
            ErrorMessage = ex.Message;
            throw new InvalidOperationException("The supplied Certificate of Analysis JSON is malformed.", ex);
        }

        if (parsed == null)
        {
            const string nullMsg = "The supplied Certificate of Analysis JSON deserialized to null.";
            ErrorMessage = nullMsg;
            throw new InvalidOperationException(nullMsg);
        }

        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FabricationCertificateVerificationReport report = await _client.VerifyFabricationCertificateAsync(profile, token, parsed, ct).ConfigureAwait(false);
            LastCertificateVerification = report;
            return report;
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
    /// Computes a deterministic, zero-LINQ <see cref="DepotWorkspaceSummary"/> aggregating current formulations,
    /// longitudinal safety guardrails, wet-lab lot statuses, and sovereign palette severity color.
    /// </summary>
    /// <returns>Immutable <see cref="DepotWorkspaceSummary"/> snapshot.</returns>
    public DepotWorkspaceSummary BuildSummary()
    {
        // WHAT: Aggregates workspace KPI counts and highest clinical severity in a single allocation-free pass.
        // WHY:  Drives native header cards, desktop tray safety badges, and mobile compact status pills.
        // HOW:  Loops over FabricationLots with a simple for-loop, inspects LatestCalibration and LastCertificateVerification, and maps color.
        int released = 0;
        int quarantined = 0;
        int revoked = 0;

        for (int i = 0; i < _fabricationLots.Count; i++)
        {
            FabricationLotStatus status = _fabricationLots[i].LotStatus;
            if (status == FabricationLotStatus.Released)
            {
                released++;
            }
            else if (status == FabricationLotStatus.Quarantined)
            {
                quarantined++;
            }
            else if (status == FabricationLotStatus.Revoked)
            {
                revoked++;
            }
        }

        int safetyAlerts = _latestCalibration != null ? _latestCalibration.Alerts.Count : 0;
        int? redoseDay = _latestCalibration != null ? _latestCalibration.AdjustedRedoseDay : null;
        double? nextMass = _latestCalibration != null ? _latestCalibration.RecommendedNextMassMg : null;

        int failedPeerSyncs = 0;
        for (int i = 0; i < _peerSyncEndpoints.Count; i++)
        {
            if (_peerSyncEndpoints[i].LastStatus == FederatedPeerSyncStatus.Failed)
            {
                failedPeerSyncs++;
            }
        }

        int newFederatedRecalls = _lastPeerSyncSweep != null
            ? _lastPeerSyncSweep.TotalNewRevocationsImported
            : _lastPeerSyncRun != null
                ? _lastPeerSyncRun.ImportedCount
                : 0;

        int compromisedColdChain = _latestLotStability != null && !_latestLotStability.IsSafeForImplantation ? 1 : 0;
        int criticalPharmacovigilance = _latestPharmacovigilanceReport != null && _latestPharmacovigilanceReport.IsQuarantineTriggered ? 1 : 0;

        ClinicalWorkspaceSeverity severity = ResolveWorkspaceSeverity(
            quarantined,
            revoked,
            failedPeerSyncs,
            newFederatedRecalls,
            compromisedColdChain,
            criticalPharmacovigilance);
        string accentHex = MapSeverityToPaletteHex(severity);

        return new DepotWorkspaceSummary(
            ActivePatientId: _activePatientId,
            FormulationCount: _formulations.Count,
            AssayCount: _recordedAssays.Count,
            TotalLotCount: _fabricationLots.Count,
            ReleasedLotCount: released,
            QuarantinedLotCount: quarantined,
            RevokedLotCount: revoked,
            ActiveSafetyAlertCount: safetyAlerts,
            AdjustedRedoseDay: redoseDay,
            RecommendedNextMassMg: nextMass,
            Severity: severity,
            SeverityAccentHex: accentHex,
            FailedPeerSyncCount: failedPeerSyncs,
            NewFederatedRecallCount: newFederatedRecalls,
            CompromisedColdChainLotCount: compromisedColdChain,
            CriticalPharmacovigilanceLotCount: criticalPharmacovigilance);
    }

    /// <summary>
    /// Maps a <see cref="FabricationCertificateVerificationReport"/> to its sovereign high-contrast dark-mode
    /// badge accent hex color code.
    /// </summary>
    /// <param name="report">Verification report to classify, or <see langword="null"/> when none has been evaluated.</param>
    /// <returns>Sovereign palette hex string.</returns>
    public static string ResolveVerificationAccentHex(FabricationCertificateVerificationReport? report)
    {
        // WHAT: Selects the sovereign dark-mode accent hex color for a cross-node CoA verification verdict.
        // WHY:  Enforces visual parity between web Depot Studio and native Avalonia desktop/mobile trust banners.
        // HOW:  Returns dim/neutral when null, magenta for revoked/invalid/revoked-signer, amber for untrusted signer, green when trusted.
        if (report == null)
        {
            return SovereignPalette.TextMutedHex;
        }

        if (report.IsRevoked ||
            report.TrustSource == FabricationCertificateTrustSource.InvalidSignature ||
            report.TrustSource == FabricationCertificateTrustSource.RevokedSigner)
        {
            return SovereignPalette.AlertMagentaHex;
        }

        if (report.TrustSource == FabricationCertificateTrustSource.UntrustedSigner)
        {
            return SovereignPalette.WarningAmberHex;
        }

        return SovereignPalette.SuccessGreenHex;
    }

    /// <summary>
    /// Formats a concise localized status summary string suitable for desktop headers, tooltips, and mobile cards.
    /// </summary>
    /// <param name="localizer">Localization provider resolving translated labels across all 18 community languages.</param>
    /// <param name="language">Target display language.</param>
    /// <returns>Localized status summary string.</returns>
    public string BuildLocalizedStatusSummary(IWeaverLocalizer localizer, WeaverLanguage language)
    {
        // WHAT: Builds a single-line localized clinical status banner combining depot count, lot count, and severity label.
        // WHY:  Provides consistent 18-language status text across desktop, mobile, and system tray surfaces.
        // HOW:  Computes BuildSummary(), resolves the severity localization key via IWeaverLocalizer, and formats invariants.
        ArgumentNullException.ThrowIfNull(localizer);

        DepotWorkspaceSummary summary = BuildSummary();
        string severityKey = summary.Severity switch
        {
            ClinicalWorkspaceSeverity.Critical => "Gui.DepotWorkspace.SeverityCritical",
            ClinicalWorkspaceSeverity.Warning => "Gui.DepotWorkspace.SeverityWarning",
            ClinicalWorkspaceSeverity.Optimal => "Gui.DepotWorkspace.SeverityOptimal",
            _ => "Gui.DepotWorkspace.SeverityIdle"
        };

        string severityLabel = ResolveLocalizedOrFallback(localizer, language, severityKey, summary.Severity.ToString());
        string format = ResolveLocalizedOrFallback(
            localizer,
            language,
            "Gui.DepotWorkspace.SummaryFormat",
            "{0} — Depots: {1}, Lots: {2} (Released: {3}, Alerts: {4})");

        return string.Format(
            CultureInfo.InvariantCulture,
            format,
            severityLabel,
            summary.FormulationCount,
            summary.TotalLotCount,
            summary.ReleasedLotCount,
            summary.ActiveSafetyAlertCount);
    }

    /// <summary>
    /// Cryptographically zeroes cached certificate bytes and purges volatile clinical state on session lock,
    /// idle timeout, or mobile backgrounding.
    /// </summary>
    public void ClearSensitiveClinicalCache()
    {
        // WHAT: Wipes cached CoA JSON bytes and clears patient-scoped clinical state from memory.
        // WHY:  Prime Directive 12: Prevents residual PHI or signed certificate artifacts from lingering after lock/background.
        // HOW:  Zeroizes _lastDownloadedCertificateBytes via CryptographicOperations.ZeroMemory and resets observable fields.
        WipeCertificateBytes();
        ActivePatientId = null;
        SelectedDepotId = null;
        SelectedLotId = null;
        Formulations = [];
        ActiveSimulation = null;
        RecordedAssays = [];
        LatestCalibration = null;
        FabricationLots = [];
        LatestLotRelease = null;
        LatestLotCertificate = null;
        LastCertificateVerification = null;
        LastRevocation = null;
        LatestExportedRevocationFeed = null;
        LastRevocationFeedImport = null;
        FederatedRevocations = [];
        PeerSyncEndpoints = [];
        LastPeerSyncRun = null;
        LastPeerSyncSweep = null;
        LatestLotStability = null;
        LatestCustodyLedger = null;
        LatestPharmacovigilanceReport = null;
        ActiveLotAdverseEvents = [];
        ErrorMessage = null;
    }

    /// <summary>
    /// Resolves the highest clinical severity across calibration guardrails, lot states, certificate verification,
    /// federated peer revocation pull-sync telemetry, cold-chain thermal stability, and post-implantation pharmacovigilance.
    /// </summary>
    /// <param name="quarantinedCount">Count of quarantined lots.</param>
    /// <param name="revokedCount">Count of revoked lots.</param>
    /// <param name="failedPeerSyncCount">Count of federated peer sync endpoints in Failed state.</param>
    /// <param name="newFederatedRecallCount">Count of newly imported federated recall tombstones.</param>
    /// <param name="compromisedColdChainCount">Count of lots flagged thermally compromised, expired, or custody-tampered.</param>
    /// <param name="criticalPharmacovigilanceCount">Count of lots flagged for CARPA/CTCAE pharmacovigilance quarantine.</param>
    /// <returns>The resolved <see cref="ClinicalWorkspaceSeverity"/>.</returns>
    private ClinicalWorkspaceSeverity ResolveWorkspaceSeverity(
        int quarantinedCount,
        int revokedCount,
        int failedPeerSyncCount,
        int newFederatedRecallCount,
        int compromisedColdChainCount,
        int criticalPharmacovigilanceCount)
    {
        // WHAT: Evaluates fail-closed clinical priority ordering (Critical > Warning > Optimal > Idle).
        // WHY:  Guarantees severe clinical alerts, recalled batches, compromised cold-chain lots, CARPA quarantines, or newly imported federated recalls are never masked.
        // HOW:  Checks critical safety ceiling, revoked lots, failed verification, compromised cold chain, CARPA quarantine, and new federated recalls first, then warnings, then loaded state.
        bool calibrationCritical = _latestCalibration != null && _latestCalibration.OverallSafetyStatus == DepotSafetyStatus.CriticalSafety;
        bool verificationCritical = _lastCertificateVerification != null &&
            (_lastCertificateVerification.IsRevoked ||
             _lastCertificateVerification.TrustSource == FabricationCertificateTrustSource.InvalidSignature ||
             _lastCertificateVerification.TrustSource == FabricationCertificateTrustSource.RevokedSigner);

        if (calibrationCritical || revokedCount > 0 || verificationCritical || newFederatedRecallCount > 0 || compromisedColdChainCount > 0 || criticalPharmacovigilanceCount > 0)
        {
            return ClinicalWorkspaceSeverity.Critical;
        }

        bool calibrationWarning = _latestCalibration != null &&
            (_latestCalibration.OverallSafetyStatus == DepotSafetyStatus.WarningEscape ||
             _latestCalibration.OverallSafetyStatus == DepotSafetyStatus.AdvisoryDrift);
        bool verificationWarning = _lastCertificateVerification != null &&
            _lastCertificateVerification.TrustSource == FabricationCertificateTrustSource.UntrustedSigner;
        bool coldChainAdvisory = _latestLotStability != null && _latestLotStability.Status == ThermalStabilityStatus.AdvisoryExcursion;
        bool pvMonitor = _latestPharmacovigilanceReport != null && _latestPharmacovigilanceReport.Posture == PharmacovigilancePosture.Monitor;

        if (calibrationWarning || quarantinedCount > 0 || verificationWarning || failedPeerSyncCount > 0 || coldChainAdvisory || pvMonitor)
        {
            return ClinicalWorkspaceSeverity.Warning;
        }

        if (_formulations.Count > 0 || _fabricationLots.Count > 0 || _latestCalibration != null)
        {
            return ClinicalWorkspaceSeverity.Optimal;
        }

        return ClinicalWorkspaceSeverity.Idle;
    }

    /// <summary>
    /// Maps a <see cref="ClinicalWorkspaceSeverity"/> level to its corresponding <see cref="SovereignPalette"/> hex color.
    /// </summary>
    /// <param name="severity">Clinical workspace severity level.</param>
    /// <returns>Hex color string from <see cref="SovereignPalette"/>.</returns>
    private static string MapSeverityToPaletteHex(ClinicalWorkspaceSeverity severity)
    {
        // WHAT: Translates workspace severity enum into high-contrast sovereign dark-mode hex token.
        // WHY:  Ensures consistent visual signaling across Avalonia desktop, mobile, and tray surfaces.
        // HOW:  Switches over ClinicalWorkspaceSeverity to pick Magenta, Amber, Green, or Muted Slate.
        return severity switch
        {
            ClinicalWorkspaceSeverity.Critical => SovereignPalette.AlertMagentaHex,
            ClinicalWorkspaceSeverity.Warning => SovereignPalette.WarningAmberHex,
            ClinicalWorkspaceSeverity.Optimal => SovereignPalette.SuccessGreenHex,
            _ => SovereignPalette.TextMutedHex
        };
    }

    /// <summary>
    /// Resolves a localization key or returns the specified fallback when missing or unresolved.
    /// </summary>
    /// <param name="localizer">Localization provider.</param>
    /// <param name="language">Target language.</param>
    /// <param name="key">Resource key.</param>
    /// <param name="fallback">Default English fallback string.</param>
    /// <returns>Resolved localized string or fallback.</returns>
    private static string ResolveLocalizedOrFallback(
        IWeaverLocalizer localizer,
        WeaverLanguage language,
        string key,
        string fallback)
    {
        // WHAT: Queries IWeaverLocalizer and falls back gracefully if the key is absent.
        // WHY:  Prevents raw resource key strings from leaking into clinical UI surfaces.
        // HOW:  Checks for empty or key-identical return values before returning fallback.
        string value = localizer.GetString(key, language);
        return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
            ? fallback
            : value;
    }

    /// <summary>
    /// Zeroizes and clears any cached portable Certificate of Analysis bytes.
    /// </summary>
    private void WipeCertificateBytes()
    {
        // WHAT: Overwrites cached CoA bytes with zeros and clears the reference.
        // WHY:  Prevents patient-linked certificate data from lingering in heap memory.
        // HOW:  Invokes CryptographicOperations.ZeroMemory when non-null and resets LastDownloadedCertificateBytes.
        if (_lastDownloadedCertificateBytes != null)
        {
            CryptographicOperations.ZeroMemory(_lastDownloadedCertificateBytes);
            LastDownloadedCertificateBytes = null;
        }
    }

    /// <summary>
    /// Validates and returns <see cref="SelectedDepotId"/>, throwing <see cref="InvalidOperationException"/> when unselected.
    /// </summary>
    /// <returns>Non-empty selected depot identifier.</returns>
    private string RequireSelectedDepotId()
    {
        // WHAT: Ensures a depot formulation is currently selected before dispatching depot-scoped commands.
        // WHY:  Prevents malformed API paths and provides clear clinical feedback.
        // HOW:  Checks string.IsNullOrWhiteSpace(_selectedDepotId) and throws InvalidOperationException if absent.
        if (string.IsNullOrWhiteSpace(_selectedDepotId))
        {
            throw new InvalidOperationException("No active depot formulation is selected.");
        }

        return _selectedDepotId;
    }

    /// <summary>
    /// Validates and returns <see cref="SelectedLotId"/>, throwing <see cref="InvalidOperationException"/> when unselected.
    /// </summary>
    /// <returns>Non-empty selected fabrication lot identifier.</returns>
    private string RequireSelectedLotId()
    {
        // WHAT: Ensures a wet-lab fabrication lot is selected before executing QC, release, download, or revocation commands.
        // WHY:  Guards against null/empty lot identifiers in API routes.
        // HOW:  Checks string.IsNullOrWhiteSpace(_selectedLotId) and throws InvalidOperationException if absent.
        if (string.IsNullOrWhiteSpace(_selectedLotId))
        {
            throw new InvalidOperationException("No active fabrication lot is selected.");
        }

        return _selectedLotId;
    }

    /// <summary>
    /// Resolves the active instance profile and bearer token from the parent shell, failing fast when unauthenticated.
    /// </summary>
    /// <returns>A tuple containing the active <see cref="InstanceProfile"/> and bearer token.</returns>
    private (InstanceProfile Profile, string Token) ResolveEffectiveSession()
    {
        // WHAT: Retrieves active node connection profile and bearer token for authenticated REST calls.
        // WHY:  Guarantees clinical operations fail cleanly when no node is connected or session has locked.
        // HOW:  Invokes _getActiveProfile and _getActiveToken delegates and throws InvalidOperationException if missing.
        InstanceProfile? profile = _getActiveProfile();
        if (profile == null)
        {
            throw new InvalidOperationException("No active NanoWeaver node instance is selected.");
        }

        string? token = _getActiveToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("No authenticated session token is available for the selected node.");
        }

        return (profile, token);
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for the specified property.
    /// </summary>
    /// <param name="propertyName">Name of the changed property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Dispatches INotifyPropertyChanged notification to bound Avalonia views.
        // WHY:  Drives reactive desktop and mobile UI updates without manual polling.
        // HOW:  Invokes PropertyChanged?.Invoke with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Backing field for <see cref="LatestExportedRevocationFeed"/>.
    /// </summary>
    private SignedLotRevocationFeed? _latestExportedRevocationFeed;

    /// <summary>
    /// Backing field for <see cref="LastRevocationFeedImport"/>.
    /// </summary>
    private ImportRevocationFeedResponse? _lastRevocationFeedImport;

    /// <summary>
    /// Backing field for <see cref="FederatedRevocations"/>.
    /// </summary>
    private IReadOnlyList<FederatedLotRevocationRecord> _federatedRevocations = [];

    /// <summary>
    /// Gets the most recently exported ML-DSA-65 signed lot revocation feed from the connected node.
    /// </summary>
    public SignedLotRevocationFeed? LatestExportedRevocationFeed
    {
        get => _latestExportedRevocationFeed;
        private set
        {
            _latestExportedRevocationFeed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the summary result of the most recent verified peer revocation feed import.
    /// </summary>
    public ImportRevocationFeedResponse? LastRevocationFeedImport
    {
        get => _lastRevocationFeedImport;
        private set
        {
            _lastRevocationFeedImport = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the list of verified federated peer lot recall tombstones synced on the connected node.
    /// </summary>
    public IReadOnlyList<FederatedLotRevocationRecord> FederatedRevocations
    {
        get => _federatedRevocations;
        private set
        {
            _federatedRevocations = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Pulls the connected node's ML-DSA-65 signed lot revocation feed via GET /api/fabrication/revocations/feed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signed <see cref="SignedLotRevocationFeed"/> ready for export to peer laboratories.</returns>
    public async Task<SignedLotRevocationFeed> ExportSignedRevocationFeedAsync(CancellationToken ct = default)
    {
        // WHAT: Fetches the local node's ML-DSA-65 signed revocation feed and updates LatestExportedRevocationFeed.
        // WHY:  Milestone 38 (P202): Lets clinicians export signed batch recall feeds from desktop and mobile shells.
        // HOW:  Resolves session credentials, calls GetSignedRevocationFeedAsync, and updates observable state.
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            SignedLotRevocationFeed feed = await _client.GetSignedRevocationFeedAsync(profile, token, ct).ConfigureAwait(false);
            LatestExportedRevocationFeed = feed;
            return feed;
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
    /// Parses a portable peer revocation feed JSON bundle, verifies &amp; imports it via POST /api/fabrication/revocations/import,
    /// and refreshes <see cref="FederatedRevocations"/>.
    /// </summary>
    /// <param name="feedJson">Raw UTF-8 JSON string representing a <see cref="SignedLotRevocationFeed"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified <see cref="ImportRevocationFeedResponse"/> summary.</returns>
    public async Task<ImportRevocationFeedResponse> ImportSignedRevocationFeedJsonAsync(
        string feedJson,
        CancellationToken ct = default)
    {
        // WHAT: Deserializes a peer node's signed revocation feed JSON and submits it to the connected node for verification and ingestion.
        // WHY:  Milestone 38 (P202): Enables air-gapped or remote recall feed synchronization from desktop and mobile UIs.
        // HOW:  Parses JSON with case-insensitive options, invokes ImportSignedRevocationFeedAsync, and refreshes FederatedRevocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(feedJson);
        SignedLotRevocationFeed? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SignedLotRevocationFeed>(feedJson, s_certificateJsonOptions);
        }
        catch (JsonException ex)
        {
            ErrorMessage = ex.Message;
            throw new InvalidOperationException("The supplied signed revocation feed JSON is malformed.", ex);
        }

        if (parsed == null)
        {
            const string nullMsg = "The supplied signed revocation feed JSON deserialized to null.";
            ErrorMessage = nullMsg;
            throw new InvalidOperationException(nullMsg);
        }

        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ImportRevocationFeedResponse response = await _client.ImportSignedRevocationFeedAsync(profile, token, parsed, ct).ConfigureAwait(false);
            LastRevocationFeedImport = response;
            FederatedRevocations = await _client.GetFederatedRevocationsAsync(profile, token, ct).ConfigureAwait(false);
            return response;
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
    /// Loads all verified federated peer recall tombstones from the connected node via GET /api/fabrication/revocations/federated.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of imported <see cref="FederatedLotRevocationRecord"/> entries.</returns>
    public async Task<IReadOnlyList<FederatedLotRevocationRecord>> LoadFederatedRevocationsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries verified peer recall tombstones and populates FederatedRevocations.
        // WHY:  Milestone 38 (P202): Surfaces active upstream lot recalls across desktop and mobile clinical workspaces.
        // HOW:  Resolves session credentials, calls GetFederatedRevocationsAsync, and updates observable state.
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<FederatedLotRevocationRecord> list = await _client.GetFederatedRevocationsAsync(profile, token, ct).ConfigureAwait(false);
            FederatedRevocations = list;
            return list;
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
    /// Backing field for <see cref="PeerSyncEndpoints"/>.
    /// </summary>
    private IReadOnlyList<FederatedPeerSyncEndpointRecord> _peerSyncEndpoints = [];

    /// <summary>
    /// Backing field for <see cref="LastPeerSyncRun"/>.
    /// </summary>
    private FederatedPeerSyncRunResult? _lastPeerSyncRun;

    /// <summary>
    /// Backing field for <see cref="LastPeerSyncSweep"/>.
    /// </summary>
    private FederatedPeerSyncSweepResult? _lastPeerSyncSweep;

    /// <summary>
    /// Gets the list of configured automated federated peer revocation pull-sync subscriptions.
    /// </summary>
    public IReadOnlyList<FederatedPeerSyncEndpointRecord> PeerSyncEndpoints
    {
        get => _peerSyncEndpoints;
        private set
        {
            _peerSyncEndpoints = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the outcome of the most recent single-peer revocation pull-sync run.
    /// </summary>
    public FederatedPeerSyncRunResult? LastPeerSyncRun
    {
        get => _lastPeerSyncRun;
        private set
        {
            _lastPeerSyncRun = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the aggregate outcome of the most recent multi-peer revocation pull-sync sweep.
    /// </summary>
    public FederatedPeerSyncSweepResult? LastPeerSyncSweep
    {
        get => _lastPeerSyncSweep;
        private set
        {
            _lastPeerSyncSweep = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Loads configured automated peer revocation pull-sync subscriptions via GET /api/federation/peers/revocation-sync.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of configured <see cref="FederatedPeerSyncEndpointRecord"/> entries.</returns>
    public async Task<IReadOnlyList<FederatedPeerSyncEndpointRecord>> LoadPeerSyncEndpointsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries configured automated peer pull-sync subscriptions and updates PeerSyncEndpoints.
        // WHY:  Milestone 39 (P207): Lets desktop and mobile clinicians inspect peer sync status and tray alerts.
        // HOW:  Resolves session, invokes GetFederatedPeerSyncEndpointsAsync, and updates observable state.
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<FederatedPeerSyncEndpointRecord> list = await _client.GetFederatedPeerSyncEndpointsAsync(profile, token, ct).ConfigureAwait(false);
            PeerSyncEndpoints = list;
            return list;
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
    /// Creates or updates an automated peer revocation pull-sync subscription and refreshes <see cref="PeerSyncEndpoints"/>.
    /// </summary>
    /// <param name="request">Peer subscription configuration payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="FederatedPeerSyncEndpointRecord"/>.</returns>
    public async Task<FederatedPeerSyncEndpointRecord> UpsertPeerSyncEndpointAsync(
        UpsertFederatedPeerSyncEndpointRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Persists a peer pull-sync subscription and reloads the peer subscription list.
        // WHY:  Milestone 39 (P207): Enables native desktop/mobile peer subscription configuration.
        // HOW:  Validates request, invokes UpsertFederatedPeerSyncEndpointAsync, and refreshes PeerSyncEndpoints.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FederatedPeerSyncEndpointRecord saved = await _client.UpsertFederatedPeerSyncEndpointAsync(profile, token, request, ct).ConfigureAwait(false);
            PeerSyncEndpoints = await _client.GetFederatedPeerSyncEndpointsAsync(profile, token, ct).ConfigureAwait(false);
            return saved;
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
    /// Removes an automated peer revocation pull-sync subscription and refreshes <see cref="PeerSyncEndpoints"/>.
    /// </summary>
    /// <param name="peerId">Target peer subscription identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when deleted; false when not found.</returns>
    public async Task<bool> DeletePeerSyncEndpointAsync(string peerId, CancellationToken ct = default)
    {
        // WHAT: Deletes a peer subscription by peerId and reloads PeerSyncEndpoints.
        // WHY:  Milestone 39 (P207): Allows decommissioning peer labs from desktop/mobile shells.
        // HOW:  Validates peerId, calls DeleteFederatedPeerSyncEndpointAsync, and refreshes PeerSyncEndpoints.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            bool deleted = await _client.DeleteFederatedPeerSyncEndpointAsync(profile, token, peerId, ct).ConfigureAwait(false);
            PeerSyncEndpoints = await _client.GetFederatedPeerSyncEndpointsAsync(profile, token, ct).ConfigureAwait(false);
            return deleted;
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
    /// Executes an immediate pull-sync against one subscribed peer and refreshes peer endpoints and federated recalls.
    /// </summary>
    /// <param name="peerId">Target peer subscription identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="FederatedPeerSyncRunResult"/> produced by the pull-sync.</returns>
    public async Task<FederatedPeerSyncRunResult> RunPeerSyncAsync(string peerId, CancellationToken ct = default)
    {
        // WHAT: Triggers an on-demand revocation pull-sync for one peer and refreshes endpoints + federated recalls.
        // WHY:  Milestone 39 (P207): Surfaces newly pulled recalls immediately in native UIs and tray summaries.
        // HOW:  Calls RunFederatedPeerSyncAsync, updates LastPeerSyncRun, clears stale sweep, and refreshes lists.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FederatedPeerSyncRunResult result = await _client.RunFederatedPeerSyncAsync(profile, token, peerId, ct).ConfigureAwait(false);
            LastPeerSyncRun = result;
            LastPeerSyncSweep = null;
            PeerSyncEndpoints = await _client.GetFederatedPeerSyncEndpointsAsync(profile, token, ct).ConfigureAwait(false);
            FederatedRevocations = await _client.GetFederatedRevocationsAsync(profile, token, ct).ConfigureAwait(false);
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
    /// Executes an immediate revocation pull-sync sweep across all enabled peers and refreshes endpoints and recalls.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aggregate <see cref="FederatedPeerSyncSweepResult"/>.</returns>
    public async Task<FederatedPeerSyncSweepResult> RunAllPeerSyncAsync(CancellationToken ct = default)
    {
        // WHAT: Triggers a full sweep across all enabled peers and reloads endpoints and federated recalls.
        // WHY:  Milestone 39 (P207): Powers one-click multi-peer synchronization and desktop tray alert escalation.
        // HOW:  Calls RunAllFederatedPeerSyncAsync, updates LastPeerSyncSweep, and refreshes PeerSyncEndpoints + FederatedRevocations.
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            FederatedPeerSyncSweepResult sweep = await _client.RunAllFederatedPeerSyncAsync(profile, token, ct).ConfigureAwait(false);
            LastPeerSyncSweep = sweep;
            PeerSyncEndpoints = await _client.GetFederatedPeerSyncEndpointsAsync(profile, token, ct).ConfigureAwait(false);
            FederatedRevocations = await _client.GetFederatedRevocationsAsync(profile, token, ct).ConfigureAwait(false);
            return sweep;
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
    /// Backing field for <see cref="LatestLotStability"/>.
    /// </summary>
    private LotStabilityReport? _latestLotStability;

    /// <summary>
    /// Backing field for <see cref="LatestCustodyLedger"/>.
    /// </summary>
    private CustodyHandoffLedgerResponse? _latestCustodyLedger;

    /// <summary>
    /// Gets the most recently evaluated Arrhenius cold-chain thermal stability report for <see cref="SelectedLotId"/>.
    /// </summary>
    public LotStabilityReport? LatestLotStability
    {
        get => _latestLotStability;
        private set
        {
            _latestLotStability = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the most recently loaded SHA-256 hash-chained custody handoff ledger for <see cref="SelectedLotId"/>.
    /// </summary>
    public CustodyHandoffLedgerResponse? LatestCustodyLedger
    {
        get => _latestCustodyLedger;
        private set
        {
            _latestCustodyLedger = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Evaluates Arrhenius cold-chain thermal stability for <see cref="SelectedLotId"/> via GET /api/fabrication/lots/{lotId}/stability.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The evaluated <see cref="LotStabilityReport"/>.</returns>
    public async Task<LotStabilityReport> LoadSelectedLotStabilityAsync(CancellationToken ct = default)
    {
        // WHAT: Queries Arrhenius potency, shelf-life days, and custody-chain status for the active fabrication lot.
        // WHY:  Milestone 40 (P212): Drives native cold-chain telemetry cards and desktop tray safety alerts.
        // HOW:  Validates SelectedLotId, calls GetLotStabilityAsync, and updates LatestLotStability.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LotStabilityReport report = await _client.GetLotStabilityAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LatestLotStability = report;
            return report;
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
    /// Logs a cold-chain temperature excursion for <see cref="SelectedLotId"/> via POST /api/fabrication/lots/{lotId}/thermal-excursions
    /// and updates <see cref="LatestLotStability"/>.
    /// </summary>
    /// <param name="request">Thermal excursion temperature, duration, storage tier, and sensor payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="ThermalExcursionResponse"/>.</returns>
    public async Task<ThermalExcursionResponse> RecordThermalExcursionAsync(
        RecordThermalExcursionRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits a sensor temperature observation and updates LatestLotStability immediately.
        // WHY:  Milestone 40 (P212): Surfaces recalculated Arrhenius potency and tray critical alerts in native shells.
        // HOW:  Validates inputs, posts to RecordThermalExcursionAsync, and stores StabilityAfterExcursion.
        ArgumentNullException.ThrowIfNull(request);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ThermalExcursionResponse response = await _client.RecordThermalExcursionAsync(profile, token, lotId, request, ct).ConfigureAwait(false);
            LatestLotStability = response.StabilityAfterExcursion;
            return response;
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
    /// Loads the SHA-256 hash-chained custody handoff ledger for <see cref="SelectedLotId"/> via GET /api/fabrication/lots/{lotId}/custody-handoffs.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified <see cref="CustodyHandoffLedgerResponse"/>.</returns>
    public async Task<CustodyHandoffLedgerResponse> LoadSelectedLotCustodyLedgerAsync(CancellationToken ct = default)
    {
        // WHAT: Fetches the verified custody handoff timeline for the active fabrication lot.
        // WHY:  Milestone 40 (P212): Displays physical chain-of-custody provenance across desktop and mobile views.
        // HOW:  Validates SelectedLotId, calls GetCustodyHandoffsAsync, and updates LatestCustodyLedger.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            CustodyHandoffLedgerResponse ledger = await _client.GetCustodyHandoffsAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LatestCustodyLedger = ledger;
            return ledger;
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
    /// Appends a SHA-256 hash-chained custody handoff for <see cref="SelectedLotId"/> and refreshes
    /// <see cref="LatestCustodyLedger"/> and <see cref="LatestLotStability"/>.
    /// </summary>
    /// <param name="request">Custody stage, actors, facility code, and handoff temperature payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="CustodyHandoffRecord"/>.</returns>
    public async Task<CustodyHandoffRecord> RecordCustodyHandoffAsync(
        RecordCustodyHandoffRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits a physical custody transition and reloads both custody ledger and stability status.
        // WHY:  Milestone 40 (P212): Keeps native clinic custody timelines synchronized after each handoff.
        // HOW:  Validates request and SelectedLotId, posts handoff, then refreshes ledger and stability reports.
        ArgumentNullException.ThrowIfNull(request);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            CustodyHandoffRecord record = await _client.RecordCustodyHandoffAsync(profile, token, lotId, request, ct).ConfigureAwait(false);
            LatestCustodyLedger = await _client.GetCustodyHandoffsAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LatestLotStability = await _client.GetLotStabilityAsync(profile, token, lotId, ct).ConfigureAwait(false);
            return record;
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
    /// Backing field for <see cref="LatestPharmacovigilanceReport"/>.
    /// </summary>
    private LotPharmacovigilanceReport? _latestPharmacovigilanceReport;

    /// <summary>
    /// Backing field for <see cref="ActiveLotAdverseEvents"/>.
    /// </summary>
    private IReadOnlyList<LotAdverseEventSummary> _activeLotAdverseEvents = [];

    /// <summary>
    /// Gets the most recently evaluated post-implantation pharmacovigilance report for <see cref="SelectedLotId"/>.
    /// </summary>
    public LotPharmacovigilanceReport? LatestPharmacovigilanceReport
    {
        get => _latestPharmacovigilanceReport;
        private set
        {
            _latestPharmacovigilanceReport = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the chronological decrypted post-implantation adverse events loaded for <see cref="SelectedLotId"/>.
    /// </summary>
    public IReadOnlyList<LotAdverseEventSummary> ActiveLotAdverseEvents
    {
        get => _activeLotAdverseEvents;
        private set
        {
            _activeLotAdverseEvents = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Evaluates post-implantation pharmacovigilance and CARPA/TLR9 signals for <see cref="SelectedLotId"/>
    /// via GET /api/fabrication/lots/{lotId}/pharmacovigilance.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The evaluated <see cref="LotPharmacovigilanceReport"/>.</returns>
    public async Task<LotPharmacovigilanceReport> LoadSelectedLotPharmacovigilanceAsync(CancellationToken ct = default)
    {
        // WHAT: Queries aggregate pharmacovigilance posture, CARPA/TLR9 flags, and quarantine state for SelectedLotId.
        // WHY:  Milestone 41 (P217): Powers native pharmacovigilance summary badges and desktop tray quarantine alerts.
        // HOW:  Validates SelectedLotId, invokes GetLotPharmacovigilanceAsync, and updates LatestPharmacovigilanceReport & ActiveLotAdverseEvents.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LotPharmacovigilanceReport report = await _client.GetLotPharmacovigilanceAsync(profile, token, lotId, ct).ConfigureAwait(false);
            LatestPharmacovigilanceReport = report;
            ActiveLotAdverseEvents = report.AdverseEvents;
            return report;
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
    /// Loads decrypted chronological adverse events for <see cref="SelectedLotId"/>
    /// via GET /api/fabrication/lots/{lotId}/adverse-events.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Chronological list of <see cref="LotAdverseEventSummary"/> entries.</returns>
    public async Task<IReadOnlyList<LotAdverseEventSummary>> LoadSelectedLotAdverseEventsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries decrypted clinical adverse event history for SelectedLotId.
        // WHY:  Milestone 41 (P217): Populates native adverse event timeline tables without raw ciphertext exposure.
        // HOW:  Validates SelectedLotId, calls GetLotAdverseEventsAsync, and updates ActiveLotAdverseEvents.
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<LotAdverseEventSummary> events = await _client.GetLotAdverseEventsAsync(profile, token, lotId, ct).ConfigureAwait(false);
            ActiveLotAdverseEvents = events;
            return events;
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
    /// Records a post-implantation CTCAE v5.0 adverse event for <see cref="SelectedLotId"/> and updates
    /// <see cref="LatestPharmacovigilanceReport"/>, <see cref="ActiveLotAdverseEvents"/>, and patient lots.
    /// </summary>
    /// <param name="request">CTCAE v5.0 adverse event grading, symptom category, onset hours, and optional notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="AdverseEventResponse"/>.</returns>
    public async Task<AdverseEventResponse> RecordAdverseEventAsync(
        RecordAdverseEventRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits a clinical adverse event and refreshes pharmacovigilance posture and lot statuses.
        // WHY:  Milestone 41 (P217): Immediately surfaces Grade >= 3 auto-quarantine transitions in native views and tray badges.
        // HOW:  Validates inputs, posts to RecordLotAdverseEventAsync, updates observable properties, and reloads lots if active patient is set.
        ArgumentNullException.ThrowIfNull(request);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            AdverseEventResponse response = await _client.RecordLotAdverseEventAsync(profile, token, lotId, request, ct).ConfigureAwait(false);
            LatestPharmacovigilanceReport = response.ReportAfterEvent;
            ActiveLotAdverseEvents = response.ReportAfterEvent.AdverseEvents;
            await RefreshActivePatientLotsAsync(profile, token, ct).ConfigureAwait(false);
            return response;
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
    /// Records a CARPA/TLR9 immunogenicity panel for <see cref="SelectedLotId"/> and updates
    /// <see cref="LatestPharmacovigilanceReport"/> and patient lot statuses.
    /// </summary>
    /// <param name="request">Complement, cytokine, and antibody biomarker panel payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="ImmunogenicityAssayResponse"/>.</returns>
    public async Task<ImmunogenicityAssayResponse> RecordImmunogenicityAssayAsync(
        RecordImmunogenicityAssayRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits an immunogenicity assay panel and refreshes pharmacovigilance quarantine status.
        // WHY:  Milestone 41 (P217): Immediately reflects CARPA/TLR9 critical auto-quarantines across native UIs and tray indicators.
        // HOW:  Validates inputs, posts to RecordLotImmunogenicityAssayAsync, updates observable posture, and refreshes patient lots.
        ArgumentNullException.ThrowIfNull(request);
        string lotId = RequireSelectedLotId();
        var (profile, token) = ResolveEffectiveSession();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ImmunogenicityAssayResponse response = await _client.RecordLotImmunogenicityAssayAsync(profile, token, lotId, request, ct).ConfigureAwait(false);
            LatestPharmacovigilanceReport = response.ReportAfterAssay;
            ActiveLotAdverseEvents = response.ReportAfterAssay.AdverseEvents;
            await RefreshActivePatientLotsAsync(profile, token, ct).ConfigureAwait(false);
            return response;
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
    /// Refreshes <see cref="FabricationLots"/> when <see cref="ActivePatientId"/> is currently selected.
    /// </summary>
    /// <param name="profile">Active instance connection profile.</param>
    /// <param name="token">Active Bearer session token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous lot refresh.</returns>
    private async Task RefreshActivePatientLotsAsync(InstanceProfile profile, string token, CancellationToken ct)
    {
        // WHAT: Reloads patient fabrication lots when an active patient context exists.
        // WHY:  Avoids duplicating patient-ID checks across adverse event and immunogenicity mutation commands.
        // HOW:  Guards on _activePatientId and fetches updated FabricationLots via _client.GetFabricationLotsAsync.
        if (!string.IsNullOrWhiteSpace(_activePatientId))
        {
            FabricationLots = await _client.GetFabricationLotsAsync(profile, token, _activePatientId, ct).ConfigureAwait(false);
        }
    }
}
