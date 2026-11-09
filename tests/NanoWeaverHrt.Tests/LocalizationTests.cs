using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Regression tests verifying localization key completeness, catalog parity, and non-empty translations
/// across all 18 supported community languages in <see cref="WeaverLocalizer"/>.
/// </summary>
public sealed class LocalizationTests
{
    /// <summary>
    /// Verifies that all 18 localization resource JSON files contain every key registered in strings.en-US.json
    /// and that none of the translation strings are null, empty, or whitespace.
    /// </summary>
    [Fact]
    public void TranslationResources_AllEighteenLocales_ContainEveryKeyWithNonEmptyValue()
    {
        // WHAT: Asserts 100% key parity across all 18 language catalogs in src/NanoWeaverHrt/Localization/Resources.
        // WHY:  Guarantees complete internationalization without missing translations or fallback gaps.
        // HOW:  Parses strings.en-US.json as the baseline catalog, then verifies every other language file against it.
        string resourcesDir = FindRepoDirectory("src/NanoWeaverHrt/Localization/Resources");
        string enPath = Path.Combine(resourcesDir, "strings.en-US.json");
        Assert.True(File.Exists(enPath), $"Base English catalog not found at {enPath}");

        string enJson = File.ReadAllText(enPath);
        Dictionary<string, string>? enCatalog = JsonSerializer.Deserialize<Dictionary<string, string>>(enJson);
        Assert.NotNull(enCatalog);
        Assert.NotEmpty(enCatalog);

        string[] languageFiles = Directory.GetFiles(resourcesDir, "strings.*.json");
        Assert.Equal(18, languageFiles.Length);

        List<string> missingKeysReport = new();
        List<string> emptyValuesReport = new();

        foreach (string file in languageFiles)
        {
            string fileName = Path.GetFileName(file);
            string json = File.ReadAllText(file);
            Dictionary<string, string>? catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            Assert.NotNull(catalog);

            foreach (var kvp in enCatalog)
            {
                if (!catalog.TryGetValue(kvp.Key, out string? value))
                {
                    missingKeysReport.Add($"{fileName} is missing key '{kvp.Key}'");
                }
                else if (string.IsNullOrWhiteSpace(value))
                {
                    emptyValuesReport.Add($"{fileName} has empty/whitespace translation for key '{kvp.Key}'");
                }
            }
        }

        Assert.Empty(missingKeysReport);
        Assert.Empty(emptyValuesReport);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverLocalizer.GetString(string, WeaverLanguage)"/> returns valid non-empty
    /// translations for every base key across all 18 supported <see cref="WeaverLanguage"/> enum variants.
    /// </summary>
    [Fact]
    public void WeaverLocalizer_AllEighteenLanguages_ResolveAllKeysWithoutFallbackFailure()
    {
        // WHAT: Tests runtime translation resolution for every key and language via WeaverLocalizer.
        // WHY:  Ensures that the in-memory FrozenDictionary catalogs initialize and query deterministically.
        // HOW:  Instantiates WeaverLocalizer, reads the English key set, and asserts valid string output for every language.
        WeaverLocalizer localizer = new();
        IReadOnlyList<WeaverLanguageInfo> supportedLanguages = localizer.GetSupportedLanguages();
        Assert.Equal(18, supportedLanguages.Count);

        string resourcesDir = FindRepoDirectory("src/NanoWeaverHrt/Localization/Resources");
        string enPath = Path.Combine(resourcesDir, "strings.en-US.json");
        string enJson = File.ReadAllText(enPath);
        Dictionary<string, string>? enCatalog = JsonSerializer.Deserialize<Dictionary<string, string>>(enJson);
        Assert.NotNull(enCatalog);

        foreach (WeaverLanguageInfo langInfo in supportedLanguages)
        {
            foreach (string key in enCatalog.Keys)
            {
                string translated = localizer.GetString(key, langInfo.Code);
                Assert.False(string.IsNullOrWhiteSpace(translated),
                    $"Key '{key}' resolved to empty string in language '{langInfo.EnglishName}' ({langInfo.Code})");
            }
        }
    }

    /// <summary>
    /// Locates a directory relative to the repository root by walking upward from <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    /// <param name="relativePath">Repository-relative path.</param>
    /// <returns>The resolved absolute directory path.</returns>
    private static string FindRepoDirectory(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NanoWeaverHrt.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, relativePath);
    }
}
