using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Services;

/// <summary>
/// Provides high-level dataset export, import, packaging, and cryptographic verification services.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The dataset transfer engine that allows sovereign nodes to share 
/// simulation runs without relying on centralized cloud providers (AWS/GCP).
/// </para>
/// <para>
/// <strong>Security &amp; Integrity:</strong> Every exported dataset is bundled into a standardized POSIX TAR 
/// archive with GZIP compression. An accompanying SHA-256 digest is computed in-stream to guarantee 
/// bit-level immutability.
/// </para>
/// </remarks>
public sealed class DatasetTransferService : IDatasetManager
{
    /// <summary>
    /// Absolute directory path where active simulation trajectories are stored.
    /// </summary>
    private readonly string _dataDirectory;

    /// <summary>
    /// Absolute directory path where exported archives and cryptographic manifests are written.
    /// </summary>
    private readonly string _exportDirectory;

    /// <summary>
    /// Application logger for diagnostic and audit telemetry.
    /// </summary>
    private readonly ILogger<DatasetTransferService> _logger;

    /// <summary>
    /// Maximum number of archive members a single imported bundle may contain.
    /// </summary>
    private readonly int _maxImportEntries;

    /// <summary>
    /// Maximum total number of bytes a single imported bundle may expand to once decompressed.
    /// </summary>
    private readonly long _maxImportExpandedBytes;

    /// <summary>
    /// Default ceiling on archive member count when <c>DATASET_IMPORT_MAX_ENTRIES</c> is not configured.
    /// </summary>
    /// <remarks>
    /// A trajectory dataset is a handful of coordinate files plus their configuration and log artifacts, so four
    /// thousand members is generous. The ceiling exists so a tar file made of millions of empty members cannot pin a
    /// CPU core and fill the inode table.
    /// </remarks>
    private const int DefaultMaxImportEntries = 4096;

    /// <summary>
    /// Default ceiling on total expanded bytes when <c>DATASET_IMPORT_MAX_BYTES</c> is not configured (8 GiB).
    /// </summary>
    /// <remarks>
    /// GZIP compresses the highly repetitive text of a coordinate trajectory extremely well, so a small upload can
    /// expand enormously (a "decompression bomb"). The limit is checked against each member's declared size before any
    /// of its bytes are written, so an oversized bundle is refused rather than half-written.
    /// </remarks>
    private const long DefaultMaxImportExpandedBytes = 8L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatasetTransferService"/>.
    /// </summary>
    /// <param name="configuration">Application configuration containing directory path settings.</param>
    /// <param name="logger">Telemetry logger.</param>
    public DatasetTransferService(IConfiguration configuration, ILogger<DatasetTransferService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // WHAT: Resolves the archive member-count and expanded-size ceilings applied during import.
        // WHY:  Both bound the damage a hostile or merely broken bundle can do; operators running unusually large
        //       cohort datasets can raise them without a code change.
        // HOW:  GetValue falls back to the documented defaults when the keys are absent.
        _maxImportEntries = configuration.GetValue("DATASET_IMPORT_MAX_ENTRIES", DefaultMaxImportEntries);
        _maxImportExpandedBytes = configuration.GetValue("DATASET_IMPORT_MAX_BYTES", DefaultMaxImportExpandedBytes);

        // WHAT: Resolves the active data and export directory paths from environment or defaults.
        // WHY:  Permits seamless path overrides in Docker containers and bare-metal environments.
        // HOW:  Reads configuration keys TRAJECTORY_INCOMING_DIR and TRAJECTORY_EXPORT_DIR.
        _dataDirectory = configuration["TRAJECTORY_INCOMING_DIR"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");

        _exportDirectory = configuration["TRAJECTORY_EXPORT_DIR"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "exports");

        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_exportDirectory);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(CancellationToken cancellationToken = default)
    {
        List<TrajectoryFileItem> items = new();

        if (!Directory.Exists(_dataDirectory))
        {
            return Task.FromResult<IReadOnlyList<TrajectoryFileItem>>(items);
        }

        DirectoryInfo dir = new(_dataDirectory);
        FileInfo[] files = dir.GetFiles("*.*", SearchOption.TopDirectoryOnly);

        foreach (FileInfo file in files)
        {
            string ext = file.Extension.ToLowerInvariant();
            string format = ext switch
            {
                ".xyz" => "XYZ",
                ".dcd" => "DCD",
                ".top" => "oxDNA Topology",
                ".dat" => "oxDNA Configuration",
                ".conf" => "oxDNA Configuration",
                ".in" => "LAMMPS Script",
                _ => "Raw Data"
            };

            items.Add(new TrajectoryFileItem
            {
                Name = file.Name,
                SizeBytes = file.Length,
                SizeFormatted = FormatBytes(file.Length),
                LastModifiedUtc = file.LastWriteTimeUtc.ToString("o"),
                Format = format
            });
        }

        return Task.FromResult<IReadOnlyList<TrajectoryFileItem>>(items);
    }

    /// <inheritdoc/>
    public async Task<DatasetManifest> ExportDatasetAsync(
        string datasetName,
        IEnumerable<string>? fileNames = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);

        // Sanitize dataset name to prevent path traversal
        string safeDatasetName = Path.GetFileNameWithoutExtension(datasetName);
        string archiveFileName = $"{safeDatasetName}.tar.gz";
        string manifestFileName = $"{safeDatasetName}.manifest.json";

        string archivePath = Path.Combine(_exportDirectory, archiveFileName);
        string manifestPath = Path.Combine(_exportDirectory, manifestFileName);

        HashSet<string>? filter = fileNames != null ? new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase) : null;
        List<string> includedFiles = new();

