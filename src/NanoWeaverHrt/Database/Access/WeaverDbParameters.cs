using System.Data.Common;

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
    /// <param name="name">The placeholder name used in the SQL text, including its leading marker.</param>
    /// <param name="value">The value bound to the placeholder.</param>
    /// <returns>The attached parameter.</returns>
    public static DbParameter Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Like <see cref="Add"/>, but binds a database NULL when the value is null.
    /// </summary>
    /// <param name="command">The command the parameter is attached to.</param>
    /// <param name="name">The placeholder name used in the SQL text, including its leading marker.</param>
    /// <param name="value">The value to bind, or null for a database NULL.</param>
    /// <returns>The attached parameter.</returns>
    public static DbParameter AddNullable(DbCommand command, string name, object? value) =>
        Add(command, name, value ?? DBNull.Value);
}
