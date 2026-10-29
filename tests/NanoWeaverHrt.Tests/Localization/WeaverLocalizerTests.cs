using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text;
using Moq;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests.Localization;

/// <summary>
/// Unit tests for <see cref="WeaverLocalizer"/>, <see cref="WeaverCultureContext"/>, and localization domain models.
/// </summary>
public sealed class WeaverLocalizerTests
{
    /// <summary>
    /// Verifies that constructing <see cref="WeaverLanguageInfo"/> sets and returns all metadata properties.
    /// </summary>
    [Fact]
    public void WeaverLanguageInfo_Properties_ReturnExpectedValues()
    {
        // WHAT: Tests initialization and read-back of WeaverLanguageInfo record properties.
        // WHY:  Ensures data contract integrity across language metadata descriptors.
        // HOW:  Instantiates a WeaverLanguageInfo and asserts each property value.
        var info = new WeaverLanguageInfo
        {
            Code = WeaverLanguage.French,
            Bcp47Tag = "fr-FR",
            EnglishName = "French",
            NativeName = "Français"
        };

        Assert.Equal(WeaverLanguage.French, info.Code);
        Assert.Equal("fr-FR", info.Bcp47Tag);
        Assert.Equal("French", info.EnglishName);
        Assert.Equal("Français", info.NativeName);
        Assert.False(info.IsRightToLeft);

        var arabicInfo = new WeaverLanguageInfo
        {
            Code = WeaverLanguage.Arabic,
            Bcp47Tag = "ar-SA",
            EnglishName = "Arabic",
            NativeName = "العربية",
            IsRightToLeft = true
        };

        Assert.Equal(WeaverLanguage.Arabic, arabicInfo.Code);
        Assert.Equal("ar-SA", arabicInfo.Bcp47Tag);
        Assert.Equal("Arabic", arabicInfo.EnglishName);
        Assert.Equal("العربية", arabicInfo.NativeName);
        Assert.True(arabicInfo.IsRightToLeft);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverCultureContext"/> manages ambient language state and default fallback.
    /// </summary>
    [Fact]
    public void WeaverCultureContext_StateAndFallback_OperateCorrectly()
    {
        // WHAT: Tests ambient language assignment and fallback rule in WeaverCultureContext.
        // WHY:  Validates that asynchronous execution contexts track community language selection accurately.
        // HOW:  Gets and sets CurrentLanguage and verifies FallbackLanguage is English.
        var context = new WeaverCultureContext();

        Assert.Equal(WeaverLanguage.English, context.FallbackLanguage);

        context.CurrentLanguage = WeaverLanguage.German;
        Assert.Equal(WeaverLanguage.German, context.CurrentLanguage);

        context.CurrentLanguage = WeaverLanguage.Spanish;
        Assert.Equal(WeaverLanguage.Spanish, context.CurrentLanguage);
    }

    /// <summary>
    /// Verifies that passing a null assembly to <see cref="WeaverLocalizer"/> constructor throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullAssembly_ThrowsArgumentNullException()
    {
        // WHAT: Tests argument validation on assembly constructor.
        // WHY:  Prevent null reference exceptions during catalog loading.
        // HOW:  Asserts ArgumentNullException is thrown when null assembly is supplied.
        Assert.Throws<ArgumentNullException>(() => new WeaverLocalizer((Assembly)null!));
    }

    /// <summary>
    /// Verifies that passing null catalogs to <see cref="WeaverLocalizer"/> constructor throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullCatalogs_ThrowsArgumentNullException()
    {
        // WHAT: Tests argument validation on pre-built catalogs constructor.
        // WHY:  Guarantees internal dictionary invariants cannot be initialized to null.
        // HOW:  Asserts ArgumentNullException is thrown when null catalog map is supplied.
        Assert.Throws<ArgumentNullException>(() => new WeaverLocalizer((FrozenDictionary<WeaverLanguage, FrozenDictionary<string, string>>)null!));
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.GetSupportedLanguages"/> returns all 18 community languages.
    /// </summary>
    [Fact]
    public void GetSupportedLanguages_ReturnsAllEighteenCommunityLanguages()
    {
        // WHAT: Validates the list of supported community languages.
        // WHY:  Ensures full community coverage across the 18 target locales.
        // HOW:  Calls GetSupportedLanguages and verifies count and item values.
        var localizer = new WeaverLocalizer();
        IReadOnlyList<WeaverLanguageInfo> languages = localizer.GetSupportedLanguages();

        Assert.Equal(18, languages.Count);

        var codes = languages.Select(l => l.Code).ToHashSet();
        Assert.Contains(WeaverLanguage.English, codes);
        Assert.Contains(WeaverLanguage.Spanish, codes);
        Assert.Contains(WeaverLanguage.French, codes);
        Assert.Contains(WeaverLanguage.German, codes);
        Assert.Contains(WeaverLanguage.Portuguese, codes);
        Assert.Contains(WeaverLanguage.Italian, codes);
        Assert.Contains(WeaverLanguage.Japanese, codes);
        Assert.Contains(WeaverLanguage.Chinese, codes);
        Assert.Contains(WeaverLanguage.Dutch, codes);
        Assert.Contains(WeaverLanguage.Polish, codes);
        Assert.Contains(WeaverLanguage.Arabic, codes);
        Assert.Contains(WeaverLanguage.Korean, codes);
        Assert.Contains(WeaverLanguage.Turkish, codes);
        Assert.Contains(WeaverLanguage.Hindi, codes);
        Assert.Contains(WeaverLanguage.Ukrainian, codes);
        Assert.Contains(WeaverLanguage.Vietnamese, codes);
        Assert.Contains(WeaverLanguage.Indonesian, codes);
        Assert.Contains(WeaverLanguage.Swedish, codes);

        foreach (WeaverLanguageInfo lang in languages)
        {
            Assert.False(string.IsNullOrWhiteSpace(lang.Bcp47Tag));
            Assert.False(string.IsNullOrWhiteSpace(lang.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(lang.NativeName));
        }
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.ResolveLanguage"/> resolves diverse tag and code representations correctly.
    /// </summary>
    /// <param name="input">Language tag or code input.</param>
    /// <param name="expected">Expected WeaverLanguage resolution.</param>
    [Theory]
    [InlineData(null, WeaverLanguage.English)]
    [InlineData("", WeaverLanguage.English)]
    [InlineData("   ", WeaverLanguage.English)]
    [InlineData("unknown-tag", WeaverLanguage.English)]
    [InlineData("en-US", WeaverLanguage.English)]
    [InlineData("EN-us", WeaverLanguage.English)]
    [InlineData("en_US", WeaverLanguage.English)]
    [InlineData("en", WeaverLanguage.English)]
    [InlineData("English", WeaverLanguage.English)]
    [InlineData("es-ES", WeaverLanguage.Spanish)]
    [InlineData("es_ES", WeaverLanguage.Spanish)]
    [InlineData("es", WeaverLanguage.Spanish)]
    [InlineData("Spanish", WeaverLanguage.Spanish)]
    [InlineData("Español", WeaverLanguage.Spanish)]
    [InlineData("fr-FR", WeaverLanguage.French)]
    [InlineData("fr_FR", WeaverLanguage.French)]
    [InlineData("fr", WeaverLanguage.French)]
    [InlineData("French", WeaverLanguage.French)]
    [InlineData("Français", WeaverLanguage.French)]
    [InlineData("de-DE", WeaverLanguage.German)]
    [InlineData("de_DE", WeaverLanguage.German)]
    [InlineData("de", WeaverLanguage.German)]
    [InlineData("German", WeaverLanguage.German)]
    [InlineData("Deutsch", WeaverLanguage.German)]
    [InlineData("pt-BR", WeaverLanguage.Portuguese)]
    [InlineData("pt_BR", WeaverLanguage.Portuguese)]
    [InlineData("pt", WeaverLanguage.Portuguese)]
    [InlineData("Portuguese", WeaverLanguage.Portuguese)]
    [InlineData("Português", WeaverLanguage.Portuguese)]
    [InlineData("it-IT", WeaverLanguage.Italian)]
    [InlineData("it_IT", WeaverLanguage.Italian)]
    [InlineData("it", WeaverLanguage.Italian)]
    [InlineData("Italian", WeaverLanguage.Italian)]
    [InlineData("Italiano", WeaverLanguage.Italian)]
    [InlineData("ja-JP", WeaverLanguage.Japanese)]
    [InlineData("ja_JP", WeaverLanguage.Japanese)]
    [InlineData("ja", WeaverLanguage.Japanese)]
    [InlineData("Japanese", WeaverLanguage.Japanese)]
    [InlineData("日本語", WeaverLanguage.Japanese)]
    [InlineData("zh-CN", WeaverLanguage.Chinese)]
    [InlineData("zh_CN", WeaverLanguage.Chinese)]
    [InlineData("zh", WeaverLanguage.Chinese)]
    [InlineData("Chinese", WeaverLanguage.Chinese)]
    [InlineData("简体中文", WeaverLanguage.Chinese)]
    [InlineData("nl-NL", WeaverLanguage.Dutch)]
    [InlineData("nl_NL", WeaverLanguage.Dutch)]
    [InlineData("nl", WeaverLanguage.Dutch)]
    [InlineData("Dutch", WeaverLanguage.Dutch)]
    [InlineData("Nederlands", WeaverLanguage.Dutch)]
    [InlineData("pl-PL", WeaverLanguage.Polish)]
    [InlineData("pl_PL", WeaverLanguage.Polish)]
    [InlineData("pl", WeaverLanguage.Polish)]
    [InlineData("Polish", WeaverLanguage.Polish)]
    [InlineData("Polski", WeaverLanguage.Polish)]
    [InlineData("ar-SA", WeaverLanguage.Arabic)]
    [InlineData("ar_SA", WeaverLanguage.Arabic)]
    [InlineData("ar", WeaverLanguage.Arabic)]
    [InlineData("Arabic", WeaverLanguage.Arabic)]
    [InlineData("العربية", WeaverLanguage.Arabic)]
    [InlineData("ko-KR", WeaverLanguage.Korean)]
    [InlineData("ko_KR", WeaverLanguage.Korean)]
    [InlineData("ko", WeaverLanguage.Korean)]
    [InlineData("Korean", WeaverLanguage.Korean)]
    [InlineData("한국어", WeaverLanguage.Korean)]
    [InlineData("tr-TR", WeaverLanguage.Turkish)]
    [InlineData("tr_TR", WeaverLanguage.Turkish)]
    [InlineData("tr", WeaverLanguage.Turkish)]
    [InlineData("Turkish", WeaverLanguage.Turkish)]
    [InlineData("Türkçe", WeaverLanguage.Turkish)]
    [InlineData("hi-IN", WeaverLanguage.Hindi)]
    [InlineData("hi_IN", WeaverLanguage.Hindi)]
    [InlineData("hi", WeaverLanguage.Hindi)]
    [InlineData("Hindi", WeaverLanguage.Hindi)]
    [InlineData("हिन्दी", WeaverLanguage.Hindi)]
    [InlineData("uk-UA", WeaverLanguage.Ukrainian)]
    [InlineData("uk_UA", WeaverLanguage.Ukrainian)]
    [InlineData("uk", WeaverLanguage.Ukrainian)]
    [InlineData("Ukrainian", WeaverLanguage.Ukrainian)]
    [InlineData("Українська", WeaverLanguage.Ukrainian)]
    [InlineData("vi-VN", WeaverLanguage.Vietnamese)]
    [InlineData("vi_VN", WeaverLanguage.Vietnamese)]
    [InlineData("vi", WeaverLanguage.Vietnamese)]
    [InlineData("Vietnamese", WeaverLanguage.Vietnamese)]
    [InlineData("Tiếng Việt", WeaverLanguage.Vietnamese)]
    [InlineData("id-ID", WeaverLanguage.Indonesian)]
    [InlineData("id_ID", WeaverLanguage.Indonesian)]
    [InlineData("id", WeaverLanguage.Indonesian)]
    [InlineData("Indonesian", WeaverLanguage.Indonesian)]
    [InlineData("Bahasa Indonesia", WeaverLanguage.Indonesian)]
    [InlineData("sv-SE", WeaverLanguage.Swedish)]
    [InlineData("sv_SE", WeaverLanguage.Swedish)]
    [InlineData("sv", WeaverLanguage.Swedish)]
    [InlineData("Swedish", WeaverLanguage.Swedish)]
    [InlineData("Svenska", WeaverLanguage.Swedish)]
    public void ResolveLanguage_VariousInputs_ResolvesExpectedLanguage(string? input, WeaverLanguage expected)
    {
        // WHAT: Tests tag normalization and lookup resolution for multiple language aliases.
        // WHY:  Guarantees robust parsing of culture tags from browser headers, tokens, and settings.
        // HOW:  Calls ResolveLanguage with input and compares against expected WeaverLanguage.
        var localizer = new WeaverLocalizer();
        WeaverLanguage actual = localizer.ResolveLanguage(input);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that all 18 standard localization keys exist and return non-empty strings across all 18 community languages.
    /// </summary>
    /// <param name="language">Target community language.</param>
    [Theory]
    [InlineData(WeaverLanguage.English)]
    [InlineData(WeaverLanguage.Spanish)]
    [InlineData(WeaverLanguage.French)]
    [InlineData(WeaverLanguage.German)]
    [InlineData(WeaverLanguage.Portuguese)]
    [InlineData(WeaverLanguage.Italian)]
    [InlineData(WeaverLanguage.Japanese)]
    [InlineData(WeaverLanguage.Chinese)]
    [InlineData(WeaverLanguage.Dutch)]
    [InlineData(WeaverLanguage.Polish)]
    [InlineData(WeaverLanguage.Arabic)]
    [InlineData(WeaverLanguage.Korean)]
    [InlineData(WeaverLanguage.Turkish)]
    [InlineData(WeaverLanguage.Hindi)]
    [InlineData(WeaverLanguage.Ukrainian)]
    [InlineData(WeaverLanguage.Vietnamese)]
    [InlineData(WeaverLanguage.Indonesian)]
    [InlineData(WeaverLanguage.Swedish)]
    public void GetString_AllKeysAcrossAllLanguages_ReturnNonEmptyTranslations(WeaverLanguage language)
    {
        // WHAT: Validates presence of all 18 standard navigation, regimen, alert, and status keys.
        // WHY:  Ensures completeness of embedded JSON catalogs for every supported language.
        // HOW:  Iterates over required keys and asserts translated values are non-empty and not equal to the key.
        var localizer = new WeaverLocalizer();
        string[] requiredKeys =
        [
            "Nav.Home",
            "Nav.Endocrine",
            "Nav.Wizard",
            "Nav.Simulations",
            "Nav.Genomics",
            "Nav.Compliance",
            "Nav.Datasets",
            "Nav.Language",
            "Regimen.MtF",
            "Regimen.FtM",
            "Regimen.E2Dominant",
            "Regimen.TDominant",
            "Alert.IdleTimeout",
            "Alert.CryptoShredded",
            "Alert.TamperDetected",
            "Status.Online",
            "Status.Offline",
            "Status.Connecting"
        ];

        foreach (string key in requiredKeys)
        {
            string translation = localizer.GetString(key, language);
            Assert.False(string.IsNullOrWhiteSpace(translation), $"Key '{key}' was empty in language '{language}'.");
            Assert.NotEqual(key, translation);
        }
    }

    /// <summary>
    /// Verifies specific key translations for representative languages to ensure correctness.
    /// </summary>
    [Fact]
    public void GetString_KnownKeys_ReturnExpectedLocalizedValues()
    {
        // WHAT: Tests exact translated text for selected keys across English, Spanish, French, and German.
        // WHY:  Validates catalog accuracy against clinical and UI requirements.
        // HOW:  Asserts exact string match for known key translations.
        var localizer = new WeaverLocalizer();

        Assert.Equal("Home", localizer.GetString("Nav.Home", WeaverLanguage.English));
        Assert.Equal("Inicio", localizer.GetString("Nav.Home", WeaverLanguage.Spanish));
        Assert.Equal("Accueil", localizer.GetString("Nav.Home", WeaverLanguage.French));
        Assert.Equal("Startseite", localizer.GetString("Nav.Home", WeaverLanguage.German));
        Assert.Equal("الرئيسية", localizer.GetString("Nav.Home", WeaverLanguage.Arabic));
        Assert.Equal("홈", localizer.GetString("Nav.Home", WeaverLanguage.Korean));
        Assert.Equal("Ana Sayfa", localizer.GetString("Nav.Home", WeaverLanguage.Turkish));
        Assert.Equal("होम", localizer.GetString("Nav.Home", WeaverLanguage.Hindi));
        Assert.Equal("Головна", localizer.GetString("Nav.Home", WeaverLanguage.Ukrainian));
        Assert.Equal("Trang chủ", localizer.GetString("Nav.Home", WeaverLanguage.Vietnamese));
        Assert.Equal("Beranda", localizer.GetString("Nav.Home", WeaverLanguage.Indonesian));
        Assert.Equal("Hem", localizer.GetString("Nav.Home", WeaverLanguage.Swedish));

        Assert.Equal("Male to Female (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.English));
        Assert.Equal("Hombre a Mujer (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Spanish));
        Assert.Equal("Homme vers Femme (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.French));
        Assert.Equal("Mann zu Frau (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.German));
        Assert.Equal("ذكر إلى أنثى (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Arabic));
        Assert.Equal("남성에서 여성 (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Korean));
        Assert.Equal("Erkekten Kadına (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Turkish));
        Assert.Equal("पुरुष से महिला (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Hindi));
        Assert.Equal("Чоловік у жінку (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Ukrainian));
        Assert.Equal("Nam sang Nữ (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Vietnamese));
        Assert.Equal("Pria ke Wanita (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Indonesian));
        Assert.Equal("Man till Kvinna (MtF)", localizer.GetString("Regimen.MtF", WeaverLanguage.Swedish));

        Assert.Equal("Online", localizer.GetString("Status.Online", WeaverLanguage.English));
        Assert.Equal("En línea", localizer.GetString("Status.Online", WeaverLanguage.Spanish));
        Assert.Equal("En ligne", localizer.GetString("Status.Online", WeaverLanguage.French));
        Assert.Equal("Online", localizer.GetString("Status.Online", WeaverLanguage.German));
        Assert.Equal("متصل", localizer.GetString("Status.Online", WeaverLanguage.Arabic));
        Assert.Equal("온라인", localizer.GetString("Status.Online", WeaverLanguage.Korean));
        Assert.Equal("Çevrimiçi", localizer.GetString("Status.Online", WeaverLanguage.Turkish));
        Assert.Equal("ऑनलाइन", localizer.GetString("Status.Online", WeaverLanguage.Hindi));
        Assert.Equal("Онлайн", localizer.GetString("Status.Online", WeaverLanguage.Ukrainian));
        Assert.Equal("Trực tuyến", localizer.GetString("Status.Online", WeaverLanguage.Vietnamese));
        Assert.Equal("Online", localizer.GetString("Status.Online", WeaverLanguage.Indonesian));
        Assert.Equal("Online", localizer.GetString("Status.Online", WeaverLanguage.Swedish));
    }

    /// <summary>
    /// Verifies that passing null or empty keys returns an empty string without throwing.
    /// </summary>
    /// <param name="key">The null or empty key.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetString_NullOrEmptyKey_ReturnsEmptyString(string? key)
    {
        // WHAT: Tests zero-exception edge case for null or empty string keys.
        // WHY:  Guarantees stability when UI bindings evaluate null string expressions.
        // HOW:  Asserts GetString returns string.Empty.
        var localizer = new WeaverLocalizer();
        string result = localizer.GetString(key!, WeaverLanguage.English);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that missing keys in non-English languages fallback to English if available.
    /// </summary>
    [Fact]
    public void GetString_MissingKeyInTargetLanguage_FallsBackToEnglish()
    {
        // WHAT: Tests language catalog fallback hierarchy when key is absent from target language.
        // WHY:  Ensures non-English users see intelligible English content rather than raw cryptic keys.
        // HOW:  Builds a mock catalog with English containing 'Custom.Key' and Spanish missing it, then asserts Spanish returns the English value.
        var catalogs = new Dictionary<WeaverLanguage, FrozenDictionary<string, string>>
        {
            [WeaverLanguage.English] = new Dictionary<string, string> { ["Custom.Key"] = "English Value" }.ToFrozenDictionary(),
            [WeaverLanguage.Spanish] = new Dictionary<string, string>().ToFrozenDictionary()
        }.ToFrozenDictionary();

        var localizer = new WeaverLocalizer(catalogs);
        string result = localizer.GetString("Custom.Key", WeaverLanguage.Spanish);

        Assert.Equal("English Value", result);
    }

    /// <summary>
    /// Verifies that keys missing from both target language and English return the raw key name without throwing.
    /// </summary>
    [Fact]
    public void GetString_MissingKeyEverywhere_ReturnsRawKey()
    {
        // WHAT: Tests ultimate fallback when a key is absent from both target and English catalogs.
        // WHY:  Fulfills Zero-Exception Contract by returning raw dot-notated key.
        // HOW:  Calls GetString with nonexistent key and verifies return equals key.
        var localizer = new WeaverLocalizer();
        string result = localizer.GetString("NonExistent.Key.123", WeaverLanguage.Spanish);
        Assert.Equal("NonExistent.Key.123", result);

        string enResult = localizer.GetString("NonExistent.Key.123", WeaverLanguage.English);
        Assert.Equal("NonExistent.Key.123", enResult);
    }

    /// <summary>
    /// Verifies fallback behavior when an undefined language enum value or missing catalog is requested.
    /// </summary>
    [Fact]
    public void GetString_UndefinedLanguageEnum_FallsBackToEnglishOrRawKey()
    {
        // WHAT: Tests fallback behavior when an unknown language code is passed to GetString.
        // WHY:  Guarantees resilience against invalid enum casts.
        // HOW:  Calls GetString with (WeaverLanguage)999 for both existing English key and nonexistent key.
        var localizer = new WeaverLocalizer();
        var invalidLang = (WeaverLanguage)999;

        string existingResult = localizer.GetString("Nav.Home", invalidLang);
        Assert.Equal("Home", existingResult);

        string missingResult = localizer.GetString("Missing.Key", invalidLang);
        Assert.Equal("Missing.Key", missingResult);
    }

    /// <summary>
    /// Verifies fallback when even the English catalog is missing from the catalogs dictionary.
    /// </summary>
    [Fact]
    public void GetString_MissingEnglishCatalog_ReturnsRawKey()
    {
        // WHAT: Tests extreme edge case where English catalog itself is absent.
        // WHY:  Ensures no exception occurs even if the default catalog fails to load.
        // HOW:  Initializes WeaverLocalizer with an empty catalog dictionary and asserts raw key is returned.
        var emptyCatalogs = FrozenDictionary<WeaverLanguage, FrozenDictionary<string, string>>.Empty;
        var localizer = new WeaverLocalizer(emptyCatalogs);

        string result = localizer.GetString("Any.Key", WeaverLanguage.Spanish);
        Assert.Equal("Any.Key", result);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.GetFormattedString"/> formats templates correctly.
    /// </summary>
    [Fact]
    public void GetFormattedString_ValidTemplateAndArguments_ReturnsFormattedString()
    {
        // WHAT: Tests argument interpolation in localized format templates.
        // WHY:  Enables parameterized localized messages across UI components.
        // HOW:  Constructs a catalog with a format placeholder and asserts formatted output.
        var catalogs = new Dictionary<WeaverLanguage, FrozenDictionary<string, string>>
        {
            [WeaverLanguage.English] = new Dictionary<string, string>
            {
                ["Message.Welcome"] = "Welcome, {0}! You have {1} pending tasks."
            }.ToFrozenDictionary()
        }.ToFrozenDictionary();

        var localizer = new WeaverLocalizer(catalogs);
        string formatted = localizer.GetFormattedString("Message.Welcome", WeaverLanguage.English, "Ada", 5);

        Assert.Equal("Welcome, Ada! You have 5 pending tasks.", formatted);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.GetFormattedString"/> returns the unformatted template when args is null or empty.
    /// </summary>
    [Fact]
    public void GetFormattedString_NullOrEmptyArgs_ReturnsUnformattedTemplate()
    {
        // WHAT: Tests formatting when arguments array is null or empty.
        // WHY:  Avoids redundant string.Format overhead when no arguments are provided.
        // HOW:  Calls GetFormattedString with null args and empty args and asserts unformatted template is returned.
        var localizer = new WeaverLocalizer();

        string withNull = localizer.GetFormattedString("Nav.Home", WeaverLanguage.English, null!);
        Assert.Equal("Home", withNull);

        string withEmpty = localizer.GetFormattedString("Nav.Home", WeaverLanguage.English);
        Assert.Equal("Home", withEmpty);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.GetFormattedString"/> catches <see cref="FormatException"/> and returns the raw template.
    /// </summary>
    [Fact]
    public void GetFormattedString_MalformedTemplate_CatchesFormatExceptionAndReturnsTemplate()
    {
        // WHAT: Tests graceful handling of malformed format strings.
        // WHY:  Fulfills Zero-Exception Contract by never letting a FormatException propagate to the caller.
        // HOW:  Supplies an invalid format string (e.g. unclosed brace '{') and asserts raw template is returned.
        var catalogs = new Dictionary<WeaverLanguage, FrozenDictionary<string, string>>
        {
            [WeaverLanguage.English] = new Dictionary<string, string>
            {
                ["Malformed.Key"] = "Unclosed brace {0"
            }.ToFrozenDictionary()
        }.ToFrozenDictionary();

        var localizer = new WeaverLocalizer(catalogs);
        string result = localizer.GetFormattedString("Malformed.Key", WeaverLanguage.English, "Test");

        Assert.Equal("Unclosed brace {0", result);
    }

    /// <summary>
    /// Verifies that loading from an assembly with no matching embedded resource files produces empty catalogs without throwing.
    /// </summary>
    [Fact]
    public void Constructor_AssemblyWithNoMatchingResources_CreatesEmptyCatalogs()
    {
        // WHAT: Tests assembly scanning when no localization resources are embedded in the provided assembly.
        // WHY:  Validates that empty resources are handled cleanly without exceptions.
        // HOW:  Passes typeof(object).Assembly to WeaverLocalizer and asserts raw key is returned.
        var localizer = new WeaverLocalizer(typeof(object).Assembly);
        string result = localizer.GetString("Nav.Home", WeaverLanguage.English);
        Assert.Equal("Nav.Home", result);
    }

    /// <summary>
    /// Verifies that resource streams returning null or deserializing to null are handled gracefully.
    /// </summary>
    [Fact]
    public void Constructor_ResourceStreamNullOrDeserializationNull_HandledGracefully()
    {
        // WHAT: Tests edge cases where GetManifestResourceStream returns null or stream contains JSON null.
        // WHY:  Ensures full branch coverage in the catalog loader when assembly stream reading encounters nulls.
        // HOW:  Uses Moq to mock Assembly returning a resource name whose stream returns null, and one returning 'null' JSON.
        var mockAssembly = new Mock<Assembly>();
        mockAssembly.Setup(a => a.GetManifestResourceNames()).Returns(
        [
            "strings.en-US.json",
            "strings.es-ES.json"
        ]);

        mockAssembly.Setup(a => a.GetManifestResourceStream("strings.en-US.json"))
            .Returns((Stream?)null);

        var nullJsonStream = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        mockAssembly.Setup(a => a.GetManifestResourceStream("strings.es-ES.json"))
            .Returns(nullJsonStream);

        var localizer = new WeaverLocalizer(mockAssembly.Object);
        Assert.Equal("Nav.Home", localizer.GetString("Nav.Home", WeaverLanguage.English));
        Assert.Equal("Nav.Home", localizer.GetString("Nav.Home", WeaverLanguage.Spanish));
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLanguageInfo.IsRightToLeft"/> is correctly flagged true only for Arabic among supported languages.
    /// </summary>
    [Fact]
    public void GetSupportedLanguages_RightToLeftFlag_OnlyArabicIsTrue()
    {
        // WHAT: Tests script direction detection across all supported languages.
        // WHY:  Guarantees UI layout systems can accurately toggle RTL layout flow (e.g. dir="rtl").
        // HOW:  Queries GetSupportedLanguages and verifies that IsRightToLeft is true exclusively for Arabic.
        var localizer = new WeaverLocalizer();
        IReadOnlyList<WeaverLanguageInfo> languages = localizer.GetSupportedLanguages();

        WeaverLanguageInfo arabic = Assert.Single(languages, l => l.IsRightToLeft);
        Assert.Equal(WeaverLanguage.Arabic, arabic.Code);
        Assert.Equal("ar-SA", arabic.Bcp47Tag);
        Assert.True(arabic.IsRightToLeft);

        foreach (WeaverLanguageInfo lang in languages.Where(l => l.Code != WeaverLanguage.Arabic))
        {
            Assert.False(lang.IsRightToLeft, $"Language '{lang.Code}' should not be right-to-left.");
        }
    }
}
