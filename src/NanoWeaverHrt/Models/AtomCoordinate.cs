using System.Runtime.InteropServices;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Represents a single atom, nucleotide base, or coarse-grained particle in 3D computational space.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> In molecular simulations (such as DNA origami folding), every particle 
/// has a specific position in 3D Cartesian coordinates (X, Y, Z).
/// </para>
/// <para>
/// <strong>Why this design:</strong> This struct has an explicit, packed memory layout of exactly 12 bytes 
/// (three 32-bit floating-point numbers). Because it contains no managed references, it is "blittable", 
/// meaning millions of coordinates can be read directly from network buffers or disk into GPU memory 
/// with zero CPU memory allocations and zero Garbage Collection (GC) pauses.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct AtomCoordinate
{
    /// <summary>
    /// The size of an individual <see cref="AtomCoordinate"/> instance in raw bytes (4 bytes * 3 = 12 bytes).
    /// </summary>
    public const int SizeInBytes = 12;

    /// <summary>
    /// The Cartesian X-axis spatial coordinate, measured in Angstroms (1 Angstrom = 0.1 nanometers).
    /// </summary>
    public readonly float X;

    /// <summary>
    /// The Cartesian Y-axis spatial coordinate, measured in Angstroms.
    /// </summary>
    public readonly float Y;

    /// <summary>
    /// The Cartesian Z-axis spatial coordinate, measured in Angstroms.
    /// </summary>
    public readonly float Z;

    /// <summary>
    /// Initializes a new immutable 3D Cartesian coordinate.
    /// </summary>
    /// <param name="x">The X-axis position in Angstroms.</param>
    /// <param name="y">The Y-axis position in Angstroms.</param>
    /// <param name="z">The Z-axis position in Angstroms.</param>
    public AtomCoordinate(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// Defines the 3D periodic boundary dimensions (simulation box) of a molecular dynamics system.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The simulation box represents the physical boundaries of the virtual liquid solution 
/// in which DNA strands or molecular structures interact.
/// </para>
/// <para>
/// <strong>Why this design:</strong> To simulate infinite bulk fluid without edge artifacts, simulators use Periodic Boundary 
/// Conditions (PBC), where any particle exiting the high boundary wraps around to the low boundary.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct SimulationBox
{
    /// <summary>
    /// The lower bound along the X axis in Angstroms.
    /// </summary>
    public readonly float XLo;

    /// <summary>
    /// The upper bound along the X axis in Angstroms.
    /// </summary>
    public readonly float XHi;

    /// <summary>
    /// The lower bound along the Y axis in Angstroms.
    /// </summary>
    public readonly float YLo;

    /// <summary>
    /// The upper bound along the Y axis in Angstroms.
    /// </summary>
    public readonly float YHi;

    /// <summary>
    /// The lower bound along the Z axis in Angstroms.
    /// </summary>
    public readonly float ZLo;

    /// <summary>
    /// The upper bound along the Z axis in Angstroms.
    /// </summary>
    public readonly float ZHi;

    /// <summary>
    /// Initializes a new simulation bounding box.
    /// </summary>
    /// <param name="xLo">Lower X boundary.</param>
    /// <param name="xHi">Upper X boundary.</param>
    /// <param name="yLo">Lower Y boundary.</param>
    /// <param name="yHi">Upper Y boundary.</param>
    /// <param name="zLo">Lower Z boundary.</param>
    /// <param name="zHi">Upper Z boundary.</param>
    public SimulationBox(float xLo, float xHi, float yLo, float yHi, float zLo, float zHi)
    {
        XLo = xLo;
        XHi = xHi;
        YLo = yLo;
        YHi = yHi;
        ZLo = zLo;
        ZHi = zHi;
    }
}
