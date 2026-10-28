namespace NanoWeaverHrt.Security.Integrity;

/// <summary>
/// Defines a contract for calculating and retrieving W3C Subresource Integrity (SRI) cryptographic hashes
/// for static web assets (scripts and stylesheets) served by the sovereign nanotechnology node.
/// </summary>
/// <remarks>
/// Subresource Integrity guarantees that client browsers execute only untampered, bit-for-bit authentic
/// stylesheets and script code. Generating SRI digests dynamically server-side ensures that authored
/// client assets remain in perfect cryptographic lockstep with the running server binary.
/// </remarks>
public interface IAssetIntegrityService
{
    /// <summary>
    /// Computes or retrieves a cached W3C Subresource Integrity (SRI) digest for the specified relative asset path.
    /// </summary>
    /// <param name="relativeAssetPath">The web-root-relative path to the asset (for example, "js/app.js" or "css/app.css").</param>
    /// <returns>
    /// A W3C SRI-compliant hash string in the format "sha384-&lt;base64-digest&gt;", or an empty string if the asset file cannot be located.
    /// </returns>
    string GetSubresourceIntegrity(string relativeAssetPath);

    /// <summary>
    /// Invalidates and clears all cached SRI digests, forcing fresh computation on subsequent lookups.
    /// </summary>
    void ClearCache();
}
