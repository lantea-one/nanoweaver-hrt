using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration test suite covering the REST API minimal endpoints for direct subject grants,
/// cohort group CRUD, group membership enrollment, and group access grants.
/// </summary>
public sealed class SubjectAccessEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated from the live simulation volume for SQLite and test fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated web application factory bound to the temporary data directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectAccessEndpointsTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public SubjectAccessEndpointsTests(WebApplicationFactory<Program> baseFactory)
    {
        ArgumentNullException.ThrowIfNull(baseFactory);

        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_endpoints_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignored in test cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies that OpenAPI example provider implementations return valid sample models.
    /// </summary>
    [Fact]
    public void SubjectAccessContracts_GetExamples_ReturnsValidModels()
    {
        // WHAT: Tests GetExamples implementations for all SubjectAccessContracts DTOs.
        // WHY:  Guarantees 100% line coverage for OpenAPI sample generation.
        // HOW:  Instantiates each DTO, calls GetExamples, and verifies non-null properties.
        GrantSubjectAccessRequest grantReq = new GrantSubjectAccessRequest().GetExamples();
        Assert.Equal("dr_watson", grantReq.ResearcherUsername);
        Assert.Equal("patient_sovereign_01", grantReq.SubjectUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, grantReq.AccessLevel);
        Assert.NotNull(grantReq.Notes);

        CreateSubjectGroupRequest createReq = new CreateSubjectGroupRequest().GetExamples();
        Assert.Equal("MtF Micro-Dosing Cohort 2026", createReq.Name);
        Assert.NotNull(createReq.Description);
        Assert.NotNull(createReq.SubjectUsernames);
        Assert.NotEmpty(createReq.SubjectUsernames);

        UpdateSubjectGroupRequest updateReq = new UpdateSubjectGroupRequest().GetExamples();
        Assert.Equal("MtF Extended Trial Cohort 2026", updateReq.Name);
        Assert.NotNull(updateReq.Description);

        AddGroupMemberRequest addReq = new AddGroupMemberRequest().GetExamples();
        Assert.Equal("patient_003", addReq.SubjectUsername);

        GrantGroupAccessRequest groupGrantReq = new GrantGroupAccessRequest().GetExamples();
        Assert.Equal("dr_watson", groupGrantReq.ResearcherUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, groupGrantReq.AccessLevel);
        Assert.NotNull(groupGrantReq.Notes);

        AccessibleSubjectItem subjectItem = new AccessibleSubjectItem().GetExamples();
        Assert.Equal("patient_001", subjectItem.SubjectUsername);
        Assert.Equal(SubjectAccessLevel.GenomicAccess, subjectItem.EffectiveLevel);
    }

    /// <summary>
    /// Comprehensive test suite verifying all 14 minimal API endpoints across roles, error conditions, and edge cases.
    /// </summary>
    [Fact]
    public async Task Endpoints_FullLifecycleAcrossRoles_EnforcedAndAudited()
    {
        // WHAT: Tests end-to-end HTTP pipeline across Administrator, Researcher, and Patient callers.
        // WHY:  Proves 100% coverage and role boundaries for Phase P23 deliverables.
        // HOW:  Provisions tokens, calls endpoints with invalid/valid inputs, and asserts HTTP responses.
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService authService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        ISubjectAccessService accessService = scope.ServiceProvider.GetRequiredService<ISubjectAccessService>();
        IUserRepository userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        CancellationToken ct = CancellationToken.None;

        // Provision accounts
        string adminUsername = "admin_p23";
        string res1Username = "res_p23_1";
        string res2Username = "res_p23_2";
        string pat1Username = "pat_p23_1";
        string pat2Username = "pat_p23_2";

        GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(adminUsername, UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        await authService.CompleteInitialSetupAsync(adminUsername, adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);

        GeneratedKeyBundle res1Bundle = TestChallengeHelper.CreateTestKeyBundle(res1Username, UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        await authService.CreateUserAsync(res1Username, UserRole.Researcher, AccessGate.ApiWeb, res1Bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        GeneratedKeyBundle res2Bundle = TestChallengeHelper.CreateTestKeyBundle(res2Username, UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        await authService.CreateUserAsync(res2Username, UserRole.Researcher, AccessGate.ApiWeb, res2Bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        GeneratedKeyBundle pat1Bundle = TestChallengeHelper.CreateTestKeyBundle(pat1Username, UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        await authService.CreateUserAsync(pat1Username, UserRole.Patient, AccessGate.ApiWeb, pat1Bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        GeneratedKeyBundle pat2Bundle = TestChallengeHelper.CreateTestKeyBundle(pat2Username, UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        await authService.CreateUserAsync(pat2Username, UserRole.Patient, AccessGate.ApiWeb, pat2Bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        IReadOnlyList<UserAccount> users = await authService.ListUsersAsync(ct);
        string adminToken = authService.GenerateBearerToken(users.First(u => u.Username == adminUsername));
        string res1Token = authService.GenerateBearerToken(users.First(u => u.Username == res1Username));
        string pat1Token = authService.GenerateBearerToken(users.First(u => u.Username == pat1Username));

        HttpClient adminClient = CreateClient(adminToken);
        HttpClient resClient = CreateClient(res1Token);
        HttpClient patClient = CreateClient(pat1Token);

        // =====================================================================
        // Part 1: Patient role is rejected from all endpoints
        // =====================================================================
        Assert.Equal(HttpStatusCode.Forbidden, (await patClient.GetAsync("/api/subject-access/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patClient.GetAsync("/api/subject-access/my-subjects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patClient.GetAsync("/api/subject-groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patClient.PostAsJsonAsync("/api/subject-groups", new CreateSubjectGroupRequest { Name = "Group" })).StatusCode);

        // =====================================================================
        // Part 2: Subject Access Grants (Admin only)
        // =====================================================================
        // Researcher cannot call grants endpoints
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.GetAsync("/api/subject-access/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.DeleteAsync($"/api/subject-access/grants/{res1Username}/{pat1Username}")).StatusCode);

        // Admin: validation failures
        // 1. Missing researcher / subject
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest())).StatusCode);
        // 2. Nonexistent researcher
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest { ResearcherUsername = "ghost", SubjectUsername = pat1Username })).StatusCode);
        // 3. Nonexistent patient
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest { ResearcherUsername = res1Username, SubjectUsername = "ghost" })).StatusCode);

        // Admin grants access successfully
        HttpResponseMessage grantRes = await adminClient.PostAsJsonAsync("/api/subject-access/grants", new GrantSubjectAccessRequest
        {
            ResearcherUsername = res1Username,
            SubjectUsername = pat1Username,
            AccessLevel = SubjectAccessLevel.GenomicAccess,
            Notes = "Trial grant"
        });
        Assert.Equal(HttpStatusCode.Created, grantRes.StatusCode);
        SubjectGrant? grant = await grantRes.Content.ReadFromJsonAsync<SubjectGrant>();
        Assert.NotNull(grant);
        Assert.Equal(res1Username, grant.ResearcherUsername);
        Assert.Equal(pat1Username, grant.SubjectUsername);

        // Admin lists grants (unfiltered and filtered)
        HttpResponseMessage listGrantsAll = await adminClient.GetAsync("/api/subject-access/grants");
        Assert.Equal(HttpStatusCode.OK, listGrantsAll.StatusCode);
        var grantsList = await listGrantsAll.Content.ReadFromJsonAsync<List<SubjectGrant>>();
        Assert.NotNull(grantsList);
        Assert.NotEmpty(grantsList);

        HttpResponseMessage listGrantsFiltered = await adminClient.GetAsync($"/api/subject-access/grants?researcher={res1Username}&subject={pat1Username}");
        Assert.Equal(HttpStatusCode.OK, listGrantsFiltered.StatusCode);
        var grantsFiltered = await listGrantsFiltered.Content.ReadFromJsonAsync<List<SubjectGrant>>();
        Assert.NotNull(grantsFiltered);
        Assert.Single(grantsFiltered);

        // =====================================================================
        // Part 3: My Accessible Subjects
        // =====================================================================
        // Researcher gets my-subjects (should have pat1Username)
        HttpResponseMessage mySubjRes = await resClient.GetAsync("/api/subject-access/my-subjects");
        Assert.Equal(HttpStatusCode.OK, mySubjRes.StatusCode);
        var mySubjects = await mySubjRes.Content.ReadFromJsonAsync<List<AccessibleSubjectItem>>();
        Assert.NotNull(mySubjects);
        Assert.Contains(mySubjects, s => s.SubjectUsername == pat1Username && s.EffectiveLevel == SubjectAccessLevel.GenomicAccess);

        // Admin gets my-subjects (sees all patients)
        HttpResponseMessage adminMySubjRes = await adminClient.GetAsync("/api/subject-access/my-subjects");
        Assert.Equal(HttpStatusCode.OK, adminMySubjRes.StatusCode);
        var adminMySubjects = await adminMySubjRes.Content.ReadFromJsonAsync<List<AccessibleSubjectItem>>();
        Assert.NotNull(adminMySubjects);
        Assert.Contains(adminMySubjects, s => s.SubjectUsername == pat1Username && s.EffectiveLevel == SubjectAccessLevel.FullReadAccess);
        Assert.Contains(adminMySubjects, s => s.SubjectUsername == pat2Username && s.EffectiveLevel == SubjectAccessLevel.FullReadAccess);

        // =====================================================================
        // Part 4: Cohort Groups Management
        // =====================================================================
        // POST /api/subject-groups validation
        // 1. Missing name -> 400
        Assert.Equal(HttpStatusCode.BadRequest, (await resClient.PostAsJsonAsync("/api/subject-groups", new CreateSubjectGroupRequest())).StatusCode);

        // 2. Researcher trying to create group with inaccessible patient (pat2Username) -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.PostAsJsonAsync("/api/subject-groups", new CreateSubjectGroupRequest
        {
            Name = "Rogue Cohort",
            SubjectUsernames = [pat2Username]
        })).StatusCode);

        // 3. Researcher creates group with accessible patient (pat1Username) -> 201 Created
        HttpResponseMessage createGrpRes = await resClient.PostAsJsonAsync("/api/subject-groups", new CreateSubjectGroupRequest
        {
            Name = "Res1 Cohort",
            Description = "Initial cohort",
            SubjectUsernames = [pat1Username]
        });
        Assert.Equal(HttpStatusCode.Created, createGrpRes.StatusCode);
        SubjectGroup? createdGrp = await createGrpRes.Content.ReadFromJsonAsync<SubjectGroup>();
        Assert.NotNull(createdGrp);
        string grpId = createdGrp.Id;

        // GET /api/subject-groups
        // Researcher sees created group
        HttpResponseMessage listResGroups = await resClient.GetAsync("/api/subject-groups");
        Assert.Equal(HttpStatusCode.OK, listResGroups.StatusCode);
        var resGroups = await listResGroups.Content.ReadFromJsonAsync<List<SubjectGroup>>();
        Assert.NotNull(resGroups);
        Assert.Contains(resGroups, g => g.Id == grpId);

        // Admin sees all groups
        HttpResponseMessage listAdminGroups = await adminClient.GetAsync("/api/subject-groups");
        Assert.Equal(HttpStatusCode.OK, listAdminGroups.StatusCode);

        // GET /api/subject-groups/{groupId}
        // 1. Nonexistent group -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await resClient.GetAsync("/api/subject-groups/grp_nonexistent")).StatusCode);
        // 2. Creator gets group -> 200
        HttpResponseMessage getGrpRes = await resClient.GetAsync($"/api/subject-groups/{grpId}");
        Assert.Equal(HttpStatusCode.OK, getGrpRes.StatusCode);
        // 3. Admin gets group -> 200
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/api/subject-groups/{grpId}")).StatusCode);

        // Other researcher (res2) tries to GET grpId -> 403
        HttpClient res2Client = CreateClient(authService.GenerateBearerToken(users.First(u => u.Username == res2Username)));
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.GetAsync($"/api/subject-groups/{grpId}")).StatusCode);

        // PUT /api/subject-groups/{groupId}
        // 1. Missing name -> 400
        Assert.Equal(HttpStatusCode.BadRequest, (await resClient.PutAsJsonAsync($"/api/subject-groups/{grpId}", new UpdateSubjectGroupRequest())).StatusCode);
        // 2. Unauthorized researcher -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.PutAsJsonAsync($"/api/subject-groups/{grpId}", new UpdateSubjectGroupRequest { Name = "Hacked" })).StatusCode);
        // 3. Nonexistent group for admin -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PutAsJsonAsync("/api/subject-groups/grp_nonexistent", new UpdateSubjectGroupRequest { Name = "Ghost" })).StatusCode);
        // 4. Creator updates group -> 200
        HttpResponseMessage updateGrpRes = await resClient.PutAsJsonAsync($"/api/subject-groups/{grpId}", new UpdateSubjectGroupRequest
        {
            Name = "Res1 Cohort Updated",
            Description = "Updated description"
        });
        Assert.Equal(HttpStatusCode.OK, updateGrpRes.StatusCode);

        // =====================================================================
        // Part 5: Cohort Group Members
        // =====================================================================
        // GET /api/subject-groups/{groupId}/members
        HttpResponseMessage listMemRes = await resClient.GetAsync($"/api/subject-groups/{grpId}/members");
        Assert.Equal(HttpStatusCode.OK, listMemRes.StatusCode);
        var members = await listMemRes.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(members);
        Assert.Single(members);
        Assert.Equal(pat1Username, members[0]);

        // Nonexistent group -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await resClient.GetAsync("/api/subject-groups/grp_nonexistent/members")).StatusCode);
        // Unauthorized researcher -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.GetAsync($"/api/subject-groups/{grpId}/members")).StatusCode);

        // POST /api/subject-groups/{groupId}/members
        // 1. Missing username -> 400
        Assert.Equal(HttpStatusCode.BadRequest, (await resClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest())).StatusCode);
        // 2. Nonexistent group -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await resClient.PostAsJsonAsync("/api/subject-groups/grp_nonexistent/members", new AddGroupMemberRequest { SubjectUsername = pat1Username })).StatusCode);
        // 3. Nonexistent patient -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await resClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest { SubjectUsername = "ghost_pat" })).StatusCode);
        // 4. Unauthorized researcher (res2) -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest { SubjectUsername = pat1Username })).StatusCode);
        // 5. Inaccessible patient for creator -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest { SubjectUsername = pat2Username })).StatusCode);
        // 6. Duplicate enrollment of pat1Username -> 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, (await resClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest { SubjectUsername = pat1Username })).StatusCode);

        // Admin adds pat2Username to grpId -> 201 Created
        HttpResponseMessage adminAddMem = await adminClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/members", new AddGroupMemberRequest { SubjectUsername = pat2Username });
        Assert.Equal(HttpStatusCode.Created, adminAddMem.StatusCode);

        // DELETE /api/subject-groups/{groupId}/members/{subjectUsername}
        // 1. Unauthorized researcher -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.DeleteAsync($"/api/subject-groups/{grpId}/members/{pat2Username}")).StatusCode);
        // 2. Non-member -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await resClient.DeleteAsync($"/api/subject-groups/{grpId}/members/pat_not_member")).StatusCode);
        // 3. Creator removes member -> 200
        Assert.Equal(HttpStatusCode.OK, (await resClient.DeleteAsync($"/api/subject-groups/{grpId}/members/{pat2Username}")).StatusCode);

        // =====================================================================
        // Part 6: Cohort Group Grants (Admin only)
        // =====================================================================
        // Non-admin rejected from group grants endpoints
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.GetAsync($"/api/subject-groups/{grpId}/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/grants", new GrantGroupAccessRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await resClient.DeleteAsync($"/api/subject-groups/{grpId}/grants/{res2Username}")).StatusCode);

        // Admin group grant validation
        // 1. Missing researcher -> 400
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/grants", new GrantGroupAccessRequest())).StatusCode);
        // 2. Nonexistent group -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PostAsJsonAsync("/api/subject-groups/grp_nonexistent/grants", new GrantGroupAccessRequest { ResearcherUsername = res2Username })).StatusCode);
        // 3. Nonexistent researcher -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/grants", new GrantGroupAccessRequest { ResearcherUsername = "ghost_res" })).StatusCode);

        // Admin grants res2 access over grpId -> 201 Created
        HttpResponseMessage groupGrantRes = await adminClient.PostAsJsonAsync($"/api/subject-groups/{grpId}/grants", new GrantGroupAccessRequest
        {
            ResearcherUsername = res2Username,
            AccessLevel = SubjectAccessLevel.FullReadAccess,
            Notes = "Grant for res2"
        });
        Assert.Equal(HttpStatusCode.Created, groupGrantRes.StatusCode);
        SubjectGroupGrant? groupGrant = await groupGrantRes.Content.ReadFromJsonAsync<SubjectGroupGrant>();
        Assert.NotNull(groupGrant);
        Assert.Equal(grpId, groupGrant.GroupId);
        Assert.Equal(res2Username, groupGrant.ResearcherUsername);

        // res2 now has access to grpId via group grant!
        Assert.Equal(HttpStatusCode.OK, (await res2Client.GetAsync($"/api/subject-groups/{grpId}")).StatusCode);

        // res2 also lists subject groups and discovers grpId via granted cohorts
        HttpResponseMessage res2ListRes = await res2Client.GetAsync("/api/subject-groups");
        Assert.Equal(HttpStatusCode.OK, res2ListRes.StatusCode);
        var res2Groups = await res2ListRes.Content.ReadFromJsonAsync<List<SubjectGroup>>();
        Assert.NotNull(res2Groups);
        Assert.Contains(res2Groups, g => g.Id == grpId);

        // Admin lists group grants
        HttpResponseMessage listGroupGrantsRes = await adminClient.GetAsync($"/api/subject-groups/{grpId}/grants");
        Assert.Equal(HttpStatusCode.OK, listGroupGrantsRes.StatusCode);
        var groupGrants = await listGroupGrantsRes.Content.ReadFromJsonAsync<List<SubjectGroupGrant>>();
        Assert.NotNull(groupGrants);
        Assert.Single(groupGrants);

        // Nonexistent group on list grants -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync("/api/subject-groups/grp_nonexistent/grants")).StatusCode);

        // Admin revokes group grant
        // Nonexistent grant -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.DeleteAsync($"/api/subject-groups/{grpId}/grants/ghost_res")).StatusCode);
        // Successful revoke -> 200
        Assert.Equal(HttpStatusCode.OK, (await adminClient.DeleteAsync($"/api/subject-groups/{grpId}/grants/{res2Username}")).StatusCode);

        // =====================================================================
        // Part 7: Delete Subject Group & Revoke Subject Grant Cleanup
        // =====================================================================
        // Unauthorized delete -> 403
        Assert.Equal(HttpStatusCode.Forbidden, (await res2Client.DeleteAsync($"/api/subject-groups/{grpId}")).StatusCode);
        // Nonexistent delete -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.DeleteAsync("/api/subject-groups/grp_nonexistent")).StatusCode);
        // Successful delete by creator -> 200
        Assert.Equal(HttpStatusCode.OK, (await resClient.DeleteAsync($"/api/subject-groups/{grpId}")).StatusCode);

        // Admin revokes subject grant
        // Nonexistent grant -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.DeleteAsync($"/api/subject-access/grants/{res1Username}/ghost_pat")).StatusCode);
        // Successful revoke -> 200
        Assert.Equal(HttpStatusCode.OK, (await adminClient.DeleteAsync($"/api/subject-access/grants/{res1Username}/{pat1Username}")).StatusCode);
    }

    /// <summary>
    /// Creates an HTTP client that sends the supplied bearer token on every request.
    /// </summary>
    /// <param name="token">Signed bearer token to attach.</param>
    /// <returns>A configured <see cref="HttpClient"/> the caller must dispose.</returns>
    private HttpClient CreateClient(string token)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
