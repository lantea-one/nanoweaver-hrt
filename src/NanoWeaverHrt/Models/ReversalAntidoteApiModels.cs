using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload configuring synthesis and ML-DSA-65 cryptographic pairing of a toehold strand-displacement
/// reversal antidote against a primary nanocarrier fabrication lot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Input contract for <c>POST /api/fabrication/lots/{lotId}/antidote</c> specifying
/// optional custom target aptamer lock bases, exposed single-stranded toehold length, stoichiometric excess ratio, and stock molarity.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P231): Synthesizes patient- and batch-matched invading ssDNA oligos
/// engineered to disassemble nanocarrier cages rapidly on emergency administration.
/// </para>
/// <para>
/// <strong>How it works:</strong> Validated and processed deterministically by
/// <see cref="NanoWeaverHrt.Fabrication.IReversalAntidoteService"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PairReversalAntidoteRequest", Description = "Parameters configuring synthesis and ML-DSA-65 pairing of a lot-specific ssDNA reversal antidote.")]
public sealed record PairReversalAntidoteRequest : IExamplesProvider<PairReversalAntidoteRequest>
{
    /// <summary>
    /// Gets or initializes an optional custom 5'→3' aptamer lock DNA sequence (18–80 nt of A/C/G/T).
    /// When omitted, a deterministic target lock sequence is derived from the fabrication lot identity and regimen.
    /// </summary>
    [Description("Optional custom 5'-to-3' target aptamer lock ssDNA sequence (18 to 80 A/C/G/T nucleotides).")]
    [JsonPropertyName("targetAptamerLockSequence5To3")]
    public string? TargetAptamerLockSequence5To3 { get; init; }

    /// <summary>
    /// Gets or initializes the exposed single-stranded toehold domain length in nucleotides (6 to 12 nt; default 8).
    /// </summary>
    [Description("Single-stranded toehold overhang length in nucleotides (6 to 12; default 8).")]
    [JsonPropertyName("toeholdLengthNt")]
    public int ToeholdLengthNt { get; init; } = 8;

    /// <summary>
    /// Gets or initializes the stoichiometric antidote-to-aptamer molar excess ratio (1.5 to 20.0; default 5.0).
    /// </summary>
    [Description("Target stoichiometric antidote-to-aptamer molar excess ratio (1.5 to 20.0; default 5.0).")]
    [JsonPropertyName("molarExcessRatio")]
    public double MolarExcessRatio { get; init; } = 5.0;

    /// <summary>
    /// Gets or initializes the sterile aqueous injection stock concentration in micromolar (10.0 to 1000.0 µM; default 100.0).
    /// </summary>
    [Description("Antidote sterile aqueous formulation concentration in micromolar (10.0 to 1000.0 µM; default 100.0).")]
    [JsonPropertyName("stockConcentrationMicromolar")]
    public double StockConcentrationMicromolar { get; init; } = 100.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="PairReversalAntidoteRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated reversal antidote pairing request example.</returns>
    public PairReversalAntidoteRequest GetExamples() => new()
    {
        TargetAptamerLockSequence5To3 = "ATGCATGCTAGCTAGCTAGCTAGCATGC",
        ToeholdLengthNt = 8,
        MolarExcessRatio = 5.0,
        StockConcentrationMicromolar = 100.0
    };
}

/// <summary>
/// Request payload configuring an in-silico 24-hour strand-displacement neutralization kinetics simulation.
/// </summary>
[SwaggerSchema(Title = "SimulateNeutralizationRequest", Description = "Optional dose mass and time horizon for simulating 24-hour depot neutralization kinetics.")]
public sealed record SimulateNeutralizationRequest : IExamplesProvider<SimulateNeutralizationRequest>
{
    /// <summary>
    /// Gets or initializes an optional simulated antidote bolus mass in milligrams (0.05 to 250.0 mg).
    /// When omitted, defaults to the paired 5:1 molar-excess stoichiometric recipe mass.
    /// </summary>
    [Description("Optional rescue antidote bolus dose in milligrams (0.05 to 250.0 mg).")]
    [JsonPropertyName("dosageMg")]
    public double? DosageMg { get; init; }

    /// <summary>
    /// Gets or initializes the kinetics simulation horizon in hours (6 to 72 hours; default 24).
    /// </summary>
    [Description("Neutralization kinetics simulation horizon in hours (6 to 72; default 24).")]
    [JsonPropertyName("horizonHours")]
    public int HorizonHours { get; init; } = 24;

    /// <summary>
    /// Generates a representative example instance of <see cref="SimulateNeutralizationRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated neutralization simulation request example.</returns>
    public SimulateNeutralizationRequest GetExamples() => new()
    {
        DosageMg = 1.65,
        HorizonHours = 24
    };
}

/// <summary>
/// Request payload executing emergency clinical administration of a paired ssDNA reversal antidote
/// to irreversibly neutralize a depot fabrication lot.
/// </summary>
[SwaggerSchema(Title = "AdministerAntidoteRequest", Description = "Emergency administration dose and clinical justification neutralizing a paired depot lot.")]
public sealed record AdministerAntidoteRequest : IExamplesProvider<AdministerAntidoteRequest>
{
    /// <summary>
    /// Gets or initializes the administered antidote oligo dose mass in milligrams (0.05 to 250.0 mg).
    /// </summary>
    [Description("Administered antidote oligo mass in milligrams (0.05 to 250.0 mg).")]
    [JsonPropertyName("dosageMg")]
    public double DosageMg { get; init; } = 5.0;

    /// <summary>
    /// Gets or initializes the documented clinical reason triggering emergency neutralization (1 to 1000 characters).
    /// </summary>
    [Description("Plain-English clinical justification triggering irreversible depot reversal.")]
    [JsonPropertyName("clinicalReason")]
    public string ClinicalReason { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="AdministerAntidoteRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated emergency antidote administration request example.</returns>
    public AdministerAntidoteRequest GetExamples() => new()
    {
        DosageMg = 5.0,
        ClinicalReason = "Acute hypersensitivity event necessitating immediate depot arrest."
    };
}
