# Design Proposal: True Challenge-Response Sign-In (audit C8)

Status: **Proposal for review. Not a work package. Nothing here is approved for implementation.**
Scope: make the browser sign the server's challenge so the private key never leaves the client.
Prerequisite: the C1/C6 secret-provisioning package should land first — forging tokens is currently cheaper than stealing a private key, so fixing C8 in isolation buys little.

---

## 1. What the code does today

The sign-in path used by the Web UI hands the user's private key to the server, which then signs a nonce it generated itself and verifies its own signature.

| Step | Location | What happens |
| :-- | :--- | :--- |
| 1 | `Components/Pages/Login.razor:50` | `<InputFile id="keyFileUpload" OnChange="HandleKeyFileUpload" />` — a Blazor `InputFile`, so the chosen file is streamed **to the server** over the SignalR circuit. |
| 2 | `Components/Pages/Login.razor:144-146` | `e.File.OpenReadStream(maxAllowedSize: 65536)` is read server-side into `_privateKeyPem`. The PEM text is now a server-side variable. |
| 3 | `Components/Pages/Login.razor:167-171` | `AuthService.AuthenticateWithPrivateKeyAsync(_username, _privateKeyPem, _password, isWebRequest: true)` — an in-process call on the server. |
| 4 | `Security/Authentication/UserAuthenticationService.cs:337` | `ExtractPrivateKeyDerFromPem(privateKeyPem, password)` — the server parses the PEM and, for an encrypted key, runs PBKDF2-SHA256 (100 000 iterations) + AES-256-GCM to **decrypt the private key** (`DecryptPrivateKeyBytes`, line 630). |
| 5 | `UserAuthenticationService.cs:344-350` | The server generates its own 32-byte nonce and calls `SignEphemeralNonce` (line 547) — a **signing oracle** operating on the caller's key. |
| 6 | `UserAuthenticationService.cs:368` | The server verifies the signature **it just produced** against the stored public key. |
| 7 | `UserAuthenticationService.cs:374-378` | Mints the bearer token. **No `IComplianceAuditService.LogAction` call anywhere in this method** — contrast `VerifyChallengeAndIssueTokenAsync`, which logs `AuthenticationFailed` (line 296) and `UserAuthenticated` (line 310). |
| 8 | `Program.cs:1637-1682` | The same method is also exposed over HTTP as `POST /api/auth/login-key`, with the key in the request body (`AuthRequestModels.cs:184`, `PrivateKeyPem`), and is in the unauthenticated allow-list at `Program.cs:321-327`. |
| 9 | `Program.cs:1651` | That endpoint passes `isWebRequest: false`, which (per `UserAuthenticationService.cs:363-366`) rejects every `AccessGate.Web` account and hands an `AccessGate.Api` account a browser session cookie. Both transport boundaries are inverted. |

Meanwhile the correct machinery already exists and the Web UI does not use it:

- `POST /api/auth/challenge` → `CreateChallenge` (`UserAuthenticationService.cs:211-239`): 32 bytes from `RandomNumberGenerator.Fill`, a `Guid.NewGuid("N")` challenge id, 60-second TTL (`ChallengeLifetime`, line 58), held in a `ConcurrentDictionary<string, AuthChallenge>` (line 53).
- `POST /api/auth/token` → `VerifyChallengeAndIssueTokenAsync` (lines 242-317): single-use by construction (`_activeChallenges.TryRemove`, line 258), checks expiry, checks the username matches the challenge, checks the access gate, verifies with `VerifySignatureInternal` (line 519) against the account's stored `PublicKeyBase64`, logs both outcomes. Its own comment at `Program.cs:1598` says "without transmitting private key bytes over the wire".

So the defect is **not** a missing protocol. It is that the browser cannot currently produce the signature, so a signing oracle was built on the server instead.

What is signed today: the **raw 32 nonce bytes**, not a hash and not a structured message. Ed25519 verification is `Ed25519Signer` over `Convert.FromBase64String(challenge.NonceBase64)` (`UserAuthenticationService.cs:525-531`); ML-DSA-65 goes through `_pqcProvider.VerifySignature(data, signatureBase64, user.PublicKeyBase64)` (line 535). The stored public key is **SubjectPublicKeyInfo DER, Base64** (produced at `GenerateSecureKeyBundle`, lines 168/177), and the private key PEM is **PKCS#8 `PrivateKeyInfo` DER** (line 169/178) or the project's own encrypted envelope `[salt 16][nonce 12][tag 16][ciphertext]` (lines 616-621).

