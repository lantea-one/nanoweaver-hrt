using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Represents a direct access grant assigning a Researcher permissions over a specific Patient/Subject.
/// </summary>
public sealed record SubjectGrant
{
    /// <summary>
    /// Gets the unique grant record identifier.
    /// </summary>
    [Description("Unique identifier for this direct subject access grant.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username of the researcher receiving access permissions.
    /// </summary>
    [Description("Username of the researcher receiving access.")]
    [JsonPropertyName("researcherUsername")]
    public string ResearcherUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username of the patient or subject being accessed.
    /// </summary>
    [Description("Username of the subject/patient whose records are accessed.")]
    [JsonPropertyName("subjectUsername")]
    public string SubjectUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tiered access level governing permitted actions and records.
    /// </summary>
    [Description("Configurable tiered access level (ReadAccess, GenomicAccess, FullReadAccess).")]
    [JsonPropertyName("accessLevel")]
    public SubjectAccessLevel AccessLevel { get; init; } = SubjectAccessLevel.GenomicAccess;

    /// <summary>
    /// Gets the username of the administrator who granted access.
    /// </summary>
    [Description("Username of the administrator authorising this grant.")]
    [JsonPropertyName("grantedBy")]
    public string GrantedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the grant was created.
    /// </summary>
    [Description("UTC timestamp marking grant creation.")]
    [JsonPropertyName("grantedAtUtc")]
    public DateTimeOffset GrantedAtUtc { get; init; }

    /// <summary>
    /// Gets optional clinical trial or supervisory justification notes.
    /// </summary>
    [Description("Optional justification or clinical trial oversight notes.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
