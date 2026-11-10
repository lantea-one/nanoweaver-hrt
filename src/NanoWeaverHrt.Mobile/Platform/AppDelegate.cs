using NanoWeaverHrt.Gui.Core.ViewModels;

namespace NanoWeaverHrt.Mobile.Platform;

/// <summary>
/// iOS and iPadOS platform lifecycle coordinator managing application delegate states,
/// scene foreground/background transitions, memory warnings, and Secure Enclave session locking.
/// </summary>
/// <remarks>
/// // WHAT: Headless iOS/iPadOS UIApplicationDelegate lifecycle host for Avalonia and native iOS mobile heads.
/// // WHY:  Issue #171 (P168): Enforces strict iOS lifecycle hygiene, scene backgrounding locks, and memory pressure handling.
/// // HOW:  Tracks lifecycle transitions (FinishedLaunching, OnActivated, OnResignActivation, DidEnterBackground),
/// //       triggers biometric vault and shell locking on background transition, and handles ReceiveMemoryWarning.
/// </remarks>
public sealed class AppDelegate
{
    /// <summary>
    /// Synchronized lock guarding thread-safe mutations of delegate lifecycle state.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// Indicates whether the application has completed initial launch bootstrapping.
    /// </summary>
    private bool _isFinishedLaunching;

    /// <summary>
    /// Indicates whether the application scene is currently active in the foreground.
    /// </summary>
    private bool _isActive;

    /// <summary>
    /// Indicates whether the application scene has entered the background.
    /// </summary>
    private bool _isBackgrounded;

    /// <summary>
    /// Attached biometric vault prompt ViewModel, if active.
    /// </summary>
    private VaultUnlockViewModel? _attachedVault;

    /// <summary>
    /// Attached main shell ViewModel, if active.
    /// </summary>
    private MainShellViewModel? _attachedShell;

    /// <summary>
    /// Gets a value indicating whether the application has finished launching.
    /// </summary>
    public bool IsFinishedLaunching
    {
        get
        {
            lock (_stateLock)
            {
                return _isFinishedLaunching;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the application is active in the foreground.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _isActive;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the application is currently backgrounded.
    /// </summary>
    public bool IsBackgrounded
    {
        get
        {
            lock (_stateLock)
            {
                return _isBackgrounded;
            }
        }
    }

    /// <summary>
    /// Gets the currently attached vault unlock ViewModel, if any.
    /// </summary>
    public VaultUnlockViewModel? AttachedVault => _attachedVault;

    /// <summary>
    /// Gets the currently attached main shell ViewModel, if any.
    /// </summary>
    public MainShellViewModel? AttachedShell => _attachedShell;

    /// <summary>
    /// Associates a <see cref="VaultUnlockViewModel"/> for automated session zeroization on background transition.
    /// </summary>
    /// <param name="vault">The vault unlock ViewModel instance to attach.</param>
    public void AttachVaultViewModel(VaultUnlockViewModel? vault)
    {
        // WHAT: Attaches a VaultUnlockViewModel reference.
        // WHY:  Allows automatic biometric prompt locking when iOS backgrounding occurs.
        // HOW:  Assigns internal field under thread-safe synchronization.
        lock (_stateLock)
        {
            _attachedVault = vault;
        }
    }

    /// <summary>
    /// Associates a <see cref="MainShellViewModel"/> for automated session locking on background transition.
    /// </summary>
    /// <param name="shell">The main shell ViewModel instance to attach.</param>
    public void AttachShellViewModel(MainShellViewModel? shell)
    {
        // WHAT: Attaches a MainShellViewModel reference.
        // WHY:  Enables automatic node disconnection and secret zeroization on backgrounding.
        // HOW:  Assigns internal field under thread-safe synchronization.
        lock (_stateLock)
        {
            _attachedShell = shell;
        }
    }

    /// <summary>
    /// Handles the iOS UIApplicationDelegate FinishedLaunching lifecycle transition.
    /// </summary>
    /// <returns>True indicating the application successfully configured its initial window and services.</returns>
    public bool FinishedLaunching()
    {
        // WHAT: Initializes application delegate state after process startup.
        // WHY:  Prepares the platform head for active user interaction and UI presentation.
        // HOW:  Sets _isFinishedLaunching and _isActive to true under lock.
        lock (_stateLock)
        {
            _isFinishedLaunching = true;
            _isActive = true;
            _isBackgrounded = false;
        }

        return true;
    }

    /// <summary>
    /// Handles the iOS UIApplicationDelegate OnActivated lifecycle transition when the scene becomes active.
    /// </summary>
    public void OnActivated()
    {
        // WHAT: Transitions application to the active foreground state.
        // WHY:  Indicates that the app is visible and accepting user touch events.
        // HOW:  Sets _isActive to true and _isBackgrounded to false under lock.
        lock (_stateLock)
        {
            _isActive = true;
            _isBackgrounded = false;
        }
    }

    /// <summary>
    /// Handles the iOS UIApplicationDelegate OnResignActivation lifecycle transition (e.g. incoming call, control center).
    /// </summary>
    public void OnResignActivation()
    {
        // WHAT: Transitions application to the inactive foreground state.
        // WHY:  Temporarily pauses interactive animations and disallows active input.
        // HOW:  Sets _isActive to false under lock.
        lock (_stateLock)
        {
            _isActive = false;
        }
    }

    /// <summary>
    /// Handles the iOS UIApplicationDelegate DidEnterBackground lifecycle transition, immediately locking the
    /// biometric vault, zeroizing session tokens, and disconnecting active instances.
    /// </summary>
    public void DidEnterBackground()
    {
        // WHAT: Transitions application to background state and locks active security vaults.
        // WHY:  Prime Directive 12: Zeroizes patient DNA and credentials immediately upon backgrounding.
        // HOW:  Sets _isActive to false, _isBackgrounded to true; dispatches MobileEntryPoint.OnBackground();
        //       invokes AttachedVault.Dispose() and AttachedShell.LockActiveSession().
        VaultUnlockViewModel? vaultToLock;
        MainShellViewModel? shellToLock;

        lock (_stateLock)
        {
            _isActive = false;
            _isBackgrounded = true;
            vaultToLock = _attachedVault;
            shellToLock = _attachedShell;
        }

        MobileEntryPoint.OnBackground();
        vaultToLock?.Dispose();
        shellToLock?.LockActiveSession();
    }

    /// <summary>
    /// Handles the iOS UIApplicationDelegate ReceiveMemoryWarning callback, purging non-essential caches.
    /// </summary>
    public void ReceiveMemoryWarning()
    {
        // WHAT: Responds to iOS system memory warnings.
        // WHY:  Prevents iOS watchdog termination by releasing transient heap allocations.
        // HOW:  Forces non-blocking garbage collection.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
    }
}
