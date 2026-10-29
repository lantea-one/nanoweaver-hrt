namespace NanoWeaverHrt.Localization;

/// <summary>
/// Thread-safe ambient culture context managing the current community language across asynchronous task flows.
/// </summary>
public sealed class WeaverCultureContext : IWeaverCultureContext
{
    // WHAT: AsyncLocal cell storing the active language.
    // WHY:  Preserves language selection across async/await execution contexts without cross-thread contamination.
    // HOW:  Stores an instance of WeaverLanguage in AsyncLocal<WeaverLanguage>.
    private static readonly AsyncLocal<WeaverLanguage> _currentLanguage = new();

    /// <summary>
    /// Gets or sets the active community language for the current asynchronous execution context.
    /// </summary>
    public WeaverLanguage CurrentLanguage
    {
        // WHAT: Retrieves the ambient language from the async local storage cell.
        // WHY:  Maintains context across async/await boundaries without thread-locking.
        // HOW:  Reads the Value property of the AsyncLocal cell.
        get => _currentLanguage.Value;

        // WHAT: Updates the ambient language for the current asynchronous execution flow.
        // WHY:  Allows scoped culture switching per request or UI session.
        // HOW:  Assigns the Value property of the AsyncLocal cell.
        set => _currentLanguage.Value = value;
    }

    /// <summary>
    /// Gets the default fallback language (always <see cref="WeaverLanguage.English"/>).
    /// </summary>
    public WeaverLanguage FallbackLanguage => WeaverLanguage.English;
}
