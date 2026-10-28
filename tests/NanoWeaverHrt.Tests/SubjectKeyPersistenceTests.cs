using System.Collections.Concurrent;
using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.ExceptionHandling;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Storage;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Verification suite for Work Package P7.4 Stage A: durable subject ML-KEM-768 key storage,
/// AES-256-GCM sealing at rest, eager fail-loud startup loading, permanent GDPR Article 17 erasure tombstones,
/// and accurate client-safe error reporting.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class SubjectKeyPersistenceTests : IDisposable
{
    /// <summary>
    /// Hex digest of the historic legacy PHI key used only to verify that subject keys refuse legacy envelopes.
    /// </summary>
    private const string LegacyPhiMasterKeyHex = "FEA47E93A869FF0D5965AB61A3E7476FE15632CE387DBFBC49CB84A3E45A2FE6";

    /// <summary>
    /// Shared PostgreSQL test environment fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// xUnit test output helper.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Isolated per-test temporary root directory.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_subj_keys_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyPersistenceTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL test fixture.</param>
    /// <param name="output">Test output sink.</param>
    public SubjectKeyPersistenceTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
    {
        _postgres = postgres;
        _output = output;
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp test directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temp test directory.
            }
        }
    }

    /// <summary>
    /// Verifies criteria A1 and A2: Migration008_SubjectKeyStore metadata, SQL dialect emission, and runner registration.
    /// </summary>
    [Fact]
    public async Task A1_A2_Migration008_MetadataAndRunnerRegistration_AreDeterministic()
    {
        // WHAT: Checks Migration008_SubjectKeyStore SQL text and DatabaseMigrationRunner execution.
        // WHY:  Guarantees version 8 is registered, idempotent, and free of destructive or trigger DDL.
        // HOW:  Inspects SQL strings for both dialects and runs the migration runner twice over SQLite.
        Migration008_SubjectKeyStore migration = new();
        Assert.Equal(8L, migration.Version);
        Assert.Equal("008_SubjectKeyStore", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string sqliteSql = migration.GetSql("sqlite");
        string pgSql = migration.GetSql("postgresql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_keys", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("ck_weaver_subject_keys_state", sqliteSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_subject_keys", pgSql, StringComparison.Ordinal);
        Assert.Contains("ck_weaver_subject_keys_state", pgSql, StringComparison.Ordinal);
        Assert.Contains("BOOLEAN", pgSql, StringComparison.Ordinal);
        Assert.Contains("TIMESTAMPTZ", pgSql, StringComparison.Ordinal);

        foreach (string sql in new[] { sqliteSql, pgSql })
        {
            Assert.DoesNotContain("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRIGGER", sql, StringComparison.OrdinalIgnoreCase);
        }

        using DatabaseMigrationRunner runner = new();
        Assert.Equal(11, runner.RegisteredMigrations.Count);
        Assert.Contains(runner.RegisteredMigrations, m => m is Migration008_SubjectKeyStore);

        await using SqliteConnection conn = new("Data Source=:memory:");
        await conn.OpenAsync();
        int firstApply = await runner.ApplyPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(11, firstApply);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, await runner.GetAppliedVersionsAsync(conn, "sqlite"));
        Assert.Equal(0, await runner.ApplyPendingMigrationsAsync(conn, "sqlite"));
    }

    /// <summary>
    /// Verifies criterion A3: the database CHECK constraint rejects active rows without key material,
    /// shredded rows with key material, and updates that attempt to un-shred a tombstone.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A3_CheckConstraint_RejectsInvalidStatesAndUnshredding(bool postgres)
    {
        using SubjectKeyTestHost? host = await CreateHostOrSkipAsync(postgres, "a3");
        if (host is null)
        {
            return;
        }

        await using WeaverDbSession session = await host.ConnectionFactory.OpenAsync();
        string falseLit = session.Dialect.FalseLiteral;
        string trueLit = session.Dialect.TrueLiteral;
        object nowParam = session.Dialect.TimestampValue(DateTimeOffset.UtcNow);

        // 1. Active row with sealed_private_key NULL
        await using (DbCommand cmd1 = session.CreateCommand(
            $"INSERT INTO weaver_subject_keys (subject_key, subject_id, key_id, algorithm, public_key_base64, sealed_private_key, is_shredded, created_at_utc, shredded_at_utc) VALUES ('S1', 's1', 'k1', 'ML-KEM-768', 'pub', NULL, {falseLit}, @t, NULL)"))
        {
            WeaverDbParameters.Add(cmd1, "@t", nowParam);
            await Assert.ThrowsAnyAsync<DbException>(() => cmd1.ExecuteNonQueryAsync());
        }

        // 2. Shredded row with sealed_private_key non-NULL
        await using (DbCommand cmd2 = session.CreateCommand(
            $"INSERT INTO weaver_subject_keys (subject_key, subject_id, key_id, algorithm, public_key_base64, sealed_private_key, is_shredded, created_at_utc, shredded_at_utc) VALUES ('S2', 's2', 'k2', 'ML-KEM-768', NULL, 'sealed', {trueLit}, @t, @t)"))
        {
            WeaverDbParameters.Add(cmd2, "@t", nowParam);
            await Assert.ThrowsAnyAsync<DbException>(() => cmd2.ExecuteNonQueryAsync());
        }

        // 3. Insert valid tombstone, then attempt UPDATE setting is_shredded = FALSE/0
        await using (DbCommand insertTombstone = session.CreateCommand(
            $"INSERT INTO weaver_subject_keys (subject_key, subject_id, key_id, algorithm, public_key_base64, sealed_private_key, is_shredded, created_at_utc, shredded_at_utc) VALUES ('S3', 's3', 'k3', 'ML-KEM-768', NULL, NULL, {trueLit}, @t, @t)"))
        {
            WeaverDbParameters.Add(insertTombstone, "@t", nowParam);
            await insertTombstone.ExecuteNonQueryAsync();
        }

        await using (DbCommand revertCmd = session.CreateCommand(
            $"UPDATE weaver_subject_keys SET is_shredded = {falseLit} WHERE subject_key = 'S3'"))
        {
            await Assert.ThrowsAnyAsync<DbException>(() => revertCmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>
    /// Verifies criteria A4, A5, A6, A7, and A8: repository insert conflict handling, tombstone non-resurrection,
    /// MarkShreddedAsync semantics, source text invariants, and full column round-tripping.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A4_A5_A6_A7_A8_Repository_OperationsAndRoundTrip_BehaveDeterministically(bool postgres)
    {
        Assert.Throws<ArgumentNullException>(() => new SubjectKeyRepository(null!));

        // A7 source pin
        string repoSource = await File.ReadAllTextAsync(FindRepoFile("src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyRepository.cs"));
        Assert.DoesNotContain("OR REPLACE", repoSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DO UPDATE", repoSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", repoSource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountLiteralOccurrences(repoSource, "\"VACUUM\""));

        using SubjectKeyTestHost? host = await CreateHostOrSkipAsync(postgres, "a4_a8");
        if (host is null)
        {
            return;
        }

        SubjectKeyRepository repo = host.InnerRepository;
        DateTimeOffset t1 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = new(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);

        SubjectKeyRow activeRow = new()
        {
            SubjectKey = "SUB_ALPHA",
            SubjectId = "sub_Alpha",
            KeyId = "kid_alpha",
            Algorithm = "ML-KEM-768",
            PublicKeyBase64 = "pub_alpha",
            SealedPrivateKey = "sealed_alpha",
            IsShredded = false,
            CreatedAtUtc = t1,
            ShreddedAtUtc = null
        };

        // Argument validation on TryInsertActiveAsync
        await Assert.ThrowsAsync<ArgumentException>(() => repo.TryInsertActiveAsync(activeRow with { IsShredded = true }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.TryInsertActiveAsync(activeRow with { KeyId = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.TryInsertActiveAsync(activeRow with { PublicKeyBase64 = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.TryInsertActiveAsync(activeRow with { SealedPrivateKey = null }));

        // A4: first insert true, second insert false and row unchanged
        Assert.True(await repo.TryInsertActiveAsync(activeRow));
        Assert.False(await repo.TryInsertActiveAsync(activeRow with { PublicKeyBase64 = "pub_different", SealedPrivateKey = "sealed_different" }));
        SubjectKeyRow loadedActive = await repo.GetRequiredAsync("SUB_ALPHA");
        Assert.Equal(activeRow, loadedActive);

        // A5: MarkShreddedAsync returns true on active row, false on tombstone and on absent key
        Assert.False(await repo.MarkShreddedAsync("NEVER_EXISTED", t2));
        Assert.True(await repo.MarkShreddedAsync("SUB_ALPHA", t2));
        Assert.False(await repo.MarkShreddedAsync("SUB_ALPHA", t2.AddMinutes(5)));

        SubjectKeyRow loadedTombstone = await repo.GetRequiredAsync("SUB_ALPHA");
        Assert.True(loadedTombstone.IsShredded);
        Assert.Null(loadedTombstone.PublicKeyBase64);
        Assert.Null(loadedTombstone.SealedPrivateKey);
        Assert.Equal(t2, loadedTombstone.ShreddedAtUtc);

        // A4: TryInsertActiveAsync on a tombstone returns false and leaves tombstone untouched
        Assert.False(await repo.TryInsertActiveAsync(activeRow));
        SubjectKeyRow stillTombstone = await repo.GetRequiredAsync("SUB_ALPHA");
        Assert.True(stillTombstone.IsShredded);
        Assert.Null(stillTombstone.PublicKeyBase64);
        Assert.Null(stillTombstone.SealedPrivateKey);

        // A8: Insert a second row (active, properly sealed) and a third row with null key_id (tombstone) to test ordering and null key_id mapping
        string secondBinding = SubjectKeyVault.ComputeBinding("SUB_AA", "kid_alpha", "pub_alpha");
        SubjectKeyRow secondRow = activeRow with
        {
            SubjectKey = "SUB_AA",
            SubjectId = "sub_aa",
            SealedPrivateKey = host.Protector.EncryptRequiredField($"{SubjectKeyVault.SealPrefix}{secondBinding}\npriv_alpha")
        };
        Assert.True(await repo.TryInsertActiveAsync(secondRow));

        await using (WeaverDbSession session = await host.ConnectionFactory.OpenAsync())
        {
            await using DbCommand cmd = session.CreateCommand(
                $"INSERT INTO weaver_subject_keys (subject_key, subject_id, key_id, algorithm, public_key_base64, sealed_private_key, is_shredded, created_at_utc, shredded_at_utc) VALUES ('SUB_ZZ', 'SUB_ZZ', NULL, 'ML-KEM-768', NULL, NULL, {session.Dialect.TrueLiteral}, @t, @t)");
            WeaverDbParameters.Add(cmd, "@t", session.Dialect.TimestampValue(t2));
            await cmd.ExecuteNonQueryAsync();
        }

        IReadOnlyList<SubjectKeyRow> allRows = await repo.ListAllAsync();
        Assert.Equal(3, allRows.Count);
        Assert.Equal(new[] { "SUB_AA", "SUB_ALPHA", "SUB_ZZ" }, new[] { allRows[0].SubjectKey, allRows[1].SubjectKey, allRows[2].SubjectKey });
        Assert.Null(allRows[2].KeyId);
        Assert.True(allRows[2].IsShredded);

        // Also verify ReopenAsync loads a null-KeyId tombstone cleanly
        using SubjectKeyTestHost reopened = await host.ReopenAsync();
        Assert.Equal(1, reopened.LoadReport!.ActiveCount);
        Assert.Equal(2, reopened.LoadReport.ErasedCount);

        // A6: GetRequiredAsync on absent key throws RowMissingMessage
        InvalidOperationException missingEx = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.GetRequiredAsync("MISSING_KEY"));
        Assert.Equal(SubjectKeyVault.RowMissingMessage, missingEx.Message);
    }

    /// <summary>
    /// Verifies criteria A9 and A10: at-rest AES-256-GCM sealing, raw SQLite file inspection,
    /// and DecryptRequiredFieldWithCurrentKeyOnly rejection of legacy-key and malformed inputs.
    /// </summary>
    [Fact]
    public async Task A9_A10_SealingAtRest_AndCurrentKeyOnlyDecryption_ProtectPrivateKey()
    {
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "a9_a10"));
        SubjectKeyProfile created = await host.Vault.GetOrCreateAsync("sub_seal_check");
        string privateKeyB64 = created.KeyMaterial.PrivateKeyBase64!;

        SubjectKeyRow stored = await host.InnerRepository.GetRequiredAsync("SUB_SEAL_CHECK");
        Assert.NotNull(stored.SealedPrivateKey);
        Assert.StartsWith("data:application/encrypted;base64,", stored.SealedPrivateKey, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKeyB64, stored.SealedPrivateKey, StringComparison.Ordinal);

        for (int i = 0; i + 32 <= privateKeyB64.Length; i += 32)
        {
            string slice = privateKeyB64.Substring(i, 32);
            Assert.DoesNotContain(slice, stored.SealedPrivateKey, StringComparison.Ordinal);
        }

        string dbFile = Path.Combine(host.RootDirectory, "weaver_config.db");
        ClearSqlitePool(dbFile);
        string rawDbText = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(dbFile));
        Assert.DoesNotContain(privateKeyB64, rawDbText, StringComparison.Ordinal);

        // A10: DecryptRequiredFieldWithCurrentKeyOnly
        ProfileDataProtector protector = host.Protector;
        string sampleSecret = "sensitive_subject_secret_payload_12345";
        string encrypted = protector.EncryptRequiredField(sampleSecret);
        Assert.Equal(sampleSecret, protector.DecryptRequiredFieldWithCurrentKeyOnly(encrypted));

        Assert.Throws<ArgumentNullException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly(null!));

        foreach (string badInput in new[]
        {
            "plaintext_not_envelope_value",
            string.Empty,
            "data:application/encrypted;base64,!!!not_base64!!!",
            "data:application/encrypted;base64," + Convert.ToBase64String(new byte[16])
        })
        {
            CryptographicException ex = Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly(badInput));
            if (badInput.Length > 0)
            {
                Assert.DoesNotContain(badInput, ex.Message, StringComparison.Ordinal);
            }
        }

        string legacyEnvelope = EncryptWithLegacyKey(sampleSecret);
        CryptographicException legacyEx = Assert.Throws<CryptographicException>(() => protector.DecryptRequiredFieldWithCurrentKeyOnly(legacyEnvelope));
        Assert.DoesNotContain(legacyEnvelope, legacyEx.Message, StringComparison.Ordinal);
        Assert.Equal(0, protector.LegacyDecryptionCount);
    }

    /// <summary>
    /// Verifies criteria A11, A13, A14, and A15: key persistence across restarts, case-insensitivity,
    /// erasure semantics, and erasure survival across restarts on both database backends.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A11_A13_A14_A15_PersistenceCaseInsensitivityAndErasure_SurviveRestart(bool postgres)
    {
        using SubjectKeyTestHost? host1 = await CreateHostOrSkipAsync(postgres, "a11_a15");
        if (host1 is null)
        {
            return;
        }

        // A13: Case-insensitivity
        SubjectKeyProfile p1 = await host1.Vault.GetOrCreateAsync("Sub_X");
        SubjectKeyProfile p2 = await host1.Vault.GetOrCreateAsync("sub_x");
        Assert.Equal(p1.KeyMaterial.PublicKeyBase64, p2.KeyMaterial.PublicKeyBase64);

        SubjectKeyRow rowX = await host1.InnerRepository.GetRequiredAsync("SUB_X");
        Assert.Equal("SUB_X", rowX.SubjectKey);
        Assert.Equal("Sub_X", rowX.SubjectId);

        byte[] samplePlaintext = Encoding.UTF8.GetBytes("estradiol-cypionate-depot-kinetics");
        EncryptedEnvelope env = host1.CryptoProvider.EncryptEnvelope(samplePlaintext, p1.KeyMaterial.PublicKeyBase64, "Sub_X");

        // A11: Reopen and verify active key survives and decrypts
        using SubjectKeyTestHost host2 = await host1.ReopenAsync();
        Assert.Equal(1, host2.LoadReport!.ActiveCount);
        Assert.Equal(0, host2.LoadReport.ErasedCount);

        SubjectKeyProfile? reloaded = host2.Vault.Find("sub_x");
        Assert.NotNull(reloaded);
        Assert.Equal(p1.KeyMaterial.PublicKeyBase64, reloaded.KeyMaterial.PublicKeyBase64);
        Assert.Equal(p1.KeyMaterial.PrivateKeyBase64, reloaded.KeyMaterial.PrivateKeyBase64);
        Assert.Equal(samplePlaintext, host2.CryptoProvider.DecryptEnvelope(env, host2.Vault.GetPrivateKeyForDecryption("SUB_X")));

        // A14: EraseAsync semantics
        DateTimeOffset beforeErase = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Equal(SubjectErasureOutcome.Erased, await host2.Vault.EraseAsync("sub_x"));
        DateTimeOffset afterErase = DateTimeOffset.UtcNow.AddSeconds(1);

        SubjectKeyProfile? postErase = host2.Vault.Find("Sub_X");
        Assert.NotNull(postErase);
        Assert.True(postErase.IsShredded);
        Assert.Null(postErase.KeyMaterial.PrivateKeyBase64);
        Assert.Equal(string.Empty, postErase.KeyMaterial.PublicKeyBase64);
        Assert.NotNull(postErase.ShreddedAtUtc);
        Assert.InRange(postErase.ShreddedAtUtc.Value, beforeErase, afterErase);

        SubjectKeyRow dbTombstone = await host2.InnerRepository.GetRequiredAsync("SUB_X");
        Assert.True(dbTombstone.IsShredded);
        Assert.Null(dbTombstone.SealedPrivateKey);
        Assert.Null(dbTombstone.PublicKeyBase64);

        Assert.Equal(SubjectErasureOutcome.AlreadyErased, await host2.Vault.EraseAsync("Sub_X"));
        Assert.Equal(SubjectErasureOutcome.UnknownSubject, await host2.Vault.EraseAsync("never_seen"));
        Assert.Equal(SubjectErasureOutcome.UnknownSubject, await host2.Vault.EraseAsync("  "));
        Assert.Single(await host2.InnerRepository.ListAllAsync());

        // A15: Erasure survives restart
        using SubjectKeyTestHost host3 = await host2.ReopenAsync();
        Assert.Equal(0, host3.LoadReport!.ActiveCount);
        Assert.Equal(1, host3.LoadReport.ErasedCount);

        SubjectKeyProfile? tombstoneAfterRestart = host3.Vault.Find("Sub_X");
        Assert.NotNull(tombstoneAfterRestart);
        Assert.True(tombstoneAfterRestart.IsShredded);

        IReadOnlyList<SubjectKeyRow> rowsBefore = await host3.InnerRepository.ListAllAsync();
        SubjectKeyUnavailableException erasedEx = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => host3.Vault.GetOrCreateAsync("Sub_X"));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, erasedEx.Reason);
        IReadOnlyList<SubjectKeyRow> rowsAfter = await host3.InnerRepository.ListAllAsync();
        Assert.Equal(rowsBefore, rowsAfter);
    }

    /// <summary>
    /// Verifies criteria A12, A30, A32, and A33: full WebApplicationFactory restart survival for encrypted records
    /// and dataset bundles, HTTP compliance error responses, secret non-disclosure, and startup logging.
    /// </summary>
    [Fact]
    public async Task A12_A30_A32_A33_HostRestart_HttpComplianceAndNonDisclosure_Succeed()
    {
        string scenarioDir = Path.Combine(_tempRoot, "a12_host");
        string sharedDir = Path.Combine(scenarioDir, "shared-data");
        string exportDir = Path.Combine(scenarioDir, "exports");
        string keysDir = Path.Combine(scenarioDir, "node-keys");
        Directory.CreateDirectory(sharedDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(keysDir);
        TestFixturesHelper.EnsureTestFixtures(sharedDir);

        string subjectId = "sub_restart_patient";
        EncryptedEnvelope? envelopeFromHost1;
        string? bundlePathFromHost1;
        ConcurrentQueue<string> host1Logs = new();

        using (WebApplicationFactory<Program> baseFactory1 = new())
        using (WebApplicationFactory<Program> factory1 = ConfigureFactory(baseFactory1, sharedDir, exportDir, keysDir, host1Logs))
        using (HttpClient client1 = factory1.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client1, factory1);

            using JsonDocument doc = JsonDocument.Parse("{\"marker\":9001}");
            HttpResponseMessage encResp = await client1.PostAsJsonAsync("/api/compliance/encrypt-record", new EncryptRecordRequest
            {
                SubjectId = subjectId,
                Payload = doc.RootElement.Clone()
            });
            Assert.Equal(HttpStatusCode.OK, encResp.StatusCode);
            string encBody = await encResp.Content.ReadAsStringAsync();
            envelopeFromHost1 = JsonSerializer.Deserialize<EncryptedEnvelope>(encBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(envelopeFromHost1);

            HttpResponseMessage expResp = await client1.PostAsJsonAsync("/api/datasets/export-encrypted", new ExportEncryptedDatasetRequest
            {
                TrajectoryFileName = "dna_nanotube_sample.xyz",
                SubjectId = subjectId,
                ExportDirectory = exportDir
            });
            Assert.Equal(HttpStatusCode.OK, expResp.StatusCode);
            string expBody = await expResp.Content.ReadAsStringAsync();
            using JsonDocument expDoc = JsonDocument.Parse(expBody);
            bundlePathFromHost1 = expDoc.RootElement.GetProperty("bundle_path").GetString();
            Assert.NotNull(bundlePathFromHost1);

            // A32: Check GetAllSubjectProfiles and response bodies for absence of private key or sealed value
            IKeyManagementService kms1 = factory1.Services.GetRequiredService<IKeyManagementService>();
            IReadOnlyList<SubjectKeyProfile> allProfiles = kms1.GetAllSubjectProfiles();
            Assert.Single(allProfiles);
            Assert.Null(allProfiles[0].KeyMaterial.PrivateKeyBase64);

            string rawPrivateKey = kms1.GetSubjectPrivateKeyForDecryption(subjectId);
            ISubjectKeyRepository repo1 = factory1.Services.GetRequiredService<ISubjectKeyRepository>();
            SubjectKeyRow row1 = await repo1.GetRequiredAsync(SubjectKeyVault.NormalizeSubjectKey(subjectId));
            string sealedValue = row1.SealedPrivateKey!;

            string statusBody = await (await client1.GetAsync("/api/compliance/status")).Content.ReadAsStringAsync();
            foreach (string body in new[] { statusBody, encBody, expBody, string.Join("\n", host1Logs) })
            {
                Assert.DoesNotContain(rawPrivateKey, body, StringComparison.Ordinal);
                Assert.DoesNotContain(sealedValue, body, StringComparison.Ordinal);
            }

            // A33: Startup log line
            string[] matchingLogs = host1Logs.Where(l => l.Contains("Subject key store loaded. Active subject keys:", StringComparison.Ordinal)).ToArray();
            Assert.Single(matchingLogs);
            Assert.DoesNotContain("/", matchingLogs[0], StringComparison.Ordinal);
        }

        ClearSqlitePool(Path.Combine(sharedDir, "weaver_config.db"));

        // Host 2 over the same root after restart
        ConcurrentQueue<string> host2Logs = new();
        using (WebApplicationFactory<Program> baseFactory2 = new())
        using (WebApplicationFactory<Program> factory2 = ConfigureFactory(baseFactory2, sharedDir, exportDir, keysDir, host2Logs))
        using (HttpClient client2 = factory2.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client2, factory2);

            // A12: Decrypt envelope created on Host 1
            HttpResponseMessage decResp = await client2.PostAsJsonAsync("/api/compliance/decrypt-record", new DecryptRecordRequest
            {
                SubjectId = subjectId,
                Envelope = envelopeFromHost1
            });
            Assert.Equal(HttpStatusCode.OK, decResp.StatusCode);
            JsonElement decPayload = await decResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(9001, decPayload.GetProperty("marker").GetInt32());

            // A30: decrypt-record for unknown subject returns 400 with UnknownSubjectMessage
            string unknownId = "sub_unknown_http";
            HttpResponseMessage unknownDecResp = await client2.PostAsJsonAsync("/api/compliance/decrypt-record", new DecryptRecordRequest
            {
                SubjectId = unknownId,
                Envelope = envelopeFromHost1
            });
            Assert.Equal(HttpStatusCode.BadRequest, unknownDecResp.StatusCode);
            JsonElement unknownDecProblem = await unknownDecResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SubjectKeyUnavailableException.UnknownSubjectMessage(unknownId), unknownDecProblem.GetProperty("detail").GetString());

            // A30: crypto-shred unknown subject returns 404 with ShredUnknownMessage
            HttpResponseMessage unknownShredResp = await client2.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = unknownId });
            Assert.Equal(HttpStatusCode.NotFound, unknownShredResp.StatusCode);
            JsonElement unknownShredJson = await unknownShredResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Program.ShredUnknownMessage(unknownId), unknownShredJson.GetProperty("error").GetString());

            // Verify exported bundle from Host 1 decrypts with reopened vault before shredding
            byte[] bundleEnvelopeBytes = await File.ReadAllBytesAsync(bundlePathFromHost1!);
            EncryptedEnvelope bundleEnvelope = JsonSerializer.Deserialize<EncryptedEnvelope>(bundleEnvelopeBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            IKeyManagementService kms2 = factory2.Services.GetRequiredService<IKeyManagementService>();
            IPostQuantumCryptoProvider pqc2 = factory2.Services.GetRequiredService<IPostQuantumCryptoProvider>();
            byte[] compressedBytes = pqc2.DecryptEnvelope(bundleEnvelope, kms2.GetSubjectPrivateKeyForDecryption(subjectId));
            using MemoryStream compIn = new(compressedBytes);
            using GZipStream gz = new(compIn, CompressionMode.Decompress);
            using MemoryStream decompressed = new();
            await gz.CopyToAsync(decompressed);
            byte[] originalTrajectory = await File.ReadAllBytesAsync(Path.Combine(sharedDir, "dna_nanotube_sample.xyz"));
            Assert.Equal(originalTrajectory, decompressed.ToArray());

            // A30: crypto-shred active subject returns 200 with ShredOkMessage
            HttpResponseMessage shredOkResp = await client2.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = subjectId });
            Assert.Equal(HttpStatusCode.OK, shredOkResp.StatusCode);
            JsonElement shredOkJson = await shredOkResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Program.ShredOkMessage, shredOkJson.GetProperty("message").GetString());

            // Repeat crypto-shred returns 404 with ShredAlreadyMessage
            HttpResponseMessage shredRepeatResp = await client2.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest { SubjectId = subjectId });
            Assert.Equal(HttpStatusCode.NotFound, shredRepeatResp.StatusCode);
            JsonElement shredRepeatJson = await shredRepeatResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Program.ShredAlreadyMessage(subjectId), shredRepeatJson.GetProperty("error").GetString());

            // Decrypt after erasure returns 400 with ErasedMessage
            SubjectKeyProfile erasedProfile = kms2.GetSubjectKey(subjectId)!;
            HttpResponseMessage erasedDecResp = await client2.PostAsJsonAsync("/api/compliance/decrypt-record", new DecryptRecordRequest
            {
                SubjectId = subjectId,
                Envelope = envelopeFromHost1
            });
            Assert.Equal(HttpStatusCode.BadRequest, erasedDecResp.StatusCode);
            JsonElement erasedDecProblem = await erasedDecResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                SubjectKeyUnavailableException.ErasedMessage(subjectId, erasedProfile.ShreddedAtUtc!.Value),
                erasedDecProblem.GetProperty("detail").GetString());
        }
    }

    /// <summary>
    /// Verifies criterion A16: SQLite VACUUM purges the erased sealed value and private key from the database file
    /// and leaves no rollback journal file behind.
    /// </summary>
    [Fact]
    public async Task A16_SqliteErasure_PurgesSealedAndPrivateKeyBytesFromDatabaseFile()
    {
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "a16"));
        SubjectKeyProfile created = await host.Vault.GetOrCreateAsync("sub_vacuum_target");
        string privateKeyB64 = created.KeyMaterial.PrivateKeyBase64!;
        SubjectKeyRow rowBefore = await host.InnerRepository.GetRequiredAsync("SUB_VACUUM_TARGET");
        string sealedBefore = rowBefore.SealedPrivateKey!;

        Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_vacuum_target"));

        string dbPath = Path.Combine(host.RootDirectory, "weaver_config.db");
        ClearSqlitePool(dbPath);
        string dbBytesAsLatin1 = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(dbPath));
        Assert.DoesNotContain(sealedBefore, dbBytesAsLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKeyB64, dbBytesAsLatin1, StringComparison.Ordinal);
        Assert.False(File.Exists(dbPath + "-journal"));
    }

    /// <summary>
    /// Verifies criteria A17, A18, A19, and A21: fault injection during erase/compact/create and external database deletion.
    /// </summary>
    [Fact]
    public async Task A17_A18_A19_A21_FaultInjectionAndExternalDatabaseChange_MaintainInvariants()
    {
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "a17_a21"));
        SubjectKeyProfile active = await host.Vault.GetOrCreateAsync("sub_fault_target");
        byte[] sampleBytes = Encoding.UTF8.GetBytes("test-payload");
        EncryptedEnvelope sampleEnv = host.CryptoProvider.EncryptEnvelope(sampleBytes, active.KeyMaterial.PublicKeyBase64, "sub_fault_target");

        // A17: MarkShreddedAsync throws -> cache stays active and decryption works
        host.Repository.MarkShreddedFault = new InvalidOperationException("Simulated MarkShredded failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Vault.EraseAsync("sub_fault_target"));
        Assert.False(host.Vault.Find("sub_fault_target")!.IsShredded);
        Assert.Equal(sampleBytes, host.CryptoProvider.DecryptEnvelope(sampleEnv, host.Vault.GetPrivateKeyForDecryption("sub_fault_target")));

        host.Repository.MarkShreddedFault = null;
        Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_fault_target"));

        // A18: CompactStorageAsync throws -> erasure is still committed and cached
        await host.Vault.GetOrCreateAsync("sub_compact_fault");
        host.Repository.CompactStorageFault = new InvalidOperationException("Simulated VACUUM failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Vault.EraseAsync("sub_compact_fault"));
        host.Repository.CompactStorageFault = null;

        Assert.True(host.Vault.Find("sub_compact_fault")!.IsShredded);
        Assert.True((await host.InnerRepository.GetRequiredAsync("SUB_COMPACT_FAULT")).IsShredded);

        using SubjectKeyTestHost reopened = await host.ReopenAsync();
        Assert.Equal(2, reopened.LoadReport!.ErasedCount);

        // A19: TryInsertActiveAsync throws -> nothing cached, no row, EncryptSubjectRecordAsync propagates
        reopened.Repository.TryInsertActiveFault = new InvalidOperationException("Simulated insert failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Vault.GetOrCreateAsync("sub_unpersisted"));
        Assert.Null(reopened.Vault.Find("sub_unpersisted"));

        ComplianceAuditService audit = new(reopened.KeyManagement, reopened.CryptoProvider);
        EncryptedStorageService storage = new(reopened.KeyManagement, reopened.CryptoProvider, audit, reopened.Configuration);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.EncryptSubjectRecordAsync("sub_unpersisted", new { X = 1 }));
        reopened.Repository.TryInsertActiveFault = null;
        Assert.Equal(2, (await reopened.InnerRepository.ListAllAsync()).Count);

        // Also exercise FaultInjectingSubjectKeyRepository ListAllFault and GetRequiredFaultswitches for 100% test helper sanity
        reopened.Repository.ListAllFault = new InvalidOperationException("List fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Repository.ListAllAsync());
        reopened.Repository.ListAllFault = null;

        reopened.Repository.GetRequiredFault = new InvalidOperationException("GetRequired fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Repository.GetRequiredAsync("ANY"));
        reopened.Repository.GetRequiredFault = null;

        // A21: Delete weaver_config.db after load -> EraseAsync for cached active key throws ExternalChangeMessage
        await reopened.Vault.GetOrCreateAsync("sub_external_del");
        string a21DbPath = Path.Combine(reopened.RootDirectory, "weaver_config.db");
        ClearSqlitePool(a21DbPath);
        File.Delete(a21DbPath);

        InvalidOperationException extEx = await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Vault.EraseAsync("sub_external_del"));
        Assert.Equal(SubjectKeyVault.ExternalChangeMessage, extEx.Message);
        Assert.False(reopened.Vault.Find("sub_external_del")!.IsShredded);
    }

    /// <summary>
    /// Verifies criterion A20: concurrent key creation race resolution against pre-inserted active, tombstone,
    /// and foreign-key-sealed rows.
    /// </summary>
    [Fact]
    public async Task A20_CreateRace_ReconcilesActiveTombstoneAndForeignRows()
    {
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "a20"));

        // 1. Pre-insert active row for T directly via repository
        PqcKeyMaterial tKey = host.CryptoProvider.GenerateKemKeyPair("subj_kem_T");
        string tBinding = SubjectKeyVault.ComputeBinding("T", tKey.KeyId, tKey.PublicKeyBase64);
        string tSealed = host.Protector.EncryptRequiredField($"{SubjectKeyVault.SealPrefix}{tBinding}\n{tKey.PrivateKeyBase64}");
        Assert.True(await host.InnerRepository.TryInsertActiveAsync(new SubjectKeyRow
        {
            SubjectKey = "T",
            SubjectId = "T",
            KeyId = tKey.KeyId,
            Algorithm = "ML-KEM-768",
            PublicKeyBase64 = tKey.PublicKeyBase64,
            SealedPrivateKey = tSealed,
            IsShredded = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }));

        SubjectKeyProfile resolvedT = await host.Vault.GetOrCreateAsync("T");
        Assert.Equal(tKey.PublicKeyBase64, resolvedT.KeyMaterial.PublicKeyBase64);

        // 2. Pre-insert tombstone row for U directly via repository
        Assert.True(await host.InnerRepository.TryInsertActiveAsync(new SubjectKeyRow
        {
            SubjectKey = "U",
            SubjectId = "U",
            KeyId = "kid_u",
            Algorithm = "ML-KEM-768",
            PublicKeyBase64 = "pub_u",
            SealedPrivateKey = tSealed,
            IsShredded = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }));
        Assert.True(await host.InnerRepository.MarkShreddedAsync("U", DateTimeOffset.UtcNow));

        SubjectKeyUnavailableException uEx = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => host.Vault.GetOrCreateAsync("U"));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, uEx.Reason);

        // 3. Pre-insert row for V sealed under a foreign PHI key
        using SubjectKeyTestHost foreignHost = new(Path.Combine(_tempRoot, "a20_foreign"));
        string foreignSealed = foreignHost.Protector.EncryptRequiredField($"{SubjectKeyVault.SealPrefix}{tBinding}\n{tKey.PrivateKeyBase64}");
        Assert.True(await host.InnerRepository.TryInsertActiveAsync(new SubjectKeyRow
        {
            SubjectKey = "V",
            SubjectId = "V",
            KeyId = tKey.KeyId,
            Algorithm = "ML-KEM-768",
            PublicKeyBase64 = tKey.PublicKeyBase64,
            SealedPrivateKey = foreignSealed,
            IsShredded = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        }));

        await Assert.ThrowsAsync<SubjectKeyStoreLoadException>(() => host.Vault.GetOrCreateAsync("V"));
    }

    /// <summary>
    /// Verifies criteria A22, A23, A24, A25, and A26: fail-loud startup validation, row immutability on failure,
    /// missing phi_master.key detection, and unloaded/double-loaded guards.
    /// </summary>
    [Fact]
    public async Task A22_A23_A24_A25_A26_FailLoudStartup_RejectsCorruptOrForeignRowsWithoutAlteringDatabase()
    {
        // Also cover SubjectKeyStoreLoadException(message, innerException) constructor
        Exception innerCause = new CryptographicException("tag mismatch");
        SubjectKeyStoreLoadException withInner = new("outer message", innerCause);
        Assert.Same(innerCause, withInner.InnerException);

        // A26: Unloaded vault guards and constructor null checks
        using SubjectKeyTestHost unloaded = new(Path.Combine(_tempRoot, "a26"));
        Assert.Throws<ArgumentNullException>(() => new SubjectKeyVault(null!, unloaded.Protector, unloaded.CryptoProvider, unloaded.Journal));
        Assert.Throws<ArgumentNullException>(() => new SubjectKeyVault(unloaded.Repository, null!, unloaded.CryptoProvider, unloaded.Journal));
        Assert.Throws<ArgumentNullException>(() => new SubjectKeyVault(unloaded.Repository, unloaded.Protector, null!, unloaded.Journal));
        Assert.Throws<ArgumentNullException>(() => new SubjectKeyVault(unloaded.Repository, unloaded.Protector, unloaded.CryptoProvider, null!));

        Assert.False(unloaded.Vault.IsLoaded);
        Assert.Null(unloaded.Vault.Find("   "));
        Assert.Equal(SubjectErasureOutcome.UnknownSubject, await unloaded.Vault.EraseAsync("   "));
        Assert.Throws<ArgumentException>(() => unloaded.Vault.GetPrivateKeyForDecryption("   "));

        Assert.Equal(SubjectKeyVault.NotLoadedMessage, Assert.Throws<InvalidOperationException>(() => unloaded.Vault.Find("x")).Message);
        Assert.Equal(SubjectKeyVault.NotLoadedMessage, Assert.Throws<InvalidOperationException>(() => unloaded.Vault.ListWithoutPrivateKeys()).Message);
        Assert.Equal(SubjectKeyVault.NotLoadedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => unloaded.Vault.GetOrCreateAsync("x"))).Message);
        Assert.Equal(SubjectKeyVault.NotLoadedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => unloaded.Vault.EraseAsync("x"))).Message);
        Assert.Equal(SubjectKeyVault.NotLoadedMessage, Assert.Throws<InvalidOperationException>(() => unloaded.Vault.GetPrivateKeyForDecryption("x")).Message);

        await unloaded.Vault.InitializeAsync();
        Assert.True(unloaded.Vault.IsLoaded);
        Assert.Equal(SubjectKeyVault.AlreadyLoadedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => unloaded.Vault.InitializeAsync())).Message);

        // A22: Row sealed under a foreign PHI key
        string a22Root = Path.Combine(_tempRoot, "a22_root");
        string a22Shared = Path.Combine(a22Root, "shared-data");
        string a22Exports = Path.Combine(a22Root, "exports");
        string a22ForeignKeys = Path.Combine(_tempRoot, "a22_foreign_keys");
        Directory.CreateDirectory(a22Shared);
        Directory.CreateDirectory(a22Exports);

        using (SubjectKeyTestHost writerWithForeignKey = new(a22Shared, nodeKeysDirectoryOverride: a22ForeignKeys))
        {
            await writerWithForeignKey.Vault.InitializeAsync();
            await writerWithForeignKey.Vault.GetOrCreateAsync("sub_foreign");
        }

        SubjectKeyRow rowBeforeA22;
        using (SubjectKeyTestHost readerWithOwnKey = new(a22Shared))
        {
            rowBeforeA22 = (await readerWithOwnKey.InnerRepository.ListAllAsync())[0];
            SubjectKeyStoreLoadException loadEx = await Assert.ThrowsAsync<SubjectKeyStoreLoadException>(() => readerWithOwnKey.Vault.InitializeAsync());
            Assert.Equal(SubjectKeyVault.LoadFailureMessage(1, 1), loadEx.Message);
            Assert.DoesNotContain(a22Root, loadEx.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(rowBeforeA22.SealedPrivateKey!, loadEx.Message, StringComparison.Ordinal);
            Assert.False(readerWithOwnKey.Vault.IsLoaded);
            Assert.Equal(SubjectKeyVault.NotLoadedMessage, Assert.Throws<InvalidOperationException>(() => readerWithOwnKey.Vault.Find("x")).Message);

            // A24: row unchanged
            IReadOnlyList<SubjectKeyRow> rowsAfterA22 = await readerWithOwnKey.InnerRepository.ListAllAsync();
            Assert.Single(rowsAfterA22);
            Assert.Equal(rowBeforeA22, rowsAfterA22[0]);
        }

        // A22: Host over that root fails to start with SubjectKeyStoreLoadException in chain
        ClearSqlitePool(Path.Combine(a22Shared, "weaver_config.db"));
        using (WebApplicationFactory<Program> rawFactory = new())
        using (WebApplicationFactory<Program> failingFactory = ConfigureFactory(rawFactory, a22Shared, a22Exports, Path.Combine(a22Shared, "node-keys"), new ConcurrentQueue<string>()))
        {
            Exception startEx = Assert.ThrowsAny<Exception>(() => failingFactory.CreateClient());
            Assert.True(ContainsExceptionInChain<SubjectKeyStoreLoadException>(startEx));
        }

        // A23(a): Swapped sealed values between two rows
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_swap", 2, 2, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_one");
            await host.Vault.GetOrCreateAsync("sub_two");
            SubjectKeyRow r1 = await host.InnerRepository.GetRequiredAsync("SUB_ONE");
            SubjectKeyRow r2 = await host.InnerRepository.GetRequiredAsync("SUB_TWO");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET sealed_private_key = @v WHERE subject_key = 'SUB_ONE'", r2.SealedPrivateKey!);
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET sealed_private_key = @v WHERE subject_key = 'SUB_TWO'", r1.SealedPrivateKey!);
        });

        // A23(b): public_key_base64 replaced with another valid ML-KEM-768 public key
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_pubkey", 1, 1, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_pub");
            PqcKeyMaterial other = host.CryptoProvider.GenerateKemKeyPair("other");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET public_key_base64 = @v WHERE subject_key = 'SUB_PUB'", other.PublicKeyBase64);
        });

        // A23(c): Value sealed with the legacy key
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_legacy", 1, 1, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_leg");
            SubjectKeyRow r = await host.InnerRepository.GetRequiredAsync("SUB_LEG");
            string binding = SubjectKeyVault.ComputeBinding(r.SubjectKey, r.KeyId!, r.PublicKeyBase64!);
            string legacySealed = EncryptWithLegacyKey($"{SubjectKeyVault.SealPrefix}{binding}\npriv");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET sealed_private_key = @v WHERE subject_key = 'SUB_LEG'", legacySealed);
        });

        // A23(d): Correctly sealed plaintext with a wrong prefix
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_prefix", 1, 1, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_pfx");
            SubjectKeyRow r = await host.InnerRepository.GetRequiredAsync("SUB_PFX");
            string binding = SubjectKeyVault.ComputeBinding(r.SubjectKey, r.KeyId!, r.PublicKeyBase64!);
            string wrongPrefixSealed = host.Protector.EncryptRequiredField($"weaver-subject-key-v0\n{binding}\npriv");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET sealed_private_key = @v WHERE subject_key = 'SUB_PFX'", wrongPrefixSealed);
        });

        // A23(e): algorithm changed to ML-KEM-1024
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_alg", 1, 1, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_alg");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET algorithm = @v WHERE subject_key = 'SUB_ALG'", "ML-KEM-1024");
        });

        // A23(f): Empty private key after valid header
        await VerifyTamperedLoadFailsAndPreservesRowsAsync("a23_empty_priv", 1, 1, async host =>
        {
            await host.Vault.GetOrCreateAsync("sub_empty_priv");
            SubjectKeyRow r = await host.InnerRepository.GetRequiredAsync("SUB_EMPTY_PRIV");
            string binding = SubjectKeyVault.ComputeBinding(r.SubjectKey, r.KeyId!, r.PublicKeyBase64!);
            string emptyPrivSealed = host.Protector.EncryptRequiredField($"{SubjectKeyVault.SealPrefix}{binding}\n");
            await ExecuteRawUpdateAsync(host, "UPDATE weaver_subject_keys SET sealed_private_key = @v WHERE subject_key = 'SUB_EMPTY_PRIV'", emptyPrivSealed);
        });

        // A25: Delete phi_master.key when an active row exists -> InitializeAsync throws SubjectKeyStoreLoadException
        string a25Dir = Path.Combine(_tempRoot, "a25");
        using (SubjectKeyTestHost hostA25 = await SubjectKeyTestHost.CreateLoadedAsync(a25Dir))
        {
            await hostA25.Vault.GetOrCreateAsync("sub_a25");
            File.Delete(Path.Combine(hostA25.SecretStore.NodeKeysDirectory, "phi_master.key"));
        }

        using (SubjectKeyTestHost regeneratedA25 = new(a25Dir))
        {
            await Assert.ThrowsAsync<SubjectKeyStoreLoadException>(() => regeneratedA25.Vault.InitializeAsync());
        }
    }

    /// <summary>
    /// Verifies criteria A27, A28, A29, and A31: error messages, SanitizeDetail pass-through, and removal of conflated strings.
    /// </summary>
    [Fact]
    public async Task A27_A28_A29_A31_ErrorMessages_SanitizerAndSourceScan_MeetSpecification()
    {
        // A29: SanitizeDetail returns each client-facing message unchanged
        string sampleId = "sub_0123abcd";
        DateTimeOffset sampleTime = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
        string[] messages =
        [
            SubjectKeyUnavailableException.ErasedMessage(sampleId, sampleTime),
            SubjectKeyUnavailableException.UnknownSubjectMessage(sampleId),
            SubjectKeyVault.ExternalChangeMessage,
            Program.ShredOkMessage,
            Program.ShredAlreadyMessage(sampleId),
            Program.ShredUnknownMessage(sampleId)
        ];

        foreach (string msg in messages)
        {
            Assert.Equal(msg, SecureExceptionSanitizer.SanitizeDetail(msg, 400, "t"));
        }

        // A27 & A28: EncryptedStorageService error model
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "a27_a28"));
        TestFixturesHelper.EnsureTestFixtures(host.RootDirectory);
        ComplianceAuditService audit = new(host.KeyManagement, host.CryptoProvider);
        EncryptedStorageService storage = new(host.KeyManagement, host.CryptoProvider, audit, host.Configuration);

        EncryptedEnvelope env = await storage.EncryptSubjectRecordAsync("sub_known", new { Value = 42 });

        SubjectKeyUnavailableException unknownEx = Assert.Throws<SubjectKeyUnavailableException>(
            () => storage.DecryptSubjectRecord<JsonElement>("sub_never_created", env));
        Assert.Equal(SubjectKeyUnavailableReason.UnknownSubject, unknownEx.Reason);
        Assert.Null(unknownEx.ShreddedAtUtc);
        Assert.Equal(SubjectKeyUnavailableException.UnknownSubjectMessage("sub_never_created"), unknownEx.Message);

        Assert.Equal(SubjectErasureOutcome.Erased, await host.KeyManagement.CryptoShredSubjectKeyAsync("sub_known"));
        DateTimeOffset shreddedAt = host.KeyManagement.GetSubjectKey("sub_known")!.ShreddedAtUtc!.Value;
        string expectedErasedMsg = SubjectKeyUnavailableException.ErasedMessage("sub_known", shreddedAt);

        SubjectKeyUnavailableException encErased = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(
            () => storage.EncryptSubjectRecordAsync("sub_known", new { Value = 42 }));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, encErased.Reason);
        Assert.Equal(expectedErasedMsg, encErased.Message);

        SubjectKeyUnavailableException decErased = Assert.Throws<SubjectKeyUnavailableException>(
            () => storage.DecryptSubjectRecord<JsonElement>("sub_known", env));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, decErased.Reason);
        Assert.Equal(expectedErasedMsg, decErased.Message);

        SubjectKeyUnavailableException expErased = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(
            () => storage.ExportEncryptedDatasetBundleAsync("dna_nanotube_sample.xyz", "sub_known", host.RootDirectory));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, expErased.Reason);
        Assert.Equal(expectedErasedMsg, expErased.Message);

        // A31: Source scan over src/ for forbidden legacy strings
        string srcRoot = FindRepoFile("src");
        string[] forbidden =
        [
            "Private key has been permanently destroyed",
            "Profile was permanently crypto-shredded",
            "Subject has been crypto-shredded.",
            "permanently destroyed pursuant to GDPR Article 17",
            "Private decryption key was purged"
        ];

        foreach (string file in Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.Ordinal) || file.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(file);
            foreach (string phrase in forbidden)
            {
                Assert.DoesNotContain(phrase, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Creates an initialized <see cref="SubjectKeyTestHost"/> for SQLite or PostgreSQL, or returns <c>null</c>
    /// when PostgreSQL is requested and <c>WEAVER_TEST_POSTGRES</c> is not set.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    /// <param name="name">Scenario subdirectory suffix.</param>
    /// <returns>The initialized host, or <c>null</c> when PostgreSQL is unavailable.</returns>
    private async Task<SubjectKeyTestHost?> CreateHostOrSkipAsync(bool postgres, string name)
    {
        string dir = Path.Combine(_tempRoot, $"{name}_{postgres}_{Guid.NewGuid():N}");
        if (!postgres)
        {
            return await SubjectKeyTestHost.CreateLoadedAsync(dir);
        }

        if (!_postgres.IsAvailable)
        {
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return null;
        }

        string pgConn = await _postgres.CreateIsolatedDatabaseAsync();
        return await SubjectKeyTestHost.CreateLoadedAsync(dir, pgConn);
    }

    /// <summary>
    /// Runs a tampering callback against a populated database, verifies that <see cref="SubjectKeyVault.InitializeAsync"/>
    /// throws <see cref="SubjectKeyStoreLoadException"/> with the expected counts, and proves no row was modified.
    /// </summary>
    /// <param name="name">Scenario name.</param>
    /// <param name="expectedFailed">Expected failed active row count.</param>
    /// <param name="expectedTotal">Expected total active row count.</param>
    /// <param name="populateAndTamper">Async callback that populates and tampers with the database.</param>
    /// <returns>A task representing the verification.</returns>
    private async Task VerifyTamperedLoadFailsAndPreservesRowsAsync(
        string name,
        int expectedFailed,
        int expectedTotal,
        Func<SubjectKeyTestHost, Task> populateAndTamper)
    {
        string dir = Path.Combine(_tempRoot, name);
        using (SubjectKeyTestHost setupHost = await SubjectKeyTestHost.CreateLoadedAsync(dir))
        {
            await populateAndTamper(setupHost);
        }

        using SubjectKeyTestHost verifyHost = new(dir);
        IReadOnlyList<SubjectKeyRow> before = await verifyHost.InnerRepository.ListAllAsync();
        SubjectKeyStoreLoadException ex = await Assert.ThrowsAsync<SubjectKeyStoreLoadException>(() => verifyHost.Vault.InitializeAsync());
        Assert.Equal(SubjectKeyVault.LoadFailureMessage(expectedFailed, expectedTotal), ex.Message);
        IReadOnlyList<SubjectKeyRow> after = await verifyHost.InnerRepository.ListAllAsync();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// Executes a parameterized raw SQL update on the host's database.
    /// </summary>
    /// <param name="host">Target test host.</param>
    /// <param name="sql">SQL statement with a <c>@v</c> parameter.</param>
    /// <param name="value">Parameter value.</param>
    /// <returns>A task that completes when the update finishes.</returns>
    private static async Task ExecuteRawUpdateAsync(SubjectKeyTestHost host, string sql, string value)
    {
        await using WeaverDbSession session = await host.ConnectionFactory.OpenAsync();
        await using DbCommand cmd = session.CreateCommand(sql);
        WeaverDbParameters.Add(cmd, "@v", value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Encrypts plaintext into the <see cref="ProfileDataProtector"/> envelope format using the historic legacy PHI key.
    /// </summary>
    /// <param name="plaintext">Plaintext string to encrypt.</param>
    /// <returns>The legacy-key encrypted data-URI envelope.</returns>
    private static string EncryptWithLegacyKey(string plaintext)
    {
        byte[] key = Convert.FromHexString(LegacyPhiMasterKeyHex);
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        byte[] cipher = new byte[plainBytes.Length];
        byte[] tag = new byte[16];

        using (AesGcm aes = new(key, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        byte[] payload = new byte[12 + 16 + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, 12);
        Buffer.BlockCopy(tag, 0, payload, 12, 16);
        Buffer.BlockCopy(cipher, 0, payload, 28, cipher.Length);
        return "data:application/encrypted;base64," + Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Configures a <see cref="WebApplicationFactory{TEntryPoint}"/> over an isolated directory and attaches a log sink.
    /// </summary>
    /// <param name="factory">Base factory.</param>
    /// <param name="sharedDir">Shared data directory.</param>
    /// <param name="exportDir">Export directory.</param>
    /// <param name="keysDir">Node keys directory.</param>
    /// <param name="logSink">Queue receiving formatted log lines.</param>
    /// <returns>The configured factory.</returns>
    private static WebApplicationFactory<Program> ConfigureFactory(
        WebApplicationFactory<Program> factory,
        string sharedDir,
        string exportDir,
        string keysDir,
        ConcurrentQueue<string> logSink) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", sharedDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", exportDir);
            builder.UseSetting("WEAVER_NODE_KEYS_DIR", keysDir);
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(logSink)));
        });

    /// <summary>
    /// Checks whether an exception or any inner exception in its chain is of type <typeparamref name="TException"/>.
    /// </summary>
    /// <typeparam name="TException">Target exception type.</typeparam>
    /// <param name="ex">Root exception.</param>
    /// <returns><c>true</c> if found in the chain; otherwise <c>false</c>.</returns>
    private static bool ContainsExceptionInChain<TException>(Exception? ex)
        where TException : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts non-overlapping occurrences of a literal substring inside a source text.
    /// </summary>
    /// <param name="text">Text to search.</param>
    /// <param name="literal">Substring to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountLiteralOccurrences(string text, string literal)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(literal, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += literal.Length;
        }

        return count;
    }

    /// <summary>
    /// Locates a path relative to the repository root by walking upward from <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    /// <param name="relativePath">Repository-relative path.</param>
    /// <returns>The resolved absolute path.</returns>
    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NanoWeaverHrt.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, relativePath);
    }

    /// <summary>
    /// Minimal <see cref="ILoggerProvider"/> that records formatted log messages into a concurrent queue.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLoggerProvider"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLoggerProvider(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_sink);

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Minimal <see cref="ILogger"/> implementation that appends formatted log lines to a queue.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLogger"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLogger(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _sink.Enqueue(formatter(state, exception));
    }

    /// <summary>
    /// Clears the SQLite connection pool for a single database file path without affecting concurrent tests.
    /// </summary>
    /// <param name="dbPath">Absolute path to the SQLite database file.</param>
    private static void ClearSqlitePool(string dbPath)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        SqliteConnection.ClearPool(connection);
    }
}
