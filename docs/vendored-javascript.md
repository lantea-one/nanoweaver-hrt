# Vendored Third-Party JavaScript Inventory & Integrity Register

> **Sovereign Zero-CDN Policy & Subresource Integrity (SRI) Verification Register**

In accordance with **Prime Directive 9** and **Prime Directive 12**, NanoWeaverHrt strictly prohibits loading external scripts, styles, or cryptographic dependencies from third-party Content Delivery Networks (CDNs), external clouds, or unpinned remote sources.

All client-side cryptographic primitives and vendor libraries are audited, vendor-bundled, committed to the authoritative sovereign repository under `src/NanoWeaverHrt/wwwroot/lib/`, and verified on every pre-flight gate check via **Gate Step 8** (`scripts/verify-vendored-js.sh`).

---

## 1. Vendored Libraries Register

### `weaver-crypto-vendor.js`

- **File Path**: `src/NanoWeaverHrt/wwwroot/lib/weaver-crypto-vendor.js`
- **Bundle Format**: Standalone zero-dependency ES module bundle exposing `window.WeaverCryptoVendor` and `export` symbols
- **License**: MIT License (Paul Miller / noble-crypto contributors)
- **SHA-256 Digest**:
  ```
  a110817fa872155227a1011bd48e532ef4b93e409755f39bb416b942492e21d7
  ```
- **SHA-384 (SRI) Digest**:
  ```
  sha384-2PWlMHMrV/enpeAeEB0EtHqLjYv9Rql4aPLZNGynBOQPFkoacC01+5WwbCsr8lgD
  ```
- **Upstream Component Packages**:
  1. `@noble/ed25519` (Version: `3.2.0`): RFC 8032 Ed25519 high-assurance digital signature implementation.
  2. `@noble/hashes` (Version: `2.4.0`): NIST FIPS 180-4 SHA-512 cryptographic hash engine powering Ed25519.
  3. `@noble/post-quantum` (Version: `0.7.1`): NIST FIPS 204 ML-DSA-65 post-quantum digital signature algorithm.
  4. Native WebCrypto PBKDF2-SHA256 and AES-256-GCM envelope decryption adapter.

---

## 2. Security Audit & Human Review Record

- **Review Date**: 2026-10-28
- **Auditor**: Principal Systems Architect & Computational Nanotechnologist
- **Zero-CDN Verification**: The bundle has zero runtime network requests (`fetch`, `XMLHttpRequest`, `WebSocket`, `navigator.sendBeacon`) to any external hosts.
- **Zero-Eval Verification**: The bundle contains no dynamic code generation (`eval`, `new Function`, `setTimeout(string)`).
- **Zero-Telemetry Verification**: No tracking, phone-home, diagnostic pings, or analytics collection.
- **Memory Safety & Zeroization**: All sensitive private key material, expanded lattice matrices, and intermediate hash digests are processed in bounded TypedArrays and zeroized after challenge signature completion via `zeroizeBytes()`.

---

## 3. Automated Integrity Verification

Automated gate step `scripts/verify-vendored-js.sh` runs as Step 8 of `scripts/verify-all.sh`. It enforces:
1. Exact match of SHA-256 and SHA-384 hashes against this register.
2. Exact match of `integrity="sha384-..."` attributes in `src/NanoWeaverHrt/Components/App.razor`.
3. Strict absence of unapproved external CDN script tags in Blazor templates.
4. Execution of RFC 8032 and ML-DSA-65 known-answer test vectors in pure Node.js before deployment.
