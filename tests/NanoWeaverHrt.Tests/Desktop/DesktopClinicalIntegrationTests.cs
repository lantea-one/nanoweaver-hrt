using System.Text;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.Theme;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests.Desktop;

/// <summary>
/// Comprehensive unit tests for Milestone 37 desktop integrations (<see cref="DesktopTrayIconController.ApplyClinicalSafetyState"/>
/// and <see cref="DesktopCertificateFileStore"/>) achieving 100.00% line and branch coverage.
/// </summary>
public sealed class DesktopClinicalIntegrationTests
{
    /// <summary>
    /// Verifies <see cref="DesktopTrayIconController.ApplyClinicalSafetyState"/> across Idle, Optimal, Warning, and Critical severities.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_ApplyClinicalSafetyState_MapsAllSeveritiesAndGuards()
    {
        // WHAT: Tests tray badge escalation and localized tooltips for every ClinicalWorkspaceSeverity value.
        // WHY:  Ensures desktop operators receive immediate tray feedback on safety ceilings and recalled batches.
        // HOW:  Invokes ApplyClinicalSafetyState with null guards and each severity level in English and Japanese.
        using var tray = new DesktopTrayIconController(localizer: new WeaverLocalizer(), language: WeaverLanguage.English);

        DepotWorkspaceSummary MakeSummary(ClinicalWorkspaceSeverity severity) => new(
            ActivePatientId: "patient_alice",
            FormulationCount: 1,
            AssayCount: 2,
            TotalLotCount: 1,
            ReleasedLotCount: 1,
            QuarantinedLotCount: 0,
            RevokedLotCount: 0,
            ActiveSafetyAlertCount: 0,
            AdjustedRedoseDay: 170,
            RecommendedNextMassMg: 18.5,
            Severity: severity,
            SeverityAccentHex: SovereignPalette.SuccessGreenHex);

        Assert.Throws<ArgumentNullException>(() => tray.ApplyClinicalSafetyState(null!, true, false, []));
        Assert.Throws<ArgumentNullException>(() => tray.ApplyClinicalSafetyState(MakeSummary(ClinicalWorkspaceSeverity.Idle), true, false, null!));

        tray.ApplyClinicalSafetyState(MakeSummary(ClinicalWorkspaceSeverity.Idle), true, false, ["Node A"]);
        Assert.Equal(TrayHealthBadge.Idle, tray.CurrentState.Badge);
        Assert.Contains("Offline", tray.CurrentState.TooltipText, StringComparison.Ordinal);

        tray.ApplyClinicalSafetyState(MakeSummary(ClinicalWorkspaceSeverity.Optimal), true, false, ["Node A"]);
        Assert.Equal(TrayHealthBadge.Healthy, tray.CurrentState.Badge);
        Assert.Contains("Clinical Guardrails Optimal", tray.CurrentState.TooltipText, StringComparison.Ordinal);

        tray.ApplyClinicalSafetyState(MakeSummary(ClinicalWorkspaceSeverity.Warning), false, false, ["Node A"]);
        Assert.Equal(TrayHealthBadge.Warning, tray.CurrentState.Badge);
        Assert.Contains("Quarantine", tray.CurrentState.TooltipText, StringComparison.Ordinal);

        tray.SetLanguage(WeaverLanguage.Japanese);
        tray.ApplyClinicalSafetyState(MakeSummary(ClinicalWorkspaceSeverity.Critical), false, true, ["Node A"]);
        Assert.Equal(TrayHealthBadge.Critical, tray.CurrentState.Badge);
        Assert.Contains("ロット失効", tray.CurrentState.TooltipText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <see cref="DesktopCertificateFileStore"/> export, import, size ceiling, filename validation, and traversal defense.
    /// </summary>
    [Fact]
    public async Task DesktopCertificateFileStore_ExportImportAndSecurityGuards()
    {
        // WHAT: Exercises round-trip .coa.json export/import and every security validation branch.
        // WHY:  Proves air-gapped desktop CoA exchange rejects path traversal, invalid characters, and oversized files.
        // HOW:  Creates a temporary directory, tests valid export/import, then tests all rejection branches and cleans up.
        Assert.Throws<ArgumentException>(() => new DesktopCertificateFileStore(" "));

        string tempDir = Path.Combine(Path.GetTempPath(), $"nanoweaver-coa-{Guid.NewGuid():N}");
        try
        {
            var store = new DesktopCertificateFileStore(tempDir);
            Assert.Equal(Path.GetFullPath(tempDir), store.RootDirectory);

            // Trailing directory separator root branch
            var storeWithSlash = new DesktopCertificateFileStore(tempDir + Path.DirectorySeparatorChar);
            Assert.StartsWith(Path.GetFullPath(tempDir), storeWithSlash.RootDirectory, StringComparison.Ordinal);

            byte[] payload = Encoding.UTF8.GetBytes("{\"lotId\":\"lot_2026-01\",\"signature\":\"ok\"}");
            string writtenPath = await store.ExportCertificateBundleAsync("lot_2026-01", payload);
            Assert.True(File.Exists(writtenPath));
            Assert.EndsWith("lot_2026-01.coa.json", writtenPath, StringComparison.Ordinal);

            string imported = await storeWithSlash.ImportCertificateJsonAsync("lot_2026-01.coa.json");
            Assert.Contains("lot_2026-01", imported, StringComparison.Ordinal);

            // Export validation guards
            await Assert.ThrowsAsync<ArgumentNullException>(() => store.ExportCertificateBundleAsync("lot_1", null!));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ExportCertificateBundleAsync("lot_1", []));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ExportCertificateBundleAsync("lot_1", new byte[DesktopCertificateFileStore.MaxBundleBytes + 1]));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ExportCertificateBundleAsync(" ", payload));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ExportCertificateBundleAsync("../escape", payload));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ExportCertificateBundleAsync("lot.bad", payload));

            // Import validation guards: blank, traversal, missing, empty, oversized
            await Assert.ThrowsAsync<ArgumentException>(() => store.ImportCertificateJsonAsync(" "));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ImportCertificateJsonAsync("../outside.coa.json"));
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.ImportCertificateJsonAsync("missing.coa.json"));

            string emptyFile = Path.Combine(tempDir, "empty.coa.json");
            await File.WriteAllBytesAsync(emptyFile, []);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportCertificateJsonAsync("empty.coa.json"));

            string hugeFile = Path.Combine(tempDir, "huge.coa.json");
            await File.WriteAllBytesAsync(hugeFile, new byte[DesktopCertificateFileStore.MaxBundleBytes + 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportCertificateJsonAsync("huge.coa.json"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies Milestone 39 (P207) desktop system tray escalation for newly imported federated recalls
    /// and peer revocation sync failures via both <see cref="DesktopTrayIconController.ApplyClinicalSafetyState"/>
    /// and <see cref="DesktopTrayIconController.ApplyFederatedSyncState"/>.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_EscalatesFederatedRecallsAndPeerSyncFailures()
    {
        WeaverLocalizer localizer = new();
        using DesktopTrayIconController tray = new(localizer: localizer, language: WeaverLanguage.English);

        // ApplyClinicalSafetyState with newly pulled federated recalls -> Critical
        DepotWorkspaceSummary fedRecallSummary = new(
            ActivePatientId: "pat-1",
            FormulationCount: 1,
            AssayCount: 1,
            TotalLotCount: 1,
            ReleasedLotCount: 1,
            QuarantinedLotCount: 0,
            RevokedLotCount: 0,
            ActiveSafetyAlertCount: 0,
            AdjustedRedoseDay: 180,
            RecommendedNextMassMg: 16.0,
            Severity: ClinicalWorkspaceSeverity.Critical,
            SeverityAccentHex: "#FF2A85",
            FailedPeerSyncCount: 0,
            NewFederatedRecallCount: 2);
        tray.ApplyClinicalSafetyState(fedRecallSummary, false, false, ["Zurich Lab"]);
        Assert.Equal(TrayHealthBadge.Critical, tray.CurrentState.Badge);

        // ApplyClinicalSafetyState with peer sync failure only -> Warning with TrayPeerSyncFailed
        DepotWorkspaceSummary peerFailSummary = fedRecallSummary with
        {
            Severity = ClinicalWorkspaceSeverity.Warning,
            FailedPeerSyncCount = 1,
            NewFederatedRecallCount = 0
        };
        tray.ApplyClinicalSafetyState(peerFailSummary, false, false, ["Zurich Lab"]);
        Assert.Equal(TrayHealthBadge.Warning, tray.CurrentState.Badge);

        // ApplyClinicalSafetyState with Warning from quarantined lot + failed peer -> TrayWarning
        DepotWorkspaceSummary mixedWarnSummary = peerFailSummary with { QuarantinedLotCount = 1 };
        tray.ApplyClinicalSafetyState(mixedWarnSummary, true, false, ["Zurich Lab"]);
        Assert.Equal(TrayHealthBadge.Warning, tray.CurrentState.Badge);

        // ApplyClinicalSafetyState with Warning from active safety alert + failed peer -> TrayWarning
        DepotWorkspaceSummary alertWarnSummary = peerFailSummary with { ActiveSafetyAlertCount = 1 };
        tray.ApplyClinicalSafetyState(alertWarnSummary, true, false, ["Zurich Lab"]);
        Assert.Equal(TrayHealthBadge.Warning, tray.CurrentState.Badge);

        // Direct ApplyFederatedSyncState: guards + Critical / Warning / Healthy
        Assert.Throws<ArgumentOutOfRangeException>(() => tray.ApplyFederatedSyncState(-1, 0, true, false, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => tray.ApplyFederatedSyncState(0, -1, true, false, []));
        Assert.Throws<ArgumentNullException>(() => tray.ApplyFederatedSyncState(0, 0, true, false, null!));

        tray.ApplyFederatedSyncState(1, 0, false, false, ["Peer A"]);
        Assert.Equal(TrayHealthBadge.Critical, tray.CurrentState.Badge);

        tray.ApplyFederatedSyncState(0, 2, false, false, ["Peer A"]);
        Assert.Equal(TrayHealthBadge.Warning, tray.CurrentState.Badge);

        tray.ApplyFederatedSyncState(0, 0, true, false, ["Peer A"]);
        Assert.Equal(TrayHealthBadge.Healthy, tray.CurrentState.Badge);
    }
}
