using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting cold-chain thermal excursions and SHA-256 hash-chained
/// physical custody handoffs across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IColdChainCustodyRepository"/>
/// backed by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P210): Persists temperature-time histories and tamper-evident
/// custody chains deterministically across all four relational engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses explicit <c>{ await using ... }</c> block scopes, dialect-aware timestamp
/// coercion, ordinal reader extraction, and deterministic chronological sorting (<c>recorded_at_utc ASC, id ASC</c>).
/// </para>
/// </remarks>
public sealed class ColdChainCustodyRepository : IColdChainCustodyRepository
{
    /// <summary>
    /// Database connection factory supplying dialect-aware database sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColdChainCustodyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public ColdChainCustodyRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Stores the validated database connection factory.
        // WHY:  Every cold-chain query opens a short-lived session bound to the configured SQL engine.
        // HOW:  Guards against null injection before assigning the readonly field.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task AddThermalExcursionAsync(
        ThermalExcursionRecord record,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts a new cold-chain temperature-duration observation into weaver_lot_thermal_excursions.
        // WHY:  Milestone 40 (P210): Builds the persistent thermal history consumed by Arrhenius potency modeling.
        // HOW:  Validates required fields, binds dialect-neutral parameters, and executes inside an explicit block scope.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ExcursionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SensorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.RecordedBy);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                "INSERT INTO weaver_lot_thermal_excursions " +
                "(excursion_id, lot_id, patient_id, recorded_at_utc, temperature_celsius, duration_hours, storage_tier, sensor_id, notes_encrypted, recorded_by) " +
                $"VALUES ({session.Dialect.Parameter("eid")}, {session.Dialect.Parameter("lid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("rat")}, {session.Dialect.Parameter("temp")}, {session.Dialect.Parameter("dur")}, " +
                $"{session.Dialect.Parameter("tier")}, {session.Dialect.Parameter("sid")}, {session.Dialect.Parameter("notes")}, {session.Dialect.Parameter("rby")})";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "eid", record.ExcursionId);
            WeaverDbParameters.Add(cmd, "lid", record.LotId);
            WeaverDbParameters.Add(cmd, "pid", record.PatientId);
            WeaverDbParameters.Add(cmd, "rat", session.Dialect.TimestampValue(record.RecordedAtUtc));
            WeaverDbParameters.Add(cmd, "temp", record.TemperatureCelsius);
            WeaverDbParameters.Add(cmd, "dur", record.DurationHours);
            WeaverDbParameters.Add(cmd, "tier", record.StorageTier.ToString());
            WeaverDbParameters.Add(cmd, "sid", record.SensorId);
            WeaverDbParameters.AddNullable(cmd, "notes", record.NotesEncrypted);
            WeaverDbParameters.Add(cmd, "rby", record.RecordedBy);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThermalExcursionRecord>> ListThermalExcursionsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all thermal excursion observations for a lot ordered chronologically ascending.
        // WHY:  Arrhenius integration and clinical audit timelines require deterministic chronological ordering.
        // HOW:  Executes a parameterized SELECT and maps each row by ordinal index without LINQ allocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        List<ThermalExcursionRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT excursion_id, lot_id, patient_id, recorded_at_utc, temperature_celsius, duration_hours, storage_tier, sensor_id, notes_encrypted, recorded_by " +
                $"FROM weaver_lot_thermal_excursions WHERE lot_id = {session.Dialect.Parameter("lid")} ORDER BY recorded_at_utc ASC, excursion_id ASC");
            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string rawTier = reader.GetString(6);
                ThermalStorageTier tier = Enum.TryParse(rawTier, ignoreCase: true, out ThermalStorageTier parsedTier)
                    ? parsedTier
                    : ThermalStorageTier.ThermalExcursion;

                results.Add(new ThermalExcursionRecord
                {
                    ExcursionId = reader.GetString(0),
                    LotId = reader.GetString(1),
                    PatientId = reader.GetString(2),
                    RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 3),
                    TemperatureCelsius = Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
                    DurationHours = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
                    StorageTier = tier,
                    SensorId = reader.GetString(7),
                    NotesEncrypted = reader.IsDBNull(8) ? null : reader.GetString(8),
                    RecordedBy = reader.GetString(9)
                });
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task AddCustodyHandoffAsync(
        CustodyHandoffRecord record,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Appends an immutable SHA-256 hash-chained custody handoff into weaver_lot_custody_handoffs.
        // WHY:  Milestone 40 (P210): Preserves 21 CFR Part 11 physical batch custody provenance across restarts.
        // HOW:  Validates required fields, binds typed parameters, and executes inside an explicit block scope.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.HandoffId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.FromActor);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ToActor);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.FacilityCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ChainPrevSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EntrySha256);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                "INSERT INTO weaver_lot_custody_handoffs " +
                "(handoff_id, lot_id, patient_id, stage, from_actor, to_actor, facility_code, temperature_celsius, potency_percent_at_handoff, chain_prev_sha256, entry_sha256, recorded_at_utc) " +
                $"VALUES ({session.Dialect.Parameter("hid")}, {session.Dialect.Parameter("lid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("stg")}, {session.Dialect.Parameter("frm")}, {session.Dialect.Parameter("to")}, " +
                $"{session.Dialect.Parameter("fac")}, {session.Dialect.Parameter("temp")}, {session.Dialect.Parameter("pot")}, " +
                $"{session.Dialect.Parameter("prev")}, {session.Dialect.Parameter("hash")}, {session.Dialect.Parameter("rat")})";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "hid", record.HandoffId);
            WeaverDbParameters.Add(cmd, "lid", record.LotId);
            WeaverDbParameters.Add(cmd, "pid", record.PatientId);
            WeaverDbParameters.Add(cmd, "stg", record.Stage.ToString());
            WeaverDbParameters.Add(cmd, "frm", record.FromActor);
            WeaverDbParameters.Add(cmd, "to", record.ToActor);
            WeaverDbParameters.Add(cmd, "fac", record.FacilityCode);
            WeaverDbParameters.Add(cmd, "temp", record.TemperatureCelsius);
            WeaverDbParameters.Add(cmd, "pot", record.PotencyPercentAtHandoff);
            WeaverDbParameters.Add(cmd, "prev", record.ChainPrevSha256);
            WeaverDbParameters.Add(cmd, "hash", record.EntrySha256);
            WeaverDbParameters.Add(cmd, "rat", session.Dialect.TimestampValue(record.RecordedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustodyHandoffRecord>> ListCustodyHandoffsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all custody handoff entries for a lot ordered chronologically ascending.
        // WHY:  SHA-256 hash-chain verification walks entries sequentially from genesis to tail.
        // HOW:  Executes a parameterized SELECT and maps each row by ordinal index without LINQ allocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        List<CustodyHandoffRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT handoff_id, lot_id, patient_id, stage, from_actor, to_actor, facility_code, temperature_celsius, potency_percent_at_handoff, chain_prev_sha256, entry_sha256, recorded_at_utc " +
                $"FROM weaver_lot_custody_handoffs WHERE lot_id = {session.Dialect.Parameter("lid")} ORDER BY recorded_at_utc ASC, handoff_id ASC");
            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string rawStage = reader.GetString(3);
                CustodyStage stage = Enum.TryParse(rawStage, ignoreCase: true, out CustodyStage parsedStage)
                    ? parsedStage
                    : CustodyStage.WetLabRelease;

                results.Add(new CustodyHandoffRecord
                {
                    HandoffId = reader.GetString(0),
                    LotId = reader.GetString(1),
                    PatientId = reader.GetString(2),
                    Stage = stage,
                    FromActor = reader.GetString(4),
                    ToActor = reader.GetString(5),
                    FacilityCode = reader.GetString(6),
                    TemperatureCelsius = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
                    PotencyPercentAtHandoff = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
                    ChainPrevSha256 = reader.GetString(9),
                    EntrySha256 = reader.GetString(10),
                    RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 11)
                });
            }
        }

        return results;
    }
}
