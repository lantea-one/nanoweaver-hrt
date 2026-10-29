using System.Text.Json;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Exception thrown when a nanoweaver-hrt node REST API call fails, encapsulating RFC 7807 problem details
/// without disclosing sensitive tokens, signatures, or passphrases.
/// </summary>
public sealed class NodeApiException : Exception
{
    /// <summary>
    /// Gets the HTTP status code returned by the node, or 0 if a transport fault occurred.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the RFC 7807 problem title or sanitized error summary.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the RFC 7807 problem detail message if available and safe; otherwise null.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets the RFC 7807 trace identifier for telemetry correlation.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    /// Gets the RFC 7807 problem instance URI path if available.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeApiException"/> class with structured RFC 7807 problem details.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The sanitized problem title.</param>
    /// <param name="detail">Optional sanitized problem detail description.</param>
    /// <param name="traceId">Optional RFC 7807 correlation trace ID.</param>
    /// <param name="instance">Optional problem instance path.</param>
    /// <param name="innerException">Optional inner cause exception.</param>
    public NodeApiException(
        int statusCode,
        string title,
        string? detail = null,
        string? traceId = null,
        string? instance = null,
        Exception? innerException = null)
        : base(FormatMessage(statusCode, title, traceId), innerException)
    {
        // WHAT: Initializes structured RFC 7807 exception properties.
        // WHY:  Prime Directive 14 requires zero-information disclosure: never expose private keys or JWT tokens.
        // HOW:  Stores status code, sanitized title, detail, traceId, and instance path.
        StatusCode = statusCode;
        Title = Sanitize(title);
        Detail = detail != null ? Sanitize(detail) : null;
        TraceId = traceId != null ? Sanitize(traceId) : null;
        Instance = instance != null ? Sanitize(instance) : null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeApiException"/> class with a simple message.
    /// </summary>
    /// <param name="message">The sanitized error message.</param>
    public NodeApiException(string message)
        : this(0, message)
    {
        // WHAT: Convenience constructor for generic node errors.
        // WHY:  Allows raising non-HTTP API client errors with consistent formatting.
        // HOW:  Delegates to primary constructor with status code 0.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeApiException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The sanitized error message.</param>
    /// <param name="innerException">The inner cause exception.</param>
    public NodeApiException(string message, Exception innerException)
        : this(0, message, null, null, null, innerException)
    {
        // WHAT: Convenience constructor for client faults wrapping lower-level exceptions.
        // WHY:  Preserves causal stack trace without exposing raw secret material.
        // HOW:  Delegates to primary constructor with status code 0 and inner exception.
    }

    /// <summary>
    /// Asynchronously parses an <see cref="HttpResponseMessage"/> into a sanitized <see cref="NodeApiException"/>.
    /// </summary>
    /// <param name="response">The HTTP response message to parse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="NodeApiException"/> instance.</returns>
    public static async Task<NodeApiException> CreateAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        // WHAT: Parses RFC 7807 problem details or plain error payloads from an HTTP response.
        // WHY:  Standardizes client error representation while stripping sensitive headers, tokens, and payloads.
        // HOW:  Reads response content, attempts JSON deserialization of RFC 7807 fields, and applies fallback defaults.
        ArgumentNullException.ThrowIfNull(response);

        int statusCode = (int)response.StatusCode;
        string defaultTitle = $"HTTP {statusCode} {response.ReasonPhrase ?? "Error"}";

        string? content = response.Content != null
            ? await response.Content.ReadAsStringAsync(ct)
            : null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return new NodeApiException(statusCode, defaultTitle);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;

            string title = defaultTitle;
            if (root.TryGetProperty("title", out JsonElement titleProp) && titleProp.ValueKind == JsonValueKind.String)
            {
                title = titleProp.GetString() ?? defaultTitle;
            }

            string? detail = null;
            if (root.TryGetProperty("detail", out JsonElement detailProp) && detailProp.ValueKind == JsonValueKind.String)
            {
                detail = detailProp.GetString();
            }

            string? traceId = null;
            if (root.TryGetProperty("traceId", out JsonElement traceProp) && traceProp.ValueKind == JsonValueKind.String)
            {
                traceId = traceProp.GetString();
            }

            string? instance = null;
            if (root.TryGetProperty("instance", out JsonElement instProp) && instProp.ValueKind == JsonValueKind.String)
            {
                instance = instProp.GetString();
            }

            int finalStatus = statusCode;
            if (root.TryGetProperty("status", out JsonElement statusProp) && statusProp.TryGetInt32(out int s) && s > 0)
            {
                finalStatus = s;
            }

            return new NodeApiException(finalStatus, title, detail, traceId, instance);
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g., HTML from reverse proxy or gateway timeout).
            return new NodeApiException(statusCode, defaultTitle, Sanitize(content));
        }
    }

    /// <summary>
    /// Formats the exception message string incorporating status code and trace ID.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="title">Problem title.</param>
    /// <param name="traceId">Optional correlation trace ID.</param>
    /// <returns>A formatted exception message string.</returns>
    private static string FormatMessage(int statusCode, string title, string? traceId)
    {
        // WHAT: Formats a safe exception message without leaking request details.
        // WHY:  Ensures that Exception.Message is safe to display in UI and telemetry logs.
        // HOW:  Combines title, status code, and trace ID into a single line.
        string safeTitle = Sanitize(title);
        return traceId != null
            ? $"{safeTitle} (Status: {statusCode}, TraceId: {Sanitize(traceId)})"
            : $"{safeTitle} (Status: {statusCode})";
    }

    /// <summary>
    /// Sanitizes text to redact any accidentally included bearer tokens, passphrases, or sensitive key material.
    /// </summary>
    /// <param name="input">Raw input string.</param>
    /// <returns>Sanitized string with sensitive tokens masked.</returns>
    private static string Sanitize(string input)
    {
        // WHAT: Sanitizes error text to eliminate sensitive credentials per Prime Directive 14.
        // WHY:  Prevents bearer tokens, PEM markers, or passphrases from leaking into exception logs or UI toasts.
        // HOW:  Replaces occurrences of Bearer JWT patterns and key headers with [REDACTED].
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string result = input;
        if (result.Contains("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            int idx = result.IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                int endIdx = result.IndexOf(' ', idx + 7);
                if (endIdx < 0)
                {
                    endIdx = result.Length;
                }
                result = string.Concat(result.AsSpan(0, idx + 7), "[REDACTED]", result.AsSpan(endIdx));
                idx = result.IndexOf("Bearer ", idx + 17, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (result.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            result = "[REDACTED_KEY_MATERIAL]";
        }

        return result;
    }
}
