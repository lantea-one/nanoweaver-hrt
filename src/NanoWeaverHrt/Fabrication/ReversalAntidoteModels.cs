using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Biophysical design bounds, hybridization stoichiometry constants, and cryptographic signature defaults
/// governing toehold strand-displacement reversal antidotes.
/// </summary>
public static class ReversalAntidoteThresholds
{
    /// <summary>
    /// Minimum allowed exposed ssDNA toehold domain length in nucleotides.
    /// </summary>
    public const int MinToeholdLengthNt = 6;

    /// <summary>
    /// Maximum allowed exposed ssDNA toehold domain length in nucleotides.
    /// </summary>
    public const int MaxToeholdLengthNt = 12;

    /// <summary>
    /// Minimum allowed aptamer lock target sequence length in nucleotides.
    /// </summary>
    public const int MinTargetSequenceLengthNt = 18;

    /// <summary>
    /// Maximum allowed aptamer lock target sequence length in nucleotides.
    /// </summary>
    public const int MaxTargetSequenceLengthNt = 80;

    /// <summary>
    /// Minimum permitted stoichiometric antidote-to-aptamer molar excess ratio.
    /// </summary>
    public const double MinMolarExcessRatio = 1.5;

    /// <summary>
    /// Maximum permitted stoichiometric antidote-to-aptamer molar excess ratio.
    /// </summary>
    public const double MaxMolarExcessRatio = 20.0;

    /// <summary>
    /// Minimum permitted antidote injection stock concentration in micromolar.
    /// </summary>
    public const double MinStockConcentrationMicromolar = 10.0;

    /// <summary>
    /// Maximum permitted antidote injection stock concentration in micromolar.
    /// </summary>
    public const double MaxStockConcentrationMicromolar = 1000.0;

    /// <summary>
    /// Minimum simulation horizon in hours.
    /// </summary>
    public const int MinHorizonHours = 6;

    /// <summary>
    /// Maximum simulation horizon in hours.
    /// </summary>
    public const int MaxHorizonHours = 72;

    /// <summary>
    /// Minimum administered or simulated antidote dose mass in milligrams.
    /// </summary>
    public const double MinDosageMg = 0.05;

    /// <summary>
    /// Maximum administered or simulated antidote dose mass in milligrams.
    /// </summary>
    public const double MaxDosageMg = 250.0;

    /// <summary>
    /// Maximum character length permitted for <c>ClinicalReason</c>.
    /// </summary>
    public const int MaxClinicalReasonLength = 1000;

    /// <summary>
    /// Effective aptamer lock site density in micromoles per milligram of encapsulated nanocarrier payload.
    /// </summary>
    public const double AptamerLockMicromolesPerMgPayload = 0.002;

    /// <summary>
    /// Mean sodium-salt ssDNA nucleotide molar weight in Daltons (g/mol per base).
    /// </summary>
    public const double AverageNtMolecularWeightDa = 327.0;

    /// <summary>
    /// Normalized residual fraction (5%) defining clinical endocrine release cessation.
    /// </summary>
    public const double ClearanceThresholdFraction = 0.05;

    /// <summary>
    /// Cryptographic algorithm identifier stamped on antidote pairing attestations.
    /// </summary>
    public const string SignatureAlgorithmName = "ML-DSA-65";
}

/// <summary>
/// Single hourly sample along a modeled toehold strand-displacement depot neutralization curve.
/// </summary>
[SwaggerSchema(Title = "NeutralizationKineticsPoint", Description = "Hourly strand-displacement disassembly and systemic hormone decay sample.")]
public sealed record NeutralizationKineticsPoint
{
    /// <summary>
    /// Gets or initializes elapsed time in hours following antidote injection.
    /// </summary>
    [Description("Elapsed hours after subcutaneous antidote administration.")]
    [JsonPropertyName("hour")]
    public double Hour { get; init; }

    /// <summary>
    /// Gets or initializes remaining intact nanocarrier cage fraction (1.0 -> 0.0).
    /// </summary>
    [Description("Remaining intact hormone-eluting nanocarrier fraction (0.0 to 1.0).")]
    [JsonPropertyName("boundNanocarrierFraction")]
    public double BoundNanocarrierFraction { get; init; }

    /// <summary>
    /// Gets or initializes strand-displaced and neutralized duplex fraction (0.0 -> 1.0).
    /// </summary>
    [Description("Fraction of nanocarrier aptamer locks displaced into inactive waste duplexes (0.0 to 1.0).")]
    [JsonPropertyName("displacedComplexFraction")]
    public double DisplacedComplexFraction { get; init; }

