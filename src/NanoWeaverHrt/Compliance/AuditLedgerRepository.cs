using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Relational database repository persisting FDA 21 CFR Part 11 audit ledger records
/// across SQLite and PostgreSQL backend stores.
/// </summary>
public sealed class AuditLedgerRepository : IAuditLedgerRepository
{
    /// <summary>
    /// The database connection factory resolving live SQLite or PostgreSQL connections.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLedgerRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    public AuditLedgerRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public IReadOnlyList<AuditRecord> LoadAll()
    {
        // WHAT: Queries all committed audit records ordered by ascending sequence number.
        // WHY:  Allows ComplianceAuditService to rebuild its in-memory hash chain and verify cryptographic continuity across restarts.
        // HOW:  Opens a database session, executes a SELECT, and maps rows to AuditRecord instances.
        using WeaverDbSession session = _connectionFactory.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        using DbCommand cmd = session.CreateCommand(@"
            SELECT sequence_number, timestamp_utc, previous_hash, current_hash,
                   operator_id, action, resource, details_json,
                   signature_algorithm, signer_key_id, signature_base64, signed_at_utc
            FROM weaver_audit_ledger
            ORDER BY sequence_number ASC;");
        using DbDataReader reader = cmd.ExecuteReader();

        List<AuditRecord> records = [];
        while (reader.Read())
        {
            long seq = reader.GetInt64(0);
            DateTimeOffset ts = WeaverDbValues.ReadTimestamp(reader, 1);
            string prev = reader.GetString(2);
            string curr = reader.GetString(3);
            string op = reader.GetString(4);
            string act = reader.GetString(5);
            string res = reader.GetString(6);
            string det = reader.GetString(7);
            string algo = reader.GetString(8);
            string kid = reader.GetString(9);
            string sig = reader.GetString(10);
            DateTimeOffset signedAt = WeaverDbValues.ReadTimestamp(reader, 11);

            records.Add(new AuditRecord
            {
                SequenceNumber = seq,
                TimestampUtc = ts,
                PreviousHash = prev,
                CurrentHash = curr,
                OperatorId = op,
                Action = act,
                Resource = res,
                DetailsJson = det,
                Signature = new PqcSignature
                {
                    Algorithm = algo,
                    SignerKeyId = kid,
                    SignatureBase64 = sig,
                    SignedAtUtc = signedAt
                }
            });
        }

        return records;
    }

    /// <inheritdoc/>
    public void Append(AuditRecord record)
    {
        // WHAT: Persists a signed audit record into weaver_audit_ledger.
        // WHY:  Ensures immutable, durable recording of regulatory actions without duplication on conflict.
        // HOW:  Executes INSERT ON CONFLICT (sequence_number) DO NOTHING with parameterized dialect-adapted timestamp values.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Signature);

        using WeaverDbSession session = _connectionFactory.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        using DbCommand cmd = session.CreateCommand(@"
            INSERT INTO weaver_audit_ledger (
                sequence_number, timestamp_utc, previous_hash, current_hash,
                operator_id, action, resource, details_json,
                signature_algorithm, signer_key_id, signature_base64, signed_at_utc
            ) VALUES (
                @seq, @ts, @prev, @curr,
                @op, @act, @res, @det,
                @alg, @kid, @sig, @sts
            )
            ON CONFLICT (sequence_number) DO NOTHING;");

        WeaverDbParameters.Add(cmd, "@seq", record.SequenceNumber);
        WeaverDbParameters.Add(cmd, "@ts", session.Dialect.TimestampValue(record.TimestampUtc));
        WeaverDbParameters.Add(cmd, "@prev", record.PreviousHash);
        WeaverDbParameters.Add(cmd, "@curr", record.CurrentHash);
        WeaverDbParameters.Add(cmd, "@op", record.OperatorId);
        WeaverDbParameters.Add(cmd, "@act", record.Action);
        WeaverDbParameters.Add(cmd, "@res", record.Resource);
        WeaverDbParameters.Add(cmd, "@det", record.DetailsJson);
        WeaverDbParameters.Add(cmd, "@alg", record.Signature.Algorithm);
        WeaverDbParameters.Add(cmd, "@kid", record.Signature.SignerKeyId);
        WeaverDbParameters.Add(cmd, "@sig", record.Signature.SignatureBase64);
        WeaverDbParameters.Add(cmd, "@sts", session.Dialect.TimestampValue(record.Signature.SignedAtUtc));

        cmd.ExecuteNonQuery();
    }
}
