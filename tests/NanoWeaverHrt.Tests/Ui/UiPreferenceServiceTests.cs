using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Ui;
using Xunit;

namespace NanoWeaverHrt.Tests.Ui;

/// <summary>
/// Unit tests verifying <see cref="UiPreferenceService"/> node defaults, user override resolution,
/// 21 CFR Part 11 audit events, role-tailored Task Hub/navigation catalogs, and <see cref="UserSessionState"/> display mode state.
/// </summary>
public sealed class UiPreferenceServiceTests
{
    /// <summary>
    /// Verifies constructor guards reject null repository, configuration, or audit dependencies.
    /// </summary>
    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        // WHAT: Invokes UiPreferenceService constructor with null parameters.
        // WHY:  Ensures fail-fast dependency validation at DI resolution time.
        // HOW:  Passes null sequentially for each constructor parameter and asserts ArgumentNullException.
        IUserUiPreferenceRepository repo = Mock.Of<IUserUiPreferenceRepository>();
        IConfigurationRepository cfg = Mock.Of<IConfigurationRepository>();
        IComplianceAuditService audit = Mock.Of<IComplianceAuditService>();

        Assert.Throws<ArgumentNullException>(() => new UiPreferenceService(null!, cfg, audit));
        Assert.Throws<ArgumentNullException>(() => new UiPreferenceService(repo, null!, audit));
        Assert.Throws<ArgumentNullException>(() => new UiPreferenceService(repo, cfg, null!));
    }

    /// <summary>
    /// Verifies <see cref="UiPreferenceService.GetNodeDefaultDisplayModeAsync"/> parses configured values and falls back safely.
    /// </summary>
    /// <param name="configuredValue">Raw value returned from node configuration.</param>
    /// <param name="expectedMode">Expected parsed <see cref="UiDisplayMode"/>.</param>
    [Theory]
    [InlineData("Simple", UiDisplayMode.Simple)]
    [InlineData("Advanced", UiDisplayMode.Advanced)]
    [InlineData("  advanced ", UiDisplayMode.Advanced)]
    [InlineData("unknown_mode", UiDisplayMode.Simple)]
    [InlineData(null, UiDisplayMode.Simple)]
    public async Task GetNodeDefaultDisplayModeAsync_ParsesConfigAndFallsBackToSimple(
        string? configuredValue,
        UiDisplayMode expectedMode)
    {
        // WHAT: Tests node-wide Ui:DefaultDisplayMode resolution across valid, padded, invalid, and null strings.
        // WHY:  Covers every null-conditional and equality branch in GetNodeDefaultDisplayModeAsync.
        // HOW:  Mocks IConfigurationRepository.GetValueAsync and asserts the resolved UiDisplayMode.
        Mock<IConfigurationRepository> cfgMock = new();
        cfgMock.Setup(c => c.GetValueAsync(
                UiPreferenceService.DefaultDisplayModeConfigKey,
                nameof(UiDisplayMode.Simple),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuredValue!);

        UiPreferenceService svc = new(
            Mock.Of<IUserUiPreferenceRepository>(),
            cfgMock.Object,
            Mock.Of<IComplianceAuditService>());

        UiDisplayMode resolved = await svc.GetNodeDefaultDisplayModeAsync();
        Assert.Equal(expectedMode, resolved);
    }

    /// <summary>
    /// Verifies effective preference resolution, personal override mutations, resets, audit logging, and argument checks.
    /// </summary>
    [Fact]
    public async Task EffectiveSetAndResetPreferences_MergeNodeDefaultAuditChangesAndValidateInputs()
    {
        // WHAT: Exercises GetEffectivePreferenceAsync, SetUserDisplayModeAsync, and ResetUserDisplayModeAsync.
        // WHY:  Verifies user overrides take precedence over Ui:DefaultDisplayMode, mutations emit audits, and resets restore defaults.
        // HOW:  Configures repository/config/audit mocks across missing-row fallback, existing override, upsert, reset, and invalid inputs.
        Mock<IUserUiPreferenceRepository> repoMock = new();
        Mock<IConfigurationRepository> cfgMock = new();
        Mock<IComplianceAuditService> auditMock = new();

        cfgMock.Setup(c => c.GetValueAsync(
                UiPreferenceService.DefaultDisplayModeConfigKey,
                nameof(UiDisplayMode.Simple),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Advanced");

        DateTimeOffset savedTime = new(2026, 11, 24, 21, 0, 0, TimeSpan.Zero);
        repoMock.Setup(r => r.GetByUsernameAsync("no_override", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserUiPreferenceRecord?)null);
        repoMock.Setup(r => r.GetByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserUiPreferenceRecord
            {
                Username = "alice",
                DisplayMode = UiDisplayMode.Simple,
                UpdatedAtUtc = savedTime
            });
        repoMock.Setup(r => r.UpsertAsync("alice", UiDisplayMode.Advanced, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserUiPreferenceRecord
            {
                Username = "alice",
                DisplayMode = UiDisplayMode.Advanced,
                UpdatedAtUtc = savedTime
            });
        repoMock.Setup(r => r.DeleteByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        UiPreferenceService svc = new(repoMock.Object, cfgMock.Object, auditMock.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.GetEffectivePreferenceAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.GetEffectivePreferenceAsync("  "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.SetUserDisplayModeAsync(null!, UiDisplayMode.Simple));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetUserDisplayModeAsync(" ", UiDisplayMode.Simple));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.SetUserDisplayModeAsync("alice", (UiDisplayMode)99));
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.ResetUserDisplayModeAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ResetUserDisplayModeAsync(""));

        EffectiveUiPreference inherited = await svc.GetEffectivePreferenceAsync("no_override");
        Assert.Equal("no_override", inherited.Username);
        Assert.Equal(UiDisplayMode.Advanced, inherited.DisplayMode);
        Assert.Equal(UiDisplayMode.Advanced, inherited.NodeDefaultDisplayMode);
        Assert.False(inherited.IsUserOverride);
        Assert.Null(inherited.UpdatedAtUtc);

        EffectiveUiPreference overridden = await svc.GetEffectivePreferenceAsync("alice");
        Assert.Equal(UiDisplayMode.Simple, overridden.DisplayMode);
        Assert.Equal(UiDisplayMode.Advanced, overridden.NodeDefaultDisplayMode);
        Assert.True(overridden.IsUserOverride);
        Assert.Equal(savedTime, overridden.UpdatedAtUtc);

        EffectiveUiPreference updated = await svc.SetUserDisplayModeAsync("alice", UiDisplayMode.Advanced);
        Assert.Equal(UiDisplayMode.Advanced, updated.DisplayMode);
        Assert.True(updated.IsUserOverride);
        auditMock.Verify(a => a.LogAction("alice", "UI_DISPLAY_MODE_CHANGED", "ui/preferences", "{\"displayMode\":\"Advanced\"}"), Times.Once);

        EffectiveUiPreference reset = await svc.ResetUserDisplayModeAsync("  ALICE ");
        Assert.Equal("alice", reset.Username);
        Assert.Equal(UiDisplayMode.Advanced, reset.DisplayMode);
        Assert.False(reset.IsUserOverride);
        Assert.Null(reset.UpdatedAtUtc);
        auditMock.Verify(a => a.LogAction("alice", "UI_DISPLAY_MODE_RESET", "ui/preferences", "{\"resetToNodeDefault\":true}"), Times.Once);
    }

    /// <summary>
    /// Verifies role-tailored Task Hub cards and grouped Simple Mode navigation menus for every role and display mode.
    /// </summary>
    [Fact]
    public void RoleCatalogs_TaskCardsAndNavGroups_TailoredByRoleAndDisplayMode()
    {
        // WHAT: Evaluates GetTaskCardsForRole and GetNavigationGroupsForRole across Patient, Researcher, Admin, and unknown enum fallback.
        // WHY:  Ensures Patients never see admin/lab links, while Researchers/Admins unlock extra shortcuts in Advanced Display Mode.
        // HOW:  Inspects returned card IDs and navigation group routes under Simple and Advanced display modes.
        UiPreferenceService svc = new(
            Mock.Of<IUserUiPreferenceRepository>(),
            Mock.Of<IConfigurationRepository>(),
            Mock.Of<IComplianceAuditService>());

        IReadOnlyList<UiTaskCardDescriptor> patientSimple = svc.GetTaskCardsForRole(UserRole.Patient, UiDisplayMode.Simple);
        Assert.Equal(2, patientSimple.Count);
        Assert.DoesNotContain(patientSimple, c => c.RequiresAdvancedDisplay);

        IReadOnlyList<UiTaskCardDescriptor> patientAdvanced = svc.GetTaskCardsForRole(UserRole.Patient, UiDisplayMode.Advanced);
        Assert.Equal(3, patientAdvanced.Count);
        Assert.Contains(patientAdvanced, c => c.TaskId == "endocrine");

        IReadOnlyList<UiTaskCardDescriptor> researcherSimple = svc.GetTaskCardsForRole(UserRole.Researcher, UiDisplayMode.Simple);
        Assert.Equal(4, researcherSimple.Count);
        Assert.DoesNotContain(researcherSimple, c => c.TaskId == "users");

        IReadOnlyList<UiTaskCardDescriptor> researcherAdvanced = svc.GetTaskCardsForRole(UserRole.Researcher, UiDisplayMode.Advanced);
        Assert.Equal(7, researcherAdvanced.Count);

        IReadOnlyList<UiTaskCardDescriptor> adminSimple = svc.GetTaskCardsForRole(UserRole.Administrator, UiDisplayMode.Simple);
        Assert.Equal(4, adminSimple.Count);
        Assert.Contains(adminSimple, c => c.TaskId == "users");

        IReadOnlyList<UiTaskCardDescriptor> adminAdvanced = svc.GetTaskCardsForRole(UserRole.Administrator, UiDisplayMode.Advanced);
        Assert.Equal(9, adminAdvanced.Count);

        IReadOnlyList<UiTaskCardDescriptor> unknownRoleSimple = svc.GetTaskCardsForRole((UserRole)777, UiDisplayMode.Simple);
        Assert.Equal(2, unknownRoleSimple.Count);

        IReadOnlyList<UiNavGroupDescriptor> patientNav = svc.GetNavigationGroupsForRole(UserRole.Patient);
        Assert.Single(patientNav);
        Assert.Equal("my-care", patientNav[0].GroupId);

        IReadOnlyList<UiNavGroupDescriptor> researcherNav = svc.GetNavigationGroupsForRole(UserRole.Researcher);
        Assert.Equal(3, researcherNav.Count);

        IReadOnlyList<UiNavGroupDescriptor> adminNav = svc.GetNavigationGroupsForRole(UserRole.Administrator);
        Assert.Equal(4, adminNav.Count);
        Assert.Contains(adminNav, g => g.GroupId == "administration");

        IReadOnlyList<UiNavGroupDescriptor> unknownNav = svc.GetNavigationGroupsForRole((UserRole)777);
        Assert.Single(unknownNav);
    }

    /// <summary>
    /// Verifies <see cref="UserSessionState"/> defaults to <see cref="UiDisplayMode.Simple"/>, notifies listeners
    /// on <see cref="UserSessionState.SetDisplayMode"/>, and resets back to Simple upon <see cref="UserSessionState.SignOut"/>.
    /// </summary>
    [Fact]
    public void UserSessionState_DisplayMode_TransitionsAndResetsOnSignOut()
    {
        // WHAT: Exercises DisplayMode, IsAdvancedDisplay, SetDisplayMode, and SignOut reset in UserSessionState.
        // WHY:  Prevents one user's Advanced Display session from leaking to a subsequent sign-in on the same terminal.
        // HOW:  Tracks OnChange notifications across SetUser, SetDisplayMode(Advanced), and SignOut.
        UserSessionState session = new();
        int changeNotifications = 0;
        session.OnChange += () => changeNotifications++;

        Assert.Equal(UiDisplayMode.Simple, session.DisplayMode);
        Assert.False(session.IsAdvancedDisplay);

        session.SetDisplayMode(UiDisplayMode.Advanced);
        Assert.Equal(UiDisplayMode.Advanced, session.DisplayMode);
        Assert.True(session.IsAdvancedDisplay);
        Assert.Equal(1, changeNotifications);

        session.SignOut();
        Assert.Equal(UiDisplayMode.Simple, session.DisplayMode);
        Assert.False(session.IsAdvancedDisplay);
        Assert.Equal(2, changeNotifications);
    }
}
