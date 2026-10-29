# Work Package P40: Wet-Lab Liquid Handler Protocol Generator Engine

## 1. Context & Objective
- **Problem Statement**:
  DNA origami synthesis requires stoichiometric assembly of single-stranded M13mp18 scaffold DNA with 100+ unique synthetic staple oligonucleotides. Manual 50 µL pipetting across 96-well plates is labor-intensive, error-prone, and presents a barrier for community laboratories. Automated liquid handling robots (Opentrons OT-2 and Opentrons Flex) can automate this assembly with high precision, but require Python Protocol API v2 scripts with specific deck layouts, pipetting parameters, and temperature controls.
- **Goal & Scope**:
  1. Implement `IOpentronsProtocolGenerator` and `OpentronsProtocolGenerator` in `src/NanoWeaverHrt/Fabrication/Opentrons/`.
  2. Support Opentrons OT-2 and Opentrons Flex with single-channel (`p20_single_gen2`, `flex_1channel_50`) and multi-channel pipettes.
  3. Support 96-well (`nest_96_wellplate_100ul_pcr_full_skirt`, `corning_96_wellplate_360ul_flat`, `biorad_96_wellplate_200ul_pcr`) and 384-well labware formats.
  4. Generate deterministic, syntactically valid Python Protocol API v2 code (`apiLevel = '2.15'`) mapping staple aliquots from source plates to destination reaction vessels.
  5. Include on-deck module commands for Temperature Module (4°C hold during assembly) and optional Thermocycler Module (executing the multi-stage 90°C → 4°C annealing schedule).
  6. Achieve 100.00% Line & Branch unit test coverage in `tests/NanoWeaverHrt.Tests/Fabrication/OpentronsProtocolGeneratorTests.cs`.
- **Target Role / Model**: `coder` (`flash`)
- **Transport Boundary**: In-Memory Domain Layer (`NanoWeaverHrt.Fabrication.Opentrons`)

---

## 2. Technical Contracts & Invariants

### Enums & Models
```csharp
namespace NanoWeaverHrt.Fabrication.Opentrons;

/// <summary>
/// Supported Opentrons liquid handler robot models.
/// </summary>
public enum OpentronsRobotModel
{
    /// <summary>Standard benchtop Opentrons OT-2 robot.</summary>
    OT2,
    /// <summary>Next-generation Opentrons Flex liquid handler.</summary>
    Flex
}

/// <summary>
/// Supported pipette models for DNA origami stoichiometric dispensing.
/// </summary>
public enum OpentronsPipetteModel
{
    /// <summary>OT-2 Single-channel 20 µL pipette (Gen2).</summary>
    P20_Single_Gen2,
    /// <summary>OT-2 8-channel 20 µL pipette (Gen2).</summary>
    P20_Multi_Gen2,
    /// <summary>OT-2 Single-channel 300 µL pipette (Gen2) for master mixes.</summary>
    P300_Single_Gen2,
    /// <summary>Flex 1-channel 50 µL pipette.</summary>
    Flex_1Channel_50,
    /// <summary>Flex 8-channel 50 µL pipette.</summary>
    Flex_8Channel_50
}

/// <summary>
/// Supported labware plate types for source staple libraries and destination assemblies.
/// </summary>
public enum OpentronsPlateType
{
    /// <summary>NEST 96-well PCR plate 100 µL full skirt.</summary>
    Nest96WellPcr,
    /// <summary>Corning 96-well flat bottom 360 µL plate.</summary>
    Corning96Flat,
    /// <summary>Bio-Rad 96-well PCR plate 200 µL.</summary>
    BioRad96WellPcr,
    /// <summary>NEST 384-well PCR plate 30 µL.</summary>
    Nest384WellPcr
}

/// <summary>
/// Configuration options for Opentrons protocol script generation.
/// </summary>
public sealed record OpentronsProtocolOptions
{
    /// <summary>Target liquid handler robot model.</summary>
    public OpentronsRobotModel RobotModel { get; init; } = OpentronsRobotModel.OT2;

    /// <summary>Primary pipette model for staple transfer.</summary>
    public OpentronsPipetteModel PipetteModel { get; init; } = OpentronsPipetteModel.P20_Single_Gen2;

    /// <summary>Labware format for staple source plates.</summary>
    public OpentronsPlateType SourcePlateType { get; init; } = OpentronsPlateType.Nest96WellPcr;

    /// <summary>Whether to integrate the on-deck Temperature Module set to 4°C during assembly.</summary>
    public bool UseTemperatureModule { get; init; } = true;

    /// <summary>Whether to integrate the on-deck Thermocycler Module for the thermal ramp.</summary>
    public bool UseThermocyclerModule { get; init; } = false;

    /// <summary>Total target reaction volume in microliters (default: 50.0 µL).</summary>
    public double TotalReactionVolumeMicroliters { get; init; } = 50.0;

    /// <summary>Scaffold target concentration in nanomolar (default: 10.0 nM).</summary>
    public double ScaffoldConcentrationNanomolar { get; init; } = 10.0;

    /// <summary>Staple excess molar ratio over scaffold (default: 10.0 for 10:1 excess).</summary>
    public double StapleMolarExcessRatio { get; init; } = 10.0;
}

/// <summary>
/// Result containing the generated Python protocol script and metadata.
/// </summary>
public sealed record OpentronsProtocolResult
{
    /// <summary>Complete, syntactically valid Python protocol script text.</summary>
    public required string PythonScript { get; init; }

    /// <summary>SHA-256 hash of the generated Python script.</summary>
    public required string ScriptSha256 { get; init; }

    /// <summary>Total number of staple oligo transfers scheduled.</summary>
    public required int TransferCount { get; init; }

    /// <summary>Number of 96-well or 384-well source plates required.</summary>
    public required int SourcePlatesRequired { get; init; }

    /// <summary>Number of tip racks required on deck.</summary>
    public required int TipRacksRequired { get; init; }

    /// <summary>Whether the script includes on-deck thermocycler ramp commands.</summary>
    public required bool HasThermocyclerRamp { get; init; }

    /// <summary>Estimated robot run duration in minutes.</summary>
    public required double EstimatedRunDurationMinutes { get; init; }
}

/// <summary>
/// Interface for generating automated Opentrons liquid handler Python protocols from DNA origami packages.
/// </summary>
public interface IOpentronsProtocolGenerator
{
    /// <summary>
    /// Generates an Opentrons Protocol API v2 Python script tailored to the given DNA origami fabrication package.
    /// </summary>
    /// <param name="package">Authoritative DNA origami fabrication package containing staple sequences and annealing schedules.</param>
    /// <param name="options">Hardware, pipette, and labware options.</param>
    /// <returns>Generated Python protocol script and deck planning metadata.</returns>
    OpentronsProtocolResult GenerateProtocol(FabricationPackage package, OpentronsProtocolOptions options);
}
```

