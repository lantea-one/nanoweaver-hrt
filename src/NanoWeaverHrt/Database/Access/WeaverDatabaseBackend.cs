namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Names the database engine a connection targets. A "backend" is simply which database
/// program stores the data: the embedded single-file SQLite engine, a PostgreSQL server,
/// an in-process DuckDB engine, or an enterprise MySQL / MariaDB server.
/// </summary>
public enum WeaverDatabaseBackend
{
    /// <summary>The embedded SQLite engine that stores everything in one local file.</summary>
    Sqlite = 0,

    /// <summary>A networked PostgreSQL server.</summary>
    PostgreSql = 1,

    /// <summary>In-process columnar SQL engine optimized for analytical trajectory workflows.</summary>
    DuckDb = 2,

    /// <summary>Enterprise relational database engine (MySQL / MariaDB) for clustered deployments.</summary>
    MySql = 3
}
