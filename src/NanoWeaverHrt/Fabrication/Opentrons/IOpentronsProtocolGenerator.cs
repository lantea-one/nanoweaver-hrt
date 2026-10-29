namespace NanoWeaverHrt.Fabrication.Opentrons;

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
