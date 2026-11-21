using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Categorized physical cold-chain storage tier observed during a wet-lab fabrication lot temperature log entry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThermalStorageTier
{
    /// <summary>
    /// Ultra-low freezer storage (approximately -80°C) for long-term DNA origami depot preservation.
    /// </summary>
    UltraColdMinus80 = 0,

    /// <summary>
    /// Reference frozen clinical storage (approximately -20°C, 365-day nominal shelf-life anchor).
    /// </summary>
    FrozenMinus20 = 1,

    /// <summary>
    /// Standard clinical refrigerator storage (2°C to 8°C) prior to implantation.
    /// </summary>
    Refrigerated2To8 = 2,

    /// <summary>
    /// Controlled ambient preparation window (above 8°C up to 25°C) during sterile clinic staging.
    /// </summary>
    AmbientControlled = 3,

    /// <summary>
    /// Unplanned out-of-spec thermal excursion outside normal cold-chain envelopes.
    /// </summary>
    ThermalExcursion = 4
}

/// <summary>
/// Sequential chain-of-custody lifecycle stage for a physically fabricated nanocarrier HRT batch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustodyStage
{
    /// <summary>
    /// Initial custody anchor recorded when a released wet-lab batch leaves fabrication QC.
    /// </summary>
    WetLabRelease = 0,

    /// <summary>
    /// Handoff to an insulated cold-chain courier or cryogenic shipping container.
    /// </summary>
    ColdChainDispatch = 1,

    /// <summary>
    /// Verified arrival and cold-chain intake at the receiving clinical facility.
    /// </summary>
    ClinicReceipt = 2,

    /// <summary>
    /// Pre-implantation visual, thermal-budget, and cryptographic Certificate of Analysis inspection.
    /// </summary>
    PreImplantInspection = 3,

    /// <summary>
    /// Final clinical subcutaneous administration to the patient (fail-closed gated on potency, custody, and recall status).
    /// </summary>
    PatientAdministration = 4,

    /// <summary>
    /// Terminal quarantine destruction or biohazard disposal; blocks all subsequent custody transitions.
    /// </summary>
    QuarantineDisposal = 5
}

