using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Configuration;

/// <summary>
/// Defines the origin source of an application configuration value to make precedence clear.
/// </summary>
public enum ConfigurationSource
{
    /// <summary>
    /// Configuration value is stored in the persistent database and overrides all other sources.
    /// </summary>
    DatabaseOverride,

    /// <summary>
    /// Configuration value is inherited from a container or system environment variable.
    /// </summary>
    EnvironmentVariable,

    /// <summary>
    /// Configuration value is falling back to the built-in system code default.
    /// </summary>
    DefaultFallback
}

/// <summary>
/// Represents a configuration setting in the sovereign nanotech stack, including its active value,
/// default fallback, human-readable description, and current authority source.
/// </summary>
[SwaggerSchema(Title = "ConfigurationItem", Description = "Stack configuration setting with active value, default fallback, and database precedence source.")]
public sealed record ConfigurationItem : IExamplesProvider<ConfigurationItem>
{
    /// <summary>
    /// Unique key identifier for the configuration setting (e.g., 'Simulation:EndpointUrl').
    /// </summary>
    [Description("Unique key identifier for the configuration setting.")]
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>
    /// The currently active value in use by the application.
    /// </summary>
    [Description("The currently active value in use by the application.")]
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>
    /// The built-in default value used when neither the database nor an environment variable provides one.
    /// </summary>
    [Description("The built-in default value.")]
    [JsonPropertyName("defaultValue")]
    public required string DefaultValue { get; init; }

    /// <summary>
    /// Plain-English explanation of what this configuration setting controls in the stack.
    /// </summary>
    [Description("Plain-English explanation of what this configuration setting controls.")]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Indicates whether this value originated from the persistent database, environment variable, or code default.
    /// </summary>
    [Description("Indicates whether this value originated from the database, environment variable, or code default.")]
    [JsonPropertyName("source")]
    public required ConfigurationSource Source { get; init; }

    /// <summary>
    /// Categorical grouping for UI organization (e.g., 'Simulation Engine', 'Storage &amp; Database', 'Peer Collaboration').
    /// </summary>
    [Description("Categorical grouping for UI organization.")]
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>
    /// Timestamp when this setting was last updated in the database, or null if using environment/default.
    /// </summary>
    [Description("Timestamp when this setting was last updated in the database.")]
    [JsonPropertyName("lastUpdatedUtc")]
    public DateTime? LastUpdatedUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="ConfigurationItem"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated configuration setting example.</returns>
    public ConfigurationItem GetExamples() => new()
    {
        Key = "Simulation:AcceleratorBackend",
        Value = "Intel Arc A770 Discrete GPU",
        DefaultValue = "Intel iGPU / OpenCL (Default)",
        Description = "Active hardware accelerator backend for high-throughput trajectory parsing.",
        Source = ConfigurationSource.DatabaseOverride,
        Category = "Simulation Engine",
        LastUpdatedUtc = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc)
    };
}
