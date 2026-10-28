using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Defines the tiered access level granted to a Researcher over a Patient/Subject.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubjectAccessLevel
{
    /// <summary>
    /// Tier 1: Read-only access to clinical/demographic profile (pronouns, regimen, demographics).
    /// Physical delivery addresses and emergency outreach contacts remain restricted.
    /// </summary>
    ReadAccess = 1,

    /// <summary>
    /// Tier 2: Tier 1 plus full read/write access to digitized DNA origami blueprints, sequence imports,
    /// and analytical/simulation results.
    /// </summary>
    GenomicAccess = 2,

    /// <summary>
    /// Tier 3: Tier 2 plus complete read-only access to clinical delivery addresses and emergency contacts.
    /// Profile edits remain restricted to Administrator or Patient self.
    /// </summary>
    FullReadAccess = 3
}
