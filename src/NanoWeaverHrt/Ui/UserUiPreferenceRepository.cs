using System.Data.Common;
using NanoWeaverHrt.Database.Access;

namespace NanoWeaverHrt.Ui;

/// <summary>
/// Multi-engine relational repository persisting per-user UI display preferences in <c>weaver_user_preferences</c>
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Data access implementation of <see cref="IUserUiPreferenceRepository"/> backed by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 47 (P245): Provides dialect-neutral persistence for per-user Simple vs. Advanced display settings.
/// </para>
/// <para>
/// <strong>How it works:</strong> Normalizes usernames consistently with <c>UserRepository</c>, serializes writes behind a semaphore,
/// and delegates SQL upsert syntax and timestamp conversions to <see cref="ISqlDialect"/> and <see cref="WeaverDbValues"/>.
/// </para>
/// </remarks>
public sealed class UserUiPreferenceRepository : IUserUiPreferenceRepository, IDisposable
{
    /// <summary>
    /// Multi-engine database connection factory providing dialect-configured sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Concurrency lock serializing preference mutations against SQLite and embedded engines.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="UserUiPreferenceRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public UserUiPreferenceRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Validates and stores the multi-engine connection factory dependency.
        // WHY:  Prevents delayed null reference errors when reading or saving user UI preferences.
        // HOW:  Guards connectionFactory via ArgumentNullException.ThrowIfNull.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<UserUiPreferenceRecord?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_user_preferences for the normalized username and parses the saved display mode.
        // WHY:  Allows MainLayout and REST clients to restore the user's chosen visual complexity across logins.
        // HOW:  Opens a dialect session inside an explicit block, binds lowercase username, and reads mode + timestamp.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string normalized = NormalizeUsername(username);
        UserUiPreferenceRecord? record = null;

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT display_mode, updated_at_utc FROM weaver_user_preferences WHERE username = {session.Dialect.Parameter("u")}");
            WeaverDbParameters.Add(cmd, "u", normalized);
            await using DbDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
            {
                UiDisplayMode mode = ParseDisplayMode(rdr.GetString(0));
                DateTimeOffset updatedAtUtc = WeaverDbValues.ReadTimestamp(rdr, 1);
                record = new UserUiPreferenceRecord
                {
                    Username = normalized,
                    DisplayMode = mode,
                    UpdatedAtUtc = updatedAtUtc
                };
            }
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<UserUiPreferenceRecord> UpsertAsync(string username, UiDisplayMode displayMode, CancellationToken ct = default)
    {
        // WHAT: Atomically saves or updates the user's preferred UiDisplayMode in weaver_user_preferences.
        // WHY:  Supports idempotent single-click toggling between Simple and Advanced display across all 4 SQL backends.
        // HOW:  Validates username and enum value, builds dialect upsert SQL under _lock, and returns the saved record.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (!Enum.IsDefined(displayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(displayMode), displayMode, "Display mode must be Simple or Advanced.");
        }

        string normalized = NormalizeUsername(username);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string sql = session.Dialect.BuildUpsertSql(
                    "weaver_user_preferences",
                    ["username", "display_mode", "updated_at_utc"],
                    ["username"],
                    ["display_mode", "updated_at_utc"],
                    ["u", "mode", "updated"]);
                await using DbCommand cmd = session.CreateCommand(sql);
                WeaverDbParameters.Add(cmd, "u", normalized);
                WeaverDbParameters.Add(cmd, "mode", displayMode.ToString());
                WeaverDbParameters.Add(cmd, "updated", session.Dialect.TimestampValue(nowUtc));
                _ = await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }

        return new UserUiPreferenceRecord
        {
            Username = normalized,
            DisplayMode = displayMode,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteByUsernameAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Deletes the saved personal UI display preference for the specified username.
        // WHY:  Lets users revert to the administrator node default and enables clean account removal.
        // HOW:  Executes a parameterized DELETE statement inside a locked, block-scoped database session.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string normalized = NormalizeUsername(username);
        bool deleted;

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand(
                    $"DELETE FROM weaver_user_preferences WHERE username = {session.Dialect.Parameter("u")}");
                WeaverDbParameters.Add(cmd, "u", normalized);
                deleted = await cmd.ExecuteNonQueryAsync(ct) > 0;
            }
        }
        finally
        {
            _lock.Release();
        }

        return deleted;
    }

    /// <summary>
    /// Normalizes a username to trimmed lowercase matching <c>UserRepository</c> key conventions.
    /// </summary>
    /// <param name="username">Raw username input supplied by the caller.</param>
    /// <returns>Trimmed lowercase username string.</returns>
    private static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    /// <summary>
    /// Parses a stored display mode string safely, falling back to <see cref="UiDisplayMode.Simple"/> on unknown tokens.
    /// </summary>
    /// <param name="rawMode">Raw stored string from the <c>display_mode</c> column.</param>
    /// <returns>The parsed <see cref="UiDisplayMode"/> value.</returns>
    private static UiDisplayMode ParseDisplayMode(string rawMode) =>
        Enum.TryParse(rawMode, ignoreCase: true, out UiDisplayMode parsed) && Enum.IsDefined(parsed)
            ? parsed
            : UiDisplayMode.Simple;

    /// <summary>
    /// Releases the internal write synchronization lock.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