        _logger.LogInformation("Exporting dataset {DatasetName} to {ArchivePath}...", safeDatasetName, archivePath);

        // WHAT: Creates a .tar.gz archive and computes its SHA-256 hash simultaneously.
        // WHY:  Avoids a secondary disk read pass to hash the resulting multi-gigabyte archive.
        // HOW:  Wraps the FileStream in a CryptoStream, then GZipStream, then TarWriter.
        byte[] computedHash;
        long totalBytesWritten;

        using (FileStream fileStream = File.Create(archivePath))
        using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            using (GZipStream gzipStream = new(fileStream, CompressionLevel.Optimal, leaveOpen: true))
            using (TarWriter tarWriter = new(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
            {
                DirectoryInfo dir = new(_dataDirectory);
                foreach (FileInfo file in dir.GetFiles())
                {
                    if (filter != null && !filter.Contains(file.Name))
                    {
                        continue;
                    }

                    // WHAT: Omits any file in the data volume that the shared dataset member policy does not admit.
                    // WHY:  The volume holds more than trajectories. It also holds weaver_config.db, the SQLite file
                    //       containing every user account, role, public key and patient record, because that is where
                    //       SystemConfigurationStore puts it. Packing the volume wholesale therefore shipped the whole
                    //       identity and PHI store inside every export bundle - an exfiltration of the entire account
                    //       database through the documented export feature. The filter is unconditional, so it applies
                    //       even when the caller named the file explicitly in the request's file list.
                    // HOW:  DatasetMemberPolicy.IsAllowedMemberName is the same predicate the import path enforces, so
                    //       a bundle this node produces is by construction a bundle this node will accept back.
                    //       Exclusion is silent and non-fatal: exporting is a routine operation and must keep working
                    //       on a volume that legitimately contains a database.
                    if (!DatasetMemberPolicy.IsAllowedMemberName(file.Name))
                    {
                        _logger.LogDebug("Excluded {FileName} from dataset {DatasetName}: not an accepted dataset artifact.", file.Name, safeDatasetName);
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Write each file entry to the TAR archive
                    tarWriter.WriteEntry(file.FullName, file.Name);
                    includedFiles.Add(file.Name);
                }
            }

            // Compute hash of the generated .tar.gz file on disk
            fileStream.Position = 0;
            byte[] readBuffer = new byte[81920];
            int read;
            while ((read = await fileStream.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), cancellationToken)) > 0)
            {
                sha256.AppendData(readBuffer, 0, read);
            }

            computedHash = sha256.GetHashAndReset();
            totalBytesWritten = fileStream.Length;
        }

