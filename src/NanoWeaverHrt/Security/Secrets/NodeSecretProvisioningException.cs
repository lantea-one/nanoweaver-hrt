namespace NanoWeaverHrt.Security.Secrets;

/// <summary>
/// Thrown when node key material cannot be provisioned, read, or validated.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The fail-closed signal raised while the node is trying to obtain its token
/// signing key or its patient-data encryption key.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> There is deliberately no fallback key. Quietly regenerating a key over a
/// damaged file would make every stored patient ciphertext permanently undecryptable without telling anyone,
/// and quietly deriving one from a constant is exactly the published-secret defect this subsystem removes.
/// A distinct exception type lets the host abort startup loudly and lets tests assert the failure precisely.
/// </para>
/// <para>
/// <strong>How it works:</strong> <see cref="NodeSecretStore"/> throws it from the constructor, so the web host
/// never reaches the point of serving requests. Messages name the offending file name only — never the
/// containing directory, never a connection string and never key bytes — so that the text remains safe even if
/// it were ever surfaced to an API client (Prime Directive 14).
/// </para>
/// </remarks>
public sealed class NodeSecretProvisioningException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSecretProvisioningException"/> class.
    /// </summary>
    /// <param name="message">A message that names the offending key file but no directory path or key bytes.</param>
    public NodeSecretProvisioningException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSecretProvisioningException"/> class with an inner cause.
    /// </summary>
    /// <param name="message">A message that names the offending key file but no directory path or key bytes.</param>
    /// <param name="innerException">The underlying I/O, access or decoding failure.</param>
    public NodeSecretProvisioningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
