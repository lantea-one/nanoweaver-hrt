using System.Data.Common;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// An open database connection paired with the <see cref="ISqlDialect"/> of the engine it talks to.
/// Replaces the former (connection, dialect string) tuple.
/// </summary>
public sealed class WeaverDbSession : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbSession"/> class.
    /// </summary>
    /// <param name="connection">An already-open connection owned by this session from now on.</param>
    /// <param name="dialect">The dialect matching the connection's engine.</param>
    internal WeaverDbSession(DbConnection connection, ISqlDialect dialect)
    {
        Connection = connection;
        Dialect = dialect;
    }

    /// <summary>Gets the open connection.</summary>
    public DbConnection Connection { get; }

    /// <summary>Gets the dialect for the connection's engine.</summary>
    public ISqlDialect Dialect { get; }

    /// <summary>
    /// Creates a command on this session's connection with the given SQL text.
    /// </summary>
    /// <param name="sql">The SQL text; must not be null or whitespace.</param>
    /// <returns>A new command the caller must dispose.</returns>
    public DbCommand CreateCommand(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>
    /// Disposes the underlying connection. Safe to call more than once.
    /// </summary>
    /// <returns>A task that completes when the connection has been released.</returns>
    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    /// <summary>
    /// Synchronously disposes the underlying connection. Safe to call more than once.
    /// </summary>
    public void Dispose() => Connection.Dispose();
}
