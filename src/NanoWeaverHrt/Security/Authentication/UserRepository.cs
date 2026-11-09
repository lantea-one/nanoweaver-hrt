using System.Data.Common;
using NanoWeaverHrt.Database.Access;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Production repository persisting user accounts in whichever database engine the connection seam selected
/// (PostgreSQL with pg-duckdb, embedded SQLite, DuckDB, or MySQL). A failure of the selected engine is an error, never a quiet switch.
/// </summary>
/// <remarks>
/// Methods that produce a value assign it to a local and return it after the try/finally instead of returning
/// from inside the try. Returning from inside a block that holds `await using` locals makes the compiler emit
/// one never-executed closing-brace line per async state machine, which the un-excluded coverage ratchet counts.
/// </remarks>
public sealed class UserRepository : IUserRepository, IDisposable
{
    /// <summary>
    /// The column list shared by every user SELECT; its order is the contract with <see cref="MapUserFromReader"/>.
    /// </summary>
    private const string UserColumns = "username, role, access_gate, key_algorithm, public_key, created_at_utc, last_login_utc, is_active";

    /// <summary>
    /// Columns list for INSERT operations.
    /// </summary>
    private static readonly string[] InsertColumns =
    [
        "username", "role", "access_gate", "key_algorithm", "public_key", "created_at_utc", "last_login_utc", "is_active"
    ];

    /// <summary>
    /// Conflict key columns for user identity.
    /// </summary>
    private static readonly string[] ConflictColumns = ["username"];

    /// <summary>
    /// Columns updated on username collision during upsert.
    /// </summary>
    private static readonly string[] UpdateColumns =
    [
        "role", "access_gate", "key_algorithm", "public_key", "is_active"
    ];

    /// <summary>
    /// Parameter placeholders corresponding to <see cref="InsertColumns"/>.
    /// </summary>
    private static readonly string[] ParameterNames = ["u", "r", "g", "a", "p", "c", "l", "act"];

