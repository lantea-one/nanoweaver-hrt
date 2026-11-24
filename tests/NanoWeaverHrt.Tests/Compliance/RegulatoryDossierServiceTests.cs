using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Compliance;

/// <summary>
/// Comprehensive unit tests verifying <see cref="RegulatoryDossierService"/>,
/// <see cref="RegulatoryDossierModels"/>, and <see cref="RegulatoryDossierApiModels"/>
/// including 10-gate fail-closed IND/IMPD compilation, HIPAA Safe Harbor pseudonymization,
/// AES-256-GCM at-rest bundle encryption, patient metadata redaction, and cross-node ML-DSA-65 verification.
/// </summary>
public sealed class RegulatoryDossierServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding SQLite storage and PQC node key material.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// User account repository for satisfying foreign keys on patient DNA and result tables.
    /// </summary>
    private readonly UserRepository _userRepository;

    /// <summary>
    /// Patient digital DNA and pharmacogenomic screening result repository.
    /// </summary>
    private readonly PatientDnaRepository _dnaRepository;

    /// <summary>
    /// Semi-annual depot formulation repository.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepository;

    /// <summary>
    /// Deterministic 180-day time-release depot pharmacokinetic simulator.
    /// </summary>
    private readonly TimeReleaseDepotService _depotService;

    /// <summary>
    /// Wet-lab fabrication lot, QC, and recall repository.
    /// </summary>
    private readonly FabricationLotRepository _lotRepository;

    /// <summary>
    /// Wet-lab Certificate of Analysis reconstruction and verification service.
    /// </summary>
    private readonly FabricationLotService _lotService;

    /// <summary>
    /// Reversal antidote persistence repository.
    /// </summary>
    private readonly ReversalAntidoteRepository _antidoteRepository;

    /// <summary>
    /// Toehold strand-displacement reversal antidote pairing and kinetics service.
    /// </summary>
    private readonly ReversalAntidoteService _antidoteService;

    /// <summary>
    /// Cold-chain Arrhenius stability and custody ledger repository.
    /// </summary>
    private readonly ColdChainCustodyRepository _coldChainRepository;

    /// <summary>
    /// Cold-chain thermal stability and custody verification service.
    /// </summary>
    private readonly ColdChainStabilityService _coldChainService;

    /// <summary>
    /// Clinical pharmacovigilance adverse event and biomarker repository.
    /// </summary>
    private readonly ClinicalPharmacovigilanceRepository _pvRepository;

    /// <summary>
    /// Clinical pharmacovigilance surveillance service.
    /// </summary>
    private readonly ClinicalPharmacovigilanceService _pvService;

    /// <summary>
    /// Epigenetic toggle circuit persistence repository.
    /// </summary>
    private readonly EpigeneticCircuitRepository _circuitRepository;

    /// <summary>
    /// Multi-year coupled homeostasis forecast repository.
    /// </summary>
    private readonly MultiYearHomeostasisRepository _multiYearRepository;

    /// <summary>
    /// Regulatory dossier snapshot repository.
    /// </summary>
    private readonly RegulatoryDossierRepository _dossierRepository;

    /// <summary>
    /// Trusted peer laboratory signing key repository.
    /// </summary>
    private readonly PeerKeyRepository _peerKeyRepository;

    /// <summary>
    /// Node ML-DSA-65 signing key management service.
    /// </summary>
    private readonly KeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptography provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// FDA 21 CFR Part 11 cryptographic audit ledger service.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Authenticated AES-256-GCM storage field protector.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Regulatory dossier compiler service under test.
    /// </summary>
    private readonly RegulatoryDossierService _service;

    /// <summary>
    /// Deterministic UTC timestamp source for reproducible test assertions.
    /// </summary>
    private DateTimeOffset _clockNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegulatoryDossierServiceTests"/> class.
    /// </summary>
    public RegulatoryDossierServiceTests()
    {
        // WHAT: Provisions an isolated SQLite database, PQC crypto vault, audit ledger, and RegulatoryDossierService.
        // WHY:  Tests real database persistence, AES-256-GCM encryption, and ML-DSA-65 verification end-to-end.
        // HOW:  Constructs shared test factories, initialized repositories, and deterministic UTC clock delegate.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_dossier_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _clockNow = new DateTimeOffset(2026, 11, 24, 16, 10, 0, 123, TimeSpan.Zero);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty,
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();

        _factory = WeaverTestDb.CreateFactory(config);
        NodeSecretStore secretStore = new(config);
        _protector = new ProfileDataProtector(secretStore);
        _cryptoProvider = new PostQuantumCryptoProvider();

        SubjectKeyRepository subjectKeyRepo = new(_factory);
        SubjectErasureJournal erasureJournal = new(secretStore);
        SubjectKeyVault vault = new(subjectKeyRepo, _protector, _cryptoProvider, erasureJournal);
        vault.InitializeAsync().GetAwaiter().GetResult();

        _keyManagementService = new KeyManagementService(_cryptoProvider, vault, secretStore);
        _auditService = new ComplianceAuditService(_keyManagementService, _cryptoProvider);

        _userRepository = new UserRepository(_factory);
        _userRepository.InitializeAsync().GetAwaiter().GetResult();

        _dnaRepository = new PatientDnaRepository(_factory, _protector);
        _depotRepository = new DepotFormulationRepository(_factory);
        _depotService = new TimeReleaseDepotService();
        _lotRepository = new FabricationLotRepository(_factory);
        _lotService = new FabricationLotService(_keyManagementService, _cryptoProvider);
        _antidoteRepository = new ReversalAntidoteRepository(_factory);
        _antidoteService = new ReversalAntidoteService(_lotRepository, _antidoteRepository, _keyManagementService, _cryptoProvider, _auditService, () => _clockNow);
        _coldChainRepository = new ColdChainCustodyRepository(_factory);
        _coldChainService = new ColdChainStabilityService(_lotRepository, _coldChainRepository, _protector, _auditService, () => _clockNow, _antidoteService);
        _pvRepository = new ClinicalPharmacovigilanceRepository(_factory);
        _pvService = new ClinicalPharmacovigilanceService(_lotRepository, _pvRepository, _protector, _auditService, () => _clockNow);
        _circuitRepository = new EpigeneticCircuitRepository(_factory);
        _multiYearRepository = new MultiYearHomeostasisRepository(_factory);
        _dossierRepository = new RegulatoryDossierRepository(_factory);
        _peerKeyRepository = new PeerKeyRepository(_factory);

        _service = BuildService();
    }

    /// <summary>
    /// Verifies OpenAPI <see cref="IExamplesProvider{T}.GetExamples"/> implementations across Milestone 45 models.
    /// </summary>
    [Fact]
    public void DossierModels_GetExamples_ReturnPopulatedInstances()
    {
        // WHAT: Exercises GetExamples() on SignedRegulatoryDossierBundle, RegulatoryDossierSummary, RegulatoryDossierVerificationReport, and API request DTOs.
        // WHY:  Ensures OpenAPI documentation schemas build cleanly and all model property getters/setters hit 100% coverage.
        // HOW:  Instantiates default examples and verifies sentinel values across nested QualityCmc/PreclinicalQsp/Compliance sections.
        SignedRegulatoryDossierBundle bundleEx = new SignedRegulatoryDossierBundle().GetExamples();
        Assert.Equal("nanoweaver-ind-dossier-v1", bundleEx.BundleVersion);
        Assert.Equal("ind_20261124_001", bundleEx.Manifest.DossierId);
        Assert.True(bundleEx.Manifest.IsSubmissionReady);
        Assert.Equal(100.0, bundleEx.Manifest.CompletenessScore);
        Assert.NotNull(bundleEx.Manifest.QualityCmc.DnaBlueprintId);
        Assert.NotNull(bundleEx.Manifest.PreclinicalQsp.EpigeneticCircuitId);
        Assert.True(bundleEx.Manifest.Compliance.AuditLedgerIntact);
        Assert.Single(bundleEx.Manifest.ReadinessGates);

        RegulatoryDossierSummary summaryEx = new RegulatoryDossierSummary().GetExamples();
        Assert.Equal("ind_20261124_001", summaryEx.DossierId);
        Assert.False(summaryEx.IsRedactedForPatient);

        RegulatoryDossierVerificationReport verifyEx = new RegulatoryDossierVerificationReport().GetExamples();
        Assert.True(verifyEx.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.LocalNode, verifyEx.TrustSource);

        CompileRegulatoryDossierRequest compileReqEx = new CompileRegulatoryDossierRequest().GetExamples();
        Assert.Equal(RegulatorySubmissionFramework.FdaInd, compileReqEx.SubmissionFramework);

        VerifyRegulatoryDossierRequest verifyReqEx = new VerifyRegulatoryDossierRequest().GetExamples();
        Assert.NotNull(verifyReqEx.Bundle);
    }

    /// <summary>
    /// Verifies constructor null checks and public method argument validation guards on <see cref="RegulatoryDossierService"/>.
    /// </summary>
    [Fact]
    public async Task Service_ConstructorAndMethodGuards_ThrowOnInvalidInputs()
    {
        // WHAT: Tests every constructor dependency null guard and method input validation guard on RegulatoryDossierService.
        // WHY:  Guarantees fail-fast contracts and 100.00% branch coverage on Defensive guards.
        // HOW:  Iterates null arguments across each constructor slot and invalid arguments across service methods.
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(null!, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, null!, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, null!, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, null!, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, null!, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, null!, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, null!, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, null!, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, null!, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, null!, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, null!, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, null!, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, null!, _cryptoProvider, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, null!, _peerKeyRepository, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, null!, _protector));
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierService(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, null!));

        // Exercise default utcNow fallback branch as well.
        RegulatoryDossierService defaultClockSvc = new(_dossierRepository, _dnaRepository, _depotRepository, _depotService, _lotRepository, _lotService, _antidoteService, _coldChainService, _pvService, _circuitRepository, _multiYearRepository, _auditService, _keyManagementService, _cryptoProvider, _peerKeyRepository, _protector);
        Assert.Null(await defaultClockSvc.GetDecryptedBundleAsync("missing_dossier_id"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompileDossierAsync(" ", null, "admin_1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompileDossierAsync("pat_1", null, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ListDossiersForPatientAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetDecryptedBundleAsync(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VerifyDossierBundleAsync(null!, "admin_1"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VerifyDossierBundleAsync(new SignedRegulatoryDossierBundle { Manifest = null! }, "admin_1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.VerifyDossierBundleAsync(new SignedRegulatoryDossierBundle(), " "));
    }

    /// <summary>
    /// Verifies complete FDA IND &amp; EMA IMPD compilation, HIPAA Safe Harbor pseudonymization inside signed manifests,
    /// AES-256-GCM encrypted persistence round-trips, patient redaction, and 21 CFR Part 11 audit events.
    /// </summary>
    [Fact]
    public async Task CompileListDecrypt_ReadyPatientEvidence_PassesAllTenGatesAndEncryptsAtRest()
    {
        // WHAT: Seeds complete valid patient evidence across all 10 clinical/CMC subsystems, compiles FDA IND (auto-resolve) and EMA IMPD (explicit bindings).
        // WHY:  Verifies end-to-end 100.0 completeness scoring, zero raw PII inside signed manifests, AES-256-GCM at-rest encryption, and redaction.
        // HOW:  Compiles both frameworks, inspects database row ciphertext, verifies decrypt round-trip, redacted listing, and audit records.
        SeededPatientContext ctx = await SeedCompleteReadyPatientAsync("patient_ready_alpha", "Complete");

        // 1. Auto-discovery compilation (null request -> FdaInd default, auto-selected IDs, default sponsor & site)
        SignedRegulatoryDossierBundle indBundle = await _service.CompileDossierAsync("patient_ready_alpha", null, "admin_regulatory");

        Assert.StartsWith("ind_", indBundle.Manifest.DossierId, StringComparison.Ordinal);
        Assert.Equal(RegulatorySubmissionFramework.FdaInd, indBundle.Manifest.SubmissionFramework);
        Assert.Equal(100.0, indBundle.Manifest.CompletenessScore);
        Assert.True(indBundle.Manifest.IsSubmissionReady);
        Assert.Empty(indBundle.Manifest.BlockingReasons);
        Assert.Equal(10, indBundle.Manifest.ReadinessGates.Count);
        Assert.All(indBundle.Manifest.ReadinessGates, g => Assert.True(g.Passed, g.Summary));

        // Verify HIPAA Safe Harbor pseudonymization: no raw patient username inside manifest JSON!
        string manifestJson = JsonSerializer.Serialize(indBundle.Manifest);
        Assert.DoesNotContain("patient_ready_alpha", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_auditService.PseudonymizeSubjectId("patient_ready_alpha"), indBundle.Manifest.PseudonymizedPatientId);
        Assert.Equal(ctx.LotId, indBundle.Manifest.QualityCmc.LotId);
        Assert.Equal(ctx.CircuitId, indBundle.Manifest.PreclinicalQsp.EpigeneticCircuitId);
        Assert.Equal(ctx.DepotId, indBundle.Manifest.PreclinicalQsp.DepotId);
        Assert.Equal(1, indBundle.Manifest.QualityCmc.CustodyHandoffCount);

        // 2. Verify AES-256-GCM encryption at rest in weaver_regulatory_dossiers
        RegulatoryDossierRecord? storedRow = await _dossierRepository.GetDossierByIdAsync(indBundle.Manifest.DossierId);
        Assert.NotNull(storedRow);
        Assert.StartsWith("data:application/encrypted;base64,", storedRow.BundleJsonEncrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("nanoweaver-ind-dossier-v1", storedRow.BundleJsonEncrypted, StringComparison.Ordinal);

        SignedRegulatoryDossierBundle? decrypted = await _service.GetDecryptedBundleAsync(indBundle.Manifest.DossierId);
        Assert.NotNull(decrypted);
        Assert.Equal(indBundle.PayloadSha256, decrypted.PayloadSha256);

        // 3. Explicit EMA IMPD compilation with custom sponsor and trial site code
        _clockNow = _clockNow.AddMinutes(5);
        CompileRegulatoryDossierRequest impdRequest = new()
        {
            SubmissionFramework = RegulatorySubmissionFramework.EmaImpd,
            LotId = ctx.LotId,
            DepotId = ctx.DepotId,
            CircuitId = ctx.CircuitId,
            SponsorName = "EU-CONCEPT Trial Consortium",
            ClinicalTrialSiteCode = "ZURICH-04"
        };
        SignedRegulatoryDossierBundle impdBundle = await _service.CompileDossierAsync("patient_ready_alpha", impdRequest, "admin_regulatory");

        Assert.StartsWith("impd_", impdBundle.Manifest.DossierId, StringComparison.Ordinal);
        Assert.Equal(RegulatorySubmissionFramework.EmaImpd, impdBundle.Manifest.SubmissionFramework);
        Assert.Equal("EU-CONCEPT Trial Consortium", impdBundle.Manifest.SponsorName);
        Assert.Equal("ZURICH-04", impdBundle.Manifest.ClinicalTrialSiteCode);
        Assert.True(impdBundle.Manifest.IsSubmissionReady);

        // 4. Listing: unredacted vs patient-redacted
        IReadOnlyList<RegulatoryDossierSummary> adminList = await _service.ListDossiersForPatientAsync("patient_ready_alpha", redactForPatient: false);
        Assert.Equal(2, adminList.Count);
        Assert.Equal(impdBundle.Manifest.DossierId, adminList[0].DossierId);
        Assert.Equal("admin_regulatory", adminList[0].CompiledBy);
        Assert.False(adminList[0].IsRedactedForPatient);

        IReadOnlyList<RegulatoryDossierSummary> patientList = await _service.ListDossiersForPatientAsync("patient_ready_alpha", redactForPatient: true);
        Assert.Equal(2, patientList.Count);
        Assert.Equal("[REDACTED]", patientList[0].CompiledBy);
        Assert.Equal("[REDACTED]", patientList[0].SignerKeyId);
        Assert.True(patientList[0].IsRedactedForPatient);

        // Verify local verification + audit trail emission
        RegulatoryDossierVerificationReport localReport = await _service.VerifyDossierBundleAsync(indBundle, "auditor_eu");
        Assert.True(localReport.HashValid);
        Assert.True(localReport.SignatureValid);
        Assert.True(localReport.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.LocalNode, localReport.TrustSource);

        IReadOnlyList<AuditRecord> audits = _auditService.GetAuditRecords(20);
        Assert.Contains(audits, a => a.Action == "COMPLIANCE_IND_DOSSIER_COMPILE");
        Assert.Contains(audits, a => a.Action == "COMPLIANCE_IND_DOSSIER_VERIFY");
    }

    /// <summary>
    /// Verifies fail-closed blocking across every individual readiness gate when evidence is missing,
    /// failed, revoked, thermally compromised, untrusted, or belongs to a different patient.
    /// </summary>
    [Fact]
    public async Task CompileDossier_FailClosedGatesAndOwnership_BlockSubmissionCleanly()
    {
        // WHAT: Exercises missing-everything compilation (0.0 score) and systematic individual failure modes across all 10 gates.
        // WHY:  Ensures every single gate fails closed accurately without leaking data across patients.
        // HOW:  Compiles empty patient, then progressively breaks individual subsystems on distinct seeded patients and asserts blocker gates.
        await SeedUserAsync("patient_empty");
        SignedRegulatoryDossierBundle emptyBundle = await _service.CompileDossierAsync("patient_empty", new CompileRegulatoryDossierRequest
        {
            SponsorName = "  ",
            ClinicalTrialSiteCode = null
        }, "admin_check");

        Assert.False(emptyBundle.Manifest.IsSubmissionReady);
        Assert.Equal(10.0, emptyBundle.Manifest.CompletenessScore); // Audit ledger is valid (1/10 = 10.0)
        Assert.Equal(9, emptyBundle.Manifest.BlockingReasons.Count);
        Assert.Null(emptyBundle.Manifest.QualityCmc.DnaBlueprintId);
        Assert.Null(emptyBundle.Manifest.QualityCmc.LotId);
        Assert.Null(emptyBundle.Manifest.QualityCmc.AntidoteId);
        Assert.Null(emptyBundle.Manifest.QualityCmc.ColdChainRemainingPotencyPercent);
        Assert.Null(emptyBundle.Manifest.PreclinicalQsp.PharmacogenomicsResultId);
        Assert.Null(emptyBundle.Manifest.PreclinicalQsp.DepotId);
        Assert.Null(emptyBundle.Manifest.PreclinicalQsp.EpigeneticCircuitId);
        Assert.Null(emptyBundle.Manifest.PreclinicalQsp.MultiYearRunId);

        // Seed ready baseline for individual sabotage cases + cross-patient isolation
        SeededPatientContext otherCtx = await SeedCompleteReadyPatientAsync("patient_other", "Verified");

        // Cross-patient explicit IDs are ignored fail-closed
        SignedRegulatoryDossierBundle crossBundle = await _service.CompileDossierAsync("patient_empty", new CompileRegulatoryDossierRequest
        {
            LotId = otherCtx.LotId,
            DepotId = otherCtx.DepotId,
            CircuitId = otherCtx.CircuitId
        }, "admin_check");
        Assert.Null(crossBundle.Manifest.QualityCmc.LotId);
        Assert.Null(crossBundle.Manifest.PreclinicalQsp.DepotId);
        Assert.Null(crossBundle.Manifest.PreclinicalQsp.EpigeneticCircuitId);

        // Explicit non-existent IDs also resolve cleanly to null
        SignedRegulatoryDossierBundle missingExplicit = await _service.CompileDossierAsync("patient_empty", new CompileRegulatoryDossierRequest
        {
            LotId = "missing_lot",
            DepotId = "missing_depot",
            CircuitId = "missing_circuit"
        }, "admin_check");
        Assert.False(missingExplicit.Manifest.IsSubmissionReady);

        // Case A: Invalid DNA Kd <= 0, Draft Lot, Blank PGX -> ElevatedRisk, Non-Pharmacogenomics result skipped, Draft/Blocked circuit fallback, Low-TTR depot, MitoticDrift multi-year
        await SeedUserAsync("patient_bad_qsp");
        await _dnaRepository.UpsertDnaRecordAsync(new PatientDigitalDnaRecord
        {
            Id = "dna_bad_kd",
            Username = "patient_bad_qsp",
            Title = "Bad Kd",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = "Pituitary",
            ActiveLigand = "E2",
            NanocarrierGeometry = "Nanotube",
            TargetAptamerKdNm = 0.0,
            ScaffoldFasta = "   ",
            GcPercentage = 44.0,
            ProvenanceSha256 = new string('1', 64),
            CreatedAtUtc = _clockNow
        });
        // Add non-Pharmacogenomics result (newer than PGX so loop inspects and skips non-PGX first)
        await _dnaRepository.UpsertResultAsync(new PatientResultRecord
        {
            Id = "res_endo_1",
            Username = "patient_bad_qsp",
            ResultType = "EndocrineKinetics",
            Title = "Endo",
            Status = "Optimal",
            Summary = "Endo only",
            CreatedAtUtc = _clockNow.AddMinutes(5)
        });
        await _dnaRepository.UpsertResultAsync(new PatientResultRecord
        {
            Id = "res_pgx_risk",
            Username = "patient_bad_qsp",
            ResultType = "Pharmacogenomics",
            Title = "PGX Risk",
            Status = "ElevatedRisk",
            Summary = "CYP alert",
            CreatedAtUtc = _clockNow
        });
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "depot_bad_dose",
            PatientId = "patient_bad_qsp",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 1500.0, // Excessive dose causes TTR failure
            DailyErosionRatePercent = 4.0,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow
        });
        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = "circ_draft",
            PatientId = "patient_bad_qsp",
            CircuitStatus = EpigeneticCircuitStatus.Draft,
            MinOffTargetSpecificityScore = 99.0,
            CreatedAtUtc = _clockNow
        }, []);
        await _multiYearRepository.SaveForecastRunAsync(new MultiYearHomeostasisRunRecord
        {
            RunId = "myh_drift",
            DepotId = "depot_bad_dose",
            PatientId = "patient_bad_qsp",
            DurabilityStatus = MultiYearDurabilityStatus.MitoticDriftAdvisory,
            ProjectedTenYearTtrPercent = 82.0,
            HorizonYears = 10,
            CreatedAtUtc = _clockNow
        }, []);
        // Seed draft lot
        await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot_draft",
            DepotId = "depot_bad_dose",
            PatientId = "patient_bad_qsp",
            OpentronsProtocolSha256 = new string('e', 64),
            LotStatus = FabricationLotStatus.Draft,
            CreatedAtUtc = _clockNow
        });

        SignedRegulatoryDossierBundle badQspBundle = await _service.CompileDossierAsync("patient_bad_qsp", null, "admin_check");
        Assert.False(badQspBundle.Manifest.IsSubmissionReady);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_DNA_BLUEPRINT" && !g.Passed);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_LOT_COA" && !g.Passed);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_PHARMACOGENOMICS" && !g.Passed);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_DEPOT_180D" && !g.Passed);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_EPIGENETIC_CIRCUIT" && !g.Passed);
        Assert.Contains(badQspBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_MULTIYEAR_HOMEOSTASIS" && !g.Passed);

        // Case B: Ready patient whose lot has neutralized antidote, thermal excursion (>42C), CARPA AE quarantine, local revocation
        SeededPatientContext sabCtx = await SeedCompleteReadyPatientAsync("patient_sabotaged", "Optimal");
        // Lower CFD specificity on Approved circuit
        await _circuitRepository.UpdateCircuitEvaluationAsync(sabCtx.CircuitId, EpigeneticCircuitStatus.Approved, 97.0, 0.45, 96.5, _clockNow, "sha", "sig", "kid");
        // Update multi-year to PermanentHomeostasisVerified with < 95.0 TTR
        await _multiYearRepository.SaveForecastRunAsync(new MultiYearHomeostasisRunRecord
        {
            RunId = "myh_low_ttr",
            DepotId = sabCtx.DepotId,
            PatientId = "patient_sabotaged",
            DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
            ProjectedTenYearTtrPercent = 91.2,
            HorizonYears = 10,
            CreatedAtUtc = _clockNow.AddMinutes(2)
        }, []);
        // Trigger Grade 4 pharmacovigilance AE (auto-quarantines lot!)
        await _pvService.RecordAdverseEventAsync(sabCtx.LotId, new RecordAdverseEventRequest
        {
            SeverityGrade = AdverseEventSeverityGrade.Grade4LifeThreatening,
            SymptomCategory = "CARPA Anaphylaxis",
            OnsetHoursPostImplant = 1.5,
            ClinicalNotes = "Severe hypersensitivity"
        }, "clinician_1");
        // Trigger thermal excursion > 42C (makes cold-chain unsafe)
        await _coldChainService.RecordThermalExcursionAsync(sabCtx.LotId, new RecordThermalExcursionRequest
        {
            StorageTier = ThermalStorageTier.ThermalExcursion,
            TemperatureCelsius = 48.0,
            DurationHours = 2.0,
            SensorId = "TEMP-01"
        }, "clinician_1");
        // Administer antidote (transitions antidote to Neutralized + creates local revocation)
        await _antidoteService.AdministerAntidoteAsync(sabCtx.LotId, new AdministerAntidoteRequest
        {
            DosageMg = sabCtx.RecommendedAntidoteMg,
            ClinicalReason = "Emergency neutralization"
        }, "clinician_1");

        SignedRegulatoryDossierBundle sabotagedBundle = await _service.CompileDossierAsync("patient_sabotaged", null, "admin_check");
        Assert.False(sabotagedBundle.Manifest.IsSubmissionReady);
        Assert.True(sabotagedBundle.Manifest.QualityCmc.LotRevoked);
        Assert.True(sabotagedBundle.Manifest.Compliance.PharmacovigilanceQuarantineTriggered);
        Assert.Contains(sabotagedBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_REVERSAL_ANTIDOTE" && !g.Passed);
        Assert.Contains(sabotagedBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_COLD_CHAIN_CUSTODY" && !g.Passed);
        Assert.Contains(sabotagedBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_EPIGENETIC_CIRCUIT" && !g.Passed);
        Assert.Contains(sabotagedBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_MULTIYEAR_HOMEOSTASIS" && !g.Passed);
        Assert.Contains(sabotagedBundle.Manifest.ReadinessGates, g => g.GateCode == "COMP_PHARMACOVIGILANCE" && !g.Passed);

        // Also test Federated revocation branch cleanly & tampered CoA branches
        await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot_fed_rev",
            DepotId = sabCtx.DepotId,
            PatientId = "patient_fed_rev",
            OpentronsProtocolSha256 = "sha_ok",
            LotStatus = FabricationLotStatus.Released,
            CertificatePayloadSha256 = new string('9', 64),
            CertificateSignatureBase64 = "bad",
            CertificateSignerKeyId = "kid",
            CreatedAtUtc = _clockNow
        });
        await _lotRepository.UpsertFederatedRevocationAsync(new FederatedLotRevocationRecord
        {
            LotId = "lot_fed_rev",
            PayloadSha256 = new string('9', 64),
            IssuerKeyId = "peer_1",
            IssuerPeerLabel = "Partner Peer",
            RevokedBy = "peer_admin",
            RevocationReason = "Upstream recall",
            RevokedAtUtc = _clockNow,
            FeedPayloadSha256 = new string('8', 64),
            FeedSignatureBase64 = "TUwtRFNBLTY1",
            ImportedAtUtc = _clockNow
        });
        SignedRegulatoryDossierBundle fedBundle = await _service.CompileDossierAsync("patient_fed_rev", new CompileRegulatoryDossierRequest { LotId = "lot_fed_rev" }, "admin_check");
        Assert.True(fedBundle.Manifest.QualityCmc.LotRevoked);
        Assert.Contains(fedBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_LOT_COA" && !g.Passed);

        // Exercise lot-linked DepotId belonging to a different patient (fallback to patient depots / null)
        await _lotRepository.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = "lot_cross_depot",
            DepotId = otherCtx.DepotId, // Belongs to patient_other!
            PatientId = "patient_empty",
            OpentronsProtocolSha256 = new string('e', 64),
            LotStatus = FabricationLotStatus.Draft,
            CreatedAtUtc = _clockNow
        });
        SignedRegulatoryDossierBundle crossDepotBundle = await _service.CompileDossierAsync("patient_empty", new CompileRegulatoryDossierRequest { LotId = "lot_cross_depot" }, "admin_check");
        Assert.Null(crossDepotBundle.Manifest.PreclinicalQsp.DepotId);

        // Exercise patient with ONLY non-PGX result + DepotFormulation without MultiYear run (and no lot)
        await _dnaRepository.UpsertResultAsync(new PatientResultRecord
        {
            Id = "res_endo_only",
            Username = "patient_empty",
            ResultType = "EndocrineKinetics",
            Title = "Endo Only",
            Status = "Optimal",
            Summary = "Non-PGX result",
            CreatedAtUtc = _clockNow
        });
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = "depot_empty_nomyh",
            PatientId = "patient_empty",
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow
        });
        SignedRegulatoryDossierBundle depotNoMyhBundle = await _service.CompileDossierAsync("patient_empty", null, "admin_check");
        Assert.Contains(depotNoMyhBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_PHARMACOGENOMICS" && !g.Passed);
        Assert.Contains(depotNoMyhBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_MULTIYEAR_HOMEOSTASIS" && !g.Passed);
        Assert.Equal("depot_empty_nomyh", depotNoMyhBundle.Manifest.PreclinicalQsp.DepotId);
    }

    /// <summary>
    /// Verifies remaining defensive branches via focused service collaborators:
    /// null antidote/stability/pharmacovigilance, tampered signature on antidote, unsafe custody chain on safe thermal potency,
    /// burst/TTR edge combinations, recall-recommended without quarantine, blank pseudonym, and degraded audit ledger.
    /// </summary>
    [Fact]
    public async Task CompileDossier_EdgeGateBranchesAndDegradedAuditLedger_FailClosedCorrectly()
    {
        // WHAT: Exercises edge-case mock responses that normal healthy workflows intentionally prevent.
        // WHY:  Guarantees 100.00% line and branch coverage on every fail-closed defensive guard in RegulatoryDossierService.
        // HOW:  Mocks specific collaborator services for one focused compilation covering those exact branches.
        SeededPatientContext ctx = await SeedCompleteReadyPatientAsync("patient_mock_edge", "Optimal");
        // Also seed DNA with valid FASTA and zero Kd (hits dna.TargetAptamerKdNm branch vs blank fasta)
        await _dnaRepository.UpsertDnaRecordAsync(new PatientDigitalDnaRecord
        {
            Id = "dna_zero_kd",
            Username = "patient_mock_edge",
            Title = "Zero Kd",
            ScaffoldFasta = "ATGCGCTA",
            TargetAptamerKdNm = 0.0,
            GcPercentage = 50.0,
            ProvenanceSha256 = new string('2', 64),
            CreatedAtUtc = _clockNow.AddHours(1)
        });

        var depotSvcMock = new Mock<ITimeReleaseDepotService>();
        depotSvcMock.Setup(s => s.Simulate180DayDepot(It.IsAny<TimeReleaseDepotParameters>()))
            .Returns(new TimeReleaseSimulationResult
            {
                TimeInTargetRangePercent = 98.0,
                BurstSuppressed = false // Passes TTR, fails BurstSuppressed!
            });

        var antiSvcMock = new Mock<IReversalAntidoteService>();
        antiSvcMock.Setup(s => s.GetLatestAntidoteReportAsync(ctx.LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReversalAntidotePairingReport
            {
                AntidoteId = "anti_tampered",
                PrimaryLotId = ctx.LotId,
                PatientId = "patient_mock_edge",
                Status = ReversalAntidoteStatus.ActiveStandby,
                SignatureVerified = false // ActiveStandby but invalid ML-DSA-65!
            });

        var coldMock = new Mock<IColdChainStabilityService>();
        coldMock.Setup(s => s.EvaluateLotStabilityAsync(ctx.LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotStabilityReport
            {
                LotId = ctx.LotId,
                PatientId = "patient_mock_edge",
                RemainingPotencyPercent = 98.0,
                IsSafeForImplantation = true,
                CustodyChainValid = false, // Thermal safe, custody broken!
                CustodyHandoffCount = 3
            });

        var pvMock = new Mock<IClinicalPharmacovigilanceService>();
        pvMock.Setup(s => s.EvaluateLotPharmacovigilanceAsync(ctx.LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotPharmacovigilanceReport
            {
                LotId = ctx.LotId,
                PatientId = "patient_mock_edge",
                Posture = PharmacovigilancePosture.Monitor,
                IsQuarantineTriggered = false,
                EmergencyRecallRecommended = true // Not quarantined yet, but recall recommended!
            });

        var auditMock = new Mock<IComplianceAuditService>();
        auditMock.Setup(a => a.PseudonymizeSubjectId(It.IsAny<string>())).Returns("   ");
        auditMock.Setup(a => a.VerifyLedgerIntegrity()).Returns(new AuditLedgerVerificationResult
        {
            IsValid = false,
            TotalRecordsVerified = 5,
            Message = "Sequence 4 hash mismatch",
            SigningKeyId = "key_err"
        });
        auditMock.Setup(a => a.LogAction(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new AuditRecord());

        RegulatoryDossierService edgeSvc = new(
            _dossierRepository,
            _dnaRepository,
            _depotRepository,
            depotSvcMock.Object,
            _lotRepository,
            _lotService,
            antiSvcMock.Object,
            coldMock.Object,
            pvMock.Object,
            _circuitRepository,
            _multiYearRepository,
            auditMock.Object,
            _keyManagementService,
            _cryptoProvider,
            _peerKeyRepository,
            _protector,
            () => _clockNow);

        SignedRegulatoryDossierBundle edgeBundle = await edgeSvc.CompileDossierAsync("patient_mock_edge", null, "admin_edge");
        Assert.False(edgeBundle.Manifest.IsSubmissionReady);
        Assert.False(edgeBundle.Manifest.Compliance.HipaaDeIdentificationVerified);
        Assert.Contains(edgeBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_REVERSAL_ANTIDOTE" && !g.Passed);
        Assert.Contains(edgeBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_COLD_CHAIN_CUSTODY" && !g.Passed);
        Assert.Contains(edgeBundle.Manifest.ReadinessGates, g => g.GateCode == "QSP_DEPOT_180D" && !g.Passed);
        Assert.Contains(edgeBundle.Manifest.ReadinessGates, g => g.GateCode == "COMP_PHARMACOVIGILANCE" && !g.Passed);
        Assert.Contains(edgeBundle.Manifest.ReadinessGates, g => g.GateCode == "COMP_AUDIT_LEDGER" && !g.Passed);

        // Second pass with null stability, null pharmacovigilance, and mocked Released lot carrying blank OpentronsProtocolSha256
        FabricationLotRecord releasedLot = (await _lotRepository.GetLotByIdAsync(ctx.LotId))!;
        var lotRepoMock = new Mock<IFabricationLotRepository>();
        lotRepoMock.Setup(r => r.GetLotByIdAsync(ctx.LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(releasedLot with { OpentronsProtocolSha256 = string.Empty });
        lotRepoMock.Setup(r => r.GetLotsByPatientIdAsync("patient_mock_edge", It.IsAny<CancellationToken>()))
            .ReturnsAsync([releasedLot with { OpentronsProtocolSha256 = string.Empty }]);
        lotRepoMock.Setup(r => r.GetQcChecksByLotAsync(ctx.LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await _lotRepository.GetQcChecksByLotAsync(ctx.LotId));
        var depotRepoMock = new Mock<IDepotFormulationRepository>();
        depotRepoMock.Setup(r => r.GetDepotFormulationByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepotFormulationRecord?)null);
        depotRepoMock.Setup(r => r.GetDepotsByPatientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        RegulatoryDossierService blankOtpSvc = new(
            _dossierRepository,
            _dnaRepository,
            depotRepoMock.Object,
            depotSvcMock.Object,
            lotRepoMock.Object,
            _lotService,
            antiSvcMock.Object,
            coldMock.Object,
            pvMock.Object,
            _circuitRepository,
            _multiYearRepository,
            auditMock.Object,
            _keyManagementService,
            _cryptoProvider,
            _peerKeyRepository,
            _protector,
            () => _clockNow);

        coldMock.Setup(s => s.EvaluateLotStabilityAsync(ctx.LotId, It.IsAny<CancellationToken>())).ReturnsAsync((LotStabilityReport?)null);
        pvMock.Setup(s => s.EvaluateLotPharmacovigilanceAsync(ctx.LotId, It.IsAny<CancellationToken>())).ReturnsAsync((LotPharmacovigilanceReport?)null);
        SignedRegulatoryDossierBundle nullSubBundle = await blankOtpSvc.CompileDossierAsync("patient_mock_edge", null, "admin_edge");
        Assert.Contains(nullSubBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_LOT_COA" && !g.Passed);
        Assert.Contains(nullSubBundle.Manifest.ReadinessGates, g => g.GateCode == "CMC_COLD_CHAIN_CUSTODY" && !g.Passed);
        Assert.Contains(nullSubBundle.Manifest.ReadinessGates, g => g.GateCode == "COMP_PHARMACOVIGILANCE" && !g.Passed);
    }

    /// <summary>
    /// Verifies cross-node ML-DSA-65 dossier verification across all 5 trust outcomes:
    /// <see cref="RegulatoryDossierTrustSource.LocalNode"/>, <see cref="RegulatoryDossierTrustSource.TrustedPeer"/>,
    /// <see cref="RegulatoryDossierTrustSource.RevokedSigner"/>, <see cref="RegulatoryDossierTrustSource.UntrustedSigner"/>,
    /// and <see cref="RegulatoryDossierTrustSource.InvalidSignature"/>.
    /// </summary>
    [Fact]
    public async Task VerifyDossierBundle_AllTrustAndTamperStates_ClassifyAccurately()
    {
        // WHAT: Tests hash tampering, invalid/blank signatures, local node key spoof, Ed25519 peer skip, TrustedPeer, RevokedSigner, and UntrustedSigner.
        // WHY:  guarantees multi-center clinical audit hubs accept only cryptographically valid, actively trusted ML-DSA-65 keys.
        // HOW:  Modifies a genuine compiled bundle across tamper cases, signs peer bundles using derived ML-DSA-65 keys, and registers/revokes peers.
        await SeedCompleteReadyPatientAsync("patient_verify", "Optimal");
        SignedRegulatoryDossierBundle localBundle = await _service.CompileDossierAsync("patient_verify", null, "admin_verify");

        // 1. Tampered manifest hash mismatch
        SignedRegulatoryDossierBundle hashTampered = localBundle with
        {
            Manifest = localBundle.Manifest with { SponsorName = "Tampered Sponsor" }
        };
        RegulatoryDossierVerificationReport hashErr = await _service.VerifyDossierBundleAsync(hashTampered, "verifier_1");
        Assert.False(hashErr.HashValid);
        Assert.False(hashErr.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.InvalidSignature, hashErr.TrustSource);

        // 2. Wrong signature algorithm / blank signature / blank key / invalid signature bytes
        RegulatoryDossierVerificationReport wrongAlg = await _service.VerifyDossierBundleAsync(
            localBundle with { SignatureAlgorithm = "Ed25519" }, "verifier_1");
        Assert.Equal(RegulatoryDossierTrustSource.InvalidSignature, wrongAlg.TrustSource);
        Assert.True(wrongAlg.HashValid);
        Assert.False(wrongAlg.SignatureValid);

        RegulatoryDossierVerificationReport blankSig = await _service.VerifyDossierBundleAsync(
            localBundle with { SignatureBase64 = "   " }, "verifier_1");
        Assert.Equal(RegulatoryDossierTrustSource.InvalidSignature, blankSig.TrustSource);

        RegulatoryDossierVerificationReport blankKey = await _service.VerifyDossierBundleAsync(
            localBundle with { SignerPublicKeyBase64 = " " }, "verifier_1");
        Assert.Equal(RegulatoryDossierTrustSource.InvalidSignature, blankKey.TrustSource);

        byte[] badSigBytes = Convert.FromBase64String(localBundle.SignatureBase64);
        badSigBytes[0] ^= 0xFF;
        RegulatoryDossierVerificationReport badSig = await _service.VerifyDossierBundleAsync(
            localBundle with { SignatureBase64 = Convert.ToBase64String(badSigBytes) }, "verifier_1");
        Assert.False(badSig.SignatureValid);
        Assert.Equal(RegulatoryDossierTrustSource.InvalidSignature, badSig.TrustSource);

        // 3. Valid signature by external derived ML-DSA-65 key (UntrustedSigner)
        byte[] peerSeed1 = SHA256.HashData(Encoding.UTF8.GetBytes("peer-lab-zurich-seed-1"));
        PqcKeyMaterial peerKeyMat = _cryptoProvider.DeriveSigningKeyPairFromSeed(peerSeed1);
        byte[] peerSeed2 = SHA256.HashData(Encoding.UTF8.GetBytes("peer-lab-tokyo-seed-2"));
        PqcKeyMaterial otherPeerKeyMat = _cryptoProvider.DeriveSigningKeyPairFromSeed(peerSeed2);

        JsonSerializerOptions canonicalOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        byte[] canonicalManifest = JsonSerializer.SerializeToUtf8Bytes(localBundle.Manifest, canonicalOpts);
        string manifestSha = Convert.ToHexStringLower(SHA256.HashData(canonicalManifest));
        PqcSignature peerSig = _cryptoProvider.SignData(canonicalManifest, peerKeyMat.PrivateKeyBase64!, peerKeyMat.KeyId);

        SignedRegulatoryDossierBundle peerBundle = localBundle with
        {
            PayloadSha256 = manifestSha,
            SignatureBase64 = peerSig.SignatureBase64,
            SignerKeyId = peerKeyMat.KeyId,
            SignerPublicKeyBase64 = peerKeyMat.PublicKeyBase64
        };

        // Also test spoofing local node KeyId with peer public key (keyId matches local, pubkey does not)
        PqcKeyMaterial localKey = _keyManagementService.GetNodeSigningKey();
        RegulatoryDossierVerificationReport spoofedLocal = await _service.VerifyDossierBundleAsync(
            peerBundle with { SignerKeyId = localKey.KeyId }, "verifier_1");
        Assert.False(spoofedLocal.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.UntrustedSigner, spoofedLocal.TrustSource);

        // Register an unrelated Ed25519 peer key + wrong ML-DSA-65 key + same KeyId wrong pubkey to cover peer loop branches
        await _peerKeyRepository.TryRegisterAsync(new PeerKeyRecord
        {
            KeyId = otherPeerKeyMat.KeyId,
            Label = "Ed25519 Unrelated",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = otherPeerKeyMat.PublicKeyBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = _clockNow
        });

        RegulatoryDossierVerificationReport untrusted = await _service.VerifyDossierBundleAsync(peerBundle, "verifier_1");
        Assert.True(untrusted.HashValid);
        Assert.True(untrusted.SignatureValid);
        Assert.False(untrusted.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.UntrustedSigner, untrusted.TrustSource);

        // Register a mismatched pubkey under same KeyId branch via mock peer registry
        var mismatchedPeerReg = new Mock<IPeerKeyRegistry>();
        mismatchedPeerReg.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PeerKeyRecord
            {
                KeyId = peerKeyMat.KeyId,
                Label = "Mismatched PubKey Peer",
                Algorithm = KeyAlgorithm.MlDsa65,
                PublicKeyBase64 = otherPeerKeyMat.PublicKeyBase64,
                RegisteredBy = "admin",
                RegisteredAtUtc = _clockNow
            }
        ]);
        RegulatoryDossierService mismatchSvc = BuildService(peerKeyRegistryOverride: mismatchedPeerReg.Object);
        RegulatoryDossierVerificationReport mismatchReport = await mismatchSvc.VerifyDossierBundleAsync(peerBundle, "verifier_1");
        Assert.Equal(RegulatoryDossierTrustSource.UntrustedSigner, mismatchReport.TrustSource);

        // Register peerKeyMat as active ML-DSA-65 trusted peer -> TrustedPeer
        await _peerKeyRepository.TryRegisterAsync(new PeerKeyRecord
        {
            KeyId = peerKeyMat.KeyId,
            Label = "Zurich Clinical Core",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = peerKeyMat.PublicKeyBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = _clockNow
        });
        RegulatoryDossierVerificationReport trustedPeer = await _service.VerifyDossierBundleAsync(peerBundle, "verifier_1");
        Assert.True(trustedPeer.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.TrustedPeer, trustedPeer.TrustSource);
        Assert.Equal("Zurich Clinical Core", trustedPeer.TrustedPeerLabel);

        // Revoke peer key -> RevokedSigner fail-closed
        await _peerKeyRepository.RevokeAsync(peerKeyMat.KeyId, "admin_sec", _clockNow.AddMinutes(1));
        RegulatoryDossierVerificationReport revokedPeer = await _service.VerifyDossierBundleAsync(peerBundle, "verifier_1");
        Assert.False(revokedPeer.IsTrusted);
        Assert.Equal(RegulatoryDossierTrustSource.RevokedSigner, revokedPeer.TrustSource);
        Assert.Equal("Zurich Clinical Core", revokedPeer.TrustedPeerLabel);
    }

    /// <summary>
    /// Cleans up pooled SQLite handles and removes the isolated temporary directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore transient cleanup file locks
            }
        }
    }

    /// <summary>
    /// Builds a configured <see cref="RegulatoryDossierService"/> instance with optional peer registry override.
    /// </summary>
    /// <param name="peerKeyRegistryOverride">Optional peer key registry override for edge testing.</param>
    /// <returns>Configured <see cref="RegulatoryDossierService"/> instance.</returns>
    private RegulatoryDossierService BuildService(IPeerKeyRegistry? peerKeyRegistryOverride = null) =>
        new(
            _dossierRepository,
            _dnaRepository,
            _depotRepository,
            _depotService,
            _lotRepository,
            _lotService,
            _antidoteService,
            _coldChainService,
            _pvService,
            _circuitRepository,
            _multiYearRepository,
            _auditService,
            _keyManagementService,
            _cryptoProvider,
            peerKeyRegistryOverride ?? _peerKeyRepository,
            _protector,
            () => _clockNow);

    /// <summary>
    /// Ensures a patient user exists in <c>weaver_users</c> to satisfy foreign keys on DNA and result tables.
    /// </summary>
    /// <param name="patientId">Patient username to insert if missing.</param>
    /// <returns>A task representing the asynchronous user provisioning operation.</returns>
    private async Task SeedUserAsync(string patientId)
    {
        if (await _userRepository.GetUserAsync(patientId) is not null)
        {
            return;
        }

        await _userRepository.TryCreateUserAsync(new UserAccount
        {
            Username = patientId,
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            PublicKeyBase64 = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0000000000",
            Algorithm = KeyAlgorithm.Ed25519,
            CreatedAtUtc = _clockNow
        });
    }

    /// <summary>
    /// Seeds a complete patient cohort dataset passing every single one of the 10 mandatory IND/IMPD gates.
    /// </summary>
    /// <param name="patientId">Target patient identifier.</param>
    /// <param name="pgxStatus">Pharmacogenomics status label (<c>Optimal</c>, <c>Complete</c>, or <c>Verified</c>).</param>
    /// <returns>Identifiers of seeded lot, depot, circuit, and rescue antidote dosage.</returns>
    private async Task<SeededPatientContext> SeedCompleteReadyPatientAsync(string patientId, string pgxStatus)
    {
        await SeedUserAsync(patientId);
        string tag = Guid.NewGuid().ToString("N")[..12];

        string dnaId = $"dna_{tag}";
        await _dnaRepository.UpsertDnaRecordAsync(new PatientDigitalDnaRecord
        {
            Id = dnaId,
            Username = patientId,
            Title = "6HB Pituitary E2 Nanocarrier",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = "Anterior Pituitary Gonadotrope",
            ActiveLigand = "17beta-Estradiol",
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
            TargetAptamerKdNm = 4.2,
            ScaffoldFasta = ">M13mp18\nATGGCGATCGATCGATCGATCGATCGATCGATCGATCGATCGATCG",
            GcPercentage = 50.0,
            ProvenanceSha256 = new string('a', 64),
            CreatedAtUtc = _clockNow
        });

        await _dnaRepository.UpsertResultAsync(new PatientResultRecord
        {
            Id = $"pgx_{tag}",
            Username = patientId,
            DnaRecordId = dnaId,
            ResultType = "Pharmacogenomics",
            Title = "CYP3A4 & Factor V Leiden Screen",
            Status = pgxStatus,
            Summary = "Optimal therapeutic profile with baseline clotting risk.",
            CreatedAtUtc = _clockNow
        });

        string depotId = $"depot_{tag}";
        await _depotRepository.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            CreatedAtUtc = _clockNow
        });

        string circuitId = $"circ_{tag}";
        await _circuitRepository.SaveCircuitAsync(new EpigeneticCircuitRecord
        {
            CircuitId = circuitId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = TargetTissueType.GonadalTarget,
            EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
            CircuitStatus = EpigeneticCircuitStatus.Approved,
            PredictedTtrPercent = 98.2,
            SeparatrixMargin = 0.46,
            MinOffTargetSpecificityScore = 99.1,
            CreatedAtUtc = _clockNow,
            ApprovedAtUtc = _clockNow
        }, []);

        await _multiYearRepository.SaveForecastRunAsync(new MultiYearHomeostasisRunRecord
        {
            RunId = $"myh_{tag}",
            DepotId = depotId,
            CircuitId = circuitId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            HorizonYears = 10,
            MitoticFidelityRate = 0.9988,
            TransducedProgenitorFraction = 0.88,
            ProjectedTenYearTtrPercent = 98.5,
            DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
            CreatedAtUtc = _clockNow
        }, []);

        string lotId = $"lot_{tag}";
        string protocolSha = _lotService.ComputeOpentronsProtocolSha256(
            depotId, patientId, TherapeuticRegimen.MaleToFemale, "PLGA-PEG-PLGA", 16.0, 0.55);

        FabricationLotRecord draftLot = new()
        {
            LotId = lotId,
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 16.0,
            TargetErosionRatePercent = 0.55,
            CpGMethylationRequired = true,
            OpentronsProtocolSha256 = protocolSha,
            LotStatus = FabricationLotStatus.Draft,
            CreatedAtUtc = _clockNow
        };
        await _lotRepository.UpsertLotAsync(draftLot);

        FabricationQcCheckRecord[] qcChecks =
        [
            new() { CheckId = $"qc0_{tag}", LotId = lotId, PatientId = patientId, GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 92.5, ThresholdValue = 85.0, Passed = true, RecordedAtUtc = _clockNow },
            new() { CheckId = $"qc1_{tag}", LotId = lotId, PatientId = patientId, GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.12, ThresholdValue = 0.5, Passed = true, RecordedAtUtc = _clockNow },
            new() { CheckId = $"qc2_{tag}", LotId = lotId, PatientId = patientId, GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 54.0, SecondaryValue = 0.11, ThresholdValue = 120.0, Passed = true, RecordedAtUtc = _clockNow },
            new() { CheckId = $"qc3_{tag}", LotId = lotId, PatientId = patientId, GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 91.0, SecondaryValue = 1.2, ThresholdValue = 80.0, Passed = true, RecordedAtUtc = _clockNow },
            new() { CheckId = $"qc4_{tag}", LotId = lotId, PatientId = patientId, GateType = FabricationQcGateType.CpGMethylation, MeasuredValue = 97.5, ThresholdValue = 95.0, Passed = true, RecordedAtUtc = _clockNow }
        ];
        for (int i = 0; i < qcChecks.Length; i++)
        {
            await _lotRepository.RecordQcCheckAsync(qcChecks[i]);
        }

        FabricationLotReleaseEvaluation releaseEval = _lotService.EvaluateAndSignLotRelease(draftLot, qcChecks);
        await _lotRepository.UpsertLotAsync(draftLot with
        {
            LotStatus = FabricationLotStatus.Released,
            CertificatePayloadSha256 = releaseEval.Certificate!.PayloadSha256,
            CertificateSignatureBase64 = releaseEval.Certificate.SignatureBase64,
            CertificateSignerKeyId = releaseEval.Certificate.SignerKeyId,
            ReleasedAtUtc = releaseEval.Certificate.IssuedAtUtc
        });

        ReversalAntidotePairingReport antidoteReport = (await _antidoteService.PairAntidoteAsync(lotId, null, "clinician_pair"))!;

        await _coldChainService.RecordCustodyHandoffAsync(lotId, new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.WetLabRelease,
            FromActor = "wetlab_bot",
            ToActor = "pharmacy_vault",
            FacilityCode = "LAB-01",
            TemperatureCelsius = -20.0
        }, "clinician_cold");

        return new SeededPatientContext(lotId, depotId, circuitId, antidoteReport.Recipe.RecommendedAntidoteMassMg);
    }

    /// <summary>
    /// Internal carrier returning primary entity identifiers created during test seeding.
    /// </summary>
    /// <param name="LotId">Seeded fabrication lot identifier.</param>
    /// <param name="DepotId">Seeded depot formulation identifier.</param>
    /// <param name="CircuitId">Seeded epigenetic circuit identifier.</param>
    /// <param name="RecommendedAntidoteMg">Calculated rescue antidote dose in milligrams.</param>
    private sealed record SeededPatientContext(string LotId, string DepotId, string CircuitId, double RecommendedAntidoteMg);
}
