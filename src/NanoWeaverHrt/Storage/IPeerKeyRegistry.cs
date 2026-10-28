using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for the trusted peer dataset-signing public key registry (<c>weaver_peer_keys</c>).
/// </summary>
public interface IPeerKeyRegistry
{
    /// <summary>
    /// Inserts a new active peer key record if its <c>key_id</c> does not already exist in <c>weaver_peer_keys</c>.
    /// </summary>
    /// <param name="record">The normalized peer key record to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the row was inserted; <c>false</c> if a row with the same <c>key_id</c> already exists (active or revoked).</returns>
    Task<bool> TryRegisterAsync(PeerKeyRecord record, CancellationToken ct = default);

    /// <summary>
    /// Looks up an active (unrevoked) peer key record by its 64-character SHA-256 fingerprint.
    /// </summary>
    /// <param name="keyId">Lowercase 64-character hexadecimal SHA-256 key identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active <see cref="PeerKeyRecord"/>, or <c>null</c> when the key is unknown or has been revoked.</returns>
    Task<PeerKeyRecord?> FindActiveAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Lists all peer key records in the registry, including both active and revoked entries, ordered by <c>registered_at_utc, key_id</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered read-only list of all <see cref="PeerKeyRecord"/> rows.</returns>
    Task<IReadOnlyList<PeerKeyRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Permanently revokes an active peer key by setting <c>revoked_by</c> and <c>revoked_at_utc</c>.
    /// </summary>
    /// <param name="keyId">Lowercase 64-character hexadecimal SHA-256 key identifier to revoke.</param>
    /// <param name="revokedBy">Username of the Administrator performing the revocation.</param>
    /// <param name="revokedAtUtc">UTC timestamp of the revocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="PeerKeyRecord"/> when an active key was revoked; <c>null</c> when the key was unknown or already revoked.</returns>
    Task<PeerKeyRecord?> RevokeAsync(string keyId, string revokedBy, DateTimeOffset revokedAtUtc, CancellationToken ct = default);
}
