using System.Text.Json;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using NanoWeaverHrt.Formatting;

namespace NanoWeaverHrt.Swagger;

/// <summary>
/// OpenAPI schema and operation filter that enriches API definitions with multi-format request and response
/// examples across lower camelCase JSON, XML, and lower snake-case with hyphens YAML.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A dual Swashbuckle filter inspecting endpoints and registered schemas.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Guarantees that both Swagger UI and ReDoc render comprehensive example models
/// in JSON, XML, and YAML without manual Swagger annotation boilerplate on every controller or minimal endpoint.
/// </para>
/// <para>
/// <strong>How it works:</strong>
/// <list type="bullet">
///   <item><description>Detects types implementing <see cref="IExamplesProvider{T}"/> on requests and responses.</description></item>
///   <item><description>Generates native <see cref="OpenApiObject"/> / <see cref="OpenApiArray"/> ASTs for <c>application/json</c>.</description></item>
///   <item><description>Generates formatted XML strings for <c>application/xml</c>.</description></item>
///   <item><description>Generates hyphenated YAML strings for <c>application/x-yaml</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class OpenApiMultiFormatExampleFilter : ISchemaFilter, IOperationFilter
{
    /// <summary>
    /// Applies OpenAPI schema decorations and model examples.
    /// </summary>
    /// <param name="schema">The schema being processed.</param>
    /// <param name="context">The schema filter context.</param>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // WHAT: Injects model example into OpenAPI components.schemas.
        // WHY:  Allows Swagger and ReDoc model schema explorers to display realistic data contracts.
        // HOW:  Queries IExamplesProvider<T>, converts example to OpenApiAny.
        object? example = ContentFormatSerializer.GetExampleInstance(context.Type);
        if (example == null)
        {
            return;
        }

        string json = ContentFormatSerializer.SerializeJson(example);
        using var doc = JsonDocument.Parse(json);
        schema.Example = ConvertJsonElementToOpenApiAny(doc.RootElement);
        schema.Xml ??= new OpenApiXml { Name = context.Type.Name };
    }

    /// <summary>
    /// Applies multi-format request body and response examples to the OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation definition.</param>
    /// <param name="context">The operation filter context.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // WHAT: Populates application/json, application/xml, and application/x-yaml under requestBody and responses.
        // WHY:  Ensures both Swagger UI and ReDoc show interactive multi-format examples.
        // HOW:  Inspects endpoint parameter types and supported response types.
        EnrichRequestBodyExamples(operation, context);
        EnrichResponseExamples(operation, context);
    }

    /// <summary>
    /// Injects multi-format examples into the operation's request body content dictionary.
    /// </summary>
    /// <param name="operation">The OpenAPI operation definition.</param>
    /// <param name="context">The operation filter context.</param>
    private static void EnrichRequestBodyExamples(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.RequestBody?.Content == null)
        {
            return;
        }

        var bodyParam = context.ApiDescription.ParameterDescriptions
            .FirstOrDefault(p => p.Source.Id.Equals("Body", StringComparison.OrdinalIgnoreCase) || p.Source.Id.Equals("Custom", StringComparison.OrdinalIgnoreCase));

        Type? reqType = bodyParam?.Type;
        if (reqType == null)
        {
            return;
        }

        object? example = ContentFormatSerializer.GetExampleInstance(reqType);
        if (example == null)
        {
            return;
        }

        string json = ContentFormatSerializer.SerializeJson(example);
        string xml = ContentFormatSerializer.SerializeXml(example);
        string yaml = ContentFormatSerializer.SerializeYaml(example);

        using var doc = JsonDocument.Parse(json);
        IOpenApiAny jsonExample = ConvertJsonElementToOpenApiAny(doc.RootElement);
        var xmlExample = new OpenApiString(xml);
        var yamlExample = new OpenApiString(yaml);

        OpenApiSchema? schema = ResolveFirstSchema(operation.RequestBody.Content);

        SetContentExample(operation.RequestBody.Content, "application/json", jsonExample, schema);
        SetContentExample(operation.RequestBody.Content, "application/xml", xmlExample, schema);
        SetContentExample(operation.RequestBody.Content, "application/x-yaml", yamlExample, schema);
    }

    /// <summary>
    /// Injects multi-format examples into the operation's responses content dictionary.
    /// </summary>
    /// <param name="operation">The OpenAPI operation definition.</param>
    /// <param name="context">The operation filter context.</param>
    private static void EnrichResponseExamples(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses == null)
        {
            return;
        }

        foreach (var responseType in context.ApiDescription.SupportedResponseTypes)
        {
            string statusCode = responseType.StatusCode.ToString();
            if (!operation.Responses.TryGetValue(statusCode, out var response) || response.Content == null)
            {
                continue;
            }

            Type? respType = responseType.Type;
            if (respType == null)
            {
                continue;
            }

            object? example = ContentFormatSerializer.GetExampleInstance(respType);
            if (example == null)
            {
                continue;
            }

            string json = ContentFormatSerializer.SerializeJson(example);
            string xml = ContentFormatSerializer.SerializeXml(example);
            string yaml = ContentFormatSerializer.SerializeYaml(example);

            using var doc = JsonDocument.Parse(json);
            IOpenApiAny jsonExample = ConvertJsonElementToOpenApiAny(doc.RootElement);
            var xmlExample = new OpenApiString(xml);
            var yamlExample = new OpenApiString(yaml);

            OpenApiSchema? schema = ResolveFirstSchema(response.Content);

            SetContentExample(response.Content, "application/json", jsonExample, schema);
            SetContentExample(response.Content, "application/xml", xmlExample, schema);
            SetContentExample(response.Content, "application/x-yaml", yamlExample, schema);
        }
    }

    /// <summary>
    /// Sets an example on a specified media type in an OpenAPI content dictionary.
    /// </summary>
    /// <param name="contentDict">The OpenAPI content dictionary.</param>
    /// <param name="mediaType">The MIME media type.</param>
    /// <param name="example">The example AST node.</param>
    /// <param name="schema">The fallback schema definition.</param>
    private static void SetContentExample(IDictionary<string, OpenApiMediaType> contentDict, string mediaType, IOpenApiAny example, OpenApiSchema? schema)
    {
        // WHAT: Assigns example AST node to target media type in OpenAPI dictionary.
        // WHY:  Standardizes example assignment across request bodies and responses.
        // HOW:  Updates existing OpenApiMediaType or creates a new entry.
        if (contentDict.TryGetValue(mediaType, out var media))
        {
            media.Example = example;
        }
        else
        {
            contentDict[mediaType] = new OpenApiMediaType { Schema = schema, Example = example };
        }
    }

    /// <summary>
    /// Safely resolves the first schema definition from an OpenAPI media type collection.
    /// </summary>
    /// <param name="contentDict">The media type dictionary.</param>
    /// <returns>The first available schema, or null.</returns>
    private static OpenApiSchema? ResolveFirstSchema(IDictionary<string, OpenApiMediaType> contentDict)
    {
        // WHAT: Retrieves the first schema from the content dictionary.
        // WHY:  Propagates request/response schema metadata to newly added multi-format representations.
        // HOW:  Enumerates values using MoveNext.
        using var enumerator = contentDict.Values.GetEnumerator();
        if (enumerator.MoveNext())
        {
            return enumerator.Current.Schema;
        }
        return null;
    }

    /// <summary>
    /// Recursively maps a <see cref="JsonElement"/> into a strongly-typed <see cref="IOpenApiAny"/> node hierarchy.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <returns>A populated <see cref="IOpenApiAny"/> node.</returns>
    public static IOpenApiAny ConvertJsonElementToOpenApiAny(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new OpenApiObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ConvertJsonElementToOpenApiAny(prop.Value);
                }
                return obj;

            case JsonValueKind.Array:
                var arr = new OpenApiArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(ConvertJsonElementToOpenApiAny(item));
                }
                return arr;

            case JsonValueKind.String:
                return new OpenApiString(element.GetString()!);

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l))
                {
                    return new OpenApiLong(l);
                }
                return new OpenApiDouble(element.GetDouble());

            case JsonValueKind.True:
                return new OpenApiBoolean(true);

            case JsonValueKind.False:
                return new OpenApiBoolean(false);

            case JsonValueKind.Null:
            default:
                return new OpenApiNull();
        }
    }
}
