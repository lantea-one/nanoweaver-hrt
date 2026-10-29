using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel for onboarding a new local or remote nanoweaver-hrt instance profile and generating or sealing its PKI keypair.
/// </summary>
public sealed class InstanceOnboardingViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Repository used for saving and managing persistent instance connection profiles.
    /// </summary>
    private readonly IInstanceProfileRepository _profileRepository;

    /// <summary>
    /// Key ring vault used for asymmetric keypair generation and AES-256-GCM private key sealing.
    /// </summary>
    private readonly IClientKeyRingVault _keyRingVault;

    /// <summary>
    /// Optional multi-instance coordinator for immediate profile registration upon onboarding.
    /// </summary>
    private readonly IMultiInstanceCoordinator? _coordinator;

    /// <summary>
    /// Backing field for the human-readable instance display name.
    /// </summary>
    private string _displayName = string.Empty;

    /// <summary>
    /// Backing field for the canonical base URI.
    /// </summary>
    private string _baseUri = "http://127.0.0.1:5000";

    /// <summary>
    /// Backing field for the account username.
    /// </summary>
    private string _username = string.Empty;

    /// <summary>
    /// Backing field for the account role.
    /// </summary>
    private string _role = "Researcher";

    /// <summary>
    /// Backing field for the signature algorithm.
    /// </summary>
    private string _keyAlgorithm = "Ed25519";

    /// <summary>
    /// Backing field for the polling interval in seconds.
    /// </summary>
    private int _pollingIntervalSeconds = 15;

    /// <summary>
    /// Backing field indicating whether notifications are enabled.
    /// </summary>
    private bool _notificationsEnabled = true;

    /// <summary>
    /// Backing field for the status or validation message.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceOnboardingViewModel"/> class.
    /// </summary>
    /// <param name="profileRepository">Repository used to persist instance profiles.</param>
    /// <param name="keyRingVault">Vault used to generate asymmetric key pairs and seal private keys.</param>
    /// <param name="coordinator">Optional multi-instance coordinator for live snapshot tracking.</param>
    public InstanceOnboardingViewModel(
        IInstanceProfileRepository profileRepository,
        IClientKeyRingVault keyRingVault,
        IMultiInstanceCoordinator? coordinator = null)
    {
        // WHAT: Initializes dependencies for profile persistence, cryptographic key generation, and snapshot coordination.
        // WHY:  Decouples the presentation layer from low-level storage and cryptographic implementations.
        // HOW:  Validates non-null mandatory arguments and assigns internal fields.
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _keyRingVault = keyRingVault ?? throw new ArgumentNullException(nameof(keyRingVault));
        _coordinator = coordinator;
    }

    /// <summary>
    /// Gets or sets the human-readable instance display name.
    /// </summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    /// <summary>
    /// Gets or sets the base URI (https:// for remote instances, http://127.0.0.1:5000 for local loopback instances).
    /// </summary>
    public string BaseUri
    {
        get => _baseUri;
        set => SetField(ref _baseUri, value);
    }

    /// <summary>
    /// Gets or sets the account username.
    /// </summary>
    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    /// <summary>
    /// Gets or sets the role ("Administrator", "Researcher", or "Patient").
    /// </summary>
    public string Role
    {
        get => _role;
        set => SetField(ref _role, value);
    }

    /// <summary>
    /// Gets or sets the signature algorithm ("Ed25519" or "ML-DSA-65").
    /// </summary>
    public string KeyAlgorithm
    {
        get => _keyAlgorithm;
        set => SetField(ref _keyAlgorithm, value);
    }

    /// <summary>
    /// Gets or sets the polling interval in seconds (5 to 300).
    /// </summary>
    public int PollingIntervalSeconds
    {
        get => _pollingIntervalSeconds;
        set => SetField(ref _pollingIntervalSeconds, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether notifications are enabled for this node.
    /// </summary>
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetField(ref _notificationsEnabled, value);
    }

    /// <summary>
    /// Gets the validation or status message from the last onboarding action.
    /// </summary>
    public string? StatusMessage => _statusMessage;

    /// <summary>
    /// Generates a new client-side keypair, seals the private key with the supplied passphrase, and saves the instance profile.
    /// </summary>
    /// <param name="passphrase">User passphrase for AES-256-GCM key wrapping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="InstanceProfile"/>, or null if validation or generation failed.</returns>
    public async Task<InstanceProfile?> CreateWithNewKeyBundleAsync(
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Validates input form fields, generates an asymmetric keypair, seals the private key, and persists the profile.
        // WHY:  Allows onboarding a new instance node while keeping the unsealed private key ephemeral in memory.
        // HOW:  Run validation; call _keyRingVault.GenerateKeyBundle; assemble InstanceProfile; upsert into repository.
        if (!ValidateInputs(passphrase, out Uri? uri))
        {
            return null;
        }

        try
        {
            ClientKeyBundle bundle = _keyRingVault.GenerateKeyBundle(_keyAlgorithm.Trim(), passphrase.Span);

            string instanceId = Guid.NewGuid().ToString("N");
            // uri is guaranteed non-null because ValidateInputs returned true
            bool isLocal = uri!.IsLoopback;

            InstanceProfile profile = new(
                InstanceId: instanceId,
                DisplayName: _displayName.Trim(),
                BaseUri: uri.ToString().TrimEnd('/'),
                IsLocalInstance: isLocal,
                Username: _username.Trim().ToLowerInvariant(),
                Role: NormalizeRole(_role),
                KeyAlgorithm: bundle.KeyAlgorithm,
                PublicKeyPem: bundle.PublicKeyPem,
                EncryptedPrivateKeyEnvelopeBase64: bundle.EncryptedPrivateKeyEnvelopeBase64,
                NotificationsEnabled: _notificationsEnabled,
                PollingIntervalSeconds: _pollingIntervalSeconds,
                AddedAtUtc: DateTimeOffset.UtcNow,
                LastConnectedAtUtc: null);

            await _profileRepository.UpsertProfileAsync(profile, ct);
            if (_coordinator is MultiInstanceCoordinator multiCoord)
            {
                multiCoord.RegisterProfile(profile);
            }

            SetStatus($"Instance '{profile.DisplayName}' onboarded successfully.");
            return profile;
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to onboard instance: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Seals an existing PEM private key with the supplied passphrase and saves the instance profile.
    /// </summary>
    /// <param name="publicKeyPem">Public key PEM registered on the target node.</param>
    /// <param name="privateKeyPem">Private key PEM to seal locally.</param>
    /// <param name="passphrase">User passphrase for AES-256-GCM key wrapping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="InstanceProfile"/>, or null if validation failed.</returns>
    public async Task<InstanceProfile?> CreateWithExistingKeyAsync(
        string publicKeyPem,
        string privateKeyPem,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Validates form fields and existing PEM keys, seals the private key with the passphrase, and saves profile.
        // WHY:  Enables importing existing user credentials into a new node profile.
        // HOW:  Run validation; check PEM strings; call _keyRingVault.SealPrivateKeyPem; assemble InstanceProfile; upsert into repository.
        if (!ValidateInputs(passphrase, out Uri? uri))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            SetStatus("Public key PEM cannot be empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            SetStatus("Private key PEM cannot be empty.");
            return null;
        }

        try
        {
            string envelopeBase64 = _keyRingVault.SealPrivateKeyPem(privateKeyPem.Trim(), passphrase.Span);

            string instanceId = Guid.NewGuid().ToString("N");
            // uri is guaranteed non-null because ValidateInputs returned true
            bool isLocal = uri!.IsLoopback;

            InstanceProfile profile = new(
                InstanceId: instanceId,
                DisplayName: _displayName.Trim(),
                BaseUri: uri.ToString().TrimEnd('/'),
                IsLocalInstance: isLocal,
                Username: _username.Trim().ToLowerInvariant(),
                Role: NormalizeRole(_role),
                KeyAlgorithm: _keyAlgorithm.Trim(),
                PublicKeyPem: publicKeyPem.Trim(),
                EncryptedPrivateKeyEnvelopeBase64: envelopeBase64,
                NotificationsEnabled: _notificationsEnabled,
                PollingIntervalSeconds: _pollingIntervalSeconds,
                AddedAtUtc: DateTimeOffset.UtcNow,
                LastConnectedAtUtc: null);

            await _profileRepository.UpsertProfileAsync(profile, ct);
            if (_coordinator is MultiInstanceCoordinator multiCoord)
            {
                multiCoord.RegisterProfile(profile);
            }

            SetStatus($"Instance '{profile.DisplayName}' onboarded successfully.");
            return profile;
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to onboard instance: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates common form fields and passphrase prior to key creation or import.
    /// </summary>
    /// <param name="passphrase">User-provided key encryption passphrase.</param>
    /// <param name="uri">When valid, contains the parsed absolute URI.</param>
    /// <returns>True if all input invariants are satisfied; otherwise false.</returns>
    private bool ValidateInputs(ReadOnlyMemory<char> passphrase, out Uri? uri)
    {
        // WHAT: Validates DisplayName, BaseUri, Username, Role, KeyAlgorithm, PollingInterval, and Passphrase.
        // WHY:  Guarantees profile integrity and enforces Prime Directive 9 and 12 security rules.
        // HOW:  Performs sequential guard checks and populates StatusMessage on the first violation.
        uri = null;

        if (string.IsNullOrWhiteSpace(_displayName))
        {
            SetStatus("Display name cannot be empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_baseUri) || !Uri.TryCreate(_baseUri, UriKind.Absolute, out uri))
        {
            SetStatus("Base URI must be a valid absolute URI.");
            return false;
        }

        bool isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isLoopbackHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            SetStatus("Base URI must use HTTPS for remote instances, or HTTP for localhost/loopback instances.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_username))
        {
            SetStatus("Username cannot be empty.");
            return false;
        }

        if (!IsValidRole(_role))
        {
            SetStatus("Role must be 'Administrator', 'Researcher', or 'Patient'.");
            return false;
        }

        if (!IsValidAlgorithm(_keyAlgorithm))
        {
            SetStatus("Signature algorithm must be 'Ed25519' or 'ML-DSA-65'.");
            return false;
        }

        if (_pollingIntervalSeconds < 5 || _pollingIntervalSeconds > 300)
        {
            SetStatus("Polling interval must be between 5 and 300 seconds.");
            return false;
        }

        if (passphrase.IsEmpty || passphrase.Span.IsWhiteSpace())
        {
            SetStatus("Passphrase cannot be empty or whitespace.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether the role string matches one of the canonical role identities.
    /// </summary>
    /// <param name="role">The candidate role name.</param>
    /// <returns>True if the role is valid; otherwise false.</returns>
    private static bool IsValidRole(string role)
    {
        // WHAT: Validates role against canonical roles.
        // WHY:  Enforces Prime Directive 12 role gating.
        // HOW:  Compares case-insensitively against Administrator, Researcher, Patient.
        return string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "Researcher", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "Patient", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes role string to standard PascalCase.
    /// </summary>
    /// <param name="role">The input role string.</param>
    /// <returns>Normalized role string.</returns>
    private static string NormalizeRole(string role)
    {
        // WHAT: Converts role string to canonical PascalCase.
        // WHY:  Ensures deterministic database persistence.
        // HOW:  Matches case-insensitively and returns canonical constant.
        if (string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase))
        {
            return "Administrator";
        }

        if (string.Equals(role, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            return "Patient";
        }

        return "Researcher";
    }

    /// <summary>
    /// Checks whether the signature algorithm string matches a supported PKI algorithm.
    /// </summary>
    /// <param name="algorithm">The candidate algorithm name.</param>
    /// <returns>True if the algorithm is Ed25519 or ML-DSA-65; otherwise false.</returns>
    private static bool IsValidAlgorithm(string algorithm)
    {
        // WHAT: Checks algorithm name validity.
        // WHY:  Enforces Prime Directive 12 asymmetric PKI identity.
        // HOW:  Compares case-insensitively against Ed25519 and ML-DSA-65.
        return string.Equals(algorithm, "Ed25519", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(algorithm, "ML-DSA-65", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates the status message and fires PropertyChanged notification.
    /// </summary>
    /// <param name="message">The status message to set.</param>
    private void SetStatus(string? message)
    {
        // WHAT: Sets the status message and raises PropertyChanged.
        // WHY:  Provides feedback to the user on form validation or action outcome.
        // HOW:  Assigns _statusMessage and notifies observers.
        _statusMessage = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    /// <summary>
    /// Helper method to set property values and fire PropertyChanged events when modified.
    /// </summary>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">New value to set.</param>
    /// <param name="propertyName">Name of the property being modified.</param>
    /// <returns>True if the value was modified; otherwise false.</returns>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // WHAT: Standard MVVM property mutation helper with equality check.
        // WHY:  Eliminates redundant property change events and boilerplate across ViewModels.
        // HOW:  Checks EqualityComparer<T>.Default; if not equal, mutates field and invokes PropertyChanged.
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
        // WHAT: Invokes the PropertyChanged event.
        // WHY:  Notifies data-bound Avalonia UI components of property state changes.
        // HOW:  Raises PropertyChanged with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
