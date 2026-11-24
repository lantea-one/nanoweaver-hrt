using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting encrypted FDA IND / EMA IMPD regulatory dossier archives
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IRegulatoryDossierRepository"/> backed
/// by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P235): Guarantees deterministic, dialect-neutral storage and
/// GDPR Article 17 erasure cascades for ML-DSA-65 signed clinical trial submission packages.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses explicit <c>{ await using ... }</c> block scopes, positional column mapping
/// without LINQ allocations, and atomic DELETE + INSERT writes inside <see cref="DbTransaction"/> scopes.
/// </para>
/// </remarks>
public sealed class RegulatoryDossierRepository : IRegulatoryDossierRepository
{
    /// <summary>
    /// Shared comma-separated column projection for <c>weaver_regulatory_dossiers</c>.
    /// </summary>
    private const string DossierSelectColumns =
        "dossier_id, patient_id, lot_id, submission_framework, payload_sha256, " +
        "signature_base64, signer_key_id, bundle_json_encrypted, completeness_score, " +
        "is_submission_ready, compiled_by, compiled_at_utc";

    /// <summary>
    /// Multi-engine database connection factory supplying dialect-configured sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegulatoryDossierRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public RegulatoryDossierRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Validates and retains the database connection factory.
        // WHY:  Prevents silent null dereferences during regulatory dossier archive writes or reads.
        // HOW:  Guards connectionFactory upfront with ArgumentNullException.ThrowIfNull.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task SaveDossierAsync(
        RegulatoryDossierRecord record,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically inserts or replaces a regulatory dossier archive record in weaver_regulatory_dossiers.
        // WHY:  Provides idempotent archival across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB without dialect upsert quirks.
        // HOW:  Validates required fields and executes DELETE + INSERT inside an explicit block-scoped DbTransaction.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.DossierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PayloadSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SignatureBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SignerKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.BundleJsonEncrypted);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.CompiledBy);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delCmd = session.CreateCommand(
                $"DELETE FROM weaver_regulatory_dossiers WHERE dossier_id = {session.Dialect.Parameter("did")}"))
            {
                delCmd.Transaction = tx;
                WeaverDbParameters.Add(delCmd, "did", record.DossierId);
                await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertSql =
                $"INSERT INTO weaver_regulatory_dossiers ({DossierSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("did")}, {session.Dialect.Parameter("pid")}, {session.Dialect.Parameter("lid")}, " +
                $"{session.Dialect.Parameter("fwk")}, {session.Dialect.Parameter("sha")}, {session.Dialect.Parameter("sig")}, " +
                $"{session.Dialect.Parameter("kid")}, {session.Dialect.Parameter("bnd")}, {session.Dialect.Parameter("scr")}, " +
                $"{session.Dialect.Parameter("rdy")}, {session.Dialect.Parameter("cby")}, {session.Dialect.Parameter("cat")})";

            await using (DbCommand insCmd = session.CreateCommand(insertSql))
            {
                insCmd.Transaction = tx;
                WeaverDbParameters.Add(insCmd, "did", record.DossierId);
                WeaverDbParameters.Add(insCmd, "pid", record.PatientId);
                WeaverDbParameters.AddNullable(insCmd, "lid", string.IsNullOrWhiteSpace(record.LotId) ? null : record.LotId.Trim());
                WeaverDbParameters.Add(insCmd, "fwk", record.SubmissionFramework.ToString());
                WeaverDbParameters.Add(insCmd, "sha", record.PayloadSha256);
                WeaverDbParameters.Add(insCmd, "sig", record.SignatureBase64);
                WeaverDbParameters.Add(insCmd, "kid", record.SignerKeyId);
                WeaverDbParameters.Add(insCmd, "bnd", record.BundleJsonEncrypted);
                WeaverDbParameters.Add(insCmd, "scr", record.CompletenessScore);
                WeaverDbParameters.Add(insCmd, "rdy", session.Dialect.BooleanValue(record.IsSubmissionReady));
                WeaverDbParameters.Add(insCmd, "cby", record.CompiledBy);
                WeaverDbParameters.Add(insCmd, "cat", session.Dialect.TimestampValue(record.CompiledAtUtc));
                await insCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<RegulatoryDossierRecord?> GetDossierByIdAsync(
        string dossierId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Retrieves a single regulatory dossier archive row by dossier_id.
        // WHY:  Powers portable signed bundle download and dossier verification endpoints cleanly.
        // HOW:  Executes parameterized primary-key SELECT inside an explicit block scope and maps ordinals via ReadDossier.
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierId);

        RegulatoryDossierRecord? record = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {DossierSelectColumns} FROM weaver_regulatory_dossiers " +
                $"WHERE dossier_id = {session.Dialect.Parameter("did")}");
            WeaverDbParameters.Add(cmd, "did", dossierId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                record = ReadDossier(reader);
            }
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RegulatoryDossierRecord>> ListDossiersByPatientAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all compiled regulatory dossier records for patientId in newest-first chronological order.
        // WHY:  Surfaces trial archive history in REST APIs and ComplianceSecurity UI without LINQ allocations.
        // HOW:  Executes parameterized SELECT ORDER BY compiled_at_utc DESC, dossier_id DESC and reads rows sequentially.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        List<RegulatoryDossierRecord> records = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {DossierSelectColumns} FROM weaver_regulatory_dossiers " +
                $"WHERE patient_id = {session.Dialect.Parameter("pid")} " +
                "ORDER BY compiled_at_utc DESC, dossier_id DESC");
            WeaverDbParameters.Add(cmd, "pid", patientId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(ReadDossier(reader));
            }
        }

        return records;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically deletes all patient-owned regulatory dossier archive rows for GDPR Article 17 erasure.
        // WHY:  Ensures subject right-to-erasure purges compiled trial bundles cleanly without leaving orphaned PHI snapshots.
        // HOW:  Executes parameterized DELETE inside an explicit block scope and returns affected row count.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deleted = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"DELETE FROM weaver_regulatory_dossiers WHERE patient_id = {session.Dialect.Parameter("pid")}");
            WeaverDbParameters.Add(cmd, "pid", patientId);

            deleted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Materializes a <see cref="RegulatoryDossierRecord"/> from the current row of an open data reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="DossierSelectColumns"/>.</param>
    /// <returns>The populated regulatory dossier archive record.</returns>
    private static RegulatoryDossierRecord ReadDossier(DbDataReader reader)
    {
        // WHAT: Maps an aligned 12-column SQL row into a RegulatoryDossierRecord instance.
        // WHY:  Centralizes nullable lot_id handling, cross-engine boolean/timestamp decoding, and enum parsing.
        // HOW:  Reads columns by fixed ordinal index using WeaverDbValues helpers without LINQ allocations.
        return new RegulatoryDossierRecord
        {
            DossierId = reader.GetString(0),
            PatientId = reader.GetString(1),
            LotId = reader.IsDBNull(2) ? null : reader.GetString(2),
            SubmissionFramework = ParseFrameworkOrDefault(reader.GetString(3)),
            PayloadSha256 = reader.GetString(4),
            SignatureBase64 = reader.GetString(5),
            SignerKeyId = reader.GetString(6),
            BundleJsonEncrypted = reader.GetString(7),
            CompletenessScore = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
            IsSubmissionReady = WeaverDbValues.ReadBoolean(reader, 9),
            CompiledBy = reader.GetString(10),
            CompiledAtUtc = WeaverDbValues.ReadTimestamp(reader, 11)
        };
    }

    /// <summary>
    /// Parses a persisted submission framework label with a safe FDA IND fallback on unrecognized values.
    /// </summary>
    /// <param name="raw">Raw database string value from <c>submission_framework</c>.</param>
    /// <returns>Parsed <see cref="RegulatorySubmissionFramework"/> enum value or <see cref="RegulatorySubmissionFramework.FdaInd"/>.</returns>
    private static RegulatorySubmissionFramework ParseFrameworkOrDefault(string raw)
    {
        // WHAT: Parses stored regulatory submission framework tokens safely without throwing on unknown strings.
        // WHY:  Preserves backward-compatible row reads across schema versions.
        // HOW:  Uses Enum.TryParse with case-insensitive matching and defaults to FdaInd.
        return Enum.TryParse(raw, ignoreCase: true, out RegulatorySubmissionFramework parsed)
            ? parsed
            : RegulatorySubmissionFramework.FdaInd;
    }
}
