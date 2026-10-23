namespace NanoWeaverHrt.Security.Secrets;

/// <summary>
/// Describes where a node secret came from during the current process start.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A provenance label attached to each key the node uses for bearer-token
/// signing and patient data encryption.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Tests must be able to assert first-launch generation versus reuse without
/// inspecting the filesystem, and an operator reading the startup log must be able to tell whether this boot
/// created brand-new key material (which invalidates every previously issued token) or adopted the persisted
/// key material (which keeps sessions and encrypted patient data readable).
/// </para>
/// <para>
/// <strong>How it works:</strong> <see cref="NodeSecretStore"/> records one value per key while provisioning.
/// </para>
/// </remarks>
public enum NodeSecretOrigin
{
    /// <summary>
    /// The key file did not exist and this process generated and published it.
    /// </summary>
    GeneratedFile = 0,

    /// <summary>
    /// The key file already existed on disk and its bytes were adopted unchanged.
    /// </summary>
    ExistingFile = 1,

    /// <summary>
    /// The key was supplied explicitly through configuration, so no key file was read or written.
    /// </summary>
    ConfiguredOverride = 2
}
