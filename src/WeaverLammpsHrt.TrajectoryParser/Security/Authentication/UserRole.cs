using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Authentication;

/// <summary>
/// Defines the authorization roles governing access permissions across the sovereign platform.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    /// <summary>
    /// Full read/write administrative control over system configuration, user management, and compute engines.
    /// </summary>
    Administrator,

    /// <summary>
    /// Research access permitting execution of molecular simulations, trajectory parsing, and analytical workflows.
    /// </summary>
    Researcher,

    /// <summary>
    /// Patient access permitting inspection of targeted simulation results, endocrine profiles, and digital DNA downloads.
    /// </summary>
    Patient
}
