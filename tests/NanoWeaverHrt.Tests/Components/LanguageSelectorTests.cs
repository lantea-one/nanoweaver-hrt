using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using NanoWeaverHrt.Components.Shared;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and integration tests for <see cref="LanguageSelector"/> verifying dropdown rendering,
/// language tag resolution, JS persistence interop, first-render cookie restoration, and reactive change callbacks.
/// </summary>
public sealed class LanguageSelectorTests : TestContext
{
    /// <summary>
    /// Localization service provider supporting community translations.
    /// </summary>
    private readonly WeaverLocalizer _localizer = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageSelectorTests"/> class.
    /// </summary>
    public LanguageSelectorTests()
    {
        // WHAT: Configures dependency injection container and JSInterop mock mode for bUnit.
        // WHY:  LanguageSelector depends on IWeaverLocalizer and IJSRuntime for tag resolution and persistence.
        // HOW:  Registers WeaverLocalizer singleton and enables Loose mode on JSInterop.
        Services.AddSingleton<IWeaverLocalizer>(_localizer);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Verifies that the language selector renders the dropdown with all supported languages and appropriate CSS classes.
    /// </summary>
    [Fact]
    public void Render_RendersDropdownWithAllSupportedLanguages()
    {
        // WHAT: Tests DOM output of LanguageSelector component.
        // WHY:  Ensures all supported languages are exposed to clinicians and researchers in the dropdown.
        // HOW:  Renders component and asserts on container, icon, dropdown element, and option count.
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>();

        Assert.NotNull(cut.Find(".language-selector-container"));
        Assert.NotNull(cut.Find(".language-selector-icon"));
        var select = cut.Find("select.language-dropdown");
        Assert.NotNull(select);

        var options = cut.FindAll("option");
        IReadOnlyList<WeaverLanguageInfo> supported = _localizer.GetSupportedLanguages();
        Assert.Equal(supported.Count, options.Count);

        foreach (WeaverLanguageInfo lang in supported)
        {
            Assert.Contains(options, opt => opt.GetAttribute("value") == lang.Bcp47Tag);
        }
    }

    /// <summary>
    /// Verifies that the select element value matches the active CurrentLanguage parameter.
    /// </summary>
    [Fact]
    public void Render_WithSpecificLanguage_SetsCorrectSelectedOption()
    {
        // WHAT: Tests language parameter binding to select element.
        // WHY:  Dropdown must reflect the currently active language passed down from the master layout.
        // HOW:  Renders component with CurrentLanguage = Spanish and verifies tag is 'es-ES'.
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.Spanish));

