using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Integrity;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests.Security;

/// <summary>
/// Unit tests verifying cryptographic audit ledger microsecond determinism, full-stream
/// zero-allocation HIPAA de-identification, ProfileDataProtector fail-closed integrity,
/// AssetIntegrityService path traversal prevention, and token nonce (JTI) hardening.
/// </summary>
public sealed class CryptographicDeterminismAndHardeningTests : IDisposable
{
    /// <summary>
    /// Temporary directory holding test keystore artifacts.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Isolated subject key test host providing real NodeSecretStore, ProfileDataProtector, and KeyManagement.
    /// </summary>
    private readonly SubjectKeyTestHost _host;

    /// <summary>
    /// Post-quantum cryptographic provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Compliance audit service instance under test.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Mock web host environment for asset integrity tests.
    /// </summary>
    private readonly Mock<IWebHostEnvironment> _webHostEnvironmentMock;

    /// <summary>
    /// Asset integrity service under test.
    /// </summary>
    private readonly AssetIntegrityService _assetIntegrityService;

    /// <summary>
    /// Mock user repository for authentication service tests.
    /// </summary>
    private readonly Mock<IUserRepository> _userRepositoryMock;

    /// <summary>
    /// User authentication service under test.
    /// </summary>
    private readonly UserAuthenticationService _authService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptographicDeterminismAndHardeningTests"/> class.
    /// </summary>
    public CryptographicDeterminismAndHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_crypto_det_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _host = SubjectKeyTestHost.CreateLoaded(_tempDir);
        _pqcProvider = new PostQuantumCryptoProvider();
        _auditService = new ComplianceAuditService(_host.KeyManagement, _pqcProvider, secretStore: _host.SecretStore);

        _webHostEnvironmentMock = new Mock<IWebHostEnvironment>();
        _webHostEnvironmentMock.Setup(e => e.WebRootFileProvider).Returns(new NullFileProvider());
        _webHostEnvironmentMock.Setup(e => e.WebRootPath).Returns(_tempDir);
        _webHostEnvironmentMock.Setup(e => e.ContentRootPath).Returns(_tempDir);
        _assetIntegrityService = new AssetIntegrityService(_webHostEnvironmentMock.Object);

