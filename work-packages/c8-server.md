# Work Package P14 (`c8-server`): C8 Server-Side Web Token Endpoint & Challenge Store Hardening (L9)

- **Phase ID**: `P14` (implementing the server half of `P13` Architect design)
- **Recommended Model Tier**: `flash` / Haiku / Sonnet (Single-stage server C# work package; no JavaScript in this package)
- **Parent Document**: [`work-packages/PHASES-v2.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/PHASES-v2.md) & [`work-packages/c8-challenge-response-proposal.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/c8-challenge-response-proposal.md)
- **Prerequisites**: `P10` (`work-packages/p10-auth-and-dna-hardening.md`)
- **Blocks**: `P15` (`work-packages/c8-client.md`) and `P16` (`work-packages/c8-remove-oracle.md`)

---

## 1. Recorded User Decisions & P13 Spike Verdict (Authoritative)

### 1.1 Recorded User Decisions
1. **Challenge Payload Format**: Keep signing the **raw 32-byte challenge nonce** (`Convert.FromBase64String(challenge.NonceBase64)`) on both `POST /api/auth/token` and `POST /api/auth/web-token`. Do not add domain-separation prefixes.
2. **Transport Boundary Decided by Endpoint (P2 Invariant)**:
   - `POST /api/auth/token` passes `isWebRequest: false` and never sets a browser cookie.
   - `POST /api/auth/web-token` passes `isWebRequest: true`, sets the `weaver_session` cookie (`HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddHours(8)`), and returns `AuthTokenResponse` (so the Blazor circuit and `weaver-session.js` can register session activity heartbeats).
   - Never accept a client-supplied `transport` field.
3. **Challenge Lifetime**: Increase `ChallengeLifetime` in [`UserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs#L63) from `60` seconds to **`120` seconds** (`TimeSpan.FromSeconds(120)`).
4. **L9 Challenge Store Hardening**:
   - Cap `_activeChallenges` at **`10,000` entries** (`DefaultMaxActiveChallenges = 10_000`, exposed via `internal int MaxActiveChallenges { get; set; } = DefaultMaxActiveChallenges;` for fast deterministic testing).
   - Expose `int PruneExpiredChallenges(DateTimeOffset nowUtc)` on `IUserAuthenticationService` / `UserAuthenticationService` and invoke it on a periodic timer (`System.Threading.Timer`, 30-second default interval) as well as inside `CreateChallenge`.
   - When `_activeChallenges.Count >= MaxActiveChallenges` after pruning expired entries, throw `ChallengeStoreCapacityExceededException` and map it in `POST /api/auth/challenge` to `HTTP 429 TooManyRequests` via `SecureProblemResult`.
5. **Keep `/api/auth/login-key` Intact During P14**: Do **not** delete `/api/auth/login-key` or `AuthenticateWithPrivateKeyAsync` in P14. Removal happens exclusively in `P16` (`work-packages/c8-remove-oracle.md`) after `P15` (`c8-client.md`) is verified.

### 1.2 P13 BouncyCastle 2.7 PKCS#8 DER Spike Record (Reference for P14–P16)
Executed against `BouncyCastle.Cryptography` `2.7.0` (`TargetFramework: net10.0`) with deterministic 32-byte seed `0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20`:

1. **Ed25519 (`OID 1.3.101.112` = `06 03 2B 65 70`)**:
   - `PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded()` emits **83 bytes** (PKCS#8 v1 with public key attribute):
     - `[0..16]`: `30 51 02 01 01 30 05 06 03 2B 65 70 04 22 04 20`
     - `[16..48]`: **32-byte Ed25519 private seed**
     - `[48..83]`: `81 21 00` + 32-byte public key
   - Standard RFC 8410 v0 PKCS#8 without public key is **48 bytes**:
     - `[0..16]`: `30 2E 02 01 00 30 05 06 03 2B 65 70 04 22 04 20`
     - `[16..48]`: **32-byte Ed25519 private seed**
2. **ML-DSA-65 (`OID 2.16.840.1.101.3.4.3.18` = `06 09 60 86 48 01 65 03 04 03 12`)**:
   - **`SeedAndEncoding` (`4098` bytes — default emitted by `MLDsaKeyPairGenerator.GenerateKeyPair()` in `GenerateSecureKeyBundle`)**:
     - `[0..28]`: `30 82 0F FE 02 01 00 30 0B 06 09 60 86 48 01 65 03 04 03 12 04 82 0F EA 30 82 0F E6 04 20`
     - `[28..60]`: **32-byte ML-DSA-65 seed ($\xi$)**
     - `[60..64]`: `04 82 0F C0`
     - `[64..4096]`: **4032-byte expanded FIPS 204 secret key ($sk$)**
     - `[4096..4098]`: trailing context/attributes if any (total sequence length = `0x0FFE` + 4 = `4098` bytes; specifically `28 + 32 + 4 + 4034` where the inner OCTET STRING is `04 82 0F C0` (4032 bytes) at `[62..4098]`: `[0..26]` header + `04 20` at `[26..28]`, seed at `[28..60]`, `04 82 0F C0` at `[60..64]`, 4032-byte expanded key at `[64..4096]`).
   - **`SeedOnly` (`54` bytes — emitted by `MLDsaPrivateKeyParameters.FromSeed`)**:
     - `[0..22]`: `30 34 02 01 00 30 0B 06 09 60 86 48 01 65 03 04 03 12 04 22 80 20`
     - `[22..54]`: **32-byte ML-DSA-65 seed ($\xi$)**
   - **`EncodingOnly` (`4060` bytes — emitted by `MLDsaPrivateKeyParameters.FromEncoding`)**:
     - `[0..28]`: `30 82 0F D8 02 01 00 30 0B 06 09 60 86 48 01 65 03 04 03 12 04 82 0F C4 04 82 0F C0`
     - `[28..4060]`: **4032-byte expanded FIPS 204 secret key ($sk$)**
   - **Verdict**: Both Ed25519 and ML-DSA-65 PKCS#8 keys produced by BouncyCastle 2.7 have deterministic fixed-offset layouts compatible with `@noble/ed25519` and `@noble/post-quantum` (`ml_dsa65`). Therefore, **both Ed25519 and ML-DSA-65 remain supported for `AccessGate.Web` and `AccessGate.ApiWeb`**.

---

## 2. Files in Scope

| File | Action | Est. Lines Changed |
| :--- | :--- | :--- |
| [`src/NanoWeaverHrt/Security/Authentication/ChallengeStoreCapacityExceededException.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/ChallengeStoreCapacityExceededException.cs) | Create exception type for challenge cap overflow | +30 |
| [`src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs) | Add `int PruneExpiredChallenges(DateTimeOffset nowUtc)` | +10 |
| [`src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs) | Set `ChallengeLifetime = 120s`, cap at `10,000`, add `PruneExpiredChallenges` + `Timer`, implement `IDisposable` | +75 |
| [`src/NanoWeaverHrt/Program.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Program.cs) | Allow-list `/api/auth/web-token`, handle `429` on `/api/auth/challenge`, add `POST /api/auth/web-token` with same-origin validation | +95 |
| [`tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs) | Add unit tests for 120s TTL, `PruneExpiredChallenges`, timer tick, and `MaxActiveChallenges` cap | +70 |
| [`tests/NanoWeaverHrt.Tests/AuthEndpointsIntegrationTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/AuthEndpointsIntegrationTests.cs) | Add integration tests for `POST /api/auth/web-token` (Web/ApiWeb/Api gates, cookies, same-origin checks) and `POST /api/auth/challenge` 429 cap | +110 |

---

## 3. Step-by-Step Implementation Instructions

### Step 1: Create `ChallengeStoreCapacityExceededException.cs`
Create `src/NanoWeaverHrt/Security/Authentication/ChallengeStoreCapacityExceededException.cs`:

```csharp
namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Exception thrown when the in-memory authentication challenge store reaches its maximum capacity
/// of pending unconsumed challenges.
/// </summary>
public sealed class ChallengeStoreCapacityExceededException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChallengeStoreCapacityExceededException"/> class.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of active challenges permitted.</param>
    public ChallengeStoreCapacityExceededException(int maxCapacity)
        : base($"The authentication challenge store has reached its maximum capacity of {maxCapacity} pending challenges.")
    {
        MaxCapacity = maxCapacity;
    }

    /// <summary>
    /// Gets the configured maximum capacity that was exceeded.
    /// </summary>
    public int MaxCapacity { get; }
}
```

### Step 2: Update `IUserAuthenticationService.cs`
In [`src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs), add `PruneExpiredChallenges`:

