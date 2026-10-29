using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;

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
/// Request payload for generating an Opentrons liquid handler protocol script.
/// </summary>
public sealed record GenerateOpentronsScriptRequest : IExamplesProvider<GenerateOpentronsScriptRequest>
{
    /// <summary>Raw DNA sequence or built-in sample identifier.</summary>
    public required string SequenceOrSampleId { get; init; }

    /// <summary>Target endocrine regimen.</summary>
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>Nanocarrier geometric lattice (e.g., Nanotube, Cuboid, Cage).</summary>
    public string Geometry { get; init; } = "Nanotube";

    /// <summary>Target aptamer dissociation constant in nanomolar.</summary>
    public double TargetKd { get; init; } = 4.8;

    /// <summary>Opentrons protocol generation options.</summary>
    public OpentronsProtocolOptions Options { get; init; } = new();

    /// <summary>
    /// Generates a representative, valid example instance of <see cref="GenerateOpentronsScriptRequest"/> for OpenAPI 3.0 documentation.
    /// </summary>
    /// <returns>A populated example instance of <see cref="GenerateOpentronsScriptRequest"/>.</returns>
    public GenerateOpentronsScriptRequest GetExamples() => new()
    {
        SequenceOrSampleId = "CAGE-6HB-E2-001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        Geometry = "Nanotube",
        TargetKd = 4.8,
        Options = new OpentronsProtocolOptions()
    };
}
