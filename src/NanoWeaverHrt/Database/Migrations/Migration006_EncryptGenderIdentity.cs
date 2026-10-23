using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 006: encrypts the three gender-identity columns that migration 005 deliberately left readable.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> <c>weaver_user_profiles</c> holds the patient's therapeutic regimen
/// (<c>MaleToFemale</c> or <c>FemaleToMale</c>), their sex assigned at birth and their pronouns. Read together,
/// the regimen and the sex assigned at birth state that a person is transgender. That is special-category health
/// data under GDPR Article 9 and, for this patient population, a greater real-world danger if leaked than a
/// postal code. Migration 005 listed these columns as plain text on the grounds that they were low-cardinality;
/// low cardinality is not harmlessness, so this migration seals them.
/// </para>
/// <para>
/// <strong>What it does:</strong> three jobs. First, on PostgreSQL it widens <c>regimen</c>,
/// <c>gender_assigned_at_birth</c> (declared <c>VARCHAR</c>) and <c>pronouns_json</c> to <c>TEXT</c> so Base64
/// AES-256-GCM ciphertext fits. Second, on PostgreSQL it drops the column <c>DEFAULT</c> from
/// <c>gender_assigned_at_birth</c> (<c>'Female'</c>) and <c>pronouns_json</c> (<c>'[]'</c>). Decryption is strict:
/// a non-empty value that is not an encrypted envelope makes the read throw. A row written by any statement that
/// omitted those columns would silently take the plain-text default and then break on the next read, so the
/// default must not exist. Third, it rewrites every existing row in place with those columns encrypted.
/// </para>
/// <para>
/// <strong>SQLite and the default:</strong> SQLite cannot drop a column default without rebuilding the table, which
/// this migration refuses to do. The invariant is protected instead by the only writer,
/// <c>UserProfileRepository.UpsertProfileAsync</c>, which always supplies all three columns explicitly.
/// </para>
/// <para>
/// <strong>Idempotence and failure:</strong> a value already carrying the envelope prefix is left untouched, so
/// running the data step twice never double-encrypts. If a row needs encrypting and no protector was supplied the
/// migration throws and the runner rolls the transaction back; it never records itself as applied having
/// encrypted nothing.
/// </para>
/// <para>
/// <strong>Left alone on purpose:</strong> the separate <c>regimen</c> column on the patient DNA table from
/// migration 004 (it belongs to a different repository), <c>language_preference</c>, <c>username</c>, <c>id</c> and
/// the timestamps.
/// </para>
/// </remarks>
public sealed class Migration006_EncryptGenderIdentity : DatabaseMigrationBase
{
    /// <summary>
    /// The profile table this migration protects.
    /// </summary>
    private const string Table = "weaver_user_profiles";

    /// <summary>
    /// The primary-key column used to address a row for the update.
    /// </summary>
    private const string KeyColumn = "username";

    /// <summary>
    /// Columns whose values are encrypted. Names are compile-time constants, never user input.
    /// </summary>
    private static readonly string[] Columns = ["regimen", "gender_assigned_at_birth", "pronouns_json"];

    /// <summary>
    /// Columns that carried a plain-text <c>DEFAULT</c> in migration 003 and must lose it under PostgreSQL.
    /// </summary>
    private static readonly string[] DefaultedColumns = ["gender_assigned_at_birth", "pronouns_json"];

    /// <summary>
    /// The protector used to encrypt existing rows, or <c>null</c> when none was supplied.
    /// </summary>
    private readonly ProfileDataProtector? _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="Migration006_EncryptGenderIdentity"/> class.
    /// </summary>
    /// <param name="protector">Protector holding the node PHI key, or <c>null</c> when no key is available.</param>
    public Migration006_EncryptGenderIdentity(ProfileDataProtector? protector = null)
    {
        _protector = protector;
    }

    /// <inheritdoc/>
    public override long Version => 6;

    /// <inheritdoc/>
    public override string Name => "006_EncryptGenderIdentity";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits the PostgreSQL column widening and default removal, and nothing for SQLite.
        // WHY:  VARCHAR(n) rejects long ciphertext, and a plain-text DEFAULT would poison strict decryption.
        // HOW:  ALTER COLUMN ... TYPE TEXT for every encrypted column, then ALTER COLUMN ... DROP DEFAULT for
        //       the two defaulted columns. Both statements are harmless to repeat.
        ArgumentNullException.ThrowIfNull(dialect);

        if (!dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        List<string> statements = [];
        foreach (string column in Columns)
        {
            statements.Add($"ALTER TABLE {Table} ALTER COLUMN {column} TYPE TEXT");
        }

        foreach (string column in DefaultedColumns)
        {
            statements.Add($"ALTER TABLE {Table} ALTER COLUMN {column} DROP DEFAULT");
        }

        return string.Join(";\n", statements) + ";";
    }

    /// <inheritdoc/>
    public override async Task UpAsync(DbConnection connection, DbTransaction? transaction, string dialect, CancellationToken ct)
    {
        // WHAT: Widens the columns and removes defaults, then encrypts every plaintext value already stored.
        // WHY:  Columns must be wide enough before ciphertext is written into them.
        // HOW:  Runs the DDL from the base class, then walks the profile table inside the same transaction.
        await base.UpAsync(connection, transaction, dialect, ct);
        await EncryptRowsAsync(connection, transaction, ct);
    }

    /// <summary>
    /// Encrypts every still-plaintext gender-identity value in place.
    /// </summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="transaction">Active migration transaction, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every row has been rewritten.</returns>
    /// <exception cref="InvalidOperationException">A value needs encrypting but no protector was supplied.</exception>
    private async Task EncryptRowsAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
    {
        // WHAT: Finds rows holding plaintext and rewrites only those.
        // WHY:  Rows already encrypted must not be touched (idempotence), and the rows are read fully before
        //       any update because SQLite cannot update through a connection with an open reader.
        // HOW:  Selects key plus columns, collects the pending updates, closes the reader, then issues one
        //       parameterized UPDATE per changed row.
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
                            "Migration 006 cannot encrypt existing gender-identity data because no PHI encryption key is available. Refusing to record the migration as applied.");
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
