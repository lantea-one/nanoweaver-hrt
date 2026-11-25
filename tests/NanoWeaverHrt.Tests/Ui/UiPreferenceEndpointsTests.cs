using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Ui;
using Xunit;

namespace NanoWeaverHrt.Tests.Ui;

/// <summary>
/// End-to-end Minimal API integration tests for <c>/api/ui/preferences</c> (<c>GET</c>, <c>PUT</c>, <c>DELETE</c>),
/// node default inheritance via <c>Ui:DefaultDisplayMode</c>, role accessibility across Patient/Researcher/Admin,
/// and RFC 7807 validation error handling.
/// </summary>
public sealed class UiPreferenceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Isolated temporary data root for the test web host.
    /// </summary>
    private readonly string _testRoot;

    /// <summary>
    /// Isolated web application factory bound to <see cref="_testRoot"/>.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Administrator-authenticated HTTP client.
    /// </summary>
    private readonly HttpClient _adminClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiPreferenceEndpointsTests"/> class.
    /// </summary>
    /// <param name="factory">Shared xUnit class-fixture <see cref="WebApplicationFactory{Program}"/>.</param>
    public UiPreferenceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        // WHAT: Spins up an isolated WebApplicationFactory host with temporary storage directories and an admin client.
        // WHY:  Prevents live workspace database pollution while exercising full ASP.NET Core middleware and routing.
        // HOW:  Sets TRAJECTORY_* directories under a unique temp root and bootstraps testadmin credentials.
        _testRoot = Path.Combine(Path.GetTempPath(), $"nw_uipref_api_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(_testRoot, "shared-data");
        string exportDir = Path.Combine(_testRoot, "exports");
        string parquetDir = Path.Combine(_testRoot, "parquet");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(parquetDir);
        TestFixturesHelper.EnsureTestFixtures(dataDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", dataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", parquetDir);
        });

        _adminClient = _factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(_adminClient, _factory);
    }

    /// <summary>
    /// Provisions an account in the isolated host and returns a configured HTTP client holding its Bearer token.
    /// </summary>
    /// <param name="username">Unique username to create.</param>
    /// <param name="role">Authorization role assigned to the account.</param>
    /// <returns>An authenticated <see cref="HttpClient"/> bound to the new account.</returns>
    private async Task<HttpClient> CreateClientForRoleAsync(string username, UserRole role)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            username,
            role,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        UserAccount account = await auth.CreateUserAsync(
            username,
            role,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        string token = auth.GenerateBearerToken(account);
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Verifies authentication enforcement, validation errors, GET/PUT/DELETE preference round-trips across all roles,
    /// and inheritance of <c>Ui:DefaultDisplayMode</c> when no personal override is stored.
    /// </summary>
    [Fact]
    public async Task UiPreferenceEndpoints_FullLifecycleAndDefaultInheritance_WorkEndToEnd()
    {
        // WHAT: Exercises GET/PUT/DELETE /api/ui/preferences across unauthenticated callers, validation failures, and all roles.
        // WHY:  Milestone 47 (P247): Verifies full HTTP contract, RFC 7807 error handling, and node default fallback semantics.
        // HOW:  Executes unauthenticated requests, invalid JSON payloads, role-scoped toggles, reset, and Ui:DefaultDisplayMode override.
        using HttpClient anonClient = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync("/api/ui/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.PutAsJsonAsync("/api/ui/preferences", new UpdateUiPreferenceRequest { DisplayMode = UiDisplayMode.Advanced })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.DeleteAsync("/api/ui/preferences")).StatusCode);

        using HttpClient patientClient = await CreateClientForRoleAsync("m47_patient_user", UserRole.Patient);
        using HttpClient researcherClient = await CreateClientForRoleAsync("m47_researcher_user", UserRole.Researcher);

        // Null JSON body and invalid enum value must return HTTP 400 RFC 7807 SecureProblemDetails
        using (StringContent nullContent = new("null", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await patientClient.PutAsync("/api/ui/preferences", nullContent)).StatusCode);
        }

        using (StringContent invalidEnumContent = new("{\"displayMode\":999}", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await patientClient.PutAsync("/api/ui/preferences", invalidEnumContent)).StatusCode);
        }

        // Initial default is Simple without personal override
        EffectiveUiPreference? initialPatient = await patientClient.GetFromJsonAsync<EffectiveUiPreference>("/api/ui/preferences");
        Assert.NotNull(initialPatient);
        Assert.Equal("m47_patient_user", initialPatient.Username);
        Assert.Equal(UiDisplayMode.Simple, initialPatient.DisplayMode);
        Assert.False(initialPatient.IsUserOverride);
        Assert.Equal(UiDisplayMode.Simple, initialPatient.NodeDefaultDisplayMode);

        // PUT Advanced -> persists for the caller across GET
        HttpResponseMessage putResp = await researcherClient.PutAsJsonAsync("/api/ui/preferences", new UpdateUiPreferenceRequest
        {
            DisplayMode = UiDisplayMode.Advanced
        });
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        EffectiveUiPreference? updatedRes = await putResp.Content.ReadFromJsonAsync<EffectiveUiPreference>();
        Assert.NotNull(updatedRes);
        Assert.Equal(UiDisplayMode.Advanced, updatedRes.DisplayMode);
        Assert.True(updatedRes.IsUserOverride);

        EffectiveUiPreference? fetchedRes = await researcherClient.GetFromJsonAsync<EffectiveUiPreference>("/api/ui/preferences");
        Assert.NotNull(fetchedRes);
        Assert.Equal(UiDisplayMode.Advanced, fetchedRes.DisplayMode);
        Assert.True(fetchedRes.IsUserOverride);

        // Save explicit Simple override on Patient, then switch node default to Advanced
        HttpResponseMessage patSimpleResp = await patientClient.PutAsJsonAsync("/api/ui/preferences", new UpdateUiPreferenceRequest
        {
            DisplayMode = UiDisplayMode.Simple
        });
        Assert.Equal(HttpStatusCode.OK, patSimpleResp.StatusCode);

        using (IServiceScope cfgScope = _factory.Services.CreateScope())
        {
            IConfigurationRepository cfgRepo = cfgScope.ServiceProvider.GetRequiredService<IConfigurationRepository>();
            await cfgRepo.SetDatabaseOverrideAsync(UiPreferenceService.DefaultDisplayModeConfigKey, "Advanced", "Default to Advanced for test");
        }

        // Patient keeps explicit Simple override even though node default is Advanced; Admin without override inherits Advanced
        EffectiveUiPreference? patOverridden = await patientClient.GetFromJsonAsync<EffectiveUiPreference>("/api/ui/preferences");
        Assert.NotNull(patOverridden);
        Assert.Equal(UiDisplayMode.Simple, patOverridden.DisplayMode);
        Assert.True(patOverridden.IsUserOverride);
        Assert.Equal(UiDisplayMode.Advanced, patOverridden.NodeDefaultDisplayMode);

        EffectiveUiPreference? adminInherited = await _adminClient.GetFromJsonAsync<EffectiveUiPreference>("/api/ui/preferences");
        Assert.NotNull(adminInherited);
        Assert.Equal(UiDisplayMode.Advanced, adminInherited.DisplayMode);
        Assert.False(adminInherited.IsUserOverride);

        // DELETE removes personal override so Researcher now inherits node default Advanced
        HttpResponseMessage delResp = await researcherClient.DeleteAsync("/api/ui/preferences");
        Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
        EffectiveUiPreference? resetRes = await delResp.Content.ReadFromJsonAsync<EffectiveUiPreference>();
        Assert.NotNull(resetRes);
        Assert.Equal(UiDisplayMode.Advanced, resetRes.DisplayMode);
        Assert.False(resetRes.IsUserOverride);
    }

    /// <summary>
    /// Cleans up the HTTP client, test factory scope, and temporary data directory.
    /// </summary>
    public void Dispose()
    {
        _adminClient.Dispose();
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup
        }
    }
}
