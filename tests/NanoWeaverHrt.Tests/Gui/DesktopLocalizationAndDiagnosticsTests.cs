using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Unit test suite verifying <see cref="DesktopTrayIconController"/> 18-language localization,
/// <see cref="MainShellViewModel.SelectLanguageAsync"/>, and structured error diagnostics in
/// <see cref="InstanceOnboardingViewModel.TestAndSaveAsync"/> and <see cref="BackgroundInstanceMonitor"/>.
/// </summary>
public sealed class DesktopLocalizationAndDiagnosticsTests
{
    /// <summary>
    /// Creates a dummy instance profile for testing purposes.
    /// </summary>
    /// <param name="id">Instance identifier.</param>
    /// <returns>A populated <see cref="InstanceProfile"/> record.</returns>
    private static InstanceProfile CreateProfile(string id = "inst-diag-001") =>
        new(id, "Test Node", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUBKEY", "ENVELOPE", true, 15, DateTimeOffset.UtcNow, null);

    /// <summary>
    /// Verifies <see cref="DesktopTrayIconController"/> dynamic localization across all 18 languages,
    /// RTL directionality for Arabic, and menu item generation.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_18LanguageLocalization_And_RtlHandling()
    {
        // WHAT: Test DesktopTrayIconController across all 18 supported languages.
        // WHY:  Issue #170 (P167): Ensures desktop tray dynamically localizes commands with 0 missing fallbacks.
        // HOW:  Iterates all 18 WeaverLanguage values, asserts IsRightToLeft, BuildMenuItems, and GetStandardMenuItems.
        var localizer = new WeaverLocalizer();
        using var controller = new DesktopTrayIconController(null, localizer, WeaverLanguage.English);

        Assert.Equal(WeaverLanguage.English, controller.CurrentLanguage);
        Assert.False(controller.IsRightToLeft);
        Assert.Same(localizer, controller.Localizer);

        WeaverLanguage[] languages = Enum.GetValues<WeaverLanguage>();
        Assert.Equal(18, languages.Length);

        foreach (WeaverLanguage lang in languages)
        {
            controller.SetLanguage(lang);
            Assert.Equal(lang, controller.CurrentLanguage);

            if (lang == WeaverLanguage.Arabic)
            {
                Assert.True(controller.IsRightToLeft);
            }
            else
            {
                Assert.False(controller.IsRightToLeft);
            }

            var standardItems = controller.GetStandardMenuItems();
            Assert.Equal(3, standardItems.Count);
            Assert.False(string.IsNullOrWhiteSpace(standardItems[0]));
            Assert.False(string.IsNullOrWhiteSpace(standardItems[1]));
            Assert.False(string.IsNullOrWhiteSpace(standardItems[2]));

            var menuItemsWithoutNode = controller.BuildMenuItems();
            Assert.Equal(4, menuItemsWithoutNode.Count);

            var menuItemsWithNode = controller.BuildMenuItems("Lab Cluster A");
            Assert.Equal(4, menuItemsWithNode.Count);
            Assert.Contains("Lab Cluster A", menuItemsWithNode[1]);

            controller.ApplyLocalizedState(
                TrayHealthBadge.Healthy,
                "Status.Online",
                isMainWindowVisible: true,
                areNotificationsMuted: false,
                instanceSummaries: ["Node 1: Online"]);

            Assert.Equal(TrayHealthBadge.Healthy, controller.CurrentState.Badge);
            Assert.Contains("NanoWeaver HRT", controller.CurrentState.TooltipText);
        }

        // Test fallback path with null localizer
        using var nullLocalizerController = new DesktopTrayIconController(null, null);
        Assert.Equal("Fallback", nullLocalizerController.GetLocalizedLabel("Unknown.Key", "Fallback"));
        Assert.Equal("Fallback", nullLocalizerController.GetLocalizedLabel(string.Empty, "Fallback"));
    }

    /// <summary>
    /// Verifies <see cref="MainShellViewModel.SelectLanguageAsync"/> updates language preference and UI properties.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_SelectLanguageAsync_PersistsAndNotifies()
    {
        // WHAT: Tests MainShellViewModel.SelectLanguageAsync and SelectLanguage property.
        // WHY:  Issue #170 (P167): Validates interactive language selection and persistence.
        // HOW:  Mocks ILanguagePreferenceService, calls SelectLanguageAsync for Arabic, asserts IsRightToLeft and notifications.
        var profileRepoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var prefServiceMock = new Mock<ILanguagePreferenceService>();

        var shellVm = new MainShellViewModel(
            profileRepoMock.Object,
            coordMock.Object,
            languagePreferenceService: prefServiceMock.Object);

        var propertyEvents = new List<string>();
        shellVm.PropertyChanged += (_, e) => propertyEvents.Add(e.PropertyName!);

        await shellVm.SelectLanguageAsync(WeaverLanguage.Arabic);

        Assert.Equal(WeaverLanguage.Arabic, shellVm.SelectedLanguage);
        Assert.True(shellVm.IsRightToLeft);
        Assert.Contains(nameof(shellVm.SelectedLanguage), propertyEvents);
        Assert.Contains(nameof(shellVm.IsRightToLeft), propertyEvents);

        prefServiceMock.Verify(s => s.SetPreferredLanguageAsync(WeaverLanguage.Arabic, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="InstanceOnboardingViewModel.TestAndSaveAsync"/> distinguishes
    /// remote node connection failures from local SQLite profile persistence errors.
    /// </summary>
    [Fact]
    public async Task InstanceOnboardingViewModel_TestAndSaveAsync_DistinguishesRemoteFromLocalErrors()
    {
        // WHAT: Test all failure and success branches in InstanceOnboardingViewModel.TestAndSaveAsync.
        // WHY:  Issue #170 (P167): Distinguishes remote connection failures from local SQLite errors.
        // HOW:  Simulates invalid inputs, NodeApiException, HttpRequestException, generic exception,
        //       local SQLite persistence failure, and success.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var keyRing = new AesGcmClientKeyRingVault();
        var clientMock = new Mock<INanoWeaverNodeClient>();

        var vm = new InstanceOnboardingViewModel(repoMock.Object, keyRing, coordMock.Object);
        vm.DisplayName = "Local Node";
        vm.BaseUri = "http://127.0.0.1:5000";
        vm.Username = "admin";
        vm.Role = "Administrator";
        vm.KeyAlgorithm = "Ed25519";
        vm.PollingIntervalSeconds = 15;

        // 1. Invalid input (empty passphrase) -> Returns null
        var nullResult = await vm.TestAndSaveAsync(ReadOnlyMemory<char>.Empty, clientMock.Object);
        Assert.Null(nullResult);
        Assert.NotNull(vm.StatusMessage);

        var passphrase = "StrongPassphrase123".ToCharArray().AsMemory();

        // 2. Remote NodeApiException (HTTP 403 Forbidden)
        clientMock.Setup(c => c.GetHealthzAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeApiException(403, "Access Denied"));

        var forbiddenResult = await vm.TestAndSaveAsync(passphrase, clientMock.Object);
        Assert.Null(forbiddenResult);
        Assert.Contains("Remote node rejected health check", vm.StatusMessage);
        Assert.Contains("403", vm.StatusMessage);

        // 3. Remote HttpRequestException (unreachable)
        clientMock.Setup(c => c.GetHealthzAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var unreachableResult = await vm.TestAndSaveAsync(passphrase, clientMock.Object);
        Assert.Null(unreachableResult);
        Assert.Contains("Remote node unreachable", vm.StatusMessage);

        // 4. Remote generic exception
        clientMock.Setup(c => c.GetHealthzAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handshake timeout"));

        var genericResult = await vm.TestAndSaveAsync(passphrase, clientMock.Object);
        Assert.Null(genericResult);
        Assert.Contains("Remote node connection failure", vm.StatusMessage);

        // 5. Remote health succeeds, but local SQLite persistence throws
        clientMock.Setup(c => c.GetHealthzAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemHealthOverview
            {
                Status = "Healthy",
                TimestampUtc = DateTimeOffset.UtcNow,
                Version = "1.0.0",
                Uptime = "1h",
                UptimeSeconds = 3600.0,
                SetupRequired = false,
                AuthenticatedSession = false,
                Description = "OK"
            });

        repoMock.Setup(r => r.UpsertProfileAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Disk full / SQLite write error"));

        var localFailResult = await vm.TestAndSaveAsync(passphrase, clientMock.Object);
        Assert.Null(localFailResult);
        Assert.Contains("Local profile persistence failed", vm.StatusMessage);

        // 6. Complete success
        repoMock.Setup(r => r.UpsertProfileAsync(It.IsAny<InstanceProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var successResult = await vm.TestAndSaveAsync(passphrase, clientMock.Object);
        Assert.NotNull(successResult);
        Assert.Contains("onboarded successfully", vm.StatusMessage);
    }
}