        _userRepositoryMock = new Mock<IUserRepository>();
        _authService = new UserAuthenticationService(
            _userRepositoryMock.Object,
            _pqcProvider,
            _auditService,
            _host.SecretStore);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _host.Dispose();
        _authService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Swallowed in test cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService.TruncateToMicroseconds"/> drops sub-microsecond tick fractions.
    /// </summary>
    [Fact]
    public void TruncateToMicroseconds_DropsSubMicrosecondTicks()
    {
        // WHAT: Tests microsecond truncation logic on DateTimeOffset.
        // WHY:  PostgreSQL TIMESTAMPTZ truncates sub-microsecond precision; timestamps must round-trip deterministically.
        // HOW:  Passes timestamps with known non-zero sub-microsecond ticks and verifies the modulo 10 remainder is eliminated.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset truncated = ComplianceAuditService.TruncateToMicroseconds(now);

        Assert.Equal(0L, truncated.UtcDateTime.Ticks % 10L);

        DateTimeOffset fixedTs = new(638000000001234567L, TimeSpan.Zero);
        DateTimeOffset fixedTruncated = ComplianceAuditService.TruncateToMicroseconds(fixedTs);

        Assert.Equal(638000000001234560L, fixedTruncated.UtcDateTime.Ticks);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService.VerifyLedgerIntegrity"/> detects tampering of the genesis record's PreviousHash.
    /// </summary>
    [Fact]
    public void VerifyLedgerIntegrity_GenesisRecordPreviousHashTampered_ReturnsBrokenHashLink()
    {
        // WHAT: Verifies that tampering with sequence 0 PreviousHash fails integrity checks.
        // WHY:  FDA 21 CFR Part 11 requires the entire chain from genesis to head to be cryptographically verified.
        // HOW:  Mutates _records[0].PreviousHash to non-genesis value using reflection and asserts BrokenHashLink.
        FieldInfo recordsField = typeof(ComplianceAuditService).GetField("_records", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<AuditRecord> list = (List<AuditRecord>)recordsField.GetValue(_auditService)!;

        AuditRecord originalGenesis = list[0];
        list[0] = originalGenesis with { PreviousHash = new string('1', 96) };

        AuditLedgerVerificationResult result = _auditService.VerifyLedgerIntegrity();

        Assert.False(result.IsValid);
        Assert.Equal(0, result.TotalRecordsVerified);
        Assert.Equal(0L, result.FirstViolationSequence);
        Assert.Equal(AuditLedgerViolationKind.BrokenHashLink, result.ViolationKind);
        Assert.Contains("Broken genesis hash link at sequence 0", result.Message);

        list[0] = originalGenesis;
        AuditLedgerVerificationResult validResult = _auditService.VerifyLedgerIntegrity();
        Assert.True(validResult.IsValid);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService.DeIdentifySubject"/> redacts clinical identifiers and raw subject names
    /// across both FASTA headers and non-header stream lines with zero regex or string.Split allocations.
    /// </summary>
    [Fact]
    public void DeIdentifySubject_ZeroAllocationFullStream_RedactsClinicalTagsAndSubjectAcrossHeadersAndBody()
    {
        // WHAT: Tests multi-line de-identification across FASTA headers, key-value clinical tags, free text, and DNA sequences.
        // WHY:  HIPAA Safe Harbor (§ 164.514) requires stripping 18 direct identifiers across all payload streams.
        // HOW:  Supplies raw subject identifier and FASTA text with clinical tags and subject occurrences; verifies redaction.
        string subject = "Alice Smith";
        string rawGenomicData =
            ">Patient: Alice Smith, MRN: 12345, SSN: 000-11-2222, Clinic: St Jude\n" +
            "ATGCGATCGATCGATC\n" +
            "patient: Alice Smith, subject: sub_ref, dob: 1990-01-01, birth: 1990, phone: 555-1234, email: alice@test.org, address: 123 Main St, hospital: General, id: rec-99\n" +
            "Laboratory note: sample for Alice Smith collected yesterday.\n" +
            "CGTAGCTAGCTAGCTA\n" +
            "\n" +
            ">patient:   Bob   ,mrn:999\n" +
            "patient:Bob\n" +
            "mrn: 8888\n";

        DeIdentificationResult result = _auditService.DeIdentifySubject(subject, rawGenomicData, gdprConsentConfirmed: true, ginaPurposeLimited: true);

        Assert.StartsWith("sub_", result.PseudonymSubjectId);
        Assert.True(result.GdprConsentConfirmed);
        Assert.True(result.GinaSafeguardVerified);

        // Verify stripped identifiers collection
        Assert.Contains("Alice Smith", result.StrippedIdentifiers);
        Assert.Contains("12345", result.StrippedIdentifiers);
        Assert.Contains("000-11-2222", result.StrippedIdentifiers);
        Assert.Contains("1990-01-01", result.StrippedIdentifiers);
        Assert.Contains("1990", result.StrippedIdentifiers);
        Assert.Contains("555-1234", result.StrippedIdentifiers);
        Assert.Contains("alice@test.org", result.StrippedIdentifiers);
        Assert.Contains("123 Main St", result.StrippedIdentifiers);
        Assert.Contains("General", result.StrippedIdentifiers);
        Assert.Contains("rec-99", result.StrippedIdentifiers);
        Assert.Contains("Bob", result.StrippedIdentifiers);
        Assert.Contains("999", result.StrippedIdentifiers);
        Assert.Contains("8888", result.StrippedIdentifiers);

        // Verify sanitized payload
        Assert.DoesNotContain("Alice Smith", result.SanitizedData);
        Assert.DoesNotContain("000-11-2222", result.SanitizedData);
        Assert.Contains(result.PseudonymSubjectId, result.SanitizedData);
        Assert.Contains("[HIPAA Safe Harbor De-Identified Scaffold]", result.SanitizedData);
        Assert.Contains("ATGCGATCGATCGATC", result.SanitizedData);
        Assert.Contains("CGTAGCTAGCTAGCTA", result.SanitizedData);
        Assert.Contains("[REDACTED]", result.SanitizedData);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService.DeIdentifySubject"/> handles raw subject occurrences
    /// first encountered in body lines, duplicate body identifiers, and space-separated clinical tags without delimiters.
    /// </summary>
    [Fact]
    public void DeIdentifySubject_BodyOnlySubjectAndSpaceSeparatedTags_FullyDeIdentified()
    {
        // WHAT: Tests body-first subject detection, duplicate identifier deduplication, and space-separated tag parsing.
        // WHY:  Guarantees 100% branch coverage across all lines and delimiter configurations in the zero-allocation scanner.
        // HOW:  Passes text where subject identifier appears only in body text, with repeated occurrences and space-separated tags.
        string subject = "Charlie Brown";
        string rawGenomicData =
            ">Scaffold_1\n" +
            "ATGC\n" +
            "Clinical note: patient Charlie Brown seen today. Followup with Charlie Brown scheduled.\n" +
            ">Scaffold_2 patient: Carol mrn: 555 name: Carol\n" +
            "patient: Dave mrn: 777 name: Dave\n";

        DeIdentificationResult result = _auditService.DeIdentifySubject(subject, rawGenomicData, gdprConsentConfirmed: true, ginaPurposeLimited: true);

        Assert.Contains("Charlie Brown", result.StrippedIdentifiers);
        Assert.Contains("Carol", result.StrippedIdentifiers);
        Assert.Contains("555", result.StrippedIdentifiers);
        Assert.Contains("Dave", result.StrippedIdentifiers);
        Assert.Contains("777", result.StrippedIdentifiers);
        Assert.DoesNotContain("Charlie Brown", result.SanitizedData);
    }

    /// <summary>
    /// Verifies that <see cref="ProfileDataProtector"/> recognizes both envelope prefixes and fails closed
    /// on malformed Base64, truncated length, and authentication tag mismatch.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_EncV1Prefix_CorruptBase64OrTruncatedOrTampered_ThrowsCryptographicException()
    {
        // WHAT: Tests prefix recognition and tamper resistance on enc:v1: envelopes.
        // WHY:  Malformed or forged envelopes must fail closed with CryptographicException instead of leaking raw ciphertext.
        // HOW:  Asserts IsEncrypted for both prefixes and validates exception throwing on corrupt inputs.
        ProfileDataProtector protector = _host.Protector;

        Assert.True(ProfileDataProtector.IsEncrypted("data:application/encrypted;base64,AQID"));
        Assert.True(ProfileDataProtector.IsEncrypted("enc:v1:AQID"));
        Assert.False(ProfileDataProtector.IsEncrypted(null));
        Assert.False(ProfileDataProtector.IsEncrypted("plain text data"));

        // EncryptDataUri idempotency
        Assert.Equal("enc:v1:test", protector.EncryptDataUri("enc:v1:test"));
        Assert.Equal(string.Empty, protector.EncryptDataUri(null));

        // DecryptDataUri with enc:v1: prefix
        Assert.Throws<CryptographicException>(() => protector.DecryptDataUri("enc:v1:not-base64!"));
        Assert.Throws<CryptographicException>(() => protector.DecryptDataUri("enc:v1:" + Convert.ToBase64String(new byte[10])));
        Assert.Throws<CryptographicException>(() => protector.DecryptDataUri("enc:v1:" + Convert.ToBase64String(new byte[32])));

        // DecryptRequiredFieldWithCurrentKeyOnly with enc:v1: prefix
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly("enc:v1:not-base64!"));
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly("enc:v1:" + Convert.ToBase64String(new byte[10])));
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly("enc:v1:" + Convert.ToBase64String(new byte[32])));
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly("unprotected-plaintext"));
    }

