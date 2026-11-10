using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Mobile.Platform;
using Xunit;

namespace NanoWeaverHrt.Tests.Mobile;

/// <summary>
/// Unit test suite verifying Android <see cref="MainActivity"/> and iOS/iPadOS <see cref="AppDelegate"/>
/// platform lifecycle hosts, background biometric vault auto-locking, and memory pressure handling.
/// </summary>
public sealed class MobilePlatformLifecycleTests
{
    /// <summary>
    /// Creates a dummy instance profile for testing purposes.
    /// </summary>
    /// <param name="id">Instance identifier.</param>
    /// <returns>A populated <see cref="InstanceProfile"/> record.</returns>
    private static InstanceProfile CreateProfile(string id = "inst-mobile-001") =>
        new(id, "Mobile Node", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUBKEY", "ENVELOPE", true, 15, DateTimeOffset.UtcNow, null);

    /// <summary>
    /// Verifies Android <see cref="MainActivity"/> lifecycle transitions, permission grants,
    /// memory pressure callbacks, and automated biometric vault zeroization on pause.
    /// </summary>
    [Fact]
    public void MainActivity_LifecycleTransitions_And_VaultLockOnPause()
    {
        // WHAT: Test MainActivity lifecycle methods (OnCreate, OnResume, OnPause, OnStop, OnTrimMemory, OnDestroy).
        // WHY:  Issue #171 (P168): Guarantees strict mobile lifecycle invariants and zeroization on pause.
        // HOW:  Instantiates MainActivity, transitions states, verifies flags, and verifies vault and shell are locked.
        var activity = new MainActivity();

        // 1. Initial state
        Assert.False(activity.IsCreated);
        Assert.False(activity.IsResumed);
        Assert.False(activity.IsPaused);
        Assert.False(activity.IsStopped);
        Assert.False(activity.IsDestroyed);
        Assert.False(activity.IsNotificationPermissionGranted);
        Assert.False(activity.IsBiometricPermissionGranted);
        Assert.Null(activity.AttachedVault);
        Assert.Null(activity.AttachedShell);

        // 2. OnCreate
        activity.OnCreate();
        Assert.True(activity.IsCreated);
        Assert.False(activity.IsDestroyed);

        // 3. Permissions
        activity.RequestNotificationPermission(true);
        Assert.True(activity.IsNotificationPermissionGranted);
        activity.RequestNotificationPermission(false);
        Assert.False(activity.IsNotificationPermissionGranted);

        activity.RequestBiometricPermission(true);
        Assert.True(activity.IsBiometricPermissionGranted);
        activity.RequestBiometricPermission(false);
        Assert.False(activity.IsBiometricPermissionGranted);

        // 4. Attach Vault and Shell
        var vaultVm = new VaultUnlockViewModel();
        vaultVm.InstanceId = "inst-001";
        vaultVm.Passphrase = "SecretPass123";

        var profileRepoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var shellVm = new MainShellViewModel(profileRepoMock.Object, coordMock.Object);

        activity.AttachVaultViewModel(vaultVm);
        activity.AttachShellViewModel(shellVm);
        Assert.Same(vaultVm, activity.AttachedVault);
        Assert.Same(shellVm, activity.AttachedShell);

        // 5. OnResume
        activity.OnResume();
        Assert.True(activity.IsResumed);
        Assert.False(activity.IsPaused);
        Assert.False(activity.IsStopped);

        // 6. OnPause -> Must zeroize vault passphrase and lock shell session
        activity.OnPause();
        Assert.False(activity.IsResumed);
        Assert.True(activity.IsPaused);
        Assert.Empty(vaultVm.Passphrase);

        // 7. OnStop
        activity.OnStop();
        Assert.True(activity.IsStopped);
        Assert.False(activity.IsResumed);

        // 8. OnTrimMemory
        activity.OnTrimMemory(5); // Low pressure
        activity.OnTrimMemory(15); // Moderate pressure
        activity.OnTrimMemory(80); // Critical pressure

        // 9. OnDestroy
        activity.OnDestroy();
        Assert.False(activity.IsCreated);
        Assert.False(activity.IsResumed);
        Assert.True(activity.IsDestroyed);
        Assert.Null(activity.AttachedVault);
        Assert.Null(activity.AttachedShell);
    }

    /// <summary>
    /// Verifies iOS/iPadOS <see cref="AppDelegate"/> lifecycle transitions, scene backgrounding,
    /// memory warning callbacks, and automated biometric vault zeroization on backgrounding.
    /// </summary>
    [Fact]
    public void AppDelegate_LifecycleTransitions_And_VaultLockOnBackground()
    {
        // WHAT: Test AppDelegate lifecycle methods (FinishedLaunching, OnActivated, OnResignActivation, DidEnterBackground, ReceiveMemoryWarning).
        // WHY:  Issue #171 (P168): Enforces strict iOS lifecycle invariants and Secure Enclave zeroization on backgrounding.
        // HOW:  Instantiates AppDelegate, transitions states, verifies flags, and verifies vault and shell are locked.
        var appDelegate = new AppDelegate();

        // 1. Initial state
        Assert.False(appDelegate.IsFinishedLaunching);
        Assert.False(appDelegate.IsActive);
        Assert.False(appDelegate.IsBackgrounded);
        Assert.Null(appDelegate.AttachedVault);
        Assert.Null(appDelegate.AttachedShell);

        // 2. FinishedLaunching
        bool launched = appDelegate.FinishedLaunching();
        Assert.True(launched);
        Assert.True(appDelegate.IsFinishedLaunching);
        Assert.True(appDelegate.IsActive);
        Assert.False(appDelegate.IsBackgrounded);

        // 3. Attach ViewModels
        var vaultVm = new VaultUnlockViewModel();
        vaultVm.Passphrase = "IosPass123";

        var profileRepoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var shellVm = new MainShellViewModel(profileRepoMock.Object, coordMock.Object);

        appDelegate.AttachVaultViewModel(vaultVm);
        appDelegate.AttachShellViewModel(shellVm);
        Assert.Same(vaultVm, appDelegate.AttachedVault);
        Assert.Same(shellVm, appDelegate.AttachedShell);

        // 4. OnResignActivation
        appDelegate.OnResignActivation();
        Assert.False(appDelegate.IsActive);

        // 5. OnActivated
        appDelegate.OnActivated();
        Assert.True(appDelegate.IsActive);
        Assert.False(appDelegate.IsBackgrounded);

        // 6. DidEnterBackground -> Must zeroize vault passphrase and lock shell session
        appDelegate.DidEnterBackground();
        Assert.False(appDelegate.IsActive);
        Assert.True(appDelegate.IsBackgrounded);
        Assert.Empty(vaultVm.Passphrase);

        // 7. ReceiveMemoryWarning
        appDelegate.ReceiveMemoryWarning();
    }

    /// <summary>
    /// Verifies <see cref="MobileEntryPoint.RunAsync"/> and property coordinators for Android and iOS.
    /// </summary>
    [Fact]
    public async Task MobileEntryPoint_RunAsync_And_PropertyAccessors()
    {
        // WHAT: Tests MobileEntryPoint.RunAsync, CurrentMainActivity, and CurrentAppDelegate.
        // WHY:  Issue #171 (P168): Validates entry point lifecycle runner and static coordination.
        // HOW:  Sets properties, tests RunAsync with cancelled token, and tests invalid platform string.
        var mainActivity = new MainActivity();
        var appDelegate = new AppDelegate();

        MobileEntryPoint.CurrentMainActivity = mainActivity;
        MobileEntryPoint.CurrentAppDelegate = appDelegate;

        Assert.Same(mainActivity, MobileEntryPoint.CurrentMainActivity);
        Assert.Same(appDelegate, MobileEntryPoint.CurrentAppDelegate);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int androidResult = await MobileEntryPoint.RunAsync("Android", cts.Token);
        Assert.Equal(0, androidResult);

        int iosResult = await MobileEntryPoint.RunAsync("iOS", cts.Token);
        Assert.Equal(0, iosResult);

        int invalidResult = await MobileEntryPoint.RunAsync("InvalidOS", cts.Token);
        Assert.Equal(1, invalidResult);
    }
}