---

## 2. Target protocol

No new cryptographic design. The existing two-call handshake, driven from the browser:

```
browser                                    server
   |  POST /api/auth/challenge             |
   |  {"username":"researcher-01"}         |
   |-------------------------------------->|  CreateChallenge: 32 random bytes,
   |                                       |  challengeId, expiresAtUtc, stored once
   |<--------------------------------------|  AuthChallenge { challengeId, username,
   |                                       |                 nonceBase64, expiresAtUtc }
   |                                       |
 [ user picks a .pem file + passphrase ]   |
 [ parse PKCS#8 / decrypt envelope ]       |   <-- all of this in the browser
 [ sign the 32 nonce bytes ]               |
   |                                       |
   |  POST /api/auth/web-token             |
   |  {username, challengeId,              |
   |   signatureBase64}                    |
   |-------------------------------------->|  TryRemove(challengeId) -> single use
   |                                       |  expiry, username match, access gate,
   |                                       |  VerifySignatureInternal against the
   |                                       |  stored public key, audit log, mint token
   |<--------------------------------------|  Set-Cookie: weaver_session=...
   |                                       |  HttpOnly; Secure; SameSite=Strict
```

Properties, with what changes and what does not:

| Property | Value | Status |
| :--- | :--- | :--- |
| Nonce entropy | 256 bits from `RandomNumberGenerator.Fill` | **Already correct** (`UserAuthenticationService.cs:225-226`). Keep. |
| Challenge id | `Guid.NewGuid("N")` — a lookup handle, not a secret | Already correct. Keep. |
| TTL | 60 s today | **Recommend 120 s.** A human must pick a file and type a passphrase, and a pure-JS PBKDF2 at 100 000 iterations can take 1-3 s. 60 s is survivable but tight on a slow laptop. Make it a named constant, not a literal. |
| Single use | `_activeChallenges.TryRemove` before any validation | **Already correct and atomic.** Keep exactly as is — in particular, keep the removal *before* the signature check so a failed attempt burns the nonce. |
| Replay across users | Challenge stores the username and compares it (line 268) | Already correct. Keep. |
| Username enumeration | `CreateChallenge` never looks the user up, so a challenge is issued for any username | **Already correct, and worth protecting in review** — do not "improve" this by returning 404 for unknown users. |
| Storage | In-process `ConcurrentDictionary`, pruned only on the next `CreateChallenge` call (lines 216-223) | **Needs hardening.** This is unauthenticated-reachable (`Program.cs:321`), so the dictionary grows with request rate × TTL (audit L9). Add a hard cap (e.g. 10 000 entries; reject with 429/503 above it) and prune on a `PeriodicTimer` in a hosted service rather than lazily. Deterministic to test by injecting the timer interval or calling the prune method directly. |
| Multi-node | Node-local | Acceptable and consistent with the token key being node-local (audit M7). A challenge issued by node A and answered on node B will fail with "Invalid or expired authentication challenge." If the stack is ever load-balanced, back the store with `IValkeyCacheService` (`Caching/ValkeyCacheService.cs`) using `SET key val NX EX 120` for issuance and `GETDEL` for single-use consumption — Valkey gives both atomically. Do **not** replicate challenges by any other means. |
| Message signed | Raw 32 nonce bytes | **Recommend leaving unchanged in this proposal.** Domain separation (signing `"nanoweaver-hrt/auth/v1\n" + challengeId + "\n" + username + "\n"` ‖ nonce) is better practice, but these keys sign nothing else in the system, and changing it forces either a breaking change for every existing API client of `/api/auth/token` or a dual-accept verification path that doubles the branch count under the 100% gate. Call it out as separate optional hardening with that cost attached. |
| Transport boundary | `isWebRequest` is a literal at both call sites (`Program.cs:1609`, `1651`) | **Must be fixed as part of this work**, otherwise moving the Web UI onto `/api/auth/token` makes every `AccessGate.Web` account unable to sign in — a regression worse than the bug. Recommendation: decide it by **endpoint**, not by a client-supplied field. `POST /api/auth/token` stays the API path (`isWebRequest: false`, token in the body, no cookie). A new `POST /api/auth/web-token` is the browser path (`isWebRequest: true`, sets the `HttpOnly; Secure; SameSite=Strict` cookie as `Program.cs:1659-1665` already does, and returns the user record but **not** the raw token). A client-asserted `transport` field would be self-certifying and would void the gate. |
| Audit trail | `AuthenticateWithPrivateKeyAsync` logs nothing | Fixed for free: the challenge path already logs `UserAuthenticated` / `AuthenticationFailed`. The new `/api/auth/web-token` handler must not bypass `VerifyChallengeAndIssueTokenAsync`. |

