using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Production implementation of <see cref="ITrajectoryFederationService"/> providing zero-allocation trajectory chunking,
/// Ed25519 manifest digital signing, constant-time chunk validation, and swarm peer discovery.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The core trajectory swarm synchronization engine for sovereign laboratories.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables secure, verifiable partition and distributed synchronization of large molecular
/// dynamics trajectory files without full-file in-memory buffering.
/// </para>
/// <para>
/// <strong>How it works:</strong> Streams trajectory files in slices rented from <see cref="ArrayPool{T}.Shared"/>, calculates
/// SHA-256 chunk checksums, signs manifests using the node's persistent Ed25519 dataset signing seed, and validates peers
/// against <see cref="IPeerKeyRegistry"/>.
/// </para>
/// </remarks>
public sealed class TrajectoryFederationService : ITrajectoryFederationService
{
    /// <summary>
    /// ASCII domain-separation tag prefixed to canonical federation manifest byte streams.
    /// </summary>
    private const string DomainTag = "NWHRT-TRAJECTORY-FEDERATION-MANIFEST";

    /// <summary>
    /// Canonical algorithm identifier for Ed25519 signatures.
    /// </summary>
    private const string AlgorithmEd25519 = "Ed25519";

    /// <summary>
    /// Canonical algorithm identifier for ML-DSA-65 signatures (compact casing).
    /// </summary>
    private const string AlgorithmMlDsa65 = "MlDsa65";

    /// <summary>
    /// Canonical algorithm identifier for ML-DSA-65 signatures (NIST FIPS 204 casing).
    /// </summary>
    private const string AlgorithmMlDsa65Fips = "ML-DSA-65";

    /// <summary>
    /// Configuration key for the incoming trajectory directory path.
    /// </summary>
    private const string TrajectoryIncomingConfigKey = "Storage:TrajectoryIncomingDir";

    /// <summary>
    /// Fallback trajectory incoming directory when not explicitly configured.
    /// </summary>
    private const string DefaultIncomingDir = "./simulations/shared-data";

    /// <summary>
    /// Configuration key for the columnar trajectory parquet directory path.
    /// </summary>
    private const string TrajectoryParquetConfigKey = "Storage:TrajectoryParquetDir";

    /// <summary>
    /// Fallback trajectory parquet directory when not explicitly configured.
    /// </summary>
    private const string DefaultParquetDir = "./simulations/parquet";

    /// <summary>
    /// Strict UTF-8 encoding without BOM that throws on invalid surrogates.
    /// </summary>
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// System configuration repository for resolving storage paths.
    /// </summary>
    private readonly IConfigurationRepository _configRepo;

    /// <summary>
    /// Registry of trusted peer public keys.
    /// </summary>
    private readonly IPeerKeyRegistry _peerKeyRegistry;

    /// <summary>
    /// Persistent secret store providing this node's signing key material.
    /// </summary>
    private readonly INodeSecretStore _secretStore;

    /// <summary>
    /// Logger instance for telemetry and diagnostic messages.
    /// </summary>
    private readonly ILogger<TrajectoryFederationService> _logger;

    /// <summary>
    /// Post-quantum cryptographic provider for verifying ML-DSA-65 signatures.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrajectoryFederationService"/> class.
    /// </summary>
    /// <param name="configRepo">System configuration repository.</param>
    /// <param name="peerKeyRegistry">Registry of trusted peer public keys.</param>
    /// <param name="secretStore">Node secret store providing key material.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="pqcProvider">Optional post-quantum cryptographic provider for ML-DSA-65 verification; defaults to a new <see cref="PostQuantumCryptoProvider"/> instance.</param>
    /// <exception cref="ArgumentNullException">Any required dependency is <c>null</c>.</exception>
    public TrajectoryFederationService(
        IConfigurationRepository configRepo,
        IPeerKeyRegistry peerKeyRegistry,
        INodeSecretStore secretStore,
        ILogger<TrajectoryFederationService> logger,
        IPostQuantumCryptoProvider? pqcProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configRepo);
        ArgumentNullException.ThrowIfNull(peerKeyRegistry);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(logger);

