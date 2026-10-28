# Work Package P17: bUnit Coverage for Security-Critical Razor Components (`MainLayout`, `Login`, `SetupWizard`)

- **Phase ID**: `P17`
- **Recommended Model Tier**: `flash` / Haiku / Sonnet — **Split into Stage A (`bunit` Setup + `Login.razor` & `SetupWizard.razor` Tests) and Stage B (`MainLayout.razor` Route Guard & Idle Sign-Out Tests + `coverlet.runsettings` Un-Exclusion)**
- **Parent Document**: [`work-packages/PHASES-v2.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/PHASES-v2.md) (Phase P17)
- **Prerequisites**: `P15` (`work-packages/c8-client.md`) and `P16` (`work-packages/c8-remove-oracle.md`)
- **Blocks**: `P18` (`work-packages/p18-coverage-baseline-closeout.md`)

---

## 1. Recorded User Decisions (Authoritative)

1. **bUnit Package & Scope**:
   - Add `bunit` (`1.38.5` or latest `1.x` stable compatible with `.NET 10`) to [`tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj).
   - Cover **only** the three security-critical components:
     1. [`src/NanoWeaverHrt/Components/Layout/MainLayout.razor`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Components/Layout/MainLayout.razor)
     2. [`src/NanoWeaverHrt/Components/Pages/Login.razor`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Components/Pages/Login.razor)
     3. [`src/NanoWeaverHrt/Components/Pages/SetupWizard.razor`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Components/Pages/SetupWizard.razor)
   - Do **not** expand scope to non-security dashboard pages (`WorkflowWizard`, `EndocrineDashboard`, etc.).
2. **Coverlet Un-Exclusion**:
   - Update [`coverlet.runsettings`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/coverlet.runsettings) and [`coverage-full.runsettings`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/coverage-full.runsettings) to remove the blanket `[NanoWeaverHrt]NanoWeaverHrt.Components.*` and `**/Components/**/*.razor` exclusions, replacing them with explicit exclusions for the non-security components (`App`, `Routes`, `ComplianceSecurity`, `ConfigurationManagement`, `DnaManagementPage`, `EndocrineDashboard`, `PeerConnect`, `SimulationStudio`, `StackServices`, `TrajectoryBrowser`, `UserManagement`, `UserProfilePage`, `WorkflowWizard`), so `MainLayout`, `Login`, and `SetupWizard` are enforced at 100.00% by Gate Step 4.

---

## 2. Files in Scope

