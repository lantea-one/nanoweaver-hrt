using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Biological sex or gender assigned at birth used for baseline pharmacokinetic, receptor density,
/// and metabolic clearance calculations in hormonal simulation modeling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GenderAssignedAtBirth
{
    /// <summary>
    /// Female sex assigned at birth.
    /// </summary>
    [Description("Female sex assigned at birth.")]
    Female = 1,

    /// <summary>
    /// Male sex assigned at birth.
    /// </summary>
    [Description("Male sex assigned at birth.")]
    Male = 2,

    /// <summary>
    /// Intersex or both biological sexes represented or assigned at birth for mosaic, chimeric, or ambiguous biological modeling edge cases.
    /// </summary>
    [Description("Intersex or both biological sexes represented or assigned at birth.")]
    Both = 3
}
