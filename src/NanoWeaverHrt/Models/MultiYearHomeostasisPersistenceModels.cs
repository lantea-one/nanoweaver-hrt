using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Long-term endocrine durability classification evaluating whether epigenetic reprogramming and
/// hydrogel-to-endogenous handoff sustain target hormone homeostasis across 1–10 years of tissue turnover.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MultiYearDurabilityStatus
{
    /// <summary>
    /// Projected 10-year Time in Target Range (&gt;= 95.0%), Day 90–180 handoff continuity, and methylated progenitor pool remain conserved.
    /// </summary>
    PermanentHomeostasisVerified = 0,

    /// <summary>
    /// Mitotic epigenetic dilution, progenitor under-transduction, or handoff trough drop threatens therapeutic target stability.
    /// </summary>
    MitoticDriftAdvisory = 1
}

/// <summary>
/// Persistent database record capturing a multi-year coupled hydrogel-to-epigenetic endocrine durability forecast run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Durable run header stored in <c>weaver_multiyear_homeostasis_runs</c> linking
/// a semi-annual polymer depot (<c>depot_id</c>) and optional epigenetic toggle circuit (<c>circuit_id</c>)
/// to 1–10 year mitotic inheritance kinetics and projected Time in Target Range (TTR).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P225): Persists longitudinal patient-scoped cellular turnover
/// simulations across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB with GDPR Article 17 erasure support.
/// </para>
/// <para>
/// <strong>How it works:</strong> Saved atomically alongside annual checkpoints via
/// <see cref="NanoWeaverHrt.Storage.IMultiYearHomeostasisRepository"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "MultiYearHomeostasisRunRecord", Description = "Persisted multi-year permanent endocrine homeostasis and mitotic durability forecast header.")]
public sealed record MultiYearHomeostasisRunRecord : IExamplesProvider<MultiYearHomeostasisRunRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this multi-year forecast run.
    /// </summary>
    [Description("Unique identifier of the multi-year homeostasis forecast run.")]
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the semi-annual depot formulation identifier bridging Days 0–180.
    /// </summary>
    [Description("Parent semi-annual depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional linked epigenetic toggle circuit identifier driving permanent steroidogenesis.
    /// </summary>
    [Description("Optional linked epigenetic toggle circuit identifier.")]
    [JsonPropertyName("circuitId")]
    public string? CircuitId { get; init; }

    /// <summary>
    /// Gets or initializes the anonymized patient identifier owning this forecast.
    /// </summary>
    [Description("Patient identifier associated with this multi-year durability forecast.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the total simulated longitudinal horizon in years (1 to 10).
    /// </summary>
    [Description("Simulated longitudinal forecast horizon in years (1 to 10).")]
    [JsonPropertyName("horizonYears")]
    public int HorizonYears { get; init; } = 10;

    /// <summary>
    /// Gets or initializes the DNMT1 maintenance methylation fidelity per mitotic cell division (0.90 to 0.9999).
    /// </summary>
    [Description("DNMT1 maintenance methylation fidelity per mitotic cell division (0.90 to 0.9999).")]
    [JsonPropertyName("mitoticFidelityRate")]
    public double MitoticFidelityRate { get; init; } = 0.9985;

    /// <summary>
    /// Gets or initializes the initial fraction of target progenitor cells epigenetically transduced (0.10 to 1.0).
    /// </summary>
    [Description("Initial transduced progenitor cell fraction (0.10 to 1.0).")]
    [JsonPropertyName("transducedProgenitorFraction")]
    public double TransducedProgenitorFraction { get; init; } = 0.85;

    /// <summary>
    /// Gets or initializes the projected multi-year Time in Target Range (TTR) percentage across the simulated horizon.
    /// </summary>
    [Description("Projected multi-year Time in Target Range percentage (0.0 to 100.0).")]
    [JsonPropertyName("projectedTenYearTtrPercent")]
    public double ProjectedTenYearTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes the overall multi-year durability classification.
    /// </summary>
    [Description("Overall durability classification (PermanentHomeostasisVerified or MitoticDriftAdvisory).")]
    [JsonPropertyName("durabilityStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MultiYearDurabilityStatus DurabilityStatus { get; init; } = MultiYearDurabilityStatus.PermanentHomeostasisVerified;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this forecast run was persisted.
    /// </summary>
    [Description("UTC timestamp when the forecast run was executed and persisted.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="MultiYearHomeostasisRunRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated multi-year homeostasis run record example.</returns>
    public MultiYearHomeostasisRunRecord GetExamples() => new()
    {
        RunId = "myh_20261124_001",
        DepotId = "depot_sample_001",
        CircuitId = "epicirc_20261123_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        HorizonYears = 10,
        MitoticFidelityRate = 0.9985,
        TransducedProgenitorFraction = 0.85,
        ProjectedTenYearTtrPercent = 98.4,
        DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
        CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)
    };
}

/// <summary>
/// Persistent annual checkpoint snapshot along a simulated multi-year endocrine homeostasis trajectory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Yearly summary row stored in <c>weaver_multiyear_homeostasis_checkpoints</c>
/// capturing circulating sex steroids, HPG pituitary gonadotropins, methylated progenitor cell retention,
/// and residual hydrogel contribution from Year 0 through Year 10.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P225): Enables longitudinal inspection of mitotic chromatin memory
/// stability and deterministic chart reconstruction without re-storing 3,650 daily rows per query.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted inside the same atomic transaction as its parent
/// <see cref="MultiYearHomeostasisRunRecord"/> and erased cleanly on patient GDPR Article 17 requests.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "MultiYearHomeostasisCheckpointRecord", Description = "Annual endocrine and chromatin-memory checkpoint along a multi-year homeostasis forecast.")]
public sealed record MultiYearHomeostasisCheckpointRecord : IExamplesProvider<MultiYearHomeostasisCheckpointRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this annual checkpoint row.
    /// </summary>
    [Description("Unique identifier of the annual checkpoint record.")]
    [JsonPropertyName("checkpointId")]
    public string CheckpointId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent forecast run identifier.
    /// </summary>
    [Description("Parent multi-year forecast run identifier.")]
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the anonymized patient identifier owning this checkpoint.
    /// </summary>
    [Description("Patient identifier owning this annual checkpoint.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the elapsed year index (0 for implantation baseline through HorizonYears).
    /// </summary>
    [Description("Elapsed year index from 0 (implantation baseline) through HorizonYears (up to 10).")]
    [JsonPropertyName("yearIndex")]
    public int YearIndex { get; init; }

    /// <summary>
    /// Gets or initializes circulating serum 17β-estradiol concentration in pmol/L at this checkpoint.
    /// </summary>
    [Description("Circulating serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public double EstradiolPmolL { get; init; }

    /// <summary>
    /// Gets or initializes circulating serum total testosterone concentration in nmol/L at this checkpoint.
    /// </summary>
    [Description("Circulating serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Gets or initializes circulating luteinizing hormone (LH) concentration in IU/L at this checkpoint.
    /// </summary>
    [Description("Circulating serum luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("lhIuL")]
    public double LhIuL { get; init; }

    /// <summary>
    /// Gets or initializes circulating follicle-stimulating hormone (FSH) concentration in IU/L at this checkpoint.
    /// </summary>
    [Description("Circulating serum follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("fshIuL")]
    public double FshIuL { get; init; }

    /// <summary>
    /// Gets or initializes the fraction of target progenitors actively preserving the reprogrammed epigenetic mark (0.0 to 1.0).
    /// </summary>
    [Description("Fraction of target progenitors maintaining the reprogrammed epigenetic chromatin mark (0.0 to 1.0).")]
    [JsonPropertyName("methylatedProgenitorFraction")]
    public double MethylatedProgenitorFraction { get; init; }

    /// <summary>
    /// Gets or initializes the fraction of therapeutic hormone drive still contributed by the exogenous polymer depot (0.0 to 1.0).
    /// </summary>
    [Description("Fraction of therapeutic output contributed by exogenous hydrogel depot delivery (0.0 to 1.0).")]
    [JsonPropertyName("hydrogelContributionFraction")]
    public double HydrogelContributionFraction { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="MultiYearHomeostasisCheckpointRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated annual homeostasis checkpoint record example.</returns>
    public MultiYearHomeostasisCheckpointRecord GetExamples() => new()
    {
        CheckpointId = "chk_20261124_y05",
        RunId = "myh_20261124_001",
        PatientId = "pat_001",
        YearIndex = 5,
        EstradiolPmolL = 548.2,
        TestosteroneNmolL = 1.12,
        LhIuL = 0.42,
        FshIuL = 0.78,
        MethylatedProgenitorFraction = 0.836,
        HydrogelContributionFraction = 0.0
    };
}
