# Work Packages (`PHASES-v2.md`) — Status & Execution Playbook

> **Why this file is under `work-packages/`:** Gate step 5 scans `docs/**/*.md` and `README.md` at the repo root. Keeping work-package guides under `work-packages/` avoids interfering with documentation scans.

---

## 1. Status of Work Packages

### 1.1 Master Architecture & Design Documents
- [`PHASES-v2.md`](PHASES-v2.md) — Master Remediation & Feature Phase Plan (`P1–P20` permanent historical audit reference)

### 1.2 Active Work Packages (`feature/gui-application`: `P26–P35`)

| Phase | Work Package File | Target Model Tier | Architectural Layer & Scope | Status |
| :--- | :--- | :--- | :--- | :--- |
| **P26** | Standalone Instance Store & Vault | `NanoWeaverHrt.Client` SQLite multi-instance registry (`IInstanceProfileRepository`) & `AES-256-GCM` client key vault (`IClientKeyRingVault`, `Ed25519` & `ML-DSA-65`) | **COMPLETED** |
| **P27** | Multi-Instance API Client SDK | **Layer 2 (Domain & Auth SDK)**: `INanoWeaverNodeClient` challenge-response PKI auth, telemetry polling, `IMultiInstanceCoordinator` & state-transition alerts | **COMPLETED** |
| **P28** | Background Tray & Notifications | **Layer 2/3 (Background Daemon & Tray)**: `NanoWeaverHrt.Gui.Core` `IBackgroundInstanceMonitor`, `ITrayIconController`, `INativeNotificationSink` & 15-min idle lock | **COMPLETED** |
| **P29** | Core ViewModels & Dark Theme | **Layer 4a (Shared GUI Core & ViewModels)**: `InstanceOnboardingViewModel`, `MainShellViewModel`, sovereign dark-theme palette & headless xUnit tests | **COMPLETED** |
| **P30** | Desktop & Mobile Platform Heads | **Layer 4b (Desktop & Mobile Heads)**: `NanoWeaverHrt.Desktop` (`NanoWeaverHrt`) & `NanoWeaverHrt.Mobile` executables, native OS notification/tray adapters (`Linux`, `macOS`, `Windows`, `Android`, `iOS`, `iPadOS`) | **COMPLETED** |
| **P31** | Sovereign Code Signing & Rotation | `scripts/pki/provision-or-rotate-codesign.sh`, `sign-packages.sh`, 90-day leaf cert rotation, public trust anchors in `docs/assets/keys/`, Forgejo & GitHub Actions rotation workflows | **COMPLETED** |
| **P32** | Linux & AUR Packaging | `packaging/linux/`, `packaging/aur/`, Arch `.pkg.tar.zst` (`x86_64` & `arm64`), unified `.deb` (`amd64` & `arm64`), unified `.rpm` (`amd64` & `arm64`), and AUR (`-bin` & `-git`) | **COMPLETED** |
| **P33** | macOS, Windows & Mobile Packaging | `flash` / `sonnet` | **Layer 5c (macOS, Windows & Mobile Packaging)**: macOS `NanoWeaverHrt.dmg`, Windows dual-scope `nanoweaver-hrt-desktop-installer.exe` (`Current User` / `System`) + `NanoWeaverHrt.msix`, and Mobile `.apk` & `.ipa` (`Android`, `iOS`, `iPadOS`) | **COMPLETED** |
| **P34** | CI/CD Multi-Platform Artifact Matrix | `.forgejo/workflows/build-and-deploy.yml` & `.github/workflows/build-and-deploy.yml` triggers on `feature/gui-application`, all 8 gates mirrored, and `build-and-publish-gui-artifacts` job uploading 5 package bundles | **COMPLETED** |
| **P35** | Documentation, Verification & Final Closeout | `docs/desktop-and-mobile-gui.md`, Docsify sidebar, release notes, and full 8-gate verification | **COMPLETED** |

---

### 1.3 Completed & Ratified Work Packages (`P1–P25`)
*Note: Individual completed work package instruction files have been cleaned up and consolidated into the repository audit trail and `PHASES-v2.md`.*

