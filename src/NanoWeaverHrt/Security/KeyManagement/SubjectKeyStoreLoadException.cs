namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Exception thrown when the subject key store cannot be loaded or validated cleanly during service startup
/// or when a concurrently inserted row fails cryptographic integrity verification.
/// </summary>
public sealed class SubjectKeyStoreLoadException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyStoreLoadException"/> class with a diagnostic message.
    /// </summary>
    /// <param name="message">The client-safe message explaining why the subject key store failed to load.</param>
    public SubjectKeyStoreLoadException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectKeyStoreLoadException"/> class with a diagnostic message and inner cause.
    /// </summary>
    /// <param name="message">The client-safe message explaining why the subject key store failed to load.</param>
    /// <param name="innerException">The underlying exception that caused the load failure.</param>
    public SubjectKeyStoreLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