---

## 3. The hard part: signing in the browser with Ed25519 and ML-DSA-65 only

Directive 12 allows exactly two algorithms. ECDSA P-256 and RSA — the two things WebCrypto has supported everywhere for a decade — are **not implemented in this project and must not be reintroduced**, which removes the easy answer. The honest situation:

- **Ed25519 in WebCrypto** exists now (Chrome 137+, Firefox 130+, Safari 17+), but `window.crypto.subtle` is only present in a **secure context**: HTTPS, or `localhost`. The default stack is `http://localhost:5000` (fine), but the documented multi-node deployments reach peers over plain HTTP on a LAN hostname — `docs/example-workflows.md:142,153,156` and `docs/configuration-peers.md:102` literally tell operators to use `https://lab-a.hrt-lab.local:5000`-style URLs (and audit L3 notes those are wrong in another way). On any plain-HTTP origin that is not `localhost`, `crypto.subtle` is `undefined` and a WebCrypto-based login simply cannot work. This single fact, more than browser-version support, is why I do not recommend WebCrypto as the primary path.
- **ML-DSA-65 in WebCrypto** does not exist in any shipping browser and is not in a Candidate Recommendation. There is no native option at all.
- **No bundler exists in this repository.** `package.json` has one script (`test`) and no dependencies; `wwwroot/js/*.js` are plain scripts loaded by the page. Anything added must be a vendored ES module (or classic script) served from `wwwroot`, not an npm build artefact.

### Options

| Option | How | Honest downside |
| :--- | :--- | :--- |
| **A. WebCrypto `Ed25519` only** | `crypto.subtle.importKey('pkcs8', der, {name:'Ed25519'}, false, ['sign'])` then `sign`. | Dead on any non-`localhost` HTTP origin (no `crypto.subtle`). Covers **no** ML-DSA account, so Web-gated ML-DSA users can never sign in. Encrypted PEMs additionally need `subtle.deriveBits` (PBKDF2) + `subtle.decrypt` (AES-GCM), same secure-context constraint. Zero vendored crypto, which is its real virtue. |
| **B. Vendored pure-JS Ed25519** (`@noble/ed25519` + `@noble/hashes` for SHA-512) | Import as ES modules from `wwwroot/lib/`. | Supply-chain risk (see mitigation below). ~5-15 KB. Works in every context, no HTTPS requirement, one code path. Note: `@noble/ed25519` v2 requires an injected SHA-512 — using `crypto.subtle.digest` for that would re-import the secure-context problem, so `@noble/hashes/sha512` must be vendored too. |
| **C. Vendored WASM PQC** (`liboqs-js`, PQClean builds) | WASM module for ML-DSA-65. | Hundreds of KB to multiple MB. Reproducible-build story is poor; auditing a WASM blob is not realistic for this project. Needs `wasm-unsafe-eval` if a CSP is ever added (none is set today — `Program.cs:200-208` sets no `Content-Security-Policy`). Rejected. |
| **D. Vendored pure-JS ML-DSA-65** (`@noble/post-quantum`, `ml_dsa65`) | Pure TS/JS, no WASM, same author and audit lineage as B. | Supply-chain risk. Signing is tens of milliseconds — fine for a login. The real risk is **encoding compatibility**, see §6. |
| **E. Feature-detect: A when available, B/D otherwise** | Branch on `globalThis.crypto?.subtle`. | Two code paths for the same operation, both of which must reach 100% line/branch/function coverage under Directive 5 — meaning the test suite has to delete `globalThis.crypto.subtle` to drive the fallback. Doable, roughly doubles the JS test burden for no user-visible benefit once B/D exist. Rejected. |
| **F. Keep server-side signing, restrict it** (e.g. localhost only, or require TLS) | — | Does not satisfy Directive 12's "strictly public-key challenge-response", and the key is still recoverable from a heap dump or a proxy body log. Rejected. |

