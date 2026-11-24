using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Endocrine;

/// <summary>
/// Unit tests verifying <see cref="EpigeneticCircuitService"/> deterministic bistable Hill phase-plane physics,
/// MtF/FtM endocrine steady states, CFD &lt; 98.0% fail-closed rejection, monostable collapse blocking,
/// ML-DSA-65 cryptographic sign-off &amp; verification, AES-256-GCM note encryption, and validation guards.
/// </summary>
public sealed class EpigeneticCircuitServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary test root holding SQLite database and node cryptographic keys.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Persistent database connection factory for isolated integration testing.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Epigenetic circuit repository under test.
    /// </summary>
    private readonly EpigeneticCircuitRepository _repository;

    /// <summary>
    /// AES-256-GCM protector for clinician notes at rest.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Node ML-DSA-65 key management service.
    /// </summary>
    private readonly KeyManagementService _keyManagementService;

    /// <summary>
    /// Post-quantum cryptographic verification provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// 21 CFR Part 11 compliance audit service.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Service instance under test with deterministic fixed UTC clock.
    /// </summary>
    private readonly EpigeneticCircuitService _service;

    /// <summary>
    /// Fixed deterministic UTC timestamp used across signing and round-trip assertions.
    /// </summary>
    private DateTimeOffset _clockNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpigeneticCircuitServiceTests"/> class.
    /// </summary>
    public EpigeneticCircuitServiceTests()
    {
        // WHAT: Provisions an isolated SQLite DB, PQC key store, compliance ledger, and EpigeneticCircuitService.
        // WHY:  Enables deterministic 100%-reproducible verification of bistable physics and ML-DSA-65 approvals.
        // HOW:  Initializes secret store, protector, PQC keys, WeaverTestDb, and injectable UTC clock delegate.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_epi_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _clockNow = new DateTimeOffset(2026, 11, 23, 15, 30, 0, TimeSpan.Zero);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty,
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();

        _factory = WeaverTestDb.CreateFactory(config);
        _repository = new EpigeneticCircuitRepository(_factory);

        NodeSecretStore secretStore = new(config);
        _protector = new ProfileDataProtector(secretStore);
        _cryptoProvider = new PostQuantumCryptoProvider();
        SubjectKeyRepository subjectKeyRepo = new(_factory);
        SubjectErasureJournal journal = new(secretStore);
        SubjectKeyVault vault = new(subjectKeyRepo, _protector, _cryptoProvider, journal);
        vault.InitializeAsync().GetAwaiter().GetResult();
        _keyManagementService = new KeyManagementService(_cryptoProvider, vault, secretStore);
        _auditService = new ComplianceAuditService(_keyManagementService, _cryptoProvider);

        _service = new EpigeneticCircuitService(
            _repository,
            _protector,
            _keyManagementService,
            _cryptoProvider,
            _auditService,
            () => _clockNow);
    }

    /// <summary>
    /// Verifies constructor null guards and argument validation branches across all public service methods.
    /// </summary>
    [Fact]
    public async Task Service_ArgumentValidation_GuardsInvalidInputsDeterministically()
    {
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitService(null!, _protector, _keyManagementService, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitService(_repository, null!, _keyManagementService, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitService(_repository, _protector, null!, _cryptoProvider, _auditService));
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitService(_repository, _protector, _keyManagementService, null!, _auditService));
        Assert.Throws<ArgumentNullException>(() => new EpigeneticCircuitService(_repository, _protector, _keyManagementService, _cryptoProvider, null!));

        // Verify default clock constructor branch compiles and succeeds
        EpigeneticCircuitService defaultClockSvc = new(_repository, _protector, _keyManagementService, _cryptoProvider, _auditService);
        Assert.Empty(await defaultClockSvc.ListPatientCircuitsAsync("nonexistent-pat"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DesignCircuitAsync(null!, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1" }, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = " " }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = new string('p', 129) }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", Regimen = (TherapeuticRegimen)999 }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", TargetTissue = (TargetTissueType)999 }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", EffectorPlatform = (EpigeneticEffectorPlatform)999 }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", Notes = new string('n', 2001) }, "admin"));

        // Custom guides validation branches
        GuideRnaSpecInput[] tooManyGuides = new GuideRnaSpecInput[17];
        for (int i = 0; i < tooManyGuides.Length; i++)
        {
            tooManyGuides[i] = new GuideRnaSpecInput
            {
                TargetGeneSymbol = "CYP19A1",
                ModulationMode = GuideRnaModulationMode.Activate,
                ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
                PamSequence = "AGG",
                CfdSpecificityScore = 99.0,
                PredictedFoldChange = 3.0
            };
        }

        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", Guides = tooManyGuides }, "admin"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = "p1", Guides = [null!] }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = null!, ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = null!, PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = null!, CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = " ", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = new string('G', 33), ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ModulationMode = (GuideRnaModulationMode)999, ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "SHORTSEQ", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = new string('A', 25), PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCX", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "A", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGGGGGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 100.5, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = double.NaN, PredictedFoldChange = 2.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 0.0 }]
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DesignCircuitAsync(new DesignEpigeneticCircuitRequest
        {
            PatientId = "p1",
            Guides = [new GuideRnaSpecInput { TargetGeneSymbol = "CYP19A1", ProtospacerSequence = "GCTAGTCAGCTGATCGATCG", PamSequence = "AGG", CfdSpecificityScore = 99.0, PredictedFoldChange = 60.0 }]
        }, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ListPatientCircuitsAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.EvaluateCircuitAsync(" ", null, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.EvaluateCircuitAsync("c1", null, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.EvaluateCircuitAsync("c1", new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 0.1 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.EvaluateCircuitAsync("c1", new EvaluateEpigeneticCircuitRequest { EpigeneticStabilityFactor = 3.0 }, "admin"));
        Assert.Null(await _service.EvaluateCircuitAsync("missing-circ", null, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApproveCircuitAsync(" ", null, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApproveCircuitAsync("c1", null, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ApproveCircuitAsync("c1", new ApproveEpigeneticCircuitRequest { ClearanceMultiplier = 5.0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ApproveCircuitAsync("c1", new ApproveEpigeneticCircuitRequest { EpigeneticStabilityFactor = 0.05 }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApproveCircuitAsync("c1", new ApproveEpigeneticCircuitRequest { ReviewerNotes = new string('r', 2001) }, "admin"));
        Assert.Null(await _service.ApproveCircuitAsync("missing-circ", null, "admin"));

        Assert.Throws<ArgumentNullException>(() => _service.VerifyApprovalSignature(null!));

        // Cover nullable DTO example providers cleanly
        Assert.NotNull(new EpigeneticCircuitEvaluationReport().GetExamples());
        Assert.NotNull(new DesignEpigeneticCircuitRequest().GetExamples());
        Assert.NotNull(new EvaluateEpigeneticCircuitRequest().GetExamples());
        Assert.NotNull(new ApproveEpigeneticCircuitRequest().GetExamples());
    }

    /// <summary>
    /// Verifies canonical MtF and FtM epigenetic circuits achieve physiological steady states, bistable phase planes,
    /// encrypted notes, valid ML-DSA-65 approval signatures, and tamper detection.
    /// </summary>
    [Fact]
    public async Task DesignEvaluateAndApprove_CanonicalMtfAndFtmCircuits_ReachSteadyStatesAndVerifyMlDsa65()
    {
        // Canonical MtF CRISPRoff/on circuit with confidential clinician notes
        EpigeneticCircuitEvaluationReport mtf = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-mtf-01",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = TargetTissueType.GonadalTarget,
                EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
                Notes = "Baseline MtF aromatase switch with SRD5A2 CpG silencing."
            },
            "dr_lantea");

        Assert.Equal(EpigeneticCircuitStatus.Evaluated, mtf.CircuitStatus);
        Assert.True(mtf.IsSafeForPermanentSynthesis);
        Assert.True(mtf.PassesOffTargetGate);
        Assert.True(mtf.IsBistable);
        Assert.True(mtf.PassesBistabilityGate);
        Assert.True(mtf.PassesEndocrineSetpointGate);
        Assert.InRange(mtf.PredictedEstradiolPmolL, 350.0, 750.0);
        Assert.True(mtf.PredictedTestosteroneNmolL < 1.8);
        Assert.True(mtf.PredictedTtrPercent >= 85.0);
        Assert.Equal(4, mtf.Guides.Count);
        Assert.Equal(25, mtf.PhasePlane.EstrogenNullcline.Count);
        Assert.Equal(25, mtf.PhasePlane.AndrogenNullcline.Count);
        Assert.Equal(9, mtf.PhasePlane.SeparatrixCurve.Count);
        Assert.Equal("Baseline MtF aromatase switch with SRD5A2 CpG silencing.", mtf.DecryptedNotes);
        Assert.False(_service.VerifyApprovalSignature(mtf));

        // Verify persisted note is AES-256-GCM encrypted in SQLite
        EpigeneticCircuitRecord? storedRow = await _repository.GetCircuitByIdAsync(mtf.CircuitId);
        Assert.NotNull(storedRow);
        Assert.NotNull(storedRow.NotesEncrypted);
        Assert.NotEqual("Baseline MtF aromatase switch with SRD5A2 CpG silencing.", storedRow.NotesEncrypted);
        Assert.True(ProfileDataProtector.IsEncrypted(storedRow.NotesEncrypted));

        // Evaluate with explicit request (DCas9Dnmt3aTet1 FtM with null notes too)
        _clockNow = _clockNow.AddMinutes(5);
        EpigeneticCircuitEvaluationReport? reeval = await _service.EvaluateCircuitAsync(
            mtf.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 1.0, EpigeneticStabilityFactor = 1.0 },
            "dr_lantea");
        Assert.NotNull(reeval);
        Assert.Equal(EpigeneticCircuitStatus.Evaluated, reeval.CircuitStatus);

        // Approve with ML-DSA-65
        _clockNow = _clockNow.AddMinutes(5);
        EpigeneticCircuitEvaluationReport? approvedMtf = await _service.ApproveCircuitAsync(
            mtf.CircuitId,
            new ApproveEpigeneticCircuitRequest { ReviewerNotes = "Approved for lipid-aptamer fabrication." },
            "dr_lantea");
        Assert.NotNull(approvedMtf);
        Assert.Equal(EpigeneticCircuitStatus.Approved, approvedMtf.CircuitStatus);
        Assert.Equal("ML-DSA-65", approvedMtf.SignatureAlgorithm);
        Assert.NotNull(approvedMtf.ApprovalPayloadSha256);
        Assert.NotNull(approvedMtf.ApprovalSignatureBase64);
        Assert.NotNull(approvedMtf.ApprovalSignerPublicKeyBase64);
        Assert.True(_service.VerifyApprovalSignature(approvedMtf));

        // Tamper detection checks (sha mismatch, signature mismatch, missing signature fields)
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { PredictedEstradiolPmolL = 999.0 }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovalPayloadSha256 = new string('0', 64) }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovalSignatureBase64 = "aW52YWxpZFNpZ25hdHVyZQ==" }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovalSignerPublicKeyBase64 = " " }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovalSignatureBase64 = " " }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovalPayloadSha256 = " " }));
        Assert.False(_service.VerifyApprovalSignature(approvedMtf with { ApprovedAtUtc = null }));

        // Canonical FtM circuit using DCas9Dnmt3aTet1 with null Notes
        _clockNow = _clockNow.AddMinutes(10);
        EpigeneticCircuitEvaluationReport ftm = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-mtf-01",
                Regimen = TherapeuticRegimen.FemaleToMale,
                TargetTissue = TargetTissueType.AdrenalSteroidogenic,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1,
                Notes = null
            },
            "dr_lantea");
        Assert.Equal(EpigeneticCircuitStatus.Evaluated, ftm.CircuitStatus);
        Assert.True(ftm.IsSafeForPermanentSynthesis);
        Assert.InRange(ftm.PredictedTestosteroneNmolL, 12.0, 28.0);
        Assert.True(ftm.PredictedEstradiolPmolL < 150.0);
        Assert.Equal(3, ftm.Guides.Count);
        Assert.Null(ftm.DecryptedNotes);

        // Evaluate FtM with null request
        Assert.NotNull(await _service.EvaluateCircuitAsync(ftm.CircuitId, null, "dr_lantea"));

        // Verify ListPatientCircuitsAsync returns both chronological circuits with Approved carrying public key
        IReadOnlyList<EpigeneticCircuitEvaluationReport> listed = await _service.ListPatientCircuitsAsync("pat-mtf-01");
        Assert.Equal(2, listed.Count);
        Assert.Equal(EpigeneticCircuitStatus.Approved, listed[0].CircuitStatus);
        Assert.True(_service.VerifyApprovalSignature(listed[0]));
        Assert.Equal(EpigeneticCircuitStatus.Evaluated, listed[1].CircuitStatus);
        Assert.Null(listed[1].ApprovalSignerPublicKeyBase64);
    }

    /// <summary>
    /// Verifies fail-closed rejection across CFD off-target &lt; 98.0%, weak-basin separatrix &lt; 0.25,
    /// monostable collapse, activation-only / repression-only Custom circuits, and high-clearance setpoint failure.
    /// </summary>
    [Fact]
    public async Task SafetyGates_FailClosed_WhenOffTargetRiskBistabilityOrSetpointFails()
    {
        // 1. Custom guides with CFD specificity 96.4% (< 98.0%) on DCas9KrabVp64
        EpigeneticCircuitEvaluationReport lowCfd = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.Custom,
                TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9KrabVp64,
                Guides =
                [
                    new GuideRnaSpecInput
                    {
                        TargetGeneSymbol = "ESR1",
                        ModulationMode = GuideRnaModulationMode.Activate,
                        ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
                        PamSequence = "AGG",
                        CfdSpecificityScore = 99.1,
                        PredictedFoldChange = 3.8
                    },
                    new GuideRnaSpecInput
                    {
                        TargetGeneSymbol = "AR",
                        ModulationMode = GuideRnaModulationMode.Repress,
                        ProtospacerSequence = "CGATCGATGCTAGCTAGCTA",
                        PamSequence = "CGG",
                        CfdSpecificityScore = 96.4,
                        PredictedFoldChange = 0.18
                    }
                ]
            },
            "researcher_1");

        Assert.Equal(EpigeneticCircuitStatus.Blocked, lowCfd.CircuitStatus);
        Assert.False(lowCfd.PassesOffTargetGate);
        Assert.True(lowCfd.PassesBistabilityGate);
        Assert.False(lowCfd.IsSafeForPermanentSynthesis);
        Assert.Single(lowCfd.FailureReasons);

        // Attempting Approve MUST stay Blocked with null signature (fail-closed)
        EpigeneticCircuitEvaluationReport? blockedApprove = await _service.ApproveCircuitAsync(lowCfd.CircuitId, null, "admin");
        Assert.NotNull(blockedApprove);
        Assert.Equal(EpigeneticCircuitStatus.Blocked, blockedApprove.CircuitStatus);
        Assert.Null(blockedApprove.ApprovalSignatureBase64);

        // 2. Monostable collapse: Activation-only custom guides favoring androgen/unknown gene -> no Repress -> monostable
        EpigeneticCircuitEvaluationReport activateOnly = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.Custom,
                TargetTissue = TargetTissueType.PeripheralReceptor,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1,
                Guides =
                [
                    new GuideRnaSpecInput
                    {
                        TargetGeneSymbol = "CYP11A1",
                        ModulationMode = GuideRnaModulationMode.Activate,
                        ProtospacerSequence = "TAGCTAGCTGATCGATCGAT",
                        PamSequence = "GGG",
                        CfdSpecificityScore = 99.2,
                        PredictedFoldChange = 4.0
                    }
                ]
            },
            "researcher_1");

        Assert.Equal(EpigeneticCircuitStatus.Blocked, activateOnly.CircuitStatus);
        Assert.False(activateOnly.IsBistable);
        Assert.False(activateOnly.PassesBistabilityGate);
        Assert.False(activateOnly.PassesEndocrineSetpointGate);
        Assert.Empty(activateOnly.PhasePlane.SeparatrixCurve);

        // 3. Repression-only guides (no Activate) also cover false branch of activateCount > 0
        EpigeneticCircuitEvaluationReport repressOnly = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = TargetTissueType.GonadalTarget,
                EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
                Guides =
                [
                    new GuideRnaSpecInput
                    {
                        TargetGeneSymbol = "SRD5A2",
                        ModulationMode = GuideRnaModulationMode.Repress,
                        ProtospacerSequence = "CGATCGATGCTAGCTAGCTA",
                        PamSequence = "CGG",
                        CfdSpecificityScore = 99.0,
                        PredictedFoldChange = 0.2
                    }
                ]
            },
            "researcher_1");
        Assert.False(repressOnly.IsBistable);

        // 4. Safe baseline DCas9Dnmt3aTet1 MtF circuit stressed under 0.72 stability (bistable=true, margin < 0.25) and 3.5 clearance
        EpigeneticCircuitEvaluationReport shallowSeparatrix = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = TargetTissueType.GonadalTarget,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1
            },
            "admin");

        EpigeneticCircuitEvaluationReport? weakMargin = await _service.EvaluateCircuitAsync(
            shallowSeparatrix.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 1.0, EpigeneticStabilityFactor = 0.72 },
            "admin");
        Assert.NotNull(weakMargin);
        Assert.True(weakMargin.IsBistable);
        Assert.True(weakMargin.SeparatrixMargin < EpigeneticSafetyThresholds.MinimumSeparatrixMargin);
        Assert.False(weakMargin.PassesBistabilityGate);
        Assert.Equal(EpigeneticCircuitStatus.Blocked, weakMargin.CircuitStatus);

        // 5. High/low metabolic clearance & androgen-leakage setpoint failures across both MtF and FtM
        EpigeneticCircuitEvaluationReport? clearanceFail = await _service.EvaluateCircuitAsync(
            shallowSeparatrix.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 3.5, EpigeneticStabilityFactor = 1.0 },
            "admin");
        Assert.NotNull(clearanceFail);
        Assert.True(clearanceFail.PassesBistabilityGate);
        Assert.False(clearanceFail.PassesEndocrineSetpointGate);
        Assert.Equal(EpigeneticCircuitStatus.Blocked, clearanceFail.CircuitStatus);

        // MtF E2 > 750 pmol/L (low clearance)
        EpigeneticCircuitEvaluationReport? mtfHighE2 = await _service.EvaluateCircuitAsync(
            shallowSeparatrix.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 0.25, EpigeneticStabilityFactor = 1.0 },
            "admin");
        Assert.NotNull(mtfHighE2);
        Assert.True(mtfHighE2.PredictedEstradiolPmolL > EpigeneticSafetyThresholds.MtFMaxEstradiolPmolL);
        Assert.False(mtfHighE2.PassesEndocrineSetpointGate);

        // MtF with DCas9KrabVp64 at clearance 0.62 -> E2 in [350, 750] but T >= 1.8 nmol/L
        EpigeneticCircuitEvaluationReport krabMtf = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = TargetTissueType.GonadalTarget,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9KrabVp64
            },
            "admin");
        EpigeneticCircuitEvaluationReport? mtfHighT = await _service.EvaluateCircuitAsync(
            krabMtf.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 0.62, EpigeneticStabilityFactor = 1.0 },
            "admin");
        Assert.NotNull(mtfHighT);
        Assert.InRange(mtfHighT.PredictedEstradiolPmolL, EpigeneticSafetyThresholds.MtFMinEstradiolPmolL, EpigeneticSafetyThresholds.MtFMaxEstradiolPmolL);
        Assert.True(mtfHighT.PredictedTestosteroneNmolL >= EpigeneticSafetyThresholds.MtFMaxTestosteroneNmolL);
        Assert.False(mtfHighT.PassesEndocrineSetpointGate);

        // FtM setpoint out-of-range branches: T < 12, T > 28, and T in [12, 28] with E2 >= 150
        EpigeneticCircuitEvaluationReport ftmBase = await _service.DesignCircuitAsync(
            new DesignEpigeneticCircuitRequest
            {
                PatientId = "pat-fail-01",
                Regimen = TherapeuticRegimen.FemaleToMale,
                TargetTissue = TargetTissueType.GonadalTarget,
                EffectorPlatform = EpigeneticEffectorPlatform.DCas9Dnmt3aTet1
            },
            "admin");
        EpigeneticCircuitEvaluationReport? ftmLowT = await _service.EvaluateCircuitAsync(
            ftmBase.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 3.5, EpigeneticStabilityFactor = 1.0 },
            "admin");
        Assert.NotNull(ftmLowT);
        Assert.True(ftmLowT.PredictedTestosteroneNmolL < EpigeneticSafetyThresholds.FtMMinTestosteroneNmolL);
        Assert.False(ftmLowT.PassesEndocrineSetpointGate);

        EpigeneticCircuitEvaluationReport? ftmHighT = await _service.EvaluateCircuitAsync(
            ftmBase.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 0.3, EpigeneticStabilityFactor = 1.0 },
            "admin");
        Assert.NotNull(ftmHighT);
        Assert.True(ftmHighT.PredictedTestosteroneNmolL > EpigeneticSafetyThresholds.FtMMaxTestosteroneNmolL);
        Assert.False(ftmHighT.PassesEndocrineSetpointGate);

        EpigeneticCircuitEvaluationReport? ftmHighE2 = await _service.EvaluateCircuitAsync(
            ftmBase.CircuitId,
            new EvaluateEpigeneticCircuitRequest { ClearanceMultiplier = 0.55, EpigeneticStabilityFactor = 0.75 },
            "admin");
        Assert.NotNull(ftmHighE2);
        Assert.InRange(ftmHighE2.PredictedTestosteroneNmolL, EpigeneticSafetyThresholds.FtMMinTestosteroneNmolL, EpigeneticSafetyThresholds.FtMMaxTestosteroneNmolL);
        Assert.True(ftmHighE2.PredictedEstradiolPmolL >= EpigeneticSafetyThresholds.FtMMaxEstradiolPmolL);
        Assert.False(ftmHighE2.PassesEndocrineSetpointGate);

        // Verify 21 CFR Part 11 audit ledger recorded DESIGN, EVALUATE, and APPROVE actions
        IReadOnlyList<AuditRecord> entries = _auditService.GetAuditRecords(100);
        Assert.Contains(entries, e => e.Action == "EPIGENETIC_CIRCUIT_DESIGN");
        Assert.Contains(entries, e => e.Action == "EPIGENETIC_CIRCUIT_EVALUATE");
        Assert.Contains(entries, e => e.Action == "EPIGENETIC_CIRCUIT_APPROVE");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