| Phase | Package / Scope | Key Deliverables & Milestones | Verification Status |
| :--- | :--- | :--- | :--- |
| **P1–P3** | Core Service Architecture | Minimal API pipeline, zero-allocation SIMD trajectory parser, memory pipelines | **COMPLETED** |
| **P4 / P5** | Database Seam Rollout | `IWeaverDbConnectionFactory` seam across user, DNA, and configuration stores | **COMPLETED** |
| **P6** | User Profile Seam | `UserProfileRepository` on `IWeaverDbConnectionFactory` seam | **COMPLETED** |
| **P7.1–P7.3** | Gender Identity Protection | `Migration006_EncryptGenderIdentity` + coverage ratchet determinism | **COMPLETED** |
| **P7.4** | Subject Key Store | `Migration008_SubjectKeyStore`, `ISubjectKeyRepository`, `SubjectKeyVault` AES-256-GCM sealing, `SubjectErasureJournal`, GDPR Art. 17 erasure + `VACUUM` | **COMPLETED** |
| **P7.5** | Node Signing Key | Persistent 32-byte FIPS 204 seed (`node_signing_mldsa65.key`), `PublicKeyFingerprint`, `SignWithNodeSigningKey` | **COMPLETED** |
| **P8** | Dataset Signing | `DatasetManifest` v2 signatures, `DatasetManifestCanonicalizer`, `NodeDatasetSigner` (`dataset_signing_ed25519.key`), `DatasetManifestSignatureVerifier` | **COMPLETED** |
| **P9** | Peer Key Registry | `Migration007_PeerKeyRegistry`, `IPeerKeyRegistry`, `VerifiedDatasetImporter`, `Peer:RequireSignedDatasets`, `/api/peer-keys` endpoints | **COMPLETED** |
| **P10** | Auth & DNA Hardening | Case-insensitive usernames, `GET /api/patient/dna` scope/format validation, L8 docblock, duplicate audit assertion | **COMPLETED** |
| **P10.5** | Audit Ledger Store | `Migration009_AuditLedgerStore` (`weaver_audit_ledger`), `IAuditLedgerRepository`, synchronous `LogAction` DB persistence across restarts | **COMPLETED** |
| **P11** | Hygiene & Latent Bugs | Dead flag removal in `WorkflowWizard.razor`, `ValkeyCacheService` invariants, `CultureInfo.InvariantCulture` timestamps, simulation container permissions | **COMPLETED** |
| **P11.5** | DNA Regimen Encryption | `Migration010_EncryptPatientDnaPhi` encrypting `regimen` and `notes` on `weaver_patient_dna_records` via `ProfileDataProtector` | **COMPLETED** |
| **P12** | Deployment Truth | Removal of legacy P-256 claims in docs, complete environment variable audit, dedicated `./simulations/node-keys` mount, `docs/release-notes.md` | **COMPLETED** |
| **P13** | PKCS#8 DER Spike | BouncyCastle 2.7 Ed25519 & ML-DSA-65 PKCS#8 byte layout specification, ASN.1 SEQUENCE detection | **COMPLETED** |
| **P14** | Server Web-Token Auth | `POST /api/auth/web-token` (`isWebRequest: true`, `HttpOnly` cookie, same-origin + public origin validation), 120s lifetime, 10,000 challenge cap with pruning | **COMPLETED** |
| **P15** | Client Noble Crypto | Vendored `@noble/ed25519` + `@noble/post-quantum` (`ml_dsa65`), `wwwroot/js/weaver-auth.js` (100% JS coverage), Gate Step 8 SRI validation, rewritten `Login.razor` | **COMPLETED** |
| **P16** | Key Oracle Elimination | Complete elimination of server-side private key upload endpoint (`POST /api/auth/login-key`) and server-side private key decryption routines | **COMPLETED** |
| **P17** | bUnit Security Testing | Integrated `bunit` `1.38.5`, 100% component testing across `Login.razor`, `SetupWizard.razor`, and `MainLayout.razor` route guards | **COMPLETED** |
| **P18** | Coverage Ratchet Closeout | Reached 100.00% line & branch coverage on non-compiler code, ratified baseline at 99.51% line / 96.38% branch across all state machines | **COMPLETED** |
| **P19** | Multi-Container & E2E Verification | Multi-container Docker Compose validation (strict 127.0.0.1 bindings, 0600 keys, health probes, clean migrations), Playwright 390px mobile & interactive Ed25519/ML-DSA-65 sign-in | **COMPLETED** |
| **P20** | In-Browser Key Generation & SRI | Pure client-side Ed25519 & ML-DSA-65 keypair derivation and AES-GCM envelope sealing in browser memory, removal of server-side key generation endpoints (`POST /api/setup/generate-keypair`, `POST /api/users/generate-keypair`), and server-side dynamic .NET W3C Subresource Integrity (SRI) SHA-384 generation for all authored scripts and styles via `IAssetIntegrityService`. | **COMPLETED** |
| **P21** | Subject Access Schema & Repo | `Migration011_SubjectAccessAndGroups`, `ISubjectAccessRepository`, `SubjectGrant`, `SubjectGroup`, `SubjectGroupMember`, `SubjectGroupGrant`, tiered access levels (`ReadAccess: 1`, `GenomicAccess: 2`, `FullReadAccess: 3`) | **COMPLETED** |
| **P22** | Access Control Service & Gating | `ISubjectAccessService`, `SubjectAccessService` effective tiered access resolution, cohort boundary enforcement, audit logging to `weaver_audit_ledger`, and route gating in minimal API pipeline (`ResolveSubjectRecordAccess`) | **COMPLETED** |
| **P23** | Subject Access REST API | 14 minimal API endpoints for subject grants (`/api/subject-access/grants`), my-subjects (`/api/subject-access/my-subjects`), cohort group CRUD, group members, and group grants (`/api/subject-groups`) with OpenAPI documentation | **COMPLETED** |
| **P24** | Sovereign Cohort Web UI | `CohortManagementModal` component, `UserManagement` grants management modal & cohort launcher, `DnaManagementPage` cohort filtering & zero-access alert banner, and bUnit test suite (`CohortManagementComponentTests`) | **COMPLETED** |
| **P25** | Verification & Closeout | Full 8-gate pre-flight verification, comprehensive documentation across `compliance-and-security.md` and `api-reference.md`, and ratchet closeout | **COMPLETED** |
| **P26** | Instance Store & Key Vault | `NanoWeaverHrt.Client` SQLite multi-instance registry (`IInstanceProfileRepository`) & `AES-256-GCM` client key vault (`IClientKeyRingVault`, `Ed25519` & `ML-DSA-65`) | **COMPLETED** |
| **P27** | Multi-Instance API Client SDK | `INanoWeaverNodeClient` PKI challenge-response authentication, telemetry/simulation REST client, and `IMultiInstanceCoordinator` alert diffing | **COMPLETED** |
| **P28** | Background Tray & Notifications | `NanoWeaverHrt.Gui.Core` `IBackgroundInstanceMonitor`, `ITrayIconController`, `INativeNotificationSink` & 15-min idle lock | **COMPLETED** |
| **P29** | Core ViewModels & Dark Theme | `NanoWeaverHrt.Gui.Core` `InstanceOnboardingViewModel`, `MainShellViewModel`, `SovereignPalette` dark theme tokens, and 100% unit tests | **COMPLETED** |
| **P30** | Desktop & Mobile Platform Heads | `NanoWeaverHrt.Desktop` (`NanoWeaverHrt`) & `NanoWeaverHrt.Mobile` executables, cross-platform notification/tray adapters, and 100% unit tests | **COMPLETED** |
| **P31** | Sovereign Code Signing & Rotation | Sovereign X.509 `id-kp-codeSigning` CA + 90-day leaf certificate rotation, GPG archive keyring, trust anchors in `docs/assets/keys/`, and scheduled CI rotation workflows | **COMPLETED** |
| **P32** | Linux & AUR Packaging | Desktop integration (`.desktop`, autostart tray, AppStream metainfo, SVG icon), AUR `nanoweaver-hrt-desktop-bin` and `-git`, unified `.deb`, `.rpm`, and Arch `.pkg.tar.zst` (`x86_64` & `arm64`) | **COMPLETED** |
| **P33** | macOS, Windows & Mobile Packaging | macOS `NanoWeaverHrt.dmg`, Windows dual-scope `nanoweaver-hrt-desktop-installer.exe` (`Current User` / `System`) + `NanoWeaverHrt.msix`, and Mobile `.apk` & `.ipa` (`Android`, `iOS`, `iPadOS`) | **COMPLETED** |
| **P34** | CI/CD Artifact Pipelines | Multi-platform build, sign, and artifact upload across Forgejo Actions (`archlinux-multi-platform`) and GitHub Actions (`ubuntu-latest`) on `feature/gui-application` and `main` | **COMPLETED** |
| **P35** | Documentation & Verification Closeout | `docs/desktop-and-mobile-gui.md` comprehensive guides, Docsify sidebar integration, release notes update, and full 8-gate verification | **COMPLETED** |

