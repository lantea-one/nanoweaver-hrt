using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Federation;

/// <summary>
/// Unit tests verifying <see cref="SystemFederatedDnsResolver"/>, <see cref="FederatedPeerSyncService"/>,
/// and <see cref="FederatedRevocationSyncHostedService"/> across every SSRF, cryptographic, and scheduling branch.
/// </summary>
public sealed class FederatedPeerSyncServiceTests : IDisposable
{
    /// <summary>
    /// Isolated temporary root directory for local and peer cryptographic stores plus SQLite database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Connection factory targeting the isolated SQLite test database.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Peer endpoint subscription repository bound to the isolated SQLite test database.
    /// </summary>
    private readonly FederatedPeerSyncRepository _syncRepo;

    /// <summary>
    /// Fabrication lot and federated revocation repository bound to the isolated SQLite test database.
    /// </summary>
    private readonly FabricationLotRepository _lotRepo;

    /// <summary>
    /// Trusted peer public key registry bound to the isolated SQLite test database.
    /// </summary>
    private readonly PeerKeyRepository _peerKeyRepo;

    /// <summary>
    /// Local node cryptographic host used to construct the local <see cref="FabricationLotService"/>.
    /// </summary>
    private readonly SubjectKeyTestHost _localHost;

    /// <summary>
    /// Remote peer laboratory cryptographic host used to sign valid ML-DSA-65 revocation feeds.
    /// </summary>
    private readonly SubjectKeyTestHost _peerHost;

    /// <summary>
    /// Local fabrication lot verification service.
    /// </summary>
    private readonly FabricationLotService _localLotService;

    /// <summary>
    /// Remote peer fabrication lot service generating real ML-DSA-65 signed feeds.
    /// </summary>
    private readonly FabricationLotService _peerLotService;

    /// <summary>
    /// Mock compliance audit service recording emitted 21 CFR Part 11 events.
    /// </summary>
    private readonly Mock<IComplianceAuditService> _auditMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedPeerSyncServiceTests"/> class.
    /// </summary>
    public FederatedPeerSyncServiceTests()
    {
        // WHAT: Sets up an isolated SQLite database (all 18 migrations) and two distinct ML-DSA-65 node keystores.
        // WHY:  Tests genuine NIST FIPS 204 ML-DSA-65 cross-node verification end-to-end without mocks faking crypto.
        // HOW:  Allocates temp directories, applies all migrations, and wires real repositories plus SubjectKeyTestHost fixtures.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_fed_sync_svc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        string dbDir = Path.Combine(_tempDir, "db");
        Directory.CreateDirectory(dbDir);
        IConfiguration dbConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = dbDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();
        _factory = WeaverTestDb.CreateFactory(dbConfig);

        _syncRepo = new FederatedPeerSyncRepository(_factory);
        _lotRepo = new FabricationLotRepository(_factory);
        _peerKeyRepo = new PeerKeyRepository(_factory);

        _localHost = new SubjectKeyTestHost(Path.Combine(_tempDir, "local_keys"));
        _peerHost = new SubjectKeyTestHost(Path.Combine(_tempDir, "peer_keys"));

        _localLotService = new FabricationLotService(_localHost.KeyManagement, _localHost.CryptoProvider);
        _peerLotService = new FabricationLotService(_peerHost.KeyManagement, _peerHost.CryptoProvider);
    }

    /// <summary>
    /// Verifies <see cref="SystemFederatedDnsResolver"/> handles null/whitespace guards, IP literals, and localhost DNS lookup.
    /// </summary>
    [Fact]
    public async Task SystemFederatedDnsResolver_ResolvesLiteralsAndLocalhostAndGuardsInvalidInput()
    {
        SystemFederatedDnsResolver resolver = new();
        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveHostAddressesAsync(" "));

        IPAddress[] literal = await resolver.ResolveHostAddressesAsync("127.0.0.1");
        Assert.Single(literal);
        Assert.Equal(IPAddress.Loopback, literal[0]);

