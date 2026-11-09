using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests verifying role-based access gating, researcher patient grants,
/// self-profile access, and demographic rendering in <see cref="UserProfilePage"/>.
/// </summary>
public sealed class UserProfilePageComponentTests : TestContext
{
    /// <summary>
    /// Mock user profile repository returning profile data, addresses, and contacts.
    /// </summary>
    private readonly Mock<IUserProfileRepository> _profileRepoMock = new();

    /// <summary>
    /// Mock user repository returning user account authentication metadata.
    /// </summary>
    private readonly Mock<IUserRepository> _userRepoMock = new();

    /// <summary>
    /// Mock subject access evaluation service determining researcher grants.
    /// </summary>
    private readonly Mock<ISubjectAccessService> _accessServiceMock = new();

    /// <summary>
    /// Active user session state controlling the authenticated caller identity.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Standard patient user account fixture.
    /// </summary>
    private readonly UserAccount _patientAccount;

    /// <summary>
    /// Standard patient user profile fixture.
    /// </summary>
    private readonly UserProfile _patientProfile;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProfilePageComponentTests"/> class.
    /// Configures default mock behaviors and registers DI services.
    /// </summary>
    public UserProfilePageComponentTests()
    {
        // WHAT: Prepares mock responses for UserProfileRepository, UserRepository, and SubjectAccessService.
        // WHY:  Permits testing access boundaries and profile rendering across Admin, Researcher, and Patient roles.
        // HOW:  Sets up default returns and registers mocks in bUnit TestContext.Services.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _patientAccount = new UserAccount
        {
            Username = "patient_alex",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_patient_key"
        };

        _patientProfile = new UserProfile
        {
            Username = "patient_alex",
            FirstName = "Alex",
            LastName = "Morgan",
            PreferredName = "Alex",
            GenderAssignedAtBirth = GenderAssignedAtBirth.Female,
            Pronouns = ["they", "them"],
            Regimen = TherapeuticRegimen.FemaleToMale
        };

        _userRepoMock
            .Setup(u => u.GetUserAsync("patient_alex", default))
            .ReturnsAsync(_patientAccount);

        _profileRepoMock
            .Setup(p => p.GetProfileAsync("patient_alex", default))
            .ReturnsAsync(_patientProfile);

        _profileRepoMock
            .Setup(p => p.GetAddressesAsync("patient_alex", default))
            .ReturnsAsync(new List<PatientAddress>
            {
                new()
                {
                    Id = "addr_1",
                    Username = "patient_alex",
                    AddressType = "Residential",
                    AddressLine1 = "100 BioTech Way",
                    City = "Cambridge",
                    StateOrProvince = "MA",
                    PostalCode = "02139",
                    Country = "USA",
                    IsPrimary = true
                }
            });

        _profileRepoMock
            .Setup(p => p.GetContactsAsync("patient_alex", default))
            .ReturnsAsync(new List<PatientContact>
            {
                new()
                {
                    Id = "cont_1",
                    Username = "patient_alex",
                    ContactName = "Sam Morgan",
                    Relationship = "Partner",
                    PhoneNumber = "555-0199",
                    CanDiscussMedicalInfo = true,
                    PriorityOrder = 1
                }
            });

        Services.AddSingleton<IUserProfileRepository>(_profileRepoMock.Object);
        Services.AddSingleton<IUserRepository>(_userRepoMock.Object);
        Services.AddSingleton<ISubjectAccessService>(_accessServiceMock.Object);
        Services.AddSingleton(_sessionState);
    }

    /// <summary>
    /// Verifies that rendering the profile page while unauthenticated displays the authentication required alert.
    /// </summary>
    [Fact]
    public void Unauthenticated_DisplaysAuthenticationRequiredAlert()
    {
        // WHAT: Tests access gating for anonymous unauthenticated visitors.
        // WHY:  Ensures protected health information is never rendered without an active session.
        // HOW:  Renders UserProfilePage with empty session state and asserts authentication required message.
        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>();

        var alert = cut.Find(".alert-warning");
        Assert.Contains("Authentication Required", alert.TextContent);
        Assert.DoesNotContain("SOVEREIGN USER & PATIENT PROFILE", cut.Markup);
    }

    /// <summary>
    /// Verifies that an authenticated user can view and manage their own self-profile without explicit grants.
    /// </summary>
    [Fact]
    public void UserSelfAccess_PermitsViewingOwnProfile()
    {
        // WHAT: Tests self-profile access boundary for regular accounts.
        // WHY:  Patients and regular users must always be authorized to view and update their own records.
        // HOW:  Sets SessionState.CurrentUser to patient_alex, renders page without parameters, asserts profile details.
        _sessionState.SetUser(_patientAccount);

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>();

        Assert.Contains("Alex", cut.Markup);
        Assert.Contains("patient_alex", cut.Markup);
        Assert.Contains("Regimen: FemaleToMale", cut.Markup);
        Assert.Contains("100 BioTech Way", cut.Markup);
        Assert.Contains("Sam Morgan", cut.Markup);
    }