---

## 2. Summary of Recorded User Decisions (`P10–P20`)

All architectural and scoping questions were presented via interactive forms and resolved as follows:
1. **Repository Tracking**: Track [`work-packages/PHASES-v2.md`](PHASES-v2.md) and [`work-packages/c8-challenge-response-proposal.md`](c8-challenge-response-proposal.md) in git alongside active packages.
2. **P5–P9 Follow-Up Sub-Packages**: Keep `P10–P18` strictly scoped to `PHASES-v2.md` and publish `P10.5` (`weaver_audit_ledger` DB persistence with synchronous `LogAction`) and `P11.5` (`weaver_patient_dna_records` `regimen` + `notes` encryption at rest via `Migration010_EncryptPatientDnaPhi`).
3. **[P10] Username Case Rule & `GET /api/patient/dna`**: Normalize all usernames to lowercase (`username.Trim().ToLowerInvariant()`) across create, lookup, challenge, and delete. On `GET /api/patient/dna`, default omitted `patientId` to `currentUser.Username`, validate format (`<= 64` chars, `[A-Za-z0-9._-]`) returning `400`, and return `404 SubjectRecordNotFoundDetail` when a non-Admin requests another user's `patientId`.
4. **[P11] `WorkflowWizard.razor` & Hygiene Sweep**: Remove the dead `_isSimulating`, `_isParsing`, and `_isExportingParquet` flags and unreachable spinner branches in `WorkflowWizard.razor`, add `// WHY !:` comments in `ValkeyCacheService.cs:98` and `ValkeyEventBridge.cs:263`, use `CultureInfo.InvariantCulture` in `WeaverDbValues.ReadTimestamp`, add `/workspace/configs` to `chown 1000:1000` in `Dockerfile.simulation`, and replace `/home/fabigail` with `/home/operator` in `ApiRoleGatingAndDisclosureTests.cs`.
5. **[P12] Dedicated Node-Key Mount & Release Notes**: Set `WEAVER_NODE_KEYS_DIR=/workspace/node-keys` and mount `./simulations/node-keys:/workspace/node-keys:rw` by default in `docker-compose.yml`, add `/workspace/node-keys` to `Dockerfile.web`, track `simulations/node-keys/.gitkeep`, and create `docs/release-notes.md`.
6. **[P13 / P14 / P15 / P16] C8 Challenge-Response & ML-DSA-65**:
   - Vendor both `@noble/ed25519` (with `@noble/hashes/sha512`) and `@noble/post-quantum` (`ml_dsa65`) in `wwwroot/lib/` so both `Ed25519` and `ML-DSA-65` accounts can sign in via the browser UI.
   - Keep signing the raw 32-byte challenge nonce.
   - Cap `_activeChallenges` at `10,000` (`429 TooManyRequests` when full) and prune expired challenges via `PruneExpiredChallenges(DateTimeOffset nowUtc)` + a periodic timer.
