using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Builds the one real <see cref="WeaverDbConnectionFactory"/> that every repository test needs.
/// </summary>
/// <remarks>
/// The factory is satisfied only by the real class (never a fake), so this helper only supplies the two
/// collaborators tests do not care about: a migration runner and a logger.
/// </remarks>
internal static class WeaverTestDb
{
    /// <summary>
    /// Creates a real connection factory over the given configuration.
    /// </summary>
    /// <param name="configuration">Configuration naming the temp data directory and optional PostgreSQL string.</param>
    /// <param name="runner">Migration runner to use, or null for a default runner.</param>
    /// <param name="logger">Logger to use, or null to discard log output.</param>
    /// <returns>A real factory.</returns>
    public static WeaverDbConnectionFactory CreateFactory(
        IConfiguration configuration,
        DatabaseMigrationRunner? runner = null,
        ILogger<WeaverDbConnectionFactory>? logger = null) =>
        new(configuration, runner ?? new DatabaseMigrationRunner(), logger ?? NullLogger<WeaverDbConnectionFactory>.Instance);
}
