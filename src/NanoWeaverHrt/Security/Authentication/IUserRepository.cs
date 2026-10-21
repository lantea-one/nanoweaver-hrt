namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative persistence repository for registered user accounts, enforcing database-first precedence
/// across PostgreSQL (pg-duckdb) and embedded SQLite fallback.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Initializes schema tables for user account storage if they do not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing initialization.</returns>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Determines whether at least one active Administrator user account is registered in the database.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if at least one Administrator exists; otherwise, <c>false</c>.</returns>
    Task<bool> HasAnyAdministratorAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a user account by its username.
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user account, or null if not found.</returns>
    Task<UserAccount?> GetUserAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered user accounts from the database.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only collection of user accounts.</returns>
    Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists a new user account to the database.
    /// </summary>
    /// <param name="user">The user account to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the persist operation.</returns>
    Task CreateUserAsync(UserAccount user, CancellationToken ct = default);

    /// <summary>
    /// Updates the last login timestamp for an authenticated user.
    /// </summary>
    /// <param name="username">The username whose login timestamp is updating.</param>
    /// <param name="loginTimeUtc">The timestamp of authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the update operation.</returns>
    Task UpdateLastLoginAsync(string username, DateTimeOffset loginTimeUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes a user account from the database.
    /// </summary>
    /// <param name="username">The username of the account to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the user was found and deleted; otherwise, <c>false</c>.</returns>
    Task<bool> DeleteUserAsync(string username, CancellationToken ct = default);
}
