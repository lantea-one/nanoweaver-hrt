namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Names the database engine a connection targets. A "backend" is simply which database
/// program stores the data: the embedded single-file SQLite engine, or a PostgreSQL server.
/// </summary>
public enum WeaverDatabaseBackend
{
    /// <summary>The embedded SQLite engine that stores everything in one local file.</summary>
    Sqlite = 0,

    /// <summary>A networked PostgreSQL server.</summary>
    PostgreSql = 1
}
