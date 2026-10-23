using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 005: encrypts the patient identifiers that migration 003 stored as readable text.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Migration 003 created three tables (profiles, addresses, emergency contacts)
/// whose names, e-mail addresses, phone numbers, dates of birth, street addresses and free-text notes were all
/// stored as plain text. Only the avatar picture was encrypted. Those are exactly the identifiers that HIPAA's
/// Safe Harbor de-identification rule says must be removed before health data counts as anonymous, so anyone
/// who obtained a database file or backup could read every patient's identity.
/// </para>
/// <para>
/// <strong>What it does:</strong> two jobs. First, on PostgreSQL it widens each affected column to <c>TEXT</c>,
/// because a column declared <c>VARCHAR(50)</c> cannot hold Base64 AES-256-GCM ciphertext (a 10-character phone
/// number becomes about 90 characters). SQLite columns are already <c>TEXT</c>. Second, it rewrites every
/// existing row in place with each listed column encrypted by <see cref="ProfileDataProtector"/>.
/// </para>
/// <para>
/// <strong>Idempotence:</strong> a value that already carries the encrypted envelope prefix is left untouched, so
/// running the data step twice never double-encrypts. The migration runner additionally records version 5 and
/// will not run it again.
/// </para>
/// <para>
/// <strong>Failure is loud:</strong> if any row needs encrypting and no protector (and therefore no PHI key) was
/// supplied, the migration throws and the runner rolls the whole transaction back. It never records itself as
/// applied after encrypting nothing. When there is nothing to encrypt, no key is needed.
/// </para>
/// <para>
/// <strong>Left in plain text on purpose:</strong> <c>username</c> (foreign key with cascade delete),
/// <c>id</c>, <c>address_type</c>, <c>country</c>, <c>is_primary</c>, <c>priority_order</c>,
/// <c>can_discuss_medical_info</c>, <c>regimen</c>, <c>gender_assigned_at_birth</c>, <c>pronouns_json</c>,
/// <c>language_preference</c> and the timestamps. They are structural, low-cardinality or sort keys.
/// </para>
/// </remarks>
public sealed class Migration005_EncryptPatientPhi : DatabaseMigrationBase
{
    /// <summary>
    /// Describes one table and the key plus protected columns it holds.
    /// </summary>
    /// <param name="Table">Table name.</param>
    /// <param name="KeyColumn">Primary-key column used to address a row for the update.</param>
    /// <param name="Columns">Columns whose values are encrypted.</param>
    private sealed record ProtectedTable(string Table, string KeyColumn, string[] Columns);

    /// <summary>
    /// Every table and column this migration encrypts. Names are compile-time constants, never user input.
    /// </summary>
    private static readonly ProtectedTable[] Tables =
    [
        new("weaver_user_profiles", "username",
            ["first_name", "last_name", "preferred_name", "email_address", "phone_number", "date_of_birth", "emergency_medical_notes", "bio"]),
        new("weaver_patient_addresses", "id",
            ["address_line1", "address_line2", "city", "state_or_province", "postal_code"]),
        new("weaver_patient_contacts", "id",
            ["contact_name", "relationship", "phone_number", "email_address", "notes"])
    ];

    /// <summary>
    /// The protector used to encrypt existing rows, or <c>null</c> when none was supplied.
    /// </summary>
    private readonly ProfileDataProtector? _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="Migration005_EncryptPatientPhi"/> class.
    /// </summary>
    /// <param name="protector">Protector holding the node PHI key, or <c>null</c> when no key is available.</param>
    public Migration005_EncryptPatientPhi(ProfileDataProtector? protector = null)
    {
        _protector = protector;
    }

    /// <inheritdoc/>
    public override long Version => 5;

    /// <inheritdoc/>
    public override string Name => "005_EncryptPatientPhi";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits the PostgreSQL column widening, and nothing for SQLite.
        // WHY:  VARCHAR(n) columns reject ciphertext longer than n; SQLite's TEXT has no such limit.
        // HOW:  ALTER COLUMN ... TYPE TEXT for every encrypted column, built from the same table list the
        //       data step uses so the two can never disagree. Widening an already-TEXT column is a no-op.
        ArgumentNullException.ThrowIfNull(dialect);

        if (!dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        List<string> statements = [];
        foreach (ProtectedTable table in Tables)
        {
            foreach (string column in table.Columns)
            {
                statements.Add($"ALTER TABLE {table.Table} ALTER COLUMN {column} TYPE TEXT");
            }
        }

        return string.Join(";\n", statements) + ";";
    }

    /// <inheritdoc/>
    public override async Task UpAsync(DbConnection connection, DbTransaction? transaction, string dialect, CancellationToken ct)
    {
        // WHAT: Widens the columns, then encrypts every plaintext value already stored.
        // WHY:  Columns must be wide enough before ciphertext is written into them.
        // HOW:  Runs the DDL from the base class, then walks each protected table inside the same transaction.
        await base.UpAsync(connection, transaction, dialect, ct);

        foreach (ProtectedTable table in Tables)
        {
            await EncryptTableAsync(connection, transaction, table, ct);
        }
    }

    /// <summary>
    /// Encrypts every still-plaintext value of one table in place.
    /// </summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="transaction">Active migration transaction, if any.</param>
    /// <param name="table">The table and columns to protect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every row has been rewritten.</returns>
    /// <exception cref="InvalidOperationException">A value needs encrypting but no protector was supplied.</exception>
    private async Task EncryptTableAsync(DbConnection connection, DbTransaction? transaction, ProtectedTable table, CancellationToken ct)
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
            select.CommandText = $"SELECT {table.KeyColumn}, {string.Join(", ", table.Columns)} FROM {table.Table}";
            await using DbDataReader reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string key = reader.GetString(0);
                string?[] values = new string?[table.Columns.Length];
                bool changed = false;
                for (int i = 0; i < table.Columns.Length; i++)
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
                            "Migration 005 cannot encrypt existing patient data because no PHI encryption key is available. Refusing to record the migration as applied.");
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
            update.CommandText = $"UPDATE {table.Table} SET "
                + string.Join(", ", table.Columns.Select((column, i) => $"{column} = @v{i}"))
                + $" WHERE {table.KeyColumn} = @key";
            for (int i = 0; i < values.Length; i++)
            {
                WeaverDbParameters.AddNullable(update, $"@v{i}", values[i]);
            }

            WeaverDbParameters.Add(update, "@key", key);
            await update.ExecuteNonQueryAsync(ct);
        }
    }
}