7. **[P17 & P18] bUnit & Coverage Ratchet Closeout**:
   - Use `bunit` `1.38.5`, un-exclude `MainLayout`, `Login`, and `SetupWizard` in `coverlet.runsettings` & `coverage-full.runsettings` while keeping the remaining 14 non-security `.razor` files excluded.
   - In `P18`, test real reachable branches using SQLite/in-memory services (never mocking `DbConnection`), ratify the reduced `coverage-baseline.json`, and document any remaining compiler-generated state-machine lines honestly.
8. **[P19 Decisions] Release Verification, Directive 5 & In-Browser Key Generation**:
   - **Directive 5 Ratification**: Updated `AGENTS.md` Directive 5 to name the exact measured scope: 100.00% line and branch coverage across developer-authored code and security Razor components (Step 4), and a downward-only ratchet baseline for compiler-generated code (Step 7).
   - **In-Browser Key Generation (Phase P20)**: Scheduled follow-up work package [`p20-in-browser-key-generation.md`](p20-in-browser-key-generation.md) for pure client-side key generation in the browser (WebCrypto for Ed25519 and Noble for ML-DSA-65) so private keys never touch the server even during `/setup` or onboarding.
   - **Forgejo CI Runner Flakes**: Recorded user disposition treating intermittent simulation-image build timeouts on `heimdall` as known host infrastructure latency to be re-run on demand.
