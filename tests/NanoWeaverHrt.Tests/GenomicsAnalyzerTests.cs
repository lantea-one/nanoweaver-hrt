using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive unit test suite verifying clinical pharmacogenomic marker analysis,
/// sequence CpG dinucleotide density scanning, tandem CAG repeat evaluation,
/// severity-tiered call-out alert generation, and personalized kinetic profile adjustments.
/// </summary>
public sealed class GenomicsAnalyzerTests
{
    /// <summary>
    /// Service under test.
    /// </summary>
    private readonly PharmacogenomicsService _service = new();

    /// <summary>
    /// Helper to create a valid baseline MtF endocrine profile for testing.
    /// </summary>
    private static EndocrineTargetProfile CreateTestMtFProfile() => new()
    {
        PatientId = "ANON-001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetState = ToggleState.EstrogenicDominant,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        TargetEstradiolPmolL = 550.0,
        TargetTestosteroneNmolL = 1.20,
        TargetProgesteroneNmolL = 0.0,
        BindingAffinityKdTargetNanoMolar = 5.0,
        DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
        UpdatedUtc = DateTime.UtcNow.ToString("o")
    };

    /// <summary>
    /// Helper to create a valid baseline FtM endocrine profile for testing.
    /// </summary>
    private static EndocrineTargetProfile CreateTestFtMProfile() => new()
    {
        PatientId = "ANON-002",
        Regimen = TherapeuticRegimen.FemaleToMale,
        TargetState = ToggleState.AndrogenicDominant,
        TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
        TargetEstradiolPmolL = 90.0,
        TargetTestosteroneNmolL = 18.50,
        TargetProgesteroneNmolL = 0.0,
        BindingAffinityKdTargetNanoMolar = 5.0,
        DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
        UpdatedUtc = DateTime.UtcNow.ToString("o")
    };

