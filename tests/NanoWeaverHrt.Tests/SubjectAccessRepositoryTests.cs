using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Access;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit and integration tests for <see cref="Migration011_SubjectAccessAndGroups"/> and <see cref="SubjectAccessRepository"/>.
/// </summary>
public sealed class SubjectAccessRepositoryTests : IDisposable
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
    private readonly SubjectAccessRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectAccessRepositoryTests"/> class.
    /// </summary>
    public SubjectAccessRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_subj_repo_test_" + Guid.NewGuid().ToString("N"));
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
        _repository = new SubjectAccessRepository(_connectionFactory);
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
    /// Verifies domain models instantiate and serialize correctly.
    /// </summary>
    [Fact]
    public void DomainModels_PropertyAccessAndInstantiation()
    {
        // WHAT: Verifies property getters and setters on domain model records.
        // WHY:  Ensures all record properties are covered without compiler-generated state leaks.
        // HOW:  Instantiates SubjectGroupMember and asserts property values.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SubjectGroupMember member = new()
        {
            GroupId = "grp_test",
            SubjectUsername = "patient_alice",
            AddedAtUtc = now
        };
        Assert.Equal("grp_test", member.GroupId);
        Assert.Equal("patient_alice", member.SubjectUsername);
        Assert.Equal(now, member.AddedAtUtc);
    }

    /// <summary>
    /// Verifies migration 011 metadata and SQL emission across dialects.
    /// </summary>
    [Fact]
    public void Migration011_MetadataAndDialectValidation()
    {
        // WHAT: Tests Migration011 properties and SQL dialect generators.
        // WHY:  Ensures valid versioning, non-null guards, and dialect table definitions.
        // HOW:  Asserts Version, Name, and checks SQLite / PostgreSQL DDL content.
        Migration011_SubjectAccessAndGroups migration = new();
        Assert.Equal(11L, migration.Version);
        Assert.Equal("011_SubjectAccessAndGroups", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string sqliteSql = migration.GetSql("sqlite");
        string pgSql = migration.GetSql("postgresql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_grants", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_groups", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_group_members", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_group_grants", sqliteSql);

        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_grants", pgSql);
        Assert.Contains("TIMESTAMPTZ", pgSql);
        Assert.Contains("ck_weaver_subject_grants_level", pgSql);
        Assert.Contains("ck_weaver_subject_group_grants_level", pgSql);
    }

    /// <summary>
    /// Verifies constructor guard against null connection factory.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsOnNullFactory()
    {
        // WHAT: Tests constructor null parameter guard.
        // WHY:  Guarantees repository cannot be instantiated without a valid connection factory.
        // HOW:  Asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new SubjectAccessRepository(null!));
    }

    /// <summary>
    /// Verifies direct subject access grant CRUD, upsert, and filtering logic.
    /// </summary>
    [Fact]
    public async Task SubjectGrant_CrudAndFilteringLifecycle()
    {
        // WHAT: Tests SaveSubjectGrantAsync, GetSubjectGrantAsync, ListSubjectGrantsAsync, and RevokeSubjectGrantAsync.
        // WHY:  Validates complete direct grant lifecycle against SQLite.
        // HOW:  Inserts grants, updates via upsert, verifies filters, and revokes.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SubjectGrant grant1 = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ResearcherUsername = "Researcher_Alice",
            SubjectUsername = "Patient_Bob",
            AccessLevel = SubjectAccessLevel.GenomicAccess,
            GrantedBy = "Admin_Root",
            GrantedAtUtc = now,
            Notes = "Clinical trial alpha"
        };

        SubjectGrant grant2 = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ResearcherUsername = "Researcher_Charlie",
            SubjectUsername = "Patient_Bob",
            AccessLevel = SubjectAccessLevel.ReadAccess,
            GrantedBy = "Admin_Root",
            GrantedAtUtc = now.AddMinutes(5),
            Notes = null
        };

        // 1. Save and Get
        Assert.True(await _repository.SaveSubjectGrantAsync(grant1));
        Assert.True(await _repository.SaveSubjectGrantAsync(grant2));

        SubjectGrant? fetched = await _repository.GetSubjectGrantAsync("researcher_alice", "patient_bob");
        Assert.NotNull(fetched);
        Assert.Equal("researcher_alice", fetched.ResearcherUsername);
        Assert.Equal("patient_bob", fetched.SubjectUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, fetched.AccessLevel);
        Assert.Equal("Clinical trial alpha", fetched.Notes);

        SubjectGrant? fetchedNullNotes = await _repository.GetSubjectGrantAsync("researcher_charlie", "patient_bob");
        Assert.NotNull(fetchedNullNotes);
        Assert.Null(fetchedNullNotes.Notes);

        // Not found
        Assert.Null(await _repository.GetSubjectGrantAsync("unknown_res", "patient_bob"));

        // 2. Update via upsert
        SubjectGrant updatedGrant1 = grant1 with
        {
            AccessLevel = SubjectAccessLevel.FullReadAccess,
            Notes = "Escalated to FullReadAccess"
        };
        Assert.True(await _repository.SaveSubjectGrantAsync(updatedGrant1));

        SubjectGrant? reFetched = await _repository.GetSubjectGrantAsync("researcher_alice", "patient_bob");
        Assert.NotNull(reFetched);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, reFetched.AccessLevel);
        Assert.Equal("Escalated to FullReadAccess", reFetched.Notes);

        // 3. Filtered Listing
        var allGrants = await _repository.ListSubjectGrantsAsync();
        Assert.Equal(2, allGrants.Count);

        var aliceGrants = await _repository.ListSubjectGrantsAsync(researcherUsername: "researcher_alice");
        Assert.Single(aliceGrants);
        Assert.Equal("researcher_alice", aliceGrants[0].ResearcherUsername);

        var bobGrants = await _repository.ListSubjectGrantsAsync(subjectUsername: "patient_bob");
        Assert.Equal(2, bobGrants.Count);

        var bothFilter = await _repository.ListSubjectGrantsAsync("researcher_charlie", "patient_bob");
        Assert.Single(bothFilter);

        var noMatch = await _repository.ListSubjectGrantsAsync("researcher_nobody", "patient_bob");
        Assert.Empty(noMatch);

        // 4. Revocation
        Assert.True(await _repository.RevokeSubjectGrantAsync("researcher_alice", "patient_bob"));
        Assert.False(await _repository.RevokeSubjectGrantAsync("researcher_alice", "patient_bob"));
        Assert.Null(await _repository.GetSubjectGrantAsync("researcher_alice", "patient_bob"));
    }

    /// <summary>
    /// Verifies cohort group CRUD, membership management, and cascade deletions.
    /// </summary>
    [Fact]
    public async Task SubjectGroup_CrudMembershipAndCascadeLifecycle()
    {
        // WHAT: Tests SaveGroupAsync, GetGroupAsync, ListGroupsAsync, AddGroupMemberAsync,
        //       RemoveGroupMemberAsync, and DeleteGroupAsync.
        // WHY:  Validates cohort groupings and relational integrity on member enrollment.
        // HOW:  Creates groups, enrolls patients, verifies member counts, and checks deletion cascade.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string groupId1 = "grp_test_01";
        string groupId2 = "grp_test_02";

        SubjectGroup group1 = new()
        {
            Id = groupId1,
            Name = "Estrogen Cohort A",
            Description = "Subjects on oral 17b-estradiol",
            CreatedBy = "Researcher_Alice",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        SubjectGroup group2 = new()
        {
            Id = groupId2,
            Name = "Control Cohort",
            Description = null,
            CreatedBy = "Admin_Root",
            CreatedAtUtc = now.AddMinutes(1),
            UpdatedAtUtc = now.AddMinutes(1)
        };

        // 1. Create groups
        Assert.True(await _repository.SaveGroupAsync(group1));
        Assert.True(await _repository.SaveGroupAsync(group2));

        // 2. Fetch group
        SubjectGroup? fetched = await _repository.GetGroupAsync(groupId1);
        Assert.NotNull(fetched);
        Assert.Equal("Estrogen Cohort A", fetched.Name);
        Assert.Equal("researcher_alice", fetched.CreatedBy);
        Assert.Equal(0, fetched.MemberCount);

        SubjectGroup? fetchedGroup2 = await _repository.GetGroupAsync(groupId2);
        Assert.NotNull(fetchedGroup2);
        Assert.Null(fetchedGroup2.Description);

        // Not found
        Assert.Null(await _repository.GetGroupAsync("non_existent_group"));

        // 3. Update group
        SubjectGroup updatedGroup1 = group1 with
        {
            Name = "Estrogen Cohort A (Updated)",
            Description = "Expanded trial cohort",
            UpdatedAtUtc = now.AddMinutes(10)
        };
        Assert.True(await _repository.SaveGroupAsync(updatedGroup1));
        SubjectGroup? reFetched = await _repository.GetGroupAsync(groupId1);
        Assert.NotNull(reFetched);
        Assert.Equal("Estrogen Cohort A (Updated)", reFetched.Name);
        Assert.Equal("Expanded trial cohort", reFetched.Description);

        // 4. List groups
        var allGroups = await _repository.ListGroupsAsync();
        Assert.Equal(2, allGroups.Count);

        var aliceGroups = await _repository.ListGroupsAsync(createdBy: "researcher_alice");
        Assert.Single(aliceGroups);
        Assert.Equal(groupId1, aliceGroups[0].Id);

        var emptyFilter = await _repository.ListGroupsAsync(createdBy: "nobody");
        Assert.Empty(emptyFilter);

        // 5. Membership management
        Assert.True(await _repository.AddGroupMemberAsync(groupId1, "Patient_Bob", now));
        Assert.True(await _repository.AddGroupMemberAsync(groupId1, "Patient_Carol", now));

        // Duplicate addition is idempotent
        Assert.False(await _repository.AddGroupMemberAsync(groupId1, "Patient_Bob", now));

        // Member count reflects additions
        SubjectGroup? groupWithMembers = await _repository.GetGroupAsync(groupId1);
        Assert.NotNull(groupWithMembers);
        Assert.Equal(2, groupWithMembers.MemberCount);

        var members = await _repository.ListGroupMemberUsernamesAsync(groupId1);
        Assert.Equal(2, members.Count);
        Assert.Contains("patient_bob", members);
        Assert.Contains("patient_carol", members);

        // Enrolled groups for subject
        var bobGroups = await _repository.GetMemberGroupsForSubjectAsync("Patient_Bob");
        Assert.Single(bobGroups);
        Assert.Equal(groupId1, bobGroups[0]);

        // Remove member
        Assert.True(await _repository.RemoveGroupMemberAsync(groupId1, "patient_bob"));
        Assert.False(await _repository.RemoveGroupMemberAsync(groupId1, "patient_bob"));

        var membersAfterRemoval = await _repository.ListGroupMemberUsernamesAsync(groupId1);
        Assert.Single(membersAfterRemoval);
        Assert.Equal("patient_carol", membersAfterRemoval[0]);

        // 6. Delete Group (and cascade)
        Assert.True(await _repository.DeleteGroupAsync(groupId1));
        Assert.False(await _repository.DeleteGroupAsync(groupId1));
        Assert.Null(await _repository.GetGroupAsync(groupId1));

        var membersAfterDelete = await _repository.ListGroupMemberUsernamesAsync(groupId1);
        Assert.Empty(membersAfterDelete);
    }

    /// <summary>
    /// Verifies cohort group access grant CRUD, upsert, and filtering logic.
    /// </summary>
    [Fact]
    public async Task SubjectGroupGrant_CrudAndFilteringLifecycle()
    {
        // WHAT: Tests SaveGroupGrantAsync, GetGroupGrantAsync, ListGroupGrantsAsync, and RevokeGroupGrantAsync.
        // WHY:  Validates group-level access grants to researchers.
        // HOW:  Creates cohort group, grants access to researchers, updates, filters, and revokes.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string groupId = "grp_grant_test_01";

        SubjectGroup group = new()
        {
            Id = groupId,
            Name = "Endocrine Kinetics Cohort",
            Description = "PBPK simulation trial cohort",
            CreatedBy = "Admin_Root",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await _repository.SaveGroupAsync(group);

        SubjectGroupGrant grant1 = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            ResearcherUsername = "Researcher_Dave",
            AccessLevel = SubjectAccessLevel.GenomicAccess,
            GrantedBy = "Admin_Root",
            GrantedAtUtc = now,
            Notes = "Full genomic investigation grant"
        };

        SubjectGroupGrant grant2 = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            ResearcherUsername = "Researcher_Eve",
            AccessLevel = SubjectAccessLevel.ReadAccess,
            GrantedBy = "Admin_Root",
            GrantedAtUtc = now.AddMinutes(2),
            Notes = null
        };

        // 1. Save and Get
        Assert.True(await _repository.SaveGroupGrantAsync(grant1));
        Assert.True(await _repository.SaveGroupGrantAsync(grant2));

        SubjectGroupGrant? fetched = await _repository.GetGroupGrantAsync(groupId, "researcher_dave");
        Assert.NotNull(fetched);
        Assert.Equal(groupId, fetched.GroupId);
        Assert.Equal("researcher_dave", fetched.ResearcherUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, fetched.AccessLevel);

        SubjectGroupGrant? fetchedNullNotes = await _repository.GetGroupGrantAsync(groupId, "researcher_eve");
        Assert.NotNull(fetchedNullNotes);
        Assert.Null(fetchedNullNotes.Notes);

        // Not found
        Assert.Null(await _repository.GetGroupGrantAsync(groupId, "non_existent_researcher"));

        // 2. Update via upsert
        SubjectGroupGrant updatedGrant = grant1 with
        {
            AccessLevel = SubjectAccessLevel.FullReadAccess,
            Notes = "Promoted to FullReadAccess"
        };
        Assert.True(await _repository.SaveGroupGrantAsync(updatedGrant));

        SubjectGroupGrant? reFetched = await _repository.GetGroupGrantAsync(groupId, "researcher_dave");
        Assert.NotNull(reFetched);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, reFetched.AccessLevel);
        Assert.Equal("Promoted to FullReadAccess", reFetched.Notes);

        // 3. Filtered Listing
        var allGrants = await _repository.ListGroupGrantsAsync();
        Assert.Equal(2, allGrants.Count);

        var daveGrants = await _repository.ListGroupGrantsAsync(researcherUsername: "researcher_dave");
        Assert.Single(daveGrants);
        Assert.Equal("researcher_dave", daveGrants[0].ResearcherUsername);

        var groupGrants = await _repository.ListGroupGrantsAsync(groupId: groupId);
        Assert.Equal(2, groupGrants.Count);

        var emptyGrants = await _repository.ListGroupGrantsAsync(researcherUsername: "nobody", groupId: groupId);
        Assert.Empty(emptyGrants);

        // 4. Revocation
        Assert.True(await _repository.RevokeGroupGrantAsync(groupId, "researcher_dave"));
        Assert.False(await _repository.RevokeGroupGrantAsync(groupId, "researcher_dave"));
        Assert.Null(await _repository.GetGroupGrantAsync(groupId, "researcher_dave"));
    }

    /// <summary>
    /// Verifies ArgumentNullException guards across all repository methods.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentNullGuards()
    {
        // WHAT: Verifies null argument validation on all repository methods.
        // WHY:  Enforces strict defensive programming without silent null acceptance.
        // HOW:  Asserts ArgumentNullException on each guarded method.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetSubjectGrantAsync(null!, "sub"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetSubjectGrantAsync("res", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveSubjectGrantAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RevokeSubjectGrantAsync(null!, "sub"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RevokeSubjectGrantAsync("res", null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetGroupAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveGroupAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.DeleteGroupAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.ListGroupMemberUsernamesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.AddGroupMemberAsync(null!, "sub", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.AddGroupMemberAsync("grp", null!, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RemoveGroupMemberAsync(null!, "sub"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RemoveGroupMemberAsync("grp", null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetGroupGrantAsync(null!, "res"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetGroupGrantAsync("grp", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveGroupGrantAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RevokeGroupGrantAsync(null!, "res"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.RevokeGroupGrantAsync("grp", null!));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.GetMemberGroupsForSubjectAsync(null!));
    }
}