9. **[P21–P25 Decisions] Subject Access Gating, Tiered Access Levels & Unified Cohorts**:
   - **Tiered Researcher Access Levels**: Supported three configurable access tiers:
     - `ReadAccess` (`1`): Read-only access to clinical/demographic profile (`/api/users/{username}/profile`, pronouns, regimen).
     - `GenomicAccess` (`2`): Level 1 + full read/write access to DNA origami nanocarrier blueprints (`/api/patients/{username}/dna`), sequence imports (`/api/patients/{username}/imports`), and analytical/simulation results (`/api/patients/{username}/results`).
     - `FullReadAccess` (`3`): Level 2 + read-only access to delivery addresses and outreach emergency contacts (`/api/users/{username}/addresses`, `/api/users/{username}/contacts`).
   - **Cohort Group Creation Boundary**: Both Administrators and Researchers can create and manage subject groups (cohorts). Administrators can add any subject; Researchers can **only** add subjects they already have access to.
   - **Unified Cohort Web UI**: Dedicated cohort and group management interface accessible from both `DnaManagementPage.razor` (with cohort-based patient filtering) and `UserManagement.razor` (with grant management modal).
   - **Cleanup of Completed Work Items**: Removed completed instruction files (`docs/work-packages/non-root-containers.md` and `work-packages/c8-challenge-response-proposal.md`), while retaining `PHASES-v2.md` as the permanent completed historical reference.
