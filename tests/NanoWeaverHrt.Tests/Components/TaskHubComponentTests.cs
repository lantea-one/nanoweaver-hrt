using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Ui;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// bUnit component tests for <see cref="TaskHub"/> (<c>@page "/"</c>) and progressive disclosure
/// behavior in <see cref="SimulationStudio"/> across Simple and Advanced UI display modes.
/// </summary>
public sealed class TaskHubComponentTests : BunitContext
{
    /// <summary>
    /// Mock preference repository used when backing the catalog service.
    /// </summary>
    private readonly Mock<IUserUiPreferenceRepository> _prefRepoMock = new();

    /// <summary>
    /// Mock preference service supplying role-filtered tasks and persisting toggles.
    /// </summary>
    private readonly Mock<IUiPreferenceService> _prefServiceMock = new();

    /// <summary>
    /// Scoped user session state controlling signed-in role and active display mode.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskHubComponentTests"/> class.
    /// </summary>
    public TaskHubComponentTests()
    {
        // WHAT: Registers real catalog logic with lightweight preference persistence mocks.
        // WHY:  Tests role-filtered Task Hub grids, navigation routing, and display toggles deterministically.
        // HOW:  Configures IUiPreferenceService, IWeaverLocalizer, UserSessionState, and SimulationStudio dependencies.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Mock<IConfigurationRepository> cfgMock = new();
        cfgMock.Setup(c => c.GetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Simple");

        UiPreferenceService catalog = new(
            _prefRepoMock.Object,
            cfgMock.Object,
            Mock.Of<IComplianceAuditService>());

        _prefServiceMock
            .Setup(p => p.GetTaskCardsForRole(It.IsAny<UserRole>(), It.IsAny<UiDisplayMode>()))
            .Returns((UserRole role, UiDisplayMode mode) => catalog.GetTaskCardsForRole(role, mode));

        _prefServiceMock
            .Setup(p => p.SetUserDisplayModeAsync(It.IsAny<string>(), It.IsAny<UiDisplayMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string username, UiDisplayMode mode, CancellationToken _) => new EffectiveUiPreference
            {
                Username = username,
                DisplayMode = mode,
                IsUserOverride = true,
                NodeDefaultDisplayMode = UiDisplayMode.Simple,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        Services.AddSingleton(_prefServiceMock.Object);
        Services.AddSingleton(_sessionState);
        Services.AddSingleton<IWeaverLocalizer, WeaverLocalizer>();
        Services.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        Services.AddSingleton<ISimulationCampaignService, SimulationCampaignService>();
    }

    /// <summary>
    /// Verifies that an unauthenticated circuit renders nothing on <see cref="TaskHub"/>.
    /// </summary>
    [Fact]
    public void TaskHub_WhenUnauthenticated_RendersNothing()
    {
        // WHAT: Renders TaskHub with no authenticated user in UserSessionState.
        // WHY:  Prevents unauthenticated visitors from viewing operational task cards.
        // HOW:  Mounts TaskHub without SetUser and asserts empty primary and advanced task grids.
        IRenderedComponent<TaskHub> cut = Render<TaskHub>();
        Assert.Empty(cut.FindAll("#taskhub-primary-grid"));
        Assert.Empty(cut.FindAll("#taskhub-advanced-grid"));
    }

    /// <summary>
    /// Verifies Patient, Researcher, and Administrator role-tailored task cards in Simple Mode vs. Advanced Display,
    /// card navigation routing, and inline display mode toggling.
    /// </summary>
    [Fact]
    public async Task TaskHub_RoleTailoredCardsNavigationAndToggle_WorkAsExpected()
    {
        // WHAT: Exercises Patient, Researcher, and Administrator cards, routing clicks, and Simple/Advanced toggle.
        // WHY:  Confirms newcomers see calm role-appropriate tasks first while Advanced unlocks diagnostics on demand.
        // HOW:  Mounts TaskHub with each UserRole and UiDisplayMode and verifies rendered card counts and navigation URIs.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();

        // 1. Patient in Simple Mode -> only 2 calm cards (DNA records + Profile) and no advanced shortcuts
        _sessionState.SetUser(new UserAccount { Username = "pat1", Role = UserRole.Patient });
        IRenderedComponent<TaskHub> patCut = Render<TaskHub>(p => p
            .AddCascadingValue("UiDisplayMode", UiDisplayMode.Simple));

        Assert.Equal(2, patCut.FindAll("#taskhub-primary-grid .taskhub-card").Count);
        Assert.Empty(patCut.FindAll("#taskhub-advanced-grid"));
        patCut.Find("#btn-task-dna").Click();
        Assert.Equal("http://localhost/dna", nav.Uri);

        // 2. Researcher in Simple Mode -> 4 primary cards, hidden advanced section
        _sessionState.SetUser(new UserAccount { Username = "res1", Role = UserRole.Researcher });
        IRenderedComponent<TaskHub> resCut = Render<TaskHub>(p => p
            .AddCascadingValue("UiDisplayMode", UiDisplayMode.Simple));

        Assert.Equal(4, resCut.FindAll("#taskhub-primary-grid .taskhub-card").Count);
        Assert.Empty(resCut.FindAll("#taskhub-advanced-grid"));
        resCut.Find("#btn-task-wizard").Click();
        Assert.Equal("http://localhost/wizard", nav.Uri);

        // Toggle Simple -> Advanced from TaskHub hero bar
        await resCut.Find("#btn-taskhub-toggle-mode").ClickAsync();
        _prefServiceMock.Verify(p => p.SetUserDisplayModeAsync("res1", UiDisplayMode.Advanced, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(UiDisplayMode.Advanced, _sessionState.DisplayMode);

        // 3. Administrator in Advanced Display -> 4 primary cards + 5 advanced shortcuts + Advanced -> Simple toggle
        _sessionState.SetUser(new UserAccount { Username = "adm1", Role = UserRole.Administrator });
        IRenderedComponent<TaskHub> admCut = Render<TaskHub>(p => p
            .AddCascadingValue("UiDisplayMode", UiDisplayMode.Advanced));

        Assert.Equal(4, admCut.FindAll("#taskhub-primary-grid .taskhub-card").Count);
        Assert.Equal(5, admCut.FindAll("#taskhub-advanced-grid .taskhub-card").Count);
        admCut.Find("#btn-task-services").Click();
        Assert.Equal("http://localhost/services", nav.Uri);

        await admCut.Find("#btn-taskhub-toggle-mode").ClickAsync();
        _prefServiceMock.Verify(p => p.SetUserDisplayModeAsync("adm1", UiDisplayMode.Simple, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(UiDisplayMode.Simple, _sessionState.DisplayMode);
    }

    /// <summary>
    /// Verifies <see cref="SimulationStudio"/> collapses biophysical telemetry metrics in Simple Mode and expands
    /// them automatically when Advanced Display is active.
    /// </summary>
    [Fact]
    public void SimulationStudio_TelemetryDisclosure_FollowsUiDisplayMode()
    {
        // WHAT: Renders SimulationStudio under UiDisplayMode.Simple and UiDisplayMode.Advanced.
        // WHY:  Milestone 47 (P248): Keeps simulation prompt uncluttered by default while expanding metrics in Advanced Mode.
        // HOW:  Inspects #simulation-telemetry-disclosure open attribute under both CascadingValue settings.
        IRenderedComponent<SimulationStudio> simpleCut = Render<SimulationStudio>(p => p
            .AddCascadingValue("UiDisplayMode", UiDisplayMode.Simple));
        var simpleDetails = simpleCut.Find("#simulation-telemetry-disclosure");
        Assert.False(simpleDetails.HasAttribute("open"));

        IRenderedComponent<SimulationStudio> advCut = Render<SimulationStudio>(p => p
            .AddCascadingValue("UiDisplayMode", UiDisplayMode.Advanced));
        var advDetails = advCut.Find("#simulation-telemetry-disclosure");
        Assert.True(advDetails.HasAttribute("open"));
    }
}
