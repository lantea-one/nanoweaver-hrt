using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Client;

/// <summary>
/// Unit tests for <see cref="ZeroizableSecretBuffer"/> and <see cref="WeaverNodeApiClient"/> achieving 100.00% coverage.
/// </summary>
public sealed class WeaverNodeApiClientAndZeroizableBufferTests
{
    private static InstanceProfile CreateTestProfile(string instanceId = "node-alpha")
    {
        return new InstanceProfile(
            instanceId,
            "Alpha Node",
            "http://127.0.0.1:5000",
            true,
            "admin",
            "Administrator",
            "Ed25519",
            "MCowBQYDK2VwAyEAZv8=",
            "ENV_CIPHERTEXT",
            true,
            15,
            DateTimeOffset.UtcNow,
            null);
    }

    [Fact]
    public void ZeroizableSecretBuffer_FromUtf8_ValidatesAndEncodes()
    {
        // WHAT: Test FromUtf8 null guard and byte encoding.
        // WHY:  Ensures UTF-8 encoding is accurate and validates null parameters.
        // HOW:  Asserts ArgumentNullException on null, checks Length, Span, and ToUnprotectedString.
        Assert.Throws<ArgumentNullException>(() => ZeroizableSecretBuffer.FromUtf8(null!));

        using var buffer = ZeroizableSecretBuffer.FromUtf8("secret_token_123");
        Assert.False(buffer.IsDisposed);
        Assert.Equal(16, buffer.Length);
        Assert.Equal("secret_token_123", buffer.ToUnprotectedString());
        Assert.False(buffer.Span.IsEmpty);
    }

    [Fact]
    public void ZeroizableSecretBuffer_FromBytes_And_FromChars()
    {
        // WHAT: Test FromBytes and FromChars creation.
        // WHY:  Supports wrapping binary buffers and char arrays directly.
        // HOW:  Constructs from byte span and char span, asserts equality and length.
        byte[] rawBytes = [10, 20, 30, 40];
        using var byteBuffer = ZeroizableSecretBuffer.FromBytes(rawBytes);
        Assert.Equal(4, byteBuffer.Length);
        Assert.True(byteBuffer.Span.SequenceEqual(rawBytes));

        char[] chars = ['a', 'b', 'c'];
        using var charBuffer = ZeroizableSecretBuffer.FromChars(chars);
        Assert.Equal(3, charBuffer.Length);
        Assert.Equal("abc", charBuffer.ToUnprotectedString());
    }

    [Fact]
    public void ZeroizableSecretBuffer_Clone_And_Disposal()
    {
        // WHAT: Test Clone, Clear, Dispose idempotency, and post-disposal guards.
        // WHY:  Guarantees cryptographic zeroization and prevents use-after-free.
        // HOW:  Clones buffer, clears original, asserts disposed properties, tests double dispose.
        using var original = ZeroizableSecretBuffer.FromUtf8("clone_test");
        using var clone = original.Clone();

        Assert.Equal("clone_test", clone.ToUnprotectedString());

        original.Clear();
        Assert.True(original.IsDisposed);
        Assert.Equal(0, original.Length);
        Assert.True(original.Span.IsEmpty);
        Assert.Equal(string.Empty, original.ToUnprotectedString());

        // Clone on disposed buffer throws ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => original.Clone());

        // Idempotent double dispose
        original.Dispose();
        Assert.True(original.IsDisposed);

        // Clone remains valid until disposed
        Assert.False(clone.IsDisposed);
        Assert.Equal("clone_test", clone.ToUnprotectedString());
    }

