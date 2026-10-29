# Work Package P45: Sovereign Web UI Language Switcher & Localized Razor Components

## 1. Context & Objective
- **Problem Statement**:
  The sovereign Web UI (`NanoWeaverHrt` Blazor Interactive Server dashboard) requires an interactive language switcher in the master layout header, allowing clinicians, researchers, and patients to seamlessly toggle between English (`en-US`), Spanish (`es-ES`), French (`fr-FR`), and German (`de-DE`).
- **Goal & Scope**:
  1. Add a Language Selector dropdown component in `src/NanoWeaverHrt/Components/Layout/MainLayout.razor` (or a dedicated `LanguageSelector.razor` component in `src/NanoWeaverHrt/Components/Shared/`).
  2. Persist language selection in browser cookies (`weaver_lang`) and `sessionStorage` via `weaver-session.js`.
  3. Inject `IWeaverLocalizer` into `MainLayout.razor`, `Home.razor`, and `EndocrineHub.razor`.
  4. Localize top navigation links, header badges, endocrine toggle labels, and alert banners.
  5. Author bUnit tests asserting reactive UI language updates upon selection without requiring page reload.
  6. Synchronize `ExcludeByFile` rules between `coverlet.runsettings` and `coverage-full.runsettings`.
- **Target Role / Model**: `coder` / `mechanic` (`flash`)
- **Transport Boundary**: Blazor Interactive Server UI

---

## 2. Technical Contracts & Invariants

### UI Contract
- Component: `src/NanoWeaverHrt/Components/Shared/LanguageSelector.razor`
- Injected Dependencies:
  - `IWeaverLocalizer Localizer`
  - `IJSRuntime JSRuntime`
- Parameters / State:
  - `WeaverLanguage CurrentLanguage`
  - `EventCallback<WeaverLanguage> OnLanguageChanged`

### Invariants
1. **Zero Layout Shifts**: The language selector dropdown must fit cleanly into the sovereign top bar without causing layout wrapping or scrollbars on mobile viewport widths (320px–768px).
2. **Sovereign Dark Theme Palette**: Dropdown styled with obsidian slate (`#161f30`) surface and bioluminescent cyan (`#00f5d4`) hover accents.
3. **Cookie Enforcement**: Persists language preference to `weaver_lang` cookie (`SameSite=Strict; Secure`).

---

## 3. Step-by-Step Implementation Tasks
1. Create `src/NanoWeaverHrt/Components/Shared/LanguageSelector.razor`.
2. Embed the language selector into `MainLayout.razor` top navigation bar.
3. Add JS helper in `weaver-session.js` to get and set the `weaver_lang` cookie/session storage.
4. Replace hardcoded navigation titles in `NavMenu.razor` and `MainLayout.razor` with `Localizer.GetString(...)`.
5. Author bUnit component tests in `tests/NanoWeaverHrt.Tests/Components/LanguageSelectorTests.cs`.
6. Verify with `dotnet test tests/NanoWeaverHrt.Tests`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] bUnit component tests pass 100%.
- [ ] Step 4 Line & Branch Coverage verified at 100.00%.
- [ ] Downward coverage ratchet maintained.
- [ ] 100% XML docblocks on all component code-behind methods.