```csharp
    /// <summary>
    /// Removes all expired authentication challenges from the active challenge cache.
    /// </summary>
    /// <param name="nowUtc">The reference UTC timestamp against which challenge expiration is evaluated.</param>
    /// <returns>The number of expired challenges removed.</returns>
    int PruneExpiredChallenges(DateTimeOffset nowUtc);
```

### Step 3: Update `UserAuthenticationService.cs` (120s TTL, Cap, Timer & `PruneExpiredChallenges`)
In [`src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs):

1. Make `UserAuthenticationService` implement `IDisposable`:
   ```csharp
   public sealed class UserAuthenticationService : IUserAuthenticationService, IDisposable
   ```
2. Update `ChallengeLifetime` to `120` seconds and add `DefaultMaxActiveChallenges` + `_pruneTimer`:
   ```csharp
   /// <summary>
   /// Default maximum number of concurrent unconsumed authentication challenges permitted in memory.
   /// </summary>
   public const int DefaultMaxActiveChallenges = 10_000;

   /// <summary>
   /// Lifetime duration for authentication challenge nonces before expiration (120 seconds).
   /// </summary>
   public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(120);

   /// <summary>
   /// Default interval between background sweeps of expired authentication challenges.
   /// </summary>
   private static readonly TimeSpan DefaultPruneInterval = TimeSpan.FromSeconds(30);

   /// <summary>
   /// Periodic timer that sweeps expired authentication challenges even when no new challenges arrive.
   /// </summary>
   private readonly Timer _pruneTimer;

   /// <summary>
   /// Gets or sets the maximum number of active authentication challenges allowed before new requests are throttled.
   /// </summary>
   /// <remarks>
   /// Exposed as <c>internal</c> so unit and integration tests can exercise the capacity limit without allocating
   /// 10,000 challenge records.
   /// </remarks>
   internal int MaxActiveChallenges { get; set; } = DefaultMaxActiveChallenges;
   ```
3. In the constructor `UserAuthenticationService(...)`, initialize `_pruneTimer`:
   ```csharp
   _pruneTimer = new Timer(OnPruneTimerTick, null, DefaultPruneInterval, DefaultPruneInterval);
   ```
4. Add `OnPruneTimerTick`, `PruneExpiredChallenges`, and `Dispose`:
   ```csharp
   /// <summary>
   /// Timer callback that sweeps expired authentication challenges using the current UTC clock.
   /// </summary>
   /// <param name="state">Unused timer state object.</param>
   internal void OnPruneTimerTick(object? state)
   {
       PruneExpiredChallenges(DateTimeOffset.UtcNow);
   }

   /// <inheritdoc/>
   public int PruneExpiredChallenges(DateTimeOffset nowUtc)
   {
       // WHAT: Removes every challenge from _activeChallenges whose ExpiresAtUtc is at or before nowUtc.
       // WHY:  Prevents unbounded memory growth from abandoned authentication challenges (Audit L9).
       // HOW:  Iterates the lock-free ConcurrentDictionary enumerator and atomically removes expired keys.
       int removed = 0;
       foreach (KeyValuePair<string, AuthChallenge> kvp in _activeChallenges)
       {
           if (kvp.Value.ExpiresAtUtc <= nowUtc && _activeChallenges.TryRemove(kvp.Key, out _))
           {
               removed++;
           }
       }

       return removed;
   }

   /// <summary>
   /// Disposes the background challenge pruning timer.
   /// </summary>
   public void Dispose()
   {
       _pruneTimer.Dispose();
   }
   ```
5. In `CreateChallenge(string username)` (around line 220):
   ```csharp
   /// <inheritdoc/>
   public AuthChallenge CreateChallenge(string username)
   {
       ArgumentException.ThrowIfNullOrWhiteSpace(username);

       // WHAT: Sweeps expired challenges and enforces the hard upper bound on active challenges before allocating a nonce.
       // WHY:  /api/auth/challenge is unauthenticated; capping _activeChallenges stops an attacker from exhausting heap memory.
       // HOW:  Prunes expired entries first, then throws ChallengeStoreCapacityExceededException if still at or above MaxActiveChallenges.
       DateTimeOffset now = DateTimeOffset.UtcNow;
       PruneExpiredChallenges(now);

       if (_activeChallenges.Count >= MaxActiveChallenges)
       {
           throw new ChallengeStoreCapacityExceededException(MaxActiveChallenges);
       }

       byte[] nonce = new byte[32];
       RandomNumberGenerator.Fill(nonce);

       string challengeId = Guid.NewGuid().ToString("N");
       AuthChallenge challenge = new()
       {
           ChallengeId = challengeId,
           Username = NormalizeUsername(username),
           NonceBase64 = Convert.ToBase64String(nonce),
           ExpiresAtUtc = now.Add(ChallengeLifetime)
       };

       _activeChallenges[challengeId] = challenge;
       return challenge;
   }
   ```
   *(Note: If P10 has already landed, `NormalizeUsername(username)` is already present in `UserAuthenticationService`; otherwise use `username.Trim().ToLowerInvariant()`.)*

### Step 4: Update `Program.cs` (Allow-List, `/api/auth/challenge` 429, and `POST /api/auth/web-token`)

1. **Unauthenticated Allow-List (`Program.cs:422-425`)**:
   Add `/api/auth/web-token` alongside `/api/auth/challenge`, `/api/auth/token`, and `/api/auth/login-key`:
   ```csharp
   // 4. Public authentication endpoints (do not require an existing token)
   if (path.Equals("/api/auth/challenge", StringComparison.OrdinalIgnoreCase) ||
       path.Equals("/api/auth/token", StringComparison.OrdinalIgnoreCase) ||
       path.Equals("/api/auth/web-token", StringComparison.OrdinalIgnoreCase) ||
       path.Equals("/api/auth/login-key", StringComparison.OrdinalIgnoreCase))
   {
       await next();
       return;
   }
   ```

2. **Handle `ChallengeStoreCapacityExceededException` in `POST /api/auth/challenge` (`Program.cs:1743-1762`)**:
   ```csharp
   app.MapPost("/api/auth/challenge", (ChallengeRequest request, IUserAuthenticationService auth, HttpContext context) =>
   {
       // WHAT: Mints a time-bounded cryptographic challenge nonce.
       // WHY:  Initiates zero-knowledge asymmetric challenge-response authentication.
       // HOW:  Generates 32 random bytes and stores in active challenges with a 120-second TTL, returning 429 if the cap is reached.
       if (string.IsNullOrWhiteSpace(request.Username))
       {
           return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Username is required.");
       }

       try
       {
           AuthChallenge challenge = auth.CreateChallenge(request.Username);
           return Results.Ok(challenge);
       }
       catch (ChallengeStoreCapacityExceededException)
       {
           return SecureProblemResult(
               context,
               StatusCodes.Status429TooManyRequests,
               "Too many pending authentication challenges. Please retry shortly.");
       }
   })
   .WithName("CreateAuthChallenge")
   .WithTags("Authentication & Setup")
   .WithSummary("Issues a cryptographic challenge nonce for private-key authentication.")
   .WithDescription("Mints a time-bounded (120s) 256-bit challenge nonce that must be signed by the user's private key.")
   .Accepts<ChallengeRequest>("application/json", "application/xml", "application/x-yaml")
   .Produces<AuthChallenge>(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status400BadRequest)
   .Produces(StatusCodes.Status429TooManyRequests);
   ```

3. **Add `POST /api/auth/web-token` Immediately After `POST /api/auth/token` (`Program.cs:1805`)**:
   Add a helper method in `Program` (near `SecureProblemResult`):
   ```csharp
   /// <summary>
   /// Validates that a browser session token exchange request did not originate from an untrusted cross-origin site.
   /// </summary>
   /// <param name="context">The active HTTP request context.</param>
   /// <param name="configRepo">The system configuration repository holding the configured public endpoint.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns><c>true</c> if the request origin is permitted; otherwise, <c>false</c>.</returns>
   private static async Task<bool> IsAllowedWebTokenOriginAsync(
       HttpContext context,
       IConfigurationRepository configRepo,
       CancellationToken ct)
   {
       // WHAT: Checks Sec-Fetch-Site and Origin headers against the request host and configured public origin.
       // WHY:  /api/auth/web-token sets a browser session cookie; rejecting cross-origin requests blocks cross-site
       //       session fixation while still permitting reverse-proxy overlays (Caddy/Nginx) where Origin matches the public domain.
       // HOW:  Rejects Sec-Fetch-Site values other than 'same-origin'/'none', and when Origin is present, verifies its host
       //       matches HttpContext.Request.Host, Peer:CollaborativeWorkerEndpoint, or the DOMAIN environment variable.
       string? secFetchSite = context.Request.Headers["Sec-Fetch-Site"];
       if (!string.IsNullOrWhiteSpace(secFetchSite) &&
           !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
       {
           return false;
       }

       string? originHeader = context.Request.Headers.Origin;
       if (string.IsNullOrWhiteSpace(originHeader))
       {
           return true;
       }

       if (!Uri.TryCreate(originHeader, UriKind.Absolute, out Uri? originUri))
       {
           return false;
       }

       if (string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
       {
           return true;
       }

       string configuredEndpoint = await configRepo.GetValueAsync("Peer:CollaborativeWorkerEndpoint", "http://localhost:5000", ct);
       if (Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out Uri? configuredUri) &&
           string.Equals(originUri.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase))
       {
           return true;
       }

       string? envDomain = Environment.GetEnvironmentVariable("DOMAIN");
       if (!string.IsNullOrWhiteSpace(envDomain) &&
           string.Equals(originUri.Host, envDomain.Trim(), StringComparison.OrdinalIgnoreCase))
       {
           return true;
       }

       return false;
   }
   ```
   And map `POST /api/auth/web-token`:
   ```csharp
   // ---------------------------------------------------------------------
   // REST API: Browser Challenge-Response Session Exchange (Web UI transport)
   // ---------------------------------------------------------------------
   app.MapPost("/api/auth/web-token", async (
       TokenExchangeRequest request,
       IUserAuthenticationService auth,
       IConfigurationRepository configRepo,
       HttpContext context,
       CancellationToken ct) =>
   {
       // WHAT: Verifies a browser-signed challenge nonce and establishes an authenticated Web UI session.
       // WHY:  Completes challenge-response login for the browser UI without ever transmitting the user's private key to the server (Audit C8).
       // HOW:  Enforces same-origin rules, validates the signature with isWebRequest: true, sets the HttpOnly weaver_session cookie, and returns AuthTokenResponse.
       if (!await IsAllowedWebTokenOriginAsync(context, configRepo, ct))
       {
           return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Cross-origin browser session requests are forbidden.");
       }

       if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.SignatureBase64))
       {
           return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Username, challengeId, and signatureBase64 are all required.");
       }

       var (success, errorMessage, token, user) = await auth.VerifyChallengeAndIssueTokenAsync(
           request.Username,
           request.ChallengeId,
           request.SignatureBase64,
           isWebRequest: true,
           ct);

       if (!success || token == null || user == null)
       {
           return SecureProblemResult(context, StatusCodes.Status401Unauthorized, errorMessage ?? "Authentication failed.");
       }

       context.Response.Cookies.Append("weaver_session", token, new CookieOptions
       {
           HttpOnly = true,
           SameSite = SameSiteMode.Strict,
           Secure = context.Request.IsHttps,
           Expires = DateTimeOffset.UtcNow.AddHours(8)
       });

       return Results.Ok(new AuthTokenResponse
       {
           Token = token,
           TokenType = "Bearer",
           ExpiresInSeconds = 28800,
           User = user
       });
   })
   .WithName("ExchangeWebAuthToken")
   .WithTags("Authentication & Setup")
   .WithSummary("Exchanges a browser-signed challenge nonce for an interactive Web UI session cookie and token.")
   .WithDescription("Web-transport endpoint: refuses Api-only accounts, verifies the challenge signature against the registered public key, and sets the HttpOnly weaver_session cookie.")
   .Accepts<TokenExchangeRequest>("application/json", "application/xml", "application/x-yaml")
   .Produces<AuthTokenResponse>(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status400BadRequest)
   .Produces(StatusCodes.Status401Unauthorized)
   .Produces(StatusCodes.Status403Forbidden);
   ```

---

## 4. Required Unit & Integration Tests (100% Line & Branch Coverage)

### 4.1 In `tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs`
1. **`CreateChallenge_Sets120SecondExpiration_AndPrunesExpiredEntries`**:
   - Assert `(challenge.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds` is between `115` and `121`.
   - Register an already-expired challenge via `RegisterChallengeForTesting`, call `PruneExpiredChallenges(DateTimeOffset.UtcNow)` and `OnPruneTimerTick(null)`, and assert `1` (then `0`) removed.
2. **`CreateChallenge_WhenMaxActiveChallengesReached_ThrowsCapacityException`**:
   - Set `authService.MaxActiveChallenges = 2`, create 2 challenges, and assert the 3rd throws `ChallengeStoreCapacityExceededException` with `MaxCapacity == 2`.
   - Register 1 expired challenge when at capacity, call `CreateChallenge`, and assert it succeeds because `CreateChallenge` prunes expired challenges before checking the cap.

### 4.2 In `tests/NanoWeaverHrt.Tests/AuthEndpointsIntegrationTests.cs`
1. **`WebToken_AllowsWebAndApiWebAccounts_AndSetsHttpOnlyCookie`**:
   - Create an `AccessGate.Web` account (both `Ed25519` and `MlDsa65`), obtain a challenge via `POST /api/auth/challenge`, sign the raw 32-byte nonce, `POST /api/auth/web-token`, and assert `200 OK`, `Set-Cookie` contains `weaver_session=`, `httponly`, `samesite=strict`, and the audit ledger records `UserAuthenticated` with `"isWeb":true`.
2. **`WebToken_RejectsApiOnlyAccount_With401AndNoCookie`**:
   - Create an `AccessGate.Api` account, sign a challenge, `POST /api/auth/web-token`, and assert `401 Unauthorized` and no `Set-Cookie` header.
3. **`WebToken_ValidatesSameOriginAndConfiguredPublicOrigin`**:
   - Test with no `Origin` header -> `200 OK`.
   - Test with `Origin: http://localhost` (matches `Request.Host`) -> `200 OK`.
   - Test with `Origin: https://hrt.lantea.one` when `Peer:CollaborativeWorkerEndpoint` is set to `https://hrt.lantea.one` -> `200 OK`.
   - Test with `Origin: https://proxy.lantea.one` when `DOMAIN=proxy.lantea.one` -> `200 OK`.
   - Test with `Origin: https://evil.example.com` -> `403 Forbidden`.
   - Test with malformed `Origin: ://bad-uri` -> `403 Forbidden`.
   - Test with `Sec-Fetch-Site: cross-site` -> `403 Forbidden`.
   - Test with missing `username` / `challengeId` / `signatureBase64` -> `400 BadRequest`.
4. **`CreateChallengeEndpoint_WhenCapReached_Returns429TooManyRequests`**:
   - Resolve `IUserAuthenticationService` as `UserAuthenticationService`, set `MaxActiveChallenges = 1` in a `try/finally` block (restoring `DefaultMaxActiveChallenges`), issue 1 challenge (`200 OK`), issue a 2nd challenge, and assert `429 TooManyRequests` `SecureProblemDetails`.

---

## 5. Verification Gate Checklist

Run the full verification suite:
```bash
./scripts/verify-all.sh
```
Confirm all 7 checks pass with 0 formatting issues, 100.00% line and branch coverage in step 4, and zero regressions in step 7.