### Invariants
1. **Zero Unhandled Nulls**: `FabricationPackage` must not be null; throws `ArgumentNullException`.
2. **Deterministic Plate Well Mapping**: Staples map monotonically to wells:
   - Well indices: `A1`, `A2`, ... `A12`, `B1` ... `H12` (96-well) or `A1` ... `P24` (384-well).
   - If staple count exceeds 96, wrap to Plate 2, Plate 3, etc.
3. **Valid Python Syntax**: The generated script must strictly conform to Opentrons Protocol API v2 (`from opentrons import protocol_api`, `metadata = {'apiLevel': '2.15', ...}`, `def run(protocol: protocol_api.ProtocolContext):`).
4. **100.00% Line & Branch Coverage**: All branches (OT2 vs. Flex, single vs. multi-channel, thermocycler vs. temp module, 96 vs. 384) must be covered with tests in `OpentronsProtocolGeneratorTests.cs`.
5. **100% Docblocks**: Every member must have `<summary>`, `<param>`, `<returns>`.

---

## 3. Step-by-Step Implementation Tasks
1. Create directory `src/NanoWeaverHrt/Fabrication/Opentrons/`.
2. Implement `OpentronsModels.cs` with enums and records.
3. Implement `IOpentronsProtocolGenerator.cs`.
4. Implement `OpentronsProtocolGenerator.cs` with well mapping, master mix aspiration, transfer loops, and annealing ramp translation.
5. Create unit test fixture `tests/NanoWeaverHrt.Tests/Fabrication/OpentronsProtocolGeneratorTests.cs`.
6. Register `IOpentronsProtocolGenerator` in `src/NanoWeaverHrt/Program.cs` via `builder.Services.AddSingleton<IOpentronsProtocolGenerator, OpentronsProtocolGenerator>();`.
7. Verify with `dotnet test tests/NanoWeaverHrt.Tests`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Builds with zero warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on `OpentronsProtocolGenerator.cs`.
- [ ] Generated Python script passes Python AST parse test (`python3 -c "import ast; ast.parse(script)"`).
- [ ] 100% XML docblock coverage on all new types and members.
