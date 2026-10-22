using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NanoWeaverHrt.Tests;

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
    /// Verifies that <see cref="DatasetTransferService.ListExportsAsync"/> returns an empty list when the directory does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ListExportsAsync_WhenDirectoryDoesNotExist_ReturnsEmpty()
    {
        // WHAT: Initializes DatasetTransferService pointing to a non-existent export directory.
        // WHY:  Ensures graceful handling without throwing DirectoryNotFoundException.
        // HOW:  Points configuration to a randomized missing path and asserts an empty list.
        string missingDir = Path.Combine(Path.GetTempPath(), $"missing-dir-{Guid.NewGuid():N}");
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _testDataDir,
                ["TRAJECTORY_EXPORT_DIR"] = missingDir
            })
            .Build();

        DatasetTransferService missingService = new(config, NullLogger<DatasetTransferService>.Instance);
        if (Directory.Exists(missingDir))
        {
            Directory.Delete(missingDir, recursive: true);
        }
        IReadOnlyList<ExportFileItem> result = await missingService.ListExportsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// Verifies that <see cref="DatasetTransferService.ListExportsAsync"/> discovers and correctly categorizes all supported export artifacts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ListExportsAsync_DiscoversAndCategorizesAllExportTypes()
    {
        // WHAT: Creates a spectrum of exported files (encrypted bundles, archives, signatures, manifests, Parquet) in the export directory.
        // WHY:  Guarantees that categorization rules match file extensions correctly and ignore hidden/metadata files.
        // HOW:  Creates sample files, invokes ListExportsAsync, and asserts category mappings and metadata fields.
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, ".gitkeep"), "");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "README.md"), "# Readme");

        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim1.enc.tar.gz"), "encrypted");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim2.tar.gz"), "archive-tgz");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim3.zip"), "archive-zip");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim4.tar"), "archive-tar");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim5.manifest.pqc.json"), "pqc-manifest");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim6.manifest.json"), "provenance-manifest");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim7.json"), "general-json");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim8.parquet"), "parquet-data");
        await File.WriteAllTextAsync(Path.Combine(_testExportDir, "sim9.dat"), "raw-data");

        IReadOnlyList<ExportFileItem> list = await _service.ListExportsAsync();

        Assert.NotNull(list);
        Assert.Equal(9, list.Count);

        Dictionary<string, string> categoryMap = list.ToDictionary(f => f.Name, f => f.Category);
        Assert.Equal("PQC Encrypted Bundle", categoryMap["sim1.enc.tar.gz"]);
        Assert.Equal("Archive Bundle", categoryMap["sim2.tar.gz"]);
        Assert.Equal("Archive Bundle", categoryMap["sim3.zip"]);
        Assert.Equal("Archive Bundle", categoryMap["sim4.tar"]);
        Assert.Equal("PQC Signature Manifest", categoryMap["sim5.manifest.pqc.json"]);
        Assert.Equal("Provenance Manifest", categoryMap["sim6.manifest.json"]);
        Assert.Equal("Provenance Manifest", categoryMap["sim7.json"]);
        Assert.Equal("Analytical Parquet", categoryMap["sim8.parquet"]);
        Assert.Equal("Export Artifact", categoryMap["sim9.dat"]);

        ExportFileItem firstItem = list.First(f => f.Name == "sim1.enc.tar.gz");
        Assert.Equal("/api/datasets/download/sim1.enc.tar.gz", firstItem.DownloadUrl);
        Assert.NotEmpty(firstItem.SizeFormatted);
        Assert.NotEmpty(firstItem.LastModifiedUtc);
    }

    /// <summary>
    /// Verifies that <see cref="DatasetTransferService.DeleteExportAsync"/> removes existing files, handles non-existent files, and guards against invalid input.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteExportAsync_DeletesExistingFile_And_HandlesMissing_And_SanitizesTraversal()
    {
        // WHAT: Creates a test file, deletes it, attempts deletion of a non-existent file, and verifies argument validation.
        // WHY:  Guarantees safe file lifecycle management and directory traversal protection.
        // HOW:  Creates dummy artifact, invokes DeleteExportAsync, and asserts boolean results and exceptions.
        string targetFile = Path.Combine(_testExportDir, "deletable_bundle.tar.gz");
        await File.WriteAllTextAsync(targetFile, "temporary bundle contents");
        Assert.True(File.Exists(targetFile));

        bool deleteSuccess = await _service.DeleteExportAsync("deletable_bundle.tar.gz");
        Assert.True(deleteSuccess);
        Assert.False(File.Exists(targetFile));

        bool deleteNonExistent = await _service.DeleteExportAsync("non_existent_file.tar.gz");
        Assert.False(deleteNonExistent);

        bool deleteTraversal = await _service.DeleteExportAsync("../../etc/passwd");
        Assert.False(deleteTraversal);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteExportAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteExportAsync("   "));
    }

    /// <summary>
    /// Verifies that <see cref="DatasetTransferService.ListTrajectoriesAsync"/> populates the <see cref="TrajectoryFileItem.DownloadUrl"/> property.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ListTrajectoriesAsync_PopulatesDownloadUrl()
    {
        // WHAT: Writes a trajectory file to the incoming directory and queries ListTrajectoriesAsync.
        // WHY:  Ensures the web download endpoint is populated for frontend user consumption.
        // HOW:  Writes a sample .xyz file, queries ListTrajectoriesAsync, and asserts DownloadUrl formatting.
        string sampleFile = Path.Combine(_testDataDir, "sample_nanocarrier.xyz");
        await File.WriteAllTextAsync(sampleFile, "1\nNanocarrier Test\nC 0.0 0.0 0.0\n");

        IReadOnlyList<TrajectoryFileItem> items = await _service.ListTrajectoriesAsync();
        TrajectoryFileItem item = Assert.Single(items);

        Assert.Equal("sample_nanocarrier.xyz", item.Name);
        Assert.Equal("/api/trajectories/download/sample_nanocarrier.xyz", item.DownloadUrl);
    }

    /// <summary>
    /// Verifies that <see cref="ExportFileItem"/> OpenAPI example generator creates valid metadata.
    /// </summary>
    [Fact]
    public void ExportFileItem_GetExamples_ReturnsValidModel()
    {
        // WHAT: Instantiates ExportFileItem and calls GetExamples.
        // WHY:  Enforces 100% code coverage on OpenAPI example provider contracts.
        // HOW:  Invokes GetExamples and asserts properties are populated.
        ExportFileItem example = new ExportFileItem
        {
            Name = "placeholder",
            SizeBytes = 0,
            SizeFormatted = "0 B",
            LastModifiedUtc = "2026-01-01T00:00:00Z",
            Category = "Archive"
        }.GetExamples();

        Assert.NotNull(example);
        Assert.Equal("dna-origami-nanocarrier-001.tar.gz", example.Name);
        Assert.Equal("Archive", example.Category);
        Assert.Equal("/api/datasets/download/dna-origami-nanocarrier-001.tar.gz", example.DownloadUrl);
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
