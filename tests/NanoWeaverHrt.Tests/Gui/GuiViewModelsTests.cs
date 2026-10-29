using System.ComponentModel;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Gui.Core.Theme;
using NanoWeaverHrt.Gui.Core.ViewModels;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Comprehensive unit tests for <see cref="SovereignPalette"/>, <see cref="InstanceOnboardingViewModel"/>,
/// and <see cref="MainShellViewModel"/> achieving 100.00% line and branch coverage.
/// </summary>
public sealed class GuiViewModelsTests
{
    // =========================================================================
    // SovereignPalette Tests
    // =========================================================================

    [Fact]
    public void SovereignPalette_Tokens_AreValidHexColors()
    {
        // WHAT: Verifies all SovereignPalette color constants are non-empty and start with '#'.
        // WHY:  Ensures dark mode color tokens are valid CSS/Avalonia hex color representations.
        // HOW:  Assert starts with '#' and standard lengths of 7 characters (#rrggbb).
        Assert.StartsWith("#", SovereignPalette.BackgroundHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.BackgroundHex.Length);

        Assert.StartsWith("#", SovereignPalette.SurfaceHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.SurfaceHex.Length);

        Assert.StartsWith("#", SovereignPalette.SurfaceSubtleHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.SurfaceSubtleHex.Length);

        Assert.StartsWith("#", SovereignPalette.AccentCyanHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.AccentCyanHex.Length);

        Assert.StartsWith("#", SovereignPalette.AlertMagentaHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.AlertMagentaHex.Length);

        Assert.StartsWith("#", SovereignPalette.SuccessGreenHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.SuccessGreenHex.Length);

        Assert.StartsWith("#", SovereignPalette.WarningAmberHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.WarningAmberHex.Length);

        Assert.StartsWith("#", SovereignPalette.BorderHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.BorderHex.Length);

        Assert.StartsWith("#", SovereignPalette.BorderHighlightHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.BorderHighlightHex.Length);

        Assert.StartsWith("#", SovereignPalette.TextPrimaryHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.TextPrimaryHex.Length);

        Assert.StartsWith("#", SovereignPalette.TextMutedHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.TextMutedHex.Length);

        Assert.StartsWith("#", SovereignPalette.TextDimHex, StringComparison.Ordinal);
        Assert.Equal(7, SovereignPalette.TextDimHex.Length);
    }

    // =========================================================================
    // InstanceOnboardingViewModel Tests
    // =========================================================================

    [Fact]
    public void InstanceOnboardingViewModel_Constructor_NullArgs_ThrowsArgumentNullException()
    {
        // WHAT: Verifies argument null checks in InstanceOnboardingViewModel constructor.
        // WHY:  Ensures fail-fast semantics when required dependencies are missing.
        // HOW:  Pass null for profileRepository and keyRingVault.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();

        Assert.Throws<ArgumentNullException>(() => new InstanceOnboardingViewModel(null!, vault));
        Assert.Throws<ArgumentNullException>(() => new InstanceOnboardingViewModel(repoMock.Object, null!));
    }

    [Fact]
    public void InstanceOnboardingViewModel_PropertyChangeEvents_FireCorrectly()
    {
        // WHAT: Tests INotifyPropertyChanged firing when properties change, and suppression when unchanged.
        // WHY:  Ensures Avalonia UI bindings react properly to user input and avoid redundant redraws.
        // HOW:  Subscribe to PropertyChanged, change properties, and assert fired event names.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();
        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault);