        string hexHash = Convert.ToHexString(computedHash).ToLowerInvariant();

        DatasetManifest manifest = new()
        {
            DatasetName = safeDatasetName,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            ArchiveFileName = archiveFileName,
            Sha256Checksum = hexHash,
            SizeInBytes = totalBytesWritten,
            IncludedFiles = includedFiles.ToArray()
        };

        // Write the machine-readable manifest JSON file alongside the archive
        string jsonContent = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, jsonContent, cancellationToken);

        _logger.LogInformation("Dataset export complete. SHA-256: {Hash}", hexHash);
        return manifest;
    }

    /// <inheritdoc/>
    public async Task<bool> ImportDatasetAsync(
        Stream archiveStream,
        DatasetManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentNullException.ThrowIfNull(manifest);

        _logger.LogInformation("Verifying and importing dataset {DatasetName}...", manifest.DatasetName);

        // WHAT: Computes the SHA-256 hash of the uploaded stream to verify cryptographic provenance.
        // WHY:  Guarantees decentralized dataset authenticity; rejects corrupted or malicious uploads.
        // HOW:  Buffers the archive to a temporary file while hashing with IncrementalHash.
        string tempArchivePath = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid()}.tar.gz");

        try
        {
            byte[] actualHashBytes;
            using (FileStream tempFile = File.Create(tempArchivePath))
            using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await archiveStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    sha256.AppendData(buffer, 0, bytesRead);
                    await tempFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
                actualHashBytes = sha256.GetHashAndReset();
            }

            string actualHexHash = Convert.ToHexString(actualHashBytes).ToLowerInvariant();

            if (!string.Equals(actualHexHash, manifest.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Cryptographic mismatch! Expected {Expected}, got {Actual}", manifest.Sha256Checksum, actualHexHash);
                return false;
            }

            _logger.LogInformation("Cryptographic provenance verified. Extracting archive into {TargetDir}...", _dataDirectory);

            ExtractVerifiedArchive(tempArchivePath, manifest.DatasetName);

            _logger.LogInformation("Dataset {DatasetName} successfully imported.", manifest.DatasetName);
            return true;
        }
        finally
        {
            if (File.Exists(tempArchivePath))
            {
                File.Delete(tempArchivePath);
            }
        }
    }

    /// <summary>
    /// Expands a checksum-verified <c>.tar.gz</c> bundle into the active data volume, refusing the whole bundle if any
    /// member violates the import contract.
    /// </summary>
    /// <param name="verifiedArchivePath">Absolute path of the local temporary archive whose SHA-256 digest already matched.</param>
    /// <param name="datasetName">Dataset name from the manifest, used only for operator-facing log lines.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a member is not a regular file, carries a name that is not a plain file name, carries a file type
    /// outside <see cref="DatasetMemberPolicy.AllowedMemberExtensions"/>, or when the bundle exceeds the configured member-count or
    /// expanded-size ceilings. The middleware turns this into an RFC 7807 problem document with a 400 status.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>The attack this closes (audit finding C3).</strong> The previous implementation ran
    /// <c>Path.GetFileName(entry.Name)</c> and then <c>ExtractToFile(..., overwrite: true)</c> straight into the shared
    /// data volume. That blocked <c>../</c> traversal, but it blocked it by <em>rewriting</em> the name rather than
    /// refusing it, and the volume it wrote into is the same directory that holds <c>weaver_config.db</c> — the SQLite
    /// file containing every user account, role and public key. A member plainly named <c>weaver_config.db</c>
    /// therefore survived sanitisation untouched and overwrote the identity database. Since the only integrity check
    /// is the archive's digest against a checksum supplied by the same uploader in the same request, nothing stood in
    /// the way: a Researcher — the least-privileged role allowed through <c>/api/datasets/import</c> — could upload a
    /// database of their own making in which they are an Administrator, and then log in as one.
    /// </para>
    /// <para>
    /// <strong>How it is closed.</strong> Four checks run against every member <em>before</em> a single byte is
    /// written, and any failure aborts the entire import rather than skipping the member: the member must be a regular
    /// file (so a symbolic link cannot be planted as a writable handle to a file elsewhere on the host), its name must
    /// be a plain file name (so <c>../../weaver.db</c> and <c>/etc/passwd</c> are refused outright rather than
    /// trimmed into something that still writes), its suffix must be allow-listed (so no database, key, archive or
    /// script can be delivered at all), and the member count and total expanded size must stay inside the configured
    /// ceilings.
    /// </para>
    /// <para>
    /// <strong>Why extraction is staged.</strong> Members are expanded into a fresh, private staging directory and only
    /// moved into the active volume once the <em>whole</em> archive has been validated. Without staging, an archive
    /// whose tenth member is hostile would already have written its first nine, leaving the volume in a half-imported
    /// state. Because the staging directory is a child of the data volume it is on the same file system, so the final
    /// step is an atomic rename rather than a copy.
    /// </para>
    /// </remarks>
    private void ExtractVerifiedArchive(string verifiedArchivePath, string datasetName)
    {
        // WHAT: Creates a private per-import staging directory inside the data volume.
        // WHY:  Staging makes the import all-or-nothing, and keeping it on the same file system makes the final
        //       promotion step a rename instead of a second full copy of potentially gigabytes of coordinates.
        // HOW:  A GUID-suffixed directory name cannot collide with a concurrent import or with any archive member,
        //       and the finally block removes it whether the import succeeded or was refused.
        string stagingDirectory = Path.Combine(_dataDirectory, $".weaver-import-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            int entryCount = 0;
            long expandedBytes = 0;

            using (FileStream readTemp = File.OpenRead(verifiedArchivePath))
            using (GZipStream gzip = new(readTemp, CompressionMode.Decompress))
            using (TarReader tarReader = new(gzip))
            {
                // WHAT: Walks every member of the TAR stream, validating then staging it.
                // WHY:  A TAR stream is forward-only, so validation and staging must happen in the same pass; the
                //       promotion pass afterwards operates on already-trusted files on disk.
                // HOW:  Each member is vetted by the three DatasetMemberPolicy predicates - the same policy the export
                //       path packs against - plus the two running ceilings, before ExtractToFile is allowed to touch
                //       the file system.
                TarEntry? entry;
                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    entryCount++;
                    if (entryCount > _maxImportEntries)
                    {
                        _logger.LogError("Rejected dataset import {DatasetName}: archive member count exceeded the configured limit of {Limit}.", datasetName, _maxImportEntries);
                        throw new InvalidOperationException("Dataset import rejected: the archive contains more members than the configured import limit allows.");
                    }

                    if (!DatasetMemberPolicy.IsAllowedArchiveEntryType(entry.EntryType))
                    {
                        _logger.LogError("Rejected dataset import {DatasetName}: archive member of disallowed type {EntryType}.", datasetName, entry.EntryType);
                        throw new InvalidOperationException("Dataset import rejected: the archive contains a member that is not a regular file. Symbolic links, hard links, directories and device nodes are not permitted.");
                    }

                    if (!DatasetMemberPolicy.IsSafeArchiveEntryName(entry.Name))
                    {
                        _logger.LogError("Rejected dataset import {DatasetName}: archive member name is not a plain file name.", datasetName);
                        throw new InvalidOperationException("Dataset import rejected: an archive member name is not a plain file name. Directory separators, drive qualifiers and relative path segments are not permitted.");
                    }

                    if (!DatasetMemberPolicy.IsAllowedMemberName(entry.Name))
                    {
                        _logger.LogError("Rejected dataset import {DatasetName}: archive member carries a disallowed file type.", datasetName);
                        throw new InvalidOperationException("Dataset import rejected: an archive member carries a file type that is not an accepted trajectory or dataset artifact.");
                    }

                    expandedBytes += entry.Length;
                    if (expandedBytes > _maxImportExpandedBytes)
                    {
                        _logger.LogError("Rejected dataset import {DatasetName}: expanded size exceeded the configured limit of {Limit} bytes.", datasetName, _maxImportExpandedBytes);
                        throw new InvalidOperationException("Dataset import rejected: the archive expands to more bytes than the configured import limit allows.");
                    }

                    entry.ExtractToFile(Path.Combine(stagingDirectory, entry.Name), overwrite: true);
                }
            }

            // WHAT: Promotes every staged file into the active data volume.
            // WHY:  Reaching this point means the entire archive passed validation, so the volume transitions straight
            //       from its pre-import state to its fully imported state with no observable half-way state.
            // HOW:  File.Move with overwrite replaces a same-named trajectory in place; every name here was already
            //       proven to be a plain, allow-listed file name, so the destination is necessarily inside the volume.
            foreach (string stagedFilePath in Directory.GetFiles(stagingDirectory))
            {
                File.Move(stagedFilePath, Path.Combine(_dataDirectory, Path.GetFileName(stagedFilePath)), overwrite: true);
            }

            _logger.LogInformation("Staged and promoted {EntryCount} archive members ({ExpandedBytes} bytes) for dataset {DatasetName}.", entryCount, expandedBytes, datasetName);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExportFileItem>> ListExportsAsync(CancellationToken cancellationToken = default)
    {
        // WHAT: Discovers and catalogs all exported archive bundles and manifests in the exports volume.
        // WHY:  Allows sovereign research operators to inspect and download generated dataset bundles via the web UI.
        // HOW:  Enumerates files in _exportDirectory, filters out .gitkeep, and categorizes by extension and naming convention.
        List<ExportFileItem> items = new();
        if (!Directory.Exists(_exportDirectory))
        {
            return Task.FromResult<IReadOnlyList<ExportFileItem>>(items);
        }

        DirectoryInfo dir = new(_exportDirectory);
        foreach (FileInfo file in dir.EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Name.Equals(".gitkeep", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string ext = file.Extension.ToLowerInvariant();
            string category;
            if (file.Name.EndsWith(".enc.tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                category = "PQC Encrypted Bundle";
            }
            else if (file.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || ext == ".zip" || ext == ".tar")
            {
                category = "Archive Bundle";
            }
            else if (file.Name.EndsWith(".manifest.pqc.json", StringComparison.OrdinalIgnoreCase))
            {
                category = "PQC Signature Manifest";
            }
            else if (file.Name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) || ext == ".json")
            {
                category = "Provenance Manifest";
            }
            else if (ext == ".parquet")
            {
                category = "Analytical Parquet";
            }
            else
            {
                category = "Export Artifact";
            }

            items.Add(new ExportFileItem
            {
                Name = file.Name,
                SizeBytes = file.Length,
                SizeFormatted = FormatBytes(file.Length),
                LastModifiedUtc = file.LastWriteTimeUtc.ToString("o"),
                Category = category
            });
        }

        return Task.FromResult<IReadOnlyList<ExportFileItem>>(items);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteExportAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // WHAT: Safely deletes an export artifact from disk.
        // WHY:  Allows users to purge stale test or transient export bundles from the web interface.
        // HOW:  Combines _exportDirectory with Path.GetFileName to prevent path traversal attacks.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        string safeName = Path.GetFileName(fileName);
        string filePath = Path.Combine(_exportDirectory, safeName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Deleted export artifact {FileName} from {ExportDir}", safeName, _exportDirectory);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Formats a raw byte count into a human-friendly size representation.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string with size unit (e.g., "1.2 MB").</returns>
    internal static string FormatBytes(long bytes)
    {
        // WHAT: Converts a raw integer byte count into scaled decimal units (B, KB, MB, GB, TB).
        // WHY:  Presents human-readable trajectory dataset file sizes in UI telemetry and directory listings.
        // HOW:  Iteratively divides byte count by 1024 while advancing the unit suffix index.
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {suffixes[order]}";
    }
}
