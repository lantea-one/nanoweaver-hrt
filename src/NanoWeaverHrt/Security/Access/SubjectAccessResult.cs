namespace NanoWeaverHrt.Security.Access;

/// <summary>
/// Represents the outcome of an access authorization evaluation for a subject or cohort.
/// </summary>
public sealed record SubjectAccessResult
{
    /// <summary>
    /// Gets a value indicating whether access is permitted.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Gets the effective tiered access level granted if allowed; otherwise null.
    /// </summary>
    public SubjectAccessLevel? EffectiveLevel { get; init; }

    /// <summary>
    /// Creates an allowed access result with the specified access level.
    /// </summary>
    /// <param name="level">The effective access level granted.</param>
    /// <returns>An allowed <see cref="SubjectAccessResult"/>.</returns>
    public static SubjectAccessResult Allowed(SubjectAccessLevel level) => new()
    {
        IsAllowed = true,
        EffectiveLevel = level
    };

    /// <summary>
    /// Creates a denied access result.
    /// </summary>
    /// <returns>A denied <see cref="SubjectAccessResult"/>.</returns>
    public static SubjectAccessResult Denied() => new()
    {
        IsAllowed = false,
        EffectiveLevel = null
    };
}
