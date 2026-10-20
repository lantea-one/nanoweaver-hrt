using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Provides synthetic trajectory files and test fixture setup for integration and unit test execution,
/// ensuring that tests run deterministically without requiring data directories to be checked into version control.
/// </summary>
public static class TestFixturesHelper
{
    /// <summary>
    /// Initializes test fixtures automatically upon test assembly load.
    /// </summary>
    [ModuleInitializer]
    internal static void InitializeModuleFixtures()
    {
        // WHAT: Guarantees test fixtures exist prior to the execution of any unit or integration tests.
        // WHY:  Prevents race conditions in parallel xUnit test execution on fresh Git checkouts.
        // HOW:  Resolves solution directory and invokes EnsureTestFixtures during module initialization.
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
        {
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }
            current = parent;
        }

        if (!string.IsNullOrEmpty(current) && File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
        {
            EnsureTestFixtures(Path.Combine(current, "simulations", "shared-data"));
        }
    }

    /// <summary>
    /// Minimal multi-frame XYZ coordinate trajectory content.
    /// </summary>
    private const string SampleXyzContent =
@"16
Timestep: 0 Lattice=""100.0 0.0 0.0 0.0 100.0 0.0 0.0 0.0 100.0""
P   0.000000   10.000000    0.000000
O   1.500000   10.200000    0.500000
C   2.200000   11.000000    1.200000
N   3.100000   10.800000    2.000000
P   0.000000   -10.000000   0.000000
O   1.500000   -10.200000   0.500000
C   2.200000   -11.000000   1.200000
N   3.100000   -10.800000   2.000000
P   4.000000   10.000000    0.000000
O   5.500000   10.200000    0.500000
C   6.200000   11.000000    1.200000
N   7.100000   10.800000    2.000000
P   4.000000   -10.000000   0.000000
O   5.500000   -10.200000   0.500000
C   6.200000   -11.000000   1.200000
N   7.100000   -10.800000   2.000000
16
Timestep: 100 Lattice=""100.0 0.0 0.0 0.0 100.0 0.0 0.0 0.0 100.0""
P   0.120000   10.050000    0.080000
O   1.580000   10.240000    0.550000
C   2.270000   11.080000    1.240000
N   3.150000   10.860000    2.060000
P  -0.080000   -9.940000    0.040000
O   1.460000   -10.150000   0.480000
C   2.180000   -10.930000   1.170000
N   3.070000   -10.740000   1.950000
P   4.120000   10.050000    0.080000
O   5.580000   10.240000    0.550000
C   6.270000   11.080000    1.240000
N   7.150000   10.860000    2.060000
P   3.920000   -9.940000    0.040000
O   5.460000   -10.150000   0.480000
C   6.180000   -10.930000   1.170000
N   7.070000   -10.740000   1.950000
";

    /// <summary>
    /// Minimal multi-frame LAMMPS text dump content with close atomic binding contacts.
    /// </summary>
    private const string SampleDumpContent =
@"ITEM: TIMESTEP
0
ITEM: NUMBER OF ATOMS
4
ITEM: BOX BOUNDS pp pp pp
0.0 50.0
0.0 50.0
0.0 50.0
ITEM: ATOMS id type x y z
1 1 1.0 1.0 1.0
2 1 1.2 1.1 1.0
3 1 1.1 1.3 1.2
4 2 1.2 1.2 1.1
ITEM: TIMESTEP
100
ITEM: NUMBER OF ATOMS
4
ITEM: BOX BOUNDS pp pp pp
0.0 50.0
0.0 50.0
0.0 50.0
ITEM: ATOMS id type x y z
1 1 1.05 1.02 1.01
2 1 1.22 1.11 1.03
3 1 1.12 1.31 1.22
4 2 1.21 1.22 1.11
";

    /// <summary>
    /// Ensures that test trajectory fixtures exist in the designated directory without heap or git overhead.
    /// </summary>
    /// <param name="dataDir">The target directory path.</param>
    public static void EnsureTestFixtures(string dataDir)
    {
        // WHAT: Checks for required sample files and generates them if absent.
        // WHY:  Permits test suite execution even when data directories are strictly gitignored.
        // HOW:  Creates dataDir and writes standard sample files on demand.
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        string xyzPath = Path.Combine(dataDir, "dna_nanotube_sample.xyz");
        if (!File.Exists(xyzPath))
        {
            File.WriteAllText(xyzPath, SampleXyzContent);
        }

        string dumpPath = Path.Combine(dataDir, "dna_sample.dump");
        if (!File.Exists(dumpPath))
        {
            File.WriteAllText(dumpPath, SampleDumpContent);
        }

        string dcdPath = Path.Combine(dataDir, "dna_sample.dcd");
        if (!File.Exists(dcdPath))
        {
            File.WriteAllBytes(dcdPath, GenerateSampleDcdBytes());
        }
    }

    /// <summary>
    /// Generates a valid binary CHARMM/LAMMPS DCD stream containing 4 atoms with valid coordinates.
    /// </summary>
    /// <returns>A raw byte array representing a binary DCD trajectory.</returns>
    private static byte[] GenerateSampleDcdBytes()
    {
        // WHAT: Synthesizes a valid binary DCD trajectory file in memory.
        // WHY:  Provides a valid binary stream for endpoint integration tests.
        // HOW:  Encodes standard DCD header ("CORD", atom count 4) and coordinate blocks for X, Y, and Z.
        using MemoryStream ms = new();
        using (BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true))
        {
            // 1. Header Record (84 bytes payload)
            writer.Write(84);
            writer.Write("CORD"u8.ToArray());
            byte[] dummyHeaderInts = new byte[76];
            writer.Write(dummyHeaderInts);
            writer.Write(84);

            // 2. Title Record
            writer.Write(4);
            writer.Write("TEST"u8.ToArray());
            writer.Write(4);

            // 3. NATOM Record
            writer.Write(4);
            writer.Write(4); // 4 atoms
            writer.Write(4);

            // 4. Coordinates Frame: X block
            int coordBlockSize = 4 * 4;
            float[] xCoords = [1.0f, 1.2f, 1.1f, 1.2f];
            float[] yCoords = [1.0f, 1.1f, 1.3f, 1.2f];
            float[] zCoords = [1.0f, 1.0f, 1.2f, 1.1f];

            writer.Write(coordBlockSize);
            for (int i = 0; i < 4; i++) writer.Write(xCoords[i]);
            writer.Write(coordBlockSize);

            // Y block
            writer.Write(coordBlockSize);
            for (int i = 0; i < 4; i++) writer.Write(yCoords[i]);
            writer.Write(coordBlockSize);

            // Z block
            writer.Write(coordBlockSize);
            for (int i = 0; i < 4; i++) writer.Write(zCoords[i]);
            writer.Write(coordBlockSize);
        }

        return ms.ToArray();
    }
}
