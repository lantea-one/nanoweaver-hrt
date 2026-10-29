using NanoWeaverHrt.Database.Access;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Unit tests verifying the properties and coercion behaviors of <see cref="DuckDbDialect"/>.
/// </summary>
public sealed class DuckDbDialectTests
{
    /// <summary>
    /// Verifies that <see cref="DuckDbDialect.Instance"/> exposes the expected metadata properties and singleton instance.
    /// </summary>
    [Fact]
    public void DuckDbDialect_PropertiesAndSingleton_ExposesExpectedMetadata()
    {
        // WHAT: Asserts metadata properties on DuckDbDialect.Instance.
        // WHY:  Ensures the dialect identifier, backend enum, and SQL boolean literals match DuckDB SQL dialect specifications.
        // HOW:  Compares Name, Backend, TrueLiteral, and FalseLiteral against expected constants.
        ISqlDialect dialect = DuckDbDialect.Instance;

        Assert.NotNull(dialect);
        Assert.Same(dialect, DuckDbDialect.Instance);
        Assert.Equal("duckdb", dialect.Name);
        Assert.Equal(WeaverDatabaseBackend.DuckDb, dialect.Backend);
        Assert.Equal("TRUE", dialect.TrueLiteral);
        Assert.Equal("FALSE", dialect.FalseLiteral);
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbDialect.BooleanValue"/> returns native boolean values for both true and false.
    /// </summary>
    [Fact]
    public void DuckDbDialect_BooleanValue_ReturnsNativeBoolean()
    {
        // WHAT: Validates parameter coercion for boolean values in DuckDB.
        // WHY:  DuckDB supports native boolean types, so values must be passed as native bool.
        // HOW:  Passes true and false to BooleanValue and asserts the returned typed objects.
        ISqlDialect dialect = DuckDbDialect.Instance;

        object trueVal = dialect.BooleanValue(true);
        object falseVal = dialect.BooleanValue(false);

        Assert.IsType<bool>(trueVal);
        Assert.True((bool)trueVal);
        Assert.IsType<bool>(falseVal);
        Assert.False((bool)falseVal);
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbDialect.TimestampValue"/> converts a <see cref="DateTimeOffset"/> into a UTC <see cref="DateTime"/>.
    /// </summary>
    [Fact]
    public void DuckDbDialect_TimestampValue_ReturnsUtcDateTime()
    {
        // WHAT: Validates timestamp parameter coercion for DuckDB.
        // WHY:  DuckDB TIMESTAMP columns accept UTC DateTime instances from .NET drivers.
        // HOW:  Converts a non-UTC DateTimeOffset and verifies the result equals UtcDateTime with DateTimeKind.Utc.
        ISqlDialect dialect = DuckDbDialect.Instance;
        DateTimeOffset timestamp = new(2026, 10, 29, 14, 30, 0, TimeSpan.FromHours(-4));

        object result = dialect.TimestampValue(timestamp);

        Assert.IsType<DateTime>(result);
        DateTime dt = (DateTime)result;
        Assert.Equal(timestamp.UtcDateTime, dt);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbDialect.TimestampValueOrNull"/> handles both present timestamps and null.
    /// </summary>
    [Fact]
    public void DuckDbDialect_TimestampValueOrNull_HandlesValueAndNull()
    {
        // WHAT: Validates optional timestamp coercion for DuckDB.
        // WHY:  Nullable database columns require DBNull.Value when null and UTC DateTime when populated.
        // HOW:  Passes a DateTimeOffset? with a value and null, asserting UtcDateTime and DBNull.Value respectively.
        ISqlDialect dialect = DuckDbDialect.Instance;
        DateTimeOffset timestamp = new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);

        object populated = dialect.TimestampValueOrNull(timestamp);
        object nullResult = dialect.TimestampValueOrNull(null);

        Assert.IsType<DateTime>(populated);
        Assert.Equal(timestamp.UtcDateTime, (DateTime)populated);
        Assert.Same(DBNull.Value, nullResult);
    }
}
