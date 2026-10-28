using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Components.Cohorts;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying component rendering, user interactions, cohort filtering,
/// and access delegation in <see cref="CohortManagementModal"/>, <see cref="UserManagement"/>,
/// and <see cref="DnaManagementPage"/>.
/// </summary>
public sealed class CohortManagementComponentTests : TestContext
{
    /// <summary>
    /// Mock subject access evaluation and lifecycle service.
    /// </summary>
    private readonly Mock<ISubjectAccessService> _accessServiceMock = new();

    /// <summary>
    /// Mock subject access repository for raw data lookups.
    /// </summary>
    private readonly Mock<ISubjectAccessRepository> _accessRepoMock = new();

    /// <summary>
    /// Mock user repository supplying user accounts.
    /// </summary>
    private readonly Mock<IUserRepository> _userRepoMock = new();

    /// <summary>
    /// Mock authentication service managing identities and credentials.
    /// </summary>
    private readonly Mock<IUserAuthenticationService> _authServiceMock = new();

    /// <summary>
    /// Mock patient DNA repository for blueprints, imports, and analytical results.
    /// </summary>
    private readonly Mock<IPatientDnaRepository> _dnaRepoMock = new();

    /// <summary>
    /// Mock user profile repository for profile lookups.
    /// </summary>
    private readonly Mock<IUserProfileRepository> _profileRepoMock = new();

    /// <summary>
    /// Mock fabrication service.
    /// </summary>
    private readonly Mock<IFabricationService> _fabServiceMock = new();

    /// <summary>
    /// Mock pharmacogenomics service.
    /// </summary>
    private readonly Mock<IPharmacogenomicsService> _genomicsServiceMock = new();

    /// <summary>
    /// In-memory session state representing the authenticated identity.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Test administrator account.
    /// </summary>
    private readonly UserAccount _adminUser = new()
    {
        Username = "admin_master",
        Role = UserRole.Administrator,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1111111111111111111111111111111111111111111==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Test researcher account.
    /// </summary>
    private readonly UserAccount _researcherUser = new()
    {
        Username = "researcher_watson",
        Role = UserRole.Researcher,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA2222222222222222222222222222222222222222222==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Test patient accounts.
    /// </summary>
    private readonly UserAccount _patientAlice = new()
    {
        Username = "patient_alice",
        Role = UserRole.Patient,
        AccessGate = AccessGate.Web,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA3333333333333333333333333333333333333333333==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Second test patient account.
    /// </summary>
    private readonly UserAccount _patientBob = new()
    {
        Username = "patient_bob",
        Role = UserRole.Patient,
        AccessGate = AccessGate.Web,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA4444444444444444444444444444444444444444444==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Sample test cohort group.
    /// </summary>
    private readonly SubjectGroup _testGroup = new()
    {
        Id = "grp_test_cohort",
        Name = "MtF Micro-Dosing 2026",
        Description = "Clinical pilot cohort",
        CreatedBy = "admin_master",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="CohortManagementComponentTests"/> class.
    /// </summary>
    public CohortManagementComponentTests()
    {
        // WHAT: Registers mock dependencies and session state into bUnit's dependency injection container.
        // WHY:  Permits rendering of CohortManagementModal, UserManagement, and DnaManagementPage.
        // HOW:  Configures loose JSInterop and registers mock singletons.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_accessServiceMock.Object);
        Services.AddSingleton(_accessRepoMock.Object);
        Services.AddSingleton(_userRepoMock.Object);
        Services.AddSingleton(_authServiceMock.Object);
        Services.AddSingleton(_dnaRepoMock.Object);
        Services.AddSingleton(_profileRepoMock.Object);
        Services.AddSingleton(_fabServiceMock.Object);
        Services.AddSingleton(_genomicsServiceMock.Object);
        Services.AddSingleton(_sessionState);
    }

    /// <summary>
    /// Verifies that <see cref="CohortManagementModal"/> does not render when IsOpen is false,
    /// and renders full administrator controls when open.
    /// </summary>
    [Fact]
    public async Task CohortManagementModal_AdminRenderAndLifecycle_WorksCleanly()
    {
        // WHAT: Tests modal rendering, tab navigation, cohort creation, and member inspection for Administrators.
        // WHY:  Guarantees administrator cohort management operates reliably in the UI.
        // HOW:  Sets Admin session, renders component, simulates tab clicks, checkbox changes, and button clicks.
        _sessionState.SetUser(_adminUser);

        _userRepoMock.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_adminUser, _researcherUser, _patientAlice, _patientBob]);

        _accessRepoMock.Setup(r => r.ListGroupsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_testGroup]);

        _accessRepoMock.Setup(r => r.ListGroupMemberUsernamesAsync(_testGroup.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_patientAlice.Username]);

        _accessRepoMock.Setup(r => r.ListGroupGrantsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectGroupGrant
            {
                GroupId = _testGroup.Id,
                ResearcherUsername = _researcherUser.Username,
                AccessLevel = SubjectAccessLevel.GenomicAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow,
                Notes = "Trial grant"
            }]);

        // 1. Initial render closed
        var cut = RenderComponent<CohortManagementModal>(parameters => parameters
            .Add(p => p.IsOpen, false));

        Assert.Empty(cut.Markup.Trim());

        // 2. Open modal
        await cut.InvokeAsync(() => cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.IsOpen, true)));

