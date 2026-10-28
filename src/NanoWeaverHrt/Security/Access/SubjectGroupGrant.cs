using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Represents an access grant assigning a Researcher permissions over all member subjects within a cohort group.
/// </summary>
public sealed record SubjectGroupGrant
{
    /// <summary>
    /// Gets the unique group grant record identifier.
    /// </summary>
    [Description("Unique identifier for this cohort group access grant.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the unique identifier of the cohort group being granted.
    /// </summary>
    [Description("Identifier of the cohort group to which access is granted.")]
    [JsonPropertyName("groupId")]
    public string GroupId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username of the researcher receiving group permissions.
    /// </summary>
    [Description("Username of the researcher receiving access to this cohort.")]
    [JsonPropertyName("researcherUsername")]
    public string ResearcherUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tiered access level governing permitted actions over all member subjects in this cohort.
    /// </summary>
    [Description("Tiered access level granted across all members of this cohort group.")]
    [JsonPropertyName("accessLevel")]
    public SubjectAccessLevel AccessLevel { get; init; } = SubjectAccessLevel.GenomicAccess;

    /// <summary>
    /// Gets the username of the administrator who granted access.
    /// </summary>
    [Description("Username of the administrator authorising this group grant.")]
    [JsonPropertyName("grantedBy")]
    public string GrantedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the grant was created.
    /// </summary>
    [Description("UTC timestamp marking grant creation.")]
    [JsonPropertyName("grantedAtUtc")]
    public DateTimeOffset GrantedAtUtc { get; init; }

    /// <summary>
    /// Gets optional clinical trial or supervisory oversight notes.
    /// </summary>
    [Description("Optional supervisory or clinical justification notes.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
