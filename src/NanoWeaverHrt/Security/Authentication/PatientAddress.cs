using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Represents a structured residential, postal, clinic, or delivery address for a patient or user.
/// </summary>
public sealed record PatientAddress : IExamplesProvider<PatientAddress>
{
    /// <summary>
    /// Gets or sets the unique identifier for this address entry.
    /// </summary>
    [Description("Unique GUID identifier for the address.")]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the username of the patient or user owning this address.
    /// </summary>
    [Description("Username of the patient owning this address record.")]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the classification or type of address (e.g., Primary, Secondary, ClinicLab, Delivery).
    /// </summary>
    [Description("Address classification (Primary, Secondary, ClinicLab, Delivery).")]
    [JsonPropertyName("addressType")]
    public string AddressType { get; set; } = "Primary";

    /// <summary>
    /// Gets or sets the primary street address line.
    /// </summary>
    [Description("Primary street address line.")]
    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the secondary address line (apartment, suite, building, unit).
    /// </summary>
    [Description("Secondary address line (apartment, suite, unit).")]
    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// Gets or sets the city, municipality, or township.
    /// </summary>
    [Description("City or municipality name.")]
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state, province, county, or region.
    /// </summary>
    [Description("State, province, county, or region.")]
    [JsonPropertyName("stateOrProvince")]
    public string StateOrProvince { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the postal or ZIP code.
    /// </summary>
    [Description("Postal or ZIP code.")]
    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the country name or ISO 3166-1 alpha-2 code.
    /// </summary>
    [Description("Country name or country code.")]
    [JsonPropertyName("country")]
    public string Country { get; set; } = "United States";

    /// <summary>
    /// Gets or sets a value indicating whether this address is designated as the primary address.
    /// </summary>
    [Description("Indicates whether this is the primary designated address.")]
    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; } = true;

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
    public PatientAddress GetExamples() => new()
    {
        Id = "addr-943029402",
        Username = "patient-jane",
        AddressType = "Primary",
        AddressLine1 = "742 Evergreen Terrace",
        AddressLine2 = "Apt 4B",
        City = "Springfield",
        StateOrProvince = "OR",
        PostalCode = "97477",
        Country = "United States",
        IsPrimary = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}