        Assert.Contains("Research Cohorts &amp; Patient Groups", cut.Markup);
        Assert.Contains("MtF Micro-Dosing 2026", cut.Markup);
        Assert.Contains("admin_master", cut.Markup);

        // 3. Switch to Create Cohort tab
        var createTabBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Create New Cohort"));
        createTabBtn.Click();

        Assert.Contains("Create New Patient Cohort", cut.Markup);

        // Checkbox toggle with fresh query
        cut.Find("input[type='checkbox']").Change(true);
        cut.Find("input[type='checkbox']").Change(false);
        cut.Find("input[type='checkbox']").Change(true);

        // Input cohort name and description
        cut.Find("input#cohortName").Change("New Pilot Cohort");
        cut.Find("textarea#cohortDesc").Change("Pilot description");

        _accessServiceMock.Setup(s => s.CreateGroupAsync(
                _adminUser.Username,
                UserRole.Administrator,
                "New Pilot Cohort",
                "Pilot description",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectGroup
            {
                Id = "grp_new_pilot",
                Name = "New Pilot Cohort",
                Description = "Pilot description",
                CreatedBy = _adminUser.Username,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

        // Click create button
        var saveBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Create Cohort"));
        saveBtn.Click();

        _accessServiceMock.Verify(s => s.CreateGroupAsync(
            _adminUser.Username,
            UserRole.Administrator,
            "New Pilot Cohort",
            "Pilot description",
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // 4. Switch to members tab via SelectGroupForMembers
        var listTabBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cohort Groups"));
        listTabBtn.Click();

        var membersBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Members"));
        membersBtn.Click();

        Assert.Contains("Cohort Members: MtF Micro-Dosing 2026", cut.Markup);
        Assert.Contains("patient_alice", cut.Markup);

        // Add member
        _accessServiceMock.Setup(s => s.AddGroupMemberAsync(
                _adminUser.Username,
                UserRole.Administrator,
                _testGroup.Id,
                _patientBob.Username,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        cut.Find("select.form-control").Change(_patientBob.Username);
        var addMemberBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Add Subject"));
        addMemberBtn.Click();

        // Remove member
        _accessServiceMock.Setup(s => s.RemoveGroupMemberAsync(
                _adminUser.Username,
                UserRole.Administrator,
                _testGroup.Id,
                _patientAlice.Username,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var removeMemberBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Remove");
        removeMemberBtn.Click();

        // 5. Switch to grants tab
        listTabBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cohort Groups"));
        listTabBtn.Click();

        var grantsBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Grants"));
        grantsBtn.Click();

        Assert.Contains("Researcher Access Grants: MtF Micro-Dosing 2026", cut.Markup);
        Assert.Contains("researcher_watson", cut.Markup);

        // Grant access
        _accessServiceMock.Setup(s => s.GrantGroupAccessAsync(
                _adminUser.Username,
                _testGroup.Id,
                _researcherUser.Username,
                SubjectAccessLevel.FullReadAccess,
                "IRB approval",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectGroupGrant
            {
                GroupId = _testGroup.Id,
                ResearcherUsername = _researcherUser.Username,
                AccessLevel = SubjectAccessLevel.FullReadAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow,
                Notes = "IRB approval"
            });

        cut.Find("select#resSelect").Change(_researcherUser.Username);
        cut.Find("select#levelSelect").Change(((int)SubjectAccessLevel.FullReadAccess).ToString());
        cut.Find("input#grantNotes").Change("IRB approval");

        var grantAccessBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Grant Access"));
        grantAccessBtn.Click();

        // Revoke grant
        _accessServiceMock.Setup(s => s.RevokeGroupAccessAsync(
                _adminUser.Username,
                _testGroup.Id,
                _researcherUser.Username,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var revokeGrantBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Revoke");
        revokeGrantBtn.Click();

        // 6. Delete cohort
        listTabBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cohort Groups"));
        listTabBtn.Click();

        _accessServiceMock.Setup(s => s.DeleteGroupAsync(
                _adminUser.Username,
                UserRole.Administrator,
                _testGroup.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var deleteBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete");
        deleteBtn.Click();

        // 7. Close modal
        bool closed = false;
        await cut.InvokeAsync(() => cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.IsOpenChanged, EventCallback.Factory.Create<bool>(this, val => closed = !val))));

        var closeBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Close");
        closeBtn.Click();

        Assert.True(closed);
    }

    /// <summary>
    /// Verifies that <see cref="CohortManagementModal"/> scopes data to accessible patients for Researchers.
    /// </summary>
    [Fact]
    public void CohortManagementModal_ResearcherRender_ScopesAccessibleSubjects()
    {
        // WHAT: Tests researcher rendering in CohortManagementModal.
        // WHY:  Researchers must only see and enroll subjects they have access to, and cannot grant access.
        // HOW:  Sets Researcher session, sets up GetAccessibleSubjectUsernamesAsync, and renders modal.
        _sessionState.SetUser(_researcherUser);

        _userRepoMock.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_adminUser, _researcherUser, _patientAlice, _patientBob]);

        _accessServiceMock.Setup(s => s.GetAccessibleSubjectUsernamesAsync(
                _researcherUser.Username,
                UserRole.Researcher,
                SubjectAccessLevel.ReadAccess,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([_patientAlice.Username]);

        _accessRepoMock.Setup(r => r.ListGroupsAsync(_researcherUser.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _accessRepoMock.Setup(r => r.ListGroupGrantsAsync(_researcherUser.Username, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectGroupGrant
            {
                GroupId = _testGroup.Id,
                ResearcherUsername = _researcherUser.Username,
                AccessLevel = SubjectAccessLevel.GenomicAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow
            }]);

        _accessRepoMock.Setup(r => r.GetGroupAsync(_testGroup.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testGroup);

        _accessRepoMock.Setup(r => r.ListGroupMemberUsernamesAsync(_testGroup.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_patientAlice.Username]);

        var cut = RenderComponent<CohortManagementModal>(parameters => parameters
            .Add(p => p.IsOpen, true));

        // Wait for async load to finish
        cut.WaitForAssertion(() => Assert.Contains("MtF Micro-Dosing 2026", cut.Markup));

        // Grants button is NOT visible for Researcher
        Assert.DoesNotContain("🔑 Grants", cut.Markup);

        // Delete button is NOT visible because researcher did not create this cohort
        Assert.DoesNotContain("Delete", cut.Markup);

        // Switch to Create tab: only patientAlice is in the checklist, patientBob is excluded
        var createTabBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Create New Cohort"));
        createTabBtn.Click();

        Assert.Contains("patient_alice", cut.Markup);
        Assert.DoesNotContain("patient_bob", cut.Markup);
    }

    /// <summary>
    /// Verifies that <see cref="UserManagement"/> renders the grants button on Researcher rows
    /// and allows administrators to grant and revoke direct subject and cohort grants.
    /// </summary>
    [Fact]
    public void UserManagement_GrantsModal_GrantsAndRevokesCleanly()
    {
        // WHAT: Tests direct subject and cohort grant operations in UserManagement.
        // WHY:  Guarantees administrators can manage researcher permissions via the user table.
        // HOW:  Sets Admin session, renders UserManagement, opens grants modal, and invokes grant/revoke.
        _sessionState.SetUser(_adminUser);

        _authServiceMock.Setup(a => a.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_adminUser, _researcherUser, _patientAlice, _patientBob]);

        _accessRepoMock.Setup(r => r.ListGroupsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_testGroup]);

        _accessRepoMock.Setup(r => r.ListSubjectGrantsAsync(_researcherUser.Username, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectGrant
            {
                ResearcherUsername = _researcherUser.Username,
                SubjectUsername = _patientAlice.Username,
                AccessLevel = SubjectAccessLevel.GenomicAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow,
                Notes = "Existing grant"
            }]);

        _accessRepoMock.Setup(r => r.ListGroupGrantsAsync(null, _researcherUser.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var cut = RenderComponent<UserManagement>();

        Assert.Contains("SOVEREIGN USER MANAGEMENT", cut.Markup);
        Assert.Contains("👥 Manage Cohorts", cut.Markup);

        // Open grants modal for researcher
        var grantsBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Grants"));
        grantsBtn.Click();

        Assert.Contains("Subject &amp; Cohort Access Grants: researcher_watson", cut.Markup);
        Assert.Contains("patient_alice", cut.Markup);

        // Grant direct access to Bob
        _accessServiceMock.Setup(s => s.GrantSubjectAccessAsync(
                _adminUser.Username,
                _researcherUser.Username,
                _patientBob.Username,
                SubjectAccessLevel.FullReadAccess,
                "Expanded trial",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectGrant
            {
                ResearcherUsername = _researcherUser.Username,
                SubjectUsername = _patientBob.Username,
                AccessLevel = SubjectAccessLevel.FullReadAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow,
                Notes = "Expanded trial"
            });

        cut.Find("select#grantSubjectSelect").Change(_patientBob.Username);
        cut.Find("select#grantSubjectLevel").Change(((int)SubjectAccessLevel.FullReadAccess).ToString());
        cut.Find("input#grantSubjectNotes").Change("Expanded trial");

        var grantSubjectBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Grant Direct Patient Access"));
        grantSubjectBtn.Click();

        // Revoke direct access
        _accessServiceMock.Setup(s => s.RevokeSubjectAccessAsync(
                _adminUser.Username,
                _researcherUser.Username,
                _patientAlice.Username,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var revokeSubjectBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Revoke");
        revokeSubjectBtn.Click();

        // Grant cohort access
        _accessServiceMock.Setup(s => s.GrantGroupAccessAsync(
                _adminUser.Username,
                _testGroup.Id,
                _researcherUser.Username,
                SubjectAccessLevel.GenomicAccess,
                "Cohort trial",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectGroupGrant
            {
                GroupId = _testGroup.Id,
                ResearcherUsername = _researcherUser.Username,
                AccessLevel = SubjectAccessLevel.GenomicAccess,
                GrantedBy = _adminUser.Username,
                GrantedAtUtc = DateTimeOffset.UtcNow,
                Notes = "Cohort trial"
            });

        cut.Find("select#grantGroupSelect").Change(_testGroup.Id);
        cut.Find("select#grantGroupLevel").Change(((int)SubjectAccessLevel.GenomicAccess).ToString());
        cut.Find("input#grantGroupNotes").Change("Cohort trial");

        var grantGroupBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Grant Cohort Group Access"));
        grantGroupBtn.Click();

        // Close modal
        var doneBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Done");
        doneBtn.Click();

        Assert.DoesNotContain("Subject &amp; Cohort Access Grants", cut.Markup);
    }

    /// <summary>
    /// Verifies that <see cref="DnaManagementPage"/> filters accessible patients and respects cohort selection.
    /// </summary>
    [Fact]
    public void DnaManagementPage_CohortFilteringAndAccessScoping_WorksCleanly()
    {
        // WHAT: Tests patient dropdown filtering by cohort in DnaManagementPage.
        // WHY:  Researchers and administrators must be able to narrow patient selection by cohort.
        // HOW:  Sets Admin session, configures users and cohorts, renders DnaManagementPage, and changes cohort dropdown.
        _sessionState.SetUser(_adminUser);

        _userRepoMock.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_adminUser, _researcherUser, _patientAlice, _patientBob]);

        _accessRepoMock.Setup(r => r.ListGroupsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_testGroup]);

        // Alice is in the cohort, Bob is not
        _accessRepoMock.Setup(r => r.ListGroupMemberUsernamesAsync(_testGroup.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_patientAlice.Username]);

        _dnaRepoMock.Setup(d => d.GetDnaRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnaRepoMock.Setup(d => d.GetImportsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnaRepoMock.Setup(d => d.GetResultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var cut = RenderComponent<DnaManagementPage>();

        Assert.Contains("Patient DNA &amp; Sequence Imports", cut.Markup);
        Assert.Contains("👥 Manage Cohorts", cut.Markup);
        Assert.Contains("Cohort:", cut.Markup);

        // Before cohort filtering: both Alice and Bob are available
        Assert.Contains("patient_alice", cut.Markup);
        Assert.Contains("patient_bob", cut.Markup);

        // Filter by testGroup
        cut.Find("select#cohortFilter").Change(_testGroup.Id);

        // After cohort filtering: Alice is in the dropdown, Bob is filtered out
        var patientSelect = cut.Find("select#patientSelector");
        Assert.Contains("patient_alice", patientSelect.InnerHtml);
        Assert.DoesNotContain("patient_bob", patientSelect.InnerHtml);

        // Reset to All Cohorts
        cut.Find("select#cohortFilter").Change(string.Empty);
        patientSelect = cut.Find("select#patientSelector");
        Assert.Contains("patient_alice", patientSelect.InnerHtml);
        Assert.Contains("patient_bob", patientSelect.InnerHtml);
    }

    /// <summary>
    /// Verifies that <see cref="DnaManagementPage"/> displays an informative warning when a Researcher
    /// has not been granted access to any patient records.
    /// </summary>
    [Fact]
    public void DnaManagementPage_ResearcherWithZeroAccess_DisplaysWarningBanner()
    {
        // WHAT: Tests researcher zero-access warning alert in DnaManagementPage.
        // WHY:  Unassigned researchers must clearly see that they require administrator grants.
        // HOW:  Sets Researcher session, returns 0 accessible subjects, renders DnaManagementPage, and verifies banner.
        _sessionState.SetUser(_researcherUser);

        _userRepoMock.Setup(r => r.ListUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_adminUser, _researcherUser, _patientAlice, _patientBob]);

        _accessServiceMock.Setup(s => s.GetAccessibleSubjectUsernamesAsync(
                _researcherUser.Username,
                UserRole.Researcher,
                SubjectAccessLevel.ReadAccess,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _accessRepoMock.Setup(r => r.ListGroupsAsync(_researcherUser.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accessRepoMock.Setup(r => r.ListGroupGrantsAsync(null, _researcherUser.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _dnaRepoMock.Setup(d => d.GetDnaRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnaRepoMock.Setup(d => d.GetImportsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnaRepoMock.Setup(d => d.GetResultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var cut = RenderComponent<DnaManagementPage>();

        Assert.Contains("No Patient Access Assigned", cut.Markup);
        Assert.Contains("Please contact an Administrator to be granted access", cut.Markup);
    }
}
