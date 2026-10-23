namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Thrown when the selected database backend cannot be reached or prepared. The message names only the
/// backend; the driver exception is kept in <see cref="Exception.InnerException"/> and never shown to clients.
/// </summary>
public sealed class WeaverDatabaseUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDatabaseUnavailableException"/> class.
    /// </summary>
    /// <param name="backend">The backend that could not be reached.</param>
    /// <param name="innerException">The driver exception; it may contain connection details, so it is never surfaced.</param>
    public WeaverDatabaseUnavailableException(WeaverDatabaseBackend backend, Exception innerException)
        : base($"The {backend} database backend is unavailable.", innerException)
    {
        Backend = backend;
    }

    /// <summary>Gets the backend that could not be reached.</summary>
    public WeaverDatabaseBackend Backend { get; }
}
