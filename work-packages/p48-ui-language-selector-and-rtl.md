# Work Package P48: UI Language Selector & RTL Integration

## 1. Context & Objective
- **Problem Statement**:
  Following P47's expansion of the domain model to 18 community languages, the presentation tier (Blazor Web UI, Desktop ViewModels, and system tray menus) must support seamless selection and rendering of all 18 languages, including bidirectional right-to-left (RTL) layout switching for Arabic (`ar-SA`).
- **Goal & Scope**:
  1. Update `src/NanoWeaverHrt/Components/Shared/LanguageSelector.razor` to support all 18 languages with endonym labels, RTL awareness, and reactive change events.
  2. Update `src/NanoWeaverHrt/Components/Layout/MainLayout.razor` to set the document or container direction (`dir="rtl"` for Arabic, `dir="ltr"` for others).
  3. Ensure `MainShellViewModel.cs` and `DesktopTrayIconController.cs` support all 18 languages.
  4. Ensure `LanguagePreferenceService.cs` round-trips all 18 `WeaverLanguage` enum values across SQLite and PostgreSQL stores.
  5. Update bUnit and xUnit test suites in `tests/NanoWeaverHrt.Tests/Components/LanguageSelectorTests.cs` and `tests/NanoWeaverHrt.Tests/Gui/DesktopLocalizationTests.cs` to achieve 100.00% Line & Branch coverage.
- **Target Role / Model**: `coder` (`flash` or `inherit`)
- **Transport Boundary**: Web & Desktop Presentation Layer

---

## 2. Technical Contracts & Invariants
- **Invariants**:
  - Zero UI layout shifts when switching between LTR and RTL scripts.
  - Complete XML docblock coverage on every method and property with `<param>` and `<returns>`.
  - 100.00% Line & Branch coverage in bUnit/xUnit tests.
  - Synchronize `coverlet.runsettings` and `coverage-full.runsettings`.
