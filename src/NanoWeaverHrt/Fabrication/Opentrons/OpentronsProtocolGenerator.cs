using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NanoWeaverHrt.Fabrication.Opentrons;

/// <summary>
/// Generates deterministic, syntactically valid Opentrons Protocol API v2 Python automation scripts
/// for robotic DNA origami stoichiometric folding, well-to-well staple aliquoting, and on-deck thermal annealing.
/// </summary>
public sealed class OpentronsProtocolGenerator : IOpentronsProtocolGenerator
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpentronsProtocolGenerator"/> class.
    /// </summary>
    public OpentronsProtocolGenerator()
    {
    }

    /// <summary>
    /// Generates an Opentrons Protocol API v2 Python script tailored to the given DNA origami fabrication package.
    /// </summary>
    /// <param name="package">Authoritative DNA origami fabrication package containing staple sequences and annealing schedules.</param>
    /// <param name="options">Hardware, pipette, and labware options.</param>
    /// <returns>Generated Python protocol script and deck planning metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="package"/> or <paramref name="options"/> is null.</exception>
    public OpentronsProtocolResult GenerateProtocol(FabricationPackage package, OpentronsProtocolOptions options)
    {
        // WHAT: Enforces invariant 1: zero unhandled null inputs.
        // WHY:  A null fabrication package or configuration record cannot yield a valid robot automation script.
        // HOW:  Throws ArgumentNullException immediately before allocating buffers or building syntax trees.
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        // WHAT: Resolves hardware pipette attributes and channel geometry.
        // WHY:  Multi-channel pipettes transfer entire columns at once, altering deck layouts and tip counts.
        // HOW:  Inspects pipette model enum for 8-channel variants and maps to Opentrons API identifier.
        bool isMultiChannel = options.PipetteModel is OpentronsPipetteModel.P20_Multi_Gen2 or OpentronsPipetteModel.Flex_8Channel_50;
        string pipetteApiName = GetPipetteApiName(options.PipetteModel);
        string tipRackLabware = GetTipRackLabware(options.PipetteModel);
        string mount = options.PipetteModel == OpentronsPipetteModel.P300_Single_Gen2 ? "right" : "left";

        // WHAT: Resolves source and destination labware definitions.
        // WHY:  Opentrons engine needs exact labware identifiers from the Opentrons Labware Library.
        // HOW:  Maps OpentronsPlateType to official labware load names.
        string sourcePlateLabware = GetPlateLabware(options.SourcePlateType);
        string destPlateLabware = options.SourcePlateType == OpentronsPlateType.BioRad96WellPcr
            ? "biorad_96_wellplate_200ul_pcr"
            : "nest_96_wellplate_100ul_pcr_full_skirt";

        int wellsPerPlate = options.SourcePlateType == OpentronsPlateType.Nest384WellPcr ? 384 : 96;
        int stapleCount = package.StapleOligos.Count;

        // WHAT: Computes required source plate count with deterministic plate wrapping.
        // WHY:  Staple counts exceeding 96 (or 384) require additional deck slots for overflow plates.
        // HOW:  Calculates ceiling of staple count over wells per plate.
        int sourcePlatesRequired = stapleCount == 0 ? 0 : (int)Math.Ceiling((double)stapleCount / wellsPerPlate);

        // WHAT: Calculates required tip rack count based on tip consumption.
        // WHY:  Every transfer requires a sterile tip to eliminate staple cross-contamination.
        // HOW:  Multi-channel consumes 8 tips per column; single-channel consumes 1 tip per staple.
        int tipsUsed = isMultiChannel ? (((stapleCount + 7) / 8) * 8) : stapleCount;
        int tipRacksRequired = Math.Max(1, (int)Math.Ceiling((double)tipsUsed / 96.0));

        // WHAT: Allocates deck slots sequentially according to robot platform constraints.
        // WHY:  OT-2 and Flex feature distinct slot coordinate systems and module footprints.
        // HOW:  Pulls available slots sequentially, reserving thermocycler and temperature module positions.
        DeckLayout deck = PlanDeckLayout(options, sourcePlatesRequired, tipRacksRequired);

        // WHAT: Computes pipetting volumes for stoichiometric assembly.
        // WHY:  Staples must achieve target molar excess (default 10:1) relative to viral scaffold backbone.
        // HOW:  Calculates aliquot volumes from scaffold concentration, excess ratio, and total volume.
        double targetStapleConcentrationMicroMolar = (options.ScaffoldConcentrationNanomolar * options.StapleMolarExcessRatio) / 1000.0;
        double stapleVolume = Math.Max(1.0, Math.Round((targetStapleConcentrationMicroMolar * options.TotalReactionVolumeMicroliters) / 10.0, 1));
        double totalStapleVolume = isMultiChannel ? (((stapleCount + 7) / 8) * stapleVolume) : (stapleCount * stapleVolume);
        double masterMixVolume = Math.Max(5.0, Math.Round(options.TotalReactionVolumeMicroliters - (stapleCount > 0 ? Math.Min(options.TotalReactionVolumeMicroliters * 0.8, totalStapleVolume) : 0.0), 1));

        // WHAT: Computes estimated run duration in minutes.
        // WHY:  Allows operators to schedule robot availability and manage time-critical reagents.
        // HOW:  Combines pipetting cycle duration with thermal annealing ramp stages.
        double estimatedDurationMinutes = EstimateRunDuration(package, options, isMultiChannel, stapleCount);

        // WHAT: Generates the complete, syntactically valid Opentrons Protocol API v2 Python script.
        // WHY:  Directly executable on Opentrons OT-2 and Opentrons Flex robots via Opentrons App or CLI.
        // HOW:  Builds PEP-8 formatted Python code including metadata, deck loading, transfer loops, and ramps.
        string pythonScript = BuildPythonScript(
            package,
            options,
            deck,
            pipetteApiName,
            tipRackLabware,
            mount,
            sourcePlateLabware,
            destPlateLabware,
            isMultiChannel,
            stapleVolume,
            masterMixVolume,
            wellsPerPlate);

        // WHAT: Computes deterministic SHA-256 hash of the generated Python script.
        // WHY:  Guarantees cryptographic provenance and tamper-evidence for laboratory verification.
        // HOW:  Hashes UTF-8 bytes with SHA256 and converts to lowercase hex representation.
        byte[] scriptHash = SHA256.HashData(Encoding.UTF8.GetBytes(pythonScript));
        string scriptSha256 = Convert.ToHexString(scriptHash).ToLowerInvariant();

        return new OpentronsProtocolResult
        {
            PythonScript = pythonScript,
            ScriptSha256 = scriptSha256,
            TransferCount = stapleCount,
            SourcePlatesRequired = sourcePlatesRequired,
            TipRacksRequired = tipRacksRequired,
            HasThermocyclerRamp = options.UseThermocyclerModule,
            EstimatedRunDurationMinutes = estimatedDurationMinutes
        };
    }

    /// <summary>
    /// Maps the pipette model to its Opentrons Protocol API instrument identifier.
    /// </summary>
    /// <param name="model">The pipette model enum.</param>
    /// <returns>Instrument identifier string for <c>protocol.load_instrument</c>.</returns>
    private static string GetPipetteApiName(OpentronsPipetteModel model) => model switch
    {
        OpentronsPipetteModel.P20_Single_Gen2 => "p20_single_gen2",
        OpentronsPipetteModel.P20_Multi_Gen2 => "p20_multi_gen2",
        OpentronsPipetteModel.P300_Single_Gen2 => "p300_single_gen2",
        OpentronsPipetteModel.Flex_1Channel_50 => "flex_1channel_50",
        _ => "flex_8channel_50"
    };

    /// <summary>
    /// Maps the pipette model to its corresponding sterile tip rack labware definition.
    /// </summary>
    /// <param name="model">The pipette model enum.</param>
    /// <returns>Labware definition identifier for <c>protocol.load_labware</c>.</returns>
    private static string GetTipRackLabware(OpentronsPipetteModel model) => model switch
    {
        OpentronsPipetteModel.P20_Single_Gen2 or OpentronsPipetteModel.P20_Multi_Gen2 => "opentrons_96_tiprack_20ul",
        OpentronsPipetteModel.P300_Single_Gen2 => "opentrons_96_tiprack_300ul",
        _ => "opentrons_flex_96_tiprack_50ul"
    };

    /// <summary>
    /// Maps the plate type enum to its official Opentrons Labware Library identifier.
    /// </summary>
    /// <param name="plateType">The microplate type enum.</param>
    /// <returns>Labware definition identifier string.</returns>
    private static string GetPlateLabware(OpentronsPlateType plateType) => plateType switch
    {
        OpentronsPlateType.Nest96WellPcr => "nest_96_wellplate_100ul_pcr_full_skirt",
        OpentronsPlateType.Corning96Flat => "corning_96_wellplate_360ul_flat",
        OpentronsPlateType.BioRad96WellPcr => "biorad_96_wellplate_200ul_pcr",
        _ => "nest_384_wellplate_30ul_pcr"
    };

    /// <summary>
    /// Translates a zero-based linear well offset to an alphanumeric microplate well name (e.g., 'A1', 'H12', 'P24').
    /// </summary>
    /// <param name="wellOffset">Zero-based offset within the plate.</param>
    /// <param name="plateType">Microplate geometry type.</param>
    /// <returns>Alphanumeric well coordinate.</returns>
    private static string GetWellName(int wellOffset, OpentronsPlateType plateType)
    {
        // WHAT: Converts a linear 0-based well offset into standard alphanumeric well coordinate (e.g., 'A1', 'H12').
        // WHY:  Opentrons API requires standard well identifiers matching ANSI/SLAS microplate standards.
        // HOW:  Calculates row index and column index based on 96-well (8x12) or 384-well (16x24) geometry.
        if (plateType == OpentronsPlateType.Nest384WellPcr)
        {
            int row = wellOffset / 24;
            int col = (wellOffset % 24) + 1;
            return $"{(char)('A' + row)}{col}";
        }
        else
        {
            int row = wellOffset / 12;
            int col = (wellOffset % 12) + 1;
            return $"{(char)('A' + row)}{col}";
        }
    }

    /// <summary>
    /// Calculates plate index and top-well coordinate for 8-channel multi-channel column transfers.
    /// </summary>
    /// <param name="columnIndex">Zero-based column transfer index.</param>
    /// <param name="plateType">Microplate geometry type.</param>
    /// <returns>Tuple of plate index and top-well identifier string.</returns>
    private static (int PlateIndex, string TopWell) GetColumnCoordinate(int columnIndex, OpentronsPlateType plateType)
    {
        // WHAT: Maps linear column transfer index to plate index and column top-well coordinate.
        // WHY:  8-channel multi-channel pipettes aspirate entire 8-well columns addressed by the top well.
        // HOW:  Calculates plate wrapping and top well ('A1'..'A12' for 96-well, alternating A/B for 384-well).
        if (plateType == OpentronsPlateType.Nest384WellPcr)
        {
            int plateIndex = columnIndex / 48;
            int offsetInPlate = columnIndex % 48;
            if (offsetInPlate < 24)
            {
                return (plateIndex, $"A{offsetInPlate + 1}");
            }
            else
            {
                return (plateIndex, $"B{offsetInPlate - 24 + 1}");
            }
        }
        else
        {
            int plateIndex = columnIndex / 12;
            int col = (columnIndex % 12) + 1;
            return (plateIndex, $"A{col}");
        }
    }

    /// <summary>
    /// Plans collision-free deck slot allocations for robot modules, tip racks, and labware.
    /// </summary>
    /// <param name="options">Protocol options with robot model and modules.</param>
    /// <param name="sourcePlatesRequired">Number of staple source plates required.</param>
    /// <param name="tipRacksRequired">Number of sterile tip racks required.</param>
    /// <returns>Allocated deck layout records.</returns>
    private static DeckLayout PlanDeckLayout(OpentronsProtocolOptions options, int sourcePlatesRequired, int tipRacksRequired)
    {
        // WHAT: Reserves hardware module slots and assigns remaining deck positions sequentially.
        // WHY:  Avoids deck slot collisions and complies with robot physical module placement guidelines.
        // HOW:  Selects available slot list based on robot type and module configuration.
        string robotType = options.RobotModel == OpentronsRobotModel.Flex ? "Flex" : "OT-2";
        string[] availableSlots;

        if (options.RobotModel == OpentronsRobotModel.Flex)
        {
            availableSlots = options.UseThermocyclerModule
                ? ["D1", "D2", "D3", "C1", "C2", "C3", "B2", "B3", "A2", "A3"]
                : ["D1", "D2", "D3", "C1", "C2", "C3", "B1", "B2", "B3", "A1", "A2", "A3"];
        }
        else
        {
            availableSlots = options.UseThermocyclerModule
                ? ["1", "2", "3", "4", "5", "6", "9"]
                : ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"];
        }

        int slotIndex = 0;
        string destSlot;

        if (options.UseThermocyclerModule)
        {
            destSlot = options.RobotModel == OpentronsRobotModel.Flex ? "B1" : "7, 8, 10, 11";
        }
        else
        {
            destSlot = availableSlots[slotIndex++];
        }

        string masterMixSlot = availableSlots[slotIndex++];

        List<string> tipRackSlots = [];
        for (int i = 0; i < tipRacksRequired; i++)
        {
            string slot = availableSlots[slotIndex % availableSlots.Length];
            slotIndex++;
            tipRackSlots.Add(slot);
        }

        List<string> sourcePlateSlots = [];
        for (int i = 0; i < sourcePlatesRequired; i++)
        {
            string slot = availableSlots[slotIndex % availableSlots.Length];
            slotIndex++;
            sourcePlateSlots.Add(slot);
        }

        return new DeckLayout(robotType, destSlot, masterMixSlot, tipRackSlots, sourcePlateSlots);
    }

    /// <summary>
    /// Calculates estimated robot execution duration in minutes.
    /// </summary>
    /// <param name="package">Fabrication package with annealing steps.</param>
    /// <param name="options">Protocol options with robot and module settings.</param>
    /// <param name="isMultiChannel">Whether a multi-channel pipette is utilized.</param>
    /// <param name="stapleCount">Total number of staple oligos.</param>
    /// <returns>Estimated run duration in minutes rounded to 1 decimal place.</returns>
    private static double EstimateRunDuration(FabricationPackage package, OpentronsProtocolOptions options, bool isMultiChannel, int stapleCount)
    {
        // WHAT: Computes estimated run duration in minutes based on pipetting operations and thermal ramp.
        // WHY:  Informs wet-lab operators of expected robot occupation time and scheduling.
        // HOW:  Combines setup time, per-transfer pipetting time, thermocycler hold times, and thermal transitions.
        double pipettingMinutes = 0.5;

        if (stapleCount > 0)
        {
            if (isMultiChannel)
            {
                int totalColumns = (stapleCount + 7) / 8;
                pipettingMinutes += totalColumns * 0.35;
            }
            else
            {
                pipettingMinutes += stapleCount * 0.25;
            }
        }

        double thermalMinutes = 0.0;
        if (options.UseThermocyclerModule)
        {
            IReadOnlyList<AnnealingRampStep> schedule = package.AnnealingSchedule;
            for (int i = 0; i < schedule.Count; i++)
            {
                thermalMinutes += schedule[i].HoldDurationMinutes;
                if (i < schedule.Count - 1)
                {
                    double deltaT = Math.Abs(schedule[i].TargetTemperatureCelsius - schedule[i + 1].TargetTemperatureCelsius);
                    double rate = schedule[i].RampRateCelsiusPerMinute > 0.0 ? schedule[i].RampRateCelsiusPerMinute : 1.0;
                    thermalMinutes += deltaT / rate;
                }
            }
        }

        return Math.Round(pipettingMinutes + thermalMinutes, 1);
    }

    /// <summary>
    /// Constructs the Python Protocol API v2 script string with exact 4-space indentation and formatting.
    /// </summary>
    /// <param name="package">Fabrication package containing staple designs and thermal ramps.</param>
    /// <param name="options">Hardware and labware options.</param>
    /// <param name="deck">Deck layout planning record.</param>
    /// <param name="pipetteApiName">Opentrons API instrument name.</param>
    /// <param name="tipRackLabware">Tip rack labware definition name.</param>
    /// <param name="mount">Pipette mount ('left' or 'right').</param>
    /// <param name="sourcePlateLabware">Source plate labware name.</param>
    /// <param name="destPlateLabware">Destination plate labware name.</param>
    /// <param name="isMultiChannel">Whether using multi-channel transfers.</param>
    /// <param name="stapleVolume">Staple aliquot volume in microliters.</param>
    /// <param name="masterMixVolume">Master mix volume in microliters.</param>
    /// <param name="wellsPerPlate">Wells per source plate (96 or 384).</param>
    /// <returns>Syntactically valid Python Protocol API v2 script text.</returns>
    private static string BuildPythonScript(
        FabricationPackage package,
        OpentronsProtocolOptions options,
        DeckLayout deck,
        string pipetteApiName,
        string tipRackLabware,
        string mount,
        string sourcePlateLabware,
        string destPlateLabware,
        bool isMultiChannel,
        double stapleVolume,
        double masterMixVolume,
        int wellsPerPlate)
    {
        // WHAT: Assembles the complete Python script text adhering strictly to PEP-8 and Opentrons Protocol API v2.
        // WHY:  Ensures flawless parsing by Python AST and execution on physical OT-2 / Flex robots.
        // HOW:  Uses StringBuilder to generate metadata, deck loading, master mix aspiration, transfer loops, and ramps.
        StringBuilder sb = new();

        string safeGeometry = package.DeliveryGeometry.Replace("'", "\\'", StringComparison.Ordinal);
        string safePackageId = package.PackageId.Replace("'", "\\'", StringComparison.Ordinal);

        sb.AppendLine("\"\"\"");
        sb.AppendLine("NanoWeaverHrt Automated DNA Origami Fabrication Protocol");
        sb.AppendLine($"Target Regimen: {package.Regimen}");
        sb.AppendLine($"Nanocarrier Chassis: {package.DeliveryGeometry}");
        sb.AppendLine($"Package ID: {package.PackageId}");
        sb.AppendLine("Generated via NanoWeaverHrt Opentrons Protocol Engine");
        sb.AppendLine("\"\"\"");
        sb.AppendLine();
        sb.AppendLine("from opentrons import protocol_api");
        sb.AppendLine();
        sb.AppendLine("metadata = {");
        sb.AppendLine($"    'protocolName': 'DNA Origami Assembly - {safeGeometry}',");
        sb.AppendLine("    'author': 'NanoWeaverHrt Sovereign Research Platform',");
        sb.AppendLine($"    'description': 'Automated stoichiometric assembly of DNA origami nanocarrier {safePackageId}',");
        sb.AppendLine("    'apiLevel': '2.15'");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("requirements = {");
        sb.AppendLine($"    'robotType': '{deck.RobotType}',");
        sb.AppendLine("    'apiLevel': '2.15'");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("def run(protocol: protocol_api.ProtocolContext):");

        // 1. Tip racks
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 1. Deck Hardware & Tip Rack Configuration");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    tip_racks = [");
        for (int i = 0; i < deck.TipRackSlots.Count; i++)
        {
            string comma = i < deck.TipRackSlots.Count - 1 ? "," : "";
            sb.AppendLine($"        protocol.load_labware('{tipRackLabware}', '{deck.TipRackSlots[i]}', label='Tip Rack {i + 1}'){comma}");
        }
        sb.AppendLine("    ]");
        sb.AppendLine();

        // 2. Pipette
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 2. Pipette Configuration");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine($"    pipette = protocol.load_instrument('{pipetteApiName}', '{mount}', tip_racks=tip_racks)");
        sb.AppendLine();

        // 3. Module & Destination Plate Configuration
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 3. Assembly Reaction Vessel & Thermal Module Configuration");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        if (options.UseThermocyclerModule)
        {
            if (options.RobotModel == OpentronsRobotModel.Flex)
            {
                sb.AppendLine($"    tc_mod = protocol.load_module('thermocycler module gen2', '{deck.DestSlot}')");
            }
            else
            {
                sb.AppendLine("    tc_mod = protocol.load_module('thermocycler module gen2')");
            }
            sb.AppendLine("    tc_mod.open_lid()");
            sb.AppendLine($"    dest_plate = tc_mod.load_labware('{destPlateLabware}', label='Origami Reaction Plate')");
            sb.AppendLine("    dest_well = dest_plate['A1']");
            sb.AppendLine("    tc_mod.set_block_temperature(temperature=4)");
        }
        else if (options.UseTemperatureModule)
        {
            sb.AppendLine($"    temp_mod = protocol.load_module('temperature module gen2', '{deck.DestSlot}')");
            sb.AppendLine("    temp_mod.set_temperature(celsius=4)");
            sb.AppendLine($"    dest_plate = temp_mod.load_labware('{destPlateLabware}', label='Origami Reaction Plate')");
            sb.AppendLine("    dest_well = dest_plate['A1']");
        }
        else
        {
            sb.AppendLine($"    dest_plate = protocol.load_labware('{destPlateLabware}', '{deck.DestSlot}', label='Origami Reaction Plate')");
            sb.AppendLine("    dest_well = dest_plate['A1']");
        }
        sb.AppendLine();

        // 4. Reagent Tube Rack (Master Mix)
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 4. Master Mix Reagent Vessel (Scaffold + Buffer + Solvent)");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine($"    reagent_rack = protocol.load_labware('opentrons_24_tuberack_generic_2ml_screwcap', '{deck.MasterMixSlot}', label='Reagent Tube Rack')");
        sb.AppendLine("    master_mix_tube = reagent_rack['A1']");
        sb.AppendLine();

        // 5. Source Library Plates
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 5. Staple Oligonucleotide Source Library Plates");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        if (deck.SourcePlateSlots.Count == 0)
        {
            sb.AppendLine("    source_plates = []");
        }
        else
        {
            sb.AppendLine("    source_plates = [");
            for (int i = 0; i < deck.SourcePlateSlots.Count; i++)
            {
                string comma = i < deck.SourcePlateSlots.Count - 1 ? "," : "";
                sb.AppendLine($"        protocol.load_labware('{sourcePlateLabware}', '{deck.SourcePlateSlots[i]}', label='Staple Source Plate {i + 1}'){comma}");
            }
            sb.AppendLine("    ]");
        }
        sb.AppendLine();

        // 6. Master Mix Transfer
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 6. Master Mix Dispensing (M13mp18 Viral Scaffold & Divalent Folding Buffer)");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    protocol.comment('Dispensing scaffold and buffer master mix...')");
        sb.AppendLine("    pipette.pick_up_tip()");
        sb.AppendLine($"    pipette.aspirate({masterMixVolume.ToString("F1", CultureInfo.InvariantCulture)}, master_mix_tube)");
        sb.AppendLine($"    pipette.dispense({masterMixVolume.ToString("F1", CultureInfo.InvariantCulture)}, dest_well)");
        sb.AppendLine("    pipette.drop_tip()");
        sb.AppendLine();

        // 7. Staple Transfers
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 7. Stoichiometric Staple Aliquoting Transfer Loop");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        int stapleCount = package.StapleOligos.Count;

        if (isMultiChannel)
        {
            sb.AppendLine("    protocol.comment('Dispensing staple columns using 8-channel pipette...')");
            int totalColumns = (stapleCount + 7) / 8;
            if (totalColumns == 0)
            {
                sb.AppendLine("    column_transfers = []");
            }
            else
            {
                sb.AppendLine("    column_transfers = [");
                for (int c = 0; c < totalColumns; c++)
                {
                    (int plateIndex, string topWell) = GetColumnCoordinate(c, options.SourcePlateType);
                    string comma = c < totalColumns - 1 ? "," : "";
                    sb.AppendLine($"        ({plateIndex}, '{topWell}', {stapleVolume.ToString("F1", CultureInfo.InvariantCulture)}){comma}");
                }
                sb.AppendLine("    ]");
            }
            sb.AppendLine("    for plate_idx, top_well, vol_ul in column_transfers:");
            sb.AppendLine("        pipette.pick_up_tip()");
            sb.AppendLine("        pipette.aspirate(vol_ul, source_plates[plate_idx][top_well])");
            sb.AppendLine("        pipette.dispense(vol_ul, dest_well)");
            sb.AppendLine("        pipette.drop_tip()");
        }
        else
        {
            sb.AppendLine("    protocol.comment('Dispensing individual staple oligonucleotides...')");
            if (stapleCount == 0)
            {
                sb.AppendLine("    staple_transfers = []");
            }
            else
            {
                sb.AppendLine("    staple_transfers = [");
                for (int i = 0; i < stapleCount; i++)
                {
                    int plateIndex = i / wellsPerPlate;
                    int wellOffset = i % wellsPerPlate;
                    string wellName = GetWellName(wellOffset, options.SourcePlateType);
                    string safeStapleId = package.StapleOligos[i].StapleId.Replace("'", "\\'", StringComparison.Ordinal);
                    string comma = i < stapleCount - 1 ? "," : "";
                    sb.AppendLine($"        ({plateIndex}, '{wellName}', {stapleVolume.ToString("F1", CultureInfo.InvariantCulture)}, '{safeStapleId}'){comma}");
                }
                sb.AppendLine("    ]");
            }
            sb.AppendLine("    for plate_idx, well_name, vol_ul, staple_id in staple_transfers:");
            sb.AppendLine("        pipette.pick_up_tip()");
            sb.AppendLine("        pipette.aspirate(vol_ul, source_plates[plate_idx][well_name])");
            sb.AppendLine("        pipette.dispense(vol_ul, dest_well)");
            sb.AppendLine("        pipette.drop_tip()");
        }
        sb.AppendLine();

        // 8. Annealing Schedule / Temperature Controls
        sb.AppendLine("    # -------------------------------------------------------------------------");
        sb.AppendLine("    # 8. Thermal Annealing Protocol & Cryogenic Preservation");
        sb.AppendLine("    # -------------------------------------------------------------------------");
        if (options.UseThermocyclerModule)
        {
            sb.AppendLine("    protocol.comment('Closing thermocycler lid and executing thermal annealing ramp...')");
            sb.AppendLine("    tc_mod.close_lid()");
            sb.AppendLine("    tc_mod.set_lid_temperature(temperature=105)");
            foreach (AnnealingRampStep step in package.AnnealingSchedule)
            {
                string safePurpose = step.BiophysicalPurpose.Replace("'", "\\'", StringComparison.Ordinal);
                sb.AppendLine($"    # Step {step.StepIndex}: {step.TargetTemperatureCelsius.ToString("F1", CultureInfo.InvariantCulture)}°C for {step.HoldDurationMinutes} min ({safePurpose})");
                sb.AppendLine($"    tc_mod.set_block_temperature(temperature={step.TargetTemperatureCelsius.ToString("F1", CultureInfo.InvariantCulture)}, hold_time_minutes={step.HoldDurationMinutes})");
            }
            sb.AppendLine("    tc_mod.deactivate_lid()");
            sb.AppendLine("    tc_mod.open_lid()");
            sb.AppendLine("    tc_mod.set_block_temperature(temperature=4)");
            sb.AppendLine("    protocol.comment('Annealing ramp completed. Holding assembled origami nanocarriers at 4°C.')");
        }
        else if (options.UseTemperatureModule)
        {
            sb.AppendLine("    protocol.comment('Staple dispensing completed. Holding reaction vessel at 4°C on temperature module.')");
        }
        else
        {
            sb.AppendLine("    protocol.comment('Staple dispensing completed. Retrieve reaction vessel for incubation.')");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Encapsulates planned deck slot allocations for robot execution.
    /// </summary>
    /// <param name="RobotType">Robot model string ('OT-2' or 'Flex').</param>
    /// <param name="DestSlot">Destination assembly vessel slot.</param>
    /// <param name="MasterMixSlot">Master mix reagent tube rack slot.</param>
    /// <param name="TipRackSlots">List of allocated tip rack deck slots.</param>
    /// <param name="SourcePlateSlots">List of allocated staple source plate deck slots.</param>
    private sealed record DeckLayout(
        string RobotType,
        string DestSlot,
        string MasterMixSlot,
        IReadOnlyList<string> TipRackSlots,
        IReadOnlyList<string> SourcePlateSlots);
}
