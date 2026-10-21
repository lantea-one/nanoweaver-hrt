using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NanoWeaverHrt.Swagger;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NanoWeaverHrt.Formatting;

/// <summary>
/// Provides unified multi-format serialization and deserialization across JSON (lower camelCase),
/// XML, and YAML (lower snake-case with hyphens / kebab-case).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A central serialization engine capable of transcoding and formatting
/// health data payloads and simulation telemetry across JSON, XML, and YAML.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Biomedical and nanotech pipelines integrate with heterogeneous systems:
/// web frontends expect camelCase JSON, clinical hospital systems utilize XML/HL7 structures, and HPC simulation
/// workflows configure pipelines using hyphenated YAML.
/// </para>
/// <para>
/// <strong>How it works:</strong> Configures <see cref="JsonSerializerOptions"/> for lower camelCase, <see cref="XDocument"/>
/// mappers for clean non-namespaced XML, and <see cref="SerializerBuilder"/> with <see cref="HyphenatedNamingConvention"/>
/// for lower snake-case with hyphens YAML.
/// </para>
/// </remarks>
public static class ContentFormatSerializer
{
    /// <summary>
    /// Default JSON serialization options enforcing lower camelCase property names and formatted indentation.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// YAML serializer configured with lower snake-case with hyphens (kebab-case) naming convention.
    /// </summary>
    private static readonly ISerializer YamlSerializerInstance = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>
    /// YAML deserializer configured with lower snake-case with hyphens (kebab-case) naming convention.
    /// </summary>
    private static readonly IDeserializer YamlDeserializerInstance = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Serializes an object to formatted JSON with lower camelCase keys.
    /// </summary>
    /// <typeparam name="T">The type of the instance to serialize.</typeparam>
    /// <param name="value">The object instance.</param>
    /// <returns>A formatted JSON string.</returns>
    public static string SerializeJson<T>(T value)
    {
        // WHAT: Serializes value to JSON.
        // WHY:  Ensures lower camelCase is strictly upheld.
        // HOW:  Uses System.Text.Json with configured JsonOptions.
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Deserializes a JSON string into a strongly-typed instance.
    /// </summary>
    /// <typeparam name="T">The target model type.</typeparam>
    /// <param name="json">The JSON payload string.</param>
    /// <returns>The deserialized model instance, or null.</returns>
    public static T? DeserializeJson<T>(string json)
    {
        // WHAT: Deserializes JSON string into target type.
        // WHY:  Standard strongly-typed object extraction.
        // HOW:  Uses System.Text.Json with configured JsonOptions.
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// Serializes an object to clean, formatted XML.
    /// </summary>
    /// <typeparam name="T">The type of the instance to serialize.</typeparam>
    /// <param name="value">The object instance.</param>
    /// <param name="rootElementName">Optional XML root element name. Defaults to the type name.</param>
    /// <returns>A formatted XML document string.</returns>
    public static string SerializeXml<T>(T value, string? rootElementName = null)
    {
        // WHAT: Serializes an object to XML.
        // WHY:  Allows healthcare and laboratory workflows to export models in clean XML.
        // HOW:  Converts object to JsonElement, then recursively translates into an XDocument.
        string root = string.IsNullOrWhiteSpace(rootElementName) ? typeof(T).Name : rootElementName;
        string json = SerializeJson(value);
        return JsonToXml(json, root);
    }

    /// <summary>
    /// Deserializes an XML document string into a strongly-typed model instance.
    /// </summary>
    /// <typeparam name="T">The target model type.</typeparam>
    /// <param name="xml">The XML document string.</param>
    /// <returns>The deserialized model instance, or null.</returns>
    public static T? DeserializeXml<T>(string xml)
    {
        // WHAT: Deserializes XML into target model.
        // WHY:  Enables XML payload consumption in API endpoints.
        // HOW:  Converts XML into an intermediate JSON AST, then deserializes via System.Text.Json.
        string json = XmlToJson(xml);
        return DeserializeJson<T>(json);
    }

    /// <summary>
    /// Serializes an object to YAML using lower snake-case with hyphens (kebab-case).
    /// </summary>
    /// <typeparam name="T">The type of the instance to serialize.</typeparam>
    /// <param name="value">The object instance.</param>
    /// <returns>A formatted YAML string.</returns>
    public static string SerializeYaml<T>(T value)
    {
        // WHAT: Serializes an object to YAML.
        // WHY:  Supports HPC simulation pipelines with lower snake-case with hyphens.
        // HOW:  Uses YamlDotNet with HyphenatedNamingConvention.
        return YamlSerializerInstance.Serialize(value!);
    }

    /// <summary>
    /// Deserializes a YAML string into a strongly-typed model instance.
    /// </summary>
    /// <typeparam name="T">The target model type.</typeparam>
    /// <param name="yaml">The YAML string.</param>
    /// <returns>The deserialized model instance, or null.</returns>
    public static T? DeserializeYaml<T>(string yaml)
    {
        // WHAT: Deserializes YAML into target model.
        // WHY:  Enables YAML payload consumption in API endpoints.
        // HOW:  Uses YamlDotNet deserializer configured with HyphenatedNamingConvention.
        return YamlDeserializerInstance.Deserialize<T>(yaml);
    }

    /// <summary>
    /// Converts a JSON string into a formatted XML document string.
    /// </summary>
    /// <param name="json">The input JSON string.</param>
    /// <param name="rootElementName">The XML root element name.</param>
    /// <returns>A formatted XML document string.</returns>
    public static string JsonToXml(string json, string rootElementName = "Response")
    {
        // WHAT: Translates JSON to XML.
        // WHY:  Converts REST API responses to XML during content negotiation.
        // HOW:  Recursively maps JsonElement tree to XElement hierarchy.
        using var doc = JsonDocument.Parse(json);
        XElement root = JsonElementToXml(rootElementName, doc.RootElement);
        var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        return xDoc.ToString();
    }

    /// <summary>
    /// Converts an XML string into a lower camelCase JSON string.
    /// </summary>
    /// <param name="xml">The input XML document string.</param>
    /// <returns>A valid JSON string.</returns>
    public static string XmlToJson(string xml)
    {
        // WHAT: Translates XML to JSON.
        // WHY:  Allows incoming XML request bodies to be processed by standard ASP.NET model binders.
        // HOW:  Parses XML into XDocument, recursively builds JSON via Utf8JsonWriter.
        XDocument xDoc = XDocument.Parse(xml);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            if (xDoc.Root != null)
            {
                WriteXmlToJson(writer, xDoc.Root);
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Converts a JSON string into lower snake-case with hyphens YAML.
    /// </summary>
    /// <param name="json">The input JSON string.</param>
    /// <returns>A formatted YAML string.</returns>
    public static string JsonToYaml(string json)
    {
        // WHAT: Translates JSON to YAML.
        // WHY:  Converts REST API responses to YAML during content negotiation.
        // HOW:  Parses JSON document into a native dictionary hierarchy with hyphenated keys, then serializes via YamlDotNet.
        using var doc = JsonDocument.Parse(json);
        object? nativeObj = JsonElementToNativeObject(doc.RootElement);
        return YamlSerializerInstance.Serialize(nativeObj!);
    }

    /// <summary>
    /// Converts a YAML string into a lower camelCase JSON string.
    /// </summary>
    /// <param name="yaml">The input YAML string.</param>
    /// <returns>A valid JSON string.</returns>
    public static string YamlToJson(string yaml)
    {
        // WHAT: Translates YAML to JSON.
        // WHY:  Allows incoming YAML request bodies to be processed by standard ASP.NET model binders.
        // HOW:  Deserializes YAML into an object graph, normalizes keys to lower camelCase, then serializes via System.Text.Json.
        object? obj = YamlDeserializerInstance.Deserialize<object>(new StringReader(yaml));
        object? normalized = NormalizeYamlObjectKeys(obj);
        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> into a native object graph with hyphenated dictionary keys.
    /// </summary>
    /// <param name="element">The JSON element.</param>
    /// <returns>A native primitive, list, or dictionary with hyphenated keys.</returns>
    private static object? JsonElementToNativeObject(JsonElement element)
    {
        // WHAT: Converts JsonElement to primitive or dictionary structures.
        // WHY:  Ensures YamlDotNet serializes objects rather than internal JsonElement struct properties.
        // HOW:  Recursively traverses element tree, applying HyphenatedNamingConvention to keys.
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    string key = HyphenatedNamingConvention.Instance.Apply(prop.Name);
                    dict[key] = JsonElementToNativeObject(prop.Value);
                }
                return dict;

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(JsonElementToNativeObject(item));
                }
                return list;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l))
                {
                    return l;
                }
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    /// <summary>
    /// Recursively normalizes dictionary keys from hyphenated or snake_case naming to lower camelCase.
    /// </summary>
    /// <param name="obj">The deserialized YAML object graph.</param>
    /// <returns>An object graph with lower camelCase dictionary keys.</returns>
    private static object? NormalizeYamlObjectKeys(object? obj)
    {
        // WHAT: Traverses deserialized YAML object graph and normalizes keys.
        // WHY:  Ensures inbound YAML transcodes to valid lower camelCase JSON.
        // HOW:  Inspects dictionary implementations and transforms keys via KebabToCamelCase.
        if (obj is System.Collections.IDictionary dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                string key = KebabToCamelCase(entry.Key.ToString()!);
                result[key] = NormalizeYamlObjectKeys(entry.Value);
            }
            return result;
        }

        if (obj is IList<object> list)
        {
            var result = new List<object?>();
            foreach (var item in list)
            {
                result.Add(NormalizeYamlObjectKeys(item));
            }
            return result;
        }

        return obj;
    }

    /// <summary>
    /// Converts a kebab-case or snake_case string into a lower camelCase identifier.
    /// </summary>
    /// <param name="input">The input kebab-case or snake_case string.</param>
    /// <returns>A lower camelCase identifier string.</returns>
    private static string KebabToCamelCase(string input)
    {
        // WHAT: Converts hyphenated or underscored strings to lower camelCase.
        // WHY:  Maps YAML keys to C# model properties without loss of casing conventions.
        // HOW:  Splits on hyphens and underscores, capitalizing sub-tokens after the first.
        string[] parts = input.Split('-', '_');
        if (parts.Length == 1)
        {
            return char.ToLowerInvariant(parts[0][0]) + parts[0][1..];
        }

        var sb = new StringBuilder();
        sb.Append(parts[0].ToLowerInvariant());
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                sb.Append(char.ToUpperInvariant(parts[i][0]));
                if (parts[i].Length > 1)
                {
                    sb.Append(parts[i][1..]);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract an example instance for a specified model type by checking for <see cref="IExamplesProvider{T}"/>.
    /// </summary>
    /// <param name="type">The type to evaluate.</param>
    /// <returns>The example instance, or null if the type does not implement <see cref="IExamplesProvider{T}"/>.</returns>
    public static object? GetExampleInstance(Type type)
    {
        // WHAT: Retrieves an example instance for a given type.
        // WHY:  Dynamically discovers example generators on models implementing IExamplesProvider<T>.
        // HOW:  Checks interfaces, invokes GetExamples() via reflection.
        Type targetType = type;
        bool isCollection = false;

        if (targetType.IsGenericType && (targetType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
                                         targetType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                                         targetType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            targetType = targetType.GetGenericArguments()[0];
            isCollection = true;
        }
        else if (targetType.IsArray)
        {
            targetType = targetType.GetElementType()!;
            isCollection = true;
        }

        if (targetType == typeof(void))
        {
            return null;
        }

        Type providerInterface = typeof(IExamplesProvider<>).MakeGenericType(targetType);
        if (!providerInterface.IsAssignableFrom(targetType))
        {
            return null;
        }

        try
        {
            object? instance = targetType.IsValueType
                ? Activator.CreateInstance(targetType)
                : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(targetType);

            // WHAT: Discovers GetExamples method declared via IExamplesProvider<T>.
            // WHY:  Invokes example provider without needing constructor arguments.
            // HOW:  Reflects method info; null is impossible as targetType implements IExamplesProvider<T>.
            var method = targetType.GetMethod(nameof(IExamplesProvider<object>.GetExamples));
            object? example = method!.Invoke(instance, null);

            if (example != null)
            {
                if (isCollection)
                {
                    var listType = typeof(List<>).MakeGenericType(targetType);
                    var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
                    list.Add(example);
                    return list;
                }
                return example;
            }
        }
        catch
        {
            // Fallback to null if instantiation fails
        }

        return null;
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> to an <see cref="XElement"/>.
    /// </summary>
    /// <param name="elementName">The name for the XML element.</param>
    /// <param name="element">The JSON element.</param>
    /// <returns>A populated <see cref="XElement"/>.</returns>
    private static XElement JsonElementToXml(string elementName, JsonElement element)
    {
        var xElem = new XElement(elementName);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    xElem.Add(JsonElementToXml(prop.Name, prop.Value));
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    xElem.Add(JsonElementToXml("item", item));
                }
                break;
            case JsonValueKind.String:
                xElem.Value = element.GetString()!;
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                xElem.Value = element.GetRawText();
                break;
            case JsonValueKind.Null:
            default:
                break;
        }
        return xElem;
    }

    /// <summary>
    /// Recursively converts an <see cref="XElement"/> to JSON via <see cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="element">The XML element.</param>
    private static void WriteXmlToJson(Utf8JsonWriter writer, XElement element)
    {
        var childElements = element.Elements().ToList();
        if (childElements.Count == 0)
        {
            string val = element.Value;
            if (bool.TryParse(val, out bool b))
            {
                writer.WriteBooleanValue(b);
            }
            else if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            {
                writer.WriteNumberValue(l);
            }
            else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                writer.WriteNumberValue(d);
            }
            else
            {
                writer.WriteStringValue(val);
            }
            return;
        }

        bool isArray = childElements.All(e => e.Name.LocalName == "item") ||
                       childElements.GroupBy(e => e.Name.LocalName).Any(g => g.Count() > 1);

        if (isArray)
        {
            writer.WriteStartArray();
            foreach (var child in childElements)
            {
                WriteXmlToJson(writer, child);
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteStartObject();
            foreach (var child in childElements)
            {
                writer.WritePropertyName(child.Name.LocalName);
                WriteXmlToJson(writer, child);
            }
            writer.WriteEndObject();
        }
    }
}
