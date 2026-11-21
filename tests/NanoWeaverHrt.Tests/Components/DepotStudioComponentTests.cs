using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and integration test suite for <see cref="DepotStudio"/> validating interactive 180-day depot parameter configuration,
/// simulation execution dispatch, clinical metric displays (TTR %, burst phase, steady-state rate, re-dosing alert),
/// longitudinal serum assay calibration, observed SVG markers, safety alerts, wet-lab batch fabrication,
/// 5-gate pharmaceutical QC verification, ML-DSA-65 Certificate of Analysis signing, and reset actions.
/// </summary>
public sealed class DepotStudioComponentTests : BunitContext
{
    /// <summary>
    /// Thread-safe localizer service for UI text.
    /// </summary>
    private readonly WeaverLocalizer _localizer = new();

    /// <summary>
    /// Computational time-release depot service.
    /// </summary>
    private readonly TimeReleaseDepotService _depotService = new();

    /// <summary>
    /// Longitudinal Bayesian depot calibration service.
    /// </summary>
    private readonly DepotCalibrationService _calibrationService;

    /// <summary>
    /// Isolated key management host supplying real NIST FIPS 204 ML-DSA-65 signing for studio lot release tests.
    /// </summary>
    private readonly SubjectKeyTestHost _pqcHost;

    /// <summary>
    /// Real wet-lab 5-gate QC and ML-DSA-65 lot release service.
    /// </summary>
    private readonly FabricationLotService _lotService;

    /// <summary>
    /// Mocked depot formulation repository for studio persistence actions.
    /// </summary>
    private readonly Mock<IDepotFormulationRepository> _depotRepoMock = new();

    /// <summary>
    /// Mocked longitudinal depot assay repository for studio persistence actions.
    /// </summary>
    private readonly Mock<IDepotAssayRepository> _assayRepoMock = new();

    /// <summary>
    /// Mocked wet-lab fabrication lot repository for studio persistence actions.
    /// </summary>
    private readonly Mock<IFabricationLotRepository> _lotRepoMock = new();

    /// <summary>
    /// Mocked peer key registry for cross-node certificate verification tests.
    /// </summary>
    private readonly Mock<IPeerKeyRegistry> _peerRegistryMock = new();

    /// <summary>
    /// Mocked automated peer revocation pull-sync service for Milestone 39 UI tests.
    /// </summary>
    private readonly Mock<IFederatedPeerSyncService> _peerSyncMock = new();

    /// <summary>
    /// Mocked Arrhenius cold-chain thermal stability and SHA-256 custody ledger service for Milestone 40 UI tests.
    /// </summary>
    private readonly Mock<IColdChainStabilityService> _coldChainMock = new();

    /// <summary>
    /// In-memory list of peer subscriptions managed by <see cref="_peerSyncMock"/>.
    /// </summary>
    private readonly List<FederatedPeerSyncEndpointRecord> _peerSyncEndpoints = [];

    /// <summary>
    /// In-memory list of cold-chain custody handoffs managed by <see cref="_coldChainMock"/>.
    /// </summary>
    private readonly List<CustodyHandoffRecord> _coldChainHandoffs = [];

    /// <summary>
    /// In-memory active cold-chain stability report returned by <see cref="_coldChainMock"/>.
    /// </summary>
    private LotStabilityReport _coldChainStability = new()
    {
        LotId = "lot_studio_test_1",
        PatientId = "depot-studio-session",
        RemainingPotencyPercent = 100.0,
        CumulativeThermalBudgetUsedPercent = 0.0,
        EstimatedRemainingShelfLifeDaysAtMinus20C = 365.0,
        EstimatedRemainingShelfLifeDaysAt4C = 23.7,
        Status = ThermalStabilityStatus.Optimal,
        CustodyChainValid = true,
        IsSafeForImplantation = true,
        Summary = "Optimal cold-chain stability: 100.00% potency remaining (0.00% thermal budget used); custody chain verified."
    };

