using System.Data.Common;
using DuckDB.NET.Data;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Shared helpers for attaching named parameters to a command, replacing four private copies.
/// </summary>
public static class WeaverDbParameters
{
    /// <summary>
    /// Creates a parameter, sets its name and value, and attaches it to the command.
    /// </summary>
    /// <param name="command">The command the parameter is attached to.</param>
    /// <param name="name">The placeholder name used in the SQL text, with or without marker prefix.</param>
    /// <param name="value">The value bound to the placeholder.</param>
    /// <returns>The attached parameter.</returns>
    public static DbParameter Add(DbCommand command, string name, object value)
    {
        // WHAT: Creates, configures, and appends a named parameter to the target command.
        // WHY:  DuckDB.NET requires unprefixed parameter names on DuckDBParameter.ParameterName,
        //       while SQLite, PostgreSQL (Npgsql), and MySQL (MySqlConnector) require the '@' marker prefix.
        // HOW:  Strips leading '@' or '$' if command is DuckDBCommand; otherwise ensures a leading '@'.
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = command is DuckDBCommand
            ? name.TrimStart('@', '$')
            : (name.StartsWith('@') ? name : $"@{name.TrimStart('$')}");
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Like <see cref="Add"/>, but binds a database NULL when the value is null.
    /// </summary>
    /// <param name="command">The command the parameter is attached to.</param>
    /// <param name="name">The placeholder name used in the SQL text, with or without marker prefix.</param>
    /// <param name="value">The value to bind, or null for a database NULL.</param>
    /// <returns>The attached parameter.</returns>
    public static DbParameter AddNullable(DbCommand command, string name, object? value) =>
        Add(command, name, value ?? DBNull.Value);
}
