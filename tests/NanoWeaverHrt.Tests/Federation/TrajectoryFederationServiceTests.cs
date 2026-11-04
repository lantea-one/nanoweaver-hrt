using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Federation;

/// <summary>
/// Unit test suite verifying trajectory federation manifest generation, Ed25519 signature verification,
/// chunk reading and validation, and peer discovery under <see cref="TrajectoryFederationService"/>.
/// </summary>
public sealed class TrajectoryFederationServiceTests : IDisposable
{
    /// <summary>
    /// Temporary directory hosting test trajectory files.
    /// </summary>
    private readonly string _tempDirectory;

    /// <summary>
    /// Mock system configuration repository.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configMock;

    /// <summary>
    /// Mock peer key registry.
    /// </summary>
    private readonly Mock<IPeerKeyRegistry> _peerRegistryMock;

    /// <summary>
    /// Mock node secret store.
    /// </summary>
    private readonly Mock<INodeSecretStore> _secretStoreMock;

    /// <summary>
    /// Mock logger instance.
    /// </summary>
    private readonly Mock<ILogger<TrajectoryFederationService>> _loggerMock;

    /// <summary>
    /// Test Ed25519 private seed (32 bytes).
    /// </summary>
    private readonly byte[] _nodeSeed;

    /// <summary>
    /// Test Ed25519 private key parameters for the local node.
    /// </summary>
    private readonly Ed25519PrivateKeyParameters _nodePrivateKey;

    /// <summary>
    /// Test Ed25519 public key parameters for the local node.
    /// </summary>
    private readonly Ed25519PublicKeyParameters _nodePublicKey;

    /// <summary>
    /// Fingerprint key identifier of the local node.
    /// </summary>
    private readonly string _nodeKeyId;

    /// <summary>
    /// Base64 SubjectPublicKeyInfo DER public key of the local node.
    /// </summary>
    private readonly string _nodePublicKeyBase64;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrajectoryFederationServiceTests"/> class.
    /// </summary>
    public TrajectoryFederationServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "nw_fed_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _configMock = new Mock<IConfigurationRepository>(MockBehavior.Strict);
        _peerRegistryMock = new Mock<IPeerKeyRegistry>(MockBehavior.Strict);
        _secretStoreMock = new Mock<INodeSecretStore>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<TrajectoryFederationService>>();

