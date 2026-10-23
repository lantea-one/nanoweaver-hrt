using System.Security.Cryptography;
using System.Text;

namespace NanoWeaverHrt.Security.Secrets;

/// <summary>
/// Provisions, persists and loads the two symmetric secrets this node needs: the bearer-token signing key and
/// the patient health information (PHI) master key.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A first-launch key generator. On the very first start it rolls 256 random bits
/// per secret and writes each one into its own owner-only file; on every later start it reads those files back.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Before this class existed, the token signing key was
/// <c>SHA-256("node_signing_master:" + a constant published in this repository)</c> and the PHI key was the
/// SHA-256 of another published constant. Anyone who had read the source could therefore forge a token for any
/// username and role on any deployment, and could decrypt patient profile fields taken from any backup. Random
/// per-node keys remove both primitives.
/// </para>
/// <para>
/// <strong>How it works:</strong> all provisioning happens in the constructor so that failure aborts the host
/// start rather than surfacing on a random first request. Each key file is published atomically (write a
/// uniquely named temporary file in the same directory, then rename it into place), so a half-written key is
/// never observable and two processes starting simultaneously converge on the same key.
/// </para>
/// <para>
/// <strong>Platform:</strong> this type is deliberately <strong>Linux/Unix only</strong>. File permissions are
/// applied through <see cref="FileStreamOptions.UnixCreateMode"/> and <see cref="File.SetUnixFileMode(string, UnixFileMode)"/>,
/// both of which throw <see cref="PlatformNotSupportedException"/> on Windows. That is the intended behaviour:
/// the service must refuse to start on a platform where it cannot create a key file that only its owner can
/// read, rather than silently writing world-readable key material. No operating-system branch is used, because
/// a branch that can never be taken on the Linux build, test and deployment toolchain could never be covered by
/// the mandatory 100% branch-coverage gate.
/// </para>
/// </remarks>
public sealed class NodeSecretStore : INodeSecretStore
{
    /// <summary>
    /// Length of every provisioned secret, in bytes (32 bytes = 256 bits).
    /// </summary>
    public const int SecretLengthBytes = 32;

    /// <summary>
    /// Configuration key naming the directory that holds the key files.
    /// </summary>
    internal const string NodeKeysDirectoryConfigKey = "WEAVER_NODE_KEYS_DIR";

    /// <summary>
    /// Configuration key naming the trajectory data directory, used to derive the default key directory.
    /// </summary>
    internal const string IncomingDirectoryConfigKey = "TRAJECTORY_INCOMING_DIR";

    /// <summary>
    /// Optional configuration key supplying a Base64-encoded 32-byte token signing key.
    /// </summary>
    internal const string TokenSigningKeyConfigKey = "WEAVER_TOKEN_SIGNING_KEY";

    /// <summary>
    /// Legacy configuration key supplying the PHI key as a free-text pepper.
    /// </summary>
    internal const string ProfilePepperConfigKey = "Security:ProfilePepper";

    /// <summary>
    /// Legacy environment-style configuration key supplying the PHI key as a free-text pepper.
    /// </summary>
    internal const string ProfilePepperEnvConfigKey = "SECURITY_PEPPER";

    /// <summary>
    /// File name of the persisted bearer-token signing key.
    /// </summary>
    internal const string TokenSigningKeyFileName = "token_signing.key";

    /// <summary>
    /// File name of the persisted PHI master key.
    /// </summary>
    internal const string PhiMasterKeyFileName = "phi_master.key";

    /// <summary>
    /// Name of the key directory created underneath the trajectory data directory by default.
    /// </summary>
    internal const string NodeKeysDirectoryName = "node-keys";

    /// <summary>
    /// Unix mode 0600: readable and writable by the owning account only.
    /// </summary>
    private const UnixFileMode OwnerOnlyReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Backing field for <see cref="TokenSigningKey"/>.
    /// </summary>
    private readonly byte[] _tokenSigningKey;

