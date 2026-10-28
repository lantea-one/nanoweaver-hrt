using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Verification suite for Work Package P7.4 Stage B: external append-only erasure journal
/// (<c>subject_erasures.journal</c>), startup reconciliation against pre-erasure database backup restores,
/// and journal integrity validation.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class SubjectErasureJournalTests : IDisposable
{
    /// <summary>
    /// Shared PostgreSQL test environment fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// xUnit test output sink.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Isolated per-test temporary root directory.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_erasure_journal_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectErasureJournalTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL test fixture.</param>
    /// <param name="output">Test output sink.</param>
    public SubjectErasureJournalTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
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
    /// Verifies criterion B1: when MarkShreddedAsync throws, the erasure is already journalled while the cache
    /// remains active, and the next startup reconciliation completes the erasure.
    /// </summary>
    [Fact]
    public async Task B1_MarkShreddedFailure_LeavesJournalEntryAndActiveCache_ThenReconcilesOnReopen()
    {
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(Path.Combine(_tempRoot, "b1"));
        Assert.Throws<ArgumentNullException>(() => new SubjectErasureJournal(null!));
        Assert.Throws<ArgumentException>(() => host.Journal.Append("  ", DateTimeOffset.UtcNow));

        await host.Vault.GetOrCreateAsync("sub_b1");
        host.Repository.MarkShreddedFault = new InvalidOperationException("Simulated DB failure after journal append");

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Vault.EraseAsync("sub_b1"));
        host.Repository.MarkShreddedFault = null;

        IReadOnlyList<SubjectErasureJournalEntry> entries = host.Journal.ReadAll();
        Assert.Single(entries);
        Assert.Equal("SUB_B1", entries[0].SubjectKey);
        Assert.False(host.Vault.Find("sub_b1")!.IsShredded);

        using SubjectKeyTestHost reopened = await host.ReopenAsync();
        Assert.Equal(1, reopened.LoadReport!.ReappliedErasureCount);
        Assert.Equal(0, reopened.LoadReport.ActiveCount);
        Assert.Equal(1, reopened.LoadReport.ErasedCount);
        Assert.True(reopened.Vault.Find("sub_b1")!.IsShredded);
        Assert.True((await reopened.InnerRepository.GetRequiredAsync("SUB_B1")).IsShredded);
    }

    /// <summary>
    /// Verifies criterion B2: restoring a database backup taken while a subject was active re-applies the erasure
    /// from the journal at startup on both SQLite and PostgreSQL.
    /// </summary>
    /// <param name="postgres"><c>true</c> for PostgreSQL; <c>false</c> for SQLite.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task B2_RestoringPreErasureBackup_ReappliesErasureFromJournalAtStart(bool postgres)
    {
        using SubjectKeyTestHost? host = await CreateHostOrSkipAsync(postgres, "b2");
        if (host is null)
        {
            return;
        }

        await host.Vault.GetOrCreateAsync("sub_b2");
        SubjectKeyRow capturedActiveRow = await host.InnerRepository.GetRequiredAsync("SUB_B2");

        string sqlitePath = Path.Combine(host.RootDirectory, "weaver_config.db");
        string sqliteBackupPath = Path.Combine(host.RootDirectory, "weaver_config.db.bak");
        if (!postgres)
        {
            ClearSqlitePool(sqlitePath);
            File.Copy(sqlitePath, sqliteBackupPath, overwrite: true);
        }

        Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_b2"));

        if (!postgres)
        {
            ClearSqlitePool(sqlitePath);
            File.Copy(sqliteBackupPath, sqlitePath, overwrite: true);
        }
        else
        {
            await RestoreActiveRowInPostgresAsync(host, capturedActiveRow);
        }

        using SubjectKeyTestHost reopened = await host.ReopenAsync();
        Assert.Equal(1, reopened.LoadReport!.ReappliedErasureCount);
        Assert.Equal(0, reopened.LoadReport.ActiveCount);
        Assert.Equal(1, reopened.LoadReport.ErasedCount);

        SubjectKeyRow postReconcileRow = await reopened.InnerRepository.GetRequiredAsync("SUB_B2");
        Assert.True(postReconcileRow.IsShredded);
        Assert.Null(postReconcileRow.SealedPrivateKey);
        Assert.Null(postReconcileRow.PublicKeyBase64);

        SubjectKeyUnavailableException ex = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => reopened.Vault.GetOrCreateAsync("sub_b2"));
        Assert.Equal(SubjectKeyUnavailableReason.Erased, ex.Reason);
    }

    /// <summary>
    /// Verifies criterion B3: restoring a database backup taken before the subject existed rebuilds a tombstone row
    /// from the journal at startup.
    /// </summary>
    [Fact]
    public async Task B3_RestoringBackupFromBeforeSubjectExisted_RebuildsTombstoneAtStart()
    {
        string dir = Path.Combine(_tempRoot, "b3");
        string sqlitePath = Path.Combine(dir, "weaver_config.db");
        string sqliteBackupPath = Path.Combine(dir, "weaver_config.db.bak");

        using (SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(dir))
        {
            // Ensure the empty migrated SQLite file exists, then snapshot it before creating S.
            Assert.Empty(await host.InnerRepository.ListAllAsync());
            ClearSqlitePool(sqlitePath);
            File.Copy(sqlitePath, sqliteBackupPath, overwrite: true);

            await host.Vault.GetOrCreateAsync("Sub_B3");
            Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("Sub_B3"));

            ClearSqlitePool(sqlitePath);
            File.Copy(sqliteBackupPath, sqlitePath, overwrite: true);

            using SubjectKeyTestHost reopened = await host.ReopenAsync();
            Assert.Equal(1, reopened.LoadReport!.RebuiltTombstoneCount);
            Assert.Equal(1, reopened.LoadReport.ErasedCount);

            SubjectKeyProfile? tombstone = reopened.Vault.Find("Sub_B3");
            Assert.NotNull(tombstone);
            Assert.True(tombstone.IsShredded);
            Assert.Equal("SUB_B3", tombstone.SubjectId);

            SubjectKeyUnavailableException ex = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => reopened.Vault.GetOrCreateAsync("Sub_B3"));
            Assert.Equal(SubjectKeyUnavailableReason.Erased, ex.Reason);
        }
    }

    /// <summary>
    /// Verifies criterion B4: a Stage A database with two tombstones and no journal file backfills both entries
    /// on the first load and zero on the second load.
    /// </summary>
    [Fact]
    public async Task B4_StageADatabaseWithTombstonesAndNoJournal_BackfillsJournalOnce()
    {
        string dir = Path.Combine(_tempRoot, "b4");
        using (SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(dir))
        {
            await host.Vault.GetOrCreateAsync("sub_stage_a_1");
            await host.Vault.GetOrCreateAsync("sub_stage_a_2");
            Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_stage_a_1"));
            Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_stage_a_2"));

            string journalPath = Path.Combine(host.SecretStore.NodeKeysDirectory, SubjectErasureJournal.FileName);
            File.Delete(journalPath);

            using SubjectKeyTestHost firstReopen = await host.ReopenAsync();
            Assert.Equal(2, firstReopen.LoadReport!.JournalBackfillCount);
            Assert.Equal(2, firstReopen.Journal.ReadAll().Count);

            using SubjectKeyTestHost secondReopen = await firstReopen.ReopenAsync();
            Assert.Equal(0, secondReopen.LoadReport!.JournalBackfillCount);
        }
    }

    /// <summary>
    /// Verifies criteria B5, B7, and B10: journal file permissions, exact line format, duplicate key earliest-timestamp
    /// deduplication, empty startup behavior, and absence of secrets or paths in journal bytes.
    /// </summary>
    [Fact]
    public async Task B5_B7_B10_JournalFormatPermissionsDeduplicationAndNonExposure_MeetSpecification()
    {
        // B7: Empty database and no journal -> all-zero report, no journal file created
        string b7Dir = Path.Combine(_tempRoot, "b7");
        using (SubjectKeyTestHost emptyHost = await SubjectKeyTestHost.CreateLoadedAsync(b7Dir))
        {
            Assert.Equal(new SubjectKeyLoadReport(), emptyHost.LoadReport);
            string emptyJournalPath = Path.Combine(emptyHost.SecretStore.NodeKeysDirectory, SubjectErasureJournal.FileName);
            Assert.False(File.Exists(emptyJournalPath));

            // Also verify header-only journal file parses to 0 entries
            await File.WriteAllTextAsync(emptyJournalPath, SubjectErasureJournal.HeaderLine + "\n");
            Assert.Empty(emptyHost.Journal.ReadAll());
        }

        // B5 & B10: Create, erase, inspect journal permissions, format, deduplication, and non-exposure
        string b5Dir = Path.Combine(_tempRoot, "b5");
        using SubjectKeyTestHost host = await SubjectKeyTestHost.CreateLoadedAsync(b5Dir);
        SubjectKeyProfile created = await host.Vault.GetOrCreateAsync("sub_journal_fmt");
        string privKey = created.KeyMaterial.PrivateKeyBase64!;
        string sealedVal = (await host.InnerRepository.GetRequiredAsync("SUB_JOURNAL_FMT")).SealedPrivateKey!;

        Assert.Equal(SubjectErasureOutcome.Erased, await host.Vault.EraseAsync("sub_journal_fmt"));

        string journalPath = Path.Combine(host.SecretStore.NodeKeysDirectory, SubjectErasureJournal.FileName);
#pragma warning disable CA1416
        UnixFileMode mode = File.GetUnixFileMode(journalPath);
#pragma warning restore CA1416
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);

        string rawJournal = await File.ReadAllTextAsync(journalPath, Encoding.UTF8);
        Assert.DoesNotContain(privKey, rawJournal, StringComparison.Ordinal);
        Assert.DoesNotContain(sealedVal, rawJournal, StringComparison.Ordinal);
        Assert.DoesNotContain(_tempRoot, rawJournal, StringComparison.Ordinal);

        string[] lines = rawJournal.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("weaver-subject-erasure-journal v1", lines[0]);

        string expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SUB_JOURNAL_FMT"));
        DateTimeOffset recordedTime = host.Vault.Find("sub_journal_fmt")!.ShreddedAtUtc!.Value;
        Assert.Equal($"E\t{expectedB64}\t{recordedTime.ToString("O", CultureInfo.InvariantCulture)}", lines[1]);

        // Duplicate entries: append a later timestamp for SUB_JOURNAL_FMT, and append two out-of-order timestamps for SUB_DUP2
        DateTimeOffset tEarlier = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset tLater = new(2026, 10, 23, 11, 0, 0, TimeSpan.Zero);
        host.Journal.Append("SUB_JOURNAL_FMT", recordedTime.AddHours(1));
        host.Journal.Append("SUB_DUP2", tLater);
        host.Journal.Append("SUB_DUP2", tEarlier);

        IReadOnlyList<SubjectErasureJournalEntry> parsed = host.Journal.ReadAll();
        Assert.Equal(2, parsed.Count);
        Assert.Equal(recordedTime, parsed[0].ShreddedAtUtc);
        Assert.Equal("SUB_DUP2", parsed[1].SubjectKey);
        Assert.Equal(tEarlier, parsed[1].ShreddedAtUtc);
    }

    /// <summary>
    /// Verifies criterion B6: malformed journal files abort InitializeAsync with SubjectKeyStoreLoadException
    /// naming subject_erasures.journal and the 1-based line number, without disclosing the directory or altering DB rows.
    /// </summary>
    [Fact]
    public async Task B6_MalformedJournal_AbortsInitializeWithoutDisclosingDirectoryOrAlteringDatabase()
    {
        string validB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SUB_X"));
        string validTs = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        (string Name, string Content, string ExpectedLine)[] cases =
        [
            ("wrong_header", $"weaver-subject-erasure-journal v0\nE\t{validB64}\t{validTs}\n", "line 1"),
            ("header_no_newline", "weaver-subject-erasure-journal v1", "line 1"),
            ("bad_tag", $"weaver-subject-erasure-journal v1\nX\t{validB64}\t{validTs}\n", "line 2"),
            ("missing_second_tab", $"weaver-subject-erasure-journal v1\nE\t{validB64}\n", "line 2"),
            ("bad_base64", $"weaver-subject-erasure-journal v1\nE\t!!!bad_b64!!!\t{validTs}\n", "line 2"),
            ("bad_timestamp", $"weaver-subject-erasure-journal v1\nE\t{validB64}\tnot-a-timestamp\n", "line 2"),
            ("entry_no_newline", $"weaver-subject-erasure-journal v1\nE\t{validB64}\t{validTs}", "line 2")
        ];

        foreach ((string name, string content, string expectedLine) in cases)
        {
            string dir = Path.Combine(_tempRoot, "b6_" + name);
            using (SubjectKeyTestHost setup = await SubjectKeyTestHost.CreateLoadedAsync(dir))
            {
                await setup.Vault.GetOrCreateAsync("sub_intact");
            }

            using SubjectKeyTestHost verify = new(dir);
            IReadOnlyList<SubjectKeyRow> before = await verify.InnerRepository.ListAllAsync();
            string journalPath = Path.Combine(verify.SecretStore.NodeKeysDirectory, SubjectErasureJournal.FileName);
            await File.WriteAllTextAsync(journalPath, content, Encoding.UTF8);

            SubjectKeyStoreLoadException ex = await Assert.ThrowsAsync<SubjectKeyStoreLoadException>(() => verify.Vault.InitializeAsync());
            Assert.Contains(SubjectErasureJournal.FileName, ex.Message, StringComparison.Ordinal);
            Assert.Contains(expectedLine, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(verify.SecretStore.NodeKeysDirectory, ex.Message, StringComparison.Ordinal);

            IReadOnlyList<SubjectKeyRow> after = await verify.InnerRepository.ListAllAsync();
            Assert.Equal(before, after);
        }
    }

    /// <summary>
    /// Verifies criteria B8 and B9: host-level pre-erasure database backup restore triggers reconciliation and logs
    /// the expected Warning once, and reconciliation runs before opening so a journalled row sealed under a foreign
    /// PHI key is re-shredded without blocking startup.
    /// </summary>
    [Fact]
    public async Task B8_B9_HostBackupRestoreAndForeignKeyJournalReconciliation_Succeed()
    {
        // B9: Journalled row sealed under a foreign PHI key does not block startup; it is re-shredded
        string b9Dir = Path.Combine(_tempRoot, "b9");
        string b9ForeignKeys = Path.Combine(_tempRoot, "b9_foreign_keys");
        using (SubjectKeyTestHost foreignWriter = new(b9Dir, nodeKeysDirectoryOverride: b9ForeignKeys))
        {
            await foreignWriter.Vault.InitializeAsync();
            await foreignWriter.Vault.GetOrCreateAsync("sub_foreign_erased");
        }

        using (SubjectKeyTestHost localReader = new(b9Dir))
        {
            localReader.Journal.Append("SUB_FOREIGN_ERASED", DateTimeOffset.UtcNow);
            SubjectKeyLoadReport report = await localReader.Vault.InitializeAsync();
            Assert.Equal(1, report.ReappliedErasureCount);
            Assert.Equal(0, report.ActiveCount);
            Assert.Equal(1, report.ErasedCount);

            SubjectKeyRow reconciledRow = await localReader.InnerRepository.GetRequiredAsync("SUB_FOREIGN_ERASED");
            Assert.True(reconciledRow.IsShredded);
            Assert.Null(reconciledRow.SealedPrivateKey);
        }

        // B8: Host-level database restore
        string b8Root = Path.Combine(_tempRoot, "b8_host");
        string sharedDir = Path.Combine(b8Root, "shared-data");
        string exportDir = Path.Combine(b8Root, "exports");
        string keysDir = Path.Combine(b8Root, "node-keys");
        Directory.CreateDirectory(sharedDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(keysDir);
        TestFixturesHelper.EnsureTestFixtures(sharedDir);

        string subjectId = "sub_b8_patient";
        string sqlitePath = Path.Combine(sharedDir, "weaver_config.db");
        string sqliteBackup = Path.Combine(b8Root, "weaver_config.db.bak");

        // 1. Host 1 encrypts for S; dispose and copy weaver_config.db
        ConcurrentQueue<string> host1Logs = new();
        using (WebApplicationFactory<Program> base1 = new())
        using (WebApplicationFactory<Program> factory1 = ConfigureFactory(base1, sharedDir, exportDir, keysDir, host1Logs))
        using (HttpClient client1 = factory1.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client1, factory1);
            using JsonDocument doc = JsonDocument.Parse("{\"e2\":500}");
            HttpResponseMessage encResp = await client1.PostAsJsonAsync("/api/compliance/encrypt-record", new EncryptRecordRequest
            {
                SubjectId = subjectId,
                Payload = doc.RootElement.Clone()
            });
            Assert.Equal(HttpStatusCode.OK, encResp.StatusCode);
        }

        ClearSqlitePool(sqlitePath);
        File.Copy(sqlitePath, sqliteBackup, overwrite: true);
        Assert.DoesNotContain(host1Logs, l => l.Contains("Subject erasure reconciliation re-applied", StringComparison.Ordinal));

        // 2. Host 2 crypto-shreds S; dispose and restore the pre-erasure database copy
        ConcurrentQueue<string> host2Logs = new();
        using (WebApplicationFactory<Program> base2 = new())
        using (WebApplicationFactory<Program> factory2 = ConfigureFactory(base2, sharedDir, exportDir, keysDir, host2Logs))
        using (HttpClient client2 = factory2.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client2, factory2);
            HttpResponseMessage shredResp = await client2.PostAsJsonAsync("/api/compliance/crypto-shred", new CryptoShredRequest
            {
                SubjectId = subjectId
            });
            Assert.Equal(HttpStatusCode.OK, shredResp.StatusCode);
        }

        ClearSqlitePool(sqlitePath);
        File.Copy(sqliteBackup, sqlitePath, overwrite: true);
        Assert.DoesNotContain(host2Logs, l => l.Contains("Subject erasure reconciliation re-applied", StringComparison.Ordinal));

        // 3. Host 3 starts over the restored DB + live journal: encrypt-record returns 400 with ErasedMessage, and Warning is logged once
        ConcurrentQueue<string> host3Logs = new();
        using (WebApplicationFactory<Program> base3 = new())
        using (WebApplicationFactory<Program> factory3 = ConfigureFactory(base3, sharedDir, exportDir, keysDir, host3Logs))
        using (HttpClient client3 = factory3.CreateClient())
        {
            TestFixturesHelper.ConfigureAuthenticatedClient(client3, factory3);
            using JsonDocument doc = JsonDocument.Parse("{\"e2\":500}");
            HttpResponseMessage encAfterRestore = await client3.PostAsJsonAsync("/api/compliance/encrypt-record", new EncryptRecordRequest
            {
                SubjectId = subjectId,
                Payload = doc.RootElement.Clone()
            });
            Assert.Equal(HttpStatusCode.BadRequest, encAfterRestore.StatusCode);

            IKeyManagementService kms3 = factory3.Services.GetRequiredService<IKeyManagementService>();
            SubjectKeyProfile tomb = kms3.GetSubjectKey(subjectId)!;
            JsonElement problem = await encAfterRestore.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                SubjectKeyUnavailableException.ErasedMessage(subjectId, tomb.ShreddedAtUtc!.Value),
                problem.GetProperty("detail").GetString());

            string[] warnings = host3Logs.Where(l => l.Contains("Subject erasure reconciliation re-applied 1 erasures and restored 0 erasure records", StringComparison.Ordinal)).ToArray();
            Assert.Single(warnings);
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
    /// Simulates restoring a pre-erasure PostgreSQL backup by replacing the tombstone row with the captured active row.
    /// </summary>
    /// <param name="host">The PostgreSQL-backed test host.</param>
    /// <param name="captured">The active row captured before erasure.</param>
    /// <returns>A task that completes when the active row has been restored.</returns>
    private static async Task RestoreActiveRowInPostgresAsync(SubjectKeyTestHost host, SubjectKeyRow captured)
    {
        await using WeaverDbSession session = await host.ConnectionFactory.OpenAsync();
        await using DbCommand cmd = session.CreateCommand(
            $"UPDATE weaver_subject_keys SET key_id = @kid, public_key_base64 = @pub, sealed_private_key = @priv, is_shredded = {session.Dialect.FalseLiteral}, shredded_at_utc = NULL WHERE subject_key = @k");
        WeaverDbParameters.Add(cmd, "@kid", captured.KeyId!);
        WeaverDbParameters.Add(cmd, "@pub", captured.PublicKeyBase64!);
        WeaverDbParameters.Add(cmd, "@priv", captured.SealedPrivateKey!);
        WeaverDbParameters.Add(cmd, "@k", captured.SubjectKey);
        await cmd.ExecuteNonQueryAsync();
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
