using System.Text;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Path-traversal-safe desktop file store for exporting and importing portable NIST FIPS 204 ML-DSA-65
/// Certificate of Analysis (<c>.coa.json</c>) bundles across Linux, macOS, and Windows workstations.
/// </summary>
/// <remarks>
/// // WHAT: Sandboxed file-system adapter for air-gapped Certificate of Analysis bundle exchange.
/// // WHY:  Milestone 37 (P197): Allows clinicians to save downloaded CoA bundles and load peer laboratory certificates
/// //       from disk without risking directory traversal or oversized payload reads.
/// // HOW:  Canonicalizes the configured root directory, validates lot IDs and target paths with Path.GetFullPath,
/// //       and enforces a 256 KiB UTF-8 payload size ceiling.
/// </remarks>
public sealed class DesktopCertificateFileStore
{
    /// <summary>
    /// Maximum allowed Certificate of Analysis bundle file size in bytes (256 KiB).
    /// </summary>
    public const int MaxBundleBytes = 256 * 1024;

    /// <summary>
    /// Canonical root directory path where exported certificates are stored.
    /// </summary>
    private readonly string _rootDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopCertificateFileStore"/> class.
    /// </summary>
    /// <param name="rootDirectory">Target directory for portable CoA exports and imports.</param>
    public DesktopCertificateFileStore(string rootDirectory)
    {
        // WHAT: Canonicalizes and records the sandboxed root export directory.
        // WHY:  Prevents relative path escape attacks when reading or writing certificate bundles.
        // HOW:  Validates non-blank rootDirectory and resolves its full normalized filesystem path.
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// Gets the canonical root directory path managed by this certificate file store.
    /// </summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Exports a portable signed Certificate of Analysis UTF-8 JSON byte bundle to <c>{lotId}.coa.json</c>
    /// inside <see cref="RootDirectory"/>.
    /// </summary>
    /// <param name="lotId">Alphanumeric/hyphen/underscore fabrication lot identifier.</param>
    /// <param name="certificateUtf8Bytes">Raw UTF-8 JSON bytes downloaded from the node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full canonical path of the written certificate file.</returns>
    public async Task<string> ExportCertificateBundleAsync(
        string lotId,
        byte[] certificateUtf8Bytes,
        CancellationToken ct = default)
    {
        // WHAT: Persists a portable ML-DSA-65 CoA bundle cleanly under RootDirectory.
        // WHY:  Enables air-gapped transfer of signed fabrication certificates between clinics.
        // HOW:  Sanitizes lotId, verifies path containment and size ceiling, creates directory, and writes bytes asynchronously.
        ArgumentNullException.ThrowIfNull(certificateUtf8Bytes);
        if (certificateUtf8Bytes.Length == 0 || certificateUtf8Bytes.Length > MaxBundleBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(certificateUtf8Bytes),
                $"Certificate bundle size must be between 1 and {MaxBundleBytes} bytes.");
        }

        string safeFileName = BuildSafeFileName(lotId);
        string fullPath = ResolveContainedPath(safeFileName);

        Directory.CreateDirectory(_rootDirectory);
        await File.WriteAllBytesAsync(fullPath, certificateUtf8Bytes, ct).ConfigureAwait(false);
        return fullPath;
    }

    /// <summary>
    /// Imports a portable Certificate of Analysis UTF-8 JSON string from a file located inside <see cref="RootDirectory"/>.
    /// </summary>
    /// <param name="fileName">Relative file name inside <see cref="RootDirectory"/> (e.g. <c>lot_001.coa.json</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>UTF-8 JSON string ready for cross-node verification.</returns>
    public async Task<string> ImportCertificateJsonAsync(
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Reads a portable CoA JSON file from RootDirectory after strict path-containment and size checks.
        // WHY:  Prevents directory traversal or memory exhaustion when importing untrusted external peer certificates.
        // HOW:  Resolves contained full path, verifies file existence and length <= MaxBundleBytes, and decodes UTF-8.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string fullPath = ResolveContainedPath(fileName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Certificate of Analysis bundle file was not found.", fullPath);
        }

        var info = new FileInfo(fullPath);
        if (info.Length == 0 || info.Length > MaxBundleBytes)
        {
            throw new InvalidOperationException($"Certificate file size must be between 1 and {MaxBundleBytes} bytes.");
        }

        byte[] raw = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(raw);
    }

    /// <summary>
    /// Validates a fabrication lot identifier and produces a canonical <c>.coa.json</c> filename without path separators.
    /// </summary>
    /// <param name="lotId">Fabrication lot identifier.</param>
    /// <returns>Safe filename ending in <c>.coa.json</c>.</returns>
    private static string BuildSafeFileName(string lotId)
    {
        // WHAT: Enforces strict character whitelist on lotId before constructing an output filename.
        // WHY:  Prevents directory separators, null bytes, or shell metacharacters in exported certificate paths.
        // HOW:  Checks every character is ASCII letter, digit, '-', or '_' without allocating regexes or LINQ.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        string trimmed = lotId.Trim();

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool allowed = char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';
            if (!allowed)
            {
                throw new ArgumentException("Fabrication lot identifier contains unsupported filename characters.", nameof(lotId));
            }
        }

        return $"{trimmed}.coa.json";
    }

    /// <summary>
    /// Resolves a candidate file name against <see cref="_rootDirectory"/> and enforces strict prefix containment.
    /// </summary>
    /// <param name="relativeFileName">Relative filename inside <see cref="_rootDirectory"/>.</param>
    /// <returns>Canonical full path guaranteed to reside inside <see cref="_rootDirectory"/>.</returns>
    private string ResolveContainedPath(string relativeFileName)
    {
        // WHAT: Computes canonical full path and verifies it stays strictly beneath _rootDirectory.
        // WHY:  Eliminates '../' or absolute-path escape attacks when importing or exporting certificate files.
        // HOW:  Compares Path.GetFullPath(Path.Combine(_rootDirectory, relativeFileName)) against normalized prefix.
        string candidate = Path.GetFullPath(Path.Combine(_rootDirectory, relativeFileName));
        string prefix = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDirectory
            : _rootDirectory + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Certificate file path escapes the configured certificate store directory.");
        }

        return candidate;
    }
}
