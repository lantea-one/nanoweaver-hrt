# Work Package P16 (`c8-remove-oracle`): Delete the Server-Side Private-Key Signing Oracle

- **Phase ID**: `P16`
- **Recommended Model Tier**: `flash` / Haiku / Sonnet — **Split into Stage A (Migrate Test Call Sites to Challenge-Response) and Stage B (Delete Oracle Production Code, `KeyLoginRequest`, `/api/auth/login-key` & Update Docs)**
- **Parent Document**: [`work-packages/PHASES-v2.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/PHASES-v2.md) & [`work-packages/c8-challenge-response-proposal.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/c8-challenge-response-proposal.md)
- **Prerequisites**: `P14` (`work-packages/c8-server.md`) AND `P15` (`work-packages/c8-client.md`) verified end-to-end.
- **Blocks**: `P17` and `P18`.

---

## 1. Recorded User Decisions (Authoritative)

1. **Two-Stage Split for Smaller Models**:
   - **Stage A**: Add a shared test helper (`TestChallengeHelper.cs`) in `tests/NanoWeaverHrt.Tests/` that signs challenges in-process using BouncyCastle (`Ed25519Signer` or `PostQuantumCryptoProvider`) and calls `/api/auth/challenge` + `/api/auth/web-token` (or `/api/auth/token`), and migrate all integration tests in `StackTelemetryRoleGatingTests.cs`, `UserProfileAndContactTests.cs`, `ApiRoleGatingAndDisclosureTests.cs`, `AuthEndpointsIntegrationTests.cs`, `SecurityPenetrationTests.cs`, and `SystemIntegrationPipelineTests.cs` off `/api/auth/login-key`.
   - **Stage B**: Delete `AuthenticateWithPrivateKeyAsync`, `SignEphemeralNonce`, `ExtractPrivateKeyDerFromPem`, `DecryptPrivateKeyBytes` (keeping `EncryptPrivateKeyBytes` for `GenerateSecureKeyBundle`), `KeyLoginRequest`, and `POST /api/auth/login-key`, and update `SessionIdleEnforcingAuthenticationService.cs`, `UserProvisioningEndpointTests.cs`, `SessionIdleEnforcementTests.cs`, `AuthenticationServiceTests.cs`, `docs/api-reference.md`, and `docs/release-notes.md`.
2. **ML-DSA-65 × `AccessGate.Web` Verdict**:
   - Because P13/P15 vendors `@noble/post-quantum` (`ml_dsa65`) and parses BouncyCastle 2.7's ML-DSA-65 PKCS#8 `PrivateKeyInfo` (`4098`, `54`, and `4060` byte forms), ML-DSA-65 accounts remain supported on `AccessGate.Web` and `AccessGate.ApiWeb` via `POST /api/auth/web-token`.
3. **Preserve P2's Four Transport-Boundary Regression Assertions**:
   - Do **not** delete P2's transport-boundary tests in `SecurityPenetrationTests.cs:246-310` and `AuthEndpointsIntegrationTests.cs:280-320`; re-point them from `/api/auth/login-key` to `/api/auth/web-token`.
4. **Honest Documentation of Key Generation**:
   - Never claim in documentation that "the private key never leaves the browser" while `GenerateSecureKeyBundle` still mints keypairs server-side during `/setup` and user provisioning. State explicitly that **authentication** is strictly zero-private-key-transmission challenge-response (`POST /api/auth/challenge` + `POST /api/auth/token` or `POST /api/auth/web-token`).

---

## 2. Complete Inventory of Oracle References to Migrate / Remove