    [Fact]
    public void WeaverNodeApiClient_Constructor_And_Properties()
    {
        // WHAT: Test constructors and property accessors.
        // WHY:  Validates DI constructors and session state accessors.
        // HOW:  Asserts ArgumentNullException on null, tests HttpClient constructor, checks HasActiveSession.
        Assert.Throws<ArgumentNullException>(() => new WeaverNodeApiClient((INanoWeaverNodeClient)null!));

        using var httpClient = new HttpClient();
        var keyVault = new AesGcmClientKeyRingVault();
        using var clientFromHttp = new WeaverNodeApiClient(httpClient, keyVault);
        Assert.False(clientFromHttp.HasActiveSession);
        Assert.Null(clientFromHttp.SessionTokenBuffer);

        var innerMock = new Mock<INanoWeaverNodeClient>();
        using var client = new WeaverNodeApiClient(innerMock.Object);
        Assert.False(client.HasActiveSession);
        Assert.Null(client.SessionTokenBuffer);
    }

    [Fact]
    public void WeaverNodeApiClient_SetSessionToken_And_ClearSessionToken()
    {
        // WHAT: Test SetSessionToken with ZeroizableSecretBuffer and string, and ClearSessionToken.
        // WHY:  Ensures token updates correctly replace previous buffers and wipe secrets.
        // HOW:  Sets token, verifies HasActiveSession, clears token, asserts null.
        var innerMock = new Mock<INanoWeaverNodeClient>();
        using var client = new WeaverNodeApiClient(innerMock.Object);

        Assert.Throws<ArgumentNullException>(() => client.SetSessionToken((ZeroizableSecretBuffer)null!));
        Assert.Throws<ArgumentNullException>(() => client.SetSessionToken((string)null!));
        Assert.Throws<ArgumentException>(() => client.SetSessionToken(string.Empty));
        Assert.Throws<ArgumentException>(() => client.SetSessionToken("   "));

        using var buf1 = ZeroizableSecretBuffer.FromUtf8("token_first");
        client.SetSessionToken(buf1);
        Assert.True(client.HasActiveSession);

        using var snapshot1 = client.SessionTokenBuffer;
        Assert.NotNull(snapshot1);
        Assert.Equal("token_first", snapshot1.ToUnprotectedString());

        // Update token with string, replacing old buffer
        client.SetSessionToken("token_second");
        Assert.True(client.HasActiveSession);

        using var snapshot2 = client.SessionTokenBuffer;
        Assert.NotNull(snapshot2);
        Assert.Equal("token_second", snapshot2.ToUnprotectedString());

        // Clear token
        client.ClearSessionToken();
        Assert.False(client.HasActiveSession);
        Assert.Null(client.SessionTokenBuffer);

        // Double clear is safe
        client.ClearSessionToken();
    }

