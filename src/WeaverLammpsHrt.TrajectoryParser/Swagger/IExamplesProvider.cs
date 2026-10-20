namespace WeaverLammpsHrt.TrajectoryParser.Swagger;

/// <summary>
/// Defines a contract for domain models and request/response payloads to supply representative OpenAPI example instances.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A strongly-typed interface allowing classes and records to define their own canonical OpenAPI examples.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Placing example generation directly on the target models keeps the codebase concise, eliminates
/// boilerplate provider classes, and ensures documentation examples evolve synchronously with model schema changes.
/// </para>
/// <para>
/// <strong>How it works:</strong> Swagger schema and operation filters inspect registered types. If a type implements
/// <see cref="IExamplesProvider{T}"/>, the filter invokes <see cref="GetExamples"/> and generates JSON (camelCase),
/// XML, and YAML (lower snake-case with hyphens) examples.
/// </para>
/// </remarks>
/// <typeparam name="T">The model type providing canonical examples.</typeparam>
public interface IExamplesProvider<out T>
{
    /// <summary>
    /// Generates a representative, valid example instance of <typeparamref name="T"/> for OpenAPI 3.0 documentation.
    /// </summary>
    /// <returns>A populated example instance of <typeparamref name="T"/>.</returns>
    T GetExamples();
}
