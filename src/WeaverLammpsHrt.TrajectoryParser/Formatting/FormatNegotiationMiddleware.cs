using System.Text;

namespace WeaverLammpsHrt.TrajectoryParser.Formatting;

/// <summary>
/// ASP.NET Core middleware providing automatic content negotiation and transcoding for JSON, XML, and YAML.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Middleware that intercepts HTTP requests and responses to support XML and YAML
/// serialization alongside standard lower camelCase JSON.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To give external collaborators, laboratory automation scripts, and HPC pipelines
/// transparent multi-format access to all REST endpoints without modifying route handler signatures.
/// </para>
/// <para>
/// <strong>How it works:</strong>
/// <list type="bullet">
///   <item><description><strong>Inbound:</strong> If the request body is XML or YAML, transcodes it into JSON before model binding.</description></item>
///   <item><description><strong>Outbound:</strong> If the caller specifies an XML or YAML <c>Accept</c> header or <c>?format=</c> parameter,
///   transcodes the JSON response into formatted XML or hyphenated lower snake-case YAML.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class FormatNegotiationMiddleware
{
    /// <summary>
    /// Next delegate in the ASP.NET Core pipeline.
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatNegotiationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate in the processing pipeline.</param>
    public FormatNegotiationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Executes the middleware for an individual HTTP request context.
    /// </summary>
    /// <param name="context">The active HTTP context.</param>
    /// <returns>A <see cref="Task"/> representing asynchronous middleware completion.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // WHAT: Handles inbound payload transcoding if content is XML or YAML.
        // WHY:  Allows minimal API route handlers to bind strongly-typed models seamlessly.
        // HOW:  Reads raw stream, converts to JSON, re-arms context.Request.Body.
        await TranscodeInboundRequestAsync(context);

        // WHAT: Checks whether the client requested XML or YAML in the response.
        // WHY:  Determines whether output stream interception is required.
        // HOW:  Evaluates Accept header and ?format= query string parameter.
        string? accept = context.Request.Headers.Accept.ToString();
        string? formatQuery = context.Request.Query["format"].ToString();

        bool wantsXml = (!string.IsNullOrEmpty(formatQuery) && formatQuery.Equals("xml", StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(accept) && (accept.Contains("application/xml", StringComparison.OrdinalIgnoreCase) || accept.Contains("text/xml", StringComparison.OrdinalIgnoreCase)));

        bool wantsYaml = (!string.IsNullOrEmpty(formatQuery) && formatQuery.Equals("yaml", StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrEmpty(accept) && (accept.Contains("application/x-yaml", StringComparison.OrdinalIgnoreCase) || accept.Contains("text/yaml", StringComparison.OrdinalIgnoreCase) || accept.Contains("application/yaml", StringComparison.OrdinalIgnoreCase)));

        if (!wantsXml && !wantsYaml)
        {
            await _next(context);
            return;
        }

        // Intercept response stream to capture and transcode downstream JSON
        Stream originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context);

            memoryStream.Seek(0, SeekOrigin.Begin);
            string responseText = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();
            string responseContentType = context.Response.ContentType ?? string.Empty;

            if (responseContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(responseText))
            {
                if (wantsXml)
                {
                    string xml = ContentFormatSerializer.JsonToXml(responseText, "Response");
                    byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
                    context.Response.ContentType = "application/xml; charset=utf-8";
                    context.Response.ContentLength = xmlBytes.Length;
                    context.Response.Body = originalBodyStream;
                    await originalBodyStream.WriteAsync(xmlBytes);
                    return;
                }

                if (wantsYaml)
                {
                    string yaml = ContentFormatSerializer.JsonToYaml(responseText);
                    byte[] yamlBytes = Encoding.UTF8.GetBytes(yaml);
                    context.Response.ContentType = "application/x-yaml; charset=utf-8";
                    context.Response.ContentLength = yamlBytes.Length;
                    context.Response.Body = originalBodyStream;
                    await originalBodyStream.WriteAsync(yamlBytes);
                    return;
                }
            }

            // Passthrough for non-JSON content (static files, binary downloads, etc.)
            memoryStream.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBodyStream;
            await memoryStream.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    /// <summary>
    /// Transcodes inbound XML and YAML request bodies into standard JSON.
    /// </summary>
    /// <param name="context">The active HTTP context.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private static async Task TranscodeInboundRequestAsync(HttpContext context)
    {
        string? contentType = context.Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            return;
        }

        if (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("text/xml", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            string xml = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(xml))
            {
                string json = ContentFormatSerializer.XmlToJson(xml);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentType = "application/json";
                context.Request.ContentLength = bytes.Length;
            }
        }
        else if (contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            string yaml = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                string json = ContentFormatSerializer.YamlToJson(yaml);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentType = "application/json";
                context.Request.ContentLength = bytes.Length;
            }
        }
    }
}

/// <summary>
/// Extension methods for registering <see cref="FormatNegotiationMiddleware"/>.
/// </summary>
public static class FormatNegotiationMiddlewareExtensions
{
    /// <summary>
    /// Adds the format negotiation middleware to the application request pipeline.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <returns>The application builder instance for method chaining.</returns>
    public static IApplicationBuilder UseFormatNegotiation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<FormatNegotiationMiddleware>();
    }
}
