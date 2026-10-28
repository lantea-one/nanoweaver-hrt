# Work package P10 — Auth hot-path zero-alloc, username lowercase normalization, and `/api/patient/dna` scoping

> **Why this file is under `work-packages/`.** Gate step 5 scans `docs/**/*.md`, `docs/*.md` and `README.md` for paired single-`$` tokens. `work-packages/` is version-controlled and outside that scan. **Do not move this file under `docs/`.**

**Agent:** coder (suitable for lighter/smaller models: `flash`, `flash_lite`, Haiku, Sonnet).
**Dependencies:** P6, P7.4, P7.5, P8, P9 (all landed on `main`).
**Blocks:** P14 (`c8-server.md`), which also edits `UserAuthenticationService.cs` and `Program.cs`.

---

## Recorded User Decisions (2026-10-27)

1. **Username Case Rule (`A4`):** Normalize all usernames to lowercase on write and lookup (`username.Trim().ToLowerInvariant()`).
   - `"Admin"` and `"admin"` normalize to `"admin"`, cannot coexist in `weaver_users`, cannot shadow each other at login, and `DeleteUserAsync("Admin")` deletes `"admin"`.
2. **`GET /api/patient/dna` Validation & Authorization (`A5`):**
   - When `patientId` is omitted or whitespace, default to `currentUser.Username`.
   - When `patientId` is supplied, validate format: length `<= 64` and characters restricted to ASCII letters, digits, `-`, `_`, or `.`. If invalid, return **400 Bad Request** via `SecureProblemResult(context, StatusCodes.Status400BadRequest, "The requested patient identifier format is invalid.")`.
   - After normalization (`patientId.Trim().ToLowerInvariant()`), if `currentUser.Role != UserRole.Administrator` and `!string.Equals(currentUser.Username, effectivePatientId, StringComparison.OrdinalIgnoreCase)`, return **404 Not Found** via `SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail)` (matching `/api/patients/{username}/dna` to prevent subject enumeration).

---

## Sizing & Lighter-Model Execution Rules

- **Single self-contained stage (~140 lines changed across 5 files).**
- **Strict Non-Goal (`_activeChallenges`):** Do **not** touch `_activeChallenges`, `ChallengeLifetime`, or `CreateChallenge`'s pruning loop (other than normalizing `username.Trim().ToLowerInvariant()`). Challenge store capping and timer pruning belong exclusively to **P14** (`L9`).
- **Step-7 Coverage Ratchet:** After running `./scripts/verify-all.sh`, if `uncovered_lines_total` decreases or `line_rate`/`branch_rate` improves beyond tolerance, update `coverage-baseline.json` using:
  ```bash
  python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
  ```
  Never pass `--allow-regression`.

---

## Files in Scope

| Path | Role |
| :--- | :--- |
| `src/NanoWeaverHrt/Security/Authentication/UserRepository.cs` | Add `NormalizeUsername(string username)` helper (`username.Trim().ToLowerInvariant()`) and apply across `GetUserAsync`, `CreateUserAsync`, `TryCreateUserAsync`, `UpdateLastLoginAsync`, and `DeleteUserAsync`. |
| `src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs` | (1) **L4**: Replace `token.Split('.')` in `ValidateTokenAsync` with `ReadOnlySpan<char>` slicing; (2) **L8**: Expand XML `<remarks>` on `RegisterChallengeForTesting`; (3) Normalize usernames via `NormalizeUsername` across `CompleteInitialSetupAsync`, `GenerateSecureKeyBundle`, `CreateChallenge`, `VerifyChallengeAndIssueTokenAsync`, `AuthenticateWithPrivateKeyAsync`, `CreateUserAsync`, `TryCreateUserAsync`, and `DeleteUserAsync`. |
| `src/NanoWeaverHrt/Program.cs` | **L6**: Validate `patientId` format and enforce self-or-Administrator scoping on `GET /api/patient/dna` (`~2413`). |
| `tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs` | Add branch test for multi-dot malformed token (`"a.b.c"`), mixed-case username normalization/shadowing prevention across create/lookup/login/delete, and `GET /api/patient/dna` validation + self/Admin scoping tests. |
| `tests/NanoWeaverHrt.Tests/UserProvisioningEndpointTests.cs` | Assert that the compliance audit ledger (`IComplianceAuditService.GetAuditLedger()`) records **zero** `UserCreated` entries for a refused 409 duplicate username (including a mixed-case duplicate like `"PROV_DUP"` after `"prov_dup"`). |

---

## Exact Implementation Changes

### 1. `src/NanoWeaverHrt/Security/Authentication/UserRepository.cs`

Add a private static helper `NormalizeUsername`:

