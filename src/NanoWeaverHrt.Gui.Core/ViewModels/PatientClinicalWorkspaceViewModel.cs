using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel coordinating clinical patient profiles, demographic and contact records,
/// digital DNA nanocarrier regimens, clinical results, imported sequences, and research cohort grants.
/// </summary>
/// <remarks>
/// // WHAT: Provides presentation state and asynchronous commands for patient clinical workflows and cohort governance.
/// // WHY:  Issue #169 (P166): Enables clinicians, researchers, and patients to manage records, DNA blueprints, and grants.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, manages busy states, and exposes observable artifacts.
/// </remarks>
public sealed class PatientClinicalWorkspaceViewModel : INotifyPropertyChanged
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
    /// Indicates whether an asynchronous patient clinical operation is currently running.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during operation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Profile details for the currently active patient or subject.
    /// </summary>
    private UserProfile? _activeUserProfile;

    /// <summary>
    /// Collection of structured addresses registered for the patient.
    /// </summary>
    private IReadOnlyList<PatientAddress>? _userAddresses;

    /// <summary>
    /// Collection of emergency and caregiver contacts registered for the patient.
    /// </summary>
    private IReadOnlyList<PatientContact>? _userContacts;

    /// <summary>
    /// Result of the most recent patient Digital DNA fabrication package query.
    /// </summary>
    private PatientDigitalDnaPackage? _lastDnaPackage;

    /// <summary>
    /// Collection of persistent digitized DNA blueprint records for the patient.
    /// </summary>
    private IReadOnlyList<PatientDigitalDnaRecord>? _patientDnaRecords;

    /// <summary>
    /// Collection of imported DNA sequence and variant panel records for the patient.
    /// </summary>
    private IReadOnlyList<PatientDnaImportRecord>? _patientImports;

    /// <summary>
    /// Collection of clinical and simulation result records for the patient.
    /// </summary>
    private IReadOnlyList<PatientResultRecord>? _patientClinicalResults;

    /// <summary>
    /// Collection of active direct researcher subject access grants.
    /// </summary>
    private IReadOnlyList<SubjectGrant>? _subjectGrants;

    /// <summary>
    /// Collection of patient cohort groups accessible to the caller.
    /// </summary>
    private IReadOnlyList<SubjectGroup>? _subjectGroups;

    /// <summary>
    /// Collection of patient subjects accessible to the current caller.
    /// </summary>
    private IReadOnlyList<AccessibleSubjectItem>? _accessibleSubjects;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientClinicalWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing patient and clinical requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public PatientClinicalWorkspaceViewModel(
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
    /// Gets the profile details for the active patient or user.
    /// </summary>
    public UserProfile? ActiveUserProfile
    {
        get => _activeUserProfile;
        private set => SetField(ref _activeUserProfile, value);
    }

    /// <summary>
    /// Gets the collection of registered addresses for the patient.
    /// </summary>
    public IReadOnlyList<PatientAddress>? UserAddresses
    {
        get => _userAddresses;
        private set => SetField(ref _userAddresses, value);
    }

    /// <summary>
    /// Gets the collection of emergency and caregiver contacts for the patient.
    /// </summary>
    public IReadOnlyList<PatientContact>? UserContacts
    {
        get => _userContacts;
        private set => SetField(ref _userContacts, value);
    }

    /// <summary>
    /// Gets the result of the most recent patient Digital DNA fabrication package query.
    /// </summary>
    public PatientDigitalDnaPackage? LastDnaPackage
    {
        get => _lastDnaPackage;
        private set => SetField(ref _lastDnaPackage, value);
    }

    /// <summary>
    /// Gets the collection of persistent digitized DNA blueprint records for the patient.
    /// </summary>
    public IReadOnlyList<PatientDigitalDnaRecord>? PatientDnaRecords
    {
        get => _patientDnaRecords;
        private set => SetField(ref _patientDnaRecords, value);
    }

    /// <summary>
    /// Gets the collection of imported sequence and variant panel records for the patient.
    /// </summary>
    public IReadOnlyList<PatientDnaImportRecord>? PatientImports
    {
        get => _patientImports;
        private set => SetField(ref _patientImports, value);
    }

    /// <summary>
    /// Gets the collection of clinical and simulation results for the patient.
    /// </summary>
    public IReadOnlyList<PatientResultRecord>? PatientClinicalResults
    {
        get => _patientClinicalResults;
        private set => SetField(ref _patientClinicalResults, value);
    }

    /// <summary>
    /// Gets the collection of active direct researcher subject access grants.
    /// </summary>
    public IReadOnlyList<SubjectGrant>? SubjectGrants
    {
        get => _subjectGrants;
        private set => SetField(ref _subjectGrants, value);
    }

    /// <summary>
    /// Gets the collection of patient cohort groups accessible to the caller.
    /// </summary>
    public IReadOnlyList<SubjectGroup>? SubjectGroups
    {
        get => _subjectGroups;
        private set => SetField(ref _subjectGroups, value);
    }

    /// <summary>
    /// Gets the collection of patient subjects accessible to the current caller.
    /// </summary>
    public IReadOnlyList<AccessibleSubjectItem>? AccessibleSubjects
    {
        get => _accessibleSubjects;
        private set => SetField(ref _accessibleSubjects, value);
    }

    /// <summary>
    /// Retrieves demographic, clinical, and pronoun profile data for a user via GET /api/users/{username}/profile.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Demographic and clinical user profile.</returns>
    public async Task<UserProfile> GetUserProfileAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Dispatches a user profile lookup request to the node client.
        // WHY:  Loads patient demographic, endocrine, and pronoun settings into the active view model.
        // HOW:  Validates username, resolves session credentials, queries client, and captures profile.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetUserProfileAsync(profile, token, username, ct).ConfigureAwait(false);
            ActiveUserProfile = result;
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
    /// Creates or updates demographic, clinical, and pronoun profile data for a user via PUT /api/users/{username}/profile.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="profile">Updated profile payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated user profile.</returns>
    public async Task<UserProfile> UpdateUserProfileAsync(
        string username,
        UserProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Persists updated demographic, clinical, or pronoun changes to the user's profile.
        // WHY:  Maintains accurate patient identity and medical history in the sovereign ledger.
        // HOW:  Validates inputs, resolves session credentials, awaits client, and records active profile.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(profile);
        var (instProfile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.UpdateUserProfileAsync(instProfile, token, username, profile, ct).ConfigureAwait(false);
            ActiveUserProfile = result;
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
    /// Retrieves all structured addresses registered for a user via GET /api/users/{username}/addresses.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of registered addresses.</returns>
    public async Task<IReadOnlyList<PatientAddress>> ListUserAddressesAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Fetches registered postal and delivery addresses for the specified user.
        // WHY:  Supports delivery coordination for synthesized nanocarrier payloads.
        // HOW:  Validates username, resolves session, queries client, and stores addresses in UserAddresses.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListUserAddressesAsync(profile, token, username, ct).ConfigureAwait(false);
            UserAddresses = result;
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
    /// Adds or updates a structured address record for a user via POST /api/users/{username}/addresses.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="address">Address to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persisted address record.</returns>
    public async Task<PatientAddress> AddUserAddressAsync(
        string username,
        PatientAddress address,
        CancellationToken ct = default)
    {
        // WHAT: Adds a new delivery or residential address record for the target patient.
        // WHY:  Ensures physical logistics tracking for patient therapy kits.
        // HOW:  Validates arguments, resolves credentials, dispatches client request, and handles errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(address);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.AddUserAddressAsync(profile, token, username, address, ct).ConfigureAwait(false);
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
    /// Deletes an address record for a user via DELETE /api/users/{username}/addresses/{id}.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="addressId">Identifier of the address to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeleteUserAddressAsync(
        string username,
        string addressId,
        CancellationToken ct = default)
    {
        // WHAT: Removes a specific address record from the user's demographic profile.
        // WHY:  Allows patients to purge obsolete residential or shipping addresses.
        // HOW:  Validates string arguments, resolves session, calls client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteUserAddressAsync(profile, token, username, addressId, ct).ConfigureAwait(false);
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
    /// Retrieves emergency and caregiver contacts for a user via GET /api/users/{username}/contacts.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of emergency and caregiver contacts.</returns>
    public async Task<IReadOnlyList<PatientContact>> ListUserContactsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries all emergency contacts and nominated caregiver entries for a patient.
        // WHY:  Enables clinical outreach and emergency notification during HRT administration.
        // HOW:  Validates username, resolves session, queries client, and stores contacts in UserContacts.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListUserContactsAsync(profile, token, username, ct).ConfigureAwait(false);
            UserContacts = result;
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
    /// Adds or updates an emergency or caregiver contact for a user via POST /api/users/{username}/contacts.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="contact">Contact to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persisted contact record.</returns>
    public async Task<PatientContact> AddUserContactAsync(
        string username,
        PatientContact contact,
        CancellationToken ct = default)
    {
        // WHAT: Registers a new emergency contact or caregiver record for the patient.
        // WHY:  Ensures medical safety protocols have verified next-of-kin or clinician channels.
        // HOW:  Validates inputs, resolves session, dispatches to client, and handles errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(contact);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.AddUserContactAsync(profile, token, username, contact, ct).ConfigureAwait(false);
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
    /// Deletes an emergency contact record for a user via DELETE /api/users/{username}/contacts/{id}.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="contactId">Identifier of contact to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeleteUserContactAsync(
        string username,
        string contactId,
        CancellationToken ct = default)
    {
        // WHAT: Deletes a specific emergency contact record for the patient.
        // WHY:  Allows patients to update and revoke designated medical contacts.
        // HOW:  Validates string arguments, resolves session, calls client, and captures error on failure.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(contactId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteUserContactAsync(profile, token, username, contactId, ct).ConfigureAwait(false);
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
    /// Generates or fetches a patient-specific Digital DNA package via GET /api/patient/dna.
    /// </summary>
    /// <param name="patientId">Optional patient identifier to retrieve; if omitted, defaults to the authenticated subject.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete patient Digital DNA package including staple oligos and annealing ramps.</returns>
    public async Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        string? patientId = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a Digital DNA package query for the specified patient or authenticated subject.
        // WHY:  Retrieves staple oligo manifests, cadnano geometry schemas, and thermal annealing ramps.
        // HOW:  Resolves session credentials, invokes client, records LastDnaPackage, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetPatientDnaRegimensAsync(profile, token, patientId, ct).ConfigureAwait(false);
            LastDnaPackage = result;
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
    /// Retrieves digitized DNA nanocarrier fabrication regimens for a patient with filtering parameters via GET /api/patient/dna.
    /// </summary>
    /// <param name="patientId">Optional patient identifier to retrieve; if omitted, defaults to the authenticated subject.</param>
    /// <param name="regimen">Optional therapeutic regimen filter.</param>
    /// <param name="geometry">Optional nanocarrier geometry filter.</param>
    /// <param name="targetKd">Optional target aptamer dissociation constant filter in nanomolar.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Complete patient Digital DNA package including staple oligos and annealing ramps.</returns>
    public async Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        string? patientId,
        TherapeuticRegimen? regimen,
        string? geometry,
        double? targetKd,
        CancellationToken ct = default)
    {
        // WHAT: Fetches a filtered Digital DNA nanocarrier fabrication package matching the criteria.
        // WHY:  Enables precision customization of geometry, binding affinity, and therapeutic regime.
        // HOW:  Resolves session credentials, delegates to client overload, and stores LastDnaPackage.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetPatientDnaRegimensAsync(profile, token, patientId, regimen, geometry, targetKd, ct).ConfigureAwait(false);
            LastDnaPackage = result;
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
    /// Retrieves persistent digitized DNA records for a specific patient via GET /api/patients/{username}/dna.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of digitized DNA blueprint records for the patient.</returns>
    public async Task<IReadOnlyList<PatientDigitalDnaRecord>> GetPatientDigitalDnaAsync(
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Queries all persistent digitized DNA blueprint records registered under a patient.
        // WHY:  Allows clinicians and patients to browse historical nanocarrier fabrication designs.
        // HOW:  Validates username, resolves session, queries client, and populates PatientDnaRecords.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.GetPatientDigitalDnaAsync(profile, token, username, ct).ConfigureAwait(false);
            PatientDnaRecords = result;
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
    /// Retrieves a specific digitized DNA blueprint by ID via GET /api/patients/{username}/dna/{id}.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the DNA record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Digitized DNA record.</returns>
    public async Task<PatientDigitalDnaRecord> GetPatientDigitalDnaRecordAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves a single digitized DNA blueprint record by its primary identifier.
        // WHY:  Inspects routing schemas, binding affinities, and scaffold details for a specific build.
        // HOW:  Validates arguments, resolves session, queries client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.GetPatientDigitalDnaRecordAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Synthesizes and registers a digitized DNA blueprint for a patient via POST /api/patients/{username}/dna.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="request">DNA blueprint synthesis parameters and nanocarrier specifications.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="PatientDigitalDnaRecord"/> record.</returns>
    public async Task<PatientDigitalDnaRecord> CreatePatientDnaAsync(
        string username,
        CreatePatientDnaRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches a CreatePatientDnaRequest to register a new nanocarrier design in the ledger.
        // WHY:  Commits newly synthesized DNA origami architectures to the patient's digital record.
        // HOW:  Validates inputs, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreatePatientDnaAsync(profile, token, username, request, ct).ConfigureAwait(false);
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
    /// Removes a digitized DNA blueprint record by ID via DELETE /api/patients/{username}/dna/{id}.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the DNA record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeletePatientDigitalDnaAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Deletes a digitized DNA blueprint record from the patient's record registry.
        // WHY:  Allows purging obsolete or superseded experimental nanocarrier architectures.
        // HOW:  Validates string arguments, resolves session, invokes client, and handles errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeletePatientDigitalDnaAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Lists all imported sequence and dataset files for a patient via GET /api/patients/{username}/imports.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of imported sequence records.</returns>
    public async Task<IReadOnlyList<PatientDnaImportRecord>> ListPatientImportsAsync(
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Fetches imported sequence files and genetic datasets associated with a patient.
        // WHY:  Displays imported FASTA sequences, BAM alignments, or variant calling manifests.
        // HOW:  Validates username, resolves session, queries client, and sets PatientImports.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListPatientImportsAsync(profile, token, username, ct).ConfigureAwait(false);
            PatientImports = result;
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
    /// Retrieves an imported sequence or dataset record by ID via GET /api/patients/{username}/imports/{id}.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the import record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Imported sequence record.</returns>
    public async Task<PatientDnaImportRecord> GetPatientImportAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific imported sequence record by its identifier.
        // WHY:  Allows inspecting sequence metadata, file hash, and parsing status.
        // HOW:  Validates string arguments, resolves session, calls client, and captures error on failure.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.GetPatientImportAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Imports DNA sequence data or variant panels for a patient via POST /api/patients/{username}/imports.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="request">Import payload containing file name and sequence data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created import record.</returns>
    public async Task<PatientDnaImportRecord> CreatePatientImportAsync(
        string username,
        CreatePatientImportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Uploads and commits raw FASTA or genomic variant datasets for a patient.
        // WHY:  Provides patient sequence material for off-target immunogenicity and risk analysis.
        // HOW:  Validates arguments, resolves session credentials, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreatePatientImportAsync(profile, token, username, request, ct).ConfigureAwait(false);
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
    /// Deletes an imported sequence or dataset record by ID via DELETE /api/patients/{username}/imports/{id}.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the import record to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeletePatientImportAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Deletes an imported sequence file record from the patient database.
        // WHY:  Allows cleaning up transient or erroneous sequence upload artifacts.
        // HOW:  Validates string arguments, resolves session, calls client, and captures operational errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeletePatientImportAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Lists all simulation and clinical results for a patient via GET /api/patients/{username}/results.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="resultType">Optional result type filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of clinical and simulation result records.</returns>
    public async Task<IReadOnlyList<PatientResultRecord>> ListPatientClinicalResultsAsync(
        string username,
        string? resultType = null,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves historical clinical, endocrine kinetic, and simulation result records.
        // WHY:  Permits tracking patient progress across serial hormone and nanocarrier trials.
        // HOW:  Validates username, resolves session, queries client, and sets PatientClinicalResults.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListPatientClinicalResultsAsync(profile, token, username, resultType, ct).ConfigureAwait(false);
            PatientClinicalResults = result;
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
    /// Retrieves a specific simulation or clinical result record by ID via GET /api/patients/{username}/results/{id}.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="id">Identifier of the result record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Clinical or simulation result record.</returns>
    public async Task<PatientResultRecord> GetPatientClinicalResultAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Fetches a single clinical or simulation result record by identifier.
        // WHY:  Enables inspection of detailed metrics, blood serum levels, and kinetic predictions.
        // HOW:  Validates string arguments, resolves session, calls client, and captures errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.GetPatientClinicalResultAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Registers a new simulation or clinical result record via POST /api/patients/{username}/results.
    /// </summary>
    /// <param name="username">Target patient username.</param>
    /// <param name="request">Result registration payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created result record.</returns>
    public async Task<PatientResultRecord> CreatePatientClinicalResultAsync(
        string username,
        CreatePatientResultRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Persists a new clinical observation or simulation outcome for a patient.
        // WHY:  Records validated lab blood work, simulated endocrine levels, or adverse alerts.
        // HOW:  Validates arguments, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreatePatientClinicalResultAsync(profile, token, username, request, ct).ConfigureAwait(false);
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
    /// Deletes a simulation or clinical result record by ID via DELETE /api/patients/{username}/results/{id}.
    /// </summary>
    /// <param name="username">Target username.</param>
    /// <param name="id">Identifier of result record to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeletePatientClinicalResultAsync(
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Removes a specific clinical or simulation result record from the patient database.
        // WHY:  Enables clinicians to excise errant or duplicate laboratory result submissions.
        // HOW:  Validates string arguments, resolves session, calls client, and captures errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeletePatientClinicalResultAsync(profile, token, username, id, ct).ConfigureAwait(false);
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
    /// Retrieves direct researcher subject access grants via GET /api/subject-access/grants (Administrator role required).
    /// </summary>
    /// <param name="researcher">Optional researcher username filter.</param>
    /// <param name="subject">Optional subject username filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of active subject grants.</returns>
    public async Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(
        string? researcher = null,
        string? subject = null,
        CancellationToken ct = default)
    {
        // WHAT: Queries active direct researcher-to-subject access grants.
        // WHY:  Allows compliance audit and access governance over clinical patient data.
        // HOW:  Resolves session, queries client with filters, and sets SubjectGrants property.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListSubjectGrantsAsync(profile, token, researcher, subject, ct).ConfigureAwait(false);
            SubjectGrants = result;
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
    /// Grants a researcher direct tiered access to a patient via POST /api/subject-access/grants (Administrator role required).
    /// </summary>
    /// <param name="request">Subject grant request containing researcher username, subject username, and access level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="SubjectGrant"/> record.</returns>
    public async Task<SubjectGrant> CreateSubjectGrantAsync(
        GrantSubjectAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Establishes a direct scoped access grant from a researcher to a patient.
        // WHY:  Implements role-gated tiered consent access across institutional clinical trials.
        // HOW:  Validates request, resolves session, calls client, and captures operational errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreateSubjectGrantAsync(profile, token, request, ct).ConfigureAwait(false);
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
    /// Revokes a researcher's direct access to a patient via DELETE /api/subject-access/grants/{researcherUsername}/{subjectUsername} (Administrator role required).
    /// </summary>
    /// <param name="researcherUsername">Username of the researcher whose access is being revoked.</param>
    /// <param name="subjectUsername">Username of the target patient or subject.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RevokeSubjectGrantAsync(
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Revokes direct clinical data access previously granted to a researcher.
        // WHY:  Supports immediate patient consent withdrawal and data protection enforcement.
        // HOW:  Validates string arguments, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(researcherUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectUsername);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RevokeSubjectGrantAsync(profile, token, researcherUsername, subjectUsername, ct).ConfigureAwait(false);
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
    /// Lists all patient subjects accessible to the current authenticated caller via GET /api/subject-access/my-subjects.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of accessible subject usernames with effective access levels.</returns>
    public async Task<IReadOnlyList<AccessibleSubjectItem>> ListMySubjectsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries all patient subjects accessible to the current authenticated clinician or researcher.
        // WHY:  Populates the active subject picker in the clinical workspace UI.
        // HOW:  Resolves session, queries client, sets AccessibleSubjects, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListMySubjectsAsync(profile, token, ct).ConfigureAwait(false);
            AccessibleSubjects = result;
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
    /// Lists patient cohort groups accessible to the caller via GET /api/subject-groups.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of accessible <see cref="SubjectGroup"/> records.</returns>
    public async Task<IReadOnlyList<SubjectGroup>> ListSubjectGroupsAsync(CancellationToken ct = default)
    {
        // WHAT: Queries all patient cohort groups accessible to the caller.
        // WHY:  Enables cohort-level study management, blinding, and aggregate kinetics modeling.
        // HOW:  Resolves session, queries client, sets SubjectGroups, and handles errors.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListSubjectGroupsAsync(profile, token, ct).ConfigureAwait(false);
            SubjectGroups = result;
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
    /// Creates a new patient cohort group via POST /api/subject-groups.
    /// </summary>
    /// <param name="request">Cohort group creation request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="SubjectGroup"/> record.</returns>
    public async Task<SubjectGroup> CreateSubjectGroupAsync(
        CreateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Provisions a new clinical patient cohort group in the node registry.
        // WHY:  Allows grouping patients by therapy arm, dosage tier, or biomarker panel.
        // HOW:  Validates request, resolves credentials, invokes client, and manages busy state.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreateSubjectGroupAsync(profile, token, request, ct).ConfigureAwait(false);
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
    /// Retrieves a single cohort group record by its identifier via GET /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="SubjectGroup"/> record details.</returns>
    public async Task<SubjectGroup> GetSubjectGroupAsync(
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves details for a specific cohort group.
        // WHY:  Displays cohort metadata, member count, and creation timestamp.
        // HOW:  Validates groupId, resolves session, queries client, and captures operational errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.GetSubjectGroupAsync(profile, token, groupId, ct).ConfigureAwait(false);
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
    /// Updates the display name or description of an existing cohort group via PUT /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group to update.</param>
    /// <param name="request">Payload containing updated group name and optional description.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="SubjectGroup"/> record.</returns>
    public async Task<SubjectGroup> UpdateSubjectGroupAsync(
        string groupId,
        UpdateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Mutates the title and descriptive summary of an existing cohort group.
        // WHY:  Allows researchers to clarify study arm definitions as clinical protocols evolve.
        // HOW:  Validates inputs, resolves session, awaits client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.UpdateSubjectGroupAsync(profile, token, groupId, request, ct).ConfigureAwait(false);
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
    /// Permanently deletes a patient cohort group via DELETE /api/subject-groups/{groupId}.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteSubjectGroupAsync(string groupId, CancellationToken ct = default)
    {
        // WHAT: Deletes a patient cohort group from the registry.
        // WHY:  Allows cleanup of concluded clinical trial cohorts or temporary study groups.
        // HOW:  Validates groupId, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteSubjectGroupAsync(profile, token, groupId, ct).ConfigureAwait(false);
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
    /// Lists enrolled subject usernames for a patient cohort group via GET /api/subject-groups/{groupId}/members.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of enrolled subject usernames.</returns>
    public async Task<IReadOnlyList<string>> ListGroupMembersAsync(string groupId, CancellationToken ct = default)
    {
        // WHAT: Retrieves all subject usernames enrolled within a specific cohort group.
        // WHY:  Allows reviewing study membership rosters and cohort participation.
        // HOW:  Validates groupId, resolves session, calls client, and captures errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.ListGroupMembersAsync(profile, token, groupId, ct).ConfigureAwait(false);
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
    /// Enrolls a patient into a cohort group via POST /api/subject-groups/{groupId}/members.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="request">Payload containing subject username to enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddGroupMemberAsync(
        string groupId,
        AddGroupMemberRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Enrolls an individual patient subject into an active cohort group.
        // WHY:  Assigns subjects to study arms, treatment regimens, or dosage schedules.
        // HOW:  Validates arguments, resolves session, awaits client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.AddGroupMemberAsync(profile, token, groupId, request, ct).ConfigureAwait(false);
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
    /// Un-enrolls a patient from a cohort group via DELETE /api/subject-groups/{groupId}/members/{subjectUsername}.
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="subjectUsername">Username of the subject to un-enroll.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveGroupMemberAsync(
        string groupId,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Removes an individual patient subject from a cohort group.
        // WHY:  Accommodates study arm reassignments or patient withdrawal from clinical trials.
        // HOW:  Validates string arguments, resolves session, calls client, and captures errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectUsername);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RemoveGroupMemberAsync(profile, token, groupId, subjectUsername, ct).ConfigureAwait(false);
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
    /// Lists researcher access grants for a cohort group via GET /api/subject-groups/{groupId}/grants (Administrator role required).
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of <see cref="SubjectGroupGrant"/> records.</returns>
    public async Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves all researcher access grants governing an entire cohort group.
        // WHY:  Enables oversight of bulk researcher permissions across grouped patient data.
        // HOW:  Validates groupId, resolves session, queries client, and returns grants.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.ListGroupGrantsAsync(profile, token, groupId, ct).ConfigureAwait(false);
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
    /// Grants a researcher tiered access over a cohort group via POST /api/subject-groups/{groupId}/grants (Administrator role required).
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="request">Payload containing researcher username, tiered access level, and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="SubjectGroupGrant"/> record.</returns>
    public async Task<SubjectGroupGrant> CreateGroupGrantAsync(
        string groupId,
        GrantGroupAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Grants a researcher access across all members of a cohort group.
        // WHY:  Allows multi-subject research access authorization without issuing individual grants.
        // HOW:  Validates inputs, resolves session, calls client, and captures operational errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreateGroupGrantAsync(profile, token, groupId, request, ct).ConfigureAwait(false);
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
    /// Revokes a researcher's access grant over a cohort group via DELETE /api/subject-groups/{groupId}/grants/{researcherUsername} (Administrator role required).
    /// </summary>
    /// <param name="groupId">Identifier of the cohort group.</param>
    /// <param name="researcherUsername">Username of the researcher whose grant is being revoked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RevokeGroupGrantAsync(
        string groupId,
        string researcherUsername,
        CancellationToken ct = default)
    {
        // WHAT: Revokes a researcher's bulk access permissions over a cohort group.
        // WHY:  Supports access tier revocation when a researcher departs a study arm.
        // HOW:  Validates string arguments, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(researcherUsername);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RevokeGroupGrantAsync(profile, token, groupId, researcherUsername, ct).ConfigureAwait(false);
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
            throw new InvalidOperationException("An active instance profile is required to execute patient clinical operations.");
        }

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute patient clinical operations.");
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