    /// <summary>
    /// Backing field for <see cref="PhiMasterKey"/>.
    /// </summary>
    private readonly byte[] _phiMasterKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeSecretStore"/> class, provisioning both secrets.
    /// </summary>
    /// <param name="configuration">Host application configuration supplying the optional key directory and overrides.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    /// <exception cref="NodeSecretProvisioningException">
    /// A key file exists but is unreadable or is not exactly <see cref="SecretLengthBytes"/> bytes long, or a
    /// configured override is not valid Base64 of exactly <see cref="SecretLengthBytes"/> bytes.
    /// </exception>
    public NodeSecretStore(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // WHAT: Resolves and creates the directory that will hold the two key files.
        // WHY:  The service must come up with zero operator configuration, so the directory is derived rather
        //       than demanded, and it is created if a fresh deployment has never had one.
        // HOW:  CreateDirectory is idempotent and therefore needs no existence check (and adds no branch).
        NodeKeysDirectory = ResolveNodeKeysDirectory(configuration);
        Directory.CreateDirectory(NodeKeysDirectory);

        // WHAT: Adopts an explicitly configured token signing key when one is supplied, else the key file.
        // WHY:  Operators running several replicas behind a load balancer need every replica to verify the
        //       same tokens; an orchestrator secret is the supported way to share one key.
        // HOW:  A Base64 override wins; otherwise the per-node file is loaded or created.
        byte[]? tokenOverride = TryReadBase64Override(configuration, TokenSigningKeyConfigKey);
        if (tokenOverride is not null)
        {
            _tokenSigningKey = tokenOverride;
            TokenSigningKeyOrigin = NodeSecretOrigin.ConfiguredOverride;
        }
        else
        {
            (_tokenSigningKey, TokenSigningKeyOrigin) = LoadOrCreateKeyFile(NodeKeysDirectory, TokenSigningKeyFileName);
        }

        // WHAT: Adopts the historic text pepper as the PHI key when a deployment already configures one.
        // WHY:  Deployments that set Security:ProfilePepper or SECURITY_PEPPER before this change encrypted
        //       their patient data with SHA-256 of that text. Honouring it keeps their data readable.
        // HOW:  The pepper override wins; otherwise the per-node file is loaded or created.
        byte[]? phiOverride = TryReadPepperOverride(configuration);
        if (phiOverride is not null)
        {
            _phiMasterKey = phiOverride;
            PhiMasterKeyOrigin = NodeSecretOrigin.ConfiguredOverride;
        }
        else
        {
            (_phiMasterKey, PhiMasterKeyOrigin) = LoadOrCreateKeyFile(NodeKeysDirectory, PhiMasterKeyFileName);
        }
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> TokenSigningKey => _tokenSigningKey;

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> PhiMasterKey => _phiMasterKey;

    /// <inheritdoc/>
    public string NodeKeysDirectory { get; }

    /// <inheritdoc/>
    public NodeSecretOrigin TokenSigningKeyOrigin { get; }

    /// <inheritdoc/>
    public NodeSecretOrigin PhiMasterKeyOrigin { get; }

    /// <summary>
    /// Resolves the directory that holds the key files from configuration, falling back to a derived path.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <returns>The directory path that will hold <c>token_signing.key</c> and <c>phi_master.key</c>.</returns>
    /// <remarks>
    /// Resolution order: an explicit <c>WEAVER_NODE_KEYS_DIR</c>, else a <c>node-keys</c> subdirectory of
    /// <c>TRAJECTORY_INCOMING_DIR</c> (which the container and every test host already point at a writable,
    /// persistent, git-ignored location), else <c>./simulations/shared-data/node-keys</c> under the working
    /// directory for a bare <c>dotnet run</c>. Using a subdirectory matters: the trajectory listing, export,
    /// import and download routes all operate on the top level of that directory only, so key files placed one
    /// level down are reachable by no HTTP route.
    /// </remarks>
    internal static string ResolveNodeKeysDirectory(IConfiguration configuration)
    {
        string? configured = configuration[NodeKeysDirectoryConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string? incomingDirectory = configuration[IncomingDirectoryConfigKey];
        if (!string.IsNullOrWhiteSpace(incomingDirectory))
        {
            return Path.Combine(incomingDirectory, NodeKeysDirectoryName);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data", NodeKeysDirectoryName);
    }

    /// <summary>
    /// Decodes an optional Base64-encoded key override from configuration.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="configKey">The configuration key to read.</param>
    /// <returns>The decoded 32-byte key, or <c>null</c> when the key is absent or blank.</returns>
    /// <exception cref="NodeSecretProvisioningException">
    /// The configured value is not valid Base64, or does not decode to exactly <see cref="SecretLengthBytes"/> bytes.
    /// </exception>
    internal static byte[]? TryReadBase64Override(IConfiguration configuration, string configKey)
    {
        string? raw = configuration[configKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException ex)
        {
            // WHAT: Converts a malformed override into the fail-closed provisioning error.
            // WHY:  A typo in an orchestrator secret must stop the node, not silently fall back to a file key
            //       that no other replica shares.
            // HOW:  Re-throws with the configuration key name only; the value itself is never echoed.
            throw new NodeSecretProvisioningException(
                $"Configured secret '{configKey}' is not valid Base64 data.", ex);
        }

        if (decoded.Length != SecretLengthBytes)
        {
            throw new NodeSecretProvisioningException(
                $"Configured secret '{configKey}' must decode to exactly {SecretLengthBytes} bytes.");
        }

        return decoded;
    }

    /// <summary>
    /// Derives the PHI master key from the legacy free-text pepper configuration, when one is present.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <returns>The SHA-256 digest of the configured pepper, or <c>null</c> when no pepper is configured.</returns>
    /// <remarks>
    /// This reproduces exactly what the previous implementation did with a configured pepper
    /// (<c>SHA-256(UTF-8(pepper))</c>), so an existing deployment that set one keeps its key and its data.
    /// Only the removed hard-coded default is gone.
    /// </remarks>
    internal static byte[]? TryReadPepperOverride(IConfiguration configuration)
    {
        string? pepper = configuration[ProfilePepperConfigKey] ?? configuration[ProfilePepperEnvConfigKey];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            return null;
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(pepper));
    }

    /// <summary>
    /// Reads an existing key file, enforcing its length and repairing its permissions to mode 0600.
    /// </summary>
    /// <param name="path">Absolute path of the key file.</param>
    /// <returns>The 32 key bytes read from disk.</returns>
    /// <exception cref="NodeSecretProvisioningException">
    /// The file cannot be read, or does not contain exactly <see cref="SecretLengthBytes"/> bytes.
    /// </exception>
    internal static byte[] ReadSecretFile(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new NodeSecretProvisioningException(
                $"Node secret file '{Path.GetFileName(path)}' could not be read.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new NodeSecretProvisioningException(
                $"Node secret file '{Path.GetFileName(path)}' could not be read.", ex);
        }

        // WHAT: Rejects any key file that is not exactly 256 bits long.
        // WHY:  A truncated or padded file means the key material is damaged. Regenerating over it would
        //       destroy the ability to decrypt stored patient data, so the node must stop instead.
        // HOW:  Exact length comparison, with the file name (never the directory) in the message.
        if (bytes.Length != SecretLengthBytes)
        {
            throw new NodeSecretProvisioningException(
                $"Node secret file '{Path.GetFileName(path)}' must contain exactly {SecretLengthBytes} bytes.");
        }

        // WHAT: Re-asserts owner-only permissions on every load.
        // WHY:  A backup restore, a careless chmod or a copy made with a permissive umask can widen the mode
        //       of a file holding the key to every patient record on the node.
        // HOW:  SetUnixFileMode to 0600. This call is Unix-only by design (see the type remarks): on Windows
        //       it raises PlatformNotSupportedException, which is the intended loud refusal to run where the
        //       key file cannot be made owner-only. CA1416 asks for an OperatingSystem.IsWindows() guard, and
        //       that guard is deliberately not written: it would be unreachable on the Linux build, test and
        //       deployment toolchain and so could never satisfy the mandatory 100% branch-coverage gate.
#pragma warning disable CA1416 // Platform compatibility: Unix-only by design; see type remarks.
        File.SetUnixFileMode(path, OwnerOnlyReadWrite);
#pragma warning restore CA1416
        return bytes;
    }

    /// <summary>
    /// Creates a new file containing <paramref name="secret"/>, failing if the path already exists.
    /// </summary>
    /// <param name="path">Absolute path to create.</param>
    /// <param name="secret">The key bytes to persist.</param>
    /// <exception cref="NodeSecretProvisioningException">The file exists already or could not be written.</exception>
    internal static void WriteSecretFileExclusive(string path, byte[] secret)
    {
        // WHAT: Creates the file with mode 0600 at the moment of creation.
        // WHY:  Creating it with the default mode and chmod-ing afterwards leaves a window in which another
        //       local account could open the key file.
        // HOW:  FileStreamOptions.UnixCreateMode applies the mode in the underlying open(2) call.
        //       FileMode.CreateNew guarantees this process is the one that created the file. Setting
        //       UnixCreateMode throws PlatformNotSupportedException on Windows by design (see type remarks),
        //       so CA1416 is suppressed here rather than answered with an unreachable platform branch.
#pragma warning disable CA1416 // Platform compatibility: Unix-only by design; see type remarks.
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = OwnerOnlyReadWrite
        };
#pragma warning restore CA1416

        try
        {
            using FileStream stream = new(path, options);
            stream.Write(secret);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException ex)
        {
            throw new NodeSecretProvisioningException(
                $"Node secret file '{Path.GetFileName(path)}' could not be written.", ex);
        }
    }

