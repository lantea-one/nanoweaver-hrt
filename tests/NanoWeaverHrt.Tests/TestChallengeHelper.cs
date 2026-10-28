using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Helper utilities for executing challenge-response authentication in unit and integration tests.
/// </summary>
public static class TestChallengeHelper
{
    /// <summary>
    /// Extracts raw DER bytes from a PEM string, decrypting with the specified passphrase if encrypted.
    /// </summary>
    /// <param name="privateKeyPem">The PEM-encoded private key string.</param>
    /// <param name="passphrase">Optional passphrase if the PEM envelope is encrypted.</param>
    /// <returns>Decoded and decrypted raw DER bytes.</returns>
    public static byte[] ExtractDerFromPem(string privateKeyPem, string? passphrase = null)
    {
        // WHAT: Strips PEM boundary framing and decrypts PBKDF2/AES-GCM envelope if encrypted.
        // WHY:  Test helper needs the raw DER bytes to instantiate BouncyCastle signers.
        // HOW:  Decodes Base64; if header has ENCRYPTED, extracts salt, nonce, tag, ciphertext and decrypts with PBKDF2-derived key.
        string clean = privateKeyPem.Trim();
        bool isEncrypted = clean.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase);

        string[] lines = clean.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        StringBuilder base64Sb = new();
        foreach (string line in lines)
        {
            string t = line.Trim();
            if (t.StartsWith("-----") && t.EndsWith("-----"))
            {
                continue;
            }

            base64Sb.Append(t);
        }

        byte[] derBytes = Convert.FromBase64String(base64Sb.ToString());
        if (!isEncrypted)
        {
            return derBytes;
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new InvalidOperationException("Encrypted private key requires a passphrase to decrypt.");
        }

        if (derBytes.Length < 44)
        {
            throw new InvalidOperationException("Encrypted key envelope is truncated (< 44 bytes).");
        }

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        int cipherLen = derBytes.Length - 44;
        byte[] ciphertext = new byte[cipherLen];

