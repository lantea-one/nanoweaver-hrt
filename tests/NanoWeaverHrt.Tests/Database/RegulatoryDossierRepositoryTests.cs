using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying <see cref="Migration024_RegulatoryDossierArchives"/>,
/// <see cref="RegulatoryDossierRepository"/>, persistence model example providers, and GDPR Article 17 erasure cascades.
/// </summary>
public sealed class RegulatoryDossierRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test instance.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegulatoryDossierRepositoryTests"/> class.
    /// </summary>
    public RegulatoryDossierRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 24 schema migrations applied.
        // WHY:  Prevents pooled SQLite handle contention across parallel repository tests.
        // HOW:  Creates a unique temporary folder and initializes WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_dossier_repo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();
        _factory = WeaverTestDb.CreateFactory(config);
    }

    /// <summary>
    /// Verifies <see cref="Migration024_RegulatoryDossierArchives"/> emits valid DDL across all 4 dialects.
    /// </summary>
    [Fact]
    public void Migration024_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        // WHAT: Validates Migration 024 metadata, null argument guard, and DDL across PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite.
        // WHY:  MySQL requires LONGTEXT/TINYINT(1) and non-IF-NOT-EXISTS index creation while PostgreSQL/DuckDB use native BOOLEAN.
        // HOW:  Calls GetSql for each supported dialect and checks expected type and index syntax.
        Migration024_RegulatoryDossierArchives migration = new();
        Assert.Equal(24, migration.Version);
        Assert.Equal("024_RegulatoryDossierArchives", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);
        Assert.Contains("BOOLEAN", pg, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_patient", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);
        Assert.Contains("BOOLEAN", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("LONGTEXT", mysql, StringComparison.Ordinal);
        Assert.Contains("TINYINT(1)", mysql, StringComparison.Ordinal);
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("LONGTEXT", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("INTEGER NOT NULL", sqlite, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_framework", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies OpenAPI example providers on <see cref="RegulatoryDossierRecord"/>.
    /// </summary>
    [Fact]
    public void PersistenceModel_GetExamples_ReturnsPopulatedRecord()
    {
        // WHAT: Exercises IExamplesProvider<RegulatoryDossierRecord>.GetExamples.
        // WHY:  Ensures OpenAPI documentation renders realistic dossier metadata with 100% coverage.
        // HOW:  Invokes GetExamples and asserts identifier, framework, readiness, and score fields.
        RegulatoryDossierRecord ex = new RegulatoryDossierRecord().GetExamples();
        Assert.Equal("ind_20261124_001", ex.DossierId);
        Assert.Equal(RegulatorySubmissionFramework.FdaInd, ex.SubmissionFramework);
        Assert.True(ex.IsSubmissionReady);
        Assert.Equal(100.0, ex.CompletenessScore);
    }

    /// <summary>
    /// Verifies constructor and method validation guards on <see cref="RegulatoryDossierRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_ArgumentGuards_ThrowExpectedExceptions()
    {
        // WHAT: Exercises null and whitespace validation guards across every method on RegulatoryDossierRepository.
        // WHY:  Prevents persisting incomplete dossier metadata or empty identifiers.
        // HOW:  Invokes constructor and async CRUD methods with null/blank fields and checks exceptions.
        Assert.Throws<ArgumentNullException>(() => new RegulatoryDossierRepository(null!));

        RegulatoryDossierRepository repo = new(_factory);
        RegulatoryDossierRecord valid = new RegulatoryDossierRecord().GetExamples();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SaveDossierAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { DossierId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { PatientId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { PayloadSha256 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { SignatureBase64 = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { SignerKeyId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { BundleJsonEncrypted = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveDossierAsync(valid with { CompiledBy = " " }));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetDossierByIdAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ListDossiersByPatientAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteByPatientIdAsync(" "));
    }

    /// <summary>
    /// Verifies full CRUD lifecycle, nullable lot handling, newest-first ordering, idempotent upsert overwrite,
    /// unknown enum fallback, and GDPR Article 17 erasure isolation on <see cref="RegulatoryDossierRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_SaveGetListAndDelete_WorksEndToEndAndIsolatesPatients()
    {
        // WHAT: Persists dossiers for two patients (with and without LotId, across FdaInd and EmaImpd), verifies queries and GDPR deletion.
        // WHY:  Confirms multi-dialect ordering, nullable columns, fallback enum parsing, and isolated patient erasure.
        // HOW:  Saves records, updates an existing dossier ID, exercises raw DB unknown-enum row fallback, and erases one patient.
        RegulatoryDossierRepository repo = new(_factory);
        Assert.Null(await repo.GetDossierByIdAsync("missing_dossier"));
        Assert.Empty(await repo.ListDossiersByPatientAsync("missing_patient"));

        DateTimeOffset t1 = new(2026, 11, 24, 14, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

        RegulatoryDossierRecord d1 = new RegulatoryDossierRecord().GetExamples() with
        {
            DossierId = "ind_pat1_01",
            PatientId = "pat-dos-1",
            LotId = null,
            SubmissionFramework = RegulatorySubmissionFramework.FdaInd,
            CompletenessScore = 60.0,
            IsSubmissionReady = false,
            CompiledAtUtc = t1
        };

        RegulatoryDossierRecord d2 = new RegulatoryDossierRecord().GetExamples() with
        {
            DossierId = "impd_pat1_02",
            PatientId = "pat-dos-1",
            LotId = "lot_dos_01",
            SubmissionFramework = RegulatorySubmissionFramework.EmaImpd,
            CompletenessScore = 100.0,
            IsSubmissionReady = true,
            CompiledAtUtc = t2
        };

        RegulatoryDossierRecord other = new RegulatoryDossierRecord().GetExamples() with
        {
            DossierId = "ind_pat2_01",
            PatientId = "pat-dos-2",
            LotId = "lot_dos_02",
            CompiledAtUtc = t2
        };

        await repo.SaveDossierAsync(d1);
        await repo.SaveDossierAsync(d2);
        await repo.SaveDossierAsync(other);

        RegulatoryDossierRecord? initialNullLot = await repo.GetDossierByIdAsync("ind_pat1_01");
        Assert.NotNull(initialNullLot);
        Assert.Null(initialNullLot.LotId);

        // Overwrite d1 with updated readiness and non-null LotId
        await repo.SaveDossierAsync(d1 with { CompletenessScore = 80.0, LotId = "lot_dos_01" });

        RegulatoryDossierRecord? fetched1 = await repo.GetDossierByIdAsync("ind_pat1_01");
        Assert.NotNull(fetched1);
        Assert.Equal(80.0, fetched1.CompletenessScore);
        Assert.Equal("lot_dos_01", fetched1.LotId);
        Assert.False(fetched1.IsSubmissionReady);

        IReadOnlyList<RegulatoryDossierRecord> pat1List = await repo.ListDossiersByPatientAsync("pat-dos-1");
        Assert.Equal(2, pat1List.Count);
        Assert.Equal("impd_pat1_02", pat1List[0].DossierId);
        Assert.Equal(RegulatorySubmissionFramework.EmaImpd, pat1List[0].SubmissionFramework);
        Assert.True(pat1List[0].IsSubmissionReady);
        Assert.Equal("ind_pat1_01", pat1List[1].DossierId);

        // Verify fallback when submission_framework column holds an unrecognized token
        {
            await using WeaverDbSession session = await _factory.OpenAsync();
            await using DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_regulatory_dossiers SET submission_framework = 'UNKNOWN_REG' WHERE dossier_id = 'impd_pat1_02'");
            await cmd.ExecuteNonQueryAsync();
        }

        RegulatoryDossierRecord? fallbackRow = await repo.GetDossierByIdAsync("impd_pat1_02");
        Assert.NotNull(fallbackRow);
        Assert.Equal(RegulatorySubmissionFramework.FdaInd, fallbackRow.SubmissionFramework);

        // GDPR Article 17 erasure deletes pat-dos-1 without touching pat-dos-2
        int deleted = await repo.DeleteByPatientIdAsync("pat-dos-1");
        Assert.Equal(2, deleted);
        Assert.Empty(await repo.ListDossiersByPatientAsync("pat-dos-1"));

        IReadOnlyList<RegulatoryDossierRecord> pat2List = await repo.ListDossiersByPatientAsync("pat-dos-2");
        Assert.Single(pat2List);
        Assert.Equal("ind_pat2_01", pat2List[0].DossierId);
    }

    /// <summary>
    /// Clears SQLite pools and removes the temporary database directory after each test.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
