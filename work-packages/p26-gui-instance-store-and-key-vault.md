# Work Package P26: Local Instance Registry & Encrypted Key Vault (`NanoWeaverHrt.Client`)

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
git rev-parse HEAD   # must be on feature/gui-application branched from 3d451c085e003d0c736fe9bf73928574aa5d8041
```

## 1. Context & Objective
- **Problem Statement**: Standalone desktop and mobile GUI applications need to manage multiple `nanoweaver-hrt` instances (both local Docker/host nodes and remote cluster nodes) simultaneously. Each instance connection requires persistent configuration and zero-trust client-side storage of asymmetric private keys (`Ed25519` and post-quantum `ML-DSA-65`) sealed at rest with `AES-256-GCM`.
- **Goal & Scope**:
  - **Included**: Create the shared `src/NanoWeaverHrt.Client/NanoWeaverHrt.Client.csproj` class library (`net10.0`), register it in `NanoWeaverHrt.slnx` and `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`, implement local SQLite persistence for multi-instance profiles (`IInstanceProfileRepository`), implement the client-side encrypted key vault (`IClientKeyRingVault`) matching the sovereign envelope format (`[salt 16][nonce 12][tag 16][ciphertext]`, `PBKDF2-SHA256` 100,000 iterations), and implement client-side challenge signing (`IClientChallengeSigner`) for `Ed25519` and `ML-DSA-65`.
  - **Excluded**: HTTP transport calls (P27), background tray/notification orchestration (P28), and Avalonia UI components (P29–P30).
- **Target Role / Boundary**: `Administrator`, `Researcher`, `Patient`; `Api` / `ApiWeb` transport boundary.

## 2. Technical Contracts & Invariants

### 2.1 Solution & Project Structure
- Create `src/NanoWeaverHrt.Client/NanoWeaverHrt.Client.csproj`:
  - `<TargetFramework>net10.0</TargetFramework>`
  - `<Nullable>enable</Nullable>`
  - `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  - `<InternalsVisibleTo Include="NanoWeaverHrt.Tests" />`
  - PackageReferences: `Microsoft.Data.Sqlite` (version matching `src/NanoWeaverHrt/NanoWeaverHrt.csproj`) and `BouncyCastle.Cryptography` (`2.7.0`).
- Add `src/NanoWeaverHrt.Client/NanoWeaverHrt.Client.csproj` to `NanoWeaverHrt.slnx` and add a `<ProjectReference Include="..\..\src\NanoWeaverHrt.Client\NanoWeaverHrt.Client.csproj" />` to `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`.
  - **CRITICAL INVARIANT**: Keep all unit tests inside `tests/NanoWeaverHrt.Tests` so `dotnet test` emits a single unified `coverage.cobertura.xml` evaluated by `scripts/verify-all.sh` Step 4 and Step 7.

### 2.2 Interfaces & Types (`namespace NanoWeaverHrt.Client.Storage` & `NanoWeaverHrt.Client.Security`)

```csharp
namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Represents a configured connection profile for a local or remote nanoweaver-hrt instance.
/// </summary>
/// <param name="InstanceId">Unique deterministic identifier for the instance profile.</param>
/// <param name="DisplayName">Human-readable label for the instance (e.g., "Local Workstation", "Cluster Alpha").</param>
/// <param name="BaseUri">Canonical HTTPS or localhost HTTP base URI of the instance.</param>
/// <param name="IsLocalInstance">True if the instance runs on the local loopback interface.</param>
/// <param name="Username">Lowercase normalized username used for asymmetric PKI authentication.</param>
/// <param name="Role">Expected role on the target node (Administrator, Researcher, Patient).</param>
/// <param name="KeyAlgorithm">Asymmetric signature algorithm ("Ed25519" or "ML-DSA-65").</param>
/// <param name="PublicKeyPem">PEM-encoded SubjectPublicKeyInfo registered on the instance.</param>
/// <param name="EncryptedPrivateKeyEnvelopeBase64">Base64-encoded AES-256-GCM sealed private key envelope.</param>
/// <param name="NotificationsEnabled">True if desktop/mobile alerts are enabled for this instance.</param>
/// <param name="PollingIntervalSeconds">Telemetry and health polling interval in seconds (5 to 300).</param>
/// <param name="AddedAtUtc">UTC timestamp when the profile was registered.</param>
/// <param name="LastConnectedAtUtc">Optional UTC timestamp of the most recent successful handshake.</param>
public sealed record InstanceProfile(
    string InstanceId,
    string DisplayName,
    string BaseUri,
    bool IsLocalInstance,
    string Username,
    string Role,
    string KeyAlgorithm,
    string PublicKeyPem,
    string EncryptedPrivateKeyEnvelopeBase64,
    bool NotificationsEnabled,
    int PollingIntervalSeconds,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? LastConnectedAtUtc);

/// <summary>
/// Provides CRUD persistence for local and remote nanoweaver-hrt instance profiles.
/// </summary>
public interface IInstanceProfileRepository
{
    /// <summary>
    /// Initializes the local SQLite schema if it does not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all configured instance profiles ordered by display name.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of saved instance profiles.</returns>
    Task<IReadOnlyList<InstanceProfile>> ListProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single instance profile by its unique identifier.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching profile, or null if not found.</returns>
    Task<InstanceProfile?> GetProfileAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an instance connection profile.
    /// </summary>
    /// <param name="profile">The profile to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertProfileAsync(InstanceProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates the last connected timestamp for an existing instance profile.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="connectedAtUtc">The UTC connection timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a row was updated; otherwise false.</returns>
    Task<bool> UpdateLastConnectedAsync(string instanceId, DateTimeOffset connectedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes an instance profile by its unique identifier.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the profile existed and was removed; otherwise false.</returns>
    Task<bool> DeleteProfileAsync(string instanceId, CancellationToken ct = default);
}
```