    /// <summary>
    /// Verifies that an administrator has unrestricted access to view any target user's profile.
    /// </summary>
    [Fact]
    public void Administrator_HasUnrestrictedAccessToAnyUserProfile()
    {
        // WHAT: Tests administrator supervisory oversight of user profiles.
        // WHY:  Administrators must be able to audit and oversee clinical user accounts across the platform.
        // HOW:  Signs in as Administrator, sets TargetUsername parameter to patient_alex, asserts full profile render.
        _sessionState.SetUser(new UserAccount
        {
            Username = "super_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>(parameters =>
            parameters.Add(p => p.TargetUsername, "patient_alex"));

        Assert.Contains("Alex", cut.Markup);
        Assert.Contains("patient_alex", cut.Markup);
        Assert.Contains("Back to User Management", cut.Markup);
    }

    /// <summary>
    /// Verifies that a researcher possessing an active ReadAccess grant is authorized to view a patient's profile.
    /// </summary>
    [Fact]
    public void Researcher_WithActiveReadAccessGrant_PermitsPatientProfileAccess()
    {
        // WHAT: Tests researcher delegated access evaluation via ISubjectAccessService.
        // WHY:  Fulfills Issue #155 (P152) requirement: researchers with ReadAccess or FullReadAccess may view patient profiles.
        // HOW:  Signs in as Researcher, mocks EvaluateAccessAsync to return IsAllowed = true, asserts profile renders.
        _sessionState.SetUser(new UserAccount
        {
            Username = "dr_researcher",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_researcher_key"
        });

        _accessServiceMock
            .Setup(a => a.EvaluateAccessAsync("dr_researcher", UserRole.Researcher, "patient_alex", SubjectAccessLevel.ReadAccess, default))
            .ReturnsAsync(SubjectAccessResult.Allowed(SubjectAccessLevel.ReadAccess));

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>(parameters =>
            parameters.Add(p => p.TargetUsername, "patient_alex"));

        Assert.Contains("Alex", cut.Markup);
        Assert.Contains("patient_alex", cut.Markup);
        Assert.DoesNotContain("Access Denied", cut.Markup);
    }

    /// <summary>
    /// Verifies that a researcher lacking an active grant is denied access to view a patient's profile.
    /// </summary>
    [Fact]
    public void Researcher_WithoutGrant_DeniesAccessWithSecurityAlert()
    {
        // WHAT: Tests researcher access denial when no active subject grant exists.
        // WHY:  Enforces strict HIPAA and GDPR boundaries against unauthorized patient record inspection.
        // HOW:  Signs in as Researcher, mocks EvaluateAccessAsync to return IsAllowed = false, asserts Access Denied alert.
        _sessionState.SetUser(new UserAccount
        {
            Username = "dr_unauthorized",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_unauthorized_key"
        });

        _accessServiceMock
            .Setup(a => a.EvaluateAccessAsync("dr_unauthorized", UserRole.Researcher, "patient_alex", SubjectAccessLevel.ReadAccess, default))
            .ReturnsAsync(SubjectAccessResult.Denied());

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>(parameters =>
            parameters.Add(p => p.TargetUsername, "patient_alex"));

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Access Denied", alert.TextContent);
        Assert.Contains("patient_alex", alert.TextContent);
    }

    /// <summary>
    /// Verifies that a patient attempting to access another user's profile is strictly denied access.
    /// </summary>
    [Fact]
    public void Patient_AccessingOtherUser_StrictlyDeniesAccess()
    {
        // WHAT: Tests horizontal authorization isolation between patient accounts.
        // WHY:  Patients must never be permitted to inspect peers' medical profiles.
        // HOW:  Signs in as patient_other, sets TargetUsername to patient_alex, asserts Access Denied.
        _sessionState.SetUser(new UserAccount
        {
            Username = "patient_other",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_other_key"
        });

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>(parameters =>
            parameters.Add(p => p.TargetUsername, "patient_alex"));

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Access Denied", alert.TextContent);
    }

    /// <summary>
    /// Verifies that requesting a target username that does not exist in the database displays the User Not Found alert.
    /// </summary>
    [Fact]
    public void Administrator_TargetUserNotFound_DisplaysUserNotFoundAlert()
    {
        // WHAT: Tests missing target account error handling.
        // WHY:  Informs administrators when a specified route username does not exist.
        // HOW:  Signs in as Admin, targets unknown_user, asserts User Not Found alert.
        _sessionState.SetUser(new UserAccount
        {
            Username = "super_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.Web,
            PublicKeyBase64 = "MIIB_admin_key"
        });

        _userRepoMock
            .Setup(u => u.GetUserAsync("unknown_user", default))
            .ReturnsAsync((UserAccount?)null);

        IRenderedComponent<UserProfilePage> cut = RenderComponent<UserProfilePage>(parameters =>
            parameters.Add(p => p.TargetUsername, "unknown_user"));

        var alert = cut.Find(".alert-danger");
        Assert.Contains("User Not Found", alert.TextContent);
        Assert.Contains("unknown_user", alert.TextContent);
    }
}