/// <summary>
/// Persistent database record capturing a single temperature-time cold-chain observation for a fabrication lot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A row stored in <c>weaver_lot_thermal_excursions</c> recording sensor temperature,
/// exposure duration, storage tier, and optional AES-256-GCM encrypted operator notes.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P210): Feeds cumulative Arrhenius thermal degradation calculations
/// to determine whether a DNA origami HRT depot retains at least 90% active potency prior to implantation.
/// </para>
/// <para>
/// <strong>How it works:</strong> Appended chronologically via <see cref="NanoWeaverHrt.Storage.IColdChainCustodyRepository"/>
/// with atomic GDPR Article 17 erasure cascade linkage through <c>patient_id</c> and <c>lot_id</c>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ThermalExcursionRecord", Description = "Persisted cold-chain temperature and duration log entry for a fabrication lot.")]
public sealed record ThermalExcursionRecord : IExamplesProvider<ThermalExcursionRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this thermal excursion record.
    /// </summary>
    [Description("Unique identifier of this thermal excursion log entry.")]
    [JsonPropertyName("excursionId")]
    public string ExcursionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the associated wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Associated wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning the associated fabrication lot.
    /// </summary>
    [Description("Patient identifier owning the associated fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when this cold-chain observation was recorded (microsecond-normalized).
    /// </summary>
    [Description("UTC timestamp when this thermal observation was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the observed vial or cold-box temperature in degrees Celsius.
    /// </summary>
    [Description("Observed temperature in degrees Celsius (-196.0 to +121.0 °C).")]
    [JsonPropertyName("temperatureCelsius")]
    public double TemperatureCelsius { get; init; }

    /// <summary>
    /// Gets or initializes the exposure duration in hours at <see cref="TemperatureCelsius"/>.
    /// </summary>
    [Description("Exposure duration in hours at the recorded temperature.")]
    [JsonPropertyName("durationHours")]
    public double DurationHours { get; init; }

    /// <summary>
    /// Gets or initializes the cold-chain storage tier classification for this observation.
    /// </summary>
    [Description("Cold-chain storage tier classification.")]
    [JsonPropertyName("storageTier")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThermalStorageTier StorageTier { get; init; } = ThermalStorageTier.FrozenMinus20;

    /// <summary>
    /// Gets or initializes the calibrated temperature probe or datalogger identifier.
    /// </summary>
    [Description("Calibrated temperature sensor or datalogger identifier.")]
    [JsonPropertyName("sensorId")]
    public string SensorId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional AES-256-GCM encrypted operator notes payload stored at rest.
    /// </summary>
    [Description("Optional AES-256-GCM encrypted operator notes stored at rest.")]
    [JsonPropertyName("notesEncrypted")]
    public string? NotesEncrypted { get; init; }

    /// <summary>
    /// Gets or initializes the authenticated operator username who logged this thermal observation.
    /// </summary>
    [Description("Authenticated operator username who recorded this observation.")]
    [JsonPropertyName("recordedBy")]
    public string RecordedBy { get; init; } = string.Empty;

    /// <inheritdoc/>
    public ThermalExcursionRecord GetExamples() => new()
    {
        ExcursionId = "texc_20261119_001",
        LotId = "lot_20261119_001",
        PatientId = "patient_alice",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T14:30:00.000000Z", CultureInfo.InvariantCulture),
        TemperatureCelsius = 4.0,
        DurationHours = 6.0,
        StorageTier = ThermalStorageTier.Refrigerated2To8,
        SensorId = "NIST-PROBE-ZRH-04",
        NotesEncrypted = null,
        RecordedBy = "dr_lin"
    };
}

/// <summary>
/// Persistent hash-chained database record capturing an immutable chain-of-custody handoff event
/// for a physically fabricated nanocarrier HRT batch.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A tamper-evident row in <c>weaver_lot_custody_handoffs</c> chaining each
/// physical handoff to its predecessor via SHA-256 (<c>chain_prev_sha256</c> -> <c>entry_sha256</c>).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P210): Establishes FDA 21 CFR Part 11 end-to-end physical
/// custody provenance from wet-lab release through cold transit to clinic receipt and patient implantation.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted chronologically via <see cref="NanoWeaverHrt.Storage.IColdChainCustodyRepository"/>
/// with microsecond-normalized timestamps and 4-decimal numeric rounding so SHA-256 verification matches identically across all 4 SQL dialects.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "CustodyHandoffRecord", Description = "Tamper-evident SHA-256 hash-chained chain-of-custody handoff record for a fabrication lot.")]
public sealed record CustodyHandoffRecord : IExamplesProvider<CustodyHandoffRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this custody handoff entry.
    /// </summary>
    [Description("Unique identifier of this custody handoff entry.")]
    [JsonPropertyName("handoffId")]
    public string HandoffId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the associated wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Associated wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning the associated fabrication lot.
    /// </summary>
    [Description("Patient identifier owning the associated fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the physical custody lifecycle stage recorded by this handoff.
    /// </summary>
    [Description("Physical custody lifecycle stage.")]
    [JsonPropertyName("stage")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CustodyStage Stage { get; init; } = CustodyStage.WetLabRelease;

    /// <summary>
    /// Gets or initializes the releasing custodian or courier identifier.
    /// </summary>
    [Description("Releasing custodian, technician, or courier identifier.")]
    [JsonPropertyName("fromActor")]
    public string FromActor { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the receiving custodian, clinic, or clinician identifier.
    /// </summary>
    [Description("Receiving custodian, courier, clinic, or clinician identifier.")]
    [JsonPropertyName("toActor")]
    public string ToActor { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the standardized facility or laboratory site code where the handoff occurred.
    /// </summary>
    [Description("Standardized facility or laboratory site code (e.g. ZRH-LAB-01).")]
    [JsonPropertyName("facilityCode")]
    public string FacilityCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the observed vial temperature in degrees Celsius at the time of handoff (4-decimal rounded).
    /// </summary>
    [Description("Observed temperature in degrees Celsius at the moment of handoff.")]
    [JsonPropertyName("temperatureCelsius")]
    public double TemperatureCelsius { get; init; }

    /// <summary>
    /// Gets or initializes the Arrhenius remaining potency percentage evaluated at the moment of handoff (4-decimal rounded).
    /// </summary>
    [Description("Evaluated Arrhenius remaining potency percentage at the moment of handoff.")]
    [JsonPropertyName("potencyPercentAtHandoff")]
    public double PotencyPercentAtHandoff { get; init; }

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the preceding custody handoff (or 64 zeros for the genesis entry).
    /// </summary>
    [Description("SHA-256 hex digest of the preceding custody entry (64 zeros for the first handoff).")]
    [JsonPropertyName("chainPrevSha256")]
    public string ChainPrevSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the canonical lowercase SHA-256 hex digest committing this handoff to the custody chain.
    /// </summary>
    [Description("Canonical SHA-256 hex digest binding this handoff record to its predecessor.")]
    [JsonPropertyName("entrySha256")]
    public string EntrySha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this custody handoff was recorded.
    /// </summary>
    [Description("Microsecond-normalized UTC timestamp when this custody handoff occurred.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public CustodyHandoffRecord GetExamples() => new()
    {
        HandoffId = "chnd_20261119_001",
        LotId = "lot_20261119_001",
        PatientId = "patient_alice",
        Stage = CustodyStage.ClinicReceipt,
        FromActor = "cold-courier-07",
        ToActor = "clinic_rn_chen",
        FacilityCode = "ZRH-CLINIC-02",
        TemperatureCelsius = -19.8,
        PotencyPercentAtHandoff = 99.72,
        ChainPrevSha256 = new string('0', 64),
        EntrySha256 = "6a2f1d8c9b4e3a7f5c1e0b8a2d9f4c6e3b1a7d8f0c2e4a6b8d1f3a5c7e9b0d2f",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T15:00:00.000000Z", CultureInfo.InvariantCulture)
    };
}
