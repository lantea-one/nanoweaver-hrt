using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Unit tests verifying cryptographic packaging, provenance verification, and tamper detection in <see cref="DatasetTransferService"/>.
/// </summary>
public sealed class DatasetTransferTests : IDisposable
{
    /// <summary>
    /// Temporary working directory for simulation data during tests.
    /// </summary>
    private readonly string _testDataDir;

    /// <summary>
    /// Temporary directory for exported archives during tests.
    /// </summary>
    private readonly string _testExportDir;

    /// <summary>
    /// The dataset transfer service under test.
    /// </summary>
    private readonly DatasetTransferService _service;

    /// <summary>
    /// Initializes an isolated temporary file system environment for testing dataset transfer operations.
    /// </summary>
    public DatasetTransferTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), $"hrt-test-data-{Guid.NewGuid()}");
        _testExportDir = Path.Combine(Path.GetTempPath(), $"hrt-test-export-{Guid.NewGuid()}");

        Directory.CreateDirectory(_testDataDir);
        Directory.CreateDirectory(_testExportDir);

        Dictionary<string, string?> configValues = new()
        {
            ["TRAJECTORY_INCOMING_DIR"] = _testDataDir,
            ["TRAJECTORY_EXPORT_DIR"] = _testExportDir
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        _service = new DatasetTransferService(configuration, NullLogger<DatasetTransferService>.Instance);
    }

    /// <summary>
    /// Verifies that exporting a dataset creates both a .tar.gz bundle and a valid SHA-256 manifest.
    /// </summary>
    [Fact]
    public async Task ExportDatasetAsync_ShouldCreateArchiveAndValidSha256Manifest()
    {
        // WHAT: Creates a dummy trajectory file in the active data directory.
        // WHY:  Simulates a real simulation output ready for packaging and sharing.
        // HOW:  Writes sample text to a .xyz file.
        string sampleFile = Path.Combine(_testDataDir, "test_traj.xyz");
        await File.WriteAllTextAsync(sampleFile, "1\nTest Frame\nC 1.0 2.0 3.0\n");

        DatasetManifest manifest = await _service.ExportDatasetAsync("test-dataset");

        Assert.Equal("test-dataset", manifest.DatasetName);
        Assert.Equal("test-dataset.tar.gz", manifest.ArchiveFileName);
        Assert.True(manifest.SizeInBytes > 0);
        Assert.Matches("^[a-f0-9]{64}$", manifest.Sha256Checksum);

        // Verify files exist in the export directory
        string archivePath = Path.Combine(_testExportDir, manifest.ArchiveFileName);
        string manifestPath = Path.Combine(_testExportDir, "test-dataset.manifest.json");

        Assert.True(File.Exists(archivePath));
        Assert.True(File.Exists(manifestPath));

        // Verify SHA-256 checksum matches actual file on disk
        using FileStream stream = File.OpenRead(archivePath);
        byte[] diskHash = await SHA256.HashDataAsync(stream);
        string diskHex = Convert.ToHexString(diskHash).ToLowerInvariant();

        Assert.Equal(manifest.Sha256Checksum, diskHex);
    }

    /// <summary>
    /// Verifies that importing an archive with a modified or incorrect SHA-256 manifest is rejected.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_ShouldRejectTamperedArchive()
    {
        // WHAT: Prepares dummy archive bytes and pairs it with a fake/tampered SHA-256 checksum.
        // WHY:  Guarantees cryptographic integrity; corrupted or forged datasets must never be imported.
        // HOW:  Calls ImportDatasetAsync with a mismatching hash and asserts false is returned.
        byte[] dummyBytes = Encoding.UTF8.GetBytes("fake archive contents for testing");
        using MemoryStream archiveStream = new(dummyBytes);

        DatasetManifest forgedManifest = new()
        {
            DatasetName = "tampered-run",
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            ArchiveFileName = "tampered.tar.gz",
            Sha256Checksum = "0000000000000000000000000000000000000000000000000000000000000000",
            SizeInBytes = dummyBytes.Length
        };

        bool result = await _service.ImportDatasetAsync(archiveStream, forgedManifest);

        Assert.False(result);
    }

    /// <summary>
    /// Cleans up temporary test directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDataDir)) Directory.Delete(_testDataDir, recursive: true);
            if (Directory.Exists(_testExportDir)) Directory.Delete(_testExportDir, recursive: true);
        }
        catch
        {
            // Suppress cleanup exceptions in tests
        }
    }
}
