# NanoWeaverHrt Release Notes & Architecture Evolution

> **Changelog and Security Hardening History Across Platform Phases**

---

## v2026.11.03.2 — Full-Repository Hardening, Multi-Engine Parity & Scientific Fidelity Release (2026-11-03)

Major milestone release completing Work Packages `P61–P90` (Forgejo Issues `#64–#93`, incorporating `P61–P85` / `#64–#88` and `P86–P90` / `#89–#93`), delivering rigorous multi-engine database persistence, FIPS-compliant post-quantum cryptography, advanced closed-loop endocrine feedback modeling, zero-allocation gel densitometry, Opentrons automation, and hardware-backed mobile enclaves.

### Highlights
* **Multi-Engine Persistence & Dialect Parity (Forgejo #64 / P61)**:
  - `Migration012_GuiPreferences` introducing `weaver_gui_preferences` across SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB connection factories.
  - DuckDB sandboxing enforcing `enable_external_access = false`.
  - Mandatory AES-256-GCM PHI encryption at rest for sensitive column payloads.
* **Cryptographic & Access Hardening (Forgejo #70 / P67)**:
  - BouncyCastle FIPS 203 `ML-KEM-768` and FIPS 204 `ML-DSA-65` dataset manifest signing.
  - AES-256-GCM salt AAD interop and `GenomicAccess` cohort role gating across all 14 scientific API endpoints.
  - Server-side logout token revocation and abandoned mutex exception recovery.
* **Scientific & Wet-Lab Engines (Forgejo #77 / P74)**:
  - Closed-loop HPG axis simulation with `K_p = 14.0` proportional gain and steady-state clearance feedforward calibration.
  - `isMtF` gonadal suppression kinetics modeling.
  - 4-wide `Vector128<float>` SIMD coordinate parser and Fortran DCD binary block marker validation.
  - Zero-allocation gel densitometry image processing and Stage 4 SVG lane plot rendering.
  - Opentrons multi-step pipette volume splitting and thermal ramp rate controllers.
* **Desktop, Mobile & Packaging (Forgejo #85 / P81)**:
  - Android Keystore (`IAndroidKeyStoreBridge`) and Apple Secure Enclave (`IAppleSecureEnclaveBridge`) Ed25519 identity bridges.
  - Lifecycle vault auto-lock and single-handshake `ZeroizableSecretBuffer` session caching.
  - Hardened Docker Compose production overlays and canonical `one.lantea.nanoweaver.hrt` desktop/mobile packaging.
* **Full-Repository Audit Remediation & Verification Hardening (Forgejo #89–#93 / P86–P90)**:
  - Removed `**/Views/**,**/Shell/**` exclusions, achieving 100.00% line and 100.00% branch coverage across all desktop shell components (#89 / P86).
  - TrajectoryBrowser swarm federation build and verify UI with cryptographic verification badges and resolved duplicate gel analyzer DI injections (#90 / P87).
  - Desktop CLI `--version` flag handling and asynchronous `DefaultDesktopUiHost` view-model initialization (#91 / P88).
  - Synchronized version `2026.11.03.2` across Arch PKGBUILD, FreeBSD, Gentoo, Homebrew, AppStream, macOS Info.plist, Windows Appx/NSIS/WinGet, Nix, and mobile manifests (#92 / P89).
  - Aligned API Reference and Trajectory Analytics documentation with `Program.cs` (#93 / P90).

---

## Release v2026.11.03.1 (P2P Swarm Federation, Closed-Loop Endocrine Modeling, Gel QC & Mobile Enclave Biometrics — 2026-11-03)

Major milestone release delivering decentralized P2P trajectory swarm federation with cryptographic Ed25519 manifests, dynamic closed-loop endocrine simulation solving coupled HPG axis non-linear ODEs via Runge-Kutta 4th-order (RK4) numerical integration, automated agarose gel electrophoresis image densitometry, and native mobile hardware-backed biometric authentication with Secure Enclave vault zeroization.

### Highlights
* **P2P Trajectory Swarm Federation & Sovereign Peer Synchronization (Work Package P56 / Forgejo #59)**:
  - Slices multi-gigabyte molecular dynamics trajectories into 1 MB content-addressed chunk streams (`TrajectoryChunkDescriptor`).
  - Generates cryptographic dataset federation manifests (`TrajectoryFederationManifest`) signed via local node asymmetric identity (`INodeSecretStore`).
  - Verifies foreign manifest signatures against trusted peer laboratories in `IPeerKeyRegistry`.
  - Zero-allocation chunk reading via `ArrayPool<byte>.Shared` memory buffers and constant-time SHA-256 validation (`CryptographicOperations.FixedTimeEquals`).
* **Dynamic Closed-Loop Endocrine Simulation & LH/FSH Feedback Engine (Work Package P57 / Forgejo #60)**:
  - Coupled 4th-order Runge-Kutta (RK4) numerical solver (`IEndocrineClosedLoopEngine`) integrating circulating 17β-estradiol, testosterone, Luteinizing Hormone (LH), and Follicle-Stimulating Hormone (FSH).
  - Dynamic allosteric aptamer feedback loop recalculating nanocarrier micro-release rates in response to gonadotropin rebound and suppression.
  - Minimal API endpoint `POST /api/endocrine/closed-loop/simulate` returning discrete time-series points, time-to-suppression, and steady-state indicators.
  - Interactive HPG axis simulation panel integrated into `EndocrineDashboard.razor`.
* **Agarose Gel Electrophoresis Image Processing & Nanocarrier Band Analysis (Work Package P58 / Forgejo #61)**:
  - Automated image densitometry service (`IGelAnalysisService`) processing raw pixel arrays with zero heap allocation (`System.Buffers.ArrayPool`).
  - Automated lane segmentation via vertical projection profile valley detection.
  - Peak-detection band densitometry computing relative mobility (Rf = d_band / d_front).
  - Log-linear DNA ladder calibration against reference standards (e.g., 1 kb DNA ladder) to estimate molecular weights in base pairs.
  - Densitometric quantification of monomer folding yield %, un-annealed staple smear %, and multimer aggregates with acceptance threshold gating (monomer yield >= 65%, staple smear <= 25%).
  - Minimal API endpoint `POST /api/fabrication/gel/analyze` and interactive Stage 6 Gel Quality Inspection panel in `WorkflowWizard.razor`.
* **Native Mobile Biometric Authentication & Secure Enclave Vault Decryption (Work Package P59 / Forgejo #62)**:
  - Hardware-isolated key generation and vault decryption for sovereign mobile heads (`src/NanoWeaverHrt.Mobile`).
  - Android `BiometricPrompt` and Android Keystore StrongBox integration with Class 3 biometric verification (`AndroidBiometricSecurityProvider`).
  - iOS `LocalAuthentication` and Apple Secure Enclave keychain integration (`AppleSecureEnclaveSecurityProvider`).
  - Unified mobile biometric vault (`UnifiedMobileBiometricVault`) monitoring mobile application lifecycle events (`Paused`, `Backgrounded`, `Terminated`), executing immediate memory zeroization (`CryptographicOperations.ZeroMemory`) on decrypted patient DNA payloads.
* **Sovereign Forgejo Issue Workflow Transition**:
  - Successfully migrated and purged all 55 historical work packages from git tracking into permanently archived, closed Forgejo Issues (#2 through #58).
  - Milestone `v2026.11.03.1` tracked directly via Forgejo Issues (#59 through #63).
  - 22 curated repository topics and refined project description applied to Forgejo repository.
* **Universal Quality Gate Ratification**:
  - 1,441 unit and integration tests passing cleanly (100% pass rate).
  - Gate 4 enforced 100.00% Line and 100.00% Branch code coverage across all developer-authored C# code.
  - Gate 7 un-excluded coverage ratchet verified against committed baseline with zero regressions.
  - Gate 8 vendored JavaScript integrity and known-answer tests (RFC 8032 and NIST FIPS 204 ML-DSA-65) verified cleanly.

---

## Release v2026.10.29.4 (Multi-Engine Persistence, Columnar Analytics, 18 Languages & Accelerator Benchmarking — 2026-11-03)

Major milestone release delivering full multi-engine database persistence (SQLite, PostgreSQL, DuckDB, MySQL / MariaDB), an in-process vectorized DuckDB columnar SQL analytics engine for molecular trajectories, an automated hardware accelerator FP32/FP64 benchmark and simulation qualification suite, 18 community languages with native RTL layout, and specialized repository-embedded AI agent personas.

### Highlights
* **Expanded 18 Community Languages & Native RTL Layout**:
  - Expanded `WeaverLanguage` enum from 10 to 18 languages: Arabic (`ar-SA`), Korean (`ko-KR`), Turkish (`tr-TR`), Hindi (`hi-IN`), Ukrainian (`uk-UA`), Vietnamese (`vi-VN`), Indonesian (`id-ID`), Swedish (`sv-SE`) alongside English, Spanish, French, German, Portuguese, Italian, Japanese, Simplified Chinese, Dutch, and Polish.
  - Full Right-to-Left (RTL) directional layout support (`dir="rtl"`) dynamically bound in `MainLayout.razor` and Avalonia desktop controls when Arabic is selected.
  - Desktop `MainShellViewModel` and `DesktopTrayIconController` localization synchronized across all 18 languages.
* **Multi-Engine RDBMS Persistence Seam**:
  - Added native dialect adapters `DuckDbDialect.cs` and `MySqlDialect.cs` to `IWeaverDbConnectionFactory`.
  - Upgraded `DatabaseMigrationRunner.cs` with transactional schema migrations and idempotent upserts across SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB.
  - Upgraded `LanguagePreferenceService.cs` to persist user preferences uniformly across all 4 database backends.
* **In-Process Columnar Trajectory Analytics Engine**:
  - Implemented `ITrajectoryAnalyticsService` and `DuckDbTrajectoryAnalyticsService` executing vectorized columnar SQL queries directly over Apache Parquet coordinate archives.
  - Added `POST /api/trajectories/analytics/query` endpoint with parameter validation, AST mutation blocking, and RFC 7807 problem details error mapping.
  - Embedded interactive analytical query drawer into `TrajectoryBrowser.razor` with real-time aggregate statistics and point telemetry cards.
* **Hardware Accelerator Benchmark & Simulation Qualification Suite**:
  - Implemented `IAcceleratorBenchmarkService` and `AcceleratorBenchmarkService` evaluating FP32/FP64 GFLOPS compute throughput and memory bus bandwidth (GB/s).
  - Deterministic readiness scoring: `Go` (production-ready), `Marginal` (small-scale runs), and `Insufficient` (CPU fallback recommendation).
  - Exposed authenticated REST API endpoints: `POST /api/system/benchmark/run` and `GET /api/system/benchmark/latest`.
  - Added diagnostic Accelerator Qualification card in `StackServices.razor` with high-contrast badges and progress spinner.
* **Tailored Repository AI Agents (`.agents/`)**:
  - Embedded project-specific AI agent definitions into `.agents/agents/` registered via `.agents/agents.json`: `nanoweaver-hrt-architect`, `nanoweaver-hrt-coder`, `nanoweaver-hrt-coder-web-service`, `nanoweaver-hrt-coder-desktop`, `nanoweaver-hrt-coder-android`, `nanoweaver-hrt-coder-ios`, `nanoweaver-hrt-mechanic`.
  - Codified into `AGENTS.md` and `.agents/README.md` to guide autonomous agents and contributors.
* **Zero Quality Regressions & 100% Verification**:
  - All 1,377 unit and integration tests passed (100% pass rate).
  - Gate 4 enforced 100.00% Line and 100.00% Branch code coverage.
  - Gate 7 un-excluded coverage ratchet strictly verified against baseline with zero regressions.
  - Zero XML docblock errors across all source files.
  - Zero unescaped single-dollar LaTeX formatting tokens across all documentation files.

---

## Release v2026.10.29.3 (Opentrons Wet-Lab Automation & Community Localization — 2026-10-29)

Major milestone release bridging in-silico computational nanocarrier design with physical laboratory execution via Opentrons robotic liquid handling automation, alongside comprehensive 10-language community localization, dual-engine PostgreSQL preference storage, and downward ratchet coverage improvements.

### Highlights
* **Opentrons Python Protocol API v2 Generator**:
  - Implemented `IOpentronsProtocolGenerator` and `OpentronsProtocolGenerator` supporting both **Opentrons OT-2** and **Opentrons Flex** liquid handling robots.
  - Automated ANSI/SLAS labware mapping (96-well and 384-well plates, 12-channel reservoirs, aluminum temperature blocks, and on-deck thermocyclers).
  - Stoichiometric mastermix dispensing with 10× staple:scaffold volumetric calculations, reduced aspiration speeds to protect delicate single-stranded viral scaffolds, and tip conservation toggles.
  - On-deck thermal annealing ramps (`90°C → 20°C`) with automated 105°C heated lid control and post-synthesis 4°C holds.
* **REST API Endpoints for Wet-Lab Automation**:
  - `POST /api/fabrication/opentrons/generate`: Validates hardware parameters, computes runtime estimates, and emits fully executable Python Protocol API v2 scripts.
  - `POST /api/fabrication/opentrons/download`: Direct binary file attachment download (`text/x-python`) for immediate loading into the Opentrons App or USB drives.
  - Strict role gating (`Researcher` or `Administrator`) and RFC 7807 problem details error mapping.
* **Guided DNA Origami Workflow Wizard UI Integration**:
  - Embedded Opentrons hardware configuration cards in Stage 5 and Stage 6 of `WorkflowWizard.razor`.
  - Visual deck layout summary cards displaying active slot allocations, source tubes, destination wells, and thermocycler blocks.
  - Interactive protocol preview modal with one-click clipboard copy and direct browser blob download interop.
* **10-Language Community Localization**:
  - Delivered `IWeaverLocalizer` domain architecture with 10 native embedded JSON resource catalogs: English (`en-US`), Spanish (`es-ES`), French (`fr-FR`), German (`de-DE`), Brazilian Portuguese (`pt-BR`), Italian (`it-IT`), Japanese (`ja-JP`), Simplified Chinese (`zh-CN`), Dutch (`nl-NL`), and Polish (`pl-PL`).
  - Thread-safe zero-allocation dictionary lookups with neutral-culture and primary English fallback hierarchies.
  - Interactive language switcher (`LanguageSelector.razor`) embedded in `MainLayout.razor` with reactive navigation label updates.
  - Desktop ViewModel and tray icon localization (`MainShellViewModel`, `DesktopTrayIconController`).
* **Dual-Engine PostgreSQL & SQLite Preference Storage**:
  - `LanguagePreferenceService` provides unified locale persistence supporting both local client SQLite (`weaver_preferences.db`) and enterprise PostgreSQL (`weaver_gui_preferences` with `TIMESTAMPTZ` and `ON CONFLICT DO UPDATE`).
  - Client-side browser persistence via `weaver-session.js` using `sessionStorage` and `SameSite=Strict` cookies (`weaver_lang`) with conditional `Secure` flags.
* **Zero Quality Regressions & Ratchet Improvement**:
  - All 1,162 unit and integration tests passed (100% pass rate).
  - Gate 4 enforced 100.00% Line and 100.00% Branch code coverage.
  - Gate 7 un-excluded coverage ratchet strictly improved: branch rate increased from 97.43% to 97.56%, line rate increased from 99.63% to 99.65%, and uncovered line total held rock-solid at 54.
  - Zero XML docblock errors and zero unescaped single-dollar LaTeX formatting tokens.

---

## Release v2026.10.29.2 (Desktop & Packaging Hardening — 2026-10-29)

Maintenance and feature release delivering standalone windowed desktop user interfaces, direct-download portable executables, universal macOS Fat Mach-O dynamic slice alignment, and cross-platform manifest version synchronization across all packaging formats.

### Highlights
* **Default Interactive Windowed Desktop UI**: Launching `NanoWeaverHrt.Desktop` (or running with `--gui`) now activates the interactive desktop shell via `IDesktopUiHost` and `DefaultDesktopUiHost`, orchestrating the `MainShellViewModel`, reactive system tray icon controller, and single-instance mutex guards across Windows, macOS, and Linux.
* **Direct Portable Windows Executables**: Packaging automation now captures genuine 40 MB single-file PE32+ executables (`NanoWeaverHrt.Desktop.exe`) and publishes them directly as `nanoweaver-hrt-desktop-portable-x64.exe` alongside dual-scope NSIS installers and MSIX app packages.
* **Dynamic Universal Mach-O Fat Slice Alignment**: Resolved Mach-O fat binary slice overlap by computing exact 4096-byte page alignment (2¹² = 4096 bytes) dynamically based on `x86_64` payload lengths (`((x64_offset + x64_len + 4095) // 4096) * 4096`).
* **Universal Package Version Parameterization**: Synchronized version strings across ArchLinux (`.PKGINFO`, `.BUILDINFO`, AUR `PKGBUILD`, `.SRCINFO`), Debian (`control`), RedHat/Fedora RPM specs, Windows AppxManifest / NSIS installer, macOS `Info.plist`, AppStream AppData XML, Android `AndroidManifest.xml`, and iOS `Info.plist`.
* **Zero Quality Regressions**: Verified 100.00% line and branch test coverage (1,005 unit tests), 100% downward ratchet baseline compliance, zero compiler warnings, and zero unescaped single-dollar formatting tokens.

---

## Release v2026.10.29.1 (Initial Production Release — 2026-10-29)

The inaugural production release of **NanoWeaver HRT**, delivering decentralized synthetic biology, DNA origami nanocarrier modeling, and patient-targeted endocrine gene circuit design to researchers, clinicians, and patients.

### Highlights
* **High-Throughput SIMD Trajectory Parser**: Zero-allocation coordinate parsing for LAMMPS (`.xyz`) and oxDNA (`.dcd`) trajectories via `System.IO.Pipelines` and `ArrayPool<T>`.
* **Zero-Private-Key-Transmission Identity**: Post-quantum PKI authentication (Ed25519 & ML-DSA-65) with client-side browser signing, role gating, and automated 15-minute idle session termination.
* **Patient Privacy & PHI Cryptographic Sealing**: AES-256-GCM authenticated encryption at rest for patient demographics, gender identity, and DNA regimens with GDPR Article 17 crypto-shredding journal.
* **Audit Ledger**: FDA 21 CFR Part 11 compliant audit logging to SQLite/PostgreSQL with ML-DSA-65 tamper-evident signatures across restarts.
* **Tiered Cohort Access Control**: Granular tiered researcher permissions (`ReadAccess`, `GenomicAccess`, `FullReadAccess`) with unified cohort management.
* **Standalone Cross-Platform Applications**: Native desktop and mobile GUI applications (Avalonia UI 11 / .NET 10) for Linux, macOS, Windows, Android, iOS, and iPadOS.
* **Direct Binary Distribution**: Direct single-click release downloads for Windows (`.exe` standalone portable and installer), Linux (`.deb`, `.rpm`, `.pkg.tar.zst`, AUR), macOS (`.dmg`), and Mobile (`.apk`, `.ipa`), with detached X.509 CMS and OpenPGP signatures.
* **100% Quality Verification**: 100.00% line & branch test coverage across developer code, zero build warnings, and 100% downward ratchet compliance.

---

## Phase 2 Architecture & Security Hardening (Current Release)

Phase 2 introduces critical post-quantum cryptographic upgrades, transport boundary enforcement, zero-information-disclosure error models, and hardened identity gating.

### 1. Zero-Private-Key-Transmission Authentication (Audit C8 & `POST /api/auth/web-token`)
* **Elimination of Server-Side Signing Oracle**: Completely removed the legacy `POST /api/auth/login-key` endpoint, `AuthenticateWithPrivateKeyAsync`, and associated server-side private key decryption/signing routines. The server never receives, parses, or signs with user private keys during authentication.
* **Client-Side Browser Signing**: Private key envelopes are parsed and decrypted in-memory inside the browser using vendored zero-dependency noble crypto modules (`weaver-crypto-vendor.js`). Challenges are requested from `POST /api/auth/challenge`, signed in browser memory, and exchanged at `POST /api/auth/web-token`.
* **Transport Gate Scoping**: Strictly enforces `Api`, `Web`, and `ApiWeb` transport boundaries across all challenge-response authentication paths.
* **Same-Origin & HttpOnly Cookies**: Interactive browser sessions establish authenticated cookies via `POST /api/auth/web-token` (`HttpOnly; SameSite=Strict; Secure`), with strict same-origin and configured public origin enforcement.
* **Self-or-Administrator Scoping**: Patient endpoints (`/api/patient/dna`, `/api/patient/results`, `/api/patient/imports`) now strictly enforce role gating, returning `404 SubjectRecordNotFoundDetail` to unauthorized callers to prevent user-enumeration attacks.

### 2. PHI Protection & Column-Level Encryption at Rest
* **Database Migrations 005, 006, 010**:
  * `Migration005_EncryptPatientPhi`: Encrypts patient profile demographics, address lines, and emergency contact details using AES-256-GCM authenticated envelopes (`data:application/encrypted;base64,...`).
  * `Migration006_EncryptGenderIdentity`: Widens PostgreSQL column types to `TEXT`, removes plaintext defaults, and idempotently seals `regimen`, `gender_assigned_at_birth`, and `pronouns_json`.
  * `Migration010_EncryptPatientDnaPhi`: Widens `weaver_patient_dna_records.regimen` to `TEXT` on PostgreSQL and encrypts `regimen` and `notes` at rest via `ProfileDataProtector`.
* **Zero Heap Leakage**: Coordinate and buffer rentals leverage `ArrayPool<T>.Shared` and cryptographic memory clearing (`CryptographicOperations.ZeroMemory`).

### 3. Subject Key Persistence & GDPR Article 17 Erasure Journal
* **Post-Quantum Subject Key Vault**: Individualized NIST FIPS 203 ML-KEM-768 keypairs (`sub_...`) are sealed at rest in `weaver_subject_keys` using AES-256-GCM with HKDF-SHA256 and subject-ID AAD binding.
* **Fsynced Erasure Journal**: Deletion requests record irreversible crypto-shredding operations in `.weaver-subject-erasure.jsonl`, providing a tamper-evident audit trail for GDPR Article 17 compliance.

### 4. Authoritative Node Key Persistence & Dedicated Mount
* **Persistent Node Secret Files**: Secrets generated on first launch are persisted to `WEAVER_NODE_KEYS_DIR` (`/workspace/node-keys`) with owner-only (`0600`) filesystem permissions:
  * `token_signing.key`: 256-bit HMAC-SHA256 bearer token signing key.
  * `phi_master.key`: 256-bit AES-256-GCM master key sealing patient PHI and subject keys.
  * `node_signing_mldsa65.key`: 32-byte FIPS 204 ML-DSA-65 seed for the FDA 21 CFR Part 11 audit ledger.
  * `dataset_signing_ed25519.key`: 32-byte Ed25519 seed for dataset export manifests.
* **Dedicated Volume Mount**: Standardized `./simulations/node-keys:/workspace/node-keys:rw` mount isolating secret material from simulation and analytics engines.

### 5. Audit Ledger Persistence Across Restarts
* **Database Migration 009**: Establishes `weaver_audit_ledger` backing the post-quantum FDA 21 CFR Part 11 compliance ledger across node restarts.
* **Cryptographic Hash Chain**: Genesis and action entries maintain an unbroken SHA-256 hash chain signed with the node ML-DSA-65 private key.

### 6. Dataset Provenance & Strict Verification Mode
* **Peer Key Registry (`weaver_peer_keys`)**: Database Migration 007 persists known peer public keys for dataset manifest verification.
* **Ed25519 Manifest Signing**: Exported `.tar.gz` dataset bundles carry an Ed25519 signature of the SHA-256 manifest.
* **Strict Verification Mode (`Peer:RequireSignedDatasets`)**: When enabled, dataset imports reject unsigned manifests or keys unrecognized in the peer registry with `400 Bad Request`.

### 7. Zero-Information-Disclosure RFC 7807 Error Model
* **`SecureExceptionHandlerMiddleware`**: Maps unhandled server exceptions to RFC 7807 `SecureProblemDetails` with opaque correlation IDs.
* **Sanitized Error Payloads**: Redacts database connection strings, credentials, private key PEM headers, and internal SQL fragments from client responses.
* **Corrupt Ciphertext Contract**: Tampered or un-decryptable PHI records return an opaque `500 InternalServerError` with an audited trace identifier, never leaking corrupt ciphertext fragments to callers.

### 8. Test Coverage Ratchet Closeout & Baseline Ratification (Phase P18)
* **Un-excluded Coverage Ratchet**: Tightened `coverage-baseline.json` from `146` uncovered lines down to `59` lines across `29` classes, elevating the line rate to `99.51%` and branch rate to `96.38%`.
* **Zero-Mock ADO.NET Invariant**: Preserved strict prohibition against mocking `DbConnection`/`DbCommand`/`DbDataReader` (Directive 1). All tests execute against real SQLite database instances with schema migrations applied.
* **Eliminated Target Service Uncovered Branches**: Achieved 100.00% coverage on:
  * `UserAuthenticationService`: All validation error branches (malformed/null claims, expired tokens, deleted/inactive users, setup lockout).
  * `UserProfileRepository`: Wrapped all `await using` sessions inside scoped blocks prior to `return`, eliminating compiler-generated state machine disposal branches.
  * `ChannelEventBus`: Addressed cancellation rethrow, full bounded queue handling, and closed stream unregistration.
  * `StackServiceManager`: Covered all `GetServiceByIdAsync` nodes and hardware accelerator fallback on empty attributes.
  * `ValkeyCacheService` & `ValkeyLockService`: Full coverage of in-memory fallbacks, expired key cleanup, retry delays, and double-disposal/fault resilience.
  * `ValkeyEventBridge`: Covered all inbound listener lambdas, echo suppression, outbound failure logging, and corrupted JSON handling.

### 9. Multi-Platform Desktop & Mobile Applications (Phases P26–P35)
* **Standalone Cross-Platform Architecture**: Introduced native client platform heads for Linux (`x86_64` / `arm64`), macOS (Universal Apple Silicon & Intel), Windows (`win-x64`), Android, iOS, and iPadOS sharing 100% of ViewModels and client SDK logic.
* **Local Instance Registry & Encrypted Key Vault**: Local encrypted SQLite store (`IInstanceProfileRepository`) for multi-instance cluster connections and AES-256-GCM client-side key vault (`IClientKeyRingVault`) for Ed25519 and ML-DSA-65 keys, keeping private keys strictly on the user device.
* **Background Monitoring Daemon & Tray Controller**: 15-minute idle lock watchdog, native OS notification dispatching (`INativeNotificationSink`), tray health badging, and alert deduplication cooldowns.
* **Sovereign Code-Signing PKI & 90-Day Automated Rotation**: Dedicated sovereign X.509 `id-kp-codeSigning` Root CA, short-lived 90-day leaf certificate rotation via scheduled monthly CI workflows, and OpenPGP/GPG archive key signing for packages and `SHA256SUMS` manifests.
* **Unified Binary Packaging Matrix**: Multi-distribution packaging pipeline providing ArchLinux `.pkg.tar.zst`, AUR (`nanoweaver-hrt-desktop-bin` and `-git`), Debian/Ubuntu `.deb`, Fedora `.rpm`, macOS `NanoWeaverHrt.dmg`, Windows dual-scope installer (`nanoweaver-hrt-desktop-installer.exe` supporting Current User or System Administrator) + `NanoWeaverHrt.msix`, and sideloadable mobile packages (`.apk`, `.ipa`).
* **Remaining Uncovered Lines Accounting**:
  * `Program.cs` minimal-API lambdas (lines 1830, 1691-1700, 1973-1980, 2050, 2090, 2134, etc.): Defense-in-depth authorization and conflict return paths that are intentionally unreachable over HTTP because the upstream `ResolveSubjectRecordAccess` middleware intercepts cross-subject requests.
  * Blazor components (`MainLayout.razor`, `Login.razor`, `SetupWizard.razor`): Compiler-generated Razor component state-machine transitions and JS interop disconnected fallbacks.

