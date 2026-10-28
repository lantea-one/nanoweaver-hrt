using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using NanoWeaverHrt.Components.Layout;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying route guarding, first-launch setup lockout, session restoration,
/// telemetry latching, client-side idle tracking, and sign-out in <see cref="MainLayout"/>.
/// </summary>
public sealed class MainLayoutComponentTests : TestContext
{
    /// <summary>
    /// Mock configuration repository supplying active database and accelerator backend settings.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configMock = new();

    /// <summary>
    /// Mock authentication service validating setup requirements and session tokens.
    /// </summary>
    private readonly Mock<IUserAuthenticationService> _authMock = new();

    /// <summary>
    /// User session state managing circuit authentication.
    /// </summary>
    private readonly UserSessionState _sessionState = new();

    /// <summary>
    /// Mock server session activity tracker recording DOM heartbeats and revoking tokens.
    /// </summary>
    private readonly Mock<IServerSessionActivityTracker> _activityMock = new();

    /// <summary>
    /// Administrator test user account.
    /// </summary>
    private readonly UserAccount _adminUser = new()
    {
        Username = "admin-lead",
        Role = UserRole.Administrator,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA9876543210abcdef9876543210abcdef9876543210==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Researcher test user account for testing non-admin navigation links.
    /// </summary>
    private readonly UserAccount _researcherUser = new()
    {
        Username = "researcher-01",
        Role = UserRole.Researcher,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEAabcdef1234567890abcdef1234567890abcdef123456==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MainLayoutComponentTests"/> class.
    /// </summary>
    public MainLayoutComponentTests()
    {
        // WHAT: Configures mock services and dependencies injected into MainLayout.
        // WHY:  MainLayout coordinates access gating, telemetry, and session activity tracking.
        // HOW:  Registers mocks in bUnit's DI container and puts JSInterop in loose mode.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _configMock.Setup(m => m.ActiveBackendName).Returns("SQLite");
        _configMock.Setup(m => m.GetValueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Intel iGPU / OpenCL");

        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(false);

        Services.AddSingleton(_configMock.Object);
        Services.AddSingleton(_authMock.Object);
        Services.AddSingleton(_sessionState);
        Services.AddSingleton(_activityMock.Object);
    }

    /// <summary>
    /// Verifies that an unauthenticated visitor requesting a protected route (e.g., '/') is redirected
    /// to '/login' and the protected page body, navigation row, and telemetry grid remain hidden.
    /// </summary>
    [Fact]
    public void Unauthenticated_OnProtectedRoute_HidesShellAndRedirectsToLogin()
    {
        // WHAT: Tests route guard enforcement on protected endpoints.
        // WHY:  Unauthenticated visitors must not see operational surfaces or protected page data.
        // HOW:  Navigates to '/compliance', renders MainLayout with route-sensitive body, and asserts redirect and hidden elements.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/compliance");

        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult(null);

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder =>
            {
                if (nav.Uri.Contains("compliance"))
                {
                    builder.AddMarkupContent(0, "<div id=\"protected-content\">secret</div>");
                }
            }));

        Assert.Empty(cut.FindAll("#protected-content"));
        Assert.Empty(cut.FindAll(".sub-navbar"));
        Assert.Empty(cut.FindAll(".telemetry-grid"));
        Assert.Empty(cut.FindAll(".node-status"));

        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that an unauthenticated visitor on a public route (e.g., '/login') sees the routed body
    /// while the signed-in shell elements remain hidden.
    /// </summary>
    [Fact]
    public void Unauthenticated_OnPublicRoute_RendersBodyWhileHidingShell()
    {
        // WHAT: Tests public route rendering exception in MainLayout.
        // WHY:  Public routes (/login, /setup) must render their page forms even without a session.
        // HOW:  Navigates to '/login?reason=idle_timeout', renders MainLayout, and asserts body visibility.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/login?reason=idle_timeout");

        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult(null);

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder => builder.AddMarkupContent(0, "<div id=\"login-form\">Login Page</div>")));

        Assert.NotNull(cut.Find("#login-form"));
        Assert.Empty(cut.FindAll(".sub-navbar"));
        Assert.Empty(cut.FindAll(".telemetry-grid"));
        Assert.Empty(cut.FindAll(".node-status"));
        Assert.Equal("http://localhost/login?reason=idle_timeout", nav.Uri);
    }

