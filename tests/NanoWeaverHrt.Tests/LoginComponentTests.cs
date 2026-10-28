using System;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying rendering, DOM masking, asymmetric challenge-response sign-in,
/// and session lifecycle interactions in <see cref="Login"/>.
/// </summary>
public sealed class LoginComponentTests : TestContext
{
    /// <summary>
    /// Shared test user account representing an authenticated administrator.
    /// </summary>
    private readonly UserAccount _testUser = new()
    {
        Username = "admin-tester",
        Role = UserRole.Administrator,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890==",
        CreatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginComponentTests"/> class.
    /// </summary>
    public LoginComponentTests()
    {
        // WHAT: Registers core session dependencies for the Blazor component.
        // WHY:  Login requires UserSessionState for storing authentication status.
        // HOW:  Adds UserSessionState as a singleton into bUnit's service container.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<UserSessionState>();
    }

    /// <summary>
    /// Verifies that rendering the unauthenticated login page with an idle timeout query string
    /// displays the informational warning alert and renders all input controls.
    /// </summary>
    [Fact]
    public void UnauthenticatedRender_WithIdleTimeoutQuery_DisplaysWarningAndBindsKeyFile()
    {
        // WHAT: Tests idle timeout banner display and client-side key file binder invocation.
        // WHY:  Users bounced from protected routes due to idle expiration must understand why.
        // HOW:  Navigates to /login?reason=idle_timeout, renders Login, and verifies DOM elements.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/login?reason=idle_timeout");

        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();

        IRenderedComponent<Login> cut = RenderComponent<Login>();

        var warningAlert = cut.Find(".alert-warning");
        Assert.Contains("Your session has expired due to inactivity", warningAlert.TextContent);

        Assert.NotNull(cut.Find("#loginUser"));
        Assert.NotNull(cut.Find("#privKeyPem"));
        Assert.NotNull(cut.Find("#loginPassword"));
        Assert.NotNull(cut.Find("#keyFileUpload"));
        Assert.NotNull(cut.Find("#loginSubmitBtn"));

        JSInterop.VerifyInvoke("weaverBindKeyFileInput");
    }

    /// <summary>
    /// Verifies that rendering the unauthenticated login page without a query string
    /// displays no warning or error alerts.
    /// </summary>
    [Fact]
    public void UnauthenticatedRender_StandardRoute_DoesNotDisplayAlerts()
    {
        // WHAT: Tests clean initial render of the login view.
        // WHY:  First-time visitors must not see misleading warning or error banners.
        // HOW:  Renders Login on /login and verifies absence of .alert-warning and .alert-danger.
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/login");

        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();

        IRenderedComponent<Login> cut = RenderComponent<Login>();

        Assert.Empty(cut.FindAll(".alert-warning"));
        Assert.Empty(cut.FindAll(".alert-danger"));
    }

    /// <summary>
    /// Verifies that clicking the mask toggle button switches the private key textarea CSS class
    /// between masked and unmasked states and updates the button label.
    /// </summary>
    [Fact]
    public async Task MaskToggle_TogglesCssClassAndButtonText()
    {
        // WHAT: Tests private key visual masking toggle.
        // WHY:  Operators need to visually inspect PEM key contents or obscure them from shoulder surfing.
        // HOW:  Clicks mask toggle button and asserts 'is-masked' CSS class and button text.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var textarea = cut.Find("#privKeyPem");
        Assert.Contains("is-masked", textarea.ClassName);

        var toggleBtn = cut.Find("button.btn-secondary");
        Assert.Equal("👁 Show Key", toggleBtn.TextContent.Trim());

        // Toggle to unmasked
        await toggleBtn.ClickAsync(new MouseEventArgs());
        textarea = cut.Find("#privKeyPem");
        Assert.DoesNotContain("is-masked", textarea.ClassName);
        Assert.Equal("🔒 Mask Key", toggleBtn.TextContent.Trim());

        // Toggle back to masked
        await toggleBtn.ClickAsync(new MouseEventArgs());
        textarea = cut.Find("#privKeyPem");
        Assert.Contains("is-masked", textarea.ClassName);
        Assert.Equal("👁 Show Key", toggleBtn.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that failed authentication returning a specific error message renders the error banner.
    /// </summary>
    [Fact]
    public async Task Authenticate_WhenJsReturnsExplicitError_RendersErrorMessage()
    {
        // WHAT: Tests sign-in failure with explicit server/client error message.
        // WHY:  Ensures clear user feedback when asymmetric signature verification fails.
        // HOW:  Configures weaverSignIn to return Ok = false with error string and clicks submit.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();
        JSInterop.Setup<Login.WeaverSignInResult>("weaverSignIn")
            .SetResult(new Login.WeaverSignInResult(false, "Cryptographic signature verification failed.", null, null));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var submitBtn = cut.Find("#loginSubmitBtn");
        await submitBtn.ClickAsync(new MouseEventArgs());

        var errorAlert = cut.Find(".alert-danger");
        Assert.Equal("Cryptographic signature verification failed.", errorAlert.TextContent.Trim());

        UserSessionState session = Services.GetRequiredService<UserSessionState>();
        Assert.False(session.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that failed authentication returning a null error message renders the default error text.
    /// </summary>
    [Fact]
    public async Task Authenticate_WhenJsReturnsNullError_RendersDefaultErrorMessage()
    {
        // WHAT: Tests fallback error handling when result.Error is null.
        // WHY:  Guarantees an alert is always shown even if the JS runtime returns an empty error payload.
        // HOW:  Configures weaverSignIn to return Ok = false with null error and asserts "Authentication failed.".
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();
        JSInterop.Setup<Login.WeaverSignInResult>("weaverSignIn")
            .SetResult(new Login.WeaverSignInResult(false, null, null, null));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var submitBtn = cut.Find("#loginSubmitBtn");
        await submitBtn.ClickAsync(new MouseEventArgs());

        var errorAlert = cut.Find(".alert-danger");
        Assert.Equal("Authentication failed.", errorAlert.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that when JSInterop returns Ok = true but a null user, authentication treats it as a failure.
    /// </summary>
    [Fact]
    public async Task Authenticate_WhenJsReturnsTrueWithNullUser_RendersDefaultErrorMessage()
    {
        // WHAT: Tests defensive check when result.Ok is true but User is null.
        // WHY:  A session cannot be established without a valid UserAccount record.
        // HOW:  Configures weaverSignIn with Ok = true, User = null and asserts fallback error message.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();
        JSInterop.Setup<Login.WeaverSignInResult>("weaverSignIn")
            .SetResult(new Login.WeaverSignInResult(true, null, "tok", null));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var submitBtn = cut.Find("#loginSubmitBtn");
        await submitBtn.ClickAsync(new MouseEventArgs());

        var errorAlert = cut.Find(".alert-danger");
        Assert.Equal("Authentication failed.", errorAlert.TextContent.Trim());
    }

    /// <summary>
    /// Verifies that when JSInterop throws an exception during authentication, an error message is rendered.
    /// </summary>
    [Fact]
    public async Task Authenticate_WhenJsThrows_RendersExceptionMessage()
    {
        // WHAT: Tests exception handling when the JS interop call fails.
        // WHY:  Ensures that circuit aborts or script syntax errors do not crash the Blazor component.
        // HOW:  Configures weaverSignIn to throw a JSException and verifies alert message.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();
        JSInterop.Setup<Login.WeaverSignInResult>("weaverSignIn")
            .SetException(new JSException("Circuit disconnected."));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var submitBtn = cut.Find("#loginSubmitBtn");
        await submitBtn.ClickAsync(new MouseEventArgs());

        var errorAlert = cut.Find(".alert-danger");
        Assert.Contains("Authentication failed: Circuit disconnected.", errorAlert.TextContent);
    }

    /// <summary>
    /// Verifies that successful authentication populates UserSessionState and navigates to the root URL.
    /// </summary>
    [Fact]
    public async Task Authenticate_WhenJsSucceeds_UpdatesSessionAndNavigates()
    {
        // WHAT: Tests successful user sign-in workflow.
        // WHY:  Establishes session identity in UserSessionState and redirects to sovereign dashboard.
        // HOW:  Configures weaverSignIn to return Ok = true with test user and asserts redirection.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem").SetVoidResult();
        JSInterop.Setup<Login.WeaverSignInResult>("weaverSignIn")
            .SetResult(new Login.WeaverSignInResult(true, null, "valid-token", _testUser));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var submitBtn = cut.Find("#loginSubmitBtn");
        await submitBtn.ClickAsync(new MouseEventArgs());

        UserSessionState session = Services.GetRequiredService<UserSessionState>();
        Assert.True(session.IsAuthenticated);
        Assert.Equal(_testUser.Username, session.CurrentUser?.Username);

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    /// <summary>
    /// Verifies that when a user is already authenticated, the component renders the authenticated banner
    /// and clicking Sign Out clears the session and navigates to /login.
    /// </summary>
    [Fact]
    public async Task AlreadyAuthenticated_RendersUserInfo_AndSignOutClearsSession()
    {
        // WHAT: Tests already-authenticated UI and user-triggered sign-out.
        // WHY:  Signed-in users visiting /login must see their status and be able to sign out.
        // HOW:  Pre-populates UserSessionState, renders Login, clicks Sign Out, and verifies weaverClearSession.
        UserSessionState session = Services.GetRequiredService<UserSessionState>();
        session.SetUser(_testUser);

        JSInterop.SetupVoid("weaverClearSession").SetVoidResult();

        IRenderedComponent<Login> cut = RenderComponent<Login>();

        var infoAlert = cut.Find(".alert-info");
        Assert.Contains("Currently signed in as", infoAlert.TextContent);
        Assert.Contains(_testUser.Username, infoAlert.TextContent);

        var signOutBtn = cut.Find("button.btn-outline");
        await signOutBtn.ClickAsync(new MouseEventArgs());

        JSInterop.VerifyInvoke("weaverClearSession");
        Assert.False(session.IsAuthenticated);

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that when weaverClearSession throws an exception during sign-out, the component handles it
    /// gracefully and still clears UserSessionState and navigates to /login.
    /// </summary>
    [Fact]
    public async Task AlreadyAuthenticated_SignOut_WhenJsThrows_StillSignsOutAndNavigates()
    {
        // WHAT: Tests sign-out resilience when browser interop throws.
        // WHY:  Server-side session teardown must succeed even if client-side storage cleanup encounters an error.
        // HOW:  Configures weaverClearSession to throw a JSException, clicks Sign Out, and asserts sign-out.
        UserSessionState session = Services.GetRequiredService<UserSessionState>();
        session.SetUser(_testUser);

        JSInterop.SetupVoid("weaverClearSession")
            .SetException(new JSException("Browser storage quota error."));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        var signOutBtn = cut.Find("button.btn-outline");
        await signOutBtn.ClickAsync(new MouseEventArgs());

        Assert.False(session.IsAuthenticated);
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/login", nav.Uri);
    }

    /// <summary>
    /// Verifies that when weaverBindKeyFileInput throws an exception during initial render,
    /// the component catches the exception without failing.
    /// </summary>
    [Fact]
    public void OnAfterRenderAsync_WhenBindKeyFileThrows_HandlesGracefully()
    {
        // WHAT: Tests exception handling in OnAfterRenderAsync for key file binding.
        // WHY:  Prerendering or headless test environments must not crash on JS interop failure.
        // HOW:  Configures weaverBindKeyFileInput to throw and renders component.
        JSInterop.SetupVoid("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem")
            .SetException(new JSException("DOM element not attached."));

        IRenderedComponent<Login> cut = RenderComponent<Login>();
        Assert.NotNull(cut);
    }
}
