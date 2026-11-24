using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload configuring a 1-to-10-year coupled hydrogel-to-epigenetic endocrine durability simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Input contract submitted to <c>POST /api/depots/{depotId}/simulate-multiyear</c>
/// specifying cellular turnover kinetics, DNMT1 maintenance methylation fidelity, progenitor transduction fraction,
/// and optional linkage to an evaluated or approved epigenetic toggle circuit.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P226): Models the biological handoff from a finite 180-day polymer
/// depot into self-sustaining epigenetically reprogrammed steroidogenic tissues over years of mitotic cell division.
/// </para>
/// <para>
/// <strong>How it works:</strong> Validated by <see cref="NanoWeaverHrt.Endocrine.IMultiYearHomeostasisService"/>
/// prior to deterministic RK4 longitudinal integration and atomic persistence.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SimulateMultiYearHomeostasisRequest", Description = "Parameters configuring a 1-to-10-year coupled hydrogel-to-epigenetic mitotic durability simulation.")]
public sealed record SimulateMultiYearHomeostasisRequest : IExamplesProvider<SimulateMultiYearHomeostasisRequest>
{
    /// <summary>
    /// Gets or initializes an optional patient-owned epigenetic toggle circuit identifier to couple into endogenous drive.
    /// </summary>
    [Description("Optional identifier of an approved or evaluated epigenetic toggle circuit belonging to the same patient.")]
    [JsonPropertyName("circuitId")]
    public string? CircuitId { get; init; }

    /// <summary>
    /// Gets or initializes the simulation horizon in whole years (1 to 10 years; default 10).
    /// </summary>
    [Description("Longitudinal simulation horizon in years (1 to 10; default 10).")]
    [JsonPropertyName("horizonYears")]
    public int HorizonYears { get; init; } = 10;

    /// <summary>
    /// Gets or initializes per-division DNMT1 maintenance methylation fidelity (0.90 to 0.9999; default 0.9985).
    /// </summary>
    [Description("DNMT1 maintenance methylation fidelity per mitotic division (0.90 to 0.9999).")]
    [JsonPropertyName("mitoticFidelityRate")]
    public double MitoticFidelityRate { get; init; } = 0.9985;

    /// <summary>
    /// Gets or initializes the initial fraction of target steroidogenic progenitor cells transduced (0.10 to 1.0; default 0.85).
    /// </summary>
    [Description("Initial transduced progenitor cell fraction (0.10 to 1.0).")]
    [JsonPropertyName("transducedProgenitorFraction")]
    public double TransducedProgenitorFraction { get; init; } = 0.85;

    /// <summary>
    /// Gets or initializes the slow-cycling quiescent stem cell pool fraction within transduced tissue (0.0 to 0.95; default 0.35).
    /// </summary>
    [Description("Fraction of target niche organized as slow-cycling quiescent progenitors (0.0 to 0.95).")]
    [JsonPropertyName("quiescentStemCellFraction")]
    public double QuiescentStemCellFraction { get; init; } = 0.35;

    /// <summary>
    /// Gets or initializes mean mitotic division interval in days for proliferating target cells (7.0 to 365.0; default 45.0).
    /// </summary>
    [Description("Mean cell-division interval in days for active progenitors (7.0 to 365.0).")]
    [JsonPropertyName("tissueDivisionIntervalDays")]
    public double TissueDivisionIntervalDays { get; init; } = 45.0;

    /// <summary>
    /// Gets or initializes the patient-specific pharmacogenomic metabolic clearance multiplier (0.1 to 5.0; default 1.0).
    /// </summary>
    [Description("Patient systemic hormone clearance multiplier (0.1 to 5.0; default 1.0).")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes cooperative reader-writer chromatin reinforcement strength (0.0 to 2.0; default 1.0).
    /// </summary>
    [Description("Cooperative promoter chromatin self-reinforcement multiplier (0.0 to 2.0; default 1.0).")]
    [JsonPropertyName("promoterReinforcementStrength")]
    public double PromoterReinforcementStrength { get; init; } = 1.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="SimulateMultiYearHomeostasisRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated multi-year homeostasis simulation request example.</returns>
    public SimulateMultiYearHomeostasisRequest GetExamples() => new()
    {
        CircuitId = "epicirc_20261123_001",
        HorizonYears = 10,
        MitoticFidelityRate = 0.9985,
        TransducedProgenitorFraction = 0.85,
        QuiescentStemCellFraction = 0.35,
        TissueDivisionIntervalDays = 45.0,
        ClearanceMultiplier = 1.0,
        PromoterReinforcementStrength = 1.0
    };
}
