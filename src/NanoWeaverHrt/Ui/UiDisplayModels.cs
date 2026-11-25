using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Ui;

/// <summary>
/// Controls the visual density and progressive disclosure level of the Sovereign Web UI.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Enumeration switching between the calm task-driven default view and the full operator view.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 47 (P245): Prevents overwhelming newly signed-in users with low-level telemetry counters
/// and eleven navigation tabs while keeping full diagnostics one click away.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stored per user in <c>weaver_user_preferences</c>, defaulted from <c>Ui:DefaultDisplayMode</c>,
/// and cascaded through <c>MainLayout</c> to workspace pages.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiDisplayMode
{
    /// <summary>
    /// Calm, task-driven interface showing role-specific landing cards, grouped menus, and collapsed secondary panels.
    /// </summary>
    Simple = 0,

    /// <summary>
    /// Full operator interface showing live GC/hardware telemetry, node identity chips, flat 11-link navigation, and expanded panels.
    /// </summary>
    Advanced = 1
}

/// <summary>
/// Persisted per-user UI display mode record stored in <c>weaver_user_preferences</c>.
/// </summary>
[SwaggerSchema(Title = "UserUiPreferenceRecord", Description = "Persisted per-user display mode preference record.")]
public sealed record UserUiPreferenceRecord
{
    /// <summary>
    /// Gets or initializes the normalized lowercase username owning this preference.
    /// </summary>
    [Description("Normalized lowercase username owning this display preference.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the persisted display mode (<c>Simple</c> or <c>Advanced</c>).
    /// </summary>
    [Description("Persisted display mode ('Simple' or 'Advanced').")]
    [JsonPropertyName("displayMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiDisplayMode DisplayMode { get; init; } = UiDisplayMode.Simple;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the preference was last saved.
    /// </summary>
    [Description("UTC timestamp when the display mode preference was last saved.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Resolved UI display mode returned to the Web UI and REST API clients after merging user override and node default.
/// </summary>
[SwaggerSchema(Title = "EffectiveUiPreference", Description = "Resolved display mode combining per-user override with node-wide Ui:DefaultDisplayMode.")]
public sealed record EffectiveUiPreference : IExamplesProvider<EffectiveUiPreference>
{
    /// <summary>
    /// Gets or initializes the normalized username for this effective preference.
    /// </summary>
    [Description("Normalized username associated with this resolved preference.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the active display mode applied to the user's interface.
    /// </summary>
    [Description("Active display mode ('Simple' or 'Advanced') rendered for the user.")]
    [JsonPropertyName("displayMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiDisplayMode DisplayMode { get; init; } = UiDisplayMode.Simple;

    /// <summary>
    /// Gets or initializes a value indicating whether the active mode comes from a per-user database override.
    /// </summary>
    [Description("True when a saved user preference overrode the node default; false when inheriting Ui:DefaultDisplayMode.")]
    [JsonPropertyName("isUserOverride")]
    public bool IsUserOverride { get; init; }

    /// <summary>
    /// Gets or initializes the node-wide fallback display mode configured via <c>Ui:DefaultDisplayMode</c>.
    /// </summary>
    [Description("Node-wide default display mode configured under Ui:DefaultDisplayMode.")]
    [JsonPropertyName("nodeDefaultDisplayMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiDisplayMode NodeDefaultDisplayMode { get; init; } = UiDisplayMode.Simple;

    /// <summary>
    /// Gets or initializes the UTC timestamp of the persisted user override, or <see langword="null"/> when inheriting node default.
    /// </summary>
    [Description("UTC timestamp when the user override was saved, or null when inheriting node default.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EffectiveUiPreference"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated effective UI preference response example.</returns>
    public EffectiveUiPreference GetExamples() => new()
    {
        Username = "researcher_alex",
        DisplayMode = UiDisplayMode.Simple,
        IsUserOverride = false,
        NodeDefaultDisplayMode = UiDisplayMode.Simple,
        UpdatedAtUtc = null
    };
}

/// <summary>
/// Request payload for saving a user's preferred Web UI display mode.
/// </summary>
[SwaggerSchema(Title = "UpdateUiPreferenceRequest", Description = "Request payload specifying the user's preferred display mode ('Simple' or 'Advanced').")]
public sealed record UpdateUiPreferenceRequest : IExamplesProvider<UpdateUiPreferenceRequest>
{
    /// <summary>
    /// Gets or initializes the requested UI display mode.
    /// </summary>
    [Description("Desired Web UI display mode ('Simple' or 'Advanced').")]
    [JsonPropertyName("displayMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UiDisplayMode DisplayMode { get; init; } = UiDisplayMode.Simple;

    /// <summary>
    /// Generates a representative example instance of <see cref="UpdateUiPreferenceRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample update UI preference request payload.</returns>
    public UpdateUiPreferenceRequest GetExamples() => new()
    {
        DisplayMode = UiDisplayMode.Advanced
    };
}

/// <summary>
/// Metadata describing a single role-tailored task launch card rendered on the <c>/</c> Task Hub.
/// </summary>
public sealed record UiTaskCardDescriptor
{
    /// <summary>
    /// Gets or initializes the stable card identifier used for DOM selectors and automated tests.
    /// </summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization key for the task card headline.
    /// </summary>
    public string TitleKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization key for the plain-English task summary.
    /// </summary>
    public string DescriptionKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization key for the primary action button label.
    /// </summary>
    public string ActionLabelKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the destination route navigated to when clicking the card.
    /// </summary>
    public string Route { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the symbolic glyph displayed alongside the card title.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization key describing the functional category badge.
    /// </summary>
    public string CategoryKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether the card appears in the primary Simple Mode task grid.
    /// </summary>
    public bool IsPrimary { get; init; } = true;

    /// <summary>
    /// Gets or initializes a value indicating whether the card appears only when Advanced Display Mode is active.
    /// </summary>
    public bool RequiresAdvancedDisplay { get; init; }
}

/// <summary>
/// Metadata describing a navigation link inside a grouped Simple Mode menu.
/// </summary>
public sealed record UiNavItemDescriptor
{
    /// <summary>
    /// Gets or initializes the target route path (for example <c>/wizard</c>).
    /// </summary>
    public string Route { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization resource key for the link label.
    /// </summary>
    public string LabelKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the icon glyph shown before the navigation label.
    /// </summary>
    public string Icon { get; init; } = string.Empty;
}

/// <summary>
/// Metadata describing a grouped navigation dropdown shown in Simple Display Mode.
/// </summary>
public sealed record UiNavGroupDescriptor
{
    /// <summary>
    /// Gets or initializes the stable group identifier used in DOM attributes.
    /// </summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the localization key for the group menu trigger label.
    /// </summary>
    public string LabelKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the icon glyph displayed on the group menu trigger.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the ordered navigation destinations inside this task group.
    /// </summary>
    public IReadOnlyList<UiNavItemDescriptor> Items { get; init; } = [];
}
