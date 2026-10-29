using NanoWeaverHrt.Database.Access;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying the properties and coercion behaviors of <see cref="MySqlDialect"/>.
/// </summary>
public sealed class MySqlDialectTests
{
    /// <summary>
    /// Verifies that <see cref="MySqlDialect.Instance"/> exposes the expected metadata properties and singleton instance.
    /// </summary>
    [Fact]
    public void MySqlDialect_PropertiesAndSingleton_ExposesExpectedMetadata()
    {
        // WHAT: Asserts metadata properties on MySqlDialect.Instance.
        // WHY:  Ensures dialect identifier, backend enum, and SQL boolean literals match MySQL conventions (1 and 0).
        // HOW:  Compares Name, Backend, TrueLiteral, and FalseLiteral against expected constants.
        ISqlDialect dialect = MySqlDialect.Instance;

        Assert.NotNull(dialect);
        Assert.Same(dialect, MySqlDialect.Instance);
        Assert.Equal("mysql", dialect.Name);
        Assert.Equal(WeaverDatabaseBackend.MySql, dialect.Backend);
        Assert.Equal("1", dialect.TrueLiteral);
        Assert.Equal("0", dialect.FalseLiteral);
    }

    /// <summary>
    /// Verifies that <see cref="MySqlDialect.BooleanValue"/> returns integer 1 for true and 0 for false.
    /// </summary>
    [Fact]
    public void MySqlDialect_BooleanValue_ReturnsIntegerFlags()
    {
        // WHAT: Validates parameter coercion for boolean values in MySQL / MariaDB.
        // WHY:  MySQL uses TINYINT(1) without a native boolean type, requiring integer representations.
        // HOW:  Passes true and false to BooleanValue and asserts the returned integer values.
        ISqlDialect dialect = MySqlDialect.Instance;

        object trueVal = dialect.BooleanValue(true);
        object falseVal = dialect.BooleanValue(false);

        Assert.IsType<int>(trueVal);
        Assert.Equal(1, (int)trueVal);
        Assert.IsType<int>(falseVal);
        Assert.Equal(0, (int)falseVal);
    }

    /// <summary>
    /// Verifies that <see cref="MySqlDialect.TimestampValue"/> converts a <see cref="DateTimeOffset"/> into a UTC <see cref="DateTime"/>.
    /// </summary>
    [Fact]
    public void MySqlDialect_TimestampValue_ReturnsUtcDateTime()
    {
        // WHAT: Validates timestamp parameter coercion for MySQL.
        // WHY:  MySQL DATETIME columns accept UTC DateTime instances from .NET drivers.
        // HOW:  Converts a non-UTC DateTimeOffset and verifies the result equals UtcDateTime with DateTimeKind.Utc.
        ISqlDialect dialect = MySqlDialect.Instance;
        DateTimeOffset timestamp = new(2026, 10, 29, 14, 30, 0, TimeSpan.FromHours(-4));

        object result = dialect.TimestampValue(timestamp);

        Assert.IsType<DateTime>(result);
        DateTime dt = (DateTime)result;
        Assert.Equal(timestamp.UtcDateTime, dt);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
    }

    /// <summary>
    /// Verifies that <see cref="MySqlDialect.TimestampValueOrNull"/> handles both present timestamps and null.
    /// </summary>
    [Fact]
    public void MySqlDialect_TimestampValueOrNull_HandlesValueAndNull()
    {
        // WHAT: Validates optional timestamp coercion for MySQL.
        // WHY:  Nullable database columns require DBNull.Value when null and UTC DateTime when populated.
        // HOW:  Passes a DateTimeOffset? with a value and null, asserting UtcDateTime and DBNull.Value respectively.
        ISqlDialect dialect = MySqlDialect.Instance;
        DateTimeOffset timestamp = new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);

        object populated = dialect.TimestampValueOrNull(timestamp);
        object nullResult = dialect.TimestampValueOrNull(null);

        Assert.IsType<DateTime>(populated);
        Assert.Equal(timestamp.UtcDateTime, (DateTime)populated);
        Assert.Same(DBNull.Value, nullResult);
    }
}
