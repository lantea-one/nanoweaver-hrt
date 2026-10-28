using System.Globalization;
using System.Text;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Append-only file journal stored outside the database in <see cref="INodeSecretStore.NodeKeysDirectory"/>
/// so that restoring a pre-erasure database backup cannot silently undo a GDPR Article 17 cryptographic erasure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A plain UTF-8 append-only log (<c>subject_erasures.journal</c>) recording each
/// erased <c>subject_key</c> and its UTC erasure timestamp. It holds no cryptographic key material.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> If an operator restores <c>weaver_config.db</c> or a PostgreSQL dump from a
/// backup taken before a subject was crypto-shredded, the database alone would resurrect that subject's active row.
/// Reconciling the database against this journal at startup before opening any active key ensures every recorded
/// erasure is re-applied immediately.
/// </para>
/// <para>
/// <strong>How it works:</strong> Appends each erasure entry under a thread lock with owner-only permissions
/// (<c>0600</c>) and flushes to disk before the database <c>UPDATE</c> runs. At startup, <see cref="ReadAll"/>
/// validates every line strictly and deduplicates retries by keeping the earliest timestamp per subject key.
/// </para>
/// </remarks>
public sealed class SubjectErasureJournal
{
    /// <summary>
    /// File name of the erasure journal inside the node keys directory.
    /// </summary>
    internal const string FileName = "subject_erasures.journal";

    /// <summary>
    /// Versioned first-line header required in every non-empty erasure journal file.
    /// </summary>
    internal const string HeaderLine = "weaver-subject-erasure-journal v1";

    /// <summary>
    /// Synchronization lock serializing file reads and appends.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// Directory where node-local secret and journal files reside.
    /// </summary>
    private readonly string _nodeKeysDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubjectErasureJournal"/> class.
    /// </summary>
    /// <param name="secretStore">The node secret store providing <see cref="INodeSecretStore.NodeKeysDirectory"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="secretStore"/> is <c>null</c>.</exception>
    public SubjectErasureJournal(INodeSecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _nodeKeysDirectory = secretStore.NodeKeysDirectory;
    }