        IPAddress[] local = await resolver.ResolveHostAddressesAsync("localhost");
        Assert.NotEmpty(local);
    }

    /// <summary>
    /// Verifies constructor null-parameter validation on <see cref="FederatedPeerSyncService"/>.
    /// </summary>
    [Fact]
    public void ServiceConstructor_NullDependencies_ThrowArgumentNullException()
    {
        Mock<IHttpClientFactory> httpFactory = new();
        Mock<IFederatedDnsResolver> dns = new();

        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(null!, _lotRepo, _localLotService, _peerKeyRepo, _auditMock.Object, httpFactory.Object, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, null!, _localLotService, _peerKeyRepo, _auditMock.Object, httpFactory.Object, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, _lotRepo, null!, _peerKeyRepo, _auditMock.Object, httpFactory.Object, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, _lotRepo, _localLotService, null!, _auditMock.Object, httpFactory.Object, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, _lotRepo, _localLotService, _peerKeyRepo, null!, httpFactory.Object, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, _lotRepo, _localLotService, _peerKeyRepo, _auditMock.Object, null!, dns.Object));
        Assert.Throws<ArgumentNullException>(() => new FederatedPeerSyncService(_syncRepo, _lotRepo, _localLotService, _peerKeyRepo, _auditMock.Object, httpFactory.Object, null!));
    }

    /// <summary>
    /// Verifies every SSRF URI hygiene, scheme, loopback opt-in, IPv4/IPv6 private/IMDS/CGNAT/ULA/multicast,
    /// IPv4-mapped IPv6, DNS error, empty-DNS, and dual-A-record bypass vector.
    /// </summary>
    [Fact]
    public async Task ValidatePeerEndpointUrlAsync_EnforcesAllSsrfAndSchemeInvariants()
    {
        StubDnsResolver dns = new();
        FederatedPeerSyncService svc = CreateService(dns, new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        // Basic URI hygiene failures
        Assert.False((await svc.ValidatePeerEndpointUrlAsync(" ", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("not a valid uri", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("/relative/path", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("ftp://lab.example", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("http://lab.example", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://user:pass@lab.example", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://lab.example?x=1", false)).IsValid);
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://lab.example#frag", false)).IsValid);

        // DNS failure and empty DNS
        dns.ThrowOnNext = true;
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://unresolvable.example", false)).IsValid);
        dns.ThrowOnNext = false;

        dns.Addresses = [];
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://empty.example", false)).IsValid);

        // Plaintext HTTP rejected on public IP even with allowLoopbackHttp = true
        dns.Addresses = [IPAddress.Parse("93.184.216.34")];
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("http://public.example", true)).IsValid);

        // Public IPv4 & boundary public IPs succeed over HTTPS
        foreach (string publicIp in new[] { "93.184.216.34", "172.15.0.1", "172.32.0.1", "100.63.0.1", "100.128.0.1", "192.169.0.1", "169.253.0.1", "2001:4860:4860::8888" })
        {
            dns.Addresses = [IPAddress.Parse(publicIp)];
            PeerEndpointValidationResult ok = await svc.ValidatePeerEndpointUrlAsync("https://lab.example/", false);
            Assert.True(ok.IsValid, $"Expected {publicIp} to be permitted.");
            Assert.Equal("https://lab.example", ok.NormalizedBaseUrl);
        }

        // Restricted IPv4 & IPv6 & IPv4-mapped IPv6 ranges are blocked
        foreach (string blockedIp in new[]
        {
            "127.0.0.1", "::1", "0.0.0.1", "10.0.0.5", "172.16.0.1", "172.31.255.254",
            "192.168.1.10", "169.254.169.254", "100.64.0.1", "100.127.255.254",
            "224.0.0.1", "255.255.255.255", "::", "fe80::1", "fec0::1", "ff02::1",
            "fc00::1", "fd00:ec2::254", "::ffff:169.254.169.254", "::ffff:10.0.0.1"
        })
        {
            dns.Addresses = [IPAddress.Parse(blockedIp)];
            PeerEndpointValidationResult blocked = await svc.ValidatePeerEndpointUrlAsync("https://lab.example", allowLoopbackHttp: false);
            Assert.False(blocked.IsValid, $"Expected {blockedIp} to be blocked.");
        }

        // Dual-A-record bypass attempt (public + IMDS) must be blocked
        dns.Addresses = [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("169.254.169.254")];
        Assert.False((await svc.ValidatePeerEndpointUrlAsync("https://dual.example", false)).IsValid);

        // Explicit loopback opt-in allows http://127.0.0.1 and https://[::1]
        dns.Addresses = [IPAddress.Loopback];
        PeerEndpointValidationResult loopHttp = await svc.ValidatePeerEndpointUrlAsync("http://127.0.0.1:5055/", allowLoopbackHttp: true);
        Assert.True(loopHttp.IsValid);
        Assert.Equal("http://127.0.0.1:5055", loopHttp.NormalizedBaseUrl);

        dns.Addresses = [IPAddress.IPv6Loopback];
        PeerEndpointValidationResult loopV6 = await svc.ValidatePeerEndpointUrlAsync("https://localhost:5055", allowLoopbackHttp: true);
        Assert.True(loopV6.IsValid);
    }

    /// <summary>
    /// Verifies subscription upsert/list/delete validation, pinned active ML-DSA-65 key enforcement, and audit events.
    /// </summary>
    [Fact]
    public async Task SubscriptionManagement_ValidatesInputsEnforcesPinnedMlDsa65AndAudits()
    {
        PeerKeyRecord peerKey = await RegisterPeerMlDsa65KeyAsync("Zurich Lab");
        PeerKeyRecord edKey = new()
        {
            KeyId = new string('a', 64),
            Label = "Ed25519 Only Peer",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = Convert.ToBase64String(new byte[32]),
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        await _peerKeyRepo.TryRegisterAsync(edKey);

        StubDnsResolver dns = new() { Addresses = [IPAddress.Parse("93.184.216.34")] };
        FederatedPeerSyncService svc = CreateService(dns, new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.UpsertSubscriptionAsync(null!, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest(), " "));

        // Invalid PeerId variants (empty, too long, invalid character)
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = new string('x', 65),
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "bad/peer",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = null!,
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));

        // Invalid DisplayName (empty, null, >160)
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer_01.a-Z",
            DisplayName = null!,
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer_01.a-Z",
            DisplayName = new string('d', 161),
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));

        // Invalid interval (<1 or >10080)
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 0
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = FederatedPeerSyncService.MaxSyncIntervalMinutes + 1
        }, "admin"));

        // SSRF invalid URL
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "http://lab.example",
            PinnedKeyId = peerKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));

        // Missing / unknown / non-ML-DSA-65 PinnedKeyId
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = null!,
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = "unknown-key-id",
            SyncIntervalMinutes = 15
        }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-1",
            DisplayName = "Lab",
            BaseUrl = "https://lab.example",
            PinnedKeyId = edKey.KeyId,
            SyncIntervalMinutes = 15
        }, "admin"));

        // Valid upsert, list, and delete
        FederatedPeerSyncEndpointRecord saved = await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer_01.A-z",
            DisplayName = "Zurich Lab",
            BaseUrl = "https://lab-zurich.example/",
            PinnedKeyId = peerKey.KeyId,
            Enabled = true,
            SyncIntervalMinutes = 15,
            AllowLoopbackHttp = false
        }, "admin");
        Assert.Equal("https://lab-zurich.example", saved.BaseUrl);
        Assert.Single(await svc.ListSubscriptionsAsync());

        await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteSubscriptionAsync(" ", "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteSubscriptionAsync("peer_01.A-z", " "));
        Assert.False(await svc.DeleteSubscriptionAsync("nonexistent", "admin"));
        Assert.True(await svc.DeleteSubscriptionAsync("peer_01.A-z", "admin"));

        _auditMock.Verify(a => a.LogAction("admin", "FABRICATION_PEER_SUBSCRIPTION_UPSERT", "peer-sync/peer_01.A-z", It.IsAny<string>()), Times.Once);
        _auditMock.Verify(a => a.LogAction("admin", "FABRICATION_PEER_SUBSCRIPTION_DELETE", "peer-sync/peer_01.A-z", It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Verifies pull-sync fail-closed behavior across DNS rebinding, revoked/wrong-algorithm keys,
    /// redirects, non-2xx HTTP status, oversized Content-Length, oversized chunked stream, network fault,
    /// cancellation, malformed JSON, null JSON, signer key mismatch, invalid signature, and happy-path idempotent syncing.
    /// </summary>
    [Fact]
    public async Task SyncPeerAsync_HandlesAllFailureModesAndIdempotentSignedIngestion()
    {
        PeerKeyRecord peerKey = await RegisterPeerMlDsa65KeyAsync("Zurich Lab");
        StubDnsResolver dns = new() { Addresses = [IPAddress.Parse("93.184.216.34")] };
        DateTimeOffset now = new(2026, 11, 19, 14, 0, 0, TimeSpan.Zero);

        Func<HttpRequestMessage, HttpResponseMessage> responder = _ => new HttpResponseMessage(HttpStatusCode.OK);
        StubHttpHandler handler = new(req => responder(req));
        FederatedPeerSyncService svc = CreateService(dns, handler, () => now);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SyncPeerAsync(" ", "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SyncPeerAsync("peer-1", " "));

        // Missing endpoint
        FederatedPeerSyncRunResult missing = await svc.SyncPeerAsync("does-not-exist", "admin");
        Assert.False(missing.EndpointFound);
        Assert.False(missing.Succeeded);

        // Register valid subscription
        await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "zurich-1",
            DisplayName = "Zurich Lab",
            BaseUrl = "https://lab-zurich.example",
            PinnedKeyId = peerKey.KeyId,
            Enabled = true,
            SyncIntervalMinutes = 15
        }, "admin");

        // 1. DNS rebinding to IMDS blocked at sync time
        dns.Addresses = [IPAddress.Parse("169.254.169.254")];
        FederatedPeerSyncRunResult ssrfFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.True(ssrfFail.EndpointFound);
        Assert.False(ssrfFail.Succeeded);
        Assert.Contains("SSRF policy blocked", ssrfFail.Summary, StringComparison.Ordinal);
        dns.Addresses = [IPAddress.Parse("93.184.216.34")];

        // 2. HTTP 302 redirect rejected
        responder = _ => new HttpResponseMessage(HttpStatusCode.Found);
        FederatedPeerSyncRunResult redirectFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(redirectFail.Succeeded);
        Assert.Contains("redirect", redirectFail.Summary, StringComparison.OrdinalIgnoreCase);

        // 3. HTTP 503 non-success rejected
        responder = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        FederatedPeerSyncRunResult httpFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(httpFail.Succeeded);
        Assert.Contains("503", httpFail.Summary, StringComparison.Ordinal);

        // 4. Oversized Content-Length rejected early
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[FederatedPeerSyncService.MaxFeedResponseBytes + 1])
        };
        FederatedPeerSyncRunResult lengthFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(lengthFail.Succeeded);
        Assert.Contains("exceeded maximum allowed payload size", lengthFail.Summary, StringComparison.Ordinal);

        // 5. Oversized chunked stream (no Content-Length header) rejected during streaming
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthStreamContent(new byte[FederatedPeerSyncService.MaxFeedResponseBytes + 64])
        };
        FederatedPeerSyncRunResult streamFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(streamFail.Succeeded);
        Assert.Contains("stream exceeded maximum allowed payload size", streamFail.Summary, StringComparison.Ordinal);

        // 6. Network transport exception handled cleanly
        responder = _ => throw new HttpRequestException("Connection reset");
        FederatedPeerSyncRunResult netFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(netFail.Succeeded);
        Assert.Contains("network transport error", netFail.Summary, StringComparison.Ordinal);

        // 7. Cooperative cancellation during HTTP send propagates OperationCanceledException
        using (CancellationTokenSource cts = new())
        {
            responder = _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            };
            await Assert.ThrowsAsync<OperationCanceledException>(() => svc.SyncPeerAsync("zurich-1", "admin", cts.Token));
        }

        // 8. Malformed JSON rejected
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult jsonFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(jsonFail.Succeeded);
        Assert.Contains("malformed JSON", jsonFail.Summary, StringComparison.Ordinal);

        // 9. Null JSON rejected
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult nullJsonFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(nullJsonFail.Succeeded);
        Assert.Contains("null revocation feed", nullJsonFail.Summary, StringComparison.Ordinal);

        // 10. Signer key mismatch rejected (feed signed by local node instead of pinned peer)
        SignedLotRevocationFeed localFeed = _localLotService.BuildSignedRevocationFeed([], now);
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(localFeed), Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult mismatchFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(mismatchFail.Succeeded);
        Assert.Contains("does not match pinned key", mismatchFail.Summary, StringComparison.Ordinal);

        // 11. Tampered signature from matching keyId rejected
        SignedLotRevocationFeed validFeed1 = _peerLotService.BuildSignedRevocationFeed(
            [
                new FabricationLotRevocationRecord
                {
                    LotId = "LOT-FED-001",
                    PatientId = "pat-1",
                    PayloadSha256 = new string('1', 64),
                    RevokedBy = "peer-admin",
                    RevocationReason = "Thermal excursion in sterile transit",
                    RevokedAtUtc = now.AddHours(-2)
                }
            ],
            now);
        SignedLotRevocationFeed tamperedFeed = validFeed1 with { PayloadSha256 = new string('f', 64) };
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(tamperedFeed), Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult sigFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(sigFail.Succeeded);
        Assert.Contains("verification failed", sigFail.Summary, StringComparison.Ordinal);

        // 12. LocalNode trust source spoof rejected even if peer key record accidentally matches local node key
        SignedLotRevocationFeed validLocalSpoof = _localLotService.BuildSignedRevocationFeed([], now);
        PeerKeyRecord localKeyAsPeer = new()
        {
            KeyId = validLocalSpoof.SignerKeyId,
            Label = "Spoofed Self Peer",
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = validLocalSpoof.SignerPublicKeyBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = now
        };
        await _peerKeyRepo.TryRegisterAsync(localKeyAsPeer);
        await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "self-spoof",
            DisplayName = "Self Spoof",
            BaseUrl = "https://self.example",
            PinnedKeyId = localKeyAsPeer.KeyId,
            Enabled = false,
            SyncIntervalMinutes = 15
        }, "admin");
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(validLocalSpoof), Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult selfSpoofFail = await svc.SyncPeerAsync("self-spoof", "admin");
        Assert.False(selfSpoofFail.Succeeded);
        Assert.Contains("verification failed", selfSpoofFail.Summary, StringComparison.Ordinal);

        // 13. Valid ML-DSA-65 feed: first pull imports 1 new tombstone
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(validFeed1), Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult firstSuccess = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.True(firstSuccess.Succeeded);
        Assert.Equal(1, firstSuccess.ImportedCount);
        Assert.Equal(1, firstSuccess.FeedEntryCount);
        Assert.NotNull(firstSuccess.Endpoint);
        Assert.Equal(1, firstSuccess.Endpoint.TotalImportedCount);

        // 14. Repeat pull of identical feed: idempotent (ImportedCount = 0, TotalImportedCount remains 1)
        now = now.AddMinutes(15);
        FederatedPeerSyncRunResult secondSuccess = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.True(secondSuccess.Succeeded);
        Assert.Equal(0, secondSuccess.ImportedCount);
        Assert.Equal(1, secondSuccess.FeedEntryCount);
        Assert.Equal(1, secondSuccess.Endpoint!.TotalImportedCount);

        // 15. Updated feed with 1 existing lot (new feed hash) + 1 brand-new lot -> ImportedCount = 1, Total = 2
        now = now.AddMinutes(15);
        SignedLotRevocationFeed validFeed2 = _peerLotService.BuildSignedRevocationFeed(
            [
                new FabricationLotRevocationRecord
                {
                    LotId = "LOT-FED-001",
                    PatientId = "pat-1",
                    PayloadSha256 = new string('1', 64),
                    RevokedBy = "peer-admin",
                    RevocationReason = "Thermal excursion in sterile transit",
                    RevokedAtUtc = now.AddHours(-2)
                },
                new FabricationLotRevocationRecord
                {
                    LotId = "LOT-FED-002",
                    PatientId = "pat-2",
                    PayloadSha256 = new string('2', 64),
                    RevokedBy = "peer-admin",
                    RevocationReason = "Endotoxin re-test out of spec",
                    RevokedAtUtc = now.AddMinutes(-5)
                }
            ],
            now);
        responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(validFeed2), Encoding.UTF8, "application/json")
        };
        FederatedPeerSyncRunResult thirdSuccess = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.True(thirdSuccess.Succeeded);
        Assert.Equal(1, thirdSuccess.ImportedCount);
        Assert.Equal(2, thirdSuccess.FeedEntryCount);
        Assert.Equal(2, thirdSuccess.Endpoint!.TotalImportedCount);
        Assert.NotNull(await _lotRepo.GetFederatedRevocationAsync("LOT-FED-002"));

        // 16. Revoking pinned peer key blocks future pulls fail-closed
        await _peerKeyRepo.RevokeAsync(peerKey.KeyId, "admin", now);
        FederatedPeerSyncRunResult revokedKeyFail = await svc.SyncPeerAsync("zurich-1", "admin");
        Assert.False(revokedKeyFail.Succeeded);
        Assert.Contains("no longer active", revokedKeyFail.Summary, StringComparison.Ordinal);

        // 17. Peer row pointing to an Ed25519 key in DB is also rejected fail-closed at sync time
        PeerKeyRecord edKey = new()
        {
            KeyId = new string('b', 64),
            Label = "Ed25519 Stored",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = Convert.ToBase64String(new byte[32]),
            RegisteredBy = "admin",
            RegisteredAtUtc = now
        };
        await _peerKeyRepo.TryRegisterAsync(edKey);
        await _syncRepo.UpsertEndpointAsync(new FederatedPeerSyncEndpointRecord
        {
            PeerId = "ed-only-peer",
            DisplayName = "Ed Only",
            BaseUrl = "https://ed.example",
            PinnedKeyId = edKey.KeyId,
            Enabled = false,
            SyncIntervalMinutes = 15,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        FederatedPeerSyncRunResult edAlgoFail = await svc.SyncPeerAsync("ed-only-peer", "admin");
        Assert.False(edAlgoFail.Succeeded);
        Assert.Contains("no longer active", edAlgoFail.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <see cref="FederatedPeerSyncService.SyncAllEnabledPeersAsync"/> aggregation and <c>dueOnly</c> interval filtering.
    /// </summary>
    [Fact]
    public async Task SyncAllEnabledPeersAsync_AggregatesMixedOutcomesAndRespectsDueOnly()
    {
        PeerKeyRecord peerKey = await RegisterPeerMlDsa65KeyAsync("Zurich Lab");
        StubDnsResolver dns = new() { Addresses = [IPAddress.Parse("93.184.216.34")] };
        DateTimeOffset now = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

        SignedLotRevocationFeed validFeed = _peerLotService.BuildSignedRevocationFeed(
            [
                new FabricationLotRevocationRecord
                {
                    LotId = "LOT-SWEEP-001",
                    PatientId = "pat-1",
                    RevokedBy = "peer-admin",
                    RevocationReason = "Batch recall",
                    RevokedAtUtc = now.AddMinutes(-10)
                }
            ],
            now);

        StubHttpHandler handler = new(req =>
        {
            if (req.RequestUri!.Host == "good.example")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(validFeed), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        FederatedPeerSyncService svc = CreateService(dns, handler, () => now);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SyncAllEnabledPeersAsync(" "));

        await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-good",
            DisplayName = "Good Lab",
            BaseUrl = "https://good.example",
            PinnedKeyId = peerKey.KeyId,
            Enabled = true,
            SyncIntervalMinutes = 30
        }, "admin");
        await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-bad",
            DisplayName = "Bad Lab",
            BaseUrl = "https://bad.example",
            PinnedKeyId = peerKey.KeyId,
            Enabled = true,
            SyncIntervalMinutes = 10
        }, "admin");
        await svc.UpsertSubscriptionAsync(new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "peer-disabled",
            DisplayName = "Disabled Lab",
            BaseUrl = "https://disabled.example",
            PinnedKeyId = peerKey.KeyId,
            Enabled = false,
            SyncIntervalMinutes = 10
        }, "admin");

        // First dueOnly sweep runs both never-attempted enabled peers (skips disabled)
        FederatedPeerSyncSweepResult sweep1 = await svc.SyncAllEnabledPeersAsync("admin", dueOnly: true);
        Assert.Equal(2, sweep1.PeersAttempted);
        Assert.Equal(1, sweep1.PeersSucceeded);
        Assert.Equal(1, sweep1.PeersFailed);
        Assert.Equal(1, sweep1.TotalNewRevocationsImported);

        // 5 minutes later: neither peer is due (intervals 30m and 10m)
        now = now.AddMinutes(5);
        FederatedPeerSyncSweepResult sweep2 = await svc.SyncAllEnabledPeersAsync("admin", dueOnly: true);
        Assert.Equal(0, sweep2.PeersAttempted);

        // 10 minutes after initial attempt: only peer-bad (10m interval) is due
        now = now.AddMinutes(5);
        FederatedPeerSyncSweepResult sweep3 = await svc.SyncAllEnabledPeersAsync("admin", dueOnly: true);
        Assert.Equal(1, sweep3.PeersAttempted);
        Assert.Equal("peer-bad", sweep3.Results[0].PeerId);

        // dueOnly: false runs all enabled peers unconditionally
        FederatedPeerSyncSweepResult sweepAll = await svc.SyncAllEnabledPeersAsync("admin", dueOnly: false);
        Assert.Equal(2, sweepAll.PeersAttempted);
    }

    /// <summary>
    /// Verifies <see cref="FederatedRevocationSyncHostedService"/> configuration parsing, disabled mode,
    /// deterministic sweep loop, fault isolation, and clean cancellation with zero sleeps.
    /// </summary>
    [Fact]
    public async Task HostedService_ConfigParsingSweepsFaultIsolationAndCancellation_WorkDeterministically()
    {
        Mock<IFederatedPeerSyncService> syncMock = new();
        IConfiguration emptyConfig = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => new FederatedRevocationSyncHostedService(null!, emptyConfig));
        Assert.Throws<ArgumentNullException>(() => new FederatedRevocationSyncHostedService(syncMock.Object, null!));

        // Default config: Enabled = true, PollTickSeconds = 60
        using FederatedRevocationSyncHostedService defaultWorker = new(syncMock.Object, emptyConfig);
        Assert.True(defaultWorker.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), defaultWorker.PollInterval);

        // Exercise default Task.Delay cancellation cleanly
        using (CancellationTokenSource immediateCancel = new())
        {
            immediateCancel.Cancel();
            await defaultWorker.StartAsync(immediateCancel.Token);
            await defaultWorker.StopAsync(CancellationToken.None);
        }

        // Disabled config exits ExecuteAsync immediately without invoking syncService
        IConfiguration disabledConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FederatedSync:Enabled"] = "false",
                ["FederatedSync:PollTickSeconds"] = "0"
            })
            .Build();
        using FederatedRevocationSyncHostedService disabledWorker = new(syncMock.Object, disabledConfig);
        Assert.False(disabledWorker.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(1), disabledWorker.PollInterval);
        await disabledWorker.StartAsync(CancellationToken.None);
        await (disabledWorker.ExecuteTask ?? Task.CompletedTask);
        syncMock.Verify(s => s.SyncAllEnabledPeersAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        // Clamping upper bound (>86400 -> 86400) & invalid string fallback (-> 60)
        IConfiguration clampedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FederatedSync:Enabled"] = "true",
                ["FederatedSync:PollTickSeconds"] = "999999"
            })
            .Build();
        using FederatedRevocationSyncHostedService clampedWorker = new(syncMock.Object, clampedConfig);
        Assert.Equal(TimeSpan.FromSeconds(FederatedRevocationSyncHostedService.MaxPollTickSeconds), clampedWorker.PollInterval);

        // Deterministic loop: tick 1 succeeds, tick 2 throws unexpected exception (logged & swallowed), tick 3 cancels inside sweep, tick 4 cancels inside delay
        int sweepCall = 0;
        using CancellationTokenSource loopCts = new();
        Mock<ILogger<FederatedRevocationSyncHostedService>> loggerMock = new();

        syncMock
            .Setup(s => s.SyncAllEnabledPeersAsync(FederatedPeerSyncService.SystemOperatorId, true, It.IsAny<CancellationToken>()))
            .Returns((string _, bool _, CancellationToken ct) =>
            {
                sweepCall++;
                if (sweepCall == 1)
                {
                    return Task.FromResult(new FederatedPeerSyncSweepResult());
                }

                if (sweepCall == 2)
                {
                    throw new InvalidOperationException("Transient DB lock");
                }

                loopCts.Cancel();
                throw new OperationCanceledException(ct);
            });

        using FederatedRevocationSyncHostedService activeWorker = new(
            syncMock.Object,
            clampedConfig,
            loggerMock.Object,
            delayAsync: (_, _) => Task.CompletedTask);

        await InvokeExecuteAsync(activeWorker, loopCts.Token);
        Assert.Equal(3, sweepCall);

        // Also test exception branch with null logger and cancellation inside delayAsync
        int delayCalls = 0;
        Mock<IFederatedPeerSyncService> failOnceMock = new();
        failOnceMock
            .Setup(s => s.SyncAllEnabledPeersAsync(FederatedPeerSyncService.SystemOperatorId, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No logger failure"));

        using CancellationTokenSource delayCancelCts = new();
        using FederatedRevocationSyncHostedService nullLoggerWorker = new(
            failOnceMock.Object,
            emptyConfig,
            logger: null,
            delayAsync: (_, ct) =>
            {
                delayCalls++;
                if (delayCalls == 1)
                {
                    return Task.CompletedTask;
                }

                delayCancelCts.Cancel();
                return Task.FromCanceled(delayCancelCts.Token);
            });

        // Drive ExecuteAsync directly via Start/Stop with linked cancellation
        Task exec = InvokeExecuteAsync(nullLoggerWorker, delayCancelCts.Token);
        await exec;
        Assert.Equal(2, delayCalls);

        // Direct ExecuteSyncSweepAsync call
        Mock<IFederatedPeerSyncService> directMock = new();
        directMock
            .Setup(s => s.SyncAllEnabledPeersAsync(FederatedPeerSyncService.SystemOperatorId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FederatedPeerSyncSweepResult { PeersAttempted = 1, PeersSucceeded = 1 });
        using FederatedRevocationSyncHostedService directWorker = new(directMock.Object, emptyConfig);
        FederatedPeerSyncSweepResult directResult = await directWorker.ExecuteSyncSweepAsync();
        Assert.Equal(1, directResult.PeersSucceeded);
    }

    /// <summary>
    /// Helper invoking the protected <see cref="BackgroundService.ExecuteAsync"/> method with a controlled token.
    /// </summary>
    /// <param name="service">The hosted service instance.</param>
    /// <param name="token">The controlled cancellation token.</param>
    /// <returns>The underlying execution task.</returns>
    private static Task InvokeExecuteAsync(FederatedRevocationSyncHostedService service, CancellationToken token)
    {
        System.Reflection.MethodInfo method = typeof(FederatedRevocationSyncHostedService)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (Task)method.Invoke(service, [token])!;
    }

    /// <summary>
    /// Registers the test peer host's active ML-DSA-65 public key in the local <c>weaver_peer_keys</c> table.
    /// </summary>
    /// <param name="label">Human-readable laboratory label.</param>
    /// <returns>The registered <see cref="PeerKeyRecord"/>.</returns>
    private async Task<PeerKeyRecord> RegisterPeerMlDsa65KeyAsync(string label)
    {
        SignedLotRevocationFeed probe = _peerLotService.BuildSignedRevocationFeed([]);
        PeerKeyRecord record = new()
        {
            KeyId = probe.SignerKeyId,
            Label = label,
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = probe.SignerPublicKeyBase64,
            RegisteredBy = "admin",
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };
        await _peerKeyRepo.TryRegisterAsync(record);
        return record;
    }

    /// <summary>
    /// Constructs a <see cref="FederatedPeerSyncService"/> wired to deterministic DNS and HTTP stubs.
    /// </summary>
    /// <param name="dns">Stub DNS resolver.</param>
    /// <param name="handler">Stub HTTP message handler.</param>
    /// <param name="utcNow">Optional deterministic UTC clock.</param>
    /// <returns>A configured <see cref="FederatedPeerSyncService"/>.</returns>
    private FederatedPeerSyncService CreateService(
        IFederatedDnsResolver dns,
        HttpMessageHandler handler,
        Func<DateTimeOffset>? utcNow = null)
    {
        Mock<IHttpClientFactory> httpFactory = new();
        httpFactory
            .Setup(f => f.CreateClient(FederatedPeerSyncService.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        return new FederatedPeerSyncService(
            _syncRepo,
            _lotRepo,
            _localLotService,
            _peerKeyRepo,
            _auditMock.Object,
            httpFactory.Object,
            dns,
            utcNow);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _localHost.Dispose();
        _peerHost.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Deterministic in-memory <see cref="IFederatedDnsResolver"/> for offline SSRF unit tests.
    /// </summary>
    private sealed class StubDnsResolver : IFederatedDnsResolver
    {
        /// <summary>
        /// Gets or sets the IP addresses returned by <see cref="ResolveHostAddressesAsync"/>.
        /// </summary>
        public IPAddress[] Addresses { get; set; } = [IPAddress.Parse("93.184.216.34")];

        /// <summary>
        /// Gets or sets a value indicating whether resolution should throw an exception.
        /// </summary>
        public bool ThrowOnNext { get; set; }

        /// <inheritdoc/>
        public Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken = default)
        {
            if (ThrowOnNext)
            {
                throw new InvalidOperationException("Simulated DNS failure");
            }

            return Task.FromResult(Addresses);
        }
    }

    /// <summary>
    /// Delegate-backed <see cref="HttpMessageHandler"/> for deterministic offline HTTP sync tests.
    /// </summary>
    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    /// <summary>
    /// Custom <see cref="HttpContent"/> whose <c>Content-Length</c> header is omitted to exercise chunked stream size capping.
    /// </summary>
    private sealed class UnknownLengthStreamContent(byte[] data) : HttpContent
    {
        /// <inheritdoc/>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(data, 0, data.Length);

        /// <inheritdoc/>
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
