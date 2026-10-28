using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Represents a trusted peer laboratory's dataset-signing public key and its revocation audit state.
/// </summary>
[SwaggerSchema(Title = "PeerKeyRecord", Description = "Trusted peer laboratory dataset-signing public key entry and revocation metadata.")]
public sealed record PeerKeyRecord : IExamplesProvider<PeerKeyRecord>
{
    /// <summary>
    /// Lowercase 64-character hexadecimal SHA-256 fingerprint of the peer's SubjectPublicKeyInfo DER public key.
    /// </summary>
    [Description("Lowercase 64-character hexadecimal SHA-256 fingerprint of the peer's SubjectPublicKeyInfo DER public key.")]
    [JsonPropertyName("keyId")]
    public required string KeyId { get; init; }

    /// <summary>
    /// Human-readable label identifying the peer node or laboratory.
    /// </summary>
    [Description("Human-readable label identifying the peer node or laboratory.")]
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>
    /// Digital signature algorithm associated with this peer public key (<see cref="KeyAlgorithm.Ed25519"/> or <see cref="KeyAlgorithm.MlDsa65"/>).
    /// </summary>
    [Description("Digital signature algorithm associated with this peer public key (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public required KeyAlgorithm Algorithm { get; init; }

    /// <summary>
    /// Canonical Base64-encoded SubjectPublicKeyInfo DER representation of the peer's public key.
    /// </summary>
    [Description("Canonical Base64-encoded SubjectPublicKeyInfo DER representation of the peer's public key.")]
    [JsonPropertyName("publicKeyBase64")]
    public required string PublicKeyBase64 { get; init; }

    /// <summary>
    /// Username of the Administrator who registered this peer key.
    /// </summary>
    [Description("Username of the Administrator who registered this peer key.")]
    [JsonPropertyName("registeredBy")]
    public required string RegisteredBy { get; init; }

    /// <summary>
    /// UTC timestamp when this peer key was registered.
    /// </summary>
    [Description("UTC timestamp when this peer key was registered.")]
    [JsonPropertyName("registeredAtUtc")]
    public required DateTimeOffset RegisteredAtUtc { get; init; }

    /// <summary>
    /// Username of the Administrator who revoked this peer key, or <c>null</c> while active.
    /// </summary>
    [Description("Username of the Administrator who revoked this peer key, or null while active.")]
    [JsonPropertyName("revokedBy")]
    public string? RevokedBy { get; init; }

    /// <summary>
    /// UTC timestamp when this peer key was revoked, or <c>null</c> while active.
    /// </summary>
    [Description("UTC timestamp when this peer key was revoked, or null while active.")]
    [JsonPropertyName("revokedAtUtc")]
    public DateTimeOffset? RevokedAtUtc { get; init; }

    /// <summary>
    /// Indicates whether this peer key is currently active and trusted for dataset manifest verification.
    /// </summary>
    [Description("Indicates whether this peer key is currently active and trusted for dataset manifest verification.")]
    [JsonPropertyName("isActive")]
    public bool IsActive => RevokedAtUtc is null;

    /// <summary>
    /// Generates a representative example instance of <see cref="PeerKeyRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated peer key record example.</returns>
    public PeerKeyRecord GetExamples() => new()
    {
        KeyId = "1ee7bc6377fc1f2befef5a5a324d86df31df832a27f9d9bd37846d313db1a5a2",
        Label = "Partner Lab Zurich — Molecular Dynamics Node",
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
        RegisteredBy = "admin",
        RegisteredAtUtc = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
        RevokedBy = null,
        RevokedAtUtc = null
    };
}

/// <summary>
/// Request payload for registering a trusted peer laboratory's dataset-signing public key.
/// </summary>
[SwaggerSchema(Title = "RegisterPeerKeyRequest", Description = "Request payload to register a trusted peer laboratory's dataset-signing public key.")]
public sealed record RegisterPeerKeyRequest : IExamplesProvider<RegisterPeerKeyRequest>
{
    /// <summary>
    /// Human-readable label identifying the peer node or laboratory (1 to 128 printable characters).
    /// </summary>
    [Description("Human-readable label identifying the peer node or laboratory (1 to 128 printable characters).")]
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>
    /// Digital signature algorithm of the peer's public key (<see cref="KeyAlgorithm.Ed25519"/> or <see cref="KeyAlgorithm.MlDsa65"/>).
    /// </summary>
    [Description("Digital signature algorithm of the peer's public key (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; }

    /// <summary>
    /// Base64-encoded SubjectPublicKeyInfo DER public key of the peer node.
    /// </summary>
    [Description("Base64-encoded SubjectPublicKeyInfo DER public key of the peer node.")]
    [JsonPropertyName("publicKeyBase64")]
    public string? PublicKeyBase64 { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RegisterPeerKeyRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated peer key registration request example.</returns>
    public RegisterPeerKeyRequest GetExamples() => new()
    {
        Label = "Partner Lab Zurich — Molecular Dynamics Node",
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S="
    };
}

/// <summary>
/// Public dataset manifest signing identity of the local NanoWeaver node.
/// </summary>
/// <param name="KeyId">Lowercase 64-character hexadecimal SHA-256 fingerprint of this node's SubjectPublicKeyInfo DER public key.</param>
/// <param name="Algorithm">Digital signature algorithm of this node's dataset signing key.</param>
/// <param name="PublicKeyBase64">Canonical Base64-encoded SubjectPublicKeyInfo DER representation of this node's public key.</param>
[SwaggerSchema(Title = "LocalDatasetSigningKey", Description = "Public dataset manifest signing identity of the local NanoWeaver node.")]
public sealed record LocalDatasetSigningKey(
    [property: Description("Lowercase 64-character hexadecimal SHA-256 fingerprint of this node's SubjectPublicKeyInfo DER public key."), JsonPropertyName("keyId")]
    string KeyId,
    [property: Description("Digital signature algorithm of this node's dataset signing key."), JsonPropertyName("algorithm")]
    KeyAlgorithm Algorithm,
    [property: Description("Canonical Base64-encoded SubjectPublicKeyInfo DER representation of this node's public key."), JsonPropertyName("publicKeyBase64")]
    string PublicKeyBase64) : IExamplesProvider<LocalDatasetSigningKey>
{
    /// <summary>
    /// Generates a representative example instance of <see cref="LocalDatasetSigningKey"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated local dataset signing key example.</returns>
    public LocalDatasetSigningKey GetExamples() => new(
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        KeyAlgorithm.Ed25519,
        "MCowBQYDK2VwAyEA1a2b3c4d5e6f7g8h9i0j1k2l3m4n5o6p7q8r9s0t1u2=");
}

/// <summary>
/// Response payload returned by <c>GET /api/peer-keys</c> containing this node's public dataset signing identity and all registered peer keys.
/// </summary>
[SwaggerSchema(Title = "PeerKeyListResponse", Description = "Local node dataset signing public key and registered peer laboratory keys.")]
public sealed record PeerKeyListResponse : IExamplesProvider<PeerKeyListResponse>
{
    /// <summary>
    /// Public dataset signing key identity of this local node, suitable for sharing with peer administrators out of band.
    /// </summary>
    [Description("Public dataset signing key identity of this local node.")]
    [JsonPropertyName("localNode")]
    public required LocalDatasetSigningKey LocalNode { get; init; }

    /// <summary>
    /// Ordered list of all peer keys registered on this node, including both active and permanently revoked entries.
    /// </summary>
    [Description("Ordered list of all peer keys registered on this node, including both active and revoked entries.")]
    [JsonPropertyName("peers")]
    public required IReadOnlyList<PeerKeyRecord> Peers { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="PeerKeyListResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated peer key list response example.</returns>
    public PeerKeyListResponse GetExamples() => new()
    {
        LocalNode = new LocalDatasetSigningKey(
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            KeyAlgorithm.Ed25519,
            "MCowBQYDK2VwAyEA1a2b3c4d5e6f7g8h9i0j1k2l3m4n5o6p7q8r9s0t1u2="),
        Peers =
        [
            new PeerKeyRecord
            {
                KeyId = "1ee7bc6377fc1f2befef5a5a324d86df31df832a27f9d9bd37846d313db1a5a2",
                Label = "Partner Lab Zurich — Molecular Dynamics Node",
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEAX1Y2Z3A4B5C6D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S=",
                RegisteredBy = "admin",
                RegisteredAtUtc = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
                RevokedBy = null,
                RevokedAtUtc = null
            }
        ]
    };
}