    /// <summary>
    /// In-memory stored revocation record for verifying post-revocation checks in bUnit.
    /// </summary>
    private FabricationLotRevocationRecord? _storedRevocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotStudioComponentTests"/> class.
    /// </summary>
    public DepotStudioComponentTests()
    {
        // WHAT: Configures dependency injection container for bUnit component testing.
        // WHY:  DepotStudio depends on ITimeReleaseDepotService, IDepotCalibrationService, IFabricationLotService, repositories, and IWeaverLocalizer.
        // HOW:  Registers real computational/cryptographic services and mocked repositories into BunitContext.Services.
        _calibrationService = new DepotCalibrationService(_depotService);
        string pqcDir = Path.Combine(Path.GetTempPath(), "weaver_depot_studio_bunit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pqcDir);
        _pqcHost = new SubjectKeyTestHost(pqcDir);
        _lotService = new FabricationLotService(_pqcHost.KeyManagement, _pqcHost.CryptoProvider);

        _depotRepoMock
            .Setup(r => r.UpsertDepotFormulationAsync(It.IsAny<DepotFormulationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _assayRepoMock
            .Setup(r => r.RecordObservationAsync(It.IsAny<DepotAssayObservationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepotAssayObservationRecord obs, CancellationToken _) => obs with { ObservationId = "assay_test_1" });

        _assayRepoMock
            .Setup(r => r.SaveCalibrationSnapshotAsync(It.IsAny<DepotCalibrationSnapshotRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepotCalibrationSnapshotRecord snap, CancellationToken _) => snap with { SnapshotId = "calib_test_1" });

        _lotRepoMock
            .Setup(r => r.UpsertLotAsync(It.IsAny<FabricationLotRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FabricationLotRecord lot, CancellationToken _) => string.IsNullOrWhiteSpace(lot.LotId) ? lot with { LotId = "lot_studio_test_1" } : lot);

        _lotRepoMock
            .Setup(r => r.RecordQcCheckAsync(It.IsAny<FabricationQcCheckRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FabricationQcCheckRecord chk, CancellationToken _) => string.IsNullOrWhiteSpace(chk.CheckId) ? chk with { CheckId = $"qc_{Guid.NewGuid():N}" } : chk);

        _lotRepoMock
            .Setup(r => r.RecordLotRevocationAsync(It.IsAny<FabricationLotRevocationRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FabricationLotRevocationRecord rev, CancellationToken _) =>
            {
                _storedRevocation = rev;
                return rev;
            });

        _lotRepoMock
            .Setup(r => r.GetLotRevocationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _storedRevocation is not null && _storedRevocation.LotId == id ? _storedRevocation : null);

        _peerRegistryMock
            .Setup(p => p.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _lotRepoMock
            .Setup(r => r.ListFederatedRevocationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _peerSyncMock
            .Setup(s => s.ListSubscriptionsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _peerSyncEndpoints.ToList());

        _coldChainMock
            .Setup(c => c.EvaluateLotStabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lotId, CancellationToken _) => _coldChainStability with { LotId = lotId });

        _coldChainMock
            .Setup(c => c.GetCustodyLedgerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lotId, CancellationToken _) => new CustodyHandoffLedgerResponse
            {
                LotId = lotId,
                PatientId = "depot-studio-session",
                CustodyChainValid = _coldChainStability.CustodyChainValid,
                ChainValidationSummary = _coldChainStability.CustodyChainValid
                    ? $"Verified {_coldChainHandoffs.Count} hash-chained custody handoff(s) cleanly from genesis."
                    : "Custody chain integrity compromised.",
                Handoffs = _coldChainHandoffs.ToList()
            });

        _coldChainMock
            .Setup(c => c.ApplyColdChainStabilityGateAsync(It.IsAny<FabricationCertificateVerificationReport>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FabricationCertificateVerificationReport r, CancellationToken _) => r with
            {
                ColdChainStatus = _coldChainStability.Status,
                RemainingPotencyPercent = _coldChainStability.RemainingPotencyPercent,
                CustodyChainValid = _coldChainStability.CustodyChainValid,
                IsSafeForImplantation = _coldChainStability.IsSafeForImplantation
            });

        Services.AddSingleton<IWeaverLocalizer>(_localizer);
        Services.AddSingleton<ITimeReleaseDepotService>(_depotService);
        Services.AddSingleton<IDepotCalibrationService>(_calibrationService);
        Services.AddSingleton<IFabricationLotService>(_lotService);
        Services.AddSingleton(_depotRepoMock.Object);
        Services.AddSingleton(_assayRepoMock.Object);
        Services.AddSingleton(_lotRepoMock.Object);
        Services.AddSingleton(_peerRegistryMock.Object);
        Services.AddSingleton(_peerSyncMock.Object);
        Services.AddSingleton(_coldChainMock.Object);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            string root = _pqcHost.RootDirectory;
            _pqcHost.Dispose();
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Verifies that <see cref="DepotStudio"/> renders the form controls and computes default baseline simulation metrics on initial render.
    /// </summary>
    [Fact]
    public void DepotStudio_InitialRender_RendersFormAndDefaultSimulationMetrics()
    {
        // WHAT: Renders DepotStudio and inspects initial DOM structure.
        // WHY:  Guarantees that clinicians receive immediate visual feedback and default 180-day simulation metrics upon loading the page.
        // HOW:  Renders component and asserts on header, inputs, buttons, metric cards, SVG chart, and longitudinal calibration panel.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        Assert.Contains("180-Day Semi-Annual Time-Release Depot Studio", cut.Markup);
        Assert.NotNull(cut.Find("#select-regimen"));
        Assert.NotNull(cut.Find("#select-matrix"));
        Assert.NotNull(cut.Find("#input-initial-mass"));
        Assert.NotNull(cut.Find("#input-erosion-rate"));
        Assert.NotNull(cut.Find("#btn-run-depot-simulation"));
        Assert.NotNull(cut.Find("#btn-reset-depot-defaults"));
        Assert.NotNull(cut.Find("#longitudinal-calibration-panel"));
        Assert.NotNull(cut.Find("#input-assay-day"));
        Assert.NotNull(cut.Find("#btn-record-assay-recalibrate"));

        // Asserts clinical metric cards rendered
        Assert.Contains("TIME IN TARGET RANGE", cut.Markup);
        Assert.Contains("PHASE I BURST FRACTION", cut.Markup);
        Assert.Contains("STEADY-STATE RATE", cut.Markup);
        Assert.Contains("TERMINAL RE-DOSING DAY", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);

        // Asserts SVG trajectory and table
        Assert.NotNull(cut.Find("svg"));
        Assert.NotNull(cut.Find("table.data-table"));
    }

    /// <summary>
    /// Verifies that modifying formulation parameters and clicking "Run 180-Day Simulation" updates the simulation result.
    /// </summary>
    [Fact]
    public void DepotStudio_RunSimulation_UpdatesMetricsOnFormChanges()
    {
        // WHAT: Changes initial mass and triggers the simulation button.
        // WHY:  Ensures interactive parameter explorations trigger computational re-evaluations accurately.
        // HOW:  Changes input values, clicks the execute button, and verifies updated markup values.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        cut.Find("#input-initial-mass").Change("25.0");
        cut.Find("#btn-run-depot-simulation").Click();

        Assert.Contains("25.0 mg", cut.Markup);
    }

    /// <summary>
    /// Verifies that changing the therapeutic regimen dropdown automatically re-executes the simulation with regimen-specific setpoints.
    /// </summary>
    [Fact]
    public void DepotStudio_HandleRegimenChanged_ReExecutesSimulationWithNewRegimen()
    {
        // WHAT: Selects FemaleToMale in regimen dropdown and then switches back to MaleToFemale.
        // WHY:  Verifies that switching between estrogenic and androgenic therapies updates default loaded mass (1050.0 mg FtM, 16.0 mg MtF).
        // HOW:  Fires change event on #select-regimen for both regimens using fresh cut.Find queries.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        cut.Find("#select-regimen").Change(TherapeuticRegimen.FemaleToMale.ToString());
        Assert.Contains("1050.0 mg", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);

        cut.Find("#select-regimen").Change(TherapeuticRegimen.MaleToFemale.ToString());
        Assert.Contains("16.0 mg", cut.Markup);
        Assert.Contains("OPTIMAL CLINICAL EFFICACY", cut.Markup);
    }

    /// <summary>
    /// Verifies that clicking "Reset Defaults" restores initial clinical formulation values.
    /// </summary>
    [Fact]
    public void DepotStudio_ResetDefaults_RestoresInitialParameters()
    {
        // WHAT: Modifies input values and then clicks the reset defaults button.
        // WHY:  Allows clinicians to restore standard baseline values with a single action.
        // HOW:  Alters mass input, clicks reset button, and verifies fields reset to 16.0 mg.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        cut.Find("#input-initial-mass").Change("50.0");
        cut.Find("#btn-reset-depot-defaults").Click();

        Assert.Contains("16.0", cut.Markup);
    }

    /// <summary>
    /// Verifies that a severely under-dosed formulation triggers the suboptimal clinical verdict badge.
    /// </summary>
    [Fact]
    public void DepotStudio_SuboptimalFormulation_RendersSuboptimalVerdict()
    {
        // WHAT: Sets initial mass to an extremely low value (1.0 mg) causing premature depletion and inadequate TTR.
        // WHY:  Ensures that dangerous or ineffective depot formulations are flagged with warning/danger indicators.
        // HOW:  Sets initial mass to 1.0, clicks run, and asserts on suboptimal verdict text.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        cut.Find("#input-initial-mass").Change("1.0");
        cut.Find("#btn-run-depot-simulation").Click();

        Assert.Contains("SUBOPTIMAL FORMULATION", cut.Markup);
    }

    /// <summary>
    /// Verifies recording longitudinal serum assays, rendering observed SVG markers and recalibrated trajectory,
    /// triggering clinical safety alerts (e.g. FtM critical hematocrit), validating invalid inputs, and clearing assays.
    /// </summary>
    [Fact]
    public void DepotStudio_LongitudinalAssayCalibration_RendersOverlayAlertsAndValidation()
    {
        // WHAT: Exercises the Milestone 34 longitudinal calibration panel end-to-end in bUnit.
        // WHY:  Verifies assay recording, SVG marker projection, recalibrated curve overlay, safety alert callouts, and input validation.
        // HOW:  Uses fresh cut.Find(...) queries after each dispatch to test validation error, MtF optimal calibration,
        //       FtM critical hematocrit alert, and Clear Assays reset.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        // 1. Invalid day triggers validation banner
        cut.Find("#input-assay-day").Change("0");
        cut.Find("#btn-record-assay-recalibrate").Click();
        Assert.Contains("Day since implant must be between 1 and 365.", cut.Find("#assay-validation-error").TextContent);

        // 2. Valid MtF Day 30 and Day 60 assays render SVG recalibrated overlay, markers, and adaptive telemetry card
        cut.Find("#input-assay-day").Change("30");
        cut.Find("#input-assay-e2").Change("515");
        cut.Find("#btn-record-assay-recalibrate").Click();

        Assert.NotNull(cut.Find("#adaptive-calibration-card"));
        Assert.NotNull(cut.Find("#calibration-safety-badge"));
        Assert.NotNull(cut.Find("path[data-series='recalibrated']"));
        Assert.Single(cut.FindAll("circle.assay-marker"));
        Assert.Contains("Day 30", cut.Find("#recorded-assays-table").TextContent);

        cut.Find("#input-assay-day").Change("60");
        cut.Find("#input-assay-e2").Change("490");
        cut.Find("#btn-record-assay-recalibrate").Click();
        Assert.Equal(2, cut.FindAll("circle.assay-marker").Count);

        // 3. Clear Assays removes recalibration overlay and table
        cut.Find("#btn-clear-assays").Click();
        Assert.Empty(cut.FindAll("#adaptive-calibration-card"));
        Assert.Empty(cut.FindAll("circle.assay-marker"));

        // 4. Switch to FtM regimen and record critical hematocrit (>= 54.0%) -> CriticalSafety alert rendered
        cut.Find("#select-regimen").Change(TherapeuticRegimen.FemaleToMale.ToString());
        cut.Find("#input-assay-day").Change("60");
        cut.Find("#input-assay-t").Change("24.5");
        cut.Find("#input-assay-hct").Change("55.2");
        cut.Find("#btn-record-assay-recalibrate").Click();

        Assert.Contains("FTM_HEMATOCRIT_CRITICAL", cut.Find("#calibration-safety-alerts").TextContent);
        Assert.Contains("Critical Safety Ceiling", cut.Find("#calibration-safety-badge").TextContent);
    }

    /// <summary>
    /// Verifies the Milestone 35 Wet-Lab Batch Fabrication &amp; Cryptographic QC Lot Release workflow in bUnit:
    /// minting an adaptive lot, fail-closed quarantine on contaminated endotoxin reading, re-testing all 5 gates,
    /// and issuing a live-verified NIST FIPS 204 ML-DSA-65 Certificate of Analysis.
    /// </summary>
    [Fact]
    public void DepotStudio_WetLabLotFabricationAndQcRelease_QuarantinesThenSignsMlDsa65Certificate()
    {
        // WHAT: Drives the full Milestone 35 UI workflow in DepotStudio using fresh cut.Find queries.
        // WHY:  Verifies adaptive lot minting, 5-gate QC entry, quarantine blocking, and verified ML-DSA-65 CoA display.
        // HOW:  Mints a lot, submits a failing endotoxin reading and triggers quarantine, then submits all 5 passing gates
        //       and verifies the ML-DSA-65 SIGNATURE VERIFIED badge and SHA-256 certificate card render cleanly.
        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();

        Assert.NotNull(cut.Find("#wet-lab-lot-release-panel"));
        cut.Find("#btn-mint-fabrication-lot").Click();
        Assert.NotNull(cut.Find("#active-lot-summary"));
        Assert.Contains("lot_studio_test_1", cut.Find("#active-lot-summary").TextContent);

        // 1. Submit failing Endotoxin reading (0.85 EU/mL > 0.50 EU/mL) and verify fail-closed quarantine
        cut.Find("#select-qc-gate").Change(FabricationQcGateType.EndotoxinSterility.ToString());
        cut.Find("#input-qc-measured").Change("0.85");
        cut.Find("#btn-record-qc-gate").Click();
        Assert.Contains("FAIL", cut.Find("#lot-qc-checks-table").TextContent);

        cut.Find("#btn-release-lot").Click();
        Assert.NotNull(cut.Find("#lot-quarantine-banner"));
        Assert.Contains("Quarantined", cut.Find("#lot-status-badge").TextContent);

        // 2. Record passing readings for all 5 pharmaceutical QC gates
        cut.Find("#select-qc-gate").Change(FabricationQcGateType.GelFoldingYield.ToString());
        cut.Find("#input-qc-measured").Change("92.4");
        cut.Find("#btn-record-qc-gate").Click();

        cut.Find("#select-qc-gate").Change(FabricationQcGateType.EndotoxinSterility.ToString());
        cut.Find("#input-qc-measured").Change("0.12");
        cut.Find("#btn-record-qc-gate").Click();

        cut.Find("#select-qc-gate").Change(FabricationQcGateType.HydrodynamicSizePdi.ToString());
        cut.Find("#input-qc-measured").Change("61.5");
        cut.Find("#input-qc-secondary").Change("0.11");
        cut.Find("#btn-record-qc-gate").Click();

        cut.Find("#select-qc-gate").Change(FabricationQcGateType.PayloadEncapsulation.ToString());
        cut.Find("#input-qc-measured").Change("88.5");
        cut.Find("#input-qc-secondary").Change("1.2");
        cut.Find("#btn-record-qc-gate").Click();

        cut.Find("#select-qc-gate").Change(FabricationQcGateType.CpGMethylation.ToString());
        cut.Find("#input-qc-measured").Change("97.9");
        cut.Find("#btn-record-qc-gate").Click();

        // 3. Execute cryptographic release -> Released + Verified ML-DSA-65 Certificate of Analysis
        cut.Find("#btn-release-lot").Click();
        Assert.NotNull(cut.Find("#lot-certificate-card"));
        Assert.Contains("ML-DSA-65 SIGNATURE VERIFIED", cut.Find("#lot-certificate-verified-badge").TextContent);
        Assert.Contains("Released (ML-DSA-65 Signed)", cut.Find("#lot-status-badge").TextContent);

        // 4. Milestone 36: Copy CoA into verifier -> verifies TRUSTED FOR CLINICAL USE (LocalNode)
        cut.Find("#btn-load-active-coa").Click();
        cut.Find("#btn-verify-peer-certificate").Click();
        Assert.Contains("TRUSTED FOR CLINICAL USE", cut.Find("#peer-verify-trust-badge").TextContent);

        // 5. Permanently revoke lot -> shows Revoked banner and re-verification flags REVOKED
        cut.Find("#input-revoke-reason").Change("Cold-chain temperature excursion");
        cut.Find("#btn-revoke-lot").Click();
        Assert.Contains("Cold-chain temperature excursion", cut.Find("#lot-revoked-banner").TextContent);
        Assert.Contains("Revoked", cut.Find("#lot-status-badge").TextContent);

        cut.Find("#btn-verify-peer-certificate").Click();
        Assert.Contains("REVOKED", cut.Find("#peer-verify-trust-badge").TextContent);
    }

    /// <summary>
    /// Verifies Milestone 39 (P208) Automated Peer Endpoint Subscriptions UI inside <see cref="DepotStudio"/>:
    /// validation error handling, subscription creation, individual sync (success and failure), sweep-all, and removal.
    /// </summary>
    [Fact]
    public void DepotStudio_AutomatedPeerSyncPanel_SavesSyncsSweepsAndRemovesSubscriptions()
    {
        FederatedPeerSyncEndpointRecord sampleEndpoint = new FederatedPeerSyncEndpointRecord().GetExamples();
        _peerSyncMock
            .Setup(s => s.UpsertSubscriptionAsync(It.Is<UpsertFederatedPeerSyncEndpointRequest>(r => r.PeerId == "bad"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("SSRF policy rejected endpoint URL."));
        _peerSyncMock
            .Setup(s => s.UpsertSubscriptionAsync(It.Is<UpsertFederatedPeerSyncEndpointRequest>(r => r.PeerId == "clinic-zurich-01"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _peerSyncEndpoints.Clear();
                _peerSyncEndpoints.Add(sampleEndpoint);
                return sampleEndpoint;
            });
        _peerSyncMock
            .Setup(s => s.SyncPeerAsync("clinic-zurich-01", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FederatedPeerSyncRunResult().GetExamples());
        _peerSyncMock
            .Setup(s => s.SyncAllEnabledPeersAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FederatedPeerSyncSweepResult().GetExamples());
        _peerSyncMock
            .Setup(s => s.DeleteSubscriptionAsync("clinic-zurich-01", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _peerSyncEndpoints.Clear();
                return true;
            });

        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();
        Assert.NotNull(cut.Find("#automated-peer-sync-section"));
        Assert.NotNull(cut.Find("#peer-sync-empty"));

        // 1. Validation error displays alert banner
        cut.Find("#input-peer-sync-id").Change("bad");
        cut.Find("#btn-save-peer-sync").Click();
        Assert.Contains("SSRF policy rejected endpoint URL.", cut.Find("#peer-sync-error").TextContent);

        // 2. Valid save populates table
        cut.Find("#input-peer-sync-id").Change("clinic-zurich-01");
        cut.Find("#input-peer-sync-name").Change("Zurich Sovereign Origami Lab");
        cut.Find("#input-peer-sync-url").Change("https://lab-zurich.lantea.one");
        cut.Find("#input-peer-sync-key").Change("peer-mldsa65-zurich-2026");
        cut.Find("#btn-save-peer-sync").Click();
        Assert.Contains("Saved automated peer sync subscription", cut.Find("#peer-sync-status").TextContent);
        Assert.NotNull(cut.Find("#peer-sync-endpoints-table"));

        // 3. Individual Sync Now (success)
        cut.Find("#btn-run-peer-clinic-zurich-01").Click();
        Assert.Contains("Synchronized 1 new recall", cut.Find("#peer-sync-status").TextContent);

        // 4. Sync All Peers
        cut.Find("#btn-sync-all-peers").Click();
        Assert.Contains("Swept 1 peer(s)", cut.Find("#peer-sync-status").TextContent);

        // 5. Remove subscription restores empty state
        cut.Find("#btn-remove-peer-clinic-zurich-01").Click();
        Assert.Contains("Removed peer sync subscription", cut.Find("#peer-sync-status").TextContent);
        Assert.NotNull(cut.Find("#peer-sync-empty"));
    }

    /// <summary>
    /// Verifies Milestone 40 (P213) Cold-Chain Thermal Stability, Arrhenius Shelf-Life &amp; SHA-256 Chain-of-Custody
    /// Ledger interactions inside <see cref="DepotStudio"/>.
    /// </summary>
    [Fact]
    public void DepotStudio_ColdChainAndCustodyPanel_LogsExcursionsAppendsCustodyAndBlocksCompromisedAdministration()
    {
        // WHAT: Exercises the Milestone 40 #cold-chain-custody-panel inside DepotStudio using fresh cut.Find queries.
        // WHY:  Verifies Arrhenius stability badge updates, excursion logging, custody handoff chaining, and fail-closed administration blocks.
        // HOW:  Mints a lot, logs an advisory thermal interval, appends a custody handoff, then triggers a blocked PatientAdministration attempt.
        _coldChainMock
            .Setup(c => c.RecordThermalExcursionAsync(It.IsAny<string>(), It.IsAny<RecordThermalExcursionRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lotId, RecordThermalExcursionRequest req, string op, CancellationToken _) =>
            {
                bool catastrophic = req.TemperatureCelsius > 42.0;
                _coldChainStability = new LotStabilityReport
                {
                    LotId = lotId,
                    PatientId = "depot-studio-session",
                    RemainingPotencyPercent = catastrophic ? 72.50 : 99.64,
                    CumulativeThermalBudgetUsedPercent = catastrophic ? 305.00 : 3.42,
                    EstimatedRemainingShelfLifeDaysAtMinus20C = catastrophic ? 0.0 : 352.5,
                    EstimatedRemainingShelfLifeDaysAt4C = catastrophic ? 0.0 : 22.9,
                    Status = catastrophic ? ThermalStabilityStatus.CatastrophicDenaturation : ThermalStabilityStatus.Optimal,
                    HasCatastrophicExcursion = catastrophic,
                    CustodyChainValid = true,
                    IsSafeForImplantation = !catastrophic,
                    Summary = catastrophic
                        ? "Catastrophic dsDNA origami denaturation (> 42.0°C excursion): 72.50% potency remaining. Permanently unsafe for implantation."
                        : "Optimal cold-chain stability: 99.64% potency remaining (3.42% thermal budget used); custody chain verified."
                };

                return new ThermalExcursionResponse
                {
                    ExcursionId = "texc_bunit_01",
                    LotId = lotId,
                    PatientId = "depot-studio-session",
                    TemperatureCelsius = req.TemperatureCelsius,
                    DurationHours = req.DurationHours,
                    StorageTier = req.StorageTier,
                    SensorId = req.SensorId,
                    Notes = req.Notes,
                    RecordedBy = op,
                    RecordedAtUtc = DateTimeOffset.UtcNow,
                    StabilityAfterExcursion = _coldChainStability
                };
            });

        _coldChainMock
            .Setup(c => c.RecordCustodyHandoffAsync(It.IsAny<string>(), It.Is<RecordCustodyHandoffRequest>(r => r.Stage != CustodyStage.PatientAdministration), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lotId, RecordCustodyHandoffRequest req, string _, CancellationToken _) =>
            {
                var rec = new CustodyHandoffRecord
                {
                    HandoffId = "chnd_bunit_01",
                    LotId = lotId,
                    PatientId = "depot-studio-session",
                    Stage = req.Stage,
                    FromActor = req.FromActor,
                    ToActor = req.ToActor,
                    FacilityCode = req.FacilityCode,
                    TemperatureCelsius = req.TemperatureCelsius,
                    PotencyPercentAtHandoff = _coldChainStability.RemainingPotencyPercent,
                    ChainPrevSha256 = new string('0', 64),
                    EntrySha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                    RecordedAtUtc = DateTimeOffset.UtcNow
                };
                _coldChainHandoffs.Add(rec);
                return rec;
            });

        _coldChainMock
            .Setup(c => c.RecordCustodyHandoffAsync(It.IsAny<string>(), It.Is<RecordCustodyHandoffRequest>(r => r.Stage == CustodyStage.PatientAdministration), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Patient administration blocked fail-closed: Catastrophic dsDNA origami denaturation."));

        IRenderedComponent<DepotStudio> cut = Render<DepotStudio>();
        cut.Find("#btn-mint-fabrication-lot").Click();

        Assert.NotNull(cut.Find("#cold-chain-custody-panel"));
        Assert.NotNull(cut.Find("#cc-excursions-empty"));
        Assert.NotNull(cut.Find("#cc-custody-empty"));
        Assert.Contains("SAFE FOR CLINICAL IMPLANTATION", cut.Find("#cc-implantation-badge").TextContent);
        Assert.Contains("SHA-256 CUSTODY CHAIN VERIFIED", cut.Find("#cc-custody-chain-badge").TextContent);

        // 1. Log refrigerated interval -> populates excursions table and updates potency
        cut.Find("#select-cc-tier").Change(ThermalStorageTier.Refrigerated2To8.ToString());
        cut.Find("#input-cc-temp").Change("4.0");
        cut.Find("#input-cc-duration").Change("6.0");
        cut.Find("#input-cc-sensor").Change("NIST-PROBE-ZRH-04");
        cut.Find("#input-cc-notes").Change("Routine transfer to cold box");
        cut.Find("#btn-cc-log-excursion").Click();

        Assert.Contains("99.64%", cut.Find("#cc-remaining-potency").TextContent);
        Assert.Contains("NIST-PROBE-ZRH-04", cut.Find("#cc-excursions-table").TextContent);
        Assert.Contains("99.64% potency remaining", cut.Find("#cc-status").TextContent);

        // 2. Append WetLabRelease custody handoff -> populates custody table
        cut.Find("#select-cc-stage").Change(CustodyStage.WetLabRelease.ToString());
        cut.Find("#input-cc-from").Change("wetlab_lin");
        cut.Find("#input-cc-to").Change("courier_07");
        cut.Find("#input-cc-facility").Change("ZRH-LAB-01");
        cut.Find("#input-cc-handoff-temp").Change("-19.8");
        cut.Find("#btn-cc-record-handoff").Click();

        Assert.Contains("wetlab_lin", cut.Find("#cc-custody-table").TextContent);
        Assert.Contains("ZRH-LAB-01", cut.Find("#cc-custody-table").TextContent);
        Assert.Contains("Appended WetLabRelease custody handoff", cut.Find("#cc-status").TextContent);

        // 3. Log catastrophic 48°C excursion and attempt PatientAdministration -> blocked fail-closed!
        cut.Find("#select-cc-tier").Change(ThermalStorageTier.ThermalExcursion.ToString());
        cut.Find("#input-cc-temp").Change("48.0");
        cut.Find("#input-cc-duration").Change("1.5");
        cut.Find("#btn-cc-log-excursion").Click();

        Assert.Contains("IMPLANTATION BLOCKED", cut.Find("#cc-implantation-badge").TextContent);
        Assert.Contains("72.50%", cut.Find("#cc-remaining-potency").TextContent);

        cut.Find("#select-cc-stage").Change(CustodyStage.PatientAdministration.ToString());
        cut.Find("#btn-cc-record-handoff").Click();

        Assert.Contains("Patient administration blocked fail-closed", cut.Find("#cc-error").TextContent);
    }
}