    /// <summary>
    /// Loads the named key file if it exists, otherwise generates a fresh key and publishes it atomically.
    /// </summary>
    /// <param name="directory">Directory holding the key files.</param>
    /// <param name="fileName">Key file name, for example <c>token_signing.key</c>.</param>
    /// <param name="concurrentWriterHook">
    /// Test seam invoked with the destination path immediately before the atomic rename, so the lost-race
    /// branch can be exercised deterministically without threads or sleeps. Always <c>null</c> in production.
    /// </param>
    /// <returns>The key bytes together with their provenance.</returns>
    /// <exception cref="NodeSecretProvisioningException">The file exists but is unreadable or malformed.</exception>
    internal static (byte[] Key, NodeSecretOrigin Origin) LoadOrCreateKeyFile(
        string directory,
        string fileName,
        Action<string>? concurrentWriterHook = null)
    {
        string path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            return (ReadSecretFile(path), NodeSecretOrigin.ExistingFile);
        }

        // WHAT: Generates 256 bits of key material from the operating system's cryptographic RNG.
        // WHY:  This is the one and only source of the node's authentication and PHI secrets; a predictable
        //       generator (Random, Guid, a hash of a constant) would re-introduce the forged-token defect.
        // HOW:  RandomNumberGenerator.GetBytes, which draws from getrandom(2).
        byte[] generated = RandomNumberGenerator.GetBytes(SecretLengthBytes);

        // The Guid below is only used to make the temporary file name unique. It is a NAME, never key material.
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        WriteSecretFileExclusive(tempPath, generated);

        concurrentWriterHook?.Invoke(path);

        try
        {
            // WHAT: Publishes the key by renaming it into its final name.
            // WHY:  A reader must never observe a partially written key file, and two nodes starting at the
            //       same moment must end up agreeing on one key.
            // HOW:  File.Move with overwrite:false is a single atomic rename that fails if the name was taken.
            File.Move(tempPath, path, overwrite: false);
            return (generated, NodeSecretOrigin.GeneratedFile);
        }
        catch (IOException)
        {
            // WHAT: Handles losing the publication race.
            // WHY:  Another process published the file first; its value is authoritative, because it may
            //       already have signed tokens or encrypted data. Returning our own bytes would split the node.
            // HOW:  Wipe the generated key from memory, remove our temporary file, and adopt the winner's key.
            CryptographicOperations.ZeroMemory(generated);
            File.Delete(tempPath);
            return (ReadSecretFile(path), NodeSecretOrigin.ExistingFile);
        }
    }
}
