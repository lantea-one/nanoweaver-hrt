using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Opentrons;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit test suite for <see cref="OpentronsProtocolGenerator"/> validating Opentrons Protocol API v2
/// Python script generation, Python AST compilation, well routing, hardware options, and downward ratchet invariants.
/// </summary>
public sealed class OpentronsProtocolGeneratorTests
{
    /// <summary>
    /// Helper method to create a test <see cref="FabricationPackage"/> with specified staple count and annealing steps.
    /// </summary>
    /// <param name="stapleCount">Number of synthetic staple oligos to generate.</param>
    /// <param name="schedule">Optional custom annealing schedule.</param>
    /// <returns>A populated fabrication package instance.</returns>
    private static FabricationPackage CreateTestPackage(int stapleCount, IReadOnlyList<AnnealingRampStep>? schedule = null)
    {
        // WHAT: Creates a deterministic test fabrication package with N synthetic staple strands.
        // WHY:  Allows parametric testing of well mapping, plate wrapping, and transfer counts.
        // HOW:  Generates synthetic StapleOligoRecord entries with sequential IDs ST-001..ST-NNN.
        List<StapleOligoRecord> staples = [];
        for (int i = 1; i <= stapleCount; i++)
        {
            staples.Add(new StapleOligoRecord
            {
                StapleId = $"ST-{i:D3}",
                Sequence = "ACGTACGTACGTACGTACGT",
                Length = 20,
                MeltingTemperatureCelsius = 58.0,
                GcPercentage = 50.0,
                StockConcentrationMicromolar = 100.0,
                FunctionalRole = $"Core Hexagonal Lattice Staple {i}"
            });
        }

        List<AnnealingRampStep> steps = schedule != null ? [.. schedule] :
        [
            new AnnealingRampStep
            {
                StepIndex = 1,
                TargetTemperatureCelsius = 90.0,
                HoldDurationMinutes = 5,
                RampRateCelsiusPerMinute = 2.0,
                BiophysicalPurpose = "Denature viral scaffold secondary structure"
            },
            new AnnealingRampStep
            {
                StepIndex = 2,
                TargetTemperatureCelsius = 65.0,
                HoldDurationMinutes = 15,
                RampRateCelsiusPerMinute = 1.0,
                BiophysicalPurpose = "Nucleate core staple crossovers"
            },
            new AnnealingRampStep
            {
                StepIndex = 3,
                TargetTemperatureCelsius = 4.0,
                HoldDurationMinutes = 10,
                RampRateCelsiusPerMinute = 2.0,
                BiophysicalPurpose = "Cryogenic lattice stabilization"
            }
        ];

        return new FabricationPackage
        {
            PackageId = "fab_test_nanotube_001",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ScaffoldBackbone = "M13mp18 ssDNA (7,249 nt)",
            DeliveryGeometry = "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            TargetTissue = "Anterior Pituitary Gonadotrope (GnRH-R)",
            StapleOligos = staples,
            AnnealingSchedule = steps,
            StoichiometricRatio = "10:1 staple-to-scaffold stoichiometric molar excess",
            FoldingBufferRecipe = "1x TAE + 12.5 mM MgCl2",
            ExpectedHydrodynamicRadiusNm = 18.5,
            Sha256Manifest = "test_hash",
            DiyFabricationGuide = DiyFabricationGuide.CreateExample(),
            CreatedAtUtc = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Validates that a generated Python script compiles cleanly into an Abstract Syntax Tree via Python 3's <c>ast</c> module.
    /// </summary>
    /// <param name="pythonScript">The Python script text to validate.</param>
    private static void AssertPythonAstCompiles(string pythonScript)
    {
        // WHAT: Executes Python 3 AST parser on the generated protocol script.
        // WHY:  Guarantees that the output is syntactically valid Python without indentation or token errors.
        // HOW:  Spawns 'python3 -c "import sys, ast; ast.parse(sys.stdin.read())"' and streams the script via stdin.
        ProcessStartInfo psi = new()
        {
            FileName = "python3",
            Arguments = "-c \"import sys, ast; ast.parse(sys.stdin.read())\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? proc = Process.Start(psi);
        Assert.NotNull(proc);

        proc.StandardInput.Write(pythonScript);
        proc.StandardInput.Close();

        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"Python AST parse failed with error: {stderr}\nScript:\n{pythonScript}");
    }

    /// <summary>
    /// Verifies that <see cref="OpentronsProtocolGenerator.GenerateProtocol"/> throws <see cref="ArgumentNullException"/>
    /// when the input package is null.
    /// </summary>
    [Fact]
    public void GenerateProtocol_NullPackage_ThrowsArgumentNullException()
    {
        // WHAT: Tests null package validation.
        // WHY:  Adheres to Invariant 1 (zero unhandled null inputs).
        // HOW:  Passes null package to GenerateProtocol.
        OpentronsProtocolGenerator generator = new();
        Assert.Throws<ArgumentNullException>("package", () => generator.GenerateProtocol(null!, new OpentronsProtocolOptions()));
    }

    /// <summary>
    /// Verifies that <see cref="OpentronsProtocolGenerator.GenerateProtocol"/> throws <see cref="ArgumentNullException"/>
    /// when the input options is null.
    /// </summary>
    [Fact]
    public void GenerateProtocol_NullOptions_ThrowsArgumentNullException()
    {
        // WHAT: Tests null options validation.
        // WHY:  Adheres to Invariant 1 (zero unhandled null inputs).
        // HOW:  Passes null options to GenerateProtocol.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(10);
        Assert.Throws<ArgumentNullException>("options", () => generator.GenerateProtocol(package, null!));
    }

    /// <summary>
    /// Verifies default OT-2 single-channel protocol generation with Temperature Module hold at 4°C.
    /// </summary>
    [Fact]
    public void GenerateProtocol_DefaultOptions_GeneratesValidOt2ScriptWithTempModule()
    {
        // WHAT: Tests default configuration (OT-2, P20 Single Gen2, 96-well PCR, Temp Module at 4°C).
        // WHY:  Validates primary bench setup for community and institutional laboratories.
        // HOW:  Calls GenerateProtocol and asserts Python AST, deck metadata, and SHA-256 integrity.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(24);
        OpentronsProtocolOptions options = new();

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.NotNull(result);
        Assert.Equal(24, result.TransferCount);
        Assert.Equal(1, result.SourcePlatesRequired);
        Assert.Equal(1, result.TipRacksRequired);
        Assert.False(result.HasThermocyclerRamp);
        Assert.True(result.EstimatedRunDurationMinutes > 0.0);

        // Verify SHA-256 calculation
        byte[] expectedHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(result.PythonScript));
        string expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();
        Assert.Equal(expectedHash, result.ScriptSha256);

        // Assert Python AST compiles cleanly
        AssertPythonAstCompiles(result.PythonScript);

        // Assert content markers
        Assert.Contains("'robotType': 'OT-2'", result.PythonScript);
        Assert.Contains("p20_single_gen2", result.PythonScript);
        Assert.Contains("temp_mod = protocol.load_module('temperature module gen2'", result.PythonScript);
        Assert.Contains("temp_mod.set_temperature(celsius=4)", result.PythonScript);
        Assert.Contains("dest_plate = temp_mod.load_labware('nest_96_wellplate_100ul_pcr_full_skirt'", result.PythonScript);
        Assert.Contains("opentrons_24_tuberack_generic_2ml_screwcap", result.PythonScript);
    }

    /// <summary>
    /// Verifies OT-2 protocol generation with on-deck Thermocycler Module executing multi-stage annealing ramp.
    /// </summary>
    [Fact]
    public void GenerateProtocol_Ot2ThermocyclerModule_GeneratesOnDeckAnnealingRamp()
    {
        // WHAT: Tests OT-2 with Thermocycler Module.
        // WHY:  Verifies automated on-deck 90°C -> 4°C denaturation and folding schedules.
        // HOW:  Enables UseThermocyclerModule and asserts module calls, lid control, and step temperatures.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(20);
        OpentronsProtocolOptions options = new()
        {
            RobotModel = OpentronsRobotModel.OT2,
            UseThermocyclerModule = true
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.True(result.HasThermocyclerRamp);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.Contains("tc_mod = protocol.load_module('thermocycler module gen2')", result.PythonScript);
        Assert.Contains("tc_mod.open_lid()", result.PythonScript);
        Assert.Contains("tc_mod.close_lid()", result.PythonScript);
        Assert.Contains("tc_mod.set_lid_temperature(temperature=105)", result.PythonScript);
        Assert.Contains("tc_mod.set_block_temperature(temperature=90.0, hold_time_minutes=5)", result.PythonScript);
        Assert.Contains("tc_mod.set_block_temperature(temperature=65.0, hold_time_minutes=15)", result.PythonScript);
        Assert.Contains("tc_mod.set_block_temperature(temperature=4.0, hold_time_minutes=10)", result.PythonScript);
        Assert.Contains("tc_mod.deactivate_lid()", result.PythonScript);
    }

    /// <summary>
    /// Verifies Opentrons Flex protocol generation with on-deck Thermocycler Module and 1-channel 50 µL pipette.
    /// </summary>
    [Fact]
    public void GenerateProtocol_FlexThermocyclerModule_GeneratesFlexSlotsAndRequirements()
    {
        // WHAT: Tests Opentrons Flex with 1-channel 50 µL pipette and Thermocycler Module.
        // WHY:  Verifies Flex-specific requirements, slot coordinates (B1, D1, etc.), and tip racks.
        // HOW:  Sets RobotModel = Flex, PipetteModel = Flex_1Channel_50, UseThermocyclerModule = true.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(15);
        OpentronsProtocolOptions options = new()
        {
            RobotModel = OpentronsRobotModel.Flex,
            PipetteModel = OpentronsPipetteModel.Flex_1Channel_50,
            UseThermocyclerModule = true
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.True(result.HasThermocyclerRamp);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.Contains("'robotType': 'Flex'", result.PythonScript);
        Assert.Contains("flex_1channel_50", result.PythonScript);
        Assert.Contains("opentrons_flex_96_tiprack_50ul", result.PythonScript);
        Assert.Contains("tc_mod = protocol.load_module('thermocycler module gen2', 'B1')", result.PythonScript);
    }

    /// <summary>
    /// Verifies protocol generation without on-deck modules, mounting destination plate directly in a standard deck slot.
    /// </summary>
    [Fact]
    public void GenerateProtocol_NoModules_LoadsDestinationPlateDirectlyOnDeck()
    {
        // WHAT: Tests generation when both temperature and thermocycler modules are disabled.
        // WHY:  Supports basic bench setups where heating/cooling is performed on external devices.
        // HOW:  Sets UseTemperatureModule = false and UseThermocyclerModule = false.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(10);
        OpentronsProtocolOptions options = new()
        {
            RobotModel = OpentronsRobotModel.OT2,
            UseTemperatureModule = false,
            UseThermocyclerModule = false
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.False(result.HasThermocyclerRamp);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.DoesNotContain("tc_mod", result.PythonScript);
        Assert.DoesNotContain("temp_mod", result.PythonScript);
        Assert.Contains("dest_plate = protocol.load_labware('nest_96_wellplate_100ul_pcr_full_skirt', '1'", result.PythonScript);
        Assert.Contains("Retrieve reaction vessel for incubation", result.PythonScript);
    }

    /// <summary>
    /// Verifies multi-plate wrapping when staple count exceeds 96 wells for 96-well source plates.
    /// </summary>
    [Fact]
    public void GenerateProtocol_MoreThan96Staples_WrapsToPlate2AndPlansTwoTipRacks()
    {
        // WHAT: Tests plate wrapping and tip rack calculations for 100 staples in 96-well format.
        // WHY:  Adheres to Invariant 2 (deterministic plate well mapping wrapping past 96).
        // HOW:  Creates package with 100 staples and asserts SourcePlatesRequired == 2, TipRacksRequired == 2.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(100);
        OpentronsProtocolOptions options = new()
        {
            SourcePlateType = OpentronsPlateType.Nest96WellPcr
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.Equal(100, result.TransferCount);
        Assert.Equal(2, result.SourcePlatesRequired);
        Assert.Equal(2, result.TipRacksRequired);
        AssertPythonAstCompiles(result.PythonScript);

        // Verify Plate 1 boundary (well H12) and Plate 2 start (well A1)
        Assert.Contains("(0, 'H12', 1.0, 'ST-096')", result.PythonScript);
        Assert.Contains("(1, 'A1', 1.0, 'ST-097')", result.PythonScript);
        Assert.Contains("(1, 'A4', 1.0, 'ST-100')", result.PythonScript);
    }

    /// <summary>
    /// Verifies multi-plate wrapping when staple count exceeds 384 wells for 384-well source plates.
    /// </summary>
    [Fact]
    public void GenerateProtocol_MoreThan384Staples_WrapsToSecond384WellPlate()
    {
        // WHAT: Tests plate wrapping for 384-well microplate geometry with 390 staples.
        // WHY:  Validates Invariant 2 ANSI/SLAS 384-well coordinate generation (A1..P24).
        // HOW:  Creates package with 390 staples and asserts Plate 1 ends at P24 and Plate 2 begins at A1.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(390);
        OpentronsProtocolOptions options = new()
        {
            SourcePlateType = OpentronsPlateType.Nest384WellPcr
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.Equal(390, result.TransferCount);
        Assert.Equal(2, result.SourcePlatesRequired);
        Assert.Equal(5, result.TipRacksRequired); // 390 / 96 = 4.06 -> 5 tip racks
        AssertPythonAstCompiles(result.PythonScript);

        // Verify Plate 1 boundary (well P24) and Plate 2 start (well A1)
        Assert.Contains("(0, 'P24', 1.0, 'ST-384')", result.PythonScript);
        Assert.Contains("(1, 'A1', 1.0, 'ST-385')", result.PythonScript);
        Assert.Contains("(1, 'A6', 1.0, 'ST-390')", result.PythonScript);
    }

    /// <summary>
    /// Verifies 8-channel multi-channel pipette transfer loop for OT-2 (P20_Multi_Gen2) across 96-well plate.
    /// </summary>
    [Fact]
    public void GenerateProtocol_Ot2MultiChannelPipette_GeneratesColumnTransfers()
    {
        // WHAT: Tests 8-channel multi-channel transfers with P20_Multi_Gen2.
        // WHY:  Validates multi-channel column dispensing reducing robot run duration.
        // HOW:  Sets PipetteModel = P20_Multi_Gen2 with 24 staples (3 columns: A1, A2, A3).
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(24);
        OpentronsProtocolOptions options = new()
        {
            PipetteModel = OpentronsPipetteModel.P20_Multi_Gen2,
            SourcePlateType = OpentronsPlateType.Nest96WellPcr
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.Equal(24, result.TransferCount);
        Assert.Equal(1, result.SourcePlatesRequired);
        Assert.Equal(1, result.TipRacksRequired);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.Contains("p20_multi_gen2", result.PythonScript);
        Assert.Contains("column_transfers = [", result.PythonScript);
        Assert.Contains("(0, 'A1', 1.0)", result.PythonScript);
        Assert.Contains("(0, 'A2', 1.0)", result.PythonScript);
        Assert.Contains("(0, 'A3', 1.0)", result.PythonScript);
        Assert.Contains("for plate_idx, top_well, vol_ul in column_transfers:", result.PythonScript);
    }

    /// <summary>
    /// Verifies 8-channel multi-channel pipette transfer loop for Flex (Flex_8Channel_50) across 384-well plate,
    /// exercising alternating row top-well coordinates (A1..A24 and B1..B24).
    /// </summary>
    [Fact]
    public void GenerateProtocol_Flex8ChannelOn384WellPlate_ExercisesAlternatingRowOffsets()
    {
        // WHAT: Tests 8-channel pipette dispensing from 384-well plates spanning > 24 columns.
        // WHY:  Exercises both offset < 24 and offset >= 24 branches of GetColumnCoordinate.
        // HOW:  Creates 200 staples (25 columns) on 384-well plate with Flex_8Channel_50.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(200);
        OpentronsProtocolOptions options = new()
        {
            RobotModel = OpentronsRobotModel.Flex,
            PipetteModel = OpentronsPipetteModel.Flex_8Channel_50,
            SourcePlateType = OpentronsPlateType.Nest384WellPcr
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.Equal(200, result.TransferCount);
        Assert.Equal(1, result.SourcePlatesRequired);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.Contains("flex_8channel_50", result.PythonScript);
        Assert.Contains("(0, 'A24', 1.0)", result.PythonScript); // Column 23 (offset 23 < 24)
        Assert.Contains("(0, 'B1', 1.0)", result.PythonScript);  // Column 24 (offset 24 >= 24)
    }

    /// <summary>
    /// Verifies all supported pipette models across OT-2 and Flex robots.
    /// </summary>
    /// <param name="pipetteModel">Pipette model enum value.</param>
    /// <param name="expectedApiName">Expected Opentrons API instrument identifier.</param>
    /// <param name="expectedTipRack">Expected tip rack labware identifier.</param>
    /// <param name="expectedMount">Expected mount string.</param>
    [Theory]
    [InlineData(OpentronsPipetteModel.P20_Single_Gen2, "p20_single_gen2", "opentrons_96_tiprack_20ul", "left")]
    [InlineData(OpentronsPipetteModel.P20_Multi_Gen2, "p20_multi_gen2", "opentrons_96_tiprack_20ul", "left")]
    [InlineData(OpentronsPipetteModel.P300_Single_Gen2, "p300_single_gen2", "opentrons_96_tiprack_300ul", "right")]
    [InlineData(OpentronsPipetteModel.Flex_1Channel_50, "flex_1channel_50", "opentrons_flex_96_tiprack_50ul", "left")]
    [InlineData(OpentronsPipetteModel.Flex_8Channel_50, "flex_8channel_50", "opentrons_flex_96_tiprack_50ul", "left")]
    public void GenerateProtocol_AllPipetteModels_MapsCorrectInstrumentAndTipRack(
        OpentronsPipetteModel pipetteModel,
        string expectedApiName,
        string expectedTipRack,
        string expectedMount)
    {
        // WHAT: Tests pipette model to labware and mount mappings.
        // WHY:  Guarantees 100% branch coverage across GetPipetteApiName and GetTipRackLabware.
        // HOW:  Iterates all 5 OpentronsPipetteModel enum entries.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(8);
        OpentronsProtocolOptions options = new()
        {
            PipetteModel = pipetteModel
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        AssertPythonAstCompiles(result.PythonScript);
        Assert.Contains($"protocol.load_instrument('{expectedApiName}', '{expectedMount}', tip_racks=tip_racks)", result.PythonScript);
        Assert.Contains(expectedTipRack, result.PythonScript);
    }

    /// <summary>
    /// Verifies all supported microplate labware definitions for source libraries.
    /// </summary>
    /// <param name="plateType">Microplate type enum value.</param>
    /// <param name="expectedLabware">Expected labware definition name in python script.</param>
    [Theory]
    [InlineData(OpentronsPlateType.Nest96WellPcr, "nest_96_wellplate_100ul_pcr_full_skirt")]
    [InlineData(OpentronsPlateType.Corning96Flat, "corning_96_wellplate_360ul_flat")]
    [InlineData(OpentronsPlateType.BioRad96WellPcr, "biorad_96_wellplate_200ul_pcr")]
    [InlineData(OpentronsPlateType.Nest384WellPcr, "nest_384_wellplate_30ul_pcr")]
    public void GenerateProtocol_AllPlateTypes_MapsCorrectLabwareDefinition(
        OpentronsPlateType plateType,
        string expectedLabware)
    {
        // WHAT: Tests plate type to labware load name mapping.
        // WHY:  Guarantees 100% branch coverage across GetPlateLabware.
        // HOW:  Iterates all 4 OpentronsPlateType enum entries.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(12);
        OpentronsProtocolOptions options = new()
        {
            SourcePlateType = plateType
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        AssertPythonAstCompiles(result.PythonScript);
        Assert.Contains(expectedLabware, result.PythonScript);
    }

    /// <summary>
    /// Verifies protocol generation when zero staples are present in the package for both single and multi-channel pipettes.
    /// </summary>
    /// <param name="pipetteModel">Pipette model enum.</param>
    [Theory]
    [InlineData(OpentronsPipetteModel.P20_Single_Gen2)]
    [InlineData(OpentronsPipetteModel.P20_Multi_Gen2)]
    public void GenerateProtocol_ZeroStaples_GeneratesEmptyTransfersAndValidScript(OpentronsPipetteModel pipetteModel)
    {
        // WHAT: Tests boundary edge case of 0 staple oligos for single and multi-channel pipettes.
        // WHY:  Guarantees that 0 transfers and 0 source plates do not cause index-out-of-range or syntax errors.
        // HOW:  Passes package with empty StapleOligos and asserts empty transfer lists in script.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(0);
        OpentronsProtocolOptions options = new()
        {
            PipetteModel = pipetteModel
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.Equal(0, result.TransferCount);
        Assert.Equal(0, result.SourcePlatesRequired);
        Assert.Equal(1, result.TipRacksRequired);
        AssertPythonAstCompiles(result.PythonScript);

        Assert.Contains("source_plates = []", result.PythonScript);
        if (pipetteModel == OpentronsPipetteModel.P20_Multi_Gen2)
        {
            Assert.Contains("column_transfers = []", result.PythonScript);
        }
        else
        {
            Assert.Contains("staple_transfers = []", result.PythonScript);
        }
    }

    /// <summary>
    /// Verifies thermocycler ramp execution duration estimation with custom ramp rates,
    /// including non-positive ramp rates falling back to 1.0°C/min.
    /// </summary>
    [Fact]
    public void GenerateProtocol_ThermocyclerRampRates_CalculatesDurationAccurately()
    {
        // WHAT: Tests duration estimation for thermal ramps with standard and zero/negative ramp rates.
        // WHY:  Guarantees 100% branch coverage in EstimateRunDuration for rate <= 0.0 fallback.
        // HOW:  Supplies an AnnealingRampStep with RampRateCelsiusPerMinute = 0.0.
        OpentronsProtocolGenerator generator = new();
        List<AnnealingRampStep> schedule =
        [
            new AnnealingRampStep
            {
                StepIndex = 1,
                TargetTemperatureCelsius = 90.0,
                HoldDurationMinutes = 5,
                RampRateCelsiusPerMinute = 0.0, // Non-positive rate triggers fallback to 1.0
                BiophysicalPurpose = "Denature"
            },
            new AnnealingRampStep
            {
                StepIndex = 2,
                TargetTemperatureCelsius = 60.0,
                HoldDurationMinutes = 10,
                RampRateCelsiusPerMinute = 2.0,
                BiophysicalPurpose = "Anneal"
            }
        ];
        FabricationPackage package = CreateTestPackage(10, schedule);
        OpentronsProtocolOptions options = new()
        {
            UseThermocyclerModule = true
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.True(result.HasThermocyclerRamp);
        // Base pipetting = 0.5 + 10 * 0.25 = 3.0 min.
        // Thermal hold = 5 + 10 = 15 min.
        // Transition = |90 - 60| / 1.0 (fallback) = 30 min.
        // Total = 48.0 min.
        Assert.Equal(48.0, result.EstimatedRunDurationMinutes);
        AssertPythonAstCompiles(result.PythonScript);
    }

    /// <summary>
    /// Verifies thermocycler option when the annealing schedule list is empty.
    /// </summary>
    [Fact]
    public void GenerateProtocol_ThermocyclerWithEmptySchedule_GeneratesValidScript()
    {
        // WHAT: Tests thermocycler module enabled with an empty AnnealingSchedule list.
        // WHY:  Guarantees branch coverage when schedule is empty.
        // HOW:  Passes empty schedule with UseThermocyclerModule = true.
        OpentronsProtocolGenerator generator = new();
        FabricationPackage package = CreateTestPackage(5, []);
        OpentronsProtocolOptions options = new()
        {
            UseThermocyclerModule = true
        };

        OpentronsProtocolResult result = generator.GenerateProtocol(package, options);

        Assert.True(result.HasThermocyclerRamp);
        AssertPythonAstCompiles(result.PythonScript);
        Assert.Contains("tc_mod.close_lid()", result.PythonScript);
        Assert.Contains("tc_mod.open_lid()", result.PythonScript);
    }
}
