using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Represents a subject membership record linking a patient to a cohort group.
/// </summary>
public sealed record SubjectGroupMember
{
    /// <summary>
    /// Gets the unique identifier of the cohort group.
    /// </summary>
    [Description("Cohort group identifier.")]
    [JsonPropertyName("groupId")]
    public string GroupId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username of the member subject/patient.
    /// </summary>
    [Description("Username of the patient or subject enrolled in the cohort.")]
    [JsonPropertyName("subjectUsername")]
    public string SubjectUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the subject was enrolled in the group.
    /// </summary>
    [Description("UTC timestamp marking cohort enrollment.")]
    [JsonPropertyName("addedAtUtc")]
    public DateTimeOffset AddedAtUtc { get; init; }
}
