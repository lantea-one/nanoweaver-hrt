namespace NanoWeaverHrt.Localization;

/// <summary>
/// Ambient culture context managing the active language and fallback hierarchy for the executing thread or task.
/// </summary>
public interface IWeaverCultureContext
{
    /// <summary>
    /// Gets or sets the active community language for the current asynchronous execution context.
    /// </summary>
    WeaverLanguage CurrentLanguage { get; set; }

    /// <summary>
    /// Gets the default fallback language used when a key or catalog is missing.
    /// </summary>
    WeaverLanguage FallbackLanguage { get; }
}
