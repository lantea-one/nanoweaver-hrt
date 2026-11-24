using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Dialect-agnostic persistence contract for encrypted FDA IND / EMA IMPD regulatory dossier snapshots
/// stored in <c>weaver_regulatory_dossiers</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Data-access abstraction over <c>weaver_regulatory_dossiers</c> supporting atomic
/// archival, primary-key retrieval, newest-first patient history queries, and GDPR Article 17 patient erasure.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P235): Isolates regulatory submission bundle storage from SQL
/// dialect differences across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="RegulatoryDossierRepository"/> using explicit transaction
/// scopes, zero-LINQ ordinal mapping, and parameterized dialect queries.
/// </para>
/// </remarks>
public interface IRegulatoryDossierRepository
{
    /// <summary>
    /// Atomically persists or replaces an encrypted regulatory dossier snapshot record.
    /// </summary>
    /// <param name="record">Encrypted dossier snapshot record to store.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task representing the asynchronous transactional upsert.</returns>
    Task SaveDossierAsync(
        RegulatoryDossierRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single regulatory dossier snapshot record by its primary identifier.
    /// </summary>
    /// <param name="dossierId">Unique dossier snapshot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The matching <see cref="RegulatoryDossierRecord"/>, or <see langword="null"/> when absent.</returns>
    Task<RegulatoryDossierRecord?> GetDossierByIdAsync(
        string dossierId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all compiled regulatory dossier snapshots belonging to <paramref name="patientId"/> ordered newest first.
    /// </summary>
    /// <param name="patientId">Patient identifier whose dossiers should be listed.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Newest-first read-only list of <see cref="RegulatoryDossierRecord"/> records.</returns>
    Task<IReadOnlyList<RegulatoryDossierRecord>> ListDossiersByPatientAsync(
        string patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all regulatory dossier archive rows belonging to <paramref name="patientId"/> for GDPR Article 17 erasure.
    /// </summary>
    /// <param name="patientId">Patient identifier whose dossier archives must be permanently removed.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Total count of deleted dossier archive rows.</returns>
    Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default);
}