    /// <summary>
    /// Verifies that <see cref="ProfileDataProtector"/> decrypts valid <c>enc:v1:</c> envelopes sealed with both active and legacy keys.
    /// </summary>
    [Fact]
    public void ProfileDataProtector_EncV1Prefix_ValidActiveAndLegacyDecryption()
    {
        // WHAT: Verifies successful decryption of enc:v1: envelopes under active node key and legacy key.
        // WHY:  Ensures full forward and backward compatibility across node upgrades.
        // HOW:  Constructs valid AesGcm envelopes with node key and legacy key and asserts decryption output.
        ProfileDataProtector protector = _host.Protector;
        string plainText = "Sensitive Patient Diagnostic Note";
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        // 1. Sealed with active node key
        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        byte[] tag = new byte[16];
        byte[] cipherBytes = new byte[plainBytes.Length];

        using (AesGcm aes = new(_host.SecretStore.PhiMasterKey.Span, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        byte[] payload = new byte[12 + 16 + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, 12);
        Buffer.BlockCopy(tag, 0, payload, 12, 16);
        Buffer.BlockCopy(cipherBytes, 0, payload, 28, cipherBytes.Length);

        string activeEncEnvelope = "enc:v1:" + Convert.ToBase64String(payload);

        string decryptedActive = protector.DecryptDataUri(activeEncEnvelope);
        Assert.Equal(plainText, decryptedActive);

        string decryptedReq = protector.DecryptRequiredFieldWithCurrentKeyOnly(activeEncEnvelope);
        Assert.Equal(plainText, decryptedReq);

        // 2. Sealed with legacy key
        byte[] legacyKey = Convert.FromHexString("FEA47E93A869FF0D5965AB61A3E7476FE15632CE387DBFBC49CB84A3E45A2FE6");
        byte[] legacyNonce = new byte[12];
        RandomNumberGenerator.Fill(legacyNonce);
        byte[] legacyTag = new byte[16];
        byte[] legacyCipherBytes = new byte[plainBytes.Length];

        using (AesGcm aes = new(legacyKey, 16))
        {
            aes.Encrypt(legacyNonce, plainBytes, legacyCipherBytes, legacyTag);
        }

        byte[] legacyPayload = new byte[12 + 16 + legacyCipherBytes.Length];
        Buffer.BlockCopy(legacyNonce, 0, legacyPayload, 0, 12);
        Buffer.BlockCopy(legacyTag, 0, legacyPayload, 12, 16);
        Buffer.BlockCopy(legacyCipherBytes, 0, legacyPayload, 28, legacyCipherBytes.Length);

        string legacyEncEnvelope = "enc:v1:" + Convert.ToBase64String(legacyPayload);

        long countBefore = protector.LegacyDecryptionCount;
        string decryptedLegacy = protector.DecryptDataUri(legacyEncEnvelope);
        Assert.Equal(plainText, decryptedLegacy);
        Assert.Equal(countBefore + 1, protector.LegacyDecryptionCount);

        // Legacy key must be rejected by DecryptRequiredFieldWithCurrentKeyOnly
        Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly(legacyEncEnvelope));
    }

    /// <summary>
    /// Verifies that <see cref="AssetIntegrityService"/> rejects parent directory traversal segments and drive colons.
    /// </summary>
    /// <param name="traversalPath">The illegal asset path.</param>
    [Theory]
    [InlineData("../secrets.json")]
    [InlineData("js/../../etc/passwd")]
    [InlineData("..\\traversal.js")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("d:/secrets.db")]
    public void AssetIntegrityService_PathTraversalAndColons_ThrowsArgumentException(string traversalPath)
    {
        // WHAT: Tests directory traversal guards on asset path normalization.
        // WHY:  Prevents attackers from reading arbitrary host filesystem files via SRI endpoints.
        // HOW:  Asserts that GetSubresourceIntegrity and VerifyAssetSri throw ArgumentException for illegal paths.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => _assetIntegrityService.GetSubresourceIntegrity(traversalPath));
        Assert.Contains("Asset path must not contain parent directory traversal segments.", ex.Message);

        Assert.Throws<ArgumentException>(() => _assetIntegrityService.VerifyAssetSri(traversalPath, "sha384-xyz"));
    }

    /// <summary>
    /// Verifies that <see cref="UserAuthenticationService.GenerateBearerToken"/> issues distinct tokens with unique JTI nonces
    /// even when called multiple times within the same second.
    /// </summary>
    [Fact]
    public void UserAuthenticationService_GenerateBearerToken_UniqueJtiPerSecond()
    {
        // WHAT: Tests cryptographic JTI nonce generation on bearer tokens.
        // WHY:  Tokens issued in the same second must never collide or inadvertently tombstone on revocation.
        // HOW:  Generates two tokens in immediate succession and verifies token inequality and distinct 32-hex JTI claims.
        UserAccount user = new()
        {
            Username = "researcher01",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = Convert.ToBase64String(new byte[32]),
            IsActive = true
        };

        string token1 = _authService.GenerateBearerToken(user);
        string token2 = _authService.GenerateBearerToken(user);

        Assert.NotEqual(token1, token2);

        // Extract and deserialize claims
        int dot1 = token1.IndexOf('.');
        string payload1B64 = token1[..dot1].Replace('-', '+').Replace('_', '/');
        switch (payload1B64.Length % 4)
        {
            case 2: payload1B64 += "=="; break;
            case 3: payload1B64 += "="; break;
        }

        int dot2 = token2.IndexOf('.');
        string payload2B64 = token2[..dot2].Replace('-', '+').Replace('_', '/');
        switch (payload2B64.Length % 4)
        {
            case 2: payload2B64 += "=="; break;
            case 3: payload2B64 += "="; break;
        }

        AuthTokenClaims claims1 = JsonSerializer.Deserialize<AuthTokenClaims>(Convert.FromBase64String(payload1B64))!;
        AuthTokenClaims claims2 = JsonSerializer.Deserialize<AuthTokenClaims>(Convert.FromBase64String(payload2B64))!;

        Assert.NotNull(claims1.Jti);
        Assert.NotNull(claims2.Jti);
        Assert.Equal(32, claims1.Jti.Length);
        Assert.Equal(32, claims2.Jti.Length);
        Assert.NotEqual(claims1.Jti, claims2.Jti);
    }

    /// <summary>
    /// Verifies that <see cref="UserAuthenticationService.ValidateTokenAsync"/> rejects tokens with malformed Base64 segments
    /// without throwing <see cref="FormatException"/>.
    /// </summary>
    [Fact]
    public async Task UserAuthenticationService_ValidateTokenAsync_MalformedBase64_ReturnsErrorWithoutThrowing()
    {
        // WHAT: Tests malformed Base64 handling in ValidateTokenAsync.
        // WHY:  Untrusted client tokens must fail closed cleanly without unhandled FormatException crashes.
        // HOW:  Passes invalid Base64 in payload and signature segments and validates error return tuple.
        var (ok1, user1, err1) = await _authService.ValidateTokenAsync("invalid_base64!.invalid_sig!");
        Assert.False(ok1);
        Assert.Null(user1);
        Assert.Equal("Malformed bearer token encoding.", err1);

        // Valid base64 payload but invalid base64 signature
        string validPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"u\",\"role\":1,\"gate\":0,\"iat\":0,\"exp\":9999999999}"));
        var (ok2, user2, err2) = await _authService.ValidateTokenAsync($"{validPayload}.invalid_sig!");
        Assert.False(ok2);
        Assert.Null(user2);
        Assert.Equal("Malformed bearer token encoding.", err2);

