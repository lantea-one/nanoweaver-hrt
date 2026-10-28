# Work Package P31: Sovereign Code-Signing PKI & Automated 90-Day Certificate Rotation

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: All desktop and mobile binary packages (`.deb`, `.rpm`, `.pkg.tar.zst`, macOS `NanoWeaverHrt.app`, Windows `nanoweaver-hrt-desktop-installer.exe` & `NanoWeaverHrt.msix`, and Mobile `.apk` & `.ipa`) must be cryptographically signed so users and package managers can verify authenticity and tamper resistance.
- **Architectural Fact Regarding Let's Encrypt**:
  - Let's Encrypt (and the ACME RFC 8555 protocol) strictly issues **TLS Server Authentication (`id-kp-serverAuth`, OID `1.3.6.1.5.5.7.3.1`)** certificates and explicitly forbids the **Code Signing Extended Key Usage (`id-kp-codeSigning`, OID `1.3.6.1.5.5.7.3.3`)** required by Windows Authenticode/MSIX, macOS `codesign`/`rcodesign`, and package verifiers.
  - To provide the exact same **automated 90-day short-lived certificate renewal and rotation lifecycle** as Let's Encrypt without paid commercial CA lock-in, `nanoweaver-hrt` implements a **Sovereign X.509 Code-Signing CA (`id-kp-codeSigning`) + OpenPGP/GPG Archive Signing Key** with automated scheduled rotation workflows in Forgejo Actions and mirrored GitHub Actions.
- **Goal & Scope**:
  - **Included**:
    - Create `scripts/pki/provision-or-rotate-codesign.sh` to generate or rotate a 90-day leaf X.509 Code-Signing certificate (`EKU = codeSigning`, OID `1.3.6.1.5.5.7.3.3`), PKCS#12 bundle (`codesign-leaf.pfx`), Android APK signing keystore, and OpenPGP/GPG archive signing subkey.
    - Create `scripts/pki/sign-packages.sh` to sign Windows PE/MSIX (`osslsigncode`), macOS bundles/IPA (`rcodesign` or detached CMS/SHA-256 signature), Android APK (`apksigner` / `jarsigner`), Linux packages (`.deb`, `.rpm`, `.pkg.tar.zst` GPG detached `.sig`), and a master `SHA256SUMS` + `SHA256SUMS.asc` manifest.
    - Create scheduled CI workflows `.forgejo/workflows/rotate-codesign-certs.yml` and `.github/workflows/rotate-codesign-certs.yml` running monthly (`cron: '0 3 1 * *'`) and on `workflow_dispatch` to check leaf certificate expiry (`< 30` days remaining), rotate the 90-day leaf certificate, and publish public trust anchors to `docs/assets/keys/`.

## 2. Technical Contracts & Invariants

### 2.1 Certificate Profile & File Paths
- **Root CA Certificate (Public Trust Anchor)**: `docs/assets/keys/nanoweaver-codesign-root-ca.crt`
  - Subject: `CN=NanoWeaver HRT Sovereign Code Signing Root CA, O=Lantea Sovereign Research, C=US`
  - Key: ECDSA P-384 or RSA-4096, `basicConstraints = critical, CA:TRUE, pathlen:0`, `keyUsage = critical, keyCertSign, cRLSign`.
- **Leaf Code-Signing Certificate (90-Day Automated Rotation)**: `docs/assets/keys/nanoweaver-codesign-leaf.crt`
  - Subject: `CN=NanoWeaver HRT Release Engineering, O=Lantea Sovereign Research, C=US`
  - Validity: `90 days` (automatically rotated every 60 days / when `< 30` days remain via `openssl x509 -checkend 2592000`).
  - Extensions:
    - `basicConstraints = critical, CA:FALSE`
    - `keyUsage = critical, digitalSignature`
    - `extendedKeyUsage = critical, codeSigning (1.3.6.1.5.5.7.3.3)`
- **Public GPG Keyring**: `docs/assets/keys/nanoweaver-archive-keyring.asc`
- **Strict Secret Hygiene**:
  - Private keys (`*.key`, `*.pfx`, `*.jks`, private GPG keyring) MUST NEVER be committed to Git. Add `artifacts/pki-private/` and `*.pfx` to `.gitignore`. During CI builds, if `CODESIGN_CA_PRIVATE_KEY_PEM` secret is configured, use it to issue a 90-day leaf certificate; otherwise generate an ephemeral build-scoped sovereign CA + 90-day leaf certificate and attach its public verification bundle alongside the release artifacts.

### 2.2 Scripts & CLI Contracts
1. `scripts/pki/provision-or-rotate-codesign.sh --out-dir <dir> [--check-expiry-seconds 2592000]`:
   - Inspects existing `nanoweaver-codesign-leaf.crt` if present.
   - If missing or expiring within 30 days (`2592000` seconds), mints a fresh 90-day `id-kp-codeSigning` certificate, PKCS#12 `.pfx`, and GPG signing key.
   - Verifies `openssl verify -purpose codesign` exits `0`.
2. `scripts/pki/sign-packages.sh --artifacts-dir <dir> --pki-dir <dir>`:
   - Signs all present `.deb`, `.rpm`, `.pkg.tar.zst`, `.exe`, `.msix`, `.dmg`/`.app.tar.gz`, `.apk`, and `.ipa` packages, generates `SHA256SUMS` and `SHA256SUMS.sig`, and verifies every signature.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Update `.gitignore` to exclude private PKI material (`artifacts/pki-private/`, `*.pfx`, `*.p12`, `*.jks`, `*.keystore`).
2. **Task 2**: Author `scripts/pki/provision-or-rotate-codesign.sh` and `scripts/pki/sign-packages.sh` with `set -euo pipefail` and `# WHAT: / # WHY: / # HOW:` comments.
3. **Task 3**: Generate the initial public trust anchors in `docs/assets/keys/` (`nanoweaver-codesign-root-ca.crt`, `nanoweaver-codesign-leaf.crt`, `nanoweaver-archive-keyring.asc`) and verify `openssl x509 -in docs/assets/keys/nanoweaver-codesign-leaf.crt -noout -ext extendedKeyUsage` contains `Code Signing`.
4. **Task 4**: Author `.forgejo/workflows/rotate-codesign-certs.yml` and `.github/workflows/rotate-codesign-certs.yml` with `schedule` (`0 3 1 * *`) and `workflow_dispatch`.
5. **Task 5**: Run `./scripts/verify-all.sh` to confirm zero regressions.

## 4. Verification Gate & Acceptance Criteria
- [ ] `scripts/pki/provision-or-rotate-codesign.sh` passes `openssl verify` and confirms `Code Signing` EKU.
- [ ] Zero private keys exist in `git status`.
- [ ] `./scripts/verify-all.sh` passes all 8 gates.
