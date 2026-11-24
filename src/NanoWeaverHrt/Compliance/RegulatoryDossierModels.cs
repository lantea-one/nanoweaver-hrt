using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Trust provenance classification returned when verifying a signed FDA IND or EMA IMPD regulatory dossier bundle.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegulatoryDossierTrustSource
{
    /// <summary>
    /// Canonical SHA-256 digest and ML-DSA-65 signature match this local sovereign node's active signing key.
    /// </summary>
    LocalNode = 0,

    /// <summary>
    /// Canonical SHA-256 digest and ML-DSA-65 signature match an active trusted peer laboratory key in <c>weaver_peer_keys</c>.
    /// </summary>
    TrustedPeer = 1,

    /// <summary>
    /// Signature is mathematically valid, but the matching peer laboratory signing key has been permanently revoked.
    /// </summary>
    RevokedSigner = 2,

    /// <summary>
    /// Signature is mathematically valid, but the signer public key is not registered in this node's trusted peer registry.
    /// </summary>
    UntrustedSigner = 3,

    /// <summary>
    /// Canonical SHA-256 manifest digest mismatch or invalid NIST FIPS 204 ML-DSA-65 signature bytes.
    /// </summary>
    InvalidSignature = 4
}

/// <summary>
/// Individual fail-closed readiness gate evaluation contributing to FDA IND or EMA IMPD submission completeness.
/// </summary>
[SwaggerSchema(Title = "DossierReadinessGate", Description = "Single mandatory Quality/CMC, Preclinical/QSP, or Compliance gate evaluation in a regulatory dossier.")]
public sealed record DossierReadinessGate
{
    /// <summary>
    /// Gets or initializes the canonical gate identifier code (e.g. <c>CMC_DNA_BLUEPRINT</c>).
    /// </summary>
    [Description("Unique identifier code of the regulatory readiness gate.")]
    [JsonPropertyName("gateCode")]
    public string GateCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the dossier section name (<c>QualityCmc</c>, <c>PreclinicalQsp</c>, or <c>Compliance</c>).
    /// </summary>
    [Description("Dossier section containing this gate (QualityCmc, PreclinicalQsp, or Compliance).")]
    [JsonPropertyName("section")]
    public string Section { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether the mandatory gate passed fail-closed validation.
    /// </summary>
    [Description("True when the mandatory gate passed cleanly.")]
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    /// <summary>
    /// Gets or initializes the percentage weight contributed by this gate toward the 100-point dossier score.
    /// </summary>
    [Description("Completeness score weight percentage contributed when passed (10.0).")]
    [JsonPropertyName("scoreWeight")]
    public double ScoreWeight { get; init; } = 10.0;

    /// <summary>
    /// Gets or initializes a plain-English explanation of the verified evidence or blocking failure.
    /// </summary>
    [Description("Plain-English summary describing verified evidence or blocking deficiency.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Quality and Chemistry, Manufacturing, and Controls (CMC) evidence section for FDA 21 CFR 312 / EMA IMPD dossiers.
/// </summary>
[SwaggerSchema(Title = "DossierQualityCmcSection", Description = "CMC scaffold DNA, Opentrons digest, 5-gate wet-lab CoA, reversal antidote, and cold-chain custody section.")]
public sealed record DossierQualityCmcSection
{
    /// <summary>
    /// Gets or initializes the identifier of the evaluated digitized DNA nanocarrier blueprint.
    /// </summary>
    [Description("Evaluated digitized DNA nanocarrier blueprint identifier.")]
    [JsonPropertyName("dnaBlueprintId")]
    public string? DnaBlueprintId { get; init; }

    /// <summary>
    /// Gets or initializes the anatomical target tissue of the DNA origami nanocarrier.
    /// </summary>
    [Description("Target anatomical tissue for aptamer-gated delivery.")]
    [JsonPropertyName("targetTissue")]
    public string? TargetTissue { get; init; }

    /// <summary>
    /// Gets or initializes the encapsulated active steroid hormone ligand name.
    /// </summary>
    [Description("Encapsulated steroid ligand (e.g. 17beta-Estradiol or Testosterone).")]
    [JsonPropertyName("activeLigand")]
    public string? ActiveLigand { get; init; }

    /// <summary>
    /// Gets or initializes the folded 3D nanocarrier wireframe or bundle geometry descriptor.
    /// </summary>
    [Description("3D folded DNA origami nanocarrier geometry descriptor.")]
    [JsonPropertyName("nanocarrierGeometry")]
    public string? NanocarrierGeometry { get; init; }

    /// <summary>
    /// Gets or initializes the target aptamer receptor dissociation constant in nanomolar.
    /// </summary>
    [Description("Target aptamer dissociation constant Kd in nanomolar.")]
    [JsonPropertyName("targetAptamerKdNm")]
    public double? TargetAptamerKdNm { get; init; }

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the validated scaffold FASTA sequence.
    /// </summary>
    [Description("Lowercase SHA-256 hex digest of the nanocarrier scaffold FASTA sequence.")]
    [JsonPropertyName("scaffoldFastaSha256")]
    public string? ScaffoldFastaSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the scaffold guanine-cytosine nucleotide composition percentage.
    /// </summary>
    [Description("Scaffold guanine-cytosine content percentage.")]
    [JsonPropertyName("scaffoldGcPercentage")]
    public double? ScaffoldGcPercentage { get; init; }

    /// <summary>
    /// Gets or initializes the evaluated primary wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Primary wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string? LotId { get; init; }

    /// <summary>
    /// Gets or initializes the lifecycle status string of the evaluated fabrication lot.
    /// </summary>
    [Description("Lifecycle status of the evaluated fabrication lot.")]
    [JsonPropertyName("lotStatus")]
    public string? LotStatus { get; init; }

    /// <summary>
    /// Gets or initializes the SHA-256 hex digest of the deterministic Opentrons Protocol API v2 synthesis script.
    /// </summary>
    [Description("SHA-256 hex digest of the Opentrons Protocol API v2 synthesis script.")]
    [JsonPropertyName("opentronsProtocolSha256")]
    public string? OpentronsProtocolSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the canonical SHA-256 digest of the wet-lab Certificate of Analysis payload.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the 5-gate wet-lab Certificate of Analysis.")]
    [JsonPropertyName("lotCertificatePayloadSha256")]
    public string? LotCertificatePayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the wet-lab Certificate of Analysis signature verified cleanly.
    /// </summary>
    [Description("True when the reconstructed 5-gate CoA verifies with ML-DSA-65.")]
    [JsonPropertyName("lotCertificateVerified")]
    public bool LotCertificateVerified { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the lot is flagged as locally or federatedly revoked.
    /// </summary>
    [Description("True when the fabrication lot carries a local or federated recall tombstone.")]
    [JsonPropertyName("lotRevoked")]
    public bool LotRevoked { get; init; }

    /// <summary>
    /// Gets or initializes the identifier of the paired sequence-specific reversal antidote oligo.
    /// </summary>
    [Description("Identifier of the paired ssDNA reversal antidote.")]
    [JsonPropertyName("antidoteId")]
    public string? AntidoteId { get; init; }

    /// <summary>
    /// Gets or initializes the current operational status of the paired reversal antidote.
    /// </summary>
    [Description("Lifecycle status of the paired reversal antidote.")]
    [JsonPropertyName("antidoteStatus")]
    public string? AntidoteStatus { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether live ML-DSA-65 verification of the paired antidote passed.
    /// </summary>
    [Description("True when the paired reversal antidote ML-DSA-65 binding verified cleanly.")]
    [JsonPropertyName("antidoteSignatureVerified")]
    public bool AntidoteSignatureVerified { get; init; }

    /// <summary>
    /// Gets or initializes the canonical SHA-256 hex digest of the paired antidote-lot binding payload.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the reversal antidote pairing record.")]
    [JsonPropertyName("antidotePayloadSha256")]
    public string? AntidotePayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the calculated remaining Arrhenius nanocarrier potency percentage.
    /// </summary>
    [Description("Remaining Arrhenius cold-chain nanocarrier potency percentage.")]
    [JsonPropertyName("coldChainRemainingPotencyPercent")]
    public double? ColdChainRemainingPotencyPercent { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether thermal stability satisfies clinical implantation safety limits.
    /// </summary>
    [Description("True when remaining potency >= 90%, not expired, and no >42°C excursion occurred.")]
    [JsonPropertyName("coldChainSafeForImplantation")]
    public bool ColdChainSafeForImplantation { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the physical custody SHA-256 hash chain verified cleanly.
    /// </summary>
    [Description("True when physical custody handoffs verify cleanly from genesis to current holder.")]
    [JsonPropertyName("custodyChainValid")]
    public bool CustodyChainValid { get; init; }

    /// <summary>
    /// Gets or initializes the total number of physical chain-of-custody handoff events recorded on the lot.
    /// </summary>
    [Description("Count of recorded physical chain-of-custody handoffs.")]
    [JsonPropertyName("custodyHandoffCount")]
    public int CustodyHandoffCount { get; init; }
}

/// <summary>
/// Preclinical and Quantitative Systems Pharmacology (QSP) section covering pharmacogenomics,
/// 180-day depot elution kinetics, epigenetic specificity, and 10-year mitotic durability.
/// </summary>
[SwaggerSchema(Title = "DossierPreclinicalQspSection", Description = "Pharmacogenomic safety, 180-day depot simulation, epigenetic CFD specificity, and 10-year durability evidence.")]
public sealed record DossierPreclinicalQspSection
{
    /// <summary>
    /// Gets or initializes the evaluated patient pharmacogenomics result identifier.
    /// </summary>
    [Description("Patient pharmacogenomics screening result identifier.")]
    [JsonPropertyName("pharmacogenomicsResultId")]
    public string? PharmacogenomicsResultId { get; init; }

    /// <summary>
    /// Gets or initializes the outcome status label reported by the latest pharmacogenomics assessment.
    /// </summary>
    [Description("Outcome status reported by the latest pharmacogenomics assessment.")]
    [JsonPropertyName("pharmacogenomicsStatus")]
    public string? PharmacogenomicsStatus { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether pharmacogenomic screening passed without elevated thrombosis/alert risk.
    /// </summary>
    [Description("True when pharmacogenomic screening reported Optimal, Complete, or Verified safety status.")]
    [JsonPropertyName("pharmacogenomicsCleared")]
    public bool PharmacogenomicsCleared { get; init; }

    /// <summary>
    /// Gets or initializes the evaluated semi-annual hydrogel depot formulation identifier.
    /// </summary>
    [Description("Evaluated semi-annual hydrogel depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string? DepotId { get; init; }

    /// <summary>
    /// Gets or initializes the biocompatible polymer matrix carrier type of the depot formulation.
    /// </summary>
    [Description("Biocompatible polymer matrix type (e.g. PLGA-PEG-PLGA).")]
    [JsonPropertyName("depotPolymerMatrix")]
    public string? DepotPolymerMatrix { get; init; }

    /// <summary>
    /// Gets or initializes the simulated 180-day Time in Target Range (TTR) percentage.
    /// </summary>
    [Description("Simulated 180-day depot Time in Target Range percentage.")]
    [JsonPropertyName("depot180DayTtrPercent")]
    public double? Depot180DayTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether initial 7-day burst release was suppressed below 5%.
    /// </summary>
    [Description("True when 180-day depot simulation verified Day 7 burst suppression.")]
    [JsonPropertyName("depotBurstSuppressed")]
    public bool DepotBurstSuppressed { get; init; }

    /// <summary>
    /// Gets or initializes the evaluated epigenetic reprogramming toggle circuit identifier.
    /// </summary>
    [Description("Evaluated epigenetic steroidogenic toggle circuit identifier.")]
    [JsonPropertyName("epigeneticCircuitId")]
    public string? EpigeneticCircuitId { get; init; }

    /// <summary>
    /// Gets or initializes the safety lifecycle status of the evaluated epigenetic toggle circuit.
    /// </summary>
    [Description("Lifecycle approval status of the evaluated epigenetic circuit.")]
    [JsonPropertyName("epigeneticCircuitStatus")]
    public string? EpigeneticCircuitStatus { get; init; }

    /// <summary>
    /// Gets or initializes the lowest genome-wide CFD off-target specificity score across guide RNAs.
    /// </summary>
    [Description("Minimum genome-wide CFD off-target specificity score percentage.")]
    [JsonPropertyName("epigeneticMinSpecificityPercent")]
    public double? EpigeneticMinSpecificityPercent { get; init; }

    /// <summary>
    /// Gets or initializes the normalized bistable separatrix stability margin of the circuit.
    /// </summary>
    [Description("Normalized phase-plane separatrix margin of the bistable circuit.")]
    [JsonPropertyName("epigeneticSeparatrixMargin")]
    public double? EpigeneticSeparatrixMargin { get; init; }

    /// <summary>
    /// Gets or initializes the evaluated multi-year coupled durability forecast run identifier.
    /// </summary>
    [Description("Latest multi-year coupled homeostasis forecast run identifier.")]
    [JsonPropertyName("multiYearRunId")]
    public string? MultiYearRunId { get; init; }

    /// <summary>
    /// Gets or initializes the longitudinal durability status classification (<c>PermanentHomeostasisVerified</c> or <c>MitoticDriftAdvisory</c>).
    /// </summary>
    [Description("Longitudinal durability status classification.")]
    [JsonPropertyName("multiYearDurabilityStatus")]
    public string? MultiYearDurabilityStatus { get; init; }

    /// <summary>
    /// Gets or initializes the projected multi-year Time in Target Range (TTR) percentage.
    /// </summary>
    [Description("Projected multi-year Time in Target Range percentage.")]
    [JsonPropertyName("multiYearProjectedTtrPercent")]
    public double? MultiYearProjectedTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes the simulated multi-year horizon in years (1 to 10).
    /// </summary>
    [Description("Simulated longitudinal horizon duration in years.")]
    [JsonPropertyName("multiYearHorizonYears")]
    public int? MultiYearHorizonYears { get; init; }
}

/// <summary>
/// Regulatory compliance section documenting HIPAA Safe Harbor pseudonymization, CARPA/CTCAE pharmacovigilance,
/// and continuous FDA 21 CFR Part 11 SHA-384 Merkle audit integrity.
/// </summary>
[SwaggerSchema(Title = "DossierComplianceSection", Description = "HIPAA Safe Harbor pseudonymization proof, pharmacovigilance posture, and 21 CFR Part 11 audit ledger proof.")]
public sealed record DossierComplianceSection
{
    /// <summary>
    /// Gets or initializes the deterministic HIPAA Safe Harbor (§ 164.514) subject pseudonym token.
    /// </summary>
    [Description("HIPAA Safe Harbor pseudonymized subject token (sub_...) replacing raw patient identifiers.")]
    [JsonPropertyName("hipaaSafeHarborPseudonym")]
    public string HipaaSafeHarborPseudonym { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether HIPAA Safe Harbor de-identification succeeded cleanly.
    /// </summary>
    [Description("True when subject identifiers were scrubbed to an irreversible HIPAA Safe Harbor pseudonym.")]
    [JsonPropertyName("hipaaDeIdentificationVerified")]
    public bool HipaaDeIdentificationVerified { get; init; }

    /// <summary>
    /// Gets or initializes the aggregate post-implantation pharmacovigilance posture label.
    /// </summary>
    [Description("Post-implantation pharmacovigilance safety posture.")]
    [JsonPropertyName("pharmacovigilancePosture")]
    public string? PharmacovigilancePosture { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether severe CTCAE or CARPA/TLR9 signals triggered lot quarantine.
    /// </summary>
    [Description("True when clinical pharmacovigilance signals triggered fail-closed lot quarantine.")]
    [JsonPropertyName("pharmacovigilanceQuarantineTriggered")]
    public bool PharmacovigilanceQuarantineTriggered { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether emergency lot recall is recommended by pharmacovigilance.
    /// </summary>
    [Description("True when clinical pharmacovigilance signals recommend an emergency recall.")]
    [JsonPropertyName("pharmacovigilanceEmergencyRecallRecommended")]
    public bool PharmacovigilanceEmergencyRecallRecommended { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the entire 21 CFR Part 11 audit ledger verified cleanly.
    /// </summary>
    [Description("True when unbroken SHA-384 chaining and ML-DSA-65 signatures verify across all audit records.")]
    [JsonPropertyName("auditLedgerIntact")]
    public bool AuditLedgerIntact { get; init; }

    /// <summary>
    /// Gets or initializes the total number of audit ledger records cryptographically verified.
    /// </summary>
    [Description("Count of cryptographically verified 21 CFR Part 11 audit ledger records.")]
    [JsonPropertyName("auditLedgerRecordsVerified")]
    public int AuditLedgerRecordsVerified { get; init; }

    /// <summary>
    /// Gets or initializes the 64-character SHA-256 fingerprint of the node key signing the audit ledger.
    /// </summary>
    [Description("SHA-256 fingerprint of the node signing key anchoring the Part 11 audit ledger.")]
    [JsonPropertyName("auditLedgerSigningKeyId")]
    public string AuditLedgerSigningKeyId { get; init; } = string.Empty;
}

/// <summary>
/// Canonical, HIPAA Safe Harbor de-identified regulatory manifest sealed under NIST FIPS 204 ML-DSA-65.
/// Contains zero raw patient usernames or protected direct identifiers.
/// </summary>
[SwaggerSchema(Title = "RegulatoryDossierManifest", Description = "Canonical de-identified FDA IND / EMA IMPD dossier manifest containing CMC, Preclinical/QSP, and Compliance sections.")]
public sealed record RegulatoryDossierManifest
{
    /// <summary>
    /// Gets or initializes the unique regulatory dossier archive identifier.
    /// </summary>
    [Description("Unique identifier of this regulatory dossier manifest.")]
    [JsonPropertyName("dossierId")]
    public string DossierId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target international submission framework (<see cref="RegulatorySubmissionFramework.FdaInd"/> or <see cref="RegulatorySubmissionFramework.EmaImpd"/>).
    /// </summary>
    [Description("Target regulatory submission framework (FdaInd or EmaImpd).")]
    [JsonPropertyName("submissionFramework")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatorySubmissionFramework SubmissionFramework { get; init; } = RegulatorySubmissionFramework.FdaInd;

    /// <summary>
    /// Gets or initializes the HIPAA Safe Harbor pseudonymized subject identifier.
    /// </summary>
    [Description("Irreversible HIPAA Safe Harbor subject pseudonym replacing raw patient identifiers.")]
    [JsonPropertyName("pseudonymizedPatientId")]
    public string PseudonymizedPatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the clinical trial sponsor organization name recorded on the regulatory submission.
    /// </summary>
    [Description("Clinical trial sponsor organization name.")]
    [JsonPropertyName("sponsorName")]
    public string SponsorName { get; init; } = "NanoWeaverHrt Sovereign Research Consortium";

    /// <summary>
    /// Gets or initializes the multi-center clinical trial site code.
    /// </summary>
    [Description("Clinical trial investigative site code.")]
    [JsonPropertyName("clinicalTrialSiteCode")]
    public string ClinicalTrialSiteCode { get; init; } = "SITE-SOVEREIGN-001";

    /// <summary>
    /// Gets or initializes the deterministic 0–100 completeness score across all 10 mandatory gates.
    /// </summary>
    [Description("Dossier readiness completeness score (0.0 to 100.0 percent).")]
    [JsonPropertyName("completenessScore")]
    public double CompletenessScore { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether all 10 fail-closed regulatory gates passed.
    /// </summary>
    [Description("True only when all 10 mandatory CMC, QSP, and Compliance gates pass.")]
    [JsonPropertyName("isSubmissionReady")]
    public bool IsSubmissionReady { get; init; }

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this manifest was compiled.
    /// </summary>
    [Description("Microsecond-normalized UTC timestamp when compilation completed.")]
    [JsonPropertyName("compiledAtUtc")]
    public DateTimeOffset CompiledAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the Quality / Chemistry, Manufacturing, and Controls (CMC) dossier section.
    /// </summary>
    [Description("Quality / Chemistry, Manufacturing, and Controls (CMC) evidence section.")]
    [JsonPropertyName("qualityCmc")]
    public DossierQualityCmcSection QualityCmc { get; init; } = new();

    /// <summary>
    /// Gets or initializes the Preclinical &amp; Quantitative Systems Pharmacology (QSP) dossier section.
    /// </summary>
    [Description("Preclinical and Quantitative Systems Pharmacology (QSP) evidence section.")]
    [JsonPropertyName("preclinicalQsp")]
    public DossierPreclinicalQspSection PreclinicalQsp { get; init; } = new();

    /// <summary>
    /// Gets or initializes the HIPAA, Pharmacovigilance, and 21 CFR Part 11 Compliance section.
    /// </summary>
    [Description("HIPAA Safe Harbor, Pharmacovigilance, and 21 CFR Part 11 compliance section.")]
    [JsonPropertyName("compliance")]
    public DossierComplianceSection Compliance { get; init; } = new();

    /// <summary>
    /// Gets or initializes the ordered list of all 10 evaluated regulatory readiness gates.
    /// </summary>
    [Description("Ordered list of all 10 mandatory readiness gate evaluations.")]
    [JsonPropertyName("readinessGates")]
    public IReadOnlyList<DossierReadinessGate> ReadinessGates { get; init; } = [];

    /// <summary>
    /// Gets or initializes the ordered list of fail-closed blocking deficiency reasons when not submission-ready.
    /// </summary>
    [Description("Blocking regulatory deficiency explanations when IsSubmissionReady is false.")]
    [JsonPropertyName("blockingReasons")]
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];
}

/// <summary>
/// Portable, self-contained FDA IND or EMA IMPD submission bundle cryptographically sealed with
/// a canonical SHA-256 digest and NIST FIPS 204 ML-DSA-65 post-quantum digital signature.
/// </summary>
[SwaggerSchema(Title = "SignedRegulatoryDossierBundle", Description = "Portable ML-DSA-65 signed FDA IND / EMA IMPD electronic submission bundle.")]
public sealed record SignedRegulatoryDossierBundle : IExamplesProvider<SignedRegulatoryDossierBundle>
{
    /// <summary>
    /// Gets or initializes the canonical schema format version identifier of this portable dossier bundle.
    /// </summary>
    [Description("Portable regulatory dossier bundle format version identifier.")]
    [JsonPropertyName("bundleVersion")]
    public string BundleVersion { get; init; } = "nanoweaver-ind-dossier-v1";

    /// <summary>
    /// Gets or initializes the de-identified canonical FDA IND or EMA IMPD regulatory manifest.
    /// </summary>
    [Description("HIPAA-pseudonymized regulatory dossier manifest.")]
    [JsonPropertyName("manifest")]
    public RegulatoryDossierManifest Manifest { get; init; } = new();

    /// <summary>
    /// Gets or initializes the lowercase 64-character SHA-256 hex digest of <see cref="Manifest"/>.
    /// </summary>
    [Description("Canonical lowercase SHA-256 hex digest over the manifest.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the digital signature suite algorithm identifier (<c>ML-DSA-65</c>).
    /// </summary>
    [Description("Cryptographic digital signature algorithm identifier (ML-DSA-65).")]
    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; init; } = "ML-DSA-65";

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 signature over the canonical manifest JSON bytes.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 signature authenticating the canonical manifest.")]
    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the 64-character SHA-256 fingerprint of the signing node key.
    /// </summary>
    [Description("SHA-256 fingerprint identifier of the signing node ML-DSA-65 key.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Base64-encoded SubjectPublicKeyInfo DER ML-DSA-65 public key of the signer.
    /// </summary>
    [Description("Base64-encoded SPKI DER ML-DSA-65 public key verifying the signature.")]
    [JsonPropertyName("signerPublicKeyBase64")]
    public string SignerPublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="SignedRegulatoryDossierBundle"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated signed regulatory dossier bundle example.</returns>
    public SignedRegulatoryDossierBundle GetExamples() => new()
    {
        BundleVersion = "nanoweaver-ind-dossier-v1",
        Manifest = new RegulatoryDossierManifest
        {
            DossierId = "ind_20261124_001",
            SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
            PseudonymizedPatientId = "sub_9f86d081884c7d65",
            SponsorName = "NanoWeaverHrt Sovereign Research Consortium",
            ClinicalTrialSiteCode = "SITE-SOVEREIGN-001",
            CompletenessScore = 100.0,
            IsSubmissionReady = true,
            CompiledAtUtc = new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero),
            QualityCmc = new DossierQualityCmcSection
            {
                DnaBlueprintId = "dna_001",
                TargetTissue = "GonadalTarget",
                ActiveLigand = "17beta-Estradiol",
                NanocarrierGeometry = "Hexagonal Nanotube",
                TargetAptamerKdNm = 4.2,
                ScaffoldFastaSha256 = new string('b', 64),
                ScaffoldGcPercentage = 42.5,
                LotId = "lot_001",
                LotStatus = "Released",
                OpentronsProtocolSha256 = new string('c', 64),
                LotCertificatePayloadSha256 = new string('d', 64),
                LotCertificateVerified = true,
                LotRevoked = false,
                AntidoteId = "anti_001",
                AntidoteStatus = "ActiveStandby",
                AntidoteSignatureVerified = true,
                AntidotePayloadSha256 = new string('e', 64),
                ColdChainRemainingPotencyPercent = 99.5,
                ColdChainSafeForImplantation = true,
                CustodyChainValid = true,
                CustodyHandoffCount = 2
            },
            PreclinicalQsp = new DossierPreclinicalQspSection
            {
                PharmacogenomicsResultId = "res_pgx_001",
                PharmacogenomicsStatus = "Optimal",
                PharmacogenomicsCleared = true,
                DepotId = "depot_001",
                DepotPolymerMatrix = "PLGA-PEG-PLGA",
                Depot180DayTtrPercent = 98.5,
                DepotBurstSuppressed = true,
                EpigeneticCircuitId = "epicirc_001",
                EpigeneticCircuitStatus = "Approved",
                EpigeneticMinSpecificityPercent = 99.1,
                EpigeneticSeparatrixMargin = 0.48,
                MultiYearRunId = "myh_001",
                MultiYearDurabilityStatus = "PermanentHomeostasisVerified",
                MultiYearProjectedTtrPercent = 98.4,
                MultiYearHorizonYears = 10
            },
            Compliance = new DossierComplianceSection
            {
                HipaaSafeHarborPseudonym = "sub_9f86d081884c7d65",
                HipaaDeIdentificationVerified = true,
                PharmacovigilancePosture = "Nominal",
                PharmacovigilanceQuarantineTriggered = false,
                PharmacovigilanceEmergencyRecallRecommended = false,
                AuditLedgerIntact = true,
                AuditLedgerRecordsVerified = 42,
                AuditLedgerSigningKeyId = new string('f', 64)
            },
            ReadinessGates =
            [
                new DossierReadinessGate
                {
                    GateCode = "CMC_DNA_BLUEPRINT",
                    Section = "QualityCmc",
                    Passed = true,
                    ScoreWeight = 10.0,
                    Summary = "Digitized DNA origami scaffold and aptamer dissociation constant validated."
                }
            ],
            BlockingReasons = []
        },
        PayloadSha256 = new string('a', 64),
        SignatureAlgorithm = "ML-DSA-65",
        SignatureBase64 = "TUwtRFNBLTY1LURPU1NJRVI=",
        SignerKeyId = new string('f', 64),
        SignerPublicKeyBase64 = "MIIB..."
    };
}

/// <summary>
/// Summary projection of a persisted regulatory dossier snapshot with optional patient-self metadata redaction.
/// </summary>
[SwaggerSchema(Title = "RegulatoryDossierSummary", Description = "Summary metadata of a persisted IND or IMPD regulatory dossier snapshot.")]
public sealed record RegulatoryDossierSummary : IExamplesProvider<RegulatoryDossierSummary>
{
    /// <summary>
    /// Gets or initializes the unique regulatory dossier archive identifier.
    /// </summary>
    [Description("Unique identifier of the compiled regulatory dossier.")]
    [JsonPropertyName("dossierId")]
    public string DossierId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this dossier snapshot.
    /// </summary>
    [Description("Patient identifier associated with this dossier snapshot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional primary wet-lab fabrication lot identifier evaluated in the dossier.
    /// </summary>
    [Description("Optional primary fabrication lot identifier evaluated in the dossier.")]
    [JsonPropertyName("lotId")]
    public string? LotId { get; init; }

    /// <summary>
    /// Gets or initializes the target international regulatory submission framework.
    /// </summary>
    [Description("Target regulatory submission framework (FdaInd or EmaImpd).")]
    [JsonPropertyName("submissionFramework")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatorySubmissionFramework SubmissionFramework { get; init; } = RegulatorySubmissionFramework.FdaInd;

    /// <summary>
    /// Gets or initializes the lowercase SHA-256 hex digest of the canonical dossier manifest.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the compiled dossier manifest.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the signing key identifier (or <c>[REDACTED]</c> in patient-redacted views).
    /// </summary>
    [Description("Signing node ML-DSA-65 key identifier or [REDACTED] for patient self-queries.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the 0–100 completeness score achieved across all 10 mandatory gates.
    /// </summary>
    [Description("Readiness completeness score (0.0 to 100.0 percent).")]
    [JsonPropertyName("completenessScore")]
    public double CompletenessScore { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether all 10 regulatory readiness gates passed cleanly.
    /// </summary>
    [Description("True when all 10 mandatory gates passed cleanly.")]
    [JsonPropertyName("isSubmissionReady")]
    public bool IsSubmissionReady { get; init; }

    /// <summary>
    /// Gets or initializes the compiling administrator username (or <c>[REDACTED]</c> in patient-redacted views).
    /// </summary>
    [Description("Compiling administrator username or [REDACTED] for patient self-queries.")]
    [JsonPropertyName("compiledBy")]
    public string CompiledBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when this dossier snapshot was compiled.
    /// </summary>
    [Description("UTC timestamp when the regulatory dossier was compiled.")]
    [JsonPropertyName("compiledAtUtc")]
    public DateTimeOffset CompiledAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether sensitive operator and node key metadata was redacted for patient access.
    /// </summary>
    [Description("True when operator and signing-key metadata were redacted for patient-self viewing.")]
    [JsonPropertyName("isRedactedForPatient")]
    public bool IsRedactedForPatient { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RegulatoryDossierSummary"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated regulatory dossier summary example.</returns>
    public RegulatoryDossierSummary GetExamples() => new()
    {
        DossierId = "ind_20261124_001",
        PatientId = "pat_001",
        LotId = "lot_001",
        SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
        PayloadSha256 = new string('a', 64),
        SignerKeyId = new string('f', 64),
        CompletenessScore = 100.0,
        IsSubmissionReady = true,
        CompiledBy = "admin_lead",
        CompiledAtUtc = new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero),
        IsRedactedForPatient = false
    };
}

/// <summary>
/// Cross-node cryptographic verification report evaluating canonical SHA-256 digest integrity,
/// NIST FIPS 204 ML-DSA-65 signature validity, and local or trusted peer laboratory key status.
/// </summary>
[SwaggerSchema(Title = "RegulatoryDossierVerificationReport", Description = "Verification report for an imported or archived signed FDA IND / EMA IMPD dossier bundle.")]
public sealed record RegulatoryDossierVerificationReport : IExamplesProvider<RegulatoryDossierVerificationReport>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the evaluated regulatory dossier.
    /// </summary>
    [Description("Evaluated regulatory dossier identifier.")]
    [JsonPropertyName("dossierId")]
    public string DossierId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the regulatory framework carried inside the verified bundle manifest.
    /// </summary>
    [Description("Target regulatory submission framework inside the dossier manifest.")]
    [JsonPropertyName("submissionFramework")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatorySubmissionFramework SubmissionFramework { get; init; } = RegulatorySubmissionFramework.FdaInd;

    /// <summary>
    /// Gets or initializes the recomputed canonical lowercase SHA-256 hex digest of the manifest.
    /// </summary>
    [Description("Recomputed lowercase SHA-256 hex digest over the manifest.")]
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether the manifest canonical digest matches <c>PayloadSha256</c>.
    /// </summary>
    [Description("True when the recomputed manifest SHA-256 digest matches bundle payloadSha256.")]
    [JsonPropertyName("hashValid")]
    public bool HashValid { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the ML-DSA-65 digital signature verifies mathematically.
    /// </summary>
    [Description("True when NIST FIPS 204 ML-DSA-65 signature verification passed.")]
    [JsonPropertyName("signatureValid")]
    public bool SignatureValid { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the bundle is both cryptographically valid and anchored to an active trusted key.
    /// </summary>
    [Description("True only when hash, ML-DSA-65 signature, and active node/peer trust anchor all verify cleanly.")]
    [JsonPropertyName("isTrusted")]
    public bool IsTrusted { get; init; }

    /// <summary>
    /// Gets or initializes the submission-readiness flag carried inside the verified manifest.
    /// </summary>
    [Description("True if all 10 mandatory gates passed inside the compiled manifest.")]
    [JsonPropertyName("isSubmissionReady")]
    public bool IsSubmissionReady { get; init; }

    /// <summary>
    /// Gets or initializes the resolved trust provenance classification for the signer key.
    /// </summary>
    [Description("Trust provenance classification (LocalNode, TrustedPeer, RevokedSigner, UntrustedSigner, or InvalidSignature).")]
    [JsonPropertyName("trustSource")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegulatoryDossierTrustSource TrustSource { get; init; }

    /// <summary>
    /// Gets or initializes the friendly label of the matched peer laboratory key when signed by a registered peer.
    /// </summary>
    [Description("Label of the matched peer laboratory trust anchor, or null.")]
    [JsonPropertyName("trustedPeerLabel")]
    public string? TrustedPeerLabel { get; init; }

    /// <summary>
    /// Gets or initializes the signer public key fingerprint inspected during verification.
    /// </summary>
    [Description("SHA-256 key identifier declared by the dossier bundle.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a plain-English summary describing verification and trust outcomes.
    /// </summary>
    [Description("Plain-English verification diagnosis and trust summary.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when this cross-node verification completed.
    /// </summary>
    [Description("UTC timestamp when bundle verification executed.")]
    [JsonPropertyName("verifiedAtUtc")]
    public DateTimeOffset VerifiedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RegulatoryDossierVerificationReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated regulatory dossier verification report example.</returns>
    public RegulatoryDossierVerificationReport GetExamples() => new()
    {
        DossierId = "ind_20261124_001",
        SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
        PayloadSha256 = new string('a', 64),
        HashValid = true,
        SignatureValid = true,
        IsTrusted = true,
        IsSubmissionReady = true,
        TrustSource = RegulatoryDossierTrustSource.LocalNode,
        TrustedPeerLabel = null,
        SignerKeyId = new string('f', 64),
        Summary = "Verified ML-DSA-65 regulatory dossier bundle sealed by this sovereign node.",
        VerifiedAtUtc = new DateTimeOffset(2026, 11, 24, 16, 5, 0, TimeSpan.Zero)
    };
}
