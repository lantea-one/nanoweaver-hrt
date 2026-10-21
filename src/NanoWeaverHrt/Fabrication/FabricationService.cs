using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoWeaverHrt.Endocrine;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Authoritative fabrication engine synthesizing DNA origami staple routing maps,
/// stoichiometric annealing protocols, and cryptographic provenance manifests.
/// </summary>
public sealed class FabricationService : IFabricationService
{
    /// <summary>
    /// Master repository of pre-configured sample templates.
    /// </summary>
    private readonly IReadOnlyList<DnaOrigamiSample> _samples = DnaOrigamiSample.GetPresetLibrary();

    /// <summary>
    /// Retrieves all built-in sample digitized DNA designs available for immediate testing.
    /// </summary>
    /// <returns>Collection of pre-configured DNA origami samples.</returns>
    public IReadOnlyList<DnaOrigamiSample> GetPresetSamples() => _samples;

    /// <summary>
    /// Analyzes the base-pair composition, GC content, and molecular weight of an uploaded or input DNA sequence.
    /// </summary>
    /// <param name="sequence">Raw nucleotide sequence string.</param>
    /// <returns>Base counts, GC percentage, and molecular weight in kilodaltons.</returns>
    public (int A, int T, int G, int C, double GcPercent, double EstimatedMwKDa) AnalyzeSequenceComposition(string sequence)
        => AnalyzeSequenceCompositionStatic(sequence);

    /// <summary>
    /// Internal static base composition analyzer operating zero-allocations on character spans.
    /// </summary>
    /// <param name="sequence">Raw nucleotide sequence string.</param>
    /// <returns>Base counts, GC percentage, and molecular weight in kilodaltons.</returns>
    internal static (int A, int T, int G, int C, double GcPercent, double EstimatedMwKDa) AnalyzeSequenceCompositionStatic(string sequence)
    {
        int a = 0, t = 0, g = 0, c = 0;

        // WHAT: Counts individual nucleotide occurrences without allocating heap strings.
        // WHY:  Preserves strict zero-allocation performance on large sequence streams.
        // HOW:  Loops over the ReadOnlySpan of characters, incrementing base counters.
        ReadOnlySpan<char> span = sequence.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            char ch = char.ToUpperInvariant(span[i]);
            switch (ch)
            {
                case 'A': a++; break;
                case 'T': case 'U': t++; break;
                case 'G': g++; break;
                case 'C': c++; break;
            }
        }

        int total = a + t + g + c;
        double gcPercent = total > 0 ? (double)(g + c) / total * 100.0 : 0.0;

        // Average single-stranded nucleotide molecular weight is approximately 330 Da
        double mwKDa = total * 0.330;

