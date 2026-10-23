using Microsoft.AspNetCore.Http;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Decorates an <see cref="IUserAuthenticationService"/> with authoritative server-side idle expiration of
/// bearer tokens, as mandated by Prime Directive 12.
/// </summary>
/// <remarks>
/// The inner service only enforces the absolute 8-hour token lifetime, which left the 15-minute idle sign-out
/// a purely cosmetic, client-side affair: the browser redirected to the sign-in form while the token it had
/// just stopped using stayed valid for hours. This decorator closes that gap by consulting
/// <see cref="IServerSessionActivityTracker"/> on every validation, so a token presented after its idle window
/// is refused no matter what the browser does, says, or stores.
/// </remarks>
public sealed class SessionIdleEnforcingAuthenticationService : IUserAuthenticationService
{
    /// <summary>
    /// The decorated authentication service performing the cryptographic work.
    /// </summary>
    private readonly IUserAuthenticationService _inner;

    /// <summary>
    /// Authoritative registry of per-session last-activity timestamps.
    /// </summary>
    private readonly IServerSessionActivityTracker _activityTracker;

    /// <summary>
    /// Accessor used to tell an inbound REST API call apart from an interactive circuit's token check.
    /// </summary>
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionIdleEnforcingAuthenticationService"/> class.
    /// </summary>
    /// <param name="inner">The authentication service to decorate.</param>
    /// <param name="activityTracker">The authoritative server-side session activity registry.</param>
    /// <param name="httpContextAccessor">
    /// Optional accessor for the ambient HTTP request; when absent, every validation is treated as a
    /// non-extending token presentation.
    /// </param>
    public SessionIdleEnforcingAuthenticationService(
        IUserAuthenticationService inner,
        IServerSessionActivityTracker activityTracker,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public async Task<(bool Valid, UserAccount? User, string? ErrorReason)> ValidateTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        // WHAT: Cryptographic validation first, idle validation second.
        // WHY:  The idle registry is keyed by token, so there is no point tracking a session for a
        //       token whose signature or absolute expiry already failed - and an unauthenticated
        //       caller must never be able to seed registry entries by spraying forged tokens.
        // HOW:  Delegates to the inner service, then consults the activity tracker for anything it
        //       accepted, and finally records activity when the caller is driving the REST API.
        (bool Valid, UserAccount? User, string? ErrorReason) result = await _inner.ValidateTokenAsync(token, ct);

        if (!result.Valid || result.User == null)
        {
            return result;
        }

        if (!_activityTracker.IsSessionActive(token))
        {
            return (false, null, $"Session expired after {_activityTracker.IdleTimeout.TotalMinutes:F0} minutes of inactivity. Please sign in again.");
        }

        if (IsApiRequestContext())
        {
            _activityTracker.RecordActivity(token);
        }

        return result;
    }

    /// <summary>
    /// Determines whether the ambient request is an inbound call to the REST API surface.
    /// </summary>
    /// <returns><c>true</c> for an <c>/api</c> request; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// A REST call is genuine activity by the token holder, so it extends the idle window and API clients
    /// keep working for the full token lifetime. Everything else - above all the Blazor circuit restoring a
    /// session on first render - is only a token presentation and deliberately extends nothing, which is what
    /// stops a page reload from resetting the idle clock.
    /// </remarks>
    private bool IsApiRequestContext() =>
        _httpContextAccessor?.HttpContext is HttpContext httpContext &&
        httpContext.Request.Path.StartsWithSegments("/api");

    /// <inheritdoc/>
    public Task<bool> IsSetupRequiredAsync(CancellationToken ct = default) =>
        _inner.IsSetupRequiredAsync(ct);

    /// <inheritdoc/>
    public Task<UserAccount> CompleteInitialSetupAsync(
        string adminUsername,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        AccessGate accessGate = AccessGate.ApiWeb,
        CancellationToken ct = default) =>
        _inner.CompleteInitialSetupAsync(adminUsername, publicKeyBase64, algorithm, accessGate, ct);

    /// <inheritdoc/>
    public GeneratedKeyBundle GenerateSecureKeyBundle(
        string username,
        UserRole role,
        AccessGate gate,
        KeyAlgorithm algorithm,
        string? password = null) =>
        _inner.GenerateSecureKeyBundle(username, role, gate, algorithm, password);

    /// <inheritdoc/>
    public AuthChallenge CreateChallenge(string username) =>
        _inner.CreateChallenge(username);

    /// <inheritdoc/>
    public Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> VerifyChallengeAndIssueTokenAsync(
        string username,
        string challengeId,
        string signatureBase64,
        bool isWebRequest = false,
        CancellationToken ct = default) =>
        _inner.VerifyChallengeAndIssueTokenAsync(username, challengeId, signatureBase64, isWebRequest, ct);

    /// <inheritdoc/>
    public Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> AuthenticateWithPrivateKeyAsync(
        string username,
        string privateKeyPem,
        string? password = null,
        bool isWebRequest = true,
        CancellationToken ct = default) =>
        _inner.AuthenticateWithPrivateKeyAsync(username, privateKeyPem, password, isWebRequest, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default) =>
        _inner.ListUsersAsync(ct);

    /// <inheritdoc/>
    public Task<UserAccount> CreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default) =>
        _inner.CreateUserAsync(username, role, gate, publicKeyBase64, algorithm, ct);

    /// <inheritdoc/>
    public Task<UserAccount?> TryCreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default) =>
        _inner.TryCreateUserAsync(username, role, gate, publicKeyBase64, algorithm, ct);

    /// <inheritdoc/>
    public string GenerateBearerToken(UserAccount user) =>
        _inner.GenerateBearerToken(user);

    /// <inheritdoc/>
    public Task<bool> DeleteUserAsync(string username, CancellationToken ct = default) =>
        _inner.DeleteUserAsync(username, ct);
}
