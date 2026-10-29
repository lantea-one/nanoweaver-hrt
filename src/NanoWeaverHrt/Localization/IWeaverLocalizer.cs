namespace NanoWeaverHrt.Localization;

/// <summary>
/// Thread-safe localizer providing translated UI strings and formatted messages.
/// </summary>
public interface IWeaverLocalizer
{
    /// <summary>
    /// Retrieves the localized string for the specified key in the target language.
    /// </summary>
    /// <param name="key">Unique dot-notated string key (e.g., 'Nav.EndocrineHub').</param>
    /// <param name="language">Target language.</param>
    /// <returns>Translated string, or English fallback if not found.</returns>
    string GetString(string key, WeaverLanguage language);

    /// <summary>
    /// Retrieves the localized string for the specified key formatted with arguments.
    /// </summary>
    /// <param name="key">Unique dot-notated string key.</param>
    /// <param name="language">Target language.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>Formatted translated string.</returns>
    string GetFormattedString(string key, WeaverLanguage language, params object[] args);

    /// <summary>
    /// Retrieves all supported languages and their metadata.
    /// </summary>
    /// <returns>Collection of supported languages.</returns>
    IReadOnlyList<WeaverLanguageInfo> GetSupportedLanguages();

    /// <summary>
    /// Parses a BCP-47 tag or language code into a <see cref="WeaverLanguage"/>, defaulting to English on unknown tags.
    /// </summary>
    /// <param name="tagOrCode">Language tag (e.g. 'es', 'es-ES', 'Spanish').</param>
    /// <returns>Resolved language enumeration.</returns>
    WeaverLanguage ResolveLanguage(string? tagOrCode);
}
