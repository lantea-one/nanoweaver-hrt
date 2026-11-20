using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Concrete SSRF-hardened, post-quantum pinned pull-sync service implementing <see cref="IFederatedPeerSyncService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Active federation transport engine pulling <c>GET /api/fabrication/revocations/feed</c>
/// from subscribed peer NanoWeaverHrt laboratories and ingesting verified ML-DSA-65 recall tombstones.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P206): Ensures upstream clinical recalls propagate automatically
/// across sovereign clinics without exposing the node to SSRF, redirect spoofing, memory exhaustion, or unpinned keys.
/// </para>
/// <para>
/// <strong>How it works:</strong> Re-validates DNS and IP ranges before every request, caps responses to 512 KiB
/// using <see cref="ArrayPool{T}.Shared"/>, rejects 3xx redirects, enforces pinned active ML-DSA-65 peer keys,
/// counts only newly discovered lot tombstones, and logs FDA 21 CFR Part 11 audit events.
/// </para>
/// </remarks>
public sealed class FederatedPeerSyncService : IFederatedPeerSyncService
{
    /// <summary>
    /// Maximum permitted revocation feed response size in bytes (512 KiB) to prevent memory exhaustion.
    /// </summary>
    public const int MaxFeedResponseBytes = 524_288;

    /// <summary>
    /// Named <see cref="HttpClient"/> registration configured with <c>AllowAutoRedirect = false</c> and bounded timeouts.
    /// </summary>
    public const string HttpClientName = "FederatedRevocationSync";

    /// <summary>
    /// System audit operator identifier recorded during scheduled background pull-sync sweeps.
    /// </summary>
    public const string SystemOperatorId = "system:federated-revocation-sync";

    /// <summary>
    /// Maximum permitted polling interval in minutes (7 days = 10,080 minutes).
    /// </summary>
    public const int MaxSyncIntervalMinutes = 10_080;

    /// <summary>
    /// Relative path queried on peer NanoWeaverHrt nodes to fetch signed de-identified lot revocation feeds.
    /// </summary>
    private const string RevocationFeedPath = "/api/fabrication/revocations/feed";

    /// <summary>
    /// Chunk buffer size rented from <see cref="ArrayPool{T}.Shared"/> when streaming HTTP response bodies.
    /// </summary>
    private const int ReadBufferSize = 16_384;

    /// <summary>
    /// Case-insensitive JSON serializer options used when decoding peer revocation feeds.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Repository storing peer pull-sync endpoint subscriptions and sync telemetry.
    /// </summary>
    private readonly IFederatedPeerSyncRepository _syncRepository;

    /// <summary>
    /// Repository storing verified federated lot revocation tombstones.
    /// </summary>
    private readonly IFabricationLotRepository _lotRepository;

    /// <summary>
    /// Cryptographic service verifying ML-DSA-65 signed revocation feeds.
    /// </summary>
    private readonly IFabricationLotService _lotService;

    /// <summary>
    /// Registry storing active and revoked trusted peer signing keys.
    /// </summary>
    private readonly IPeerKeyRegistry _peerKeyRegistry;

    /// <summary>
    /// FDA 21 CFR Part 11 hash-chained audit logging service.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Factory supplying the redirect-disabled named HTTP client.
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Injectable DNS resolver used during SSRF IP validation.
    /// </summary>
    private readonly IFederatedDnsResolver _dnsResolver;