10. **[P26–P35 Decisions] Standalone Cross-Platform Desktop & Mobile GUI Applications (`feature/gui-application`)**:
   - **Cross-Platform GUI Architecture**: Selected Avalonia UI 11 + shared .NET 10 Client SDK (`NanoWeaverHrt.Client` + `NanoWeaverHrt.Gui.Core` + `NanoWeaverHrt.Desktop` + `NanoWeaverHrt.Mobile`) with headless xUnit testing (`Avalonia.Headless.XUnit`) so Linux (`amd64`/`arm64`), macOS, Windows, Android, iOS, and iPadOS share 100% of business logic, ViewModels, and PKI security invariants while maintaining 100.00% line and branch coverage on Linux CI.
   - **Sovereign Code-Signing PKI & 90-Day Automated Rotation**: Because Let's Encrypt ACME only issues TLS `serverAuth` (`1.3.6.1.5.5.7.3.1`) certificates and prohibits the `codeSigning` (`1.3.6.1.5.5.7.3.3`) EKU, ratified a Sovereign X.509 Code-Signing CA (`id-kp-codeSigning`) + OpenPGP/GPG Archive Signing Key with automated 90-day leaf certificate renewal and rotation via scheduled Forgejo and mirrored GitHub Actions workflows (`.forgejo/workflows/rotate-codesign-certs.yml` and `.github/workflows/rotate-codesign-certs.yml`).
   - **Unified Linux Package Naming**: Consolidated self-contained Debian and Ubuntu packages into `nanoweaver-hrt-desktop-amd64.deb` and `nanoweaver-hrt-desktop-arm64.deb`, and consolidated Fedora/RPM packages into `nanoweaver-hrt-desktop-amd64.rpm` and `nanoweaver-hrt-desktop-arm64.rpm`, alongside ArchLinux `nanoweaver-hrt-desktop-x86_64.pkg.tar.zst` and `nanoweaver-hrt-desktop-arm64.pkg.tar.zst` and AUR packages `nanoweaver-hrt-desktop-bin` and `nanoweaver-hrt-desktop-git`.
   - **Branch Isolation & Mirroring Strategy**: All GUI work packages and implementation are isolated on `feature/gui-application` pushed to `origin` (`git.lantea.one`, which automatically mirrors to GitHub on its backend), building all Linux, macOS, Windows (`nanoweaver-hrt-desktop-installer.exe` + `NanoWeaverHrt.msix`), and Mobile (`Android`, `iOS`, `iPadOS`) artifacts on both Forgejo Actions and GitHub Actions prior to merging into `main` and deleting `feature/gui-application`.

---

## 3. Playbook for Implementation & Verification

When executing work packages, ensure full compliance with the following quality invariants:

### Rule 1 — Protect the Step-7 Coverage Ratchet (`coverage-baseline.json`)
Gate step 7 (`scripts/check-coverage-ratchet.py`) keys compiler-generated state machines by their ordinal names (`<MethodName>d__N` and `<<Main>b__4_N>d`). To prevent ordinal renumbering or uncovered state-machine lines:
1. **Append new methods at the END of existing classes**, never in the middle.
2. **Append new minimal-API endpoints at the END of `Program.Main`** (immediately before `app.Run();`), and **never capture local variables from `Main`** inside endpoint lambdas (pass all dependencies as lambda parameters instead).
3. **Use type-based DI registration** (`builder.Services.AddSingleton<IService, ServiceImpl>()`) rather than `sp => ...` factory lambdas in `Program.Main`.
4. **Wrap `await using` inside a block** in async repository methods:
   ```csharp
   int affected;
   {
       await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
       await using DbCommand cmd = session.CreateCommand("...");
       affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
   }
   return affected == 1;
   ```
   This prevents the C# compiler from emitting an unreachable exception-path disposal branch at the end of the async state machine.
5. **Avoid unreachable defensive branches**.
6. If Step 7 reports `IMPROVED ... Baseline is stale`, update the baseline with:
   ```bash
   python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
   ```

### Rule 2 — Maintain 100.00% Step-4 Line & Branch Coverage
- Gate step 4 requires `100.00%` line and `100.00%` branch coverage across all non-compiler-generated code.
- Every `if`, `switch`, ternary `? :`, `??`, `?.`, and `catch` in a synchronous method must have a unit test exercising both branches.

### Rule 3 — Pass Step-6 XML Docblock Check (`scripts/check-docblocks.py`)
- Every type, method, property, field, and constant — **including `private` and `internal` ones** — must have an XML doc comment (`/// <summary>`).
- Every method or constructor with parameters must have one `/// <param name="...">` per parameter, and every non-`void` method must have `/// <returns>` (or `/// <inheritdoc/>` when implementing an interface/base member).
- Every justification for the null-forgiving operator `!` must have an inline `// WHY '!': ...` comment, and every algorithm/query/endpoint must have `// WHAT:`, `// WHY:`, `// HOW:` comments.

### Rule 4 — SQLite Connection Pool Isolation in Tests
- **Never call `SqliteConnection.ClearAllPools()`** in tests! Clear only the specific test's SQLite file pool:
   ```csharp
   using SqliteConnection connection = new($"Data Source={dbPath}");
   SqliteConnection.ClearPool(connection);
   ```