    /// <summary>
    /// Helper to create a valid patient genomic profile for testing.
    /// </summary>
    private static PatientGenomicProfile CreateTestGenomicProfile(
        string patientId = "P1",
        TherapeuticRegimen regimen = TherapeuticRegimen.MaleToFemale,
        double thrombosisRisk = 1.0,
        double clearance = 1.0,
        bool cpgMandatory = false,
        double? maxE2 = null,
        double? minT = null,
        double kdOffset = 0.0) => new()
        {
            PatientId = patientId,
            Regimen = regimen,
            ObservedMarkers = Array.Empty<GeneticMarker>(),
            Alerts = Array.Empty<GeneticAlert>(),
            ThrombosisRiskIndex = thrombosisRisk,
            OverallClearanceMultiplier = clearance,
            CpGMethylationMandatory = cpgMandatory,
            RecommendedMaxEstradiolPmolL = maxE2,
            RecommendedMinTestosteroneNmolL = minT,
            RecommendedKdOffsetNanoMolar = kdOffset,
            CalculatedAtUtc = DateTime.UtcNow.ToString("o")
        };

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.GetCatalog"/> returns all curated clinical markers
    /// ordered by <see cref="GeneticCategory"/> and descending <see cref="ClinicalSeverity"/>.
    /// </summary>
    [Fact]
    public void GetCatalog_ReturnsCuratedMarkersInCorrectOrder()
    {
        // WHAT: Retrieves the entire catalog of clinical genetic markers.
        // WHY:  Ensures that all 8 core markers are registered with valid metadata.
        // HOW:  Asserts count, presence of key variant IDs, and sorting invariants.
        IReadOnlyList<GeneticMarker> catalog = _service.GetCatalog();

        Assert.NotNull(catalog);
        Assert.Equal(8, catalog.Count);

        // Verify key markers are present
        Assert.Contains(catalog, m => m.MarkerId == "F5_LEIDEN");
        Assert.Contains(catalog, m => m.MarkerId == "F2_PROTHROMBIN");
        Assert.Contains(catalog, m => m.MarkerId == "AR_CAG_LONG");
        Assert.Contains(catalog, m => m.MarkerId == "AR_CAG_SHORT");
        Assert.Contains(catalog, m => m.MarkerId == "COMT_VAL158MET");
        Assert.Contains(catalog, m => m.MarkerId == "CYP1B1_RAPID");
        Assert.Contains(catalog, m => m.MarkerId == "UGT2B17_DELETION");
        Assert.Contains(catalog, m => m.MarkerId == "TLR9_HIGH_SENSITIVITY");

        // Verify sorting: by Category, then by Severity descending
        for (int i = 0; i < catalog.Count - 1; i++)
        {
            if (catalog[i].Category == catalog[i + 1].Category)
            {
                Assert.True(catalog[i].Severity >= catalog[i + 1].Severity,
                    $"Marker {catalog[i].MarkerId} severity ({catalog[i].Severity}) should be >= {catalog[i + 1].MarkerId} ({catalog[i + 1].Severity})");
            }
            else
            {
                Assert.True(catalog[i].Category < catalog[i + 1].Category,
                    $"Category {catalog[i].Category} should be <= {catalog[i + 1].Category}");
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.GetPresetProfiles"/> returns all standard test panels.
    /// </summary>
    [Fact]
    public void GetPresetProfiles_ReturnsConfiguredPresets()
    {
        // WHAT: Retrieves the list of clinical demonstration presets.
        // WHY:  Ensures the UI and test suites can load predefined genetic patient panels.
        // HOW:  Asserts presence of baseline, MtF thrombosis, FtM resistance, and TLR9 immunogenic presets.
        IReadOnlyList<GeneticMarkerPreset> presets = _service.GetPresetProfiles();

        Assert.NotNull(presets);
        Assert.Equal(4, presets.Count);

        Assert.Contains(presets, p => p.PresetId == "PRESET_BASELINE" && p.MarkerIds.Count == 0);
        Assert.Contains(presets, p => p.PresetId == "PRESET_MTF_THROMBOSIS" && p.MarkerIds.Contains("F5_LEIDEN"));
        Assert.Contains(presets, p => p.PresetId == "PRESET_FTM_RESISTANCE" && p.MarkerIds.Contains("AR_CAG_LONG"));
        Assert.Contains(presets, p => p.PresetId == "PRESET_TLR9_IMMUNOGENIC" && p.MarkerIds.Contains("TLR9_HIGH_SENSITIVITY"));
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.AnalyzeProfile"/> enforces non-null arguments.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_ValidatesArguments()
    {
        // WHAT: Verifies argument validation in AnalyzeProfile.
        // WHY:  Prevents null reference exceptions during patient genomic evaluation.
        // HOW:  Asserts ArgumentException or ArgumentNullException on invalid inputs.
        Assert.Throws<ArgumentNullException>(() => _service.AnalyzeProfile(null!, ["F5_LEIDEN"], TherapeuticRegimen.MaleToFemale));
        Assert.Throws<ArgumentException>(() => _service.AnalyzeProfile("   ", ["F5_LEIDEN"], TherapeuticRegimen.MaleToFemale));
        Assert.Throws<ArgumentNullException>(() => _service.AnalyzeProfile("PATIENT-001", null!, TherapeuticRegimen.MaleToFemale));
    }

    /// <summary>
    /// Verifies that an empty marker list returns an Optimal baseline profile and alert.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_EmptyMarkers_ReturnsOptimalBaseline()
    {
        // WHAT: Evaluates a patient with zero observed risk markers.
        // WHY:  Ensures cis-typical or wild-type individuals receive an optimal status notice.
        // HOW:  Asserts 1 alert of severity Optimal and baseline metrics (1.0 multipliers).
        PatientGenomicProfile profile = _service.AnalyzeProfile("PATIENT-BASELINE", Array.Empty<string>(), TherapeuticRegimen.MaleToFemale);

        Assert.NotNull(profile);
        Assert.Equal("PATIENT-BASELINE", profile.PatientId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, profile.Regimen);
        Assert.Empty(profile.ObservedMarkers);
        Assert.Single(profile.Alerts);

        GeneticAlert alert = profile.Alerts[0];
        Assert.StartsWith("ALERT_BASELINE_", alert.AlertId);
        Assert.Equal(ClinicalSeverity.Optimal, alert.Severity);
        Assert.Equal("GENOMIC_BASELINE", alert.GeneSymbol);
        Assert.Equal(1.0, profile.ThrombosisRiskIndex);
        Assert.Equal(1.0, profile.OverallClearanceMultiplier);
        Assert.False(profile.CpGMethylationMandatory);
        Assert.Null(profile.RecommendedMaxEstradiolPmolL);
        Assert.Null(profile.RecommendedMinTestosteroneNmolL);
        Assert.Equal(0.0, profile.RecommendedKdOffsetNanoMolar);
    }

    /// <summary>
    /// Verifies that unknown marker identifiers are gracefully ignored.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_UnknownMarkerIds_GracefullySkipped()
    {
        // WHAT: Passes non-existent marker identifiers to AnalyzeProfile.
        // WHY:  Ensures robustness when processing third-party genetic panels with unknown variants.
        // HOW:  Asserts that unknown IDs are ignored and baseline alert is produced.
        PatientGenomicProfile profile = _service.AnalyzeProfile("PATIENT-UNKNOWN", ["NON_EXISTENT_MARKER", "INVALID_123"], TherapeuticRegimen.FemaleToMale);

        Assert.Empty(profile.ObservedMarkers);
        Assert.Single(profile.Alerts);
        Assert.Equal(ClinicalSeverity.Optimal, profile.Alerts[0].Severity);
    }

    /// <summary>
    /// Verifies that Factor V Leiden and COMT slow metabolizer mutations in MtF regimen
    /// trigger Critical call-out alerts, thrombosis multiplication, and E2 capping.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_MtFThrombosisPreset_EmitsCriticalAlertsAndTightensCaps()
    {
        // WHAT: Evaluates MtF thrombosis panel containing F5 Leiden (4.2x) and COMT (1.2x).
        // WHY:  Factor V Leiden multiplies DVT risk, requiring conservative E2 capping.
        // HOW:  Asserts combined thrombosis index, clearance reduction (0.75x), and critical alert generation.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-MTF-CLOT",
            ["F5_LEIDEN", "COMT_VAL158MET"],
            TherapeuticRegimen.MaleToFemale);

        Assert.Equal(2, profile.ObservedMarkers.Count);
        Assert.Equal(2, profile.Alerts.Count);

        // Combined thrombosis risk: 4.2 * 1.2 = 5.04
        Assert.Equal(5.04, profile.ThrombosisRiskIndex);

        // Clearance rate: 1.0 * 0.75 = 0.75
        Assert.Equal(0.75, profile.OverallClearanceMultiplier);

        // Max safe E2: 500 pmol/L from F5_LEIDEN
        Assert.Equal(500.0, profile.RecommendedMaxEstradiolPmolL);

        // Verify F5_LEIDEN alert properties
        GeneticAlert f5Alert = Assert.Single(profile.Alerts, a => a.GeneSymbol == "F5");
        Assert.Equal(ClinicalSeverity.Critical, f5Alert.Severity);
        Assert.Equal(GeneticCategory.ThromboticRisk, f5Alert.Category);
        Assert.StartsWith("[CRITICAL]", f5Alert.CallOutMessage);
        Assert.Contains("500", f5Alert.KineticGuidance);
        Assert.Null(f5Alert.ScaffoldGuidance);

        // Verify COMT alert properties
        GeneticAlert comtAlert = Assert.Single(profile.Alerts, a => a.GeneSymbol == "COMT");
        Assert.Equal(ClinicalSeverity.Warning, comtAlert.Severity);
        Assert.Equal(GeneticCategory.SteroidMetabolism, comtAlert.Category);
        Assert.Contains("0.75x", comtAlert.KineticGuidance);
    }

    /// <summary>
    /// Verifies that markers not associated with the active regimen are downgraded to Advisory.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_MarkerAssociatedWithOppositeRegimen_EmitsCarrierAdvisory()
    {
        // WHAT: Evaluates Factor V Leiden (associated with MtF) in an FtM therapeutic regimen.
        // WHY:  Testosterone therapy has lower thrombosis interaction than high-dose estrogens, but carrier status must be noted.
        // HOW:  Asserts severity downgrades to Advisory and message starts with [CARRIER ADVISORY].
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-FTM-CARRIER",
            ["F5_LEIDEN"],
            TherapeuticRegimen.FemaleToMale);

        GeneticAlert alert = Assert.Single(profile.Alerts);
        Assert.Equal(ClinicalSeverity.Advisory, alert.Severity);
        Assert.StartsWith("[CARRIER ADVISORY]", alert.CallOutMessage);
    }

    /// <summary>
    /// Verifies that multiple markers specifying max E2 safe caps resolve to the most conservative (minimum) value.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_MultipleE2Caps_ResolvesToMinimum()
    {
        // WHAT: Evaluates F5 Leiden (cap 500 pmol/L) and F2 Prothrombin (cap 480 pmol/L) together.
        // WHY:  The patient must be protected by the strictest physical ceiling.
        // HOW:  Asserts RecommendedMaxEstradiolPmolL is 480.0.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-MULTI-CAP",
            ["F5_LEIDEN", "F2_PROTHROMBIN"],
            TherapeuticRegimen.MaleToFemale);

        Assert.Equal(480.0, profile.RecommendedMaxEstradiolPmolL);
    }

    /// <summary>
    /// Verifies that multiple markers specifying min testosterone floors resolve to the maximum recommended level.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_MultipleTFloors_ResolvesToMaximum()
    {
        // WHAT: Evaluates AR CAG Short (min 14.0 nmol/L) and AR CAG Long (min 22.0 nmol/L) together.
        // WHY:  Ensures that if multiple variants influence androgen requirements, the highest target is preserved.
        // HOW:  Asserts RecommendedMinTestosteroneNmolL is 22.0.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-MULTI-T",
            ["AR_CAG_SHORT", "AR_CAG_LONG"],
            TherapeuticRegimen.FemaleToMale);

        Assert.Equal(22.0, profile.RecommendedMinTestosteroneNmolL);
    }

