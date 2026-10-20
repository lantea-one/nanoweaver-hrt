using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Configuration;

/// <summary>
/// Represents a cryptographically secure, single-use share key used to federate an external
/// UI instance or collaborative compute worker with this local sovereign node.
/// </summary>
public sealed record ShareKeyRecord
{
    /// <summary>
    /// Unique internal identifier for the share key entry.
    /// </summary>
    [JsonPropertyName("token_id")]
    public required string TokenId { get; init; }

    /// <summary>
    /// The cryptographically secure token string delivered in the invitation link.
    /// </summary>
    [JsonPropertyName("key_token")]
    public required string KeyToken { get; init; }

    /// <summary>
    /// Human-readable label or purpose describing who or what this invitation link was created for.
    /// </summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>
    /// Timestamp when this share key was generated.
    /// </summary>
    [JsonPropertyName("created_at_utc")]
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Expiration timestamp after which the link can no longer be redeemed.
    /// </summary>
    [JsonPropertyName("expires_at_utc")]
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// Flag indicating whether this key has already been redeemed by an external instance.
    /// </summary>
    [JsonPropertyName("is_consumed")]
    public bool IsConsumed { get; init; }

    /// <summary>
    /// Timestamp when this single-use key was redeemed, or null if still pending.
    /// </summary>
    [JsonPropertyName("consumed_at_utc")]
    public DateTime? ConsumedAtUtc { get; init; }

    /// <summary>
    /// Network information or client identifier of the remote peer that redeemed this key.
    /// </summary>
    [JsonPropertyName("remote_peer_info")]
    public string? RemotePeerInfo { get; init; }

    /// <summary>
    /// Authorization scope granted to the peer (e.g., 'FullCollaborator', 'ReadOnlyTelemetry').
    /// </summary>
    [JsonPropertyName("permissions")]
    public required string Permissions { get; init; }

    /// <summary>
    /// Indicates whether this share key is configured to never expire.
    /// </summary>
    [JsonPropertyName("is_never_expires")]
    public bool IsNeverExpires => ExpiresAtUtc >= DateTime.UtcNow.AddYears(50);

    /// <summary>
    /// Checks whether this key is currently active and eligible for redemption.
    /// </summary>
    [JsonPropertyName("is_active")]
    public bool IsActive => !IsConsumed && (IsNeverExpires || DateTime.UtcNow < ExpiresAtUtc);
}

/// <summary>
/// Result returned when an external UI instance attempts to redeem a single-use share key.
/// </summary>
public sealed record ShareKeyConsumeResult
{
    /// <summary>
    /// Indicates whether the share key was successfully validated and redeemed.
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>
    /// Informative message or error description explaining the outcome of the redemption attempt.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// The share key metadata if redemption was successful, otherwise null.
    /// </summary>
    [JsonPropertyName("record")]
    public ShareKeyRecord? Record { get; init; }

    /// <summary>
    /// Authenticated session token assigned to the newly connected remote peer.
    /// </summary>
    [JsonPropertyName("peer_session_token")]
    public string? PeerSessionToken { get; init; }
}