### Rule 5 — Single-Command Format & Verification
Always format before running `./scripts/verify-all.sh`:
```bash
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
```

---

## 4. Work Package Authoring & LLM Task Phasing Guide

This guide establishes the standard operating procedure for human architects and AI assistants when specifying, decomposing, and executing complex features. It cross-references and implements the requirements of [`AGENTS.md`](../AGENTS.md) (Section 1.15 and Section 5).

### 4.1 When and How to Create Work Packages
Whenever a new feature or remediation spans multiple layers (e.g., schema migration, service logic, HTTP API, or UI rendering), create a dedicated work package file under `work-packages/` following the naming convention:
```
work-packages/p<Number>-<descriptive-slug>.md
```
*(Example: `work-packages/p26-biometric-audit-stream.md`)*

### 4.2 Standard Work Package Template
Every authored work package MUST follow this markdown structure:

```markdown
# Work Package PXX: <Feature Title>

## 1. Context & Objective
- **Problem Statement**: Plain-English description of the gap or capability needed.
- **Goal & Scope**: High-level boundaries of what is included and explicitly excluded.
- **Target Role / Boundary**: Target actors (`Administrator`, `Researcher`, `Patient`) and transport boundary (`Api`, `Web`, `ApiWeb`).

## 2. Technical Contracts & Invariants
- **Interfaces & Types**: Concrete method signatures, models, and return types before writing implementation.
- **Database Schema**: Explicit SQL DDL (SQLite & PostgreSQL) and migration version identifier.
- **REST Endpoints**: HTTP method, URI, request DTO, response DTO, and RFC 7807 problem details mappings.
- **Coverage & Ratchet Invariants**: Applicable `ExcludeByFile` rules or required test fixture locations.

## 3. Step-by-Step Implementation Tasks
1. Task 1: Schema migration & connection factory queries.
2. Task 2: Service layer implementation & unit tests.
3. Task 3: API route mapping & integration tests.
4. Task 4: Blazor UI component & bUnit tests.
5. Task 5: Pre-flight gate verification & documentation.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
```

### 4.3 Phasing Rules for Smaller LLMs
Smaller context-window models or autonomous subagents succeed when tasks have tightly constrained scopes and deterministic boundaries. When orchestrating work for smaller LLMs:
1. **One Architectural Layer Per Turn**: Decompose the master plan into 1-layer units (Migration/Repo → Service → Minimal API → Blazor UI → Verification). Never combine database migration writing with Blazor UI component authoring in the same prompt.
2. **Pre-Specify Method Signatures**: Never allow the LLM to invent its own interface method signatures during service or UI implementation. Always provide the exact `Task<T>` signature, parameter names, and order in the work package prompt.
3. **Moq Argument Order Invariance**: In tests, ensure mock setups (`Setup(x => x.Method(...))`) match the exact parameter order of the pre-specified interface to avoid Moq loose-mode silent `null` returns.
4. **Interactive Form Pattern**: When a requirement fork is encountered, prompt the user with a structured form containing a clearly reasoned `(Recommended)` default option before proceeding.

### 4.4 Lifecycle & Clean-Up Protocol
To prevent workspace drift, token bloat, and stale branch clutter:
1. **Active State**: The active work package instruction file (`pXX-*.md`) resides in `work-packages/` while in progress.
2. **Review & Gate**: The implementation is verified via `./scripts/verify-all.sh`.
3. **Ratification & Pruning**:
   - Record user decisions and key milestones in `work-packages/README.md` (Section 1.3 and Section 2).
   - Update `work-packages/PHASES-v2.md` to reflect completion.
   - Delete the temporary `pXX-*.md` instruction file from `work-packages/`.
   - Prune all ephemeral git worktrees (`git worktree prune`) and delete stale local branches (`git branch -d ...`).
   - Commit and push clean state to sovereign Forgejo remote `origin/main`.