```csharp
    /// <summary>
    /// Normalizes an account username by trimming surrounding whitespace and converting to lowercase invariant form.
    /// </summary>
    /// <param name="username">The raw username supplied by the caller.</param>
    /// <returns>The trimmed, lowercase-normalized username string.</returns>
    private static string NormalizeUsername(string username)
    {
        // WHAT: Trims whitespace and normalizes the username to lowercase invariant characters.
        // WHY:  Prevents mixed-case duplicate accounts (e.g. "Admin" and "admin") from coexisting or shadowing each other.
        // HOW:  Calls Trim().ToLowerInvariant() on the validated non-empty string.
        return username.Trim().ToLowerInvariant();
    }
```

Use `NormalizeUsername` in:
- `GetUserAsync`: `WeaverDbParameters.Add(cmd, "@u", NormalizeUsername(username));`
- `UpdateLastLoginAsync`: `WeaverDbParameters.Add(cmd, "@u", NormalizeUsername(username));`
- `DeleteUserAsync`: `WeaverDbParameters.Add(cmd, "@u", NormalizeUsername(username));`
- `BindUser`: `WeaverDbParameters.Add(cmd, "@u", NormalizeUsername(user.Username));`

### 2. `src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`

#### 2a. **L8** — Document `RegisterChallengeForTesting` (`:98-105`)
Update the XML doc comment on `RegisterChallengeForTesting` to explicitly record why it is `internal`:

```csharp
    /// <summary>
    /// Registers an explicit authentication challenge for testing expiration workflows.
    /// </summary>
    /// <param name="challenge">The challenge instance to register.</param>
    /// <remarks>
    /// Test-only surface reachable exclusively via <c>InternalsVisibleTo("NanoWeaverHrt.Tests")</c> so unit tests
    /// can deterministically inject pre-expired challenge nonces without <c>Thread.Sleep</c> (Directive 6).
    /// Never called by production code.
    /// </remarks>
    internal void RegisterChallengeForTesting(AuthChallenge challenge)
    {
        _activeChallenges[challenge.ChallengeId] = challenge;
    }
```

#### 2b. **L4** — Replace `token.Split('.')` in `ValidateTokenAsync` (`:400-408`)
Replace:
```csharp
        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return (false, null, "Malformed bearer token format.");
        }

        string payloadBase64 = parts[0];
        string signatureBase64 = parts[1];
```
With:
```csharp
        // WHAT: Splits the bearer token around its single '.' delimiter using ReadOnlySpan<char>.
        // WHY:  ValidateTokenAsync runs on every authenticated HTTP request; string.Split('.') allocates a string[] on the hot path.
        // HOW:  Checks IndexOf('.') and LastIndexOf('.') to ensure exactly one separator exists before slicing payload and signature.
        ReadOnlySpan<char> tokenSpan = token.AsSpan();
        int dotIndex = tokenSpan.IndexOf('.');
        if (dotIndex < 0 || dotIndex != tokenSpan.LastIndexOf('.'))
        {
            return (false, null, "Malformed bearer token format.");
        }

        string payloadBase64 = tokenSpan[..dotIndex].ToString();
        string signatureBase64 = tokenSpan[(dotIndex + 1)..].ToString();
```

#### 2c. Lowercase Username Normalization (`NormalizeUsername`)
Add private static helper to `UserAuthenticationService`:
```csharp
    /// <summary>
    /// Normalizes a username by trimming whitespace and converting to lowercase invariant characters.
    /// </summary>
    /// <param name="username">The raw username string.</param>
    /// <returns>The normalized lowercase username.</returns>
    private static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
```
Apply `NormalizeUsername` in:
- `CompleteInitialSetupAsync`: `Username = NormalizeUsername(adminUsername)`
- `GenerateSecureKeyBundle`: `Username = NormalizeUsername(username)`
- `CreateChallenge`: `Username = NormalizeUsername(username)`
- `VerifyChallengeAndIssueTokenAsync`:
  ```csharp
  string normalizedUsername = NormalizeUsername(username);
  if (!string.Equals(challenge.Username, normalizedUsername, StringComparison.Ordinal))
  {
      return (false, "Challenge username mismatch.", null, null);
  }

  UserAccount? user = await _userRepository.GetUserAsync(normalizedUsername, ct);
  ```
- `AuthenticateWithPrivateKeyAsync`:
  ```csharp
  UserAccount? user = await _userRepository.GetUserAsync(NormalizeUsername(username), ct);
  ```
