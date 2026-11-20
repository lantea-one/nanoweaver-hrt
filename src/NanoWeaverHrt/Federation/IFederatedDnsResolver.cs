using System.Net;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Abstraction for resolving hostnames into IP addresses during SSRF validation of federated peer endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> An injectable DNS lookup boundary used by <see cref="FederatedPeerSyncService"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P206): Enables deterministic offline unit testing of every
/// SSRF IP range, IPv4-mapped IPv6 bypass, and dual-A-record edge case without real network DNS calls.
/// </para>
/// <para>
/// <strong>How it works:</strong> Production DI binds <see cref="SystemFederatedDnsResolver"/>; tests inject
/// deterministic resolvers returning controlled <see cref="IPAddress"/> arrays.
/// </para>
/// </remarks>
public interface IFederatedDnsResolver
{
    /// <summary>
    /// Resolves all IPv4 and IPv6 addresses associated with the specified hostname or IP literal.
    /// </summary>
    /// <param name="host">The hostname or IP address literal extracted from a peer endpoint URI.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>All resolved <see cref="IPAddress"/> records for <paramref name="host"/>.</returns>
    Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>
/// Production <see cref="IFederatedDnsResolver"/> backed by <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>.
/// </summary>
public sealed class SystemFederatedDnsResolver : IFederatedDnsResolver
{
    /// <inheritdoc/>
    public Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken = default)
    {
        // WHAT: Resolves a hostname or IP literal using the operating system DNS resolver.
        // WHY:  Allows SSRF validation to inspect every target IP before issuing an outbound HTTP request.
        // HOW:  Validates non-empty host input, fast-paths IP literals without network I/O, or delegates to Dns.GetHostAddressesAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            return Task.FromResult(new[] { literal });
        }

        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
