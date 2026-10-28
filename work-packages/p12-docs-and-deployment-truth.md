# Work package P12 — Documentation and deployment truth pass

> **Why this file is under `work-packages/`.** Gate step 5 scans `docs/**/*.md`, `docs/*.md` and `README.md` for paired single-`$` tokens. `work-packages/` is version-controlled and outside that scan. **Do not move this file under `docs/`.**

**Agent:** coder (suitable for lighter/smaller models: `flash`, `flash_lite`, Haiku, Sonnet).
**Dependencies:** P7.4, P7.5, P8, P9 (all landed on `main`).

---

## Recorded User Decisions (2026-10-27)

1. **Dedicated Node-Key Mount Default (`A3`):** Set `WEAVER_NODE_KEYS_DIR=/workspace/node-keys` and mount `./simulations/node-keys:/workspace/node-keys:rw` by default in `docker-compose.yml`, add `/workspace/node-keys` to `mkdir`, `chown`, and `VOLUME` in `docker/web/Dockerfile.web`, and track `simulations/node-keys/.gitkeep` (with `simulations/node-keys/*` and `!simulations/node-keys/.gitkeep` in `.gitignore`) so a fresh clone starts cleanly without root-owned bind-mount permission errors.
2. **Release Notes Location (`A4`):** Create a dedicated `docs/release-notes.md` (and link it from `docs/_sidebar.md` and `docs/index.md`) covering the P2 `login-key` transport boundary fix, P5/P6/P7.4/P7.5 PHI & key persistence changes, P8/P9 dataset signing & strict mode, and the RFC 7807 `500` corrupt-ciphertext contract.

> **Pre-verified already landed (do not re-do):**
> - **L3** (`docs/example-workflows.md:142,153,156` and `docs/configuration-peers.md:102-110`) already point at `https://lab-a.hrt-lab.local` with the port-5000 warning callout.
> - `.gitignore` already ignores `package-lock.json` with a comment (`c2b118c`).

---

## Sizing & Lighter-Model Execution Rules

