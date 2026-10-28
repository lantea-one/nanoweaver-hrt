using System.Buffers.Binary;
using System.Text;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Produces the unambiguous, length-prefixed binary representation of a <see cref="DatasetManifest"/> over which
/// Ed25519 and ML-DSA-65 dataset signatures are computed and verified.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A pure, deterministic binary encoder that turns every signed property of a
/// <see cref="DatasetManifest"/> into a single byte array with big-endian length prefixes.
/// </para>
/// <para>
/// <strong>Why it exists (and why naive concatenation is forgeable):</strong> If a signer simply concatenated fields
/// end-to-end without length prefixes, characters could slide across field boundaries without changing the signed byte
/// stream. For example, a manifest with <c>DatasetName = "ab"</c> and <c>ArchiveFileName = "c.tar.gz"</c> and a second
/// manifest with <c>DatasetName = "abc"</c> and <c>ArchiveFileName = ".tar.gz"</c> would produce the exact same
/// concatenated bytes (<c>"abc.tar.gz"</c>), allowing one signature to authorize two different manifests. Delimiters
/// such as pipes or newlines fail whenever a filename contains that character. Prefixing every variable-length string
/// with its 4-byte unsigned big-endian UTF-8 byte count and prefixing the <c>IncludedFiles</c> list with its 4-byte
/// element count makes the decomposition mathematically unique, while reading properties from the model rather than
/// JSON text eliminates formatting, whitespace, and escaping differences.
/// </para>
/// <para>
/// <strong>How it works:</strong> Validates that no signed field is <c>null</c> and that every string is valid UTF-16
/// using a strict <see cref="UTF8Encoding"/> instance configured with <c>throwOnInvalidBytes: true</c>, computes the
/// exact output length in one pass, allocates a single <c>byte[]</c> of that size, and writes all fields in fixed order
/// via a <see cref="Span{T}"/> cursor and <see cref="BinaryPrimitives"/>. <see cref="DatasetManifest.SignatureBase64"/>
/// is never read.
/// </para>
/// </remarks>
public static class DatasetManifestCanonicalizer
{
    /// <summary>
    /// Strict UTF-8 encoding without BOM that throws <see cref="EncoderFallbackException"/> on unpaired UTF-16 surrogates.
    /// </summary>
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Computes the canonical signing byte sequence for <paramref name="manifest"/>.
    /// </summary>
    /// <param name="manifest">The dataset manifest to encode.</param>
    /// <returns>A newly allocated byte array containing the canonical length-prefixed encoding.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="manifest"/> is <c>null</c>, any signed field or <c>IncludedFiles</c> element is <c>null</c>,
    /// or any string contains ill-formed UTF-16 (such as an unpaired surrogate).
    /// </exception>
    public static byte[] GetSigningBytes(DatasetManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentException("Dataset manifest cannot be null.", nameof(manifest));
        }

        if (manifest.ManifestVersion is null)
        {
            throw new ArgumentException("ManifestVersion cannot be null when computing canonical signing bytes.", nameof(manifest));
        }

        string signatureAlgorithm = RequireNonNull(manifest.SignatureAlgorithm, nameof(manifest.SignatureAlgorithm));
        string signerKeyId = RequireNonNull(manifest.SignerKeyId, nameof(manifest.SignerKeyId));
        string sha256Checksum = RequireNonNull(manifest.Sha256Checksum, nameof(manifest.Sha256Checksum));
        string datasetName = RequireNonNull(manifest.DatasetName, nameof(manifest.DatasetName));
        string archiveFileName = RequireNonNull(manifest.ArchiveFileName, nameof(manifest.ArchiveFileName));
        string timestampUtc = RequireNonNull(manifest.TimestampUtc, nameof(manifest.TimestampUtc));

        string[]? includedFiles = manifest.IncludedFiles;
        if (includedFiles is null)
        {
            throw new ArgumentException("IncludedFiles cannot be null.", nameof(manifest));
        }

        // WHAT: Measure the exact UTF-8 byte length of every framed string field and compute total buffer size.
        // WHY:  Allows a single exact-sized byte[] allocation with zero intermediate arrays or stream resizing.
        // HOW:  Calls s_strictUtf8.GetByteCount on each field (catching EncoderFallbackException) and sums big-endian frame headers.
        int domainTagBytes = MeasureStrictUtf8ByteCount(DatasetManifestSignature.DomainTag, nameof(DatasetManifestSignature.DomainTag));
        int algorithmBytes = MeasureStrictUtf8ByteCount(signatureAlgorithm, nameof(manifest.SignatureAlgorithm));
        int keyIdBytes = MeasureStrictUtf8ByteCount(signerKeyId, nameof(manifest.SignerKeyId));
        int sha256Bytes = MeasureStrictUtf8ByteCount(sha256Checksum, nameof(manifest.Sha256Checksum));
        int datasetNameBytes = MeasureStrictUtf8ByteCount(datasetName, nameof(manifest.DatasetName));
        int archiveFileBytes = MeasureStrictUtf8ByteCount(archiveFileName, nameof(manifest.ArchiveFileName));
        int timestampBytes = MeasureStrictUtf8ByteCount(timestampUtc, nameof(manifest.TimestampUtc));

