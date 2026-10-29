namespace NanoWeaverHrt.Client.Storage;

using NanoWeaverHrt.Localization;

/// <summary>
/// Service managing persistent user language and locale preferences across desktop and mobile clients.
/// </summary>
public interface ILanguagePreferenceService
{
    /// <summary>
    /// Gets the currently configured language preference.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Configured language code (default: English).</returns>
    ValueTask<WeaverLanguage> GetPreferredLanguageAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the persistent language preference.
    /// </summary>
    /// <param name="language">Selected language.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    ValueTask SetPreferredLanguageAsync(WeaverLanguage language, CancellationToken ct = default);
}
