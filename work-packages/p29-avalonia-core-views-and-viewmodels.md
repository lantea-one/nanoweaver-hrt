# Work Package P29: Sovereign Cross-Platform GUI Core ViewModels & Avalonia Views (`NanoWeaverHrt.Gui.Core`)

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: Both the Desktop (`Linux`, `macOS`, `Windows`) and Mobile (`Android`, `iOS`, `iPadOS`) standalone applications share the same multi-instance management workflows: onboarding local/remote nodes with `Ed25519`/`ML-DSA-65` keys, switching between connected instances, monitoring live GPU/parser telemetry, launching simulation campaigns, and reviewing notification history.
- **Goal & Scope**:
  - **Included**: Implement the MVVM ViewModels (`MainShellViewModel`, `InstanceOnboardingViewModel`, `InstanceDashboardViewModel`, `NotificationCenterViewModel`) and shared Avalonia UI 11 dark-mode views (`App.axaml`, `MainShellView.axaml`) inside `src/NanoWeaverHrt.Gui.Core`, with 100% unit and headless test coverage in `tests/NanoWeaverHrt.Tests/Gui/`.
  - **Excluded**: OS-specific executable wrappers and installer scripts (P30, P32, P33).
- **Target Role / Boundary**: `Administrator`, `Researcher`, `Patient`; Desktop & Mobile shared UI layer.

## 2. Technical Contracts & Invariants

### 2.1 ViewModels (`namespace NanoWeaverHrt.Gui.Core.ViewModels`)

```csharp
namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel for onboarding a new local or remote nanoweaver-hrt instance profile and generating or sealing its PKI keypair.
/// </summary>
public sealed class InstanceOnboardingViewModel : INotifyPropertyChanged
{
    /// <summary>Gets or sets the human-readable instance display name.</summary>
    public string DisplayName { get; set; }

    /// <summary>Gets or sets the base URI (https:// for remote, http://127.0.0.1:5000 for local).</summary>
    public string BaseUri { get; set; }

    /// <summary>Gets or sets the account username.</summary>
    public string Username { get; set; }

    /// <summary>Gets or sets the role ("Administrator", "Researcher", or "Patient").</summary>
    public string Role { get; set; }

    /// <summary>Gets or sets the signature algorithm ("Ed25519" or "ML-DSA-65").</summary>
    public string KeyAlgorithm { get; set; }

    /// <summary>Gets or sets the polling interval in seconds (5 to 300).</summary>
    public int PollingIntervalSeconds { get; set; }

    /// <summary>Gets or sets a value indicating whether notifications are enabled for this node.</summary>
    public bool NotificationsEnabled { get; set; }

    /// <summary>Gets the validation or status message from the last onboarding action.</summary>
    public string? StatusMessage { get; }

    /// <summary>
    /// Generates a new client-side keypair, seals the private key with the supplied passphrase, and saves the instance profile.
    /// </summary>
    /// <param name="passphrase">User passphrase for AES-256-GCM key wrapping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created InstanceProfile, or null if validation failed.</returns>
    public Task<InstanceProfile?> CreateWithNewKeyBundleAsync(ReadOnlyMemory<char> passphrase, CancellationToken ct = default);

    /// <summary>
    /// Seals an existing PEM private key with the supplied passphrase and saves the instance profile.
    /// </summary>
    /// <param name="publicKeyPem">Public key PEM registered on the target node.</param>
    /// <param name="privateKeyPem">Private key PEM to seal locally.</param>
    /// <param name="passphrase">User passphrase for AES-256-GCM key wrapping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created InstanceProfile, or null if validation failed.</returns>
    public Task<InstanceProfile?> CreateWithExistingKeyAsync(
        string publicKeyPem,
        string privateKeyPem,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default);
}

/// <summary>
/// Root shell ViewModel coordinating the multi-instance sidebar, active node dashboard, background tray state, and alert feed.
/// </summary>
public sealed class MainShellViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the collection of configured instance profiles.</summary>
    public IReadOnlyList<InstanceProfile> Profiles { get; }

    /// <summary>Gets the collection of live connection snapshots across all instances.</summary>
    public IReadOnlyList<InstanceConnectionSnapshot> Snapshots { get; }

    /// <summary>Gets the recent alert history feed.</summary>
    public IReadOnlyList<InstanceAlertEvent> AlertFeed { get; }

    /// <summary>Gets or sets the currently selected instance ID in the multi-instance switcher.</summary>
    public string? SelectedInstanceId { get; set; }

    /// <summary>Gets the snapshot for the currently selected instance, if any.</summary>
    public InstanceConnectionSnapshot? SelectedSnapshot { get; }

    /// <summary>
    /// Loads saved profiles from SQLite and refreshes the shell state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous load operation.</returns>
    public Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Connects the currently selected instance using the provided passphrase.
    /// </summary>
    /// <param name="passphrase">Passphrase for unsealing the selected instance's private key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if authentication succeeded; otherwise false.</returns>
    public Task<bool> ConnectSelectedInstanceAsync(ReadOnlyMemory<char> passphrase, CancellationToken ct = default);

    /// <summary>
    /// Triggers a simulation campaign on the currently selected online instance.
    /// </summary>
    /// <param name="simulationType">Campaign slug ("serum-stability", "aptamer-gating", "payload-leakage", "pbpk-endocrine").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the simulation started; otherwise false.</returns>
    public Task<bool> TriggerSimulationOnSelectedAsync(string simulationType, CancellationToken ct = default);

    /// <summary>
    /// Removes an instance profile from the local registry and disconnects any active session.
    /// </summary>
    /// <param name="instanceId">Instance identifier to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if removed; otherwise false.</returns>
    public Task<bool> RemoveInstanceAsync(string instanceId, CancellationToken ct = default);
}
```

### 2.2 Coverage & Runsettings Invariants
- If Avalonia XAML views (`*.axaml`) generate compiler code, ensure `coverlet.runsettings` and `coverage-full.runsettings` remain 100% synchronized in `<ExcludeByFile>` if any non-security XAML views are excluded, while all ViewModels (`InstanceOnboardingViewModel`, `MainShellViewModel`) are 100% line- and branch-tested in `tests/NanoWeaverHrt.Tests/Gui/GuiViewModelsTests.cs`.
- Never store raw passphrases as properties on any ViewModel ( mirror P15/P20 security invariant: passphrases are passed only as `ReadOnlyMemory<char>` / `ReadOnlySpan<char>` parameters to action methods).

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Implement `InstanceOnboardingViewModel` and `MainShellViewModel` in `src/NanoWeaverHrt.Gui.Core/ViewModels/` with complete XML docblocks and `// WHAT / WHY / HOW` inline comments.
2. **Task 2**: Add Avalonia UI 11 styling and dark-mode responsive shell descriptors (`SovereignPalette` constants matching `#0b0f19` background, `#00f5d4` cyan accent, `#ff2a85` magenta alert) so desktop and mobile heads share identical visual tokens.
3. **Task 3**: Write 100% line and branch unit tests in `tests/NanoWeaverHrt.Tests/Gui/GuiViewModelsTests.cs` exercising all validation paths, multi-instance switching, simulation triggering, error recovery, and profile deletion.
4. **Task 4**: Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
