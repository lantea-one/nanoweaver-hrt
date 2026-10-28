using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Services;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Acceptance tests for Work Package P9 Stage B: <see cref="PeerKeyValidation"/>, the three
/// <c>/api/peer-keys</c> administrator endpoints, role gating, zero-disclosure guarantees,
/// configuration exposure, and OpenAPI schema generation (Criteria 10–16, 32, and 35).
/// </summary>
public sealed class PeerKeyEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Base application factory fixture.
    /// </summary>
    private readonly WebApplicationFactory<Program> _baseFactory;

    /// <summary>
    /// Root temporary directory for this test class instance.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_peerkey_ep_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerKeyEndpointTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base web application factory fixture.</param>
    public PeerKeyEndpointTests(WebApplicationFactory<Program> baseFactory)
    {
        ArgumentNullException.ThrowIfNull(baseFactory);
        _baseFactory = baseFactory;
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
                // Best-effort cleanup of temp directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temp directory.
            }
        }
    }

    /// <summary>
    /// Verifies criteria 10, 11, and 12: <see cref="PeerKeyValidation.TryNormalizeLabel"/> and
    /// <see cref="PeerKeyValidation.TryNormalizePublicKey"/> accept valid inputs and reject all malformed,
    /// wrong-algorithm, wrong-parameter-set, and private-key inputs.
    /// </summary>
    [Fact]
    public void Criteria10To12_PeerKeyValidation_NormalizesAndRejectsAllBoundaryInputs()
    {
        // WHAT: Tests TryNormalizeLabel and TryNormalizePublicKey across all valid and invalid boundary cases.
        // WHY:  Proves pure step-4-visible validation of labels and SubjectPublicKeyInfo DER keys.
        // HOW:  Covers null, empty, whitespace, 1/128/129 chars, control chars, PEM, non-Base64, random DER,
        //       algorithm swaps, ML-DSA-44 parameter mismatch, and Ed25519 PKCS#8 private keys.

        // Criterion 10: TryNormalizeLabel
        Assert.True(PeerKeyValidation.TryNormalizeLabel("  A  ", out string oneChar));
        Assert.Equal("A", oneChar);

        string maxLabel = new('Z', PeerKeyValidation.MaxLabelLength);
        Assert.True(PeerKeyValidation.TryNormalizeLabel(" " + maxLabel + " ", out string normMax));
        Assert.Equal(maxLabel, normMax);

        Assert.False(PeerKeyValidation.TryNormalizeLabel(null, out _));
        Assert.False(PeerKeyValidation.TryNormalizeLabel(string.Empty, out _));
        Assert.False(PeerKeyValidation.TryNormalizeLabel("   \t  ", out _));
        Assert.False(PeerKeyValidation.TryNormalizeLabel(new string('X', PeerKeyValidation.MaxLabelLength + 1), out _));
        Assert.False(PeerKeyValidation.TryNormalizeLabel("Valid\nInvalid", out _));

        // Criterion 11: Genuine Ed25519 and ML-DSA-65 SPKI acceptance and canonical Base64 re-encoding
        var (edSpkiDer, edPkcs8Base64) = GenerateEd25519SpkiAndPkcs8();
        string edCanonical = Convert.ToBase64String(edSpkiDer);
        string edWithWhitespace = "  " + edCanonical[..12] + "\r\n  " + edCanonical[12..] + "  ";

        Assert.True(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, edCanonical, out string normEd1, out string keyIdEd1));
        Assert.True(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, edWithWhitespace, out string normEd2, out string keyIdEd2));
        Assert.Equal(edCanonical, normEd1);
        Assert.Equal(edCanonical, normEd2);
        Assert.Equal(DatasetManifestSignature.ComputeKeyId(edSpkiDer), keyIdEd1);
        Assert.Equal(keyIdEd1, keyIdEd2);

        PostQuantumCryptoProvider pqc = new();
        PqcKeyMaterial mlDsa65 = pqc.GenerateSigningKeyPair("peer_mldsa65");
        byte[] mlDsa65Der = Convert.FromBase64String(mlDsa65.PublicKeyBase64);
        Assert.True(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, mlDsa65.PublicKeyBase64, out string normMl, out string keyIdMl));
        Assert.Equal(mlDsa65.PublicKeyBase64, normMl);
        Assert.Equal(DatasetManifestSignature.ComputeKeyId(mlDsa65Der), keyIdMl);

        // Criterion 12: Rejections
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, null, out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, "   ", out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, new string('A', PeerKeyValidation.MaxPublicKeyBase64Length + 1), out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, $"-----BEGIN PUBLIC KEY-----\n{edCanonical}\n-----END PUBLIC KEY-----", out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, "%%%not_base64%%%", out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }), out _, out _));

        // Algorithm swaps
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, edCanonical, out _, out _));
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, mlDsa65.PublicKeyBase64, out _, out _));

        // ML-DSA-44 key declared as MlDsa65
        string mlDsa44SpkiBase64 = GenerateMlDsa44SpkiBase64();
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, mlDsa44SpkiBase64, out _, out _));

        // Ed25519 PKCS#8 private key (starts with MC4CAQAwBQYDK2Vw)
        Assert.StartsWith("MC4CAQAwBQYDK2Vw", edPkcs8Base64, StringComparison.Ordinal);
        Assert.False(PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.Ed25519, edPkcs8Base64, out _, out _));
    }

    /// <summary>
    /// Verifies criterion 15: <see cref="Program.ResolveRouteRoleRequirement"/> returns
    /// <see cref="Program.RouteRoleRequirement.AdministratorOnly"/> for all three <c>/api/peer-keys</c> routes
    /// and <see cref="Program.RouteRoleRequirement.None"/> for <c>/api/peer-keysx</c>.
    /// </summary>
    [Fact]
    public void Criterion15_ResolveRouteRoleRequirement_GatesPeerKeyRoutesSegmentExact()
    {
        // WHAT: Tests ResolveRouteRoleRequirement for /api/peer-keys, /api/peer-keys/{keyId}/revoke, and /api/peer-keysx.
        // WHY:  Guards against segment-prefix regressions (C4) and ensures GET and POST are Administrator-only.
        // HOW:  Invokes the pure route resolver directly.
        string hex64 = new('a', 64);

        Assert.Equal(
            Program.RouteRoleRequirement.AdministratorOnly,
            Program.ResolveRouteRoleRequirement("GET", new PathString("/api/peer-keys")));
        Assert.Equal(
            Program.RouteRoleRequirement.AdministratorOnly,
            Program.ResolveRouteRoleRequirement("POST", new PathString("/api/peer-keys")));
        Assert.Equal(
            Program.RouteRoleRequirement.AdministratorOnly,
            Program.ResolveRouteRoleRequirement("POST", new PathString($"/api/peer-keys/{hex64}/revoke")));
        Assert.Equal(
            Program.RouteRoleRequirement.None,
            Program.ResolveRouteRoleRequirement("GET", new PathString("/api/peer-keysx")));
    }

    /// <summary>
    /// Verifies criteria 13, 14, 32, and 35: Administrator peer-key lifecycle, compliance audit ledger entries,
    /// zero private-key or input disclosure in responses, configuration exposure, and Swagger generation.
    /// </summary>
    [Fact]
    public async Task Criteria13_14_32_35_AdministratorLifecycle_AuditLedger_ZeroDisclosure_AndConfig()
    {
        // WHAT: Executes the full register/list/revoke lifecycle, error responses, audit ledger checks, configuration check, and Swagger check.
        // WHY:  Verifies end-to-end HTTP contracts, audit logging, and zero private-key/input disclosure.
        // HOW:  Spins up an isolated WebApplicationFactory host and asserts every response status, header, and body.
        string hostDir = Path.Combine(_tempRoot, "c13_host");
        using WebApplicationFactory<Program> factory = CreateIsolatedFactory(hostDir);
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(factory);
        using HttpClient adminClient = CreateClientWithToken(factory, adminToken);

        INodeDatasetSigner hostSigner = factory.Services.GetRequiredService<INodeDatasetSigner>();
        byte[] hostSeedBytes = await File.ReadAllBytesAsync(Path.Combine(hostDir, "node-keys", NodeDatasetSigner.KeyFileName));
        string hostSeedBase64 = Convert.ToBase64String(hostSeedBytes);

        var (edSpkiDer, edPkcs8Base64) = GenerateEd25519SpkiAndPkcs8();
        string edPubBase64 = Convert.ToBase64String(edSpkiDer);
        string expectedEdKeyId = DatasetManifestSignature.ComputeKeyId(edSpkiDer);

        PostQuantumCryptoProvider pqc = new();
        PqcKeyMaterial mlKey = pqc.GenerateSigningKeyPair("peer_mldsa65");
        string expectedMlKeyId = DatasetManifestSignature.ComputeKeyId(Convert.FromBase64String(mlKey.PublicKeyBase64));

        List<string> inspectedBodies = new();

        // 1. Register Ed25519 peer key -> 201 Created, Location: /api/peer-keys/{keyId}, isActive: true
        HttpResponseMessage regEdResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = "Peer Lab Alpha",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edPubBase64
        });
        string regEdBody = await regEdResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(regEdBody);
        Assert.Equal(HttpStatusCode.Created, regEdResp.StatusCode);
        Assert.Equal($"/api/peer-keys/{expectedEdKeyId}", regEdResp.Headers.Location?.OriginalString);

        PeerKeyRecord? regEdRecord = JsonSerializer.Deserialize<PeerKeyRecord>(regEdBody);
        Assert.NotNull(regEdRecord);
        Assert.Equal(expectedEdKeyId, regEdRecord.KeyId);
        Assert.True(regEdRecord.IsActive);

        // 2. Register ML-DSA-65 peer key -> 201 Created
        HttpResponseMessage regMlResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = "Peer Lab Beta",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = mlKey.PublicKeyBase64
        });
        string regMlBody = await regMlResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(regMlBody);
        Assert.Equal(HttpStatusCode.Created, regMlResp.StatusCode);

        // 3. Register the first again -> 409 Conflict, no label echoed
        const string submittedDuplicateLabel = "SecretDuplicateLabelSentinel99";
        HttpResponseMessage dupResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = submittedDuplicateLabel,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edPubBase64
        });
        string dupBody = await dupResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(dupBody);
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);
        Assert.DoesNotContain(submittedDuplicateLabel, dupBody, StringComparison.Ordinal);

        // 4. Register this host's own localNode.publicKeyBase64 -> 409 Conflict
        HttpResponseMessage selfResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = submittedDuplicateLabel,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = hostSigner.PublicKeyBase64
        });
        string selfBody = await selfResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(selfBody);
        Assert.Equal(HttpStatusCode.Conflict, selfResp.StatusCode);
        Assert.DoesNotContain(submittedDuplicateLabel, selfBody, StringComparison.Ordinal);

        // 5. GET /api/peer-keys -> 200 OK, localNode.keyId == hostSigner.KeyId, both peers listed
        HttpResponseMessage listResp = await adminClient.GetAsync("/api/peer-keys");
        string listBody = await listResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(listBody);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        PeerKeyListResponse? listPayload = JsonSerializer.Deserialize<PeerKeyListResponse>(listBody);
        Assert.NotNull(listPayload);
        Assert.Equal(hostSigner.KeyId, listPayload.LocalNode.KeyId);
        Assert.Equal(hostSigner.PublicKeyBase64, listPayload.LocalNode.PublicKeyBase64);
        Assert.Equal(2, listPayload.Peers.Count);

        // 6. Revoke first peer key -> 200 OK with isActive: false
        HttpResponseMessage revResp = await adminClient.PostAsync($"/api/peer-keys/{expectedEdKeyId}/revoke", content: null);
        string revBody = await revResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(revBody);
        Assert.Equal(HttpStatusCode.OK, revResp.StatusCode);

        PeerKeyRecord? revRecord = JsonSerializer.Deserialize<PeerKeyRecord>(revBody);
        Assert.NotNull(revRecord);
        Assert.False(revRecord.IsActive);
        Assert.Equal("gate_admin", revRecord.RevokedBy);
        Assert.NotNull(revRecord.RevokedAtUtc);

        // 7. Revoke again -> 404 NotFound
        HttpResponseMessage revAgainResp = await adminClient.PostAsync($"/api/peer-keys/{expectedEdKeyId}/revoke", content: null);
        string revAgainBody = await revAgainResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(revAgainBody);
        Assert.Equal(HttpStatusCode.NotFound, revAgainResp.StatusCode);

        // 8. Revoke localNode.keyId -> 404 NotFound
        HttpResponseMessage revSelfResp = await adminClient.PostAsync($"/api/peer-keys/{hostSigner.KeyId}/revoke", content: null);
        string revSelfBody = await revSelfResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(revSelfBody);
        Assert.Equal(HttpStatusCode.NotFound, revSelfResp.StatusCode);

        // 9. Re-register the revoked key -> 409 Conflict
        HttpResponseMessage reregResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = submittedDuplicateLabel,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edPubBase64
        });
        string reregBody = await reregResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(reregBody);
        Assert.Equal(HttpStatusCode.Conflict, reregResp.StatusCode);
        Assert.DoesNotContain(submittedDuplicateLabel, reregBody, StringComparison.Ordinal);

        // 10. Compliance audit ledger contains RegisterPeerKey (2x) and RevokePeerKey (1x) by gate_admin
        HttpResponseMessage ledgerResp = await adminClient.GetAsync("/api/compliance/audit-ledger");
        Assert.Equal(HttpStatusCode.OK, ledgerResp.StatusCode);
        string ledgerJson = await ledgerResp.Content.ReadAsStringAsync();
        using (JsonDocument ledgerDoc = JsonDocument.Parse(ledgerJson))
        {
            int registerCount = 0;
            int revokeCount = 0;
            foreach (JsonElement item in ledgerDoc.RootElement.EnumerateArray())
            {
                string? action = item.GetProperty("action").GetString();
                string? operatorId = item.GetProperty("operatorId").GetString();
                if (action == "RegisterPeerKey" && operatorId == "gate_admin")
                {
                    registerCount++;
                }
                else if (action == "RevokePeerKey" && operatorId == "gate_admin")
                {
                    revokeCount++;
                }
            }

            Assert.Equal(2, registerCount);
            Assert.Equal(1, revokeCount);
        }

        // Criterion 14: Send invalid 400 requests over HTTP and verify no private key material or submitted values appear
        const string badLabelSentinel = "BadLabelControl\nSentinel";
        HttpResponseMessage badLabelResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = badLabelSentinel,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edPubBase64
        });
        string badLabelBody = await badLabelResp.Content.ReadAsStringAsync();
        inspectedBodies.Add(badLabelBody);
        Assert.Equal(HttpStatusCode.BadRequest, badLabelResp.StatusCode);
        Assert.DoesNotContain("BadLabelControl", badLabelBody, StringComparison.Ordinal);

        string[] invalidKeyPayloads =
        [
            "-----BEGIN PRIVATE KEY-----\n" + edPkcs8Base64 + "\n-----END PRIVATE KEY-----",
            edPkcs8Base64,
            "%%%invalid-base64-payload%%%",
            GenerateMlDsa44SpkiBase64()
        ];

        foreach (string invalidKey in invalidKeyPayloads)
        {
            HttpResponseMessage badKeyResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
            {
                Label = submittedDuplicateLabel,
                Algorithm = KeyAlgorithm.MlDsa65,
                PublicKeyBase64 = invalidKey
            });
            string badKeyBody = await badKeyResp.Content.ReadAsStringAsync();
            inspectedBodies.Add(badKeyBody);
            Assert.Equal(HttpStatusCode.BadRequest, badKeyResp.StatusCode);
            Assert.DoesNotContain(invalidKey, badKeyBody, StringComparison.Ordinal);
            Assert.DoesNotContain(submittedDuplicateLabel, badKeyBody, StringComparison.Ordinal);
        }

        foreach (string body in inspectedBodies)
        {
            Assert.DoesNotContain(hostSeedBase64, body, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE KEY", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MC4CAQAwBQYDK2Vw", body, StringComparison.Ordinal);
        }

        // Criterion 32: GET /api/configuration lists Peer:RequireSignedDatasets with value "false", isSecret false, not [REDACTED]
        Assert.False(SystemConfigurationStore.IsSecretConfigurationKey("Peer:RequireSignedDatasets"));
        Assert.Equal("Peer:RequireSignedDatasets", VerifiedDatasetImporter.StrictModeConfigurationKey);

        HttpResponseMessage configResp = await adminClient.GetAsync("/api/configuration");
        Assert.Equal(HttpStatusCode.OK, configResp.StatusCode);
        string configJson = await configResp.Content.ReadAsStringAsync();
        using (JsonDocument configDoc = JsonDocument.Parse(configJson))
        {
            JsonElement? strictItem = null;
            foreach (JsonElement elem in configDoc.RootElement.EnumerateArray())
            {
                if (elem.GetProperty("key").GetString() == "Peer:RequireSignedDatasets")
                {
                    strictItem = elem;
                    break;
                }
            }

            Assert.NotNull(strictItem);
            Assert.Equal("false", strictItem.Value.GetProperty("value").GetString());
            Assert.Equal("false", strictItem.Value.GetProperty("defaultValue").GetString());
            Assert.Equal("Peer Collaboration", strictItem.Value.GetProperty("category").GetString());
            Assert.False(strictItem.Value.GetProperty("isSecret").GetBoolean());
            Assert.NotEqual("[REDACTED]", strictItem.Value.GetProperty("value").GetString());
        }

        // Criterion 35: /swagger/v1/swagger.json generates and contains /api/peer-keys
        HttpResponseMessage swaggerResp = await adminClient.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerResp.StatusCode);
        string swaggerJson = await swaggerResp.Content.ReadAsStringAsync();
        Assert.Contains("\"/api/peer-keys\"", swaggerJson, StringComparison.Ordinal);
        Assert.Contains("\"/api/peer-keys/{keyId}/revoke\"", swaggerJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies criterion 16: Researcher and Patient tokens each receive 403 Forbidden on all three
    /// <c>/api/peer-keys</c> endpoints when sending valid bodies and real route values, while the
    /// Administrator token receives 201, 200, and 200 on the same three requests.
    /// </summary>
    [Fact]
    public async Task Criterion16_HttpRoleGate_RejectsResearcherAndPatientWith403AndAdmitsAdministrator()
    {
        // WHAT: Sends valid POST /api/peer-keys, GET /api/peer-keys, and POST /api/peer-keys/{keyId}/revoke requests as Researcher, Patient, and Administrator.
        // WHY:  Proves the HTTP authentication middleware enforces AdministratorOnly on all three endpoints.
        // HOW:  Uses valid SPKI payloads and a real 64-hex keyId so 403 cannot be confused with 400.
        string hostDir = Path.Combine(_tempRoot, "c16_host");
        using WebApplicationFactory<Program> factory = CreateIsolatedFactory(hostDir);
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync(factory);

        var (edSpkiDer, _) = GenerateEd25519SpkiAndPkcs8();
        string edPubBase64 = Convert.ToBase64String(edSpkiDer);
        string edKeyId = DatasetManifestSignature.ComputeKeyId(edSpkiDer);

        RegisterPeerKeyRequest validRequest = new()
        {
            Label = "Valid Peer Lab",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edPubBase64
        };

        foreach (string deniedToken in new[] { researcherToken, patientToken })
        {
            using HttpClient deniedClient = CreateClientWithToken(factory, deniedToken);

            HttpResponseMessage postReg = await deniedClient.PostAsJsonAsync("/api/peer-keys", validRequest);
            Assert.Equal(HttpStatusCode.Forbidden, postReg.StatusCode);

            HttpResponseMessage getList = await deniedClient.GetAsync("/api/peer-keys");
            Assert.Equal(HttpStatusCode.Forbidden, getList.StatusCode);

            HttpResponseMessage postRev = await deniedClient.PostAsync($"/api/peer-keys/{edKeyId}/revoke", content: null);
            Assert.Equal(HttpStatusCode.Forbidden, postRev.StatusCode);
        }

        using HttpClient adminClient = CreateClientWithToken(factory, adminToken);

        HttpResponseMessage adminReg = await adminClient.PostAsJsonAsync("/api/peer-keys", validRequest);
        Assert.Equal(HttpStatusCode.Created, adminReg.StatusCode);

        HttpResponseMessage adminList = await adminClient.GetAsync("/api/peer-keys");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        HttpResponseMessage adminRev = await adminClient.PostAsync($"/api/peer-keys/{edKeyId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, adminRev.StatusCode);
    }

    /// <summary>
    /// Creates an isolated <see cref="WebApplicationFactory{TEntryPoint}"/> bound to <paramref name="hostDir"/>.
    /// </summary>
    /// <param name="hostDir">Isolated directory for incoming trajectories, exports, parquet, and SQLite DB.</param>
    /// <returns>Configured web application factory.</returns>
    private WebApplicationFactory<Program> CreateIsolatedFactory(string hostDir)
    {
        Directory.CreateDirectory(hostDir);
        TestFixturesHelper.EnsureTestFixtures(hostDir);

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", hostDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(hostDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(hostDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });
    }

    /// <summary>
    /// Provisions one Administrator, one Researcher, and one Patient account and mints a bearer token for each.
    /// </summary>
    /// <param name="factory">The target web application factory.</param>
    /// <returns>A tuple of signed bearer tokens for Administrator, Researcher, and Patient.</returns>
    private static async Task<(string Administrator, string Researcher, string Patient)> ProvisionRoleTokensAsync(
        WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("gate_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("gate_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);

            GeneratedKeyBundle researcherBundle = auth.GenerateSecureKeyBundle("gate_researcher", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("gate_researcher", UserRole.Researcher, AccessGate.ApiWeb, researcherBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

            GeneratedKeyBundle patientBundle = auth.GenerateSecureKeyBundle("gate_patient", UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("gate_patient", UserRole.Patient, AccessGate.ApiWeb, patientBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        }

        IReadOnlyList<UserAccount> users = await auth.ListUsersAsync();
        string TokenFor(string username) => auth.GenerateBearerToken(
            users.First(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));

        return (TokenFor("gate_admin"), TokenFor("gate_researcher"), TokenFor("gate_patient"));
    }

    /// <summary>
    /// Creates an HTTP client with the specified bearer token attached.
    /// </summary>
    /// <param name="factory">The target web application factory.</param>
    /// <param name="token">Bearer token string.</param>
    /// <returns>Configured <see cref="HttpClient"/>.</returns>
    private static HttpClient CreateClientWithToken(WebApplicationFactory<Program> factory, string token)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Generates a fresh Ed25519 key pair and returns its SPKI DER bytes and RFC 8410 v0 PKCS#8 private key Base64 string.
    /// </summary>
    /// <returns>Tuple of SPKI DER bytes and PKCS#8 private key Base64.</returns>
    private static (byte[] SpkiDer, string Pkcs8Base64) GenerateEd25519SpkiAndPkcs8()
    {
        Ed25519PrivateKeyParameters priv = new(new SecureRandom());
        Ed25519PublicKeyParameters pub = priv.GeneratePublicKey();
        byte[] spkiDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pub).GetDerEncoded();

        ReadOnlySpan<byte> pkcs8Prefix = [0x30, 0x2E, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x04, 0x22, 0x04, 0x20];
        byte[] pkcs8Der = new byte[48];
        pkcs8Prefix.CopyTo(pkcs8Der);
        priv.GetEncoded().CopyTo(pkcs8Der.AsSpan(16));
        return (spkiDer, Convert.ToBase64String(pkcs8Der));
    }

    /// <summary>
    /// Generates an ML-DSA-44 SubjectPublicKeyInfo DER Base64 string to test parameter-set rejection.
    /// </summary>
    /// <returns>Base64-encoded ML-DSA-44 SPKI DER public key.</returns>
    private static string GenerateMlDsa44SpkiBase64()
    {
        MLDsaKeyPairGenerator generator = new();
        generator.Init(new MLDsaKeyGenerationParameters(new SecureRandom(), MLDsaParameters.ml_dsa_44));
        AsymmetricCipherKeyPair keyPair = generator.GenerateKeyPair();
        byte[] spkiDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded();
        return Convert.ToBase64String(spkiDer);
    }
}
