using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel coordinating the secure passphrase prompt and asynchronous vault unsealing workflow.
/// </summary>
/// <remarks>
/// // WHAT: Manages user passphrase input and credentials unsealing without persistent secret retention.
/// // WHY:  Issue #70 (P67): Encapsulates master passphrase unlock dialog state for Avalonia UI views.
/// // HOW:  Provides asynchronous unlock delegation, error reporting, zeroization on disposal, and PropertyChanged notifications.
/// </remarks>
public sealed class VaultUnlockViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// Target instance identifier whose vault is being unlocked.
    /// </summary>
    private string _instanceId = string.Empty;

    /// <summary>
    /// Friendly display name of target instance.
    /// </summary>
    private string _instanceDisplayName = string.Empty;

    /// <summary>
    /// In-memory passphrase buffer entered by operator.
    /// </summary>
    private string _passphrase = string.Empty;

    /// <summary>
    /// Indicates whether an unlocking operation is currently underway.
    /// </summary>
    private bool _isUnlocking;

    /// <summary>
    /// Descriptive error message if unlocking failed.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Indicates whether the vault was successfully unlocked.
    /// </summary>
    private bool _isUnlocked;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the target instance identifier.
    /// </summary>
    public string InstanceId
    {
        get => _instanceId;
        set
        {
            if (!string.Equals(_instanceId, value, StringComparison.Ordinal))
            {
                _instanceId = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the friendly display name of the instance.
    /// </summary>
    public string InstanceDisplayName
    {
        get => _instanceDisplayName;
        set
        {
            if (!string.Equals(_instanceDisplayName, value, StringComparison.Ordinal))
            {
                _instanceDisplayName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the passphrase string.
    /// </summary>
    public string Passphrase
    {
        get => _passphrase;
        set
        {
            if (!string.Equals(_passphrase, value, StringComparison.Ordinal))
            {
                _passphrase = value ?? string.Empty;
                ErrorMessage = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUnlock));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the passphrase is non-empty and unlock can be executed.
    /// </summary>
    public bool CanUnlock => !string.IsNullOrWhiteSpace(_passphrase) && !_isUnlocking;

    /// <summary>
    /// Gets a value indicating whether an unlocking attempt is currently in progress.
    /// </summary>
    public bool IsUnlocking
    {
        get => _isUnlocking;
        private set
        {
            if (_isUnlocking != value)
            {
                _isUnlocking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUnlock));
            }
        }
    }

    /// <summary>
    /// Gets the current error message, if any.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether an error is currently displayed.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    /// <summary>
    /// Gets a value indicating whether the vault is currently in an unlocked state.
    /// </summary>
    public bool IsUnlocked
    {
        get => _isUnlocked;
        private set
        {
            if (_isUnlocked != value)
            {
                _isUnlocked = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Executes the vault unlocking workflow by delegating the entered passphrase to the specified handler.
    /// </summary>
    /// <param name="unlockHandler">Asynchronous delegate executing the cryptographic unseal operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if unlocking succeeded; otherwise false.</returns>
    public async Task<bool> UnlockAsync(
        Func<ReadOnlyMemory<char>, Task<bool>> unlockHandler,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches entered passphrase to unlockHandler and updates UI feedback states.
        // WHY:  Decouples the passphrase prompt view model from concrete vault implementation.
        // HOW:  Validates passphrase, sets IsUnlocking, awaits handler, clears passphrase on success, sets error on failure.
        ArgumentNullException.ThrowIfNull(unlockHandler);

        if (string.IsNullOrWhiteSpace(_passphrase))
        {
            ErrorMessage = "Passphrase cannot be empty.";
            return false;
        }

        IsUnlocking = true;
        ErrorMessage = null;

        try
        {
            char[] chars = _passphrase.ToCharArray();
            bool success = await unlockHandler(chars.AsMemory()).ConfigureAwait(false);
            Array.Clear(chars, 0, chars.Length);

            if (success)
            {
                IsUnlocked = true;
                Clear();
                return true;
            }

            ErrorMessage = "Invalid passphrase. Failed to unseal cryptographic envelope.";
            IsUnlocked = false;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unlocking error: {ex.Message}";
            IsUnlocked = false;
            return false;
        }
        finally
        {
            IsUnlocking = false;
        }
    }

    /// <summary>
    /// Clears the passphrase text and resets error state.
    /// </summary>
    public void Clear()
    {
        // WHAT: Resets the passphrase buffer and error indicators.
        // WHY:  Prevents lingering passphrase characters in the UI control state.
        // HOW:  Sets _passphrase to string.Empty, clears ErrorMessage, and notifies observers.
        _passphrase = string.Empty;
        _errorMessage = null;
        OnPropertyChanged(nameof(Passphrase));
        OnPropertyChanged(nameof(CanUnlock));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Clears and zeroizes ViewModel state.
        // WHY:  Idempotent cleanup ensuring credentials are removed on dialog dismissal.
        // HOW:  Calls Clear and resets IsUnlocked.
        Clear();
        IsUnlocked = false;
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for the specified property name.
    /// </summary>
    /// <param name="propertyName">Name of the changed property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Dispatches property change notification.
        // WHY:  Notifies Avalonia UI bindings of state changes.
        // HOW:  Invokes PropertyChanged event handler.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