        _nodeSeed = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            _nodeSeed[i] = (byte)(i + 1);
        }

        _nodePrivateKey = new Ed25519PrivateKeyParameters(_nodeSeed, 0);
        _nodePublicKey = _nodePrivateKey.GeneratePublicKey();
        byte[] spkiDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(_nodePublicKey).GetDerEncoded();
        _nodeKeyId = DatasetManifestSignature.ComputeKeyId(spkiDer);
        _nodePublicKeyBase64 = Convert.ToBase64String(spkiDer);

        _secretStoreMock.Setup(s => s.GetDatasetSigningKey()).Returns(_nodeSeed);
        _configMock.Setup(c => c.GetValueAsync("Storage:TrajectoryIncomingDir", "./simulations/shared-data", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tempDirectory);
        string parquetDir = Path.Combine(_tempDirectory, "parquet");
        Directory.CreateDirectory(parquetDir);
        _configMock.Setup(c => c.GetValueAsync("Storage:TrajectoryParquetDir", "./simulations/parquet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parquetDir);
    }

    /// <summary>
    /// Cleans up temporary test files and directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies constructor parameter null checks throw <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TrajectoryFederationService(
            null!, _peerRegistryMock.Object, _secretStoreMock.Object, _loggerMock.Object));

        Assert.Throws<ArgumentNullException>(() => new TrajectoryFederationService(
            _configMock.Object, null!, _secretStoreMock.Object, _loggerMock.Object));

        Assert.Throws<ArgumentNullException>(() => new TrajectoryFederationService(
            _configMock.Object, _peerRegistryMock.Object, null!, _loggerMock.Object));

        Assert.Throws<ArgumentNullException>(() => new TrajectoryFederationService(
            _configMock.Object, _peerRegistryMock.Object, _secretStoreMock.Object, null!));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.BuildManifestAsync"/> validates invalid file names and chunk sizes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_InvalidParameters_ThrowsExpectedExceptions()
    {
        TrajectoryFederationService service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.BuildManifestAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildManifestAsync("   "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.BuildManifestAsync("test.xyz", chunkSizeBytes: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.BuildManifestAsync("test.xyz", chunkSizeBytes: -10));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.BuildManifestAsync"/> throws <see cref="FileNotFoundException"/> when file does not exist,
    /// and throws <see cref="ArgumentException"/> when a rooted or traversing path is passed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_MissingFile_ThrowsFileNotFoundException()
    {
        TrajectoryFederationService service = CreateService();

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.BuildManifestAsync("non_existent.xyz"));

        string rootedPath = Path.Combine(_tempDirectory, "non_existent_rooted.xyz");
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildManifestAsync(rootedPath));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.BuildManifestAsync"/> on an empty (0-byte) file generates 0 chunks.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_EmptyFile_GeneratesZeroChunks()
    {
        string filePath = Path.Combine(_tempDirectory, "empty.xyz");
        await File.WriteAllBytesAsync(filePath, []);

        TrajectoryFederationService service = CreateService();
        TrajectoryFederationManifest manifest = await service.BuildManifestAsync("empty.xyz", chunkSizeBytes: 1024);

        Assert.NotNull(manifest);
        Assert.Equal("empty.xyz", manifest.DatasetName);
        Assert.Equal(0, manifest.TotalSizeBytes);
        Assert.Equal(0, manifest.TotalChunks);
        Assert.Empty(manifest.Chunks);
        Assert.Equal(_nodeKeyId, manifest.SigningNodeId);
        Assert.Equal("Ed25519", manifest.SignatureAlgorithm);
        Assert.False(string.IsNullOrWhiteSpace(manifest.SignatureBase64));

        bool verified = await service.VerifyManifestAsync(manifest);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.BuildManifestAsync"/> partitions files cleanly across exact and partial chunk boundaries.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_NonEmptyFile_GeneratesValidChunksAndSignature()
    {
        // 2500 bytes with 1000-byte chunks -> 3 chunks (1000, 1000, 500)
        byte[] payload = new byte[2500];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        string filePath = Path.Combine(_tempDirectory, "trajectory.xyz");
        await File.WriteAllBytesAsync(filePath, payload);

        TrajectoryFederationService service = CreateService();
        TrajectoryFederationManifest manifest = await service.BuildManifestAsync("trajectory.xyz", chunkSizeBytes: 1000);

        Assert.Equal("trajectory.xyz", manifest.DatasetName);
        Assert.Equal(2500, manifest.TotalSizeBytes);
        Assert.Equal(1000, manifest.ChunkSizeBytes);
        Assert.Equal(3, manifest.TotalChunks);
        Assert.Equal(3, manifest.Chunks.Count);

        // Chunk 0: 0..1000
        Assert.Equal(0, manifest.Chunks[0].ChunkIndex);
        Assert.Equal(0, manifest.Chunks[0].OffsetBytes);
        Assert.Equal(1000, manifest.Chunks[0].LengthBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload.AsSpan(0, 1000))), manifest.Chunks[0].Sha256Checksum);

        // Chunk 1: 1000..2000
        Assert.Equal(1, manifest.Chunks[1].ChunkIndex);
        Assert.Equal(1000, manifest.Chunks[1].OffsetBytes);
        Assert.Equal(1000, manifest.Chunks[1].LengthBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload.AsSpan(1000, 1000))), manifest.Chunks[1].Sha256Checksum);

        // Chunk 2: 2000..2500
        Assert.Equal(2, manifest.Chunks[2].ChunkIndex);
        Assert.Equal(2000, manifest.Chunks[2].OffsetBytes);
        Assert.Equal(500, manifest.Chunks[2].LengthBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload.AsSpan(2000, 500))), manifest.Chunks[2].Sha256Checksum);

        bool verified = await service.VerifyManifestAsync(manifest);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.BuildManifestAsync"/> respects cancellation tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_CancellationTokenCancelled_ThrowsOperationCanceledException()
    {
        string filePath = Path.Combine(_tempDirectory, "cancel.xyz");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        TrajectoryFederationService service = CreateService();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.BuildManifestAsync("cancel.xyz", chunkSizeBytes: 10, ct: cts.Token));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.ReadChunkAsync"/> parameter validations and out of bounds detection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkAsync_InvalidArgumentsAndOutOfBounds_ThrowsExpectedExceptions()
    {
        TrajectoryFederationService service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ReadChunkAsync(null!, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadChunkAsync("   ", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkAsync("test.xyz", -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkAsync("test.xyz", 0, chunkSizeBytes: 0));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadChunkAsync("missing.xyz", 0));

        string filePath = Path.Combine(_tempDirectory, "data.xyz");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkAsync("data.xyz", chunkIndex: 1, chunkSizeBytes: 100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkAsync("data.xyz", chunkIndex: 2, chunkSizeBytes: 50));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.ReadChunkAsync"/> handles empty files correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkAsync_EmptyFile_ReturnsEmptyByteArray()
    {
        string filePath = Path.Combine(_tempDirectory, "empty.xyz");
        await File.WriteAllBytesAsync(filePath, []);

        TrajectoryFederationService service = CreateService();
        byte[] chunk = await service.ReadChunkAsync("empty.xyz", chunkIndex: 0, chunkSizeBytes: 1024);

        Assert.Empty(chunk);
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.ReadChunkAsync"/> reads exact byte payloads for slices.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkAsync_ValidSlices_ReturnsAccuratePayloads()
    {
        byte[] payload = new byte[250];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i + 1);
        }

        string filePath = Path.Combine(_tempDirectory, "sample.xyz");
        await File.WriteAllBytesAsync(filePath, payload);

        TrajectoryFederationService service = CreateService();

        byte[] chunk0 = await service.ReadChunkAsync("sample.xyz", chunkIndex: 0, chunkSizeBytes: 100);
        Assert.Equal(100, chunk0.Length);
        Assert.Equal(payload.AsSpan(0, 100).ToArray(), chunk0);

        byte[] chunk1 = await service.ReadChunkAsync("sample.xyz", chunkIndex: 1, chunkSizeBytes: 100);
        Assert.Equal(100, chunk1.Length);
        Assert.Equal(payload.AsSpan(100, 100).ToArray(), chunk1);

        byte[] chunk2 = await service.ReadChunkAsync("sample.xyz", chunkIndex: 2, chunkSizeBytes: 100);
        Assert.Equal(50, chunk2.Length);
        Assert.Equal(payload.AsSpan(200, 50).ToArray(), chunk2);
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.ReadChunkAsync"/> respects cancellation tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        string filePath = Path.Combine(_tempDirectory, "sample.xyz");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        TrajectoryFederationService service = CreateService();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReadChunkAsync("sample.xyz", 0, chunkSizeBytes: 50, ct: cts.Token));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.ValidateChunkAsync"/> validates digests in constant time.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateChunkAsync_VariousChecksums_ValidatesAccurately()
    {
        TrajectoryFederationService service = CreateService();

        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        string validSha256 = Convert.ToHexStringLower(SHA256.HashData(data));

        Assert.True(await service.ValidateChunkAsync(data, validSha256));
        Assert.True(await service.ValidateChunkAsync(data, validSha256.ToUpperInvariant()));

        // Corrupted payload
        byte[] corrupted = [1, 2, 3, 4, 5, 6, 7, 9];
        Assert.False(await service.ValidateChunkAsync(corrupted, validSha256));

        // Corrupted checksum
        Assert.False(await service.ValidateChunkAsync(data, "00" + validSha256[2..]));

        // Null, whitespace, wrong length, and invalid hex
        Assert.False(await service.ValidateChunkAsync(data, null!));
        Assert.False(await service.ValidateChunkAsync(data, ""));
        Assert.False(await service.ValidateChunkAsync(data, "   "));
        Assert.False(await service.ValidateChunkAsync(data, "abc"));
        Assert.False(await service.ValidateChunkAsync(data, new string('z', 64)));

        // Cancellation
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ValidateChunkAsync(data, validSha256, cts.Token));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.VerifyManifestAsync"/> handles null or malformed manifests cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_MalformedOrIncompleteManifest_ReturnsFalse()
    {
        TrajectoryFederationService service = CreateService();

        Assert.False(await service.VerifyManifestAsync(null!));

        TrajectoryFederationManifest baseManifest = new()
        {
            DatasetName = "dataset.xyz",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 100,
            TotalChunks = 1,
            Chunks = [new TrajectoryChunkDescriptor { ChunkIndex = 0, OffsetBytes = 0, LengthBytes = 100, Sha256Checksum = new string('a', 64) }],
            SigningNodeId = _nodeKeyId,
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = "valid-format",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        Assert.False(await service.VerifyManifestAsync(baseManifest with { DatasetName = "" }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { SigningNodeId = " " }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { SignatureAlgorithm = "" }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { SignatureBase64 = " " }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { Chunks = null! }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { TotalChunks = 5 }));
        Assert.False(await service.VerifyManifestAsync(baseManifest with { SignatureAlgorithm = "RSA-2048" }));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.VerifyManifestAsync"/> detects altered fields and invalid signatures.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_TamperedManifest_ReturnsFalse()
    {
        string filePath = Path.Combine(_tempDirectory, "valid.xyz");
        await File.WriteAllBytesAsync(filePath, [10, 20, 30, 40]);

        TrajectoryFederationService service = CreateService();
        TrajectoryFederationManifest manifest = await service.BuildManifestAsync("valid.xyz", 2);

        Assert.True(await service.VerifyManifestAsync(manifest));

        // Tamper dataset name
        Assert.False(await service.VerifyManifestAsync(manifest with { DatasetName = "altered.xyz" }));

        // Tamper size
        Assert.False(await service.VerifyManifestAsync(manifest with { TotalSizeBytes = 999 }));

        // Tamper chunk size
        Assert.False(await service.VerifyManifestAsync(manifest with { ChunkSizeBytes = 10 }));

        // Tamper chunk checksum
        List<TrajectoryChunkDescriptor> tamperedChunks = new(manifest.Chunks);
        tamperedChunks[0] = tamperedChunks[0] with { Sha256Checksum = new string('f', 64) };
        Assert.False(await service.VerifyManifestAsync(manifest with { Chunks = tamperedChunks }));

        // Corrupt signature Base64
        Assert.False(await service.VerifyManifestAsync(manifest with { SignatureBase64 = "not-valid-base64%%%" }));

        // Corrupt signature bytes
        byte[] sigBytes = Convert.FromBase64String(manifest.SignatureBase64);
        sigBytes[0] ^= 0xFF;
        Assert.False(await service.VerifyManifestAsync(manifest with { SignatureBase64 = Convert.ToBase64String(sigBytes) }));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.VerifyManifestAsync"/> with trusted peer signatures and unknown/revoked keys.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_PeerSignatures_EvaluatesTrustAccurately()
    {
        TrajectoryFederationService service = CreateService();

        // Generate peer Ed25519 keypair
        Ed25519KeyPairGenerator keyGen = new();
        keyGen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair peerPair = keyGen.GenerateKeyPair();
        Ed25519PrivateKeyParameters peerPriv = (Ed25519PrivateKeyParameters)peerPair.Private;
        Ed25519PublicKeyParameters peerPub = (Ed25519PublicKeyParameters)peerPair.Public;

        byte[] peerSpki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(peerPub).GetDerEncoded();
        string peerKeyId = DatasetManifestSignature.ComputeKeyId(peerSpki);
        string peerPubBase64 = Convert.ToBase64String(peerSpki);

        TrajectoryChunkDescriptor chunk = new()
        {
            ChunkIndex = 0,
            OffsetBytes = 0,
            LengthBytes = 50,
            Sha256Checksum = new string('c', 64)
        };

        TrajectoryFederationManifest peerManifest = new()
        {
            DatasetName = "peer_dataset.xyz",
            TotalSizeBytes = 50,
            ChunkSizeBytes = 50,
            TotalChunks = 1,
            Chunks = [chunk],
            SigningNodeId = peerKeyId,
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = string.Empty,
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        byte[] canonical = TrajectoryFederationService.ComputeCanonicalBytes(peerManifest);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, peerPriv);
        signer.BlockUpdate(canonical, 0, canonical.Length);
        string signatureBase64 = Convert.ToBase64String(signer.GenerateSignature());

        peerManifest = peerManifest with { SignatureBase64 = signatureBase64 };

        // Case 1: Peer key is active and trusted in registry
        PeerKeyRecord activeRecord = new()
        {
            KeyId = peerKeyId,
            Label = "https://peer.zurich.ch/api",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = peerPubBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(peerKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeRecord);

        Assert.True(await service.VerifyManifestAsync(peerManifest));

        // Case 2: Unknown peer key (registry returns null)
        _peerRegistryMock.Setup(r => r.FindActiveAsync("unknown-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PeerKeyRecord?)null);

        Assert.False(await service.VerifyManifestAsync(peerManifest with { SigningNodeId = "unknown-key" }));

        // Case 3: Revoked peer key
        PeerKeyRecord revokedRecord = activeRecord with
        {
            RevokedBy = "admin",
            RevokedAtUtc = DateTimeOffset.UtcNow
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(peerKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedRecord);

        Assert.False(await service.VerifyManifestAsync(peerManifest));

        // Case 4: Algorithm is MlDsa65 instead of Ed25519
        PeerKeyRecord mlDsaRecord = activeRecord with
        {
            Algorithm = KeyAlgorithm.MlDsa65
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(peerKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mlDsaRecord);

        Assert.False(await service.VerifyManifestAsync(peerManifest));

        // Case 5: KeyId in record does not match computed fingerprint of PublicKeyBase64
        PeerKeyRecord mismatchedRecord = activeRecord with
        {
            PublicKeyBase64 = _nodePublicKeyBase64 // different key bytes
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(peerKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mismatchedRecord);

        Assert.False(await service.VerifyManifestAsync(peerManifest));

        // Case 6: PublicKeyBase64 is invalid Base64 or non-Ed25519 key
        PeerKeyRecord corruptedKeyRecord = activeRecord with
        {
            PublicKeyBase64 = "not-valid-base64"
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(peerKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(corruptedKeyRecord);

        Assert.False(await service.VerifyManifestAsync(peerManifest));

        // Case 7: Key is valid SPKI but non-Ed25519 algorithm (e.g. RSA)
        RsaKeyPairGenerator rsaGen = new();
        rsaGen.Init(new KeyGenerationParameters(new SecureRandom(), 1024));
        AsymmetricCipherKeyPair rsaPair = rsaGen.GenerateKeyPair();
        byte[] rsaSpki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(rsaPair.Public).GetDerEncoded();
        string rsaKeyId = DatasetManifestSignature.ComputeKeyId(rsaSpki);
        PeerKeyRecord rsaRecord = new()
        {
            KeyId = rsaKeyId,
            Label = "RSA Node",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = Convert.ToBase64String(rsaSpki),
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(rsaKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rsaRecord);

        Assert.False(await service.VerifyManifestAsync(peerManifest with { SigningNodeId = rsaKeyId }));
    }

    /// <summary>
    /// Verifies <see cref="ITrajectoryFederationService.DiscoverPeersAsync"/> discovers and formats active peer descriptors.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverPeersAsync_MapsActivePeersAndResolvesEndpoints()
    {
        TrajectoryFederationService service = CreateService();

        PeerKeyRecord active1 = new()
        {
            KeyId = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            Label = "http://lab1.local:5000",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        PeerKeyRecord active2 = new()
        {
            KeyId = "222233334444555566667777888899990000aaaabbbbccccddddeeeeffff1111",
            Label = "https://lab2.cloud.org",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        PeerKeyRecord active3 = new()
        {
            KeyId = "33334444555566667777888899990000aaaabbbbccccddddeeeeffff11112222",
            Label = "weaver://custom-node-address",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        PeerKeyRecord active4Fallback = new()
        {
            KeyId = "4444555566667777888899990000aaaabbbbccccddddeeeeffff111122223333",
            Label = "Zurich Laboratory Alpha",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        PeerKeyRecord revoked = new()
        {
            KeyId = "555566667777888899990000aaaabbbbccccddddeeeeffff1111222233334444",
            Label = "Revoked Node",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            RevokedBy = "admin",
            RevokedAtUtc = DateTimeOffset.UtcNow
        };

        _peerRegistryMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([active1, revoked, active2, active3, active4Fallback]);

        IReadOnlyList<FederationPeerNode> discovered = await service.DiscoverPeersAsync();

        Assert.Equal(4, discovered.Count);

        Assert.Equal(active1.KeyId, discovered[0].NodeId);
        Assert.Equal("http://lab1.local:5000", discovered[0].EndpointUri);
        Assert.Equal(KeyAlgorithm.Ed25519, discovered[0].Algorithm);
        Assert.Equal(1.0, discovered[0].LatencyMilliseconds);

        Assert.Equal(active2.KeyId, discovered[1].NodeId);
        Assert.Equal("https://lab2.cloud.org", discovered[1].EndpointUri);
        Assert.Equal(KeyAlgorithm.MlDsa65, discovered[1].Algorithm);

        Assert.Equal(active3.KeyId, discovered[2].NodeId);
        Assert.Equal("weaver://custom-node-address", discovered[2].EndpointUri);

        Assert.Equal(active4Fallback.KeyId, discovered[3].NodeId);
        Assert.StartsWith("weaver://4444555566667777", discovered[3].EndpointUri);
    }

    /// <summary>
    /// Verifies properties and initialization of record DTOs.
    /// </summary>
    [Fact]
    public void RecordDtos_InitializeAndExposePropertiesCorrectly()
    {
        TrajectoryChunkDescriptor chunk = new()
        {
            ChunkIndex = 1,
            OffsetBytes = 1024,
            LengthBytes = 512,
            Sha256Checksum = "abc"
        };
        Assert.Equal(1, chunk.ChunkIndex);
        Assert.Equal(1024, chunk.OffsetBytes);
        Assert.Equal(512, chunk.LengthBytes);
        Assert.Equal("abc", chunk.Sha256Checksum);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TrajectoryFederationManifest manifest = new()
        {
            DatasetName = "dataset.xyz",
            TotalSizeBytes = 1536,
            ChunkSizeBytes = 1024,
            TotalChunks = 2,
            Chunks = [chunk],
            SigningNodeId = "node-1",
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = "sig==",
            ManifestTimestampUtc = now
        };
        Assert.Equal("dataset.xyz", manifest.DatasetName);
        Assert.Equal(1536, manifest.TotalSizeBytes);
        Assert.Equal(1024, manifest.ChunkSizeBytes);
        Assert.Equal(2, manifest.TotalChunks);
        Assert.Single(manifest.Chunks);
        Assert.Equal("node-1", manifest.SigningNodeId);
        Assert.Equal("Ed25519", manifest.SignatureAlgorithm);
        Assert.Equal("sig==", manifest.SignatureBase64);
        Assert.Equal(now, manifest.ManifestTimestampUtc);

        FederationPeerNode node = new()
        {
            NodeId = "node-id-123",
            EndpointUri = "weaver://local",
            PublicKeyBase64 = "pubKey==",
            Algorithm = KeyAlgorithm.Ed25519,
            LatencyMilliseconds = 12.5,
            LastSeenUtc = now
        };
        Assert.Equal("node-id-123", node.NodeId);
        Assert.Equal("weaver://local", node.EndpointUri);
        Assert.Equal("pubKey==", node.PublicKeyBase64);
        Assert.Equal(KeyAlgorithm.Ed25519, node.Algorithm);
        Assert.Equal(12.5, node.LatencyMilliseconds);
        Assert.Equal(now, node.LastSeenUtc);
    }

    /// <summary>
    /// Verifies <see cref="NodeSecretStore.GetDatasetSigningKey"/> loads or provisions the 32-byte Ed25519 dataset signing key.
    /// </summary>
    [Fact]
    public void NodeSecretStore_GetDatasetSigningKey_Returns32ByteSeed()
    {
        Microsoft.Extensions.Configuration.ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEAVER_NODE_KEYS_DIR"] = Path.Combine(_tempDirectory, "keys")
        });
        Microsoft.Extensions.Configuration.IConfiguration configuration = builder.Build();

        NodeSecretStore store = new(configuration);
        byte[] key = store.GetDatasetSigningKey();

        Assert.NotNull(key);
        Assert.Equal(32, key.Length);

        // Successive call should return identical seed from disk
        byte[] key2 = store.GetDatasetSigningKey();
        Assert.Equal(key, key2);
    }

    /// <summary>
    /// Creates a new <see cref="TrajectoryFederationService"/> instance with test mock dependencies.
    /// </summary>
    /// <param name="pqcProvider">Optional post-quantum crypto provider.</param>
    /// <returns>A populated service instance.</returns>
    private TrajectoryFederationService CreateService(IPostQuantumCryptoProvider? pqcProvider = null) =>
        new(_configMock.Object, _peerRegistryMock.Object, _secretStoreMock.Object, _loggerMock.Object, pqcProvider);

    /// <summary>
    /// Verifies path-traversal hardening rejects malicious file names with rooted paths, separators, and parent navigation.
    /// </summary>
    /// <param name="maliciousName">The malicious trajectory file name payload.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("../escape.xyz")]
    [InlineData("..\\escape.xyz")]
    [InlineData("subdir/nested.xyz")]
    [InlineData("subdir\\nested.xyz")]
    [InlineData("/etc/shadow")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    public async Task PathTraversal_MaliciousFileNames_ThrowArgumentException(string maliciousName)
    {
        // WHAT: Tests path traversal attack attempts against trajectory operations.
        // WHY:  Ensures that directory escape and arbitrary local file system disclosure are prevented.
        // HOW:  Asserts ArgumentException is thrown across BuildManifestAsync, ReadChunkAsync, and ReadChunkIntoAsync.
        TrajectoryFederationService service = CreateService();
        byte[] buffer = new byte[64];

        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildManifestAsync(maliciousName));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadChunkAsync(maliciousName, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadChunkIntoAsync(maliciousName, 0, buffer));
    }

    /// <summary>
    /// Verifies that <see cref="TrajectoryFederationService.ReadChunkIntoAsync"/> validates argument parameters.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkIntoAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests argument validation in ReadChunkIntoAsync.
        // WHY:  Protects against null or whitespace paths, negative offsets, and zero-length destination memory.
        // HOW:  Asserts ArgumentNullException, ArgumentException, and ArgumentOutOfRangeException.
        TrajectoryFederationService service = CreateService();
        byte[] buffer = new byte[64];

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ReadChunkIntoAsync(null!, 0, buffer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadChunkIntoAsync("   ", 0, buffer));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkIntoAsync("valid.xyz", -1, buffer));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkIntoAsync("valid.xyz", 0, Memory<byte>.Empty));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.ReadChunkIntoAsync"/> streams slices with zero heap allocations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkIntoAsync_ValidFile_StreamsChunksCorrectly()
    {
        // WHAT: Streams data from disk directly into pre-allocated memory slices.
        // WHY:  Confirms zero-allocation reading across full and partial chunk slices.
        // HOW:  Writes test payload, calls ReadChunkIntoAsync, and verifies contents and byte count.
        byte[] payload = new byte[350];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 3 + 1);
        }

        string filePath = Path.Combine(_tempDirectory, "stream.xyz");
        await File.WriteAllBytesAsync(filePath, payload);

        TrajectoryFederationService service = CreateService();
        byte[] buffer = new byte[100];

        // Chunk 0: 100 bytes
        int bytesRead0 = await service.ReadChunkIntoAsync("stream.xyz", 0, buffer);
        Assert.Equal(100, bytesRead0);
        Assert.Equal(payload.AsSpan(0, 100).ToArray(), buffer);

        // Chunk 1: 100 bytes
        int bytesRead1 = await service.ReadChunkIntoAsync("stream.xyz", 100, buffer);
        Assert.Equal(100, bytesRead1);
        Assert.Equal(payload.AsSpan(100, 100).ToArray(), buffer);

        // Chunk 3 (partial): remaining 50 bytes into a 100-byte buffer
        Array.Clear(buffer);
        int bytesRead3 = await service.ReadChunkIntoAsync("stream.xyz", 300, buffer);
        Assert.Equal(50, bytesRead3);
        Assert.Equal(payload.AsSpan(300, 50).ToArray(), buffer.AsSpan(0, 50).ToArray());
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.ReadChunkIntoAsync"/> throws <see cref="ArgumentOutOfRangeException"/> when offset is out of bounds.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkIntoAsync_OffsetOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        // WHAT: Tests offset boundary checks in ReadChunkIntoAsync.
        // WHY:  Reading at or beyond file EOF must throw ArgumentOutOfRangeException.
        // HOW:  Creates 100-byte file and passes offset 100 and offset 200.
        string filePath = Path.Combine(_tempDirectory, "bounds.xyz");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        TrajectoryFederationService service = CreateService();
        byte[] buffer = new byte[50];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkIntoAsync("bounds.xyz", 100, buffer));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadChunkIntoAsync("bounds.xyz", 200, buffer));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.ReadChunkIntoAsync"/> throws <see cref="FileNotFoundException"/> when file is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkIntoAsync_MissingFile_ThrowsFileNotFoundException()
    {
        // WHAT: Tests missing file handling in ReadChunkIntoAsync.
        // WHY:  Target file not found in allowed directories must throw FileNotFoundException.
        // HOW:  Requests non-existent file name.
        TrajectoryFederationService service = CreateService();
        byte[] buffer = new byte[50];

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadChunkIntoAsync("nonexistent.xyz", 0, buffer));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.ReadChunkIntoAsync"/> respects cancellation tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadChunkIntoAsync_CancellationTokenCancelled_ThrowsOperationCanceledException()
    {
        // WHAT: Tests cancellation token responsiveness during chunk streaming.
        // WHY:  Swarm node disconnects must cleanly cancel pending disk I/O.
        // HOW:  Passes canceled token and asserts OperationCanceledException.
        string filePath = Path.Combine(_tempDirectory, "cancel_stream.xyz");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        TrajectoryFederationService service = CreateService();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReadChunkIntoAsync("cancel_stream.xyz", 0, new byte[50], ct: cts.Token));
    }

    /// <summary>
    /// Verifies path resolution falls back to the Parquet directory when file exists in parquet storage root.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveTrajectoryFilePathAsync_ParquetStorageFallback_Succeeds()
    {
        // WHAT: Tests resolution of trajectory parquet files from the columnar storage directory.
        // WHY:  Enables federation swarm synchronization of converted columnar parquet datasets.
        // HOW:  Creates temporary parquet directory, places parquet dataset there, and reads chunks.
        string parquetDir = Path.Combine(Path.GetTempPath(), "nw_fed_parquet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parquetDir);
        try
        {
            string parquetFile = Path.Combine(parquetDir, "trajectory.parquet");
            await File.WriteAllBytesAsync(parquetFile, [1, 2, 3, 4, 5, 6, 7, 8]);

            _configMock.Setup(c => c.GetValueAsync("Storage:TrajectoryParquetDir", "./simulations/parquet", It.IsAny<CancellationToken>()))
                .ReturnsAsync(parquetDir);

            TrajectoryFederationService service = CreateService();
            byte[] dest = new byte[4];
            int read = await service.ReadChunkIntoAsync("trajectory.parquet", 0, dest);

            Assert.Equal(4, read);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, dest);
        }
        finally
        {
            if (Directory.Exists(parquetDir))
            {
                Directory.Delete(parquetDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.VerifyManifestAsync"/> verifies valid ML-DSA-65 post-quantum signatures.
    /// </summary>
    /// <param name="algorithmName">The algorithm string to test ("MlDsa65" or "ML-DSA-65").</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("MlDsa65")]
    [InlineData("ML-DSA-65")]
    public async Task VerifyManifestAsync_MlDsa65ValidSignature_ReturnsTrue(string algorithmName)
    {
        // WHAT: Validates post-quantum ML-DSA-65 manifest digital signature verification.
        // WHY:  Enables quantum-resistant swarm distribution manifests conforming to NIST FIPS 204.
        // HOW:  Generates ML-DSA-65 keypair via PostQuantumCryptoProvider, registers active peer key, signs canonical bytes, and asserts VerifyManifestAsync is true.
        PostQuantumCryptoProvider pqc = new();
        PqcKeyMaterial keyMaterial = pqc.GenerateSigningKeyPair("peer-node-pqc-1");

        bool normalized = PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, keyMaterial.PublicKeyBase64, out string canonicalBase64, out string keyId);
        Assert.True(normalized);

        PeerKeyRecord peerRecord = new()
        {
            KeyId = keyId,
            Label = "trusted-pqc-peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = canonicalBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(peerRecord);

        TrajectoryFederationManifest unsignedManifest = new()
        {
            DatasetName = "pqc_sim.xyz",
            TotalSizeBytes = 1000,
            ChunkSizeBytes = 500,
            TotalChunks = 2,
            Chunks =
            [
                new TrajectoryChunkDescriptor { ChunkIndex = 0, OffsetBytes = 0, LengthBytes = 500, Sha256Checksum = new string('a', 64) },
                new TrajectoryChunkDescriptor { ChunkIndex = 1, OffsetBytes = 500, LengthBytes = 500, Sha256Checksum = new string('b', 64) }
            ],
            SigningNodeId = keyId,
            SignatureAlgorithm = algorithmName,
            SignatureBase64 = string.Empty,
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        byte[] canonicalBytes = TrajectoryFederationService.ComputeCanonicalBytes(unsignedManifest);
        Assert.NotNull(keyMaterial.PrivateKeyBase64);
        PqcSignature signature = pqc.SignData(canonicalBytes, keyMaterial.PrivateKeyBase64!, keyMaterial.KeyId); // Justification: Assert.NotNull above confirms non-null.

        TrajectoryFederationManifest signedManifest = unsignedManifest with { SignatureBase64 = signature.SignatureBase64 };

        TrajectoryFederationService service = CreateService(pqc);
        bool verified = await service.VerifyManifestAsync(signedManifest);

        Assert.True(verified);
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.VerifyManifestAsync"/> rejects ML-DSA-65 manifests when peer key is revoked, unknown, or mismatched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_MlDsa65InvalidPeerOrSignature_ReturnsFalse()
    {
        // WHAT: Tests negative branches in ML-DSA-65 manifest verification.
        // WHY:  Unknown peers, revoked keys, algorithm mismatches, or tampered signatures must not pass verification.
        // HOW:  Mocks invalid states in IPeerKeyRegistry and asserts VerifyManifestAsync returns false.
        PostQuantumCryptoProvider pqc = new();
        PqcKeyMaterial keyMaterial = pqc.GenerateSigningKeyPair("peer-node-pqc-2");
        PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, keyMaterial.PublicKeyBase64, out string canonicalBase64, out string keyId);

        TrajectoryFederationManifest manifest = new()
        {
            DatasetName = "pqc_sim.xyz",
            TotalSizeBytes = 500,
            ChunkSizeBytes = 500,
            TotalChunks = 1,
            Chunks = [new TrajectoryChunkDescriptor { ChunkIndex = 0, OffsetBytes = 0, LengthBytes = 500, Sha256Checksum = new string('a', 64) }],
            SigningNodeId = keyId,
            SignatureAlgorithm = "MlDsa65",
            SignatureBase64 = Convert.ToBase64String(new byte[3309]),
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        TrajectoryFederationService service = CreateService(pqc);

        // 1. Unknown peer (null)
        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PeerKeyRecord?)null);
        Assert.False(await service.VerifyManifestAsync(manifest));

        // 2. Revoked peer key
        PeerKeyRecord revokedPeer = new()
        {
            KeyId = keyId,
            Label = "revoked-peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = canonicalBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAtUtc = DateTimeOffset.UtcNow,
            RevokedBy = "admin"
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedPeer);
        Assert.False(await service.VerifyManifestAsync(manifest));

        // 3. Algorithm mismatch (registered as Ed25519)
        PeerKeyRecord mismatchedPeer = new()
        {
            KeyId = keyId,
            Label = "mismatched-peer",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = canonicalBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mismatchedPeer);
        Assert.False(await service.VerifyManifestAsync(manifest));

        // 4. Invalid signature bytes
        PeerKeyRecord validPeer = new()
        {
            KeyId = keyId,
            Label = "valid-peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = canonicalBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validPeer);
        Assert.False(await service.VerifyManifestAsync(manifest));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.VerifyManifestAsync"/> rejects unrecognized signature algorithms.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_UnrecognizedAlgorithm_ReturnsFalse()
    {
        // WHAT: Tests unsupported signature algorithm rejection.
        // WHY:  Prevents algorithm downgrade and unknown algorithm exploits.
        // HOW:  Supplies "RSA-4096" and asserts VerifyManifestAsync returns false.
        TrajectoryFederationManifest manifest = new()
        {
            DatasetName = "data.xyz",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 100,
            TotalChunks = 1,
            Chunks = [new TrajectoryChunkDescriptor { ChunkIndex = 0, OffsetBytes = 0, LengthBytes = 100, Sha256Checksum = new string('c', 64) }],
            SigningNodeId = "some-id",
            SignatureAlgorithm = "RSA-4096",
            SignatureBase64 = "invalid==",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        TrajectoryFederationService service = CreateService();
        Assert.False(await service.VerifyManifestAsync(manifest));
    }

    /// <summary>
    /// Verifies default constructor initializes without an explicit <see cref="IPostQuantumCryptoProvider"/>.
    /// </summary>
    [Fact]
    public void Constructor_DefaultPqcProvider_InitializesSuccessfully()
    {
        // WHAT: Tests default optional parameter for IPostQuantumCryptoProvider.
        // WHY:  Guarantees backwards-compatible dependency injection.
        // HOW:  Instantiates TrajectoryFederationService with 4 parameters and asserts instance is created.
        TrajectoryFederationService service = new(
            _configMock.Object,
            _peerRegistryMock.Object,
            _secretStoreMock.Object,
            _loggerMock.Object);

        Assert.NotNull(service);
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.BuildManifestAsync"/> throws <see cref="ArgumentException"/> when trajectoryFileName is whitespace.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildManifestAsync_WhitespaceFileName_ThrowsArgumentException()
    {
        // WHAT: Tests argument validation in path resolution for empty or whitespace file name.
        // WHY:  Guarantees line coverage and defense against invalid file names.
        // HOW:  Calls BuildManifestAsync with whitespace and asserts ArgumentException is thrown.
        TrajectoryFederationService service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.BuildManifestAsync("   ", 1024));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.VerifyCanonicalContainment"/> throws <see cref="ArgumentException"/> when path escapes root.
    /// </summary>
    [Fact]
    public void VerifyCanonicalContainment_EscapedPath_ThrowsArgumentException()
    {
        // WHAT: Tests defense-in-depth canonical directory containment validation.
        // WHY:  Ensures attempts to access files outside incoming/parquet roots are deterministically rejected.
        // HOW:  Passes path outside incoming/parquet directories and verifies ArgumentException is thrown.
        Assert.Throws<ArgumentException>(() =>
            TrajectoryFederationService.VerifyCanonicalContainment("/etc/passwd", "/incoming/", "/parquet/", "passwd"));
    }

    /// <summary>
    /// Verifies <see cref="TrajectoryFederationService.VerifyManifestAsync"/> returns false when peer public key ID mismatches manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VerifyManifestAsync_MlDsa65_MismatchedKeyIdOrInvalidBase64_ReturnsFalse()
    {
        // WHAT: Tests signature rejection when peer key fails normalization or key ID doesn't match manifest.
        // WHY:  Prevents spoofing and key-substitution attacks.
        // HOW:  Configures peer key record with invalid base64 and verifies VerifyManifestAsync returns false.
        string keyId = "mismatched-node";
        TrajectoryFederationManifest manifest = new()
        {
            DatasetName = "data.xyz",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 100,
            TotalChunks = 1,
            Chunks = [new TrajectoryChunkDescriptor { ChunkIndex = 0, OffsetBytes = 0, LengthBytes = 100, Sha256Checksum = new string('c', 64) }],
            SigningNodeId = keyId,
            SignatureAlgorithm = "MlDsa65",
            SignatureBase64 = "c2lnbmF0dXJl",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        PeerKeyRecord badPeer = new()
        {
            KeyId = keyId,
            Label = "invalid-peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = "not-valid-base64!!!",
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        _peerRegistryMock.Setup(r => r.FindActiveAsync(keyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(badPeer);

        TrajectoryFederationService service = CreateService();
        Assert.False(await service.VerifyManifestAsync(manifest));
    }
}