- `CreateUserAsync`: `Username = NormalizeUsername(username)`
- `TryCreateUserAsync`: `Username = NormalizeUsername(username)`
- `DeleteUserAsync`:
  ```csharp
  string normalizedUsername = NormalizeUsername(username);
  IReadOnlyList<UserAccount> allUsers = await _userRepository.ListUsersAsync(ct);
  UserAccount? target = allUsers.FirstOrDefault(u => string.Equals(u.Username, normalizedUsername, StringComparison.Ordinal));
  ...
  bool deleted = await _userRepository.DeleteUserAsync(target.Username, ct);
  if (deleted)
  {
      _auditService.LogAction(
          "USER_MANAGEMENT",
          "UserDeleted",
          $"user:{target.Username}",
          $"{{\"role\":\"{target.Role}\"}}");
  }
  ```

### 3. `src/NanoWeaverHrt/Program.cs` — **L6** (`GET /api/patient/dna` at `:2413-2464`)

Add a private static validation helper in `Program.cs` (near `IsAuthorizedForPatientUsername`):
```csharp
    /// <summary>
    /// Validates that a supplied patient identifier is non-empty, at most 64 characters, and contains only safe ASCII identifier characters.
    /// </summary>
    /// <param name="patientId">The candidate patient identifier to validate.</param>
    /// <returns><c>true</c> if the identifier is syntactically valid; otherwise <c>false</c>.</returns>
    private static bool IsValidPatientIdentifier(string patientId)
    {
        // WHAT: Checks length and character set of a patient identifier query parameter.
        // WHY:  Prevents log/manifest injection or path-like characters in synthesized DNA package metadata.
        // HOW:  Enforces 1..64 characters drawn from ASCII letters, digits, '-', '_', or '.'.
        if (patientId.Length == 0 || patientId.Length > 64)
        {
            return false;
        }

        foreach (char c in patientId)
        {
            bool allowed = char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
```

In `app.MapGet("/api/patient/dna", ...)`:
```csharp
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            string effectivePatientId;
            if (!string.IsNullOrWhiteSpace(patientId))
            {
                string trimmedId = patientId.Trim().ToLowerInvariant();
                if (!IsValidPatientIdentifier(trimmedId))
                {
                    return SecureProblemResult(
                        context,
                        StatusCodes.Status400BadRequest,
                        "The requested patient identifier format is invalid.");
                }

                if (currentUser == null
                    || (currentUser.Role != UserRole.Administrator
                        && !string.Equals(currentUser.Username, trimmedId, StringComparison.OrdinalIgnoreCase)))
                {
                    return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
                }

                effectivePatientId = trimmedId;
            }
            else
            {
                effectivePatientId = currentUser?.Username ?? "patient-sovereign-01";
            }
```
Also chain `.Produces(StatusCodes.Status400BadRequest)` and `.Produces(StatusCodes.Status404NotFound)` onto the `MapGet("/api/patient/dna", ...)` route builder.

---

## Acceptance Criteria

1. `grep -rn "token\.Split" src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs` returns 0 matches.
2. `TokenValidation_TamperedAndMalformedTokens_Rejected` in `AuthenticationServiceTests.cs` tests both a zero-dot token (`"this_is_not_a_valid_token"`) and a two-dot token (`"part1.part2.part3"`), covering both branches of `dotIndex < 0 || dotIndex != tokenSpan.LastIndexOf('.')`.
3. A unit test in `AuthenticationServiceTests.cs` proves that creating `"MixedCaseUser"` stores `"mixedcaseuser"`, attempting `TryCreateUserAsync("MIXEDCASEUSER", ...)` returns `null` (cannot coexist/shadow), `VerifyChallengeAndIssueTokenAsync("MixedCaseUser", ...)` succeeds, and `DeleteUserAsync("MIXEDCASEUSER")` returns `true` and removes the account.
4. `UserProvisioningEndpointTests.cs` asserts that after a 409 Conflict on `POST /api/users` (both exact-case and mixed-case duplicate), `IComplianceAuditService.GetAuditLedger()` contains only the original single `UserCreated` entry for that username.
5. Integration tests verify `GET /api/patient/dna`:
   - Omitted `patientId` defaults to `currentUser.Username` (`200 OK`).
   - Invalid `patientId` (`"bad/id!"` or `new string('a', 65)`) returns `400 Bad Request` (`application/problem+json`).
   - Non-Admin (`Patient` or `Researcher`) requesting `?patientId=someone_else` receives `404 Not Found` (`application/problem+json` with `SubjectRecordNotFoundDetail`).
   - `Administrator` requesting `?patientId=patient_007` receives `200 OK` with `PatientId == "patient_007"`.
6. `./scripts/verify-all.sh` passes all 7 steps (`100.00%` line and branch coverage in Step 4, 0 warnings, 0 docblock issues, Step 7 ratchet green).
