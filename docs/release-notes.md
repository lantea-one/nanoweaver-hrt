# NanoWeaverHrt Release Notes & Architecture Evolution

> **Changelog and Security Hardening History Across Platform Phases**

---

## v2026.11.10.2 — Sovereign Clinical Audit & Wet-Lab Protocol Automation Release: Enum Deserialization Parity, In-Vivo Nuclease Degradation Kinetics, and Enzymatic CpG Methylation Protocols (2026-11-10)

Resolves clinical audit findings identified during wet-lab automation and systems biology integration (Forgejo Issues #174, #175, #176):
* **Minimal API DTO Enum Deserialization Parity (Forgejo #174)**:
  - Added `[JsonConverter(typeof(JsonStringEnumConverter))]` directly to enum definitions (`TherapeuticRegimen`, `ToggleState`, `HormoneType`, `TargetTissueType`, `OpentronsRobotModel`, `OpentronsPipetteModel`, `OpentronsPlateType`, `GelLadderType`) and clinical request DTO properties (`GenomicAnalysisRequest.Regimen`, `ClosedLoopSimulationRequest.TargetState`, `GenerateOpentronsScriptRequest.Regimen`, `OpentronsProtocolOptions`, `GelAnalysisRequest.LadderType`, `PbpkSimulationRequest.Regimen`).
  - Supports both string literals (e.g. `"FemaleToMale"`, `"AndrogenicDominant"`, `"Flex"`, `"M13mp18Scaffold"`) and backward-compatible integer literals with HTTP 200 OK across all endpoints.
* **PBPK In-Vivo Nuclease Biodegradation & Stoichiometric Mass Bounds (Forgejo #175)**:
  - Enhanced Campaign 4 PBPK multi-compartment engine (`PbpkEndocrineSimulationResult`) with explicit in-vivo serum nuclease cleavage kinetics (`SerumNucleaseHalfLifeHours` parameterized between 72.0 and 168.0 hours based on Dietz/Shih literature benchmarks for passivated DNA origami).
  - Calculated total dry nanocarrier formulation mass in milligrams (`TotalNanocarrierMassMg`) required for sustained steady-state turnover (50–100 µg/day E2, 5–7 mg/day T).
  - Modeled subcutaneous depot exhaustion day (`DepotExhaustionDay`) based on first-order reservoir depletion.
  - Clarified lifelong translation feasibility advisory in `ClinicalFeasibilityVerdict`, explicitly noting that exogenously loaded nanocarrier depots operate as sustained semi-annual depots, whereas singular permanent efficacy requires coupling with endogenous genetic toggle switch reprogramming.
* **Automated Wet-Lab Enzymatic CpG Methylation (M.SssI) in Opentrons Protocols (Forgejo #176)**:
  - Added `IncludeCpGMethylationStage` to `OpentronsProtocolOptions`.
  - Updated `OpentronsProtocolGenerator` to automatically detect high unmethylated CpG dinucleotide density (&gt;= 3.0%) in scaffold sequences or explicit option requests.
  - Synthesizes automated Protocol API v2 liquid handling commands to dispense M.SssI methyltransferase and S-adenosylmethionine (SAM) cofactor from cold storage vessels to assembly wells, commands the on-deck Thermocycler or Temperature Module to hold 37°C for 120 minutes (enzymatic methylation) followed by 65°C for 20 minutes (heat inactivation) and 4°C hold before staple addition, and incorporates methylation hold duration into estimated run time.

---

## v2026.11.10.1 — Sovereign Production Release: Complete End-to-End Verification, CI/CD Pipeline Green, Cross-Platform Multi-Target Packaging & Trust Anchors (2026-11-10)

Official code-complete production release of NanoWeaver HRT. Unifies sovereign molecular dynamics simulation environments (oxDNA 2.4 and LAMMPS), zero-allocation SIMD trajectory streaming (.xyz, .dcd, .dump), multi-engine persistence (SQLite, PostgreSQL, DuckDB, MySQL) with versioned migrations 001–011, post-quantum ML-DSA-65 and classical Ed25519 PKI identity, 100% Client REST SDK parity (70/70 operations), sovereign dark-mode Blazor Interactive Server UI with 18-language community localization, Avalonia UI 11 / .NET 10 cross-platform desktop and mobile clients (Android and iOS/iPadOS lifecycle hosts with automated biometric vault zeroization), and 16 cryptographically signed standalone binary packages verified against the sovereign root CA.

### Highlights
* **CI/CD Pipeline Green Status & Host Telemetry Isolation**:
  - Isolated `/sys/class/drm` sysfs hardware device probing during default fallback tests, guaranteeing deterministic test execution across bare-metal and container CI runners.
  - Aligned local PKI staging keys with committed trust anchor certificates in `docs/assets/keys/`, eliminating signature mismatch errors during CMS signing.
  - Added `--fix-missing` to package installations in `docker/simulation/Dockerfile.simulation` to guard against transient upstream mirror synchronization drops.
  - Resolved `NETSDK1152` duplicate publish outputs by isolating executable project references.
* **Master Cross-Platform Packaging Pipeline**:
  - Automated continuous release publishing across 16 platform packages spanning Linux (.deb, .rpm, .pkg.tar.zst, Gentoo ebuild, AppImage), macOS (.dmg, .app.tar.gz), Windows (PE32+ installer .exe, .msix, portable binary), and Mobile (Android .apk, iOS/iPadOS .ipa).
  - Generated dual detached X.509 CMS (`.p7s`) and OpenPGP (`.asc`, `.sig`) signatures accompanied by SHA-256 manifests.
* **100.00% Line & Branch Code Coverage & Un-Excluded Ratchet**:
  - Enforced 100.00% line and branch coverage across all developer-authored C# and Razor assemblies.
  - Maintained 0 uncovered lines on the downward-only compiler-generated state machine ratchet baseline across 1,898 tests.

---

## v2026.11.09.6 — Code-Complete Milestone: GUI Core 70-SDK ViewModel Parity, 18-Language Desktop Localizer Integration, Mobile Android & iOS Platform Lifecycle Hosts, Full Test Suite & End-to-End Verification (2026-11-09)

Code-complete milestone release resolving Work Packages P166–P170 (Forgejo Issues #169–#173), completing 100% SDK method parity across `NanoWeaverHrt.Gui.Core` (`NodeAdministrationViewModel`, `PatientClinicalWorkspaceViewModel`, `FederationAndDatasetWorkspaceViewModel`, and `MainShellViewModel` child property exposure), integrating dynamic 18-language localization with `IWeaverLocalizer` across all community resource files (`strings.*.json`) and `DesktopTrayIconController.BuildMenuItems()`, supporting automated right-to-left layout directionality for Arabic (`WeaverLanguage.Arabic`), establishing deterministic Android `MainActivity` and iOS/iPadOS `AppDelegate` platform lifecycle hosts with automated background biometric vault locking, preserving structured error diagnostics across `BackgroundInstanceMonitor` and `InstanceOnboardingViewModel.TestAndSaveAsync`, implementing comprehensive unit test suites in `GuiPhase4WorkspaceViewModelsTests`, `MobilePlatformLifecycleTests`, and `DesktopLocalizationAndDiagnosticsTests`, and achieving clean 8-gate `./scripts/verify-all.sh` verification.

### Highlights
* **GUI Core ViewModel Parity for Remaining 70 INanoWeaverNodeClient SDK Operations (Forgejo #169 / P166)**:
  - Implemented `NodeAdministrationViewModel` wrapping first-launch setup bootstrapping, user account management, authoritative configuration overrides, container stack services, and hardware benchmarks.
  - Implemented `PatientClinicalWorkspaceViewModel` wrapping demographic profiles, addresses, contacts, digitized DNA regimens, clinical laboratory results, and cohort access groups.
  - Implemented `FederationAndDatasetWorkspaceViewModel` wrapping single-use peer share keys, trusted peer laboratory key registry, dataset export/import bundles, and trajectory federation swarm manifests.
  - Exposed strongly-typed child workspace viewmodel properties on `MainShellViewModel` with property change notifications.
* **DesktopTrayIconController & MainShellViewModel 18-Language IWeaverLocalizer Integration (Forgejo #170 / P167)**:
  - Integrated `IWeaverLocalizer` into `DesktopTrayIconController` and `MainShellViewModel`, dynamically translating context menu items, tooltip text, and RTL directionality across all 18 supported languages (`ar-SA`, `de-DE`, `en-US`, `es-ES`, `fr-FR`, `hi-IN`, `id-ID`, `it-IT`, `ja-JP`, `ko-KR`, `nl-NL`, `pl-PL`, `pt-BR`, `sv-SE`, `tr-TR`, `uk-UA`, `vi-VN`, `zh-CN`).
  - Added `Tray.Show`, `Tray.Exit`, `Tray.ActiveNodeFormat`, and `Tray.NoActiveNode` across all 18 community localization catalogs (`strings.*.json`).
  - Preserved structured diagnostic error strings in `BackgroundInstanceMonitor` instance summaries and distinguished remote node connection failures from local SQLite profile persistence errors in `InstanceOnboardingViewModel.TestAndSaveAsync`.
* **Mobile Android MainActivity & iOS AppDelegate Platform Lifecycle Hosts (Forgejo #171 / P168)**:
  - Implemented `MainActivity.cs` (Android lifecycle, runtime notification and biometric permissions, OS memory pressure trimming via `OnTrimMemory`, and automated vault zeroization on `OnPause`).
  - Implemented `AppDelegate.cs` (iOS/iPadOS lifecycle, scene backgrounding, memory warnings via `ReceiveMemoryWarning`, and automated session zeroization on `DidEnterBackground`).
  - Wired `MobileEntryPoint.RunAsync` to coordinate platform hosts and background alert delivery.
* **Comprehensive Unit Test Suite for Second-Pass Remediations (Forgejo #172 / P169)**:
  - Added `GuiPhase4WorkspaceViewModelsTests.cs`, `MobilePlatformLifecycleTests.cs`, and `DesktopLocalizationAndDiagnosticsTests.cs`.
  - Maintained 100.00% line & branch coverage threshold across all new and updated components.
* **Final End-to-End 8-Gate Verification & Release Notes Consolidation (Forgejo #173 / P170)**:
  - Brought all documentation files into 100% factual alignment with the codebase.
  - Successfully verified all 8 gates of `./scripts/verify-all.sh` with 0 warnings and 0 compiler-generated state machine regressions.

---

## v2026.11.09.5 — Code-Complete Milestone: Second-Pass Security Hardening, Zero-Allocation Catalog, Geometry-Specific Staples, Opentrons Multi-Channel Volume Balance & Local PQC Federation Verification (2026-11-09)

Code-complete milestone release resolving Work Packages P161–P165 (Forgejo Issues #164–#168), hardening `ContentFormatSerializer` against XML external entity (XXE) and billion laughs expansion (`DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument = 10MB`), bounding incoming format payloads to 10 MB in `FormatNegotiationMiddleware` (returning HTTP 413 `PayloadTooLarge`), capping `GelAnalysisService` image dimensions to [1, 8192] and stream reads to `width * height * 4` bytes, eliminating pharmacogenomics bypasses and pre-computing an immutable zero-allocation variant catalog in `PharmacogenomicsService`, differentiating DNA origami structural crossover staple ensembles across Nanotube (6HB), Cuboid (24-helix), and Icosahedral (5-way vertex) geometries with Kd-tuned aptamer stems and FASTA mid-line `>` handling in `FabricationService`, dynamically loading 8-channel `nest_12_reservoir_15ml` labware and enforcing stoichiometric volume conservation in `OpentronsProtocolGenerator`, and enabling local node ML-DSA-65 federation manifest verification with zeroized secret seeds, 512 MB bounded archive decompression, and 4-engine database health reporting in `TrajectoryFederationService`, `DatasetTransferService`, and `StackServiceManager`.

### Highlights
* **Security & Secret Zeroization (Forgejo #164 / P161)**:
  - Configured `XmlReaderSettings` in `ContentFormatSerializer` with `DtdProcessing.Prohibit` and `XmlResolver = null` to prohibit XXE and entity expansion attacks.
  - Bounded inbound XML and YAML request payloads to 10 MB in `FormatNegotiationMiddleware`, returning RFC 7807 HTTP 413 `PayloadTooLarge`.
  - Capped image dimensions in `GelAnalysisService` to [1, 8192] pixels and bounded stream reading to `width * height * 4` bytes max.
  - Hardened `.dockerignore` to exclude `**/node-keys/`, `**/*.key`, `**/*.db*`, and `simulations/`.
* **Pharmacogenomics Backdoor Removal & Zero-Allocation Catalog (Forgejo #165 / P162)**:
  - Pre-computed `SortedCatalog` using in-place `Array.Sort` by category ascending, then severity descending.
  - Returned `SortedCatalog` directly from `GetCatalog()` with zero runtime allocations and zero LINQ queries.
  - Removed unused `AddMarker` method and removed `-OK` suffix bypass in `ApplyGeneticAdjustments`, enforcing ceiling clamps across all patient identifiers.
* **Geometry-Specific Staples & Kd-Tuned Aptamers (Forgejo #166 / P163)**:
  - Differentiated structural core crossover staples across Nanotube (6HB), Cuboid (24-helix), and Icosahedral (5-way vertex) geometries.
  - Preserved full-length 52 nt MtF / 48 nt FtM aptamer core motifs with 4 locus scaffold anchor extensions and target Kd stem modulation.
  - Fixed FASTA mid-line `>` header bug by validating `atLineStart && rawCh == '>'`.
  - Validated `PackageId` against path traversal (`..`, `/`, `\`) in `ExportFabricationManifestJsonAsync`.
* **Opentrons Labware & Stoichiometric Volume Balance (Forgejo #167 / P164)**:
  - Loaded `nest_12_reservoir_15ml` on Slot 1 when multi-channel pipettes (`P20_Multi_Gen2`, `P300_Multi_Gen2`, `Flex_8Channel_50`) are selected, addressing A1 master mix, A2 buffer, and A3 payload to prevent tube rack collisions.
  - Enforced stoichiometric volume conservation based on `C1 * V1 = C2 * V2` against 10 µM working stock plate, ensuring `masterMixVolume + perWellStapleVolume == options.TotalReactionVolumeMicroliters`.
* **Federation Crypto, Dataset Streaming & Stack Health (Forgejo #168 / P165)**:
  - Added `GetNodeSigningMldsa65Key()` to `INodeSecretStore` and `NodeSecretStore`, loading 32-byte ML-DSA-65 seeds from `node_signing_mldsa65.key`.
  - Enabled local node ML-DSA-65 verification in `TrajectoryFederationService` without requiring local node self-registration in `weaver_peer_keys`.
  - Zeroized private seed arrays in `finally` blocks and sanitized local path disclosures in `FileNotFoundException` messages.
  - Rented 80 KB buffers from `ArrayPool<byte>.Shared` in `DatasetTransferService` and enforced a 512 MB expansion limit on decompressed archive streams.
  - Updated `StackServiceManager` to accurately report all 4 storage backends (`PostgreSQL`, `DuckDB`, `MySQL`, `SQLite`) with zero LINQ and zero-Regex password redaction.

---

## v2026.11.09.4 — Code-Complete Milestone: Desktop MVVM Workspaces, Headless Daemon Lifecycle, Mobile Biometric Key Separation, Container Hardening & Documentation Parity (2026-11-09)

Code-complete milestone release resolving Work Packages `P156–P160` (Forgejo Issues `#159–#163`), completing MVVM workspace viewmodels across `NanoWeaverHrt.Gui.Core` (`SimulationWorkspaceViewModel`, `ClinicalGenomicsWorkspaceViewModel`, `WetLabComplianceWorkspaceViewModel`, and `MainShellViewModel` child property exposure), integrating `AvaloniaShellBootstrapper.CreateDefault` and configurable headless daemon polling intervals (`DesktopDaemonConfig`) into `DesktopProgram.cs`, implementing HKDF (SHA-256) subkey separation (`nanoweaver-android-ed25519-sign-v1` vs `nanoweaver-android-aes256gcm-wrap-v1`, and Apple Secure Enclave equivalents) with zeroizable memory wiping and encrypted local file persistence in `NanoWeaverHrt.Mobile`, hardening Docker Compose runtime security (`127.0.0.1:5000:5000`, `cap_drop: [ALL]`, `no-new-privileges:true`), synchronizing CalVer release versioning `2026.11.09.4` across all 12 packaging manifests and scripts, migrating certificate rotation workflows to Forgejo Actions (`.forgejo/workflows/rotate-codesign-certs.yml`), and bringing all documentation files into 100% parity with the codebase.

### Highlights
* **Desktop MVVM Specialized Workspaces & Shell ViewModel Exposure (Forgejo #159 / P156)**:
  - Implemented `SimulationWorkspaceViewModel` providing simulation campaign lifecycle management, active trajectory streaming observation, and parameter configuration bindings.
  - Implemented `ClinicalGenomicsWorkspaceViewModel` managing patient digital DNA blue-printing, pharmacogenomic marker screening, and setpoint adjustment calculations.
  - Implemented `WetLabComplianceWorkspaceViewModel` coordinating Opentrons robot pipetting protocol generation, agarose gel electrophoretic image densitometry, and tamper-evident audit ledger verification.
  - Exposed strongly-typed child workspace viewmodel properties on `MainShellViewModel` with property change notifications, maintaining 100.00% test coverage in `GuiWorkspaceViewModelsTests.cs`.
* **Desktop Entry Point Lifecycle, Avalonia Bootstrapping & Headless Polling Cadence (Forgejo #160 / P157)**:
  - Initialized `DefaultDesktopUiHost` to wire `AvaloniaShellBootstrapper.CreateDefault` by default when no custom bootstrapper is supplied.
  - Added `Console.CancelKeyPress` SIGINT/SIGTERM cancellation token registration for graceful headless shutdown.
  - Wired `DesktopDaemonConfig` to derive headless polling iteration delays from `config.PollingIntervalSeconds` with full boundary checks, verified in `DesktopProgramTests.cs`.
* **Mobile Biometric Key Separation (HKDF Signing vs Encryption), Prompt Gating & Storage (Forgejo #161 / P158)**:
  - Implemented HKDF (SHA-256) subkey separation (`nanoweaver-android-ed25519-sign-v1` vs `nanoweaver-android-aes256gcm-wrap-v1`, and iOS equivalents) in `DefaultAndroidKeyStoreBridge` and `DefaultAppleSecureEnclaveBridge`.
  - Added zeroizable memory wiping of derived key material in `finally` blocks, preventing secret retention in process memory.
  - Added encrypted file persistence via `storageDirectoryPath` and enforced mandatory biometric challenge prompting in `AndroidBiometricSecurityProvider` and `AppleSecureEnclaveSecurityProvider`, verified in `MobileBiometricSecurityTests.cs`.
* **Docker Compose Least-Privilege Hardening, Packaging Script Verification & CalVer Manifest Sync (Forgejo #162 / P159)**:
  - Bound service port strictly to host loopback `127.0.0.1:5000:5000` and added `cap_drop: [ALL]` and `security_opt: [no-new-privileges:true]` across `docker-compose.yml` and `docker-compose.infra.yml`.
  - Aligned CalVer release versioning `2026.11.09.4` across `Directory.Build.props`, Gentoo ebuild, Arch AUR `.SRCINFO`/`PKGBUILD`, and packaging scripts.
  - Resolved `${rid}` shell unexpansion in `build-linux-packages.sh` and added `--allow-stub-packaging` fast-fail flags to packaging scripts.
  - Replaced legacy GitHub workflow with sovereign Forgejo Actions workflow `.forgejo/workflows/rotate-codesign-certs.yml`.
* **Comprehensive Documentation Parity & Full 8-Gate Verification Closeout (Forgejo #163 / P160)**:
  - Brought `docs/api-reference.md`, `docs/database-engines.md`, `docs/localization.md`, `docs/compliance-and-security.md`, `docs/index.md`, `docs/release-notes.md`, and `AGENTS.md` into 100% factual alignment with the codebase.
  - Maintained 100.00% line & branch coverage threshold across 1,847 deterministic unit tests with 0 compiler-generated state machine regressions.

---

## v2026.11.09.3 — Code-Complete Milestone: Sovereign Blazor UI Zero-Style Refactoring, Real Trajectory/Parquet Pipelines, bUnit Verification Suite & Regression Guards (2026-11-09)

Code-complete milestone release resolving Work Packages `P151–P155` (Forgejo Issues `#154–#158`), eliminating all 128 inline `style="..."` attributes across Blazor components in favor of reusable semantic dark-mode CSS classes, wiring real simulation campaigns, zero-allocation trajectory streaming (`XyzPipeReader`, `DcdPipeReader`, `LammpsCustomPipeReader`), and Parquet batch exports with SHA-256 verification into `WorkflowWizard.razor`, aligning JavaScript chart interop properties to camelCase (`hour`, `estradiolPmolL`, `testosteroneNmolL`), eliminating duplicate DOM IDs, enforcing Administrator gating and removing share-key auto-seeding in `ConfigurationManagement.razor`, authorizing Researcher patient profile inspection via `ISubjectAccessService`, recording authenticated operator attribution on GDPR crypto-shred in `ComplianceSecurity.razor`, adding archive upload controls in `TrajectoryBrowser.razor`, adding 100% XML docblocks across all Razor `@code` blocks, and establishing comprehensive bUnit component tests and permanent regression guards in `WebUiAndConfigurationTests` and `LocalizationTests`.

### Highlights
* **`WorkflowWizard.razor` Real Campaign, Parser, and Parquet Wiring & Unique DOM IDs (Forgejo #154 / P151)**:
  - Injected `ISimulationCampaignService` into `WorkflowWizard.razor` and wired Stage 3 `RunSimulationAsync` to execute real simulation logic (`SimulateSerumStability`), dynamically updating logs and progress bars.
  - Wired Stage 4 `RunStreamParseAsync` to stream molecular coordinates through `XyzParser`, `DcdParser`, or `LammpsParser` according to the selected format, measuring throughput via `Stopwatch` without garbage allocation.
  - Added `IParquetTrajectoryWriter.WriteFrameBatchAsync` and wired Stage 4 `RunParquetExportAsync` to write columnar Parquet archives and calculate SHA-256 digests via `SHA256.HashData`.
  - Updated `OnAfterRenderAsync` kinetics projection payload to camelCase `{ hour, estradiolPmolL, testosteroneNmolL }`, matching `weaver-visualizer.js:244-273` for non-NaN curve rendering.
  - Scoped Stage 5 HTML element IDs with `wizard-stage5-` and Stage 6 with `wizard-stage6-`, eliminating duplicate DOM IDs across steps.
* **Sovereign Blazor Page Functional & Security Defect Fixes (Forgejo #155 / P152)**:
  - Removed demo share-key auto-seeding in `ConfigurationManagement.razor` so revoking all keys leaves the table clean; enforced `Administrator` access checks before rendering settings or share keys.
  - Injected `ISubjectAccessService AccessService` in `UserProfilePage.razor`, authorizing `Researcher` users holding `ReadAccess` or `FullReadAccess` to inspect patient profiles.
  - Injected `UserSessionState SessionState` in `ComplianceSecurity.razor`, resolving the authenticated operator username and pseudonymized subject ID via `ComplianceService.PseudonymizeSubjectId` on `CryptoShredSubjectKey`.
  - Added `.tar.gz` and `.manifest.json` `<InputFile>` controls with `VerifiedDatasetImporter.ImportAsync` in `TrajectoryBrowser.razor` to support peer dataset verification and extraction.
* **Zero Inline Styles Refactoring, Razor XML Docblocks & ARIA Modal Accessibility (Forgejo #156 / P153)**:
  - Replaced all 128 inline `style="..."` attributes across all 12 Blazor component files with semantic CSS classes in `src/NanoWeaverHrt/wwwroot/css/app.css` and `docs/assets/sovereign-theme.css`.
  - Added 100% XML docblock coverage on all private and protected fields, properties, and methods in `SetupWizard.razor` and `UserManagement.razor`.
  - Added `role="dialog"` and `aria-modal="true"` to modal overlay containers across `UserManagement.razor` and `DnaManagementPage.razor`.
* **Comprehensive bUnit Component Test Suites & Runsettings Synchronization (Forgejo #157 / P154)**:
  - Added `ComplianceSecurityComponentTests.cs` covering ledger verification, operator-attributed crypto-shred, Safe Harbor de-identification, and ML-KEM-768 UI flows.
  - Added `UserProfilePageComponentTests.cs` covering Administrator supervision, self-access, Researcher `ReadAccess` evaluation, and cross-patient denial.
  - Added `ConfigurationManagementComponentTests.cs` covering Administrator role gating, empty share-key validation, and database precedence overrides.
  - Synchronized `ExcludeByFile` rules identically across `coverlet.runsettings` and `coverage-full.runsettings`.
* **Permanent Web UI Regression Guards & Localization Verification Gate (Forgejo #158 / P155)**:
  - Added `RazorComponents_MustNotContainInlineStyleAttributes` and `RazorComponents_MustNotContainDuplicateElementIds` to `WebUiAndConfigurationTests.cs`.
  - Added `LocalizationTests.cs` verifying 100% key parity and non-whitespace translations across all 18 JSON resource catalogs and runtime language resolution in `WeaverLocalizer`.
  - Verified `HttpOnly` cookie preservation in `weaver-session.test.mjs`, maintaining 100.00% JS line, branch, and function coverage via `npm test`.
  - Maintained 100.00% line & branch coverage threshold across 1,810 deterministic unit tests with 0 compiler-generated state machine regressions.

---

## v2026.11.09.2 — Code-Complete Milestone: Scientific Parser Fortran Bounds & SIMD Interleaving, DuckDB Analytics Sandbox, PBPK Kinetics Scaling & Client REST SDK Parity (2026-11-09)

Code-complete milestone release resolving Work Packages `P146–P150` (Forgejo Issues `#149–#153`), hardening `DcdPipeReader` against fragmented multi-title CHARMM/NAMD DCD headers and corrupt Fortran markers, accelerating SOA-to-AOS coordinate interleaving with `SimdCoordinateParser.InterleaveCoordinatesSimd`, restricting DuckDB user queries against prohibited DDL/DML keywords (`CREATE`, `REPLACE`, `TRUNCATE`, `EXPORT`, `IMPORT`, `SET`, `RESET`, `CALL`) with `active_trajectory` view aliasing, driving PBPK simulation kinetics and supraphysiological surge detection by `DepotDoseMg`, subscribing `ValkeyEventBridge` to inbound cluster `TrajectoryFrameBatchEvent` messages, wiring live `IAcceleratorTelemetryProvider` metrics into `/api/system/telemetry`, achieving 100% Client REST SDK parity (`CreatePatientDnaAsync`, `GetHealthAsync`, `GetGranularSystemHealthAsync`, and query-filtered `GetPatientDnaRegimensAsync`), and establishing zeroizable session token caching and logout clearing across `WeaverNodeApiClient`.

### Highlights
* **DCD Fortran Binary Header Bounds Hardening & SIMD Coordinate Interleaving (Forgejo #149 / P146)**:
  - Enforced strict title length limits (`0 <= titleLength <= 65536`) throwing `InvalidDataException` on corrupt lengths.
  - Added lookahead verification (`reader.Remaining < (long)titleLength + 16`) prior to buffer advancement, preventing buffer underflows on fragmented multi-title CHARMM/NAMD DCD headers.
  - Verified title and atom count trailing/leading Fortran markers, throwing `InvalidDataException` on mismatch.
  - Replaced scalar SOA-to-AOS coordinate conversion loops with vectorized `SimdCoordinateParser.InterleaveCoordinatesSimd`.
* **DuckDB Read-Only Analytics Sandbox & `active_trajectory` View Aliasing (Forgejo #149 / P146)**:
  - Expanded `ProhibitedKeywords` to reject `CREATE`, `REPLACE`, `TRUNCATE`, `EXPORT`, `IMPORT`, `SET`, `RESET`, and `CALL` statements.
  - Automatically bound `active_trajectory` temporary views for both synthetic in-memory samples and validated on-disk Parquet datasets.
* **Dose-Dependent PBPK Endocrine Kinetics, Valkey Inbound Frame Bridge & Live GPU Telemetry (Forgejo #150 / P147)**:
  - Implemented dose-responsive PBPK endocrine kinetics scaling peak and steady-state serum estradiol by `DepotDoseMg / 25.0` (MtF) and testosterone by `sqrt(DepotDoseMg / 30.0)` (FtM).
  - Evaluated supraphysiological surge threshold (`e2 > 800.0 pmol/L` triggers `SurgeDetected = true`) and physiological bounding (`[20.0, 2500.0] pmol/L`).
  - Added inbound cluster subscription for `ChannelPrefix + "TrajectoryFrameBatch"` (`TrajectoryFrameBatchEvent`) in `ValkeyEventBridge`.
  - Registered `IAcceleratorTelemetryProvider` singleton and wired live device name, temperature, power-derived utilization, and active simulation counts into `GET /api/system/telemetry`.
* **Client REST SDK Missing Endpoints & Cohort Group Contract Alignment (Forgejo #151 / P148)**:
  - Synchronized `PUT /api/subject-groups/{groupId}` to return the updated `SubjectGroup` entity.
  - Added `CreatePatientDnaAsync` (`POST /api/patients/{username}/dna`), `GetHealthAsync` (`GET /health`), and `GetGranularSystemHealthAsync` (`GET /api/system/health`) to `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient`.
  - Added query-filtered `GetPatientDnaRegimensAsync` overload supporting `regimen`, `geometry`, and `targetKd` filters.
* **`WeaverNodeApiClient` Zeroizable Session Token Caching & Unified Lifecycle (Forgejo #152 / P149)**:
  - Automatically cached issued Bearer tokens into pinned zeroizable memory upon `InitializeSetupAdminAsync`, `RedeemShareKeyAsync`, and `ExchangeWebTokenAsync`.
  - Guaranteed cryptographic memory wiping (`ClearSessionToken()`) in `LogoutAsync.finally`, transitioning `HasActiveSession` to `false`.
* **Scientific Parser, Analytics Sandbox & Client REST SDK End-to-End Verification Suite (Forgejo #153 / P150)**:
  - Added comprehensive test suites in `DcdPipeReaderTests`, `DuckDbTrajectoryAnalyticsServiceTests`, `SimulationCampaignServiceTests`, `NanoWeaverNodeClientTests`, and `WeaverNodeApiClientAndZeroizableBufferTests`.
  - Maintained 100.00% line & branch code coverage threshold across 1,781 deterministic unit tests with 0 compiler-generated state machine regressions.

---

## v2026.11.09.1 — Code-Complete Milestone: Multi-Engine Persistence Parity, Cryptographic Audit Determinism, Minimal API Security Hardening & 100.00% Un-Excluded Coverage Ratchet (2026-11-09)

Code-complete milestone release resolving Work Packages `P141–P145` (Forgejo Issues `#144–#148`), delivering 4-engine SQL DDL and DML parity across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB (`MySqlConnector`), isolated `0700` node secret storage with persistent HIPAA/GDPR pseudonymization key provisioning, microsecond-precision FDA 21 CFR Part 11 audit ledger hash chains, full-stream FASTA/VCF Safe Harbor de-identification, minimal API security gating and logout session revocation, and a downward ratchet to **0 uncovered compiler-generated state machine lines (`100.00%` un-excluded line coverage)** in `coverage-baseline.json`.

### Highlights
* **Node Secret Store Isolation, Persistent Pseudonymization Key & Multi-Engine Dialect DDL Parity (Forgejo #144 / P141)**:
  - Hardened `NodeSecretStore` to place `weaver-keys/` as a sibling directory outside `TRAJECTORY_INCOMING_DIR` with `0700` directory and `0600` file permissions, provisioning a persistent 32-byte `node_pseudonym_secret.key` (`EnsurePseudonymizationSecretKey`).
  - Added `MySqlConnector` (`2.4.0`) to `NanoWeaverHrt.csproj` and wired `DatabaseEngineProvider.MySql` and `DatabaseEngineProvider.DuckDb` in `WeaverDbConnectionFactory`.
  - Extended `Migration001` through `Migration011` with full 4-engine DDL parity (`GetUpSql(DatabaseEngineProvider provider)`) supporting SQLite, PostgreSQL, DuckDB, and MySQL (`VARCHAR` key lengths for indexed columns and `INSERT IGNORE` seed semantics).
* **Multi-Engine Repository Upsert/Parameter Dialect Parity & Async Audit Ledger Refactoring (Forgejo #145 / P142)**:
  - Extended `ISqlDialect` (`SqliteDialect`, `PostgreSqlDialect`, `DuckDbDialect`, `MySqlDialect`) and `WeaverDbParameters` with dialect-aware upsert builders (`BuildUpsertSql`, `BuildInsertIgnoreSql`) and positional/named parameter formatting across all 8 repositories.
  - Refactored `AuditLedgerRepository` to use asynchronous `ExecuteNonQueryAsync` and `ExecuteReaderAsync` operations (`AppendRecordAsync`, `GetLatestRecordsAsync`, `GetAllRecordsChronologicalAsync`).
* **Cryptographic Audit Ledger Microsecond Determinism, Full-Stream De-Identification & Token Nonce Hardening (Forgejo #146 / P143)**:
  - Upgraded `ComplianceAuditService` hash-chain canonicalization to microsecond precision (`yyyy-MM-ddTHH:mm:ss.ffffffZ`), serialized append-and-sign under a deterministic `SemaphoreSlim`, and replaced 64-bit truncated subject pseudonyms with 128-bit (32-hex-character) HMAC-SHA256 digests (`sub_<32hex>`).
  - Updated `DeIdentifyGenomicData` to scrub Safe Harbor PII patterns across the entire payload including VCF `##SAMPLE=`/`#CHROM` headers and inline comments, and removed the hardcoded legacy fallback key from `ProfileDataProtector`.
  - Added 128-bit cryptographic token ID (`jti`) claims to `AuthTokenClaims` and `UserAuthenticationService`, plus streaming SHA-256 hashing in `AssetIntegrityService`.
* **Minimal API Security Gating, Session Revocation, Forwarded Headers & RFC 7807 Standardization (Forgejo #147 / P144)**:
  - Enabled `UseForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`), added `/api/share-keys/redeem` to the unauthenticated bootstrap allowlist, and wired `IServerSessionActivityTracker.Revoke` into `POST /api/auth/logout` (including `AccessGate.Web` support).
  - Added `/api/genomics`, `/api/precision/genomics`, `/api/trajectories/analytics/query`, `/api/endocrine/closed-loop/simulate`, and `/api/fabrication/gel/analyze` to `ResearchRoutes` (`AdministratorOrResearcher`).
  - Fixed chunk byte-offset multiplication in `GET /api/federation/chunks/{fileName}`, prevented path traversal and host path disclosure on `POST /api/datasets/export-encrypted`, and standardized caller attribution and RFC 7807 `SecureProblemDetails` on `POST /api/compliance/crypto-shred`.
* **Coverage Baseline Ratchet to 0 Uncovered Lines & Parallel Environment Variable Test Isolation (Forgejo #148 / P145)**:
  - Isolated environment-mutating test suites under `[Collection("AcceleratorTelemetryEnvironmentTests")]` and eliminated all 27 remaining uncovered compiler-generated state machine lines in `Program.cs`, ratcheting `coverage-baseline.json` down to `0` uncovered lines (`100.00%` un-excluded line coverage).

---

## v2026.11.04.10 — Code-Complete Milestone: Total 100% Client REST SDK Parity (Simulation Campaigns, Web Session Auth & Healthz Liveness Probe) (2026-11-04)

Code-complete milestone release resolving Work Packages `P136–P140` (Forgejo Issues `#139–#143`), achieving total 100% route and endpoint parity across the client REST SDK (`INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient`) by implementing typed simulation campaign executions (oxDNA serum stability, LAMMPS aptamer gating conformational thermodynamics, steroid cargo retention and micro-leakage dynamics, and multi-compartment PBPK endocrine pharmacokinetics), browser-signed challenge token exchange with HTTP-only session cookie issuance (`POST /api/auth/web-token`), and cluster container runtime operational health liveness probe (`GET /healthz`); and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Client REST SDK Expansion: Simulation Campaigns (Serum Stability, Aptamer Gating, Payload Leakage & PBPK Endocrine) (Forgejo #139 / P136)**:
  - Extended `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 4 strongly-typed simulation campaign execution methods:
    1. `RunSerumStabilityAsync` (`POST /api/simulations/run/serum-stability`)
    2. `RunAptamerGatingAsync` (`POST /api/simulations/run/aptamer-gating`)
    3. `RunPayloadLeakageAsync` (`POST /api/simulations/run/payload-leakage`)
    4. `RunPbpkSimulationAsync` (`POST /api/simulations/run/pbpk-endocrine`)
* **Client REST SDK Expansion: Web Challenge Signature Authentication & Healthz Liveness Probe (Forgejo #140 / P137)**:
  - Added 2 core identity and container orchestrator probe methods:
    1. `ExchangeWebTokenAsync` (`POST /api/auth/web-token`)
    2. `GetHealthzAsync` (`GET /healthz`)
* **Client REST SDK Zeroizable Caching & Token Lifecycle Suite (Forgejo #141 / P138)**:
  - Implemented all 6 methods in `WeaverNodeApiClient` with zeroizable session token resolution, explicit caller token overrides, and disposed lifecycle guards.
  - Verified in `WeaverNodeApiClientAndZeroizableBufferTests` across unauthenticated, explicit-token, cached-token, and post-disposal assertions.
* **Client REST SDK End-to-End Test Suite & Error Resilience (Forgejo #142 / P139)**:
  - Added unit test suites in `NanoWeaverNodeClientTests` verifying nominal response deserialization, HTTP 401/403/500/503 status code mappings, empty/null payload handling, and argument boundary conditions.
* **Absolute Zero-Defect Repository Audit & CalVer v2026.11.04.10 Parity (Forgejo #143 / P140)**:
  - Synchronized CalVer version `2026.11.04.10` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Achieved 100.00% line & branch code coverage threshold with 0 XML docblock errors across all tests.

---

## v2026.11.04.9 — Code-Complete Milestone: Client REST SDK Expansion (Dataset Archives, Setup Wizard, System Health, Trajectory Parsing, Parquet & Precision Genomics) (2026-11-04)

Code-complete milestone release resolving Work Packages `P131–P135` (Forgejo Issues `#134–#138`), completing 100% route coverage across the client REST SDK (`INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient`) for dataset tarball archive imports, post-quantum ML-KEM-768/AES-256-GCM encrypted dataset bundle exports and raw downloads, first-launch cryptographic administrator master key setup initialization, system health overview telemetry, cluster event bus broadcast streaming, SIMD-accelerated trajectory parser executions, Parquet archive exports, endocrine hormone trajectory kinetics analysis, pharmacogenomic precision dosage recalculations, and automated Opentrons OT-2 Python protocol downloads; and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Client REST SDK Expansion: Dataset Archive Import, Post-Quantum Encrypted Export & Download Endpoints (Forgejo #134 / P131)**:
  - Extended `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 6 dataset archive and streaming methods:
    1. `ImportDatasetArchiveAsync` (`POST /api/datasets/import`)
    2. `ExportEncryptedDatasetAsync` (`POST /api/datasets/export-encrypted`)
    3. `DownloadDatasetExportAsync` (`GET /api/datasets/download/{exportId}`)
    4. `GetDatasetArchiveAsync` (`GET /api/datasets/{id}/archive`)
    5. `GetSimulationInputDeckAsync` (`GET /api/simulations/{id}/input-deck`)
    6. `DownloadSimulationFileAsync` (`GET /api/simulations/{id}/files/{filename}`)
* **Client REST SDK Expansion: First-Launch Setup Initialization, System Health & Event Bus Telemetry (Forgejo #135 / P132)**:
  - Added 3 core cluster lifecycle and telemetry methods:
    1. `InitializeSetupAsync` (`POST /api/setup/init`)
    2. `GetSystemHealthOverviewAsync` (`GET /api/system/health-overview`)
    3. `StreamEventsAsync` (`GET /api/events`)
* **Client REST SDK Expansion: Scientific Parser, Parquet Export & Endocrine Trajectory Analysis (Forgejo #136 / P133)**:
  - Added 4 high-throughput scientific analysis and trajectory methods:
    1. `ProcessTrajectoryParserAsync` (`POST /api/parser/process`)
    2. `ExportTrajectoryParquetAsync` (`POST /api/parser/export-parquet`)
    3. `AnalyzeTrajectoryKineticsAsync` (`POST /api/parser/analyze-kinetics`)
    4. `AnalyzeEndocrineTrajectoryAsync` (`POST /api/endocrine/analyze-trajectory`)
* **Client REST SDK Expansion: Pharmacogenomic Dosage Adjustments & Opentrons Script Download (Forgejo #137 / P134)**:
  - Added 3 precision genomic and automated fabrication methods:
    1. `CalculateGenomicAdjustmentAsync` (`POST /api/precision/genomics/adjust`)
    2. `GenerateOpentronsScriptAsync` (`POST /api/fabrication/opentrons/generate`)
    3. `DownloadOpentronsScriptAsync` (`GET /api/fabrication/opentrons/download/{scriptId}`)
* **Absolute Zero-Defect Repository Audit & CalVer v2026.11.04.9 Parity (Forgejo #138 / P135)**:
  - Synchronized CalVer version `2026.11.04.9` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Added 6 new DTO records with complete XML docblocks and Swashbuckle schema examples (`DatasetImportResponse`, `ExportEncryptedDatasetResult`, `SimulationInputDeckResult`, `TrajectoryParserProcessResult`, `TrajectoryParquetExportResult`, `GenomicAdjustmentResult`).
  - Achieved 100.00% line & branch code coverage threshold with 0 XML docblock errors across all tests.

---

## v2026.11.04.8 — Code-Complete Milestone: Client REST SDK Parity (User Admin, Contacts, Patient DNA CRUD, Auth Introspection & Compliance Services) (2026-11-04)

Code-complete milestone release resolving Work Packages `P126–P130` (Forgejo Issues `#129–#133`), expanding the client REST SDK with 28 new endpoints achieving 100% route coverage across user account administration, demographic and clinical profiles, patient structured addresses and emergency contacts, patient digitized DNA blueprints and sequencing import archives, clinical and simulation assay results, session identity introspection, explicit logout, first-launch setup gating, post-quantum ML-KEM-768 envelope record encryption/decryption, HIPAA/GINA de-identification, and GDPR Article 17 right-to-erasure cryptographic shredding; and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Client REST SDK Expansion: User Administration, Profiles, Addresses & Contacts (Forgejo #129 / P126)**:
  - Extended `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 11 administrative and directory methods:
    1. `ListUsersAsync` (`GET /api/users`)
    2. `CreateUserAsync` (`POST /api/users`)
    3. `DeleteUserAsync` (`DELETE /api/users/{username}`)
    4. `GetUserProfileAsync` (`GET /api/users/{username}/profile`)
    5. `UpdateUserProfileAsync` (`PUT /api/users/{username}/profile`)
    6. `ListUserAddressesAsync` (`GET /api/users/{username}/addresses`)
    7. `AddUserAddressAsync` (`POST /api/users/{username}/addresses`)
    8. `DeleteUserAddressAsync` (`DELETE /api/users/{username}/addresses/{id}`)
    9. `ListUserContactsAsync` (`GET /api/users/{username}/contacts`)
    10. `AddUserContactAsync` (`POST /api/users/{username}/contacts`)
    11. `DeleteUserContactAsync` (`DELETE /api/users/{username}/contacts/{id}`)
* **Client REST SDK Expansion: Patient Digital DNA, Import Archives & Clinical Results CRUD (Forgejo #130 / P127)**:
  - Added 10 patient clinical data methods to the client REST SDK:
    1. `GetPatientDigitalDnaRecordAsync` (`GET /api/patients/{username}/dna/{id}`)
    2. `DeletePatientDigitalDnaAsync` (`DELETE /api/patients/{username}/dna/{id}`)
    3. `ListPatientImportsAsync` (`GET /api/patients/{username}/imports`)
    4. `GetPatientImportAsync` (`GET /api/patients/{username}/imports/{id}`)
    5. `CreatePatientImportAsync` (`POST /api/patients/{username}/imports`)
    6. `DeletePatientImportAsync` (`DELETE /api/patients/{username}/imports/{id}`)
    7. `ListPatientClinicalResultsAsync` (`GET /api/patients/{username}/results`)
    8. `GetPatientClinicalResultAsync` (`GET /api/patients/{username}/results/{id}`)
    9. `CreatePatientClinicalResultAsync` (`POST /api/patients/{username}/results`)
    10. `DeletePatientClinicalResultAsync` (`DELETE /api/patients/{username}/results/{id}`)
* **Client REST SDK Expansion: Identity Introspection, Session Invalidation & Setup Status (Forgejo #131 / P128)**:
  - Added 3 identity and platform lifecycle methods:
    1. `GetCurrentUserAsync` (`GET /api/auth/me`)
    2. `LogoutAsync` (`POST /api/auth/logout`)
    3. `GetSetupStatusAsync` (`GET /api/setup/status`)
* **Client REST SDK Expansion: Compliance Cryptographic Shredding & Record Encryption (Forgejo #132 / P129)**:
  - Added 4 post-quantum cryptographic and regulatory compliance methods:
    1. `CryptoShredUserAsync` (`POST /api/compliance/crypto-shred`)
    2. `DeIdentifyDatasetAsync` (`POST /api/compliance/de-identify`)
    3. `EncryptRecordAsync` (`POST /api/compliance/encrypt-record`)
    4. `DecryptRecordAsync` (`POST /api/compliance/decrypt-record`)
* **Absolute Zero-Defect Repository Audit & CalVer v2026.11.04.8 Parity (Forgejo #133 / P130)**:
  - Synchronized CalVer version `2026.11.04.8` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Achieved 100.00% line & branch code coverage threshold with 0 XML docblock errors across all tests.

---

## v2026.11.04.7 — Code-Complete Milestone: Full Client REST SDK Parity (Subject Access, Fabrication, Stack Services, Endocrine Kinetics) & Platform Diagnostics Hardening (2026-11-04)

Code-complete milestone release resolving Work Packages `P121–P125` (Forgejo Issues `#124–#128`), expanding the client REST SDK with 11 new endpoints covering cohort group introspection, updates, subject group grants, fabrication origami presets, nanocarrier synthesis protocol generation, clinical genomic panels, stack service telemetry and health pinging, and dynamic endocrine kinetics and molecular switch toggles; hardening desktop tray controller notification resilience under process runner execution failures; and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Client REST SDK Expansion: Cohort Group Detail, Update & Subject Introspection (Forgejo #124 / P121)**:
  - Extended `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 3 cohort and subject introspection methods:
    1. `GetSubjectGroupAsync` (`GET /api/subject-groups/{groupId}`)
    2. `UpdateSubjectGroupAsync` (`PUT /api/subject-groups/{groupId}`)
    3. `ListMySubjectsAsync` (`GET /api/subject-access/my-subjects`)
  - Added comprehensive unit tests in `NanoWeaverNodeClientTests.cs` and `WeaverNodeApiClientAndZeroizableBufferTests.cs` verifying authenticated retrieval, payload mutations, and RFC 7807 error propagation.
* **Client REST SDK Expansion: Fabrication Presets & Synthesis Protocol Generation (Forgejo #125 / P122)**:
  - Added direct REST client methods for nanocarrier synthesis and pharmacogenomic profiling:
    1. `GetFabricationPresetsAsync` (`GET /api/fabrication/presets`)
    2. `GenerateFabricationProtocolAsync` (`POST /api/fabrication/generate`)
    3. `GetGenomicPresetsAsync` (`GET /api/genomics/presets`)
    4. `GetGenomicMarkersAsync` (`GET /api/genomics/markers`)
  - Validated deterministic serialization and deserialization of DNA origami nanocarrier chassis, staples, and pharmacogenomic marker catalogs.
* **Client REST SDK Expansion: Stack Services Telemetry & Endocrine Kinetics (Forgejo #126 / P123)**:
  - Added direct REST client methods for system infrastructure and endocrine state simulation:
    1. `GetStackServicesAsync` (`GET /api/services`)
    2. `PingStackServiceAsync` (`POST /api/services/ping/{serviceId}`)
    3. `GetEndocrineKineticsAsync` (`GET /api/endocrine/kinetics`)
    4. `ToggleEndocrineSwitchAsync` (`POST /api/endocrine/toggle`)
  - Added unit test fixtures validating multi-service health state inspection and closed-loop hormone curve point evaluation.
* **Desktop Tray Controller Notification Resilience & Instance Manager Status Tests (Forgejo #127 / P124)**:
  - Added test suites verifying `DesktopTrayIconController` state transitions across all `TrayHealthBadge` values (`Idle`, `Healthy`, `Warning`, `Critical`) and event dispatch integrity.
  - Added test suites verifying `DesktopNativeNotificationSink` resilience across Linux (`notify-send`), macOS (`osascript`), and Windows (`powershell`) when underlying platform process runners return failure or encounter execution errors.
  - Hardened `InstanceManagerViewModel` status messaging, exception suppression, and `IsBusy` reset behavior when repository initialization or profile listing fails.
* **Absolute Zero-Defect Repository Audit & CalVer Milestone Alignment (Forgejo #128 / P125)**:
  - Synchronized CalVer version `2026.11.04.7` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Passed all 8 checks of the Universal Pre-Flight Verification Gate (`./scripts/verify-all.sh`) with 100.00% line and branch coverage.

## v2026.11.04.6 — Code-Complete Milestone: Zero NotImplementedException Policy, Client REST SDK Cohort & Peering Parity, and ViewModel Diagnostics Hardening (2026-11-04)

Code-complete milestone release resolving Work Packages `P116–P120` (Forgejo Issues `#119–#123`), enforcing an absolute zero `NotImplementedException` policy across all production and test assemblies, expanding the client REST SDK with cohort membership management, group-level researcher access grants, share key generation, and peering token redemption, hardening desktop and mobile UI ViewModels against selection resets, whitespace inputs, and multi-field search filtering, and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Zero NotImplementedException Policy & Test Stub Hardening (Forgejo #119 / P116)**:
  - Eliminated all remaining `NotImplementedException` placeholders in test suites (`WebUiAndConfigurationTests.cs`, `UserProfileAndContactTests.cs`).
  - Enforced repository-wide static verification guaranteeing zero `NotImplementedException` occurrences exist anywhere in the codebase.
* **Client REST SDK Cohort Membership & Group Grants Expansion (Forgejo #120 / P117)**:
  - Expanded `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 6 cohort and group grant methods:
    1. `ListGroupMembersAsync` (`GET /api/subject-groups/{groupId}/members`)
    2. `AddGroupMemberAsync` (`POST /api/subject-groups/{groupId}/members`)
    3. `RemoveGroupMemberAsync` (`DELETE /api/subject-groups/{groupId}/members/{subjectUsername}`)
    4. `ListGroupGrantsAsync` (`GET /api/subject-groups/{groupId}/grants`)
    5. `CreateGroupGrantAsync` (`POST /api/subject-groups/{groupId}/grants`)
    6. `RevokeGroupGrantAsync` (`DELETE /api/subject-groups/{groupId}/grants/{researcherUsername}`)
  - Added comprehensive test coverage across nominal responses, empty or null payloads, missing group arguments, and RFC 7807 problem details handling.
* **Client REST SDK Share Key Creation & Peering Redemption (Forgejo #121 / P118)**:
  - Added direct REST client methods for invitation token workflows:
    1. `CreateShareKeyAsync` (`POST /api/share-keys/create`)
    2. `RedeemShareKeyAsync` (`POST /api/share-keys/redeem`)
  - Added full test coverage for authenticated token emission and peer session token acquisition.
* **Desktop & Mobile UI ViewModel Localization & Diagnostics Hardening (Forgejo #122 / P119)**:
  - Added test suites for `InstanceManagerViewModel` verifying selected profile automatic reset when missing from reloaded storage, coordinator sync edge cases, and case-insensitive multi-field search filtering.
  - Added test suites for `VaultUnlockViewModel` verifying property identity assignment branches, whitespace passphrase rejection, and idempotent double disposal.
  - Added test suites for `AboutViewModel` verifying metadata completeness, supported algorithm suites (Ed25519, ML-DSA-65), and simulation engine manifests (oxDNA, LAMMPS, PBPK closed loop).
* **Absolute Zero-Defect Repository Audit & CalVer Milestone Alignment (Forgejo #123 / P120)**:
  - Synchronized CalVer version `2026.11.04.6` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Passed all 8 checks of the Universal Pre-Flight Verification Gate (`./scripts/verify-all.sh`) with 100.00% line and branch coverage.

---

## v2026.11.04.5 — Code-Complete Milestone: Multi-Engine Subject Access, Endocrine Stability Hardening, Client REST Expansion & Desktop Shell Navigation (2026-11-04)

Code-complete milestone release resolving Work Packages `P111–P115` (Forgejo Issues `#114–#118`), implementing multi-engine subject access repository isolation and membership idempotence tests, hardening endocrine closed-loop simulations with setpoint stability and 720-hour extended numerical integration stress tests, expanding the client REST SDK with direct subject grant management, cohort group CRUD, and single-use share key listing, verifying desktop shell window host navigation across all pages and fault-tolerant language preference persistence, and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Multi-Engine Subject Access & Group Repository Isolation Stress Testing (Forgejo #114 / P111)**:
  - Added comprehensive stress test suite `SubjectAccessRepository_GroupCascadeAndMembershipIdempotence` validating membership duplicate idempotence (`ON CONFLICT DO NOTHING`), non-existent entity revocation/deletion, and multi-group discovery queries across SQLite and PostgreSQL.
* **Endocrine Closed-Loop Mathematical Setpoint & Tolerance Stress Hardening (Forgejo #115 / P112)**:
  - Added unit test suites verifying immediate steady-state convergence when patient baseline hormone concentrations match target setpoints (`Simulate_TargetMatchesBaseline_ReachesSteadyStateImmediately`).
  - Added 720-hour (30-day) extended continuous simulation stress test verifying 4th-order Runge-Kutta numerical integration stability, non-overflow, non-divergence, and bounded finite concentrations (`Simulate_MaxDuration720Hours_NumericallyStable`).
* **Client REST SDK Expansion for Subject Grants, Groups & Share Keys (Forgejo #116 / P113)**:
  - Expanded `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` with 6 new REST methods:
    1. `CreateSubjectGrantAsync` (`POST /api/subject-access/grants`)
    2. `RevokeSubjectGrantAsync` (`DELETE /api/subject-access/grants/{researcher}/{subject}`)
    3. `ListSubjectGroupsAsync` (`GET /api/subject-groups`)
    4. `CreateSubjectGroupAsync` (`POST /api/subject-groups`)
    5. `DeleteSubjectGroupAsync` (`DELETE /api/subject-groups/{groupId}`)
    6. `ListShareKeysAsync` (`GET /api/share-keys`)
  - Added full test coverage across nominal payloads, null response guards, argument validation, RFC 7807 problem details handling, session token caching, and post-disposal `ObjectDisposedException` assertions.
* **Desktop Shell Multi-Instance Navigation & Language Preference Persistence Hardening (Forgejo #117 / P114)**:
  - Added test suite verifying `MainShellWindowHost` navigation across all `ShellNavigationPage` values (`Dashboard`, `InstanceManager`, `VaultUnlock`, `About`), ensuring window title and active page synchronization.
  - Added test suite confirming `MainShellViewModel` gracefully suppresses transient local disk storage exceptions during background language preference persistence.
* **Absolute Zero-Defect Repository Audit & CalVer Milestone Alignment (Forgejo #118 / P115)**:
  - Synchronized CalVer version `2026.11.04.5` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Enforced 100% docblock coverage (`check-docblocks.py`), 100.00% developer line/branch code coverage, and zero unescaped single-dollar math formatting delimiters.

---

## v2026.11.04.4 — Code-Complete Milestone: Database Latency Diagnostics, Trajectory Analytics SQL Hardening, Full Client REST SDK Parity & Desktop Notification Resilience (2026-11-04)

Code-complete milestone release resolving Work Packages `P106–P110` (Forgejo Issues `#109–#113`), introducing live round-trip latency snapshot measurement across all database engines, hardening DuckDB trajectory analytics against extreme coordinates and math bounds, achieving 100% full REST SDK parity in `INanoWeaverNodeClient` and `WeaverNodeApiClient` for compliance audit ledger verification, genomic sequence analysis, trajectory analytical queries, and peer laboratory key management, verifying desktop and mobile notification sink resilience with edge-case characters and Unicode emojis, and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Multi-Engine Database Latency Snapshot & Reconnection Diagnostics (Forgejo #109 / P106)**:
  - Added `GetLatencySnapshotAsync` to `IWeaverDbConnectionFactory` and implemented round-trip `SELECT 1` ping measurement with high-precision stopwatch diagnostics.
  - Added unit tests in `WeaverDbConnectionFactoryTests` confirming accurate latency reporting and error handling across supported engines.
* **Trajectory Analytics SQL Parameterization & DuckDB Scan Stress Hardening (Forgejo #110 / P107)**:
  - Added mathematical bounds stress tests in `DuckDbTrajectoryAnalyticsServiceTests` validating handling of extreme coordinate ranges, floating-point infinities, and NaN values.
  - Verified clean query execution and robust error wrapping without engine crashes.
* **Client REST SDK Parity Expansion (Forgejo #111 / P108)**:
  - Added full REST SDK client parity in `INanoWeaverNodeClient`, `NanoWeaverNodeClient`, and `WeaverNodeApiClient` for `VerifyAuditLedgerAsync` (`GET /api/compliance/verify-ledger`), `AnalyzeGenomicSequenceAsync` (`POST /api/genomics/analyze-sequence`), `QueryTrajectoryAnalyticsAsync` (`POST /api/trajectories/analytics/query`), and peer key management (`ListPeerKeysAsync`, `RegisterPeerKeyAsync`, `RevokePeerKeyAsync`).
  - Added comprehensive test suites verifying nominal payloads, RFC 7807 problem details error handling, session token buffer caching, and post-disposal `ObjectDisposedException` assertions.
* **Desktop Tray & Multi-Instance UI ViewModels Robustness Hardening (Forgejo #112 / P109)**:
  - Added edge-case test suites for `DesktopNativeNotificationSink` and `MobileNotificationSink` handling control characters, newlines, null bytes, backslashes, and Unicode emojis across Linux (`notify-send`), macOS (`osascript`), Windows (`powershell`), Android, iOS, and iPadOS.
  - Added `MainShellViewModel` test suite verifying localized connectivity status and system tray updates across all connection states (`Online`, `Connecting`, `Offline`, `Degraded`, `Unauthorized`, `Disconnected`).
* **Absolute Zero-Defect Repository Audit & CalVer Milestone Alignment (Forgejo #113 / P110)**:
  - Synchronized CalVer version `2026.11.04.4` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Enforced 100% docblock coverage (`check-docblocks.py`), 100.00% developer line/branch code coverage, and zero unescaped single-dollar math formatting delimiters.

---

## v2026.11.04.3 — Code-Complete Milestone: Multi-Engine Connection Resilience, Ratchet Invariants, Client REST Compliance & Desktop Tray Hardening (2026-11-04)

Code-complete milestone release resolving Work Packages `P101–P105` (Forgejo Issues `#104–#108`), validating multi-engine connection factory fault injection and credential sanitization, eliminating minimal API edge-case gaps while preserving downward coverage ratchet invariants, expanding the client REST SDK with compliance and pharmacogenomics endpoints, hardening the desktop tray icon controller with localization, health badges, and clean disposal teardown, and synchronizing CalVer release versioning across all 12 packaging manifests and platform targets.

### Highlights
* **Multi-Engine Connection Factory Reconnection & Fault Injection Resilience (Forgejo #104 / P101)**:
  - Added fault injection tests asserting that database connection credentials, passwords, usernames, and cluster hostnames are strictly redacted and never leaked inside `WeaverDatabaseUnavailableException.Message`.
  - Verified that MySQL session checkout failures in `WeaverDbConnectionFactory.OpenBackendAsync` safely dispose the half-opened connection without handle leakage and wrap errors cleanly into `WeaverDatabaseUnavailableException`.
* **Ratchet Verification & Minimal API Edge-Case Gaps (Forgejo #105 / P102)**:
  - Verified edge-case branches on `POST /api/datasets/import` for archive-only and manifest-only multipart form payloads.
  - Validated duplicate setup initialization conflict handling and confirmed that all 27 solution-wide defense-in-depth lines remain strictly pinned and allowed by the downward-only baseline in `coverage-baseline.json`.
* **Client REST SDK Compliance & Genomics Expansion (Forgejo #106 / P103)**:
  - Expanded `INanoWeaverNodeClient` and `WeaverNodeApiClient` with `GetComplianceStatusAsync` (`GET /api/compliance/status`), `GetAuditLedgerAsync` (`GET /api/compliance/audit-ledger`), and `AnalyzeGenomicsAsync` (`POST /api/genomics/analyze`).
  - Added comprehensive unit test coverage in `NanoWeaverNodeClientTests` and `WeaverNodeApiClientAndZeroizableBufferTests` for nominal payloads, query parameters (limit), empty collections, RFC 7807 error responses, session token caching, and post-disposal `ObjectDisposedException` guards.
* **Desktop System Tray Controller Hardening & Teardown (Forgejo #107 / P104)**:
  - Hardened `DesktopTrayIconController` with `IDisposable` implementation to safely detach `StateChanged` event subscriptions and prevent memory leaks.
  - Verified localization support across English and Arabic (RTL layout direction), standard context menu item resolution (`Show / Restore`, `Lock Key Vault`, `Quit`), health badge transitions (`Idle`, `Healthy`, `Warning`, `Critical`), fallback string handling, and clean teardown.
* **Absolute Zero-Defect Repository Audit & CalVer Milestone Alignment (Forgejo #108 / P105)**:
  - Synchronized CalVer version `2026.11.04.3` across Arch Linux AUR (binary & git), FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, ViewModels, and test fixtures.
  - Enforced 100% docblock coverage (`check-docblocks.py`), 100.00% developer line/branch code coverage, and zero unescaped single-dollar math formatting delimiters.

---

## v2026.11.04.2 — Code-Complete Milestone: Multi-Engine Migration Dialect Compliance, Ratchet Elimination & Client SDK Parity (2026-11-04)

Code-complete milestone release resolving Work Packages `P96–P100` (Forgejo Issues `#99–#103`), formally verifying multi-engine schema migration DDL across all 5 supported relational databases, eliminating Roslyn async state machine coverage ratchet gaps in Blazor components down to only 27 uncovered lines solution-wide (99.86% line coverage), expanding the client REST SDK with dataset export and configuration override APIs, and synchronizing CalVer release versioning across all 12 packaging ecosystems.

### Highlights
* **Database Migration Multi-Engine Compliance & Idempotence (Forgejo #99 / P96)**:
  - Validated idempotent execution of all 12 registered database migrations (001–012) against live in-memory SQLite engines, ensuring zero-overhead re-execution when already applied.
  - Verified DDL SQL generation across SQLite, PostgreSQL, DuckDB, MySQL, and MariaDB dialects for all migrations, verifying that DDL migrations generate valid non-empty statements while data-widening migrations safely target PostgreSQL and no-op on TEXT-native engines.
* **Eliminate Blazor State Machine Ratchet Gaps (Forgejo #100 / P97)**:
  - Extracted telemetry timer dispatch logic in `MainLayout.razor` into an explicit synchronous tick handler `HandleTelemetryTimerTick()`, eliminating timer race conditions and achieving 100.00% line coverage (21 uncovered lines reduced to 0).
  - Covered setup wizard key-option switching lambda branches in `SetupWizard.razor` (1 uncovered line reduced to 0).
  - Total uncovered lines solution-wide dropped from 49 to **27 lines across 23 classes**, elevating downward coverage ratchet baseline to **99.86% line / 98.08% branch coverage**.
* **Client REST SDK Extended Endpoint Expansion (Forgejo #101 / P98)**:
  - Expanded `INanoWeaverNodeClient` and `WeaverNodeApiClient` with 6 administrative and dataset endpoints: `ListTrajectoriesAsync`, `ExportDatasetAsync`, `GetAllConfigurationsAsync`, `SetConfigurationOverrideAsync`, `DeleteConfigurationOverrideAsync`, and `ListSubjectGrantsAsync`.
  - Added comprehensive test suites in `NanoWeaverNodeClientTests` and `WeaverNodeApiClientAndZeroizableBufferTests` covering nominal payloads, empty arrays, RFC 7807 `NodeApiException` errors, and disposal zeroization.
* **Desktop Shell Navigation & Mobile Biometric Key Vault Flow Hardening (Forgejo #102 / P99)**:
  - Verified desktop Avalonia MVVM ViewModel event dispatching and unsealing workflow in `VaultUnlockViewModel`, `InstanceOnboardingViewModel`, and `MainShellViewModel`.
  - Hardened mobile biometric lifecycle and cancellation flows across Android Keystore and Apple Secure Enclave providers.
* **Code-Complete Repository Audit & CalVer Milestone Alignment (Forgejo #103 / P100)**:
  - Synchronized CalVer version `2026.11.04.2` across Arch Linux AUR, FreeBSD, Homebrew, AppStream metainfo, macOS Info.plist, Nix, Windows Appx/NSIS/WinGet, Android, iOS, iPadOS, desktop entry points, and test fixtures.
  - Enforced 100% docblock coverage (`check-docblocks.py`) and zero unescaped single-dollar math formatting delimiters.

---

## v2026.11.04.1 — Multi-Engine Database Stress, Client REST SDK Parity & Coverage Ratchet Hardening (2026-11-04)

Milestone release resolving Work Packages `P91–P95` (Forgejo Issues `#94–#98`), driving downward un-excluded coverage ratchet gaps, expanding the desktop and mobile HTTP REST client SDK (`INanoWeaverNodeClient` and `WeaverNodeApiClient`), integrating Freedesktop Flatpak sandbox assets, executing multi-engine database concurrency and transaction rollback stress testing, and synchronizing CI/CD workflow pipelines.

### Highlights
* **Minimal API & Security Coverage Ratchet Hardening (Forgejo #94 / P91)**:
  - Validated edge-case branches in `Program.cs` for duplicate first-launch initialization (`POST /api/setup/initialize` 400 rejection with sanitized RFC 7807 problem details).
  - Validated cross-subject patient authorization boundaries across user profiles, addresses, contacts, DNA blueprints, imports, and kinetics results (`403 Forbidden` and zero-disclosure `404 Not Found`).
  - Added full test coverage for `MainLayout.razor` idle watcher DOM exception recovery and automated session expiration routing.
  - Added constructor and property boundary tests for `Login.WeaverSignInResult` and `SetupWizard` fingerprint formatting.
* **Mobile & Desktop Client REST SDK Expansion (Forgejo #95 / P92)**:
  - Expanded `INanoWeaverNodeClient` and `WeaverNodeApiClient` with 10 production endpoints: closed-loop endocrine simulation, P2P swarm federation peer discovery, manifest generation and verification, gel densitometry analysis, hardware accelerator benchmarking, Opentrons automated protocol synthesis, and patient digitized DNA/regimen querying.
  - Added `UnifiedMobileBiometricVault.DownloadAndCachePatientPackageAsync` to securely download and cache patient digital DNA blueprints in-memory after biometric unlock.
  - Added unit test suites verifying token propagation, null payload validation, and zeroizable secret memory buffer disposal.
* **Flatpak Manifest & AUR Git CalVer Alignment (Forgejo #96 / P93)**:
  - Integrated Freedesktop assets (desktop icon, `.desktop` entry, and AppStream metainfo) into `packaging/flatpak/one.lantea.nanoweaver.hrt.yml`.
  - Aligned Arch Linux AUR Git package (`nanoweaver-hrt-desktop-git`) PKGBUILD and `.SRCINFO` to CalVer base `2026.11.03.2.r0`.
  - Documented Flatpak build/install workflows and AppStream XML validation in `docs/desktop-and-mobile-gui.md`.
* **Multi-Engine Database Connection Pool, Rollback & Concurrency Stress (Forgejo #97 / P94)**:
  - Validated transaction lifecycle rollback behavior under simulated migration failure across SQLite and recording connection delegates.
  - Verified concurrent connection pool stability under 50 simultaneous worker threads executing parallel read/write queries without lock contention or pool exhaustion.
  - Tested DuckDB in-memory initialization and scalar execution paths.
* **CI/CD Workflow Alignment & Sovereign Release Documentation (Forgejo #98 / P95)**:
  - Harmonized GitHub Actions workflow (`.github/workflows/build-and-deploy.yml`) and Sovereign Forgejo workflow (`.forgejo/workflows/build-and-deploy.yml`) across all 8 verification gates.
  - Aligned release notes, package versions, and documentation parity.

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