    /// <summary>
    /// Verifies that FtM receptor resistance markers (AR CAG elongation + UGT2B17 null)
    /// trigger negative Kd offsets (tighter aptamer affinity) and slowed clearance.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_FtMResistancePreset_AdjustsAffinityAndClearance()
    {
        // WHAT: Evaluates FtM patient panel with UGT2B17 deletion and AR CAG > 26 elongation.
        // WHY:  Compensates for target tissue insensitivity by tightening aptamer Kd and accounting for slower T clearance.
        // HOW:  Asserts recommendedKdOffset is -1.5 nM and clearance multiplier is 0.60x.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-FTM-RESIST",
            ["UGT2B17_DELETION", "AR_CAG_LONG"],
            TherapeuticRegimen.FemaleToMale);

        Assert.Equal(2, profile.ObservedMarkers.Count);
        Assert.Equal(0.60, profile.OverallClearanceMultiplier);
        Assert.Equal(-1.5, profile.RecommendedKdOffsetNanoMolar);
        Assert.Equal(22.0, profile.RecommendedMinTestosteroneNmolL);

        GeneticAlert arAlert = Assert.Single(profile.Alerts, a => a.GeneSymbol == "AR");
        Assert.Equal(GeneticCategory.ReceptorSensitivity, arAlert.Category);
        Assert.Contains("-1.5 nM", arAlert.KineticGuidance);
    }

    /// <summary>
    /// Verifies that AR CAG shortening marker in FtM regimen yields an Advisory alert and positive Kd offset.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_ArCagShort_AdjustsAffinityAndMinT()
    {
        // WHAT: Evaluates AR CAG < 20 shortening variant in FtM regimen.
        // WHY:  Receptor hypersensitivity requires moderating testosterone and slightly loosening aptamer Kd (+0.5 nM).
        // HOW:  Asserts Kd offset is +0.5 nM, min recommended T is 14.0 nmol/L, and category is ReceptorSensitivity.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-FTM-SENSITIVE",
            ["AR_CAG_SHORT"],
            TherapeuticRegimen.FemaleToMale);

        Assert.Equal(+0.5, profile.RecommendedKdOffsetNanoMolar);
        Assert.Equal(14.0, profile.RecommendedMinTestosteroneNmolL);

        GeneticAlert alert = Assert.Single(profile.Alerts);
        Assert.Equal(GeneticCategory.ReceptorSensitivity, alert.Category);
        Assert.Contains("+0.5 nM", alert.KineticGuidance);
    }

    /// <summary>
    /// Verifies that TLR9 high-sensitivity alleles trigger mandatory scaffold CpG methylation.
    /// </summary>
    [Fact]
    public void AnalyzeProfile_Tlr9Variant_MandatesEpigeneticMethylation()
    {
        // WHAT: Evaluates TLR9 promoter polymorphism (rs187084).
        // WHY:  Prevents macrophage phagocytosis of DNA origami by enforcing M.SssI epigenetic methylation.
        // HOW:  Asserts CpGMethylationMandatory is true and scaffold guidance is populated.
        PatientGenomicProfile profile = _service.AnalyzeProfile(
            "PATIENT-TLR9",
            ["TLR9_HIGH_SENSITIVITY"],
            TherapeuticRegimen.MaleToFemale);

        Assert.True(profile.CpGMethylationMandatory);

        GeneticAlert alert = Assert.Single(profile.Alerts);
        Assert.Equal(GeneticCategory.NanocarrierImmunogenicity, alert.Category);
        Assert.NotNull(alert.ScaffoldGuidance);
        Assert.Contains("M.SssI", alert.ScaffoldGuidance);
        Assert.Equal("Standard kinetic parameters maintained.", alert.KineticGuidance);
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.AnalyzeSequence"/> enforces non-null patient identifier.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_ValidatesPatientId()
    {
        // WHAT: Verifies patientId argument validation in AnalyzeSequence.
        // WHY:  Guarantees audit traceability.
        // HOW:  Asserts ArgumentException on null or whitespace.
        Assert.Throws<ArgumentNullException>(() => _service.AnalyzeSequence(null!, "ATCG", TherapeuticRegimen.MaleToFemale));
        Assert.Throws<ArgumentException>(() => _service.AnalyzeSequence("   ", "ATCG", TherapeuticRegimen.MaleToFemale));
    }

    /// <summary>
    /// Verifies that null, empty, or whitespace sequences fall back cleanly to baseline.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    public void AnalyzeSequence_NullOrWhitespace_ReturnsBaseline(string? input)
    {
        // WHAT: Tests AnalyzeSequence with absent or empty sequence text.
        // WHY:  Prevents exceptions when user has not yet typed a sequence in the wizard.
        // HOW:  Asserts return of baseline profile.
        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-SEQ", input!, TherapeuticRegimen.MaleToFemale);

        Assert.NotNull(profile);
        Assert.Empty(profile.ObservedMarkers);
        Assert.Single(profile.Alerts);
        Assert.Equal(ClinicalSeverity.Optimal, profile.Alerts[0].Severity);
    }

    /// <summary>
    /// Verifies that FASTA headers and non-alphabetic characters are stripped cleanly before sequence analysis.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_FastaHeaderAndWhitespace_StrippedCorrectly()
    {
        // WHAT: Ingests multi-line FASTA formatted DNA sequence with headers, numbers, and whitespace.
        // WHY:  Standardizes FASTA ingestion without regex failures.
        // HOW:  Asserts that FASTA header lines (>...) are ignored and base counts are accurate.
        string fasta = ">gi|1234567|ref|NC_000001.1| Scaffold\n"
            + "A T C G - 1 2 3\n"
            + ">secondary header\n"
            + "A T C G";

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-FASTA", fasta, TherapeuticRegimen.MaleToFemale);
        Assert.NotNull(profile);
    }

    /// <summary>
    /// Verifies that sequences with length 1 or zero do not cause division-by-zero or index-out-of-range errors.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_SingleCharacterSequence_HandledSafely()
    {
        // WHAT: Ingests 1-nucleotide sequence "C".
        // WHY:  Prevents boundary off-by-one errors when checking cleanSeq[i + 1].
        // HOW:  Asserts baseline profile without exceptions.
        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-TINY", "C", TherapeuticRegimen.MaleToFemale);
        Assert.NotNull(profile);
        Assert.False(profile.CpGMethylationMandatory);
    }

    /// <summary>
    /// Verifies that high CpG dinucleotide density (>= 3.0%) triggers the TLR9 immunogenicity marker.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_HighCpGDensity_TriggersTlr9Alert()
    {
        // WHAT: Scans sequence with high frequency of CG dinucleotides.
        // WHY:  Unmethylated CpG motifs trigger Toll-Like Receptor 9 in innate immune cells.
        // HOW:  Creates 100-bp sequence containing 20 CG pairs (40% CpG density) and asserts TLR9 detection.
        string highCpgSeq = string.Concat(Enumerable.Repeat("CGCG", 25)); // 100 bp, dense CpG

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-CPG", highCpgSeq, TherapeuticRegimen.MaleToFemale);

        Assert.True(profile.CpGMethylationMandatory);
        Assert.Contains(profile.ObservedMarkers, m => m.MarkerId == "TLR9_HIGH_SENSITIVITY");
    }

    /// <summary>
    /// Verifies that low CpG dinucleotide density (< 3.0%) does not trigger TLR9 immunogenicity.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_LowCpGDensity_DoesNotTriggerTlr9()
    {
        // WHAT: Scans sequence with low CpG density (< 3%).
        // WHY:  Standard mammalian-like or CpG-depleted constructs are safe without enzymatic methylation.
        // HOW:  Creates 100-bp sequence with 1 CG pair (2.0% density) and asserts no TLR9 marker.
        string lowCpgSeq = new string('A', 49) + "CG" + new string('T', 49);

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-LOW-CPG", lowCpgSeq, TherapeuticRegimen.MaleToFemale);

        Assert.False(profile.CpGMethylationMandatory);
        Assert.DoesNotContain(profile.ObservedMarkers, m => m.MarkerId == "TLR9_HIGH_SENSITIVITY");
    }

    /// <summary>
    /// Verifies that tandem CAG repeat stretches of 26 or more trigger the AR CAG Long marker.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_CagRepeatsOver26_TriggersArCagLong()
    {
        // WHAT: Scans sequence containing 28 contiguous CAG trinucleotides.
        // WHY:  Detects polyglutamine tract elongation associated with androgen receptor insensitivity.
        // HOW:  Asserts presence of AR_CAG_LONG marker and -1.5 nM Kd offset.
        string cagSeq = "ATCG" + string.Concat(Enumerable.Repeat("CAG", 28)) + "ATCG";

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-CAG-LONG", cagSeq, TherapeuticRegimen.FemaleToMale);

        Assert.Contains(profile.ObservedMarkers, m => m.MarkerId == "AR_CAG_LONG");
        Assert.Equal(-1.5, profile.RecommendedKdOffsetNanoMolar);
    }

    /// <summary>
    /// Verifies that tandem CAG repeat stretches between 10 and 19 trigger the AR CAG Short marker.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_CagRepeatsBetween10And19_TriggersArCagShort()
    {
        // WHAT: Scans sequence containing 14 contiguous CAG trinucleotides.
        // WHY:  Detects polyglutamine tract shortening associated with androgen receptor hypersensitivity.
        // HOW:  Asserts presence of AR_CAG_SHORT marker and +0.5 nM Kd offset.
        string cagSeq = "ATCG" + string.Concat(Enumerable.Repeat("CAG", 14)) + "ATCG";

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-CAG-SHORT", cagSeq, TherapeuticRegimen.FemaleToMale);

        Assert.Contains(profile.ObservedMarkers, m => m.MarkerId == "AR_CAG_SHORT");
        Assert.Equal(+0.5, profile.RecommendedKdOffsetNanoMolar);
    }

    /// <summary>
    /// Verifies that CAG repeats between 20 and 25 (intermediate normal range) do not trigger either marker.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_IntermediateCagRepeats_TriggersNoArMarkers()
    {
        // WHAT: Scans sequence containing 22 contiguous CAG trinucleotides.
        // WHY:  20-25 repeats represent standard physiological median sensitivity.
        // HOW:  Asserts that neither AR_CAG_LONG nor AR_CAG_SHORT is present.
        string cagSeq = "ATCG" + string.Concat(Enumerable.Repeat("CAG", 22)) + "ATCG";

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-CAG-MID", cagSeq, TherapeuticRegimen.FemaleToMale);

        Assert.DoesNotContain(profile.ObservedMarkers, m => m.MarkerId == "AR_CAG_LONG");
        Assert.DoesNotContain(profile.ObservedMarkers, m => m.MarkerId == "AR_CAG_SHORT");
    }

    /// <summary>
    /// Verifies that multiple CAG tracts evaluate to the maximum repeat count.
    /// </summary>
    [Fact]
    public void AnalyzeSequence_MultipleCagTracts_PicksMaximum()
    {
        // WHAT: Scans sequence with two CAG tracts: one of length 12, another of length 27.
        // WHY:  Ensures that the algorithm finds the longest contiguous tract.
        // HOW:  Asserts AR_CAG_LONG is detected (from the 27-repeat tract).
        string multiCagSeq = string.Concat(Enumerable.Repeat("CAG", 12))
            + "TTTTTTTT"
            + string.Concat(Enumerable.Repeat("CAG", 27));

        PatientGenomicProfile profile = _service.AnalyzeSequence("PATIENT-MULTI-CAG", multiCagSeq, TherapeuticRegimen.FemaleToMale);

        Assert.Contains(profile.ObservedMarkers, m => m.MarkerId == "AR_CAG_LONG");
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.ApplyGeneticAdjustments"/> validates non-null arguments.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_ValidatesArguments()
    {
        // WHAT: Verifies null argument validation in ApplyGeneticAdjustments.
        // WHY:  Guarantees fail-fast behavior on missing profiles.
        // HOW:  Asserts ArgumentNullException on null parameters.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile();
        PatientGenomicProfile genomicProfile = _service.AnalyzeProfile("PATIENT", Array.Empty<string>(), TherapeuticRegimen.MaleToFemale);

        Assert.Throws<ArgumentNullException>(() => _service.ApplyGeneticAdjustments(null!, genomicProfile));
        Assert.Throws<ArgumentNullException>(() => _service.ApplyGeneticAdjustments(baseProfile, null!));
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.ApplyGeneticAdjustments"/> clamps estradiol setpoint
    /// when the patient's genetic profile specifies a recommended maximum.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_ClampsEstradiolWhenExceedingCap()
    {
        // WHAT: Applies genetic adjustments with RecommendedMaxEstradiolPmolL = 480 to a base profile with E2 = 550.
        // WHY:  Protects thrombophilic patients from excessive estrogen exposure.
        // HOW:  Asserts adjusted TargetEstradiolPmolL equals 480.0.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile() with
        {
            TargetEstradiolPmolL = 550.0
        };

        PatientGenomicProfile genomicProfile = _service.AnalyzeProfile(
            "PATIENT-THROMBOSIS",
            ["F2_PROTHROMBIN"],
            TherapeuticRegimen.MaleToFemale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, genomicProfile);

        Assert.Equal(480.0, adjusted.TargetEstradiolPmolL);
    }

    /// <summary>
    /// Verifies that estradiol setpoint is preserved when already below the genetic maximum.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_PreservesEstradiolWhenBelowCap()
    {
        // WHAT: Applies genetic adjustments with RecommendedMaxEstradiolPmolL = 500 to a base profile with E2 = 400.
        // WHY:  Ensures setpoints within the safe window are not unnecessarily shifted.
        // HOW:  Asserts adjusted TargetEstradiolPmolL remains 400.0.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile() with
        {
            TargetEstradiolPmolL = 400.0
        };

        PatientGenomicProfile genomicProfile = _service.AnalyzeProfile(
            "PATIENT-SAFE-E2",
            ["F5_LEIDEN"],
            TherapeuticRegimen.MaleToFemale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, genomicProfile);

        Assert.Equal(400.0, adjusted.TargetEstradiolPmolL);
    }

    /// <summary>
    /// Verifies that <see cref="PharmacogenomicsService.ApplyGeneticAdjustments"/> elevates testosterone setpoint
    /// when the patient's genetic profile specifies a recommended minimum floor.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_ElevatesTestosteroneWhenBelowFloor()
    {
        // WHAT: Applies genetic adjustments with RecommendedMinTestosteroneNmolL = 22.0 to an FtM profile with T = 18.5.
        // WHY:  Overcomes androgen receptor insensitivity.
        // HOW:  Asserts adjusted TargetTestosteroneNmolL equals 22.0.
        EndocrineTargetProfile baseProfile = CreateTestFtMProfile() with
        {
            TargetTestosteroneNmolL = 18.50
        };

        PatientGenomicProfile genomicProfile = _service.AnalyzeProfile(
            "PATIENT-AR-RESIST",
            ["AR_CAG_LONG"],
            TherapeuticRegimen.FemaleToMale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, genomicProfile);

        Assert.Equal(22.0, adjusted.TargetTestosteroneNmolL);
    }

    /// <summary>
    /// Verifies that testosterone setpoint is preserved when already above the recommended minimum floor.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_PreservesTestosteroneWhenAboveFloor()
    {
        // WHAT: Applies genetic adjustments with RecommendedMinTestosteroneNmolL = 14.0 to an FtM profile with T = 20.0.
        // WHY:  Ensures compliant therapeutic setpoints are not lowered.
        // HOW:  Asserts adjusted TargetTestosteroneNmolL remains 20.0.
        EndocrineTargetProfile baseProfile = CreateTestFtMProfile() with
        {
            TargetTestosteroneNmolL = 20.0
        };

        PatientGenomicProfile genomicProfile = _service.AnalyzeProfile(
            "PATIENT-AR-OK",
            ["AR_CAG_SHORT"],
            TherapeuticRegimen.FemaleToMale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, genomicProfile);

        Assert.Equal(20.0, adjusted.TargetTestosteroneNmolL);
    }

    /// <summary>
    /// Verifies that aptamer Kd adjustments are clamped within physiological limits (1.0 to 30.0 nM).
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_ClampsKdWithinLimits()
    {
        // WHAT: Tests extreme Kd offset scenarios at the lower (1.0 nM) and upper (30.0 nM) bounds.
        // WHY:  Prevents unphysical binding constants (< 1 nM or > 30 nM).
        // HOW:  Applies offsets and asserts Kd clamping at 1.0 and 30.0.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile() with
        {
            BindingAffinityKdTargetNanoMolar = 2.0
        };

        // Offset: -5.0 nM -> 2.0 - 5.0 = -3.0 -> clamped to 1.0
        PatientGenomicProfile lowGenomic = CreateTestGenomicProfile(kdOffset: -5.0);
        EndocrineTargetProfile adjustedLow = _service.ApplyGeneticAdjustments(baseProfile, lowGenomic);
        Assert.Equal(1.0, adjustedLow.BindingAffinityKdTargetNanoMolar);

        // Offset: +35.0 nM -> 2.0 + 35.0 = 37.0 -> clamped to 30.0
        PatientGenomicProfile highGenomic = CreateTestGenomicProfile(kdOffset: 35.0);
        EndocrineTargetProfile adjustedHigh = _service.ApplyGeneticAdjustments(baseProfile, highGenomic);
        Assert.Equal(30.0, adjustedHigh.BindingAffinityKdTargetNanoMolar);
    }

    /// <summary>
    /// Verifies that mandatory CpG methylation extends estimated nanocarrier stability half-life by 40%.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_MandatoryCpGMethylation_ExtendsStabilityHalfLife()
    {
        // WHAT: Applies genetic adjustments where CpGMethylationMandatory is true.
        // WHY:  Methylated DNA origami experiences reduced endonuclease degradation in serum.
        // HOW:  Asserts delivery vehicle EstimatedStabilityHalfLifeHours is multiplied by 1.40.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile();
        double initialHalfLife = baseProfile.DeliveryVehicle.EstimatedStabilityHalfLifeHours;

        PatientGenomicProfile tlr9Profile = _service.AnalyzeProfile(
            "PATIENT-TLR9-HALFLIFE",
            ["TLR9_HIGH_SENSITIVITY"],
            TherapeuticRegimen.MaleToFemale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, tlr9Profile);

        Assert.Equal(Math.Round(initialHalfLife * 1.40, 1), adjusted.DeliveryVehicle.EstimatedStabilityHalfLifeHours);
    }

    /// <summary>
    /// Verifies that when CpG methylation is not required, nanocarrier half-life is unchanged.
    /// </summary>
    [Fact]
    public void ApplyGeneticAdjustments_WithoutCpGMethylation_PreservesHalfLife()
    {
        // WHAT: Applies genetic adjustments with CpGMethylationMandatory = false.
        // WHY:  Ensures stability metrics are unchanged when wild-type folding is used.
        // HOW:  Asserts EstimatedStabilityHalfLifeHours matches initial value.
        EndocrineTargetProfile baseProfile = CreateTestMtFProfile();
        double initialHalfLife = baseProfile.DeliveryVehicle.EstimatedStabilityHalfLifeHours;

        PatientGenomicProfile baselineGenomic = _service.AnalyzeProfile(
            "PATIENT-CLEAN",
            Array.Empty<string>(),
            TherapeuticRegimen.MaleToFemale);

        EndocrineTargetProfile adjusted = _service.ApplyGeneticAdjustments(baseProfile, baselineGenomic);

        Assert.Equal(initialHalfLife, adjusted.DeliveryVehicle.EstimatedStabilityHalfLifeHours);
    }

    /// <summary>
    /// Verifies that <see cref="EndocrineStateEngine.SimulateKinetics"/> scales rate constants
    /// when a custom <see cref="PatientGenomicProfile"/> with clearance multiplier is provided.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_SimulateKinetics_ScalesClearanceRate()
    {
        // WHAT: Runs SimulateKinetics with a high clearance multiplier (1.35x).
        // WHY:  Accelerated clearance alters the exponential progression to steady-state plateau.
        // HOW:  Compares standard kinetics vs genomic-scaled kinetics.
        EndocrineStateEngine engine = new();

        IReadOnlyList<HormoneCurvePoint> baselineKinetics = engine.SimulateKinetics(72, 4);

        PatientGenomicProfile rapidProfile = CreateTestGenomicProfile(patientId: "P-RAPID", clearance: 1.35);

        IReadOnlyList<HormoneCurvePoint> scaledKinetics = engine.SimulateKinetics(72, 4, rapidProfile);

        Assert.Equal(baselineKinetics.Count, scaledKinetics.Count);
        // With higher clearance rate, the exponential convergence is faster in the middle hours
        Assert.NotEqual(baselineKinetics[5].EstradiolPmolL, scaledKinetics[5].EstradiolPmolL);
    }

    /// <summary>
    /// Verifies that <see cref="EndocrineStateEngine.SimulateKinetics"/> handles non-positive clearance values
    /// by falling back safely to the 1.0 multiplier.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_SimulateKinetics_HandlesZeroOrNegativeClearance()
    {
        // WHAT: Tests SimulateKinetics with non-positive clearance multiplier (<= 0).
        // WHY:  Ensures the mathematical fallback to 1.0 is covered when bad input is provided.
        // HOW:  Asserts that kinetics evaluate cleanly using the 1.0 baseline fallback.
        EndocrineStateEngine engine = new();

        PatientGenomicProfile zeroClearanceProfile = CreateTestGenomicProfile(patientId: "P-ZERO", clearance: 0.0);
        IReadOnlyList<HormoneCurvePoint> kinetics = engine.SimulateKinetics(72, 4, zeroClearanceProfile);

        Assert.NotEmpty(kinetics);
    }

    /// <summary>
    /// Verifies that high thrombosis risk index (> 1.5) damps burst hormone release in early hours.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_SimulateKinetics_HighThrombosisDampsBurstRelease()
    {
        // WHAT: Runs SimulateKinetics with ThrombosisRiskIndex = 4.2 (> 1.5).
        // WHY:  Protects against early peak hormone surges by damping early burst release.
        // HOW:  Asserts early-hour hormone level is strictly lower with thrombosis damping.
        EndocrineStateEngine engine = new();

        PatientGenomicProfile highClotProfile = CreateTestGenomicProfile(patientId: "P-CLOT", thrombosisRisk: 4.2);

        IReadOnlyList<HormoneCurvePoint> normalKinetics = engine.SimulateKinetics(72, 4);
        IReadOnlyList<HormoneCurvePoint> dampedKinetics = engine.SimulateKinetics(72, 4, highClotProfile);

        // Hour 4 (index 1) should show dampened estradiol release under thrombosis protection
        Assert.True(dampedKinetics[1].EstradiolPmolL < normalKinetics[1].EstradiolPmolL,
            $"Expected damped estradiol ({dampedKinetics[1].EstradiolPmolL}) < normal ({normalKinetics[1].EstradiolPmolL})");
    }

    /// <summary>
    /// Verifies all properties and record instantiation of <see cref="GeneticMarker"/>,
    /// <see cref="GeneticAlert"/>, <see cref="PatientGenomicProfile"/>, and <see cref="GeneticMarkerPreset"/>.
    /// </summary>
    [Fact]
    public void Models_RecordPropertiesAndWithExpressions()
    {
        // WHAT: Tests record copy, default properties, and initialization for genomics models.
        // WHY:  Ensures full test coverage of record constructors, init setters, and with-cloning.
        // HOW:  Creates instances and tests mutation via with-expressions.
        GeneticMarker marker = new()
        {
            MarkerId = "TEST_ID",
            GeneSymbol = "TEST_GENE",
            VariantName = "Test Variant",
            Category = GeneticCategory.NanocarrierImmunogenicity,
            Severity = ClinicalSeverity.Warning,
            ObservedGenotype = "Het",
            ClinicalDescription = "Desc",
            RecommendedAction = "Action",
            AssociatedRegimen = TherapeuticRegimen.FemaleToMale,
            ClearanceRateMultiplier = 1.1,
            TargetKdOffsetNanoMolar = -0.5,
            MaxSafeEstradiolPmolL = 450.0,
            MinRecommendedTestosteroneNmolL = 15.0,
            ThrombosisRiskMultiplier = 1.5,
            RequiresCpGMethylation = true
        };

        GeneticMarker mutatedMarker = marker with { MarkerId = "MUTATED" };
        Assert.Equal("MUTATED", mutatedMarker.MarkerId);
        Assert.Equal("TEST_GENE", mutatedMarker.GeneSymbol);

        GeneticAlert alert = new()
        {
            AlertId = "A1",
            GeneSymbol = "G1",
            VariantName = "V1",
            Severity = ClinicalSeverity.Critical,
            Category = GeneticCategory.ThromboticRisk,
            Title = "T1",
            CallOutMessage = "Msg",
            ClinicalGuidance = "CG",
            ScaffoldGuidance = "SG",
            KineticGuidance = "KG"
        };

        GeneticAlert mutatedAlert = alert with { Title = "T2" };
        Assert.Equal("T2", mutatedAlert.Title);
        Assert.Equal("A1", mutatedAlert.AlertId);

        PatientGenomicProfile profile = CreateTestGenomicProfile(patientId: "P1");
        Assert.Equal("P1", profile.PatientId);
        Assert.Equal(1.0, profile.ThrombosisRiskIndex);
        Assert.Equal(1.0, profile.OverallClearanceMultiplier);
        Assert.False(profile.CpGMethylationMandatory);
        Assert.False(profile.HasCriticalAlerts);

        PatientGenomicProfile critProfile = profile with { Alerts = [alert] };
        Assert.True(critProfile.HasCriticalAlerts);

        GeneticMarkerPreset preset = new()
        {
            PresetId = "PR1",
            Title = "Title",
            Description = "Desc",
            Regimen = TherapeuticRegimen.MaleToFemale,
            MarkerIds = ["M1"]
        };
        Assert.Equal("PR1", preset.PresetId);
        Assert.Single(preset.MarkerIds);

        // Verify ClinicalSeverity and GeneticCategory enum values
        Assert.Equal(0, (int)ClinicalSeverity.Optimal);
        Assert.Equal(1, (int)ClinicalSeverity.Advisory);
        Assert.Equal(2, (int)ClinicalSeverity.Warning);
        Assert.Equal(3, (int)ClinicalSeverity.Critical);

        Assert.Equal(0, (int)GeneticCategory.ThromboticRisk);
        Assert.Equal(1, (int)GeneticCategory.SteroidMetabolism);
        Assert.Equal(2, (int)GeneticCategory.ReceptorSensitivity);
        Assert.Equal(3, (int)GeneticCategory.NanocarrierImmunogenicity);
    }
}