    /// <summary>
    /// Deterministic UTC clock provider (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedPeerSyncService"/> class.
    /// </summary>
    /// <param name="syncRepository">Peer endpoint subscription repository.</param>
    /// <param name="lotRepository">Fabrication lot &amp; federated revocation repository.</param>
    /// <param name="lotService">ML-DSA-65 revocation feed verification service.</param>
    /// <param name="peerKeyRegistry">Trusted peer public key registry.</param>
    /// <param name="auditService">Compliance audit logging service.</param>
    /// <param name="httpClientFactory">HTTP client factory providing <see cref="HttpClientName"/>.</param>
    /// <param name="dnsResolver">Injectable DNS resolver for SSRF validation.</param>
    /// <param name="utcNow">Optional deterministic UTC clock delegate for testing.</param>
    public FederatedPeerSyncService(
        IFederatedPeerSyncRepository syncRepository,
        IFabricationLotRepository lotRepository,
        IFabricationLotService lotService,
        IPeerKeyRegistry peerKeyRegistry,
        IComplianceAuditService auditService,
        IHttpClientFactory httpClientFactory,
        IFederatedDnsResolver dnsResolver,
        Func<DateTimeOffset>? utcNow = null)
    {
        // WHAT: Validates and stores all required service dependencies.
        // WHY:  Guarantees fail-fast DI wiring with zero null-reference surprises at runtime.
        // HOW:  Throws ArgumentNullException on any missing dependency and defaults the clock to UtcNow.
        ArgumentNullException.ThrowIfNull(syncRepository);
        ArgumentNullException.ThrowIfNull(lotRepository);
        ArgumentNullException.ThrowIfNull(lotService);
        ArgumentNullException.ThrowIfNull(peerKeyRegistry);
        ArgumentNullException.ThrowIfNull(auditService);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(dnsResolver);

        _syncRepository = syncRepository;
        _lotRepository = lotRepository;
        _lotService = lotService;
        _peerKeyRegistry = peerKeyRegistry;
        _auditService = auditService;
        _httpClientFactory = httpClientFactory;
        _dnsResolver = dnsResolver;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<PeerEndpointValidationResult> ValidatePeerEndpointUrlAsync(
        string baseUrl,
        bool allowLoopbackHttp,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Enforces strict SSRF, scheme, URI hygiene, and IP-range invariants on a peer base URL.
        // WHY:  Prevents SSRF attacks targeting RFC 1918 networks, cloud IMDS (169.254.169.254), or local services.
        // HOW:  Parses absolute URI, checks scheme/credentials/query/fragment, resolves all host IPs, and blocks restricted ranges.
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return InvalidUrl("Peer endpoint base URL is required.");
        }

        string trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return InvalidUrl("Peer endpoint base URL must be a valid absolute URI.");
        }

        bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            return InvalidUrl("Peer endpoint base URL must use HTTPS (or HTTP only when loopback opt-in is enabled).");
        }

        if (isHttp && !allowLoopbackHttp)
        {
            return InvalidUrl("Plaintext HTTP peer endpoints are forbidden unless loopback HTTP opt-in is explicitly enabled.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return InvalidUrl("Peer endpoint base URL must not contain embedded user credentials.");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return InvalidUrl("Peer endpoint base URL must not contain query strings or URL fragments.");
        }

        IPAddress[] resolvedAddresses;
        try
        {
            resolvedAddresses = await _dnsResolver.ResolveHostAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return InvalidUrl("Peer endpoint hostname could not be resolved.");
        }

        if (resolvedAddresses.Length == 0)
        {
            return InvalidUrl("Peer endpoint hostname resolved to zero IP addresses.");
        }

        for (int i = 0; i < resolvedAddresses.Length; i++)
        {
            IPAddress raw = resolvedAddresses[i];
            IPAddress ip = raw.IsIPv4MappedToIPv6 ? raw.MapToIPv4() : raw;
            bool isLoopback = IPAddress.IsLoopback(ip);

            if (isHttp && !isLoopback)
            {
                return InvalidUrl("Plaintext HTTP is permitted only for loopback hosts (127.0.0.0/8 or ::1).");
            }

            if (IsRestrictedIpAddress(ip, allowLoopbackHttp))
            {
                return InvalidUrl("Peer endpoint resolved to a restricted private, link-local, loopback, or multicast IP address.");
            }
        }

        string normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return new PeerEndpointValidationResult
        {
            IsValid = true,
            NormalizedBaseUrl = normalized,
            Error = null
        };
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncEndpointRecord> UpsertSubscriptionAsync(
        UpsertFederatedPeerSyncEndpointRequest request,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Validates subscription parameters, SSRF-safe base URL, and pinned active ML-DSA-65 peer key, then persists and audits.
        // WHY:  Blocks misconfigured, SSRF-unsafe, or unpinned peer subscriptions before they ever enter the scheduler.
        // HOW:  Validates PeerId/DisplayName/SyncIntervalMinutes, verifies URL and IPeerKeyRegistry, upserts, and logs audit event.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string peerId = request.PeerId?.Trim() ?? string.Empty;
        if (!IsValidPeerId(peerId))
        {
            throw new ArgumentException("PeerId must be 1 to 64 ASCII characters using only letters, digits, '.', '_', or '-'.", nameof(request));
        }

        string displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0 || displayName.Length > 160)
        {
            throw new ArgumentException("DisplayName must be between 1 and 160 characters.", nameof(request));
        }

        if (request.SyncIntervalMinutes < 1 || request.SyncIntervalMinutes > MaxSyncIntervalMinutes)
        {
            throw new ArgumentException($"SyncIntervalMinutes must be between 1 and {MaxSyncIntervalMinutes}.", nameof(request));
        }

        PeerEndpointValidationResult urlValidation = await ValidatePeerEndpointUrlAsync(
            request.BaseUrl,
            request.AllowLoopbackHttp,
            cancellationToken).ConfigureAwait(false);
        if (!urlValidation.IsValid)
        {
            throw new ArgumentException(urlValidation.Error, nameof(request));
        }

        string pinnedKeyId = request.PinnedKeyId?.Trim() ?? string.Empty;
        if (pinnedKeyId.Length == 0)
        {
            throw new ArgumentException("PinnedKeyId is required and must reference an active ML-DSA-65 peer key.", nameof(request));
        }

        PeerKeyRecord? activeKey = await _peerKeyRegistry.FindActiveAsync(pinnedKeyId, cancellationToken).ConfigureAwait(false);
        if (activeKey is null || activeKey.Algorithm != KeyAlgorithm.MlDsa65)
        {
            throw new ArgumentException("PinnedKeyId must reference an active (non-revoked) ML-DSA-65 key in the peer key registry.", nameof(request));
        }

        DateTimeOffset now = _utcNow();
        FederatedPeerSyncEndpointRecord persisted = await _syncRepository.UpsertEndpointAsync(
            new FederatedPeerSyncEndpointRecord
            {
                PeerId = peerId,
                DisplayName = displayName,
                // NormalizedBaseUrl is guaranteed non-null whenever urlValidation.IsValid is true.
                BaseUrl = urlValidation.NormalizedBaseUrl!,
                PinnedKeyId = activeKey.KeyId,
                Enabled = request.Enabled,
                SyncIntervalMinutes = request.SyncIntervalMinutes,
                AllowLoopbackHttp = request.AllowLoopbackHttp,
                LastStatus = FederatedPeerSyncStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            cancellationToken).ConfigureAwait(false);

        string auditDetails = JsonSerializer.Serialize(new
        {
            peerId = persisted.PeerId,
            baseUrl = persisted.BaseUrl,
            pinnedKeyId = persisted.PinnedKeyId,
            enabled = persisted.Enabled,
            syncIntervalMinutes = persisted.SyncIntervalMinutes,
            allowLoopbackHttp = persisted.AllowLoopbackHttp
        });
        _auditService.LogAction(operatorId, "FABRICATION_PEER_SUBSCRIPTION_UPSERT", $"peer-sync/{persisted.PeerId}", auditDetails);

        return persisted;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FederatedPeerSyncEndpointRecord>> ListSubscriptionsAsync(
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Returns persisted peer revocation pull-sync subscriptions.
        // WHY:  Exposes read-only subscription inventory to API callers, Blazor UI, and background sweeps.
        // HOW:  Delegates directly to IFederatedPeerSyncRepository.ListEndpointsAsync.
        return _syncRepository.ListEndpointsAsync(enabledOnly, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSubscriptionAsync(
        string peerId,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Deletes a peer subscription and records an audit event if a row was removed.
        // WHY:  Maintains full 21 CFR Part 11 traceability for federation topology changes.
        // HOW:  Validates arguments, delegates deletion to repository, and logs FABRICATION_PEER_SUBSCRIPTION_DELETE on true.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string trimmedPeerId = peerId.Trim();
        bool deleted = await _syncRepository.DeleteEndpointAsync(trimmedPeerId, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            string auditDetails = JsonSerializer.Serialize(new { peerId = trimmedPeerId });
            _auditService.LogAction(operatorId, "FABRICATION_PEER_SUBSCRIPTION_DELETE", $"peer-sync/{trimmedPeerId}", auditDetails);
        }

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncRunResult> SyncPeerAsync(
        string peerId,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Executes an SSRF-checked, 512 KiB bounded, pinned ML-DSA-65 revocation pull-sync for one peer.
        // WHY:  Automatically ingests upstream wet-lab recalls while rejecting DNS rebinding, redirects, tampering, or revoked signers.
        // HOW:  Loads endpoint, re-validates URL/DNS, checks pinned key, fetches feed, verifies ML-DSA-65 signature, upserts tombstones, updates telemetry, and audits.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        string trimmedPeerId = peerId.Trim();
        DateTimeOffset attemptedAtUtc = _utcNow();

        FederatedPeerSyncEndpointRecord? endpoint = await _syncRepository.GetEndpointAsync(trimmedPeerId, cancellationToken).ConfigureAwait(false);
        if (endpoint is null)
        {
            return new FederatedPeerSyncRunResult
            {
                PeerId = trimmedPeerId,
                EndpointFound = false,
                Succeeded = false,
                ImportedCount = 0,
                FeedEntryCount = 0,
                Summary = $"Peer sync subscription '{trimmedPeerId}' was not found.",
                CompletedAtUtc = attemptedAtUtc,
                Endpoint = null
            };
        }

        PeerEndpointValidationResult urlValidation = await ValidatePeerEndpointUrlAsync(
            endpoint.BaseUrl,
            endpoint.AllowLoopbackHttp,
            cancellationToken).ConfigureAwait(false);
        if (!urlValidation.IsValid)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                $"SSRF policy blocked peer endpoint: {urlValidation.Error}",
                cancellationToken).ConfigureAwait(false);
        }

        PeerKeyRecord? activePeerKey = await _peerKeyRegistry.FindActiveAsync(endpoint.PinnedKeyId, cancellationToken).ConfigureAwait(false);
        if (activePeerKey is null || activePeerKey.Algorithm != KeyAlgorithm.MlDsa65)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                $"Pinned ML-DSA-65 peer key '{endpoint.PinnedKeyId}' is no longer active in the local trust registry.",
                cancellationToken).ConfigureAwait(false);
        }

        string feedUrl = urlValidation.NormalizedBaseUrl + RevocationFeedPath;
        byte[]? responseBytes;
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            using HttpRequestMessage httpRequest = new(HttpMethod.Get, feedUrl);
            httpRequest.Headers.Accept.ParseAdd("application/json");

            using HttpResponseMessage response = await client.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            int statusCode = (int)response.StatusCode;
            if (statusCode >= 300 && statusCode <= 399)
            {
                return await RecordFailureAsync(
                    endpoint.PeerId,
                    operatorId,
                    attemptedAtUtc,
                    $"Peer endpoint returned HTTP {statusCode} redirect, which is forbidden by SSRF policy.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await RecordFailureAsync(
                    endpoint.PeerId,
                    operatorId,
                    attemptedAtUtc,
                    $"Peer endpoint returned non-success HTTP status {statusCode}.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxFeedResponseBytes)
            {
                return await RecordFailureAsync(
                    endpoint.PeerId,
                    operatorId,
                    attemptedAtUtc,
                    $"Peer revocation feed exceeded maximum allowed payload size of {MaxFeedResponseBytes} bytes.",
                    cancellationToken).ConfigureAwait(false);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            (bool exceeded, byte[] payload) = await ReadBoundedStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            if (exceeded)
            {
                return await RecordFailureAsync(
                    endpoint.PeerId,
                    operatorId,
                    attemptedAtUtc,
                    $"Peer revocation feed stream exceeded maximum allowed payload size of {MaxFeedResponseBytes} bytes.",
                    cancellationToken).ConfigureAwait(false);
            }

            responseBytes = payload;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                "Failed to fetch revocation feed from peer endpoint due to a network transport error.",
                cancellationToken).ConfigureAwait(false);
        }

        SignedLotRevocationFeed? feed;
        try
        {
            feed = JsonSerializer.Deserialize<SignedLotRevocationFeed>(responseBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                "Peer endpoint returned malformed JSON that could not be parsed as a SignedLotRevocationFeed.",
                cancellationToken).ConfigureAwait(false);
        }

        if (feed is null)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                "Peer endpoint returned a null revocation feed payload.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(feed.SignerKeyId, endpoint.PinnedKeyId, StringComparison.OrdinalIgnoreCase))
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                $"Revocation feed signer key '{feed.SignerKeyId}' does not match pinned key '{endpoint.PinnedKeyId}'.",
                cancellationToken).ConfigureAwait(false);
        }

        RevocationFeedVerificationResult verification = _lotService.VerifySignedRevocationFeed(feed, activePeerKey);
        if (!verification.IsTrusted || verification.TrustSource != FabricationCertificateTrustSource.TrustedPeer)
        {
            return await RecordFailureAsync(
                endpoint.PeerId,
                operatorId,
                attemptedAtUtc,
                $"ML-DSA-65 revocation feed verification failed: {verification.Summary}",
                cancellationToken).ConfigureAwait(false);
        }

        int newlyImported = 0;
        DateTimeOffset importedAtUtc = _utcNow();
        for (int i = 0; i < feed.Entries.Count; i++)
        {
            SignedLotRevocationEntry entry = feed.Entries[i];
            FederatedLotRevocationRecord? existing = await _lotRepository.GetFederatedRevocationAsync(entry.LotId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                newlyImported++;
            }

            if (existing is null || !string.Equals(existing.FeedPayloadSha256, feed.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                await _lotRepository.UpsertFederatedRevocationAsync(
                    new FederatedLotRevocationRecord
                    {
                        LotId = entry.LotId,
                        PayloadSha256 = entry.PayloadSha256,
                        IssuerKeyId = feed.SignerKeyId,
                        IssuerPeerLabel = activePeerKey.Label,
                        RevokedBy = entry.RevokedBy,
                        RevocationReason = entry.RevocationReason,
                        RevokedAtUtc = entry.RevokedAtUtc,
                        FeedPayloadSha256 = feed.PayloadSha256,
                        FeedSignatureBase64 = feed.SignatureBase64,
                        ImportedAtUtc = importedAtUtc
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        DateTimeOffset completedAtUtc = _utcNow();
        FederatedPeerSyncEndpointRecord? updated = await _syncRepository.RecordSyncOutcomeAsync(
            endpoint.PeerId,
            completedAtUtc,
            completedAtUtc,
            FederatedPeerSyncStatus.Succeeded,
            null,
            newlyImported,
            cancellationToken).ConfigureAwait(false);

        string summary =
            $"Synchronized {newlyImported} new recall tombstone(s) ({feed.Entries.Count} total feed entries) from pinned peer key '{activePeerKey.KeyId}'.";

        string successAudit = JsonSerializer.Serialize(new
        {
            peerId = endpoint.PeerId,
            pinnedKeyId = activePeerKey.KeyId,
            importedCount = newlyImported,
            feedEntryCount = feed.Entries.Count,
            feedPayloadSha256 = feed.PayloadSha256
        });
        _auditService.LogAction(operatorId, "FABRICATION_PEER_SYNC_SUCCESS", $"peer-sync/{endpoint.PeerId}", successAudit);

        return new FederatedPeerSyncRunResult
        {
            PeerId = endpoint.PeerId,
            EndpointFound = true,
            Succeeded = true,
            ImportedCount = newlyImported,
            FeedEntryCount = feed.Entries.Count,
            Summary = summary,
            CompletedAtUtc = completedAtUtc,
            Endpoint = updated
        };
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncSweepResult> SyncAllEnabledPeersAsync(
        string operatorId,
        bool dueOnly = false,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Iterates over all enabled peer subscriptions and pulls revocation feeds for every eligible peer.
        // WHY:  Drives both manual "Sync All Peers" actions and periodic background scheduled sweeps.
        // HOW:  Optionally filters by elapsed SyncIntervalMinutes, invokes SyncPeerAsync per peer, and aggregates totals.
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        IReadOnlyList<FederatedPeerSyncEndpointRecord> enabledPeers = await _syncRepository.ListEndpointsAsync(
            enabledOnly: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        DateTimeOffset sweepNow = _utcNow();
        List<FederatedPeerSyncRunResult> results = [];
        int succeeded = 0;
        int failed = 0;
        int totalImported = 0;

        for (int i = 0; i < enabledPeers.Count; i++)
        {
            FederatedPeerSyncEndpointRecord peer = enabledPeers[i];
            if (dueOnly && peer.LastAttemptUtc.HasValue)
            {
                DateTimeOffset nextDueUtc = peer.LastAttemptUtc.Value.AddMinutes(peer.SyncIntervalMinutes);
                if (sweepNow < nextDueUtc)
                {
                    continue;
                }
            }

            FederatedPeerSyncRunResult run = await SyncPeerAsync(peer.PeerId, operatorId, cancellationToken).ConfigureAwait(false);
            results.Add(run);
            if (run.Succeeded)
            {
                succeeded++;
                totalImported += run.ImportedCount;
            }
            else
            {
                failed++;
            }
        }

        return new FederatedPeerSyncSweepResult
        {
            PeersAttempted = results.Count,
            PeersSucceeded = succeeded,
            PeersFailed = failed,
            TotalNewRevocationsImported = totalImported,
            CompletedAtUtc = _utcNow(),
            Results = results
        };
    }

    /// <summary>
    /// Persists a failed pull-sync outcome, records a sanitized <c>FABRICATION_PEER_SYNC_FAILURE</c> audit event,
    /// and returns a structured failure <see cref="FederatedPeerSyncRunResult"/>.
    /// </summary>
    /// <param name="peerId">The peer identifier whose sync attempt failed.</param>
    /// <param name="operatorId">The operator or system worker identity initiating the attempt.</param>
    /// <param name="attemptedAtUtc">The UTC timestamp of the failed attempt.</param>
    /// <param name="errorMessage">Sanitized diagnostic reason with zero secrets or host paths.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A failed <see cref="FederatedPeerSyncRunResult"/>.</returns>
    private async Task<FederatedPeerSyncRunResult> RecordFailureAsync(
        string peerId,
        string operatorId,
        DateTimeOffset attemptedAtUtc,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        // WHAT: Updates endpoint failure telemetry and appends a 21 CFR Part 11 failure audit record.
        // WHY:  Gives clinical operators deterministic visibility into failing or tampered peer endpoints.
        // HOW:  Calls RecordSyncOutcomeAsync with null succeededAtUtc and logs FABRICATION_PEER_SYNC_FAILURE.
        FederatedPeerSyncEndpointRecord? updated = await _syncRepository.RecordSyncOutcomeAsync(
            peerId,
            attemptedAtUtc,
            null,
            FederatedPeerSyncStatus.Failed,
            errorMessage,
            0,
            cancellationToken).ConfigureAwait(false);

        string auditDetails = JsonSerializer.Serialize(new
        {
            peerId,
            error = errorMessage
        });
        _auditService.LogAction(operatorId, "FABRICATION_PEER_SYNC_FAILURE", $"peer-sync/{peerId}", auditDetails);

        return new FederatedPeerSyncRunResult
        {
            PeerId = peerId,
            EndpointFound = true,
            Succeeded = false,
            ImportedCount = 0,
            FeedEntryCount = 0,
            Summary = errorMessage,
            CompletedAtUtc = attemptedAtUtc,
            Endpoint = updated
        };
    }

    /// <summary>
    /// Reads up to <see cref="MaxFeedResponseBytes"/> bytes from an HTTP response stream using a rented buffer.
    /// </summary>
    /// <param name="stream">The HTTP response body stream.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A tuple indicating whether the payload exceeded the size cap and the accumulated byte array when within limit.</returns>
    private static async Task<(bool ExceededLimit, byte[] Payload)> ReadBoundedStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        // WHAT: Copies an HTTP response stream into memory while strictly capping total byte count at 512 KiB.
        // WHY:  Guards against chunked-encoding memory exhaustion DoS from malicious or misconfigured peers.
        // HOW:  Rents a 16 KiB buffer from ArrayPool<byte>.Shared, checks running length on every chunk, and returns buffer in finally.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            using MemoryStream ms = new();
            int totalRead = 0;
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, ReadBufferSize), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
                if (totalRead > MaxFeedResponseBytes)
                {
                    return (true, []);
                }

                ms.Write(buffer, 0, bytesRead);
            }

            return (false, ms.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Evaluates whether an IPv4 or IPv6 address belongs to a restricted private, loopback, link-local,
    /// cloud-metadata, CGNAT, ULA, or multicast range forbidden by SSRF policy.
    /// </summary>
    /// <param name="ip">The normalized IP address (with IPv4-mapped IPv6 already unwrapped).</param>
    /// <param name="allowLoopback">Whether loopback addresses are explicitly permitted.</param>
    /// <returns>True when the address is forbidden; false when it is a permitted public (or opted-in loopback) address.</returns>
    private static bool IsRestrictedIpAddress(IPAddress ip, bool allowLoopback)
    {
        // WHAT: Classifies IPv4 and IPv6 addresses against SSRF-restricted address blocks using zero-allocation stack buffers.
        // WHY:  Prevents peer sync requests from reaching internal networks, loopback daemons, or cloud metadata endpoints.
        // HOW:  Checks loopback opt-in first, then inspects IPv4 RFC1918/IMDS/CGNAT/multicast or IPv6 ULA/link-local/multicast bits.
        if (IPAddress.IsLoopback(ip))
        {
            return !allowLoopback;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            ip.TryWriteBytes(bytes, out _);

            byte b0 = bytes[0];
            byte b1 = bytes[1];

            return b0 == 0                                          // 0.0.0.0/8 ("this network")
                || b0 == 10                                         // 10.0.0.0/8 (RFC 1918)
                || (b0 == 172 && b1 >= 16 && b1 <= 31)              // 172.16.0.0/12 (RFC 1918)
                || (b0 == 192 && b1 == 168)                         // 192.168.0.0/16 (RFC 1918)
                || (b0 == 169 && b1 == 254)                         // 169.254.0.0/16 (link-local & cloud IMDS)
                || (b0 == 100 && b1 >= 64 && b1 <= 127)             // 100.64.0.0/10 (RFC 6598 CGNAT)
                || b0 >= 224;                                       // 224.0.0.0/3 (multicast, reserved, broadcast)
        }

        Span<byte> v6 = stackalloc byte[16];
        ip.TryWriteBytes(v6, out _);

        return ip.Equals(IPAddress.IPv6None)
            || ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || ip.IsIPv6Multicast
            || (v6[0] & 0xFE) == 0xFC;                              // fc00::/7 (RFC 4193 ULA, including AWS fd00:ec2::254)
    }

    /// <summary>
    /// Validates that a peer identifier consists strictly of 1 to 64 ASCII letters, digits, '.', '_', or '-'.
    /// </summary>
    /// <param name="peerId">The trimmed candidate peer identifier.</param>
    /// <returns>True when <paramref name="peerId"/> meets length and character constraints; otherwise false.</returns>
    private static bool IsValidPeerId(string peerId)
    {
        // WHAT: Enforces strict ASCII identifier hygiene on peer subscription keys.
        // WHY:  Prevents path traversal or route encoding anomalies when peerId appears in /api/federation/peers/revocation-sync/{peerId}.
        // HOW:  Iterates characters directly with zero regex or heap allocations.
        if (peerId.Length == 0 || peerId.Length > 64)
        {
            return false;
        }

        for (int i = 0; i < peerId.Length; i++)
        {
            char c = peerId[i];
            bool ok = (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '.'
                || c == '_'
                || c == '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Helper creating a rejected <see cref="PeerEndpointValidationResult"/> with a sanitized error message.
    /// </summary>
    /// <param name="error">The sanitized validation failure message.</param>
    /// <returns>An invalid <see cref="PeerEndpointValidationResult"/>.</returns>
    private static PeerEndpointValidationResult InvalidUrl(string error) => new()
    {
        IsValid = false,
        NormalizedBaseUrl = null,
        Error = error
    };
}