        List<string> firedProperties = new();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                firedProperties.Add(args.PropertyName);
            }
        };

        // DisplayName
        vm.DisplayName = "Node 1";
        Assert.Equal("Node 1", vm.DisplayName);
        Assert.Contains(nameof(vm.DisplayName), firedProperties);
        firedProperties.Clear();
        vm.DisplayName = "Node 1"; // No change
        Assert.Empty(firedProperties);

        // BaseUri
        vm.BaseUri = "https://node.domain.org:5000";
        Assert.Equal("https://node.domain.org:5000", vm.BaseUri);
        Assert.Contains(nameof(vm.BaseUri), firedProperties);
        firedProperties.Clear();
        vm.BaseUri = "https://node.domain.org:5000"; // No change
        Assert.Empty(firedProperties);

        // Username
        vm.Username = "alice";
        Assert.Equal("alice", vm.Username);
        Assert.Contains(nameof(vm.Username), firedProperties);
        firedProperties.Clear();
        vm.Username = "alice"; // No change
        Assert.Empty(firedProperties);

        // Role
        vm.Role = "Administrator";
        Assert.Equal("Administrator", vm.Role);
        Assert.Contains(nameof(vm.Role), firedProperties);
        firedProperties.Clear();
        vm.Role = "Administrator"; // No change
        Assert.Empty(firedProperties);

        // KeyAlgorithm
        vm.KeyAlgorithm = "ML-DSA-65";
        Assert.Equal("ML-DSA-65", vm.KeyAlgorithm);
        Assert.Contains(nameof(vm.KeyAlgorithm), firedProperties);
        firedProperties.Clear();
        vm.KeyAlgorithm = "ML-DSA-65"; // No change
        Assert.Empty(firedProperties);

        // PollingIntervalSeconds
        vm.PollingIntervalSeconds = 30;
        Assert.Equal(30, vm.PollingIntervalSeconds);
        Assert.Contains(nameof(vm.PollingIntervalSeconds), firedProperties);
        firedProperties.Clear();
        vm.PollingIntervalSeconds = 30; // No change
        Assert.Empty(firedProperties);

        // NotificationsEnabled
        vm.NotificationsEnabled = false;
        Assert.False(vm.NotificationsEnabled);
        Assert.Contains(nameof(vm.NotificationsEnabled), firedProperties);
        firedProperties.Clear();
        vm.NotificationsEnabled = false; // No change
        Assert.Empty(firedProperties);
    }

    [Theory]
    [InlineData("", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 15, "pass", "Display name cannot be empty.")]
    [InlineData("   ", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 15, "pass", "Display name cannot be empty.")]
    [InlineData("Node", "", "admin", "Administrator", "Ed25519", 15, "pass", "Base URI must be a valid absolute URI.")]
    [InlineData("Node", "invalid-uri", "admin", "Administrator", "Ed25519", 15, "pass", "Base URI must be a valid absolute URI.")]
    [InlineData("Node", "http://remote-server.org", "admin", "Administrator", "Ed25519", 15, "pass", "Base URI must use HTTPS for remote instances, or HTTP for localhost/loopback instances.")]
    [InlineData("Node", "ftp://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 15, "pass", "Base URI must use HTTPS for remote instances, or HTTP for localhost/loopback instances.")]
    [InlineData("Node", "http://127.0.0.1:5000", "", "Administrator", "Ed25519", 15, "pass", "Username cannot be empty.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "InvalidRole", "Ed25519", 15, "pass", "Role must be 'Administrator', 'Researcher', or 'Patient'.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "Administrator", "RSA-2048", 15, "pass", "Signature algorithm must be 'Ed25519' or 'ML-DSA-65'.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 4, "pass", "Polling interval must be between 5 and 300 seconds.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 301, "pass", "Polling interval must be between 5 and 300 seconds.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 15, "", "Passphrase cannot be empty or whitespace.")]
    [InlineData("Node", "http://127.0.0.1:5000", "admin", "Administrator", "Ed25519", 15, "   ", "Passphrase cannot be empty or whitespace.")]
    public async Task CreateWithNewKeyBundleAsync_ValidationFailures_ReturnsNullAndSetsStatus(
        string name,
        string uri,
        string user,
        string role,
        string algo,
        int interval,
        string pass,
        string expectedStatus)
    {
        // WHAT: Verifies all input validation failure paths for CreateWithNewKeyBundleAsync.
        // WHY:  Guarantees invalid form fields are rejected with informative error messages.
        // HOW:  Provide parameterized input, execute method, and assert null result with status message.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();
        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = name,
            BaseUri = uri,
            Username = user,
            Role = role,
            KeyAlgorithm = algo,
            PollingIntervalSeconds = interval
        };

        InstanceProfile? result = await vm.CreateWithNewKeyBundleAsync(pass.ToCharArray());

        Assert.Null(result);
        Assert.Equal(expectedStatus, vm.StatusMessage);
    }

    [Fact]
    public async Task CreateWithNewKeyBundleAsync_Success_LoopbackHttp_And_CoordinatorNull()
    {
        // WHAT: Verifies successful onboarding of local loopback node without a coordinator.
        // WHY:  Confirms keypair generation, profile construction, and persistence.
        // HOW:  Setup vault, execute CreateWithNewKeyBundleAsync, and assert returned profile.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault
        {
            GenerateHandler = (algo, pass) => new ClientKeyBundle(algo, "PUB_PEM", "ENV_B64", "FINGERPRINT")
        };

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "Local Node",
            BaseUri = "http://127.0.0.1:5000",
            Username = "Alice",
            Role = "administrator",
            KeyAlgorithm = "Ed25519",
            PollingIntervalSeconds = 15,
            NotificationsEnabled = true
        };

        InstanceProfile? profile = await vm.CreateWithNewKeyBundleAsync("passphrase123".ToCharArray());

        Assert.NotNull(profile);
        Assert.Equal("Local Node", profile.DisplayName);
        Assert.Equal("http://127.0.0.1:5000", profile.BaseUri);
        Assert.True(profile.IsLocalInstance);
        Assert.Equal("alice", profile.Username);
        Assert.Equal("Administrator", profile.Role);
        Assert.Equal("Ed25519", profile.KeyAlgorithm);
        Assert.Equal("PUB_PEM", profile.PublicKeyPem);
        Assert.Equal("ENV_B64", profile.EncryptedPrivateKeyEnvelopeBase64);
        Assert.Contains("onboarded successfully", vm.StatusMessage);

        repoMock.Verify(r => r.UpsertProfileAsync(It.Is<InstanceProfile>(p => p.DisplayName == "Local Node"), default), Times.Once);
    }

    [Fact]
    public async Task CreateWithNewKeyBundleAsync_Success_RemoteHttps_And_MultiInstanceCoordinator()
    {
        // WHAT: Verifies successful onboarding of remote HTTPS node with MultiInstanceCoordinator.
        // WHY:  Confirms registration in coordinator when coordinator is MultiInstanceCoordinator.
        // HOW:  Instantiate real MultiInstanceCoordinator, run CreateWithNewKeyBundleAsync, assert registration.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();
        var coordinator = new MultiInstanceCoordinator(repoMock.Object, nodeMock.Object);

        var vault = new TestKeyRingVault
        {
            GenerateHandler = (algo, pass) => new ClientKeyBundle(algo, "PUB_PEM_PQ", "ENV_B64_PQ", "FINGERPRINT_PQ")
        };

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault, coordinator)
        {
            DisplayName = "Remote Node",
            BaseUri = "https://node.cluster.net:8443/",
            Username = "Bob",
            Role = "patient",
            KeyAlgorithm = "ML-DSA-65",
            PollingIntervalSeconds = 60,
            NotificationsEnabled = false
        };

        InstanceProfile? profile = await vm.CreateWithNewKeyBundleAsync("passphrase123".ToCharArray());

        Assert.NotNull(profile);
        Assert.Equal("Remote Node", profile.DisplayName);
        Assert.Equal("https://node.cluster.net:8443", profile.BaseUri);
        Assert.False(profile.IsLocalInstance);
        Assert.Equal("bob", profile.Username);
        Assert.Equal("Patient", profile.Role);
        Assert.Equal("ML-DSA-65", profile.KeyAlgorithm);
        Assert.False(profile.NotificationsEnabled);

        // Check registered in coordinator snapshots
        IReadOnlyList<InstanceConnectionSnapshot> snapshots = coordinator.GetSnapshots();
        Assert.Contains(snapshots, s => s.DisplayName == "Remote Node");
    }

    [Fact]
    public async Task CreateWithNewKeyBundleAsync_RoleResearcher_NormalizesCorrectly()
    {
        // WHAT: Verifies NormalizeRole returns "Researcher" for researcher role.
        // WHY:  Ensures case normalization for Researcher role.
        // HOW:  Set role to "researcher", invoke creation, check profile.Role.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "Lab Node",
            BaseUri = "http://localhost:5000",
            Username = "researcher1",
            Role = "researcher",
            KeyAlgorithm = "Ed25519",
            PollingIntervalSeconds = 10
        };

        InstanceProfile? profile = await vm.CreateWithNewKeyBundleAsync("pass".ToCharArray());
        Assert.NotNull(profile);
        Assert.Equal("Researcher", profile.Role);
    }

    [Fact]
    public async Task CreateWithNewKeyBundleAsync_Exception_ReturnsNullAndSetsStatus()
    {
        // WHAT: Tests error handling when key generation or persistence throws an exception.
        // WHY:  Ensures errors are gracefully captured in StatusMessage without unhandled crashes.
        // HOW:  Make vault throw InvalidOperationException and assert error status.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault
        {
            GenerateHandler = (algo, pass) => throw new InvalidOperationException("Key generation entropy exhausted")
        };

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "Faulty Node",
            BaseUri = "http://127.0.0.1:5000",
            Username = "carol",
            Role = "Researcher",
            KeyAlgorithm = "Ed25519"
        };

        InstanceProfile? result = await vm.CreateWithNewKeyBundleAsync("pass".ToCharArray());

        Assert.Null(result);
        Assert.Contains("Failed to onboard instance: Key generation entropy exhausted", vm.StatusMessage);
    }

    [Theory]
    [InlineData("", "PRIV_KEY", "Public key PEM cannot be empty.")]
    [InlineData("   ", "PRIV_KEY", "Public key PEM cannot be empty.")]
    [InlineData("PUB_KEY", "", "Private key PEM cannot be empty.")]
    [InlineData("PUB_KEY", "   ", "Private key PEM cannot be empty.")]
    public async Task CreateWithExistingKeyAsync_KeyValidationFailures_ReturnsNull(
        string pubKey,
        string privKey,
        string expectedStatus)
    {
        // WHAT: Verifies public and private key PEM validation in CreateWithExistingKeyAsync.
        // WHY:  Ensures invalid key material strings are rejected before sealing.
        // HOW:  Pass empty or whitespace keys and assert null result with status message.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();
        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "Imported Node",
            BaseUri = "http://127.0.0.1:5000",
            Username = "dave",
            Role = "Researcher",
            KeyAlgorithm = "Ed25519"
        };

        InstanceProfile? result = await vm.CreateWithExistingKeyAsync(pubKey, privKey, "pass".ToCharArray());

        Assert.Null(result);
        Assert.Equal(expectedStatus, vm.StatusMessage);
    }

    [Fact]
    public async Task CreateWithExistingKeyAsync_InputValidationFailure_ReturnsNull()
    {
        // WHAT: Verifies standard form input validation in CreateWithExistingKeyAsync.
        // WHY:  Guarantees common fields (DisplayName, Uri, etc.) are verified on import.
        // HOW:  Leave DisplayName empty, execute CreateWithExistingKeyAsync, assert null result.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault();
        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "", // Invalid
            BaseUri = "http://127.0.0.1:5000",
            Username = "dave",
            Role = "Researcher",
            KeyAlgorithm = "Ed25519"
        };

        InstanceProfile? result = await vm.CreateWithExistingKeyAsync("PUB_PEM", "PRIV_PEM", "pass".ToCharArray());

        Assert.Null(result);
        Assert.Equal("Display name cannot be empty.", vm.StatusMessage);
    }

    [Fact]
    public async Task CreateWithExistingKeyAsync_Success_And_CoordinatorRegistration()
    {
        // WHAT: Verifies successful import with existing PEM keys and MultiInstanceCoordinator registration.
        // WHY:  Confirms key sealing, profile creation, and persistence.
        // HOW:  Setup vault.SealPrivateKeyPem, run CreateWithExistingKeyAsync, assert profile.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();
        var coordinator = new MultiInstanceCoordinator(repoMock.Object, nodeMock.Object);

        var vault = new TestKeyRingVault
        {
            SealHandler = (pem, pass) => "SEALED_ENVELOPE_B64"
        };

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault, coordinator)
        {
            DisplayName = "Existing Node",
            BaseUri = "http://localhost:5000",
            Username = "Eve",
            Role = "Administrator",
            KeyAlgorithm = "Ed25519",
            PollingIntervalSeconds = 20,
            NotificationsEnabled = true
        };

        InstanceProfile? profile = await vm.CreateWithExistingKeyAsync("PUB_PEM", "PRIV_PEM", "pass123".ToCharArray());

        Assert.NotNull(profile);
        Assert.Equal("Existing Node", profile.DisplayName);
        Assert.Equal("SEALED_ENVELOPE_B64", profile.EncryptedPrivateKeyEnvelopeBase64);
        Assert.Equal("PUB_PEM", profile.PublicKeyPem);
        Assert.True(profile.IsLocalInstance);
        Assert.Contains("onboarded successfully", vm.StatusMessage);

        IReadOnlyList<InstanceConnectionSnapshot> snapshots = coordinator.GetSnapshots();
        Assert.Contains(snapshots, s => s.DisplayName == "Existing Node");
    }

    [Fact]
    public async Task CreateWithExistingKeyAsync_Exception_ReturnsNullAndSetsStatus()
    {
        // WHAT: Tests error handling when key sealing throws an exception.
        // WHY:  Ensures unexpected cryptographic errors are caught gracefully.
        // HOW:  Make vault.SealPrivateKeyPem throw ArgumentException and assert status message.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var vault = new TestKeyRingVault
        {
            SealHandler = (pem, pass) => throw new ArgumentException("Invalid PEM format")
        };

        var vm = new InstanceOnboardingViewModel(repoMock.Object, vault)
        {
            DisplayName = "Import Failure",
            BaseUri = "http://127.0.0.1:5000",
            Username = "frank",
            Role = "Researcher",
            KeyAlgorithm = "Ed25519"
        };

        InstanceProfile? result = await vm.CreateWithExistingKeyAsync("PUB_PEM", "BAD_PRIV_PEM", "pass".ToCharArray());

        Assert.Null(result);
        Assert.Contains("Failed to onboard instance: Invalid PEM format", vm.StatusMessage);
    }

    // =========================================================================
    // MainShellViewModel Tests
    // =========================================================================

    [Fact]
    public void MainShellViewModel_Constructor_NullArgs_ThrowsArgumentNullException()
    {
        // WHAT: Verifies constructor guard clauses on MainShellViewModel.
        // WHY:  Ensures mandatory dependencies cannot be null.
        // HOW:  Pass null for profileRepository and coordinator.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        Assert.Throws<ArgumentNullException>(() => new MainShellViewModel(null!, coordMock.Object));
        Assert.Throws<ArgumentNullException>(() => new MainShellViewModel(repoMock.Object, null!));
    }

    [Fact]
    public async Task MainShellViewModel_InitializeAsync_WithProfiles_SelectsFirstAndUpdatesSnapshot()
    {
        // WHAT: Verifies initialization with saved profiles and real MultiInstanceCoordinator.
        // WHY:  Ensures profiles and snapshots are loaded and the first instance is selected by default.
        // HOW:  Provide profiles in repository, call InitializeAsync, and assert selected state.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();
        var coordinator = new MultiInstanceCoordinator(repoMock.Object, nodeMock.Object);

        var p1 = new InstanceProfile("id-1", "Node Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB1", "ENV1", true, 15, DateTimeOffset.UtcNow, null);
        var p2 = new InstanceProfile("id-2", "Node Beta", "http://127.0.0.1:5001", true, "admin", "Administrator", "Ed25519", "PUB2", "ENV2", true, 15, DateTimeOffset.UtcNow, null);

        repoMock.Setup(r => r.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1, p2 });

        var vm = new MainShellViewModel(repoMock.Object, coordinator, nodeMock.Object);

        List<string> firedProperties = new();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                firedProperties.Add(args.PropertyName);
            }
        };

        await vm.InitializeAsync();

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal("id-1", vm.SelectedInstanceId);
        Assert.NotNull(vm.SelectedSnapshot);
        Assert.Equal("id-1", vm.SelectedSnapshot.InstanceId);
        Assert.Equal("Node Alpha", vm.SelectedSnapshot.DisplayName);
        Assert.Equal(InstanceConnectionState.Disconnected, vm.SelectedSnapshot.State);

        Assert.Contains(nameof(vm.Profiles), firedProperties);
        Assert.Contains(nameof(vm.Snapshots), firedProperties);
        Assert.Contains(nameof(vm.SelectedInstanceId), firedProperties);
        Assert.Contains(nameof(vm.SelectedSnapshot), firedProperties);
    }

    [Fact]
    public async Task MainShellViewModel_InitializeAsync_EmptyProfiles_And_NonMultiInstanceCoordinator()
    {
        // WHAT: Verifies initialization when repository is empty and coordinator is a generic mock.
        // WHY:  Ensures branch coverage for non-MultiInstanceCoordinator coordinator and empty profiles list.
        // HOW:  Provide empty profiles list from repository, call InitializeAsync, assert null selection.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        repoMock.Setup(r => r.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceProfile>());
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vm.InitializeAsync();

        Assert.Empty(vm.Profiles);
        Assert.Empty(vm.Snapshots);
        Assert.Null(vm.SelectedInstanceId);
        Assert.Null(vm.SelectedSnapshot);
    }

    [Fact]
    public void MainShellViewModel_SelectedInstanceId_ChangeAndNoChange()
    {
        // WHAT: Tests changing SelectedInstanceId updates SelectedSnapshot and fires notifications.
        // WHY:  Ensures switching instances updates active dashboard telemetry and state.
        // HOW:  Mutate SelectedInstanceId to new value, assert event; mutate to same value, assert no event.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        var snap1 = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        var snap2 = new InstanceConnectionSnapshot("id-2", "Beta", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null);

        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { snap1, snap2 });

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        List<string> firedProperties = new();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                firedProperties.Add(args.PropertyName);
            }
        };

        // Select id-1
        vm.SelectedInstanceId = "id-1";
        Assert.Equal("id-1", vm.SelectedInstanceId);
        Assert.Contains(nameof(vm.SelectedInstanceId), firedProperties);
        Assert.Contains(nameof(vm.SelectedSnapshot), firedProperties);

        // Select id-1 again (no change)
        firedProperties.Clear();
        vm.SelectedInstanceId = "id-1";
        Assert.Empty(firedProperties);

        // Select unknown instance ID with no profile
        firedProperties.Clear();
        vm.SelectedInstanceId = "unknown-id";
        Assert.Null(vm.SelectedSnapshot);
        Assert.Contains(nameof(vm.SelectedInstanceId), firedProperties);

        // Select null
        firedProperties.Clear();
        vm.SelectedInstanceId = null;
        Assert.Null(vm.SelectedSnapshot);
        Assert.Contains(nameof(vm.SelectedInstanceId), firedProperties);
    }

    [Fact]
    public async Task MainShellViewModel_ConnectSelectedInstanceAsync_ValidationFailures()
    {
        // WHAT: Tests validation guards in ConnectSelectedInstanceAsync.
        // WHY:  Ensures invalid state or missing credentials immediately return false.
        // HOW:  Test with null SelectedInstanceId, empty passphrase, whitespace passphrase, unknown profile.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        // SelectedInstanceId is null
        Assert.False(await vm.ConnectSelectedInstanceAsync("pass".ToCharArray()));

        // SelectedInstanceId set, but empty/whitespace passphrase
        vm.SelectedInstanceId = "id-1";
        Assert.False(await vm.ConnectSelectedInstanceAsync(ReadOnlyMemory<char>.Empty));
        Assert.False(await vm.ConnectSelectedInstanceAsync("   ".ToCharArray()));

        // Profile not in _profiles
        Assert.False(await vm.ConnectSelectedInstanceAsync("valid_pass".ToCharArray()));
    }

    [Fact]
    public async Task MainShellViewModel_ConnectSelectedInstanceAsync_Success_WithNodeClient()
    {
        // WHAT: Tests successful connection when nodeClient is provided and authenticates successfully.
        // WHY:  Verifies session token is cached for subsequent simulation campaigns.
        // HOW:  Setup mocks for coordinator and nodeClient, execute ConnectSelectedInstanceAsync, assert true.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        var onlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.ConnectInstanceAsync("id-1", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(onlineSnapshot);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { onlineSnapshot });

        nodeMock.Setup(n => n.AuthenticateAsync(p1, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("active_token", "Bearer", 3600, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "admin", "Administrator"));

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        await vm.InitializeAsync();

        bool connected = await vm.ConnectSelectedInstanceAsync("passphrase123".ToCharArray());

        Assert.True(connected);
        Assert.NotNull(vm.SelectedSnapshot);
        Assert.Equal(InstanceConnectionState.Online, vm.SelectedSnapshot.State);
    }

    [Fact]
    public async Task MainShellViewModel_ConnectSelectedInstanceAsync_Success_NodeClientAuthThrows_StillReturnsTrue()
    {
        // WHAT: Tests connection when nodeClient.AuthenticateAsync throws an exception.
        // WHY:  Verifies graceful swallowing of nodeClient error so snapshot state is still reflected.
        // HOW:  Make nodeMock.AuthenticateAsync throw HttpRequestException, assert method still returns true.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        var onlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.ConnectInstanceAsync("id-1", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(onlineSnapshot);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { onlineSnapshot });

        nodeMock.Setup(n => n.AuthenticateAsync(p1, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection reset"));

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        await vm.InitializeAsync();

        bool connected = await vm.ConnectSelectedInstanceAsync("pass".ToCharArray());
        Assert.True(connected);
    }

    [Fact]
    public async Task MainShellViewModel_ConnectSelectedInstanceAsync_OfflineSnapshot_ReturnsFalse()
    {
        // WHAT: Tests connection returning an Offline or Unauthorized snapshot.
        // WHY:  Ensures ConnectSelectedInstanceAsync returns false when node is not online.
        // HOW:  Make coordinator return Offline snapshot and assert false result.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        var offlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, "Unreachable");
        coordMock.Setup(c => c.ConnectInstanceAsync("id-1", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(offlineSnapshot);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { offlineSnapshot });

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vm.InitializeAsync();

        bool connected = await vm.ConnectSelectedInstanceAsync("pass".ToCharArray());
        Assert.False(connected);
    }

    [Fact]
    public async Task MainShellViewModel_ConnectSelectedInstanceAsync_CoordinatorThrows_ReturnsFalse()
    {
        // WHAT: Tests outer catch in ConnectSelectedInstanceAsync when coordinator throws.
        // WHY:  Ensures exceptions during handshake are caught and return false.
        // HOW:  Make coordinator.ConnectInstanceAsync throw InvalidOperationException.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        coordMock.Setup(c => c.ConnectInstanceAsync("id-1", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fatal handshake crash"));
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vm.InitializeAsync();

        bool connected = await vm.ConnectSelectedInstanceAsync("pass".ToCharArray());
        Assert.False(connected);
    }

    [Fact]
    public void MainShellViewModel_DisconnectSelectedInstance_CallsCoordinatorAndClearsState()
    {
        // WHAT: Verifies DisconnectSelectedInstance clears active session and notifies UI.
        // WHY:  Ensures manual disconnection triggers coordinator cleanup.
        // HOW:  Call DisconnectSelectedInstance with null ID (no-op), then with valid ID.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        // When SelectedInstanceId is null -> no-op
        vm.DisconnectSelectedInstance();
        coordMock.Verify(c => c.DisconnectInstance(It.IsAny<string>()), Times.Never);

        // When SelectedInstanceId is set
        vm.SelectedInstanceId = "id-1";
        vm.DisconnectSelectedInstance();
        coordMock.Verify(c => c.DisconnectInstance("id-1"), Times.Once);
    }

    [Fact]
    public async Task MainShellViewModel_TriggerSimulationOnSelectedAsync_ValidationFailures()
    {
        // WHAT: Verifies validation paths for TriggerSimulationOnSelectedAsync.
        // WHY:  Ensures simulations cannot be triggered without valid selection, simulation type, or online status.
        // HOW:  Test multiple combinations: null selected ID, empty type, null snapshot, offline snapshot, null nodeClient, missing token.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        var offlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { offlineSnapshot });

        // 1. null selected ID
        var vm1 = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        Assert.False(await vm1.TriggerSimulationOnSelectedAsync("serum-stability"));

        // 2. empty simulation type
        await vm1.InitializeAsync();
        Assert.False(await vm1.TriggerSimulationOnSelectedAsync(""));
        Assert.False(await vm1.TriggerSimulationOnSelectedAsync("   "));

        // 3. offline snapshot
        Assert.False(await vm1.TriggerSimulationOnSelectedAsync("serum-stability"));

        // 4. null nodeClient
        var vm2 = new MainShellViewModel(repoMock.Object, coordMock.Object, null);
        await vm2.InitializeAsync();
        Assert.False(await vm2.TriggerSimulationOnSelectedAsync("serum-stability"));

        // 5. online snapshot but no active token cached
        var onlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { onlineSnapshot });

        var vm3 = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        await vm3.InitializeAsync();
        Assert.False(await vm3.TriggerSimulationOnSelectedAsync("serum-stability"));

        // 6. online snapshot exists in coordinator, but profile is missing from repository profiles
        var orphanedSnapshot = new InstanceConnectionSnapshot("orphaned-id", "Orphan", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { orphanedSnapshot });
        var vm4 = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        await vm4.InitializeAsync();
        vm4.SelectedInstanceId = "orphaned-id";
        Assert.NotNull(vm4.SelectedSnapshot);
        Assert.Equal(InstanceConnectionState.Online, vm4.SelectedSnapshot.State);
        Assert.False(await vm4.TriggerSimulationOnSelectedAsync("serum-stability"));
    }

    [Fact]
    public async Task MainShellViewModel_TriggerSimulationOnSelectedAsync_Success_And_NodeException()
    {
        // WHAT: Tests successful simulation launch and error handling when node client fails.
        // WHY:  Verifies full workflow from connection to simulation trigger and exception handling.
        // HOW:  Connect to acquire token, call TriggerSimulationOnSelectedAsync, verify result.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB", "ENV", true, 15, DateTimeOffset.UtcNow, null);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        var onlineSnapshot = new InstanceConnectionSnapshot("id-1", "Alpha", InstanceConnectionState.Online, 10, 20, 30, 0, 40, DateTimeOffset.UtcNow, null);
        coordMock.Setup(c => c.ConnectInstanceAsync("id-1", It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(onlineSnapshot);
        coordMock.Setup(c => c.GetSnapshots()).Returns(new[] { onlineSnapshot });

        nodeMock.Setup(n => n.AuthenticateAsync(p1, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("valid_token", "Bearer", 3600, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "admin", "Administrator"));

        nodeMock.Setup(n => n.StartSimulationCampaignAsync(p1, "valid_token", "serum-stability", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        nodeMock.Setup(n => n.StartSimulationCampaignAsync(p1, "valid_token", "aptamer-gating", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        nodeMock.Setup(n => n.StartSimulationCampaignAsync(p1, "valid_token", "pbpk-endocrine", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulation engine busy"));

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        await vm.InitializeAsync();
        await vm.ConnectSelectedInstanceAsync("pass".ToCharArray());

        // Success
        bool started = await vm.TriggerSimulationOnSelectedAsync("serum-stability");
        Assert.True(started);

        // Returned false from client
        bool rejected = await vm.TriggerSimulationOnSelectedAsync("aptamer-gating");
        Assert.False(rejected);

        // Exception
        bool failed = await vm.TriggerSimulationOnSelectedAsync("pbpk-endocrine");
        Assert.False(failed);
    }

    [Fact]
    public async Task MainShellViewModel_RemoveInstanceAsync_ValidationAndSuccessPaths()
    {
        // WHAT: Tests RemoveInstanceAsync validation, deletion, and selection updates.
        // WHY:  Ensures deleting an instance cleans up state and updates the switcher selection.
        // HOW:  Test empty instanceId, failed deletion, deletion of selected instance, and deletion of unselected instance.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        var p1 = new InstanceProfile("id-1", "Alpha", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUB1", "ENV1", true, 15, DateTimeOffset.UtcNow, null);
        var p2 = new InstanceProfile("id-2", "Beta", "http://127.0.0.1:5001", true, "admin", "Administrator", "Ed25519", "PUB2", "ENV2", true, 15, DateTimeOffset.UtcNow, null);

        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1, p2 });
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vm.InitializeAsync();

        // 1. Empty / whitespace ID -> returns false
        Assert.False(await vm.RemoveInstanceAsync(""));
        Assert.False(await vm.RemoveInstanceAsync("   "));

        // 2. Repository returns false (profile not found in DB)
        repoMock.Setup(r => r.DeleteProfileAsync("unknown", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.False(await vm.RemoveInstanceAsync("unknown"));

        // 3. Delete unselected instance (id-2 when id-1 is selected)
        repoMock.Setup(r => r.DeleteProfileAsync("id-2", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p1 });

        bool removedUnselected = await vm.RemoveInstanceAsync("id-2");
        Assert.True(removedUnselected);
        Assert.Equal("id-1", vm.SelectedInstanceId);
        Assert.Single(vm.Profiles);

        // 4. Delete selected instance (id-1) when no profiles remain
        repoMock.Setup(r => r.DeleteProfileAsync("id-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InstanceProfile>());

        bool removedSelected = await vm.RemoveInstanceAsync("id-1");
        Assert.True(removedSelected);
        Assert.Null(vm.SelectedInstanceId);
        Assert.Empty(vm.Profiles);
    }

    [Fact]
    public void MainShellViewModel_Alerts_Add_Clear_And_CapCapacity()
    {
        // WHAT: Tests AddAlert, capacity capping at 100 alerts, and ClearAlerts.
        // WHY:  Ensures the alert feed behaves like a bounded FIFO history and notifies observers.
        // HOW:  Add null alert (throws ArgumentNullException), add 105 alerts, assert count is 100, then clear.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        // Null alert throws ArgumentNullException
        Assert.Throws<ArgumentNullException>(() => vm.AddAlert(null!));

        List<string> firedProperties = new();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                firedProperties.Add(args.PropertyName);
            }
        };

        // Add 105 alerts
        for (int i = 0; i < 105; i++)
        {
            vm.AddAlert(new InstanceAlertEvent(
                Guid.NewGuid().ToString("N"),
                "node-1",
                "Node 1",
                InstanceAlertSeverity.Info,
                $"Alert {i}",
                "Details",
                DateTimeOffset.UtcNow));
        }

        Assert.Equal(100, vm.AlertFeed.Count);
        Assert.Equal("Alert 104", vm.AlertFeed[0].Title);
        Assert.Contains(nameof(vm.AlertFeed), firedProperties);

        // Clear alerts
        firedProperties.Clear();
        vm.ClearAlerts();
        Assert.Empty(vm.AlertFeed);
        Assert.Contains(nameof(vm.AlertFeed), firedProperties);
    }

    [Fact]
    public async Task MainShellViewModel_PollAlertsAsync_ZeroAlerts_And_WithAlerts()
    {
        // WHAT: Tests PollAlertsAsync with 0 alerts and with new alerts.
        // WHY:  Verifies coordinator polling, feed prepending, capacity capping, and property notifications.
        // HOW:  Setup coordinator.PollAllConnectedAsync with empty and non-empty lists.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();

        // 1. Zero alerts
        coordMock.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InstanceAlertEvent>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        List<string> firedProperties = new();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                firedProperties.Add(args.PropertyName);
            }
        };

        int countZero = await vm.PollAlertsAsync();
        Assert.Equal(0, countZero);
        Assert.Empty(firedProperties);

        // 2. With alerts and exceeding capacity
        for (int i = 0; i < 100; i++)
        {
            vm.AddAlert(new InstanceAlertEvent(
                Guid.NewGuid().ToString("N"),
                "node-1",
                "Node 1",
                InstanceAlertSeverity.Info,
                $"Old Alert {i}",
                "Details",
                DateTimeOffset.UtcNow));
        }

        var alert1 = new InstanceAlertEvent("a1", "node-1", "Node 1", InstanceAlertSeverity.Warning, "High Temp", "86C", DateTimeOffset.UtcNow);
        var alert2 = new InstanceAlertEvent("a2", "node-1", "Node 1", InstanceAlertSeverity.Critical, "Offline", "Unreachable", DateTimeOffset.UtcNow);

        coordMock.Setup(c => c.PollAllConnectedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { alert1, alert2 });
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        firedProperties.Clear();
        int countTwo = await vm.PollAlertsAsync();
        Assert.Equal(2, countTwo);
        Assert.Equal(100, vm.AlertFeed.Count);
        Assert.Equal("High Temp", vm.AlertFeed[1].Title);
        Assert.Equal("Offline", vm.AlertFeed[0].Title);
        Assert.Contains(nameof(vm.AlertFeed), firedProperties);
        Assert.Contains(nameof(vm.Snapshots), firedProperties);
        Assert.Contains(nameof(vm.SelectedSnapshot), firedProperties);
    }

    /// <summary>
    /// Test double for <see cref="IClientKeyRingVault"/> supporting customizable return values and exceptions.
    /// </summary>
    private sealed class TestKeyRingVault : IClientKeyRingVault
    {
        /// <summary>
        /// Custom handler for GenerateKeyBundle.
        /// </summary>
        public Func<string, ReadOnlyMemory<char>, ClientKeyBundle>? GenerateHandler { get; set; }

        /// <summary>
        /// Custom handler for SealPrivateKeyPem.
        /// </summary>
        public Func<string, ReadOnlyMemory<char>, string>? SealHandler { get; set; }


        /// <inheritdoc/>
        public ClientKeyBundle GenerateKeyBundle(string keyAlgorithm, ReadOnlySpan<char> passphrase)
        {
            // WHAT: Dispatches to custom handler or default bundle.
            // WHY:  Permits testing of success and failure branches in ViewModels.
            // HOW:  Invokes GenerateHandler if present; otherwise returns default ClientKeyBundle.
            if (GenerateHandler != null)
            {
                return GenerateHandler(keyAlgorithm, passphrase.ToArray());
            }

            return new ClientKeyBundle(keyAlgorithm, "PUB_PEM", "ENV_B64", "FINGERPRINT");
        }

        /// <inheritdoc/>
        public string SealPrivateKeyPem(string privateKeyPem, ReadOnlySpan<char> passphrase)
        {
            // WHAT: Dispatches to custom handler or default sealed string.
            // WHY:  Permits testing of key import branches.
            // HOW:  Invokes SealHandler if present; otherwise returns "SEALED_ENVELOPE_B64".
            if (SealHandler != null)
            {
                return SealHandler(privateKeyPem, passphrase.ToArray());
            }

            return "SEALED_ENVELOPE_B64";
        }

        /// <inheritdoc/>
        public string SignChallenge(
            string keyAlgorithm,
            string encryptedEnvelopeBase64,
            ReadOnlySpan<char> passphrase,
            ReadOnlySpan<byte> challengeNonce)
        {
            // WHAT: Stubs challenge signing.
            // WHY:  Satisfies IClientChallengeSigner interface.
            // HOW:  Returns a dummy signature base64 string.
            return "DUMMY_SIGNATURE_BASE64";
        }
    }
}
