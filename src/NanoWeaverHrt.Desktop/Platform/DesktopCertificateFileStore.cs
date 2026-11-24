using System.Text;

namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Path-traversal-safe desktop file store for exporting and importing portable NIST FIPS 204 ML-DSA-65
/// Certificate of Analysis (<c>.coa.json</c>) and IND/IMPD regulatory dossier (<c>ind_dossier_*.json</c>)
/// bundles across Linux, macOS, and Windows workstations.
/// </summary>
/// <remarks>
/// // WHAT: Sandboxed file-system adapter for air-gapped CoA and regulatory dossier bundle exchange.
/// // WHY:  Milestones 37 &amp; 45: Allows clinicians to save downloaded bundles and load peer laboratory archives
/// //       from disk without risking directory traversal or oversized payload reads.
/// // HOW:  Canonicalizes the configured root directory, validates IDs and target paths with Path.GetFullPath,
/// //       and enforces 256 KiB (CoA) and 2 MiB (dossier) UTF-8 payload size ceilings.
/// </remarks>
public sealed class DesktopCertificateFileStore
{
    /// <summary>
    /// Maximum allowed Certificate of Analysis bundle file size in bytes (256 KiB).
    /// </summary>
    public const int MaxBundleBytes = 256 * 1024;

    /// <summary>
    /// Maximum allowed portable IND/IMPD regulatory dossier bundle file size in bytes (2 MiB).
    /// </summary>
    public const int MaxDossierBundleBytes = 2 * 1024 * 1024;

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
        // HOW:  Validates lotId and delegates bounded file write to ExportValidatedBytesAsync.
        string sanitized = SanitizeIdentifier(lotId, nameof(lotId), "Fabrication lot identifier contains unsupported filename characters.");
        return await ExportValidatedBytesAsync(
            $"{sanitized}.coa.json",
            certificateUtf8Bytes,
            MaxBundleBytes,
            nameof(certificateUtf8Bytes),
            $"Certificate bundle size must be between 1 and {MaxBundleBytes} bytes.",
            ct).ConfigureAwait(false);
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
        // HOW:  Delegates contained read with 256 KiB limit to ImportValidatedJsonAsync.
        return await ImportValidatedJsonAsync(
            fileName,
            MaxBundleBytes,
            "Certificate of Analysis bundle file was not found.",
            $"Certificate file size must be between 1 and {MaxBundleBytes} bytes.",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports a portable signed FDA IND / EMA IMPD regulatory dossier UTF-8 JSON bundle to
    /// <c>ind_dossier_{dossierId}.json</c> inside <see cref="RootDirectory"/>.
    /// </summary>
    /// <param name="dossierId">Alphanumeric/hyphen/underscore regulatory dossier identifier.</param>
    /// <param name="dossierUtf8Bytes">Raw UTF-8 JSON bytes downloaded from the node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full canonical path of the written dossier file.</returns>
    public async Task<string> ExportRegulatoryDossierBundleAsync(
        string dossierId,
        byte[] dossierUtf8Bytes,
        CancellationToken ct = default)
    {
        // WHAT: Persists a portable ML-DSA-65 IND/IMPD dossier bundle cleanly under RootDirectory.
        // WHY:  Milestone 45 (P237): Supports offline multi-center clinical audit exchange without directory traversal risks.
        // HOW:  Validates dossierId and delegates bounded file write (up to 2 MiB) to ExportValidatedBytesAsync.
        string sanitized = SanitizeIdentifier(dossierId, nameof(dossierId), "Regulatory dossier identifier contains unsupported filename characters.");
        return await ExportValidatedBytesAsync(
            $"ind_dossier_{sanitized}.json",
            dossierUtf8Bytes,
            MaxDossierBundleBytes,
            nameof(dossierUtf8Bytes),
            $"Regulatory dossier bundle size must be between 1 and {MaxDossierBundleBytes} bytes.",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a portable FDA IND / EMA IMPD dossier UTF-8 JSON string from a file inside <see cref="RootDirectory"/>.
    /// </summary>
    /// <param name="fileName">Relative file name inside <see cref="RootDirectory"/> (e.g. <c>ind_dossier_ind_001.json</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>UTF-8 JSON string ready for cross-node verification.</returns>
    public async Task<string> ImportRegulatoryDossierJsonAsync(
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Reads a portable IND/IMPD dossier JSON file after strict containment and 2 MiB size checks.
        // WHY:  Milestone 45 (P237): Shields desktop clients from path escape and memory exhaustion on untrusted bundles.
        // HOW:  Delegates contained read with 2 MiB ceiling to ImportValidatedJsonAsync.
        return await ImportValidatedJsonAsync(
            fileName,
            MaxDossierBundleBytes,
            "Regulatory dossier bundle file was not found.",
            $"Regulatory dossier file size must be between 1 and {MaxDossierBundleBytes} bytes.",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates byte size ceiling, verifies directory containment, and writes UTF-8 bundle bytes to disk.
    /// </summary>
    /// <param name="safeFileName">Validated single-segment filename beneath <see cref="_rootDirectory"/>.</param>
    /// <param name="utf8Bytes">Raw UTF-8 JSON payload bytes to persist.</param>
    /// <param name="maxBytes">Maximum allowed byte length.</param>
    /// <param name="bytesParamName">Parameter name reported when size validation fails.</param>
    /// <param name="sizeErrorMessage">Diagnostic error message thrown when length is out of range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Canonical full path of the written bundle file.</returns>
    private async Task<string> ExportValidatedBytesAsync(
        string safeFileName,
        byte[] utf8Bytes,
        int maxBytes,
        string bytesParamName,
        string sizeErrorMessage,
        CancellationToken ct)
    {
        // WHAT: Enforces size bounds, creates RootDirectory, and writes bundle bytes asynchronously.
        // WHY:  Eliminates duplicated export validation across CoA and regulatory dossier workflows.
        // HOW:  Checks null/range bounds, resolves contained path, and awaits File.WriteAllBytesAsync.
        ArgumentNullException.ThrowIfNull(utf8Bytes, bytesParamName);
        if (utf8Bytes.Length == 0 || utf8Bytes.Length > maxBytes)
        {
            throw new ArgumentOutOfRangeException(bytesParamName, sizeErrorMessage);
        }

        string fullPath = ResolveContainedPath(safeFileName);
        Directory.CreateDirectory(_rootDirectory);
        await File.WriteAllBytesAsync(fullPath, utf8Bytes, ct).ConfigureAwait(false);
        return fullPath;
    }

    /// <summary>
    /// Verifies directory containment, file existence, and size bounds before reading a UTF-8 JSON file.
    /// </summary>
    /// <param name="fileName">Relative file name inside <see cref="_rootDirectory"/>.</param>
    /// <param name="maxBytes">Maximum allowed file length in bytes.</param>
    /// <param name="missingMessage">Error message thrown when the target file does not exist.</param>
    /// <param name="sizeErrorMessage">Error message thrown when file size exceeds <paramref name="maxBytes"/> or is zero.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decoded UTF-8 JSON text.</returns>
    private async Task<string> ImportValidatedJsonAsync(
        string fileName,
        int maxBytes,
        string missingMessage,
        string sizeErrorMessage,
        CancellationToken ct)
    {
        // WHAT: Reads a UTF-8 JSON file cleanly beneath RootDirectory with bounded memory usage.
        // WHY:  Centralizes path-traversal and file-size hardening for CoA and dossier bundle imports.
        // HOW:  Resolves contained full path, verifies File.Exists and FileInfo.Length bounds, and reads UTF-8.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string fullPath = ResolveContainedPath(fileName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(missingMessage, fullPath);
        }

        var info = new FileInfo(fullPath);
        if (info.Length == 0 || info.Length > maxBytes)
        {
            throw new InvalidOperationException(sizeErrorMessage);
        }

        byte[] raw = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(raw);
    }

    /// <summary>
    /// Validates an identifier against the strict <c>[A-Za-z0-9_-]</c> filename whitelist without path separators.
    /// </summary>
    /// <param name="rawId">Input identifier token.</param>
    /// <param name="paramName">Parameter name for exception reporting.</param>
    /// <param name="errorMessage">Validation error message thrown on disallowed characters.</param>
    /// <returns>Trimmed validated identifier token.</returns>
    private static string SanitizeIdentifier(string rawId, string paramName, string errorMessage)
    {
        // WHAT: Enforces strict character whitelist on identifiers before constructing output filenames.
        // WHY:  Prevents directory separators, null bytes, or shell metacharacters in exported paths.
        // HOW:  Checks every character is ASCII letter, digit, '-', or '_' without allocating regexes or LINQ.
        ArgumentException.ThrowIfNullOrWhiteSpace(rawId, paramName);
        string trimmed = rawId.Trim();

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool allowed = char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';
            if (!allowed)
            {
                throw new ArgumentException(errorMessage, paramName);
            }
        }

        return trimmed;
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