### 2.1 Production Files (Stage B)
| File | Lines | Symbol / Endpoint | Action |
| :--- | :--- | :--- | :--- |
| [`src/NanoWeaverHrt/Components/Pages/Login.razor`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Components/Pages/Login.razor#L167) | 167 | `AuthService.AuthenticateWithPrivateKeyAsync` | Already removed in `P15` (`c8-client.md`); verify 0 hits remain |
| [`src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs#L71-L86) | 71–86 | `AuthenticateWithPrivateKeyAsync` | Delete interface method |
| [`src/NanoWeaverHrt/Security/Authentication/SessionIdleEnforcingAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/SessionIdleEnforcingAuthenticationService.cs#L133-L140) | 133–140 | `AuthenticateWithPrivateKeyAsync` | Delete decorator forwarding method |
| [`src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs#L329-L388) | 329–388, 604–633, 714–815 | `AuthenticateWithPrivateKeyAsync`, `SignEphemeralNonce`, `DecryptPrivateKeyBytes`, `ExtractPrivateKeyDerFromPem` | Delete all 4 methods (**keep** `EncryptPrivateKeyBytes` at lines 681–712!) |
| [`src/NanoWeaverHrt/Security/Authentication/AuthRequestModels.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Security/Authentication/AuthRequestModels.cs#L167-L203) | 167–203 | `KeyLoginRequest` | Delete record and its `GetExamples()` method |
| [`src/NanoWeaverHrt/Program.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Program.cs#L181) | 181, 424, 1806–1859 | Swagger description, allow-list entry, `app.MapPost("/api/auth/login-key", ...)` | Update line 181 to `/api/auth/web-token`; remove `/api/auth/login-key` from allow-list and delete endpoint |

### 2.2 Test & Documentation Files (Stage A & Stage B)
| File | Lines | Action |
| :--- | :--- | :--- |
| [`tests/NanoWeaverHrt.Tests/StackTelemetryRoleGatingTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/StackTelemetryRoleGatingTests.cs#L143-L155) | 143–155 | Stage A: Replace `/api/auth/login-key` with `TestChallengeHelper.ExchangeChallengeTokenAsync` |
| [`tests/NanoWeaverHrt.Tests/UserProfileAndContactTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/UserProfileAndContactTests.cs#L813-L840) | 813, 836 | Stage A: Replace `/api/auth/login-key` with `TestChallengeHelper.ExchangeChallengeTokenAsync` |
| [`tests/NanoWeaverHrt.Tests/ApiRoleGatingAndDisclosureTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/ApiRoleGatingAndDisclosureTests.cs#L565-L572) | 565, 569 | Stage A: Re-point validation/unknown-user checks to `/api/auth/web-token` |
| [`tests/NanoWeaverHrt.Tests/AuthEndpointsIntegrationTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/AuthEndpointsIntegrationTests.cs#L210-L320) | 210–225, 280–320 | Stage A: Re-point P2 transport-boundary and role-login tests to `/api/auth/challenge` + `/api/auth/web-token` |
| [`tests/NanoWeaverHrt.Tests/SecurityPenetrationTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/SecurityPenetrationTests.cs#L97-L306) | 97–105, 246–260, 297–305 | Stage A: Re-point P2 transport-boundary tests (`gate_web_user` / `gate_api_user`) to `/api/auth/web-token` |
| [`tests/NanoWeaverHrt.Tests/SystemIntegrationPipelineTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/SystemIntegrationPipelineTests.cs#L121-L390) | 121–130, 157–166, 356–390 | Stage A: Replace `/api/auth/login-key` calls with `TestChallengeHelper.ExchangeChallengeTokenAsync` |
| [`tests/NanoWeaverHrt.Tests/UserProvisioningEndpointTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/UserProvisioningEndpointTests.cs#L89-L90) | 89–90 | Stage B: Remove `AuthenticateWithPrivateKeyAsync` from `SpyAuthenticationService` |
| [`tests/NanoWeaverHrt.Tests/SessionIdleEnforcementTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/SessionIdleEnforcementTests.cs#L464-L482) | 464, 481 | Stage B: Remove `AuthenticateWithPrivateKeyAsync` delegation assertion |
| [`tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs#L532-L908) | 532–670, 802, 880–908 | Stage B: Replace `AuthenticateWithPrivateKeyAsync` tests with `CreateChallenge` + `VerifyChallengeAndIssueTokenAsync` (`isWebRequest: true/false`), remove `KeyLoginRequest` example assertion, and replace `DecryptPrivateKeyBytes` test with an `EncryptPrivateKeyBytes` AES-GCM round-trip verification |
| [`docs/api-reference.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/docs/api-reference.md#L1223-L1255) | 1223–1255 | Stage B: Replace `POST /api/auth/login-key` documentation section with `POST /api/auth/web-token` |

---

## 3. Step-by-Step Implementation Instructions

### Stage A: Create `TestChallengeHelper.cs` & Migrate Integration Tests

1. Create `tests/NanoWeaverHrt.Tests/TestChallengeHelper.cs`:
   - Provide static helpers:
     - `public static string SignNonceWithPem(string privateKeyPem, string nonceBase64, KeyAlgorithm algorithm = KeyAlgorithm.Ed25519, string? passphrase = null)`
       *(Strips PEM headers, decrypts if encrypted using AES-GCM + PBKDF2 matching `EncryptPrivateKeyBytes`, and signs `Convert.FromBase64String(nonceBase64)` with BouncyCastle `Ed25519Signer` or `PostQuantumCryptoProvider`.)*
     - `public static async Task<HttpResponseMessage> LoginViaChallengeAsync(HttpClient client, string username, string privateKeyPem, KeyAlgorithm algorithm = KeyAlgorithm.Ed25519, bool useWebTokenEndpoint = true, string? passphrase = null)`
       *(POSTs `/api/auth/challenge`, signs `challenge.NonceBase64`, and POSTs `TokenExchangeRequest` to `/api/auth/web-token` when `useWebTokenEndpoint == true` or `/api/auth/token` when `false`.)*
2. Update `StackTelemetryRoleGatingTests.cs`, `UserProfileAndContactTests.cs`, `ApiRoleGatingAndDisclosureTests.cs`, `AuthEndpointsIntegrationTests.cs`, `SecurityPenetrationTests.cs`, and `SystemIntegrationPipelineTests.cs` to use `TestChallengeHelper.LoginViaChallengeAsync` instead of `/api/auth/login-key`.
3. Run `XDG_RUNTIME_DIR=/tmp dotnet test NanoWeaverHrt.slnx` to confirm all migrated integration tests pass before deleting the oracle.

### Stage B: Delete the Oracle & Update Unit Tests + Documentation

1. Delete `AuthenticateWithPrivateKeyAsync` from:
   - `src/NanoWeaverHrt/Security/Authentication/IUserAuthenticationService.cs`
   - `src/NanoWeaverHrt/Security/Authentication/SessionIdleEnforcingAuthenticationService.cs`
   - `src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`
2. In `src/NanoWeaverHrt/Security/Authentication/UserAuthenticationService.cs`, also delete:
   - `SignEphemeralNonce`
   - `DecryptPrivateKeyBytes`
   - `ExtractPrivateKeyDerFromPem`
   *(Keep `EncryptPrivateKeyBytes` intact because `GenerateSecureKeyBundle` uses it when a passphrase is provided.)*
3. In `src/NanoWeaverHrt/Security/Authentication/AuthRequestModels.cs`, delete `KeyLoginRequest`.
4. In `src/NanoWeaverHrt/Program.cs`:
   - Update the OpenAPI security definition description at line 181 to reference `/api/auth/token` and `/api/auth/web-token` (removing `/api/auth/login-key`).
   - Remove `path.Equals("/api/auth/login-key", StringComparison.OrdinalIgnoreCase)` from the unauthenticated allow-list at line 424.
   - Delete `app.MapPost("/api/auth/login-key", ...)` (lines 1806–1859).
5. Update `UserProvisioningEndpointTests.cs`, `SessionIdleEnforcementTests.cs`, and `AuthenticationServiceTests.cs`.
   - In `AuthenticationServiceTests.cs`, add an assertion that `POST /api/auth/login-key` now returns `401 Unauthorized` (unauthenticated) rather than accepting a private key, and verify that `VerifyChallengeAndIssueTokenAsync(..., isWebRequest: true)` logs `UserAuthenticated` / `AuthenticationFailed` to `IComplianceAuditService`.
6. Update `docs/api-reference.md` (replace `POST /api/auth/login-key` with `POST /api/auth/web-token`) and record the breaking removal of `POST /api/auth/login-key` in `docs/release-notes.md`.

---

## 4. Verification Gate Checklist

1. Confirm zero oracle references remain:
   ```bash
   grep -rn --exclude-dir=obj --exclude-dir=bin --exclude-dir=TestResults -E "login-key|AuthenticateWithPrivateKeyAsync|KeyLoginRequest|ExtractPrivateKeyDerFromPem|DecryptPrivateKeyBytes|SignEphemeralNonce" src/ tests/ docs/
   ```
   *(Only historical mention in `docs/release-notes.md` is permitted.)*
2. Run the full verification gate:
   ```bash
   ./scripts/verify-all.sh
   ```
   Confirm all steps pass and update `coverage-baseline.json` downward if deleting `AuthenticateWithPrivateKeyAsync` reduced `uncovered_lines_total` (it removes 4 uncovered state-machine lines in `UserAuthenticationService/<AuthenticateWithPrivateKeyAsync>d__15`).
