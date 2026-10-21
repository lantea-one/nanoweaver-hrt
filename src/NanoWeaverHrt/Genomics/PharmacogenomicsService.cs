using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NanoWeaverHrt.Endocrine;

namespace NanoWeaverHrt.Genomics;

/// <summary>
/// Implements clinical pharmacogenomic analysis, genetic predisposition evaluation, 
/// severity-tiered call-out alert generation, and personalized kinetic profile adjustment.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This engine scans a patient's genetic markers or DNA sequence,
/// checks for high-impact mutations (like blood clotting variants or hormone clearance mutations),
/// and outputs explicit clinical call-outs with adjusted hormone targets so nanocarrier treatment is safe and effective.
/// </para>
/// </remarks>
public sealed class PharmacogenomicsService : IPharmacogenomicsService
{
    /// <summary>
    /// Thread-safe in-memory cache of curated pharmacogenomic markers indexed by unique marker ID.
    /// </summary>
    private readonly ConcurrentDictionary<string, GeneticMarker> _markerCatalog = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// List of standard clinical genetic test presets for demonstration, testing, and validation.
    /// </summary>
    private readonly List<GeneticMarkerPreset> _presets = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PharmacogenomicsService"/> with authoritative clinical genetic markers.
    /// </summary>
    public PharmacogenomicsService()
    {
        InitializeCatalog();
        InitializePresets();
    }

