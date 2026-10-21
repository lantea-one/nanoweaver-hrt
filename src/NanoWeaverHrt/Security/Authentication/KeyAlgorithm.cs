using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Specifies the cryptographic signature algorithm associated with a user's asymmetric public/private key identity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeyAlgorithm
{
    /// <summary>
    /// Edwards-curve Digital Signature Algorithm over Curve25519 (RFC 8032), providing high-speed, compact signatures.
    /// </summary>
    Ed25519,

    /// <summary>
    /// NIST FIPS 204 Module-Lattice-Based Digital Signature Algorithm (ML-DSA-65) providing post-quantum resistance.
    /// </summary>
    MlDsa65
}