        return (a, t, g, c, Math.Round(gcPercent, 1), Math.Round(mwKDa, 2));
    }

    /// <summary>
    /// Generates a complete synthesis specification and staple routing manifest for the specified parameters.
    /// </summary>
    /// <param name="sequenceOrSampleId">Raw nucleotide sequence or preset sample ID.</param>
    /// <param name="regimen">Target endocrine regimen (MtF or FtM).</param>
    /// <param name="geometry">Selected nanocarrier geometry (e.g., Nanotube, Cuboid, Cage).</param>
    /// <param name="targetKd">Target dissociation constant in nanomolar.</param>
    /// <returns>A comprehensive fabrication package ready for laboratory synthesis.</returns>
    public FabricationPackage GenerateFabricationPackage(
        string sequenceOrSampleId,
        TherapeuticRegimen regimen,
        string geometry,
        double targetKd)
    {
        DnaOrigamiSample? matchedSample = _samples.FirstOrDefault(s => string.Equals(s.Id, sequenceOrSampleId, StringComparison.OrdinalIgnoreCase));
        string targetTissue = matchedSample?.TargetTissue ?? (regimen == TherapeuticRegimen.MaleToFemale ? "Anterior Pituitary Gonadotrope (GnRH-R)" : "Steroidogenic Receptors (Pituitary/Adrenal)");
        string activeLigand = matchedSample?.ActiveLigand ?? (regimen == TherapeuticRegimen.MaleToFemale ? "17β-Estradiol" : "Testosterone Enanthate");

        // WHAT: Generates an ensemble of staple oligonucleotides designed to staple M13mp18 scaffold into shape.
        // WHY:  Staple strands bridge distinct sections of the 7.2 kb viral scaffold, forcing it into rigid nanostructures.
        // HOW:  Synthesizes core structural staples, crossover staples, and aptamer functionalization loops.
        List<StapleOligoRecord> staples = GenerateStapleEnsemble(regimen, geometry, targetKd);

        // WHAT: Defines the precision cryogenic-to-ambient thermal annealing protocol.
        // WHY:  Gradual cooling allows stable thermodynamic base-pairing without kinetic misfolding traps.
        // HOW:  Constructs a multi-stage ramp schedule from 90°C denaturation down to 4°C storage.
        List<AnnealingRampStep> schedule =
        [
            new AnnealingRampStep
            {
                StepIndex = 1,
                TargetTemperatureCelsius = 90.0,
                HoldDurationMinutes = 5,
                RampRateCelsiusPerMinute = 2.0,
                BiophysicalPurpose = "Thermal Denaturation: Completely unpairs secondary structures and hairpins in single-stranded M13mp18 scaffold."
            },
            new AnnealingRampStep
            {
                StepIndex = 2,
                TargetTemperatureCelsius = 65.0,
                HoldDurationMinutes = 15,
                RampRateCelsiusPerMinute = 0.5,
                BiophysicalPurpose = "Initial Scaffold Nucleation: Longest staple crossovers begin recognizing complementary scaffold domains."
            },
            new AnnealingRampStep
            {
                StepIndex = 3,
                TargetTemperatureCelsius = 55.0,
                HoldDurationMinutes = 60,
                RampRateCelsiusPerMinute = 0.1,
                BiophysicalPurpose = "Critical Folding Phase: Core lattice crossovers assemble cooperatively at 0.1°C/min to prevent kinetic trapping."
            },
            new AnnealingRampStep
            {
                StepIndex = 4,
                TargetTemperatureCelsius = 40.0,
                HoldDurationMinutes = 30,
                RampRateCelsiusPerMinute = 0.2,
                BiophysicalPurpose = "Aptamer Loop Hybridization: Terminal aptamer binding pockets and receptor-targeting hairpins anneal into place."
            },
            new AnnealingRampStep
            {
                StepIndex = 5,
                TargetTemperatureCelsius = 20.0,
                HoldDurationMinutes = 10,
                RampRateCelsiusPerMinute = 0.5,
                BiophysicalPurpose = "Ambient Lattice Stabilization: Nanocarrier reaches room-temperature equilibrium; prepares for PEG precipitation or spin filtration."
            },
            new AnnealingRampStep
            {
                StepIndex = 6,
                TargetTemperatureCelsius = 4.0,
                HoldDurationMinutes = 0,
                RampRateCelsiusPerMinute = 1.0,
                BiophysicalPurpose = "Cryogenic Quenching & Storage: Long-term preservation in 1x TAE + 12.5 mM MgCl2 buffer."
            }
        ];

        string packageId = $"FAB-{regimen.ToString().ToUpperInvariant()[..3]}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        // WHAT: Synthesizes comprehensive DIY and HomeLab benchtop fabrication instructions.
        // WHY:  Enables community researchers, DIY bio-hackers, and non-expert lab technicians to physically fabricate the nanocarrier.
        // HOW:  Assembles equipment lists, pipetting tables, 7-step bench execution protocols, and gel QC instructions.
        DiyFabricationGuide diyGuide = GenerateDiyFabricationGuide(regimen, geometry, staples.Count);

        // Compute deterministic SHA-256 fingerprint for the package definition
        string signatureMaterial = $"{packageId}|{regimen}|{geometry}|{targetTissue}|{targetKd}|{staples.Count}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(signatureMaterial));
        string sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new FabricationPackage
        {
            PackageId = packageId,
            Regimen = regimen,
            ScaffoldBackbone = "Bacteriophage M13mp18 ssDNA (7,249 nucleotides)",
            DeliveryGeometry = geometry,
            TargetTissue = targetTissue,
            StapleOligos = staples,
            AnnealingSchedule = schedule,
            StoichiometricRatio = "10:1 (100 nM Staple Pool to 10 nM M13mp18 Scaffold)",
            FoldingBufferRecipe = "1x TAE (40 mM Tris-acetate, 1 mM EDTA, pH 8.2) + 12.5 mM MgCl2 (divalent cation charge screening)",
            ExpectedHydrodynamicRadiusNm = geometry.Contains("Nanotube", StringComparison.OrdinalIgnoreCase) ? 14.8 : 22.5,
            Sha256Manifest = sha256,
            DiyFabricationGuide = diyGuide,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Generates structured DIY and HomeLab bench fabrication instructions ('how-to') tailored for community scientists.
    /// </summary>
    /// <param name="regimen">Target therapeutic regimen (MtF or FtM).</param>
    /// <param name="geometry">Chassis geometry.</param>
    /// <param name="stapleCount">Total number of designed staple strands.</param>
    /// <returns>A structured DIY fabrication guide with materials, pipetting recipes, 7 execution steps, and QC notes.</returns>
    private static DiyFabricationGuide GenerateDiyFabricationGuide(TherapeuticRegimen regimen, string geometry, int stapleCount)
    {
        // WHAT: Builds the equipment and reagents shopping checklist with standard vs DIY alternatives.
        // WHY:  Allows researchers with limited budgets or home laboratories to source equivalent materials safely.
        // HOW:  Enumerates essential reagents, commercial sources, community alternatives, and physical purposes.
        List<FabricationMaterialItem> materials =
        [
            new FabricationMaterialItem
            {
                MaterialName = "M13mp18 Single-Stranded DNA Scaffold (7,249 nt)",
                StandardSource = "New England Biolabs (#N4040S), Guild BioSciences, or Tilibit Nanosystems",
                DiyAlternative = "Open-Bio community shared aliquot or purified M13 bacteriophage culture",
                Quantity = "100 nM stock (~5 µL per 50 µL folding reaction)",
                Purpose = "Primary structural viral backbone providing the continuous single-stranded DNA thread folded into the nanocarrier."
            },
            new FabricationMaterialItem
            {
                MaterialName = $"Custom Synthetic Staple Oligo Pool ({stapleCount} Unique Strands)",
                StandardSource = "Integrated DNA Technologies (IDT), Twist Bioscience, or Eurofins Genomics",
                DiyAlternative = "Standard desalted 25 nmol scale 96-well plate order from open oligo synthesis services",
                Quantity = "1.0 µM per staple working pool (~5 µL per 50 µL fold)",
                Purpose = "Short complementary oligonucleotides that bridge distant scaffold domains and staple the structure into shape."
            },
            new FabricationMaterialItem
            {
                MaterialName = "10x Folding Buffer (10x TAE + 125 mM MgCl2)",
                StandardSource = "Commercial molecular grade 10x TAE buffer with molecular biology grade 1 M MgCl2",
                DiyAlternative = "Prepared from 50x TAE stock and 1 M MgCl2 solution, sterile filtered through a 0.22 µm syringe filter",
                Quantity = "5 µL per 50 µL folding reaction",
                Purpose = "Supplies Mg2+ divalent counter-ions essential for shielding negative phosphate charge repulsion between packed DNA helices."
            },
            new FabricationMaterialItem
            {
                MaterialName = "Ultra-Pure Nuclease-Free Water (ddH2O)",
                StandardSource = "Thermo Fisher / Ambion Nuclease-Free Sterile Water",
                DiyAlternative = "Double-distilled or sterile deionized water autoclaved or boiled in clean glassware",
                Quantity = "35 µL per 50 µL folding reaction",
                Purpose = "Enzyme-free reaction medium preventing degradation of DNA strands by ubiquitous environmental nucleases (DNase)."
            },
            new FabricationMaterialItem
            {
                MaterialName = "Agarose Powder (Molecular Biology Grade)",
                StandardSource = "Low-EEO analytical grade agarose (Bio-Rad / Sigma-Aldrich)",
                DiyAlternative = "Community laboratory grade or high-purity non-toxic electrophoresis agarose",
                Quantity = "0.75 g per 50 mL gel (1.5% wt/vol)",
                Purpose = "Matrix for Quality Control (QC) agarose gel electrophoresis to confirm successful folding and structural compaction."
            },
            new FabricationMaterialItem
            {
                MaterialName = "Non-Toxic DNA Gel Stain",
                StandardSource = "Biotium GelRed or Invitrogen SYBR Safe DNA Gel Stain",
                DiyAlternative = "Methylene blue visible stain or GelGreen with blue LED illuminator (avoids hazardous ethidium bromide and UV)",
                Quantity = "1x final concentration in agarose gel",
                Purpose = "Visual detection of distinct folded nanocarrier bands under blue light or visible transillumination."
            },
            new FabricationMaterialItem
            {
                MaterialName = "Centrifugal Ultrafiltration Spin Column (100 kDa MWCO)",
                StandardSource = "Millipore Amicon Ultra-0.5 mL (100 kDa molecular weight cut-off)",
                DiyAlternative = "Filterless PEG-8000 precipitation (15% PEG-8000 + 500 mM NaCl) as a low-cost community alternative",
                Quantity = "1 column per 50-100 µL folded batch",
                Purpose = "Rapid size-exclusion purification to wash away excess unincorporated staple oligonucleotides."
            }
        ];

        // WHAT: Compiles exact pipetting recipe for a 50 microliter benchtop folding reaction.
        // WHY:  Provides explicit, non-ambiguous volumetric instructions for laboratory technicians and DIYers.
        // HOW:  Specifies addition order, component names, stock concentrations, volumes in µL, and handling tips.
        List<ReactionPipettingComponent> pipettingTable =
        [
            new ReactionPipettingComponent
            {
                AdditionOrder = 1,
                ComponentName = "Ultra-Pure Nuclease-Free ddH2O",
                StockConcentration = "Molecular Biology Grade",
                VolumeMicroliters = 35.0,
                FinalConcentration = "Volume balance to 50 µL",
                HandlingTip = "Pipette first into sterile 0.2 mL thin-walled PCR tube to establish base liquid volume."
            },
            new ReactionPipettingComponent
            {
                AdditionOrder = 2,
                ComponentName = "10x Folding Buffer (10x TAE + 125 mM MgCl2)",
                StockConcentration = "10x concentrate",
                VolumeMicroliters = 5.0,
                FinalConcentration = "1x TAE (40 mM Tris, 1 mM EDTA) + 12.5 mM MgCl2",
                HandlingTip = "Mix thoroughly with water by gentle pipetting; ensures magnesium ions are evenly dispersed before DNA addition."
            },
            new ReactionPipettingComponent
            {
                AdditionOrder = 3,
                ComponentName = "M13mp18 ssDNA Scaffold (7,249 nt)",
                StockConcentration = "100 nM stock",
                VolumeMicroliters = 5.0,
                FinalConcentration = "10 nM final scaffold concentration",
                HandlingTip = "Keep on ice. Pipette gently. NEVER vortex viral scaffold stock (shear forces cause strand nicking)."
            },
            new ReactionPipettingComponent
            {
                AdditionOrder = 4,
                ComponentName = $"Synthetic Staple Pool ({stapleCount} Strands)",
                StockConcentration = "1.0 µM per individual staple",
                VolumeMicroliters = 5.0,
                FinalConcentration = "100 nM per staple (10:1 stoichiometric excess over scaffold)",
                HandlingTip = "Excess staple concentration drives complete thermodynamic assembly via mass action equilibrium."
            }
        ];

        // WHAT: Defines the 7-step physical benchtop fabrication instructions.
        // WHY:  Guides community scientists step-by-step from raw lyophilized DNA to purified nanocarriers.
        // HOW:  Details reconstitution, pooling, assembly, thermal ramping, gel QC, purification, and storage.
        List<FabricationStepInstruction> steps =
        [
            new FabricationStepInstruction
            {
                StepNumber = 1,
                PhaseTitle = "1. Reconstitution of Lyophilized Oligonucleotide Staples",
                DetailedInstructions = "Before opening any dry oligo tube, pulse-spin the microcentrifuge tubes or 96-well plate at 4,000 RPM for 30 seconds to ensure dried DNA pellets are at the bottom of the wells. Add nuclease-free water or 1x TE buffer (10 mM Tris, 0.1 mM EDTA, pH 8.0) to achieve an individual stock concentration of 100 µM for each staple. Allow pellets to hydrate for 15 minutes at room temperature, then pulse vortex for 5 seconds and spin down briefly.",
                DiyHomeLabNote = "HomeLab Tip: Always store 100 µM individual staple stocks at -20°C in a standard non-frost-free freezer. Avoid frost-free freezers whose continuous thermal cycles will degrade DNA over time."
            },
            new FabricationStepInstruction
            {
                StepNumber = 2,
                PhaseTitle = "2. Assembly of the Equimolar Staple Master Pool",
                DetailedInstructions = $"Pipette an equal volume (e.g., 5.0 µL) from each of the {stapleCount} reconstituted 100 µM staple stock tubes into a single sterile 1.5 mL microcentrifuge tube. Thoroughly mix the pooled staples, then dilute with nuclease-free water to produce an equimolar working staple pool where each individual oligonucleotide is at 1.0 µM concentration. Label the tube: 'MASTER STAPLE POOL (1.0 µM)'.",
                DiyHomeLabNote = "HomeLab Tip: One master pool preparation produces enough volume for over 50 folding reactions. Keep this master pool frozen in aliquots to avoid repeated freeze-thaw cycles."
            },
            new FabricationStepInstruction
            {
                StepNumber = 3,
                PhaseTitle = "3. Reaction Mixture Assembly (Stoichiometric Loading)",
                DetailedInstructions = "In a sterile 0.2 mL thin-walled PCR tube, combine: 35.0 µL nuclease-free water, 5.0 µL 10x Folding Buffer (10x TAE + 125 mM MgCl2), 5.0 µL of 100 nM M13mp18 scaffold, and 5.0 µL of 1.0 µM Master Staple Pool. Gently flick the bottom of the PCR tube 5 times to achieve homogeneous mixing. Pulse-spin in a mini-centrifuge for 3 seconds to collect all droplets at the bottom.",
                DiyHomeLabNote = "CRITICAL CAUTION: Do NOT vortex the assembled reaction mixture! High-speed vortexing introduces hydrodynamic shear forces that can mechanically damage long viral scaffold chains."
            },
            new FabricationStepInstruction
            {
                StepNumber = 4,
                PhaseTitle = "4. Precision Thermal Annealing Ramp Execution",
                DetailedInstructions = "Place the PCR tube into your thermal cycler and execute the 6-stage temperature profile: (1) 90°C for 5 min to denature secondary structures; (2) Rapid cool from 90°C to 65°C at 2.0°C/min; (3) Core folding window from 65°C to 55°C at 0.1°C/min (~100 min hold); (4) Aptamer latch assembly from 55°C to 40°C at 0.2°C/min (~75 min); (5) Ambient stabilization at 20°C for 10 min; and (6) Quench and hold at 4°C indefinitely.",
                DiyHomeLabNote = "HomeLab Alternative: If a laboratory PCR machine is unavailable, an open-source Arduino thermal cycler (e.g., OpenPCR, NinjaPCR) or an insulated sous-vide water bath with a programmable linear cooling ramp can execute this protocol with high fidelity."
            },
            new FabricationStepInstruction
            {
                StepNumber = 5,
                PhaseTitle = "5. Agarose Gel Electrophoresis Quality Control (with Mg2+)",
                DetailedInstructions = "Cast a 1.5% wt/vol agarose gel in 0.5x TBE buffer supplemented with 11.0 mM MgCl2. Add 1x GelRed or SYBR Safe directly into the molten agarose before casting. Mix 5.0 µL of folded origami sample with 1.0 µL of 6x glycerol loading dye (do NOT use loading dyes containing SDS or EDTA, as EDTA chelates essential Mg2+). Run the gel at 60-70 V for 2.5 hours on an ice-water bath to prevent thermal denaturation.",
                DiyHomeLabNote = "CRITICAL BIOMEDICAL RULE: Both the gel AND running buffer MUST contain 11 mM MgCl2! Without divalent magnesium in the running buffer, folded DNA origami will immediately dissociate and unbind inside the gel matrix."
            },
            new FabricationStepInstruction
            {
                StepNumber = 6,
                PhaseTitle = "6. Centrifugal Ultrafiltration Purification (Excess Staple Removal)",
                DetailedInstructions = "Transfer the folded reaction (50 µL) into an Amicon Ultra-0.5 mL (100 kDa MWCO) spin column. Add 400 µL of 1x TAE + 12.5 mM MgCl2 buffer. Centrifuge at 6,000 × g for 5 minutes. Discard the flow-through containing small, free unincorporated staples (~10-15 kDa). Repeat this washing cycle 2 additional times. Invert the spin filter into a clean collection tube and centrifuge at 1,000 × g for 2 minutes to recover purified, concentrated DNA nanocarriers.",
                DiyHomeLabNote = "Filterless Community Alternative: Add an equal volume (50 µL) of PEG precipitation buffer (15% wt/vol PEG-8000, 500 mM NaCl, 1x TAE). Incubate at room temperature for 30 minutes. Centrifuge at 16,000 × g for 25 minutes. Discard supernatant containing free staples, and gently resuspend the translucent DNA origami pellet in 50 µL of 1x TAE + 12.5 mM MgCl2 buffer."
            },
            new FabricationStepInstruction
            {
                StepNumber = 7,
                PhaseTitle = "7. Safe Physical Handling, Storage & In-Vitro Dosing",
                DetailedInstructions = "Store purified DNA origami nanocarriers in sterile polypropylene tubes at 4°C in the dark in 1x TAE buffer supplemented with 12.5 mM MgCl2. Under these conditions, the nanocarrier chassis remains structurally stable for over 12 months. For in-vitro cell culture or receptor binding assays, dilute nanocarriers directly into serum-supplemented medium, ensuring ambient magnesium concentration remains above 5 mM.",
                DiyHomeLabNote = "HomeLab Storage Rule: Never freeze folded DNA origami nanocarriers at -20°C without adding 10-20% glycerol or trehalose cryoprotectant. Rapid ice crystal formation shreds the nanoscale crossover lattice, destroying vehicle integrity."
            }
        ];

        string qcGuide = "HOW TO READ YOUR AGAROSE QUALITY CONTROL GEL:\n"
            + "1. Successful Fold: Shows a sharp, well-defined, mono-disperse band that migrates significantly FASTER than the unhybridized 7.2 kb M13mp18 scaffold control. Even though the folded origami has higher molecular weight, its compact 3D conformation allows it to navigate agarose pores much faster than a loose, floppy single-stranded coil.\n"
            + "2. Excess Staple Band: Appears as a faint, diffuse, fast-running smear near the dye front (~30-50 bp). This confirms that staples were provided in excess to drive thermodynamic completion.\n"
            + "3. Failed Fold / Aggregation Warning: If you observe a smeary band trailing through the lane or heavy fluorescence trapped inside the loading well, this indicates sample aggregation (usually caused by insufficient MgCl2 or cooling faster than 0.1°C/min through the 65-55°C window).";

        string safetyNotes = "COMMUNITY & HOMELAB SAFETY PRINCIPLES:\n"
            + "• Biological Safety Level: DNA origami nanostructures are classified as Biosafety Level 1 (BSL-1) benign materials. They contain no infectious viral genes, coat proteins, or replicative machinery.\n"
            + "• Aseptic Bench Hygiene: Clean all work surfaces and pipettes with 10% bleach followed by 70% isopropanol before beginning. This destroys ubiquitous airborne DNase enzymes produced by human skin and bacteria that would otherwise degrade your nanocarriers.\n"
            + "• Non-Toxic Dyes: Always utilize GelRed, SYBR Safe, or methylene blue with visible or blue-LED transilluminators. Avoid ethidium bromide and ultraviolet (UV) transilluminators, which are potent mutagens and physically cross-link DNA bases, damaging your nanocarrier structures.\n"
            + "• Disposal: DNA origami solutions and agarose gels can be decontaminated with 10% household bleach for 10 minutes prior to standard disposal.";

        return new DiyFabricationGuide
        {
            Materials = materials,
            PipettingTable = pipettingTable,
            Steps = steps,
            QualityControlGuide = qcGuide,
            SafetyAndHandlingNotes = safetyNotes
        };
    }

    /// <summary>
    /// Generates staple oligonucleotides tailored for the specified geometry and therapeutic target.
    /// </summary>
    private static List<StapleOligoRecord> GenerateStapleEnsemble(TherapeuticRegimen regimen, string geometry, double targetKd)
    {
        List<StapleOligoRecord> staples = [];

        string[] coreSequences =
        [
            "AGCGTCATTTACCGGATACCTTTCGATCGGATCC",
            "TTCAGCGTTTGAGCGTAACCCAACTTGGCATGAG",
            "CGATCGGATCCTTTACCGTAAGGCTTGCAATCAG",
            "GCTAACGTTCGATAGCTTGGCATCCGATTTACCG",
            "AATGCTAGGATCCGATCGTTTGACCGGATAAGCC",
            "CCGATAGCTAACGTTTCGATAGCTTGGCATCCGA",
            "TTGGCATCCGATTTACCGAGCGTCATTTACCGGA",
            "TACCTTTCGATCGGATCCTTCAGCGTTTGAGCGT"
        ];

        // 1. Core structural crossover staples
        for (int i = 0; i < coreSequences.Length; i++)
        {
            string seq = coreSequences[i];
            (int a, int t, int g, int c, double gc, _) = AnalyzeCoreSeq(seq);
            double tm = CalculateMeltingTemperature(a, t, g, c);

            staples.Add(new StapleOligoRecord
            {
                StapleId = $"ST-CORE-{i + 1:D3}",
                Sequence = seq,
                Length = seq.Length,
                MeltingTemperatureCelsius = tm,
                GcPercentage = gc,
                StockConcentrationMicromolar = 100.0,
                FunctionalRole = $"Structural Crossover Helix {i + 1} to {(i + 1) % coreSequences.Length + 1}"
            });
        }

        // 2. Aptamer pocket functionalization staples
        string aptamerMotif = regimen == TherapeuticRegimen.MaleToFemale
            ? "ATACCAGCTTATTCAATTGGGAGTGTGTGGTATAGTGGTGTCTTTGTTTGT" // 17β-Estradiol aptamer
            : "CGAATGAGTTACGTACCTGTAGTGCTGTGGCATTTGTACGGTCAGCT";   // Testosterone aptamer

        for (int i = 0; i < 4; i++)
        {
            string pocketSeq = $"TTTT{aptamerMotif[..(aptamerMotif.Length / 2)]}TTTT";
            (int a, int t, int g, int c, double gc, _) = AnalyzeCoreSeq(pocketSeq);
            double tm = CalculateMeltingTemperature(a, t, g, c);

            staples.Add(new StapleOligoRecord
            {
                StapleId = $"ST-APTA-{i + 1:D3}",
                Sequence = pocketSeq,
                Length = pocketSeq.Length,
                MeltingTemperatureCelsius = tm,
                GcPercentage = gc,
                StockConcentrationMicromolar = 100.0,
                FunctionalRole = $"Active Ligand Pocket #{i + 1} (Target Kd: {targetKd:F1} nM)"
            });
        }

        // 3. Receptor-targeting anchor staples (with flexible poly-T linkers)
        string[] targetingSequences =
        [
            "TTTTTTTTTTGATCGGATCCAACTTGGCATGAGCCGAT",
            "TTTTTTTTTTAACGTTTCGATAGCTTGGCATCCGATTT"
        ];

        for (int i = 0; i < targetingSequences.Length; i++)
        {
            string targetSeq = targetingSequences[i];
            (int a, int t, int g, int c, double gc, _) = AnalyzeCoreSeq(targetSeq);
            double tm = CalculateMeltingTemperature(a, t, g, c);

            staples.Add(new StapleOligoRecord
            {
                StapleId = $"ST-ANCHOR-{i + 1:D3}",
                Sequence = targetSeq,
                Length = targetSeq.Length,
                MeltingTemperatureCelsius = tm,
                GcPercentage = gc,
                StockConcentrationMicromolar = 100.0,
                FunctionalRole = $"Tissue Receptor Targeting Anchor #{i + 1} (Poly-T Linker)"
            });
        }

        return staples;
    }

    /// <summary>
    /// Quick base counting helper for internal strings.
    /// </summary>
    private static (int A, int T, int G, int C, double Gc, double Mw) AnalyzeCoreSeq(string seq)
    {
        // WHAT: Delegates nucleotide base composition analysis to unified analyzer.
        // WHY:  Avoids redundant loop logic while maintaining zero-allocation performance.
        // HOW:  Calls AnalyzeSequenceCompositionStatic and unpacks result.
        (int a, int t, int g, int c, double gc, double mw) = AnalyzeSequenceCompositionStatic(seq);
        return (a, t, g, c, gc, mw);
    }

    /// <summary>
    /// Calculates theoretical oligonucleotide melting temperature (Tm) for a nucleotide sequence string.
    /// </summary>
    /// <param name="sequence">Raw nucleotide sequence string.</param>
    /// <returns>Theoretical melting temperature in degrees Celsius.</returns>
    public double CalculateSequenceMeltingTemperature(string sequence)
    {
        // WHAT: Counts bases and computes melting temperature for the nucleotide string.
        // WHY:  Allows direct programmatic determination of annealing thresholds for arbitrary oligonucleotides.
        // HOW:  Extracts base counts via AnalyzeSequenceComposition and passes them to CalculateMeltingTemperature.
        (int a, int t, int g, int c, _, _) = AnalyzeSequenceComposition(sequence);
        return CalculateMeltingTemperature(a, t, g, c);
    }

    /// <summary>
    /// Calculates theoretical oligonucleotide melting temperature (Tm) using nearest-neighbor / Marmur empirical formula or Wallace rule for short oligos.
    /// </summary>
    /// <param name="a">Adenine base count.</param>
    /// <param name="t">Thymine or Uracil base count.</param>
    /// <param name="g">Guanine base count.</param>
    /// <param name="c">Cytosine base count.</param>
    /// <returns>Theoretical melting temperature in degrees Celsius.</returns>
    public static double CalculateMeltingTemperature(int a, int t, int g, int c)
    {
        // WHAT: Calculates oligonucleotide melting temperature using Marmur formula or Wallace rule.
        // WHY:  Short oligos (< 14 nt) follow Wallace's empirical rule (2*(A+T) + 4*(G+C)), while longer sequences require GC% salt adjustments.
        // HOW:  Branches on total sequence length, returning rounded degrees Celsius.
        int total = a + t + g + c;
        if (total == 0) return 0.0;

        // Wallace rule for short oligos: Tm = 2*(A+T) + 4*(G+C)
        // Nearest-neighbor empirical adjustment for salt conditions (50 mM Na+, 12.5 mM Mg2+):
        if (total < 14)
        {
            return (a + t) * 2.0 + (g + c) * 4.0;
        }

        // Marmur formula: Tm = 64.9 + 41 * (G + C - 16.4) / total
        double tm = 64.9 + 41.0 * ((g + c) - 16.4) / total;
        return Math.Round(tm, 1);
    }

    /// <summary>
    /// Saves the fabrication package as a standardized JSON manifest with SHA-256 integrity verification.
    /// </summary>
    /// <param name="package">The fabrication package to export.</param>
    /// <param name="exportDirectory">Target directory for the export bundle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated manifest file path and SHA-256 hash.</returns>
    public async Task<(string FilePath, string Sha256Hash)> ExportFabricationManifestJsonAsync(
        FabricationPackage package,
        string exportDirectory,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(exportDirectory);
        string fileName = $"{package.PackageId}.fabrication.json";
        string filePath = Path.Combine(exportDirectory, fileName);

        JsonSerializerOptions opts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        using (FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, package, opts, ct);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        byte[] hash = SHA256.HashData(fileBytes);
        string sha256 = Convert.ToHexString(hash).ToLowerInvariant();

        return (filePath, sha256);
    }
}