    /// <summary>
    /// Populates the internal catalog with clinically validated endocrine and nanocarrier genetic markers.
    /// </summary>
    private void InitializeCatalog()
    {
        // ---------------------------------------------------------------------
        // 1. Factor V Leiden (rs6025) - High Thrombotic Risk
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "F5_LEIDEN",
            GeneSymbol = "F5",
            VariantName = "Factor V Leiden (rs6025, c.1691G>A, p.Arg506Gln)",
            Category = GeneticCategory.ThromboticRisk,
            Severity = ClinicalSeverity.Critical,
            ObservedGenotype = "G/A (Heterozygous Carrier)",
            ClinicalDescription = "Resistance to Activated Protein C (APC) prevents inactivation of clotting Factor Va, multiplying venous thromboembolism risk 3x to 8x.",
            RecommendedAction = "In MtF regimens, cap peak 17β-estradiol setpoint to <= 500 pmol/L. Prohibit bolus burst release; enforce zero-pulsatile continuous micro-release kinetics.",
            AssociatedRegimen = TherapeuticRegimen.MaleToFemale,
            ClearanceRateMultiplier = 1.0,
            TargetKdOffsetNanoMolar = 0.0,
            MaxSafeEstradiolPmolL = 500.0,
            ThrombosisRiskMultiplier = 4.2
        });

        // ---------------------------------------------------------------------
        // 2. Prothrombin G20210A (rs1799963) - Thrombotic Risk
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "F2_PROTHROMBIN",
            GeneSymbol = "F2",
            VariantName = "Prothrombin G20210A (rs1799963, 3' UTR)",
            Category = GeneticCategory.ThromboticRisk,
            Severity = ClinicalSeverity.Critical,
            ObservedGenotype = "G/A (Heterozygous Carrier)",
            ClinicalDescription = "Elevated 3' UTR mRNA stability results in 30% higher circulating plasma prothrombin, significantly elevating deep vein thrombosis (DVT) probability.",
            RecommendedAction = "Enforce conservative steady-state estradiol concentration and verify bilateral lower extremity Doppler clearance prior to dose escalation.",
            AssociatedRegimen = TherapeuticRegimen.MaleToFemale,
            ClearanceRateMultiplier = 1.0,
            TargetKdOffsetNanoMolar = 0.0,
            MaxSafeEstradiolPmolL = 480.0,
            ThrombosisRiskMultiplier = 2.8
        });

        // ---------------------------------------------------------------------
        // 3. Androgen Receptor CAG Repeat Elongation - Androgen Resistance
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "AR_CAG_LONG",
            GeneSymbol = "AR",
            VariantName = "Androgen Receptor Exon 1 Polyglutamine Elongation (CAG > 26)",
            Category = GeneticCategory.ReceptorSensitivity,
            Severity = ClinicalSeverity.Warning,
            ObservedGenotype = "CAG Repeat Count: 28 (Extended)",
            ClinicalDescription = "Extended polyglutamine tract impairs p160 co-activator recruitment to the AF-1 transactivation domain, decreasing target gene responsiveness to testosterone.",
            RecommendedAction = "In FtM regimens, increase minimum target testosterone to >= 22.0 nmol/L. Tune nanocarrier aptamer affinity by -1.5 nM (higher affinity) for increased pocket contact frequency.",
            AssociatedRegimen = TherapeuticRegimen.FemaleToMale,
            ClearanceRateMultiplier = 1.0,
            TargetKdOffsetNanoMolar = -1.5,
            MinRecommendedTestosteroneNmolL = 22.0,
            ThrombosisRiskMultiplier = 1.0
        });

        // ---------------------------------------------------------------------
        // 4. Androgen Receptor CAG Repeat Shortening - Androgen Hypersensitivity
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "AR_CAG_SHORT",
            GeneSymbol = "AR",
            VariantName = "Androgen Receptor Exon 1 Polyglutamine Shortening (CAG < 20)",
            Category = GeneticCategory.ReceptorSensitivity,
            Severity = ClinicalSeverity.Advisory,
            ObservedGenotype = "CAG Repeat Count: 16 (Shortened)",
            ClinicalDescription = "Shortened polyglutamine tract enhances ligand-dependent transcriptional transactivation, making target tissues hypersensitive to low circulating androgens.",
            RecommendedAction = "In FtM regimens, moderate target testosterone (14.0 to 18.0 nmol/L) to prevent excessive erythrocytosis, severe cystic acne, or premature androgenic alopecia.",
            AssociatedRegimen = TherapeuticRegimen.FemaleToMale,
            ClearanceRateMultiplier = 1.0,
            TargetKdOffsetNanoMolar = 0.5,
            MinRecommendedTestosteroneNmolL = 14.0,
            ThrombosisRiskMultiplier = 1.0
        });

        // ---------------------------------------------------------------------
        // 5. COMT Val158Met (rs4680) - Slow Catechol-Estrogen Methylation
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "COMT_VAL158MET",
            GeneSymbol = "COMT",
            VariantName = "Catechol-O-Methyltransferase Val158Met (rs4680, p.Val158Met)",
            Category = GeneticCategory.SteroidMetabolism,
            Severity = ClinicalSeverity.Warning,
            ObservedGenotype = "A/A (Met/Met - Low Activity)",
            ClinicalDescription = "3- to 4-fold reduction in enzymatic methylation activity causes accumulation of reactive 2- and 4-hydroxyestrone catechol metabolites and localized oxidative stress.",
            RecommendedAction = "In MtF regimens, slow delivery clearance multiplier to 0.75x. Recommend adjuvant antioxidant supplementation (e.g. N-acetylcysteine) and monitor liver function enzymes.",
            AssociatedRegimen = TherapeuticRegimen.MaleToFemale,
            ClearanceRateMultiplier = 0.75,
            TargetKdOffsetNanoMolar = 0.0,
            ThrombosisRiskMultiplier = 1.2
        });

        // ---------------------------------------------------------------------
        // 6. CYP1B1*3 (rs1056836) - Rapid Estrogen 4-Hydroxylation
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "CYP1B1_RAPID",
            GeneSymbol = "CYP1B1",
            VariantName = "Cytochrome P450 1B1*3 (rs1056836, c.1294C>G, p.Leu432Val)",
            Category = GeneticCategory.SteroidMetabolism,
            Severity = ClinicalSeverity.Advisory,
            ObservedGenotype = "G/G (Homozygous Rapid Hydroxylase)",
            ClinicalDescription = "Enhanced catalytic efficiency preferentially converts estradiol into 4-hydroxyestradiol, accelerating systemic estrogen clearance.",
            RecommendedAction = "In MtF regimens, increase clearance rate multiplier to 1.35x. Monitor E2 serum levels to confirm sustained gonadotropin (LH/FSH) negative feedback suppression.",
            AssociatedRegimen = TherapeuticRegimen.MaleToFemale,
            ClearanceRateMultiplier = 1.35,
            TargetKdOffsetNanoMolar = 0.0,
            ThrombosisRiskMultiplier = 1.0
        });

        // ---------------------------------------------------------------------
        // 7. UGT2B17 Deletion (*2/*2) - Impaired Testosterone Glucuronidation
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "UGT2B17_DELETION",
            GeneSymbol = "UGT2B17",
            VariantName = "UDP-Glucuronosyltransferase 2B17 Null Deletion (CNV *2/*2)",
            Category = GeneticCategory.SteroidMetabolism,
            Severity = ClinicalSeverity.Warning,
            ObservedGenotype = "Del/Del (Homozygous Null)",
            ClinicalDescription = "Complete loss of major hepatic testosterone glucuronidation enzyme prolongs circulating testosterone and DHT half-life by up to 60%.",
            RecommendedAction = "In FtM regimens, decrease clearance rate multiplier to 0.60x. Avoid standard dosage stacking; extend steady-state plateau half-life monitoring.",
            AssociatedRegimen = TherapeuticRegimen.FemaleToMale,
            ClearanceRateMultiplier = 0.60,
            TargetKdOffsetNanoMolar = 0.0,
            ThrombosisRiskMultiplier = 1.1
        });

        // ---------------------------------------------------------------------
        // 8. TLR9 High-Sensitivity Allele (rs187084) - DNA Origami Immunogenicity
        // ---------------------------------------------------------------------
        AddMarker(new GeneticMarker
        {
            MarkerId = "TLR9_HIGH_SENSITIVITY",
            GeneSymbol = "TLR9",
            VariantName = "Toll-Like Receptor 9 Promoter Polymorphism (rs187084, -1486T>C)",
            Category = GeneticCategory.NanocarrierImmunogenicity,
            Severity = ClinicalSeverity.Critical,
            ObservedGenotype = "C/C (High Expression / Hyper-Reactive)",
            ClinicalDescription = "Up-regulated TLR9 receptor expression triggers acute endosomal recognition of unmethylated CpG motifs in viral scaffold DNA (e.g. M13mp18), inducing inflammatory cytokines and rapid macrophage clearance.",
            RecommendedAction = "MANDATORY: Perform full enzymatic CpG methylation (using M.SssI CpG methyltransferase and S-adenosylmethionine) on scaffold DNA before thermal annealing to prevent carrier clearance.",
            AssociatedRegimen = null,
            ClearanceRateMultiplier = 1.20,
            TargetKdOffsetNanoMolar = 0.0,
            RequiresCpGMethylation = true
        });
    }

    /// <summary>
    /// Initializes pre-configured clinical demonstration presets.
    /// </summary>
    private void InitializePresets()
    {
        _presets.Add(new GeneticMarkerPreset
        {
            PresetId = "PRESET_BASELINE",
            Title = "Standard Cis-Typical Baseline (Zero Risk Alleles)",
            Description = "Wild-type baseline genetics with normal steroid metabolism, baseline thrombosis risk, and standard DNA origami tolerance.",
            Regimen = TherapeuticRegimen.MaleToFemale,
            MarkerIds = Array.Empty<string>()
        });

        _presets.Add(new GeneticMarkerPreset
        {
            PresetId = "PRESET_MTF_THROMBOSIS",
            Title = "MtF Patient - Factor V Leiden Heterozygote (High Clot Risk)",
            Description = "Carries Factor V Leiden and COMT slow metabolizer alleles. Requires conservative E2 setpoint capping (<= 500 pmol/L) and continuous non-burst delivery.",
            Regimen = TherapeuticRegimen.MaleToFemale,
            MarkerIds = new[] { "F5_LEIDEN", "COMT_VAL158MET" }
        });

        _presets.Add(new GeneticMarkerPreset
        {
            PresetId = "PRESET_FTM_RESISTANCE",
            Title = "FtM Patient - UGT2B17 Deletion + AR CAG Elongation",
            Description = "Combines receptor insensitivity (CAG > 26) with impaired testosterone clearance (null UGT2B17). Requires higher target T and tighter aptamer Kd.",
            Regimen = TherapeuticRegimen.FemaleToMale,
            MarkerIds = new[] { "UGT2B17_DELETION", "AR_CAG_LONG" }
        });

        _presets.Add(new GeneticMarkerPreset
        {
            PresetId = "PRESET_TLR9_IMMUNOGENIC",
            Title = "Nanocarrier Sensitive - TLR9 High-Sensitivity Variant",
            Description = "Elevated Toll-Like Receptor 9 expression. DNA origami chassis requires enzymatic CpG methylation prior to thermal synthesis to avoid macrophage clearance.",
            Regimen = TherapeuticRegimen.MaleToFemale,
            MarkerIds = new[] { "TLR9_HIGH_SENSITIVITY", "CYP1B1_RAPID" }
        });
    }

    /// <summary>
    /// Adds a marker definition into the thread-safe catalog.
    /// </summary>
    /// <param name="marker">The marker definition to store.</param>
    private void AddMarker(GeneticMarker marker)
    {
        _markerCatalog[marker.MarkerId] = marker;
    }

    /// <inheritdoc />
    public IReadOnlyList<GeneticMarker> GetCatalog()
    {
        return _markerCatalog.Values.OrderBy(m => m.Category).ThenByDescending(m => m.Severity).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<GeneticMarkerPreset> GetPresetProfiles()
    {
        return _presets;
    }

    /// <inheritdoc />
    public PatientGenomicProfile AnalyzeProfile(string patientId, IEnumerable<string> markerIds, TherapeuticRegimen regimen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);
        ArgumentNullException.ThrowIfNull(markerIds);

        // WHAT: Resolves verified markers from the catalog matching requested IDs.
        // WHY:  Translates raw string identifiers into strongly-typed pharmacogenomic models.
        // HOW:  Queries the thread-safe _markerCatalog dictionary.
        List<GeneticMarker> matchedMarkers = new();
        foreach (string id in markerIds)
        {
            if (_markerCatalog.TryGetValue(id.Trim(), out GeneticMarker? marker))
            {
                matchedMarkers.Add(marker);
            }
        }

        // WHAT: Synthesizes actionable call-out alerts and computes aggregate kinetic modifiers.
        // WHY:  Combines multiple genetic effects into a coherent clinical guidance report.
        // HOW:  Evaluates each marker in the context of the active therapeutic regimen.
        List<GeneticAlert> alerts = new();
        double thrombosisRiskIndex = 1.0;
        double overallClearanceMultiplier = 1.0;
        bool cpgMethylationMandatory = false;
        double? recommendedMaxE2 = null;
        double? recommendedMinT = null;
        double recommendedKdOffset = 0.0;

        foreach (GeneticMarker m in matchedMarkers)
        {
            thrombosisRiskIndex *= m.ThrombosisRiskMultiplier;
            overallClearanceMultiplier *= m.ClearanceRateMultiplier;
            recommendedKdOffset += m.TargetKdOffsetNanoMolar;

            if (m.RequiresCpGMethylation)
            {
                cpgMethylationMandatory = true;
            }

            if (m.MaxSafeEstradiolPmolL.HasValue)
            {
                recommendedMaxE2 = recommendedMaxE2.HasValue
                    ? Math.Min(recommendedMaxE2.Value, m.MaxSafeEstradiolPmolL.Value)
                    : m.MaxSafeEstradiolPmolL.Value;
            }

            if (m.MinRecommendedTestosteroneNmolL.HasValue)
            {
                recommendedMinT = recommendedMinT.HasValue
                    ? Math.Max(recommendedMinT.Value, m.MinRecommendedTestosteroneNmolL.Value)
                    : m.MinRecommendedTestosteroneNmolL.Value;
            }

            // Determine if marker is relevant to the active regimen
            bool isRelevant = m.AssociatedRegimen == null || m.AssociatedRegimen == regimen;

            string title = $"{m.GeneSymbol}: {m.VariantName}";
            string callOutMsg = isRelevant
                ? $"[{m.Severity.ToString().ToUpperInvariant()}] {m.ClinicalDescription}"
                : $"[CARRIER ADVISORY] {m.ClinicalDescription} (Latent for {regimen} regimen)";

            string kineticGuidance = m.Category switch
            {
                GeneticCategory.ThromboticRisk => $"Limit peak hormone burst; cap E2 to <= {m.MaxSafeEstradiolPmolL ?? 500} pmol/L.",
                GeneticCategory.SteroidMetabolism => $"Clearance multiplier shifted to {m.ClearanceRateMultiplier:F2}x baseline.",
                GeneticCategory.ReceptorSensitivity => $"Tune aptamer Kd offset by {m.TargetKdOffsetNanoMolar:+0.0;-0.0;0.0} nM.",
                _ => "Standard kinetic parameters maintained."
            };

            string? scaffoldGuidance = m.RequiresCpGMethylation
                ? "Enzymatic CpG methylation (M.SssI) mandatory on scaffold prior to thermal annealing."
                : null;

            alerts.Add(new GeneticAlert
            {
                AlertId = $"ALERT_{m.MarkerId}_{Guid.NewGuid().ToString("N")[..8]}",
                GeneSymbol = m.GeneSymbol,
                VariantName = m.VariantName,
                Severity = isRelevant ? m.Severity : ClinicalSeverity.Advisory,
                Category = m.Category,
                Title = title,
                CallOutMessage = callOutMsg,
                ClinicalGuidance = m.RecommendedAction,
                ScaffoldGuidance = scaffoldGuidance,
                KineticGuidance = kineticGuidance
            });
        }

        // If no markers detected, emit an Optimal baseline notice
        if (matchedMarkers.Count == 0)
        {
            alerts.Add(new GeneticAlert
            {
                AlertId = $"ALERT_BASELINE_{Guid.NewGuid().ToString("N")[..8]}",
                GeneSymbol = "GENOMIC_BASELINE",
                VariantName = "Wild-Type Homogeneous (No High-Risk Variants Detected)",
                Severity = ClinicalSeverity.Optimal,
                Category = GeneticCategory.SteroidMetabolism,
                Title = "Wild-Type Baseline Genome",
                CallOutMessage = "No high-risk thrombosis, hyper-metabolic, or receptor insensitivity mutations detected.",
                ClinicalGuidance = "Standard physiological setpoints and delivery kinetics are appropriate for this subject.",
                ScaffoldGuidance = "Standard folding protocol without mandatory epigenetic methylation.",
                KineticGuidance = "Baseline 0.08 h⁻¹ exponential stabilization rate applied."
            });
        }

        return new PatientGenomicProfile
        {
            PatientId = patientId,
            Regimen = regimen,
            ObservedMarkers = matchedMarkers,
            Alerts = alerts,
            ThrombosisRiskIndex = Math.Round(thrombosisRiskIndex, 2),
            OverallClearanceMultiplier = Math.Round(overallClearanceMultiplier, 2),
            CpGMethylationMandatory = cpgMethylationMandatory,
            RecommendedMaxEstradiolPmolL = recommendedMaxE2,
            RecommendedMinTestosteroneNmolL = recommendedMinT,
            RecommendedKdOffsetNanoMolar = Math.Round(recommendedKdOffset, 2),
            CalculatedAtUtc = DateTime.UtcNow.ToString("o")
        };
    }

    /// <inheritdoc />
    public PatientGenomicProfile AnalyzeSequence(string patientId, string fastaOrDnaSequence, TherapeuticRegimen regimen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        if (string.IsNullOrWhiteSpace(fastaOrDnaSequence))
        {
            return AnalyzeProfile(patientId, Array.Empty<string>(), regimen);
        }

        // WHAT: Strips FASTA header lines and whitespace, converting sequence to uppercase.
        // WHY:  Standardizes sequence processing across multi-line FASTA files.
        // HOW:  Regex removes header line starting with > and all non-alphabetic characters.
        string cleanSeq = Regex.Replace(fastaOrDnaSequence, @"^>.*$", "", RegexOptions.Multiline);
        cleanSeq = Regex.Replace(cleanSeq, @"[^A-Za-z]", "").ToUpperInvariant();

        List<string> detectedMarkerIds = new();

        // 1. Check CpG Dinucleotide Density (TLR9 Immunogenicity Trigger)
        // WHAT: Counts CG dinucleotides and calculates density percentage.
        // WHY:  High unmethylated CpG density (> 3.0%) in viral/bacterial scaffolds triggers TLR9 immune rejection.
        // HOW:  Iterates characters looking for 'C' followed immediately by 'G'.
        int cpgCount = 0;
        for (int i = 0; i < cleanSeq.Length - 1; i++)
        {
            if (cleanSeq[i] == 'C' && cleanSeq[i + 1] == 'G')
            {
                cpgCount++;
            }
        }

        double cpgDensityPercent = cleanSeq.Length > 1 ? (double)(cpgCount * 2) / cleanSeq.Length * 100.0 : 0.0;
        if (cpgDensityPercent >= 3.0)
        {
            detectedMarkerIds.Add("TLR9_HIGH_SENSITIVITY");
        }

        // 2. Check for Tandem CAG Repeat Stretches (Androgen Receptor Exon 1 Mimic)
        // WHAT: Scans for contiguous (CAG)n trinucleotide repeats.
        // WHY:  Detects sequences containing extended polyglutamine codons affecting receptor sensitivity.
        // HOW:  Finds matches of repeating CAG triplets.
        MatchCollection cagMatches = Regex.Matches(cleanSeq, @"(CAG){4,}");
        int maxCagRepeats = 0;
        foreach (Match m in cagMatches)
        {
            int repeats = m.Length / 3;
            if (repeats > maxCagRepeats)
            {
                maxCagRepeats = repeats;
            }
        }

        if (maxCagRepeats >= 26)
        {
            detectedMarkerIds.Add("AR_CAG_LONG");
        }
        else if (maxCagRepeats >= 10 && maxCagRepeats <= 19)
        {
            detectedMarkerIds.Add("AR_CAG_SHORT");
        }

        return AnalyzeProfile(patientId, detectedMarkerIds, regimen);
    }

    /// <inheritdoc />
    public EndocrineTargetProfile ApplyGeneticAdjustments(EndocrineTargetProfile baseProfile, PatientGenomicProfile genomicProfile)
    {
        ArgumentNullException.ThrowIfNull(baseProfile);
        ArgumentNullException.ThrowIfNull(genomicProfile);

        // WHAT: Derives safe and effective setpoints by combining baseline targets with genomic modifiers.
        // WHY:  Implements the personalized nanomedicine principle: adjust dosing to the patient's genetic limits.
        // HOW:  Clamps estradiol to recommendedMaxE2, raises testosterone to recommendedMinT, and offsets Kd.
        double targetE2 = baseProfile.TargetEstradiolPmolL;
        if (genomicProfile.RecommendedMaxEstradiolPmolL.HasValue && targetE2 > genomicProfile.RecommendedMaxEstradiolPmolL.Value)
        {
            targetE2 = genomicProfile.RecommendedMaxEstradiolPmolL.Value;
        }

        double targetT = baseProfile.TargetTestosteroneNmolL;
        if (genomicProfile.RecommendedMinTestosteroneNmolL.HasValue && targetT < genomicProfile.RecommendedMinTestosteroneNmolL.Value)
        {
            targetT = genomicProfile.RecommendedMinTestosteroneNmolL.Value;
        }

        double targetKd = Math.Clamp(baseProfile.BindingAffinityKdTargetNanoMolar + genomicProfile.RecommendedKdOffsetNanoMolar, 1.0, 30.0);

        // Modify estimated stability half-life if CpG methylation is required
        double halfLife = baseProfile.DeliveryVehicle.EstimatedStabilityHalfLifeHours;
        if (genomicProfile.CpGMethylationMandatory)
        {
            // Epigenetically methylated DNA origami has 40% greater in-vivo enzymatic half-life in human serum
            halfLife *= 1.40;
        }

        NanocarrierDeliveryVehicle adjustedVehicle = baseProfile.DeliveryVehicle with
        {
            EstimatedStabilityHalfLifeHours = Math.Round(halfLife, 1)
        };

        return baseProfile with
        {
            TargetEstradiolPmolL = Math.Round(targetE2, 1),
            TargetTestosteroneNmolL = Math.Round(targetT, 2),
            BindingAffinityKdTargetNanoMolar = Math.Round(targetKd, 2),
            DeliveryVehicle = adjustedVehicle,
            UpdatedUtc = DateTime.UtcNow.ToString("o")
        };
    }
}