### Recommendation

**B + D, vendored, as the single code path.** No WebCrypto dependency for signing, hashing, PBKDF2 or AES-GCM. Rationale: one path means one set of tests and a realistic shot at 100% JS coverage; no secure-context trap, so plain-HTTP LAN nodes keep working; and it is the only option that covers ML-DSA-65 at all without WASM.

Supply-chain mitigation, to be treated as non-negotiable if this is approved:
1. Vendor exact files into `src/NanoWeaverHrt/wwwroot/lib/<package>@<version>/`, pinned by version **and** by recorded SHA-256 of every file, listed in a `docs/` manifest (same discipline as the dataset SHA-256 manifests the project already ships).
2. Add `integrity="sha384-…"` on every `<script>` tag that loads them; for ES-module imports, record hashes in the manifest and add a `scripts/verify-vendored-js.sh` check that re-hashes the tree (a candidate sixth step for `scripts/verify-all.sh`).
3. Review the vendored source once, by hand, and record who reviewed it. These files sign authentication challenges with patient-identifying key material; "it's popular on npm" is not a review.
4. Never load them from a CDN. Directive 11's sovereignty posture and Directive 9's hardening both argue against an external origin, and SRI does not stop a CDN from being unavailable.
5. `npm test` must exclude the vendored tree from the coverage numbers (`--test-coverage-exclude`), otherwise third-party branches sink the 100% gate. **Not verified:** which Node version CI runs, and therefore whether `--test-coverage-exclude` is available. If it is not, the alternative is to keep the vendored modules out of the test's import graph by testing our wrapper against an injected signer interface — which is arguably better design anyway.

Our own code (`weaver-auth.js`) must reach 100% line, branch and function coverage. Design it to be testable from `node --test` with no DOM: pure functions (`parsePrivateKeyPem`, `decryptKeyEnvelope`, `signChallenge`) plus one thin DOM/fetch shell, exactly the split that makes `weaver-visualizer.js` testable today.

---

## 4. How the PEM-file sign-in UX survives

The user experience does not change: pick a `.pem`/`.key` file (or paste the text), optionally type a passphrase, press "Sign In with Private Key". What changes is that every byte of the key stays in the tab.

Required changes to make that true:

1. **Replace `<InputFile>` with a plain `<input type="file">`.** `InputFile` is a Blazor component that streams the file to the server — keeping it would defeat the entire exercise even if the signing moved. The file is read in JS via `FileReader`/`Blob.text()`, with the existing 64 KB cap enforced client-side (`file.size > 65536` → refuse).
2. **The textarea paste path stays**, but it must be a plain `<textarea>` read by JS (`document.getElementById('privKeyPem').value`), **not** `@bind="_privateKeyPem"` — a Blazor two-way binding sends every keystroke across the circuit. This is a subtle trap: the existing textarea with `@bind` (`Login.razor:64`) already ships pasted PEM text to the server on blur.
3. **The passphrase field likewise** moves off `@bind` (`Login.razor:70`).
4. **PEM parsing in JS.** Strip the `-----BEGIN/END-----` lines, Base64-decode, then: for `PRIVATE KEY`, parse the PKCS#8 `PrivateKeyInfo` to recover the raw key (Ed25519: the 32-byte seed sits inside a nested OCTET STRING; a minimal DER walker is ~40 lines and must be covered 100%, including malformed input); for `ENCRYPTED PRIVATE KEY`, reproduce the project's own envelope — `[salt 16][nonce 12][tag 16][ciphertext]`, PBKDF2-SHA256 100 000 iterations, AES-256-GCM with the salt as associated data (`UserAuthenticationService.cs:594-625`). Wrong passphrase must produce the same user-facing message as today ("Failed to parse or decrypt private key. Check passphrase or format.").
5. **Key material lifetime.** Overwrite the typed arrays with zeros after signing and drop references. Be honest in the docs: JavaScript cannot guarantee erasure (strings are immutable, the GC may have copied), so the claim is "best effort", not "guaranteed". It is still categorically better than posting the key to a server that logs request bodies.
6. **After a successful `POST /api/auth/web-token`** the server sets the `HttpOnly` cookie and the page navigates to `/`. The Blazor circuit then picks the session up — but note that `MainLayout.OnAfterRenderAsync` currently rehydrates from `window.weaverGetSessionToken()` (`MainLayout.razor:432`), which reads `localStorage`. That is audit C7's defect. **This proposal should not write the token to `localStorage` at all**; rehydration should read the cookie server-side. That overlaps with C7's fix and the two must be sequenced or merged — see §6.

