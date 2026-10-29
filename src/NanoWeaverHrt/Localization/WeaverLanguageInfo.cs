namespace NanoWeaverHrt.Localization;

/// <summary>
/// Metadata describing a supported localization language.
/// </summary>
public sealed record WeaverLanguageInfo
{
    /// <summary>Enumeration code.</summary>
    public required WeaverLanguage Code { get; init; }

    /// <summary>BCP-47 culture tag (e.g., 'en-US', 'es-ES').</summary>
    public required string Bcp47Tag { get; init; }

    /// <summary>English display name (e.g., 'Spanish').</summary>
    public required string EnglishName { get; init; }

    /// <summary>Native autonym (e.g., 'Español').</summary>
    public required string NativeName { get; init; }
}
