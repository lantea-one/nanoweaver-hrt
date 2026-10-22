using System.Formats.Tar;

namespace NanoWeaverHrt.Services;

/// <summary>
/// The single authoritative policy describing which files may cross a dataset bundle boundary, in either direction.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary.</strong> Sovereign research nodes trade simulation results as <c>.tar.gz</c>
/// bundles: a list of files glued together and compressed. Both ends of that exchange need the same answer to one
/// question — "is this file part of a dataset, or is it something that merely happens to live in the same folder?"
/// The shared data volume holds both: the coordinate trajectories a dataset is made of, and <c>weaver_config.db</c>,
/// the SQLite file listing every user account, their role and their public key, alongside whatever other artifacts
/// the node keeps there.
/// </para>
/// <para>
/// <strong>Why one class and not two lists.</strong> Export decides what goes into a bundle and import decides what
/// may come out of one. If those decisions are expressed as two separate lists they will drift, and the first
/// divergence is a security hole or a broken round trip: a suffix added to export but not import makes every bundle
/// un-importable, and a suffix added to import but not export silently reopens an exfiltration or overwrite route.
/// This repository already carries that failure mode elsewhere (the default database password is duplicated in three
/// files), so the two directions are deliberately wired to the same constant here.
/// </para>
/// <para>
/// <strong>The two defects this policy closes.</strong> On the import side, a bundle member plainly named
/// <c>weaver_config.db</c> used to be written straight over the live identity database, letting a Researcher upload a
/// database in which they are an Administrator. On the export side, the bundle was assembled from every file in the
/// data volume with no filter at all, so every export quietly contained that same identity and patient database —
/// an exfiltration of the whole account and PHI store through the documented, advertised export feature.
/// </para>
/// </remarks>
internal static class DatasetMemberPolicy
{
    /// <summary>
    /// File name suffixes that may appear as a member of a dataset bundle, in either direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an allow-list, not a deny-list. Anything not named here is excluded on export and refused on import.
    /// That is what keeps the node's SQLite database (<c>*.db</c>), private key material (<c>*.pem</c>), executables,
    /// shell scripts and nested archives from travelling in either direction.
    /// </para>
    /// <para>
    /// The entries are the molecular dynamics coordinate formats the parsers understand (<c>.xyz</c>, <c>.dcd</c>,
    /// <c>.dump</c>, <c>.lammpstrj</c>), the oxDNA and LAMMPS input artifacts that accompany them (<c>.top</c>,
    /// <c>.dat</c>, <c>.conf</c>, <c>.in</c>), and the plain-text and columnar side-cars a dataset carries for
    /// provenance (<c>.json</c>, <c>.txt</c>, <c>.md</c>, <c>.log</c>, <c>.csv</c>, <c>.parquet</c>).
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlySet<string> AllowedMemberExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".xyz", ".dcd", ".dump", ".lammpstrj", ".top", ".dat", ".conf", ".in",
        ".json", ".txt", ".md", ".log", ".csv", ".parquet"
    };

    /// <summary>
    /// Determines whether a file may be a member of a dataset bundle, judged purely by its suffix.
    /// </summary>
    /// <param name="fileName">A plain file name, with or without a leading directory path.</param>
    /// <returns>True when the suffix appears in <see cref="AllowedMemberExtensions"/>; otherwise false.</returns>
    /// <remarks>
    /// Consulted by the export path to decide what to pack and by the import path to decide what to unpack, so the
    /// two can never disagree. A file with no suffix at all is excluded, because every artifact a dataset
    /// legitimately carries has one.
    /// </remarks>
    internal static bool IsAllowedMemberName(string fileName)
    {
        return AllowedMemberExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>
    /// Determines whether a TAR member type may be imported.
    /// </summary>
    /// <param name="entryType">The member type declared in the TAR header.</param>
    /// <returns>True for regular file members only; false for every other member type.</returns>
    /// <remarks>
    /// Only plain data files are accepted. A symbolic or hard link member is a request to create a pointer to some
    /// other location on the host, which a later member of the same archive (or a later import) could then write
    /// through to reach the identity database or any other file the service account can touch — so links are refused
    /// rather than followed. Directory members are refused because every accepted name is a plain file name and so
    /// needs no directory created for it, and device, FIFO and metadata members have no meaning for a dataset at all.
    /// <c>V7RegularFile</c> is accepted alongside <c>RegularFile</c> because the pre-POSIX V7 tar format encodes an
    /// ordinary file with a different type byte, and some older simulation tooling still writes it.
    /// </remarks>
    internal static bool IsAllowedArchiveEntryType(TarEntryType entryType)
    {
        return entryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile;
    }

    /// <summary>
    /// Determines whether a TAR member name is a plain file name that cannot escape the import directory.
    /// </summary>
    /// <param name="entryName">The member name exactly as recorded in the TAR header.</param>
    /// <returns>True when the name is a single, non-relative file name component; otherwise false.</returns>
    /// <remarks>
    /// <para>
    /// The name is <em>rejected</em>, never repaired. Repairing it was the original defect: trimming
    /// <c>../../weaver.db</c> down to <c>weaver.db</c> still produced a write, just to a different file than the
    /// attacker named. Refusing the archive outright means nothing is written and the caller is told why.
    /// </para>
    /// <para>
    /// Both separator characters are checked regardless of host platform, because a backslash is an ordinary
    /// filename character on Linux and so <see cref="Path.GetFileName(string)"/> would hand back a Windows-style path
    /// such as <c>..\\..\\weaver.db</c> unchanged. The colon check rejects drive qualifiers and NTFS alternate data
    /// stream syntax for the same reason.
    /// </para>
    /// </remarks>
    internal static bool IsSafeArchiveEntryName(string? entryName)
    {
        // WHAT: Rejects anything that is not a bare file name.
        // WHY:  A bare file name combined with the import directory can only ever resolve inside that directory, so
        //       this single invariant removes path traversal as a category rather than patching instances of it.
        // HOW:  Separator, drive-qualifier and relative-segment characters are refused outright, and the name is
        //       compared against its own Path.GetFileName to catch anything the explicit checks missed.
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        if (entryName.Contains('/', StringComparison.Ordinal) || entryName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (entryName.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(entryName, ".", StringComparison.Ordinal) || string.Equals(entryName, "..", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(entryName, Path.GetFileName(entryName), StringComparison.Ordinal);
    }
}
