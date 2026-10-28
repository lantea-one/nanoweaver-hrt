using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Verification suite for Work Package P7.5: persistent 32-byte FIPS 204 ML-DSA-65 node signing key seed,
/// SHA-256 SPKI key identifiers, private key encapsulation, and classified FDA 21 CFR Part 11 ledger verification.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class NodeSigningKeyPersistenceTests : IDisposable
{
    /// <summary>
    /// Isolated per-test temporary root directory.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_node_sign_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSigningKeyPersistenceTests"/> class.
    /// </summary>
    public NodeSigningKeyPersistenceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of isolated temp directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of isolated temp directory.
            }
        }
    }

    /// <summary>
    /// Verifies criteria 1, 2, 3, and 4: <see cref="PublicKeyFingerprint.ComputeKeyId"/>, deterministic ML-DSA-65
    /// key-pair derivation from a 32-byte FIPS 204 seed, SPKI DER header and length, invalid seed length rejection,
    /// and signature round-trip verification.
    /// </summary>
    [Fact]
    public void C1_To_C4_FingerprintAndSeedDerivation_MatchFips204AndRfc9881Invariants()
    {
        // Criterion 1: Empty span and random 100-byte input
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            PublicKeyFingerprint.ComputeKeyId(ReadOnlySpan<byte>.Empty));

        byte[] random100 = RandomNumberGenerator.GetBytes(100);
        string expectedRandomKeyId = Convert.ToHexString(SHA256.HashData(random100)).ToLowerInvariant();
        Assert.Equal(expectedRandomKeyId, PublicKeyFingerprint.ComputeKeyId(random100));

        // Criterion 2: Deterministic derivation on 00 01 02 ... 1f
        PostQuantumCryptoProvider provider = new();
        byte[] seed = CreateSequentialSeed();
        PqcKeyMaterial first = provider.DeriveSigningKeyPairFromSeed(seed);
        PqcKeyMaterial second = provider.DeriveSigningKeyPairFromSeed(seed);

        Assert.Equal(first.KeyId, second.KeyId);
        Assert.Equal(first.PublicKeyBase64, second.PublicKeyBase64);
        Assert.Equal(first.PrivateKeyBase64, second.PrivateKeyBase64);
        Assert.Equal("ML-DSA-65", first.Algorithm);

        byte[] spkiDer = Convert.FromBase64String(first.PublicKeyBase64);
        Assert.Equal(1974, spkiDer.Length);
        Assert.Equal(
            "308207b2300b0609608648016503040312038207a100",
            Convert.ToHexStringLower(spkiDer.AsSpan(0, 22)));
        Assert.Equal(PublicKeyFingerprint.ComputeKeyId(spkiDer), first.KeyId);
        Assert.Equal(64, first.KeyId.Length);
        Assert.All(first.KeyId, ch => Assert.True((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')));

        // Criterion 3: Flipping 1 bit produces a different KeyId; 31-byte and 33-byte seeds throw without leaking seed bytes
        byte[] flippedSeed = CreateSequentialSeed();
        flippedSeed[0] ^= 0x01;
        PqcKeyMaterial flipped = provider.DeriveSigningKeyPairFromSeed(flippedSeed);
        Assert.NotEqual(first.KeyId, flipped.KeyId);

        foreach (int invalidLen in new[] { 31, 33 })
        {
            byte[] badSeed = new byte[invalidLen];
            for (int i = 0; i < badSeed.Length; i++)
            {
                badSeed[i] = (byte)(0xA0 + i);
            }

            ArgumentException ex = Assert.Throws<ArgumentException>(() => provider.DeriveSigningKeyPairFromSeed(badSeed));
            Assert.DoesNotContain(Convert.ToHexStringLower(badSeed), ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Convert.ToBase64String(badSeed), ex.Message, StringComparison.Ordinal);
        }

        // Criterion 4: SignData / VerifySignature round-trip and single-byte tamper rejection
        byte[] data = Encoding.UTF8.GetBytes(" FDA 21 CFR Part 11 canonical ledger digest ");
        PqcSignature sig = provider.SignData(data, first.PrivateKeyBase64!, first.KeyId);
        Assert.True(provider.VerifySignature(data, sig.SignatureBase64, first.PublicKeyBase64));

        byte[] tampered = (byte[])data.Clone();
        tampered[10] ^= 0xFF;
        Assert.False(provider.VerifySignature(tampered, sig.SignatureBase64, first.PublicKeyBase64));

        // Regression pin derived once from the implementation after criteria 1-3 passed (per Criterion 4).
        Assert.Equal(PinnedSequentialSeedKeyId, first.KeyId);
    }

    /// <summary>
    /// Verifies criteria 5, 6, 7, 8, 9, 10, and 19: key file creation with 0600 permissions, restart survival across
    /// instances, pre-written seed loading, fail-loud rejection of corrupted lengths and directories without overwriting,
    /// permission repair from 0644 to 0600, and isolation from <c>token_signing.key</c> and <c>phi_master.key</c>.
    /// </summary>
    [Fact]
    public void C5_To_C10_And_C19_KeyFileLifecycleFaultsRepairAndIsolation_BehaveAsSpecified()
    {
        // Criterion 5, 6, 19: Fresh directory -> GeneratedFile (0600, 32 bytes), second instance -> ExistingFile, same KeyId
        string dirA = Path.Combine(_tempRoot, "node_a");
        string keysDirA = Path.Combine(dirA, "node-keys");
        using SubjectKeyTestHost hostA1 = SubjectKeyTestHost.CreateLoaded(dirA);

        string tokenKeyPath = Path.Combine(keysDirA, "token_signing.key");
        string phiKeyPath = Path.Combine(keysDirA, "phi_master.key");
        string nodeSigningKeyPath = Path.Combine(keysDirA, KeyManagementService.NodeSigningKeyFileName);

        byte[] tokenBefore = File.ReadAllBytes(tokenKeyPath);
        byte[] phiBefore = File.ReadAllBytes(phiKeyPath);
        byte[] nodeSeedBytes = File.ReadAllBytes(nodeSigningKeyPath);

        Assert.Equal(32, nodeSeedBytes.Length);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(nodeSigningKeyPath));
        Assert.Equal(NodeSecretOrigin.GeneratedFile, hostA1.KeyManagement.NodeSigningKeyOrigin);
        Assert.NotEqual(tokenBefore, nodeSeedBytes);
        Assert.NotEqual(phiBefore, nodeSeedBytes);

        byte[] message = Encoding.UTF8.GetBytes("persisted-signature-check");
        PqcSignature sigFromA1 = hostA1.KeyManagement.SignWithNodeSigningKey(message);
        Assert.Equal(hostA1.KeyManagement.GetNodeSigningKey().KeyId, sigFromA1.SignerKeyId);

        // Construct a second KeyManagementService over the same directory
        KeyManagementService kmsA2 = new(hostA1.CryptoProvider, hostA1.Vault, hostA1.SecretStore);
        Assert.Equal(NodeSecretOrigin.ExistingFile, kmsA2.NodeSigningKeyOrigin);
        Assert.Equal(hostA1.KeyManagement.GetNodeSigningKey().KeyId, kmsA2.GetNodeSigningKey().KeyId);
        Assert.Equal(hostA1.KeyManagement.GetNodeSigningKey().PublicKeyBase64, kmsA2.GetNodeSigningKey().PublicKeyBase64);
        Assert.True(hostA1.CryptoProvider.VerifySignature(message, sigFromA1.SignatureBase64, kmsA2.GetNodeSigningKey().PublicKeyBase64));

        // Criterion 19: token_signing.key and phi_master.key untouched
        Assert.Equal(tokenBefore, File.ReadAllBytes(tokenKeyPath));
        Assert.Equal(phiBefore, File.ReadAllBytes(phiKeyPath));

        // Criterion 6: Second directory produces a different KeyId
        using SubjectKeyTestHost hostB = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempRoot, "node_b"));
        Assert.NotEqual(hostA1.KeyManagement.GetNodeSigningKey().KeyId, hostB.KeyManagement.GetNodeSigningKey().KeyId);

        // Criterion 7: Pre-writing seed 00..1f gives PinnedSequentialSeedKeyId and ExistingFile
        string dirPinned = Path.Combine(_tempRoot, "node_pinned");
        string keysDirPinned = Path.Combine(dirPinned, "node-keys");
        Directory.CreateDirectory(keysDirPinned);
        File.WriteAllBytes(Path.Combine(keysDirPinned, KeyManagementService.NodeSigningKeyFileName), CreateSequentialSeed());

        using SubjectKeyTestHost hostPinned = SubjectKeyTestHost.CreateLoaded(dirPinned);
        Assert.Equal(NodeSecretOrigin.ExistingFile, hostPinned.KeyManagement.NodeSigningKeyOrigin);
        Assert.Equal(PinnedSequentialSeedKeyId, hostPinned.KeyManagement.GetNodeSigningKey().KeyId);

        // Criterion 8: 0, 31, and 33 byte files throw NodeSecretProvisioningException and leave file untouched
        foreach (int badLen in new[] { 0, 31, 33 })
        {
            string badKeysDir = Path.Combine(_tempRoot, $"bad_len_{badLen}");
            Directory.CreateDirectory(badKeysDir);
            string badFilePath = Path.Combine(badKeysDir, KeyManagementService.NodeSigningKeyFileName);
            byte[] written = RandomNumberGenerator.GetBytes(badLen);
            File.WriteAllBytes(badFilePath, written);

            NodeSecretStore store = CreateStoreForKeysDir(badKeysDir);
            NodeSecretProvisioningException ex = Assert.Throws<NodeSecretProvisioningException>(
                () => new KeyManagementService(hostA1.CryptoProvider, hostA1.Vault, store));

            Assert.Contains(KeyManagementService.NodeSigningKeyFileName, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(badKeysDir, ex.Message, StringComparison.Ordinal);
            Assert.Equal(written, File.ReadAllBytes(badFilePath));
        }

        // Criterion 9: Directory at key path throws NodeSecretProvisioningException, directory remains, no *.tmp left
        string dirCollisionKeys = Path.Combine(_tempRoot, "dir_collision");
        Directory.CreateDirectory(dirCollisionKeys);
        string collisionPath = Path.Combine(dirCollisionKeys, KeyManagementService.NodeSigningKeyFileName);
        Directory.CreateDirectory(collisionPath);

        NodeSecretStore collisionStore = CreateStoreForKeysDir(dirCollisionKeys);
        Assert.Throws<NodeSecretProvisioningException>(
            () => new KeyManagementService(hostA1.CryptoProvider, hostA1.Vault, collisionStore));
        Assert.True(Directory.Exists(collisionPath));
        Assert.Empty(Directory.GetFiles(dirCollisionKeys, "*.tmp"));

        // Criterion 10: Valid 32-byte key file with mode 0644 loads (ExistingFile, KeyId unchanged) and is repaired to 0600
        string modeKeysDir = Path.Combine(_tempRoot, "mode_repair");
        Directory.CreateDirectory(modeKeysDir);
        string modeKeyPath = Path.Combine(modeKeysDir, KeyManagementService.NodeSigningKeyFileName);
        File.WriteAllBytes(modeKeyPath, CreateSequentialSeed());
        File.SetUnixFileMode(modeKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        NodeSecretStore modeStore = CreateStoreForKeysDir(modeKeysDir);
        KeyManagementService repairedKms = new(hostA1.CryptoProvider, hostA1.Vault, modeStore);
        Assert.Equal(NodeSecretOrigin.ExistingFile, repairedKms.NodeSigningKeyOrigin);
        Assert.Equal(PinnedSequentialSeedKeyId, repairedKms.GetNodeSigningKey().KeyId);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(modeKeyPath));
    }

    /// <summary>
    /// Verifies criteria 11, 12, 13, and 18: host-level restart survival across two <see cref="WebApplicationFactory{TEntryPoint}"/>
    /// instances, startup failure on a 31-byte key file, startup log formatting without path disclosure, and zero exposure
    /// of seed or PKCS#8 private key bytes across HTTP responses, manifests, and logs.
    /// </summary>
    [Fact]
    public async Task C11_C12_C13_C18_HttpHostRestartSurvivalLogAndZeroSecretExposure_Succeed()
    {
        string hostRoot = Path.Combine(_tempRoot, "http_host");
        string sharedDir = Path.Combine(hostRoot, "shared-data");
        string exportDir = Path.Combine(hostRoot, "exports");
        string parquetDir = Path.Combine(hostRoot, "parquet");
        string keysDir = Path.Combine(hostRoot, "node-keys");
        Directory.CreateDirectory(sharedDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(parquetDir);
        Directory.CreateDirectory(keysDir);
        TestFixturesHelper.EnsureTestFixtures(sharedDir);

        string host1KeyId;
        string host1PublicKeyBase64;
        AuditRecord[] host1Ledger;
        string manifestPath;

        ConcurrentQueue<string> host1Logs = new();
        using (WebApplicationFactory<Program> base1 = new())
        using (WebApplicationFactory<Program> factory1 = ConfigureFactory(base1, sharedDir, exportDir, parquetDir, keysDir, host1Logs))
        using (HttpClient client1 = factory1.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client1, factory1);

            // GET /api/compliance/status
            HttpResponseMessage statusResp = await client1.GetAsync("/api/compliance/status");
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
            string statusBody = await statusResp.Content.ReadAsStringAsync();
            ComplianceStatusReport status = JsonSerializer.Deserialize<ComplianceStatusReport>(statusBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            host1KeyId = status.AuditSigningKeyId;
            host1PublicKeyBase64 = status.AuditSigningPublicKeyBase64;
            Assert.Equal(64, host1KeyId.Length);
            Assert.NotEmpty(host1PublicKeyBase64);

            // POST /api/datasets/export-encrypted
            HttpResponseMessage exportResp = await client1.PostAsJsonAsync("/api/datasets/export-encrypted", new ExportEncryptedDatasetRequest
            {
                TrajectoryFileName = "dna_nanotube_sample.xyz",
                SubjectId = "sub_node_sign_export"
            });
            Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
            string exportBody = await exportResp.Content.ReadAsStringAsync();

            // GET /api/compliance/audit-ledger
            HttpResponseMessage ledgerResp = await client1.GetAsync("/api/compliance/audit-ledger?limit=50");
            Assert.Equal(HttpStatusCode.OK, ledgerResp.StatusCode);
            string ledgerBody = await ledgerResp.Content.ReadAsStringAsync();
            host1Ledger = JsonSerializer.Deserialize<AuditRecord[]>(ledgerResp.Content.ReadAsStream(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.NotEmpty(host1Ledger);

            // GET /api/compliance/verify-ledger
            HttpResponseMessage verifyResp = await client1.GetAsync("/api/compliance/verify-ledger");
            Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
            string verifyBody = await verifyResp.Content.ReadAsStringAsync();

            string[] manifests = Directory.GetFiles(exportDir, "*.manifest.pqc.json");
            Assert.Single(manifests);
            manifestPath = manifests[0];
            string manifestBody = await File.ReadAllTextAsync(manifestPath);

            // Criterion 13: Exactly one startup log line matching the expected format and containing no '/'
            string expectedLogLine = $"Node audit signing key ready. Algorithm: ML-DSA-65. Origin: GeneratedFile. Fingerprint: {host1KeyId}.";
            string[] matchingLogs = host1Logs.Where(l => l.Contains("Node audit signing key ready.", StringComparison.Ordinal)).ToArray();
            Assert.Single(matchingLogs);
            Assert.Contains(expectedLogLine, matchingLogs[0], StringComparison.Ordinal);
            Assert.DoesNotContain("/", matchingLogs[0], StringComparison.Ordinal);

            // Criterion 18: Verify zero exposure of seed Base64, seed hex, PKCS#8 Base64, or non-null privateKey
            byte[] diskSeed = await File.ReadAllBytesAsync(Path.Combine(keysDir, KeyManagementService.NodeSigningKeyFileName));
            string seedBase64 = Convert.ToBase64String(diskSeed);
            string seedHex = Convert.ToHexStringLower(diskSeed);
            PostQuantumCryptoProvider pqc = new();
            string pkcs8Base64 = pqc.DeriveSigningKeyPairFromSeed(diskSeed).PrivateKeyBase64!;

            string[] allSurfaces = new[] { statusBody, ledgerBody, verifyBody, exportBody, manifestBody }
                .Concat(host1Logs)
                .ToArray();

            foreach (string surface in allSurfaces)
            {
                Assert.DoesNotContain(seedBase64, surface, StringComparison.Ordinal);
                Assert.DoesNotContain(seedHex, surface, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(pkcs8Base64, surface, StringComparison.Ordinal);
                Assert.DoesNotContain("\"privateKey\":\"", surface, StringComparison.Ordinal);
            }
        }

        ClearSqlitePool(Path.Combine(sharedDir, "weaver_config.db"));

        // Host 2 over the same temp root after restart
        ConcurrentQueue<string> host2Logs = new();
        using (WebApplicationFactory<Program> base2 = new())
        using (WebApplicationFactory<Program> factory2 = ConfigureFactory(base2, sharedDir, exportDir, parquetDir, keysDir, host2Logs))
        using (HttpClient client2 = factory2.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client2, factory2);

            HttpResponseMessage statusResp2 = await client2.GetAsync("/api/compliance/status");
            Assert.Equal(HttpStatusCode.OK, statusResp2.StatusCode);
            ComplianceStatusReport status2 = (await statusResp2.Content.ReadFromJsonAsync<ComplianceStatusReport>())!;
            Assert.Equal(host1KeyId, status2.AuditSigningKeyId);
            Assert.Equal(host1PublicKeyBase64, status2.AuditSigningPublicKeyBase64);

            // Every ledger record from host 1 verifies against host 2's public key
            PostQuantumCryptoProvider verifier = new();
            foreach (AuditRecord record in host1Ledger)
            {
                Assert.Equal(host1KeyId, record.Signature.SignerKeyId);
                Assert.True(verifier.VerifySignature(
                    Encoding.UTF8.GetBytes(record.CurrentHash),
                    record.Signature.SignatureBase64,
                    status2.AuditSigningPublicKeyBase64));
            }

            // Host 1's .manifest.pqc.json verifies against host 2's public key
            using JsonDocument manifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            string sha256Hex = manifestDoc.RootElement.GetProperty("sha256").GetString()!;
            string signatureBase64 = manifestDoc.RootElement.GetProperty("signature").GetString()!;
            string signerKeyId = manifestDoc.RootElement.GetProperty("signerKeyId").GetString()!;

            Assert.Equal(host1KeyId, signerKeyId);
            Assert.NotEqual(ComplianceAuditService.LegacyEphemeralSigningKeyId, signerKeyId);
            Assert.True(verifier.VerifySignature(
                Encoding.UTF8.GetBytes(sha256Hex),
                signatureBase64,
                status2.AuditSigningPublicKeyBase64));

            // Criterion 13 on Host 2: Origin is ExistingFile
            string expectedLogLine2 = $"Node audit signing key ready. Algorithm: ML-DSA-65. Origin: ExistingFile. Fingerprint: {host1KeyId}.";
            string[] matchingLogs2 = host2Logs.Where(l => l.Contains("Node audit signing key ready.", StringComparison.Ordinal)).ToArray();
            Assert.Single(matchingLogs2);
            Assert.Contains(expectedLogLine2, matchingLogs2[0], StringComparison.Ordinal);
            Assert.DoesNotContain("/", matchingLogs2[0], StringComparison.Ordinal);
        }

        // Criterion 12: Host with 31-byte node_signing_mldsa65.key fails to start with NodeSecretProvisioningException in chain
        string badHostRoot = Path.Combine(_tempRoot, "bad_http_host");
        string badShared = Path.Combine(badHostRoot, "shared-data");
        string badExport = Path.Combine(badHostRoot, "exports");
        string badParquet = Path.Combine(badHostRoot, "parquet");
        string badKeys = Path.Combine(badHostRoot, "node-keys");
        Directory.CreateDirectory(badShared);
        Directory.CreateDirectory(badExport);
        Directory.CreateDirectory(badParquet);
        Directory.CreateDirectory(badKeys);
        await File.WriteAllBytesAsync(Path.Combine(badKeys, KeyManagementService.NodeSigningKeyFileName), new byte[31]);

        using (WebApplicationFactory<Program> badBase = new())
        using (WebApplicationFactory<Program> badFactory = ConfigureFactory(badBase, badShared, badExport, badParquet, badKeys, new ConcurrentQueue<string>()))
        {
            Exception startEx = Assert.ThrowsAny<Exception>(() => badFactory.CreateClient());
            Assert.True(ContainsExceptionInChain<NodeSecretProvisioningException>(startEx));
        }
    }

    /// <summary>
    /// Verifies criteria 14, 15, and 16: genesis <c>DetailsJson</c> format for <c>GeneratedFile</c> and <c>ExistingFile</c>,
    /// distinct <see cref="AuditLedgerViolationKind"/> classification without consulting <c>VerifySignature</c> for legacy
    /// or unknown keys, precedence of hash-chain checks over key classification, and <c>Degraded</c> compliance status.
    /// </summary>
    [Fact]
    public void C14_C15_C16_AuditLedgerVerificationClassificationAndGenesisDetails_BehaveAccurately()
    {
        string dir = Path.Combine(_tempRoot, "audit_classify");
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(dir);
        ComplianceAuditService audit1 = new(host.KeyManagement, host.CryptoProvider);

        audit1.LogAction("OP_1", "ActionOne", "res_1", "{\"a\":1}");
        audit1.LogAction("OP_2", "ActionTwo", "res_2", "{\"b\":2}");

        string keyId = host.KeyManagement.GetNodeSigningKey().KeyId;
        AuditLedgerVerificationResult okResult = audit1.VerifyLedgerIntegrity();
        Assert.True(okResult.IsValid);
        Assert.Equal(AuditLedgerViolationKind.None, okResult.ViolationKind);
        Assert.Equal(keyId, okResult.SigningKeyId);

        IReadOnlyList<AuditRecord> records1 = audit1.GetAuditRecords(10);
        AuditRecord genesis1 = records1[^1];
        Assert.Equal(
            $"{{\"genesis\":true,\"signingKeyId\":\"{keyId}\",\"signingKeyOrigin\":\"GeneratedFile\"}}",
            genesis1.DetailsJson);

        // Second KeyManagementService + ComplianceAuditService over the same directory records ExistingFile in genesis
        KeyManagementService kms2 = new(host.CryptoProvider, host.Vault, host.SecretStore);
        ComplianceAuditService audit2 = new(kms2, host.CryptoProvider);
        AuditRecord genesis2 = audit2.GetAuditRecords(10)[^1];
        Assert.Equal(
            $"{{\"genesis\":true,\"signingKeyId\":\"{keyId}\",\"signingKeyOrigin\":\"ExistingFile\"}}",
            genesis2.DetailsJson);

        // Criterion 15 & 16 via reflection on _records of audit1
        FieldInfo recordsField = typeof(ComplianceAuditService).GetField("_records", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<AuditRecord> list = (List<AuditRecord>)recordsField.GetValue(audit1)!;
        AuditRecord originalRec1 = list[1];

        // 15(a) & 16: SignerKeyId = "node_signing_master" with valid current-key SignatureBase64 -> LegacyEphemeralSigningKey
        list[1] = originalRec1 with
        {
            Signature = originalRec1.Signature with
            {
                SignerKeyId = ComplianceAuditService.LegacyEphemeralSigningKeyId
            }
        };
        AuditLedgerVerificationResult legacyRes = audit1.VerifyLedgerIntegrity();
        Assert.False(legacyRes.IsValid);
        Assert.Equal(AuditLedgerViolationKind.LegacyEphemeralSigningKey, legacyRes.ViolationKind);
        Assert.Equal(1, legacyRes.FirstViolationSequence);
        Assert.Equal(keyId, legacyRes.SigningKeyId);
        Assert.Equal(
            "Audit record 1 carries a signature from the pre-persistence ephemeral node key 'node_signing_master'. That key was never stored, so this signature can never be verified. This is not evidence of tampering.",
            legacyRes.Message);
        Assert.Equal("Degraded", audit1.GetComplianceStatus().OverallStatus);
        list[1] = originalRec1;

        // 15(b): Record 1 re-signed by a different, valid ML-DSA-65 key -> UnknownSigningKey (message does not echo foreign key id)
        PqcKeyMaterial foreignKey = host.CryptoProvider.DeriveSigningKeyPairFromSeed(CreateSequentialSeed());
        PqcSignature foreignSig = host.CryptoProvider.SignData(
            Encoding.UTF8.GetBytes(originalRec1.CurrentHash),
            foreignKey.PrivateKeyBase64!,
            foreignKey.KeyId);
        list[1] = originalRec1 with { Signature = foreignSig };

        AuditLedgerVerificationResult unknownRes = audit1.VerifyLedgerIntegrity();
        Assert.False(unknownRes.IsValid);
        Assert.Equal(AuditLedgerViolationKind.UnknownSigningKey, unknownRes.ViolationKind);
        Assert.Equal(1, unknownRes.FirstViolationSequence);
        Assert.Equal(keyId, unknownRes.SigningKeyId);
        Assert.Equal(
            "Audit record 1 is signed by a key that is not this node's audit signing key, so its signature cannot be verified here.",
            unknownRes.Message);
        Assert.DoesNotContain(foreignKey.KeyId, unknownRes.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Degraded", audit1.GetComplianceStatus().OverallStatus);

        // 16: Prove VerifySignature was not consulted for (b) by keeping originalRec1's valid current-key SignatureBase64 with foreign KeyId
        list[1] = originalRec1 with
        {
            Signature = originalRec1.Signature with
            {
                SignerKeyId = foreignKey.KeyId
            }
        };
        AuditLedgerVerificationResult unknownWithValidBytes = audit1.VerifyLedgerIntegrity();
        Assert.False(unknownWithValidBytes.IsValid);
        Assert.Equal(AuditLedgerViolationKind.UnknownSigningKey, unknownWithValidBytes.ViolationKind);
        list[1] = originalRec1;

        // 15(c) & 16: Signature zeroed, key id unchanged -> InvalidSignature with exact pinned message
        list[1] = originalRec1 with
        {
            Signature = originalRec1.Signature with
            {
                SignatureBase64 = Convert.ToBase64String(new byte[64])
            }
        };
        AuditLedgerVerificationResult invalidSigRes = audit1.VerifyLedgerIntegrity();
        Assert.False(invalidSigRes.IsValid);
        Assert.Equal(AuditLedgerViolationKind.InvalidSignature, invalidSigRes.ViolationKind);
        Assert.Equal(1, invalidSigRes.FirstViolationSequence);
        Assert.Equal("Invalid ML-DSA-65 signature on audit record 1.", invalidSigRes.Message);
        Assert.Equal("Degraded", audit1.GetComplianceStatus().OverallStatus);
        list[1] = originalRec1;

        // 15(d): Action tampered AND SignerKeyId = "node_signing_master" -> TamperedPayload takes precedence
        list[1] = originalRec1 with
        {
            Action = "TamperedAction",
            Signature = originalRec1.Signature with
            {
                SignerKeyId = ComplianceAuditService.LegacyEphemeralSigningKeyId
            }
        };
        AuditLedgerVerificationResult precedenceRes = audit1.VerifyLedgerIntegrity();
        Assert.False(precedenceRes.IsValid);
        Assert.Equal(AuditLedgerViolationKind.TamperedPayload, precedenceRes.ViolationKind);
        list[1] = originalRec1;

        // 15(e): SequenceDiscontinuity, BrokenHashLink, TamperedPayload
        list[1] = originalRec1 with { SequenceNumber = 42 };
        Assert.Equal(AuditLedgerViolationKind.SequenceDiscontinuity, audit1.VerifyLedgerIntegrity().ViolationKind);
        list[1] = originalRec1;

        list[1] = originalRec1 with { PreviousHash = new string('f', 96) };
        Assert.Equal(AuditLedgerViolationKind.BrokenHashLink, audit1.VerifyLedgerIntegrity().ViolationKind);
        list[1] = originalRec1;

        list[1] = originalRec1 with { Resource = "tampered_res" };
        Assert.Equal(AuditLedgerViolationKind.TamperedPayload, audit1.VerifyLedgerIntegrity().ViolationKind);
        list[1] = originalRec1;
    }

    /// <summary>
    /// Verifies criteria 17, 19, and 20: reflection and source-level invariants ensuring no non-private key-bearing
    /// members exist on <see cref="KeyManagementService"/> or <see cref="IKeyManagementService"/>, and example
    /// providers populate the new compliance report and verification fields.
    /// </summary>
    [Fact]
    public void C17_C20_ReflectionAndModelExamples_EnforcePrivateKeyEncapsulation()
    {
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempRoot, "c17"));
        Assert.Null(host.KeyManagement.GetNodeSigningKey().PrivateKeyBase64);
        Assert.Null(host.KeyManagement.GetNodeSigningKey().PrivateKeyBase64);

        Type[] forbiddenFieldOrPropTypes =
        [
            typeof(string),
            typeof(byte[]),
            typeof(ReadOnlyMemory<byte>),
            typeof(PqcKeyMaterial)
        ];

        // Non-private instance fields on KeyManagementService must be empty
        FieldInfo[] nonPrivateInstanceFields = typeof(KeyManagementService)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => !f.IsPrivate && forbiddenFieldOrPropTypes.Contains(f.FieldType))
            .ToArray();
        Assert.Empty(nonPrivateInstanceFields);

        // Non-private properties on KeyManagementService must only be those declared on IKeyManagementService
        HashSet<string> interfacePropNames = typeof(IKeyManagementService)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        PropertyInfo[] nonPrivateProps = typeof(KeyManagementService)
            .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                MethodInfo? getter = p.GetGetMethod(nonPublic: true);
                return getter is not null && !getter.IsPrivate && forbiddenFieldOrPropTypes.Contains(p.PropertyType) && !interfacePropNames.Contains(p.Name);
            })
            .ToArray();
        Assert.Empty(nonPrivateProps);

        // No member of IKeyManagementService returns byte[], ReadOnlyMemory<byte>, Memory<byte>, or ReadOnlySpan<byte>
        Type[] forbiddenReturnTypes =
        [
            typeof(byte[]),
            typeof(ReadOnlyMemory<byte>),
            typeof(Memory<byte>),
            typeof(ReadOnlySpan<byte>)
        ];
        foreach (MethodInfo method in typeof(IKeyManagementService).GetMethods())
        {
            Assert.DoesNotContain(method.ReturnType, forbiddenReturnTypes);
        }

        // Exercise GetExamples() on AuditLedgerVerificationResult and ComplianceStatusReport
        AuditLedgerVerificationResult resultExample = new AuditLedgerVerificationResult().GetExamples();
        Assert.Equal(AuditLedgerViolationKind.None, resultExample.ViolationKind);
        Assert.Equal(64, resultExample.SigningKeyId.Length);

        ComplianceStatusReport statusExample = new ComplianceStatusReport().GetExamples();
        Assert.Equal(64, statusExample.AuditSigningKeyId.Length);
        Assert.NotEmpty(statusExample.AuditSigningPublicKeyBase64);
    }

    /// <summary>
    /// Pinned 64-character SHA-256 SPKI fingerprint for the 32-byte sequential seed <c>00 01 02 ... 1f</c>,
    /// derived once from the FIPS 204 implementation after criteria 1-3 passed (per Criterion 4).
    /// </summary>
    private const string PinnedSequentialSeedKeyId = "b8b62131bfbe84433efb2273d7f5b87f7a22854a2cfd366fc2aead86d837c52d";

    /// <summary>
    /// Creates the deterministic 32-byte seed <c>00 01 02 ... 1f</c>.
    /// </summary>
    /// <returns>A 32-byte array containing bytes <c>0x00</c> through <c>0x1F</c>.</returns>
    private static byte[] CreateSequentialSeed()
    {
        byte[] seed = new byte[32];
        for (int i = 0; i < seed.Length; i++)
        {
            seed[i] = (byte)i;
        }

        return seed;
    }

    /// <summary>
    /// Creates a <see cref="NodeSecretStore"/> bound to a specific node-keys directory.
    /// </summary>
    /// <param name="keysDir">Absolute path to the node-keys directory.</param>
    /// <returns>The initialized <see cref="NodeSecretStore"/>.</returns>
    private static NodeSecretStore CreateStoreForKeysDir(string keysDir)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEAVER_NODE_KEYS_DIR"] = keysDir
            })
            .Build();
        return new NodeSecretStore(config);
    }

    /// <summary>
    /// Configures a <see cref="WebApplicationFactory{TEntryPoint}"/> bound to isolated temporary directories and a log sink.
    /// </summary>
    /// <param name="factory">Base web application factory.</param>
    /// <param name="sharedDir">Incoming shared data directory.</param>
    /// <param name="exportDir">Export bundle directory.</param>
    /// <param name="parquetDir">Parquet output directory.</param>
    /// <param name="keysDir">Node secret keys directory.</param>
    /// <param name="logSink">Queue capturing formatted log messages.</param>
    /// <returns>The configured <see cref="WebApplicationFactory{TEntryPoint}"/>.</returns>
    private static WebApplicationFactory<Program> ConfigureFactory(
        WebApplicationFactory<Program> factory,
        string sharedDir,
        string exportDir,
        string parquetDir,
        string keysDir,
        ConcurrentQueue<string> logSink)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = sharedDir,
                    ["TRAJECTORY_EXPORT_DIR"] = exportDir,
                    ["TRAJECTORY_PARQUET_DIR"] = parquetDir,
                    ["WEAVER_NODE_KEYS_DIR"] = keysDir,
                    ["POSTGRES_CONNECTION_STRING"] = string.Empty
                });
            });
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CapturingLoggerProvider(logSink));
            });
        });
    }

    /// <summary>
    /// Walks an exception chain (including <see cref="AggregateException.InnerExceptions"/>) to check for a specific exception type.
    /// </summary>
    /// <typeparam name="TException">Target exception type.</typeparam>
    /// <param name="exception">Root exception thrown by host startup.</param>
    /// <returns><c>true</c> if an instance of <typeparamref name="TException"/> is present in the chain; otherwise, <c>false</c>.</returns>
    private static bool ContainsExceptionInChain<TException>(Exception? exception)
        where TException : Exception
    {
        while (exception is not null)
        {
            if (exception is TException)
            {
                return true;
            }

            if (exception is AggregateException agg)
            {
                foreach (Exception inner in agg.InnerExceptions)
                {
                    if (ContainsExceptionInChain<TException>(inner))
                    {
                        return true;
                    }
                }
            }

            exception = exception.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Clears the SQLite connection pool for a single database file path without affecting concurrent tests.
    /// </summary>
    /// <param name="dbPath">Absolute path to the SQLite database file.</param>
    private static void ClearSqlitePool(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>
    /// Logger provider that records every formatted log line into a thread-safe queue.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLoggerProvider"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLoggerProvider(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_sink);

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Minimal <see cref="ILogger"/> implementation that appends formatted log lines to a queue.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLogger"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLogger(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _sink.Enqueue(formatter(state, exception));
    }
}
