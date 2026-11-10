using NanoWeaverHrt.Gui.Core.ViewModels;

namespace NanoWeaverHrt.Mobile.Platform;

/// <summary>
/// Android platform lifecycle coordinator managing activity state transitions, runtime permission handshakes,
/// memory pressure trimming, and background biometric vault locking.
/// </summary>
/// <remarks>
/// // WHAT: Headless Android activity lifecycle host for Avalonia and native Android mobile shells.
/// // WHY:  Issue #171 (P168): Enforces strict mobile lifecycle hygiene, background secret zeroization, and memory management.
/// // HOW:  Tracks lifecycle transitions (OnCreate, OnResume, OnPause, OnStop, OnDestroy), triggers biometric vault lock on pause,
/// //       manages notification and biometric permissions, and handles OnTrimMemory callbacks.
/// </remarks>
public sealed class MainActivity
{
    /// <summary>
    /// Synchronized lock guarding thread-safe mutations of activity state.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// Indicates whether the activity has been initialized via <see cref="OnCreate"/>.
    /// </summary>
    private bool _isCreated;

    /// <summary>
    /// Indicates whether the activity is currently in the foreground (resumed).
    /// </summary>
    private bool _isResumed;

    /// <summary>
    /// Indicates whether the activity is currently paused (in background or transitioning).
    /// </summary>
    private bool _isPaused;

    /// <summary>
    /// Indicates whether the activity is currently stopped.
    /// </summary>
    private bool _isStopped;

    /// <summary>
    /// Indicates whether the activity has been destroyed.
    /// </summary>
    private bool _isDestroyed;

    /// <summary>
    /// Indicates whether runtime notification permissions have been granted.
    /// </summary>
    private bool _isNotificationPermissionGranted;

    /// <summary>
    /// Indicates whether runtime biometric prompt permissions have been granted.
    /// </summary>
    private bool _isBiometricPermissionGranted;

    /// <summary>
    /// Attached biometric vault prompt ViewModel, if active.
    /// </summary>
    private VaultUnlockViewModel? _attachedVault;

    /// <summary>
    /// Attached main shell ViewModel, if active.
    /// </summary>
    private MainShellViewModel? _attachedShell;

