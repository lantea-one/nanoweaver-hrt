using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Request payload for granting a researcher direct access to a patient or subject.
/// </summary>
[SwaggerSchema(Title = "GrantSubjectAccessRequest", Description = "Request payload for granting a researcher direct tiered access to a patient or subject.")]
public sealed record GrantSubjectAccessRequest : IExamplesProvider<GrantSubjectAccessRequest>
{
    /// <summary>
    /// Gets the username of the researcher receiving access.
    /// </summary>
    [Description("Username of the researcher receiving access.")]
    [JsonPropertyName("researcherUsername")]
    public string ResearcherUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username of the patient or subject being accessed.
    /// </summary>
    [Description("Username of the patient or subject being accessed.")]
    [JsonPropertyName("subjectUsername")]
    public string SubjectUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tiered access level granted (1: ReadAccess, 2: GenomicAccess, 3: FullReadAccess).
    /// </summary>
    [Description("Tiered access level granted (1: ReadAccess, 2: GenomicAccess, 3: FullReadAccess).")]
    [JsonPropertyName("accessLevel")]
    public SubjectAccessLevel AccessLevel { get; init; } = SubjectAccessLevel.GenomicAccess;

    /// <summary>
    /// Gets optional administrative or clinical notes explaining the access justification.
    /// </summary>
    [Description("Optional administrative or clinical notes explaining the access justification.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="GrantSubjectAccessRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated grant request example.</returns>
    public GrantSubjectAccessRequest GetExamples() => new()
    {
        ResearcherUsername = "dr_watson",
        SubjectUsername = "patient_sovereign_01",
        AccessLevel = SubjectAccessLevel.GenomicAccess,
        Notes = "Approved for molecular dynamics origami fabrication trial."
    };
}

/// <summary>
/// Request payload for creating a new patient cohort group.
/// </summary>
[SwaggerSchema(Title = "CreateSubjectGroupRequest", Description = "Request payload for creating a new patient cohort group.")]
public sealed record CreateSubjectGroupRequest : IExamplesProvider<CreateSubjectGroupRequest>
{
    /// <summary>
    /// Gets the name of the cohort group.
    /// </summary>
    [Description("Human-readable name of the cohort group.")]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets an optional description of the study cohort or clinical trial.
    /// </summary>
    [Description("Optional clinical description or study purpose.")]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Gets initial subject usernames to enroll into the cohort upon creation.
    /// </summary>
    [Description("Initial list of patient usernames to enroll into the cohort.")]
    [JsonPropertyName("subjectUsernames")]
    public IReadOnlyList<string>? SubjectUsernames { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreateSubjectGroupRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated cohort group creation request example.</returns>
    public CreateSubjectGroupRequest GetExamples() => new()
    {
        Name = "MtF Micro-Dosing Cohort 2026",
        Description = "Patients receiving 17β-estradiol encapsulated in 6HB nanocarriers.",
        SubjectUsernames = ["patient_001", "patient_002"]
    };
}

/// <summary>
/// Request payload for updating cohort group metadata.
/// </summary>
[SwaggerSchema(Title = "UpdateSubjectGroupRequest", Description = "Request payload for updating cohort group name and description.")]
public sealed record UpdateSubjectGroupRequest : IExamplesProvider<UpdateSubjectGroupRequest>
{
    /// <summary>
    /// Gets the updated name of the cohort group.
    /// </summary>
    [Description("Updated name of the cohort group.")]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the updated description of the cohort group.
    /// </summary>
    [Description("Updated clinical description or trial notes.")]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="UpdateSubjectGroupRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated cohort group update request example.</returns>
    public UpdateSubjectGroupRequest GetExamples() => new()
    {
        Name = "MtF Extended Trial Cohort 2026",
        Description = "Extended trial cohort covering secondary endocrine feedback."
    };
}

/// <summary>
/// Request payload for enrolling a subject into a cohort group.
/// </summary>
[SwaggerSchema(Title = "AddGroupMemberRequest", Description = "Request payload for enrolling a patient into an existing cohort group.")]
public sealed record AddGroupMemberRequest : IExamplesProvider<AddGroupMemberRequest>
{
    /// <summary>
    /// Gets the patient or subject username to enroll into the group.
    /// </summary>
    [Description("Patient username to enroll into the group.")]
    [JsonPropertyName("subjectUsername")]
    public string SubjectUsername { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="AddGroupMemberRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated group membership request example.</returns>
    public AddGroupMemberRequest GetExamples() => new()
    {
        SubjectUsername = "patient_003"
    };
}

/// <summary>
/// Request payload for granting a researcher access over an entire cohort group.
/// </summary>
[SwaggerSchema(Title = "GrantGroupAccessRequest", Description = "Request payload for granting a researcher access over an entire cohort group.")]
public sealed record GrantGroupAccessRequest : IExamplesProvider<GrantGroupAccessRequest>
{
    /// <summary>
    /// Gets the username of the researcher receiving group access.
    /// </summary>
    [Description("Username of the researcher receiving cohort group access.")]
    [JsonPropertyName("researcherUsername")]
    public string ResearcherUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tiered access level granted over all group members.
    /// </summary>
    [Description("Tiered access level granted over group members (1: ReadAccess, 2: GenomicAccess, 3: FullReadAccess).")]
    [JsonPropertyName("accessLevel")]
    public SubjectAccessLevel AccessLevel { get; init; } = SubjectAccessLevel.GenomicAccess;

    /// <summary>
    /// Gets optional administrative or trial protocol notes.
    /// </summary>
    [Description("Optional administrative or trial protocol notes.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="GrantGroupAccessRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated group grant request example.</returns>
    public GrantGroupAccessRequest GetExamples() => new()
    {
        ResearcherUsername = "dr_watson",
        AccessLevel = SubjectAccessLevel.GenomicAccess,
        Notes = "Assigned as co-investigator on MtF delivery trial."
    };
}

/// <summary>
/// Represents an accessible subject record with the caller's effective access tier.
/// </summary>
[SwaggerSchema(Title = "AccessibleSubjectItem", Description = "Patient identifier paired with the caller's effective access level.")]
public sealed record AccessibleSubjectItem : IExamplesProvider<AccessibleSubjectItem>
{
    /// <summary>
    /// Gets the patient or subject username.
    /// </summary>
    [Description("Patient username.")]
    [JsonPropertyName("subjectUsername")]
    public string SubjectUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the caller's effective access tier over this subject.
    /// </summary>
    [Description("Effective access level (1: ReadAccess, 2: GenomicAccess, 3: FullReadAccess).")]
    [JsonPropertyName("effectiveLevel")]
    public SubjectAccessLevel EffectiveLevel { get; init; } = SubjectAccessLevel.ReadAccess;

    /// <summary>
    /// Generates a representative example instance of <see cref="AccessibleSubjectItem"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated accessible subject item example.</returns>
    public AccessibleSubjectItem GetExamples() => new()
    {
        SubjectUsername = "patient_001",
        EffectiveLevel = SubjectAccessLevel.GenomicAccess
    };
}
