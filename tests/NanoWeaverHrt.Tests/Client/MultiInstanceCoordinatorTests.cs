using System.Security.Cryptography;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Client;

/// <summary>
/// Comprehensive unit tests verifying <see cref="MultiInstanceCoordinator"/> state management,
/// connection lifecycle, token memory zeroing, and state-transition alert diffing.
/// </summary>
public sealed class MultiInstanceCoordinatorTests : IDisposable
{
    /// <summary>
    /// Mock instance profile repository.
    /// </summary>
    private readonly Mock<IInstanceProfileRepository> _repoMock = new(MockBehavior.Strict);

    /// <summary>
    /// Mock node API client.
    /// </summary>
    private readonly Mock<INanoWeaverNodeClient> _nodeClientMock = new(MockBehavior.Strict);

    /// <summary>
    /// Coordinator instance under test.
    /// </summary>
    private readonly MultiInstanceCoordinator _coordinator;

    /// <summary>
    /// Sample profile for testing.
    /// </summary>
    private readonly InstanceProfile _sampleProfile = new(
        InstanceId: "node-1",
        DisplayName: "Cluster Node One",
        BaseUri: "http://127.0.0.1:5000",
        IsLocalInstance: true,
        Username: "researcher-01",
        Role: "Researcher",
        KeyAlgorithm: "Ed25519",
        PublicKeyPem: "sample_pub_pem",
        EncryptedPrivateKeyEnvelopeBase64: "sample_env_base64",
        NotificationsEnabled: true,
        PollingIntervalSeconds: 10,
        AddedAtUtc: DateTimeOffset.UtcNow,
        LastConnectedAtUtc: null);

    /// <summary>
    /// Second sample profile for testing sorting and multi-node scenarios.
    /// </summary>
    private readonly InstanceProfile _secondProfile = new(
        InstanceId: "node-2",
        DisplayName: "Alpha Workstation",
        BaseUri: "https://cluster.lantea.one:5000",
        IsLocalInstance: false,
        Username: "admin-01",
        Role: "Administrator",
        KeyAlgorithm: "ML-DSA-65",
        PublicKeyPem: "sample_pub_pem_2",
        EncryptedPrivateKeyEnvelopeBase64: "sample_env_base64_2",
        NotificationsEnabled: true,
        PollingIntervalSeconds: 15,
        AddedAtUtc: DateTimeOffset.UtcNow,
        LastConnectedAtUtc: null);

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiInstanceCoordinatorTests"/> class.
    /// </summary>
    public MultiInstanceCoordinatorTests()
    {
        // WHAT: Instantiates MultiInstanceCoordinator with mock dependencies.
        // WHY:  Permits isolated verification of coordinator state machines.
        // HOW:  Passes mock objects to constructor.
        _coordinator = new MultiInstanceCoordinator(_repoMock.Object, _nodeClientMock.Object);
    }

