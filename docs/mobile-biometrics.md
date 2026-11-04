# Native Mobile Biometrics & Hardware-Backed Secure Enclave Vault

> **Hardware-Isolated Cryptographic Vault Decryption and Automated Memory Zeroization for Sovereign Android and iOS Clients**

---

## 1. Overview & Security Imperative

NanoWeaverHrt mobile clients (Android and iOS/iPadOS) allow patients and clinicians to view personalized DNA origami nanocarrier regimens, monitor endocrine transitions, and review sensitive pharmacogenomic risk markers. Because this data constitutes Protected Health Information (PHI) under HIPAA and GDPR Article 9 special category personal data, storing decrypted regimen manifests in plaintext on mobile flash memory or leaving decrypted buffers in process RAM poses critical physical extraction risks.

Work Package P59 delivers **Native Mobile Biometrics & Hardware-Backed Secure Enclave Vault Decryption**:
1. Private cryptographic secrets and vault decryption keys are generated inside and sealed by hardware-isolated security modules (Android Keystore StrongBox and Apple Secure Enclave).
2. Unlocking the vault requires user biometric verification (Android `BiometricPrompt` with Class 3 biometrics; iOS `LocalAuthentication` with Face ID or Touch ID).
3. The unified vault manager actively monitors OS application lifecycle transitions (`OnPause`, `DidEnterBackground`), executing immediate cryptographic zeroization (`CryptographicOperations.ZeroMemory`) on all plaintext in-memory arrays.

---

## 2. Platform Architecture

```
+-----------------------------------------------------------------------------------+
|                        Mobile Biometric Security Topology                         |
+-----------------------------------------------------------------------------------+

     +-------------------------------------------------------------------------+
     |                       UnifiedMobileBiometricVault                       |
     |  - Coordinates active provider based on runtime platform detection      |
     |  - Monitors MobileAppLifecycleState (Resumed, Paused, Backgrounded)     |
     |  - Automatic Zeroization: CryptographicOperations.ZeroMemory            |
     +--------------------+-------------------------------+--------------------+
                          |                               |
             Android Host |                               | Apple iOS / iPadOS
                          v                               v
     +----------------------------------+   +----------------------------------+
     | AndroidBiometricSecurityProvider |   | AppleSecureEnclaveSecurityProv.  |
     | - AndroidX BiometricPrompt       |   | - LocalAuthentication (LAContext)|
     | - AndroidKeyStore StrongBox      |   | - Apple Secure Enclave Keychain  |
     | - Hardware-Bound Ed25519 Keys    |   | - kSecAttrAccessControlBiometry  |
     +----------------------------------+   +----------------------------------+
```

### 2.1 Android Keystore & BiometricPrompt
- Uses `KeyGenParameterSpec.Builder` with `PURPOSE_SIGN | PURPOSE_DECRYPT`.
- Sets `setUserAuthenticationRequired(true)` with `AUTH_BIOMETRIC_STRONG` (Class 3 biometric sensors: optical/ultrasonic in-display fingerprint and 3D facial mapping).
- In simulated test modes, injectable delegates allow 100% deterministic testing across `Available`, `NotEnrolled`, `HardwareUnavailable`, `Lockout`, and `UserCancelled` outcomes.

### 2.2 Apple Secure Enclave & LocalAuthentication
- Uses `LAContext` with policy `deviceOwnerAuthenticationWithBiometrics`.
- Keypair generation binds private keys to the hardware Secure Enclave via `kSecAttrTokenIDSecureEnclave` and access control `kSecAccessControlBiometryAny`.
- Private keys can never be exported or read by the operating system kernel; signing and decryption operations execute strictly inside the isolated coprocessor.

### 2.3 Automated Memory Zeroization Lifecycle
When the user switches apps, receives a phone call, or locks the screen:
1. The Avalonia mobile head detects the lifecycle transition (`Paused` or `Backgrounded`).
2. `UnifiedMobileBiometricVault.OnAppLifecycleStateChanged` triggers immediately.
3. Every cached byte array holding patient DNA sequences, regimen parameters, or decrypted credentials is systematically overwritten with zeros:
   ```csharp
   CryptographicOperations.ZeroMemory(cachedPayloadBytes);
   ```
4. All references are purged and garbage collected, rendering physical memory scraping attacks ineffective.

---

## 4. `IAndroidKeyStoreBridge` & `IAppleSecureEnclaveBridge` Lifecycle Auto-Lock
- **`IAndroidKeyStoreBridge` & `IAppleSecureEnclaveBridge`**: Dedicated hardware abstraction interfaces linking Android Keystore StrongBox and Apple Secure Enclave coprocessors to platform Ed25519 authentication flows.
- **Lifecycle Auto-Lock & Zeroization**: Automated background monitoring immediately invokes `CryptographicOperations.ZeroMemory` on session vault buffers upon application pausing or backgrounding, ensuring uncompromised mobile data security.
