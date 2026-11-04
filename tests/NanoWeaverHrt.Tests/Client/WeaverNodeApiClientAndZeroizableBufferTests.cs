using System.Text.Json;
using Moq;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt;
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

        var trajItems = (IReadOnlyList<TrajectoryFileItem>)[new TrajectoryFileItem { Name = "dna.xyz", SizeBytes = 100, SizeFormatted = "100 B", LastModifiedUtc = "2026-10-20T18:00:00Z", Format = "XYZ" }];
        innerMock.Setup(i => i.ListTrajectoriesAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(trajItems);
        innerMock.Setup(i => i.ListTrajectoriesAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(trajItems);

        var expReq = new ExportRequest { DatasetName = "ds1" };
        var expRes = new DatasetManifest { DatasetName = "ds1", TimestampUtc = "2026-10-20T18:00:00Z", ArchiveFileName = "ds1.tar.gz", Sha256Checksum = "sha", SizeInBytes = 100, IncludedFiles = [] };
        innerMock.Setup(i => i.ExportDatasetAsync(profile, "explicit_token", expReq, It.IsAny<CancellationToken>())).ReturnsAsync(expRes);
        innerMock.Setup(i => i.ExportDatasetAsync(profile, "cached_token", expReq, It.IsAny<CancellationToken>())).ReturnsAsync(expRes);

        var configItems = (IReadOnlyList<ConfigurationItem>)[new ConfigurationItem { Key = "k1", Value = "v1", DefaultValue = "v0", Description = "Test config", Source = ConfigurationSource.DatabaseOverride, Category = "General" }];
        innerMock.Setup(i => i.GetAllConfigurationsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(configItems);
        innerMock.Setup(i => i.GetAllConfigurationsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(configItems);

        var cfgReq = new ConfigOverrideRequest { Key = "k1", Value = "v1" };
        innerMock.Setup(i => i.SetConfigurationOverrideAsync(profile, "explicit_token", cfgReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.SetConfigurationOverrideAsync(profile, "cached_token", cfgReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.DeleteConfigurationOverrideAsync(profile, "explicit_token", "k1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteConfigurationOverrideAsync(profile, "cached_token", "k1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var grants = (IReadOnlyList<SubjectGrant>)[new SubjectGrant { ResearcherUsername = "r1", SubjectUsername = "s1" }];
        innerMock.Setup(i => i.ListSubjectGrantsAsync(profile, "explicit_token", "r1", "s1", It.IsAny<CancellationToken>())).ReturnsAsync(grants);
        innerMock.Setup(i => i.ListSubjectGrantsAsync(profile, "cached_token", "r1", "s1", It.IsAny<CancellationToken>())).ReturnsAsync(grants);

        var compRes = new ComplianceStatusReport { EncryptionAtRestAlgorithm = "ML-KEM-768", AuditSignatureAlgorithm = "ML-DSA-65", IsEncryptionAtRestActive = true, IsAuditLedgerIntact = true, AuditRecordCount = 1 };
        innerMock.Setup(i => i.GetComplianceStatusAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(compRes);
        innerMock.Setup(i => i.GetComplianceStatusAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(compRes);

        var auditRecords = (IReadOnlyList<AuditRecord>)[new AuditRecord { SequenceNumber = 1 }];
        innerMock.Setup(i => i.GetAuditLedgerAsync(profile, "explicit_token", 10, It.IsAny<CancellationToken>())).ReturnsAsync(auditRecords);
        innerMock.Setup(i => i.GetAuditLedgerAsync(profile, "cached_token", 10, It.IsAny<CancellationToken>())).ReturnsAsync(auditRecords);

        var genReq = new GenomicAnalysisRequest { PatientId = "pt1", Regimen = TherapeuticRegimen.FemaleToMale, MarkerIds = [] };
        var genProfile = new PatientGenomicProfile { PatientId = "pt1", Regimen = TherapeuticRegimen.FemaleToMale, ObservedMarkers = [], Alerts = [], ThrombosisRiskIndex = 1.0, OverallClearanceMultiplier = 1.0, CpGMethylationMandatory = false, CalculatedAtUtc = "2026-11-04T00:00:00Z" };
        innerMock.Setup(i => i.AnalyzeGenomicsAsync(profile, "explicit_token", genReq, It.IsAny<CancellationToken>())).ReturnsAsync(genProfile);
        innerMock.Setup(i => i.AnalyzeGenomicsAsync(profile, "cached_token", genReq, It.IsAny<CancellationToken>())).ReturnsAsync(genProfile);

        var seqReq = new SequenceAnalysisRequest { PatientId = "pt1", Sequence = "ACGT", Regimen = TherapeuticRegimen.FemaleToMale };
        innerMock.Setup(i => i.AnalyzeGenomicSequenceAsync(profile, "explicit_token", seqReq, It.IsAny<CancellationToken>())).ReturnsAsync(genProfile);
        innerMock.Setup(i => i.AnalyzeGenomicSequenceAsync(profile, "cached_token", seqReq, It.IsAny<CancellationToken>())).ReturnsAsync(genProfile);

        var auditVerRes = new AuditLedgerVerificationResult { IsValid = true, TotalRecordsVerified = 10, Message = "OK" };
        innerMock.Setup(i => i.VerifyAuditLedgerAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(auditVerRes);
        innerMock.Setup(i => i.VerifyAuditLedgerAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(auditVerRes);

        var queryReq = new TrajectoryAnalyticsApiRequest("ds1", "SELECT 1");
        var queryRes = new TrajectoryAnalyticsApiResponse("ds1", ["x"], [["1"]], 1, 1.0);
        innerMock.Setup(i => i.QueryTrajectoryAnalyticsAsync(profile, "explicit_token", queryReq, It.IsAny<CancellationToken>())).ReturnsAsync(queryRes);
        innerMock.Setup(i => i.QueryTrajectoryAnalyticsAsync(profile, "cached_token", queryReq, It.IsAny<CancellationToken>())).ReturnsAsync(queryRes);

        var localKey = new LocalDatasetSigningKey("k-local", KeyAlgorithm.Ed25519, "pk==");
        var peerKeyList = new PeerKeyListResponse { LocalNode = localKey, Peers = [] };
        innerMock.Setup(i => i.ListPeerKeysAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(peerKeyList);
        innerMock.Setup(i => i.ListPeerKeysAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(peerKeyList);

        var regKeyReq = new RegisterPeerKeyRequest { Label = "Lab", Algorithm = KeyAlgorithm.Ed25519, PublicKeyBase64 = "pk2==" };
        var peerKeyRec = new PeerKeyRecord { KeyId = "k1", Label = "Lab", Algorithm = KeyAlgorithm.Ed25519, PublicKeyBase64 = "pk2==", RegisteredBy = "admin", RegisteredAtUtc = DateTimeOffset.UtcNow };
        innerMock.Setup(i => i.RegisterPeerKeyAsync(profile, "explicit_token", regKeyReq, It.IsAny<CancellationToken>())).ReturnsAsync(peerKeyRec);
        innerMock.Setup(i => i.RegisterPeerKeyAsync(profile, "cached_token", regKeyReq, It.IsAny<CancellationToken>())).ReturnsAsync(peerKeyRec);

        innerMock.Setup(i => i.RevokePeerKeyAsync(profile, "explicit_token", "k1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.RevokePeerKeyAsync(profile, "cached_token", "k1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var subjGrantReq = new GrantSubjectAccessRequest { ResearcherUsername = "r1", SubjectUsername = "s1", AccessLevel = SubjectAccessLevel.GenomicAccess };
        var subjGrantRec = new SubjectGrant { Id = "g1", ResearcherUsername = "r1", SubjectUsername = "s1", AccessLevel = SubjectAccessLevel.GenomicAccess, GrantedBy = "admin", GrantedAtUtc = DateTimeOffset.UtcNow };
        innerMock.Setup(i => i.CreateSubjectGrantAsync(profile, "explicit_token", subjGrantReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGrantRec);
        innerMock.Setup(i => i.CreateSubjectGrantAsync(profile, "cached_token", subjGrantReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGrantRec);

        innerMock.Setup(i => i.RevokeSubjectGrantAsync(profile, "explicit_token", "r1", "s1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.RevokeSubjectGrantAsync(profile, "cached_token", "r1", "s1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var subjGroups = (IReadOnlyList<SubjectGroup>)[new SubjectGroup { Id = "grp1", Name = "Cohort", CreatedBy = "admin", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }];
        innerMock.Setup(i => i.ListSubjectGroupsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(subjGroups);
        innerMock.Setup(i => i.ListSubjectGroupsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(subjGroups);

        var createGrpReq = new CreateSubjectGroupRequest { Name = "Cohort" };
        var subjGroupRec = new SubjectGroup { Id = "grp1", Name = "Cohort", CreatedBy = "admin", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        innerMock.Setup(i => i.CreateSubjectGroupAsync(profile, "explicit_token", createGrpReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);
        innerMock.Setup(i => i.CreateSubjectGroupAsync(profile, "cached_token", createGrpReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);

        innerMock.Setup(i => i.DeleteSubjectGroupAsync(profile, "explicit_token", "grp1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteSubjectGroupAsync(profile, "cached_token", "grp1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var shareKeyRecords = (IReadOnlyList<ShareKeyRecord>)[new ShareKeyRecord { TokenId = "tok1", KeyToken = "nanoweaver_hrt_tok", Label = "Lab", CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1), Permissions = "FullCollaborator" }];
        innerMock.Setup(i => i.ListShareKeysAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(shareKeyRecords);
        innerMock.Setup(i => i.ListShareKeysAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(shareKeyRecords);

        innerMock.Setup(i => i.ListGroupMembersAsync(profile, "explicit_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(["pt1"]);
        innerMock.Setup(i => i.ListGroupMembersAsync(profile, "cached_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(["pt1"]);

        var addMemberReq = new AddGroupMemberRequest { SubjectUsername = "pt1" };
        innerMock.Setup(i => i.AddGroupMemberAsync(profile, "explicit_token", "grp1", addMemberReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.AddGroupMemberAsync(profile, "cached_token", "grp1", addMemberReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.RemoveGroupMemberAsync(profile, "explicit_token", "grp1", "pt1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.RemoveGroupMemberAsync(profile, "cached_token", "grp1", "pt1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var groupGrants = (IReadOnlyList<SubjectGroupGrant>)[new SubjectGroupGrant { Id = "g1", GroupId = "grp1", ResearcherUsername = "r1", AccessLevel = SubjectAccessLevel.FullReadAccess, GrantedBy = "admin", GrantedAtUtc = DateTimeOffset.UtcNow }];
        innerMock.Setup(i => i.ListGroupGrantsAsync(profile, "explicit_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(groupGrants);
        innerMock.Setup(i => i.ListGroupGrantsAsync(profile, "cached_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(groupGrants);

        var groupGrantReq = new GrantGroupAccessRequest { ResearcherUsername = "r1", AccessLevel = SubjectAccessLevel.FullReadAccess };
        var groupGrantRec = new SubjectGroupGrant { Id = "g1", GroupId = "grp1", ResearcherUsername = "r1", AccessLevel = SubjectAccessLevel.FullReadAccess, GrantedBy = "admin", GrantedAtUtc = DateTimeOffset.UtcNow };
        innerMock.Setup(i => i.CreateGroupGrantAsync(profile, "explicit_token", "grp1", groupGrantReq, It.IsAny<CancellationToken>())).ReturnsAsync(groupGrantRec);
        innerMock.Setup(i => i.CreateGroupGrantAsync(profile, "cached_token", "grp1", groupGrantReq, It.IsAny<CancellationToken>())).ReturnsAsync(groupGrantRec);

        innerMock.Setup(i => i.RevokeGroupGrantAsync(profile, "explicit_token", "grp1", "r1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.RevokeGroupGrantAsync(profile, "cached_token", "grp1", "r1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var createShareKeyReq = new CreateShareKeyRequest { Label = "Lab" };
        var createdShareKeyRec = new ShareKeyRecord { TokenId = "tok1", KeyToken = "nanoweaver_hrt_tok", Label = "Lab", CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1), Permissions = "FullCollaborator" };
        innerMock.Setup(i => i.CreateShareKeyAsync(profile, "explicit_token", createShareKeyReq, It.IsAny<CancellationToken>())).ReturnsAsync(createdShareKeyRec);
        innerMock.Setup(i => i.CreateShareKeyAsync(profile, "cached_token", createShareKeyReq, It.IsAny<CancellationToken>())).ReturnsAsync(createdShareKeyRec);

        var redeemReq = new RedeemShareKeyRequest { Token = "nanoweaver_hrt_tok" };
        var redeemRes = new ShareKeyConsumeResult { Success = true, Message = "Token redeemed.", Record = null, PeerSessionToken = "jwt" };
        innerMock.Setup(i => i.RedeemShareKeyAsync(profile, redeemReq, It.IsAny<CancellationToken>())).ReturnsAsync(redeemRes);

        var updateGrpReq = new UpdateSubjectGroupRequest { Name = "Updated Cohort" };
        innerMock.Setup(i => i.GetSubjectGroupAsync(profile, "explicit_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);
        innerMock.Setup(i => i.GetSubjectGroupAsync(profile, "cached_token", "grp1", It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);
        innerMock.Setup(i => i.UpdateSubjectGroupAsync(profile, "explicit_token", "grp1", updateGrpReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);
        innerMock.Setup(i => i.UpdateSubjectGroupAsync(profile, "cached_token", "grp1", updateGrpReq, It.IsAny<CancellationToken>())).ReturnsAsync(subjGroupRec);

        var mySubjects = new[] { new AccessibleSubjectItem { SubjectUsername = "s1", EffectiveLevel = SubjectAccessLevel.FullReadAccess } };
        innerMock.Setup(i => i.ListMySubjectsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(mySubjects);
        innerMock.Setup(i => i.ListMySubjectsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(mySubjects);

        var fabPresets = new FabricationService().GetPresetSamples();
        innerMock.Setup(i => i.GetFabricationPresetsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(fabPresets);
        innerMock.Setup(i => i.GetFabricationPresetsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(fabPresets);

        var fabGenReq = new FabricationGenerateRequest { SequenceOrSampleId = "6hb" };
        FabricationService fabService = new();
        var fabPkg = fabService.GenerateFabricationPackage(fabPresets[0].Id, TherapeuticRegimen.MaleToFemale, "Hexagonal 6-Helix Bundle Nanotube (6HB)", 4.2);
        innerMock.Setup(i => i.GenerateFabricationProtocolAsync(profile, "explicit_token", fabGenReq, It.IsAny<CancellationToken>())).ReturnsAsync(fabPkg);
        innerMock.Setup(i => i.GenerateFabricationProtocolAsync(profile, "cached_token", fabGenReq, It.IsAny<CancellationToken>())).ReturnsAsync(fabPkg);

        var pharmaService = new PharmacogenomicsService();
        var genPresets = new[] { pharmaService.AnalyzeProfile("pt1", Array.Empty<string>(), TherapeuticRegimen.MaleToFemale) };
        innerMock.Setup(i => i.GetGenomicPresetsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(genPresets);
        innerMock.Setup(i => i.GetGenomicPresetsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(genPresets);

        var genMarkers = pharmaService.GetCatalog();
        innerMock.Setup(i => i.GetGenomicMarkersAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(genMarkers);
        innerMock.Setup(i => i.GetGenomicMarkersAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(genMarkers);

        var stackInfo = new StackServiceInfo
        {
            ServiceId = "valkey",
            DisplayName = "Valkey",
            RoleDescription = "Cache",
            EndpointOrLocation = "valkey://127.0.0.1:6379",
            Health = ServiceHealthState.Healthy,
            StatusMessage = "Operational",
            TechnologyStack = "Valkey"
        };
        var stackSvcs = new[] { stackInfo };
        innerMock.Setup(i => i.GetStackServicesAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(stackSvcs);
        innerMock.Setup(i => i.GetStackServicesAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(stackSvcs);

        innerMock.Setup(i => i.PingStackServiceAsync(profile, "explicit_token", "valkey", It.IsAny<CancellationToken>())).ReturnsAsync(stackInfo);
        innerMock.Setup(i => i.PingStackServiceAsync(profile, "cached_token", "valkey", It.IsAny<CancellationToken>())).ReturnsAsync(stackInfo);

        EndocrineStateEngine stateEngine = new();
        var kinetics = stateEngine.SimulateKinetics();
        innerMock.Setup(i => i.GetEndocrineKineticsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(kinetics);
        innerMock.Setup(i => i.GetEndocrineKineticsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(kinetics);

        var toggleReq = new ToggleSwitchRequest { TargetState = ToggleState.EstrogenicDominant };
        var toggleRes = stateEngine.GetActiveProfile();
        innerMock.Setup(i => i.ToggleEndocrineSwitchAsync(profile, "explicit_token", toggleReq, It.IsAny<CancellationToken>())).ReturnsAsync(toggleRes);
        innerMock.Setup(i => i.ToggleEndocrineSwitchAsync(profile, "cached_token", toggleReq, It.IsAny<CancellationToken>())).ReturnsAsync(toggleRes);

        UserAccount sampleUser = new UserAccount().GetExamples();
        CreateUserRequest sampleCreateReq = new CreateUserRequest().GetExamples();
        UserProfile sampleProfile = new UserProfile().GetExamples();
        PatientAddress sampleAddress = new PatientAddress().GetExamples();
        PatientContact sampleContact = new PatientContact().GetExamples();

        innerMock.Setup(i => i.ListUsersAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleUser });
        innerMock.Setup(i => i.ListUsersAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleUser });

        innerMock.Setup(i => i.CreateUserAsync(profile, "explicit_token", sampleCreateReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleUser);
        innerMock.Setup(i => i.CreateUserAsync(profile, "cached_token", sampleCreateReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleUser);

        innerMock.Setup(i => i.DeleteUserAsync(profile, "explicit_token", "user1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteUserAsync(profile, "cached_token", "user1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.GetUserProfileAsync(profile, "explicit_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleProfile);
        innerMock.Setup(i => i.GetUserProfileAsync(profile, "cached_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleProfile);

        innerMock.Setup(i => i.UpdateUserProfileAsync(profile, "explicit_token", "user1", sampleProfile, It.IsAny<CancellationToken>())).ReturnsAsync(sampleProfile);
        innerMock.Setup(i => i.UpdateUserProfileAsync(profile, "cached_token", "user1", sampleProfile, It.IsAny<CancellationToken>())).ReturnsAsync(sampleProfile);

        innerMock.Setup(i => i.ListUserAddressesAsync(profile, "explicit_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleAddress });
        innerMock.Setup(i => i.ListUserAddressesAsync(profile, "cached_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleAddress });

        innerMock.Setup(i => i.AddUserAddressAsync(profile, "explicit_token", "user1", sampleAddress, It.IsAny<CancellationToken>())).ReturnsAsync(sampleAddress);
        innerMock.Setup(i => i.AddUserAddressAsync(profile, "cached_token", "user1", sampleAddress, It.IsAny<CancellationToken>())).ReturnsAsync(sampleAddress);

        innerMock.Setup(i => i.DeleteUserAddressAsync(profile, "explicit_token", "user1", "addr1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteUserAddressAsync(profile, "cached_token", "user1", "addr1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.ListUserContactsAsync(profile, "explicit_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleContact });
        innerMock.Setup(i => i.ListUserContactsAsync(profile, "cached_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleContact });

        innerMock.Setup(i => i.AddUserContactAsync(profile, "explicit_token", "user1", sampleContact, It.IsAny<CancellationToken>())).ReturnsAsync(sampleContact);
        innerMock.Setup(i => i.AddUserContactAsync(profile, "cached_token", "user1", sampleContact, It.IsAny<CancellationToken>())).ReturnsAsync(sampleContact);

        innerMock.Setup(i => i.DeleteUserContactAsync(profile, "explicit_token", "user1", "ct1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteUserContactAsync(profile, "cached_token", "user1", "ct1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        PatientDigitalDnaRecord sampleDna = new PatientDigitalDnaRecord().GetExamples();
        PatientDnaImportRecord sampleImport = new PatientDnaImportRecord().GetExamples();
        CreatePatientImportRequest sampleImportReq = new()
        {
            FileName = "patient_scaffold_variant.fasta",
            SequenceOrData = ">sample\nATGC"
        };
        PatientResultRecord sampleResult = new PatientResultRecord().GetExamples();
        CreatePatientResultRequest sampleResultReq = new()
        {
            ResultType = "EndocrineKinetics",
            Title = "Sample Title",
            Summary = "Sample Summary"
        };
        SetupStatusResponse sampleStatus = new SetupStatusResponse().GetExamples();
        CryptoShredRequest shredReq = new CryptoShredRequest().GetExamples();
        DeIdentifyRequest deIdReq = new DeIdentifyRequest().GetExamples();
        DeIdentificationResult deIdRes = new DeIdentificationResult().GetExamples();
        EncryptRecordRequest encReq = new EncryptRecordRequest().GetExamples();
        EncryptedEnvelope sampleEnv = new EncryptedEnvelope().GetExamples();
        DecryptRecordRequest decReq = new DecryptRecordRequest().GetExamples();
        using var jsonDoc = JsonDocument.Parse("{\"k\":\"v\"}");
        JsonElement sampleJsonElement = jsonDoc.RootElement.Clone();

        DatasetImportResponse sampleImportRes = new DatasetImportResponse().GetExamples();
        ExportEncryptedDatasetResult sampleExportRes = new ExportEncryptedDatasetResult().GetExamples();
        ExportEncryptedDatasetRequest sampleExportReq = new() { TrajectoryFileName = "traj.xyz", SubjectId = "sub_42" };
        List<ExportFileItem> sampleExportList = [new ExportFileItem { Name = "a", SizeBytes = 1, SizeFormatted = "1B", LastModifiedUtc = "now", Category = "Archive" }.GetExamples()];
        byte[] dummyBytes = [1, 2, 3];
        InitialSetupRequest sampleSetupReq = new InitialSetupRequest().GetExamples();
        AuthTokenResponse sampleAuthToken = new AuthTokenResponse().GetExamples();
        SystemHealthOverview sampleHealth = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "0d 4h 12m 30s",
            UptimeSeconds = 15150.0,
            SetupRequired = false,
            AuthenticatedSession = false,
            Description = "High-level system health status."
        };
        EventBusTelemetry sampleBus = new EventBusTelemetry
        {
            IsInternalBusActive = true,
            IsValkeyConnected = true,
            IsDistributedCache = true,
            ValkeyEndpoint = "endpoint",
            OutboundEventsCount = 0,
            InboundEventsCount = 0,
            ActiveSubscribers = []
        }.GetExamples();
        TrajectoryParserProcessResult sampleParser = new TrajectoryParserProcessResult().GetExamples();
        TrajectoryParquetExportResult sampleParquet = new TrajectoryParquetExportResult().GetExamples();
        AptamerTrajectoryMetrics sampleMetrics = new AptamerTrajectoryMetrics
        {
            TrajectoryFileName = "traj.xyz",
            FramesAnalyzed = 100,
            AverageContactsPerFrame = 2.0,
            BindingOccupancyRatio = 0.9,
            StabilityVerdict = "Stable"
        }.GetExamples();
        SimulationInputDeckResult sampleDeck = new SimulationInputDeckResult().GetExamples();
        GenomicAnalysisRequest sampleGenomicReq = new GenomicAnalysisRequest().GetExamples();
        GenomicAdjustmentResult sampleGenomicRes = new GenomicAdjustmentResult().GetExamples();
        GenerateOpentronsScriptRequest sampleOpentronsReq = new GenerateOpentronsScriptRequest { SequenceOrSampleId = "s1" }.GetExamples();

        innerMock.Setup(i => i.GetPatientDigitalDnaRecordAsync(profile, "explicit_token", "user1", "dna1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleDna);
        innerMock.Setup(i => i.GetPatientDigitalDnaRecordAsync(profile, "cached_token", "user1", "dna1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleDna);

        innerMock.Setup(i => i.DeletePatientDigitalDnaAsync(profile, "explicit_token", "user1", "dna1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeletePatientDigitalDnaAsync(profile, "cached_token", "user1", "dna1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.ListPatientImportsAsync(profile, "explicit_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleImport });
        innerMock.Setup(i => i.ListPatientImportsAsync(profile, "cached_token", "user1", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleImport });

        innerMock.Setup(i => i.GetPatientImportAsync(profile, "explicit_token", "user1", "imp1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleImport);
        innerMock.Setup(i => i.GetPatientImportAsync(profile, "cached_token", "user1", "imp1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleImport);

        innerMock.Setup(i => i.CreatePatientImportAsync(profile, "explicit_token", "user1", sampleImportReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleImport);
        innerMock.Setup(i => i.CreatePatientImportAsync(profile, "cached_token", "user1", sampleImportReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleImport);

        innerMock.Setup(i => i.DeletePatientImportAsync(profile, "explicit_token", "user1", "imp1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeletePatientImportAsync(profile, "cached_token", "user1", "imp1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.ListPatientClinicalResultsAsync(profile, "explicit_token", "user1", null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleResult });
        innerMock.Setup(i => i.ListPatientClinicalResultsAsync(profile, "cached_token", "user1", null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { sampleResult });

        innerMock.Setup(i => i.GetPatientClinicalResultAsync(profile, "explicit_token", "user1", "res1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleResult);
        innerMock.Setup(i => i.GetPatientClinicalResultAsync(profile, "cached_token", "user1", "res1", It.IsAny<CancellationToken>())).ReturnsAsync(sampleResult);

        innerMock.Setup(i => i.CreatePatientClinicalResultAsync(profile, "explicit_token", "user1", sampleResultReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleResult);
        innerMock.Setup(i => i.CreatePatientClinicalResultAsync(profile, "cached_token", "user1", sampleResultReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleResult);

        innerMock.Setup(i => i.DeletePatientClinicalResultAsync(profile, "explicit_token", "user1", "res1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeletePatientClinicalResultAsync(profile, "cached_token", "user1", "res1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.GetCurrentUserAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleUser);
        innerMock.Setup(i => i.GetCurrentUserAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleUser);

        innerMock.Setup(i => i.LogoutAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.LogoutAsync(profile, "cached_token", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.GetSetupStatusAsync(profile, It.IsAny<CancellationToken>())).ReturnsAsync(sampleStatus);

        innerMock.Setup(i => i.CryptoShredUserAsync(profile, "explicit_token", shredReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.CryptoShredUserAsync(profile, "cached_token", shredReq, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.DeIdentifyDatasetAsync(profile, "explicit_token", deIdReq, It.IsAny<CancellationToken>())).ReturnsAsync(deIdRes);
        innerMock.Setup(i => i.DeIdentifyDatasetAsync(profile, "cached_token", deIdReq, It.IsAny<CancellationToken>())).ReturnsAsync(deIdRes);

        innerMock.Setup(i => i.EncryptRecordAsync(profile, "explicit_token", encReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleEnv);
        innerMock.Setup(i => i.EncryptRecordAsync(profile, "cached_token", encReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleEnv);

        innerMock.Setup(i => i.DecryptRecordAsync(profile, "explicit_token", decReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleJsonElement);
        innerMock.Setup(i => i.DecryptRecordAsync(profile, "cached_token", decReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleJsonElement);

        innerMock.Setup(i => i.ImportDatasetArchiveAsync(profile, "explicit_token", It.IsAny<byte[]>(), "a.tar.gz", It.IsAny<byte[]>(), "m.json", It.IsAny<CancellationToken>())).ReturnsAsync(sampleImportRes);
        innerMock.Setup(i => i.ImportDatasetArchiveAsync(profile, "cached_token", It.IsAny<byte[]>(), "a.tar.gz", It.IsAny<byte[]>(), "m.json", It.IsAny<CancellationToken>())).ReturnsAsync(sampleImportRes);

        innerMock.Setup(i => i.ExportEncryptedDatasetAsync(profile, "explicit_token", sampleExportReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleExportRes);
        innerMock.Setup(i => i.ExportEncryptedDatasetAsync(profile, "cached_token", sampleExportReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleExportRes);

        innerMock.Setup(i => i.ListExportedDatasetBundlesAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleExportList);
        innerMock.Setup(i => i.ListExportedDatasetBundlesAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleExportList);

        innerMock.Setup(i => i.DeleteExportedDatasetBundleAsync(profile, "explicit_token", "bundle.tar.gz", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerMock.Setup(i => i.DeleteExportedDatasetBundleAsync(profile, "cached_token", "bundle.tar.gz", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        innerMock.Setup(i => i.DownloadDatasetArchiveAsync(profile, "explicit_token", "bundle.tar.gz", It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);
        innerMock.Setup(i => i.DownloadDatasetArchiveAsync(profile, "cached_token", "bundle.tar.gz", It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);

        innerMock.Setup(i => i.DownloadTrajectoryAsync(profile, "explicit_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);
        innerMock.Setup(i => i.DownloadTrajectoryAsync(profile, "cached_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);

        innerMock.Setup(i => i.InitializeSetupAdminAsync(profile, sampleSetupReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleAuthToken);

        innerMock.Setup(i => i.GetSystemHealthAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleHealth);
        innerMock.Setup(i => i.GetSystemHealthAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleHealth);
        innerMock.Setup(i => i.GetSystemHealthAsync(profile, null, It.IsAny<CancellationToken>())).ReturnsAsync(sampleHealth);

        innerMock.Setup(i => i.GetEventBusMetricsAsync(profile, "explicit_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleBus);
        innerMock.Setup(i => i.GetEventBusMetricsAsync(profile, "cached_token", It.IsAny<CancellationToken>())).ReturnsAsync(sampleBus);

        innerMock.Setup(i => i.ProcessTrajectoryCoordinatesAsync(profile, "explicit_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleParser);
        innerMock.Setup(i => i.ProcessTrajectoryCoordinatesAsync(profile, "cached_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleParser);

        innerMock.Setup(i => i.ExportCoordinatesParquetAsync(profile, "explicit_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleParquet);
        innerMock.Setup(i => i.ExportCoordinatesParquetAsync(profile, "cached_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleParquet);

        innerMock.Setup(i => i.AnalyzeTrajectoryEndocrineAsync(profile, "explicit_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleMetrics);
        innerMock.Setup(i => i.AnalyzeTrajectoryEndocrineAsync(profile, "cached_token", "traj.xyz", It.IsAny<CancellationToken>())).ReturnsAsync(sampleMetrics);

        innerMock.Setup(i => i.GetSimulationCampaignConfigAsync(profile, "explicit_token", "CAMPAIGN_SERUM_STABILITY", It.IsAny<CancellationToken>())).ReturnsAsync(sampleDeck);
        innerMock.Setup(i => i.GetSimulationCampaignConfigAsync(profile, "cached_token", "CAMPAIGN_SERUM_STABILITY", It.IsAny<CancellationToken>())).ReturnsAsync(sampleDeck);

        innerMock.Setup(i => i.ApplyGenomicAdjustmentsAsync(profile, "explicit_token", sampleGenomicReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleGenomicRes);
        innerMock.Setup(i => i.ApplyGenomicAdjustmentsAsync(profile, "cached_token", sampleGenomicReq, It.IsAny<CancellationToken>())).ReturnsAsync(sampleGenomicRes);

        innerMock.Setup(i => i.DownloadOpentronsScriptAsync(profile, "explicit_token", sampleOpentronsReq, It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);
        innerMock.Setup(i => i.DownloadOpentronsScriptAsync(profile, "cached_token", sampleOpentronsReq, It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);

        innerMock.Setup(i => i.GetFederationChunkAsync(profile, "explicit_token", "traj.xyz", null, It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);
        innerMock.Setup(i => i.GetFederationChunkAsync(profile, "cached_token", "traj.xyz", null, It.IsAny<CancellationToken>())).ReturnsAsync(dummyBytes);

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
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListTrajectoriesAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExportDatasetAsync(profile, "", expReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAllConfigurationsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetConfigurationOverrideAsync(profile, "", cfgReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteConfigurationOverrideAsync(profile, "", "k1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListSubjectGrantsAsync(profile, "", "r1", "s1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetComplianceStatusAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAuditLedgerAsync(profile, "", 10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AnalyzeGenomicsAsync(profile, "", genReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyAuditLedgerAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AnalyzeGenomicSequenceAsync(profile, "", seqReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryTrajectoryAnalyticsAsync(profile, "", queryReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListPeerKeysAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterPeerKeyAsync(profile, "", regKeyReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RevokePeerKeyAsync(profile, "", "k1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateSubjectGrantAsync(profile, "", subjGrantReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RevokeSubjectGrantAsync(profile, "", "r1", "s1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListSubjectGroupsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateSubjectGroupAsync(profile, "", createGrpReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteSubjectGroupAsync(profile, "", "grp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListShareKeysAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListGroupMembersAsync(profile, "", "grp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AddGroupMemberAsync(profile, "", "grp1", addMemberReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RemoveGroupMemberAsync(profile, "", "grp1", "pt1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListGroupGrantsAsync(profile, "", "grp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateGroupGrantAsync(profile, "", "grp1", groupGrantReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RevokeGroupGrantAsync(profile, "", "grp1", "r1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateShareKeyAsync(profile, "", createShareKeyReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSubjectGroupAsync(profile, "", "grp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateSubjectGroupAsync(profile, "", "grp1", updateGrpReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListMySubjectsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetFabricationPresetsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateFabricationProtocolAsync(profile, "", fabGenReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetGenomicPresetsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetGenomicMarkersAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStackServicesAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PingStackServiceAsync(profile, "", "valkey"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetEndocrineKineticsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ToggleEndocrineSwitchAsync(profile, "", toggleReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListUsersAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateUserAsync(profile, "", sampleCreateReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteUserAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetUserProfileAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpdateUserProfileAsync(profile, "", "user1", sampleProfile));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListUserAddressesAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AddUserAddressAsync(profile, "", "user1", sampleAddress));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteUserAddressAsync(profile, "", "user1", "addr1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListUserContactsAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AddUserContactAsync(profile, "", "user1", sampleContact));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteUserContactAsync(profile, "", "user1", "ct1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPatientDigitalDnaRecordAsync(profile, "", "user1", "dna1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeletePatientDigitalDnaAsync(profile, "", "user1", "dna1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListPatientImportsAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPatientImportAsync(profile, "", "user1", "imp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreatePatientImportAsync(profile, "", "user1", sampleImportReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeletePatientImportAsync(profile, "", "user1", "imp1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListPatientClinicalResultsAsync(profile, "", "user1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPatientClinicalResultAsync(profile, "", "user1", "res1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreatePatientClinicalResultAsync(profile, "", "user1", sampleResultReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeletePatientClinicalResultAsync(profile, "", "user1", "res1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetCurrentUserAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LogoutAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CryptoShredUserAsync(profile, "", shredReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeIdentifyDatasetAsync(profile, "", deIdReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EncryptRecordAsync(profile, "", encReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DecryptRecordAsync(profile, "", decReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ImportDatasetArchiveAsync(profile, "", dummyBytes, "a.tar.gz", dummyBytes, "m.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExportEncryptedDatasetAsync(profile, "", sampleExportReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListExportedDatasetBundlesAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteExportedDatasetBundleAsync(profile, "", "bundle.tar.gz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadDatasetArchiveAsync(profile, "", "bundle.tar.gz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadTrajectoryAsync(profile, "", "traj.xyz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetEventBusMetricsAsync(profile, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ProcessTrajectoryCoordinatesAsync(profile, "", "traj.xyz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExportCoordinatesParquetAsync(profile, "", "traj.xyz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AnalyzeTrajectoryEndocrineAsync(profile, "", "traj.xyz"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSimulationCampaignConfigAsync(profile, "", "CAMPAIGN_SERUM_STABILITY"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyGenomicAdjustmentsAsync(profile, "", sampleGenomicReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadOpentronsScriptAsync(profile, "", sampleOpentronsReq));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetFederationChunkAsync(profile, "", "traj.xyz"));

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

        var trajRes1 = await client.ListTrajectoriesAsync(profile, "explicit_token");
        Assert.Single(trajRes1);

        var expRes1 = await client.ExportDatasetAsync(profile, "explicit_token", expReq);
        Assert.Equal("ds1", expRes1.DatasetName);

        var cfgRes1 = await client.GetAllConfigurationsAsync(profile, "explicit_token");
        Assert.Single(cfgRes1);

        await client.SetConfigurationOverrideAsync(profile, "explicit_token", cfgReq);
        await client.DeleteConfigurationOverrideAsync(profile, "explicit_token", "k1");

        var grantRes1 = await client.ListSubjectGrantsAsync(profile, "explicit_token", "r1", "s1");
        Assert.Single(grantRes1);

        var compRes1 = await client.GetComplianceStatusAsync(profile, "explicit_token");
        Assert.True(compRes1.IsAuditLedgerIntact);

        var auditRes1 = await client.GetAuditLedgerAsync(profile, "explicit_token", 10);
        Assert.Single(auditRes1);

        var genRes1 = await client.AnalyzeGenomicsAsync(profile, "explicit_token", genReq);
        Assert.Equal("pt1", genRes1.PatientId);

        var auditVerRes1 = await client.VerifyAuditLedgerAsync(profile, "explicit_token");
        Assert.True(auditVerRes1.IsValid);

        var seqRes1 = await client.AnalyzeGenomicSequenceAsync(profile, "explicit_token", seqReq);
        Assert.Equal("pt1", seqRes1.PatientId);

        var queryRes1 = await client.QueryTrajectoryAnalyticsAsync(profile, "explicit_token", queryReq);
        Assert.Equal("ds1", queryRes1.DatasetName);

        var peerListRes1 = await client.ListPeerKeysAsync(profile, "explicit_token");
        Assert.Equal(KeyAlgorithm.Ed25519, peerListRes1.LocalNode.Algorithm);

        var regKeyRes1 = await client.RegisterPeerKeyAsync(profile, "explicit_token", regKeyReq);
        Assert.Equal("k1", regKeyRes1.KeyId);

        await client.RevokePeerKeyAsync(profile, "explicit_token", "k1");

        var createGrantRes1 = await client.CreateSubjectGrantAsync(profile, "explicit_token", subjGrantReq);
        Assert.Equal("r1", createGrantRes1.ResearcherUsername);

        await client.RevokeSubjectGrantAsync(profile, "explicit_token", "r1", "s1");

        var groupsRes1 = await client.ListSubjectGroupsAsync(profile, "explicit_token");
        Assert.Single(groupsRes1);

        var createGrpRes1 = await client.CreateSubjectGroupAsync(profile, "explicit_token", createGrpReq);
        Assert.Equal("grp1", createGrpRes1.Id);

        await client.DeleteSubjectGroupAsync(profile, "explicit_token", "grp1");

        var shareKeysRes1 = await client.ListShareKeysAsync(profile, "explicit_token");
        Assert.Single(shareKeysRes1);

        var grpMembers1 = await client.ListGroupMembersAsync(profile, "explicit_token", "grp1");
        Assert.Single(grpMembers1);

        await client.AddGroupMemberAsync(profile, "explicit_token", "grp1", addMemberReq);
        await client.RemoveGroupMemberAsync(profile, "explicit_token", "grp1", "pt1");

        var grpGrants1 = await client.ListGroupGrantsAsync(profile, "explicit_token", "grp1");
        Assert.Single(grpGrants1);

        var createdGrpGrant1 = await client.CreateGroupGrantAsync(profile, "explicit_token", "grp1", groupGrantReq);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, createdGrpGrant1.AccessLevel);

        await client.RevokeGroupGrantAsync(profile, "explicit_token", "grp1", "r1");

        var createdShareKey1 = await client.CreateShareKeyAsync(profile, "explicit_token", createShareKeyReq);
        Assert.Equal("tok1", createdShareKey1.TokenId);

        var redeemRes1 = await client.RedeemShareKeyAsync(profile, redeemReq);
        Assert.True(redeemRes1.Success);

        var grpDetail1 = await client.GetSubjectGroupAsync(profile, "explicit_token", "grp1");
        Assert.Equal("grp1", grpDetail1.Id);

        var updatedGrp1 = await client.UpdateSubjectGroupAsync(profile, "explicit_token", "grp1", updateGrpReq);
        Assert.Equal("grp1", updatedGrp1.Id);

        var mySubj1 = await client.ListMySubjectsAsync(profile, "explicit_token");
        Assert.Single(mySubj1);

        var fabPresets1 = await client.GetFabricationPresetsAsync(profile, "explicit_token");
        Assert.Equal(fabPresets.Count, fabPresets1.Count);

        var fabGen1 = await client.GenerateFabricationProtocolAsync(profile, "explicit_token", fabGenReq);
        Assert.Equal(fabPkg.PackageId, fabGen1.PackageId);

        var genPresets1 = await client.GetGenomicPresetsAsync(profile, "explicit_token");
        Assert.Single(genPresets1);

        var genMarkers1 = await client.GetGenomicMarkersAsync(profile, "explicit_token");
        Assert.Equal(genMarkers.Count, genMarkers1.Count);

        var stackSvcs1 = await client.GetStackServicesAsync(profile, "explicit_token");
        Assert.Single(stackSvcs1);

        var pingSvc1 = await client.PingStackServiceAsync(profile, "explicit_token", "valkey");
        Assert.Equal("valkey", pingSvc1.ServiceId);

        var kinetics1 = await client.GetEndocrineKineticsAsync(profile, "explicit_token");
        Assert.Equal(kinetics.Count, kinetics1.Count);

        var toggleProf1 = await client.ToggleEndocrineSwitchAsync(profile, "explicit_token", toggleReq);
        Assert.Equal(toggleRes.TargetState, toggleProf1.TargetState);

        var users1 = await client.ListUsersAsync(profile, "explicit_token");
        Assert.Single(users1);

        var createdUser1 = await client.CreateUserAsync(profile, "explicit_token", sampleCreateReq);
        Assert.Equal(sampleUser.Username, createdUser1.Username);

        await client.DeleteUserAsync(profile, "explicit_token", "user1");

        var profile1 = await client.GetUserProfileAsync(profile, "explicit_token", "user1");
        Assert.Equal(sampleProfile.Username, profile1.Username);

        var updatedProf1 = await client.UpdateUserProfileAsync(profile, "explicit_token", "user1", sampleProfile);
        Assert.Equal(sampleProfile.Username, updatedProf1.Username);

        var addresses1 = await client.ListUserAddressesAsync(profile, "explicit_token", "user1");
        Assert.Single(addresses1);

        var addedAddr1 = await client.AddUserAddressAsync(profile, "explicit_token", "user1", sampleAddress);
        Assert.Equal(sampleAddress.Id, addedAddr1.Id);

        await client.DeleteUserAddressAsync(profile, "explicit_token", "user1", "addr1");

        var contacts1 = await client.ListUserContactsAsync(profile, "explicit_token", "user1");
        Assert.Single(contacts1);

        var addedCt1 = await client.AddUserContactAsync(profile, "explicit_token", "user1", sampleContact);
        Assert.Equal(sampleContact.Id, addedCt1.Id);

        await client.DeleteUserContactAsync(profile, "explicit_token", "user1", "ct1");

        var dna1 = await client.GetPatientDigitalDnaRecordAsync(profile, "explicit_token", "user1", "dna1");
        Assert.Equal(sampleDna.Id, dna1.Id);

        await client.DeletePatientDigitalDnaAsync(profile, "explicit_token", "user1", "dna1");

        var imports1 = await client.ListPatientImportsAsync(profile, "explicit_token", "user1");
        Assert.Single(imports1);

        var imp1 = await client.GetPatientImportAsync(profile, "explicit_token", "user1", "imp1");
        Assert.Equal(sampleImport.Id, imp1.Id);

        var createdImp1 = await client.CreatePatientImportAsync(profile, "explicit_token", "user1", sampleImportReq);
        Assert.Equal(sampleImport.Id, createdImp1.Id);

        await client.DeletePatientImportAsync(profile, "explicit_token", "user1", "imp1");

        var results1 = await client.ListPatientClinicalResultsAsync(profile, "explicit_token", "user1");
        Assert.Single(results1);

        var res1 = await client.GetPatientClinicalResultAsync(profile, "explicit_token", "user1", "res1");
        Assert.Equal(sampleResult.Id, res1.Id);

        var createdRes1 = await client.CreatePatientClinicalResultAsync(profile, "explicit_token", "user1", sampleResultReq);
        Assert.Equal(sampleResult.Id, createdRes1.Id);

        await client.DeletePatientClinicalResultAsync(profile, "explicit_token", "user1", "res1");

        var me1 = await client.GetCurrentUserAsync(profile, "explicit_token");
        Assert.Equal(sampleUser.Username, me1.Username);

        await client.LogoutAsync(profile, "explicit_token");

        var setupStatus1 = await client.GetSetupStatusAsync(profile);
        Assert.Equal(sampleStatus.SetupRequired, setupStatus1.SetupRequired);

        await client.CryptoShredUserAsync(profile, "explicit_token", shredReq);

        var deId1 = await client.DeIdentifyDatasetAsync(profile, "explicit_token", deIdReq);
        Assert.Equal(deIdRes.PseudonymSubjectId, deId1.PseudonymSubjectId);

        var enc1 = await client.EncryptRecordAsync(profile, "explicit_token", encReq);
        Assert.Equal(sampleEnv.KeyId, enc1.KeyId);

        var dec1 = await client.DecryptRecordAsync(profile, "explicit_token", decReq);
        Assert.Equal("v", dec1.GetProperty("k").GetString());

        var importRes1 = await client.ImportDatasetArchiveAsync(profile, "explicit_token", dummyBytes, "a.tar.gz", dummyBytes, "m.json");
        Assert.Equal(sampleImportRes.Message, importRes1.Message);

        var exportRes1 = await client.ExportEncryptedDatasetAsync(profile, "explicit_token", sampleExportReq);
        Assert.Equal(sampleExportRes.BundlePath, exportRes1.BundlePath);

        var listRes1 = await client.ListExportedDatasetBundlesAsync(profile, "explicit_token");
        Assert.Single(listRes1);

        await client.DeleteExportedDatasetBundleAsync(profile, "explicit_token", "bundle.tar.gz");

        var dlArch1 = await client.DownloadDatasetArchiveAsync(profile, "explicit_token", "bundle.tar.gz");
        Assert.Equal(dummyBytes, dlArch1);

        var dlTraj1 = await client.DownloadTrajectoryAsync(profile, "explicit_token", "traj.xyz");
        Assert.Equal(dummyBytes, dlTraj1);

        var initAdmin1 = await client.InitializeSetupAdminAsync(profile, sampleSetupReq);
        Assert.Equal(sampleAuthToken.Token, initAdmin1.Token);

        var health1 = await client.GetSystemHealthAsync(profile, "explicit_token");
        Assert.Equal(sampleHealth.Status, health1.Status);

        var bus1 = await client.GetEventBusMetricsAsync(profile, "explicit_token");
        Assert.Equal(sampleBus.IsInternalBusActive, bus1.IsInternalBusActive);

        var proc1 = await client.ProcessTrajectoryCoordinatesAsync(profile, "explicit_token", "traj.xyz");
        Assert.Equal(sampleParser.FileName, proc1.FileName);

        var parq1 = await client.ExportCoordinatesParquetAsync(profile, "explicit_token", "traj.xyz");
        Assert.Equal(sampleParquet.ParquetFile, parq1.ParquetFile);

        var metric1 = await client.AnalyzeTrajectoryEndocrineAsync(profile, "explicit_token", "traj.xyz");
        Assert.Equal(sampleMetrics.TrajectoryFileName, metric1.TrajectoryFileName);

        var cfg1 = await client.GetSimulationCampaignConfigAsync(profile, "explicit_token", "CAMPAIGN_SERUM_STABILITY");
        Assert.Equal(sampleDeck.CampaignId, cfg1.CampaignId);

        var gen1 = await client.ApplyGenomicAdjustmentsAsync(profile, "explicit_token", sampleGenomicReq);
        Assert.NotNull(gen1.GenomicProfile);

        var open1 = await client.DownloadOpentronsScriptAsync(profile, "explicit_token", sampleOpentronsReq);
        Assert.Equal(dummyBytes, open1);

        var chunk1 = await client.GetFederationChunkAsync(profile, "explicit_token", "traj.xyz");
        Assert.Equal(dummyBytes, chunk1);

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

        var trajRes2 = await client.ListTrajectoriesAsync(profile, "");
        Assert.Single(trajRes2);

        var expRes2 = await client.ExportDatasetAsync(profile, "", expReq);
        Assert.Equal("ds1", expRes2.DatasetName);

        var cfgRes2 = await client.GetAllConfigurationsAsync(profile, "");
        Assert.Single(cfgRes2);

        await client.SetConfigurationOverrideAsync(profile, "", cfgReq);
        await client.DeleteConfigurationOverrideAsync(profile, "", "k1");

        var grantRes2 = await client.ListSubjectGrantsAsync(profile, "", "r1", "s1");
        Assert.Single(grantRes2);

        var compRes2 = await client.GetComplianceStatusAsync(profile, "");
        Assert.True(compRes2.IsAuditLedgerIntact);

        var auditRes2 = await client.GetAuditLedgerAsync(profile, "", 10);
        Assert.Single(auditRes2);

        var genRes2 = await client.AnalyzeGenomicsAsync(profile, "", genReq);
        Assert.Equal("pt1", genRes2.PatientId);

        var auditVerRes2 = await client.VerifyAuditLedgerAsync(profile, "");
        Assert.True(auditVerRes2.IsValid);

        var seqRes2 = await client.AnalyzeGenomicSequenceAsync(profile, "", seqReq);
        Assert.Equal("pt1", seqRes2.PatientId);

        var queryRes2 = await client.QueryTrajectoryAnalyticsAsync(profile, "", queryReq);
        Assert.Equal("ds1", queryRes2.DatasetName);

        var peerListRes2 = await client.ListPeerKeysAsync(profile, "");
        Assert.Equal(KeyAlgorithm.Ed25519, peerListRes2.LocalNode.Algorithm);

        var regKeyRes2 = await client.RegisterPeerKeyAsync(profile, "", regKeyReq);
        Assert.Equal("k1", regKeyRes2.KeyId);

        await client.RevokePeerKeyAsync(profile, "", "k1");

        var grantRes2Cached = await client.CreateSubjectGrantAsync(profile, "", subjGrantReq);
        Assert.Equal("r1", grantRes2Cached.ResearcherUsername);

        await client.RevokeSubjectGrantAsync(profile, "", "r1", "s1");

        var groupsRes2 = await client.ListSubjectGroupsAsync(profile, "");
        Assert.Single(groupsRes2);

        var createGrpRes2 = await client.CreateSubjectGroupAsync(profile, "", createGrpReq);
        Assert.Equal("grp1", createGrpRes2.Id);

        await client.DeleteSubjectGroupAsync(profile, "", "grp1");

        var shareKeysRes2 = await client.ListShareKeysAsync(profile, "");
        Assert.Single(shareKeysRes2);

        var grpMembers2 = await client.ListGroupMembersAsync(profile, "", "grp1");
        Assert.Single(grpMembers2);

        await client.AddGroupMemberAsync(profile, "", "grp1", addMemberReq);
        await client.RemoveGroupMemberAsync(profile, "", "grp1", "pt1");

        var grpGrants2 = await client.ListGroupGrantsAsync(profile, "", "grp1");
        Assert.Single(grpGrants2);

        var createdGrpGrant2 = await client.CreateGroupGrantAsync(profile, "", "grp1", groupGrantReq);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, createdGrpGrant2.AccessLevel);

        await client.RevokeGroupGrantAsync(profile, "", "grp1", "r1");

        var createdShareKey2 = await client.CreateShareKeyAsync(profile, "", createShareKeyReq);
        Assert.Equal("tok1", createdShareKey2.TokenId);

        var redeemRes2 = await client.RedeemShareKeyAsync(profile, redeemReq);
        Assert.True(redeemRes2.Success);

        var grpDetail2 = await client.GetSubjectGroupAsync(profile, "", "grp1");
        Assert.Equal("grp1", grpDetail2.Id);

        var updatedGrp2 = await client.UpdateSubjectGroupAsync(profile, "", "grp1", updateGrpReq);
        Assert.Equal("grp1", updatedGrp2.Id);

        var mySubj2 = await client.ListMySubjectsAsync(profile, "");
        Assert.Single(mySubj2);

        var fabPresets2 = await client.GetFabricationPresetsAsync(profile, "");
        Assert.Equal(fabPresets.Count, fabPresets2.Count);

        var fabGen2 = await client.GenerateFabricationProtocolAsync(profile, "", fabGenReq);
        Assert.Equal(fabPkg.PackageId, fabGen2.PackageId);

        var genPresets2 = await client.GetGenomicPresetsAsync(profile, "");
        Assert.Single(genPresets2);

        var genMarkers2 = await client.GetGenomicMarkersAsync(profile, "");
        Assert.Equal(genMarkers.Count, genMarkers2.Count);

        var stackSvcs2 = await client.GetStackServicesAsync(profile, "");
        Assert.Single(stackSvcs2);

        var pingSvc2 = await client.PingStackServiceAsync(profile, "", "valkey");
        Assert.Equal("valkey", pingSvc2.ServiceId);

        var kinetics2 = await client.GetEndocrineKineticsAsync(profile, "");
        Assert.Equal(kinetics.Count, kinetics2.Count);

        var toggleProf2 = await client.ToggleEndocrineSwitchAsync(profile, "", toggleReq);
        Assert.Equal(toggleRes.TargetState, toggleProf2.TargetState);

        var users2 = await client.ListUsersAsync(profile, "");
        Assert.Single(users2);

        var createdUser2 = await client.CreateUserAsync(profile, "", sampleCreateReq);
        Assert.Equal(sampleUser.Username, createdUser2.Username);

        await client.DeleteUserAsync(profile, "", "user1");

        var profile2 = await client.GetUserProfileAsync(profile, "", "user1");
        Assert.Equal(sampleProfile.Username, profile2.Username);

        var updatedProf2 = await client.UpdateUserProfileAsync(profile, "", "user1", sampleProfile);
        Assert.Equal(sampleProfile.Username, updatedProf2.Username);

        var addresses2 = await client.ListUserAddressesAsync(profile, "", "user1");
        Assert.Single(addresses2);

        var addedAddr2 = await client.AddUserAddressAsync(profile, "", "user1", sampleAddress);
        Assert.Equal(sampleAddress.Id, addedAddr2.Id);

        await client.DeleteUserAddressAsync(profile, "", "user1", "addr1");

        var contacts2 = await client.ListUserContactsAsync(profile, "", "user1");
        Assert.Single(contacts2);

        var addedCt2 = await client.AddUserContactAsync(profile, "", "user1", sampleContact);
        Assert.Equal(sampleContact.Id, addedCt2.Id);

        await client.DeleteUserContactAsync(profile, "", "user1", "ct1");

        var dna2 = await client.GetPatientDigitalDnaRecordAsync(profile, "", "user1", "dna1");
        Assert.Equal(sampleDna.Id, dna2.Id);

        await client.DeletePatientDigitalDnaAsync(profile, "", "user1", "dna1");

        var imports2 = await client.ListPatientImportsAsync(profile, "", "user1");
        Assert.Single(imports2);

        var imp2 = await client.GetPatientImportAsync(profile, "", "user1", "imp1");
        Assert.Equal(sampleImport.Id, imp2.Id);

        var createdImp2 = await client.CreatePatientImportAsync(profile, "", "user1", sampleImportReq);
        Assert.Equal(sampleImport.Id, createdImp2.Id);

        await client.DeletePatientImportAsync(profile, "", "user1", "imp1");

        var results2 = await client.ListPatientClinicalResultsAsync(profile, "", "user1");
        Assert.Single(results2);

        var res2 = await client.GetPatientClinicalResultAsync(profile, "", "user1", "res1");
        Assert.Equal(sampleResult.Id, res2.Id);

        var createdRes2 = await client.CreatePatientClinicalResultAsync(profile, "", "user1", sampleResultReq);
        Assert.Equal(sampleResult.Id, createdRes2.Id);

        await client.DeletePatientClinicalResultAsync(profile, "", "user1", "res1");

        var me2 = await client.GetCurrentUserAsync(profile, "");
        Assert.Equal(sampleUser.Username, me2.Username);

        await client.LogoutAsync(profile, "");

        var setupStatus2 = await client.GetSetupStatusAsync(profile);
        Assert.Equal(sampleStatus.SetupRequired, setupStatus2.SetupRequired);

        await client.CryptoShredUserAsync(profile, "", shredReq);

        var deId2 = await client.DeIdentifyDatasetAsync(profile, "", deIdReq);
        Assert.Equal(deIdRes.PseudonymSubjectId, deId2.PseudonymSubjectId);

        var enc2 = await client.EncryptRecordAsync(profile, "", encReq);
        Assert.Equal(sampleEnv.KeyId, enc2.KeyId);

        var dec2 = await client.DecryptRecordAsync(profile, "", decReq);
        Assert.Equal("v", dec2.GetProperty("k").GetString());

        var importRes2 = await client.ImportDatasetArchiveAsync(profile, "", dummyBytes, "a.tar.gz", dummyBytes, "m.json");
        Assert.Equal(sampleImportRes.Message, importRes2.Message);

        var exportRes2 = await client.ExportEncryptedDatasetAsync(profile, "", sampleExportReq);
        Assert.Equal(sampleExportRes.BundlePath, exportRes2.BundlePath);

        var listRes2 = await client.ListExportedDatasetBundlesAsync(profile, "");
        Assert.Single(listRes2);

        await client.DeleteExportedDatasetBundleAsync(profile, "", "bundle.tar.gz");

        var dlArch2 = await client.DownloadDatasetArchiveAsync(profile, "", "bundle.tar.gz");
        Assert.Equal(dummyBytes, dlArch2);

        var dlTraj2 = await client.DownloadTrajectoryAsync(profile, "", "traj.xyz");
        Assert.Equal(dummyBytes, dlTraj2);

        var health2 = await client.GetSystemHealthAsync(profile);
        Assert.Equal(sampleHealth.Status, health2.Status);

        var bus2 = await client.GetEventBusMetricsAsync(profile, "");
        Assert.Equal(sampleBus.IsInternalBusActive, bus2.IsInternalBusActive);

        var proc2 = await client.ProcessTrajectoryCoordinatesAsync(profile, "", "traj.xyz");
        Assert.Equal(sampleParser.FileName, proc2.FileName);

        var parq2 = await client.ExportCoordinatesParquetAsync(profile, "", "traj.xyz");
        Assert.Equal(sampleParquet.ParquetFile, parq2.ParquetFile);

        var metric2 = await client.AnalyzeTrajectoryEndocrineAsync(profile, "", "traj.xyz");
        Assert.Equal(sampleMetrics.TrajectoryFileName, metric2.TrajectoryFileName);

        var cfg2 = await client.GetSimulationCampaignConfigAsync(profile, "", "CAMPAIGN_SERUM_STABILITY");
        Assert.Equal(sampleDeck.CampaignId, cfg2.CampaignId);

        var gen2 = await client.ApplyGenomicAdjustmentsAsync(profile, "", sampleGenomicReq);
        Assert.NotNull(gen2.GenomicProfile);

        var open2 = await client.DownloadOpentronsScriptAsync(profile, "", sampleOpentronsReq);
        Assert.Equal(dummyBytes, open2);

        var chunk2 = await client.GetFederationChunkAsync(profile, "", "traj.xyz");
        Assert.Equal(dummyBytes, chunk2);

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
        byte[] dummyBytes = [1, 2, 3];
        ExportEncryptedDatasetRequest sampleExportReq = new() { TrajectoryFileName = "traj.xyz", SubjectId = "sub_42" };
        InitialSetupRequest sampleSetupReq = new InitialSetupRequest().GetExamples();
        GenomicAnalysisRequest sampleGenomicReq = new GenomicAnalysisRequest().GetExamples();
        GenerateOpentronsScriptRequest sampleOpentronsReq = new GenerateOpentronsScriptRequest { SequenceOrSampleId = "s1" }.GetExamples();
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
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListTrajectoriesAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExportDatasetAsync(profile, "token", new ExportRequest { DatasetName = "ds1" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAllConfigurationsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SetConfigurationOverrideAsync(profile, "token", new ConfigOverrideRequest { Key = "k", Value = "v" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteConfigurationOverrideAsync(profile, "token", "k"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListSubjectGrantsAsync(profile, "token", "subj1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetComplianceStatusAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAuditLedgerAsync(profile, "token", 10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AnalyzeGenomicsAsync(profile, "token", new GenomicAnalysisRequest { PatientId = "pt1", Regimen = TherapeuticRegimen.FemaleToMale }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.VerifyAuditLedgerAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AnalyzeGenomicSequenceAsync(profile, "token", new SequenceAnalysisRequest { PatientId = "pt1", Sequence = "ACGT", Regimen = TherapeuticRegimen.FemaleToMale }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.QueryTrajectoryAnalyticsAsync(profile, "token", new TrajectoryAnalyticsApiRequest("ds1", "SELECT 1")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListPeerKeysAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RegisterPeerKeyAsync(profile, "token", new RegisterPeerKeyRequest { Label = "Lab", Algorithm = KeyAlgorithm.Ed25519, PublicKeyBase64 = "pk==" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RevokePeerKeyAsync(profile, "token", "k1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreateSubjectGrantAsync(profile, "token", new GrantSubjectAccessRequest()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RevokeSubjectGrantAsync(profile, "token", "r1", "s1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListSubjectGroupsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreateSubjectGroupAsync(profile, "token", new CreateSubjectGroupRequest()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteSubjectGroupAsync(profile, "token", "grp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListShareKeysAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListGroupMembersAsync(profile, "token", "grp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AddGroupMemberAsync(profile, "token", "grp1", new AddGroupMemberRequest { SubjectUsername = "pt1" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RemoveGroupMemberAsync(profile, "token", "grp1", "pt1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListGroupGrantsAsync(profile, "token", "grp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreateGroupGrantAsync(profile, "token", "grp1", new GrantGroupAccessRequest { ResearcherUsername = "r1" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RevokeGroupGrantAsync(profile, "token", "grp1", "r1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreateShareKeyAsync(profile, "token", new CreateShareKeyRequest()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RedeemShareKeyAsync(profile, new RedeemShareKeyRequest { Token = "tok" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetSubjectGroupAsync(profile, "token", "grp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.UpdateSubjectGroupAsync(profile, "token", "grp1", new UpdateSubjectGroupRequest { Name = "Updated" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListMySubjectsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetFabricationPresetsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GenerateFabricationProtocolAsync(profile, "token", new FabricationGenerateRequest { SequenceOrSampleId = "6hb" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetGenomicPresetsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetGenomicMarkersAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetStackServicesAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PingStackServiceAsync(profile, "token", "valkey"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetEndocrineKineticsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ToggleEndocrineSwitchAsync(profile, "token", new ToggleSwitchRequest { TargetState = ToggleState.EstrogenicDominant }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListUsersAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreateUserAsync(profile, "token", new CreateUserRequest().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteUserAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetUserProfileAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.UpdateUserProfileAsync(profile, "token", "user1", new UserProfile().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListUserAddressesAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AddUserAddressAsync(profile, "token", "user1", new PatientAddress().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteUserAddressAsync(profile, "token", "user1", "addr1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListUserContactsAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AddUserContactAsync(profile, "token", "user1", new PatientContact().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteUserContactAsync(profile, "token", "user1", "ct1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPatientDigitalDnaRecordAsync(profile, "token", "user1", "dna1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeletePatientDigitalDnaAsync(profile, "token", "user1", "dna1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListPatientImportsAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPatientImportAsync(profile, "token", "user1", "imp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreatePatientImportAsync(profile, "token", "user1", new CreatePatientImportRequest { FileName = "f", SequenceOrData = "s" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeletePatientImportAsync(profile, "token", "user1", "imp1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListPatientClinicalResultsAsync(profile, "token", "user1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPatientClinicalResultAsync(profile, "token", "user1", "res1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CreatePatientClinicalResultAsync(profile, "token", "user1", new CreatePatientResultRequest { ResultType = "r", Title = "t", Summary = "s" }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeletePatientClinicalResultAsync(profile, "token", "user1", "res1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetCurrentUserAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.LogoutAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetSetupStatusAsync(profile));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CryptoShredUserAsync(profile, "token", new CryptoShredRequest().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeIdentifyDatasetAsync(profile, "token", new DeIdentifyRequest().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.EncryptRecordAsync(profile, "token", new EncryptRecordRequest().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DecryptRecordAsync(profile, "token", new DecryptRecordRequest().GetExamples()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ImportDatasetArchiveAsync(profile, "token", dummyBytes, "a.tar.gz", dummyBytes, "m.json"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExportEncryptedDatasetAsync(profile, "token", sampleExportReq));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListExportedDatasetBundlesAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DeleteExportedDatasetBundleAsync(profile, "token", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DownloadDatasetArchiveAsync(profile, "token", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DownloadTrajectoryAsync(profile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.InitializeSetupAdminAsync(profile, sampleSetupReq));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetSystemHealthAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetEventBusMetricsAsync(profile, "token"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProcessTrajectoryCoordinatesAsync(profile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExportCoordinatesParquetAsync(profile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AnalyzeTrajectoryEndocrineAsync(profile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetSimulationCampaignConfigAsync(profile, "token", "CAMPAIGN_SERUM_STABILITY"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ApplyGenomicAdjustmentsAsync(profile, "token", sampleGenomicReq));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DownloadOpentronsScriptAsync(profile, "token", sampleOpentronsReq));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetFederationChunkAsync(profile, "token", "traj.xyz"));
    }
}
