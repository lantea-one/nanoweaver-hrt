using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Services;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Security tests for the dataset import path, covering audit finding C3: an imported <c>.tar.gz</c> bundle must
/// never be able to overwrite the node's authoritative SQLite identity database, nor write to any location outside
/// the import directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary of the hole being tested.</strong> Research nodes swap simulation results as
/// <c>.tar.gz</c> bundles, which are just a list of files glued together and compressed. The import endpoint used to
/// unpack every file in the bundle straight into the shared data folder, overwriting whatever was already there. That
/// folder also holds <c>weaver_config.db</c>, the small database file listing every user account, their role
/// (Administrator, Researcher or Patient) and their public key. So a Researcher — the lowest role allowed to import
/// at all — could put their own hand-made copy of that database inside a bundle, upload it, and be an Administrator a
/// moment later. The same trick could quietly replace other scientists' trajectory files.
/// </para>
/// <para>
/// Each test below builds a deliberately hostile bundle in memory, imports it, and asserts both that the import was
/// refused and that every sentinel file on disk is byte-for-byte untouched.
/// </para>
/// </remarks>
public sealed class DatasetImportHardeningTests : IDisposable
{
    /// <summary>
    /// Root of this test instance's isolated temporary file system.
    /// </summary>
    private readonly string _testRoot;

    /// <summary>
    /// The active data volume bound to <c>TRAJECTORY_INCOMING_DIR</c>, where imports land.
    /// </summary>
    private readonly string _dataDir;

    /// <summary>
    /// The export volume bound to <c>TRAJECTORY_EXPORT_DIR</c>.
    /// </summary>
    private readonly string _exportDir;

    /// <summary>
    /// A sibling directory of the data volume used to prove that nothing escapes the import directory.
    /// </summary>
    private readonly string _outsideDir;

    /// <summary>
    /// Exact bytes of the sentinel database file, used to prove the identity database was never modified.
    /// </summary>
    private const string DatabaseSentinelContent = "ORIGINAL-IDENTITY-DATABASE-DO-NOT-REPLACE";

    /// <summary>
    /// Exact bytes of the sentinel file placed outside the import directory.
    /// </summary>
    private const string OutsideSentinelContent = "ORIGINAL-FILE-OUTSIDE-THE-IMPORT-DIRECTORY";

