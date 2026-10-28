using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace NanoWeaverHrt.Security.Integrity;

/// <summary>
/// Server-side service that generates and caches W3C Subresource Integrity (SRI) SHA-384 cryptographic digests
/// for client-side static assets (JavaScript files and CSS stylesheets).
/// </summary>
/// <remarks>
/// Generating SRI digests server-side using .NET ensures that &lt;script&gt; and &lt;link&gt; tags in Razor files
/// always reference the exact, bit-for-bit SHA-384 fingerprint of the upstream files on disk without
/// requiring manual hash updates whenever client scripts or stylesheets are modified.
/// </remarks>
public sealed class AssetIntegrityService : IAssetIntegrityService
{
    /// <summary>
    /// The hosting environment providing access to web root and content root file providers.
    /// </summary>
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Thread-safe in-memory cache mapping normalized asset paths to their computed SRI strings.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _integrityCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetIntegrityService"/> class.
    /// </summary>
    /// <param name="environment">The hosting environment used to locate static web assets.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="environment"/> is null.</exception>
    public AssetIntegrityService(IWebHostEnvironment environment)
    {
        // WHAT: Injects the web host environment dependency.
        // WHY:  Allows locating static assets across WebRootFileProvider and physical disk directories.
        // HOW:  Validates non-null parameter and assigns to private readonly field.
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc/>
    public string GetSubresourceIntegrity(string relativeAssetPath)
    {
        // WHAT: Computes or retrieves from cache the SHA-384 W3C Subresource Integrity string for a given web asset.
        // WHY:  Guarantees client browsers verify script/style tampering before executing in the DOM.
        // HOW:  Normalizes the path, checks the ConcurrentDictionary cache, reads the asset bytes, hashes with SHA-384, and caches.
        if (string.IsNullOrWhiteSpace(relativeAssetPath))
        {
            return string.Empty;
        }

        string normalized = NormalizePath(relativeAssetPath);
        if (_integrityCache.TryGetValue(normalized, out string? cached))
        {
            return cached;
        }

        byte[]? fileBytes = TryReadAssetBytes(normalized);
        if (fileBytes is null || fileBytes.Length == 0)
        {
            return string.Empty;
        }

        byte[] hash = SHA384.HashData(fileBytes);
        string sriDigest = $"sha384-{Convert.ToBase64String(hash)}";

        _integrityCache[normalized] = sriDigest;
        return sriDigest;
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        // WHAT: Clears the in-memory SRI digest cache.
        // WHY:  Enables test fixtures and development workflows to invalidate stale asset digests after editing files.
        // HOW:  Calls Clear() on the ConcurrentDictionary cache.
        _integrityCache.Clear();
    }

    /// <summary>
    /// Normalizes a relative web path by trimming whitespace, removing leading slashes and tildes, and replacing backslashes.
    /// </summary>
    /// <param name="rawPath">The unnormalized asset path.</param>
    /// <returns>A normalized, forward-slash relative path.</returns>
    private static string NormalizePath(string rawPath)
    {
        // WHAT: Converts diverse path representations (e.g., "~/js/app.js", "/js/app.js", "js\\app.js") to a canonical key.
        // WHY:  Ensures deterministic cache hits regardless of leading delimiter styling in Razor templates.
        // HOW:  Trims whitespace, replaces '\\' with '/', and removes leading '~' and '/' characters.
        string cleaned = rawPath.Trim().Replace('\\', '/');
        while (cleaned.StartsWith("~/", StringComparison.Ordinal) || cleaned.StartsWith("/", StringComparison.Ordinal))
        {
            cleaned = cleaned.StartsWith("~/", StringComparison.Ordinal) ? cleaned[2..] : cleaned[1..];
        }

        return cleaned;
    }

    /// <summary>
    /// Attempts to read the raw bytes of an asset from the WebRootFileProvider or physical disk fallback locations.
    /// </summary>
    /// <param name="normalizedPath">The normalized asset path.</param>
    /// <returns>The raw byte content of the asset, or null if the file cannot be found.</returns>
    private byte[]? TryReadAssetBytes(string normalizedPath)
    {
        // WHAT: Resolves asset bytes through ASP.NET Core IFileProvider and physical disk paths.
        // WHY:  Supports both standalone execution, unit test WebApplicationFactory hosting, and containerized deployments.
        // HOW:  Queries WebRootFileProvider first; if unavailable, checks WebRootPath and ContentRootPath/wwwroot.
        IFileProvider? provider = _environment.WebRootFileProvider;
        if (provider is not null)
        {
            IFileInfo fileInfo = provider.GetFileInfo(normalizedPath);
            if (fileInfo.Exists)
            {
                using Stream stream = fileInfo.CreateReadStream();
                using MemoryStream ms = new();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        string? webRoot = _environment.WebRootPath;
        if (!string.IsNullOrEmpty(webRoot))
        {
            string fullPath = Path.Combine(webRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
        }

        string? contentRoot = _environment.ContentRootPath;
        if (!string.IsNullOrEmpty(contentRoot))
        {
            string fullPath = Path.Combine(contentRoot, "wwwroot", normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
        }

        return null;
    }
}