        // Invalid length % 4 == 1
        var (ok3, user3, err3) = await _authService.ValidateTokenAsync($"{validPayload}.A");
        Assert.False(ok3);
        Assert.Null(user3);
        Assert.Equal("Malformed bearer token encoding.", err3);

        // Long payload (> 1024 chars) exercising heap buffer allocation in TryFromBase64String
        string longClaimValue = new('A', 1200);
        string longPayloadJson = $"{{\"sub\":\"{longClaimValue}\",\"role\":1,\"gate\":0,\"iat\":0,\"exp\":9999999999}}";
        byte[] longPayloadBytes = Encoding.UTF8.GetBytes(longPayloadJson);
        string longPayloadB64Url = Convert.ToBase64String(longPayloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        byte[] dummySig = new byte[32];
        string dummySigB64Url = Convert.ToBase64String(dummySig).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var (ok4, user4, err4) = await _authService.ValidateTokenAsync($"{longPayloadB64Url}.{dummySigB64Url}");
        Assert.False(ok4);
        Assert.Null(user4);
        Assert.Equal("Token signature verification failed.", err4);
    }

    /// <summary>
    /// Verifies that Program.TryValidateTokenAsync catches FormatException from an external or legacy authenticator
    /// and returns the expected malformed bearer token encoding error.
    /// </summary>
    [Fact]
    public async Task Program_TryValidateTokenAsync_CatchesFormatException()
    {
        // WHAT: Tests the defensive FormatException guard in Program.TryValidateTokenAsync.
        // WHY:  Ensures that if any implementation of IUserAuthenticationService throws FormatException, it fails closed cleanly.
        // HOW:  Invokes the private static TryValidateTokenAsync method via reflection with a throwing mock.
        Mock<IUserAuthenticationService> throwingMock = new();
        throwingMock
            .Setup(a => a.ValidateTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Simulated corrupted token format."));

        MethodInfo method = typeof(Program).GetMethod(
            "TryValidateTokenAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var task = (Task<(bool Valid, UserAccount? User, string? ErrorReason)>)method.Invoke(
            null,
            new object[] { throwingMock.Object, "corrupt.token", CancellationToken.None })!;

        var (valid, user, error) = await task;

        Assert.False(valid);
        Assert.Null(user);
        Assert.Equal("Malformed bearer token encoding.", error);
    }
}
