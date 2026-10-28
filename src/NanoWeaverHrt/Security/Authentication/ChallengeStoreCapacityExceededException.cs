namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Exception thrown when the in-memory authentication challenge store reaches its maximum capacity
/// of pending unconsumed challenges.
/// </summary>
public sealed class ChallengeStoreCapacityExceededException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChallengeStoreCapacityExceededException"/> class.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of active challenges permitted.</param>
    public ChallengeStoreCapacityExceededException(int maxCapacity)
        : base($"The authentication challenge store has reached its maximum capacity of {maxCapacity} pending challenges.")
    {
        MaxCapacity = maxCapacity;
    }

    /// <summary>
    /// Gets the configured maximum capacity that was exceeded.
    /// </summary>
    public int MaxCapacity { get; }
}
