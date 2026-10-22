using System.Text.RegularExpressions;

namespace NanoWeaverHrt.Security.ExceptionHandling;

/// <summary>
/// Utility sanitizing exception details to prevent sensitive credential, path, and cryptographic key leakage.
/// </summary>
public static class SecureExceptionSanitizer
{
    /// <summary>
    /// Regular expression matching Unix and Windows file system paths.
    /// </summary>
    private static readonly Regex PathRegex = new(@"(\/[a-zA-Z0-9_\.\-]+)+|[a-zA-Z]:\\[a-zA-Z0-9_\.\-\\]+", RegexOptions.Compiled);

    /// <summary>
    /// Regular expression matching connection string key-value pairs that identify or authenticate against a database.
    /// </summary>
    /// <remarks>
    /// Covers (case-insensitive) Password, pwd, Username, User Id, uid, User, Database, Initial Catalog, Server, Host,
    /// Data Source, Port, Application Name, and Persist Security Info. Alternation is first-match-wins, so longer keys
    /// (username, user id) precede the shorter key they contain (user). The left lookbehind stops a key from matching
    /// inside an unrelated word such as "superuser".
    /// </remarks>
    private static readonly Regex ConnectionStringRegex = new(@"(?<![A-Za-z])(Password|pwd|username|user id|uid|user|database|initial catalog|server|host|data source|port|application name|persist security info)\s*=\s*[^;]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Regular expression matching PEM cryptographic key envelopes.
    /// </summary>
    private static readonly Regex PemKeyRegex = new(@"-----BEGIN[^\-]+-----[A-Za-z0-9+/=\s]+-----END[^\-]+-----", RegexOptions.Compiled);

    /// <summary>
    /// Regular expression matching UNC network share paths such as '\\fileserver\weaver-data\patients'.
    /// </summary>
    /// <remarks>
    /// <see cref="PathRegex"/> only recognizes rooted Unix paths and drive-letter Windows paths, so a UNC
    /// share — the normal shape of a clustered simulation volume — previously passed through a 4xx detail intact.
    /// </remarks>
    private static readonly Regex UncPathRegex = new(@"\\\\[A-Za-z0-9_\.\-]+(\\[A-Za-z0-9_\.\-\$]+)+", RegexOptions.Compiled);

    /// <summary>
    /// Regular expression matching SQL statement fragments that would disclose internal schema or query structure.
    /// </summary>
    /// <remarks>
    /// Prime Directive 14 names "internal SQL fragments" explicitly. Each alternative requires two SQL tokens
    /// (for example SELECT paired with FROM, or UPDATE paired with SET) so that ordinary English prose using the
    /// word "update" or "select" is not redacted. A match is consumed up to the next statement terminator.
    /// </remarks>
    private static readonly Regex SqlFragmentRegex = new(
        @"\b(?:select\b[\s\S]*?\bfrom\b|insert\s+(?:or\s+replace\s+)?into\b|update\b[\s\S]*?\bset\b|delete\s+from\b|create\s+(?:unique\s+)?(?:table|index)\b|alter\s+table\b|drop\s+(?:table|index)\b|truncate\s+table\b|on\s+conflict\b)[^;]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sanitizes an exception message, redacting any sensitive server paths, credentials, or SQL syntax.
    /// </summary>
    /// <param name="message">Raw exception message text.</param>
    /// <param name="statusCode">Target HTTP status code.</param>
    /// <param name="traceId">Unique correlation identifier.</param>
    /// <returns>Descriptive, sanitized explanation safe for external consumption.</returns>
    public static string SanitizeDetail(string? message, int statusCode, string traceId)
    {
        // WHAT: Sanitizes error message strings or replaces internal 500 details with safe correlation notices.
        // WHY:  Provides accurate diagnostic feedback to API consumers without leaking filesystem paths,
        //       credentials, private key material, or internal SQL schema (Prime Directive 14).
        // HOW:  Checks status code; if 500+, returns a redacted correlation message; otherwise applies the
        //       redaction passes in decreasing specificity: key envelopes, credential pairs, SQL statements,
        //       UNC shares, and finally rooted Unix and Windows paths.
        if (statusCode >= 500)
        {
            return $"An internal error occurred during request processing. Correlation ID: {traceId}.";
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return GetDefaultDetail(statusCode);
        }

        string sanitized = message;
        sanitized = PemKeyRegex.Replace(sanitized, "[REDACTED_KEY]");
        sanitized = ConnectionStringRegex.Replace(sanitized, "$1=[REDACTED]");
        sanitized = SqlFragmentRegex.Replace(sanitized, "[REDACTED_SQL]");
        sanitized = UncPathRegex.Replace(sanitized, "[REDACTED_PATH]");
        sanitized = PathRegex.Replace(sanitized, "[REDACTED_PATH]");

        return sanitized.Trim();
    }

    /// <summary>
    /// Maps an HTTP status code to an RFC 7807 problem type URI.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <returns>Uniform resource name URI string.</returns>
    public static string GetProblemType(int statusCode) => statusCode switch
    {
        400 => "urn:weaver:error:bad-request",
        401 => "urn:weaver:error:unauthorized",
        403 => "urn:weaver:error:forbidden",
        404 => "urn:weaver:error:not-found",
        409 => "urn:weaver:error:conflict",
        415 => "urn:weaver:error:unsupported-media-type",
        503 => "urn:weaver:error:service-unavailable",
        _ => "urn:weaver:error:internal"
    };

    /// <summary>
    /// Maps an HTTP status code to a human-readable title.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <returns>Short error title.</returns>
    public static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Resource Not Found",
        409 => "Conflict",
        415 => "Unsupported Media Type",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };

    /// <summary>
    /// Returns default explanation text for a status code when no message is available.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <returns>Default description string.</returns>
    public static string GetDefaultDetail(int statusCode) => statusCode switch
    {
        400 => "The request parameters or body could not be validated.",
        401 => "Authentication credentials are missing or invalid.",
        403 => "The authenticated identity is forbidden from accessing this resource.",
        404 => "The requested resource was not found.",
        409 => "The operation conflicts with existing system state.",
        415 => "The provided media format is not supported.",
        503 => "The service is temporarily unavailable or requires initial setup.",
        _ => "An unexpected error occurred."
    };

    /// <summary>
    /// Maps an exception instance to an appropriate HTTP status code.
    /// </summary>
    /// <param name="ex">The thrown exception.</param>
    /// <returns>HTTP status code matching exception semantics.</returns>
    public static int MapExceptionToStatusCode(Exception ex)
    {
        // WHAT: Categorizes exception types into RFC 7231 status codes.
        // WHY:  Produces accurate HTTP error codes reflecting user error vs server error.
        // HOW:  Pattern matches exception types and inspects conflict keywords.
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is BadHttpRequestException badHttp)
        {
            return badHttp.StatusCode;
        }

        if (ex is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return 400;
        }

        if (ex is UnauthorizedAccessException)
        {
            return 401;
        }

        if (ex is KeyNotFoundException or FileNotFoundException)
        {
            return 404;
        }

        if (ex is InvalidOperationException invalidOp)
        {
            string msg = invalidOp.Message;
            if (msg.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("already completed", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            {
                return 409;
            }

            return 400;
        }

        return 500;
    }
}
