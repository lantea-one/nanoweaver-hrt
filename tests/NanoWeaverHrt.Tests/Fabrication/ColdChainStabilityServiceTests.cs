using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit tests verifying <see cref="ColdChainStabilityService"/> Arrhenius degradation physics,
/// dsDNA origami melting cliffs, SHA-256 custody hash-chaining, fail-closed patient administration gating,
/// and CoA cold-chain stability enforcement.
/// </summary>
public sealed class ColdChainStabilityServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite database file and protector key.
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
    /// Fabrication lot repository for seeding lots and revocations.
    /// </summary>
    private readonly FabricationLotRepository _lotRepo;

    /// <summary>
    /// Cold-chain excursion and custody ledger repository.
    /// </summary>
    private readonly ColdChainCustodyRepository _coldRepo;

    /// <summary>
    /// AES-256-GCM field protector for optional clinical excursion notes.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Mock compliance audit service verifying 21 CFR Part 11 event emissions.
    /// </summary>
    private readonly Mock<IComplianceAuditService> _auditMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ColdChainStabilityServiceTests"/> class.
    /// </summary>
    public ColdChainStabilityServiceTests()
    {
        // WHAT: Creates an isolated SQLite database with all 19 migrations and an isolated ProfileDataProtector.
        // WHY:  Tests genuine multi-table persistence, AES-256-GCM encryption, and SHA-256 custody chains deterministically.
        // HOW:  Allocates a unique temp folder, builds WeaverTestDb factory, and instantiates real repositories.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_cold_chain_svc_{Guid.NewGuid():N}");
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
        _coldRepo = new ColdChainCustodyRepository(_factory);
        _protector = new ProfileDataProtector(new NodeSecretStore(config));
    }

    /// <summary>
    /// Verifies OpenAPI example providers across all Milestone 40 stability and custody DTOs.
    /// </summary>
    [Fact]
    public void ApiDtos_GetExamples_ReturnValidPopulatedSamples()
    {
        LotStabilityReport stab = new LotStabilityReport().GetExamples();
        Assert.Equal("lot_20261119_001", stab.LotId);
        Assert.Equal(ThermalStabilityStatus.Optimal, stab.Status);
        Assert.True(stab.IsSafeForImplantation);

        RecordThermalExcursionRequest exReq = new RecordThermalExcursionRequest().GetExamples();
        Assert.Equal(4.0, exReq.TemperatureCelsius);
        Assert.Equal(ThermalStorageTier.Refrigerated2To8, exReq.StorageTier);

        ThermalExcursionResponse exResp = new ThermalExcursionResponse().GetExamples();
        Assert.Equal("texc_20261119_001", exResp.ExcursionId);
        Assert.True(exResp.StabilityAfterExcursion.IsSafeForImplantation);

        RecordCustodyHandoffRequest hReq = new RecordCustodyHandoffRequest().GetExamples();
        Assert.Equal(CustodyStage.ClinicReceipt, hReq.Stage);

        CustodyHandoffLedgerResponse ledger = new CustodyHandoffLedgerResponse().GetExamples();
        Assert.True(ledger.CustodyChainValid);
        Assert.Single(ledger.Handoffs);
    }

    /// <summary>
    /// Verifies constructor null guards and Arrhenius rate constant bounds, monotonicity, and melting multipliers.
    /// </summary>
    [Fact]
    public void ArrheniusRateAndConstructor_EnforcePhysicsAndValidationGuards()
    {
        Assert.Throws<ArgumentNullException>(() => new ColdChainStabilityService(null!, _coldRepo, _protector, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ColdChainStabilityService(_lotRepo, null!, _protector, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ColdChainStabilityService(_lotRepo, _coldRepo, null!, _auditMock.Object));
        Assert.Throws<ArgumentNullException>(() => new ColdChainStabilityService(_lotRepo, _coldRepo, _protector, null!));

        ColdChainStabilityService svc = new(_lotRepo, _coldRepo, _protector, _auditMock.Object);

        // Bounds guards
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.ComputeDegradationRatePerHour(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.ComputeDegradationRatePerHour(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.ComputeDegradationRatePerHour(-200.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => svc.ComputeDegradationRatePerHour(125.0));

        // Exact reference calibration at -20°C: 365 days yields exactly 90.0% potency
        double kRef = svc.ComputeDegradationRatePerHour(-20.0);
        Assert.Equal(ArrheniusColdChainConstants.ReferenceRateConstantPerHour, kRef, 12);
        Assert.Equal(90.0, 100.0 * Math.Exp(-kRef * 365.0 * 24.0), 8);

        // Monotonic Arrhenius acceleration + melting cliffs at 37°C (10x) and >42°C (100x)
        double kMinus80 = svc.ComputeDegradationRatePerHour(-80.0);
        double k4C = svc.ComputeDegradationRatePerHour(4.0);
        double k36C = svc.ComputeDegradationRatePerHour(36.9);
        double k37C = svc.ComputeDegradationRatePerHour(37.0);
        double k42C = svc.ComputeDegradationRatePerHour(42.0);
        double k43C = svc.ComputeDegradationRatePerHour(43.0);

        Assert.True(kMinus80 < kRef);
        Assert.True(k4C > kRef * 10.0);
        Assert.True(k37C > k36C * 9.0);
        Assert.True(k43C > k42C * 9.0);
    }

    /// <summary>
    /// Verifies thermal excursion recording validation, AES-256-GCM note encryption, advisory/expired/catastrophic
    /// stability transitions, nominal 365-day expiry, and 21 CFR Part 11 audit events.
    /// </summary>
    [Fact]
    public async Task ThermalExcursionsAndStability_CoverAllStatusTransitionsAndValidationGuards()
    {
        DateTimeOffset t0 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = t0;
        ColdChainStabilityService svc = new(_lotRepo, _coldRepo, _protector, _auditMock.Object, () => now);

        await SeedLotAsync("lot-opt", "pat-1", FabricationLotStatus.Released, t0, t0);
        await SeedLotAsync("lot-unreleased", "pat-1", FabricationLotStatus.Draft, t0, null);
        await SeedLotAsync("lot-expired-potency", "pat-1", FabricationLotStatus.Released, t0, t0);
        await SeedLotAsync("lot-catastrophic", "pat-1", FabricationLotStatus.Released, t0, t0);

        // Validation guards
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync(" ", new RecordThermalExcursionRequest(), "admin"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.RecordThermalExcursionAsync("lot-opt", null!, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest(), " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 150.0, DurationHours = 1.0, SensorId = "S1" }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 0.0, SensorId = "S1" }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = double.NaN, SensorId = "S1" }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 90_000.0, SensorId = "S1" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 1.0, StorageTier = (ThermalStorageTier)99, SensorId = "S1" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 1.0, SensorId = null! }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 1.0, SensorId = new string('s', 129) }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest { TemperatureCelsius = 4.0, DurationHours = 1.0, SensorId = "S1", Notes = new string('n', ColdChainStabilityService.MaxNotesLength + 1) }, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.EvaluateLotStabilityAsync(" "));
        Assert.Null(await svc.EvaluateLotStabilityAsync("missing-lot"));
        Assert.Null(await svc.RecordThermalExcursionAsync("missing-lot", new RecordThermalExcursionRequest { TemperatureCelsius = -20.0, DurationHours = 1.0, SensorId = "S1" }, "admin"));

        // 1. Optimal state (also exercises ReleasedAtUtc == null fallback to CreatedAtUtc on lot-unreleased)
        LotStabilityReport? draftReport = await svc.EvaluateLotStabilityAsync("lot-unreleased");
        Assert.NotNull(draftReport);
        Assert.Equal(ThermalStabilityStatus.Optimal, draftReport.Status);
        Assert.True(draftReport.IsSafeForImplantation);

        // Log -20°C optimal excursion with encrypted notes
        ThermalExcursionResponse? optResp = await svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = -20.0,
            DurationHours = 2.0,
            StorageTier = ThermalStorageTier.FrozenMinus20,
            SensorId = "PROBE-1",
            Notes = "Cold freezer check OK."
        }, "dr_lin");
        Assert.NotNull(optResp);
        Assert.Equal("Cold freezer check OK.", optResp.Notes);
        Assert.Equal(ThermalStabilityStatus.Optimal, optResp.StabilityAfterExcursion.Status);

        // Confirm stored notes are AES-256-GCM encrypted at rest
        IReadOnlyList<ThermalExcursionRecord> storedEx = await _coldRepo.ListThermalExcursionsByLotAsync("lot-opt");
        Assert.Single(storedEx);
        Assert.True(ProfileDataProtector.IsEncrypted(storedEx[0].NotesEncrypted));

        // 2. AdvisoryExcursion via AmbientControlled tier & null notes (temperature > 8°C)
        ThermalExcursionResponse? advResp = await svc.RecordThermalExcursionAsync("lot-opt", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 15.0,
            DurationHours = 0.5,
            StorageTier = ThermalStorageTier.AmbientControlled,
            SensorId = "PROBE-1",
            Notes = "   ",
            RecordedAtUtc = t0.AddHours(1)
        }, "dr_lin");
        Assert.NotNull(advResp);
        Assert.Null(advResp.Notes);
        Assert.Equal(ThermalStabilityStatus.AdvisoryExcursion, advResp.StabilityAfterExcursion.Status);
        Assert.True(advResp.StabilityAfterExcursion.IsSafeForImplantation);

        // Also cover ThermalExcursion tier at low temp + budget >= 25% branch
        await SeedLotAsync("lot-tier-adv", "pat-1", FabricationLotStatus.Released, t0, t0);
        ThermalExcursionResponse? tierAdv = await svc.RecordThermalExcursionAsync("lot-tier-adv", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 4.0,
            DurationHours = 1.0,
            StorageTier = ThermalStorageTier.ThermalExcursion,
            SensorId = "PROBE-2"
        }, "dr_lin");
        Assert.Equal(ThermalStabilityStatus.AdvisoryExcursion, tierAdv!.StabilityAfterExcursion.Status);

        await SeedLotAsync("lot-budget-adv", "pat-1", FabricationLotStatus.Released, t0, t0);
        now = t0.AddDays(120); // ~33% thermal budget at -20°C -> AdvisoryExcursion
        LotStabilityReport? budgetAdv = await svc.EvaluateLotStabilityAsync("lot-budget-adv");
        Assert.Equal(ThermalStabilityStatus.AdvisoryExcursion, budgetAdv!.Status);

        // 3. ExpiredOrCompromised via nominal 365-day shelf-life expiry (lot-opt at day 366)
        now = t0.AddDays(366);
        LotStabilityReport? expiredNominal = await svc.EvaluateLotStabilityAsync("lot-opt");
        Assert.Equal(ThermalStabilityStatus.ExpiredOrCompromised, expiredNominal!.Status);
        Assert.True(expiredNominal.IsPastNominalExpiry);
        Assert.False(expiredNominal.IsSafeForImplantation);
        Assert.Equal(0.0, expiredNominal.EstimatedRemainingShelfLifeDaysAtMinus20C);
        Assert.Equal(0.0, expiredNominal.EstimatedRemainingShelfLifeDaysAt4C);

        // 4. ExpiredOrCompromised via thermal degradation (< 90% potency before day 365)
        now = t0.AddDays(1);
        ThermalExcursionResponse? potExpired = await svc.RecordThermalExcursionAsync("lot-expired-potency", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 37.5, // 10x melting penalty -> rapid degradation
            DurationHours = 24.0,
            StorageTier = ThermalStorageTier.ThermalExcursion,
            SensorId = "PROBE-3"
        }, "dr_lin");
        Assert.Equal(ThermalStabilityStatus.ExpiredOrCompromised, potExpired!.StabilityAfterExcursion.Status);
        Assert.False(potExpired.StabilityAfterExcursion.IsPastNominalExpiry);
        Assert.False(potExpired.StabilityAfterExcursion.IsSafeForImplantation);

        // 5. CatastrophicDenaturation (> 42.0°C)
        ThermalExcursionResponse? catResp = await svc.RecordThermalExcursionAsync("lot-catastrophic", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 45.0,
            DurationHours = 0.25,
            StorageTier = ThermalStorageTier.ThermalExcursion,
            SensorId = "PROBE-4"
        }, "dr_lin");
        Assert.Equal(ThermalStabilityStatus.CatastrophicDenaturation, catResp!.StabilityAfterExcursion.Status);
        Assert.True(catResp.StabilityAfterExcursion.HasCatastrophicExcursion);
        Assert.False(catResp.StabilityAfterExcursion.IsSafeForImplantation);
        Assert.Equal(0.0, catResp.StabilityAfterExcursion.EstimatedRemainingShelfLifeDaysAtMinus20C);

        _auditMock.Verify(a => a.LogAction("dr_lin", "FABRICATION_THERMAL_LOG_RECORD", It.IsAny<string>(), It.IsAny<string>()), Times.AtLeast(4));
    }

    /// <summary>
    /// Verifies SHA-256 custody hash-chaining, tamper detection (prev hash, entry hash, length mismatch),
    /// fail-closed <see cref="CustodyStage.PatientAdministration"/> blocking (revoked local, revoked federated,
    /// non-Released status, thermally compromised, tampered chain, post-disposal), and CoA stability gating.
    /// </summary>
    [Fact]
    public async Task CustodyChainAndCoaGate_EnforceTamperDetectionAndFailClosedBlocks()
    {
        DateTimeOffset t0 = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = t0.AddMinutes(10);
        ColdChainStabilityService svc = new(_lotRepo, _coldRepo, _protector, _auditMock.Object, () => now);

        Assert.Throws<ArgumentNullException>(() => svc.ComputeCustodyEntrySha256(null!));
        Assert.Throws<ArgumentNullException>(() => svc.VerifyCustodyChain(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.GetCustodyLedgerAsync(" "));
        Assert.Null(await svc.GetCustodyLedgerAsync("missing-lot"));

        // RecordCustodyHandoffAsync validation guards
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync(" ", new RecordCustodyHandoffRequest(), "admin"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.RecordCustodyHandoffAsync("lot-1", null!, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest(), " "));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = (CustodyStage)99, FromActor = "a", ToActor = "b", FacilityCode = "f" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = null!, ToActor = "b", FacilityCode = "f" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = new string('a', 129), ToActor = "b", FacilityCode = "f" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = null!, FacilityCode = "f" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = new string('b', 129), FacilityCode = "f" }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = "b", FacilityCode = null! }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = "b", FacilityCode = new string('f', 65) }, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.RecordCustodyHandoffAsync("lot-1", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = "b", FacilityCode = "f", TemperatureCelsius = -250.0 }, "admin"));
        Assert.Null(await svc.RecordCustodyHandoffAsync("missing-lot", new RecordCustodyHandoffRequest { Stage = CustodyStage.WetLabRelease, FromActor = "a", ToActor = "b", FacilityCode = "f", TemperatureCelsius = -20.0 }, "admin"));

        // 1. Happy path chain: WetLabRelease -> ClinicReceipt -> PatientAdministration
        await SeedLotAsync("lot-ok", "pat-1", FabricationLotStatus.Released, t0, t0);
        CustodyHandoffRecord? h1 = await svc.RecordCustodyHandoffAsync("lot-ok", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.WetLabRelease,
            FromActor = "wetlab_lead",
            ToActor = "courier_01",
            FacilityCode = "ZRH-LAB-01",
            TemperatureCelsius = -20.0,
            RecordedAtUtc = t0.AddMinutes(5)
        }, "wetlab_lead");
        Assert.NotNull(h1);
        Assert.Equal(ArrheniusColdChainConstants.GenesisCustodyPrevSha256, h1.ChainPrevSha256);

        CustodyHandoffRecord? h2 = await svc.RecordCustodyHandoffAsync("lot-ok", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "clinic_rn",
            ToActor = "pat-1",
            FacilityCode = "ZRH-CLINIC-01",
            TemperatureCelsius = 4.0
        }, "clinic_rn");
        Assert.NotNull(h2);
        Assert.Equal(h1.EntrySha256, h2.ChainPrevSha256);

        CustodyHandoffLedgerResponse? okLedger = await svc.GetCustodyLedgerAsync("lot-ok");
        Assert.NotNull(okLedger);
        Assert.True(okLedger.CustodyChainValid);
        Assert.Equal(2, okLedger.Handoffs.Count);

        // 2. Terminal QuarantineDisposal blocks subsequent custody transitions
        await SeedLotAsync("lot-disposed", "pat-1", FabricationLotStatus.Released, t0, t0);
        await svc.RecordCustodyHandoffAsync("lot-disposed", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.QuarantineDisposal,
            FromActor = "qa_lead",
            ToActor = "biohazard_incinerator",
            FacilityCode = "ZRH-LAB-01",
            TemperatureCelsius = 20.0
        }, "qa_lead");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-disposed", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.ColdChainDispatch,
            FromActor = "a",
            ToActor = "b",
            FacilityCode = "F1",
            TemperatureCelsius = -20.0
        }, "qa_lead"));

        // 3. Fail-closed PatientAdministration blocks: Quarantined status, Local Revocation, Federated Revocation, Revoked status, and Catastrophic excursion
        await SeedLotAsync("lot-quarantined", "pat-1", FabricationLotStatus.Quarantined, t0, t0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-quarantined", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "rn",
            ToActor = "pat-1",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        await SeedLotAsync("lot-rev-status", "pat-1", FabricationLotStatus.Revoked, t0, t0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-rev-status", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "rn",
            ToActor = "pat-1",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        await SeedLotAsync("lot-rev-local", "pat-1", FabricationLotStatus.Released, t0, t0);
        await _lotRepo.RecordLotRevocationAsync(new FabricationLotRevocationRecord
        {
            LotId = "lot-rev-local",
            PatientId = "pat-1",
            RevokedBy = "admin",
            RevocationReason = "Local recall",
            RevokedAtUtc = t0
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-rev-local", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "rn",
            ToActor = "pat-1",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        await SeedLotAsync("lot-rev-fed", "pat-1", FabricationLotStatus.Released, t0, t0);
        await _lotRepo.UpsertFederatedRevocationAsync(new FederatedLotRevocationRecord
        {
            LotId = "lot-rev-fed",
            IssuerKeyId = new string('a', 64),
            RevokedBy = "peer_admin",
            RevocationReason = "Federated recall",
            RevokedAtUtc = t0,
            FeedPayloadSha256 = new string('b', 64),
            FeedSignatureBase64 = "SIG==",
            ImportedAtUtc = t0
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-rev-fed", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "rn",
            ToActor = "pat-1",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        await SeedLotAsync("lot-melted", "pat-1", FabricationLotStatus.Released, t0, t0);
        await svc.RecordThermalExcursionAsync("lot-melted", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 50.0,
            DurationHours = 1.0,
            StorageTier = ThermalStorageTier.ThermalExcursion,
            SensorId = "S-MELT"
        }, "admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-melted", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "rn",
            ToActor = "pat-1",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        // 4. Direct chain tamper vectors: prev-hash mismatch, entry digest mismatch, short digest length
        CustodyChainVerificationResult prevMismatch = svc.VerifyCustodyChain([h1 with { ChainPrevSha256 = new string('9', 64) }]);
        Assert.False(prevMismatch.IsValid);
        CustodyChainVerificationResult hashMismatch = svc.VerifyCustodyChain([h1 with { EntrySha256 = new string('f', 64) }]);
        Assert.False(hashMismatch.IsValid);
        CustodyChainVerificationResult lenMismatch = svc.VerifyCustodyChain([h1 with { EntrySha256 = "short" }]);
        Assert.False(lenMismatch.IsValid);

        // Tamper persisted row in DB -> GetCustodyLedgerAsync + EvaluateLotStabilityAsync + RecordCustodyHandoffAsync all fail closed
        await using (WeaverDbSession session = await _factory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_lot_custody_handoffs SET to_actor = 'tampered_actor' WHERE handoff_id = @hid");
            WeaverDbParameters.Add(cmd, "hid", h1.HandoffId);
            await cmd.ExecuteNonQueryAsync();
        }

        CustodyHandoffLedgerResponse? tamperedLedger = await svc.GetCustodyLedgerAsync("lot-ok");
        Assert.False(tamperedLedger!.CustodyChainValid);
        Assert.Contains("tampered", tamperedLedger.ChainValidationSummary, StringComparison.OrdinalIgnoreCase);

        LotStabilityReport? tamperedStab = await svc.EvaluateLotStabilityAsync("lot-ok");
        Assert.False(tamperedStab!.CustodyChainValid);
        Assert.False(tamperedStab.IsSafeForImplantation);
        Assert.Contains("Custody chain integrity compromised", tamperedStab.Summary, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordCustodyHandoffAsync("lot-ok", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PreImplantInspection,
            FromActor = "rn",
            ToActor = "md",
            FacilityCode = "F1",
            TemperatureCelsius = 4.0
        }, "rn"));

        // 5. ApplyColdChainStabilityGateAsync: null guard, blank lotId passthrough, foreign lot passthrough, safe lot preserves true, compromised lot flips to false
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.ApplyColdChainStabilityGateAsync(null!));

        FabricationCertificateVerificationReport blankReport = new FabricationCertificateVerificationReport().GetExamples() with { LotId = " " };
        Assert.Same(blankReport, await svc.ApplyColdChainStabilityGateAsync(blankReport));

        FabricationCertificateVerificationReport foreignReport = new FabricationCertificateVerificationReport().GetExamples() with { LotId = "foreign-peer-lot" };
        Assert.Same(foreignReport, await svc.ApplyColdChainStabilityGateAsync(foreignReport));

        await SeedLotAsync("lot-safe-coa", "pat-1", FabricationLotStatus.Released, t0, t0);
        FabricationCertificateVerificationReport safeBase = new FabricationCertificateVerificationReport().GetExamples() with { LotId = "lot-safe-coa", IsTrusted = true };
        FabricationCertificateVerificationReport safeGated = await svc.ApplyColdChainStabilityGateAsync(safeBase);
        Assert.True(safeGated.IsTrusted);
        Assert.Equal(ThermalStabilityStatus.Optimal, safeGated.ColdChainStatus);
        Assert.True(safeGated.IsSafeForImplantation);
        Assert.True(safeGated.CustodyChainValid);

        FabricationCertificateVerificationReport meltedBase = new FabricationCertificateVerificationReport().GetExamples() with { LotId = "lot-melted", IsTrusted = true };
        FabricationCertificateVerificationReport meltedGated = await svc.ApplyColdChainStabilityGateAsync(meltedBase);
        Assert.False(meltedGated.IsTrusted);
        Assert.Equal(ThermalStabilityStatus.CatastrophicDenaturation, meltedGated.ColdChainStatus);
        Assert.False(meltedGated.IsSafeForImplantation);
        Assert.Contains("COLD-CHAIN BLOCK", meltedGated.Summary, StringComparison.Ordinal);

        // Already-untrusted base stays untrusted even when local lot is thermally safe
        FabricationCertificateVerificationReport alreadyUntrusted = safeBase with { IsTrusted = false };
        Assert.False((await svc.ApplyColdChainStabilityGateAsync(alreadyUntrusted)).IsTrusted);
    }

    /// <summary>
    /// Verifies that enabling <see cref="ColdChainStabilityService.RequirePairedAntidoteConfigKey"/> via static
    /// <see cref="IConfiguration"/> or runtime <see cref="IConfigurationRepository"/> blocks un-paired
    /// <see cref="CustodyStage.PatientAdministration"/> handoffs fail-closed and allows them once an active ML-DSA-65 antidote is paired.
    /// </summary>
    [Fact]
    public async Task RecordCustodyHandoffAsync_RequirePairedAntidotePolicy_EnforcesActiveVerifiedStandby()
    {
        // WHAT: Tests fail-closed RequirePairedAntidote gating across static IConfiguration and runtime SystemConfigurationStore.
        // WHY:  Milestone 44 (P231): Clinics can mandate verified molecular reversal antidote standby prior to patient implantation.
        // HOW:  Exercises missing service, un-paired lot, paired-antidote standby, and policy-disabled flows.
        DateTimeOffset t0 = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
        await SeedLotAsync("lot-gate-test", "pat-gate", FabricationLotStatus.Released, t0, t0);

        IConfiguration strictStaticConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ColdChainStabilityService.RequirePairedAntidoteConfigKey] = "true"
            })
            .Build();

        // 1. Static strict config without IReversalAntidoteService injected -> fail-closed
        ColdChainStabilityService svcNoAntidoteSvc = new(
            _lotRepo, _coldRepo, _protector, _auditMock.Object, () => t0, null, strictStaticConfig, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svcNoAntidoteSvc.RecordCustodyHandoffAsync(
            "lot-gate-test",
            new RecordCustodyHandoffRequest
            {
                Stage = CustodyStage.PatientAdministration,
                FromActor = "nurse",
                ToActor = "patient",
                FacilityCode = "CLINIC-1",
                TemperatureCelsius = 4.0
            },
            "clinician"));

        // 2. Construct genuine ReversalAntidoteService and runtime SystemConfigurationStore
        NodeSecretStore secretStore = new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [NodeSecretStore.NodeKeysDirectoryConfigKey] = _tempDir })
            .Build());
        PostQuantumCryptoProvider crypto = new();
        SubjectKeyRepository skRepo = new(_factory);
        SubjectErasureJournal journal = new(secretStore);
        SubjectKeyVault vault = new(skRepo, _protector, crypto, journal);
        await vault.InitializeAsync();
        KeyManagementService kms = new(crypto, vault, secretStore);
        ReversalAntidoteRepository antiRepo = new(_factory);
        ReversalAntidoteService antiSvc = new(_lotRepo, antiRepo, kms, crypto, _auditMock.Object, () => t0);

        using SystemConfigurationStore configStore = new(_factory);
        await configStore.InitializeAsync();
        await configStore.SetDatabaseOverrideAsync(ColdChainStabilityService.RequirePairedAntidoteConfigKey, "true");

        IConfiguration disabledStaticConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ColdChainStabilityService.RequirePairedAntidoteConfigKey] = "false"
            })
            .Build();

        ColdChainStabilityService svcStrictViaRepo = new(
            _lotRepo, _coldRepo, _protector, _auditMock.Object, () => t0, antiSvc, disabledStaticConfig, configStore);

        // Runtime DB override blocks PatientAdministration while no antidote is paired
        await Assert.ThrowsAsync<InvalidOperationException>(() => svcStrictViaRepo.RecordCustodyHandoffAsync(
            "lot-gate-test",
            new RecordCustodyHandoffRequest
            {
                Stage = CustodyStage.PatientAdministration,
                FromActor = "nurse",
                ToActor = "patient",
                FacilityCode = "CLINIC-1",
                TemperatureCelsius = 4.0
            },
            "clinician"));

        // Pair active verified antidote -> PatientAdministration succeeds cleanly!
        await antiSvc.PairAntidoteAsync("lot-gate-test", null, "admin");
        CustodyHandoffRecord? allowed = await svcStrictViaRepo.RecordCustodyHandoffAsync(
            "lot-gate-test",
            new RecordCustodyHandoffRequest
            {
                Stage = CustodyStage.PatientAdministration,
                FromActor = "nurse",
                ToActor = "patient",
                FacilityCode = "CLINIC-1",
                TemperatureCelsius = 4.0
            },
            "clinician");
        Assert.NotNull(allowed);
        Assert.Equal(CustodyStage.PatientAdministration, allowed.Stage);

        // Policy disabled ("false") in both static & repo allows non-paired lot administration
        await SeedLotAsync("lot-gate-open", "pat-gate", FabricationLotStatus.Released, t0, t0);
        await configStore.SetDatabaseOverrideAsync(ColdChainStabilityService.RequirePairedAntidoteConfigKey, "false");
        CustodyHandoffRecord? openAllowed = await svcStrictViaRepo.RecordCustodyHandoffAsync(
            "lot-gate-open",
            new RecordCustodyHandoffRequest
            {
                Stage = CustodyStage.PatientAdministration,
                FromActor = "nurse",
                ToActor = "patient",
                FacilityCode = "CLINIC-1",
                TemperatureCelsius = 4.0
            },
            "clinician");
        Assert.NotNull(openAllowed);
    }

    /// <summary>
    /// Helper seeding a parent depot formulation and fabrication lot for cold-chain tests.
    /// </summary>
    /// <param name="lotId">Fabrication lot identifier.</param>
    /// <param name="patientId">Patient identifier.</param>
    /// <param name="status">Initial fabrication lot lifecycle status.</param>
    /// <param name="createdAtUtc">Lot creation timestamp.</param>
    /// <param name="releasedAtUtc">Optional lot release timestamp.</param>
    /// <returns>A task representing the asynchronous seed operation.</returns>
    private async Task SeedLotAsync(
        string lotId,
        string patientId,
        FabricationLotStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? releasedAtUtc)
    {
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
            OpentronsProtocolSha256 = new string('c', 64),
            LotStatus = status,
            CreatedAtUtc = createdAtUtc,
            ReleasedAtUtc = releasedAtUtc
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