---

## 5. Blast radius

### Server (C#)

| File | Change |
| :--- | :--- |
| `Security/Authentication/IUserAuthenticationService.cs:72-84` | Remove `AuthenticateWithPrivateKeyAsync` from the interface. |
| `Security/Authentication/UserAuthenticationService.cs` | Delete `AuthenticateWithPrivateKeyAsync` (320-379) and `SignEphemeralNonce` (547-564). `ExtractPrivateKeyDerFromPem` (672-708) and `DecryptPrivateKeyBytes` (630-667) become unreachable from production once that method is gone — they must be deleted too (their logic is reimplemented in JS). `EncryptPrivateKeyBytes` (594-625) **stays**: `GenerateSecureKeyBundle` still uses it at line 188. Add the challenge-store cap/prune. |
| `Security/Authentication/AuthRequestModels.cs:170-203` | Delete `KeyLoginRequest` entirely (it exists only for the deleted endpoint), including its `GetExamples()` sample private key at line 200. |
| `Program.cs` | Delete the `/api/auth/login-key` endpoint (1637-1682) and its entry in the unauthenticated allow-list (line 323). Add `POST /api/auth/web-token` next to `/api/auth/token` (1595-1632) with `isWebRequest: true`, same-origin enforcement, antiforgery consideration, and the cookie block currently at 1659-1665. Add it to the allow-list at 321-327. |
| `Swagger` surface | `AddSecurityDefinition`'s description at `Program.cs:143` names `/api/auth/login-key`; update it. |

### Client

| File | Change |
| :--- | :--- |
| `src/NanoWeaverHrt/wwwroot/js/weaver-auth.js` | **New.** `requestChallenge`, `parsePrivateKeyPem`, `decryptKeyEnvelope`, `signChallenge`, `submitSignature`, plus one `window.weaverSignIn(username, pemText, passphrase)` entry point returning `{ok, error}`. Pure functions separated from `fetch`/DOM so `node --test` can cover them. |
| `src/NanoWeaverHrt/wwwroot/lib/<vendored>` | **New.** Pinned, hashed, hand-reviewed Ed25519 + SHA-512 (+ ML-DSA-65, + PBKDF2/AES-GCM) ES modules. |
| `src/NanoWeaverHrt/wwwroot/js/app.js:419-463` | The `weaverSetSession` / `weaverGetSessionToken` / `weaverClearSession` trio should stop persisting the bearer token in `localStorage`. **Coordinate with C7** rather than changing it twice. |
| `Components/Pages/Login.razor` | Rewritten: no `InputFile`, no `@bind` on the key or passphrase fields, no `AuthService` injection, no `AuthenticateWithPrivateKeyAsync` call. `Authenticate()` becomes `await JS.InvokeAsync<SignInResult>("weaverSignIn", …)` followed by a navigation. **Reminder:** `.razor` files are excluded from coverage (`coverlet.runsettings:10`) and the repo has no bUnit (verified: `NanoWeaverHrt.Tests.csproj` lists seven packages, none of them a component test host), so this rewrite is **completely untested by the C# suite**. Every regression here must be caught by the JS tests plus manual verification. That is exactly how the UI gating regression in commit `be3d207` shipped with a green gate. |
| `Components/Layout/MainLayout.razor:426-446` | Session rehydration source changes if the token stops living in `localStorage`. C7 territory. |