| Stage | File | Action | Est. Lines |
| :--- | :--- | :--- | :--- |
| **Stage A** | [`tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj) | Add `<PackageReference Include="bunit" Version="1.38.5" />` | +1 |
| **Stage A** | `tests/NanoWeaverHrt.Tests/LoginComponentTests.cs` | New bUnit test suite for `Login.razor` (100% line/branch) | +160 |
| **Stage A** | `tests/NanoWeaverHrt.Tests/SetupWizardComponentTests.cs` | New bUnit test suite for `SetupWizard.razor` (100% line/branch) | +280 |
| **Stage B** | `tests/NanoWeaverHrt.Tests/MainLayoutComponentTests.cs` | New bUnit test suite for `MainLayout.razor` route guard, telemetry latch, and idle sign-out | +260 |
| **Stage B** | [`coverlet.runsettings`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/coverlet.runsettings) | Un-exclude `MainLayout`, `Login`, and `SetupWizard` | +5 / -2 |
| **Stage B** | [`coverage-full.runsettings`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/coverage-full.runsettings) | Keep `Exclude` / `ExcludeByFile` in sync with `coverlet.runsettings` | +5 / -2 |

---

## 3. Stage A: `bunit` Package & `Login.razor` + `SetupWizard.razor` Tests

### Step A1: Add `bunit` to `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`
Add `<PackageReference Include="bunit" Version="1.38.5" />` to `NanoWeaverHrt.Tests.csproj` and run `XDG_RUNTIME_DIR=/tmp dotnet restore NanoWeaverHrt.slnx` (if inside the offline sandbox, verify whether `bunit` is already in `/home/fabigail/.nuget/packages` or run `dotnet restore` with `BypassSandbox: true`). Confirm zero `NuGetAudit` warnings under `/warnaserror`.

### Step A2: Create `tests/NanoWeaverHrt.Tests/LoginComponentTests.cs`
Using bUnit's `TestContext`:
1. **Unauthenticated Render & Idle Timeout Banner**:
   - Navigate to `http://localhost/login?reason=idle_timeout` via `Services.GetRequiredService<NavigationManager>().NavigateTo(...)`.
   - Render `<Login />`.
   - Assert `.alert-warning` displays `"Your session has expired due to inactivity. Please sign in again to access the platform."`.
   - Assert `#loginUser`, `#privKeyPem`, `#loginPassword`, `#keyFileUpload`, and `#loginSubmitBtn` exist, and `JSInterop` invoked `weaverBindKeyFileInput`.
2. **Mask / Show Private Key Toggle**:
   - Click the mask toggle button (`🔒 Mask Key` / `👁 Show Key`) and assert `#privKeyPem` toggles the `is-masked` CSS class.
3. **Sign-In Success & Failure via `weaverSignIn` JS Interop**:
   - Configure `JSInterop.Setup<WeaverSignInResult>("weaverSignIn")` to return `{ Ok = false, Error = "Cryptographic signature verification failed." }`, click `#loginSubmitBtn`, and assert `.alert-danger` renders the error message.
   - Configure `JSInterop.Setup<WeaverSignInResult>("weaverSignIn")` to return `{ Ok = true, User = adminUser, Token = "tok" }`, click `#loginSubmitBtn`, and assert `SessionState.IsAuthenticated == true` and `NavigationManager.Uri` navigated to `http://localhost/`.
4. **Already-Authenticated View & SignOut Button**:
   - Pre-populate `SessionState.SetUser(adminUser)`, render `<Login />`, assert `"Currently signed in as"` is shown, click `Sign Out`, and assert `weaverClearSession` was called (and test the `catch` branch when `JSInterop` throws `JSDisconnectedException`), `SessionState.IsAuthenticated == false`, and navigation is `/login`.

### Step A3: Create `tests/NanoWeaverHrt.Tests/SetupWizardComponentTests.cs`
Using bUnit's `TestContext`, cover all 3 steps and branches of `SetupWizard.razor`:
1. **Already Initialized State (`_isAlreadySetup == true`)**:
   - When `AuthService.IsSetupRequiredAsync()` returns `false`, assert `"System Initialized:"` alert renders and the stepper is hidden.
2. **Step 1 Validation & Navigation**:
   - Clear `#adminUsername` to `""`, click `Next: Cryptographic Keys`, and assert `"Administrator username must not be empty."`.
   - Set `#adminUsername` to `"sysadmin"`, select `AccessGate.Web`, click `Next`, and assert Step 2 renders.
3. **Step 2 Option 0 (Generate Secure Keypair)**:
   - Test `ProceedToStep3` before generating keys -> `"Please click 'Generate High-Security Keypair' first."`.
   - Test `GenerateKeys` success (`Ed25519` and `MlDsa65`, with and without `_keyPassword`) and exception branch (`"Key generation error: ..."`).
   - Test `ProceedToStep3` before checking `#backupConfirm` -> `"You must confirm that you have downloaded, copied, or backed up your private key."`.
   - Test `DownloadPrivateKey` (`weaverDownloadBlob` returning `true`, returning `false`, and throwing an exception).
   - Test `CopyPrivateKey` (`weaverCopyText` succeeding and throwing an exception).
4. **Step 2 Option 1 (Upload / Paste Custom Public Key)**:
   - Switch to tab 1 (`Upload Custom Public Key`), test `ProceedToStep3` with empty `_customPublicKeyText` -> `"Please provide your public key text."`.
   - Test `HandleCustomPubKeyUpload` with a valid PEM file stream and with an oversized/throwing file stream.
   - Test `GetFingerprint()` with empty string (`"N/A"`), short string (`<= 24` chars), and standard PEM/Base64 string (`> 24` chars).
5. **Step 3 FinalizeSetup**:
   - Click `Initialize Sovereign Platform`, verify `AuthService.CompleteInitialSetupAsync`, `AuthService.GenerateBearerToken`, `weaverSetSession` (both success and throwing catch branch), and redirect to `/`.
   - Test `FinalizeSetup` when `CompleteInitialSetupAsync` throws -> `"Initialization failed: ..."`.

---

## 4. Stage B: `MainLayout.razor` Tests & Coverlet Un-Exclusion

### Step B1: Create `tests/NanoWeaverHrt.Tests/MainLayoutComponentTests.cs`
Using bUnit's `TestContext`:
1. **Unauthenticated Visitor on Protected Route (`/` or `/compliance`)**:
   - `AuthService.IsSetupRequiredAsync()` returns `false`, `weaverGetSessionToken` returns `null`.
   - Render `<MainLayout Body="@(builder => builder.AddMarkupContent(0, "<div id='protected-body'>secret</div>"))" />`.
   - Assert `#protected-body`, `.sub-navbar`, `.telemetry-grid`, and `.node-status` are **not** in the rendered markup (`ShellVisible == false`), and `NavigationManager.Uri` redirected to `/login`.
2. **Setup Lock-Out (`_setupRequired == true`)**:
   - `AuthService.IsSetupRequiredAsync()` returns `true`.
   - Navigate to `/login` or `/`, render `<MainLayout />`, and assert redirect to `/setup`.
   - When on `/setup`, assert `@Body` renders while `.sub-navbar` and `.telemetry-grid` remain hidden.
3. **Session Restoration via `weaverGetSessionToken` & Authenticated Shell**:
   - `weaverGetSessionToken` returns `"valid-token"`, `AuthService.ValidateTokenAsync("valid-token")` returns `(true, adminUser, null)`.
   - Render `<MainLayout />` on `/`.
   - Assert `.node-status`, `.sub-navbar`, `.telemetry-grid`, and `👥 User Management` (Admin-only link) render, `weaverRegisterIdleWatcher` is invoked, and `EnsureTelemetryStartedAsync` initializes once.
   - Also test a non-Admin (`UserRole.Researcher`) to cover the `@if (SessionState.IsAdmin)` false branch.
4. **LocationChanged Event, SignOut, Heartbeat (`RecordActivity`), and Idle Timeout (`OnUserIdleTimeout`)**:
   - Invoke `MainLayout.RecordActivity()` -> asserts `SessionActivity.RecordActivity("valid-token")` is called.
   - Invoke `MainLayout.OnUserIdleTimeout()` when authenticated (and when unauthenticated early-return) -> asserts `SessionActivity.Revoke("valid-token")`, `weaverClearSession`, `SessionState.IsAuthenticated == false`, and navigation to `/login?reason=idle_timeout`.
   - Click the `Sign Out` button (`HandleSignOut`) -> asserts `SessionActivity.Revoke`, `weaverClearSession`, and navigation to `/login`.
   - Dispose the component (`Dispose()`) and assert clean teardown.

### Step B2: Un-Exclude `MainLayout`, `Login`, and `SetupWizard` in `coverlet.runsettings` & `coverage-full.runsettings`
Update both `coverlet.runsettings` and `coverage-full.runsettings` so `MainLayout.razor`, `Login.razor`, and `SetupWizard.razor` are no longer excluded, while keeping the remaining 14 non-security `.razor` files excluded in `<ExcludeByFile>`:
```xml
<Exclude>[NanoWeaverHrt.Tests]*,[NanoWeaverHrt]AutoGeneratedProgram</Exclude>
<ExcludeByFile>**/Components/App.razor,**/Components/Routes.razor,**/Components/Pages/ComplianceSecurity.razor,**/Components/Pages/ConfigurationManagement.razor,**/Components/Pages/DnaManagementPage.razor,**/Components/Pages/EndocrineDashboard.razor,**/Components/Pages/PeerConnect.razor,**/Components/Pages/SimulationStudio.razor,**/Components/Pages/StackServices.razor,**/Components/Pages/TrajectoryBrowser.razor,**/Components/Pages/UserManagement.razor,**/Components/Pages/UserProfilePage.razor,**/Components/Pages/WorkflowWizard.razor</ExcludeByFile>
```

---

## 5. Verification Gate Checklist

Run the full verification gate:
```bash
./scripts/verify-all.sh
```
1. Confirm Gate Step 4 reports 100.00% line and branch coverage with `MainLayout`, `Login`, and `SetupWizard` included in the Cobertura report.
2. Temporarily comment out the `EnforceRouteGuard()` call in `MainLayout.razor`, run `dotnet test`, verify `MainLayoutComponentTests` fails immediately, and restore `EnforceRouteGuard()`.
