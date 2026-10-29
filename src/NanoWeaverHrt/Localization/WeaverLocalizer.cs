using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace NanoWeaverHrt.Localization;

/// <summary>
/// Thread-safe localization provider loading embedded JSON catalogs and performing zero-allocation string lookups.
/// </summary>
public sealed class WeaverLocalizer : IWeaverLocalizer
{
    /// <summary>
    /// Metadata describing all 18 supported community languages.
    /// </summary>
    private static readonly WeaverLanguageInfo[] SupportedLanguages =
    [
        new() { Code = WeaverLanguage.English, Bcp47Tag = "en-US", EnglishName = "English", NativeName = "English" },
        new() { Code = WeaverLanguage.Spanish, Bcp47Tag = "es-ES", EnglishName = "Spanish", NativeName = "Español" },
        new() { Code = WeaverLanguage.French, Bcp47Tag = "fr-FR", EnglishName = "French", NativeName = "Français" },
        new() { Code = WeaverLanguage.German, Bcp47Tag = "de-DE", EnglishName = "German", NativeName = "Deutsch" },
        new() { Code = WeaverLanguage.Portuguese, Bcp47Tag = "pt-BR", EnglishName = "Portuguese", NativeName = "Português" },
        new() { Code = WeaverLanguage.Italian, Bcp47Tag = "it-IT", EnglishName = "Italian", NativeName = "Italiano" },
        new() { Code = WeaverLanguage.Japanese, Bcp47Tag = "ja-JP", EnglishName = "Japanese", NativeName = "日本語" },
        new() { Code = WeaverLanguage.Chinese, Bcp47Tag = "zh-CN", EnglishName = "Chinese", NativeName = "简体中文" },
        new() { Code = WeaverLanguage.Dutch, Bcp47Tag = "nl-NL", EnglishName = "Dutch", NativeName = "Nederlands" },
        new() { Code = WeaverLanguage.Polish, Bcp47Tag = "pl-PL", EnglishName = "Polish", NativeName = "Polski" },
        new() { Code = WeaverLanguage.Arabic, Bcp47Tag = "ar-SA", EnglishName = "Arabic", NativeName = "العربية", IsRightToLeft = true },
        new() { Code = WeaverLanguage.Korean, Bcp47Tag = "ko-KR", EnglishName = "Korean", NativeName = "한국어" },
        new() { Code = WeaverLanguage.Turkish, Bcp47Tag = "tr-TR", EnglishName = "Turkish", NativeName = "Türkçe" },
        new() { Code = WeaverLanguage.Hindi, Bcp47Tag = "hi-IN", EnglishName = "Hindi", NativeName = "हिन्दी" },
        new() { Code = WeaverLanguage.Ukrainian, Bcp47Tag = "uk-UA", EnglishName = "Ukrainian", NativeName = "Українська" },
        new() { Code = WeaverLanguage.Vietnamese, Bcp47Tag = "vi-VN", EnglishName = "Vietnamese", NativeName = "Tiếng Việt" },
        new() { Code = WeaverLanguage.Indonesian, Bcp47Tag = "id-ID", EnglishName = "Indonesian", NativeName = "Bahasa Indonesia" },
        new() { Code = WeaverLanguage.Swedish, Bcp47Tag = "sv-SE", EnglishName = "Swedish", NativeName = "Svenska" },
    ];

    /// <summary>
    /// Case-insensitive mapping table resolving BCP-47 tags, ISO 639-1 prefixes, and language names to <see cref="WeaverLanguage"/>.
    /// </summary>
    private static readonly FrozenDictionary<string, WeaverLanguage> LanguageLookup = BuildLanguageLookup();