### Tests

`grep -c` for `login-key|AuthenticateWithPrivateKeyAsync|PrivateKeyPem`: `AuthenticationServiceTests.cs` 21, `AuthEndpointsIntegrationTests.cs` 10, `SecurityPenetrationTests.cs` 9, `SystemIntegrationPipelineTests.cs` 9, `UserProfileAndContactTests.cs` 2, `docs/api-reference.md` 1. **Those counts are not all C8** — `PrivateKeyPem` is also a property of `GeneratedKeyBundle` (the server-generated-key download flow), so each hit needs triage. Expect to delete the direct-private-key-login tests, rewrite several integration tests to drive `/api/auth/challenge` + `/api/auth/web-token`, and keep every `GeneratedKeyBundle` test untouched. Because deleting production code removes covered lines, the coverage percentage should rise, not fall — but the new `/api/auth/web-token` handler, the same-origin check and the challenge-store cap all need fresh tests at 100% branch.

`npm test` changes from one test file to two or more, and its coverage flags need the vendored exclusion.

### `/setup` wizard

The wizard needs **no change for C8 itself**: it never calls `AuthenticateWithPrivateKeyAsync`. It calls `CompleteInitialSetupAsync` and then `AuthService.GenerateBearerToken(admin)` in-process (`SetupWizard.razor:348-355`).

It does, however, have the *sibling* defect: the keypair is **generated on the server** (`GenerateSecureKeyBundle`, `UserAuthenticationService.cs:147-208`, reached from `SetupWizard.razor` and `UserManagement.razor`, and exposed as `POST /api/setup/generate-keypair` and `POST /api/users/generate-keypair`), so the server sees the private key at birth and ships it to the browser for download. Fixing C8 means a private key never travels **to** the server; it does not stop one travelling **from** it. Closing that requires in-browser key generation and is a separate, larger piece of work (§6, Phase 4). Say so plainly in the docs rather than claiming the key "never leaves the browser" while the wizard still mints it server-side.

---

## 6. Phased plan

**Phase 0 — prerequisite.** Land the C1/C6 secret-provisioning package. Until the token HMAC key is secret, an attacker forges an Administrator token in a second and the login mechanism is irrelevant.

**Phase 1 — server only, no client change.** Add `POST /api/auth/web-token` (`isWebRequest: true`, HttpOnly cookie, same-origin check). Raise `ChallengeLifetime` to 120 s. Cap and actively prune `_activeChallenges` (fixes L9). Full C# tests. `/api/auth/login-key` still works, so nothing breaks. Independently shippable and independently reviewable.

**Phase 2 — client, behind the existing endpoint.** Vendor and review the crypto modules; write `weaver-auth.js` with 100% JS coverage; rewrite `Login.razor` to call it; verify end-to-end against a real Ed25519 account and a real ML-DSA-65 account, with and without a passphrase. `login-key` still exists as a fallback during this phase, so a failure is recoverable.

**Phase 3 — remove the oracle.** Delete `AuthenticateWithPrivateKeyAsync`, `/api/auth/login-key`, `KeyLoginRequest`, `SignEphemeralNonce`, `ExtractPrivateKeyDerFromPem`, `DecryptPrivateKeyBytes` and the corresponding tests. Update `docs/api-reference.md` and the Swagger security description. This is the phase that actually satisfies Directive 12, and it is a breaking API change for any script using `login-key` — it needs a release note.

**Phase 4 — separate proposal, optional.** In-browser key generation for `/setup` and user creation, retiring `GenerateSecureKeyBundle`'s private-key output. Only the public key would ever reach the server.

Optional hardening, any time after Phase 3: domain-separated signed messages, and a Valkey-backed challenge store for multi-node.

---

## 7. What could go wrong