- **Single self-contained stage (~180 lines across 9 documentation/config files, 0 C# changes).**
- **Directive 8 (Markdown Invariant):** Never use paired single-`$` tokens around physical units, chemical formulas, or environment variables (`$VAR`) in any file under `docs/` or `README.md`. Always wrap `$VAR` inside backticks (`` `$VAR` ``) so Gate Step 5 stays at `0` errors.

---

## Files in Scope

| Path | Role |
| :--- | :--- |
| `docs/compliance-and-security.md` | (1) At `:13` and `:243`, remove `P-256` references (`grep -ri "P-256" docs/ README.md` must return 0 matches per Directive 12); (2) At `:78-80`, replace the stale `node_storage_master` / `node_signing_master` description with the persisted node key files (`token_signing.key`, `phi_master.key`, `node_signing_mldsa65.key`, `dataset_signing_ed25519.key`) and `weaver_subject_keys` / `.weaver-subject-erasure.jsonl`; (3) At `:114` in `docs/configuration-peers.md`, note that dataset export manifests are Ed25519-signed while encrypted health payloads use ML-DSA-65. |
| `docs/configuration-peers.md` | At `:114`, change `"Ed25519 or ML-DSA-65 manifest signatures"` to `"Ed25519 manifest signatures"` so it matches P8/P9 (`DatasetManifestSignatureVerifier`). |
| `docs/api-reference.md` | At `:664-677`, add `* **Authorization**: Requires \`Administrator\` or \`Researcher\` role (\`Patient\` receives \`403 Forbidden\`).` to both `GET /api/services` and `POST /api/services/ping/{serviceId}`. |
| `example.env` | Document `WEAVER_NODE_KEYS_DIR`, `WEAVER_TOKEN_SIGNING_KEY`, `WEAVER_POSTGRES_PASSWORD`, `SECURITY_PEPPER` (`Security:ProfilePepper`), and `POSTGRES_CONNECTION_STRING` (noting its deliberately empty default for local SQLite). |
| `docker-compose.yml` | Update header comment (`:18`), set `WEAVER_NODE_KEYS_DIR=/workspace/node-keys` (`:141`), and enable `- ./simulations/node-keys:/workspace/node-keys:rw` (`:147-153`). |
| `docker/web/Dockerfile.web` | Add `/workspace/node-keys` to `RUN mkdir -p ... && chown 1000:1000 ...` (`:60-61`) and `VOLUME` (`:64`). |
| `simulations/node-keys/.gitkeep` (new) | Empty `.gitkeep` file so Git creates `./simulations/node-keys` owned by the cloning user before `docker compose up`. |
| `.gitignore` | Add `simulations/node-keys/*` and `!simulations/node-keys/.gitkeep`. |
| `docs/release-notes.md` (new), `docs/_sidebar.md`, `docs/index.md` | Publish Phase 2 security & architecture release notes and link from sidebar/index. |

---

## Exact Implementation Changes

### 1. `docs/compliance-and-security.md`

- **Line 13**: Replace `(RSA-2048, ECDSA, Diffie-Hellman)` (removing `P-256` so `grep -ri "P-256" docs/ README.md` returns 0 matches).
- **Lines 78–80**: Replace with:
  ```markdown
  1. **Node Secret Files (`WEAVER_NODE_KEYS_DIR`)**: Owner-only (`0600`) persisted secrets generated on first launch:
     - `token_signing.key`: 256-bit HMAC-SHA256 bearer token signing key.
     - `phi_master.key`: 256-bit AES-256-GCM master key sealing patient PHI and the subject key vault.
     - `node_signing_mldsa65.key`: 32-byte FIPS 204 ML-DSA-65 seed signing the FDA 21 CFR Part 11 audit ledger (`KeyId` = lowercase hex SHA-256 of the 1,952-byte public key).
     - `dataset_signing_ed25519.key`: 32-byte Ed25519 seed signing `.tar.gz` dataset export manifests (`KeyId` = lowercase hex SHA-256 of the 32-byte public key).
  2. **Persisted Subject Key Profiles (`weaver_subject_keys` & `.weaver-subject-erasure.jsonl`)**: Dedicated ML-KEM-768 key pairs generated per subject (`sub_...`), sealed at rest under `phi_master.key` with HKDF-SHA256 and subject-ID AAD binding, and tracked by an append-only fsynced GDPR Article 17 erasure journal.
  ```
- **Line 243**: Replace `Ed25519 or NIST P-256 keys` with `Ed25519 or NIST FIPS 204 ML-DSA-65 keys`.

### 2. `example.env`

Append documented sections for:
```dotenv
# Optional override for the directory where the web node stores its 0600 secret key files
# (token_signing.key, phi_master.key, node_signing_mldsa65.key, dataset_signing_ed25519.key).
# WEAVER_NODE_KEYS_DIR=/workspace/node-keys

# Optional base64 32-byte bearer-token HMAC key override (auto-generated in WEAVER_NODE_KEYS_DIR if unset).
# WEAVER_TOKEN_SIGNING_KEY=

# Optional base64 32-byte PHI master encryption key override (mapped to Security:ProfilePepper; auto-generated in WEAVER_NODE_KEYS_DIR if unset).
# SECURITY_PEPPER=

# PostgreSQL connection string. Deliberately empty by default so local single-node deployments use
# the embedded SQLite database. When set, the node requires PostgreSQL to be reachable and fails
# loudly rather than silently falling back to SQLite.
# POSTGRES_CONNECTION_STRING=

# Password for the optional pg-duckdb container in docker-compose.infra.yml (required when using --profile infra).
# WEAVER_POSTGRES_PASSWORD=
```

### 3. `docker-compose.yml` & `docker/web/Dockerfile.web`

- In `docker/web/Dockerfile.web:60-64`:
  ```dockerfile
  RUN mkdir -p /workspace/data /workspace/exports /workspace/parquet /workspace/node-keys \
      && chown 1000:1000 /workspace /workspace/data /workspace/exports /workspace/parquet /workspace/node-keys

  # Mount points for shared simulation streams, cryptographic dataset exports, and isolated node keys
  VOLUME ["/workspace/data", "/workspace/exports", "/workspace/node-keys"]
  ```
- In `docker-compose.yml:141-153`:
  ```yaml
      - WEAVER_NODE_KEYS_DIR=/workspace/node-keys
    volumes:
      - ./simulations/configs:/workspace/configs:ro
      - ./simulations/shared-data:/workspace/data:rw
      - ./simulations/exports:/workspace/exports:rw
      - ./simulations/parquet:/workspace/parquet:rw
      - ./simulations/node-keys:/workspace/node-keys:rw
  ```

---

## Acceptance Criteria

1. `grep -ri "P-256" docs/ README.md` returns 0 matches.
2. `grep -ri "node_storage_master" docs/` returns 0 matches.
3. Every variable (`WEAVER_NODE_KEYS_DIR`, `WEAVER_TOKEN_SIGNING_KEY`, `WEAVER_POSTGRES_PASSWORD`, `SECURITY_PEPPER`, `POSTGRES_CONNECTION_STRING`) is documented in `example.env`.
4. `simulations/node-keys/.gitkeep` exists and `.gitignore` ignores `simulations/node-keys/*` except `!simulations/node-keys/.gitkeep`.
5. `docs/release-notes.md` exists and is linked from `docs/_sidebar.md` and `docs/index.md`.
6. `./scripts/verify-all.sh` passes all 7 steps (specifically Step 5 Markdown scan with 0 errors).
