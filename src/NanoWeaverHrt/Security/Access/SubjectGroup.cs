using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Represents a named cohort grouping of subjects/patients created for research studies or clinical trials.
/// </summary>
public sealed record SubjectGroup
{
    /// <summary>
    /// Gets the unique cohort group identifier.
    /// </summary>
    [Description("Unique identifier for this subject cohort group.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human-readable group name.
    /// </summary>
    [Description("Human-readable name for the cohort group.")]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional cohort description or clinical objectives.
    /// </summary>
    [Description("Clinical or analytical description of the cohort group.")]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Gets the username of the user who created this group.
    /// </summary>
    [Description("Username of the administrator or researcher who created the cohort.")]
    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the group was created.
    /// </summary>
    [Description("UTC timestamp marking cohort creation.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the group or its members were last modified.
    /// </summary>
    [Description("UTC timestamp marking last modification.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Gets the number of member subjects in this cohort.
    /// </summary>
    [Description("Count of patients or subjects enrolled in this cohort group.")]
    [JsonPropertyName("memberCount")]
    public int MemberCount { get; init; }
}
