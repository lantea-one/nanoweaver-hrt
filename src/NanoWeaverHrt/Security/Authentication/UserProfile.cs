using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Comprehensive demographic, clinical, and contact profile for a registered platform user or patient,
/// hardened under zero-information disclosure and cryptographic privacy standards.
/// </summary>
public sealed record UserProfile : IExamplesProvider<UserProfile>
{
    /// <summary>
    /// Gets or sets the unique username identifying the user account to which this profile belongs.
    /// </summary>
    [Description("Unique username identifying the account principal.")]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's legal or primary first name.
    /// </summary>
    [Description("Legal or primary first name.")]
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's legal or primary last name.
    /// </summary>
    [Description("Legal or primary last name.")]
    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's chosen or preferred name.
    /// </summary>
    [Description("Chosen or preferred name.")]
    [JsonPropertyName("preferredName")]
    public string? PreferredName { get; set; }

    /// <summary>
    /// Gets or sets the user's primary email address.
    /// </summary>
    [Description("Primary email address.")]
    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the user's primary phone number.
    /// </summary>
    [Description("Primary telephone or mobile number.")]
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the user's date of birth in ISO 8601 (YYYY-MM-DD) format.
    /// </summary>
    [Description("Date of birth in ISO 8601 (YYYY-MM-DD) format.")]
    [JsonPropertyName("dateOfBirth")]
    public string? DateOfBirth { get; set; }

    /// <summary>
    /// Gets or sets the user's profile avatar picture stored as an encrypted data-URI payload.
    /// </summary>
    [Description("Profile avatar image stored as an encrypted data-URI payload.")]
    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    /// <summary>
    /// Gets or sets the target therapeutic hormone regimen (e.g., MaleToFemale, FemaleToMale, or null if unassigned/researcher).
    /// </summary>
    [Description("Target therapeutic hormone regimen (MaleToFemale, FemaleToMale, or null).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen? Regimen { get; set; }

    /// <summary>
    /// Gets or sets the biological sex or gender assigned at birth for endocrine pharmacokinetics.
    /// </summary>
    [Description("Biological sex or gender assigned at birth (Female, Male).")]
    [JsonPropertyName("genderAssignedAtBirth")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GenderAssignedAtBirth GenderAssignedAtBirth { get; set; } = GenderAssignedAtBirth.Female;

    /// <summary>
    /// Gets or sets the user's personal pronouns (e.g., [\"she\", \"her\"], [\"they\", \"them\"]).
    /// </summary>
    [Description("Array of personal pronouns.")]
    [JsonPropertyName("pronouns")]
    public string[] Pronouns { get; set; } = [];

    /// <summary>
    /// Gets or sets emergency medical notes, allergies, or endocrine contraindications.
    /// </summary>
    [Description("Emergency medical notes, clinical contraindications, or allergies.")]
    [JsonPropertyName("emergencyMedicalNotes")]
    public string? EmergencyMedicalNotes { get; set; }

    /// <summary>
    /// Gets or sets the user biography or clinical notes.
    /// </summary>
    [Description("User biography or administrative notes.")]
    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    /// <summary>
    /// Gets or sets the preferred language or locale identifier (e.g., \"en-US\").
    /// </summary>
    [Description("Preferred language or locale identifier.")]
    [JsonPropertyName("languagePreference")]
    public string LanguagePreference { get; set; } = "en-US";

    /// <summary>
    /// Gets or sets the timestamp when this profile was created.
    /// </summary>
    [Description("UTC timestamp marking profile creation.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when this profile was last modified.
    /// </summary>
    [Description("UTC timestamp marking last modification.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public UserProfile GetExamples() => new()
    {
        Username = "patient-jane",
        FirstName = "Jane",
        LastName = "Doe",
        PreferredName = "Jane",
        EmailAddress = "jane.doe@example.org",
        PhoneNumber = "+1-555-0199",
        DateOfBirth = "1998-05-14",
        Picture = "data:application/encrypted;base64,enc:AES256GCM:examplepayload",
        Regimen = TherapeuticRegimen.MaleToFemale,
        GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
        Pronouns = ["she", "her"],
        EmergencyMedicalNotes = "No known drug allergies. Penicillin safe.",
        Bio = "Patient participating in sovereign targeted nanocarrier hormone therapy trial.",
        LanguagePreference = "en-US",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}