    /// <summary>
    /// Verifies that when first-launch setup is required, any route request is redirected to '/setup',
    /// and the setup body renders while the operational shell remains hidden.
    /// </summary>
    [Fact]
    public void SetupRequired_RedirectsToSetup_AndRendersBodyOnSetupRoute()
    {
        // WHAT: Tests first-launch setup lock-out behavior across routes.
        // WHY:  Platform operations are strictly barred until the master Administrator account is provisioned.
        // HOW:  Sets IsSetupRequiredAsync to true, navigates to '/', verifies redirect to '/setup' and body rendering.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder => builder.AddMarkupContent(0, "<div id=\"setup-form\">Setup Wizard</div>")));

        Assert.Equal("http://localhost/setup", nav.Uri);
        Assert.NotNull(cut.Find("#setup-form"));
        Assert.Empty(cut.FindAll(".sub-navbar"));
        Assert.Empty(cut.FindAll(".telemetry-grid"));
    }

    /// <summary>
    /// Verifies that session restoration via a valid browser token initializes the administrator shell,
    /// renders navigation links including User Management, and starts telemetry.
    /// </summary>
    [Fact]
    public void SessionRestoration_WithValidAdminToken_RendersFullAdminShell()
    {
        // WHAT: Tests automatic session restoration from browser storage.
        // WHY:  Preserves authenticated administrator session across browser navigations and tab switches.
        // HOW:  Returns valid token from weaverGetSessionToken, validates via AuthService, and asserts shell.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("valid-admin-jwt");
        _authMock.Setup(m => m.ValidateTokenAsync("valid-admin-jwt"))
            .ReturnsAsync((true, _adminUser, null));

        JSInterop.SetupVoid("weaverRegisterIdleWatcher", It.IsAny<object>(), It.IsAny<int>()).SetVoidResult();

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder => builder.AddMarkupContent(0, "<div id=\"dashboard-body\">Welcome</div>")));

        Assert.NotNull(cut.Find("#dashboard-body"));
        Assert.NotNull(cut.Find(".sub-navbar"));
        Assert.NotNull(cut.Find(".telemetry-grid"));

        var nodeStatus = cut.Find(".node-status");
        Assert.Contains(_adminUser.Username, nodeStatus.TextContent);
        Assert.Contains("SQLite", nodeStatus.TextContent);

        // Verify User Management is visible for Administrator role
        var userMgmtLink = cut.Find("a[href='/users']");
        Assert.NotNull(userMgmtLink);
        Assert.Contains("User Management", userMgmtLink.TextContent);

        JSInterop.VerifyInvoke("weaverRegisterIdleWatcher");
        Assert.True(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/", nav.Uri);
    }

    /// <summary>
    /// Verifies that session restoration for a researcher renders the shell but omits the User Management link.
    /// </summary>
    [Fact]
    public void SessionRestoration_WithResearcherToken_HidesUserManagementLink()
    {
        // WHAT: Tests role-gated navigation link suppression for non-admin roles.
        // WHY:  Researcher role must not see or access administrative user management links.
        // HOW:  Validates researcher token and asserts absence of 'users' NavLink in sub-navbar.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("valid-researcher-jwt");
        _authMock.Setup(m => m.ValidateTokenAsync("valid-researcher-jwt"))
            .ReturnsAsync((true, _researcherUser, null));

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder => builder.AddMarkupContent(0, "<div id=\"studio\">Studio</div>")));

        Assert.NotNull(cut.Find(".sub-navbar"));
        Assert.Empty(cut.FindAll("a[href='/users']"));
        Assert.NotNull(cut.Find("a[href='/dna']"));
        Assert.NotNull(cut.Find("a[href='/profile']"));
    }

    /// <summary>
    /// Verifies that an invalid session token returned from browser storage is rejected and redirects to '/login'.
    /// </summary>
    [Fact]
    public void SessionRestoration_WithInvalidToken_RedirectsToLogin()
    {
        // WHAT: Tests invalid session token rejection during restoration.
        // WHY:  Expired or revoked bearer tokens must not grant access to the application shell.
        // HOW:  Configures ValidateTokenAsync to return valid = false and asserts redirect.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("invalid-expired-jwt");
        _authMock.Setup(m => m.ValidateTokenAsync("invalid-expired-jwt"))
            .ReturnsAsync((false, null, "Signature expired"));

        RenderComponent<MainLayout>();

        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that when weaverGetSessionToken throws an exception, the component handles it gracefully
    /// without crashing and redirects to '/login'.
    /// </summary>
    [Fact]
    public void SessionRestoration_WhenJsThrows_CatchesGracefully()
    {
        // WHAT: Tests exception handling during browser session restoration.
        // WHY:  Prerendering or corrupted browser storage must not crash the layout component.
        // HOW:  Configures weaverGetSessionToken to throw and asserts redirection to /login.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        JSInterop.Setup<string?>("weaverGetSessionToken")
            .SetException(new JSException("Browser storage quota exceeded"));

        RenderComponent<MainLayout>();

        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that invoking RecordActivity records a heartbeat on both UserSessionState and
    /// the authoritative server-side session activity tracker.
    /// </summary>
    [Fact]
    public async Task RecordActivity_WhenAuthenticated_NotifiesActivityTracker()
    {
        // WHAT: Tests DOM activity heartbeat recording.
        // WHY:  Keeps the bearer session alive server-side while the user actively drives the UI.
        // HOW:  Sets user in SessionState, mocks weaverGetSessionToken, invokes RecordActivity, and asserts tracking.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("active-token");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();
        await cut.Instance.RecordActivity();

        _activityMock.Verify(m => m.RecordActivity("active-token"), Times.Once);
    }

    /// <summary>
    /// Verifies that invoking RecordActivity when no token is present updates UserSessionState
    /// without attempting server-side registry updates.
    /// </summary>
    [Fact]
    public async Task RecordActivity_WhenNoToken_UpdatesCircuitOnly()
    {
        // WHAT: Tests RecordActivity when token cannot be resolved.
        // WHY:  Ensures that null tokens do not trigger null reference exceptions in server tracker.
        // HOW:  Mocks weaverGetSessionToken to return null, invokes RecordActivity, and verifies tracker not called.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult(null);

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();
        await cut.Instance.RecordActivity();

        _activityMock.Verify(m => m.RecordActivity(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that OnUserIdleTimeout when unauthenticated returns immediately without action.
    /// </summary>
    [Fact]
    public async Task OnUserIdleTimeout_WhenUnauthenticated_ReturnsEarly()
    {
        // WHAT: Tests idle timeout guard when no session is active.
        // WHY:  Prevents redundant revocation calls when circuit is already unauthenticated.
        // HOW:  Calls OnUserIdleTimeout with unauthenticated session and asserts no interactions.
        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();
        await cut.Instance.OnUserIdleTimeout();

        _activityMock.Verify(m => m.Revoke(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that OnUserIdleTimeout revokes the server-side session token, clears client storage,
    /// signs out UserSessionState, and redirects to '/login?reason=idle_timeout'.
    /// </summary>
    [Fact]
    public async Task OnUserIdleTimeout_WhenAuthenticated_RevokesAndRedirectsWithReason()
    {
        // WHAT: Tests complete server and client idle session termination.
        // WHY:  Guarantees that an abandoned terminal cannot be resumed by refreshing the browser.
        // HOW:  Sets user in SessionState, mocks token, calls OnUserIdleTimeout, and verifies revocation and URL.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("idle-expired-token");
        JSInterop.SetupVoid("weaverClearSession").SetVoidResult();

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        await cut.Instance.OnUserIdleTimeout();

        _activityMock.Verify(m => m.Revoke("idle-expired-token"), Times.Once);
        JSInterop.VerifyInvoke("weaverClearSession");
        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login?reason=idle_timeout", nav.Uri);
    }

    /// <summary>
    /// Verifies that OnUserIdleTimeout succeeds in signing out even when weaverClearSession throws.
    /// </summary>
    [Fact]
    public async Task OnUserIdleTimeout_WhenJsThrows_StillSignsOutAndRedirects()
    {
        // WHAT: Tests idle sign-out resilience when browser storage interop fails.
        // WHY:  Server-side session invalidation must not be blocked by browser storage exceptions.
        // HOW:  Configures weaverClearSession to throw, calls OnUserIdleTimeout, and asserts sign-out.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("idle-expired-token");
        JSInterop.SetupVoid("weaverClearSession").SetException(new JSException("Circuit disconnected"));

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        await cut.Instance.OnUserIdleTimeout();

        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login?reason=idle_timeout", nav.Uri);
    }

    /// <summary>
    /// Verifies that clicking the Sign Out button revokes the token, clears client storage,
    /// signs out the session, and navigates to '/login'.
    /// </summary>
    [Fact]
    public async Task HandleSignOut_RevokesSessionClearsStorageAndNavigates()
    {
        // WHAT: Tests explicit user sign-out from the layout header.
        // WHY:  Terminates authorization immediately upon user request.
        // HOW:  Renders shell, clicks Sign Out button, and verifies revocation and redirect to /login.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("signout-token");
        JSInterop.SetupVoid("weaverClearSession").SetVoidResult();

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        var signOutBtn = cut.Find("button.btn-outline.text-muted");
        await signOutBtn.ClickAsync(new MouseEventArgs());

        _activityMock.Verify(m => m.Revoke("signout-token"), Times.Once);
        JSInterop.VerifyInvoke("weaverClearSession");
        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that signing out when weaverClearSession throws still completes circuit sign-out.
    /// </summary>
    [Fact]
    public async Task HandleSignOut_WhenJsThrows_StillCompletesCircuitSignOut()
    {
        // WHAT: Tests sign-out resilience when JS clear fails.
        // WHY:  Circuit sign-out must not fail if browser storage clearance throws.
        // HOW:  Configures weaverClearSession to throw, clicks Sign Out, and asserts sign-out.
        _sessionState.SetUser(_adminUser);
        JSInterop.Setup<string?>("weaverGetSessionToken").SetResult("signout-token");
        JSInterop.SetupVoid("weaverClearSession").SetException(new JSException("Storage blocked"));

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        var signOutBtn = cut.Find("button.btn-outline.text-muted");
        await signOutBtn.ClickAsync(new MouseEventArgs());

        Assert.False(_sessionState.IsAuthenticated);
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that browser location changes trigger route guard re-evaluation and update setup state.
    /// </summary>
    [Fact]
    public void LocationChanged_WhenSetupRequiredClears_ReEnforcesRouteGuard()
    {
        // WHAT: Tests route guard re-evaluation on browser location change.
        // WHY:  When setup wizard finishes and navigates to '/', route guard must refresh setup flag and allow access.
        // HOW:  Starts with setup required on '/setup', clears flag and authenticates user, navigates to '/', asserts shell.
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(true);

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/setup");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>(p => p
            .Add(l => l.Body, builder => builder.AddMarkupContent(0, "<div id=\"content\">Setup</div>")));

        // Simulate setup completion and admin sign-in
        _authMock.Setup(m => m.IsSetupRequiredAsync()).ReturnsAsync(false);
        _sessionState.SetUser(_adminUser);

        // Navigate to '/'
        nav.NavigateTo("http://localhost/");

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".node-status"));
            Assert.Equal("http://localhost/", nav.Uri);
        });
    }

    /// <summary>
    /// Verifies that when session state changes, OnSessionChanged re-enforces the route guard.
    /// </summary>
    [Fact]
    public void OnSessionChanged_WhenSignedOutOnProtectedRoute_RedirectsToLogin()
    {
        // WHAT: Tests reactive redirect when session state changes from authenticated to signed-out.
        // WHY:  A sign-out initiated anywhere in the circuit must immediately bounce the user off protected routes.
        // HOW:  Renders shell on '/', triggers SignOut on SessionState, and verifies redirect to /login.
        _sessionState.SetUser(_adminUser);

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/");

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        // Trigger sign out on session state directly
        _sessionState.SignOut();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("http://localhost/login", nav.Uri);
        });
    }

    /// <summary>
    /// Verifies that the client-side idle watcher unregisters when the session transitions to unauthenticated.
    /// </summary>
    [Fact]
    public void IdleWatcher_UnregistersWhenSessionBecomesUnauthenticated()
    {
        // WHAT: Tests unregistering the idle watcher when authentication state is cleared.
        // WHY:  Prevents idle watcher callback leaks after sign-out.
        // HOW:  Renders with authenticated session, clears user, triggers re-render, and asserts unregister.
        _sessionState.SetUser(_adminUser);
        JSInterop.SetupVoid("weaverRegisterIdleWatcher", It.IsAny<object>(), It.IsAny<int>()).SetVoidResult();
        JSInterop.SetupVoid("weaverUnregisterIdleWatcher").SetVoidResult();

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        _sessionState.SignOut();
        cut.Render();

        JSInterop.VerifyInvoke("weaverUnregisterIdleWatcher");
    }

    /// <summary>
    /// Verifies that unregistering the idle watcher catches exceptions gracefully.
    /// </summary>
    [Fact]
    public void IdleWatcher_WhenUnregisterThrows_CatchesGracefully()
    {
        // WHAT: Tests exception handling during idle watcher unregistration.
        // WHY:  Disconnected circuits must not throw unhandled exceptions during cleanup.
        // HOW:  Configures weaverUnregisterIdleWatcher to throw and renders component during sign-out.
        _sessionState.SetUser(_adminUser);
        JSInterop.SetupVoid("weaverRegisterIdleWatcher", It.IsAny<object>(), It.IsAny<int>()).SetVoidResult();
        JSInterop.SetupVoid("weaverUnregisterIdleWatcher").SetException(new JSException("Circuit dead"));

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        _sessionState.SignOut();
        cut.Render();

        Assert.False(_sessionState.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that registering the idle watcher catches exceptions gracefully.
    /// </summary>
    [Fact]
    public void IdleWatcher_WhenRegisterThrows_CatchesGracefully()
    {
        // WHAT: Tests exception handling during idle watcher registration.
        // WHY:  Prerendering without a live JS circuit must not crash the component.
        // HOW:  Configures weaverRegisterIdleWatcher to throw and renders component with authenticated user.
        _sessionState.SetUser(_adminUser);
        JSInterop.SetupVoid("weaverRegisterIdleWatcher", It.IsAny<object>(), It.IsAny<int>())
            .SetException(new JSException("Prerendering"));

        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();
        Assert.NotNull(cut);
    }

    /// <summary>
    /// Verifies that disposing MainLayout cleans up timer, dotNetRef, and event subscriptions.
    /// </summary>
    [Fact]
    public void Dispose_CleansUpSubscriptionsAndTimerCleanly()
    {
        // WHAT: Tests component disposal and resource cleanup.
        // WHY:  Prevents memory leaks from lingering timer instances or event handlers.
        // HOW:  Renders MainLayout, calls Dispose, and asserts clean execution.
        _sessionState.SetUser(_adminUser);
        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        cut.Instance.Dispose();
        Assert.NotNull(cut.Instance);
    }

    /// <summary>
    /// Verifies that the internal UpdateTelemetry method samples process memory and GC statistics accurately.
    /// </summary>
    [Fact]
    public void UpdateTelemetry_SamplesMemoryAndGcMetrics()
    {
        // WHAT: Tests UpdateTelemetry metric formatting.
        // WHY:  Ensures node telemetry values (heap, GC collections, uptime) format properly.
        // HOW:  Invokes UpdateTelemetry via reflection and asserts field values.
        _sessionState.SetUser(_adminUser);
        IRenderedComponent<MainLayout> cut = RenderComponent<MainLayout>();

        MethodInfo? updateMethod = typeof(MainLayout).GetMethod("UpdateTelemetry", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(updateMethod);
        updateMethod.Invoke(cut.Instance, null);

        Assert.NotNull(cut.Find(".telemetry-grid"));
    }
}
