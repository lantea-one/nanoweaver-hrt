using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit and integration tests for <see cref="SubjectAccessService"/> and <see cref="SubjectAccessResult"/>.
/// </summary>
public sealed class SubjectAccessServiceTests : IDisposable
{
    /// <summary>
    /// Temporary directory hosting isolated test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// File path to isolated SQLite database.
    /// </summary>
    private readonly string _dbPath;

    /// <summary>
    /// Connection string pointing to the isolated test database.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Database connection factory under test.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Subject access repository under test.
    /// </summary>
    private readonly SubjectAccessRepository _accessRepo;

    /// <summary>
    /// User account repository for test fixtures.
    /// </summary>
    private readonly UserRepository _userRepo;

    /// <summary>
    /// In-memory recording compliance audit service.
    /// </summary>
    private readonly TestComplianceAuditService _auditService;

    /// <summary>
    /// Subject access service under test.
    /// </summary>
    private readonly SubjectAccessService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectAccessServiceTests"/> class.
    /// </summary>
    public SubjectAccessServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_subj_svc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "weaver_config.db");
        _connectionString = $"Data Source={_dbPath}";

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        _connectionFactory = WeaverTestDb.CreateFactory(config);
        _accessRepo = new SubjectAccessRepository(_connectionFactory);
        _userRepo = new UserRepository(_connectionFactory);
        _auditService = new TestComplianceAuditService();
        _service = new SubjectAccessService(_accessRepo, _userRepo, _auditService);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        using (SqliteConnection conn = new(_connectionString))
        {
            SqliteConnection.ClearPool(conn);
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies constructor parameter validation.
    /// </summary>
    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // WHAT: Tests null argument rejection on SubjectAccessService constructor.
        // WHY:  Ensures robust defense-in-depth against improperly configured DI containers.
        // HOW:  Passes null for each parameter and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new SubjectAccessService(null!, _userRepo, _auditService));
        Assert.Throws<ArgumentNullException>(() => new SubjectAccessService(_accessRepo, null!, _auditService));
        Assert.Throws<ArgumentNullException>(() => new SubjectAccessService(_accessRepo, _userRepo, null!));
    }

    /// <summary>
    /// Verifies SubjectAccessResult factory methods and properties.
    /// </summary>
    [Fact]
    public void SubjectAccessResult_AllowedAndDenied_CorrectState()
    {
        // WHAT: Verifies factory methods for SubjectAccessResult.
        // WHY:  Ensures state representation is faithful.
        // HOW:  Creates allowed and denied results and asserts properties.
        SubjectAccessResult allowed = SubjectAccessResult.Allowed(SubjectAccessLevel.GenomicAccess);
        Assert.True(allowed.IsAllowed);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, allowed.EffectiveLevel);

        SubjectAccessResult denied = SubjectAccessResult.Denied();
        Assert.False(denied.IsAllowed);
        Assert.Null(denied.EffectiveLevel);
    }

    /// <summary>
    /// Verifies EvaluateAccessAsync across roles, direct grants, and boundary conditions.
    /// </summary>
    [Fact]
    public async Task EvaluateAccessAsync_RoleMatrixAndTiers_EvaluatedCorrectly()
    {
        // WHAT: Tests access evaluation across Administrator, self, researcher with/without grant, and other roles.
        // WHY:  Ensures patient confidentiality and role integrity.
        // HOW:  Invokes EvaluateAccessAsync with varying parameters and asserts result.
        CancellationToken ct = CancellationToken.None;

        // Null argument checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.EvaluateAccessAsync(null!, UserRole.Researcher, "alice", SubjectAccessLevel.ReadAccess, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.EvaluateAccessAsync("doc", UserRole.Researcher, null!, SubjectAccessLevel.ReadAccess, ct));

        // 1. Administrator has full access over any subject
        SubjectAccessResult adminRes = await _service.EvaluateAccessAsync("admin", UserRole.Administrator, "alice", SubjectAccessLevel.FullReadAccess, ct);
        Assert.True(adminRes.IsAllowed);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, adminRes.EffectiveLevel);

        // 2. Patient self-access
        SubjectAccessResult selfRes = await _service.EvaluateAccessAsync("alice", UserRole.Patient, "alice", SubjectAccessLevel.FullReadAccess, ct);
        Assert.True(selfRes.IsAllowed);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, selfRes.EffectiveLevel);

        // 3. Patient accessing someone else -> denied
        SubjectAccessResult crossPatientRes = await _service.EvaluateAccessAsync("bob", UserRole.Patient, "alice", SubjectAccessLevel.ReadAccess, ct);
        Assert.False(crossPatientRes.IsAllowed);
        Assert.Null(crossPatientRes.EffectiveLevel);

        // 4. Researcher with no grants -> denied
        SubjectAccessResult noGrantRes = await _service.EvaluateAccessAsync("dr_smith", UserRole.Researcher, "alice", SubjectAccessLevel.ReadAccess, ct);
        Assert.False(noGrantRes.IsAllowed);

        // 5. Grant researcher Tier 1 (ReadAccess)
        await _service.GrantSubjectAccessAsync("admin", "dr_smith", "alice", SubjectAccessLevel.ReadAccess, "Initial intake", ct);

        // ReadAccess requested -> Allowed
        SubjectAccessResult readRes = await _service.EvaluateAccessAsync("dr_smith", UserRole.Researcher, "alice", SubjectAccessLevel.ReadAccess, ct);
        Assert.True(readRes.IsAllowed);
        Assert.Equal(SubjectAccessLevel.ReadAccess, readRes.EffectiveLevel);

        // GenomicAccess requested with only ReadAccess -> Denied
        SubjectAccessResult genomicRes = await _service.EvaluateAccessAsync("dr_smith", UserRole.Researcher, "alice", SubjectAccessLevel.GenomicAccess, ct);
        Assert.False(genomicRes.IsAllowed);

        // Upgrade grant to GenomicAccess
        await _service.GrantSubjectAccessAsync("admin", "dr_smith", "alice", SubjectAccessLevel.GenomicAccess, "Upgraded", ct);
        SubjectAccessResult upgradedRes = await _service.EvaluateAccessAsync("dr_smith", UserRole.Researcher, "alice", SubjectAccessLevel.GenomicAccess, ct);
        Assert.True(upgradedRes.IsAllowed);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, upgradedRes.EffectiveLevel);
    }

    /// <summary>
    /// Verifies GetEffectiveAccessLevelAsync with direct grants, cohort group grants, and creator ownership.
    /// </summary>
    [Fact]
    public async Task GetEffectiveAccessLevelAsync_DirectGroupAndOwnership_ComputesMaxLevel()
    {
        // WHAT: Tests combination of direct grants, group grants, and creator ownership.
        // WHY:  Proves EffectiveLevel = max(DirectGrant, GroupGrant, CreatedGroup) logic.
        // HOW:  Creates groups, adds grants, and tests effective level at each step.
        CancellationToken ct = CancellationToken.None;

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetEffectiveAccessLevelAsync(null!, "pat_1", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetEffectiveAccessLevelAsync("res_1", null!, ct));

        // Initially null
        Assert.Null(await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Group 1 created by admin with pat_1
        SubjectGroup grp1 = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Cohort Alpha", "Admin cohort", ["pat_1"], ct);

        // Admin grants dr_jane Tier 1 (ReadAccess) over Cohort Alpha
        await _service.GrantGroupAccessAsync("admin", grp1.Id, "dr_jane", SubjectAccessLevel.ReadAccess, "Group grant", ct);
        Assert.Equal(SubjectAccessLevel.ReadAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Admin gives direct grant of Tier 3 (FullReadAccess)
        await _service.GrantSubjectAccessAsync("admin", "dr_jane", "pat_1", SubjectAccessLevel.FullReadAccess, "Direct grant", ct);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Revoke direct grant -> falls back to group grant Tier 1
        await _service.RevokeSubjectAccessAsync("admin", "dr_jane", "pat_1", ct);
        Assert.Equal(SubjectAccessLevel.ReadAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Creator ownership: dr_jane creates Cohort Beta containing pat_1 (since dr_jane already has access via Group 1)
        SubjectGroup grp2 = await _service.CreateGroupAsync("dr_jane", UserRole.Researcher, "Cohort Beta", "Researcher cohort", ["pat_1"], ct);
        // Creator gets at least GenomicAccess (Tier 2) over members of created group
        Assert.Equal(SubjectAccessLevel.GenomicAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Admin grants dr_jane Tier 3 (FullReadAccess) over Cohort Alpha -> max level becomes 3
        await _service.GrantGroupAccessAsync("admin", grp1.Id, "dr_jane", SubjectAccessLevel.FullReadAccess, "Group upgrade", ct);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));

        // Admin grants lower Tier 1 over grp2 -> does not lower max level (remains 3)
        await _service.GrantGroupAccessAsync("admin", grp2.Id, "dr_jane", SubjectAccessLevel.ReadAccess, "Lower group grant", ct);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, await _service.GetEffectiveAccessLevelAsync("dr_jane", "pat_1", ct));
    }

    /// <summary>
    /// Verifies GetAccessibleSubjectUsernamesAsync across roles and tier filters.
    /// </summary>
    [Fact]
    public async Task GetAccessibleSubjectUsernamesAsync_RolesAndTiers_ReturnsFilteredList()
    {
        // WHAT: Tests subject username aggregation for UI/API listing.
        // WHY:  Powers dropdown selectors and guarantees patients only see themselves.
        // HOW:  Registers accounts, assigns grants, and asserts accessible usernames.
        CancellationToken ct = CancellationToken.None;

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetAccessibleSubjectUsernamesAsync(null!, UserRole.Researcher, ct: ct));

        // Seed users
        string dummyPk = Convert.ToBase64String(new byte[32]);
        await _userRepo.CreateUserAsync(new UserAccount { Username = "pat_alpha", Role = UserRole.Patient, AccessGate = AccessGate.ApiWeb, PublicKeyBase64 = dummyPk, CreatedAtUtc = DateTimeOffset.UtcNow }, ct);
        await _userRepo.CreateUserAsync(new UserAccount { Username = "pat_beta", Role = UserRole.Patient, AccessGate = AccessGate.ApiWeb, PublicKeyBase64 = dummyPk, CreatedAtUtc = DateTimeOffset.UtcNow }, ct);
        await _userRepo.CreateUserAsync(new UserAccount { Username = "res_user", Role = UserRole.Researcher, AccessGate = AccessGate.ApiWeb, PublicKeyBase64 = dummyPk, CreatedAtUtc = DateTimeOffset.UtcNow }, ct);

        // 1. Admin gets all patients
        var adminAccessible = await _service.GetAccessibleSubjectUsernamesAsync("admin", UserRole.Administrator, ct: ct);
        Assert.Contains("pat_alpha", adminAccessible);
        Assert.Contains("pat_beta", adminAccessible);

        // 2. Patient gets only self
        var patientAccessible = await _service.GetAccessibleSubjectUsernamesAsync("pat_alpha", UserRole.Patient, ct: ct);
        Assert.Single(patientAccessible);
        Assert.Equal("pat_alpha", patientAccessible[0]);

        // 3. Other role (none or undefined) gets empty
        var noneAccessible = await _service.GetAccessibleSubjectUsernamesAsync("guest", (UserRole)99, ct: ct);
        Assert.Empty(noneAccessible);

        // 4. Researcher initially empty
        var resAccessible1 = await _service.GetAccessibleSubjectUsernamesAsync("res_user", UserRole.Researcher, ct: ct);
        Assert.Empty(resAccessible1);

        // Direct grant pat_alpha with ReadAccess
        await _service.GrantSubjectAccessAsync("admin", "res_user", "pat_alpha", SubjectAccessLevel.ReadAccess, null, ct);

        // Group grant pat_beta with GenomicAccess
        SubjectGroup grp = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Beta Group", null, ["pat_beta"], ct);
        await _service.GrantGroupAccessAsync("admin", grp.Id, "res_user", SubjectAccessLevel.GenomicAccess, null, ct);

        // Accessible at ReadAccess (Tier 1) -> both pat_alpha and pat_beta
        var resAccessible2 = await _service.GetAccessibleSubjectUsernamesAsync("res_user", UserRole.Researcher, SubjectAccessLevel.ReadAccess, ct);
        Assert.Equal(2, resAccessible2.Count);
        Assert.Contains("pat_alpha", resAccessible2);
        Assert.Contains("pat_beta", resAccessible2);

        // Accessible at GenomicAccess (Tier 2) -> only pat_beta
        var resAccessible3 = await _service.GetAccessibleSubjectUsernamesAsync("res_user", UserRole.Researcher, SubjectAccessLevel.GenomicAccess, ct);
        Assert.Single(resAccessible3);
        Assert.Equal("pat_beta", resAccessible3[0]);

        // Researcher creates a group containing pat_alpha
        SubjectGroup resGrp = await _service.CreateGroupAsync("res_user", UserRole.Researcher, "My Cohort", null, ["pat_alpha"], ct);
        var resAccessibleCreated = await _service.GetAccessibleSubjectUsernamesAsync("res_user", UserRole.Researcher, SubjectAccessLevel.GenomicAccess, ct);
        // Created group members are included
        Assert.Contains("pat_alpha", resAccessibleCreated);
    }

    /// <summary>
    /// Verifies CanManageGroupAsync permission boundaries.
    /// </summary>
    [Fact]
    public async Task CanManageGroupAsync_OwnershipAndRoleRules_Enforced()
    {
        // WHAT: Tests group management permission rules.
        // WHY:  Administrators manage all groups; Researchers manage only what they created.
        // HOW:  Creates groups and tests can-manage check across roles.
        CancellationToken ct = CancellationToken.None;

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CanManageGroupAsync(null!, UserRole.Researcher, "grp_1", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CanManageGroupAsync("admin", UserRole.Administrator, null!, ct));

        SubjectGroup adminGrp = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Admin Cohort", null, [], ct);
        SubjectGroup resGrp = await _service.CreateGroupAsync("dr_who", UserRole.Researcher, "Res Cohort", null, [], ct);

        // Admin can manage both
        Assert.True(await _service.CanManageGroupAsync("admin", UserRole.Administrator, adminGrp.Id, ct));
        Assert.True(await _service.CanManageGroupAsync("admin", UserRole.Administrator, resGrp.Id, ct));

        // dr_who can manage resGrp but not adminGrp
        Assert.True(await _service.CanManageGroupAsync("dr_who", UserRole.Researcher, resGrp.Id, ct));
        Assert.False(await _service.CanManageGroupAsync("dr_who", UserRole.Researcher, adminGrp.Id, ct));

        // Non-existent group
        Assert.False(await _service.CanManageGroupAsync("dr_who", UserRole.Researcher, "grp_nonexistent", ct));

        // Patient cannot manage anything
        Assert.False(await _service.CanManageGroupAsync("pat_1", UserRole.Patient, resGrp.Id, ct));
    }

    /// <summary>
    /// Verifies ValidateCanAddSubjectsToGroupAsync boundary rules.
    /// </summary>
    [Fact]
    public async Task ValidateCanAddSubjectsToGroupAsync_AccessEnforcement_Validated()
    {
        // WHAT: Tests boundary check preventing researchers from enrolling inaccessible patients.
        // WHY:  Prevents privilege escalation via cohort membership.
        // HOW:  Verifies admin allows all, empty candidates pass, researcher allows only granted subjects.
        CancellationToken ct = CancellationToken.None;

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ValidateCanAddSubjectsToGroupAsync(null!, UserRole.Researcher, [], ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ValidateCanAddSubjectsToGroupAsync("admin", UserRole.Administrator, null!, ct));

        // Admin allows any candidates
        Assert.True(await _service.ValidateCanAddSubjectsToGroupAsync("admin", UserRole.Administrator, ["any_subject_1", "any_subject_2"], ct));

        // Patient allows none
        Assert.False(await _service.ValidateCanAddSubjectsToGroupAsync("pat", UserRole.Patient, ["pat"], ct));

        // Empty candidates pass
        Assert.True(await _service.ValidateCanAddSubjectsToGroupAsync("dr_res", UserRole.Researcher, [], ct));

        // Grant dr_res access to pat_granted only
        await _service.GrantSubjectAccessAsync("admin", "dr_res", "pat_granted", SubjectAccessLevel.ReadAccess, null, ct);

        // Adding only pat_granted -> true
        Assert.True(await _service.ValidateCanAddSubjectsToGroupAsync("dr_res", UserRole.Researcher, ["pat_granted"], ct));

        // Adding pat_granted + pat_unauthorized -> false
        Assert.False(await _service.ValidateCanAddSubjectsToGroupAsync("dr_res", UserRole.Researcher, ["pat_granted", "pat_unauthorized"], ct));
    }

    /// <summary>
    /// Verifies CreateGroupAsync, UpdateGroupAsync, and DeleteGroupAsync lifecycle and audit logs.
    /// </summary>
    [Fact]
    public async Task GroupLifecycle_CreateUpdateDelete_AuditedAndEnforced()
    {
        // WHAT: Tests complete group lifecycle and compliance audit recording.
        // WHY:  Guarantees audit traceability for regulatory compliance.
        // HOW:  Executes lifecycle steps and asserts repository state and audit entries.
        CancellationToken ct = CancellationToken.None;

        // Null argument checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateGroupAsync(null!, UserRole.Administrator, "Test", null, [], ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateGroupAsync("admin", UserRole.Administrator, null!, null, [], ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateGroupAsync("admin", UserRole.Administrator, "Test", null, null!, ct));

        // Unauthorized role
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateGroupAsync("pat", UserRole.Patient, "Cohort", null, [], ct));

        // Researcher attempting to create group with unauthorized subjects throws
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateGroupAsync("dr_unauth", UserRole.Researcher, "Secret Cohort", null, ["unauthorized_patient"], ct));

        // Successful creation by Administrator
        SubjectGroup created = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Study Group", "Description", ["subj_1", "subj_2"], ct);
        Assert.NotNull(created.Id);
        Assert.Equal("Study Group", created.Name);
        Assert.Equal(2, created.MemberCount);

        Assert.Contains(_auditService.Entries, e => e.Action == "SUBJECT_GROUP_CREATED" && e.Actor == "admin" && e.Target == $"group:{created.Id}");

        // Update group - unauthorized caller throws
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.UpdateGroupAsync("dr_other", UserRole.Researcher, created.Id, "Hacked", null, ct));

        // Update nonexistent group returns false
        Assert.False(await _service.UpdateGroupAsync("admin", UserRole.Administrator, "grp_nonexistent", "Name", null, ct));

        // Update group by admin succeeds
        bool updated = await _service.UpdateGroupAsync("admin", UserRole.Administrator, created.Id, "Updated Study Group", "New Desc", ct);
        Assert.True(updated);
        Assert.Contains(_auditService.Entries, e => e.Action == "SUBJECT_GROUP_UPDATED" && e.Actor == "admin" && e.Target == $"group:{created.Id}");

        // Delete group - unauthorized caller throws
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.DeleteGroupAsync("dr_other", UserRole.Researcher, created.Id, ct));

        // Delete group by admin succeeds
        bool deleted = await _service.DeleteGroupAsync("admin", UserRole.Administrator, created.Id, ct);
        Assert.True(deleted);
        Assert.Contains(_auditService.Entries, e => e.Action == "SUBJECT_GROUP_DELETED" && e.Actor == "admin" && e.Target == $"group:{created.Id}");
    }

    /// <summary>
    /// Verifies AddGroupMemberAsync and RemoveGroupMemberAsync authorization and auditing.
    /// </summary>
    [Fact]
    public async Task GroupMembers_AddAndRemove_AuditedAndEnforced()
    {
        // WHAT: Tests member addition and removal in cohort groups.
        // WHY:  Ensures membership changes are verified against permissions and logged.
        // HOW:  Adds and removes members as admin and researcher, asserting state and audit trail.
        CancellationToken ct = CancellationToken.None;

        SubjectGroup group = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Membership Cohort", null, [], ct);

        // Null argument checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AddGroupMemberAsync(null!, UserRole.Administrator, group.Id, "pat", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AddGroupMemberAsync("admin", UserRole.Administrator, null!, "pat", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AddGroupMemberAsync("admin", UserRole.Administrator, group.Id, null!, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RemoveGroupMemberAsync(null!, UserRole.Administrator, group.Id, "pat", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RemoveGroupMemberAsync("admin", UserRole.Administrator, null!, "pat", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RemoveGroupMemberAsync("admin", UserRole.Administrator, group.Id, null!, ct));

        // Unauthorized caller cannot add
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.AddGroupMemberAsync("dr_rogue", UserRole.Researcher, group.Id, "pat_x", ct));

        // Researcher owner cannot add unauthorized subject
        SubjectGroup resGroup = await _service.CreateGroupAsync("dr_owner", UserRole.Researcher, "Res Cohort", null, [], ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.AddGroupMemberAsync("dr_owner", UserRole.Researcher, resGroup.Id, "pat_unauthorized", ct));

        // Grant dr_owner access to pat_authorized
        await _service.GrantSubjectAccessAsync("admin", "dr_owner", "pat_authorized", SubjectAccessLevel.ReadAccess, null, ct);
        // Researcher owner adds authorized subject -> succeeds!
        bool resAdded = await _service.AddGroupMemberAsync("dr_owner", UserRole.Researcher, resGroup.Id, "pat_authorized", ct);
        Assert.True(resAdded);

        // Researcher owner removes authorized subject -> succeeds!
        bool resRemoved = await _service.RemoveGroupMemberAsync("dr_owner", UserRole.Researcher, resGroup.Id, "pat_authorized", ct);
        Assert.True(resRemoved);

        // Admin adds member
        bool added = await _service.AddGroupMemberAsync("admin", UserRole.Administrator, group.Id, "pat_enrolled", ct);
        Assert.True(added);
        Assert.Contains(_auditService.Entries, e => e.Action == "SUBJECT_GROUP_MEMBER_ADDED" && e.Actor == "admin" && e.Target == $"group:{group.Id}");

        // Duplicate add returns false
        Assert.False(await _service.AddGroupMemberAsync("admin", UserRole.Administrator, group.Id, "pat_enrolled", ct));

        // Unauthorized caller cannot remove
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RemoveGroupMemberAsync("dr_rogue", UserRole.Researcher, group.Id, "pat_enrolled", ct));

        // Admin removes member
        bool removed = await _service.RemoveGroupMemberAsync("admin", UserRole.Administrator, group.Id, "pat_enrolled", ct);
        Assert.True(removed);
        Assert.Contains(_auditService.Entries, e => e.Action == "SUBJECT_GROUP_MEMBER_REMOVED" && e.Actor == "admin" && e.Target == $"group:{group.Id}");

        // Removing non-member returns false
        Assert.False(await _service.RemoveGroupMemberAsync("admin", UserRole.Administrator, group.Id, "pat_enrolled", ct));
    }

    /// <summary>
    /// Verifies GrantGroupAccessAsync and RevokeGroupAccessAsync lifecycle and audit logs.
    /// </summary>
    [Fact]
    public async Task GroupGrants_GrantAndRevoke_AuditedAndPersisted()
    {
        // WHAT: Tests granting and revoking group access for researchers.
        // WHY:  Allows administrators to assign cohorts to researchers with full audit logging.
        // HOW:  Grants and revokes group access, asserting records and audit trail.
        CancellationToken ct = CancellationToken.None;

        SubjectGroup group = await _service.CreateGroupAsync("admin", UserRole.Administrator, "Grant Cohort", null, [], ct);

        // Null checks
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GrantGroupAccessAsync(null!, group.Id, "dr_res", SubjectAccessLevel.GenomicAccess, null, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GrantGroupAccessAsync("admin", null!, "dr_res", SubjectAccessLevel.GenomicAccess, null, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GrantGroupAccessAsync("admin", group.Id, null!, SubjectAccessLevel.GenomicAccess, null, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RevokeGroupAccessAsync(null!, group.Id, "dr_res", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RevokeGroupAccessAsync("admin", null!, "dr_res", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RevokeGroupAccessAsync("admin", group.Id, null!, ct));

        // Grant access
        SubjectGroupGrant grant = await _service.GrantGroupAccessAsync("admin", group.Id, "dr_res", SubjectAccessLevel.GenomicAccess, "Clinical trial grant", ct);
        Assert.NotNull(grant.Id);
        Assert.Equal(group.Id, grant.GroupId);
        Assert.Equal("dr_res", grant.ResearcherUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, grant.AccessLevel);
        Assert.Contains(_auditService.Entries, e => e.Action == "GROUP_ACCESS_GRANTED" && e.Actor == "admin" && e.Target == $"group:{group.Id}");

        // Revoke access
        bool revoked = await _service.RevokeGroupAccessAsync("admin", group.Id, "dr_res", ct);
        Assert.True(revoked);
        Assert.Contains(_auditService.Entries, e => e.Action == "GROUP_ACCESS_REVOKED" && e.Actor == "admin" && e.Target == $"group:{group.Id}");

        // Revoking non-existent grant returns false
        bool revokedAgain = await _service.RevokeGroupAccessAsync("admin", group.Id, "dr_res", ct);
        Assert.False(revokedAgain);

        // Revoking non-existent subject grant returns false
        bool subjRevokedNone = await _service.RevokeSubjectAccessAsync("admin", "dr_res", "nonexistent_pat", ct);
        Assert.False(subjRevokedNone);
    }

    /// <summary>
    /// In-memory test implementation of <see cref="IComplianceAuditService"/> recording action calls.
    /// </summary>
    private sealed class TestComplianceAuditService : IComplianceAuditService
    {
        /// <summary>
        /// Gets recorded compliance audit entries.
        /// </summary>
        public List<(string Actor, string Action, string Target, string Details)> Entries { get; } = [];

        /// <inheritdoc/>
        public AuditRecord LogAction(string operatorId, string action, string resource, string detailsJson)
        {
            Entries.Add((operatorId, action, resource, detailsJson));
            return new AuditRecord
            {
                OperatorId = operatorId,
                Action = action,
                Resource = resource,
                DetailsJson = detailsJson
            };
        }

        /// <inheritdoc/>
        public AuditLedgerVerificationResult VerifyLedgerIntegrity() =>
            new() { IsValid = true, TotalRecordsVerified = Entries.Count };

        /// <inheritdoc/>
        public IReadOnlyList<AuditRecord> GetAuditRecords(int limit = 100) => [];

        /// <inheritdoc/>
        public DeIdentificationResult DeIdentifySubject(string rawSubjectIdentifier, string rawGenomicData, bool gdprConsentConfirmed, bool ginaPurposeLimited) =>
            new() { PseudonymSubjectId = rawSubjectIdentifier, SanitizedData = rawGenomicData };

        /// <inheritdoc/>
        public ComplianceStatusReport GetComplianceStatus() => new();
    }
}
