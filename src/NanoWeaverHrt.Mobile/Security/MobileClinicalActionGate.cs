using System.Text;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Biometric-gated execution wrapper for high-consequence mobile wet-lab clinical actions
/// (NIST FIPS 204 ML-DSA-65 lot release signing and permanent clinical lot revocation).
/// </summary>
/// <remarks>
/// // WHAT: Enforces hardware biometric presence verification before dispatching irreversible wet-lab commands from mobile heads.
/// // WHY:  Milestone 37 (P197): Prevents accidental or unauthorized lot releases and recalls on unlocked handheld tablets/phones.
/// // HOW:  Queries UnifiedMobileBiometricVault.ActiveProvider for availability and interactive authentication, failing closed on denial.
/// </remarks>
public sealed class MobileClinicalActionGate
{
    /// <summary>
    /// Key alias requested from Android Keystore or Apple Secure Enclave for clinical operator presence checks.
    /// </summary>
    public const string ClinicalSigningKeyAlias = "one.lantea.nanoweaver.clinical.operator";

    /// <summary>
    /// Unified mobile biometric vault providing the OS-native <see cref="IBiometricSecurityProvider"/>.
    /// </summary>
    private readonly UnifiedMobileBiometricVault _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobileClinicalActionGate"/> class.
    /// </summary>
    /// <param name="vault">Unified mobile biometric vault coordinating hardware authentication.</param>
    public MobileClinicalActionGate(UnifiedMobileBiometricVault vault)
    {
        // WHAT: Stores the mobile biometric vault reference used to enforce operator presence.
        // WHY:  Centralizes Android Keystore and Apple Secure Enclave verification for clinical commands.
        // HOW:  Validates non-null vault via ArgumentNullException.ThrowIfNull.
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>
    /// Requires hardware biometric confirmation before executing 5-gate wet-lab lot release and ML-DSA-65 signing
    /// on the currently selected fabrication lot.
    /// </summary>
    /// <param name="workspace">Active depot and fabrication lot workspace ViewModel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="FabricationLotReleaseResponse"/> returned by the node after biometric approval.</returns>
    public async Task<FabricationLotReleaseResponse> ReleaseSelectedLotWithBiometricApprovalAsync(
        DepotAndLotWorkspaceViewModel workspace,
        CancellationToken ct = default)
    {
        // WHAT: Prompts for OS biometric confirmation before releasing a fabrication lot for clinical implantation.
        // WHY:  Protects cryptographic ML-DSA-65 CoA issuance with hardware-backed operator presence.
        // HOW:  Verifies biometric availability and interactive auth via EnsureBiometricApprovalAsync, then invokes ReleaseSelectedLotAsync.
        ArgumentNullException.ThrowIfNull(workspace);
        await EnsureBiometricApprovalAsync(
            title: "Approve Wet-Lab Lot Release",
            subtitle: "ML-DSA-65 Certificate Signing",
            description: "Confirm hardware biometrics to execute 5-gate QC release and sign this batch Certificate of Analysis.",
            ct).ConfigureAwait(false);

        return await workspace.ReleaseSelectedLotAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Requires hardware biometric confirmation before permanently revoking the currently selected fabrication lot.
    /// </summary>
    /// <param name="workspace">Active depot and fabrication lot workspace ViewModel.</param>
    /// <param name="reason">Plain-English clinical or cold-chain recall reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The permanent <see cref="FabricationLotRevocationResponse"/>.</returns>
    public async Task<FabricationLotRevocationResponse> RevokeSelectedLotWithBiometricApprovalAsync(
        DepotAndLotWorkspaceViewModel workspace,
        string reason,
        CancellationToken ct = default)
    {
        // WHAT: Prompts for OS biometric confirmation before executing an irreversible clinical lot revocation.
        // WHY:  Permanent lot recall cannot be undone; biometric approval prevents unintended recall dispatches.
        // HOW:  Validates reason, awaits EnsureBiometricApprovalAsync, and invokes workspace.RevokeSelectedLotAsync.
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await EnsureBiometricApprovalAsync(
            title: "Confirm Permanent Lot Recall",
            subtitle: "Irreversible Clinical Revocation",
            description: "Confirm hardware biometrics to permanently revoke this fabrication lot and block implantation.",
            ct).ConfigureAwait(false);

        return await workspace.RevokeSelectedLotAsync(reason, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies hardware biometric availability and completes an interactive OS authentication challenge,
    /// failing closed with <see cref="UnauthorizedAccessException"/> if unavailable or denied.
    /// </summary>
    /// <param name="title">Prompt headline.</param>
    /// <param name="subtitle">Prompt subtitle.</param>
    /// <param name="description">Detailed action explanation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes only when hardware biometric verification succeeds.</returns>
    private async Task EnsureBiometricApprovalAsync(
        string title,
        string subtitle,
        string description,
        CancellationToken ct)
    {
        // WHAT: Checks biometric sensor readiness and requests interactive user approval from the OS provider.
        // WHY:  Enforces fail-closed security semantics when biometrics are disabled, locked out, or cancelled.
        // HOW:  Calls CheckBiometricAvailabilityAsync and AuthenticateAndDecryptAsync; throws UnauthorizedAccessException on failure.
        IBiometricSecurityProvider provider = _vault.ActiveProvider;
        BiometricAvailabilityResult availability = await provider.CheckBiometricAvailabilityAsync(ct).ConfigureAwait(false);
        if (!availability.IsAvailable)
        {
            throw new UnauthorizedAccessException($"Biometric authentication is unavailable ({availability.Status}): {availability.StatusMessage}");
        }

        byte[] challengeBytes = Encoding.UTF8.GetBytes($"{title}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        var authRequest = new BiometricAuthRequest(
            Title: title,
            Subtitle: subtitle,
            Description: description,
            KeyAlias: ClinicalSigningKeyAlias,
            EncryptedVaultPayload: challengeBytes);

        BiometricAuthResult authResult = await provider.AuthenticateAndDecryptAsync(authRequest, ct).ConfigureAwait(false);
        if (!authResult.Success)
        {
            string detail = string.IsNullOrWhiteSpace(authResult.ErrorMessage)
                ? authResult.Status.ToString()
                : authResult.ErrorMessage;
            throw new UnauthorizedAccessException($"Biometric approval was denied ({authResult.Status}): {detail}");
        }
    }
}
