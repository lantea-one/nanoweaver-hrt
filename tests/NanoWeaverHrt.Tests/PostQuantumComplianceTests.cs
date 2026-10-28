using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit and integration test suite verifying Post-Quantum Cryptography (NIST FIPS 203 &amp; 204),
/// HIPAA Safe Harbor de-identification, GDPR Article 17 crypto-shredding, and FDA 21 CFR Part 11 audit trails.
/// </summary>
public sealed class PostQuantumComplianceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Root temp directory created by this instance's constructor; deleted in <see cref="Dispose"/>.
    /// </summary>
    private readonly string _testRoot;

    /// <summary>
    /// Deletes the temporary directories this test instance created.
    /// </summary>
    /// <remarks>
    /// xUnit builds one instance per test method, so without this every test would leave a directory behind.
    /// <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> are swallowed because a file
    /// still held open by a disposing host is realistic and cleanup must never fail a test. The shared
    /// <c>WebApplicationFactory</c> is owned by xUnit's class fixture and is deliberately not disposed here.
    /// </remarks>
    public void Dispose()
    {
        // WHAT: Removes this instance's temporary directories.
        // WHY:  Prevents leaking weaver_* directories under the OS temp path on every test.
        // HOW:  Guards with Directory.Exists, deletes recursively, and ignores IO/permission failures.
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a file still held open must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: a locked or read-only entry must not fail the test run.
        }
    }

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _sharedDataDir;
    private readonly string _exportDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostQuantumComplianceTests"/> class.
    /// </summary>
    /// <param name="factory">The test web application factory fixture.</param>
    public PostQuantumComplianceTests(WebApplicationFactory<Program> factory)
    {
        // WHAT: Allocates a private, writable data root for this test class instance.
        // WHY:  SystemConfigurationStore places weaver_config.db inside TRAJECTORY_INCOMING_DIR, so binding
        //       that to the live simulations/shared-data made the suite share one database across test
        //       classes and write into the repository working tree.
        // HOW:   Derives a GUID-suffixed directory under the OS temp path.
        string testRoot = Path.Combine(Path.GetTempPath(), "weaver_pqc_" + Guid.NewGuid().ToString("N"));
        _testRoot = testRoot;
        _sharedDataDir = Path.Combine(testRoot, "shared-data");
        _exportDir = Path.Combine(testRoot, "exports");
        Directory.CreateDirectory(_sharedDataDir);

        TestFixturesHelper.EnsureTestFixtures(_sharedDataDir);
        Directory.CreateDirectory(_exportDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _sharedDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", _exportDir);
        });

        _client = _factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(_client, _factory);
    }

    /// <summary>
    /// Verifies that ML-KEM-768 and ML-DSA-65 cryptographic operations execute cleanly in roundtrips.
    /// </summary>
    [Fact]
    public void PostQuantumCryptoProvider_FullRoundtrip_Succeeds()
    {
        // WHAT: Test ML-KEM-768 key encapsulation and ML-DSA-65 signature roundtrips.
        // WHY:  Ensures the underlying lattice cryptography engine functions accurately.
        // HOW:  Generates key pairs, encapsulates a shared secret, encrypts data with AES-256-GCM, and verifies signatures.
        PostQuantumCryptoProvider provider = new();

        PqcKeyMaterial kemKey = provider.GenerateKemKeyPair("subj_test_01");
        Assert.Equal("ML-KEM-768", kemKey.Algorithm);
        Assert.False(string.IsNullOrWhiteSpace(kemKey.PublicKeyBase64));
        Assert.False(string.IsNullOrWhiteSpace(kemKey.PrivateKeyBase64));

        PqcKeyMaterial signingKey = provider.GenerateSigningKeyPair("node_sign_01");
        Assert.Equal("ML-DSA-65", signingKey.Algorithm);
        Assert.False(string.IsNullOrWhiteSpace(signingKey.PublicKeyBase64));
        Assert.False(string.IsNullOrWhiteSpace(signingKey.PrivateKeyBase64));

        byte[] plaintext = Encoding.UTF8.GetBytes("Patient Nanocarrier Formulation: 17β-estradiol 550 pmol/L");
        EncryptedEnvelope envelope = provider.EncryptEnvelope(plaintext, kemKey.PublicKeyBase64, "subj_test_01");

        Assert.Equal("ML-KEM-768+AES-256-GCM", envelope.Algorithm);
        Assert.Equal("subj_test_01", envelope.KeyId);
        Assert.False(string.IsNullOrWhiteSpace(envelope.EncapsulatedKeyBase64));
        Assert.False(string.IsNullOrWhiteSpace(envelope.CiphertextBase64));

        byte[] decrypted = provider.DecryptEnvelope(envelope, kemKey.PrivateKeyBase64!);
        Assert.Equal(plaintext, decrypted);

        // Test signature roundtrip
        byte[] auditPayload = Encoding.UTF8.GetBytes("FDA_PART11_AUDIT_LOG_ENTRY_#42");
        PqcSignature signature = provider.SignData(auditPayload, signingKey.PrivateKeyBase64!, "node_sign_01");

        Assert.Equal("ML-DSA-65", signature.Algorithm);
        Assert.True(provider.VerifySignature(auditPayload, signature.SignatureBase64, signingKey.PublicKeyBase64));
        Assert.False(provider.VerifySignature(Encoding.UTF8.GetBytes("Tampered Payload"), signature.SignatureBase64, signingKey.PublicKeyBase64));

        // Test boundary / failure branches
        Assert.Throws<ArgumentException>(() => provider.EncryptEnvelope(plaintext, "", "key"));
        Assert.Throws<ArgumentException>(() => provider.EncryptEnvelope(plaintext, kemKey.PublicKeyBase64, ""));
        Assert.Throws<ArgumentNullException>(() => provider.DecryptEnvelope(null!, kemKey.PrivateKeyBase64!));
        Assert.Throws<ArgumentException>(() => provider.DecryptEnvelope(envelope, ""));
        Assert.Throws<ArgumentException>(() => provider.SignData(auditPayload, "", "signer"));
        Assert.Throws<ArgumentException>(() => provider.SignData(auditPayload, signingKey.PrivateKeyBase64!, ""));

        Assert.False(provider.VerifySignature(auditPayload, "", signingKey.PublicKeyBase64));
        Assert.False(provider.VerifySignature(auditPayload, signature.SignatureBase64, ""));
        Assert.False(provider.VerifySignature(auditPayload, "invalid_base64_sig!@#", signingKey.PublicKeyBase64));
    }

    /// <summary>
    /// Verifies that AES-GCM authentication detects tampered ciphertexts.
    /// </summary>
    [Fact]
    public void PostQuantumCryptoProvider_TamperedCiphertext_ThrowsCryptographicException()
    {
        PostQuantumCryptoProvider provider = new();
        PqcKeyMaterial kemKey = provider.GenerateKemKeyPair("subj_tamper");

        byte[] plaintext = Encoding.UTF8.GetBytes("Original confidential data");
        EncryptedEnvelope envelope = provider.EncryptEnvelope(plaintext, kemKey.PublicKeyBase64, "subj_tamper");

        byte[] rawCiphertext = Convert.FromBase64String(envelope.CiphertextBase64);
        rawCiphertext[0] ^= 0xFF; // Invert first byte

        EncryptedEnvelope tampered = envelope with
        {
            CiphertextBase64 = Convert.ToBase64String(rawCiphertext)
        };

        Assert.Throws<AuthenticationTagMismatchException>(() => provider.DecryptEnvelope(tampered, kemKey.PrivateKeyBase64!));
    }

    /// <summary>
    /// Verifies key management lifecycle, subject isolation, and GDPR Article 17 crypto-shredding.
    /// </summary>
    [Fact]
    public async Task KeyManagementService_CryptoShredding_PermanentlyDestroysDecryptionCapability()
    {
        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_testRoot, "kms_unit"));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!, host.Vault, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, null!, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, host.Vault, null!));
        KeyManagementService keyMgmt = host.KeyManagement;

        Assert.NotNull(keyMgmt.GetNodeSigningKey());
        Assert.Null(keyMgmt.GetNodeSigningKey().PrivateKeyBase64);

        string subjectId = "sub_patient_alpha";
        SubjectKeyProfile profile = await keyMgmt.GetOrCreateSubjectKeyAsync(subjectId);
        Assert.Equal(subjectId, profile.SubjectId);
        Assert.False(profile.IsShredded);
        Assert.NotNull(profile.KeyMaterial.PrivateKeyBase64);

        // Same call returns existing profile
        SubjectKeyProfile profile2 = await keyMgmt.GetOrCreateSubjectKeyAsync(subjectId);
        Assert.Equal(profile.KeyMaterial.PublicKeyBase64, profile2.KeyMaterial.PublicKeyBase64);

        // GetSubjectKey lookup
        Assert.NotNull(keyMgmt.GetSubjectKey(subjectId));
        Assert.Null(keyMgmt.GetSubjectKey("non_existent_subject"));
        Assert.Null(keyMgmt.GetSubjectKey(""));

        // Boundary tests for GetOrCreateSubjectKeyAsync
        await Assert.ThrowsAsync<ArgumentException>(() => keyMgmt.GetOrCreateSubjectKeyAsync(""));

        // Execute Crypto-Shredding
        Assert.Equal(SubjectErasureOutcome.Erased, await keyMgmt.CryptoShredSubjectKeyAsync(subjectId));

        SubjectKeyProfile shreddedProfile = keyMgmt.GetSubjectKey(subjectId)!;
        Assert.True(shreddedProfile.IsShredded);
        Assert.Null(shreddedProfile.KeyMaterial.PrivateKeyBase64);
        Assert.NotNull(shreddedProfile.ShreddedAtUtc);

        var ex = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => keyMgmt.GetOrCreateSubjectKeyAsync(subjectId));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, ex.Reason);

        // Second shred returns AlreadyErased; unknown or whitespace returns UnknownSubject
        Assert.Equal(SubjectErasureOutcome.AlreadyErased, await keyMgmt.CryptoShredSubjectKeyAsync(subjectId));
        Assert.Equal(SubjectErasureOutcome.UnknownSubject, await keyMgmt.CryptoShredSubjectKeyAsync("non_existent"));
        Assert.Equal(SubjectErasureOutcome.UnknownSubject, await keyMgmt.CryptoShredSubjectKeyAsync(""));

        // Verify profiles list contains subject
        var all = keyMgmt.GetAllSubjectProfiles();
        Assert.Contains(all, p => p.SubjectId == subjectId && p.IsShredded);
    }

    /// <summary>
    /// Verifies FDA 21 CFR Part 11 audit ledger hash chaining and tamper detection.
    /// </summary>
    [Fact]
    public void ComplianceAuditService_LedgerIntegrityAndTamperDetection_WorksCorrectly()
    {
        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_testRoot, "ledger_unit"));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!, host.Vault, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, null!, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, host.Vault, null!));
        KeyManagementService keyMgmt = host.KeyManagement;
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(null!, provider));
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(keyMgmt, null!));
        ComplianceAuditService audit = new(keyMgmt, provider);

        // Genesis record exists
        var records = audit.GetAuditRecords(100);
        Assert.NotEmpty(records);
        Assert.Equal(0, records[^1].SequenceNumber);
        Assert.Equal("GenesisAuditLedger", records[^1].Action);

        // Append events
        audit.LogAction("USER_01", "ScreenGenomics", "scaffold_01", "{\"gc\":42.5}");
        audit.LogAction("USER_02", "RecalibrateKinetics", "endocrine_hub", "{\"keff\":0.068}");

        AuditLedgerVerificationResult initialVerify = audit.VerifyLedgerIntegrity();
        Assert.True(initialVerify.IsValid);
        Assert.Equal(3, initialVerify.TotalRecordsVerified);
        Assert.Null(initialVerify.FirstViolationSequence);

        // Boundary tests on LogAction
        Assert.Throws<ArgumentException>(() => audit.LogAction("", "Action", "Res", "{}"));
        Assert.Throws<ArgumentException>(() => audit.LogAction("Op", "", "Res", "{}"));
        Assert.Throws<ArgumentException>(() => audit.LogAction("Op", "Action", "", "{}"));
        var nullDetailsRec = audit.LogAction("Op", "Action", "Res", "");
        Assert.Equal("{}", nullDetailsRec.DetailsJson);

        // Verify GetAuditRecords limits
        Assert.NotEmpty(audit.GetAuditRecords(-5));
        Assert.NotEmpty(audit.GetAuditRecords(2000));

        // Test tamper detection by modifying a private record using reflection
        FieldInfo recordsField = typeof(ComplianceAuditService).GetField("_records", BindingFlags.NonPublic | BindingFlags.Instance)!;
        List<AuditRecord> list = (List<AuditRecord>)recordsField.GetValue(audit)!;

        // Tamper test 1: Sequence jump
        AuditRecord originalRec1 = list[1];
        list[1] = originalRec1 with { SequenceNumber = 99 };
        AuditLedgerVerificationResult seqTamper = audit.VerifyLedgerIntegrity();
        Assert.False(seqTamper.IsValid);
        Assert.Contains("Sequence discontinuity", seqTamper.Message);
        ComplianceStatusReport degradedStatus = audit.GetComplianceStatus();
        Assert.Equal("Degraded", degradedStatus.OverallStatus);
        Assert.False(degradedStatus.IsAuditLedgerIntact);
        list[1] = originalRec1;

        // Tamper test 2: Broken previousHash link
        list[1] = originalRec1 with { PreviousHash = "000000000000000000000000000000000000000000000000" };
        AuditLedgerVerificationResult linkTamper = audit.VerifyLedgerIntegrity();
        Assert.False(linkTamper.IsValid);
        Assert.Contains("Broken hash link", linkTamper.Message);
        list[1] = originalRec1;

        // Tamper test 3: Tampered action / CurrentHash mismatch
        list[1] = originalRec1 with { Action = "TamperedAction" };
        AuditLedgerVerificationResult payloadTamper = audit.VerifyLedgerIntegrity();
        Assert.False(payloadTamper.IsValid);
        Assert.Contains("Tampered audit payload", payloadTamper.Message);
        list[1] = originalRec1;

        // Tamper test 4: Invalid signature
        list[1] = originalRec1 with
        {
            Signature = originalRec1.Signature with
            {
                SignatureBase64 = Convert.ToBase64String(new byte[64])
            }
        };
        AuditLedgerVerificationResult sigTamper = audit.VerifyLedgerIntegrity();
        Assert.False(sigTamper.IsValid);
        Assert.Contains("Invalid ML-DSA-65 signature", sigTamper.Message);
        list[1] = originalRec1;
    }

    /// <summary>
    /// Verifies HIPAA Safe Harbor de-identification, identifier stripping, and GINA/GDPR consent validation.
    /// </summary>
    [Fact]
    public void ComplianceAuditService_DeIdentifySubject_EnforcesSafeHarborAndConsent()
    {
        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_testRoot, "deid_unit"));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!, host.Vault, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, null!, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, host.Vault, null!));
        KeyManagementService keyMgmt = host.KeyManagement;
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(null!, provider));
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(keyMgmt, null!));
        ComplianceAuditService audit = new(keyMgmt, provider);

        string rawPatient = "Jane Doe (Hospital-MRN-98432)";
        string rawFasta = ">Patient: Jane Doe, MRN: 98432, Clinic: St Jude\nATGCGATCGATCGATC\n>Control: Ref-Seq\nGCTAGCTAGCTA";

        // Successful de-identification
        DeIdentificationResult result = audit.DeIdentifySubject(rawPatient, rawFasta, gdprConsentConfirmed: true, ginaPurposeLimited: true);
        Assert.StartsWith("sub_", result.PseudonymSubjectId);
        Assert.True(result.GdprConsentConfirmed);
        Assert.True(result.GinaSafeguardVerified);
        Assert.NotEmpty(result.StrippedIdentifiers);
        Assert.Contains("sub_", result.SanitizedData);
        Assert.DoesNotContain("Jane Doe", result.SanitizedData);

        // Consent violation branches
        Assert.Throws<InvalidOperationException>(() => audit.DeIdentifySubject(rawPatient, rawFasta, gdprConsentConfirmed: false, ginaPurposeLimited: true));
        Assert.Throws<InvalidOperationException>(() => audit.DeIdentifySubject(rawPatient, rawFasta, gdprConsentConfirmed: true, ginaPurposeLimited: false));

        // Argument validation
        Assert.Throws<ArgumentException>(() => audit.DeIdentifySubject("", rawFasta, true, true));
        Assert.Throws<ArgumentNullException>(() => audit.DeIdentifySubject(rawPatient, null!, true, true));

        // Compliance status report
        ComplianceStatusReport status = audit.GetComplianceStatus();
        Assert.Equal("Compliant", status.OverallStatus);
        Assert.True(status.IsAuditLedgerIntact);
        Assert.True(status.IsEncryptionAtRestActive);
    }

    /// <summary>
    /// Verifies encrypted storage service record encryption, crypto-shredding enforcement, and trajectory bundle export.
    /// </summary>
    [Fact]
    public async Task EncryptedStorageService_RecordAndDatasetEncryption_Succeeds()
    {
        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_testRoot, "storage_unit"));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!, host.Vault, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, null!, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, host.Vault, null!));
        KeyManagementService keyMgmt = host.KeyManagement;
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(null!, provider));
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(keyMgmt, null!));
        ComplianceAuditService audit = new(keyMgmt, provider);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _sharedDataDir,
                ["TRAJECTORY_EXPORT_DIR"] = _exportDir
            })
            .Build();

        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(null!, provider, audit, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, null!, audit, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, provider, null!, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, provider, audit, null!));

        // Test configuration fallbacks
        var configSharedOnly = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedDataPath"] = _sharedDataDir
            })
            .Build();
        EncryptedStorageService storageShared = new(keyMgmt, provider, audit, configSharedOnly);
        Assert.NotNull(storageShared);

        var configDefault = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        EncryptedStorageService storageDefault = new(keyMgmt, provider, audit, configDefault);
        Assert.NotNull(storageDefault);

        EncryptedStorageService storage = new(keyMgmt, provider, audit, config);

        string subjectId = "sub_storage_test";
        var record = new { Treatment = "MtF", TargetE2 = 550, TargetT = 1.2 };

        EncryptedEnvelope envelope = await storage.EncryptSubjectRecordAsync(subjectId, record);
        Assert.Equal("ML-KEM-768+AES-256-GCM", envelope.Algorithm);

        var decrypted = storage.DecryptSubjectRecord<JsonElement>(subjectId, envelope);
        Assert.Equal(550, decrypted.GetProperty("TargetE2").GetInt32());

        // Export encrypted trajectory bundle
        string bundlePath = await storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", subjectId, _exportDir);
        Assert.True(File.Exists(bundlePath));
        Assert.EndsWith(".enc.tar.gz", bundlePath);

        // Crypto-Shred subject and verify decryption is permanently blocked
        Assert.Equal(SubjectErasureOutcome.Erased, await keyMgmt.CryptoShredSubjectKeyAsync(subjectId));
        var encErased = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => storage.EncryptSubjectRecordAsync(subjectId, record));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, encErased.Reason);
        var decErased = Assert.Throws<SubjectKeyUnavailableException>(() => storage.DecryptSubjectRecord<JsonElement>(subjectId, envelope));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, decErased.Reason);
        // Erased always carries the timestamp.
        Assert.Equal(SubjectKeyUnavailableException.ErasedMessage(subjectId, decErased.ShreddedAtUtc!.Value), decErased.Message);
        var expErased = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", subjectId, _exportDir));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, expErased.Reason);

        // Boundary tests
        await Assert.ThrowsAsync<ArgumentException>(() => storage.EncryptSubjectRecordAsync<object>("", record));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.EncryptSubjectRecordAsync<object>("valid", null!));
        Assert.Throws<ArgumentException>(() => storage.DecryptSubjectRecord<object>("", envelope));
        Assert.Throws<ArgumentNullException>(() => storage.DecryptSubjectRecord<object>("valid", null!));
        var unknownEx = Assert.Throws<SubjectKeyUnavailableException>(() => storage.DecryptSubjectRecord<object>("unknown_subj", envelope));
        Assert.Equal(SubjectKeyUnavailableReason.UnknownSubject, unknownEx.Reason);
        Assert.DoesNotContain("Article 17", unknownEx.Message);
        Assert.DoesNotContain("shred", unknownEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destroy", unknownEx.Message, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.ExportEncryptedDatasetBundleAsync("", "sub", _exportDir));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", "", _exportDir));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", "sub", ""));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.ExportEncryptedDatasetBundleAsync("non_existent_trajectory.xyz", "sub", _exportDir));
    }

    /// <summary>
    /// Verifies all Compliance and PQC REST API endpoints.
    /// </summary>
    [Fact]
    public async Task ComplianceEndpoints_HttpRequests_ReturnExpectedResponses()
    {
        // 1. GET /api/compliance/status
        var statusResp = await _client.GetAsync("/api/compliance/status");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        var status = await statusResp.Content.ReadFromJsonAsync<ComplianceStatusReport>();
        Assert.NotNull(status);
        Assert.Equal("ML-KEM-768 + AES-256-GCM (NIST FIPS 203)", status.EncryptionAtRestAlgorithm);
        Assert.Equal("Compliant", status.OverallStatus);

        // 2. GET /api/compliance/audit-ledger
        var ledgerResp = await _client.GetAsync("/api/compliance/audit-ledger?limit=10");
        Assert.Equal(HttpStatusCode.OK, ledgerResp.StatusCode);
        var records = await ledgerResp.Content.ReadFromJsonAsync<List<AuditRecord>>();
        Assert.NotNull(records);
        Assert.NotEmpty(records);

        // 3. GET /api/compliance/verify-ledger
        var verifyResp = await _client.GetAsync("/api/compliance/verify-ledger");
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        var verify = await verifyResp.Content.ReadFromJsonAsync<AuditLedgerVerificationResult>();
        Assert.NotNull(verify);
        Assert.True(verify.IsValid);

        // 4. POST /api/compliance/de-identify
        var deIdReq = new DeIdentifyRequest
        {
            RawSubjectIdentifier = "Alex Smith (Hospital-XYZ)",
            RawGenomicData = ">Patient: Alex Smith\nATGCGATCGATC",
            GdprConsentConfirmed = true,
            GinaPurposeLimited = true
        };
        var deIdResp = await _client.PostAsJsonAsync("/api/compliance/de-identify", deIdReq);
        Assert.Equal(HttpStatusCode.OK, deIdResp.StatusCode);
        var deIdResult = await deIdResp.Content.ReadFromJsonAsync<DeIdentificationResult>();
        Assert.NotNull(deIdResult);
        Assert.StartsWith("sub_", deIdResult.PseudonymSubjectId);

        // Error branches on de-identify
        var badDeId1 = await _client.PostAsJsonAsync("/api/compliance/de-identify", new DeIdentifyRequest { RawSubjectIdentifier = "" });
        Assert.Equal(HttpStatusCode.BadRequest, badDeId1.StatusCode);
        var badDeId2 = await _client.PostAsJsonAsync("/api/compliance/de-identify", new DeIdentifyRequest { RawSubjectIdentifier = "Valid", GdprConsentConfirmed = false });
        Assert.Equal(HttpStatusCode.BadRequest, badDeId2.StatusCode);

        // 5. POST /api/compliance/encrypt-record
        string subId = deIdResult.PseudonymSubjectId;
        using var jsonDoc = JsonDocument.Parse("{\"regimen\":\"MaleToFemale\",\"target_e2\":520}");
        var encReq = new EncryptRecordRequest
        {
            SubjectId = subId,
            Payload = jsonDoc.RootElement.Clone()
        };
        var encResp = await _client.PostAsJsonAsync("/api/compliance/encrypt-record", encReq);
        Assert.Equal(HttpStatusCode.OK, encResp.StatusCode);
        var envelope = await encResp.Content.ReadFromJsonAsync<EncryptedEnvelope>();
        Assert.NotNull(envelope);
        Assert.Equal("ML-KEM-768+AES-256-GCM", envelope.Algorithm);

        var badEnc = await _client.PostAsJsonAsync("/api/compliance/encrypt-record", new EncryptRecordRequest { SubjectId = "" });
        Assert.Equal(HttpStatusCode.BadRequest, badEnc.StatusCode);

        // 6. POST /api/compliance/decrypt-record
        var decReq = new DecryptRecordRequest
        {
            SubjectId = subId,
            Envelope = envelope
        };
        var decResp = await _client.PostAsJsonAsync("/api/compliance/decrypt-record", decReq);
        Assert.Equal(HttpStatusCode.OK, decResp.StatusCode);
        var decryptedPayload = await decResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(520, decryptedPayload.GetProperty("target_e2").GetInt32());

        var badDec = await _client.PostAsJsonAsync("/api/compliance/decrypt-record", new DecryptRecordRequest { SubjectId = "" });
        Assert.Equal(HttpStatusCode.BadRequest, badDec.StatusCode);

        // 7. POST /api/datasets/export-encrypted
        var exportReq = new ExportEncryptedDatasetRequest
        {
            TrajectoryFileName = "dna_nanotube_sample.xyz",
            SubjectId = subId,
            ExportDirectory = _exportDir
        };
        var exportResp = await _client.PostAsJsonAsync("/api/datasets/export-encrypted", exportReq);
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);

        // Export error branches
        var badExport1 = await _client.PostAsJsonAsync("/api/datasets/export-encrypted", new ExportEncryptedDatasetRequest { TrajectoryFileName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, badExport1.StatusCode);
        var badExport2 = await _client.PostAsJsonAsync("/api/datasets/export-encrypted", new ExportEncryptedDatasetRequest { TrajectoryFileName = "missing_file.xyz" });
        Assert.Equal(HttpStatusCode.NotFound, badExport2.StatusCode);

        // 8. POST /api/compliance/crypto-shred
        var shredResp = await _client.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = subId });
        Assert.Equal(HttpStatusCode.OK, shredResp.StatusCode);

        // Second shred returns 404 (already shredded)
        var shredAgain = await _client.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = subId });
        Assert.Equal(HttpStatusCode.NotFound, shredAgain.StatusCode);

        var badShred = await _client.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = "" });
        Assert.Equal(HttpStatusCode.BadRequest, badShred.StatusCode);

        // Now encrypt/decrypt/export on shredded subject returns BadRequest
        var encShredded = await _client.PostAsJsonAsync("/api/compliance/encrypt-record", encReq);
        Assert.Equal(HttpStatusCode.BadRequest, encShredded.StatusCode);

        var decShredded = await _client.PostAsJsonAsync("/api/compliance/decrypt-record", decReq);
        Assert.Equal(HttpStatusCode.BadRequest, decShredded.StatusCode);

        var exportShredded = await _client.PostAsJsonAsync("/api/datasets/export-encrypted", exportReq);
        Assert.Equal(HttpStatusCode.BadRequest, exportShredded.StatusCode);
    }

    /// <summary>
    /// Verifies the Blazor UI ComplianceSecurity razor component lifecycle and action methods.
    /// </summary>
    [Fact]
    public async Task ComplianceSecurityRazorComponent_MethodsAndLifecycle_ExecuteCleanly()
    {
        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_testRoot, "razor_unit"));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!, host.Vault, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, null!, host.SecretStore));
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(provider, host.Vault, null!));
        KeyManagementService keyMgmt = host.KeyManagement;
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(null!, provider));
        Assert.Throws<ArgumentNullException>(() => new ComplianceAuditService(keyMgmt, null!));
        ComplianceAuditService audit = new(keyMgmt, provider);

        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(null!, provider, audit, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, null!, audit, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, provider, null!, config));
        Assert.Throws<ArgumentNullException>(() => new EncryptedStorageService(keyMgmt, provider, audit, null!));

        // Test configuration fallbacks
        var configSharedOnly = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedDataPath"] = _sharedDataDir
            })
            .Build();
        EncryptedStorageService storageShared = new(keyMgmt, provider, audit, configSharedOnly);
        Assert.NotNull(storageShared);

        var configDefault = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        EncryptedStorageService storageDefault = new(keyMgmt, provider, audit, configDefault);
        Assert.NotNull(storageDefault);

        EncryptedStorageService storage = new(keyMgmt, provider, audit, config);

        ComplianceSecurity page = new();

        // Inject properties via reflection
        typeof(ComplianceSecurity).GetProperty("ComplianceService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, audit);
        typeof(ComplianceSecurity).GetProperty("KeyManagementService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, keyMgmt);
        typeof(ComplianceSecurity).GetProperty("StorageService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, storage);

        MethodInfo onInit = typeof(ComplianceSecurity).GetMethod("OnInitialized", BindingFlags.NonPublic | BindingFlags.Instance)!;
        onInit.Invoke(page, null);

        MethodInfo verifyLedger = typeof(ComplianceSecurity).GetMethod("VerifyLedger", BindingFlags.NonPublic | BindingFlags.Instance)!;
        verifyLedger.Invoke(page, null);

        MethodInfo executeDeId = typeof(ComplianceSecurity).GetMethod("ExecuteDeIdentification", BindingFlags.NonPublic | BindingFlags.Instance)!;
        executeDeId.Invoke(page, null);

        MethodInfo executeShred = typeof(ComplianceSecurity).GetMethod("ExecuteCryptoShred", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // ExecuteCryptoShred returns Task, so Invoke returns non-null.
        await (Task)executeShred.Invoke(page, null)!;

        // Test shredding empty string
        typeof(ComplianceSecurity).GetField("_targetShredSubjectId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "");
        // ExecuteCryptoShred returns Task, so Invoke returns non-null.
        await (Task)executeShred.Invoke(page, null)!;

        // Test exception branch in DeIdentification
        typeof(ComplianceSecurity).GetField("_consentGdpr", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
        executeDeId.Invoke(page, null);
    }

    /// <summary>
    /// Verifies that <see cref="Migration009_AuditLedgerStore"/> emits correct DDL for SQLite and PostgreSQL,
    /// adheres to Version 9 naming conventions, and enforces argument validation.
    /// </summary>
    [Fact]
    public void Migration009_MetadataAndDialectEmission_AreDeterministic()
    {
        // WHAT: Checks Migration009_AuditLedgerStore metadata and SQL dialect emission.
        // WHY:  Guarantees version 9 is registered, idempotent, and produces dialect-compatible DDL.
        // HOW:  Inspects Version, Name, ArgumentNullException on null, and dialect strings for SQLite and PostgreSQL.
        Migration009_AuditLedgerStore migration = new();
        Assert.Equal(9L, migration.Version);
        Assert.Equal("009_AuditLedgerStore", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string sqliteSql = migration.GetSql("sqlite");
        string pgSql = migration.GetSql("postgresql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_audit_ledger", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("sequence_number", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_audit_ledger", pgSql, StringComparison.Ordinal);
        Assert.Contains("TIMESTAMPTZ", pgSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that <see cref="AuditLedgerRepository"/> enforces constructor and argument null checks.
    /// </summary>
    [Fact]
    public void AuditLedgerRepository_ValidationAndNullGuards_ThrowExpectedExceptions()
    {
        // WHAT: Tests null guards on AuditLedgerRepository constructor and Append method.
        // WHY:  Protects against improper DI configuration or null argument propagation.
        // HOW:  Invokes constructor with null, and Append with null record and null signature.
        Assert.Throws<ArgumentNullException>(() => new AuditLedgerRepository(null!));

        IConfiguration config = new ConfigurationBuilder().Build();
        IWeaverDbConnectionFactory factory = WeaverTestDb.CreateFactory(config);
        AuditLedgerRepository repo = new(factory);

        Assert.Throws<ArgumentNullException>(() => repo.Append(null!));
        Assert.Throws<ArgumentNullException>(() => repo.Append(new AuditRecord { Signature = null! }));
    }

    /// <summary>
    /// Verifies that audit ledger records and their cryptographic hash chain persist across service instances
    /// when backed by a real <see cref="AuditLedgerRepository"/>.
    /// </summary>
    [Fact]
    public void AuditLedger_PersistsAcrossRestarts_AndMaintainsIntegrity()
    {
        // WHAT: Tests that ComplianceAuditService restores committed records across restarts using AuditLedgerRepository.
        // WHY:  Mandated by FDA 21 CFR Part 11 so audit history survives service restarts without losing hash-chain continuity.
        // HOW:  Creates a SQLite database, logs 2 actions in instance 1, instantiates instance 2 against the same DB, logs 1 action,
        //       and asserts the restored ledger contains 4 records (Genesis + 3 actions) with valid hash chain and ML-DSA-65 signatures.
        string testDbDir = Path.Combine(_testRoot, "audit_restart_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDbDir);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = testDbDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        DatabaseMigrationRunner runner = new();
        IWeaverDbConnectionFactory connectionFactory = WeaverTestDb.CreateFactory(config, runner);
        AuditLedgerRepository repository = new(connectionFactory);

        PostQuantumCryptoProvider provider = new();
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(testDbDir, "keys"));
        KeyManagementService keyMgmt = host.KeyManagement;

        // Instance 1: Initial creation mints Genesis (0) and persists it, then logs actions 1 and 2
        ComplianceAuditService service1 = new(keyMgmt, provider, repository: repository);
        AuditRecord rec1 = service1.LogAction("op1", "Action1", "Resource1", "{\"step\":1}");
        Assert.Equal(1L, rec1.SequenceNumber);
        AuditRecord rec2 = service1.LogAction("op2", "Action2", "Resource2", "{\"step\":2}");
        Assert.Equal(2L, rec2.SequenceNumber);

        Assert.Equal(3, service1.GetAuditRecords(100).Count);
        Assert.True(service1.VerifyLedgerIntegrity().IsValid);

        // Instance 2: Restarts against the same repository without re-minting Genesis
        ComplianceAuditService service2 = new(keyMgmt, provider, repository: repository);
        Assert.Equal(3, service2.GetAuditRecords(100).Count);

        AuditRecord rec3 = service2.LogAction("op3", "Action3", "Resource3", "{\"step\":3}");
        Assert.Equal(3L, rec3.SequenceNumber);

        IReadOnlyList<AuditRecord> records = service2.GetAuditRecords(100);
        Assert.Equal(4, records.Count);

        AuditLedgerVerificationResult verification = service2.VerifyLedgerIntegrity();
        Assert.True(verification.IsValid);
        Assert.Equal(4, verification.TotalRecordsVerified);
    }
}