        var select = cut.Find("select.language-dropdown");
        Assert.Equal("es-ES", select.GetAttribute("value"));
        Assert.Equal("Idioma", select.GetAttribute("aria-label"));
    }

    /// <summary>
    /// Verifies that changing the selection updates CurrentLanguage, invokes JS persistence, and fires OnLanguageChanged.
    /// </summary>
    [Fact]
    public async Task HandleSelectionChanged_ValidLanguage_UpdatesStateAndPersists()
    {
        // WHAT: Tests user selection change interaction.
        // WHY:  Selecting a new language must persist to cookies and inform the parent layout.
        // HOW:  Renders component with callback capture, changes select value to 'de-DE', and verifies invocations.
        WeaverLanguage? changedLang = null;
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.English)
            .Add(p => p.OnLanguageChanged, EventCallback.Factory.Create<WeaverLanguage>(this, lang => changedLang = lang)));

        var select = cut.Find("select.language-dropdown");
        await cut.InvokeAsync(() => select.Change("de-DE"));

        Assert.Equal(WeaverLanguage.German, cut.Instance.CurrentLanguage);
        Assert.Equal(WeaverLanguage.German, changedLang);
        JSInterop.VerifyInvoke("weaverSetLanguage");
    }

    /// <summary>
    /// Verifies that selection change with a null or unknown language tag gracefully falls back to English.
    /// </summary>
    [Fact]
    public async Task HandleSelectionChanged_NullOrUnknownTag_FallsBackToEnglish()
    {
        // WHAT: Tests graceful fallback when an invalid or null tag is passed in change event.
        // WHY:  Robustness against unexpected browser event payloads.
        // HOW:  Invokes HandleSelectionChangedAsync directly with null and unknown values.
        WeaverLanguage? changedLang = null;
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.French)
            .Add(p => p.OnLanguageChanged, EventCallback.Factory.Create<WeaverLanguage>(this, lang => changedLang = lang)));

        await cut.InvokeAsync(() => cut.Instance.HandleSelectionChangedAsync(new ChangeEventArgs { Value = null }));
        Assert.Equal(WeaverLanguage.English, cut.Instance.CurrentLanguage);
        Assert.Equal(WeaverLanguage.English, changedLang);

        await cut.InvokeAsync(() => cut.Instance.HandleSelectionChangedAsync(new ChangeEventArgs { Value = "unknown-xyz" }));
        Assert.Equal(WeaverLanguage.English, cut.Instance.CurrentLanguage);
    }

    /// <summary>
    /// Verifies that when JS interop throws during selection change, the exception is caught gracefully.
    /// </summary>
    [Fact]
    public async Task HandleSelectionChanged_WhenJsThrows_CatchesGracefully()
    {
        // WHAT: Tests exception handling in HandleSelectionChangedAsync.
        // WHY:  Browser storage quota or disconnected circuit must not crash the component.
        // HOW:  Configures weaverSetLanguage invocation to throw JSException and verifies clean completion.
        JSInterop.SetupVoid("weaverSetLanguage", _ => true)
            .SetException(new JSException("Storage quota exceeded"));

        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>();

        Exception? caught = null;
        try
        {
            await cut.InvokeAsync(() => cut.Instance.HandleSelectionChangedAsync(new ChangeEventArgs { Value = "es-ES" }));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.Null(caught);
        Assert.Equal(WeaverLanguage.Spanish, cut.Instance.CurrentLanguage);
    }

    /// <summary>
    /// Verifies that on initial render, persisted language preference is restored from cookies via JS interop.
    /// </summary>
    [Fact]
    public void OnAfterRender_FirstRenderWithStoredLanguage_RestoresLanguageAndNotifies()
    {
        // WHAT: Tests cookie restoration flow on first interactive render.
        // WHY:  User's previously selected language should be activated immediately without page reload.
        // HOW:  Mocks weaverGetLanguage returning 'fr-FR', renders component, and asserts German -> French transition.
        JSInterop.Setup<string?>("weaverGetLanguage").SetResult("fr-FR");

        WeaverLanguage? captured = null;
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.English)
            .Add(p => p.OnLanguageChanged, EventCallback.Factory.Create<WeaverLanguage>(this, lang => captured = lang)));

        Assert.Equal(WeaverLanguage.French, cut.Instance.CurrentLanguage);
        Assert.Equal(WeaverLanguage.French, captured);
    }

    /// <summary>
    /// Verifies that on first render, if stored language is identical to CurrentLanguage, OnLanguageChanged is not re-invoked.
    /// </summary>
    [Fact]
    public void OnAfterRender_FirstRenderWithSameLanguage_DoesNotFireCallback()
    {
        // WHAT: Tests no-op behavior when stored language equals current language.
        // WHY:  Avoids unnecessary circuit re-renders and cycle loops.
        // HOW:  Mocks weaverGetLanguage returning 'en-US' when CurrentLanguage is English.
        JSInterop.Setup<string?>("weaverGetLanguage").SetResult("en-US");

        bool callbackFired = false;
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.English)
            .Add(p => p.OnLanguageChanged, EventCallback.Factory.Create<WeaverLanguage>(this, _ => callbackFired = true)));

        Assert.Equal(WeaverLanguage.English, cut.Instance.CurrentLanguage);
        Assert.False(callbackFired);
    }

    /// <summary>
    /// Verifies that on first render, if stored language is empty or whitespace, state remains unchanged.
    /// </summary>
    [Fact]
    public void OnAfterRender_FirstRenderWithEmptyStoredLanguage_MaintainsCurrentLanguage()
    {
        // WHAT: Tests empty or whitespace stored language handling on first render.
        // WHY:  Uninitialized browser storage should keep default language.
        // HOW:  Mocks weaverGetLanguage returning null and whitespace.
        JSInterop.Setup<string?>("weaverGetLanguage").SetResult("   ");

        bool callbackFired = false;
        IRenderedComponent<LanguageSelector> cut = RenderComponent<LanguageSelector>(parameters => parameters
            .Add(p => p.CurrentLanguage, WeaverLanguage.German)
            .Add(p => p.OnLanguageChanged, EventCallback.Factory.Create<WeaverLanguage>(this, _ => callbackFired = true)));

        Assert.Equal(WeaverLanguage.German, cut.Instance.CurrentLanguage);
        Assert.False(callbackFired);
    }

    /// <summary>
    /// Verifies that on first render, if JS interop throws an exception, the component catches it gracefully.
    /// </summary>
    [Fact]
    public void OnAfterRender_FirstRenderWhenJsThrows_CatchesGracefully()
    {
        // WHAT: Tests exception resilience during first-render cookie retrieval.
        // WHY:  Prerendering or security sandbox restrictions must not throw unhandled exceptions.
        // HOW:  Configures weaverGetLanguage to throw JSException and verifies render completes cleanly.
        JSInterop.Setup<string?>("weaverGetLanguage")
            .SetException(new JSException("Prerendering interop not allowed"));

        Exception? caught = null;
        try
        {
            RenderComponent<LanguageSelector>(parameters => parameters
                .Add(p => p.CurrentLanguage, WeaverLanguage.Italian));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.Null(caught);
    }
}
