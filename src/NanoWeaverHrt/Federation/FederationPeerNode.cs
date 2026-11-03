using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Represents an active peer node within the sovereign trajectory federation network.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A representation of a remote laboratory node participating in trajectory dataset swarm synchronization.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Tracks network topology, public-key identity credentials, transport URIs, and observed latency for peer routing.
/// </para>
/// <para>
/// <strong>How it works:</strong> Derived from trusted peer registrations in the peer key registry with endpoint and latency metadata.
/// </para>
/// </remarks>
public sealed record FederationPeerNode
{
    /// <summary>
    /// Gets the unique 64-character lowercase hexadecimal SHA-256 fingerprint identifying this node.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Gets the network endpoint URI through which this peer node is reachable.
    /// </summary>
    public required string EndpointUri { get; init; }

    /// <summary>
    /// Gets the Base64-encoded SubjectPublicKeyInfo DER public key belonging to this peer node.
    /// </summary>
    public required string PublicKeyBase64 { get; init; }

    /// <summary>
    /// Gets the cryptographic algorithm used by this peer node (e.g., <see cref="KeyAlgorithm.Ed25519"/> or <see cref="KeyAlgorithm.MlDsa65"/>).
    /// </summary>
    public required KeyAlgorithm Algorithm { get; init; }

    /// <summary>
    /// Gets the observed round-trip latency to this peer node in milliseconds.
    /// </summary>
    public required double LatencyMilliseconds { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when this peer node was last observed or registered.
    /// </summary>
    public required DateTimeOffset LastSeenUtc { get; init; }
}