        int totalLength =
            (4 + domainTagBytes) +
            4 +
            (4 + algorithmBytes) +
            (4 + keyIdBytes) +
            (4 + sha256Bytes) +
            (4 + datasetNameBytes) +
            (4 + archiveFileBytes) +
            8 +
            (4 + timestampBytes) +
            4;

        for (int i = 0; i < includedFiles.Length; i++)
        {
            string entry = RequireNonNull(includedFiles[i], nameof(manifest.IncludedFiles));
            int entryBytes = MeasureStrictUtf8ByteCount(entry, nameof(manifest.IncludedFiles));
            totalLength += 4 + entryBytes;
        }

        // WHAT: Allocate the exact-sized canonical buffer and write every field in fixed protocol order.
        // WHY:  Guarantees that signer and verifier produce identical byte sequences without reading SignatureBase64.
        // HOW:  Advances a Span<byte> cursor using BinaryPrimitives big-endian integer writers and s_strictUtf8.GetBytes.
        byte[] buffer = new byte[totalLength];
        Span<byte> cursor = buffer.AsSpan();

        WriteFramedString(ref cursor, DatasetManifestSignature.DomainTag);
        WriteUInt32(ref cursor, (uint)manifest.ManifestVersion.Value);
        WriteFramedString(ref cursor, signatureAlgorithm);
        WriteFramedString(ref cursor, signerKeyId);
        WriteFramedString(ref cursor, sha256Checksum);
        WriteFramedString(ref cursor, datasetName);
        WriteFramedString(ref cursor, archiveFileName);
        WriteInt64(ref cursor, manifest.SizeInBytes);
        WriteFramedString(ref cursor, timestampUtc);
        WriteUInt32(ref cursor, (uint)includedFiles.Length);

        for (int i = 0; i < includedFiles.Length; i++)
        {
            WriteFramedString(ref cursor, includedFiles[i]);
        }

        return buffer;
    }

    /// <summary>
    /// Validates that a string field is not <c>null</c>.
    /// </summary>
    /// <param name="value">String value to check.</param>
    /// <param name="fieldName">Field name for the exception message.</param>
    /// <returns>The non-null string.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <c>null</c>.</exception>
    private static string RequireNonNull(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new ArgumentException($"Manifest field '{fieldName}' cannot be null.", fieldName);
        }

        return value;
    }

    /// <summary>
    /// Computes the strict UTF-8 byte count of <paramref name="value"/>, translating invalid UTF-16 surrogates into <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="value">String to measure.</param>
    /// <param name="fieldName">Field name for the exception message.</param>
    /// <returns>The exact UTF-8 byte count.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains ill-formed UTF-16 code units.</exception>
    private static int MeasureStrictUtf8ByteCount(string value, string fieldName)
    {
        try
        {
            return s_strictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException($"Manifest field '{fieldName}' contains ill-formed UTF-16 characters.", fieldName, ex);
        }
    }

    /// <summary>
    /// Writes a 4-byte unsigned big-endian integer and advances the span cursor.
    /// </summary>
    /// <param name="cursor">Target span cursor.</param>
    /// <param name="value">Unsigned 32-bit integer to write.</param>
    private static void WriteUInt32(ref Span<byte> cursor, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(cursor, value);
        cursor = cursor.Slice(4);
    }

    /// <summary>
    /// Writes an 8-byte signed two's-complement big-endian integer and advances the span cursor.
    /// </summary>
    /// <param name="cursor">Target span cursor.</param>
    /// <param name="value">Signed 64-bit integer to write.</param>
    private static void WriteInt64(ref Span<byte> cursor, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(cursor, value);
        cursor = cursor.Slice(8);
    }

    /// <summary>
    /// Writes a length-prefixed UTF-8 string (<c>U32(byteLength) || UTF8(value)</c>) and advances the span cursor.
    /// </summary>
    /// <param name="cursor">Target span cursor.</param>
    /// <param name="value">Validated string to write.</param>
    private static void WriteFramedString(ref Span<byte> cursor, string value)
    {
        int written = s_strictUtf8.GetBytes(value.AsSpan(), cursor.Slice(4));
        BinaryPrimitives.WriteUInt32BigEndian(cursor, (uint)written);
        cursor = cursor.Slice(4 + written);
    }
}
