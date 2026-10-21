using System.Text.Json;
using System.Xml.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Security.ExceptionHandling;

/// <summary>
/// ASP.NET Core middleware intercepting API exceptions and formatting RFC 7807 problem details with zero sensitive data leakage.
/// </summary>
public sealed class SecureExceptionHandlerMiddleware
{
    /// <summary>
    /// Next delegate in the HTTP request processing pipeline.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// System diagnostic logger.
    /// </summary>
    private readonly ILogger<SecureExceptionHandlerMiddleware> _logger;

    /// <summary>
    /// Standard JSON serialization options for problem details formatting.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureExceptionHandlerMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the request processing pipeline.</param>
    /// <param name="logger">Diagnostic logger.</param>
    public SecureExceptionHandlerMiddleware(RequestDelegate next, ILogger<SecureExceptionHandlerMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the middleware to process the HTTP request.
    /// </summary>
    /// <param name="context">Active HTTP context.</param>
    /// <returns>Asynchronous task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // WHAT: Intercepts unhandled exceptions thrown during API endpoint execution.
        // WHY:  Guarantees consistent, descriptive RFC 7807 problem responses without leaking secrets or paths.
        // HOW:  Wraps _next for /api routes in a try-catch block and formats SecureProblemDetails.
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleApiExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Formats and writes the sanitized RFC 7807 ProblemDetails response.
    /// </summary>
    /// <param name="context">Active HTTP context.</param>
    /// <param name="ex">The intercepted exception.</param>
    private async Task HandleApiExceptionAsync(HttpContext context, Exception ex)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(ex, "Response has already started; cannot write SecureProblemDetails for trace {TraceId}", context.TraceIdentifier);
            return;
        }

        int statusCode = SecureExceptionSanitizer.MapExceptionToStatusCode(ex);
        string detail = SecureExceptionSanitizer.SanitizeDetail(ex.Message, statusCode, context.TraceIdentifier);

        SecureProblemDetails problem = new()
        {
            Type = SecureExceptionSanitizer.GetProblemType(statusCode),
            Title = SecureExceptionSanitizer.GetTitle(statusCode),
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            TraceId = context.TraceIdentifier,
            Timestamp = DateTimeOffset.UtcNow
        };

        if (statusCode >= 500)
        {
            _logger.LogError(ex, "API Internal Error on {Path} (Trace: {TraceId})", context.Request.Path, context.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning("API Client Error {StatusCode} on {Path} (Trace: {TraceId}): {Detail}", statusCode, context.Request.Path, context.TraceIdentifier, detail);
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        string accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("application/xml", StringComparison.OrdinalIgnoreCase) || accept.Contains("text/xml", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/problem+xml; charset=utf-8";
            XmlSerializer serializer = new(typeof(SecureProblemDetails));
            using StringWriter sw = new();
            serializer.Serialize(sw, problem);
            await context.Response.WriteAsync(sw.ToString());
        }
        else
        {
            context.Response.ContentType = "application/problem+json; charset=utf-8";
            string json = JsonSerializer.Serialize(problem, JsonOptions);
            await context.Response.WriteAsync(json);
        }
    }
}
