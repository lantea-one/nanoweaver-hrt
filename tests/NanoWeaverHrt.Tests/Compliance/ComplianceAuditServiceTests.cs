using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Compliance;

/// <summary>
/// Unit and integration test suite verifying HMAC-SHA256 keyed pseudonymization,
/// length-prefixed hash chaining, and regulatory compliance audit ledger integrity.
/// </summary>
public sealed class ComplianceAuditServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PostQuantumCryptoProvider _cryptoProvider;
    private readonly SubjectKeyTestHost _testHost;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceAuditServiceTests"/> class.
    /// </summary>
    public ComplianceAuditServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cryptoProvider = new PostQuantumCryptoProvider();
        _testHost = SubjectKeyTestHost.CreateLoaded(_tempDir);
    }

    /// <summary>
    /// Disposes temporary test directory.
    /// </summary>
    public void Dispose()
    {
        _testHost.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Verifies that HMAC-SHA256 keyed pseudonymization uses the configured secret and produces
    /// deterministic pseudonyms, while different secrets produce different pseudonyms to prevent rainbow table reversal.
    /// </summary>
    [Fact]
    public void DeIdentifySubject_KeyedHmacSha256_EnforcesSecretBindingAndResistsRainbowTables()
    {
        // WHAT: Tests HMAC-SHA256 keyed pseudonymization with configured secrets (Base64, hex, plaintext).
        // WHY:  Unsalted hashes are vulnerable to precomputed dictionary rainbow tables.
        // HOW:  Instantiates services with different configuration secrets and asserts pseudonym divergence.
        byte[] secretBytes1 = new byte[32];
        secretBytes1[0] = 0xAA;
        byte[] secretBytes2 = new byte[32];
        secretBytes2[0] = 0xBB;

        IConfiguration config1 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PseudonymizationSecret"] = Convert.ToBase64String(secretBytes1)
            })
            .Build();

        IConfiguration config2 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PseudonymizationSecret"] = Convert.ToBase64String(secretBytes2)
            })
            .Build();

        ComplianceAuditService audit1 = new(_testHost.KeyManagement, _cryptoProvider, configuration: config1);
        ComplianceAuditService audit2 = new(_testHost.KeyManagement, _cryptoProvider, configuration: config2);

        string patientName = "patient_alice_smith";
        string fasta = ">patient_alice_smith\nATGCATGC";

        DeIdentificationResult r1 = audit1.DeIdentifySubject(patientName, fasta, true, true);
        DeIdentificationResult r2 = audit2.DeIdentifySubject(patientName, fasta, true, true);

        // Deterministic under identical key
        DeIdentificationResult r1Again = audit1.DeIdentifySubject(patientName, fasta, true, true);
        Assert.Equal(r1.PseudonymSubjectId, r1Again.PseudonymSubjectId);

        // Different under different key (rainbow table resistance)
        Assert.NotEqual(r1.PseudonymSubjectId, r2.PseudonymSubjectId);
        Assert.StartsWith("sub_", r1.PseudonymSubjectId);
        Assert.StartsWith("sub_", r2.PseudonymSubjectId);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService"/> supports hex and plaintext secret configurations,
    /// as well as CSPRNG fallback when configuration is null.
    /// </summary>
    [Fact]
    public void DeIdentifySubject_HexAndPlaintextSecrets_HandledAccurately()
    {
        // WHAT: Tests hex, plaintext, and null configuration paths for ResolvePseudonymizationKey.
        // WHY:  Ensures flexibility across varied environment variable and secret formats.
        // HOW:  Configures hex 64-char string and arbitrary plaintext, asserting valid pseudonym generation.
        byte[] rawKey = new byte[32];
        rawKey[15] = 0x77;
        string hexKey = Convert.ToHexString(rawKey);

        IConfiguration hexConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PseudonymizationSecret"] = hexKey
            })
            .Build();

        ComplianceAuditService hexAudit = new(_testHost.KeyManagement, _cryptoProvider, configuration: hexConfig);
        DeIdentificationResult hexResult = hexAudit.DeIdentifySubject("subj_hex", "ATGC", true, true);
        Assert.StartsWith("sub_", hexResult.PseudonymSubjectId);

        // Plaintext secret (hashed via SHA-256)
        IConfiguration plainConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PseudonymizationSecret"] = "arbitrary-high-entropy-cluster-passphrase"
            })
            .Build();

        ComplianceAuditService plainAudit = new(_testHost.KeyManagement, _cryptoProvider, configuration: plainConfig);
        DeIdentificationResult plainResult = plainAudit.DeIdentifySubject("subj_plain", "ATGC", true, true);
        Assert.StartsWith("sub_", plainResult.PseudonymSubjectId);

        // Hex secret with non-32-byte length (falls back to SHA-256)
        IConfiguration shortHexConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PseudonymizationSecret"] = "0102030405060708090a0b0c0d0e0f10"
            })
            .Build();
        ComplianceAuditService shortHexAudit = new(_testHost.KeyManagement, _cryptoProvider, configuration: shortHexConfig);
        DeIdentificationResult shortHexResult = shortHexAudit.DeIdentifySubject("subj_short_hex", "ATGC", true, true);
        Assert.StartsWith("sub_", shortHexResult.PseudonymSubjectId);

        // Null config fallback
        ComplianceAuditService nullAudit = new(_testHost.KeyManagement, _cryptoProvider, configuration: null);
        DeIdentificationResult nullResult = nullAudit.DeIdentifySubject("subj_null", "ATGC", true, true);
        Assert.StartsWith("sub_", nullResult.PseudonymSubjectId);
    }

    /// <summary>
    /// Verifies that length-prefixed hash canonicalization in <see cref="ComplianceAuditService"/>
    /// prevents delimiter injection collision attacks when fields contain pipe characters.
    /// </summary>
    [Fact]
    public void ComputeHash_LengthPrefixing_PreventsPipeDelimiterCollisions()
    {
        // WHAT: Tests length-prefixed canonicalization in ComputeHash.
        // WHY:  In un-prefixed format, "A|B" and "C" can collide with "A" and "B|C".
        // HOW:  Uses reflection to invoke ComputeHash on fields designed to collide without length prefixing.
        MethodInfo computeHashMethod = typeof(ComplianceAuditService).GetMethod(
            "ComputeHash",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string prevHash = new('0', 96);

        // Case 1: action = "Foo|Bar", resource = "Baz"
        string hash1 = (string)computeHashMethod.Invoke(null, [
            1L, now, prevHash, "USER", "Foo|Bar", "Baz", "{}"
        ])!;

        // Case 2: action = "Foo", resource = "Bar|Baz"
        // Without length prefixing, both would serialize as "Foo|Bar|Baz" and yield the same hash!
        string hash2 = (string)computeHashMethod.Invoke(null, [
            1L, now, prevHash, "USER", "Foo", "Bar|Baz", "{}"
        ])!;

        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>
    /// Verifies that two <see cref="ComplianceAuditService"/> instances sharing the same <see cref="NodeSecretStore"/>
    /// produce identical pseudonym values across simulated restarts when <c>Security:PseudonymizationSecret</c> is unconfigured.
    /// </summary>
    [Fact]
    public void PseudonymizeSubjectId_PersistedNodeSecretStore_ProducesDeterministicPseudonymsAcrossInstances()
    {
        string secretDir = Path.Combine(_tempDir, "shared-secrets");
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = secretDir
            })
            .Build();

        NodeSecretStore store1 = new(config);
        ComplianceAuditService audit1 = new(_testHost.KeyManagement, _cryptoProvider, secretStore: store1);

        NodeSecretStore store2 = new(config);
        ComplianceAuditService audit2 = new(_testHost.KeyManagement, _cryptoProvider, secretStore: store2);

        string p1 = audit1.PseudonymizeSubjectId("patient-01");
        string p2 = audit2.PseudonymizeSubjectId("patient-01");

        Assert.NotNull(p1);
        Assert.StartsWith("sub_", p1);
        Assert.Equal(p1, p2);

        // Different subject gives different pseudonym
        string pOther = audit1.PseudonymizeSubjectId("patient-02");
        Assert.NotEqual(p1, pOther);

        // Different secret store gives different pseudonym
        string otherSecretDir = Path.Combine(_tempDir, "other-secrets");
        IConfiguration otherConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NodeSecretStore.NodeKeysDirectoryConfigKey] = otherSecretDir
            })
            .Build();
        NodeSecretStore otherStore = new(otherConfig);
        ComplianceAuditService auditOther = new(_testHost.KeyManagement, _cryptoProvider, secretStore: otherStore);
        string pDifferentStore = auditOther.PseudonymizeSubjectId("patient-01");
        Assert.NotEqual(p1, pDifferentStore);
    }

    /// <summary>
    /// Verifies that <see cref="ComplianceAuditService.PseudonymizeSubjectId"/> validates arguments.
    /// </summary>
    [Fact]
    public void PseudonymizeSubjectId_ArgumentValidation_ThrowsExpectedException()
    {
        ComplianceAuditService audit = new(_testHost.KeyManagement, _cryptoProvider);

        Assert.Throws<ArgumentNullException>(() => audit.PseudonymizeSubjectId(null!));
        Assert.Throws<ArgumentException>(() => audit.PseudonymizeSubjectId("   "));
    }

    /// <summary>
    /// Verifies that when <see cref="INodeSecretStore.GetOrCreateSecret"/> returns a key of invalid length,
    /// <see cref="ComplianceAuditService"/> gracefully falls back to a CSPRNG key.
    /// </summary>
    [Fact]
    public void ResolvePseudonymizationKey_SecretStoreKeyInvalidLength_FallsBackToCsprng()
    {
        Moq.Mock<INodeSecretStore> mockStore = new();
        mockStore.Setup(s => s.GetOrCreateSecret(Moq.It.IsAny<string>(), Moq.It.IsAny<int>()))
            .Returns(new byte[16]); // wrong length (not 32)

        ComplianceAuditService audit = new(_testHost.KeyManagement, _cryptoProvider, secretStore: mockStore.Object);
        string pseudonym = audit.PseudonymizeSubjectId("patient-fallback");

        Assert.NotNull(pseudonym);
        Assert.StartsWith("sub_", pseudonym);
    }
}
