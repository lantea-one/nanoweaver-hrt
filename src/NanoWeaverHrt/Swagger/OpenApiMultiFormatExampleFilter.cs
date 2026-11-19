using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
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
///   <item><description>Generates native <see cref="JsonNode"/> trees for <c>application/json</c>.</description></item>
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
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // WHAT: Injects model example into OpenAPI components.schemas.
        // WHY:  Allows Swagger and ReDoc model schema explorers to display realistic data contracts.
        // HOW:  Queries IExamplesProvider<T> and converts example JSON to a Microsoft.OpenApi JsonNode.
        if (schema is not OpenApiSchema concreteSchema)
        {
            return;
        }

        object? example = ContentFormatSerializer.GetExampleInstance(context.Type);
        if (example == null)
        {
            return;
        }

        string json = ContentFormatSerializer.SerializeJson(example);
        using var doc = JsonDocument.Parse(json);
        // WHY `!`: example is guaranteed non-null above, so SerializeJson emits a non-null JSON root value.
        concreteSchema.Examples = [ConvertJsonElementToJsonNode(doc.RootElement)!];
        concreteSchema.Xml ??= new OpenApiXml { Name = context.Type.Name };
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
        JsonNode? jsonExample = ConvertJsonElementToJsonNode(doc.RootElement);
        JsonNode xmlExample = JsonValue.Create(xml);
        JsonNode yamlExample = JsonValue.Create(yaml);

        IOpenApiSchema? schema = ResolveFirstSchema(operation.RequestBody.Content);

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
            if (!operation.Responses.TryGetValue(statusCode, out IOpenApiResponse? response) || response.Content == null)
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
            JsonNode? jsonExample = ConvertJsonElementToJsonNode(doc.RootElement);
            JsonNode xmlExample = JsonValue.Create(xml);
            JsonNode yamlExample = JsonValue.Create(yaml);

            IOpenApiSchema? schema = ResolveFirstSchema(response.Content);

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
    /// <param name="example">The example JSON AST node.</param>
    /// <param name="schema">The fallback schema definition.</param>
    private static void SetContentExample(IDictionary<string, IOpenApiMediaType> contentDict, string mediaType, JsonNode? example, IOpenApiSchema? schema)
    {
        // WHAT: Assigns example JsonNode to target media type in OpenAPI dictionary.
        // WHY:  Standardizes example assignment across request bodies and responses.
        // HOW:  Updates existing OpenApiMediaType or creates a new entry.
        if (contentDict.TryGetValue(mediaType, out IOpenApiMediaType? media) && media is OpenApiMediaType concreteMedia)
        {
            concreteMedia.Example = example;
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
    private static IOpenApiSchema? ResolveFirstSchema(IDictionary<string, IOpenApiMediaType> contentDict)
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
    /// Recursively maps a <see cref="JsonElement"/> into a <see cref="JsonNode"/> hierarchy compatible with OpenAPI v3.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <returns>A populated <see cref="JsonNode"/>, or <see langword="null"/> for JSON null literals.</returns>
    public static JsonNode? ConvertJsonElementToJsonNode(JsonElement element)
    {
        // WHAT: Converts System.Text.Json JsonElement values into mutable System.Text.Json.Nodes.JsonNode trees.
        // WHY:  Microsoft.OpenApi v3 represents schema and media-type examples natively as JsonNode instances.
        // HOW:  Switches on element.ValueKind and recursively builds JsonObject, JsonArray, and JsonValue nodes.
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ConvertJsonElementToJsonNode(prop.Value);
                }
                return obj;

            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(ConvertJsonElementToJsonNode(item));
                }
                return arr;

            case JsonValueKind.String:
                // WHY `!`: GetString() returns null only when the value kind is Null. This case label
                //          guarantees the kind is String, so the decoded text is always present.
                return JsonValue.Create(element.GetString()!);

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l))
                {
                    return JsonValue.Create(l);
                }
                return JsonValue.Create(element.GetDouble());

            case JsonValueKind.True:
                return JsonValue.Create(true);

            case JsonValueKind.False:
                return JsonValue.Create(false);

            case JsonValueKind.Null:
            default:
                return null;
        }
    }
}
