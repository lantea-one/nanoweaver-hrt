# Work Package P44: Multi-Platform Desktop & ViewModel Localization

## 1. Context & Objective
- **Problem Statement**:
  With `IWeaverLocalizer` established in P43, the native desktop and mobile platform heads (`NanoWeaverHrt.Desktop`, `NanoWeaverHrt.Gui.Core`) require culture awareness. ViewModels currently hardcode English labels for instance connection statuses, system tray badging, and background notifications.
- **Goal & Scope**:
  1. Add `ILanguagePreferenceService` and `LanguagePreferenceService` in `src/NanoWeaverHrt.Client/Storage/` (persisting user preference to local SQLite instance profile store).
  2. Extend `MainShellViewModel` in `src/NanoWeaverHrt.Gui.Core/ViewModels/MainShellViewModel.cs`:
     - Expose `SelectedLanguage` (with property change notification triggering UI update).
     - Expose `AvailableLanguages` (list of `WeaverLanguageInfo`).
     - Localize tray menu items (`Tray.Show`, `Tray.LockVault`, `Tray.Exit`) and notification messages.
  3. Update `DesktopTrayIconController` and notification sinks to display translated status messages.
  4. Author unit tests in `tests/NanoWeaverHrt.Tests/Gui/DesktopLocalizationTests.cs` exercising 100% of branches.
- **Target Role / Model**: `coder` (`flash`)
- **Transport Boundary**: Client SDK & GUI ViewModels (`NanoWeaverHrt.Client`, `NanoWeaverHrt.Gui.Core`)

---

## 2. Technical Contracts & Invariants

### Preference Service Contract
```csharp
namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Service managing persistent user language and locale preferences.
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
    ValueTask SetPreferredLanguageAsync(WeaverLanguage language, CancellationToken ct = default);
}
```

### Invariants
1. **Append Order Invariant**: When adding new methods to `MainShellViewModel`, append them to the bottom of the class to preserve Roslyn compiler state-machine numbering.
2. **Reactive UI Updates**: Changing `SelectedLanguage` must raise `PropertyChanged` for all dependent localized string properties so Avalonia / Blazor views refresh reactively without application restart.
3. **100.00% Line & Branch Coverage**: All branches in `LanguagePreferenceService` and ViewModel language selection logic must be exercised in unit tests.

---

## 3. Step-by-Step Implementation Tasks
1. Implement `ILanguagePreferenceService` and `LanguagePreferenceService` in `src/NanoWeaverHrt.Client/Storage/`.
2. Update `MainShellViewModel.cs` to inject `IWeaverLocalizer` and `ILanguagePreferenceService`.
3. Add properties: `SelectedLanguage`, `AvailableLanguages`, `LocalizedStatusText`, `LocalizedIdleLockMessage`.
4. Update `DesktopTrayIconController.cs` to use localized menu item labels.
5. Author comprehensive unit tests in `tests/NanoWeaverHrt.Tests/Gui/DesktopLocalizationTests.cs`.
6. Verify with `dotnet test tests/NanoWeaverHrt.Tests`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] 100.00% Line & Branch coverage on all new and modified classes.
- [ ] Downward coverage ratchet maintained.
- [ ] 100% XML docblock coverage on all members.
