using System.Text.Json;
using System.Xml.Serialization;

namespace NanoWeaverHrt.Security.ExceptionHandling;

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
        // WHAT: Intercepts unhandled exceptions thrown anywhere downstream in the pipeline.
        // WHY:  Guarantees consistent, descriptive RFC 7807 problem responses for /api routes without leaking
        //       secrets or paths, and guarantees that no other route (a Web UI page, /healthz, /health) can
        //       surface a stack trace either. ASP.NET Core registers its Development-mode developer exception
        //       page ahead of this middleware, so an escaping exception would otherwise be rendered in full —
        //       exactly the disclosure Prime Directive 14 prohibits.
        // HOW:  Wraps _next in a try-catch for every path, formatting SecureProblemDetails for /api routes and
        //       an opaque correlation notice for everything else.
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleNonApiExceptionAsync(context, ex);
            }

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
    /// Writes an opaque, trace-correlated failure notice for a non-API request.
    /// </summary>
    /// <param name="context">Active HTTP context.</param>
    /// <param name="ex">The intercepted exception.</param>
    /// <returns>Asynchronous task.</returns>
    /// <remarks>
    /// Non-API callers are browsers and container orchestrators, so no problem-details content negotiation
    /// applies; they receive the same opaque correlation sentence that a 500-class API response carries,
    /// and the full exception is recorded in the server log only.
    /// </remarks>
    private async Task HandleNonApiExceptionAsync(HttpContext context, Exception ex)
    {
        _logger.LogError(ex, "Non-API Internal Error on {Path} (Trace: {TraceId})", context.Request.Path, context.TraceIdentifier);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response has already started; cannot write failure notice for trace {TraceId}", context.TraceIdentifier);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(
            SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status500InternalServerError, context.TraceIdentifier));
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
