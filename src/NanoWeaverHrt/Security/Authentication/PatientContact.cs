using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Contact lookup entity representing trusted individuals, caregivers, advocates, or emergency contacts
/// to reach out to if a patient cannot be reached.
/// </summary>
public sealed record PatientContact : IExamplesProvider<PatientContact>
{
    /// <summary>
    /// Gets or sets the unique identifier for this contact entry.
    /// </summary>
    [Description("Unique GUID identifier for the contact entry.")]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the username of the patient associated with this contact.
    /// </summary>
    [Description("Username of the patient associated with this contact.")]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full name of the contact person.
    /// </summary>
    [Description("Full name of the contact person.")]
    [JsonPropertyName("contactName")]
    public string ContactName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the relationship of this contact to the patient (e.g., Spouse, Partner, Parent, Sibling, Friend, Legal Guardian, Caregiver, Physician).
    /// </summary>
    [Description("Relationship to patient (Spouse, Partner, Parent, Sibling, Friend, Legal Guardian, Caregiver, Physician).")]
    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "Partner";

    /// <summary>
    /// Gets or sets the primary telephone or mobile phone number for this contact.
    /// </summary>
    [Description("Telephone number for reaching this contact.")]
    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the email address for this contact.
    /// </summary>
    [Description("Optional email address for this contact.")]
    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the priority sequence order for reaching this contact (1 = first to call, 2 = secondary, etc.).
    /// </summary>
    [Description("Priority order for contact outreach (1 = first, 2 = second, etc.).")]
    [JsonPropertyName("priorityOrder")]
    public int PriorityOrder { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether care providers are authorized to discuss medical information with this contact under HIPAA/GDPR.
    /// </summary>
    [Description("Authorization flag indicating whether medical information may be discussed with this contact.")]
    [JsonPropertyName("canDiscussMedicalInfo")]
    public bool CanDiscussMedicalInfo { get; set; }

    /// <summary>
    /// Gets or sets optional notes or instructions (e.g. \"Available after 5pm\", \"Speaks Spanish\").
    /// </summary>
    [Description("Optional notes or outreach instructions.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation timestamp.
    /// </summary>
    [Description("UTC timestamp marking creation.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the UTC last update timestamp.
    /// </summary>
    [Description("UTC timestamp marking last modification.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public PatientContact GetExamples() => new()
    {
        Id = "cont-103940294",
        Username = "patient-jane",
        ContactName = "Alex Smith",
        Relationship = "Partner",
        PhoneNumber = "+1-555-0142",
        EmailAddress = "alex.smith@example.org",
        PriorityOrder = 1,
        CanDiscussMedicalInfo = true,
        Notes = "Primary emergency contact. Authorized to receive lab updates if unreachable.",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}
