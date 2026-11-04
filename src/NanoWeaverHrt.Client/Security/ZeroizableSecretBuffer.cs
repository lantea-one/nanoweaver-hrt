using System.Security.Cryptography;
using System.Text;

namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Managed wrapper holding sensitive in-memory secrets in a byte buffer that can be deterministically zeroized.
/// </summary>
/// <remarks>
/// // WHAT: Cryptographic memory container for authentication tokens and private key material.
/// // WHY:  Fulfills Prime Directive 12 and Issue #84 (P81): Guarantees secrets are erased from heap memory upon logout or idle timeout.
/// // HOW:  Stores bytes in an isolated byte array, tracks disposal state, and zeroes memory via <see cref="CryptographicOperations.ZeroMemory"/>.
/// </remarks>
public sealed class ZeroizableSecretBuffer : IDisposable
{
    /// <summary>
    /// Underlying memory buffer holding the raw secret bytes.
    /// </summary>
    private readonly byte[] _buffer;

    /// <summary>
    /// Indicates whether the buffer has already been zeroized and disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Synchronization lock guarding buffer state mutations.
    /// </summary>
    private readonly object _syncLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroizableSecretBuffer"/> class wrapping a copy of the provided bytes.
    /// </summary>
    /// <param name="bytes">Source byte span to protect.</param>
    private ZeroizableSecretBuffer(ReadOnlySpan<byte> bytes)
    {
        // WHAT: Allocates isolated byte array and copies input span.
        // WHY:  Ensures the secret buffer owns its memory without external references.
        // HOW:  Allocates byte[] with matching length and copies span into it.
        _buffer = new byte[bytes.Length];
        bytes.CopyTo(_buffer);
    }

    /// <summary>
    /// Creates a new <see cref="ZeroizableSecretBuffer"/> from a UTF-8 string secret.
    /// </summary>
    /// <param name="secret">Plaintext secret string to wrap.</param>
    /// <returns>A new populated <see cref="ZeroizableSecretBuffer"/>.</returns>
    public static ZeroizableSecretBuffer FromUtf8(string secret)
    {
        // WHAT: Encodes string secret into UTF-8 bytes within a new ZeroizableSecretBuffer.
        // WHY:  Converts incoming token strings into securely zeroizable in-memory buffers.
        // HOW:  Validates string, encodes via Encoding.UTF8.GetBytes, and initializes buffer.
        ArgumentNullException.ThrowIfNull(secret);
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            return new ZeroizableSecretBuffer(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Creates a new <see cref="ZeroizableSecretBuffer"/> from a raw byte span.
    /// </summary>
    /// <param name="bytes">Raw secret bytes to copy.</param>
    /// <returns>A new populated <see cref="ZeroizableSecretBuffer"/>.</returns>
    public static ZeroizableSecretBuffer FromBytes(ReadOnlySpan<byte> bytes)
    {
        // WHAT: Copies provided byte span into a new zeroizable buffer.
        // WHY:  Allows wrapping binary keys or nonces without string conversions.
        // HOW:  Instantiates private constructor with span.
        return new ZeroizableSecretBuffer(bytes);
    }

    /// <summary>
    /// Creates a new <see cref="ZeroizableSecretBuffer"/> from a character span.
    /// </summary>
    /// <param name="chars">Character span containing the secret.</param>
    /// <returns>A new populated <see cref="ZeroizableSecretBuffer"/>.</returns>
    public static ZeroizableSecretBuffer FromChars(ReadOnlySpan<char> chars)
    {
        // WHAT: Encodes character span to UTF-8 bytes into a stack or pooled buffer and wraps it.
        // WHY:  Enables zeroizable token ingestion directly from character buffers without intermediate strings.
        // HOW:  Calculates UTF-8 byte count, stackallocs if small or allocates array, encodes, copies, and zeroes.
        int byteCount = Encoding.UTF8.GetByteCount(chars);
        byte[] bytes = new byte[byteCount];
        try
        {
            Encoding.UTF8.GetBytes(chars, bytes);
            return new ZeroizableSecretBuffer(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Gets a read-only span over the secret bytes if not disposed; otherwise returns an empty span.
    /// </summary>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            lock (_syncLock)
            {
                return _disposed ? ReadOnlySpan<byte>.Empty : _buffer.AsSpan();
            }
        }
    }

    /// <summary>
    /// Gets the length of the secret buffer in bytes. Returns 0 if disposed.
    /// </summary>
    public int Length
    {
        get
        {
            lock (_syncLock)
            {
                return _disposed ? 0 : _buffer.Length;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this buffer has been zeroized and disposed.
    /// </summary>
    public bool IsDisposed
    {
        get
        {
            lock (_syncLock)
            {
                return _disposed;
            }
        }
    }

    /// <summary>
    /// Decodes the secret bytes into an unprotected string representation.
    /// </summary>
    /// <returns>The decoded string, or an empty string if disposed.</returns>
    public string ToUnprotectedString()
    {
        // WHAT: Decodes underlying UTF-8 bytes into a temporary string for HTTP Authorization headers.
        // WHY:  Required by HttpClient headers while minimizing lifetime of the string in memory.
        // HOW:  Checks disposed state under lock and decodes via Encoding.UTF8.GetString.
        lock (_syncLock)
        {
            if (_disposed)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(_buffer);
        }
    }

    /// <summary>
    /// Creates a cloned copy of this buffer.
    /// </summary>
    /// <returns>A new <see cref="ZeroizableSecretBuffer"/> with identical content.</returns>
    public ZeroizableSecretBuffer Clone()
    {
        // WHAT: Creates an independent copy of this buffer.
        // WHY:  Allows transferring session tokens across ViewModels without shared lifecycle contention.
        // HOW:  Inspects Span under lock and invokes FromBytes.
        lock (_syncLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ZeroizableSecretBuffer), "Cannot clone a disposed buffer.");
            }

            return new ZeroizableSecretBuffer(_buffer);
        }
    }

    /// <summary>
    /// Wipes all memory in the underlying buffer with zeros and marks the buffer as disposed.
    /// </summary>
    public void Clear()
    {
        // WHAT: Deterministically zeroes the internal byte buffer.
        // WHY:  Prevents secrets from remaining in process memory after session termination.
        // HOW:  Invokes CryptographicOperations.ZeroMemory and flags _disposed = true.
        Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Cryptographically wipes buffer memory and marks disposed.
        // WHY:  Idempotent IDisposable implementation for deterministic cleanup.
        // HOW:  Checks _disposed under lock; executes ZeroMemory on _buffer; marks _disposed = true.
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(_buffer);
            _disposed = true;
        }
    }
}
