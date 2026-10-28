namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Represents a configured connection profile for a local or remote nanoweaver-hrt instance.
/// </summary>
/// <param name="InstanceId">Unique deterministic identifier for the instance profile.</param>
/// <param name="DisplayName">Human-readable label for the instance (e.g., "Local Workstation", "Cluster Alpha").</param>
/// <param name="BaseUri">Canonical HTTPS or localhost HTTP base URI of the instance.</param>
/// <param name="IsLocalInstance">True if the instance runs on the local loopback interface.</param>
/// <param name="Username">Lowercase normalized username used for asymmetric PKI authentication.</param>
/// <param name="Role">Expected role on the target node (Administrator, Researcher, Patient).</param>
/// <param name="KeyAlgorithm">Asymmetric signature algorithm ("Ed25519" or "ML-DSA-65").</param>
/// <param name="PublicKeyPem">PEM-encoded SubjectPublicKeyInfo registered on the instance.</param>
/// <param name="EncryptedPrivateKeyEnvelopeBase64">Base64-encoded AES-256-GCM sealed private key envelope.</param>
/// <param name="NotificationsEnabled">True if desktop/mobile alerts are enabled for this instance.</param>
/// <param name="PollingIntervalSeconds">Telemetry and health polling interval in seconds (5 to 300).</param>
/// <param name="AddedAtUtc">UTC timestamp when the profile was registered.</param>
/// <param name="LastConnectedAtUtc">Optional UTC timestamp of the most recent successful handshake.</param>
public sealed record InstanceProfile(
    string InstanceId,
    string DisplayName,
    string BaseUri,
    bool IsLocalInstance,
    string Username,
    string Role,
    string KeyAlgorithm,
    string PublicKeyPem,
    string EncryptedPrivateKeyEnvelopeBase64,
    bool NotificationsEnabled,
    int PollingIntervalSeconds,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? LastConnectedAtUtc);