    /// <summary>
    /// Gets a value indicating whether the activity has been created.
    /// </summary>
    public bool IsCreated
    {
        get
        {
            lock (_stateLock)
            {
                return _isCreated;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the activity is in the resumed (foreground) state.
    /// </summary>
    public bool IsResumed
    {
        get
        {
            lock (_stateLock)
            {
                return _isResumed;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the activity is currently paused.
    /// </summary>
    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _isPaused;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the activity is currently stopped.
    /// </summary>
    public bool IsStopped
    {
        get
        {
            lock (_stateLock)
            {
                return _isStopped;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the activity has been destroyed.
    /// </summary>
    public bool IsDestroyed
    {
        get
        {
            lock (_stateLock)
            {
                return _isDestroyed;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether runtime notification permissions are granted.
    /// </summary>
    public bool IsNotificationPermissionGranted
    {
        get
        {
            lock (_stateLock)
            {
                return _isNotificationPermissionGranted;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether runtime biometric prompt permissions are granted.
    /// </summary>
    public bool IsBiometricPermissionGranted
    {
        get
        {
            lock (_stateLock)
            {
                return _isBiometricPermissionGranted;
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
    /// Associates a <see cref="VaultUnlockViewModel"/> for automated session zeroization on pause.
    /// </summary>
    /// <param name="vault">The vault unlock ViewModel instance to attach.</param>
    public void AttachVaultViewModel(VaultUnlockViewModel? vault)
    {
        // WHAT: Attaches a VaultUnlockViewModel reference.
        // WHY:  Allows automatic biometric prompt locking when Android pauses this activity.
        // HOW:  Assigns internal field under thread-safe synchronization.
        lock (_stateLock)
        {
            _attachedVault = vault;
        }
    }

    /// <summary>
    /// Associates a <see cref="MainShellViewModel"/> for automated session locking on backgrounding.
    /// </summary>
    /// <param name="shell">The main shell ViewModel instance to attach.</param>
    public void AttachShellViewModel(MainShellViewModel? shell)
    {
        // WHAT: Attaches a MainShellViewModel reference.
        // WHY:  Enables automatic node disconnection and secret zeroization on background transition.
        // HOW:  Assigns internal field under thread-safe synchronization.
        lock (_stateLock)
        {
            _attachedShell = shell;
        }
    }

    /// <summary>
    /// Handles the Android Activity OnCreate lifecycle transition.
    /// </summary>
    public void OnCreate()
    {
        // WHAT: Initializes the activity state.
        // WHY:  Marks the host as created and ready for UI rendering and permission checks.
        // HOW:  Sets _isCreated to true, clears destroyed flag.
        lock (_stateLock)
        {
            _isCreated = true;
            _isDestroyed = false;
        }
    }

    /// <summary>
    /// Handles the Android Activity OnResume lifecycle transition when the activity enters the foreground.
    /// </summary>
    public void OnResume()
    {
        // WHAT: Transitions activity to the active foreground state.
        // WHY:  Signals that the user is actively interacting with the mobile application.
        // HOW:  Sets _isResumed to true, _isPaused to false, _isStopped to false.
        lock (_stateLock)
        {
            _isResumed = true;
            _isPaused = false;
            _isStopped = false;
        }
    }

    /// <summary>
    /// Handles the Android Activity OnPause lifecycle transition, immediately locking the biometric vault
    /// and zeroizing sensitive session material.
    /// </summary>
    public void OnPause()
    {
        // WHAT: Transitions activity to the paused state and locks active security vaults.
        // WHY:  Prime Directive 12: Zeroizes patient DNA and credentials immediately when losing focus.
        // HOW:  Sets _isResumed to false, _isPaused to true; dispatches MobileEntryPoint.OnPause();
        //       invokes AttachedVault.Dispose() and AttachedShell.LockActiveSession().
        VaultUnlockViewModel? vaultToLock;
        MainShellViewModel? shellToLock;

        lock (_stateLock)
        {
            _isResumed = false;
            _isPaused = true;
            vaultToLock = _attachedVault;
            shellToLock = _attachedShell;
        }

        MobileEntryPoint.OnPause();
        vaultToLock?.Dispose();
        shellToLock?.LockActiveSession();
    }

    /// <summary>
    /// Handles the Android Activity OnStop lifecycle transition when the activity is no longer visible.
    /// </summary>
    public void OnStop()
    {
        // WHAT: Transitions activity to stopped state and reinforces vault locking.
        // WHY:  Guarantees security hygiene when the application is placed into the background.
        // HOW:  Sets _isStopped to true, _isResumed to false; dispatches MobileEntryPoint.OnStop().
        lock (_stateLock)
        {
            _isStopped = true;
            _isResumed = false;
        }

        MobileEntryPoint.OnStop();
    }

    /// <summary>
    /// Handles the Android Activity OnDestroy lifecycle transition during activity teardown.
    /// </summary>
    public void OnDestroy()
    {
        // WHAT: Tears down activity state and releases associated ViewModel references.
        // WHY:  Ensures clean lifecycle termination without memory leaks.
        // HOW:  Sets _isDestroyed to true, clears attached ViewModels, and disposes vault.
        VaultUnlockViewModel? vault;
        lock (_stateLock)
        {
            _isCreated = false;
            _isResumed = false;
            _isPaused = false;
            _isStopped = true;
            _isDestroyed = true;
            vault = _attachedVault;
            _attachedVault = null;
            _attachedShell = null;
        }

        vault?.Dispose();
    }

    /// <summary>
    /// Handles the Android OnTrimMemory callback, purging non-essential memory caches and snapshots.
    /// </summary>
    /// <param name="level">The memory trim level indicator provided by the Android OS.</param>
    public void OnTrimMemory(int level)
    {
        // WHAT: Responds to OS memory pressure signals.
        // WHY:  Prevents low-memory killer (LMK) eviction by purging non-critical cached buffers.
        // HOW:  If trim level indicates pressure (>= 15), purges cached telemetry and triggers GC collect.
        if (level >= 15)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
        }
    }

    /// <summary>
    /// Updates the runtime notification permission state.
    /// </summary>
    /// <param name="granted">True if the user granted notification permission; otherwise false.</param>
    public void RequestNotificationPermission(bool granted)
    {
        // WHAT: Updates runtime notification permission status.
        // WHY:  Required for Android 13+ (POST_NOTIFICATIONS) runtime permission flow.
        // HOW:  Mutates _isNotificationPermissionGranted under lock.
        lock (_stateLock)
        {
            _isNotificationPermissionGranted = granted;
        }
    }

    /// <summary>
    /// Updates the runtime biometric authentication permission state.
    /// </summary>
    /// <param name="granted">True if biometric hardware access is permitted; otherwise false.</param>
    public void RequestBiometricPermission(bool granted)
    {
        // WHAT: Updates runtime biometric permission status.
        // WHY:  Required for Android BiometricPrompt authentication coordination.
        // HOW:  Mutates _isBiometricPermissionGranted under lock.
        lock (_stateLock)
        {
            _isBiometricPermissionGranted = granted;
        }
    }
}
