using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 010: Encrypts patient DNA blueprint regimen and notes columns at rest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> <c>weaver_patient_dna_records</c> holds patient digital DNA blueprints,
/// including their therapeutic regimen (<c>MaleToFemale</c> or <c>FemaleToMale</c>) and clinician/patient notes.
/// Together with patient identity, these constitute special-category protected health information (PHI) under
/// GDPR Article 9. This migration ensures they are sealed at rest with AES-256-GCM.
/// </para>
/// <para>
/// <strong>What it does:</strong> On PostgreSQL, it widens <c>regimen</c> from <c>VARCHAR(64)</c> to <c>TEXT</c>
/// so that the Base64 authenticated ciphertext envelope fits. On SQLite, column types are dynamic so no DDL
/// modification is required. It then scans existing rows and seals plaintext <c>regimen</c> and non-empty
/// <c>notes</c> values in place using <see cref="ProfileDataProtector"/>.
/// </para>
/// <para>
/// <strong>Idempotence and failure:</strong> Values already carrying the encrypted envelope prefix are left untouched,
/// ensuring repeated executions are idempotent. If plaintext data is found but no protector was supplied,
/// the migration throws <see cref="InvalidOperationException"/> so the transaction is rolled back.
/// </para>
/// </remarks>
public sealed class Migration010_EncryptPatientDnaPhi : DatabaseMigrationBase
{
    /// <summary>
    /// The patient DNA blueprints table protected by this migration.
    /// </summary>
    private const string Table = "weaver_patient_dna_records";

    /// <summary>
    /// The primary-key column used to address a row for the update.
    /// </summary>
    private const string KeyColumn = "id";

    /// <summary>
    /// Columns whose values are encrypted. Names are compile-time constants, never user input.
    /// </summary>
    private static readonly string[] Columns = ["regimen", "notes"];

    /// <summary>
    /// The protector used to encrypt existing rows, or <c>null</c> when none was supplied.
    /// </summary>
    private readonly ProfileDataProtector? _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="Migration010_EncryptPatientDnaPhi"/> class.
    /// </summary>
    /// <param name="protector">Protector holding the node PHI key, or <c>null</c> when no key is available.</param>
    public Migration010_EncryptPatientDnaPhi(ProfileDataProtector? protector = null)
    {
        _protector = protector;
    }

    /// <inheritdoc/>
    public override long Version => 10;

    /// <inheritdoc/>
    public override string Name => "010_EncryptPatientDnaPhi";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits PostgreSQL DDL widening regimen to TEXT, or empty string for SQLite.
        // WHY:  Base64-encoded AES-256-GCM envelopes exceed the original VARCHAR(64) limit.
        // HOW:  Issues ALTER TABLE ... ALTER COLUMN regimen TYPE TEXT for PostgreSQL.
        ArgumentNullException.ThrowIfNull(dialect);

        if (!dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"ALTER TABLE {Table} ALTER COLUMN regimen TYPE TEXT;";
    }

    /// <inheritdoc/>
    public override async Task UpAsync(DbConnection connection, DbTransaction? transaction, string dialect, CancellationToken ct)
    {
        // WHAT: Executes DDL changes and encrypts all existing plaintext rows.
        // WHY:  Ensures schema capacity before writing encrypted ciphertext envelopes.
        // HOW:  Runs base DDL execution, then rewrites plaintext rows in the same transaction.
        await base.UpAsync(connection, transaction, dialect, ct);
        await EncryptRowsAsync(connection, transaction, ct);
    }

    /// <summary>
    /// Encrypts every still-plaintext patient DNA regimen and notes value in place.
    /// </summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="transaction">Active migration transaction, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every row has been rewritten.</returns>
    /// <exception cref="InvalidOperationException">A value needs encrypting but no protector was supplied.</exception>
    private async Task EncryptRowsAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        // WHAT: Finds rows holding plaintext and rewrites only those with encrypted envelopes.
        // WHY:  Rows already encrypted must not be touched (idempotence), and rows are read before updating.
        // HOW:  Selects id, regimen, notes, collects pending updates, then issues parameterized updates.
        List<(string Key, string?[] Values)> pending = [];

        await using (DbCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {KeyColumn}, {string.Join(", ", Columns)} FROM {Table}";
            await using DbDataReader reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string key = reader.GetString(0);
                string?[] values = new string?[Columns.Length];
                bool changed = false;
                for (int i = 0; i < Columns.Length; i++)
                {
                    string? current = reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1);
                    if (string.IsNullOrEmpty(current) || ProfileDataProtector.IsEncrypted(current))
                    {
                        values[i] = current;
                        continue;
                    }

                    if (_protector is null)
                    {
                        throw new InvalidOperationException(
                            "Migration 010 cannot encrypt existing patient DNA data because no PHI encryption key is available. Refusing to record the migration as applied.");
                    }

                    values[i] = _protector.EncryptField(current);
                    changed = true;
                }

                if (changed)
                {
                    pending.Add((key, values));
                }
            }
        }

        foreach ((string key, string?[] values) in pending)
        {
            await using DbCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {Table} SET "
                + string.Join(", ", Columns.Select((column, i) => $"{column} = @v{i}"))
                + $" WHERE {KeyColumn} = @key";
            for (int i = 0; i < values.Length; i++)
            {
                WeaverDbParameters.AddNullable(update, $"@v{i}", values[i]);
            }

            WeaverDbParameters.Add(update, "@key", key);
            await update.ExecuteNonQueryAsync(ct);
        }
    }
}
