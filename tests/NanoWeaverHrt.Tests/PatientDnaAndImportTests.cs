using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive unit, integration, and security test suite validating patient-associated digitized DNA blueprints,
/// imported sequence files, personalized simulation/clinical results, and role-based access gating.
/// </summary>
public sealed class PatientDnaAndImportTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for test database fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance targeting isolated database directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client for API verification.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientDnaAndImportTests"/> class with isolated database fixtures.
    /// </summary>
    /// <param name="factory">The base WebApplicationFactory.</param>
    public PatientDnaAndImportTests(WebApplicationFactory<Program> factory)
    {
        // WHAT: Creates a unique temporary storage path for SQLite database testing.
        // WHY:  Ensures test isolation and prevents race conditions with other suites.
        // HOW:  Overrides TRAJECTORY_INCOMING_DIR in WebApplicationFactory configuration.
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_dna_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = _tempDataDir
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);
    }

    /// <summary>
    /// Disposes test resources and cleans up temporary database fixtures.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
        try
        {
            if (Directory.Exists(_tempDataDir))
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Verifies property assignments, factory conversions, and OpenAPI documentation examples for DNA models.
    /// </summary>
    [Fact]
    public void DnaModels_PropertyVerification_AndPackageConversions()
    {
        // WHAT: Tests constructors, conversions, and OpenAPI examples for PatientDigitalDnaRecord, ImportRecord, and ResultRecord.
        // WHY:  Ensures data consistency and accurate mapping between persistent entities and export formats.
        // HOW:  Instantiates records, executes ToPackage and FromPackage, and validates GetExamples outputs.
        PatientDigitalDnaRecord record = new()
        {
            Id = "dna_test_01",
            Username = "patient_alice",
            Title = "Hexagonal Nanotube (6HB)",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = "Anterior Pituitary Gonadotrope",
            ActiveLigand = "17β-Estradiol (E2)",
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
            TargetAptamerKdNm = 3.5,
            ScaffoldFasta = ">M13mp18\nAAGCTC...",
            GcPercentage = 42.5,
            EstimatedMwKDa = 2400.0,
            Staples = [],
            AnnealingProtocol = [],
            ProvenanceSha256 = "sha256_mock_hash",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Notes = "Custom synthesis"
        };

        PatientDigitalDnaPackage pkg = record.ToPackage();
        Assert.Equal(record.Username, pkg.PatientId);
        Assert.Equal(record.Regimen, pkg.Regimen);
        Assert.Equal(record.TargetTissue, pkg.TargetTissue);
        Assert.Equal(record.ProvenanceSha256, pkg.ProvenanceSha256);

        PatientDigitalDnaRecord convertedBack = PatientDigitalDnaRecord.FromPackage(pkg, "dna_custom_id", "Custom Title", "Custom Notes");
        Assert.Equal("dna_custom_id", convertedBack.Id);
        Assert.Equal("Custom Title", convertedBack.Title);
        Assert.Equal("Custom Notes", convertedBack.Notes);
        Assert.Equal(pkg.PatientId, convertedBack.Username);

        // Default ID and title path in FromPackage
        PatientDigitalDnaRecord defaultFromPkg = PatientDigitalDnaRecord.FromPackage(pkg);
        Assert.StartsWith("dna_", defaultFromPkg.Id);
        Assert.Contains(pkg.NanocarrierGeometry, defaultFromPkg.Title);
        Assert.Throws<ArgumentNullException>(() => PatientDigitalDnaRecord.FromPackage(null!));

        // OpenAPI examples
        Assert.NotNull(record.GetExamples());

        // Import Record
        PatientDnaImportRecord importRecord = new()
        {
            Id = "imp_01",
            Username = "patient_alice",
            FileName = "sequence.fasta",
            ImportType = "FASTA",
            SequenceOrData = ">Seq\nATCG",
            SizeBytes = 10,
            Sha256Checksum = "checksum123",
            ImportedBy = "researcher_01",
            Status = "Processed",
            MetadataJson = "{\"meta\":\"val\"}",
            ImportedAtUtc = DateTimeOffset.UtcNow
        };
        Assert.Equal("imp_01", importRecord.Id);
        Assert.NotNull(importRecord.GetExamples());

        // Result Record & Metrics
        PatientResultRecord resultRecord = new()
        {
            Id = "res_01",
            Username = "patient_alice",
            ResultType = "EndocrineKinetics",
            Title = "Target Kinetics",
            Status = "Optimal",
            MetricsJson = "{\"TargetE2\":\"650.0\",\"Kd\":\"3.5\"}",
            Summary = "Optimal steady state",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var metrics = resultRecord.GetMetrics();
        Assert.Equal("650.0", metrics["TargetE2"]);
        Assert.Equal("3.5", metrics["Kd"]);

        // Empty & corrupted metrics JSON
        PatientResultRecord emptyMetricsRes = new() { MetricsJson = "" };
        Assert.Empty(emptyMetricsRes.GetMetrics());
        PatientResultRecord invalidMetricsRes = new() { MetricsJson = "{corrupted_json" };
        Assert.Empty(invalidMetricsRes.GetMetrics());
        PatientResultRecord nullLiteralMetricsRes = new() { MetricsJson = "null" };
        Assert.Empty(nullLiteralMetricsRes.GetMetrics());

        Assert.NotNull(resultRecord.GetExamples());

        // DTO Requests
        CreatePatientDnaRequest dnaReq = new() { Title = "Req" };
        Assert.NotNull(dnaReq.GetExamples());
        CreatePatientImportRequest impReq = new() { FileName = "test.fa", SequenceOrData = "ATCG" };
        Assert.NotNull(impReq.GetExamples());
        CreatePatientResultRequest resReq = new() { Title = "Res", ResultType = "EndocrineKinetics", Summary = "Sum" };
        Assert.NotNull(resReq.GetExamples());
    }

    /// <summary>
    /// Verifies factory conversions from genomic, endocrine, and fabrication packages into patient result records.
    /// </summary>
    [Fact]
    public void ResultRecord_FactoryConversions_HandleVariousProfiles()
    {
        // WHAT: Tests PatientResultRecord.FromGenomicProfile, FromEndocrineProfile, and FromFabricationPackage.
        // WHY:  Ensures analytical evaluations map into persistent clinical result records with correct status and metrics.
        // HOW:  Synthesizes test profiles with and without critical alerts, verifying status classifications.
        GeneticAlert criticalAlert = GeneticAlert.CreateExample();

        PatientGenomicProfile criticalProfile = new()
        {
            PatientId = "alice",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [],
            Alerts = [criticalAlert],
            ThrombosisRiskIndex = 4.5,
            OverallClearanceMultiplier = 1.2,
            CpGMethylationMandatory = true,
            RecommendedMaxEstradiolPmolL = 400.0,
            RecommendedMinTestosteroneNmolL = null,
            RecommendedKdOffsetNanoMolar = 0.5,
            CalculatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        PatientResultRecord criticalRes = PatientResultRecord.FromGenomicProfile(criticalProfile, "imp_123");
        Assert.Equal("ElevatedRisk", criticalRes.Status);
        Assert.Equal("imp_123", criticalRes.ImportId);
        Assert.Contains("Identified 1 clinical pharmacogenomic alert(s)", criticalRes.Summary);

        // Optimal genomic profile (0 alerts)
        PatientGenomicProfile optimalProfile = new()
        {
            PatientId = "bob",
            Regimen = TherapeuticRegimen.FemaleToMale,
            ObservedMarkers = [],
            Alerts = [],
            ThrombosisRiskIndex = 1.0,
            OverallClearanceMultiplier = 1.0,
            CpGMethylationMandatory = false,
            RecommendedMaxEstradiolPmolL = null,
            RecommendedMinTestosteroneNmolL = 15.0,
            RecommendedKdOffsetNanoMolar = 0.0,
            CalculatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        PatientResultRecord optimalRes = PatientResultRecord.FromGenomicProfile(optimalProfile);
        Assert.Equal("Optimal", optimalRes.Status);

        // Endocrine profile
        EndocrineTargetProfile endocrineProfile = new()
        {
            PatientId = "alice",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetState = ToggleState.EstrogenicDominant,
            TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
            TargetEstradiolPmolL = 650.0,
            TargetTestosteroneNmolL = 1.2,
            BindingAffinityKdTargetNanoMolar = 3.5,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        PatientResultRecord endocrineRes = PatientResultRecord.FromEndocrineProfile(endocrineProfile, "alice", "dna_999");
        Assert.Equal("Optimal", endocrineRes.Status);
        Assert.Equal("dna_999", endocrineRes.DnaRecordId);
        Assert.Equal("650.0", endocrineRes.GetMetrics()["TargetE2PmolL"]);

        // Fabrication package
        FabricationService fabService = new();
        FabricationPackage fabPkg = fabService.GenerateFabricationPackage(fabService.GetPresetSamples()[0].Id, TherapeuticRegimen.MaleToFemale, "Hexagonal Nanotube (6HB)", 3.5);
        Assert.NotNull(fabPkg.GetExamples());
        PatientResultRecord fabRes = PatientResultRecord.FromFabricationPackage(fabPkg, "alice", "dna_888");
        Assert.Equal("Complete", fabRes.Status);
        Assert.Equal(fabPkg.StapleOligos.Count.ToString(), fabRes.GetMetrics()["StapleCount"]);
        Assert.Equal(fabPkg.FoldingBufferRecipe, fabRes.GetMetrics()["BufferRecipe"]);
    }

    /// <summary>
    /// Verifies SQL generation and schema properties in Migration004 for both PostgreSQL and SQLite.
    /// </summary>
    [Fact]
    public void Migration004_BothDialects_GeneratesValidDdl()
    {
        // WHAT: Tests Migration004_PatientDnaImportsAndResults version, name, and SQL generation.
        // WHY:  Guarantees DDL statements contain valid data types and constraints for both database engines.
        // HOW:  Validates PostgreSQL and SQLite SQL strings, and asserts ArgumentNullException on null dialect.
        Migration004_PatientDnaImportsAndResults m4 = new();
        Assert.Equal(4, m4.Version);
        Assert.Equal("004_PatientDnaImportsAndResults", m4.Name);

        string pgSql = m4.GetSql("postgresql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_dna_records", pgSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_dna_imports", pgSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_results", pgSql);
        Assert.Contains("DOUBLE PRECISION", pgSql);
        Assert.Contains("BIGINT", pgSql);

        string sqliteSql = m4.GetSql("sqlite");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_dna_records", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_dna_imports", sqliteSql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_patient_results", sqliteSql);
        Assert.Contains("REAL NOT NULL", sqliteSql);
        Assert.Contains("INTEGER NOT NULL", sqliteSql);

        Assert.Throws<ArgumentNullException>(() => m4.GetSql(null!));
    }

    /// <summary>
    /// Verifies full persistent lifecycle of DNA blueprints, imported sequence files, and results on SQLite.
    /// </summary>
    [Fact]
    public async Task PatientDnaRepository_FullLifecycle_Succeeds()
    {
        // WHAT: Performs end-to-end CRUD operations on PatientDnaRepository across DNA records, imports, and results.
        // WHY:  Ensures SQLite persistence, foreign key relationships, and query ordering behave accurately.
        // HOW:  Initializes UserRepository and PatientDnaRepository on a temporary SQLite database.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_repo_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = tempDir
                })
                .Build();

            SystemConfigurationStore configStore = new(config);
            DatabaseMigrationRunner migrationRunner = new();
            UserRepository userRepo = new(config, configStore, migrationRunner);
            await userRepo.InitializeAsync();

            string username = "patient_charlie";
            await userRepo.CreateUserAsync(new UserAccount
            {
                Username = username,
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEA1111111111111111111111111111111111111111111="
            });

            using PatientDnaRepository repo = new(config, configStore, migrationRunner);

            // 1. DNA Records CRUD
            var initialDna = await repo.GetDnaRecordsAsync(username);
            Assert.Empty(initialDna);

            PatientDigitalDnaRecord dnaRecord = new()
            {
                Id = "dna_charlie_01",
                Username = username,
                Title = "Hexagonal Nanotube 6HB - Test",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = "Pituitary Gonadotrope",
                ActiveLigand = "17β-Estradiol",
                NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
                TargetAptamerKdNm = 3.5,
                ScaffoldFasta = ">M13\nAAGCTC",
                GcPercentage = 42.5,
                EstimatedMwKDa = 2400.0,
                Staples = [StapleOligoRecord.CreateExample()],
                AnnealingProtocol = [AnnealingRampStep.CreateExample()],
                ProvenanceSha256 = "sha256_mock_1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Notes = "Initial design"
            };

            await repo.UpsertDnaRecordAsync(dnaRecord);

            var fetchedDnaList = await repo.GetDnaRecordsAsync(username);
            Assert.Single(fetchedDnaList);
            Assert.Equal("dna_charlie_01", fetchedDnaList[0].Id);
            Assert.Single(fetchedDnaList[0].Staples);
            Assert.Single(fetchedDnaList[0].AnnealingProtocol);

            var fetchedById = await repo.GetDnaRecordByIdAsync("dna_charlie_01");
            Assert.NotNull(fetchedById);
            Assert.Equal("Hexagonal Nanotube 6HB - Test", fetchedById.Title);

            var missingDna = await repo.GetDnaRecordByIdAsync("dna_nonexistent");
            Assert.Null(missingDna);

            // Update DNA record
            PatientDigitalDnaRecord updatedDna = dnaRecord with { Title = "Updated Title", Notes = null };
            await repo.UpsertDnaRecordAsync(updatedDna);
            var reFetched = await repo.GetDnaRecordByIdAsync("dna_charlie_01");
            Assert.NotNull(reFetched);
            Assert.Equal("Updated Title", reFetched.Title);
            Assert.Null(reFetched.Notes);

            // 2. Imports CRUD
            var initialImports = await repo.GetImportsAsync(username);
            Assert.Empty(initialImports);

            PatientDnaImportRecord importRecord = new()
            {
                Id = "imp_charlie_01",
                Username = username,
                FileName = "test_scaffold.fasta",
                ImportType = "FASTA",
                SequenceOrData = ">Scaffold\nATCGATCG",
                SizeBytes = 18,
                Sha256Checksum = "checksum_abc",
                ImportedBy = "researcher_01",
                Status = "Processed",
                MetadataJson = "{\"origin\":\"test\"}",
                ImportedAtUtc = DateTimeOffset.UtcNow
            };

            await repo.UpsertImportAsync(importRecord);

            var fetchedImports = await repo.GetImportsAsync(username);
            Assert.Single(fetchedImports);
            Assert.Equal("imp_charlie_01", fetchedImports[0].Id);

            var fetchedImportById = await repo.GetImportByIdAsync("imp_charlie_01");
            Assert.NotNull(fetchedImportById);
            Assert.Equal("test_scaffold.fasta", fetchedImportById.FileName);

            var missingImport = await repo.GetImportByIdAsync("imp_nonexistent");
            Assert.Null(missingImport);

            // Update Import
            PatientDnaImportRecord updatedImport = importRecord with { Status = "Verified" };
            await repo.UpsertImportAsync(updatedImport);
            var reFetchedImport = await repo.GetImportByIdAsync("imp_charlie_01");
            Assert.NotNull(reFetchedImport);
            Assert.Equal("Verified", reFetchedImport.Status);

            // 3. Results CRUD
            var initialResults = await repo.GetResultsAsync(username);
            Assert.Empty(initialResults);

            PatientResultRecord resultRecord = new()
            {
                Id = "res_charlie_01",
                Username = username,
                DnaRecordId = "dna_charlie_01",
                ImportId = "imp_charlie_01",
                ResultType = "EndocrineKinetics",
                Title = "Target Steady State",
                Status = "Optimal",
                MetricsJson = "{\"E2\":\"650.0\"}",
                Summary = "Normal clearance rate",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            await repo.UpsertResultAsync(resultRecord);

            var fetchedResults = await repo.GetResultsAsync(username);
            Assert.Single(fetchedResults);
            Assert.Equal("res_charlie_01", fetchedResults[0].Id);
            Assert.Equal("dna_charlie_01", fetchedResults[0].DnaRecordId);
            Assert.Equal("imp_charlie_01", fetchedResults[0].ImportId);

            var fetchedResultById = await repo.GetResultByIdAsync("res_charlie_01");
            Assert.NotNull(fetchedResultById);
            Assert.Equal("Target Steady State", fetchedResultById.Title);

            var missingResult = await repo.GetResultByIdAsync("res_nonexistent");
            Assert.Null(missingResult);

            // Update Result
            PatientResultRecord updatedResult = resultRecord with { Title = "Updated Result Title", Status = "Caution" };
            await repo.UpsertResultAsync(updatedResult);
            var reFetchedResult = await repo.GetResultByIdAsync("res_charlie_01");
            Assert.NotNull(reFetchedResult);
            Assert.Equal("Updated Result Title", reFetchedResult.Title);
            Assert.Equal("Caution", reFetchedResult.Status);

            // Delete operations
            bool deletedResult = await repo.DeleteResultAsync("res_charlie_01");
            Assert.True(deletedResult);
            bool deletedResultMissing = await repo.DeleteResultAsync("res_charlie_01");
            Assert.False(deletedResultMissing);

            bool deletedImport = await repo.DeleteImportAsync("imp_charlie_01");
            Assert.True(deletedImport);
            bool deletedImportMissing = await repo.DeleteImportAsync("imp_charlie_01");
            Assert.False(deletedImportMissing);

            bool deletedDna = await repo.DeleteDnaRecordAsync("dna_charlie_01");
            Assert.True(deletedDna);
            bool deletedDnaMissing = await repo.DeleteDnaRecordAsync("dna_charlie_01");
            Assert.False(deletedDnaMissing);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Verifies corrupted JSON fallbacks and datetime parsing types in PatientDnaRepository.
    /// </summary>
    [Fact]
    public async Task PatientDnaRepository_CorruptedJson_AndDateTimeBranches()
    {
        // WHAT: Tests reader fallbacks when JSON columns are corrupted and exercises DateTimeOffset parser branches.
        // WHY:  Ensures malformed database records do not cause unhandled crashes during mapping.
        // HOW:  Injects invalid JSON directly into SQLite columns and queries via PatientDnaRepository.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_corrupt_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = tempDir
                })
                .Build();

            SystemConfigurationStore configStore = new(config);
            DatabaseMigrationRunner migrationRunner = new();
            UserRepository userRepo = new(config, configStore, migrationRunner);
            await userRepo.InitializeAsync();

            string username = "patient_corrupt";
            await userRepo.CreateUserAsync(new UserAccount
            {
                Username = username,
                Role = UserRole.Patient,
                AccessGate = AccessGate.ApiWeb,
                Algorithm = KeyAlgorithm.Ed25519,
                PublicKeyBase64 = "MCowBQYDK2VwAyEA2222222222222222222222222222222222222222222="
            });

            using PatientDnaRepository repo = new(config, configStore, migrationRunner);

            PatientDigitalDnaRecord dna = new()
            {
                Id = "dna_corrupt_01",
                Username = username,
                Title = "Test Corrupted",
                Regimen = TherapeuticRegimen.MaleToFemale,
                TargetTissue = "Tissue",
                ActiveLigand = "E2",
                NanocarrierGeometry = "6HB",
                TargetAptamerKdNm = 3.5,
                ScaffoldFasta = "ATCG",
                GcPercentage = 50.0,
                EstimatedMwKDa = 2000.0,
                Staples = [],
                AnnealingProtocol = [],
                ProvenanceSha256 = "sha",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await repo.UpsertDnaRecordAsync(dna);

            // Corrupt JSON columns in SQLite directly
            using (SqliteConnection conn = new($"Data Source={Path.Combine(tempDir, "weaver_config.db")}"))
            {
                await conn.OpenAsync();
                using var cmd = new SqliteCommand("UPDATE weaver_patient_dna_records SET staples_json = '{invalid_json', annealing_protocol_json = '{invalid_json' WHERE id = 'dna_corrupt_01'", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var fetched = await repo.GetDnaRecordByIdAsync("dna_corrupt_01");
            Assert.NotNull(fetched);
            Assert.Empty(fetched.Staples);
            Assert.Empty(fetched.AnnealingProtocol);

            // Test literal "null" JSON strings deserializing to null list triggering ?? [] fallback
            using (SqliteConnection conn = new($"Data Source={Path.Combine(tempDir, "weaver_config.db")}"))
            {
                await conn.OpenAsync();
                using var cmd = new SqliteCommand("UPDATE weaver_patient_dna_records SET staples_json = 'null', annealing_protocol_json = 'null' WHERE id = 'dna_corrupt_01'", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var nullJsonFetched = await repo.GetDnaRecordByIdAsync("dna_corrupt_01");
            Assert.NotNull(nullJsonFetched);
            Assert.Empty(nullJsonFetched.Staples);
            Assert.Empty(nullJsonFetched.AnnealingProtocol);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Custom test DbDataReader to exercise ParseDateTimeOffset branches.
    /// </summary>
    private sealed class MockDateTimeDataReader : DbDataReader
    {
        private readonly object?[] _values;
        public MockDateTimeDataReader(params object?[] values) => _values = values;
        public override object GetValue(int ordinal) => _values[ordinal] ?? DBNull.Value;
        public override bool IsDBNull(int ordinal) => _values[ordinal] == null || _values[ordinal] is DBNull;
        public override string GetString(int ordinal) => _values[ordinal]?.ToString() ?? string.Empty;
        public override int FieldCount => _values.Length;
        public override bool Read() => false;
        public override bool NextResult() => false;
        public override int Depth => 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override bool HasRows => false;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => DBNull.Value;
        public override bool GetBoolean(int ordinal) => false;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => ' ';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "string";
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => 0m;
        public override double GetDouble(int ordinal) => 0.0;
        public override System.Collections.IEnumerator GetEnumerator() => _values.GetEnumerator();
        public override Type GetFieldType(int ordinal) => _values[ordinal] is DateTime ? typeof(DateTime) : typeof(string);
        public override float GetFloat(int ordinal) => 0f;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 0;
        public override long GetInt64(int ordinal) => 0;
        public override string GetName(int ordinal) => $"col{ordinal}";
        public override int GetOrdinal(string name) => 0;
        public override int GetValues(object[] values) => 0;
    }

    /// <summary>
    /// Exercises all branches of ParseDateTimeOffset with various data reader types.
    /// </summary>
    [Fact]
    public void ParseDateTimeOffset_AllBranches_EvaluatedCorrectly()
    {
        // WHAT: Tests DateTimeOffset parser helper against native DateTime (Postgres) and ISO string (SQLite).
        // WHY:  Guarantees 100% branch coverage across all database reader types.
        // HOW:  Constructs MockDateTimeDataReader with DateTime and string representations.
        DateTime nowDt = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        DateTimeOffset nowDto = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        using MockDateTimeDataReader reader = new(
            nowDt,
            "2026-10-20T12:00:00Z"
        );

        // Index 0: DateTime (PostgreSQL)
        DateTimeOffset resDt = PatientDnaRepository.ParseDateTimeOffset(reader, 0);
        Assert.Equal(nowDt, resDt.UtcDateTime);

        // Index 1: ISO string (SQLite)
        DateTimeOffset resStr = PatientDnaRepository.ParseDateTimeOffset(reader, 1);
        Assert.Equal(nowDto, resStr);
    }

    /// <summary>
    /// Verifies parameter validation on PatientDnaRepository methods.
    /// </summary>
    [Fact]
    public async Task PatientDnaRepository_ArgumentValidation_RejectsInvalidInputs()
    {
        // WHAT: Asserts ArgumentNullException and ArgumentException on invalid null/empty arguments.
        // WHY:  Maintains API robustness and prevents unexpected SQL errors.
        // HOW:  Invokes repository methods with null arguments.
        var config = new ConfigurationBuilder().Build();
        SystemConfigurationStore store = new(config);
        using PatientDnaRepository repo = new(config, store);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetDnaRecordsAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetDnaRecordByIdAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertDnaRecordAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteDnaRecordAsync(""));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetImportsAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetImportByIdAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertImportAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteImportAsync(""));

        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetResultsAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetResultByIdAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertResultAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteResultAsync(""));
    }

    /// <summary>
    /// Verifies constructor branches, null checks, disposal, and PostgreSQL fallback paths in <see cref="PatientDnaRepository"/>.
    /// </summary>
    [Fact]
    public async Task PatientDnaRepository_PostgresFallback_AndConstructorBranches()
    {
        // WHAT: Tests constructor parameter validation, disposal, and PostgreSQL fallback on failure.
        // WHY:  Ensures full branch coverage for connection setup and resilience across environments.
        // HOW:  Validates ArgumentNullException on nulls and uses MockPostgresConfigRepo to trigger fallback.
        var config = new ConfigurationBuilder().Build();
        SystemConfigurationStore configStore = new(config);

        Assert.Throws<ArgumentNullException>(() => new PatientDnaRepository(null!, configStore));
        Assert.Throws<ArgumentNullException>(() => new PatientDnaRepository(config, null!));

        // Constructor with explicit runner and dispose
        using DatabaseMigrationRunner explicitRunner = new();
        PatientDnaRepository repoWithRunner = new(config, configStore, explicitRunner);
        Assert.NotNull(repoWithRunner);
        repoWithRunner.Dispose();

        // Constructor with empty configuration
        var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using PatientDnaRepository repoEmptyConfig = new(emptyConfig, configStore);
        Assert.NotNull(repoEmptyConfig);

        // Test fallback when Postgres backend is reported
        MockPostgresConfigRepo mockPgRepo = new();
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_dna_pg_fb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var pgConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = tempDir,
                    ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54398;Database=nonexistent;Timeout=1"
                })
                .Build();

            using PatientDnaRepository pgFallbackRepo = new(pgConfig, mockPgRepo);
            var list = await pgFallbackRepo.GetDnaRecordsAsync("some_user");
            Assert.Empty(list);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Verifies REST API endpoints for DNA blueprints, sequence imports, and results with role-based access gating.
    /// </summary>
    [Fact]
    public async Task RestApi_PatientDnaImportsAndResults_EnforcesAuthAndExecutesCrud()
    {
        // WHAT: Tests REST API endpoints for DNA blueprints, imports, and results with Administrator and Patient tokens.
        // WHY:  Validates end-to-end integration across HTTP middleware, role gating, and database persistence.
        // HOW:  Generates tokens for Administrator and Patient accounts, executes HTTP operations, and asserts status codes.
        TestFixturesHelper.ConfigureAuthenticatedClient(_client, _factory);

        using var scope = _factory.Services.CreateScope();
        IUserRepository userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IUserAuthenticationService authService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        // Create test patient and test researcher
        string patientUser = "patient_dana";
        UserAccount patientAccount = new()
        {
            Username = patientUser,
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA3333333333333333333333333333333333333333333="
        };
        await userRepo.CreateUserAsync(patientAccount);

        string otherPatientUser = "patient_eve";
        await userRepo.CreateUserAsync(new UserAccount
        {
            Username = otherPatientUser,
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA4444444444444444444444444444444444444444444="
        });

        string patientToken = authService.GenerateBearerToken(patientAccount);

        using HttpClient patientClient = _factory.CreateClient();
        patientClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);

        // 1. DNA Blueprints
        // Patient can view own DNA (empty initially)
        var ownDnaRes = await patientClient.GetAsync($"/api/patients/{patientUser}/dna");
        Assert.Equal(HttpStatusCode.OK, ownDnaRes.StatusCode);
        var ownDna = await ownDnaRes.Content.ReadFromJsonAsync<List<PatientDigitalDnaRecord>>();
        Assert.NotNull(ownDna);
        Assert.Empty(ownDna);

        // Patient CANNOT view other patient's DNA, and the rejection is masked as 404 Not Found so that
        // another patient's records are indistinguishable from a subject that does not exist (Directive 12).
        var forbiddenDnaRes = await patientClient.GetAsync($"/api/patients/{otherPatientUser}/dna");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenDnaRes.StatusCode);

        // Admin can create DNA blueprint for patient
        CreatePatientDnaRequest createDnaReq = new()
        {
            Title = "Hexagonal Nanotube 6HB - Dana",
            Regimen = TherapeuticRegimen.MaleToFemale,
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
            TargetAptamerKdNm = 3.5,
            Notes = "Custom dosage"
        };
        var createDnaRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/dna", createDnaReq);
        Assert.Equal(HttpStatusCode.Created, createDnaRes.StatusCode);
        PatientDigitalDnaRecord? createdDna = await createDnaRes.Content.ReadFromJsonAsync<PatientDigitalDnaRecord>();
        Assert.NotNull(createdDna);
        Assert.Equal(patientUser, createdDna.Username);
        Assert.Equal("Hexagonal Nanotube 6HB - Dana", createdDna.Title);

        // Nonexistent user returns 404
        var notFoundDnaRes = await _client.PostAsJsonAsync("/api/patients/nonexistent_user/dna", createDnaReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundDnaRes.StatusCode);

        // Get by ID
        var getDnaByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/dna/{createdDna.Id}");
        Assert.Equal(HttpStatusCode.OK, getDnaByIdRes.StatusCode);

        var missingDnaByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/dna/dna_missing");
        Assert.Equal(HttpStatusCode.NotFound, missingDnaByIdRes.StatusCode);

        // 2. DNA & Sequence Imports
        CreatePatientImportRequest importReq = new()
        {
            FileName = "patient_scaffold.fasta",
            ImportType = "FASTA",
            SequenceOrData = ">PatientScaffold\nAAGCTCAGAGACCTCAGGAG",
            AutoAnalyze = true
        };
        var importRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/imports", importReq);
        Assert.Equal(HttpStatusCode.Created, importRes.StatusCode);
        PatientDnaImportRecord? createdImport = await importRes.Content.ReadFromJsonAsync<PatientDnaImportRecord>();
        Assert.NotNull(createdImport);
        Assert.Equal("patient_scaffold.fasta", createdImport.FileName);

        // Validation failures on empty file/sequence
        var emptyFileRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/imports", new CreatePatientImportRequest
        {
            FileName = "",
            SequenceOrData = "ATCG"
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyFileRes.StatusCode);

        var emptySeqRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/imports", new CreatePatientImportRequest
        {
            FileName = "test.fa",
            SequenceOrData = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptySeqRes.StatusCode);

        // Patient can view own imports
        var getImportsRes = await patientClient.GetAsync($"/api/patients/{patientUser}/imports");
        Assert.Equal(HttpStatusCode.OK, getImportsRes.StatusCode);
        var importsList = await getImportsRes.Content.ReadFromJsonAsync<List<PatientDnaImportRecord>>();
        Assert.NotNull(importsList);
        Assert.Single(importsList);

        // Get import by ID
        var getImportByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/imports/{createdImport.Id}");
        Assert.Equal(HttpStatusCode.OK, getImportByIdRes.StatusCode);

        var missingImportByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/imports/imp_missing");
        Assert.Equal(HttpStatusCode.NotFound, missingImportByIdRes.StatusCode);

        // 3. Results & Clinical Findings
        // Automated results were created by both the DNA creation and the AutoAnalyze import!
        var getResultsRes = await patientClient.GetAsync($"/api/patients/{patientUser}/results");
        Assert.Equal(HttpStatusCode.OK, getResultsRes.StatusCode);
        var resultsList = await getResultsRes.Content.ReadFromJsonAsync<List<PatientResultRecord>>();
        Assert.NotNull(resultsList);
        Assert.True(resultsList.Count >= 2);

        // Filter by resultType
        var filterResultsRes = await patientClient.GetAsync($"/api/patients/{patientUser}/results?resultType=Pharmacogenomics");
        Assert.Equal(HttpStatusCode.OK, filterResultsRes.StatusCode);
        var filteredList = await filterResultsRes.Content.ReadFromJsonAsync<List<PatientResultRecord>>();
        Assert.NotNull(filteredList);
        Assert.All(filteredList, r => Assert.Equal("Pharmacogenomics", r.ResultType));

        // Create custom result
        CreatePatientResultRequest customResultReq = new()
        {
            Title = "Manual Trajectory Analysis",
            ResultType = "SimulationTrajectory",
            Status = "Optimal",
            MetricsJson = "{\"Yield\":\"98%\"}",
            Summary = "High-stability origami conformation"
        };
        var createResultRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/results", customResultReq);
        Assert.Equal(HttpStatusCode.Created, createResultRes.StatusCode);
        PatientResultRecord? createdResult = await createResultRes.Content.ReadFromJsonAsync<PatientResultRecord>();
        Assert.NotNull(createdResult);

        // Validation error on empty fields
        var badResultRes = await _client.PostAsJsonAsync($"/api/patients/{patientUser}/results", new CreatePatientResultRequest
        {
            Title = "",
            ResultType = "",
            Summary = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, badResultRes.StatusCode);

        // Get result by ID
        var getResultByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/results/{createdResult.Id}");
        Assert.Equal(HttpStatusCode.OK, getResultByIdRes.StatusCode);

        var missingResultByIdRes = await patientClient.GetAsync($"/api/patients/{patientUser}/results/res_missing");
        Assert.Equal(HttpStatusCode.NotFound, missingResultByIdRes.StatusCode);

        // 4. Deletions
        var deleteResultRes = await _client.DeleteAsync($"/api/patients/{patientUser}/results/{createdResult.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResultRes.StatusCode);
        var deleteResultNotFound = await _client.DeleteAsync($"/api/patients/{patientUser}/results/{createdResult.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResultNotFound.StatusCode);

        var deleteImportRes = await _client.DeleteAsync($"/api/patients/{patientUser}/imports/{createdImport.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteImportRes.StatusCode);
        var deleteImportNotFound = await _client.DeleteAsync($"/api/patients/{patientUser}/imports/{createdImport.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteImportNotFound.StatusCode);

        var deleteDnaRes = await _client.DeleteAsync($"/api/patients/{patientUser}/dna/{createdDna.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteDnaRes.StatusCode);
        var deleteDnaNotFound = await _client.DeleteAsync($"/api/patients/{patientUser}/dna/{createdDna.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteDnaNotFound.StatusCode);
    }
}
