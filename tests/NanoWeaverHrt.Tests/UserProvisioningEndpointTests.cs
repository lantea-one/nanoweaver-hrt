using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// HTTP integration suite for the Administrator-only account provisioning endpoint
/// <c>POST /api/users</c>, and proof that a fault thrown inside the endpoint handler is sanitized
/// before it reaches the client.
/// </summary>
/// <remarks>
/// Two separate gaps are closed here.
/// <para>
/// First, coverage: <c>POST /api/users</c> is the endpoint that mints accounts bound to caller-supplied
/// key material, and it had no HTTP test at all. Its 26 lines were invisible to the gate only because
/// <c>coverlet.runsettings</c> excludes <c>CompilerGeneratedAttribute</c>, which hides every minimal-API
/// endpoint lambda. An untested account-provisioning route is a hole whatever that exclusion is set to.
/// </para>
/// <para>
/// Second, disclosure: the handler used to answer a fault with <c>{ error = ex.Message }</c>. That
/// returns normally, so <c>SecureExceptionHandlerMiddleware</c> never saw it and
/// <c>SecureExceptionSanitizer</c> never ran. The sanitizer was well tested through the middleware path
/// and not at all through the handler path, which is exactly where the raw text was escaping.
/// </para>
/// </remarks>
public sealed class UserProvisioningEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// A hostile exception sentence carrying one of every secret class Prime Directive 14 names.
    /// </summary>
    /// <remarks>
    /// The segments are semicolon-separated on purpose. <c>SecureExceptionSanitizer</c>'s
    /// connection-string pattern consumes everything up to the next <c>;</c>, so without the separators a
    /// single redaction would swallow the SQL fragment too and the test could not prove that the SQL pass
    /// fired independently.
    /// </remarks>
    private const string HostileFaultMessage =
        "Repository write failed at /home/operator/weaver/keys/admin.pem; "
        + "Host=db.internal;Port=5432;Username=weaver;Password=SuperSecretDbPassword123!; "
        + "failing statement was SELECT username, public_key FROM weaver_users WHERE username = 'gate_admin'";

    /// <summary>
    /// Authentication service decorator that delegates everything except account creation, which faults.
    /// </summary>
    /// <remarks>
    /// A wholesale fake cannot be used: the authentication middleware validates the bearer token through
    /// this same interface, so replacing it entirely would make every request 401 and the handler under
    /// test would never run. Delegating keeps the real sign-in path intact and fails only the one call
    /// whose catch block is being proven.
    /// </remarks>
    private sealed class FaultInjectingAuthenticationService : IUserAuthenticationService
    {
        /// <summary>
        /// The real service every call but <see cref="CreateUserAsync"/> is forwarded to.
        /// </summary>
        private readonly IUserAuthenticationService _inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="FaultInjectingAuthenticationService"/> class.
        /// </summary>
        /// <param name="inner">The real authentication service to delegate to.</param>
        public FaultInjectingAuthenticationService(IUserAuthenticationService inner) => _inner = inner;

        /// <inheritdoc/>
        public Task<bool> IsSetupRequiredAsync(CancellationToken ct = default) => _inner.IsSetupRequiredAsync(ct);

        /// <inheritdoc/>
        public Task<UserAccount> CompleteInitialSetupAsync(string adminUsername, string publicKeyBase64, KeyAlgorithm algorithm, AccessGate accessGate = AccessGate.ApiWeb, CancellationToken ct = default)
            => _inner.CompleteInitialSetupAsync(adminUsername, publicKeyBase64, algorithm, accessGate, ct);

        /// <inheritdoc/>
        public GeneratedKeyBundle GenerateSecureKeyBundle(string username, UserRole role, AccessGate gate, KeyAlgorithm algorithm, string? password = null)
            => _inner.GenerateSecureKeyBundle(username, role, gate, algorithm, password);

        /// <inheritdoc/>
        public AuthChallenge CreateChallenge(string username) => _inner.CreateChallenge(username);

        /// <inheritdoc/>
        public Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> VerifyChallengeAndIssueTokenAsync(string username, string challengeId, string signatureBase64, bool isWebRequest = false, CancellationToken ct = default)
            => _inner.VerifyChallengeAndIssueTokenAsync(username, challengeId, signatureBase64, isWebRequest, ct);

        /// <inheritdoc/>
        public Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> AuthenticateWithPrivateKeyAsync(string username, string privateKeyPem, string? password = null, bool isWebRequest = true, CancellationToken ct = default)
            => _inner.AuthenticateWithPrivateKeyAsync(username, privateKeyPem, password, isWebRequest, ct);

        /// <inheritdoc/>
        public Task<(bool Valid, UserAccount? User, string? ErrorReason)> ValidateTokenAsync(string token, CancellationToken ct = default)
            => _inner.ValidateTokenAsync(token, ct);

        /// <inheritdoc/>
        public Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default) => _inner.ListUsersAsync(ct);

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">Always, carrying <see cref="HostileFaultMessage"/>.</exception>
        public Task<UserAccount> CreateUserAsync(string username, UserRole role, AccessGate gate, string publicKeyBase64, KeyAlgorithm algorithm, CancellationToken ct = default)
            => throw new InvalidOperationException(HostileFaultMessage);

        /// <inheritdoc/>
        public string GenerateBearerToken(UserAccount user) => _inner.GenerateBearerToken(user);

        /// <inheritdoc/>
        public Task<bool> DeleteUserAsync(string username, CancellationToken ct = default) => _inner.DeleteUserAsync(username, ct);
    }

    /// <summary>
    /// Temporary directory isolated from the live simulation volume, holding this suite's SQLite database.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Base factory fixture, retained so a second host with an injected fault can be derived from it.
    /// </summary>
    private readonly WebApplicationFactory<Program> _baseFactory;

    /// <summary>
    /// Isolated application host bound to <see cref="_tempDataDir"/>.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserProvisioningEndpointTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture supplied by xUnit.</param>
    public UserProvisioningEndpointTests(WebApplicationFactory<Program> baseFactory)
    {
        ArgumentNullException.ThrowIfNull(baseFactory);

        // WHY: The suite must never touch the repository's own simulations/ directories, so every path the
        //      host resolves is redirected into a per-run temporary directory that Dispose deletes.
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_user_provisioning_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);

        _baseFactory = baseFactory;
        _factory = ConfigureHost(baseFactory, Path.Combine(_tempDataDir, "primary"));
    }

    /// <summary>
    /// Binds a factory to an isolated data directory and an unreachable PostgreSQL endpoint.
    /// </summary>
    /// <param name="baseFactory">Factory to derive the configured host from.</param>
    /// <param name="dataDir">Directory the host must use for its database and trajectory fixtures.</param>
    /// <returns>A configured factory the caller must dispose.</returns>
    private static WebApplicationFactory<Program> ConfigureHost(WebApplicationFactory<Program> baseFactory, string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        TestFixturesHelper.EnsureTestFixtures(dataDir);

        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", dataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(dataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(dataDir, "parquet"));
            // WHY: Port 54399 is closed, so the host takes its SQLite path deterministically rather than
            //      depending on whether a PostgreSQL server happens to be running on the build machine.
            builder.UseSetting("POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1");
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
                // Ignored in test cleanup: the SQLite file may still be held by the disposing host.
            }
        }
    }

    /// <summary>
    /// Completes first-launch setup on the supplied host and returns a token per role.
    /// </summary>
    /// <param name="factory">Host to provision the accounts in.</param>
    /// <returns>Signed bearer tokens for the Administrator, Researcher, and Patient identities.</returns>
    /// <remarks>
    /// Provisioning runs through the service rather than over HTTP, so the endpoint under test is never
    /// also the thing that builds its own fixture.
    /// </remarks>
    private static async Task<(string Administrator, string Researcher, string Patient)> ProvisionRoleTokensAsync(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<UserAuthenticationService>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("prov_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("prov_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);

            GeneratedKeyBundle researcherBundle = auth.GenerateSecureKeyBundle("prov_researcher", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("prov_researcher", UserRole.Researcher, AccessGate.ApiWeb, researcherBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

            GeneratedKeyBundle patientBundle = auth.GenerateSecureKeyBundle("prov_patient", UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("prov_patient", UserRole.Patient, AccessGate.ApiWeb, patientBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        }

        IReadOnlyList<UserAccount> users = await auth.ListUsersAsync();
        string TokenFor(string username) => auth.GenerateBearerToken(
            users.First(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));

        return (TokenFor("prov_admin"), TokenFor("prov_researcher"), TokenFor("prov_patient"));
    }

    /// <summary>
    /// Creates an HTTP client that sends the supplied bearer token on every request.
    /// </summary>
    /// <param name="factory">Host to create the client against.</param>
    /// <param name="token">Signed bearer token to attach, or null to stay unauthenticated.</param>
    /// <returns>A configured <see cref="HttpClient"/> the caller must dispose.</returns>
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string? token)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// Generates a syntactically valid Ed25519 public key for a request body.
    /// </summary>
    /// <param name="factory">Host whose authentication service generates the bundle.</param>
    /// <param name="username">Username the bundle is generated for.</param>
    /// <returns>A Base64 public key the endpoint will accept.</returns>
    private static string FreshPublicKey(WebApplicationFactory<Program> factory, string username)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<UserAuthenticationService>();
        return auth.GenerateSecureKeyBundle(username, UserRole.Researcher, AccessGate.Api, KeyAlgorithm.Ed25519).PublicKeyBase64;
    }

    /// <summary>
    /// Verifies that an Administrator can provision an account and that the new account is really stored.
    /// </summary>
    [Fact]
    public async Task CreateUser_AsAdministrator_StoresTheAccountAndReturnsCreated()
    {
        // WHAT: POSTs a well-formed account, then reads the account listing back.
        // WHY:  This is the endpoint that binds a role and an access gate to caller-supplied key material.
        //       It had no HTTP test, so nothing proved the role and gate in the request are the ones stored.
        // HOW:  Asserts 201 with the documented Location header, then confirms the listing agrees.
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(_factory);
        string publicKey = FreshPublicKey(_factory, "prov_created");
        using HttpClient adminClient = CreateClient(_factory, adminToken);

        HttpResponseMessage response = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "prov_created",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = publicKey
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/users/prov_created", response.Headers.Location?.ToString());

        UserAccount? created = await response.Content.ReadFromJsonAsync<UserAccount>();
        Assert.NotNull(created);
        Assert.Equal("prov_created", created.Username);
        Assert.Equal(UserRole.Patient, created.Role);
        Assert.Equal(AccessGate.Web, created.AccessGate);
        Assert.True(created.IsActive);

        // The account must exist in the authoritative repository, not merely in the response body.
        HttpResponseMessage listing = await adminClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        UserAccount[]? users = await listing.Content.ReadFromJsonAsync<UserAccount[]>();
        Assert.NotNull(users);
        UserAccount stored = users.First(u => u.Username == "prov_created");
        Assert.Equal(UserRole.Patient, stored.Role);
        Assert.Equal(AccessGate.Web, stored.AccessGate);
    }

    /// <summary>
    /// Verifies that a blank username and a blank public key are both refused as RFC 7807 problem details.
    /// </summary>
    /// <param name="username">Username to submit.</param>
    /// <param name="publicKey">Public key to submit.</param>
    [Theory]
    [InlineData("", "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=")]
    [InlineData("   ", "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=")]
    [InlineData("prov_no_key", "")]
    [InlineData("prov_blank_key", "   ")]
    public async Task CreateUser_WithMissingUsernameOrKey_IsRejectedAsProblemDetails(string username, string publicKey)
    {
        // WHAT: Submits each incomplete body and inspects the rejection.
        // WHY:  An account with no key material could never authenticate, and Prime Directive 14 requires the
        //       single SecureProblemDetails schema rather than a second hand-rolled '{ error }' object.
        // HOW:  Asserts 400 with the application/problem+json media type and the published RFC 7807 members.
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(_factory);
        using HttpClient adminClient = CreateClient(_factory, adminToken);

        HttpResponseMessage response = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = username,
            PublicKeyBase64 = publicKey
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Username and publicKeyBase64 are required.", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
        Assert.Contains("urn:weaver:error:", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that only the Administrator role may provision accounts.
    /// </summary>
    [Fact]
    public async Task CreateUser_RejectsEveryNonAdministratorCaller()
    {
        // WHAT: Calls POST /api/users unauthenticated, as Researcher, and as Patient.
        // WHY:  Prime Directive 12 grants user creation to the Administrator alone. An unguarded account
        //       factory is a privilege escalation primitive: any caller could mint an Administrator bound
        //       to a key they hold.
        // HOW:  Asserts 401 without a token and 403 for each authenticated non-Administrator role, and
        //       confirms afterwards that no account was created by any of those attempts.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync(_factory);
        CreateUserRequest escalation = new()
        {
            Username = "prov_escalated_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = FreshPublicKey(_factory, "prov_escalated_admin")
        };

        using HttpClient anonymous = CreateClient(_factory, token: null);
        HttpResponseMessage anonymousResponse = await anonymous.PostAsJsonAsync("/api/users", escalation);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        foreach (string deniedToken in new[] { researcherToken, patientToken })
        {
            using HttpClient deniedClient = CreateClient(_factory, deniedToken);
            HttpResponseMessage denied = await deniedClient.PostAsJsonAsync("/api/users", escalation);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("application/problem+json", denied.Content.Headers.ContentType?.MediaType);

            string deniedBody = await denied.Content.ReadAsStringAsync();
            Assert.Contains("urn:weaver:error:forbidden", deniedBody, StringComparison.Ordinal);
        }

        // None of the three attempts may have created the account they asked for.
        using HttpClient adminClient = CreateClient(_factory, adminToken);
        UserAccount[]? users = await adminClient.GetFromJsonAsync<UserAccount[]>("/api/users");
        Assert.NotNull(users);
        Assert.DoesNotContain(users, u => u.Username == "prov_escalated_admin");
    }

    /// <summary>
    /// Documents that reusing an existing username silently rebinds that account instead of being refused.
    /// </summary>
    /// <remarks>
    /// This test records current behaviour, which is a FINDING rather than a desired contract.
    /// <c>UserRepository.CreateUserAsync</c> issues <c>ON CONFLICT(username) DO UPDATE</c>, an upsert, so a
    /// second <c>POST /api/users</c> for a name that already exists overwrites that account's role, access
    /// gate, algorithm and public key, answers <c>201 Created</c>, and is recorded in the compliance audit
    /// ledger as <c>UserCreated</c>. The endpoint's own <c>catch (InvalidOperationException)</c> is
    /// therefore unreachable on the SQLite path. An endpoint named CreateUser that reports 201 should
    /// refuse a name already in use with <c>409 Conflict</c>; changing that also changes first-launch setup
    /// and the generate-keypair re-key flow, which both rely on the upsert, so it is a deliberate decision
    /// and not a drive-by fix. Should the semantics be corrected, this test is expected to fail and must be
    /// rewritten to assert the refusal.
    /// </remarks>
    [Fact]
    public async Task CreateUser_WithExistingUsername_SilentlyRebindsTheAccount_KnownFinding()
    {
        // WHAT: Creates an account, then posts the same username again with a different role and key.
        // WHY:  Pins the real behaviour of the upsert so the overwrite cannot be mistaken for a refusal, and
        //       so a future correction to 409 Conflict trips loudly here instead of passing unnoticed.
        // HOW:  Compares the stored role and public key before and after the second call.
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(_factory);
        using HttpClient adminClient = CreateClient(_factory, adminToken);

        string firstKey = FreshPublicKey(_factory, "prov_rebound");
        HttpResponseMessage first = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "prov_rebound",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = firstKey
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        string secondKey = FreshPublicKey(_factory, "prov_rebound_again");
        Assert.NotEqual(firstKey, secondKey);

        HttpResponseMessage second = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "prov_rebound",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = secondKey
        });

        // Current behaviour: accepted as a creation, not refused as a conflict.
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        UserAccount[]? users = await adminClient.GetFromJsonAsync<UserAccount[]>("/api/users");
        Assert.NotNull(users);
        Assert.Single(users, u => u.Username == "prov_rebound");

        UserAccount stored = users.First(u => u.Username == "prov_rebound");
        Assert.Equal(UserRole.Administrator, stored.Role);
        Assert.Equal(AccessGate.ApiWeb, stored.AccessGate);
        Assert.Equal(secondKey, stored.PublicKeyBase64);
    }

    /// <summary>
    /// Verifies that a fault raised inside the endpoint handler is sanitized before it reaches the client.
    /// </summary>
    [Fact]
    public async Task CreateUser_WhenProvisioningFaults_SanitizesPathsCredentialsAndSqlFromTheResponse()
    {
        // WHAT: Makes account creation throw an InvalidOperationException whose message carries a server
        //       path, a connection string with a password, and a SELECT ... FROM fragment, then reads the body.
        // WHY:  Prime Directive 14 forbids all three reaching API clients. The handler previously answered
        //       '{ error = ex.Message }', which returns normally and so never passes through
        //       SecureExceptionHandlerMiddleware -- the sanitizer was proven only on the middleware path,
        //       while the handler path was the one actually leaking.
        // HOW:  Overrides IUserAuthenticationService with a decorator that faults only on CreateUserAsync,
        //       so token validation still works, and asserts every secret is absent and each redaction
        //       marker is present.
        string faultDataDir = Path.Combine(_tempDataDir, "fault");
        using WebApplicationFactory<Program> faultFactory = ConfigureHost(_baseFactory, faultDataDir)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserAuthenticationService>(sp =>
                    new FaultInjectingAuthenticationService(sp.GetRequiredService<UserAuthenticationService>()))));

        var (adminToken, _, _) = await ProvisionRoleTokensAsync(faultFactory);
        using HttpClient adminClient = CreateClient(faultFactory, adminToken);

        HttpResponseMessage response = await adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "prov_faulting",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Api,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890="
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();

        // Not one of the three secret classes may survive into the response.
        Assert.DoesNotContain("/home/operator", body, StringComparison.Ordinal);
        Assert.DoesNotContain("admin.pem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecretDbPassword123!", body, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", body, StringComparison.Ordinal);
        Assert.DoesNotContain("weaver_users", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", body, StringComparison.Ordinal);

        // Each redaction pass must be demonstrably the thing that removed them.
        Assert.Contains("[REDACTED_PATH]", body, StringComparison.Ordinal);
        Assert.Contains("Password=[REDACTED]", body, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_SQL]", body, StringComparison.Ordinal);

        // And the rejection is still a usable, correlatable RFC 7807 document.
        Assert.Contains("urn:weaver:error:", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
        Assert.Contains("\"status\": 400", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the last-Administrator guard on account deletion also answers as problem details.
    /// </summary>
    [Fact]
    public async Task DeleteUser_RemovingTheLastAdministrator_IsRefusedAsProblemDetails()
    {
        // WHAT: Deletes the only Administrator account over HTTP.
        // WHY:  This is the one InvalidOperationException on these routes that is reachable without a fake,
        //       so it proves the DELETE handler's converted catch block on the real code path. Losing the
        //       last Administrator would lock the node out of its own administration permanently.
        // HOW:  Asserts 400 problem details carrying the published guard sentence.
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(_factory);
        using HttpClient adminClient = CreateClient(_factory, adminToken);

        HttpResponseMessage response = await adminClient.DeleteAsync("/api/users/prov_admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("last remaining active Administrator", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);

        // The account must still be there.
        UserAccount[]? users = await adminClient.GetFromJsonAsync<UserAccount[]>("/api/users");
        Assert.NotNull(users);
        Assert.Contains(users, u => u.Username == "prov_admin");
    }
}