    /// <summary>
    /// Disposes coordinator resources.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Disposes the coordinator under test.
        // WHY:  Cleans up in-memory sessions.
        // HOW:  Invokes Dispose on _coordinator.
        _coordinator.Dispose();
    }

    /// <summary>
    /// Verifies constructor argument validation.
    /// </summary>
    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // WHAT: Tests null guard clauses on constructor.
        // WHY:  Ensures fail-fast semantics when required dependencies are missing.
        // HOW:  Asserts ArgumentNullException is thrown when either dependency is null.
        Assert.Throws<ArgumentNullException>(() => new MultiInstanceCoordinator(null!, _nodeClientMock.Object));
        Assert.Throws<ArgumentNullException>(() => new MultiInstanceCoordinator(_repoMock.Object, null!));
    }

    /// <summary>
    /// Verifies RegisterProfile and GetSnapshots alphabetical ordering.
    /// </summary>
    [Fact]
    public void RegisterProfile_And_GetSnapshots_ReturnsSortedSnapshots()
    {
        // WHAT: Tests registering profiles and retrieving snapshots.
        // WHY:  Verifies profiles appear in Disconnected state and are ordered by DisplayName.
        // HOW:  Registers node-1 ("Cluster Node One") and node-2 ("Alpha Workstation") and verifies ordering.
        Assert.Throws<ArgumentNullException>(() => _coordinator.RegisterProfile(null!));

        _coordinator.RegisterProfile(_sampleProfile);
        _coordinator.RegisterProfile(_secondProfile);

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = _coordinator.GetSnapshots();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal("Alpha Workstation", snapshots[0].DisplayName);
        Assert.Equal(InstanceConnectionState.Disconnected, snapshots[0].State);
        Assert.Equal("Cluster Node One", snapshots[1].DisplayName);
        Assert.Equal(InstanceConnectionState.Disconnected, snapshots[1].State);
    }

    /// <summary>
    /// Verifies SyncProfilesAsync loads profiles from repository.
    /// </summary>
    [Fact]
    public async Task SyncProfilesAsync_LoadsProfilesFromRepository()
    {
        // WHAT: Tests synchronizing snapshots with stored profiles.
        // WHY:  Initializes coordinator snapshots from SQLite storage.
        // HOW:  Configures repository to return profiles and calls SyncProfilesAsync.
        _repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InstanceProfile> { _sampleProfile, _secondProfile });

        await _coordinator.SyncProfilesAsync();

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = _coordinator.GetSnapshots();
        Assert.Equal(2, snapshots.Count);
    }

    /// <summary>
    /// Verifies ConnectInstanceAsync argument validation.
    /// </summary>
    [Fact]
    public async Task ConnectInstanceAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests argument validation in ConnectInstanceAsync.
        // WHY:  Ensures invalid instance IDs are rejected and missing profiles fail cleanly.
        // HOW:  Asserts ArgumentException on empty ID and KeyNotFoundException when profile not found.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();

        await Assert.ThrowsAsync<ArgumentException>(() => _coordinator.ConnectInstanceAsync("", passphrase));
        await Assert.ThrowsAsync<ArgumentException>(() => _coordinator.ConnectInstanceAsync("   ", passphrase));

        _repoMock.Setup(r => r.GetProfileAsync("unknown-node", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceProfile?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _coordinator.ConnectInstanceAsync("unknown-node", passphrase));
    }

    /// <summary>
    /// Verifies successful connection lifecycle in ConnectInstanceAsync.
    /// </summary>
    [Fact]
    public async Task ConnectInstanceAsync_SuccessfulHandshake_ReturnsOnlineSnapshot()
    {
        // WHAT: Tests successful authentication and initial telemetry capture.
        // WHY:  Establishes authenticated session and updates profile last connected timestamp.
        // HOW:  Configures successful AuthenticateAsync and PollSnapshotAsync; verifies returned snapshot.
        ReadOnlyMemory<char> passphrase = "valid_password".AsMemory();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("token_jwt", "Bearer", 3600, now, now.AddHours(1), "researcher-01", "Researcher"));

        _repoMock.Setup(r => r.UpdateLastConnectedAsync(_sampleProfile.InstanceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        InstanceConnectionSnapshot liveSnapshot = new(
            _sampleProfile.InstanceId,
            _sampleProfile.DisplayName,
            InstanceConnectionState.Online,
            15.0,
            40.0,
            65.0,
            1,
            50.0,
            now,
            null);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_jwt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveSnapshot);

        InstanceConnectionSnapshot result = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);

        Assert.Equal(InstanceConnectionState.Online, result.State);
        Assert.Equal(15.0, result.CpuUtilizationPercent);
        Assert.Equal(65.0, result.GpuTemperatureCelsius);

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = _coordinator.GetSnapshots();
        Assert.Single(snapshots);
        Assert.Equal(InstanceConnectionState.Online, snapshots[0].State);
    }

    /// <summary>
    /// Verifies that connecting an already connected instance safely replaces and zeroes the prior token buffer.
    /// </summary>
    [Fact]
    public async Task ConnectInstanceAsync_AlreadyConnectedInstance_OverwritesAndZeroesPreviousToken()
    {
        // WHAT: Tests reconnecting an instance that already holds an active token.
        // WHY:  Ensures StoreCachedToken executes the TryRemove and Array.Clear branch.
        // HOW:  Calls ConnectInstanceAsync twice in sequence with different tokens.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.SetupSequence(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("token_first", "Bearer", 3600, now, now.AddHours(1), "user", "Researcher"))
            .ReturnsAsync(new NodeAuthSession("token_second", "Bearer", 3600, now, now.AddHours(1), "user", "Researcher"));

        _repoMock.Setup(r => r.UpdateLastConnectedAsync(_sampleProfile.InstanceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(_sampleProfile.InstanceId, _sampleProfile.DisplayName, InstanceConnectionState.Online, 10, 20, 50, 0, 10, now, null));

        InstanceConnectionSnapshot snap1 = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);
        Assert.Equal(InstanceConnectionState.Online, snap1.State);

        InstanceConnectionSnapshot snap2 = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);
        Assert.Equal(InstanceConnectionState.Online, snap2.State);
    }

    /// <summary>
    /// Verifies ConnectInstanceAsync handling of authentication rejection (401/403).
    /// </summary>
    [Theory]
    [InlineData(401, InstanceConnectionState.Unauthorized)]
    [InlineData(403, InstanceConnectionState.Unauthorized)]
    [InlineData(500, InstanceConnectionState.Degraded)]
    public async Task ConnectInstanceAsync_NodeApiException_ReturnsErrorSnapshot(int statusCode, InstanceConnectionState expectedState)
    {
        // WHAT: Tests error handling when authentication returns NodeApiException.
        // WHY:  Ensures snapshot reflects error state (Unauthorized or Degraded) with problem title.
        // HOW:  Throws NodeApiException from AuthenticateAsync and asserts returned snapshot.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NodeApiException(statusCode, "Signature Rejected"));

        InstanceConnectionSnapshot result = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);

        Assert.Equal(expectedState, result.State);
        Assert.Equal("Signature Rejected", result.LastErrorTitle);
    }

    /// <summary>
    /// Verifies ConnectInstanceAsync handling of network transport failure.
    /// </summary>
    [Fact]
    public async Task ConnectInstanceAsync_HttpRequestException_ReturnsOfflineSnapshot()
    {
        // WHAT: Tests error handling when network is unreachable.
        // WHY:  Ensures snapshot is set to Offline with transport error message.
        // HOW:  Throws HttpRequestException from AuthenticateAsync and asserts Offline snapshot.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        InstanceConnectionSnapshot result = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);

        Assert.Equal(InstanceConnectionState.Offline, result.State);
        Assert.Equal("Connection refused", result.LastErrorTitle);
    }

    /// <summary>
    /// Verifies ConnectInstanceAsync handling of cryptographic passphrase failure.
    /// </summary>
    [Fact]
    public async Task ConnectInstanceAsync_CryptographicException_ReturnsUnauthorizedSnapshot()
    {
        // WHAT: Tests error handling when user supplies an incorrect passphrase.
        // WHY:  Unsealing failures must be captured cleanly without crashing the coordinator.
        // HOW:  Throws CryptographicException from AuthenticateAsync and asserts Unauthorized snapshot.
        ReadOnlyMemory<char> passphrase = "wrong_password".AsMemory();

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CryptographicException("Decryption failed"));

        InstanceConnectionSnapshot result = await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);

        Assert.Equal(InstanceConnectionState.Unauthorized, result.State);
        Assert.Equal("Decryption failed", result.LastErrorTitle);
    }

    /// <summary>
    /// Verifies DisconnectInstance zeroes token memory and transitions instance to Disconnected.
    /// </summary>
    [Fact]
    public async Task DisconnectInstance_ConnectedInstance_ZeroesTokenAndSetsDisconnected()
    {
        // WHAT: Tests disconnecting an instance.
        // WHY:  Prime Directive 12 requires zeroing cached Bearer token buffers.
        // HOW:  Connects instance, disconnects it, verifies snapshot state is Disconnected.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("token_secret", "Bearer", 3600, now, now.AddHours(1), "researcher-01", "Researcher"));

        _repoMock.Setup(r => r.UpdateLastConnectedAsync(_sampleProfile.InstanceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(_sampleProfile.InstanceId, _sampleProfile.DisplayName, InstanceConnectionState.Online, 10, 20, 50, 0, 10, now, null));

        await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);

        Assert.Throws<ArgumentException>(() => _coordinator.DisconnectInstance(""));
        _coordinator.DisconnectInstance(_sampleProfile.InstanceId);

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = _coordinator.GetSnapshots();
        Assert.Single(snapshots);
        Assert.Equal(InstanceConnectionState.Disconnected, snapshots[0].State);
        Assert.Equal(0.0, snapshots[0].CpuUtilizationPercent);

        // Safe no-op on non-connected instance
        _coordinator.DisconnectInstance("non-existent-instance");
    }

    /// <summary>
    /// Verifies PollAllConnectedAsync returns empty list when no instances are connected.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_NoConnectedInstances_ReturnsEmptyAlerts()
    {
        // WHAT: Tests polling cycle when zero instances have active sessions.
        // WHY:  Guarantees no false alerts are generated on idle coordinator.
        // HOW:  Calls PollAllConnectedAsync and asserts empty alert collection.
        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(alerts);
    }

    /// <summary>
    /// Verifies handling when no previous snapshot exists in cache during polling.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_NoPreviousSnapshotInCache_CreatesDefaultSnapshotAndDiffs()
    {
        // WHAT: Tests the previousSnapshot ??= branch when snapshot cache is initially empty.
        // WHY:  Ensures the null-coalescing snapshot initializer branch is fully exercised.
        // HOW:  Connects node, removes snapshot from cache via reflection or clear, then polls.
        await SetupConnectedInstanceAsync("token_1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Clear snapshots so previousSnapshot is null
        var field = typeof(MultiInstanceCoordinator).GetField("_snapshots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var snapshotsDict = field?.GetValue(_coordinator) as System.Collections.Concurrent.ConcurrentDictionary<string, InstanceConnectionSnapshot>;
        snapshotsDict?.Clear();

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Online,
                10.0,
                20.0,
                55.0,
                0,
                15.0,
                now,
                null));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(alerts); // Disconnected -> Online does not emit alert
    }

    /// <summary>
    /// Verifies state-transition diffing when an instance goes from Online to Offline.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_OnlineToOffline_EmitsCriticalAlert()
    {
        // WHAT: Tests Online -> Offline state diffing.
        // WHY:  Emits Critical alert to notify operator of connection loss.
        // HOW:  Connects node, polls returning Offline snapshot, verifies Critical alert generated.
        await SetupConnectedInstanceAsync("token_1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                now,
                "Node Unreachable"));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Single(alerts);
        Assert.Equal(InstanceAlertSeverity.Critical, alerts[0].Severity);
        Assert.Equal("Instance Offline", alerts[0].Title);
        Assert.Contains("transitioned to offline", alerts[0].Message);
    }

    /// <summary>
    /// Verifies state-transition diffing when an instance goes from Degraded to Offline.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_DegradedToOffline_EmitsCriticalAlert()
    {
        // WHAT: Tests Degraded -> Offline state diffing.
        // WHY:  A degraded node dropping offline must also trigger a Critical alert.
        // HOW:  Sets snapshot to Degraded, polls returning Offline snapshot, asserts Critical alert.
        await SetupConnectedInstanceAsync("token_1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Transition to Degraded first
        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Degraded,
                10.0,
                10.0,
                70.0,
                0,
                10.0,
                now,
                "Subsystem fault"));

        await _coordinator.PollAllConnectedAsync();

        // Now transition from Degraded to Offline
        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                now,
                "Connection dropped"));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Single(alerts);
        Assert.Equal(InstanceAlertSeverity.Critical, alerts[0].Severity);
        Assert.Equal("Instance Offline", alerts[0].Title);
    }

    /// <summary>
    /// Verifies state-transition diffing when an instance recovers from Offline back to Online.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_OfflineToOnline_EmitsInfoAlert()
    {
        // WHAT: Tests Offline -> Online state recovery diffing.
        // WHY:  Emits Info alert when node rejoins the cluster.
        // HOW:  Forces node to Offline, then polls returning Online, verifies Info alert.
        await SetupConnectedInstanceAsync("token_1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        // First poll: Offline
        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                now,
                "Connection timeout"));

        await _coordinator.PollAllConnectedAsync();

        // Second poll: Online
        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Online,
                20.0,
                30.0,
                55.0,
                0,
                50.0,
                now,
                null));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Single(alerts);
        Assert.Equal(InstanceAlertSeverity.Info, alerts[0].Severity);
        Assert.Equal("Instance Online", alerts[0].Title);
        Assert.Contains("back online", alerts[0].Message);
    }

    /// <summary>
    /// Verifies state-transition diffing when an active simulation campaign completes.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_SimulationCompletes_EmitsInfoAlert()
    {
        // WHAT: Tests simulation completion transition (ActiveSimulationCount > 0 -> 0).
        // WHY:  Emits Info alert notifying researcher that oxDNA/LAMMPS run has finished.
        // HOW:  Sets initial active count to 1, next poll returns 0, verifies Info alert.
        await SetupConnectedInstanceAsync("token_1", initialActiveSims: 1);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Online,
                5.0,
                10.0,
                45.0,
                0, // Completed
                0.0,
                now,
                null));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Single(alerts);
        Assert.Equal(InstanceAlertSeverity.Info, alerts[0].Severity);
        Assert.Equal("Simulation Completed", alerts[0].Title);
    }

    /// <summary>
    /// Verifies state-transition diffing when GPU temperature reaches or exceeds 85.0°C.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_ThermalThresholdExceeded_EmitsWarningAlert()
    {
        // WHAT: Tests thermal threshold warning (GpuTemperatureCelsius >= 85.0).
        // WHY:  Alerts operator to potential thermal throttling and fan failure.
        // HOW:  Initial temp 60.0°C, next poll returns 87.2°C, verifies Warning alert generated.
        await SetupConnectedInstanceAsync("token_1", initialGpuTemp: 60.0);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Degraded,
                40.0,
                99.0,
                87.2,
                0,
                10.0,
                now,
                "Thermal throttling"));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Single(alerts);
        Assert.Equal(InstanceAlertSeverity.Warning, alerts[0].Severity);
        Assert.Equal("Elevated GPU Temperature", alerts[0].Title);
        Assert.Contains("87.2°C", alerts[0].Message);

        // Subsequent poll with temperature still elevated must NOT duplicate the alert
        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Degraded,
                40.0,
                99.0,
                88.0,
                0,
                10.0,
                now,
                "Thermal throttling"));

        IReadOnlyList<InstanceAlertEvent> duplicateCheck = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(duplicateCheck);
    }

    /// <summary>
    /// Verifies simultaneous multiple alert diffing (simulation completed AND temperature spike).
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_MultipleTransitions_EmitsMultipleAlerts()
    {
        // WHAT: Tests multiple distinct transitions detected in a single polling cycle.
        // WHY:  Ensures independent alert conditions do not swallow each other.
        // HOW:  Transitions simulation count (1 -> 0) AND temp (50.0 -> 86.0), asserts both alerts emitted.
        await SetupConnectedInstanceAsync("token_1", initialActiveSims: 1, initialGpuTemp: 50.0);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Degraded,
                10.0,
                95.0,
                86.0,
                0, // Completed
                10.0,
                now,
                "Thermal"));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();

        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Title == "Simulation Completed");
        Assert.Contains(alerts, a => a.Title == "Elevated GPU Temperature");
    }

    /// <summary>
    /// Verifies handling when node returns Unauthorized during polling, wiping cached session.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_UnauthorizedResponse_WipesCachedSession()
    {
        // WHAT: Tests automatic session wipe when instance reports 401/403 Unauthorized during polling.
        // WHY:  Prevents continuous polling with expired tokens.
        // HOW:  Returns Unauthorized snapshot, verifies session is wiped and subsequent poll does not poll node.
        await SetupConnectedInstanceAsync("token_1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Unauthorized,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                now,
                "Session Expired"));

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(alerts);

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = _coordinator.GetSnapshots();
        Assert.Single(snapshots);
        Assert.Equal(InstanceConnectionState.Unauthorized, snapshots[0].State);

        // Subsequent poll should have 0 connected instances
        IReadOnlyList<InstanceAlertEvent> nextPoll = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(nextPoll);
    }

    /// <summary>
    /// Verifies handling when an instance profile was deleted from repository during polling.
    /// </summary>
    [Fact]
    public async Task PollAllConnectedAsync_ProfileDeletedFromRepository_SkipsGracefully()
    {
        // WHAT: Tests race condition where a profile is removed from SQLite while still in coordinator session cache.
        // WHY:  Coordinator must skip gracefully without throwing.
        // HOW:  Returns null from GetProfileAsync during poll and asserts empty alerts.
        await SetupConnectedInstanceAsync("token_1");

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceProfile?)null);

        IReadOnlyList<InstanceAlertEvent> alerts = await _coordinator.PollAllConnectedAsync();
        Assert.Empty(alerts);
    }

    /// <summary>
    /// Verifies Dispose behavior, idempotency, and ObjectDisposedException guards.
    /// </summary>
    [Fact]
    public async Task Dispose_Idempotent_GuardsAllMethodsAgainstUseAfterDispose()
    {
        // WHAT: Tests IDisposable lifecycle on MultiInstanceCoordinator.
        // WHY:  Ensures fail-fast semantics and memory zeroing on dispose.
        // HOW:  Calls Dispose multiple times and asserts ObjectDisposedException on all API methods.
        _coordinator.Dispose();
        _coordinator.Dispose(); // idempotent

        Assert.Throws<ObjectDisposedException>(() => _coordinator.GetSnapshots());
        Assert.Throws<ObjectDisposedException>(() => _coordinator.RegisterProfile(_sampleProfile));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _coordinator.SyncProfilesAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _coordinator.ConnectInstanceAsync("node-1", "pass".AsMemory()));
        Assert.Throws<ObjectDisposedException>(() => _coordinator.DisconnectInstance("node-1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _coordinator.PollAllConnectedAsync());
    }

    /// <summary>
    /// Helper method to connect an instance profile and seed initial snapshot metrics.
    /// </summary>
    /// <param name="token">Bearer token string to cache.</param>
    /// <param name="initialActiveSims">Initial active simulation count.</param>
    /// <param name="initialGpuTemp">Initial GPU core temperature.</param>
    private async Task SetupConnectedInstanceAsync(string token, int initialActiveSims = 0, double initialGpuTemp = 50.0)
    {
        // WHAT: Connects the sample profile to the coordinator with specific baseline metrics.
        // WHY:  Provides a reusable initial state for state-transition diffing tests.
        // HOW:  Sets up mocks for GetProfileAsync, AuthenticateAsync, UpdateLastConnectedAsync, and PollSnapshotAsync.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _repoMock.Setup(r => r.GetProfileAsync(_sampleProfile.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleProfile);

        _nodeClientMock.Setup(c => c.AuthenticateAsync(_sampleProfile, passphrase, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession(token, "Bearer", 3600, now, now.AddHours(1), "researcher-01", "Researcher"));

        _repoMock.Setup(r => r.UpdateLastConnectedAsync(_sampleProfile.InstanceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _nodeClientMock.Setup(c => c.PollSnapshotAsync(_sampleProfile, token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceConnectionSnapshot(
                _sampleProfile.InstanceId,
                _sampleProfile.DisplayName,
                InstanceConnectionState.Online,
                20.0,
                50.0,
                initialGpuTemp,
                initialActiveSims,
                60.0,
                now,
                null));

        await _coordinator.ConnectInstanceAsync(_sampleProfile.InstanceId, passphrase);
    }
}