    /// <summary>
    /// Gets or initializes normalized circulating hormone drive percentage relative to therapeutic steady state (100% -> &lt; 5%).
    /// </summary>
    [Description("Normalized circulating hormone output percentage (100% at t=0 decaying toward basal).")]
    [JsonPropertyName("normalizedHormoneOutputPercent")]
    public double NormalizedHormoneOutputPercent { get; init; }

    /// <summary>
    /// Gets or initializes predicted circulating 17β-estradiol concentration in pmol/L at this hour.
    /// </summary>
    [Description("Modeled serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("predictedEstradiolPmolL")]
    public double PredictedEstradiolPmolL { get; init; }

    /// <summary>
    /// Gets or initializes predicted circulating total testosterone concentration in nmol/L at this hour.
    /// </summary>
    [Description("Modeled serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("predictedTestosteroneNmolL")]
    public double PredictedTestosteroneNmolL { get; init; }
}

/// <summary>
/// Sequence-specific wet-lab formulation recipe and hybridization thermodynamics for an invading ssDNA antidote oligo.
/// </summary>
[SwaggerSchema(Title = "ReversalAntidoteRecipe", Description = "Watson-Crick antidote sequence, thermodynamic stability, and 5:1 molar-excess preparation recipe.")]
public sealed record ReversalAntidoteRecipe
{
    /// <summary>
    /// Gets or initializes the target 5'→3' aptamer lock sequence embedded in the primary nanocarrier batch.
    /// </summary>
    [Description("5'-to-3' target aptamer lock ssDNA sequence incorporated into the primary depot lot.")]
    [JsonPropertyName("targetAptamerLockSequence5To3")]
    public string TargetAptamerLockSequence5To3 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complementary invading 5'→3' ssDNA antidote oligo sequence.
    /// </summary>
    [Description("Complementary 5'-to-3' invading ssDNA antidote sequence.")]
    [JsonPropertyName("antidoteSequence5To3")]
    public string AntidoteSequence5To3 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the leading single-stranded toehold initiation domain subsequence (5' end of antidote).
    /// </summary>
    [Description("Exposed single-stranded toehold nucleation sequence initiating branch migration.")]
    [JsonPropertyName("toeholdDomain5To3")]
    public string ToeholdDomain5To3 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the length of <see cref="ToeholdDomain5To3"/> in nucleotides.
    /// </summary>
    [Description("Length of the single-stranded toehold nucleation domain in nucleotides.")]
    [JsonPropertyName("toeholdLengthNt")]
    public int ToeholdLengthNt { get; init; }

    /// <summary>
    /// Gets or initializes the target antidote-to-aptamer molar excess ratio (default 5.0).
    /// </summary>
    [Description("Stoichiometric molar excess ratio of invading antidote over embedded aptamer lock.")]
    [JsonPropertyName("molarExcessRatio")]
    public double MolarExcessRatio { get; init; }

    /// <summary>
    /// Gets or initializes the calculated molecular weight of the ssDNA antidote oligo in kilodaltons (kDa).
    /// </summary>
    [Description("Calculated molecular mass of the synthesized ssDNA oligo in kDa.")]
    [JsonPropertyName("antidoteMolecularWeightKda")]
    public double AntidoteMolecularWeightKda { get; init; }

    /// <summary>
    /// Gets or initializes the recommended standby antidote dose mass in milligrams for the paired lot.
    /// </summary>
    [Description("Stoichiometric standby rescue dose mass in milligrams.")]
    [JsonPropertyName("recommendedAntidoteMassMg")]
    public double RecommendedAntidoteMassMg { get; init; }

    /// <summary>
    /// Gets or initializes the formulated stock solution concentration in micromolar.
    /// </summary>
    [Description("Sterile formulation concentration in micromolar (µM).")]
    [JsonPropertyName("stockConcentrationMicromolar")]
    public double StockConcentrationMicromolar { get; init; }

    /// <summary>
    /// Gets or initializes the injection volume in microliters at <see cref="StockConcentrationMicromolar"/>.
    /// </summary>
    [Description("Calculated rescue injection volume in microliters (µL).")]
    [JsonPropertyName("injectionVolumeMicroliters")]
    public double InjectionVolumeMicroliters { get; init; }

    /// <summary>
    /// Gets or initializes guanine-cytosine nucleotide percentage (0 to 100%).
    /// </summary>
    [Description("GC base percentage of the paired duplex.")]
    [JsonPropertyName("gcContentPercent")]
    public double GcContentPercent { get; init; }

    /// <summary>
    /// Gets or initializes predicted DNA duplex melting temperature Tm in °C at 150 mM physiological Na⁺.
    /// </summary>
    [Description("Predicted antidote-target duplex melting temperature Tm in °C.")]
    [JsonPropertyName("meltingTempCelsius")]
    public double MeltingTempCelsius { get; init; }

    /// <summary>
    /// Gets or initializes standard Gibbs free energy of hybridization ΔG°37 in kcal/mol.
    /// </summary>
    [Description("Standard free energy of hybridization ΔG°37 in kcal/mol.")]
    [JsonPropertyName("bindingDeltaGKcalMol")]
    public double BindingDeltaGKcalMol { get; init; }

    /// <summary>
    /// Gets or initializes the sterile physiological reconstitution buffer specification.
    /// </summary>
    [Description("Sterile physiological reconstitution buffer formulation.")]
    [JsonPropertyName("bufferComposition")]
    public string BufferComposition { get; init; } = "1x PBS, 5 mM Mg²⁺, pH 7.4 (RNase/DNase-free sterile)";
}

/// <summary>
/// Cryptographically verified reversal antidote pairing report returned when pairing or inspecting a lot antidote.
/// </summary>
[SwaggerSchema(Title = "ReversalAntidotePairingReport", Description = "Paired reversal antidote formulation, ML-DSA-65 signature verification status, and neutralization events.")]
public sealed record ReversalAntidotePairingReport : IExamplesProvider<ReversalAntidotePairingReport>
{
    /// <summary>
    /// Gets or initializes the unique reversal antidote identifier.
    /// </summary>
    [Description("Unique identifier of this paired reversal antidote protocol.")]
    [JsonPropertyName("antidoteId")]
    public string AntidoteId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent primary wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Parent primary fabrication lot identifier.")]
    [JsonPropertyName("primaryLotId")]
    public string PrimaryLotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning patient identifier.
    /// </summary>
    [Description("Owning patient identifier.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the primary lot therapeutic hormone regimen.
    /// </summary>
    [Description("Therapeutic endocrine regimen of the paired lot.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the current status of the paired primary fabrication lot.
    /// </summary>
    [Description("Current lifecycle status of the primary fabrication lot.")]
    [JsonPropertyName("primaryLotStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus PrimaryLotStatus { get; init; } = FabricationLotStatus.Released;

    /// <summary>
    /// Gets or initializes the current operational status of the paired reversal antidote.
    /// </summary>
    [Description("Current lifecycle status of the reversal antidote (ActiveStandby, Neutralized, or Revoked).")]
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReversalAntidoteStatus Status { get; init; } = ReversalAntidoteStatus.ActiveStandby;

    /// <summary>
    /// Gets or initializes the oligonucleotide design, thermodynamic stability, and stoichiometric buffer recipe.
    /// </summary>
    [Description("ssDNA antidote sequence, thermodynamics, and stoichiometric preparation recipe.")]
    [JsonPropertyName("recipe")]
    public ReversalAntidoteRecipe Recipe { get; init; } = new();

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the canonical antidote-lot pairing payload.
    /// </summary>
    [Description("Canonical SHA-256 hex digest binding antidote parameters to the primary lot.")]
    [JsonPropertyName("pairedPayloadSha256")]
    public string PairedPayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 pairing signature.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 signature over the canonical pairing payload.")]
    [JsonPropertyName("pairedSignatureBase64")]
    public string PairedSignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the node ML-DSA-65 public signing key identifier.
    /// </summary>
    [Description("Signing node ML-DSA-65 key identifier.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the node Base64-encoded ML-DSA-65 public key matching <see cref="SignerKeyId"/>.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 public key verifying this pairing attestation.")]
    [JsonPropertyName("signerPublicKeyBase64")]
    public string SignerPublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the cryptographic signature algorithm identifier.
    /// </summary>
    [Description("Digital signature algorithm identifier (ML-DSA-65).")]
    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; init; } = ReversalAntidoteThresholds.SignatureAlgorithmName;

    /// <summary>
    /// Gets or initializes a value indicating whether live SHA-256 and ML-DSA-65 signature verification succeeded.
    /// </summary>
    [Description("True when live canonical SHA-256 and ML-DSA-65 signature verification passed cleanly.")]
    [JsonPropertyName("signatureVerified")]
    public bool SignatureVerified { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this antidote pairing was created.
    /// </summary>
    [Description("UTC timestamp when the antidote pairing was persisted and signed.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes all emergency neutralization events recorded against the primary fabrication lot.
    /// </summary>
    [Description("Chronological history of emergency neutralization administrations executed against this lot.")]
    [JsonPropertyName("neutralizationEvents")]
    public IReadOnlyList<LotNeutralizationEventRecord> NeutralizationEvents { get; init; } = [];

    /// <summary>
    /// Generates a representative example instance of <see cref="ReversalAntidotePairingReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated reversal antidote pairing report example.</returns>
    public ReversalAntidotePairingReport GetExamples() => new()
    {
        AntidoteId = "anti_20261124_001",
        PrimaryLotId = "lot_sample_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        PrimaryLotStatus = FabricationLotStatus.Released,
        Status = ReversalAntidoteStatus.ActiveStandby,
        Recipe = new ReversalAntidoteRecipe
        {
            TargetAptamerLockSequence5To3 = "ATGCATGCTAGCTAGCTAGCTAGCATGC",
            AntidoteSequence5To3 = "GCATGCTAGCTAGCTAGCTAGCATGCAT",
            ToeholdDomain5To3 = "GCATGCTA",
            ToeholdLengthNt = 8,
            MolarExcessRatio = 5.0,
            AntidoteMolecularWeightKda = 9.156,
            RecommendedAntidoteMassMg = 1.65,
            StockConcentrationMicromolar = 100.0,
            InjectionVolumeMicroliters = 1800.0,
            GcContentPercent = 50.0,
            MeltingTempCelsius = 69.8,
            BindingDeltaGKcalMol = -42.5
        },
        PairedPayloadSha256 = new string('a', 64),
        PairedSignatureBase64 = "TUwtRFNBLTY1LVNJRw==",
        SignerKeyId = "key_node_001",
        SignerPublicKeyBase64 = "UFVCLUtFWQ==",
        SignatureAlgorithm = ReversalAntidoteThresholds.SignatureAlgorithmName,
        SignatureVerified = true,
        CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero),
        NeutralizationEvents = []
    };
}

/// <summary>
/// Longitudinal strand-displacement depot disassembly and systemic hormone cessation kinetics report.
/// </summary>
[SwaggerSchema(Title = "NeutralizationSimulationReport", Description = "24-hour strand-displacement disassembly and systemic hormone washout kinetics prediction.")]
public sealed record NeutralizationSimulationReport : IExamplesProvider<NeutralizationSimulationReport>
{
    /// <summary>
    /// Gets or initializes the paired reversal antidote identifier evaluated in this simulation.
    /// </summary>
    [Description("Paired reversal antidote identifier.")]
    [JsonPropertyName("antidoteId")]
    public string AntidoteId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target fabrication lot identifier.
    /// </summary>
    [Description("Target primary fabrication lot identifier.")]
    [JsonPropertyName("primaryLotId")]
    public string PrimaryLotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning patient identifier.
    /// </summary>
    [Description("Owning patient identifier.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the modeled hormone replacement regimen.
    /// </summary>
    [Description("Target endocrine regimen of the neutralized lot.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the antidote bolus mass applied in milligrams.
    /// </summary>
    [Description("Applied invading ssDNA antidote bolus dose in milligrams.")]
    [JsonPropertyName("appliedDosageMg")]
    public double AppliedDosageMg { get; init; }

    /// <summary>
    /// Gets or initializes the effective stoichiometric molar excess ratio achieved at <see cref="AppliedDosageMg"/>.
    /// </summary>
    [Description("Effective antidote-to-aptamer molar excess ratio achieved at AppliedDosageMg.")]
    [JsonPropertyName("molarExcessAchieved")]
    public double MolarExcessAchieved { get; init; }

    /// <summary>
    /// Gets or initializes the simulated kinetics horizon in hours.
    /// </summary>
    [Description("Total simulated kinetics horizon in hours.")]
    [JsonPropertyName("horizonHours")]
    public int HorizonHours { get; init; } = 24;

    /// <summary>
    /// Gets or initializes the predicted biological strand-displacement half-life t1/2 in hours.
    /// </summary>
    [Description("Predicted time in hours to disassemble 50% of active nanocarrier depots.")]
    [JsonPropertyName("estimatedHalfLifeHours")]
    public double EstimatedHalfLifeHours { get; init; }

    /// <summary>
    /// Gets or initializes the predicted time in hours until residual hormone elution falls below 5% baseline.
    /// </summary>
    [Description("Predicted hours until systemic hormone output falls below 5% of plateau drive.")]
    [JsonPropertyName("estimatedClearanceHours")]
    public double EstimatedClearanceHours { get; init; }

    /// <summary>
    /// Gets or initializes the percentage reduction in active nanocarrier release achieved at 24 hours.
    /// </summary>
    [Description("Percentage reduction in active hormone release achieved by hour 24.")]
    [JsonPropertyName("twentyFourHourCessationPercent")]
    public double TwentyFourHourCessationPercent { get; init; }

    /// <summary>
    /// Gets or initializes hourly trajectory points across 0 through <see cref="HorizonHours"/>.
    /// </summary>
    [Description("Hourly nanocarrier displacement and systemic hormone washout curve.")]
    [JsonPropertyName("trajectory")]
    public IReadOnlyList<NeutralizationKineticsPoint> Trajectory { get; init; } = [];

    /// <summary>
    /// Gets or initializes the UTC timestamp when this kinetics forecast was computed.
    /// </summary>
    [Description("UTC timestamp when neutralization kinetics were computed.")]
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="NeutralizationSimulationReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated neutralization simulation report example.</returns>
    public NeutralizationSimulationReport GetExamples() => new()
    {
        AntidoteId = "anti_20261124_001",
        PrimaryLotId = "lot_sample_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        AppliedDosageMg = 1.65,
        MolarExcessAchieved = 5.0,
        HorizonHours = 24,
        EstimatedHalfLifeHours = 2.9,
        EstimatedClearanceHours = 12.6,
        TwentyFourHourCessationPercent = 99.6,
        Trajectory =
        [
            new NeutralizationKineticsPoint
            {
                Hour = 0.0,
                BoundNanocarrierFraction = 1.0,
                DisplacedComplexFraction = 0.0,
                NormalizedHormoneOutputPercent = 100.0,
                PredictedEstradiolPmolL = 550.0,
                PredictedTestosteroneNmolL = 1.05
            }
        ],
        GeneratedAtUtc = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)
    };
}

/// <summary>
/// Aggregate response returned after executing an irreversible emergency antidote administration,
/// combining the recorded event, updated antidote state, permanent lot revocation, and 24-hour kinetics.
/// </summary>
[SwaggerSchema(Title = "AntidoteAdministrationResponse", Description = "Complete clinical record returned after emergency antidote depot neutralization.")]
public sealed record AntidoteAdministrationResponse : IExamplesProvider<AntidoteAdministrationResponse>
{
    /// <summary>
    /// Gets or initializes the persisted emergency clinical neutralization event record.
    /// </summary>
    [Description("Recorded emergency clinical neutralization administration event.")]
    [JsonPropertyName("event")]
    public LotNeutralizationEventRecord Event { get; init; } = new();

    /// <summary>
    /// Gets or initializes the updated reversal antidote record (<see cref="ReversalAntidoteStatus.Neutralized"/>).
    /// </summary>
    [Description("Updated reversal antidote record in Neutralized state.")]
    [JsonPropertyName("antidote")]
    public ReversalAntidoteRecord Antidote { get; init; } = new();

    /// <summary>
    /// Gets or initializes the permanently updated lifecycle status of the primary fabrication lot (<see cref="FabricationLotStatus.Revoked"/>).
    /// </summary>
    [Description("Updated primary fabrication lot status (Revoked).")]
    [JsonPropertyName("lotStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus LotStatus { get; init; } = FabricationLotStatus.Revoked;

    /// <summary>
    /// Gets or initializes the permanent local recall tombstone written for the neutralized lot.
    /// </summary>
    [Description("Permanent lot revocation record created upon emergency neutralization.")]
    [JsonPropertyName("revocation")]
    public FabricationLotRevocationRecord Revocation { get; init; } = new();

    /// <summary>
    /// Gets or initializes the 24-hour neutralization decay kinetics computed for the administered bolus dose.
    /// </summary>
    [Description("24-hour post-administration nanocarrier disassembly and hormone washout kinetics.")]
    [JsonPropertyName("kinetics")]
    public NeutralizationSimulationReport Kinetics { get; init; } = new();

    /// <summary>
    /// Generates a representative example instance of <see cref="AntidoteAdministrationResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated antidote administration response example.</returns>
    public AntidoteAdministrationResponse GetExamples() => new()
    {
        Event = new LotNeutralizationEventRecord().GetExamples(),
        Antidote = new ReversalAntidoteRecord().GetExamples() with { Status = ReversalAntidoteStatus.Neutralized },
        LotStatus = FabricationLotStatus.Revoked,
        Revocation = new FabricationLotRevocationRecord().GetExamples(),
        Kinetics = new NeutralizationSimulationReport().GetExamples()
    };
}
