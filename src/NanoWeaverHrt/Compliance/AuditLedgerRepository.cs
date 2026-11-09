using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Relational database repository persisting FDA 21 CFR Part 11 audit ledger records
/// across SQLite, PostgreSQL, DuckDB, and MySQL backend stores.
/// </summary>
public sealed class AuditLedgerRepository : IAuditLedgerRepository
{
    /// <summary>
    /// Column projection for queries on <c>weaver_audit_ledger</c>.
    /// </summary>
    private static readonly string[] Columns =
    [
        "sequence_number", "timestamp_utc", "previous_hash", "current_hash",
        "operator_id", "action", "resource", "details_json",
        "signature_algorithm", "signer_key_id", "signature_base64", "signed_at_utc"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="Columns"/>.
    /// </summary>
    private static readonly string[] ParameterNames =
    [
        "seq", "ts", "prev", "curr",
        "op", "act", "res", "det",
        "alg", "kid", "sig", "sts"
    ];

    /// <summary>
    /// Formatted SELECT column string.
    /// </summary>
    private static readonly string ColumnList = string.Join(", ", Columns);

    /// <summary>
    /// The database connection factory resolving live database sessions.
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
        // WHAT: Synchronously queries all committed audit records ordered by ascending sequence number.
        // WHY:  Allows ComplianceAuditService to rebuild in-memory hash chain without async deadlocks.
        // HOW:  Opens a synchronous session, executes a SELECT, and maps rows via MapAuditRecordFromReader.
        using WeaverDbSession session = _connectionFactory.Open();
        using DbCommand cmd = session.CreateCommand($"SELECT {ColumnList} FROM weaver_audit_ledger ORDER BY sequence_number ASC;");
        using DbDataReader reader = cmd.ExecuteReader();

        List<AuditRecord> records = [];
        while (reader.Read())
        {
            records.Add(MapAuditRecordFromReader(reader));
        }

        return records;
    }

    /// <inheritdoc/>
    public void Append(AuditRecord record)
    {
        // WHAT: Synchronously persists a signed audit record into weaver_audit_ledger.
        // WHY:  Ensures immutable, durable recording of regulatory actions without thread-pool starvation.
        // HOW:  Builds engine-specific insert-ignore SQL via dialect, binds parameters, and executes synchronously.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Signature);

        using WeaverDbSession session = _connectionFactory.Open();
        string sql = session.Dialect.BuildInsertIgnoreSql("weaver_audit_ledger", Columns, ["sequence_number"], ParameterNames);
        using DbCommand cmd = session.CreateCommand(sql);
        BindAuditRecord(cmd, session.Dialect, record);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AuditRecord>> LoadAllAsync(CancellationToken ct = default)
    {
        // WHAT: Asynchronously queries all committed audit records ordered by ascending sequence number.
        // WHY:  Provides non-blocking stream reads for asynchronous verification loops and telemetry exports.
        // HOW:  Awaits OpenAsync, executes reader, and maps records inside an explicit block scope.
        List<AuditRecord> records = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.CreateCommand($"SELECT {ColumnList} FROM weaver_audit_ledger ORDER BY sequence_number ASC;");
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                records.Add(MapAuditRecordFromReader(reader));
            }
        }

        return records;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(AuditRecord record, CancellationToken ct = default)
    {
        // WHAT: Asynchronously persists a signed audit record into weaver_audit_ledger.
        // WHY:  Allows non-blocking audit logging from asynchronous endpoints and services.
        // HOW:  Awaits OpenAsync, creates dialect-compliant insert-ignore command, and executes non-query.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Signature);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            string sql = session.Dialect.BuildInsertIgnoreSql("weaver_audit_ledger", Columns, ["sequence_number"], ParameterNames);
            await using DbCommand cmd = session.CreateCommand(sql);
            BindAuditRecord(cmd, session.Dialect, record);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Maps a database reader row to an <see cref="AuditRecord"/> instance.
    /// </summary>
    /// <param name="reader">The active reader positioned on an audit row.</param>
    /// <returns>Hydrated <see cref="AuditRecord"/>.</returns>
    internal static AuditRecord MapAuditRecordFromReader(DbDataReader reader)
    {
        // WHAT: Hydrates an AuditRecord and its cryptographic signature from positional reader columns.
        // WHY:  Centralizes reader-mapping across synchronous and asynchronous queries.
        // HOW:  Reads sequence number, hashes, operator identity, action metadata, and nested PqcSignature.
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

        return new AuditRecord
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
        };
    }

    /// <summary>
    /// Binds audit record columns to a command using dialect-aware parameter placeholders and timestamp coercions.
    /// </summary>
    /// <param name="cmd">The command to receive parameters.</param>
    /// <param name="dialect">The SQL dialect for the target engine.</param>
    /// <param name="record">The audit record being persisted.</param>
    private static void BindAuditRecord(DbCommand cmd, ISqlDialect dialect, AuditRecord record)
    {
        // WHAT: Attaches all 12 audit record parameter values to the given command.
        // WHY:  Ensures parameter types and timestamp conversions match engine dialect expectations.
        // HOW:  Binds numeric, text, and dialect-formatted timestamp values for timestamps and signature fields.
        WeaverDbParameters.Add(cmd, "seq", record.SequenceNumber);
        WeaverDbParameters.Add(cmd, "ts", dialect.TimestampValue(record.TimestampUtc));
        WeaverDbParameters.Add(cmd, "prev", record.PreviousHash);
        WeaverDbParameters.Add(cmd, "curr", record.CurrentHash);
        WeaverDbParameters.Add(cmd, "op", record.OperatorId);
        WeaverDbParameters.Add(cmd, "act", record.Action);
        WeaverDbParameters.Add(cmd, "res", record.Resource);
        WeaverDbParameters.Add(cmd, "det", record.DetailsJson);
        WeaverDbParameters.Add(cmd, "alg", record.Signature!.Algorithm);
        WeaverDbParameters.Add(cmd, "kid", record.Signature.SignerKeyId);
        WeaverDbParameters.Add(cmd, "sig", record.Signature.SignatureBase64);
        WeaverDbParameters.Add(cmd, "sts", dialect.TimestampValue(record.Signature.SignedAtUtc));
    }
}
