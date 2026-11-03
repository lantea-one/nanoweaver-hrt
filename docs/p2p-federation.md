# P2P Trajectory Swarm Federation & Sovereign Peer Synchronization

> **Decentralized, Cryptographically Signed Molecular Dynamics Trajectory Exchange for Federated Synthetic Biology Research**

---

## 1. Overview & Problem Statement

Molecular dynamics simulations of DNA origami nanocarriers generate massive trajectory coordinate bundles (tens of gigabytes of `.xyz`, `.dcd`, and `.parquet` frames). In centralized scientific workflows, sharing these multi-gigabyte files creates severe bandwidth bottlenecks, introduces single points of failure, and exposes sensitive synthetic biology research to surveillance and administrative disruption.

NanoWeaverHrt introduces **P2P Trajectory Swarm Federation**: a decentralized, peer-to-peer data distribution mechanism allowing sovereign laboratory nodes to discover peers, exchange chunked trajectory streams, verify cryptographic manifests, and maintain immutable data integrity across federated clusters.

---

## 2. Technical Architecture

```
+-----------------------------------------------------------------------------------+
|                        Trajectory Federation Architecture                         |
+-----------------------------------------------------------------------------------+

     +-----------------------+                    +-----------------------+
     |   Publishing Node     |                    |   Subscribing Node    |
     |   (Key: Ed25519)      |                    |   (Peer Registry)     |
     +-----------+-----------+                    +-----------+-----------+
                 |                                            |
                 | 1. Stream Trajectory & Slice Chunks        |
                 |    (1 MB blocks, ArrayPool rented)         |
                 v                                            |
     +-----------------------+                                |
     | Compute SHA-256 Chunk |                                |
     | Hashing & Manifest    |                                |
     +-----------+-----------+                                |
                 |                                            |
                 | 2. Sign Manifest (Ed25519 / ML-DSA-65)     |
                 v                                            |
     +-----------------------+   3. P2P Sync Request          |
     | Federation Manifest   | -----------------------------> |
     +-----------------------+                                |
                 |                                            |
                 | 4. Verify Signature (PeerKeyRegistry)      |
                 |    & Validate Signer Trust                 |
                 v                                            v
     +-----------------------+                   +-----------------------+
     | Stream Chunks via P2P | <================ | Request Missing       |
     | (Offset, Length, Hash)|                   | Chunks (Multi-Peer)   |
     +-----------------------+                   +-----------------------+
```

### Core Components

1. **`TrajectoryChunkDescriptor`**:
   - Represents an immutable, content-addressed slice of a trajectory archive:
     - `ChunkIndex`: 0-based sequence index.
     - `OffsetBytes`: Byte offset within the uncompressed or archive stream.
     - `LengthBytes`: Exact byte count (default: 1,048,576 bytes / 1 MB).
     - `Sha256Checksum`: Lowercase 64-character hexadecimal SHA-256 fingerprint of the chunk bytes.

2. **`TrajectoryFederationManifest`**:
   - Cryptographic manifest describing the entire trajectory dataset:
     - `DatasetName`: Canonical dataset identifier.
     - `TotalSizeBytes`: Aggregate file size in bytes.
     - `ChunkSizeBytes`: Standard chunk dimension.
     - `TotalChunks`: Total number of descriptors.
     - `Chunks`: Ordered collection of `TrajectoryChunkDescriptor` records.
     - `SigningNodeId`: Hexadecimal fingerprint of the signing node's SubjectPublicKeyInfo.
     - `SignatureAlgorithm`: Asymmetric algorithm (`Ed25519` or `MlDsa65`).
     - `SignatureBase64`: Base64-encoded cryptographic signature over canonical manifest payload bytes.
     - `ManifestTimestampUtc`: UTC creation timestamp.

3. **`FederationPeerNode`**:
   - Peer node metadata discovered in the peer network:
     - `NodeId`: Unique node identifier derived from public key fingerprint.
     - `EndpointUri`: HTTPS/gRPC endpoint for peer synchronization.
     - `PublicKeyBase64`: Canonical SubjectPublicKeyInfo DER public key.
     - `Algorithm`: Asymmetric signature scheme.
     - `LatencyMilliseconds`: Measured round-trip communication latency.
     - `LastSeenUtc`: Most recent heartbeat timestamp.

4. **`ITrajectoryFederationService` & `TrajectoryFederationService`**:
   - High-throughput, zero-allocation service implementing:
     - `BuildManifestAsync`: Streams the trajectory file, slices 1 MB chunks, computes per-chunk SHA-256 hashes, and signs the completed manifest with the local node's persisted signing key (`INodeSecretStore`).
     - `VerifyManifestAsync`: Resolves the signing key against the node's own key or trusted peer records in `IPeerKeyRegistry`, verifying cryptographic validity before accepting foreign chunks.
     - `ReadChunkAsync`: Rents memory buffers from `ArrayPool<byte>.Shared` to read exact chunk slices at specified offsets without heap allocations.
     - `ValidateChunk`: Performs constant-time cryptographic verification comparing candidate byte hashes against expected manifest checksums.
     - `DiscoverPeersAsync`: Queries active, trusted peer nodes registered in the sovereign node's peer key repository.

---

## 3. Cryptographic Verification & Trust Boundaries

The federation engine enforces strict trust boundaries:
- **No Untrusted Code Execution**: Trajectory chunks contain raw byte coordinate data only. Parsers process coordinates using safe, bounds-checked blit operations.
- **Constant-Time Verification**: Hash comparisons utilize constant-time equality checks (`CryptographicOperations.FixedTimeEquals`) to eliminate timing side-channel leakage.
- **Sovereign Peer Gating**: Nodes only pull chunks from peers whose signing keys have been explicitly ratified in `IPeerKeyRegistry` by an Administrator.
- **Fail-Closed Policy**: If a manifest signature is invalid, expired, or references an unratified key ID, the swarm engine rejects the dataset before writing any bytes to disk.

---

## 4. Usage Example

```csharp
// 1. Build a signed federation manifest for a newly computed trajectory
TrajectoryFederationManifest manifest = await federationService.BuildManifestAsync(
    trajectoryFileName: "nanocarrier_folding_72h.xyz",
    chunkSizeBytes: 1048576,
    ct: cancellationToken);

// 2. Discover active swarm peers
IReadOnlyList<FederationPeerNode> peers = await federationService.DiscoverPeersAsync(cancellationToken);

// 3. Verify an incoming peer manifest
bool isTrusted = await federationService.VerifyManifestAsync(incomingManifest, cancellationToken);
if (!isTrusted)
{
    throw new SecurityException("Manifest signature invalid or signer key unratified.");
}

// 4. Stream and validate a specific chunk
byte[] chunkBytes = await federationService.ReadChunkAsync("nanocarrier_folding_72h.xyz", chunkIndex: 0);
bool chunkValid = federationService.ValidateChunk(chunkBytes, incomingManifest.Chunks[0].Sha256Checksum);
```