        Buffer.BlockCopy(derBytes, 0, salt, 0, 16);
        Buffer.BlockCopy(derBytes, 16, nonce, 0, 12);
        Buffer.BlockCopy(derBytes, 28, tag, 0, 16);
        Buffer.BlockCopy(derBytes, 44, ciphertext, 0, cipherLen);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] plaintext = new byte[cipherLen];
        try
        {
            using AesGcm aesGcm = new(key, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData: salt);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Computes a digital signature over an authentication challenge nonce using a PEM-encoded private key.
    /// </summary>
    /// <param name="privateKeyPem">The PEM-encoded private key string.</param>
    /// <param name="nonceBase64">Base64-encoded challenge nonce string.</param>
    /// <param name="algorithm">The cryptographic algorithm (Ed25519 or ML-DSA-65).</param>
    /// <param name="passphrase">Optional passphrase if the PEM envelope is encrypted.</param>
    /// <returns>Base64-encoded digital signature string.</returns>
    public static string SignNonceWithPem(
        string privateKeyPem,
        string nonceBase64,
        KeyAlgorithm algorithm = KeyAlgorithm.Ed25519,
        string? passphrase = null)
    {
        // WHAT: Signs nonceBase64 using BouncyCastle Ed25519 or PostQuantumCryptoProvider.
        // WHY:  Provides client-side signature generation in integration tests.
        // HOW:  Extracts DER, decodes nonceBase64 to byte[], and generates signature.
        byte[] derBytes = ExtractDerFromPem(privateKeyPem, passphrase);
        byte[] nonceBytes = Convert.FromBase64String(nonceBase64);

        if (algorithm == KeyAlgorithm.Ed25519)
        {
            var privKey = (Ed25519PrivateKeyParameters)PrivateKeyFactory.CreateKey(derBytes);
            Ed25519Signer signer = new();
            signer.Init(true, privKey);
            signer.BlockUpdate(nonceBytes, 0, nonceBytes.Length);
            byte[] sig = signer.GenerateSignature();
            return Convert.ToBase64String(sig);
        }
        else
        {
            PostQuantumCryptoProvider pqcProvider = new();
            string privBase64 = Convert.ToBase64String(derBytes);
            PqcSignature sig = pqcProvider.SignData(nonceBytes, privBase64, "challenge-test");
            return sig.SignatureBase64;
        }
    }

    /// <summary>
    /// Executes a full challenge-response sign-in sequence against the API.
    /// </summary>
    /// <param name="client">The HTTP client to perform requests against.</param>
    /// <param name="username">The account username requesting sign-in.</param>
    /// <param name="privateKeyPem">The user's PEM-encoded private key.</param>
    /// <param name="algorithm">The key algorithm (Ed25519 or ML-DSA-65).</param>
    /// <param name="useWebTokenEndpoint">True to exchange signature at /api/auth/web-token; false for /api/auth/token.</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <returns>The HTTP response message from the token exchange endpoint.</returns>
    public static async Task<HttpResponseMessage> LoginViaChallengeAsync(
        HttpClient client,
        string username,
        string privateKeyPem,
        KeyAlgorithm algorithm = KeyAlgorithm.Ed25519,
        bool useWebTokenEndpoint = true,
        string? passphrase = null)
    {
        // WHAT: Requests a single-use challenge nonce, signs it, and exchanges for token.
        // WHY:  Standard challenge-response authentication handshake (Audit C8).
        // HOW:  POSTs /api/auth/challenge, calls SignNonceWithPem, then POSTs to /api/auth/web-token or /api/auth/token.
        HttpResponseMessage chalRes = await client.PostAsJsonAsync("/api/auth/challenge", new ChallengeRequest { Username = username });
        if (!chalRes.IsSuccessStatusCode)
        {
            return chalRes;
        }

        AuthChallenge? chal = await chalRes.Content.ReadFromJsonAsync<AuthChallenge>();
        if (chal == null)
        {
            throw new InvalidOperationException("Failed to deserialize AuthChallenge from /api/auth/challenge.");
        }

        string signatureBase64 = SignNonceWithPem(privateKeyPem, chal.NonceBase64, algorithm, passphrase);
        string endpoint = useWebTokenEndpoint ? "/api/auth/web-token" : "/api/auth/token";

        TokenExchangeRequest exchangeReq = new()
        {
            Username = username,
            ChallengeId = chal.ChallengeId,
            SignatureBase64 = signatureBase64
        };

        return await client.PostAsJsonAsync(endpoint, exchangeReq);
    }

    /// <summary>
    /// Executes challenge-response sign-in and extracts the resulting JWT bearer token.
    /// </summary>
    /// <param name="client">The HTTP client to perform requests against.</param>
    /// <param name="username">The account username requesting sign-in.</param>
    /// <param name="privateKeyPem">The user's PEM-encoded private key.</param>
    /// <param name="algorithm">The key algorithm (Ed25519 or ML-DSA-65).</param>
    /// <param name="useWebTokenEndpoint">True to exchange signature at /api/auth/web-token; false for /api/auth/token.</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <returns>The JWT bearer token string.</returns>
    public static async Task<string> ExchangeChallengeTokenAsync(
        HttpClient client,
        string username,
        string privateKeyPem,
        KeyAlgorithm algorithm = KeyAlgorithm.Ed25519,
        bool useWebTokenEndpoint = true,
        string? passphrase = null)
    {
        // WHAT: Calls LoginViaChallengeAsync and deserializes the bearer token.
        // WHY:  Convenience helper for tests needing an authenticated Authorization header.
        // HOW:  Asserts 200 OK and reads AuthTokenResponse.Token.
        HttpResponseMessage response = await LoginViaChallengeAsync(
            client,
            username,
            privateKeyPem,
            algorithm,
            useWebTokenEndpoint,
            passphrase);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Challenge sign-in failed with status {response.StatusCode}: {errorContent}");
        }

        AuthTokenResponse? auth = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        if (auth == null || string.IsNullOrEmpty(auth.Token))
        {
            throw new InvalidOperationException("Authentication response was missing a valid bearer token.");
        }

        return auth.Token;
    }

    /// <summary>
    /// Generates a cryptographic keypair bundle locally for test assertions, without server intervention.
    /// </summary>
    /// <param name="username">The username for the test bundle.</param>
    /// <param name="role">The role assigned to the identity.</param>
    /// <param name="gate">The access gate constraint.</param>
    /// <param name="algorithm">The key algorithm (Ed25519 or ML-DSA-65).</param>
    /// <param name="passphrase">Optional passphrase to encrypt the private key envelope.</param>
    /// <returns>A populated <see cref="GeneratedKeyBundle"/> ready for test registration.</returns>
    public static GeneratedKeyBundle CreateTestKeyBundle(
        string username,
        UserRole role = UserRole.Researcher,
        AccessGate gate = AccessGate.ApiWeb,
        KeyAlgorithm algorithm = KeyAlgorithm.Ed25519,
        string? passphrase = null)
    {
        // WHAT: Derives Ed25519 or ML-DSA-65 keypair, optionally encrypts private key with passphrase, and formats PEMs.
        // WHY:  Eliminates dependency on server-side private key generation endpoints in test fixtures (Phase P20).
        // HOW:  Uses BouncyCastle generators, formats SPKI/PKCS#8 DER, and encrypts envelope if passphrase is provided.
        SecureRandom random = new();
        byte[] pubDer;
        byte[] privDer;

        if (algorithm == KeyAlgorithm.Ed25519)
        {
            Ed25519KeyPairGenerator keyGen = new();
            keyGen.Init(new Ed25519KeyGenerationParameters(random));
            AsymmetricCipherKeyPair pair = keyGen.GenerateKeyPair();
            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        }
        else
        {
            MLDsaKeyPairGenerator keyGen = new();
            keyGen.Init(new MLDsaKeyGenerationParameters(random, MLDsaParameters.ml_dsa_65));
            AsymmetricCipherKeyPair pair = keyGen.GenerateKeyPair();
            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        }

        string pubBase64 = Convert.ToBase64String(pubDer);
        string pubPem = FormatPem("PUBLIC KEY", pubDer);
        string privPem;
        bool isEncrypted = false;

        if (!string.IsNullOrWhiteSpace(passphrase))
        {
            byte[] encBytes = EncryptPrivateKeyBytes(privDer, passphrase);
            privPem = FormatPem("ENCRYPTED PRIVATE KEY", encBytes);
            isEncrypted = true;
        }
        else
        {
            privPem = FormatPem("PRIVATE KEY", privDer);
        }

        return new GeneratedKeyBundle
        {
            Username = username.Trim().ToLowerInvariant(),
            Role = role,
            AccessGate = gate,
            Algorithm = algorithm,
            PublicKeyBase64 = pubBase64,
            PublicKeyPem = pubPem,
            PrivateKeyPem = privPem,
            IsPasswordProtected = isEncrypted
        };
    }

    /// <summary>
    /// Encrypts raw private key DER bytes into the standard [Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N] envelope.
    /// </summary>
    /// <param name="privateKeyDer">Raw private key DER bytes.</param>
    /// <param name="passphrase">User-entered encryption passphrase.</param>
    /// <returns>Full encrypted envelope byte array.</returns>
    public static byte[] EncryptPrivateKeyBytes(byte[] privateKeyDer, string passphrase)
    {
        // WHAT: Encrypts DER bytes using AES-256-GCM with a PBKDF2-SHA256 derived key (100,000 iterations).
        // WHY:  Standard envelope layout matching client and server encryption formats.
        // HOW:  Fills random salt and nonce, derives key via PBKDF2, encrypts via AesGcm, and concatenates bytes.
        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] ciphertext = new byte[privateKeyDer.Length];
        byte[] tag = new byte[16];

        using (AesGcm aesGcm = new(key, 16))
        {
            aesGcm.Encrypt(nonce, privateKeyDer, ciphertext, tag, associatedData: salt);
        }

        byte[] output = new byte[salt.Length + nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, output, 0, 16);
        Buffer.BlockCopy(nonce, 0, output, 16, 12);
        Buffer.BlockCopy(tag, 0, output, 28, 16);
        Buffer.BlockCopy(ciphertext, 0, output, 44, ciphertext.Length);

        CryptographicOperations.ZeroMemory(key);
        return output;
    }

    /// <summary>
    /// Formats DER bytes into standard RFC 7468 PEM text with 64-character lines.
    /// </summary>
    /// <param name="type">The label for the header and footer (e.g., PUBLIC KEY, PRIVATE KEY).</param>
    /// <param name="derBytes">Raw bytes to encode as Base64.</param>
    /// <returns>Formatted PEM string with newline terminator.</returns>
    public static string FormatPem(string type, byte[] derBytes)
    {
        // WHAT: Encodes bytes to Base64 and wraps in 64-character lines surrounded by PEM delimiters.
        // WHY:  Standard PEM rendering for cryptographic key material.
        // HOW:  Uses Convert.ToBase64String and StringBuilder chunks of 64 characters.
        string b64 = Convert.ToBase64String(derBytes);
        StringBuilder sb = new();
        sb.Append("-----BEGIN ").Append(type).Append("-----\n");

        for (int i = 0; i < b64.Length; i += 64)
        {
            int length = Math.Min(64, b64.Length - i);
            sb.Append(b64.AsSpan(i, length)).Append('\n');
        }

        sb.Append("-----END ").Append(type).Append("-----\n");
        return sb.ToString();
    }
}
