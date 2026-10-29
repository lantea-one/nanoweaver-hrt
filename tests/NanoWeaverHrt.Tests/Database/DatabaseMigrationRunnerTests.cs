using System.Collections;
using System.Data;
using System.Data.Common;
using NanoWeaverHrt.Database.Migrations;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying <see cref="DatabaseMigrationRunner"/> table creation and record upsert SQL generation across dialects.
/// </summary>
public sealed class DatabaseMigrationRunnerTests
{
    /// <summary>
    /// Test migration for verifying runner execution across dialects.
    /// </summary>
    private sealed class TestDialectMigration : DatabaseMigrationBase
    {
        /// <summary>Gets the migration version.</summary>
        public override long Version => 42;

        /// <summary>Gets the migration name.</summary>
        public override string Name => "042_DialectTest";

        /// <summary>
        /// Returns empty SQL DDL for test.
        /// </summary>
        /// <param name="dialect">Database dialect.</param>
        /// <returns>Empty string.</returns>
        public override string GetSql(string dialect) => string.Empty;
    }

    /// <summary>
    /// Test double recording database commands for migration runner assertion.
    /// </summary>
    private sealed class RecordingDbConnection : DbConnection
    {
        /// <summary>Gets the list of created commands.</summary>
        public List<RecordingDbCommand> CreatedCommands { get; } = new();

        /// <summary>Gets or sets the applied version numbers returned by reader queries.</summary>
        public List<long> AppliedVersionsToReturn { get; set; } = new();

        /// <summary>
        /// Creates a new recording command.
        /// </summary>
        /// <returns>A new <see cref="RecordingDbCommand"/> instance.</returns>
        protected override DbCommand CreateDbCommand()
        {
            RecordingDbCommand cmd = new(this);
            CreatedCommands.Add(cmd);
            return cmd;
        }

        /// <summary>
        /// Begins a recording database transaction.
        /// </summary>
        /// <param name="isolationLevel">Isolation level.</param>
        /// <returns>A new <see cref="RecordingDbTransaction"/> instance.</returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new RecordingDbTransaction(this);

        /// <summary>Changes the database.</summary>
        /// <param name="databaseName">Database name.</param>
        public override void ChangeDatabase(string databaseName) { }

        /// <summary>Closes the connection.</summary>
        public override void Close() { }

        /// <summary>Opens the connection.</summary>
        public override void Open() { }

        /// <summary>Gets or sets the connection string.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        /// <summary>Gets the database name.</summary>
        public override string Database => "test";

        /// <summary>Gets the connection state.</summary>
        public override ConnectionState State => ConnectionState.Open;

        /// <summary>Gets the data source.</summary>
        public override string DataSource => "memory";

        /// <summary>Gets the server version.</summary>
        public override string ServerVersion => "1.0";
    }

    /// <summary>
    /// Test double recording transactions.
    /// </summary>
    private sealed class RecordingDbTransaction : DbTransaction
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingDbTransaction"/> class.
        /// </summary>
        /// <param name="conn">Associated database connection.</param>
        public RecordingDbTransaction(DbConnection conn) => DbConnection = conn;

        /// <summary>Gets the database connection.</summary>
        protected override DbConnection DbConnection { get; }

        /// <summary>Gets the isolation level.</summary>
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        /// <summary>Commits the transaction.</summary>
        public override void Commit() { }