    /// <summary>
    /// In-memory catalog storage mapping language enum values to frozen string dictionaries.
    /// </summary>
    private readonly FrozenDictionary<WeaverLanguage, FrozenDictionary<string, string>> _catalogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverLocalizer"/> class using the default assembly resources.
    /// </summary>
    public WeaverLocalizer() : this(typeof(WeaverLocalizer).Assembly)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverLocalizer"/> class using the specified assembly containing embedded resources.
    /// </summary>
    /// <param name="assembly">The assembly to scan for embedded JSON localization catalogs.</param>
    internal WeaverLocalizer(Assembly assembly)
    {
        // WHAT: Guard clause ensuring the assembly reference is valid.
        // WHY:  Prevent null reference errors during catalog initialization.
        // HOW:  Throws ArgumentNullException if assembly is null.
        ArgumentNullException.ThrowIfNull(assembly);

        // WHAT: Loads and caches embedded JSON string catalogs into immutable frozen dictionaries.
        // WHY:  Guarantees zero-allocation O(1) concurrent lookups on hot request paths.
        // HOW:  Delegates to LoadCatalogs.
        _catalogs = LoadCatalogs(assembly);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverLocalizer"/> class with explicit pre-built catalogs for unit testing.
    /// </summary>
    /// <param name="catalogs">Pre-built language catalogs.</param>
    internal WeaverLocalizer(FrozenDictionary<WeaverLanguage, FrozenDictionary<string, string>> catalogs)
    {
        // WHAT: Assigns explicit pre-built catalogs directly.
        // WHY:  Enables fine-grained testing of fallback boundaries and missing-catalog edge cases.
        // HOW:  Stores the provided dictionary after null checking.
        ArgumentNullException.ThrowIfNull(catalogs);
        _catalogs = catalogs;
    }

    /// <summary>
    /// Builds the lookup dictionary mapping various language representations to <see cref="WeaverLanguage"/>.
    /// </summary>
    /// <returns>A frozen dictionary configured with case-insensitive ordinal string comparison.</returns>
    private static FrozenDictionary<string, WeaverLanguage> BuildLanguageLookup()
    {
        // WHAT: Builds a multi-key lookup index for language resolution.
        // WHY:  Allows clients to resolve languages via BCP-47 tags (en-US), ISO codes (en), or autonyms (Español).
        // HOW:  Populates a dictionary with case-insensitive ordinal keys and converts it to a FrozenDictionary.
        var dict = new Dictionary<string, WeaverLanguage>(StringComparer.OrdinalIgnoreCase);

        foreach (WeaverLanguageInfo info in SupportedLanguages)
        {
            dict[info.Bcp47Tag] = info.Code;
            dict[info.Bcp47Tag.Replace('-', '_')] = info.Code;

            string isoPrefix = info.Bcp47Tag.Split('-')[0];
            dict[isoPrefix] = info.Code;

            dict[info.Code.ToString()] = info.Code;
            dict[info.EnglishName] = info.Code;
            dict[info.NativeName] = info.Code;
        }

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads embedded JSON localization resource catalogs from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan for embedded resources.</param>
    /// <returns>A frozen dictionary mapping each language to its loaded string table.</returns>
    private static FrozenDictionary<WeaverLanguage, FrozenDictionary<string, string>> LoadCatalogs(Assembly assembly)
    {
        // WHAT: Discovers and deserializes embedded JSON string catalogs from the assembly.
        // WHY:  Bundles language resources into the compiled binary without runtime filesystem dependencies.
        // HOW:  Inspects manifest resource names, filters by language BCP-47 suffix, and parses JSON content.
        var map = new Dictionary<WeaverLanguage, FrozenDictionary<string, string>>();
        string[] manifestResourceNames = assembly.GetManifestResourceNames();

        foreach (WeaverLanguageInfo info in SupportedLanguages)
        {
            string expectedSuffix = $"strings.{info.Bcp47Tag}.json";
            string? resourceName = manifestResourceNames.FirstOrDefault(
                name => name.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase));

            FrozenDictionary<string, string> stringTable = FrozenDictionary<string, string>.Empty;

            if (resourceName != null)
            {
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    Dictionary<string, string>? deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                    if (deserialized != null)
                    {
                        stringTable = deserialized.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            map[info.Code] = stringTable;
        }

        return map.ToFrozenDictionary();
    }

    /// <summary>
    /// Retrieves the localized string for the specified key in the target language.
    /// </summary>
    /// <param name="key">Unique dot-notated string key (e.g., 'Nav.EndocrineHub').</param>
    /// <param name="language">Target language.</param>
    /// <returns>Translated string, or English fallback if not found.</returns>
    public string GetString(string key, WeaverLanguage language)
    {
        // WHAT: Resolves a localized string using a resilient multi-tier fallback mechanism.
        // WHY:  Guarantees zero exceptions (RFC 7807 compliance) and seamless degradation to English or raw key.
        // HOW:  1. Return empty string on null or empty key.
        //       2. Lookup in target language catalog.
        //       3. If missing and language is not English, lookup in English catalog.
        //       4. Fallback to raw key if not found in any catalog.
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_catalogs.TryGetValue(language, out FrozenDictionary<string, string>? langCatalog) &&
            langCatalog.TryGetValue(key, out string? localizedValue))
        {
            return localizedValue;
        }

        if (language != WeaverLanguage.English &&
            _catalogs.TryGetValue(WeaverLanguage.English, out FrozenDictionary<string, string>? enCatalog) &&
            enCatalog.TryGetValue(key, out string? enValue))
        {
            return enValue;
        }

        return key;
    }

    /// <summary>
    /// Retrieves the localized string for the specified key formatted with arguments.
    /// </summary>
    /// <param name="key">Unique dot-notated string key.</param>
    /// <param name="language">Target language.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>Formatted translated string.</returns>
    public string GetFormattedString(string key, WeaverLanguage language, params object[] args)
    {
        // WHAT: Formats a localized template string using the supplied positional arguments.
        // WHY:  Supports dynamic interpolation for parameterized messages while preventing formatting exceptions.
        // HOW:  Retrieves the template via GetString, validates arguments, formats with InvariantCulture, and catches FormatException.
        string template = GetString(key, language);
        if (args == null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Retrieves all supported languages and their metadata.
    /// </summary>
    /// <returns>Collection of supported languages.</returns>
    public IReadOnlyList<WeaverLanguageInfo> GetSupportedLanguages()
    {
        // WHAT: Exposes the full list of supported community languages and metadata.
        // WHY:  Supplies UI selectors and client applications with available localization options.
        // HOW:  Returns the static array of WeaverLanguageInfo instances.
        return SupportedLanguages;
    }

    /// <summary>
    /// Parses a BCP-47 tag or language code into a <see cref="WeaverLanguage"/>, defaulting to English on unknown tags.
    /// </summary>
    /// <param name="tagOrCode">Language tag (e.g. 'es', 'es-ES', 'Spanish').</param>
    /// <returns>Resolved language enumeration.</returns>
    public WeaverLanguage ResolveLanguage(string? tagOrCode)
    {
        // WHAT: Resolves a string identifier to its corresponding WeaverLanguage enum.
        // WHY:  Normalizes input from HTTP Accept-Language headers, cookies, query parameters, and configuration files.
        // HOW:  Trims the input and probes the pre-built case-insensitive lookup table, defaulting to English.
        if (string.IsNullOrWhiteSpace(tagOrCode))
        {
            return WeaverLanguage.English;
        }

        if (LanguageLookup.TryGetValue(tagOrCode.Trim(), out WeaverLanguage matched))
        {
            return matched;
        }

        return WeaverLanguage.English;
    }
}