1. **ML-DSA-65 key encoding mismatch — the biggest technical risk.** The server stores the public key as SubjectPublicKeyInfo DER and the private key as PKCS#8 `PrivateKeyInfo`, both produced by BouncyCastle 2.7. Pure-JS ML-DSA implementations generally want the **raw** 32-byte seed (or the expanded secret key) and the raw 1952-byte public key. Which of those BouncyCastle writes into the PKCS#8 `privateKey` field — seed, expanded key, or both, and under which of the several competing draft encodings — I have **not verified**, and the drafts changed more than once. If BouncyCastle writes an expanded key and the JS library only accepts a seed, ML-DSA browser login is blocked until one side is adapted. **Mitigation: run a spike before committing to Phase 2** — generate one ML-DSA-65 bundle via `POST /api/setup/generate-keypair`, dump the PKCS#8 bytes, and sign/verify across both implementations. If it fails, the user must decide between (a) Ed25519-only Web sign-in with ML-DSA accounts restricted to `AccessGate.Api` (which contradicts the spirit of Directive 12), (b) changing the stored private-key encoding (breaks every existing ML-DSA key file), or (c) WASM after all.
2. **Signature-encoding mismatch for Ed25519.** Less likely — `Ed25519Signer.VerifySignature` expects the raw 64-byte signature, which is what every library produces. Verify anyway with a known-answer test (RFC 8032 test vectors) on both sides.
3. **Pure-JS PBKDF2 at 100 000 iterations blocks the UI thread** for roughly 1-3 s on a laptop, longer on a phone (the repo has recent phone-layout fixes, so mobile is in scope). Mitigation: a Web Worker, or a visible progress state. If it is done on the main thread with no feedback, users will conclude the button is broken and click it repeatedly — and each click burns a challenge.
4. **The `.razor` blind spot.** `Login.razor` markup is not covered by anything (no bUnit, `.razor` excluded from coverage). A typo in an element id silently breaks sign-in with a 100% green gate. Mitigation: have `weaver-auth.js` fail loudly with a specific message when an expected element is missing, and cover that branch in the JS tests; and add the sign-in flow to a manual pre-release checklist.
5. **Supply chain.** Two to four vendored crypto modules now sit on the authentication path. A compromised update exfiltrates private keys from every user's browser — strictly worse than today's defect, which at least confines the key to the operator's own server. This risk is the price of the design and is only acceptable with pinning, hashing, SRI, a `verify-all.sh` integrity step and a recorded human review.
6. **Coverage gate collisions.** Vendored JS inside the coverage scope will fail the 100% threshold; the hybrid WebCrypto option would need `globalThis.crypto.subtle` deleted in tests; and new C# branches (same-origin check, challenge cap) all need tests. Budget for the gate, not just the feature.
7. **Sequencing conflict with C7.** Both this proposal and C7 rewrite the browser session-persistence story (`app.js:419-463`, `MainLayout.razor:426-446`, `Login.razor`). Doing them independently guarantees a merge conflict in files with no test coverage. Recommend: decide the final session model (HttpOnly cookie only, server-side idle tracking) once, then implement C7 and C8 Phase 2 against that single decision.
8. **Lockout risk.** If Phase 2 ships with a parsing bug for encrypted PEMs and Phase 3 has already removed `login-key`, every passphrase-protected account is locked out of the Web UI with no fallback. Mitigation: the phase ordering above (Phase 3 strictly after Phase 2 is verified against both algorithms, both passphrase states), and keep `/api/auth/challenge` + `/api/auth/token` usable from `curl` so an Administrator can always get in.
9. **Same-origin enforcement on `/api/auth/web-token`** must not break the Blazor circuit or the reverse-proxy deployments. Behind Caddy/Nginx the `Origin` header is the public domain, not the container host; validate against the configured public origin, not `HttpContext.Request.Host`, or the hardened overlays will refuse logins.

---

## 8. Questions for the reviewer

1. ML-DSA-65 in the browser: accept the spike risk in item 1, or scope Phase 2 to Ed25519 first and treat ML-DSA Web sign-in as a follow-up with a documented interim limitation?
2. Vendored pure-JS crypto (recommended, one code path, no HTTPS requirement) versus WebCrypto-where-available (no vendored crypto, but broken on plain-HTTP LAN origins and no ML-DSA at all)?
3. Is a breaking removal of `POST /api/auth/login-key` acceptable, or does it need a deprecation window with a `Warning` header?
4. Should C7 and C8 Phase 2 be merged into one session-model work package to avoid two rewrites of the same untested files?
5. Domain-separated signed message now (cleaner, breaks existing API clients of `/api/auth/token`) or never?
