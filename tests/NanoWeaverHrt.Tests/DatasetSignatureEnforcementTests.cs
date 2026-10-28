using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Services;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Acceptance tests for Work Package P9 Stage B: <see cref="VerifiedDatasetImporter"/>, strict-mode policy
/// (<c>Peer:RequireSignedDatasets</c>), size binding in <see cref="DatasetTransferService.ImportDatasetAsync"/>,
/// precedence ordering, and zero-disclosure RFC 7807 responses on <c>POST /api/datasets/import</c> (Criteria 17–31).
/// </summary>
public sealed class DatasetSignatureEnforcementTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Stream test double that throws if any byte is read, proving provenance rejections never read the archive stream.
    /// </summary>
    private sealed class ThrowingArchiveStream : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Archive stream must not be read when provenance is rejected.");

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Archive stream must not be read when provenance is rejected.");

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Simple in-memory logger that captures formatted log messages for verifying warning diagnostics.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Gets the captured log entries.
        /// </summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Base web application factory fixture.
    /// </summary>
    private readonly WebApplicationFactory<Program> _baseFactory;

    /// <summary>
    /// Root temporary directory for this test class instance.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_ds_enforce_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="DatasetSignatureEnforcementTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base web application factory fixture.</param>
    public DatasetSignatureEnforcementTests(WebApplicationFactory<Program> baseFactory)
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
    /// Verifies criteria 17, 18, 19, 20, and 25: unsigned import under default strict OFF, signed Ed25519 peer import
    /// (by both Administrator and Researcher), signed ML-DSA-65 peer import, self-signed import, and strict ON enforcement.
    /// </summary>
    [Fact]
    public async Task Criteria17To20And25_EnforcementTable_SuccessAndStrictModePaths()
    {
        // WHAT: Exercises unsigned, Ed25519-peer-signed, ML-DSA-65-peer-signed, and self-signed imports under strict OFF and strict ON.
        // WHY:  Proves the core enforcement table and that strict mode blocks only unsigned bundles while admitting trusted signed bundles.
        // HOW:  Exports real bundles from a peer DatasetTransferService and the host itself, then posts them to /api/datasets/import.
        string hostDir = Path.Combine(_tempRoot, "host_17_25");
        using WebApplicationFactory<Program> factory = CreateIsolatedFactory(hostDir);
        var (adminToken, researcherToken, _) = await ProvisionRoleTokensAsync(factory);
        using HttpClient adminClient = CreateClientWithToken(factory, adminToken);
        using HttpClient researcherClient = CreateClientWithToken(factory, researcherToken);

        var (peerSigner, peerArchiveBytes, peerSignedManifest) = await ExportPeerBundleAsync("peer_ed25519_bundle");

        // Criterion 17: Unsigned, strict OFF (default) -> 200, provenance: "unsigned", files extracted
        DatasetManifest unsignedManifest = new()
        {
            DatasetName = "unsigned_default_ok",
            TimestampUtc = peerSignedManifest.TimestampUtc,
            ArchiveFileName = peerSignedManifest.ArchiveFileName,
            Sha256Checksum = peerSignedManifest.Sha256Checksum,
            SizeInBytes = peerSignedManifest.SizeInBytes,
            IncludedFiles = peerSignedManifest.IncludedFiles
        };

        File.Delete(Path.Combine(hostDir, "dna_nanotube_sample.xyz"));
        HttpResponseMessage c17Resp = await PostImportAsync(adminClient, peerArchiveBytes, unsignedManifest);
        Assert.Equal(HttpStatusCode.OK, c17Resp.StatusCode);
        using (JsonDocument c17Doc = JsonDocument.Parse(await c17Resp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("unsigned", c17Doc.RootElement.GetProperty("provenance").GetString());
            Assert.Equal(JsonValueKind.Null, c17Doc.RootElement.GetProperty("signerKeyId").ValueKind);
        }
        Assert.True(File.Exists(Path.Combine(hostDir, "dna_nanotube_sample.xyz")));

        // Register the Ed25519 peer key
        HttpResponseMessage regEd = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = "Trusted Ed25519 Peer",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = peerSigner.PublicKeyBase64
        });
        Assert.Equal(HttpStatusCode.Created, regEd.StatusCode);

        // Criterion 18: Signed by registered Ed25519 peer -> 200, provenance: "signed", signerKeyId == peerSigner.KeyId (Admin + Researcher)
        File.Delete(Path.Combine(hostDir, "dna_nanotube_sample.xyz"));
        HttpResponseMessage c18Admin = await PostImportAsync(adminClient, peerArchiveBytes, peerSignedManifest);
        Assert.Equal(HttpStatusCode.OK, c18Admin.StatusCode);
        using (JsonDocument c18Doc = JsonDocument.Parse(await c18Admin.Content.ReadAsStringAsync()))
        {
            Assert.Equal("signed", c18Doc.RootElement.GetProperty("provenance").GetString());
            Assert.Equal(peerSigner.KeyId, c18Doc.RootElement.GetProperty("signerKeyId").GetString());
        }
        Assert.True(File.Exists(Path.Combine(hostDir, "dna_nanotube_sample.xyz")));

        HttpResponseMessage c18Researcher = await PostImportAsync(researcherClient, peerArchiveBytes, peerSignedManifest);
        Assert.Equal(HttpStatusCode.OK, c18Researcher.StatusCode);

        // Criterion 19: Signed by registered ML-DSA-65 peer -> 200
        PostQuantumCryptoProvider pqc = new();
        PqcKeyMaterial mlPeerKey = pqc.GenerateSigningKeyPair("peer_mldsa65");
        byte[] mlSpkiDer = Convert.FromBase64String(mlPeerKey.PublicKeyBase64);
        string mlPeerKeyId = DatasetManifestSignature.ComputeKeyId(mlSpkiDer);

        HttpResponseMessage regMl = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = "Trusted ML-DSA-65 Peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = mlPeerKey.PublicKeyBase64
        });
        Assert.Equal(HttpStatusCode.Created, regMl.StatusCode);

        DatasetManifest mlUnsigned = unsignedManifest with
        {
            DatasetName = "peer_mldsa65_bundle",
            ManifestVersion = DatasetManifestSignature.SignedManifestVersion,
            SignatureAlgorithm = nameof(KeyAlgorithm.MlDsa65),
            SignerKeyId = mlPeerKeyId
        };
        byte[] mlCanonical = DatasetManifestCanonicalizer.GetSigningBytes(mlUnsigned);
        // WHY `!`: GenerateSigningKeyPair always populates PrivateKeyBase64 on the newly generated key material.
        PqcSignature mlSig = pqc.SignData(mlCanonical, mlPeerKey.PrivateKeyBase64!, mlPeerKeyId);
        DatasetManifest mlSignedManifest = mlUnsigned with { SignatureBase64 = mlSig.SignatureBase64 };

        HttpResponseMessage c19Resp = await PostImportAsync(adminClient, peerArchiveBytes, mlSignedManifest);
        Assert.Equal(HttpStatusCode.OK, c19Resp.StatusCode);
        using (JsonDocument c19Doc = JsonDocument.Parse(await c19Resp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("signed", c19Doc.RootElement.GetProperty("provenance").GetString());
            Assert.Equal(mlPeerKeyId, c19Doc.RootElement.GetProperty("signerKeyId").GetString());
        }

        // Criterion 20: Signed by the host's own key (export on host, import on same host, nothing registered for host key) -> 200
        IDatasetManager hostManager = factory.Services.GetRequiredService<IDatasetManager>();
        INodeDatasetSigner hostSigner = factory.Services.GetRequiredService<INodeDatasetSigner>();
        DatasetManifest selfManifest = await hostManager.ExportDatasetAsync("self_signed_bundle", ["dna_nanotube_sample.xyz"]);
        byte[] selfArchiveBytes = await File.ReadAllBytesAsync(Path.Combine(hostDir, "exports", selfManifest.ArchiveFileName));

        HttpResponseMessage c20Resp = await PostImportAsync(adminClient, selfArchiveBytes, selfManifest);
        Assert.Equal(HttpStatusCode.OK, c20Resp.StatusCode);
        using (JsonDocument c20Doc = JsonDocument.Parse(await c20Resp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("signed", c20Doc.RootElement.GetProperty("provenance").GetString());
            Assert.Equal(hostSigner.KeyId, c20Doc.RootElement.GetProperty("signerKeyId").GetString());
        }

        // Self-trust never bypasses signature verification: a tampered signature claiming hostSigner.KeyId returns 400
        byte[] selfSigBytes = Convert.FromBase64String(selfManifest.SignatureBase64!);
        selfSigBytes[0] ^= 0xFF;
        DatasetManifest selfTampered = selfManifest with { SignatureBase64 = Convert.ToBase64String(selfSigBytes) };
        HttpResponseMessage selfTamperedResp = await PostImportAsync(adminClient, selfArchiveBytes, selfTampered);
        await AssertProblemRejectionAsync(selfTamperedResp, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, selfTampered, hostSigner.PublicKeyBase64);

        // Criterion 25: Enable strict mode via database override -> unsigned returns 403, signed peer (c18) and self-signed (c20) return 200
        IConfigurationRepository configRepo = factory.Services.GetRequiredService<IConfigurationRepository>();
        await configRepo.SetDatabaseOverrideAsync(VerifiedDatasetImporter.StrictModeConfigurationKey, "true");

        HttpResponseMessage c25Unsigned = await PostImportAsync(adminClient, peerArchiveBytes, unsignedManifest);
        await AssertProblemRejectionAsync(c25Unsigned, HttpStatusCode.Forbidden, VerifiedDatasetImporter.UnsignedRejectedDetail, hostDir, unsignedManifest, peerSigner.PublicKeyBase64);

        HttpResponseMessage c25PeerSigned = await PostImportAsync(adminClient, peerArchiveBytes, peerSignedManifest);
        Assert.Equal(HttpStatusCode.OK, c25PeerSigned.StatusCode);

        HttpResponseMessage c25SelfSigned = await PostImportAsync(adminClient, selfArchiveBytes, selfManifest);
        Assert.Equal(HttpStatusCode.OK, c25SelfSigned.StatusCode);
    }

    /// <summary>
    /// Verifies criteria 21, 22, 23, 24, 26, 27, 28, 29, and 30: rejection outcomes, revocation enforcement,
    /// strict-mode fail-closed parsing, precedence rules, archive size binding, zero-read stream guarantee,
    /// and RFC 7807 leak-free responses.
    /// </summary>
    [Fact]
    public async Task Criteria21To30_RejectionsPrecedenceSizeBindingAndZeroReadStream()
    {
        // WHAT: Tests every rejection path over HTTP and at unit level on VerifiedDatasetImporter.
        // WHY:  Proves fixed precedence, zero archive reads on provenance rejection, size binding, and RFC 7807 leak safety.
        // HOW:  Runs HTTP assertions against an isolated host and unit assertions with ThrowingArchiveStream.
        string hostDir = Path.Combine(_tempRoot, "host_21_30");
        using WebApplicationFactory<Program> factory = CreateIsolatedFactory(hostDir);
        var (adminToken, _, _) = await ProvisionRoleTokensAsync(factory);
        using HttpClient adminClient = CreateClientWithToken(factory, adminToken);

        var (unregisteredPeer, peerArchiveBytes, unregisteredManifest) = await ExportPeerBundleAsync("unregistered_bundle");
        var (trustedPeer, trustedArchiveBytes, trustedManifest) = await ExportPeerBundleAsync("trusted_bundle");

        // Criterion 21 (HTTP): Signed, signer not registered -> 403 SignerNotTrustedDetail, host file list unchanged
        string[] beforeFiles = Directory.GetFiles(hostDir).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        HttpResponseMessage c21Resp = await PostImportAsync(adminClient, peerArchiveBytes, unregisteredManifest);
        await AssertProblemRejectionAsync(c21Resp, HttpStatusCode.Forbidden, VerifiedDatasetImporter.SignerNotTrustedDetail, hostDir, unregisteredManifest, unregisteredPeer.PublicKeyBase64);
        string[] afterFiles = Directory.GetFiles(hostDir).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(beforeFiles, afterFiles);

        // Register trustedPeer
        HttpResponseMessage regResp = await adminClient.PostAsJsonAsync("/api/peer-keys", new RegisterPeerKeyRequest
        {
            Label = "Trusted Peer 23",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = trustedPeer.PublicKeyBase64
        });
        Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

        // Criterion 23: Signed by registered peer, invalid in 3 cases -> 400 SignatureInvalidDetail
        byte[] rawSig = Convert.FromBase64String(trustedManifest.SignatureBase64!);
        rawSig[5] ^= 0x80;
        DatasetManifest flippedSigManifest = trustedManifest with { SignatureBase64 = Convert.ToBase64String(rawSig) };
        HttpResponseMessage c23a = await PostImportAsync(adminClient, trustedArchiveBytes, flippedSigManifest);
        await AssertProblemRejectionAsync(c23a, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, flippedSigManifest, trustedPeer.PublicKeyBase64);

        DatasetManifest editedNameManifest = trustedManifest with { DatasetName = "tampered_name" };
        HttpResponseMessage c23b = await PostImportAsync(adminClient, trustedArchiveBytes, editedNameManifest);
        await AssertProblemRejectionAsync(c23b, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, editedNameManifest, trustedPeer.PublicKeyBase64);

        DatasetManifest editedSizeManifest = trustedManifest with { SizeInBytes = trustedManifest.SizeInBytes + 1 };
        HttpResponseMessage c23c = await PostImportAsync(adminClient, trustedArchiveBytes, editedSizeManifest);
        await AssertProblemRejectionAsync(c23c, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, editedSizeManifest, trustedPeer.PublicKeyBase64);

        // Criterion 24: Malformed signature block in 2 cases -> 400 SignatureInvalidDetail
        DatasetManifest missingKeyIdManifest = trustedManifest with { SignerKeyId = null };
        HttpResponseMessage c24a = await PostImportAsync(adminClient, trustedArchiveBytes, missingKeyIdManifest);
        await AssertProblemRejectionAsync(c24a, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, missingKeyIdManifest, trustedPeer.PublicKeyBase64);

        DatasetManifest dashedAlgManifest = trustedManifest with { SignatureAlgorithm = "ML-DSA-65" };
        HttpResponseMessage c24b = await PostImportAsync(adminClient, trustedArchiveBytes, dashedAlgManifest);
        await AssertProblemRejectionAsync(c24b, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, dashedAlgManifest, trustedPeer.PublicKeyBase64);

        // Criterion 27: Precedence
        // (a) Unregistered signer AND corrupted signature -> 403 SignerNotTrustedDetail
        DatasetManifest unregCorrupt = unregisteredManifest with { SignatureBase64 = flippedSigManifest.SignatureBase64 };
        HttpResponseMessage c27a = await PostImportAsync(adminClient, peerArchiveBytes, unregCorrupt);
        await AssertProblemRejectionAsync(c27a, HttpStatusCode.Forbidden, VerifiedDatasetImporter.SignerNotTrustedDetail, hostDir, unregCorrupt, unregisteredPeer.PublicKeyBase64);

        // (b) Malformed block AND unknown key id -> 400 SignatureInvalidDetail
        DatasetManifest malformedUnknown = unregisteredManifest with { SignatureAlgorithm = "ML-DSA-65" };
        HttpResponseMessage c27b = await PostImportAsync(adminClient, peerArchiveBytes, malformedUnknown);
        await AssertProblemRejectionAsync(c27b, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, malformedUnknown, unregisteredPeer.PublicKeyBase64);

        // (c) Registered signer, valid signature, wrong archive bytes -> 400 IntegrityMismatchDetail
        byte[] corruptedArchiveBytes = (byte[])trustedArchiveBytes.Clone();
        corruptedArchiveBytes[^1] ^= 0xFF;
        HttpResponseMessage c27c = await PostImportAsync(adminClient, corruptedArchiveBytes, trustedManifest);
        await AssertProblemRejectionAsync(c27c, HttpStatusCode.BadRequest, VerifiedDatasetImporter.IntegrityMismatchDetail, hostDir, trustedManifest, trustedPeer.PublicKeyBase64);

        // (d) Registered signer, corrupted signature, AND wrong archive -> 400 SignatureInvalidDetail
        HttpResponseMessage c27d = await PostImportAsync(adminClient, corruptedArchiveBytes, flippedSigManifest);
        await AssertProblemRejectionAsync(c27d, HttpStatusCode.BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail, hostDir, flippedSigManifest, trustedPeer.PublicKeyBase64);

        // Criterion 22: Signed, signer registered then revoked -> 403 SignerNotTrustedDetail
        HttpResponseMessage revResp = await adminClient.PostAsync($"/api/peer-keys/{trustedPeer.KeyId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revResp.StatusCode);

        HttpResponseMessage c22Resp = await PostImportAsync(adminClient, trustedArchiveBytes, trustedManifest);
        await AssertProblemRejectionAsync(c22Resp, HttpStatusCode.Forbidden, VerifiedDatasetImporter.SignerNotTrustedDetail, hostDir, trustedManifest, trustedPeer.PublicKeyBase64);

        // Criterion 28: Size binding (SHA-256 matches, SizeInBytes off by one) -> false at service level, 400 IntegrityMismatchDetail over HTTP
        DatasetManifest unsignedWrongSize = new()
        {
            DatasetName = "unsigned_size_mismatch",
            TimestampUtc = trustedManifest.TimestampUtc,
            ArchiveFileName = trustedManifest.ArchiveFileName,
            Sha256Checksum = trustedManifest.Sha256Checksum,
            SizeInBytes = trustedManifest.SizeInBytes + 1,
            IncludedFiles = trustedManifest.IncludedFiles
        };

        IDatasetManager hostDatasetManager = factory.Services.GetRequiredService<IDatasetManager>();
        File.Delete(Path.Combine(hostDir, "dna_nanotube_sample.xyz"));
        using (MemoryStream ms = new(trustedArchiveBytes))
        {
            Assert.False(await hostDatasetManager.ImportDatasetAsync(ms, unsignedWrongSize));
        }
        Assert.False(File.Exists(Path.Combine(hostDir, "dna_nanotube_sample.xyz")));

        HttpResponseMessage c28Http = await PostImportAsync(adminClient, trustedArchiveBytes, unsignedWrongSize);
        await AssertProblemRejectionAsync(c28Http, HttpStatusCode.BadRequest, VerifiedDatasetImporter.IntegrityMismatchDetail, hostDir, unsignedWrongSize, trustedPeer.PublicKeyBase64);

        // Criterion 26: Strict value "yes" (fail closed -> 403, warning logs key name and never "yes") and " FALSE " (-> 200)
        DatasetManifest validUnsigned = unsignedWrongSize with { SizeInBytes = trustedManifest.SizeInBytes };
        IConfigurationRepository hostConfigRepo = factory.Services.GetRequiredService<IConfigurationRepository>();
        await hostConfigRepo.SetDatabaseOverrideAsync(VerifiedDatasetImporter.StrictModeConfigurationKey, "yes");

        HttpResponseMessage c26YesHttp = await PostImportAsync(adminClient, trustedArchiveBytes, validUnsigned);
        await AssertProblemRejectionAsync(c26YesHttp, HttpStatusCode.Forbidden, VerifiedDatasetImporter.UnsignedRejectedDetail, hostDir, validUnsigned, trustedPeer.PublicKeyBase64);

        await hostConfigRepo.SetDatabaseOverrideAsync(VerifiedDatasetImporter.StrictModeConfigurationKey, " FALSE ");
        HttpResponseMessage c26FalseHttp = await PostImportAsync(adminClient, trustedArchiveBytes, validUnsigned);
        Assert.Equal(HttpStatusCode.OK, c26FalseHttp.StatusCode);

        // Criterion 21 (Unit level with ThrowingArchiveStream) + Criterion 26 warning check + Criterion 30 pure helpers
        await VerifyUnitImporterWithThrowingStreamAsync(trustedPeer, trustedManifest, flippedSigManifest, missingKeyIdManifest, unregisteredManifest, validUnsigned);
    }

    /// <summary>
    /// Verifies unit-level behavior of <see cref="VerifiedDatasetImporter"/> using a <see cref="ThrowingArchiveStream"/>
    /// to prove the archive is never read on provenance rejections (Criterion 21), checks the fail-closed warning
    /// log (Criterion 26), and tests <see cref="VerifiedDatasetImporter.DescribeRejection"/> and
    /// <see cref="VerifiedDatasetImporter.TryParseStrictMode"/> (Criterion 30).
    /// </summary>
    /// <param name="trustedPeer">Signer for the registered peer.</param>
    /// <param name="trustedManifest">Valid signed manifest from <paramref name="trustedPeer"/>.</param>
    /// <param name="flippedSigManifest">Corrupted-signature manifest from <paramref name="trustedPeer"/>.</param>
    /// <param name="malformedManifest">Malformed-signature-block manifest.</param>
    /// <param name="unregisteredManifest">Signed manifest from an unregistered peer.</param>
    /// <param name="unsignedManifest">Valid unsigned manifest.</param>
    private async Task VerifyUnitImporterWithThrowingStreamAsync(
        NodeDatasetSigner trustedPeer,
        DatasetManifest trustedManifest,
        DatasetManifest flippedSigManifest,
        DatasetManifest malformedManifest,
        DatasetManifest unregisteredManifest,
        DatasetManifest unsignedManifest)
    {
        string unitDir = Path.Combine(_tempRoot, "unit_21");
        Directory.CreateDirectory(unitDir);
        TestFixturesHelper.EnsureTestFixtures(unitDir);

        IConfiguration unitConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = unitDir,
                ["TRAJECTORY_EXPORT_DIR"] = Path.Combine(unitDir, "exports"),
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        WeaverDbConnectionFactory dbFactory = WeaverTestDb.CreateFactory(unitConfig);
        SystemConfigurationStore configStore = new(dbFactory);
        PeerKeyRepository peerRepo = new(dbFactory);
        NodeSecretStore secretStore = new(unitConfig);
        NodeDatasetSigner localSigner = new(secretStore);
        DatasetTransferService transferService = new(unitConfig, NullLogger<DatasetTransferService>.Instance, localSigner);
        DatasetManifestSignatureVerifier verifier = new(new PostQuantumCryptoProvider());
        CapturingLogger<VerifiedDatasetImporter> logger = new();

        // Constructor null guards
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(null!, peerRepo, localSigner, verifier, configStore, logger));
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(transferService, null!, localSigner, verifier, configStore, logger));
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(transferService, peerRepo, null!, verifier, configStore, logger));
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(transferService, peerRepo, localSigner, null!, configStore, logger));
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(transferService, peerRepo, localSigner, verifier, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new VerifiedDatasetImporter(transferService, peerRepo, localSigner, verifier, configStore, null!));

        VerifiedDatasetImporter importer = new(transferService, peerRepo, localSigner, verifier, configStore, logger);

        Assert.True(await peerRepo.TryRegisterAsync(new PeerKeyRecord
        {
            KeyId = trustedPeer.KeyId,
            Label = "Unit Trusted Peer",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = trustedPeer.PublicKeyBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        }));

        await using ThrowingArchiveStream throwingStream = new();

        // Step 2: SignatureInvalid from Malformed block — ThrowingArchiveStream not read
        DatasetImportResult resMalformed = await importer.ImportAsync(throwingStream, malformedManifest);
        Assert.Equal(DatasetImportOutcome.SignatureInvalid, resMalformed.Outcome);

        // Step 4a: SignerNotTrusted — ThrowingArchiveStream not read
        DatasetImportResult resUntrusted = await importer.ImportAsync(throwingStream, unregisteredManifest);
        Assert.Equal(DatasetImportOutcome.SignerNotTrusted, resUntrusted.Outcome);

        // Step 4b: SignatureInvalid from non-verifying signature — ThrowingArchiveStream not read
        DatasetImportResult resBadSig = await importer.ImportAsync(throwingStream, flippedSigManifest);
        Assert.Equal(DatasetImportOutcome.SignatureInvalid, resBadSig.Outcome);

        // Step 3: UnsignedRejectedByPolicy with strict value "yes" — ThrowingArchiveStream not read, warning logged without "yes"
        await configStore.SetDatabaseOverrideAsync(VerifiedDatasetImporter.StrictModeConfigurationKey, "yes");
        logger.Entries.Clear();
        DatasetImportResult resUnsignedStrict = await importer.ImportAsync(throwingStream, unsignedManifest);
        Assert.Equal(DatasetImportOutcome.UnsignedRejectedByPolicy, resUnsignedStrict.Outcome);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(VerifiedDatasetImporter.StrictModeConfigurationKey, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("yes", StringComparison.OrdinalIgnoreCase));

        // Criterion 30: DescribeRejection and TryParseStrictMode
        Assert.Equal((StatusCodes.Status400BadRequest, VerifiedDatasetImporter.IntegrityMismatchDetail), VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.IntegrityMismatch));
        Assert.Equal((StatusCodes.Status400BadRequest, VerifiedDatasetImporter.SignatureInvalidDetail), VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.SignatureInvalid));
        Assert.Equal((StatusCodes.Status403Forbidden, VerifiedDatasetImporter.SignerNotTrustedDetail), VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.SignerNotTrusted));
        Assert.Equal((StatusCodes.Status403Forbidden, VerifiedDatasetImporter.UnsignedRejectedDetail), VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.UnsignedRejectedByPolicy));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.ImportedSigned));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerifiedDatasetImporter.DescribeRejection(DatasetImportOutcome.ImportedUnsigned));

        Assert.Equal(true, VerifiedDatasetImporter.TryParseStrictMode("True"));
        Assert.Equal(false, VerifiedDatasetImporter.TryParseStrictMode("false"));
        Assert.Null(VerifiedDatasetImporter.TryParseStrictMode("yes"));
        Assert.Null(VerifiedDatasetImporter.TryParseStrictMode(null));
        Assert.Null(VerifiedDatasetImporter.TryParseStrictMode(string.Empty));

        _ = trustedManifest;
    }

    /// <summary>
    /// Exports a real signed dataset bundle using an isolated peer <see cref="NodeDatasetSigner"/> and <see cref="DatasetTransferService"/>.
    /// </summary>
    /// <param name="datasetName">Name of the exported dataset bundle.</param>
    /// <returns>Tuple of the peer signer, archive bytes, and signed manifest.</returns>
    private async Task<(NodeDatasetSigner Signer, byte[] ArchiveBytes, DatasetManifest Manifest)> ExportPeerBundleAsync(string datasetName)
    {
        string peerDir = Path.Combine(_tempRoot, "peer_" + datasetName + "_" + Guid.NewGuid().ToString("N"));
        string peerExportDir = Path.Combine(peerDir, "exports");
        Directory.CreateDirectory(peerDir);
        Directory.CreateDirectory(peerExportDir);
        TestFixturesHelper.EnsureTestFixtures(peerDir);

        IConfiguration peerConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = peerDir,
                ["TRAJECTORY_EXPORT_DIR"] = peerExportDir
            })
            .Build();

        NodeSecretStore peerSecrets = new(peerConfig);
        NodeDatasetSigner peerSigner = new(peerSecrets);
        DatasetTransferService peerTransfer = new(peerConfig, NullLogger<DatasetTransferService>.Instance, peerSigner);

        DatasetManifest manifest = await peerTransfer.ExportDatasetAsync(datasetName, ["dna_nanotube_sample.xyz"]);
        byte[] archiveBytes = await File.ReadAllBytesAsync(Path.Combine(peerExportDir, manifest.ArchiveFileName));
        return (peerSigner, archiveBytes, manifest);
    }

    /// <summary>
    /// Posts a multipart dataset import request to <c>POST /api/datasets/import</c>.
    /// </summary>
    /// <param name="client">Authenticated HTTP client.</param>
    /// <param name="archiveBytes">Raw <c>.tar.gz</c> archive bytes.</param>
    /// <param name="manifest">Manifest to serialize as JSON.</param>
    /// <returns>The HTTP response message.</returns>
    private static async Task<HttpResponseMessage> PostImportAsync(
        HttpClient client,
        byte[] archiveBytes,
        DatasetManifest manifest)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent archiveContent = new(archiveBytes);
        ByteArrayContent manifestContent = new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
        form.Add(archiveContent, "archive", manifest.ArchiveFileName);
        form.Add(manifestContent, "manifest", "manifest.json");
        return await client.PostAsync("/api/datasets/import", form);
    }

    /// <summary>
    /// Verifies criterion 29 (leak contract) and exact RFC 7807 status/detail on an HTTP import rejection.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <param name="expectedStatus">Expected HTTP status code.</param>
    /// <param name="expectedDetail">Expected constant detail sentence.</param>
    /// <param name="hostDir">Host temp directory that must never leak in the response.</param>
    /// <param name="manifest">Submitted manifest whose signature must never leak in the response.</param>
    /// <param name="publicKeyBase64">Registered public key that must never leak in the response.</param>
    private static async Task AssertProblemRejectionAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedDetail,
        string hostDir,
        DatasetManifest manifest,
        string publicKeyBase64)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        Assert.Equal(expectedDetail, doc.RootElement.GetProperty("detail").GetString());

        Assert.DoesNotContain(hostDir, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weaver_peer_keys", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(manifest.SignatureBase64))
        {
            Assert.DoesNotContain(manifest.SignatureBase64, body, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(publicKeyBase64, body, StringComparison.Ordinal);
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
}