        /// <summary>Rolls back the transaction.</summary>
        public override void Rollback() { }
    }

    /// <summary>
    /// Test double recording command text and parameters.
    /// </summary>
    private sealed class RecordingDbCommand : DbCommand
    {
        /// <summary>The owner connection.</summary>
        private readonly RecordingDbConnection _conn;

        /// <summary>Parameter collection.</summary>
        private readonly RecordingDbParameterCollection _parameters = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingDbCommand"/> class.
        /// </summary>
        /// <param name="conn">Associated database connection.</param>
        public RecordingDbCommand(RecordingDbConnection conn) => _conn = conn;

        /// <summary>Gets or sets the command text.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        /// <summary>Gets or sets the command timeout.</summary>
        public override int CommandTimeout { get; set; }

        /// <summary>Gets or sets the command type.</summary>
        public override CommandType CommandType { get; set; } = CommandType.Text;

        /// <summary>Gets or sets design-time visibility.</summary>
        public override bool DesignTimeVisible { get; set; }

        /// <summary>Gets or sets updated row source.</summary>
        public override UpdateRowSource UpdatedRowSource { get; set; }

        /// <summary>Gets or sets the database connection.</summary>
        protected override DbConnection? DbConnection { get => _conn; set { } }

        /// <summary>Gets the parameter collection.</summary>
        protected override DbParameterCollection DbParameterCollection => _parameters;

        /// <summary>Gets or sets the transaction.</summary>
        protected override DbTransaction? DbTransaction { get; set; }

        /// <summary>Cancels command execution.</summary>
        public override void Cancel() { }

        /// <summary>Executes non query synchronously.</summary>
        /// <returns>Rows affected.</returns>
        public override int ExecuteNonQuery() => 1;

        /// <summary>
        /// Executes non query asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Rows affected.</returns>
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        /// <summary>Executes scalar query.</summary>
        /// <returns>Scalar result.</returns>
        public override object? ExecuteScalar() => null;

        /// <summary>Prepares the command.</summary>
        public override void Prepare() { }

        /// <summary>Creates a parameter.</summary>
        /// <returns>A new <see cref="RecordingDbParameter"/> instance.</returns>
        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        /// <summary>
        /// Executes reader synchronously.
        /// </summary>
        /// <param name="behavior">Command behavior.</param>
        /// <returns>A reader.</returns>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => new RecordingDbDataReader(_conn.AppliedVersionsToReturn);

        /// <summary>
        /// Executes reader asynchronously.
        /// </summary>
        /// <param name="behavior">Command behavior.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A reader task.</returns>
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult<DbDataReader>(new RecordingDbDataReader(_conn.AppliedVersionsToReturn));
    }

    /// <summary>
    /// Test double parameter.
    /// </summary>
    private sealed class RecordingDbParameter : DbParameter
    {
        /// <summary>Gets or sets the parameter database type.</summary>
        public override DbType DbType { get; set; }

        /// <summary>Gets or sets the direction.</summary>
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        /// <summary>Gets or sets a value indicating whether the parameter is nullable.</summary>
        public override bool IsNullable { get; set; }

        /// <summary>Gets or sets the parameter name.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        /// <summary>Gets or sets the source column.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        /// <summary>Gets or sets the parameter value.</summary>
        public override object? Value { get; set; }

        /// <summary>Gets or sets a value indicating whether null is mapped.</summary>
        public override bool SourceColumnNullMapping { get; set; }

        /// <summary>Gets or sets the parameter size.</summary>
        public override int Size { get; set; }

        /// <summary>Resets the database type.</summary>
        public override void ResetDbType() { }
    }

    /// <summary>
    /// Test double parameter collection.
    /// </summary>
    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        /// <summary>The internal parameter list.</summary>
        private readonly List<DbParameter> _list = new();

        /// <summary>Gets the collection count.</summary>
        public override int Count => _list.Count;

        /// <summary>Gets the synchronization root.</summary>
        public override object SyncRoot => ((ICollection)_list).SyncRoot;

        /// <summary>
        /// Adds a parameter.
        /// </summary>
        /// <param name="value">Parameter to add.</param>
        /// <returns>Index of added parameter.</returns>
        public override int Add(object? value) { _list.Add((DbParameter)value!); return _list.Count - 1; }

        /// <summary>
        /// Adds an array of parameters.
        /// </summary>
        /// <param name="values">Array of parameters.</param>
        public override void AddRange(Array values) { foreach (object v in values) Add(v); }

        /// <summary>Clears the collection.</summary>
        public override void Clear() => _list.Clear();

        /// <summary>
        /// Checks if collection contains parameter.
        /// </summary>
        /// <param name="value">Parameter to check.</param>
        /// <returns>True if contained.</returns>
        public override bool Contains(object? value) => value is DbParameter p && _list.Contains(p);

        /// <summary>
        /// Checks if collection contains parameter name.
        /// </summary>
        /// <param name="value">Parameter name.</param>
        /// <returns>True if contained.</returns>
        public override bool Contains(string value) => _list.Any(p => p.ParameterName == value);

        /// <summary>
        /// Copies collection to array.
        /// </summary>
        /// <param name="array">Target array.</param>
        /// <param name="index">Target index.</param>
        public override void CopyTo(Array array, int index) => ((ICollection)_list).CopyTo(array, index);

        /// <summary>
        /// Gets collection enumerator.
        /// </summary>
        /// <returns>An enumerator.</returns>
        public override IEnumerator GetEnumerator() => _list.GetEnumerator();

        /// <summary>
        /// Gets parameter by index.
        /// </summary>
        /// <param name="index">Parameter index.</param>
        /// <returns>Parameter at index.</returns>
        protected override DbParameter GetParameter(int index) => _list[index];

        /// <summary>
        /// Gets parameter by name.
        /// </summary>
        /// <param name="parameterName">Parameter name.</param>
        /// <returns>Parameter matching name.</returns>
        protected override DbParameter GetParameter(string parameterName) => _list.First(p => p.ParameterName == parameterName);

        /// <summary>
        /// Gets index of parameter.
        /// </summary>
        /// <param name="value">Parameter instance.</param>
        /// <returns>Zero-based index or -1.</returns>
        public override int IndexOf(object? value) => value is DbParameter p ? _list.IndexOf(p) : -1;

        /// <summary>
        /// Gets index of parameter name.
        /// </summary>
        /// <param name="parameterName">Parameter name.</param>
        /// <returns>Zero-based index or -1.</returns>
        public override int IndexOf(string parameterName) => _list.FindIndex(p => p.ParameterName == parameterName);

        /// <summary>
        /// Inserts parameter at index.
        /// </summary>
        /// <param name="index">Insert index.</param>
        /// <param name="value">Parameter instance.</param>
        public override void Insert(int index, object? value) => _list.Insert(index, (DbParameter)value!);

        /// <summary>
        /// Removes parameter.
        /// </summary>
        /// <param name="value">Parameter to remove.</param>
        public override void Remove(object? value)
        {
            if (value is DbParameter p) _list.Remove(p);
        }

        /// <summary>
        /// Removes parameter at index.
        /// </summary>
        /// <param name="index">Index to remove.</param>
        public override void RemoveAt(int index) => _list.RemoveAt(index);

        /// <summary>
        /// Removes parameter by name.
        /// </summary>
        /// <param name="parameterName">Parameter name.</param>
        public override void RemoveAt(string parameterName)
        {
            int idx = IndexOf(parameterName);
            if (idx >= 0) RemoveAt(idx);
        }

        /// <summary>
        /// Sets parameter at index.
        /// </summary>
        /// <param name="index">Index to set.</param>
        /// <param name="value">Parameter value.</param>
        protected override void SetParameter(int index, DbParameter value) => _list[index] = value;

        /// <summary>
        /// Sets parameter by name.
        /// </summary>
        /// <param name="parameterName">Parameter name.</param>
        /// <param name="value">Parameter value.</param>
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            int idx = IndexOf(parameterName);
            if (idx >= 0) _list[idx] = value;
            else Add(value);
        }
    }

    /// <summary>
    /// Test double data reader returning version numbers.
    /// </summary>
    private sealed class RecordingDbDataReader : DbDataReader
    {
        /// <summary>Versions list.</summary>
        private readonly List<long> _versions;

        /// <summary>Current reader index.</summary>
        private int _index = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingDbDataReader"/> class.
        /// </summary>
        /// <param name="versions">Versions list.</param>
        public RecordingDbDataReader(List<long> versions) => _versions = versions;

        /// <summary>Gets depth of nesting.</summary>
        public override int Depth => 0;

        /// <summary>Advances reader.</summary>
        /// <returns>True if more rows exist.</returns>
        public override bool Read() => ++_index < _versions.Count;

        /// <summary>
        /// Advances reader asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if more rows exist.</returns>
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        /// <summary>
        /// Gets 64-bit integer at ordinal.
        /// </summary>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Version number.</returns>
        public override long GetInt64(int ordinal) => _versions[_index];

        /// <summary>Gets field count.</summary>
        public override int FieldCount => 1;

        /// <summary>Gets a value indicating whether rows exist.</summary>
        public override bool HasRows => _versions.Count > 0;

        /// <summary>Gets a value indicating whether reader is closed.</summary>
        public override bool IsClosed => false;

        /// <summary>Gets records affected.</summary>
        public override int RecordsAffected => 0;

        /// <summary>Gets value by ordinal.</summary>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Value.</returns>
        public override object this[int ordinal] => _versions[_index];

        /// <summary>Gets value by column name.</summary>
        /// <param name="name">Column name.</param>
        /// <returns>Value.</returns>
        public override object this[string name] => _versions[_index];

        /// <summary>Gets boolean.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets byte.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets bytes.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <param name="dataOffset">Data offset.</param>
        /// <param name="buffer">Target buffer.</param>
        /// <param name="bufferOffset">Buffer offset.</param>
        /// <param name="length">Length.</param>
        /// <returns>Not supported.</returns>
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        /// <summary>Gets char.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override char GetChar(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets chars.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <param name="dataOffset">Data offset.</param>
        /// <param name="buffer">Target buffer.</param>
        /// <param name="bufferOffset">Buffer offset.</param>
        /// <param name="length">Length.</param>
        /// <returns>Not supported.</returns>
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        /// <summary>Gets data type name.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Data type name.</returns>
        public override string GetDataTypeName(int ordinal) => "BIGINT";

        /// <summary>Gets date time.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets decimal.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets double.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets field type.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Type of long.</returns>
        public override Type GetFieldType(int ordinal) => typeof(long);

        /// <summary>Gets float.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets GUID.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets short.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets integer.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Integer value.</returns>
        public override int GetInt32(int ordinal) => (int)_versions[_index];

        /// <summary>Gets column name.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Column name.</returns>
        public override string GetName(int ordinal) => "version";

        /// <summary>Gets ordinal.</summary>
        /// <param name="name">Name.</param>
        /// <returns>Ordinal index.</returns>
        public override int GetOrdinal(string name) => 0;

        /// <summary>Gets string representation.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>String value.</returns>
        public override string GetString(int ordinal) => _versions[_index].ToString();

        /// <summary>Gets value.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Value object.</returns>
        public override object GetValue(int ordinal) => _versions[_index];

        /// <summary>Gets values array.</summary>
        /// <param name="values">Target array.</param>
        /// <returns>Number of values.</returns>
        public override int GetValues(object[] values) { values[0] = _versions[_index]; return 1; }

        /// <summary>Checks if DB null.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>False.</returns>
        public override bool IsDBNull(int ordinal) => false;

        /// <summary>Moves to next result.</summary>
        /// <returns>False.</returns>
        public override bool NextResult() => false;

        /// <summary>Gets enumerator.</summary>
        /// <returns>Not supported.</returns>
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    /// <summary>
    /// Verifies tracking table DDL generation across supported dialects in <see cref="DatabaseMigrationRunner.EnsureTrackingTableAsync"/>.
    /// </summary>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="expectedType">Expected column type fragment in generated DDL.</param>
    [Theory]
    [InlineData("postgresql", "TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP")]
    [InlineData("duckdb", "TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP")]
    [InlineData("mysql", "DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)")]
    [InlineData("mariadb", "DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)")]
    [InlineData("sqlite", "applied_at_utc TEXT NOT NULL")]
    public async Task EnsureTrackingTableAsync_GeneratesDialectAppropriateDdl(string dialect, string expectedType)
    {
        // WHAT: Tests tracking table DDL statements across all supported database engine dialects.
        // WHY:  Ensures proper column type emission for 64-bit IDs and timestamps on PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite.
        // HOW:  Runs EnsureTrackingTableAsync against a recording connection and verifies the executed CommandText.
        using DatabaseMigrationRunner runner = new();
        RecordingDbConnection conn = new();

        await runner.EnsureTrackingTableAsync(conn, dialect);

        Assert.Single(conn.CreatedCommands);
        string executedSql = conn.CreatedCommands[0].CommandText;
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_schema_migrations", executedSql);
        Assert.Contains(expectedType, executedSql);
    }

    /// <summary>
    /// Verifies migration upsert SQL generation and timestamp parameter coercion across dialects in <see cref="DatabaseMigrationRunner.ApplyPendingMigrationsAsync"/>.
    /// </summary>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="expectedUpsertFragment">Expected upsert clause in the executed SQL.</param>
    /// <param name="expectedTimestampType">Expected CLR type of the applied_at_utc parameter.</param>
    [Theory]
    [InlineData("postgresql", "ON CONFLICT (version) DO NOTHING", typeof(DateTime))]
    [InlineData("duckdb", "ON CONFLICT (version) DO NOTHING", typeof(DateTime))]
    [InlineData("mysql", "ON DUPLICATE KEY UPDATE version = version", typeof(DateTime))]
    [InlineData("mariadb", "ON DUPLICATE KEY UPDATE version = version", typeof(DateTime))]
    [InlineData("sqlite", "INSERT OR REPLACE INTO weaver_schema_migrations", typeof(string))]
    public async Task ApplyPendingMigrationsAsync_GeneratesDialectAppropriateUpsertAndTimestamp(
        string dialect,
        string expectedUpsertFragment,
        Type expectedTimestampType)
    {
        // WHAT: Tests record upsert SQL and timestamp parameter coercion across dialects during migration execution.
        // WHY:  PostgreSQL and DuckDB require ON CONFLICT, MySQL/MariaDB requires ON DUPLICATE KEY, SQLite requires INSERT OR REPLACE,
        //       and timestamp parameter binding types must match each driver's requirements.
        // HOW:  Runs ApplyPendingMigrationsAsync with a single pending migration and checks the recording command properties.
        using DatabaseMigrationRunner runner = new(new IDatabaseMigration[] { new TestDialectMigration() });
        RecordingDbConnection conn = new();

        int applied = await runner.ApplyPendingMigrationsAsync(conn, dialect);

        Assert.Equal(1, applied);
        // The first command created is from EnsureTrackingTableAsync, the second is the SELECT for applied versions,
        // and the third is the recordCmd for inserting migration history.
        Assert.True(conn.CreatedCommands.Count >= 3);
        RecordingDbCommand recordCmd = conn.CreatedCommands[^1];

        Assert.Contains(expectedUpsertFragment, recordCmd.CommandText);
        Assert.Equal(3, recordCmd.Parameters.Count);
        Assert.Equal(42L, recordCmd.Parameters["@v"].Value);
        Assert.Equal("042_DialectTest", recordCmd.Parameters["@n"].Value);

        object? timestampValue = recordCmd.Parameters["@t"].Value;
        Assert.NotNull(timestampValue);
        Assert.IsType(expectedTimestampType, timestampValue);

        if (expectedTimestampType == typeof(DateTime))
        {
            Assert.Equal(DateTimeKind.Utc, ((DateTime)timestampValue).Kind);
        }
    }
}