    /// <summary>
    /// Creates an isolated data volume seeded with the sentinel files every test asserts against.
    /// </summary>
    /// <remarks>
    /// The directories live under the OS temp path, never under the repository's <c>simulations/</c> tree, so a test
    /// run can never touch an operator's real trajectories or real user database.
    /// </remarks>
    public DatasetImportHardeningTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "weaver_import_sec_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_testRoot, "shared-data");
        _exportDir = Path.Combine(_testRoot, "exports");
        _outsideDir = Path.Combine(_testRoot, "outside");

        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_exportDir);
        Directory.CreateDirectory(_outsideDir);

        File.WriteAllText(Path.Combine(_dataDir, "weaver_config.db"), DatabaseSentinelContent);
        File.WriteAllText(Path.Combine(_outsideDir, "weaver_config.db"), OutsideSentinelContent);
        File.WriteAllText(Path.Combine(_outsideDir, "victim.xyz"), OutsideSentinelContent);
    }

    /// <summary>
    /// Removes this instance's temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a file still held open must never fail a test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: a locked or read-only entry must never fail a test run.
        }
    }

    /// <summary>
    /// Describes one member to place inside a synthetic hostile archive.
    /// </summary>
    /// <param name="EntryType">The TAR member type to declare in the header.</param>
    /// <param name="Name">The member name to record, verbatim and unsanitised.</param>
    /// <param name="Content">The member's file content, or null for members that carry no data.</param>
    /// <param name="LinkName">The link target for symbolic and hard link members, or null otherwise.</param>
    private sealed record ArchiveMemberSpec(TarEntryType EntryType, string Name, string? Content, string? LinkName);

    /// <summary>
    /// Builds a GZIP-compressed TAR archive containing exactly the supplied members, bypassing every sanitisation
    /// the production export path would apply.
    /// </summary>
    /// <param name="members">The members to write, in order.</param>
    /// <returns>The raw <c>.tar.gz</c> bytes.</returns>
    /// <remarks>
    /// Entry names are written straight into the TAR header, which is what lets this helper forge the traversal,
    /// absolute-path and symbolic-link members a real attacker would hand-craft.
    /// </remarks>
    private static byte[] BuildArchive(params ArchiveMemberSpec[] members)
    {
        using MemoryStream archive = new();

        using (GZipStream gzip = new(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (TarWriter tarWriter = new(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (ArchiveMemberSpec member in members)
            {
                PaxTarEntry entry = new(member.EntryType, member.Name);

                if (member.LinkName != null)
                {
                    entry.LinkName = member.LinkName;
                }

                if (member.Content != null)
                {
                    entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(member.Content));
                }

                tarWriter.WriteEntry(entry);
            }
        }

        return archive.ToArray();
    }

    /// <summary>
    /// Creates a manifest whose SHA-256 checksum genuinely matches the supplied archive bytes.
    /// </summary>
    /// <param name="archiveBytes">The archive the manifest should describe.</param>
    /// <returns>A manifest that passes the import endpoint's integrity check.</returns>
    /// <remarks>
    /// This is the point the audit made about the integrity check: the checksum is supplied by the uploader in the
    /// same request, so it proves only self-consistency and is no obstacle at all to a hostile bundle. Every test
    /// here therefore passes the checksum stage and is judged purely on the extraction hardening.
    /// </remarks>
    private static DatasetManifest BuildMatchingManifest(byte[] archiveBytes)
    {
        return new DatasetManifest
        {
            DatasetName = "hostile-dataset",
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            ArchiveFileName = "hostile-dataset.tar.gz",
            Sha256Checksum = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant(),
            SizeInBytes = archiveBytes.Length
        };
    }

    /// <summary>
    /// Constructs the service under test bound to this instance's isolated directories.
    /// </summary>
    /// <param name="extraSettings">Additional configuration keys, used to shrink the import ceilings for testing.</param>
    /// <returns>A configured <see cref="DatasetTransferService"/>.</returns>
    private DatasetTransferService CreateService(Dictionary<string, string?>? extraSettings = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["TRAJECTORY_INCOMING_DIR"] = _dataDir,
            ["TRAJECTORY_EXPORT_DIR"] = _exportDir
        };

        if (extraSettings != null)
        {
            foreach (KeyValuePair<string, string?> pair in extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new DatasetTransferService(configuration, NullLogger<DatasetTransferService>.Instance);
    }

    /// <summary>
    /// Imports the supplied archive and asserts the whole bundle was refused.
    /// </summary>
    /// <param name="archiveBytes">The hostile archive bytes.</param>
    /// <param name="service">The service instance to import through.</param>
    /// <returns>The sanitised rejection message.</returns>
    private static async Task<string> AssertImportRefusedAsync(byte[] archiveBytes, DatasetTransferService service)
    {
        using MemoryStream archiveStream = new(archiveBytes);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportDatasetAsync(archiveStream, BuildMatchingManifest(archiveBytes)));

        Assert.StartsWith("Dataset import rejected", error.Message, StringComparison.Ordinal);
        return error.Message;
    }

    /// <summary>
    /// Asserts that both sentinel files and the wider file system are exactly as the constructor left them.
    /// </summary>
    private void AssertNothingWasOverwritten()
    {
        Assert.Equal(DatabaseSentinelContent, File.ReadAllText(Path.Combine(_dataDir, "weaver_config.db")));
        AssertOutsideSentinelsUntouched();
    }

    /// <summary>
    /// Asserts that the sentinel files sitting outside the import directory are exactly as the constructor left them.
    /// </summary>
    /// <remarks>
    /// Used on its own by the HTTP-level test, which must let the real application host own its data volume (and
    /// therefore create a genuine SQLite database there) rather than seeding a fake one.
    /// </remarks>
    private void AssertOutsideSentinelsUntouched()
    {
        Assert.Equal(OutsideSentinelContent, File.ReadAllText(Path.Combine(_outsideDir, "weaver_config.db")));
        Assert.Equal(OutsideSentinelContent, File.ReadAllText(Path.Combine(_outsideDir, "victim.xyz")));
    }

    /// <summary>
    /// Verifies that a relative traversal member name is refused outright rather than trimmed into a name that still
    /// writes somewhere.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsRelativeTraversalMember()
    {
        // WHAT: Imports an archive whose single member is named "../../weaver_config.db".
        // WHY:  This is the canonical Zip Slip payload. The old code ran Path.GetFileName over it, which silently
        //       turned it into "weaver_config.db" and then overwrote the real identity database with it.
        // HOW:  Asserts the import throws and that both the in-volume and out-of-volume sentinels are untouched.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "../../weaver_config.db", "ATTACKER-CONTROLLED-DATABASE", null));

        await AssertImportRefusedAsync(archive, CreateService());

        AssertNothingWasOverwritten();
        Assert.False(File.Exists(Path.Combine(_dataDir, "weaver.db")));
    }

    /// <summary>
    /// Verifies that a single-level traversal member targeting a sibling directory is refused.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsSingleLevelTraversalMember()
    {
        // WHAT: Imports an archive whose member name climbs one directory and lands on a real victim file.
        // WHY:  Proves the refusal is structural rather than a special case for the deep "../../" form.
        // HOW:  Targets ../outside/victim.xyz, whose suffix is allow-listed, so only the name check can stop it.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "../outside/victim.xyz", "OVERWRITTEN", null));

        await AssertImportRefusedAsync(archive, CreateService());

        AssertNothingWasOverwritten();
    }

    /// <summary>
    /// Verifies that an absolute member path is refused rather than being rebased into the import directory.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsAbsolutePathMember()
    {
        // WHAT: Imports an archive whose member name is a fully qualified path to a file outside the volume.
        // WHY:  An absolute name is the other half of the traversal family; rebasing it would still produce a write.
        // HOW:  Uses the real absolute path of the out-of-volume sentinel and asserts that file is untouched.
        string absoluteTarget = Path.Combine(_outsideDir, "victim.xyz");
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, absoluteTarget, "OVERWRITTEN", null));

        await AssertImportRefusedAsync(archive, CreateService());

        AssertNothingWasOverwritten();
    }

    /// <summary>
    /// Verifies that a symbolic link member is refused.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsSymbolicLinkMember()
    {
        // WHAT: Imports an archive containing a symbolic link named like an ordinary trajectory file but pointing at
        //       the identity database.
        // WHY:  A planted symlink is a writable handle to a file elsewhere on the host: a later import writing to
        //       "link.xyz" would follow it straight into the database, so the link must never be created.
        // HOW:  Asserts the import throws and that no link was left behind in the data volume.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.SymbolicLink, "link.xyz", null, Path.Combine(_dataDir, "weaver_config.db")));

        string message = await AssertImportRefusedAsync(archive, CreateService());

        Assert.Contains("not a regular file", message, StringComparison.Ordinal);
        AssertNothingWasOverwritten();
        Assert.False(File.Exists(Path.Combine(_dataDir, "link.xyz")));
    }

    /// <summary>
    /// Verifies that a hard link member is refused.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsHardLinkMember()
    {
        // WHAT: Imports an archive containing a hard link member.
        // WHY:  A hard link is the same escalation primitive as a symbolic link without the indirection.
        // HOW:  Asserts the import throws before anything is written.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.HardLink, "hard.xyz", null, Path.Combine(_dataDir, "weaver_config.db")));

        await AssertImportRefusedAsync(archive, CreateService());

        AssertNothingWasOverwritten();
        Assert.False(File.Exists(Path.Combine(_dataDir, "hard.xyz")));
    }

    /// <summary>
    /// Verifies that a directory member is refused.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsDirectoryMember()
    {
        // WHAT: Imports an archive containing a directory member.
        // WHY:  Every accepted member name is a plain file name, so no directory ever needs creating; accepting one
        //       would reintroduce nested paths by the back door.
        // HOW:  Asserts the import throws and no directory appeared in the volume.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.Directory, "nested", null, null));

        await AssertImportRefusedAsync(archive, CreateService());

        Assert.False(Directory.Exists(Path.Combine(_dataDir, "nested")));
    }

    /// <summary>
    /// Verifies that a plainly named database member is refused by the suffix allow-list, closing the actual
    /// privilege-escalation route.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsIdentityDatabaseMember_WithNoTraversalAtAll()
    {
        // WHAT: Imports an archive whose single member is literally named "weaver_config.db" with no path trickery.
        // WHY:  This is the escalation the audit actually described: Path.GetFileName left this name untouched, and
        //       overwrite:true then replaced the live user database with the attacker's, making them Administrator.
        // HOW:  Asserts the import throws on the suffix allow-list and the real database is byte-identical afterwards.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "weaver_config.db", "ATTACKER-CONTROLLED-DATABASE", null));

        string message = await AssertImportRefusedAsync(archive, CreateService());

        Assert.Contains("not an accepted trajectory or dataset artifact", message, StringComparison.Ordinal);
        AssertNothingWasOverwritten();
    }

    /// <summary>
    /// Verifies that other inherently dangerous member suffixes are refused.
    /// </summary>
    /// <param name="memberName">The member name to attempt.</param>
    [Theory]
    [InlineData("node_signing_master.pem")]
    [InlineData("payload.sh")]
    [InlineData("payload.dll")]
    [InlineData("nested.tar.gz")]
    [InlineData("README")]
    [InlineData(".gitkeep")]
    public async Task ImportDatasetAsync_RejectsNonDatasetMemberSuffixes(string memberName)
    {
        // WHAT: Imports archives carrying key material, scripts, native libraries, nested archives and extension-less
        //       files.
        // WHY:  The import directory is a shared volume read by the database layer and the simulation engines, so the
        //       allow-list must admit dataset artifacts only.
        // HOW:  Asserts each is refused and nothing lands in the volume.
        byte[] archive = BuildArchive(new ArchiveMemberSpec(TarEntryType.RegularFile, memberName, "PAYLOAD", null));

        await AssertImportRefusedAsync(archive, CreateService());

        Assert.False(File.Exists(Path.Combine(_dataDir, memberName)));
    }

    /// <summary>
    /// Verifies that an archive with more members than the configured ceiling is refused.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsArchiveExceedingTheMemberCountCeiling()
    {
        // WHAT: Imports a three-member archive against a service configured to allow two.
        // WHY:  Bounds the inode and CPU cost of a bundle made of a huge number of tiny members.
        // HOW:  Overrides DATASET_IMPORT_MAX_ENTRIES and asserts the refusal names the member-count limit.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "a.xyz", "1", null),
            new ArchiveMemberSpec(TarEntryType.RegularFile, "b.xyz", "2", null),
            new ArchiveMemberSpec(TarEntryType.RegularFile, "c.xyz", "3", null));

        DatasetTransferService service = CreateService(new Dictionary<string, string?>
        {
            ["DATASET_IMPORT_MAX_ENTRIES"] = "2"
        });

        string message = await AssertImportRefusedAsync(archive, service);

        Assert.Contains("more members than", message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dataDir, "a.xyz")));
    }

    /// <summary>
    /// Verifies that an archive expanding beyond the configured byte ceiling is refused before it is written.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RejectsArchiveExceedingTheExpandedByteCeiling()
    {
        // WHAT: Imports an archive whose members expand to more bytes than the configured ceiling permits.
        // WHY:  Coordinate text compresses extremely well, so a small upload can expand enormously; the ceiling is
        //       checked against each member's declared size before any of its bytes are written to disk.
        // HOW:  Overrides DATASET_IMPORT_MAX_BYTES to 16 and submits two members of 16 bytes each.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "a.xyz", new string('A', 16), null),
            new ArchiveMemberSpec(TarEntryType.RegularFile, "b.xyz", new string('B', 16), null));

        DatasetTransferService service = CreateService(new Dictionary<string, string?>
        {
            ["DATASET_IMPORT_MAX_BYTES"] = "16"
        });

        string message = await AssertImportRefusedAsync(archive, service);

        Assert.Contains("more bytes than", message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dataDir, "a.xyz")));
        Assert.False(File.Exists(Path.Combine(_dataDir, "b.xyz")));
    }

    /// <summary>
    /// Verifies that one hostile member aborts the entire import, so no earlier well-formed member is left behind.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_RefusesTheWholeBundleWhenAnyMemberIsHostile()
    {
        // WHAT: Imports an archive whose first member is a perfectly legitimate trajectory and whose second is a
        //       traversal payload.
        // WHY:  Skipping the hostile member would leave a partially imported dataset, and the operator would have no
        //       signal that anything was wrong. The contract is reject, not skip.
        // HOW:  Asserts the import throws and that the innocent first member never reached the data volume.
        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "good.xyz", "1\nframe\nC 1.0 2.0 3.0\n", null),
            new ArchiveMemberSpec(TarEntryType.RegularFile, "../../weaver_config.db", "ATTACKER", null));

        await AssertImportRefusedAsync(archive, CreateService());

        AssertNothingWasOverwritten();
        Assert.False(File.Exists(Path.Combine(_dataDir, "good.xyz")));
    }

    /// <summary>
    /// Verifies that a well-formed bundle still imports correctly and leaves no staging directory behind.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_ImportsWellFormedBundleAndCleansUpStaging()
    {
        // WHAT: Imports a two-member bundle of legitimate dataset artifacts, one of which replaces an existing file.
        // WHY:  The hardening must not break the feature it protects: legitimate trajectories and their side-cars
        //       still have to arrive, and re-importing a corrected dataset must still replace the old copy.
        // HOW:  Asserts both files are present with the expected content and that the private staging directory has
        //       been removed.
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "run.xyz"), "STALE");

        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "run.xyz", "1\nframe\nC 1.0 2.0 3.0\n", null),
            new ArchiveMemberSpec(TarEntryType.RegularFile, "run.manifest.json", "{\"ok\":true}", null));

        using MemoryStream archiveStream = new(archive);
        bool imported = await CreateService().ImportDatasetAsync(archiveStream, BuildMatchingManifest(archive));

        Assert.True(imported);
        Assert.Equal("1\nframe\nC 1.0 2.0 3.0\n", await File.ReadAllTextAsync(Path.Combine(_dataDir, "run.xyz")));
        Assert.Equal("{\"ok\":true}", await File.ReadAllTextAsync(Path.Combine(_dataDir, "run.manifest.json")));
        AssertNothingWasOverwritten();
        Assert.Empty(Directory.GetDirectories(_dataDir));
    }

    /// <summary>
    /// Verifies that a checksum mismatch is still reported as a plain false rather than an exception.
    /// </summary>
    [Fact]
    public async Task ImportDatasetAsync_StillReturnsFalseOnChecksumMismatch()
    {
        // WHAT: Imports a well-formed archive paired with a deliberately wrong SHA-256 digest.
        // WHY:  The hardening must not change the existing contract for a corrupted upload, which the endpoint turns
        //       into a 400 of its own.
        // HOW:  Asserts false is returned and the archive was never expanded.
        byte[] archive = BuildArchive(new ArchiveMemberSpec(TarEntryType.RegularFile, "run.xyz", "data", null));

        DatasetManifest forged = BuildMatchingManifest(archive) with
        {
            Sha256Checksum = new string('0', 64)
        };

        using MemoryStream archiveStream = new(archive);
        bool imported = await CreateService().ImportDatasetAsync(archiveStream, forged);

        Assert.False(imported);
        Assert.False(File.Exists(Path.Combine(_dataDir, "run.xyz")));
    }

    /// <summary>
    /// Pins the member-name validator against every shape of unsafe name.
    /// </summary>
    /// <param name="entryName">The candidate TAR member name.</param>
    /// <param name="expected">Whether the name is expected to be accepted.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("../weaver_config.db", false)]
    [InlineData("../../weaver.db", false)]
    [InlineData("nested/run.xyz", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("sub/../run.xyz", false)]
    [InlineData("..\\..\\weaver.db", false)]
    [InlineData("C:\\Windows\\System32\\run.xyz", false)]
    [InlineData("run.xyz:stream", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("run.xyz", true)]
    [InlineData("dna_nanotube_sample.xyz", true)]
    [InlineData(".hidden.xyz", true)]
    public void IsSafeArchiveEntryName_AcceptsOnlyPlainFileNames(string? entryName, bool expected)
    {
        // WHAT: Exercises the name validator directly across separators, drive qualifiers, stream syntax and the
        //       bare relative segments "." and "..".
        // WHY:  This single invariant is what removes path traversal as a category: a bare file name combined with
        //       the import directory can only ever resolve inside it.
        // HOW:  Asserts acceptance for ordinary dataset file names and rejection for everything else.
        Assert.Equal(expected, DatasetMemberPolicy.IsSafeArchiveEntryName(entryName));
    }

    /// <summary>
    /// Pins the member-suffix allow-list.
    /// </summary>
    /// <param name="entryName">The candidate TAR member name.</param>
    /// <param name="expected">Whether the suffix is expected to be accepted.</param>
    [Theory]
    [InlineData("run.xyz", true)]
    [InlineData("run.DCD", true)]
    [InlineData("run.dump", true)]
    [InlineData("run.lammpstrj", true)]
    [InlineData("origami.top", true)]
    [InlineData("origami.dat", true)]
    [InlineData("origami.conf", true)]
    [InlineData("job.in", true)]
    [InlineData("run.manifest.json", true)]
    [InlineData("notes.txt", true)]
    [InlineData("notes.md", true)]
    [InlineData("engine.log", true)]
    [InlineData("summary.csv", true)]
    [InlineData("frames.parquet", true)]
    [InlineData("weaver_config.db", false)]
    [InlineData("key.pem", false)]
    [InlineData("script.sh", false)]
    [InlineData("README", false)]
    public void IsAllowedMemberName_AdmitsOnlyDatasetArtifacts(string entryName, bool expected)
    {
        // WHAT: Exercises the suffix allow-list directly, including a mixed-case suffix.
        // WHY:  This is the check that makes delivering the identity database or key material impossible even under a
        //       perfectly well-formed plain file name.
        // HOW:  Asserts each documented dataset suffix is admitted and each dangerous one is refused.
        Assert.Equal(expected, DatasetMemberPolicy.IsAllowedMemberName(entryName));
    }

    /// <summary>
    /// Pins the member-type allow-list.
    /// </summary>
    /// <param name="entryType">The candidate TAR member type.</param>
    /// <param name="expected">Whether the member type is expected to be accepted.</param>
    [Theory]
    [InlineData(TarEntryType.RegularFile, true)]
    [InlineData(TarEntryType.V7RegularFile, true)]
    [InlineData(TarEntryType.SymbolicLink, false)]
    [InlineData(TarEntryType.HardLink, false)]
    [InlineData(TarEntryType.Directory, false)]
    [InlineData(TarEntryType.CharacterDevice, false)]
    [InlineData(TarEntryType.BlockDevice, false)]
    [InlineData(TarEntryType.Fifo, false)]
    [InlineData(TarEntryType.GlobalExtendedAttributes, false)]
    public void IsAllowedArchiveEntryType_AdmitsOnlyRegularFiles(TarEntryType entryType, bool expected)
    {
        // WHAT: Exercises the member-type allow-list directly.
        // WHY:  Only plain data files belong in a dataset; links in particular are writable handles to arbitrary
        //       locations on the host.
        // HOW:  Asserts both regular file encodings are admitted and every other member type is refused.
        Assert.Equal(expected, DatasetMemberPolicy.IsAllowedArchiveEntryType(entryType));
    }

    /// <summary>
    /// Reads back the member names of an exported bundle.
    /// </summary>
    /// <param name="archivePath">Absolute path of the exported <c>.tar.gz</c> bundle.</param>
    /// <returns>Every member name the bundle contains, in archive order.</returns>
    private static List<string> ReadArchiveMemberNames(string archivePath)
    {
        List<string> names = new();

        using FileStream fileStream = File.OpenRead(archivePath);
        using GZipStream gzip = new(fileStream, CompressionMode.Decompress);
        using TarReader tarReader = new(gzip);

        TarEntry? entry;
        while ((entry = tarReader.GetNextEntry()) != null)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    /// <summary>
    /// Verifies that an export bundle never carries the node's identity database, nor any other file the shared
    /// member policy excludes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExportDatasetAsync_ExcludesEveryMemberTheSharedPolicyDisallows()
    {
        // WHAT: Seeds the data volume with one file per allow-listed suffix plus a spread of excluded ones, exports
        //       it, and compares the bundle's member list against the shared policy.
        // WHY:  The export path used to pack the volume wholesale, so every bundle shipped weaver_config.db - the
        //       whole account, role, public key and patient record store - to whoever received it. The assertion is
        //       driven off DatasetMemberPolicy.AllowedMemberExtensions rather than a copy of it, so adding a suffix to
        //       the policy automatically extends this test instead of silently escaping it.
        // HOW:  Asserts the bundle contains exactly the allow-listed files, that every member satisfies the policy,
        //       and that the manifest's own file list agrees.
        List<string> expectedMembers = new();
        foreach (string extension in DatasetMemberPolicy.AllowedMemberExtensions)
        {
            string allowedName = "sample" + extension;
            await File.WriteAllTextAsync(Path.Combine(_dataDir, allowedName), "legitimate dataset artifact");
            expectedMembers.Add(allowedName);
        }

        string[] excludedNames =
        {
            "weaver_config.db", "weaver_users.db", "node_signing_master.pem", "secrets.key",
            "payload.sh", "payload.dll", "nested.tar.gz", "README", ".gitkeep"
        };
        foreach (string excludedName in excludedNames)
        {
            await File.WriteAllTextAsync(Path.Combine(_dataDir, excludedName), "MUST-NEVER-BE-EXPORTED");
        }

        DatasetManifest manifest = await CreateService().ExportDatasetAsync("policy-export");
        List<string> members = ReadArchiveMemberNames(Path.Combine(_exportDir, manifest.ArchiveFileName));

        expectedMembers.Sort(StringComparer.Ordinal);
        members.Sort(StringComparer.Ordinal);
        Assert.Equal(expectedMembers, members);

        foreach (string member in members)
        {
            Assert.True(DatasetMemberPolicy.IsAllowedMemberName(member), $"Bundle member '{member}' violates the shared dataset member policy.");
        }

        foreach (string excludedName in excludedNames)
        {
            Assert.DoesNotContain(excludedName, members);
            Assert.DoesNotContain(excludedName, manifest.IncludedFiles);
        }
    }

    /// <summary>
    /// Verifies that naming an excluded file explicitly in the export request does not smuggle it into the bundle.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExportDatasetAsync_IgnoresAnExplicitRequestForAnExcludedFile()
    {
        // WHAT: Asks for an export of exactly two files, one legitimate and one being the identity database.
        // WHY:  The caller-supplied file list narrows an export; it must never be able to widen it past the policy,
        //       or the exfiltration route would simply move from "every export" to "any export that asks".
        // HOW:  Asserts the bundle holds only the legitimate file and the export still succeeds.
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "run.xyz"), "1\nframe\nC 1.0 2.0 3.0\n");

        DatasetManifest manifest = await CreateService().ExportDatasetAsync(
            "targeted-export",
            new[] { "run.xyz", "weaver_config.db" });

        List<string> members = ReadArchiveMemberNames(Path.Combine(_exportDir, manifest.ArchiveFileName));

        Assert.Equal(new[] { "run.xyz" }, members);
        Assert.Equal(new[] { "run.xyz" }, manifest.IncludedFiles);
    }

    /// <summary>
    /// Verifies that a bundle this node exports is a bundle this node imports, even when the source data volume
    /// contains the identity database.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExportThenImport_RoundTripsCleanly_FromAVolumeContainingTheIdentityDatabase()
    {
        // WHAT: Exports a data volume that holds the identity database alongside real trajectories, then imports the
        //       resulting bundle into a second, empty volume.
        // WHY:  Hardening import alone would have broken the headline feature: a bundle produced from any real volume
        //       carried weaver_config.db and would have been refused on arrival. Export and import now consult one
        //       shared policy, so agreement between them is structural rather than coincidental.
        // HOW:  Asserts the bundle excludes the database, the import returns true, the trajectories land intact, and
        //       no database file materialises in the receiving volume.
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "nanocarrier.xyz"), "1\nframe\nC 1.0 2.0 3.0\n");
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "origami.top"), "100 50\n");
        await File.WriteAllTextAsync(Path.Combine(_dataDir, "run.manifest.json"), "{\"run\":1}");

        DatasetManifest manifest = await CreateService().ExportDatasetAsync("round-trip");
        string archivePath = Path.Combine(_exportDir, manifest.ArchiveFileName);

        List<string> members = ReadArchiveMemberNames(archivePath);
        Assert.DoesNotContain("weaver_config.db", members);
        Assert.Equal(3, members.Count);

        // The receiving node is a separate, empty data volume, as it would be in a real peer-to-peer exchange.
        string receivingDir = Path.Combine(_testRoot, "receiving-shared-data");
        Directory.CreateDirectory(receivingDir);

        IConfiguration receivingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = receivingDir,
                ["TRAJECTORY_EXPORT_DIR"] = Path.Combine(_testRoot, "receiving-exports")
            }).Build();
        DatasetTransferService receivingService = new(receivingConfiguration, NullLogger<DatasetTransferService>.Instance);

        using FileStream bundleStream = File.OpenRead(archivePath);
        bool imported = await receivingService.ImportDatasetAsync(bundleStream, manifest);

        Assert.True(imported);
        Assert.Equal("1\nframe\nC 1.0 2.0 3.0\n", await File.ReadAllTextAsync(Path.Combine(receivingDir, "nanocarrier.xyz")));
        Assert.Equal("100 50\n", await File.ReadAllTextAsync(Path.Combine(receivingDir, "origami.top")));
        Assert.Equal("{\"run\":1}", await File.ReadAllTextAsync(Path.Combine(receivingDir, "run.manifest.json")));
        Assert.False(File.Exists(Path.Combine(receivingDir, "weaver_config.db")));
        Assert.Empty(Directory.GetDirectories(receivingDir));
    }

    /// <summary>
    /// Verifies that the rejection surfaces over HTTP as an RFC 7807 problem document that leaks no server paths.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ImportEndpoint_ReturnsRfc7807ProblemDetails_WithoutLeakingFilesystemPaths()
    {
        // WHAT: Posts a traversal bundle to /api/datasets/import through the real HTTP pipeline.
        // WHY:  Prime Directive 14 requires API failures to be RFC 7807 SecureProblemDetails carrying no stack
        //       traces, no local filesystem paths, no connection strings and no key material.
        // HOW:  Asserts the status, the application/problem+json content type, the problem document fields, and that
        //       the detail text contains neither the data volume path nor any directory separator.
        // The application host places its own SQLite database inside TRAJECTORY_INCOMING_DIR, so this test binds a
        // dedicated empty volume rather than the sentinel-seeded one the in-process tests use.
        string hostDataDir = Path.Combine(_testRoot, "host-shared-data");
        Directory.CreateDirectory(hostDataDir);

        using WebApplicationFactory<Program> baseFactory = new();
        using WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", hostDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", _exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_testRoot, "parquet"));
        });

        using HttpClient client = factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(client, factory);

        byte[] archive = BuildArchive(
            new ArchiveMemberSpec(TarEntryType.RegularFile, "../../weaver_config.db", "ATTACKER", null));
        DatasetManifest manifest = BuildMatchingManifest(archive);

        using MultipartFormDataContent form = new();
        ByteArrayContent archiveContent = new(archive);
        archiveContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(archiveContent, "archive", "hostile-dataset.tar.gz");

        ByteArrayContent manifestContent = new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
        manifestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(manifestContent, "manifest", "manifest.json");

        HttpResponseMessage response = await client.PostAsync("/api/datasets/import", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument problem = JsonDocument.Parse(body);
        JsonElement root = problem.RootElement;

        Assert.Equal("urn:weaver:error:bad-request", root.GetProperty("type").GetString());
        Assert.Equal("Bad Request", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("/api/datasets/import", root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

        string detail = root.GetProperty("detail").GetString() ?? string.Empty;
        Assert.StartsWith("Dataset import rejected", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(hostDataDir, detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), detail, StringComparison.Ordinal);
        Assert.DoesNotContain("/", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        AssertOutsideSentinelsUntouched();
        Assert.False(File.Exists(Path.Combine(_testRoot, "weaver_config.db")));
    }
}