```csharp
namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Represents a newly generated client-side asymmetric keypair and sealed private key envelope.
/// </summary>
/// <param name="KeyAlgorithm">Canonical algorithm identifier ("Ed25519" or "ML-DSA-65").</param>
/// <param name="PublicKeyPem">PEM-encoded public key for server registration.</param>
/// <param name="EncryptedPrivateKeyEnvelopeBase64">Base64-encoded AES-256-GCM encrypted PKCS#8 private key DER.</param>
/// <param name="PublicKeyFingerprint">SHA-256 hex fingerprint of the public key DER.</param>
public sealed record ClientKeyBundle(
    string KeyAlgorithm,
    string PublicKeyPem,
    string EncryptedPrivateKeyEnvelopeBase64,
    string PublicKeyFingerprint);

/// <summary>
/// Provides zero-trust client-side keypair generation, envelope sealing, and challenge signing.
/// </summary>
public interface IClientKeyRingVault
{
    /// <summary>
    /// Generates a new Ed25519 or ML-DSA-65 keypair in memory and seals the private key with AES-256-GCM.
    /// </summary>
    /// <param name="keyAlgorithm">Algorithm name ("Ed25519" or "ML-DSA-65").</param>
    /// <param name="passphrase">User passphrase used to derive the 256-bit wrapping key via PBKDF2-SHA256.</param>
    /// <returns>The generated public key PEM and sealed private key envelope.</returns>
    ClientKeyBundle GenerateKeyBundle(string keyAlgorithm, ReadOnlySpan<char> passphrase);

    /// <summary>
    /// Seals an existing PEM-encoded PKCS#8 private key into an AES-256-GCM envelope.
    /// </summary>
    /// <param name="privateKeyPem">Unencrypted PEM-encoded PKCS#8 private key.</param>
    /// <param name="passphrase">Passphrase for PBKDF2-SHA256 key derivation.</param>
    /// <returns>Base64-encoded envelope ([salt 16][nonce 12][tag 16][ciphertext]).</returns>
    string SealPrivateKeyPem(string privateKeyPem, ReadOnlySpan<char> passphrase);

    /// <summary>
    /// Unseals an AES-256-GCM private key envelope and signs a raw 32-byte server challenge nonce.
    /// </summary>
    /// <param name="keyAlgorithm">Algorithm name ("Ed25519" or "ML-DSA-65").</param>
    /// <param name="encryptedEnvelopeBase64">Base64-encoded sealed private key envelope.</param>
    /// <param name="passphrase">Passphrase used to unseal the envelope.</param>
    /// <param name="challengeNonce">Raw challenge bytes issued by POST /api/auth/challenge.</param>
    /// <returns>Base64-encoded cryptographic signature.</returns>
    string SignChallenge(
        string keyAlgorithm,
        string encryptedEnvelopeBase64,
        ReadOnlySpan<char> passphrase,
        ReadOnlySpan<byte> challengeNonce);
}
```

### 2.3 Database Schema (Client Local SQLite)
```sql
CREATE TABLE IF NOT EXISTS weaver_gui_instances (
    instance_id TEXT PRIMARY KEY NOT NULL,
    display_name TEXT NOT NULL,
    base_uri TEXT NOT NULL,
    is_local_instance INTEGER NOT NULL,
    username TEXT NOT NULL,
    role TEXT NOT NULL,
    key_algorithm TEXT NOT NULL,
    public_key_pem TEXT NOT NULL,
    encrypted_private_key_envelope TEXT NOT NULL,
    notifications_enabled INTEGER NOT NULL,
    polling_interval_seconds INTEGER NOT NULL,
    added_at_utc TEXT NOT NULL,
    last_connected_at_utc TEXT NULL
);
```

### 2.4 Coverage & Ratchet Invariants
- Wrap all `await using` statements in `SqliteInstanceProfileRepository` inside explicit `{ await using ... }` scopes so Roslyn does not emit unreachable async state-machine disposal branches.
- In unit tests, always isolate SQLite cleanup via `SqliteConnection.ClearPool(connection)` — never `SqliteConnection.ClearAllPools()`.
- Update `coverage-baseline.json` via `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update` if total covered lines increase with 0 new uncovered lines.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create `src/NanoWeaverHrt.Client/NanoWeaverHrt.Client.csproj`, add it to `NanoWeaverHrt.slnx`, and reference it from `tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj`.
2. **Task 2**: Implement `InstanceProfile`, `IInstanceProfileRepository`, and `SqliteInstanceProfileRepository` with input validation (normalizing `Username` to lowercase, clamping `PollingIntervalSeconds` to `[5, 300]`, validating HTTPS or loopback HTTP on `BaseUri` to enforce Prime Directive 9).
3. **Task 3**: Implement `ClientKeyBundle`, `IClientKeyRingVault`, and `AesGcmClientKeyRingVault` using `BouncyCastle.Cryptography` for `Ed25519` and `ML-DSA-65` (`MLDsaParameters.ml_dsa_65`), zeroing raw PKCS#8 DER bytes in a `finally` block with `CryptographicOperations.ZeroMemory`.
4. **Task 4**: Write 100% line and branch unit tests in `tests/NanoWeaverHrt.Tests/Client/SqliteInstanceProfileRepositoryTests.cs` and `tests/NanoWeaverHrt.Tests/Client/AesGcmClientKeyRingVaultTests.cs`.
5. **Task 5**: Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all public, internal, and private members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
