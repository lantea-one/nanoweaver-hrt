namespace NanoWeaverHrt.Security.Secrets;

/// <summary>
/// Supplies the symmetric key material that belongs to this sovereign node and to no other.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Two long random numbers that only this node knows. One stamps login tokens
/// so they cannot be forged; the other encrypts patient health information (PHI) before it is written to disk.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Both numbers used to be hard-coded in this public repository, which means
/// anybody who had read the source could mint an Administrator token for any deployment on earth and decrypt
/// every patient avatar and profile field. Key material must be generated per node and never shipped in source.
/// </para>
/// <para>
/// <strong>How it works:</strong> The implementation generates 256-bit keys with a cryptographic random number
/// generator on first launch, persists them in owner-only (mode 0600) files, and reads them back on every later
/// start so that tokens survive a restart and encrypted PHI stays readable.
/// </para>
/// <para>
/// <strong>On the choice of <see cref="ReadOnlyMemory{T}"/>:</strong> the key is handed out as
/// <c>ReadOnlyMemory&lt;byte&gt;</c> because that type can be stored in a field of a class that has
/// <c>async</c> methods (a <c>ReadOnlySpan&lt;byte&gt;</c> cannot) and still be passed to
/// <c>HMACSHA256.HashData</c> and <c>AesGcm</c> through <c>.Span</c> without copying. This is an API-shape
/// guard, not a sandbox: code inside this assembly can still reach the backing array through
/// <c>System.Runtime.InteropServices.MemoryMarshal</c>. It prevents accidental mutation, not a determined caller.
/// </para>
/// </remarks>
public interface INodeSecretStore
{
    /// <summary>
    /// Gets the 256-bit HMAC-SHA256 key used to seal and verify tamper-evident bearer tokens.
    /// </summary>
    /// <value>Exactly 32 bytes of key material.</value>
    ReadOnlyMemory<byte> TokenSigningKey { get; }

    /// <summary>
    /// Gets the 256-bit AES-256-GCM key used to protect patient profile fields at rest.
    /// </summary>
    /// <value>Exactly 32 bytes of key material.</value>
    ReadOnlyMemory<byte> PhiMasterKey { get; }

    /// <summary>
    /// Gets the absolute path of the directory that holds the persisted key files.
    /// </summary>
    /// <value>A directory path that exists by the time construction completes.</value>
    string NodeKeysDirectory { get; }

    /// <summary>
    /// Gets the provenance of <see cref="TokenSigningKey"/> for the current process start.
    /// </summary>
    /// <value>One of the <see cref="NodeSecretOrigin"/> values.</value>
    NodeSecretOrigin TokenSigningKeyOrigin { get; }

    /// <summary>
    /// Gets the provenance of <see cref="PhiMasterKey"/> for the current process start.
    /// </summary>
    /// <value>One of the <see cref="NodeSecretOrigin"/> values.</value>
    NodeSecretOrigin PhiMasterKeyOrigin { get; }
}