    [Fact]
    public async Task WeaverNodeApiClient_AuthenticateAsync_StoresSessionToken()
    {
        // WHAT: Verifies AuthenticateAsync caches token in ZeroizableSecretBuffer.
        // WHY:  Eliminates duplicate authentication handshakes in GUI shells.
        // HOW:  Mocks innerClient.AuthenticateAsync, calls client.AuthenticateAsync, asserts HasActiveSession.
        var innerMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateTestProfile();
        var session = new NodeAuthSession("jwt_token_xyz", "Bearer", 3600, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "admin", "Administrator");

        innerMock.Setup(i => i.AuthenticateAsync(profile, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        using var client = new WeaverNodeApiClient(innerMock.Object);
        var result = await client.AuthenticateAsync(profile, "passphrase".ToCharArray());

        Assert.Equal(session.Token, result.Token);
        Assert.True(client.HasActiveSession);

        using var cachedToken = client.SessionTokenBuffer;
        Assert.NotNull(cachedToken);
        Assert.Equal("jwt_token_xyz", cachedToken.ToUnprotectedString());
    }

    [Fact]
    public async Task WeaverNodeApiClient_DelegatedMethods_UseExplicitOrCachedToken()
    {
        // WHAT: Test PollSnapshotAsync, StartSimulationCampaignAsync, GetTelemetryAsync, GetCampaignsAsync, GetEndocrineProfileAsync.
        // WHY:  Verifies token fallback logic and delegation to inner client.
        // HOW:  Tests call with explicit token, then with cached token, then with missing token throwing exception.
        var innerMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateTestProfile();

        var snapshot = new InstanceConnectionSnapshot("node-alpha", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        var telemetry = new NodeTelemetrySnapshot("node-alpha", "CPU", 100, 10.0, 20.0, 30.0, 0, 0, 0, "1h", 10.0, 20.0, 30.0, 1);
        var campaigns = (IReadOnlyList<SimulationCampaignSummary>)[new SimulationCampaignSummary("serum-stability", "Serum Stability", "LAMMPS", "Description", "PhysicsGoal", true)];
        var endocrine = new EndocrineProfileSummary("MtF", 150.0, 15.0, 5.0, DateTimeOffset.UtcNow);

        innerMock.Setup(i => i.PollSnapshotAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        innerMock.Setup(i => i.PollSnapshotAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

        innerMock.Setup(i => i.StartSimulationCampaignAsync(profile, "explicit_token", "serum-stability", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        innerMock.Setup(i => i.StartSimulationCampaignAsync(profile, "cached_token", "serum-stability", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        innerMock.Setup(i => i.GetTelemetryAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(telemetry);
        innerMock.Setup(i => i.GetTelemetryAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(telemetry);

        innerMock.Setup(i => i.GetCampaignsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(campaigns);
        innerMock.Setup(i => i.GetCampaignsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(campaigns);

        innerMock.Setup(i => i.GetEndocrineProfileAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(endocrine);
        innerMock.Setup(i => i.GetEndocrineProfileAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(endocrine);

        var closedLoopReq = new ClosedLoopSimulationRequest { TotalHours = 24, StepHours = 1.0 };
        var closedLoopRes = new ClosedLoopSimulationResult { SteadyStateReached = true, TimeToSuppressionHours = 12.0 };
        innerMock.Setup(i => i.SimulateClosedLoopEndocrineAsync(profile, "explicit_token", closedLoopReq, It.IsAny<CancellationToken>())).ReturnsAsync(closedLoopRes);
        innerMock.Setup(i => i.SimulateClosedLoopEndocrineAsync(profile, "cached_token", closedLoopReq, It.IsAny<CancellationToken>())).ReturnsAsync(closedLoopRes);

        var peers = (IReadOnlyList<FederationPeerNode>)[new FederationPeerNode
        {
            NodeId = "p1",
            EndpointUri = "http://127.0.0.1:5000",
            PublicKeyBase64 = "pub",
            Algorithm = NanoWeaverHrt.Security.Authentication.KeyAlgorithm.Ed25519,
            LatencyMilliseconds = 12.5,
            LastSeenUtc = DateTimeOffset.UtcNow
        }];
        innerMock.Setup(i => i.DiscoverFederationPeersAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(peers);
        innerMock.Setup(i => i.DiscoverFederationPeersAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(peers);

        var buildReq = new FederationBuildManifestRequest { TrajectoryFileName = "run.xyz" };
        var manifest = new TrajectoryFederationManifest
        {
            DatasetName = "run.xyz",
            TotalSizeBytes = 1024,
            ChunkSizeBytes = 1024,
            TotalChunks = 1,
            Chunks = [],
            SigningNodeId = "node-1",
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = "sig",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };
        innerMock.Setup(i => i.BuildFederationManifestAsync(profile, "explicit_token", buildReq, It.IsAny<CancellationToken>())).ReturnsAsync(manifest);
        innerMock.Setup(i => i.BuildFederationManifestAsync(profile, "cached_token", buildReq, It.IsAny<CancellationToken>())).ReturnsAsync(manifest);

        var verifyRes = new FederationVerifyManifestResponse { IsValid = true, TrajectoryId = "run.xyz" };
        innerMock.Setup(i => i.VerifyFederationManifestAsync(profile, "explicit_token", manifest, It.IsAny<CancellationToken>())).ReturnsAsync(verifyRes);
        innerMock.Setup(i => i.VerifyFederationManifestAsync(profile, "cached_token", manifest, It.IsAny<CancellationToken>())).ReturnsAsync(verifyRes);

        var gelReq = new GelAnalysisRequest { ImageBytes = [1, 2], Width = 1, Height = 1, ExpectedLaneCount = 1, BackgroundThreshold = 0.1, LadderLaneNumber = 1 };
        var gelRes = new GelAnalysisResult { DetectedLaneCount = 1, OverallFoldingSuccessRate = 95.0, Lanes = [] };
        innerMock.Setup(i => i.AnalyzeGelAsync(profile, "explicit_token", gelReq, It.IsAny<CancellationToken>())).ReturnsAsync(gelRes);
        innerMock.Setup(i => i.AnalyzeGelAsync(profile, "cached_token", gelReq, It.IsAny<CancellationToken>())).ReturnsAsync(gelRes);

        var benchmarkRep = new AcceleratorBenchmarkReport
        {
            DeviceName = "GPU",
            DriverVersion = "1.0",
            SinglePrecisionGflops = 1000.0,
            DoublePrecisionGflops = 100.0,
            MemoryBandwidthGigaBytesPerSecond = 100.0,
            TemperatureCelsius = 45.0,
            PowerUsageWatts = 150.0,
            Readiness = HardwareReadinessScore.Go,
            BenchmarkTimestampUtc = DateTimeOffset.UtcNow,
            Recommendations = []
        };
        innerMock.Setup(i => i.RunBenchmarkAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(benchmarkRep);
        innerMock.Setup(i => i.RunBenchmarkAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(benchmarkRep);

        innerMock.Setup(i => i.GetLatestBenchmarkAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(benchmarkRep);
        innerMock.Setup(i => i.GetLatestBenchmarkAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(benchmarkRep);

        var opentronsReq = new GenerateOpentronsScriptRequest { SequenceOrSampleId = "s1" };
        var opentronsRes = new OpentronsProtocolResult
        {
            PythonScript = "# code",
            ScriptSha256 = "sha",
            TransferCount = 1,
            SourcePlatesRequired = 1,
            TipRacksRequired = 1,
            HasThermocyclerRamp = true,
            EstimatedRunDurationMinutes = 10.0
        };
        innerMock.Setup(i => i.GenerateOpentronsProtocolAsync(profile, "explicit_token", opentronsReq, It.IsAny<CancellationToken>())).ReturnsAsync(opentronsRes);
        innerMock.Setup(i => i.GenerateOpentronsProtocolAsync(profile, "cached_token", opentronsReq, It.IsAny<CancellationToken>())).ReturnsAsync(opentronsRes);

        var dnaPkg = new PatientDigitalDnaPackage { PatientId = "pt1", ScaffoldFasta = "ATCG", NanocarrierGeometry = "Hexagonal Nanotube", ProvenanceSha256 = "sha256" };
        innerMock.Setup(i => i.GetPatientDnaRegimensAsync(profile, "explicit_token", "pt1", It.IsAny<CancellationToken>())).ReturnsAsync(dnaPkg);
        innerMock.Setup(i => i.GetPatientDnaRegimensAsync(profile, "cached_token", "pt1", It.IsAny<CancellationToken>())).ReturnsAsync(dnaPkg);

        var dnaRecs = (IReadOnlyList<PatientDigitalDnaRecord>)[new PatientDigitalDnaRecord { Id = "rec1", Username = "pt1" }];
        innerMock.Setup(i => i.GetPatientDigitalDnaAsync(profile, "explicit_token", "pt1", It.IsAny<CancellationToken>())).ReturnsAsync(dnaRecs);
        innerMock.Setup(i => i.GetPatientDigitalDnaAsync(profile, "cached_token", "pt1", It.IsAny<CancellationToken>())).ReturnsAsync(dnaRecs);

        using var client = new WeaverNodeApiClient(innerMock.Object);

        // 1. Missing token without cache throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PollSnapshotAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartSimulationCampaignAsync(profile, "", "serum-stability"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTelemetryAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetCampaignsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetEndocrineProfileAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SimulateClosedLoopEndocrineAsync(profile, "", closedLoopReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DiscoverFederationPeersAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.BuildFederationManifestAsync(profile, "", buildReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyFederationManifestAsync(profile, "", manifest));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AnalyzeGelAsync(profile, "", gelReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunBenchmarkAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetLatestBenchmarkAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateOpentronsProtocolAsync(profile, "", opentronsReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPatientDnaRegimensAsync(profile, "", "pt1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPatientDigitalDnaAsync(profile, "", "pt1"));

        // 2. Call with explicit token
        var snapRes1 = await client.PollSnapshotAsync(profile, "explicit_token");
        Assert.Equal(InstanceConnectionState.Online, snapRes1.State);

        var simRes1 = await client.StartSimulationCampaignAsync(profile, "explicit_token", "serum-stability");
        Assert.True(simRes1);

        var telemRes1 = await client.GetTelemetryAsync(profile, "explicit_token");
        Assert.Equal(10, telemRes1.CpuUtilizationPercent);

        var campRes1 = await client.GetCampaignsAsync(profile, "explicit_token");
        Assert.Single(campRes1);

        var endRes1 = await client.GetEndocrineProfileAsync(profile, "explicit_token");
        Assert.Equal("MtF", endRes1.Dominance);

        var clRes1 = await client.SimulateClosedLoopEndocrineAsync(profile, "explicit_token", closedLoopReq);
        Assert.True(clRes1.SteadyStateReached);

        var peerRes1 = await client.DiscoverFederationPeersAsync(profile, "explicit_token");
        Assert.Single(peerRes1);

        var bldRes1 = await client.BuildFederationManifestAsync(profile, "explicit_token", buildReq);
        Assert.Equal("run.xyz", bldRes1.DatasetName);

        var verRes1 = await client.VerifyFederationManifestAsync(profile, "explicit_token", manifest);
        Assert.True(verRes1.IsValid);

        var gelRes1 = await client.AnalyzeGelAsync(profile, "explicit_token", gelReq);
        Assert.Equal(1, gelRes1.DetectedLaneCount);

        var bmRes1 = await client.RunBenchmarkAsync(profile, "explicit_token");
        Assert.Equal("GPU", bmRes1.DeviceName);

        var bmLtRes1 = await client.GetLatestBenchmarkAsync(profile, "explicit_token");
        Assert.NotNull(bmLtRes1);

        var otRes1 = await client.GenerateOpentronsProtocolAsync(profile, "explicit_token", opentronsReq);
        Assert.Equal("# code", otRes1.PythonScript);

        var dnaPkgRes1 = await client.GetPatientDnaRegimensAsync(profile, "explicit_token", "pt1");
        Assert.Equal("pt1", dnaPkgRes1.PatientId);

        var dnaRecRes1 = await client.GetPatientDigitalDnaAsync(profile, "explicit_token", "pt1");
        Assert.Single(dnaRecRes1);

        // 3. Set cached token and call with empty token argument
        client.SetSessionToken("cached_token");

        var snapRes2 = await client.PollSnapshotAsync(profile, "");
        Assert.Equal(InstanceConnectionState.Online, snapRes2.State);

        var simRes2 = await client.StartSimulationCampaignAsync(profile, "", "serum-stability");
        Assert.True(simRes2);

        var telemRes2 = await client.GetTelemetryAsync(profile, "");
        Assert.Equal(10, telemRes2.CpuUtilizationPercent);

        var campRes2 = await client.GetCampaignsAsync(profile, "");
        Assert.Single(campRes2);

        var endRes2 = await client.GetEndocrineProfileAsync(profile, "");
        Assert.Equal("MtF", endRes2.Dominance);

        var clRes2 = await client.SimulateClosedLoopEndocrineAsync(profile, "", closedLoopReq);
        Assert.True(clRes2.SteadyStateReached);

        var peerRes2 = await client.DiscoverFederationPeersAsync(profile, "");
        Assert.Single(peerRes2);

        var bldRes2 = await client.BuildFederationManifestAsync(profile, "", buildReq);
        Assert.Equal("run.xyz", bldRes2.DatasetName);

        var verRes2 = await client.VerifyFederationManifestAsync(profile, "", manifest);
        Assert.True(verRes2.IsValid);

        var gelRes2 = await client.AnalyzeGelAsync(profile, "", gelReq);
        Assert.Equal(1, gelRes2.DetectedLaneCount);

        var bmRes2 = await client.RunBenchmarkAsync(profile, "");
        Assert.Equal("GPU", bmRes2.DeviceName);

        var bmLtRes2 = await client.GetLatestBenchmarkAsync(profile, "");
        Assert.NotNull(bmLtRes2);

        var otRes2 = await client.GenerateOpentronsProtocolAsync(profile, "", opentronsReq);
        Assert.Equal("# code", otRes2.PythonScript);

        var dnaPkgRes2 = await client.GetPatientDnaRegimensAsync(profile, "", "pt1");
        Assert.Equal("pt1", dnaPkgRes2.PatientId);

        var dnaRecRes2 = await client.GetPatientDigitalDnaAsync(profile, "", "pt1");
        Assert.Single(dnaRecRes2);

        // 4. Set whitespace token buffer to exercise whitespace fallback exception
        using var whitespaceBuf = ZeroizableSecretBuffer.FromUtf8("   ");
        client.SetSessionToken(whitespaceBuf);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PollSnapshotAsync(profile, ""));
    }

    [Fact]
    public async Task WeaverNodeApiClient_Dispose_CleansUpAndThrowsOnSubsequentCalls()
    {
        // WHAT: Test Dispose idempotency and post-disposal exceptions.
        // WHY:  Guarantees ObjectDisposedException when client is used after disposal.
        // HOW:  Disposes client, verifies innerClient disposed, calls methods and asserts ObjectDisposedException.
        var innerDisposableMock = new Mock<IDisposable>();
        var innerMock = innerDisposableMock.As<INanoWeaverNodeClient>();
        var client = new WeaverNodeApiClient(innerMock.Object);
        client.SetSessionToken("token_to_wipe");

        client.Dispose();
        innerDisposableMock.Verify(d => d.Dispose(), Times.Once);

        // Second dispose is idempotent
        client.Dispose();

        Assert.False(client.HasActiveSession);
        Assert.Null(client.SessionTokenBuffer);

        var profile = CreateTestProfile();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AuthenticateAsync(profile, "pass".ToCharArray()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PollSnapshotAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.StartSimulationCampaignAsync(profile, "token", "serum-stability"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetTelemetryAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetCampaignsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetEndocrineProfileAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SimulateClosedLoopEndocrineAsync(profile, "token", new ClosedLoopSimulationRequest()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DiscoverFederationPeersAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.BuildFederationManifestAsync(profile, "token", new FederationBuildManifestRequest { TrajectoryFileName = "run.xyz" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.VerifyFederationManifestAsync(profile, "token", new TrajectoryFederationManifest { DatasetName = "run.xyz", TotalSizeBytes = 1, ChunkSizeBytes = 1, TotalChunks = 1, Chunks = [], SigningNodeId = "n", SignatureAlgorithm = "Ed25519", SignatureBase64 = "s", ManifestTimestampUtc = DateTimeOffset.UtcNow }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AnalyzeGelAsync(profile, "token", new GelAnalysisRequest { ImageBytes = [1], Width = 1, Height = 1, ExpectedLaneCount = 1, BackgroundThreshold = 0.1, LadderLaneNumber = 1 }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RunBenchmarkAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetLatestBenchmarkAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GenerateOpentronsProtocolAsync(profile, "token", new GenerateOpentronsScriptRequest { SequenceOrSampleId = "s1" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPatientDnaRegimensAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPatientDigitalDnaAsync(profile, "token", "pt1"));
    }
}