    /// <summary>
    /// Appends a subject erasure record to <c>subject_erasures.journal</c> and flushes it to disk.
    /// </summary>
    /// <param name="subjectKey">The normalized uppercase subject key being erased.</param>
    /// <param name="shreddedAtUtc">The UTC timestamp of the erasure.</param>
    /// <exception cref="ArgumentException"><paramref name="subjectKey"/> is null, empty, or whitespace.</exception>
    public void Append(string subjectKey, DateTimeOffset shreddedAtUtc)
    {
        // WHAT: Appends an erasure line to subject_erasures.journal with mode 0600 and flushes to disk.
        // WHY:  Records the erasure outside the database before the database row is updated so a database backup restore cannot undo it.
        // HOW:  Writes the header if creating a new file, then writes E\t<base64>\t<ISO-8601>\n in one Write call and calls Flush(true).
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        lock (_sync)
        {
            Directory.CreateDirectory(_nodeKeysDirectory);
            string path = Path.Combine(_nodeKeysDirectory, FileName);

#pragma warning disable CA1416 // Platform compatibility: Unix-only by design; see NodeSecretStore remarks.
            FileStreamOptions options = new()
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            };
#pragma warning restore CA1416

            using FileStream stream = new(path, options);
            if (stream.Length == 0)
            {
                byte[] headerBytes = Encoding.UTF8.GetBytes(HeaderLine + "\n");
                stream.Write(headerBytes);
            }

            string b64Key = Convert.ToBase64String(Encoding.UTF8.GetBytes(subjectKey));
            string timestamp = shreddedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            byte[] entryBytes = Encoding.UTF8.GetBytes($"E\t{b64Key}\t{timestamp}\n");
            stream.Write(entryBytes);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// Reads and validates all entries from <c>subject_erasures.journal</c>, keeping the earliest timestamp for duplicate keys.
    /// </summary>
    /// <returns>A read-only list of deduplicated <see cref="SubjectErasureJournalEntry"/> items, or an empty list when the file does not exist.</returns>
    /// <exception cref="SubjectKeyStoreLoadException">The journal file exists but has a malformed header, line format, Base64 key, timestamp, or missing trailing newline.</exception>
    public IReadOnlyList<SubjectErasureJournalEntry> ReadAll()
    {
        // WHAT: Parses and validates subject_erasures.journal using IndexOf without Split, Regex, or LINQ.
        // WHY:  A corrupted or truncated erasure journal must fail startup loudly rather than silently skipping an erasure.
        // HOW:  Checks for file existence, verifies each newline-terminated line, and deduplicates by earliest timestamp.
        lock (_sync)
        {
            string path = Path.Combine(_nodeKeysDirectory, FileName);
            if (!File.Exists(path))
            {
                return Array.Empty<SubjectErasureJournalEntry>();
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            int pos = 0;
            int lineNumber = 1;
            Dictionary<string, DateTimeOffset> earliestByKey = new(StringComparer.Ordinal);
            List<string> order = new();

            do
            {
                int nextNewline = text.IndexOf('\n', pos);
                if (nextNewline < 0)
                {
                    throw new SubjectKeyStoreLoadException(
                        $"Malformed erasure journal '{FileName}' at line {lineNumber}: missing trailing newline.");
                }

                string line = text.Substring(pos, nextNewline - pos);
                if (lineNumber == 1)
                {
                    if (line != HeaderLine)
                    {
                        throw new SubjectKeyStoreLoadException(
                            $"Malformed erasure journal '{FileName}' at line 1: invalid header.");
                    }
                }
                else
                {
                    ParseEntryLine(line, lineNumber, out string subjectKey, out DateTimeOffset shreddedAtUtc);
                    if (!earliestByKey.TryGetValue(subjectKey, out DateTimeOffset existing))
                    {
                        earliestByKey[subjectKey] = shreddedAtUtc;
                        order.Add(subjectKey);
                    }
                    else if (shreddedAtUtc < existing)
                    {
                        earliestByKey[subjectKey] = shreddedAtUtc;
                    }
                }

                pos = nextNewline + 1;
                lineNumber++;
            }
            while (pos < text.Length);

            List<SubjectErasureJournalEntry> entries = new(order.Count);
            foreach (string key in order)
            {
                entries.Add(new SubjectErasureJournalEntry(key, earliestByKey[key]));
            }

            return entries;
        }
    }

    /// <summary>
    /// Parses a single <c>E\t&lt;Base64&gt;\t&lt;ISO-8601&gt;</c> journal line.
    /// </summary>
    /// <param name="line">The line text without its trailing newline.</param>
    /// <param name="lineNumber">The 1-based line number for error reporting.</param>
    /// <param name="subjectKey">When successful, receives the decoded UTF-8 subject key.</param>
    /// <param name="shreddedAtUtc">When successful, receives the parsed UTC timestamp.</param>
    /// <exception cref="SubjectKeyStoreLoadException">The line tag, tab delimiters, Base64 payload, or timestamp is invalid.</exception>
    private static void ParseEntryLine(
        string line,
        int lineNumber,
        out string subjectKey,
        out DateTimeOffset shreddedAtUtc)
    {
        // WHAT: Extracts the Base64 subject key and ISO-8601 round-trip timestamp from one journal line.
        // WHY:  Enforces strict structural integrity on every journal record without allocations from String.Split.
        // HOW:  Locates tab separators with IndexOf, decodes Base64, and parses the timestamp with TryParseExact("O").
        int secondTab = line.IndexOf('\t', 2);
        if (!line.StartsWith("E\t", StringComparison.Ordinal) || secondTab <= 2)
        {
            throw new SubjectKeyStoreLoadException(
                $"Malformed erasure journal '{FileName}' at line {lineNumber}: expected 'E<tab><base64><tab><timestamp>'.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(line.Substring(2, secondTab - 2));
        }
        catch (FormatException ex)
        {
            throw new SubjectKeyStoreLoadException(
                $"Malformed erasure journal '{FileName}' at line {lineNumber}: invalid Base64 subject key.", ex);
        }

        if (!DateTimeOffset.TryParseExact(
                line[(secondTab + 1)..],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out shreddedAtUtc))
        {
            throw new SubjectKeyStoreLoadException(
                $"Malformed erasure journal '{FileName}' at line {lineNumber}: invalid ISO-8601 timestamp.");
        }

        subjectKey = Encoding.UTF8.GetString(keyBytes);
    }
}
