# Work Package P43: Community Localization Architecture & Embedded Resource Provider

## 1. Context & Objective
- **Problem Statement**:
  NanoWeaver HRT is deployed globally by decentralized clinicians, researchers, and patients. The platform's user interfaces and clinical alerts are currently English-only (`en-US`), creating barriers for non-English-speaking communities. Implementing a zero-allocation, cross-platform localization framework enables translation into Spanish (`es-ES`), French (`fr-FR`), and German (`de-DE`) across Web, Desktop, and Mobile platform heads.
- **Goal & Scope**:
  1. Implement `IWeaverLocalizer` and `WeaverLocalizer` in `src/NanoWeaverHrt/Localization/`.
  2. Define `WeaverLanguage` enum (`English` `en-US`, `Spanish` `es-ES`, `French` `fr-FR`, `German` `de-DE`).
  3. Author embedded JSON localization catalog files:
     - `src/NanoWeaverHrt/Localization/Resources/strings.en-US.json`
     - `src/NanoWeaverHrt/Localization/Resources/strings.es-ES.json`
     - `src/NanoWeaverHrt/Localization/Resources/strings.fr-FR.json`
     - `src/NanoWeaverHrt/Localization/Resources/strings.de-DE.json`
  4. Implement `IWeaverCultureContext` for managing current culture, thread context, and fallback rules.
  5. Fallback rule: Missing keys or missing language catalogs fallback gracefully to `en-US`, then to raw key name, never throwing exceptions.
  6. Achieve 100.00% Line & Branch coverage in `tests/NanoWeaverHrt.Tests/Localization/WeaverLocalizerTests.cs`.
- **Target Role / Model**: `coder` (`flash`)
- **Transport Boundary**: In-Memory Domain Layer (`NanoWeaverHrt.Localization`)

---

## 2. Technical Contracts & Invariants

### Enums & Interfaces
```csharp
namespace NanoWeaverHrt.Localization;

/// <summary>
/// Supported community localization languages across NanoWeaver HRT.
/// </summary>
public enum WeaverLanguage
{
    /// <summary>English (United States, default).</summary>
    English,
    /// <summary>Spanish (Español).</summary>
    Spanish,
    /// <summary>French (Français).</summary>
    French,
    /// <summary>German (Deutsch).</summary>
    German
}

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
```

### Invariants
1. **Zero-Exception Contract**: If a key does not exist in the requested language, return English. If it does not exist in English, return the key itself. Never throw `KeyNotFoundException`.
2. **Zero-Allocation Hot Path**: Once loaded from embedded assembly streams at startup, dictionary lookups must be $O(1)$ concurrent read operations.
3. **100% Docblocks**: Every class, enum, method, property, and parameter must have full XML documentation.
4. **100.00% Line & Branch Coverage**: All branches (missing keys, unknown languages, format string errors, culture conversions) must be tested.

---

## 3. Step-by-Step Implementation Tasks
1. Create directory `src/NanoWeaverHrt/Localization/`.
2. Implement `WeaverLanguage.cs`, `WeaverLanguageInfo.cs`, `IWeaverLocalizer.cs`.
3. Implement `WeaverLocalizer.cs` reading embedded JSON catalogs from assembly resources.
4. Add JSON resource files in `src/NanoWeaverHrt/Localization/Resources/`:
   - Navigation keys (`Nav.Home`, `Nav.Endocrine`, `Nav.Wizard`, `Nav.Simulations`, `Nav.Genomics`, `Nav.Compliance`, `Nav.Datasets`)
   - Regimen titles (`Regimen.MtF`, `Regimen.FtM`, `Regimen.E2Dominant`, `Regimen.TDominant`)
   - Security alerts (`Alert.IdleTimeout`, `Alert.CryptoShredded`, `Alert.TamperDetected`)
   - Status messages (`Status.Online`, `Status.Offline`, `Status.Connecting`)
5. Configure `.csproj` with `<EmbeddedResource Include="Localization/Resources/*.json" />`.
6. Author unit tests in `tests/NanoWeaverHrt.Tests/Localization/WeaverLocalizerTests.cs`.
7. Register `builder.Services.AddSingleton<IWeaverLocalizer, WeaverLocalizer>();` in `Program.cs`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] 100.00% Line & Branch coverage on `WeaverLocalizer.cs`.
- [ ] Roundtrip tests for all 4 languages asserting non-empty translations.
- [ ] 100% XML docblock coverage.
