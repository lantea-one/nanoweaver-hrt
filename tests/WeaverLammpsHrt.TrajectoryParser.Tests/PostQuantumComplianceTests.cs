using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Compliance;
using WeaverLammpsHrt.TrajectoryParser.Components.Pages;
using WeaverLammpsHrt.TrajectoryParser.Security.Cryptography;
using WeaverLammpsHrt.TrajectoryParser.Security.KeyManagement;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;
using WeaverLammpsHrt.TrajectoryParser.Security.Storage;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Unit and integration test suite verifying Post-Quantum Cryptography (NIST FIPS 203 &amp; 204),
/// HIPAA Safe Harbor de-identification, GDPR Article 17 crypto-shredding, and FDA 21 CFR Part 11 audit trails.
/// </summary>
public sealed class PostQuantumComplianceTests : IClassFixture<WebApplicationFactory<Program>>
{
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
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string solutionRoot = current ?? Directory.GetCurrentDirectory();
        _sharedDataDir = Path.Combine(solutionRoot, "simulations", "shared-data");
        _exportDir = Path.Combine(solutionRoot, "simulations", "exports");

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
    public void KeyManagementService_CryptoShredding_PermanentlyDestroysDecryptionCapability()
    {
        PostQuantumCryptoProvider provider = new();
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!));
        KeyManagementService keyMgmt = new(provider);

        Assert.NotNull(keyMgmt.GetNodeSigningKey());
        Assert.NotNull(keyMgmt.GetNodeMasterKey());

        string subjectId = "sub_patient_alpha";
        SubjectKeyProfile profile = keyMgmt.GetOrCreateSubjectKey(subjectId);
        Assert.Equal(subjectId, profile.SubjectId);
        Assert.False(profile.IsShredded);
        Assert.NotNull(profile.KeyMaterial.PrivateKeyBase64);

        // Same call returns existing profile
        SubjectKeyProfile profile2 = keyMgmt.GetOrCreateSubjectKey(subjectId);
        Assert.Equal(profile.KeyMaterial.PublicKeyBase64, profile2.KeyMaterial.PublicKeyBase64);

        // GetSubjectKey lookup
        Assert.NotNull(keyMgmt.GetSubjectKey(subjectId));
        Assert.Null(keyMgmt.GetSubjectKey("non_existent_subject"));
        Assert.Null(keyMgmt.GetSubjectKey(""));

        // Boundary tests for GetOrCreateSubjectKey
        Assert.Throws<ArgumentException>(() => keyMgmt.GetOrCreateSubjectKey(""));

        // Execute Crypto-Shredding
        bool shredded = keyMgmt.CryptoShredSubjectKey(subjectId);
        Assert.True(shredded);

        SubjectKeyProfile shreddedProfile = keyMgmt.GetSubjectKey(subjectId)!;
        Assert.True(shreddedProfile.IsShredded);
        Assert.Null(shreddedProfile.KeyMaterial.PrivateKeyBase64);
        Assert.NotNull(shreddedProfile.ShreddedAtUtc);

        // Second shred returns false
        Assert.False(keyMgmt.CryptoShredSubjectKey(subjectId));
        Assert.False(keyMgmt.CryptoShredSubjectKey("non_existent"));
        Assert.False(keyMgmt.CryptoShredSubjectKey(""));

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
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!));
        KeyManagementService keyMgmt = new(provider);
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
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!));
        KeyManagementService keyMgmt = new(provider);
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
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!));
        KeyManagementService keyMgmt = new(provider);
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

        EncryptedEnvelope envelope = storage.EncryptSubjectRecord(subjectId, record);
        Assert.Equal("ML-KEM-768+AES-256-GCM", envelope.Algorithm);

        var decrypted = storage.DecryptSubjectRecord<JsonElement>(subjectId, envelope);
        Assert.Equal(550, decrypted.GetProperty("TargetE2").GetInt32());

        // Export encrypted trajectory bundle
        string bundlePath = await storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", subjectId, _exportDir);
        Assert.True(File.Exists(bundlePath));
        Assert.EndsWith(".enc.tar.gz", bundlePath);

        // Crypto-Shred subject and verify decryption is permanently blocked
        keyMgmt.CryptoShredSubjectKey(subjectId);
        Assert.Throws<CryptographicException>(() => storage.EncryptSubjectRecord(subjectId, record));
        Assert.Throws<CryptographicException>(() => storage.DecryptSubjectRecord<JsonElement>(subjectId, envelope));
        await Assert.ThrowsAsync<CryptographicException>(() => storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", subjectId, _exportDir));

        // Boundary tests
        Assert.Throws<ArgumentException>(() => storage.EncryptSubjectRecord<object>("", record));
        Assert.Throws<ArgumentNullException>(() => storage.EncryptSubjectRecord<object>("valid", null!));
        Assert.Throws<ArgumentException>(() => storage.DecryptSubjectRecord<object>("", envelope));
        Assert.Throws<ArgumentNullException>(() => storage.DecryptSubjectRecord<object>("valid", null!));
        Assert.Throws<CryptographicException>(() => storage.DecryptSubjectRecord<object>("unknown_subj", envelope));

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
    public void ComplianceSecurityRazorComponent_MethodsAndLifecycle_ExecuteCleanly()
    {
        PostQuantumCryptoProvider provider = new();
        Assert.Throws<ArgumentNullException>(() => new KeyManagementService(null!));
        KeyManagementService keyMgmt = new(provider);
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
        executeShred.Invoke(page, null);

        // Test shredding empty string
        typeof(ComplianceSecurity).GetField("_targetShredSubjectId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "");
        executeShred.Invoke(page, null);

        // Test exception branch in DeIdentification
        typeof(ComplianceSecurity).GetField("_consentGdpr", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
        executeDeId.Invoke(page, null);
    }
}