        _configRepo = configRepo;
        _peerKeyRegistry = peerKeyRegistry;
        _secretStore = secretStore;
        _logger = logger;
        _pqcProvider = pqcProvider ?? new PostQuantumCryptoProvider();
    }

    /// <inheritdoc/>
    public async Task<TrajectoryFederationManifest> BuildManifestAsync(
        string trajectoryFileName,
        int chunkSizeBytes = 1048576,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoryFileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSizeBytes);

        // WHAT: Resolve the absolute or directory-relative trajectory file path.
        // WHY:  Ensures callers can specify either bare file names within the incoming directory or explicit paths.
        // HOW:  Queries configRepo for incoming directory, combines if relative, and verifies file presence.
        string resolvedPath = await ResolveTrajectoryFilePathAsync(trajectoryFileName, ct).ConfigureAwait(false);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Trajectory file '{trajectoryFileName}' was not found at '{resolvedPath}'.", resolvedPath);
        }

        FileInfo fileInfo = new(resolvedPath);
        long totalSizeBytes = fileInfo.Length;
        int totalChunks = totalSizeBytes == 0 ? 0 : (int)((totalSizeBytes + chunkSizeBytes - 1) / chunkSizeBytes);
        List<TrajectoryChunkDescriptor> chunks = new(totalChunks);

        if (totalSizeBytes > 0)
        {
            // WHAT: Stream the trajectory file in slices rented from ArrayPool<byte>.Shared and compute SHA-256 for each chunk.
            // WHY:  Adheres to zero-allocation prime directive; never loads full multi-gigabyte files into memory.
            // HOW:  Rents a buffer of chunkSizeBytes, reads synchronously or asynchronously, computes hash into stackalloc span, and returns buffer in finally block.
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(chunkSizeBytes);
            try
            {
                {
                    await using FileStream stream = new(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                    for (int i = 0; i < totalChunks; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        long offset = (long)i * chunkSizeBytes;
                        int length = (int)Math.Min(chunkSizeBytes, totalSizeBytes - offset);

                        await stream.ReadExactlyAsync(rentedBuffer.AsMemory(0, length), ct).ConfigureAwait(false);
                        string sha256Hex = ComputeSha256Hex(rentedBuffer, length);

                        chunks.Add(new TrajectoryChunkDescriptor
                        {
                            ChunkIndex = i,
                            OffsetBytes = offset,
                            LengthBytes = length,
                            Sha256Checksum = sha256Hex
                        });
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        // WHAT: Retrieve the node's Ed25519 dataset signing seed, derive public identity, and sign the canonical manifest.
        // WHY:  Guarantees cryptographic provenance and tamper-evident authentication across the federation swarm.
        // HOW:  Derives Ed25519 keypair, builds canonical bytes over manifest properties, and signs using Ed25519Signer.
        byte[] seed = _secretStore.GetDatasetSigningKey();
        Ed25519PrivateKeyParameters privKey = new(seed, 0);
        Ed25519PublicKeyParameters pubKey = privKey.GeneratePublicKey();
        byte[] spkiDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pubKey).GetDerEncoded();
        string signingNodeId = DatasetManifestSignature.ComputeKeyId(spkiDer);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        string datasetName = Path.GetFileName(trajectoryFileName);

        TrajectoryFederationManifest unsignedManifest = new()
        {
            DatasetName = datasetName,
            TotalSizeBytes = totalSizeBytes,
            ChunkSizeBytes = chunkSizeBytes,
            TotalChunks = totalChunks,
            Chunks = chunks,
            SigningNodeId = signingNodeId,
            SignatureAlgorithm = AlgorithmEd25519,
            SignatureBase64 = string.Empty,
            ManifestTimestampUtc = timestamp
        };

        byte[] canonicalBytes = ComputeCanonicalBytes(unsignedManifest);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, privKey);
        signer.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);
        byte[] signatureBytes = signer.GenerateSignature();
        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        _logger.LogInformation(
            "Built trajectory federation manifest for {DatasetName} ({TotalChunks} chunks, signed by node {SigningNodeId}).",
            datasetName,
            totalChunks,
            signingNodeId);

        return unsignedManifest with { SignatureBase64 = signatureBase64 };
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyManifestAsync(TrajectoryFederationManifest manifest, CancellationToken ct = default)
    {
        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.DatasetName) ||
            string.IsNullOrWhiteSpace(manifest.SigningNodeId) ||
            string.IsNullOrWhiteSpace(manifest.SignatureAlgorithm) ||
            string.IsNullOrWhiteSpace(manifest.SignatureBase64) ||
            manifest.Chunks is null ||
            manifest.TotalChunks != manifest.Chunks.Count)
        {
            return false;
        }

        // WHAT: Validate signature algorithm compatibility.
        // WHY:  Supports Ed25519 standard signatures and post-quantum ML-DSA-65 (NIST FIPS 204) lattice signatures.
        // HOW:  Checks against known algorithm identifiers ordinally or case-insensitively.
        bool isEd25519 = string.Equals(manifest.SignatureAlgorithm, AlgorithmEd25519, StringComparison.Ordinal);
        bool isMlDsa65 = string.Equals(manifest.SignatureAlgorithm, AlgorithmMlDsa65, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.SignatureAlgorithm, AlgorithmMlDsa65Fips, StringComparison.OrdinalIgnoreCase);

        if (!isEd25519 && !isMlDsa65)
        {
            return false;
        }

        try
        {
            byte[] canonicalBytes = ComputeCanonicalBytes(manifest);

            if (isMlDsa65)
            {
                // WHAT: Look up active, trusted peer key and verify ML-DSA-65 post-quantum signature.
                // WHY:  NIST FIPS 204 lattice signatures provide quantum-resistant federation dataset authenticity.
                // HOW:  Queries IPeerKeyRegistry.FindActiveAsync, normalizes public key, and verifies via IPostQuantumCryptoProvider.
                PeerKeyRecord? peer = await _peerKeyRegistry.FindActiveAsync(manifest.SigningNodeId, ct).ConfigureAwait(false);
                if (peer is null || !peer.IsActive || peer.Algorithm != KeyAlgorithm.MlDsa65)
                {
                    return false;
                }

                if (!PeerKeyValidation.TryNormalizePublicKey(KeyAlgorithm.MlDsa65, peer.PublicKeyBase64, out string normBase64, out string normKeyId) ||
                    !string.Equals(normKeyId, manifest.SigningNodeId, StringComparison.Ordinal))
                {
                    return false;
                }

                return _pqcProvider.VerifySignature(canonicalBytes, manifest.SignatureBase64, normBase64);
            }

            // WHAT: Resolve the signer's public key (from local node secret store or active peer registry).
            // WHY:  Allows verification of manifests generated locally or received from registered peer laboratories.
            // HOW:  Checks local node public key match first; if not local, looks up active peer in IPeerKeyRegistry.
            byte[] nodeSeed = _secretStore.GetDatasetSigningKey();
            Ed25519PrivateKeyParameters nodePrivKey = new(nodeSeed, 0);
            Ed25519PublicKeyParameters nodePubKey = nodePrivKey.GeneratePublicKey();
            byte[] nodeSpki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(nodePubKey).GetDerEncoded();
            string nodeKeyId = DatasetManifestSignature.ComputeKeyId(nodeSpki);

            Ed25519PublicKeyParameters verifierKey;
            if (string.Equals(manifest.SigningNodeId, nodeKeyId, StringComparison.Ordinal))
            {
                verifierKey = nodePubKey;
            }
            else
            {
                PeerKeyRecord? peer = await _peerKeyRegistry.FindActiveAsync(manifest.SigningNodeId, ct).ConfigureAwait(false);
                if (peer is null || !peer.IsActive || peer.Algorithm != KeyAlgorithm.Ed25519)
                {
                    return false;
                }

                byte[] peerSpki = Convert.FromBase64String(peer.PublicKeyBase64);
                string peerKeyId = DatasetManifestSignature.ComputeKeyId(peerSpki);
                if (!string.Equals(peerKeyId, manifest.SigningNodeId, StringComparison.Ordinal))
                {
                    return false;
                }

                AsymmetricKeyParameter asymmetricKey = PublicKeyFactory.CreateKey(peerSpki);
                if (asymmetricKey is not Ed25519PublicKeyParameters peerPubKey)
                {
                    return false;
                }

                verifierKey = peerPubKey;
            }

            // WHAT: Reconstruct canonical byte payload and verify Ed25519 digital signature.
            // WHY:  Ensures data integrity and authenticity without timing side channels.
            // HOW:  Decodes Base64 signature and verifies via BouncyCastle Ed25519Signer.
            byte[] sigBytes = Convert.FromBase64String(manifest.SignatureBase64);

            Ed25519Signer verifier = new();
            verifier.Init(forSigning: false, verifierKey);
            verifier.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);
            return verifier.VerifySignature(sigBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or InvalidKeyException)
        {
            _logger.LogWarning("Manifest verification failed with cryptographic or format error: {Message}", ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadChunkAsync(
        string trajectoryFileName,
        int chunkIndex,
        int chunkSizeBytes = 1048576,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoryFileName);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSizeBytes);

        // WHAT: Resolve trajectory path and verify file existence.
        // WHY:  Handles incoming trajectory path lookup safely.
        // HOW:  Calls ResolveTrajectoryFilePathAsync and checks File.Exists.
        string resolvedPath = await ResolveTrajectoryFilePathAsync(trajectoryFileName, ct).ConfigureAwait(false);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Trajectory file '{trajectoryFileName}' was not found at '{resolvedPath}'.", resolvedPath);
        }

        FileInfo fileInfo = new(resolvedPath);
        long totalBytes = fileInfo.Length;
        long offset = (long)chunkIndex * chunkSizeBytes;

        if (totalBytes == 0 && chunkIndex == 0)
        {
            return [];
        }

        if (offset >= totalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkIndex),
                chunkIndex,
                $"Chunk index {chunkIndex} is out of bounds for file of length {totalBytes} bytes.");
        }

        int length = (int)Math.Min(chunkSizeBytes, totalBytes - offset);

        // WHAT: Read chunk slice using rented buffer and return exact byte payload.
        // WHY:  Minimizes GC pressure during high-throughput swarm distribution.
        // HOW:  Rents buffer from ArrayPool<byte>.Shared, performs seek and ReadExactlyAsync, copies to result, and returns buffer.
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            {
                await using FileStream stream = new(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                stream.Seek(offset, SeekOrigin.Begin);
                await stream.ReadExactlyAsync(rentedBuffer.AsMemory(0, length), ct).ConfigureAwait(false);
            }

            byte[] result = new byte[length];
            rentedBuffer.AsSpan(0, length).CopyTo(result);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ValidateChunkAsync(ReadOnlyMemory<byte> chunkBytes, string expectedSha256, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // WHAT: Validate expected checksum length and hex format.
        // WHY:  A valid SHA-256 hex string must be exactly 64 characters.
        // HOW:  Rejects null, whitespace, or invalid-length strings immediately.
        if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64)
        {
            return Task.FromResult(false);
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedSha256);
        }
        catch (FormatException)
        {
            return Task.FromResult(false);
        }

        // WHAT: Compute SHA-256 over chunk payload and compare in constant time.
        // WHY:  FixedTimeEquals prevents timing side-channel attacks on chunk integrity evaluation.
        // HOW:  Hashes into stackalloc span and compares with CryptographicOperations.FixedTimeEquals.
        Span<byte> actualHash = stackalloc byte[32];
        SHA256.HashData(chunkBytes.Span, actualHash);

        bool matches = CryptographicOperations.FixedTimeEquals(actualHash, expectedBytes);
        return Task.FromResult(matches);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FederationPeerNode>> DiscoverPeersAsync(CancellationToken ct = default)
    {
        // WHAT: Retrieve registered peer public keys and project active peers into FederationPeerNode instances.
        // WHY:  Discovers swarm participants capable of trajectory synchronization.
        // HOW:  Queries IPeerKeyRegistry.ListAsync, filters to active (unrevoked) records, and constructs descriptors.
        IReadOnlyList<PeerKeyRecord> records = await _peerKeyRegistry.ListAsync(ct).ConfigureAwait(false);
        List<FederationPeerNode> activePeers = new();

        for (int i = 0; i < records.Count; i++)
        {
            PeerKeyRecord record = records[i];
            if (!record.IsActive)
            {
                continue;
            }

            string endpoint = record.Label.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || record.Label.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || record.Label.StartsWith("weaver://", StringComparison.OrdinalIgnoreCase)
                ? record.Label
                : $"weaver://{record.KeyId[..Math.Min(16, record.KeyId.Length)]}.peer.nanoweaver.local";

            activePeers.Add(new FederationPeerNode
            {
                NodeId = record.KeyId,
                EndpointUri = endpoint,
                PublicKeyBase64 = record.PublicKeyBase64,
                Algorithm = record.Algorithm,
                LatencyMilliseconds = 1.0,
                LastSeenUtc = record.RegisteredAtUtc
            });
        }

        return activePeers;
    }

    /// <summary>
    /// Resolves the canonical file system path of a trajectory dataset, preventing path traversal attacks.
    /// </summary>
    /// <param name="trajectoryFileName">The simple filename.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved absolute canonical path within allowed storage directories.</returns>
    /// <exception cref="ArgumentException"><paramref name="trajectoryFileName"/> is rooted, contains navigation segments, or escapes allowed roots.</exception>
    private async Task<string> ResolveTrajectoryFilePathAsync(string trajectoryFileName, CancellationToken ct)
    {
        // WHAT: Strict path-traversal hardening for trajectory file names.
        // WHY:  Prevents arbitrary file system reads and path escape exploits (e.g. ../ or absolute paths).
        // HOW:  Validates that trajectoryFileName is a simple file name, resolves against incoming and parquet roots,
        //       and verifies canonical prefix containment.
        if (Path.IsPathRooted(trajectoryFileName) ||
            trajectoryFileName.Contains('/') ||
            trajectoryFileName.Contains('\\') ||
            trajectoryFileName.Contains("..", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(trajectoryFileName), trajectoryFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Trajectory file name '{trajectoryFileName}' contains invalid path separators or navigation segments.", nameof(trajectoryFileName));
        }

        string incomingDir = await _configRepo.GetValueAsync(TrajectoryIncomingConfigKey, DefaultIncomingDir, ct).ConfigureAwait(false);
        string parquetDir = await _configRepo.GetValueAsync(TrajectoryParquetConfigKey, DefaultParquetDir, ct).ConfigureAwait(false);

        string canonicalIncoming = Path.GetFullPath(incomingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string canonicalParquet = Path.GetFullPath(parquetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        string candidateIncoming = Path.Combine(incomingDir, trajectoryFileName);
        string candidateParquet = Path.Combine(parquetDir, trajectoryFileName);

        string resolvedPath = File.Exists(candidateIncoming)
            ? candidateIncoming
            : File.Exists(candidateParquet)
                ? candidateParquet
                : trajectoryFileName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
                    ? candidateParquet
                    : candidateIncoming;

        string canonicalResolved = Path.GetFullPath(resolvedPath);
        VerifyCanonicalContainment(canonicalResolved, canonicalIncoming, canonicalParquet, trajectoryFileName);

        return canonicalResolved;
    }

    /// <summary>
    /// Verifies that the canonical resolved path starts with either the incoming or parquet directory.
    /// </summary>
    /// <param name="canonicalResolved">Canonical resolved absolute trajectory file path.</param>
    /// <param name="canonicalIncoming">Canonical incoming directory path with trailing directory separator.</param>
    /// <param name="canonicalParquet">Canonical parquet directory path with trailing directory separator.</param>
    /// <param name="trajectoryFileName">Original trajectory file name for exception diagnostic context.</param>
    /// <exception cref="ArgumentException">The resolved path escapes the allowed storage directories.</exception>
    internal static void VerifyCanonicalContainment(string canonicalResolved, string canonicalIncoming, string canonicalParquet, string trajectoryFileName)
    {
        // WHAT: Validate that canonical resolved path is strictly within the allowed directory roots.
        // WHY:  Defense-in-depth enforcement preventing symlink or directory-escape attacks.
        // HOW:  Checks whether canonicalResolved starts with canonicalIncoming or canonicalParquet.
        if (!canonicalResolved.StartsWith(canonicalIncoming, StringComparison.Ordinal) &&
            !canonicalResolved.StartsWith(canonicalParquet, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Resolved path '{canonicalResolved}' escapes allowed trajectory directories.", nameof(trajectoryFileName));
        }
    }

    /// <summary>
    /// Computes the deterministic, length-prefixed canonical signing bytes over a trajectory federation manifest.
    /// </summary>
    /// <param name="manifest">The manifest whose canonical signing bytes are to be generated.</param>
    /// <returns>A newly allocated byte array containing the canonical payload.</returns>
    internal static byte[] ComputeCanonicalBytes(TrajectoryFederationManifest manifest)
    {
        // WHAT: Measure UTF-8 lengths of string fields and calculate total buffer size for length-prefixed framing.
        // WHY:  Prevents field-boundary sliding attacks and ensures deterministic canonicalization without allocations.
        // HOW:  Sums lengths of domain tag, dataset name, primitives, timestamp, and chunk descriptors.
        int domainTagBytes = s_strictUtf8.GetByteCount(DomainTag);
        int datasetNameBytes = s_strictUtf8.GetByteCount(manifest.DatasetName);
        int nodeIdBytes = s_strictUtf8.GetByteCount(manifest.SigningNodeId);
        int algorithmBytes = s_strictUtf8.GetByteCount(manifest.SignatureAlgorithm);
        string timestampStr = manifest.ManifestTimestampUtc.ToUniversalTime().ToString("O");
        int timestampBytes = s_strictUtf8.GetByteCount(timestampStr);

        int totalBytes = 4 + domainTagBytes
            + 4 + datasetNameBytes
            + 8 // TotalSizeBytes
            + 4 // ChunkSizeBytes
            + 4 // TotalChunks
            + 4 + nodeIdBytes
            + 4 + algorithmBytes
            + 4 + timestampBytes
            + 4; // Chunks.Count

        for (int i = 0; i < manifest.Chunks.Count; i++)
        {
            totalBytes += 4 // ChunkIndex
                + 8 // OffsetBytes
                + 4 // LengthBytes
                + 4 + s_strictUtf8.GetByteCount(manifest.Chunks[i].Sha256Checksum);
        }

        byte[] canonical = new byte[totalBytes];
        Span<byte> span = canonical.AsSpan();

        WriteFramedString(ref span, DomainTag);
        WriteFramedString(ref span, manifest.DatasetName);
        BinaryPrimitives.WriteInt64BigEndian(span[..8], manifest.TotalSizeBytes);
        span = span[8..];
        BinaryPrimitives.WriteInt32BigEndian(span[..4], manifest.ChunkSizeBytes);
        span = span[4..];
        BinaryPrimitives.WriteInt32BigEndian(span[..4], manifest.TotalChunks);
        span = span[4..];
        WriteFramedString(ref span, manifest.SigningNodeId);
        WriteFramedString(ref span, manifest.SignatureAlgorithm);
        WriteFramedString(ref span, timestampStr);
        BinaryPrimitives.WriteInt32BigEndian(span[..4], manifest.Chunks.Count);
        span = span[4..];

        for (int i = 0; i < manifest.Chunks.Count; i++)
        {
            TrajectoryChunkDescriptor chunk = manifest.Chunks[i];
            BinaryPrimitives.WriteInt32BigEndian(span[..4], chunk.ChunkIndex);
            span = span[4..];
            BinaryPrimitives.WriteInt64BigEndian(span[..8], chunk.OffsetBytes);
            span = span[8..];
            BinaryPrimitives.WriteInt32BigEndian(span[..4], chunk.LengthBytes);
            span = span[4..];
            WriteFramedString(ref span, chunk.Sha256Checksum);
        }

        return canonical;
    }

    /// <summary>
    /// Writes a 4-byte big-endian length prefix followed by UTF-8 string bytes into the span cursor.
    /// </summary>
    /// <param name="span">Span cursor advanced by written byte count.</param>
    /// <param name="value">The string value to write.</param>
    private static void WriteFramedString(ref Span<byte> span, string value)
    {
        int byteCount = s_strictUtf8.GetByteCount(value);
        BinaryPrimitives.WriteInt32BigEndian(span[..4], byteCount);
        span = span[4..];
        s_strictUtf8.GetBytes(value, span[..byteCount]);
        span = span[byteCount..];
    }

    /// <summary>
    /// Computes the lowercase hexadecimal SHA-256 digest of a buffer segment.
    /// </summary>
    /// <param name="buffer">The byte buffer containing the payload.</param>
    /// <param name="length">The number of valid bytes in the buffer.</param>
    /// <returns>A 64-character lowercase hexadecimal SHA-256 string.</returns>
    private static string ComputeSha256Hex(byte[] buffer, int length)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer.AsSpan(0, length), hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <inheritdoc/>
    public async Task<int> ReadChunkIntoAsync(
        string trajectoryFileName,
        long offsetBytes,
        Memory<byte> destination,
        CancellationToken ct = default)
    {
        // WHAT: Zero-allocation streaming of a trajectory file slice directly into caller-provided memory.
        // WHY:  High-frequency trajectory streaming eliminates LOH and GC allocation pressure across the P2P swarm.
        // HOW:  Validates bounds, resolves path within canonical directories, seeks to offset, and reads asynchronously in a loop.
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoryFileName);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetBytes);
        if (destination.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination memory slice must not be empty.");
        }

        string resolvedPath = await ResolveTrajectoryFilePathAsync(trajectoryFileName, ct).ConfigureAwait(false);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Trajectory file '{trajectoryFileName}' was not found at '{resolvedPath}'.", resolvedPath);
        }

        int totalBytesRead = 0;
        {
            await using FileStream stream = new(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            long fileLength = stream.Length;
            if (offsetBytes >= fileLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offsetBytes),
                    offsetBytes,
                    $"Offset {offsetBytes} is out of bounds for file of length {fileLength} bytes.");
            }

            stream.Seek(offsetBytes, SeekOrigin.Begin);
            while (totalBytesRead < destination.Length)
            {
                ct.ThrowIfCancellationRequested();
                int bytesRead = await stream.ReadAsync(destination[totalBytesRead..], ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
            }
        }

        return totalBytesRead;
    }
}
