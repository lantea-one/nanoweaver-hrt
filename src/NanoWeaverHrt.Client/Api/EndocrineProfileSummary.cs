using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Summary of the active synthetic endocrine regulation setpoints on a node.
/// </summary>
/// <param name="Dominance">Hormonal dominance regime ("Estrogenic" or "Androgenic").</param>
/// <param name="TargetEstradiolPicoMolar">Target 17β-estradiol concentration in picomoles per liter.</param>
/// <param name="TargetTestosteroneNanoMolar">Target total testosterone concentration in nanomoles per liter.</param>
/// <param name="TargetProgesteroneNanoMolar">Target progesterone concentration in nanomoles per liter.</param>
/// <param name="LastUpdatedUtc">Timestamp when this endocrine profile was last updated.</param>
public sealed record EndocrineProfileSummary(
    [property: JsonPropertyName("dominance")] string Dominance,
    [property: JsonPropertyName("targetEstradiolPicoMolar")] double TargetEstradiolPicoMolar,
    [property: JsonPropertyName("targetTestosteroneNanoMolar")] double TargetTestosteroneNanoMolar,
    [property: JsonPropertyName("targetProgesteroneNanoMolar")] double TargetProgesteroneNanoMolar,
    [property: JsonPropertyName("lastUpdatedUtc")] DateTimeOffset LastUpdatedUtc);