    /// <summary>
    /// Thread synchronization lock serializing account mutations and reads.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Connection seam that decides which database engine each call talks to.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory that opens connections to the selected backend.</param>
    public UserRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // WHAT: Ensures the weaver_users schema exists in the active database engine.
        // WHY:  Stores user accounts, roles, access gates, and public keys; a deleted SQLite file must be re-created.
        // HOW:  Opening a session applies every pending migration, so opening and releasing one is the whole job.
        //       The factory re-applies migrations on every open, which keeps the "recreate a vanished database
        //       file" property without the repository knowing any file path.
        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HasAnyAdministratorAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        bool result = false;
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand(
                    $"SELECT COUNT(*) FROM weaver_users WHERE role = 'Administrator' AND is_active = {session.Dialect.TrueLiteral}");
                object? res = await cmd.ExecuteScalarAsync(ct);
                result = Convert.ToInt64(res) > 0;
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<UserAccount?> GetUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await _lock.WaitAsync(ct);
        UserAccount? found = null;
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"SELECT {UserColumns} FROM weaver_users WHERE username = {session.Dialect.Parameter("u")}");
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                found = await reader.ReadAsync(ct) ? MapUserFromReader(reader) : null;
            }
        }
        finally
        {
            _lock.Release();
        }

        return found;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        IReadOnlyList<UserAccount> result = [];
        try
        {
            List<UserAccount> users = new();
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"SELECT {UserColumns} FROM weaver_users ORDER BY username ASC");
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    users.Add(MapUserFromReader(reader));
                }
            }

            result = users;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task CreateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.PublicKeyBase64);

        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string sql = session.Dialect.BuildUpsertSql("weaver_users", InsertColumns, ConflictColumns, UpdateColumns, ParameterNames);
                await using DbCommand cmd = session.CreateCommand(sql);
                BindUser(cmd, session.Dialect, user);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateUserAsync(UserAccount user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.PublicKeyBase64);

        await _lock.WaitAsync(ct);

        // WHAT: Insert-only account creation.
        // WHY:  A check-then-insert would let two concurrent callers both see "absent"; the unique key must decide.
        // HOW:  Engine-specific insert-ignore reports 0 affected rows when the username exists.
        bool result = false;
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                string sql = session.Dialect.BuildInsertIgnoreSql("weaver_users", InsertColumns, ConflictColumns, ParameterNames);
                await using DbCommand cmd = session.CreateCommand(sql);
                BindUser(cmd, session.Dialect, user);
                result = await cmd.ExecuteNonQueryAsync(ct) > 0;
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task UpdateLastLoginAsync(string username, DateTimeOffset loginTimeUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await _lock.WaitAsync(ct);
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"UPDATE weaver_users SET last_login_utc = {session.Dialect.Parameter("l")} WHERE username = {session.Dialect.Parameter("u")}");
                WeaverDbParameters.Add(cmd, "l", session.Dialect.TimestampValue(loginTimeUtc));
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        await _lock.WaitAsync(ct);
        bool result = false;
        try
        {
            {
                await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
                await using DbCommand cmd = session.CreateCommand($"DELETE FROM weaver_users WHERE username = {session.Dialect.Parameter("u")}");
                WeaverDbParameters.Add(cmd, "u", NormalizeUsername(username));
                result = await cmd.ExecuteNonQueryAsync(ct) > 0;
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <summary>
    /// Binds the eight user-row parameters shared by the two INSERT statements.
    /// </summary>
    /// <param name="cmd">The INSERT command to attach parameters to.</param>
    /// <param name="dialect">The dialect that converts booleans and timestamps for the live engine.</param>
    /// <param name="user">The account whose values are bound.</param>
    private static void BindUser(DbCommand cmd, ISqlDialect dialect, UserAccount user)
    {
        // WHAT: Attaches the user's columns as named parameters.
        // WHY:  Both INSERT statements take identical parameters; one binder keeps them from drifting apart.
        // HOW:  Text columns bind directly; the two timestamps and the active flag go through the dialect.
        WeaverDbParameters.Add(cmd, "u", NormalizeUsername(user.Username));
        WeaverDbParameters.Add(cmd, "r", user.Role.ToString());
        WeaverDbParameters.Add(cmd, "g", user.AccessGate.ToString());
        WeaverDbParameters.Add(cmd, "a", user.Algorithm.ToString());
        WeaverDbParameters.Add(cmd, "p", user.PublicKeyBase64);
        WeaverDbParameters.Add(cmd, "c", dialect.TimestampValue(user.CreatedAtUtc));
        WeaverDbParameters.Add(cmd, "l", dialect.TimestampValueOrNull(user.LastLoginUtc));
        WeaverDbParameters.Add(cmd, "act", dialect.BooleanValue(user.IsActive));
    }

    /// <summary>
    /// Maps a database reader record into a strongly-typed <see cref="UserAccount"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the user row to read.</param>
    /// <returns>The user account described by the current row.</returns>
    internal static UserAccount MapUserFromReader(DbDataReader reader)
    {
        // WHAT: Reads all 8 user projection columns from an open DbDataReader row.
        // WHY:  Centralizes mapping between relational schema and UserAccount model.
        // HOW:  Parses enum values and uses WeaverDbValues for dialect-portable timestamp and boolean extraction.
        string username = reader.GetString(0);
        string roleStr = reader.GetString(1);
        string gateStr = reader.GetString(2);
        string algoStr = reader.GetString(3);
        string pubKey = reader.GetString(4);
        DateTimeOffset created = WeaverDbValues.ReadTimestamp(reader, 5);
        DateTimeOffset? lastLogin = WeaverDbValues.ReadNullableTimestamp(reader, 6);
        bool isActive = WeaverDbValues.ReadBoolean(reader, 7);

        Enum.TryParse(roleStr, ignoreCase: true, out UserRole role);
        Enum.TryParse(gateStr, ignoreCase: true, out AccessGate gate);
        Enum.TryParse(algoStr, ignoreCase: true, out KeyAlgorithm algo);

        return new UserAccount
        {
            Username = username,
            Role = role,
            AccessGate = gate,
            Algorithm = algo,
            PublicKeyBase64 = pubKey,
            CreatedAtUtc = created,
            LastLoginUtc = lastLogin,
            IsActive = isActive
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }

    /// <summary>
    /// Normalizes an account username by trimming surrounding whitespace and converting to lowercase invariant form.
    /// </summary>
    /// <param name="username">The raw username supplied by the caller.</param>
    /// <returns>The trimmed, lowercase-normalized username string.</returns>
    private static string NormalizeUsername(string username)
    {
        // WHAT: Trims whitespace and normalizes the username to lowercase invariant characters.
        // WHY:  Prevents mixed-case duplicate accounts (e.g. "Admin" and "admin") from coexisting or shadowing each other.
        // HOW:  Calls Trim().ToLowerInvariant() on the validated non-empty string.
        return username.Trim().ToLowerInvariant();
    }
}
