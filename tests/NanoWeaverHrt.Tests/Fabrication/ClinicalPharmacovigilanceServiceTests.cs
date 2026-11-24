using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit tests verifying <see cref="ClinicalPharmacovigilanceService"/> CTCAE v5.0 grading,
/// CARPA/TLR9/humoral biomarker signal detection, AES-256-GCM note encryption, automatic lot quarantine,
/// Certificate of Analysis pharmacovigilance gating, and 21 CFR Part 11 audit trails.
/// </summary>
public sealed class ClinicalPharmacovigilanceServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite database file and PHI key store.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Depot formulation repository for seeding parent formulations.
    /// </summary>
    private readonly DepotFormulationRepository _depotRepo;

    /// <summary>
    /// Fabrication lot repository for seeding lots and verifying quarantine transitions.
    /// </summary>
    private readonly FabricationLotRepository _lotRepo;

    /// <summary>
    /// Pharmacovigilance persistence repository.
    /// </summary>
    private readonly ClinicalPharmacovigilanceRepository _pvRepo;

    /// <summary>
    /// AES-256-GCM field protector for clinical notes.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Mock compliance audit service verifying 21 CFR Part 11 event emissions.
    /// </summary>
    private readonly Mock<IComplianceAuditService> _auditMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClinicalPharmacovigilanceServiceTests"/> class.
    /// </summary>
    public ClinicalPharmacovigilanceServiceTests()
    {
        // WHAT: Provisions an isolated SQLite database with all 20 migrations and a dedicated ProfileDataProtector.
        // WHY:  Tests genuine multi-table persistence, AES-256-GCM note encryption, and quarantine transitions deterministically.
        // HOW:  Allocates a unique temporary directory and constructs real repositories backed by WeaverTestDb.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_pv_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty,
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir
            })
            .Build();
        _factory = WeaverTestDb.CreateFactory(config);
        _depotRepo = new DepotFormulationRepository(_factory);
        _lotRepo = new FabricationLotRepository(_factory);
        _pvRepo = new ClinicalPharmacovigilanceRepository(_factory);
        _protector = new ProfileDataProtector(new NodeSecretStore(config));
    }

    /// <summary>
    /// Verifies OpenAPI example providers across all Milestone 41 pharmacovigilance DTOs.
    /// </summary>
    [Fact]
    public void ApiDtos_GetExamples_ReturnValidPopulatedSamples()
    {
        LotAdverseEventSummary evSummary = new LotAdverseEventSummary().GetExamples();
        Assert.Equal("aevt_20261123_001", evSummary.EventId);
        Assert.Equal(AdverseEventSeverityGrade.Grade1Mild, evSummary.SeverityGrade);

        LotPharmacovigilanceReport report = new LotPharmacovigilanceReport().GetExamples();
        Assert.Equal("lot_20261123_001", report.LotId);
        Assert.Equal(PharmacovigilancePosture.Nominal, report.Posture);
        Assert.False(report.IsQuarantineTriggered);

        RecordAdverseEventRequest aeReq = new RecordAdverseEventRequest().GetExamples();
        Assert.Equal("InjectionSiteErythema", aeReq.SymptomCategory);

        AdverseEventResponse aeResp = new AdverseEventResponse().GetExamples();
        Assert.Equal(evSummary.EventId, aeResp.Event.EventId);

        RecordImmunogenicityAssayRequest immReq = new RecordImmunogenicityAssayRequest().GetExamples();
        Assert.Equal(96.0, immReq.Ch50Percent);

        ImmunogenicityAssayResponse immResp = new ImmunogenicityAssayResponse().GetExamples();
        Assert.Equal("immu_20261123_001", immResp.Assay.AssayId);
        Assert.False(immResp.ReportAfterAssay.IsQuarantineTriggered);
    }

    /// <summary>
    /// Verifies constructor null guards, method argument guards, validation errors, and missing-lot null returns.
    /// </summary>
    [Fact]
    public async Task ServiceGuardsAndMissingLot_EnforceValidationBoundaries()
    {
        Assert.Throws<ArgumentNullException>(() => new ClinicalPharmacovigilanceService(null!, _pvRepo, _protector, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ClinicalPharmacovigilanceService(_lotRepo, null!, _protector, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ClinicalPharmacovigilanceService(_lotRepo, _pvRepo, null!, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ClinicalPharmacovigilanceService(_lotRepo, _pvRepo, _protector, null!));

        ClinicalPharmacovigilanceService svc = new(_lotRepo, _pvRepo, _protector, _auditMock.Object);
        RecordAdverseEventRequest validAe = new RecordAdverseEventRequest().GetExamples();
        RecordImmunogenicityAssayRequest validAssay = new RecordImmunogenicityAssayRequest().GetExamples();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync(" ", validAe, "op"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.RecordAdverseEventAsync("lot-1", null!, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { SeverityGrade = (AdverseEventSeverityGrade)99 }, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { SymptomCategory = null! }, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { SymptomCategory = "   " }, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { SymptomCategory = new string('x', 129) }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { OnsetHoursPostImplant = double.NaN }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { OnsetHoursPostImplant = -0.1 }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { OnsetHoursPostImplant = 87_601.0 }, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordAdverseEventAsync("lot-1", validAe with { ClinicalNotes = new string('n', 2_001) }, "op"));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ListAdverseEventsAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.EvaluateLotPharmacovigilanceAsync(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.ApplyPharmacovigilanceGateAsync(null!));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordImmunogenicityAssayAsync(" ", validAssay, "op"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", null!, "op"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", validAssay, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", validAssay with { Ch50Percent = -1.0 }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", validAssay with { Ch50Percent = 201.0 }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", validAssay with { C3aNgMl = double.PositiveInfinity }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordImmunogenicityAssayAsync("lot-1", validAssay with { Tlr9ActivationIndex = 101.0 }, "op"));

        Assert.Null(await svc.RecordAdverseEventAsync("missing-lot", validAe, "op"));
        Assert.Null(await svc.ListAdverseEventsAsync("missing-lot"));
        Assert.Null(await svc.RecordImmunogenicityAssayAsync("missing-lot", validAssay, "op"));
        Assert.Null(await svc.EvaluateLotPharmacovigilanceAsync("missing-lot"));
    }

    /// <summary>
    /// Verifies adverse event AES-256-GCM encryption at rest, Grade 1–2 monitoring, Grade 3–4 auto-quarantine,
    /// preservation of <see cref="FabricationLotStatus.Revoked"/> tombstones, and 21 CFR Part 11 audit events.
    /// </summary>
    [Fact]
    public async Task AdverseEvents_EncryptionMonitoringAndAutoQuarantine_WorkDeterministically()
    {
        DateTimeOffset fixedClock = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        ClinicalPharmacovigilanceService svc = new(_lotRepo, _pvRepo, _protector, _auditMock.Object, () => fixedClock);

        await SeedLotAsync("lot-pv-ae", "pat-pv-ae", FabricationLotStatus.Released, fixedClock);

        // Baseline nominal lot
        LotPharmacovigilanceReport? baseline = await svc.EvaluateLotPharmacovigilanceAsync("lot-pv-ae");
        Assert.NotNull(baseline);
        Assert.Equal(PharmacovigilancePosture.Nominal, baseline.Posture);
        Assert.Null(baseline.HighestSeverityGrade);
        Assert.False(baseline.IsQuarantineTriggered);

        // Grade 2 moderate with encrypted notes -> Monitor, lot remains Released
        AdverseEventResponse? r2 = await svc.RecordAdverseEventAsync(
            "lot-pv-ae",
            new RecordAdverseEventRequest
            {
                SeverityGrade = AdverseEventSeverityGrade.Grade2Moderate,
                SymptomCategory = "SubcutaneousPruritus",
                OnsetHoursPostImplant = 6.25,
                ClinicalNotes = "Localized pruritus; oral antihistamine administered."
            },
            "dr_lin");
        Assert.NotNull(r2);
        Assert.Equal(PharmacovigilancePosture.Monitor, r2.ReportAfterEvent.Posture);
        Assert.False(r2.ReportAfterEvent.IsQuarantineTriggered);
        Assert.Equal(FabricationLotStatus.Released, r2.ReportAfterEvent.LotStatus);
        Assert.Equal("Localized pruritus; oral antihistamine administered.", r2.Event.ClinicalNotes);

        // Verify stored ciphertext differs from plaintext in DB
        IReadOnlyList<LotAdverseEventRecord> rawRows = await _pvRepo.ListAdverseEventsByLotAsync("lot-pv-ae");
        Assert.Single(rawRows);
        Assert.NotNull(rawRows[0].ClinicalNotesEncrypted);
        Assert.NotEqual("Localized pruritus; oral antihistamine administered.", rawRows[0].ClinicalNotesEncrypted);

        // Grade 1 mild with null notes -> HighestSeverityGrade stays Grade2Moderate
        AdverseEventResponse? r1 = await svc.RecordAdverseEventAsync(
            "lot-pv-ae",
            new RecordAdverseEventRequest
            {
                SeverityGrade = AdverseEventSeverityGrade.Grade1Mild,
                SymptomCategory = "InjectionSiteWarmth",
                OnsetHoursPostImplant = 8.0,
                ClinicalNotes = "   ",
                RecordedAtUtc = fixedClock.AddHours(1)
            },
            "rn_sam");
        Assert.NotNull(r1);
        Assert.Null(r1.Event.ClinicalNotes);
        Assert.Equal(AdverseEventSeverityGrade.Grade2Moderate, r1.ReportAfterEvent.HighestSeverityGrade);

        // Grade 3 severe -> CarpaQuarantine & transitions Released -> Quarantined
        AdverseEventResponse? r3 = await svc.RecordAdverseEventAsync(
            "lot-pv-ae",
            new RecordAdverseEventRequest
            {
                SeverityGrade = AdverseEventSeverityGrade.Grade3Severe,
                SymptomCategory = "SystemicFlushingDyspnea",
                OnsetHoursPostImplant = 10.5,
                ClinicalNotes = "Acute dyspnea requiring IV hydrocortisone.",
                RecordedAtUtc = fixedClock.AddHours(2)
            },
            "dr_lin");
        Assert.NotNull(r3);
        Assert.Equal(PharmacovigilancePosture.CarpaQuarantine, r3.ReportAfterEvent.Posture);
        Assert.True(r3.ReportAfterEvent.IsQuarantineTriggered);
        Assert.True(r3.ReportAfterEvent.EmergencyRecallRecommended);
        Assert.Equal(AdverseEventSeverityGrade.Grade3Severe, r3.ReportAfterEvent.HighestSeverityGrade);
        Assert.Equal(FabricationLotStatus.Quarantined, r3.ReportAfterEvent.LotStatus);

        FabricationLotRecord? updatedLot = await _lotRepo.GetLotByIdAsync("lot-pv-ae");
        Assert.Equal(FabricationLotStatus.Quarantined, updatedLot!.LotStatus);

        // Additional Grade 4 event when already Quarantined preserves Quarantined status cleanly
        AdverseEventResponse? r4 = await svc.RecordAdverseEventAsync(
            "lot-pv-ae",
            new RecordAdverseEventRequest
            {
                SeverityGrade = AdverseEventSeverityGrade.Grade4LifeThreatening,
                SymptomCategory = "AnaphylactoidHypotension",
                OnsetHoursPostImplant = 11.0,
                RecordedAtUtc = fixedClock.AddHours(3)
            },
            "dr_lin");
        Assert.NotNull(r4);
        Assert.Equal(AdverseEventSeverityGrade.Grade4LifeThreatening, r4.ReportAfterEvent.HighestSeverityGrade);
        Assert.Equal(FabricationLotStatus.Quarantined, r4.ReportAfterEvent.LotStatus);

        IReadOnlyList<LotAdverseEventSummary>? decryptedList = await svc.ListAdverseEventsAsync("lot-pv-ae");
        Assert.NotNull(decryptedList);
        Assert.Equal(4, decryptedList.Count);
        Assert.Equal("Localized pruritus; oral antihistamine administered.", decryptedList[0].ClinicalNotes);
        Assert.Null(decryptedList[1].ClinicalNotes);

        // Revoked lot must retain Revoked status even when Grade 3 event is recorded
        await SeedLotAsync("lot-pv-revoked", "pat-pv-revoked", FabricationLotStatus.Revoked, fixedClock);
        AdverseEventResponse? revokedResp = await svc.RecordAdverseEventAsync(
            "lot-pv-revoked",
            new RecordAdverseEventRequest
            {
                SeverityGrade = AdverseEventSeverityGrade.Grade3Severe,
                SymptomCategory = "LateHypersensitivity",
                OnsetHoursPostImplant = 24.0
            },
            "dr_lin");
        Assert.NotNull(revokedResp);
        Assert.Equal(FabricationLotStatus.Revoked, revokedResp.ReportAfterEvent.LotStatus);

        _auditMock.Verify(
            a => a.LogAction(It.IsAny<string>(), "FABRICATION_ADVERSE_EVENT_RECORD", It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(5));
        _auditMock.Verify(
            a => a.LogAction(It.IsAny<string>(), "FABRICATION_PHARMACOVIGILANCE_QUARANTINE", It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(3));
    }

    /// <summary>
    /// Verifies nominal, monitor, and critical CARPA / TLR9 / humoral immunogenicity thresholds, duplicate signal
    /// deduplication, and fail-closed Certificate of Analysis gating via <see cref="ClinicalPharmacovigilanceService.ApplyPharmacovigilanceGateAsync"/>.
    /// </summary>
    [Fact]
    public async Task ImmunogenicityAssaysAndCoAGate_DetectCarpaTlr9HumoralAndBlockUntrustedCoA()
    {
        DateTimeOffset t0 = new(2026, 11, 23, 14, 0, 0, TimeSpan.Zero);
        ClinicalPharmacovigilanceService svc = new(_lotRepo, _pvRepo, _protector, _auditMock.Object, () => t0);

        // 1. Nominal lot -> Nominal posture, CoA passes untouched
        await SeedLotAsync("lot-imm-nom", "pat-imm-nom", FabricationLotStatus.Released, t0);
        ImmunogenicityAssayResponse? nomResp = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-nom",
            new RecordImmunogenicityAssayRequest(),
            "lab_tech");
        Assert.NotNull(nomResp);
        Assert.Equal(PharmacovigilancePosture.Nominal, nomResp.ReportAfterAssay.Posture);
        Assert.False(nomResp.ReportAfterAssay.IsQuarantineTriggered);

        FabricationCertificateVerificationReport baseTrusted = new FabricationCertificateVerificationReport().GetExamples() with
        {
            LotId = "lot-imm-nom",
            IsTrusted = true
        };
        FabricationCertificateVerificationReport gatedNom = await svc.ApplyPharmacovigilanceGateAsync(baseTrusted);
        Assert.True(gatedNom.IsTrusted);
        Assert.Equal(PharmacovigilancePosture.Nominal, gatedNom.PharmacovigilancePosture);
        Assert.False(gatedNom.IsPharmacovigilanceFlagged);

        // Blank and foreign CoA edge cases
        FabricationCertificateVerificationReport blankReport = await svc.ApplyPharmacovigilanceGateAsync(baseTrusted with { LotId = " " });
        Assert.True(blankReport.IsTrusted);
        Assert.Null(blankReport.PharmacovigilancePosture);

        FabricationCertificateVerificationReport foreignReport = await svc.ApplyPharmacovigilanceGateAsync(baseTrusted with { LotId = "foreign-lot-999" });
        Assert.True(foreignReport.IsTrusted);
        Assert.Null(foreignReport.PharmacovigilancePosture);

        // 2. Monitor lot (all 8 biomarkers in Monitor band, below Critical) -> Monitor posture
        await SeedLotAsync("lot-imm-mon", "pat-imm-mon", FabricationLotStatus.Released, t0);
        ImmunogenicityAssayResponse? monResp = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-mon",
            new RecordImmunogenicityAssayRequest
            {
                Ch50Percent = 52.0,
                C3aNgMl = 310.0,
                C5aNgMl = 22.0,
                Il6PgMl = 35.0,
                TnfaPgMl = 24.0,
                AntiPegIgmTiter = 160.0,
                AntiDsDnaIgGIuMl = 45.0,
                Tlr9ActivationIndex = 2.6,
                RecordedAtUtc = t0.AddMinutes(5)
            },
            "lab_tech");
        Assert.NotNull(monResp);
        Assert.Equal(PharmacovigilancePosture.Monitor, monResp.ReportAfterAssay.Posture);
        Assert.False(monResp.ReportAfterAssay.IsQuarantineTriggered);
        Assert.Equal(8, monResp.ReportAfterAssay.TriggeredSignals.Count);

        // 3. CARPA Critical (CH50/C3a/C5a/anti-PEG IgM) + duplicate signal deduplication across second draw
        await SeedLotAsync("lot-imm-carpa", "pat-imm-carpa", FabricationLotStatus.Released, t0);
        ImmunogenicityAssayResponse? carpa1 = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-carpa",
            new RecordImmunogenicityAssayRequest
            {
                Ch50Percent = 35.0,
                C3aNgMl = 620.0,
                C5aNgMl = 48.0,
                AntiPegIgmTiter = 640.0,
                RecordedAtUtc = t0.AddMinutes(10)
            },
            "lab_tech");
        Assert.NotNull(carpa1);
        Assert.Equal(PharmacovigilancePosture.CarpaQuarantine, carpa1.ReportAfterAssay.Posture);
        Assert.True(carpa1.ReportAfterAssay.CarpaSignalDetected);
        Assert.True(carpa1.ReportAfterAssay.HumoralImmunogenicityDetected);
        Assert.False(carpa1.ReportAfterAssay.Tlr9CytokineStormDetected);
        Assert.True(carpa1.ReportAfterAssay.IsQuarantineTriggered);
        Assert.Equal(FabricationLotStatus.Quarantined, carpa1.ReportAfterAssay.LotStatus);

        // Second draw with same critical C3a hits AddUniqueSignal deduplication branch cleanly
        ImmunogenicityAssayResponse? carpa2 = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-carpa",
            new RecordImmunogenicityAssayRequest
            {
                C3aNgMl = 700.0,
                RecordedAtUtc = t0.AddMinutes(20)
            },
            "lab_tech");
        Assert.NotNull(carpa2);
        Assert.Equal(2, carpa2.ReportAfterAssay.ImmunogenicityAssayCount);

        // Verify CoA fails closed on CARPA-quarantined lot (both from trusted base and already-untrusted base)
        FabricationCertificateVerificationReport gatedQuarantined = await svc.ApplyPharmacovigilanceGateAsync(
            baseTrusted with { LotId = "lot-imm-carpa", IsTrusted = true });
        Assert.False(gatedQuarantined.IsTrusted);
        Assert.True(gatedQuarantined.IsPharmacovigilanceFlagged);
        Assert.Equal(PharmacovigilancePosture.CarpaQuarantine, gatedQuarantined.PharmacovigilancePosture);
        Assert.Contains("PHARMACOVIGILANCE BLOCK:", gatedQuarantined.Summary, StringComparison.Ordinal);

        FabricationCertificateVerificationReport gatedAlreadyUntrusted = await svc.ApplyPharmacovigilanceGateAsync(
            baseTrusted with { LotId = "lot-imm-carpa", IsTrusted = false });
        Assert.False(gatedAlreadyUntrusted.IsTrusted);

        // 4. TLR9 / Cytokine Storm Critical (without CARPA or Humoral)
        await SeedLotAsync("lot-imm-tlr9", "pat-imm-tlr9", FabricationLotStatus.Released, t0);
        ImmunogenicityAssayResponse? tlr9Resp = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-tlr9",
            new RecordImmunogenicityAssayRequest
            {
                Il6PgMl = 140.0,
                TnfaPgMl = 75.0,
                Tlr9ActivationIndex = 4.5
            },
            "lab_tech");
        Assert.NotNull(tlr9Resp);
        Assert.False(tlr9Resp.ReportAfterAssay.CarpaSignalDetected);
        Assert.True(tlr9Resp.ReportAfterAssay.Tlr9CytokineStormDetected);
        Assert.False(tlr9Resp.ReportAfterAssay.HumoralImmunogenicityDetected);
        Assert.True(tlr9Resp.ReportAfterAssay.IsQuarantineTriggered);

        // 5. Pure Humoral anti-dsDNA Critical (without CARPA or TLR9)
        await SeedLotAsync("lot-imm-humoral", "pat-imm-humoral", FabricationLotStatus.Released, t0);
        ImmunogenicityAssayResponse? humResp = await svc.RecordImmunogenicityAssayAsync(
            "lot-imm-humoral",
            new RecordImmunogenicityAssayRequest
            {
                AntiDsDnaIgGIuMl = 125.0
            },
            "lab_tech");
        Assert.NotNull(humResp);
        Assert.False(humResp.ReportAfterAssay.CarpaSignalDetected);
        Assert.False(humResp.ReportAfterAssay.Tlr9CytokineStormDetected);
        Assert.True(humResp.ReportAfterAssay.HumoralImmunogenicityDetected);
        Assert.True(humResp.ReportAfterAssay.IsQuarantineTriggered);

        _auditMock.Verify(
            a => a.LogAction(It.IsAny<string>(), "FABRICATION_IMMUNOGENICITY_RECORD", It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(6));
    }

    /// <summary>
    /// Seeds a parent depot formulation and fabrication lot inside the isolated SQLite test database.
    /// </summary>
    /// <param name="lotId">Fabrication lot primary key.</param>
    /// <param name="patientId">Owning patient identifier.</param>
    /// <param name="status">Initial lot status.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    /// <returns>A task representing the asynchronous seed operation.</returns>
    private async Task SeedLotAsync(
        string lotId,
        string patientId,
        FabricationLotStatus status,
        DateTimeOffset createdAtUtc)
    {
        // WHAT: Creates parent depot and fabrication lot rows satisfying foreign key constraints.
        // WHY:  Pharmacovigilance records reference weaver_fabrication_lots(lot_id).
        // HOW:  Inserts depot formulation and fabrication lot directly through repositories.
        string depotId = $"dep_{lotId}";
        await _depotRepo.UpsertDepotFormulationAsync(new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            CreatedAtUtc = createdAtUtc
        });
        await _lotRepo.UpsertLotAsync(new FabricationLotRecord
        {
            LotId = lotId,
            DepotId = depotId,
            PatientId = patientId,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            TargetMassMg = 16.0,
            TargetErosionRatePercent = 0.55,
            OpentronsProtocolSha256 = new string('b', 64),
            LotStatus = status,
            CreatedAtUtc = createdAtUtc,
            ReleasedAtUtc = createdAtUtc.AddHours(1)
        });
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
