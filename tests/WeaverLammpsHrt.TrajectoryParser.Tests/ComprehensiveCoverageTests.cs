using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WeaverLammpsHrt.TrajectoryParser.Abstractions;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Pipelines;
using WeaverLammpsHrt.TrajectoryParser.Pipelines.Simd;
using WeaverLammpsHrt.TrajectoryParser.Services;
using WeaverLammpsHrt.TrajectoryParser.Storage;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Comprehensive unit and boundary test suite maximizing code coverage across all core classes,
/// parsers, services, edge cases, and error branches.
/// </summary>
public sealed class ComprehensiveCoverageTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Reusable web client for hitting minimal API endpoints.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Path to active simulation shared directory.
    /// </summary>
    private readonly string _sharedDataDir;

    /// <summary>
    /// Path to export directory.
    /// </summary>
    private readonly string _exportDir;

    /// <summary>
    /// Initializes a new instance of <see cref="ComprehensiveCoverageTests"/>.
    /// </summary>
    /// <param name="factory">Test web application factory.</param>
    public ComprehensiveCoverageTests(WebApplicationFactory<Program> factory)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string solutionRoot = current ?? Directory.GetCurrentDirectory();
        _sharedDataDir = Path.Combine(solutionRoot, "simulations", "shared-data");
        _exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        string parquetDir = Path.Combine(solutionRoot, "simulations", "parquet");

        TestFixturesHelper.EnsureTestFixtures(_sharedDataDir);

        WebApplicationFactory<Program> customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _sharedDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", _exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", parquetDir);
        });

        _client = customFactory.CreateClient();
    }

    /// <summary>
    /// Tests edge cases and fallback branches of <see cref="AptamerAffinityAnalyzer"/>.
    /// </summary>
    [Fact]
    public void AptamerAffinityAnalyzer_BoundaryAndNullChecks()
    {
        // WHAT: Tests empty and out-of-range index handling.
        // WHY:  Guarantees 100% branch coverage and prevents IndexOutOfRangeException.
        // HOW:  Passes empty spans and out-of-range indices.
        Assert.Equal(0, AptamerAffinityAnalyzer.CountBindingContacts(ReadOnlySpan<AtomCoordinate>.Empty, [0], 0));
        Assert.Equal(0, AptamerAffinityAnalyzer.CountBindingContacts([new AtomCoordinate(1f, 1f, 1f)], ReadOnlySpan<int>.Empty, 0));
        Assert.Equal(0, AptamerAffinityAnalyzer.CountBindingContacts([new AtomCoordinate(1f, 1f, 1f)], [0], 99));

        Assert.Equal(default, AptamerAffinityAnalyzer.CalculateCentroid(ReadOnlySpan<AtomCoordinate>.Empty, [0]));
        Assert.Equal(default, AptamerAffinityAnalyzer.CalculateCentroid([new AtomCoordinate(1f, 1f, 1f)], ReadOnlySpan<int>.Empty));
        Assert.Equal(default, AptamerAffinityAnalyzer.CalculateCentroid([new AtomCoordinate(1f, 1f, 1f)], [99]));
    }

    /// <summary>
    /// Tests boundary and edge case parsing for <see cref="SimdCoordinateParser"/>.
    /// </summary>
    [Fact]
    public void SimdCoordinateParser_EdgeCasesAndFallbacks()
    {
        // 1. Empty span
        Assert.False(SimdCoordinateParser.TryParseFloatSimd(ReadOnlySpan<byte>.Empty, out _, out _));

        // 2. Sign only
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("-"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("+"u8, out _, out _));

        // 3. Dot only without digits
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("."u8, out _, out _));

        // 4. Large decimals exceeding powers of ten table (> 10 decimals)
        byte[] largeDecimals = "0.1234567890123"u8.ToArray();
        Assert.True(SimdCoordinateParser.TryParseFloatSimd(largeDecimals, out float largeVal, out _));
        Assert.True(largeVal > 0f);

        // 5. Scientific notation with 'e'
        byte[] sci1 = "1.5e-3"u8.ToArray();
        Assert.True(SimdCoordinateParser.TryParseFloatSimd(sci1, out float sciVal1, out _));
        Assert.Equal(0.0015f, sciVal1, precision: 4);

        // 6. Scientific notation with 'E'
        byte[] sci2 = "2.5E2"u8.ToArray();
        Assert.True(SimdCoordinateParser.TryParseFloatSimd(sci2, out float sciVal2, out _));
        Assert.Equal(250f, sciVal2, precision: 1);

        // 7. TryParseCoordinateLineSimd invalid cases
        Assert.False(SimdCoordinateParser.TryParseCoordinateLineSimd(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(SimdCoordinateParser.TryParseCoordinateLineSimd("NoSeparatorHere"u8, out _));
        Assert.False(SimdCoordinateParser.TryParseCoordinateLineSimd("C InvalidX 1.0 2.0"u8, out _));
        Assert.False(SimdCoordinateParser.TryParseCoordinateLineSimd("C 1.0 InvalidY 2.0"u8, out _));
        Assert.False(SimdCoordinateParser.TryParseCoordinateLineSimd("C 1.0 2.0 InvalidZ"u8, out _));

        // 8. TrimStartWhitespaceSimd on span with all whitespace
        ReadOnlySpan<byte> allWs = "   \t\t   "u8;
        Assert.True(SimdCoordinateParser.TrimStartWhitespaceSimd(allWs).IsEmpty);

        // 9. ComputeDistancesFromCentroidSimd on empty
        SimdCoordinateParser.ComputeDistancesFromCentroidSimd(
            ReadOnlySpan<AtomCoordinate>.Empty,
            new AtomCoordinate(0f, 0f, 0f),
            Span<float>.Empty);
    }

    /// <summary>
    /// Tests edge case and error handling in <see cref="EndocrineStateEngine"/>.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_CustomAndDefaultRegimens()
    {
        EndocrineStateEngine engine = new();

        // Custom regimen transition
        ToggleSwitchRequest customRequest = new()
        {
            TargetState = ToggleState.EstrogenicDominant,
            Regimen = TherapeuticRegimen.Custom,
            TargetTissue = TargetTissueType.PeripheralReceptor,
            EstradiolPmolL = 500.0,
            TestosteroneNmolL = 1.0
        };

        EndocrineTargetProfile profile = engine.UpdateToggleState(customRequest);
        Assert.Equal(TherapeuticRegimen.Custom, profile.Regimen);
        Assert.Equal(500.0, profile.TargetEstradiolPmolL);
        Assert.Equal(1.0, profile.TargetTestosteroneNmolL);
        Assert.Equal(TargetTissueType.PeripheralReceptor, profile.TargetTissue);

        // Transition with null overrides keeps preset values
        ToggleSwitchRequest ftmRequest = new()
        {
            TargetState = ToggleState.AndrogenicDominant,
            Regimen = TherapeuticRegimen.FemaleToMale
        };
        EndocrineTargetProfile ftmProfile = engine.UpdateToggleState(ftmRequest);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, ftmProfile.Regimen);
    }

    /// <summary>
    /// Tests edge cases, comment lines, and buffer expansion in <see cref="XyzPipeReader"/>.
    /// </summary>
    [Fact]
    public async Task XyzPipeReader_EdgeCasesAndResizing()
    {
        // 1. Trajectory where frame 2 has more atoms than frame 1 (triggers ArrayPool buffer expansion)
        string expandingXyz =
            "1\n" +
            "Frame 1\n" +
            "C 1.0 2.0 3.0\n" +
            "3\n" +
            "Frame 2 expanded\n" +
            "C 1.0 2.0 3.0\n" +
            "N 4.0 5.0 6.0\n" +
            "P 7.0 8.0 9.0\n";

        XyzPipeReader parser = new();
        Assert.Equal("XYZ", parser.FormatName);

        using MemoryStream stream = new(Encoding.UTF8.GetBytes(expandingXyz));
        PipeReader reader = PipeReader.Create(stream);

        List<int> frameCounts = [];
        await parser.ParseAsync(reader, (idx, coords) =>
        {
            frameCounts.Add(coords.Length);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, frameCounts.Count);
        Assert.Equal(1, frameCounts[0]);
        Assert.Equal(3, frameCounts[1]);

        // 2. Trajectory with carriage returns (\r\n) and blank lines
        string crlfXyz =
            "1\r\n" +
            "\r\n" +
            "Comment\r\n" +
            "C 10.0 20.0 30.0\r\n";

        using MemoryStream streamCrlf = new(Encoding.UTF8.GetBytes(crlfXyz));
        PipeReader readerCrlf = PipeReader.Create(streamCrlf);

        int countCrlf = 0;
        await parser.ParseAsync(readerCrlf, (idx, coords) =>
        {
            countCrlf++;
            return ValueTask.CompletedTask;
        });
        Assert.Equal(1, countCrlf);
    }

    /// <summary>
    /// Tests error branches, invalid formats, and box bound skips in <see cref="LammpsCustomPipeReader"/>.
    /// </summary>
    [Fact]
    public async Task LammpsCustomPipeReader_EdgeCases()
    {
        LammpsCustomPipeReader parser = new();
        Assert.Equal("LAMMPS_CUSTOM", parser.FormatName);

        // Dump with invalid atom line, unknown items, and extra carriage returns
        string dump =
            "ITEM: TIMESTEP\r\n" +
            "100\r\n" +
            "ITEM: UNKNOWN_SECTION\r\n" +
            "ignored\r\n" +
            "ITEM: NUMBER OF ATOMS\r\n" +
            "2\r\n" +
            "ITEM: BOX BOUNDS pp pp pp\r\n" +
            "0.0 10.0\r\n" +
            "0.0 10.0\r\n" +
            "0.0 10.0\r\n" +
            "ITEM: ATOMS id type x y z\r\n" +
            "invalid_atom_line\r\n" +
            "1 1 10.0 20.0 30.0\r\n" +
            "2 1 15.0 25.0 35.0\r\n";

        using MemoryStream ms = new(Encoding.UTF8.GetBytes(dump));
        PipeReader reader = PipeReader.Create(ms);

        int parsed = 0;
        await parser.ParseAsync(reader, (idx, coords) =>
        {
            parsed++;
            Assert.Equal(2, coords.Length);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1, parsed);
    }

    /// <summary>
    /// Tests header corruption and format name in <see cref="DcdPipeReader"/>.
    /// </summary>
    [Fact]
    public async Task DcdPipeReader_CorruptedHeaderYieldsZeroFrames()
    {
        DcdPipeReader parser = new();
        Assert.Equal("DCD", parser.FormatName);

        // Byte array of 120 bytes with invalid signature (not CORD)
        byte[] badHeader = new byte[120];
        using MemoryStream ms = new(badHeader);
        PipeReader reader = PipeReader.Create(ms);

        int frameCount = 0;
        await parser.ParseAsync(reader, (idx, coords) =>
        {
            frameCount++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal(0, frameCount);
    }

    /// <summary>
    /// Tests argument validation and edge cases in <see cref="DuckDbParquetWriter"/>.
    /// </summary>
    [Fact]
    public async Task DuckDbParquetWriter_ArgumentValidation()
    {
        DuckDbParquetWriter writer = new();
        XyzPipeReader parser = new();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await writer.ConvertTrajectoryToParquetAsync("", "dest.parquet", parser);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await writer.ConvertTrajectoryToParquetAsync("source.xyz", "", parser);
        });

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await writer.ConvertTrajectoryToParquetAsync("nonexistent_source.xyz", "dest.parquet", parser);
        });

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await writer.QueryRecordCountAsync("nonexistent.parquet");
        });
    }

    /// <summary>
    /// Tests file format detection and missing directory in <see cref="DatasetTransferService"/>.
    /// </summary>
    [Fact]
    public async Task DatasetTransferService_VariousFormatsAndMissingDir()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_ds_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(tempDir, "data");
        string exportDir = Path.Combine(tempDir, "exports");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(exportDir);

        try
        {
            // Create sample files of various formats
            await File.WriteAllTextAsync(Path.Combine(dataDir, "model.top"), "top");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "model.dat"), "dat");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "model.conf"), "conf");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "script.in"), "in");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "sample.dcd"), "dcd");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "sample.xyz"), new string('X', 2048));
            await File.WriteAllTextAsync(Path.Combine(dataDir, "unknown.bin"), "bin");

            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = dataDir,
                    ["TRAJECTORY_EXPORT_DIR"] = exportDir
                })
                .Build();

            // Null logger validation
            Assert.Throws<ArgumentNullException>(() => new DatasetTransferService(config, null!));

            // Default directory resolution when configuration keys are omitted
            IConfiguration emptyConfig = new ConfigurationBuilder().Build();
            DatasetTransferService defaultService = new(emptyConfig, NullLogger<DatasetTransferService>.Instance);
            Assert.NotNull(defaultService);

            DatasetTransferService service = new(config, NullLogger<DatasetTransferService>.Instance);

            IReadOnlyList<TrajectoryFileItem> items = await service.ListTrajectoriesAsync();
            Assert.Equal(7, items.Count);

            // Export with filter excluding some files
            DatasetManifest manifest = await service.ExportDatasetAsync("FilteredDataset", ["model.top", "script.in", "sample.dcd"]);
            Assert.Equal(3, manifest.IncludedFiles.Length);

            // Missing directory behavior
            Directory.Delete(dataDir, recursive: true);
            IReadOnlyList<TrajectoryFileItem> emptyItems = await service.ListTrajectoriesAsync();
            Assert.Empty(emptyItems);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests byte formatting across all order magnitudes and boundary conditions in <see cref="DatasetTransferService.FormatBytes"/>.
    /// </summary>
    [Fact]
    public void DatasetTransferService_FormatBytes_AllMagnitudes()
    {
        // WHAT: Tests zero, small, kilobyte, megabyte, gigabyte, terabyte, and maximum integer scales.
        // WHY:  Guarantees 100% line and branch coverage across both conditions in the scaling while loop.
        // HOW:  Asserts formatted string suffixes and numeric values for each order boundary.
        Assert.Equal("0 B", DatasetTransferService.FormatBytes(0));
        Assert.Equal("500 B", DatasetTransferService.FormatBytes(500));
        Assert.Equal("1 KB", DatasetTransferService.FormatBytes(1024));
        Assert.Equal("1.5 KB", DatasetTransferService.FormatBytes(1536));
        Assert.Equal("1 MB", DatasetTransferService.FormatBytes(1024 * 1024));
        Assert.Equal("1 GB", DatasetTransferService.FormatBytes(1024L * 1024 * 1024));
        Assert.Equal("1 TB", DatasetTransferService.FormatBytes(1024L * 1024 * 1024 * 1024));
        Assert.Equal("8388608 TB", DatasetTransferService.FormatBytes(long.MaxValue));
    }

    /// <summary>
    /// Tests POST /api/datasets/import with a valid cryptographic archive and manifest.
    /// </summary>
    [Fact]
    public async Task ImportDataset_ValidArchiveShouldSucceed()
    {
        // Step 1: Export a dataset first
        ExportRequest exportReq = new() { DatasetName = "ImportTargetDataset", Files = ["dna_nanotube_sample.xyz"] };
        HttpResponseMessage exportResp = await _client.PostAsJsonAsync("/api/datasets/export", exportReq);
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        DatasetManifest? manifest = await exportResp.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);

        string archivePath = Path.Combine(_exportDir, manifest.ArchiveFileName);
        Assert.True(File.Exists(archivePath));

        // Step 2: Upload archive and manifest to import endpoint
        byte[] archiveBytes = await File.ReadAllBytesAsync(archivePath);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);

        using MultipartFormDataContent form = new();
        ByteArrayContent archivePart = new(archiveBytes);
        archivePart.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(archivePart, "archive", manifest.ArchiveFileName);

        ByteArrayContent manifestPart = new(manifestBytes);
        manifestPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(manifestPart, "manifest", $"{manifest.DatasetName}.manifest.json");

        HttpResponseMessage importResp = await _client.PostAsync("/api/datasets/import", form);
        Assert.Equal(HttpStatusCode.OK, importResp.StatusCode);
    }

    /// <summary>
    /// Tests GET /api/datasets/download/{fileName} downloading an existing export bundle.
    /// </summary>
    [Fact]
    public async Task DownloadExport_ExistingFileShouldReturnStream()
    {
        // Export bundle first
        ExportRequest exportReq = new() { DatasetName = "DownloadTestDataset", Files = ["dna_nanotube_sample.xyz"] };
        HttpResponseMessage exportResp = await _client.PostAsJsonAsync("/api/datasets/export", exportReq);
        DatasetManifest? manifest = await exportResp.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);

        HttpResponseMessage dlResp = await _client.GetAsync($"/api/datasets/download/{manifest.ArchiveFileName}");
        Assert.Equal(HttpStatusCode.OK, dlResp.StatusCode);
        byte[] dlBytes = await dlResp.Content.ReadAsByteArrayAsync();
        Assert.True(dlBytes.Length > 0);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TrimStartWhitespaceSimd"/> properly skips past
    /// blocks of whitespace that are 16 bytes or longer using 128-bit SIMD intrinsics.
    /// </summary>
    [Fact]
    public void SimdCoordinateParser_TrimStartWhitespaceSimd_ExceedingVector128()
    {
        // WHAT: Provides an ASCII byte sequence with 24 consecutive whitespace characters preceding digits.
        // WHY:  Forces execution of index advancement by Vector128<byte>.Count in SimdCoordinateParser.
        // HOW:  Asserts that the returned span begins exactly at the numeric character sequence.
        byte[] input = Encoding.UTF8.GetBytes("                        42.500");
        ReadOnlySpan<byte> trimmed = SimdCoordinateParser.TrimStartWhitespaceSimd(input);
        Assert.Equal("42.500", Encoding.UTF8.GetString(trimmed));
    }

    /// <summary>
    /// Verifies edge cases and error handling branches in <see cref="DcdPipeReader"/>,
    /// including corrupted magic signatures, mismatched Fortran record markers, and unit cell blocks.
    /// </summary>
    [Fact]
    public async Task DcdPipeReader_CorruptedHeaderAndUnitCellBranches()
    {
        // WHAT: Tests header validation failures when the DCD magic signature or trailing markers are invalid.
        // WHY:  Guarantees robust error boundaries without unhandled exceptions or state-machine leaks.
        // HOW:  Feeds specially crafted byte sequences into TryParseHeader and ParseAsync.

        // 1. Empty buffer (TryReadLittleEndian returns false on line 172)
        ReadOnlySequence<byte> emptySeq = new(Array.Empty<byte>());
        SequenceReader<byte> emptyReader = new(emptySeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref emptyReader, out _, out _));

        // 2. Record length < 84 (recordLength < 84 evaluates true on line 172)
        byte[] shortRecord = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(shortRecord, 50);
        ReadOnlySequence<byte> shortRecordSeq = new(shortRecord);
        SequenceReader<byte> shortRecordReader = new(shortRecordSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref shortRecordReader, out _, out _));

        // 3. Buffer truncated before magic (TryCopyTo magic returns false on line 178)
        byte[] truncMagic = new byte[6];
        BinaryPrimitives.WriteInt32LittleEndian(truncMagic.AsSpan(0, 4), 84);
        ReadOnlySequence<byte> truncMagicSeq = new(truncMagic);
        SequenceReader<byte> truncMagicReader = new(truncMagicSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref truncMagicReader, out _, out _));

        // 4. Corrupted signature: Fortran record length 84, but signature is "ABCD" instead of "CORD"
        byte[] badMagic = new byte[104];
        BinaryPrimitives.WriteInt32LittleEndian(badMagic.AsSpan(0, 4), 84);
        "ABCD"u8.CopyTo(badMagic.AsSpan(4, 4));
        ReadOnlySequence<byte> badSeq = new(badMagic);
        SequenceReader<byte> badReader = new(badSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref badReader, out _, out _));

        // 5. Buffer truncated before header ints (TryCopyTo headerInts returns false on line 188)
        byte[] truncHeaderInts = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(truncHeaderInts.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(truncHeaderInts.AsSpan(4, 4));
        ReadOnlySequence<byte> truncHeaderSeq = new(truncHeaderInts);
        SequenceReader<byte> truncHeaderReader = new(truncHeaderSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref truncHeaderReader, out _, out _));

        // 6. Missing trailing record marker (TryReadLittleEndian trailing returns false on line 196)
        byte[] noTrailing = new byte[84];
        BinaryPrimitives.WriteInt32LittleEndian(noTrailing.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(noTrailing.AsSpan(4, 4));
        ReadOnlySequence<byte> noTrailingSeq = new(noTrailing);
        SequenceReader<byte> noTrailingReader = new(noTrailingSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref noTrailingReader, out _, out _));

        // 7. Mismatched trailing record length: leading 84, signature "CORD", but trailing marker is 999
        byte[] badTrailing = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(badTrailing.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(badTrailing.AsSpan(4, 4));
        BinaryPrimitives.WriteInt32LittleEndian(badTrailing.AsSpan(84, 4), 999);
        ReadOnlySequence<byte> badTrailingSeq = new(badTrailing);
        SequenceReader<byte> badTrailingReader = new(badTrailingSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref badTrailingReader, out _, out _));

        // 8. Truncated before titleLength (line 202)
        byte[] noTitleLen = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(noTitleLen.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(noTitleLen.AsSpan(4, 4));
        BinaryPrimitives.WriteInt32LittleEndian(noTitleLen.AsSpan(84, 4), 84);
        ReadOnlySequence<byte> noTitleLenSeq = new(noTitleLen);
        SequenceReader<byte> noTitleLenReader = new(noTitleLenSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref noTitleLenReader, out _, out _));

        // 9. Truncated before trailing title marker (line 204)
        byte[] noTitleEnd = new byte[96];
        BinaryPrimitives.WriteInt32LittleEndian(noTitleEnd.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(noTitleEnd.AsSpan(4, 4));
        BinaryPrimitives.WriteInt32LittleEndian(noTitleEnd.AsSpan(84, 4), 84);
        BinaryPrimitives.WriteInt32LittleEndian(noTitleEnd.AsSpan(88, 4), 4);
        "TEST"u8.CopyTo(noTitleEnd.AsSpan(92, 4));
        ReadOnlySequence<byte> noTitleEndSeq = new(noTitleEnd);
        SequenceReader<byte> noTitleEndReader = new(noTitleEndSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref noTitleEndReader, out _, out _));

        // 10. Truncated before natomRecordLength (line 207 part 1)
        byte[] validToTitle = new byte[100];
        BinaryPrimitives.WriteInt32LittleEndian(validToTitle.AsSpan(0, 4), 84);
        "CORD"u8.CopyTo(validToTitle.AsSpan(4, 4));
        BinaryPrimitives.WriteInt32LittleEndian(validToTitle.AsSpan(84, 4), 84);
        BinaryPrimitives.WriteInt32LittleEndian(validToTitle.AsSpan(88, 4), 4);
        "TEST"u8.CopyTo(validToTitle.AsSpan(92, 4));
        BinaryPrimitives.WriteInt32LittleEndian(validToTitle.AsSpan(96, 4), 4);
        ReadOnlySequence<byte> validToTitleSeq = new(validToTitle);
        SequenceReader<byte> validToTitleReader = new(validToTitleSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref validToTitleReader, out _, out _));

        // 11. natomRecordLength != 4 (line 207 part 2)
        byte[] badNatomLen = new byte[104];
        validToTitle.CopyTo(badNatomLen, 0);
        BinaryPrimitives.WriteInt32LittleEndian(badNatomLen.AsSpan(100, 4), 8); // != 4
        ReadOnlySequence<byte> badNatomLenSeq = new(badNatomLen);
        SequenceReader<byte> badNatomLenReader = new(badNatomLenSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref badNatomLenReader, out _, out _));

        // 12. Truncated before atomCount (line 208 part 1)
        byte[] noAtomCount = new byte[104];
        validToTitle.CopyTo(noAtomCount, 0);
        BinaryPrimitives.WriteInt32LittleEndian(noAtomCount.AsSpan(100, 4), 4);
        ReadOnlySequence<byte> noAtomCountSeq = new(noAtomCount);
        SequenceReader<byte> noAtomCountReader = new(noAtomCountSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref noAtomCountReader, out _, out _));

        // 13. atomCount <= 0 (line 208 part 2)
        byte[] zeroAtomCount = new byte[108];
        noAtomCount.CopyTo(zeroAtomCount, 0);
        BinaryPrimitives.WriteInt32LittleEndian(zeroAtomCount.AsSpan(104, 4), 0); // <= 0
        ReadOnlySequence<byte> zeroAtomCountSeq = new(zeroAtomCount);
        SequenceReader<byte> zeroAtomCountReader = new(zeroAtomCountSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref zeroAtomCountReader, out _, out _));

        // 14. Truncated before trailing natom marker (line 209)
        byte[] noTrailingNatom = new byte[108];
        noAtomCount.CopyTo(noTrailingNatom, 0);
        BinaryPrimitives.WriteInt32LittleEndian(noTrailingNatom.AsSpan(104, 4), 2); // > 0
        ReadOnlySequence<byte> noTrailingNatomSeq = new(noTrailingNatom);
        SequenceReader<byte> noTrailingNatomReader = new(noTrailingNatomSeq);
        Assert.False(DcdPipeReader.TryParseHeader(ref noTrailingNatomReader, out _, out _));

        // 15. Short stream (< 104 bytes) that completes in ParseAsync
        byte[] shortStream = new byte[50];
        PipeReader shortPipeReader = PipeReader.Create(new MemoryStream(shortStream));
        DcdPipeReader dcdParser = new();
        int frameCount = 0;
        await dcdParser.ParseAsync(shortPipeReader, (_, _) =>
        {
            frameCount++;
            return ValueTask.CompletedTask;
        });
        Assert.Equal(0, frameCount);

        // 4. Valid DCD stream with unit cell block (hasUnitCell == true)
        byte[] unitCellBytes;
        using (MemoryStream unitCellMs = new())
        {
            using (BinaryWriter writer = new(unitCellMs, Encoding.UTF8, leaveOpen: true))
            {
                // Header: 84 bytes payload
                writer.Write(84);
                writer.Write("CORD"u8.ToArray());
                byte[] headerInts = new byte[76];
                // Set unit cell flag to 1 at byte offset 36 (int index 10)
                BinaryPrimitives.WriteInt32LittleEndian(headerInts.AsSpan(36, 4), 1);
                writer.Write(headerInts);
                writer.Write(84);

                // Title
                writer.Write(4);
                writer.Write("UNIT"u8.ToArray());
                writer.Write(4);

                // NATOM
                writer.Write(4);
                writer.Write(2); // 2 atoms
                writer.Write(4);

                // Frame: Unit Cell block (56 bytes total)
                writer.Write(new byte[56]);

                // Coordinate X block: 4 + 8 + 4 = 16 bytes
                writer.Write(8);
                writer.Write(1.0f);
                writer.Write(2.0f);
                writer.Write(8);

                // Coordinate Y block
                writer.Write(8);
                writer.Write(3.0f);
                writer.Write(4.0f);
                writer.Write(8);

                // Coordinate Z block
                writer.Write(8);
                writer.Write(5.0f);
                writer.Write(6.0f);
                writer.Write(8);
            }
            unitCellBytes = unitCellMs.ToArray();
        }

        using MemoryStream streamToRead = new(unitCellBytes);
        PipeReader unitCellPipeReader = PipeReader.Create(streamToRead);
        List<AtomCoordinate[]> parsedUnitCellFrames = new();
        await dcdParser.ParseAsync(unitCellPipeReader, (_, coords) =>
        {
            parsedUnitCellFrames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Single(parsedUnitCellFrames);
        Assert.Equal(2, parsedUnitCellFrames[0].Length);
        Assert.Equal(1.0f, parsedUnitCellFrames[0][0].X);
        Assert.Equal(3.0f, parsedUnitCellFrames[0][0].Y);
        Assert.Equal(5.0f, parsedUnitCellFrames[0][0].Z);
    }

    /// <summary>
    /// Verifies edge cases and memory buffer reallocation in <see cref="LammpsCustomPipeReader"/>.
    /// </summary>
    [Fact]
    public async Task LammpsCustomPipeReader_EdgeCasesAndFallbacks()
    {
        // WHAT: Tests multi-frame LAMMPS dumps with buffer growth, UTF-8 BOM, empty lines, and bad coordinate lines.
        // WHY:  Exercises buffer growth branches and scalar/SIMD coordinate validation paths.
        // HOW:  Feeds synthetic dump text into ParseAsync and tests TryParseLammpsAtomLine directly.

        // 1. Direct validation tests for TryParseLammpsAtomLine (sep1, sep2, and bad floats)
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 2"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 not_a_float 2.0 3.0"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 1.0 not_a_float 3.0"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 1.0 2.0 not_a_float"u8, out _));

        // 2. Stream with UTF-8 BOM preamble, empty lines, and growing atom counts
        string dumpContent =
            "\uFEFF" + // UTF-8 BOM
            "\n" +     // Empty line
            "\r\n" +   // Carriage return empty line
            "ITEM: TIMESTEP\n" +
            "0\n" +
            "ITEM: NUMBER OF ATOMS\n" +
            "2\n" +
            "ITEM: BOX BOUNDS pp pp pp\n" +
            "0.0 10.0\n0.0 10.0\n0.0 10.0\n" +
            "ITEM: ATOMS id type x y z\n" +
            "1 1 1.0 2.0 3.0\n" +
            "2 1 4.0 5.0 6.0\n" +
            "ITEM: TIMESTEP\n" +
            "100\n" +
            "ITEM: NUMBER OF ATOMS\n" +
            "4\n" +
            "ITEM: BOX BOUNDS pp pp pp\n" +
            "0.0 10.0\n0.0 10.0\n0.0 10.0\n" +
            "ITEM: ATOMS id type x y z\n" +
            "1 1 1.1 2.1 3.1\n" +
            "2 1 4.1 5.1 6.1\n" +
            "3 1 7.1 8.1 9.1\n" +
            "4 1 10.1 11.1 12.1\n";

        byte[] dumpBytes = Encoding.UTF8.GetBytes(dumpContent);
        using MemoryStream ms = new(dumpBytes);
        PipeReader pipeReader = PipeReader.Create(ms);

        LammpsCustomPipeReader parser = new();
        int frames = 0;
        await parser.ParseAsync(pipeReader, (_, coords) =>
        {
            frames++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, frames);
    }

    /// <summary>
    /// Verifies that <see cref="XyzPipeReader"/> handles empty lines, buffer growth across frames,
    /// and tests <see cref="XyzPipeReader.ParseAtomLine"/>.
    /// </summary>
    [Fact]
    public async Task XyzPipeReader_MultiFrameBufferGrowthAndBlankLines()
    {
        // WHAT: Tests XYZ stream parsing with blank lines and expanding atom buffers.
        // WHY:  Ensures that the ArrayPool buffer is safely returned and resized across differing frame sizes.
        // HOW:  Parses frame 1 (2 atoms) followed by frame 2 (4 atoms) with intermittent empty lines.

        // Test ParseAtomLine helper directly
        Assert.True(XyzPipeReader.ParseAtomLine("C 1.000 2.000 3.000"u8, out AtomCoordinate parsedCoord));
        Assert.Equal(1.0f, parsedCoord.X);

        string xyzContent =
            "\n" +
            "2\n" +
            "Frame 0 Comment\n" +
            "C 1.0 2.0 3.0\n" +
            "O 4.0 5.0 6.0\n" +
            "\n" +
            "4\n" +
            "Frame 1 Comment\n" +
            "C 1.1 2.1 3.1\n" +
            "O 4.1 5.1 6.1\n" +
            "N 7.1 8.1 9.1\n" +
            "H 10.1 11.1 12.1\n";

        byte[] xyzBytes = Encoding.UTF8.GetBytes(xyzContent);
        using MemoryStream ms = new(xyzBytes);
        PipeReader pipeReader = PipeReader.Create(ms);

        XyzPipeReader parser = new();
        int frames = 0;
        await parser.ParseAsync(pipeReader, (_, coords) =>
        {
            frames++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, frames);
    }

    /// <summary>
    /// Verifies boundary cases in <see cref="DuckDbParquetWriter"/>, such as creating non-existent destination
    /// directories, processing empty coordinate slices, and computing centroids of empty spans.
    /// </summary>
    [Fact]
    public async Task DuckDbParquetWriter_EmptyFrameAndMissingDir()
    {
        // WHAT: Tests CalculateCentroid with an empty span and ConvertTrajectoryToParquetAsync with a new directory.
        // WHY:  Verifies directory creation and empty frame skipping branches.
        // HOW:  Calls CalculateCentroid directly and runs ConvertTrajectoryToParquetAsync targeting a nested folder.

        AtomCoordinate emptyCentroid = DuckDbParquetWriter.CalculateCentroid(ReadOnlySpan<AtomCoordinate>.Empty);
        Assert.Equal(default, emptyCentroid);

        string tempBase = Path.Combine(Path.GetTempPath(), $"weaver_parquet_test_{Guid.NewGuid():N}");
        string nestedDir = Path.Combine(tempBase, "nested", "storage");
        string destParquet = Path.Combine(nestedDir, "output.parquet");
        string sourceXyz = Path.Combine(tempBase, "input.xyz");

        Directory.CreateDirectory(tempBase);
        try
        {
            await File.WriteAllTextAsync(sourceXyz, "2\ncomment\nC 1.0 2.0 3.0\nO 4.0 5.0 6.0\n");

            DuckDbParquetWriter writer = new();
            XyzPipeReader parser = new();

            long count = await writer.ConvertTrajectoryToParquetAsync(sourceXyz, destParquet, parser);
            Assert.Equal(2, count);
            Assert.True(File.Exists(destParquet));

            // Test empty frame callback handling via custom parser
            MockEmptyFrameParser emptyParser = new();
            string emptyParquet = Path.Combine(nestedDir, "empty.parquet");
            long emptyCount = await writer.ConvertTrajectoryToParquetAsync(sourceXyz, emptyParquet, emptyParser);
            Assert.Equal(0, emptyCount);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="EndocrineStateEngine"/> applies balanced target setpoints when switching
    /// into the <see cref="ToggleState.Transitioning"/> calibration state.
    /// </summary>
    [Fact]
    public void EndocrineStateEngine_TransitioningState()
    {
        // WHAT: Executes a toggle switch request targeting the Transitioning state.
        // WHY:  Verifies intermediate hormonal homeostasis setpoints (E2: 300 pmol/L, T: 8.0 nmol/L).
        // HOW:  Asserts target setpoints on the resulting EndocrineTargetProfile.
        EndocrineStateEngine engine = new();
        ToggleSwitchRequest request = new()
        {
            TargetState = ToggleState.Transitioning
        };

        EndocrineTargetProfile profile = engine.UpdateToggleState(request);
        Assert.Equal(ToggleState.Transitioning, profile.TargetState);
        Assert.Equal(300.0, profile.TargetEstradiolPmolL);
        Assert.Equal(8.0, profile.TargetTestosteroneNmolL);

        // Default Regimen when null: EstrogenicDominant defaults to MaleToFemale
        EndocrineTargetProfile pMtf = engine.UpdateToggleState(new ToggleSwitchRequest
        {
            TargetState = ToggleState.EstrogenicDominant,
            Regimen = null
        });
        Assert.Equal(TherapeuticRegimen.MaleToFemale, pMtf.Regimen);

        // Default Regimen when null: AndrogenicDominant defaults to FemaleToMale
        EndocrineTargetProfile pFtm = engine.UpdateToggleState(new ToggleSwitchRequest
        {
            TargetState = ToggleState.AndrogenicDominant,
            Regimen = null
        });
        Assert.Equal(TherapeuticRegimen.FemaleToMale, pFtm.Regimen);
    }

    /// <summary>
    /// Verifies HTTP error codes, bad request validation, and multi-format dispatching across minimal API endpoints.
    /// </summary>
    [Fact]
    public async Task Endpoints_ErrorAndFormatBranches()
    {
        // WHAT: Tests validation rejections and multi-format dispatching (.dump, .dcd, .unknown).
        // WHY:  Exercises all switch branches and error return pathways in Program.cs.
        // HOW:  Issues HTTP GET and POST requests against the test server.

        // 1. POST /api/datasets/import with null/missing files
        using (MultipartFormDataContent emptyForm = new())
        {
            HttpResponseMessage emptyResp = await _client.PostAsync("/api/datasets/import", emptyForm);
            Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);
        }

        // 2. POST /api/datasets/import with corrupted manifest JSON
        using (MultipartFormDataContent badJsonForm = new())
        {
            ByteArrayContent arcPart = new("dummy archive"u8.ToArray());
            arcPart.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            badJsonForm.Add(arcPart, "archive", "test.tar.gz");

            ByteArrayContent manPart = new("not valid json"u8.ToArray());
            manPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            badJsonForm.Add(manPart, "manifest", "test.manifest.json");

            HttpResponseMessage badJsonResp = await _client.PostAsync("/api/datasets/import", badJsonForm);
            Assert.Equal(HttpStatusCode.BadRequest, badJsonResp.StatusCode);
        }

        // 3. POST /api/datasets/import with checksum mismatch
        using (MultipartFormDataContent mismatchForm = new())
        {
            ByteArrayContent arcPart = new("corrupted content"u8.ToArray());
            arcPart.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            mismatchForm.Add(arcPart, "archive", "mismatch.tar.gz");

            DatasetManifest fakeManifest = new()
            {
                DatasetName = "MismatchDS",
                ArchiveFileName = "mismatch.tar.gz",
                Sha256Checksum = "0000000000000000000000000000000000000000000000000000000000000000",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                SizeInBytes = 17
            };
            ByteArrayContent manPart = new(JsonSerializer.SerializeToUtf8Bytes(fakeManifest));
            manPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            mismatchForm.Add(manPart, "manifest", "mismatch.manifest.json");

            HttpResponseMessage mismatchResp = await _client.PostAsync("/api/datasets/import", mismatchForm);
            Assert.Equal(HttpStatusCode.BadRequest, mismatchResp.StatusCode);
        }

        // 4. Parser process endpoint: .dump, .dcd, .unknown
        HttpResponseMessage streamDump = await _client.PostAsync("/api/parser/process/dna_sample.dump", null);
        Assert.Equal(HttpStatusCode.OK, streamDump.StatusCode);

        HttpResponseMessage streamDcd = await _client.PostAsync("/api/parser/process/dna_sample.dcd", null);
        Assert.Equal(HttpStatusCode.OK, streamDcd.StatusCode);

        // Unsupported extension throws NotSupportedException (returns 500)
        string dummyUnknown = Path.Combine(_sharedDataDir, "sample.unknown");
        await File.WriteAllTextAsync(dummyUnknown, "unknown content");
        try
        {
            HttpResponseMessage streamUnknown = await _client.PostAsync("/api/parser/process/sample.unknown", null);
            Assert.Equal(HttpStatusCode.InternalServerError, streamUnknown.StatusCode);

            HttpResponseMessage exportUnknown = await _client.PostAsync("/api/parser/export-parquet/sample.unknown", null);
            Assert.Equal(HttpStatusCode.InternalServerError, exportUnknown.StatusCode);

            HttpResponseMessage analyzeUnknown = await _client.PostAsync("/api/endocrine/analyze/sample.unknown", null);
            Assert.Equal(HttpStatusCode.InternalServerError, analyzeUnknown.StatusCode);
        }
        finally
        {
            if (File.Exists(dummyUnknown)) File.Delete(dummyUnknown);
        }

        // 5. Parquet export endpoint: .dump, .dcd
        HttpResponseMessage exportDump = await _client.PostAsync("/api/parser/export-parquet/dna_sample.dump", null);
        Assert.Equal(HttpStatusCode.OK, exportDump.StatusCode);

        HttpResponseMessage exportDcd = await _client.PostAsync("/api/parser/export-parquet/dna_sample.dcd", null);
        Assert.Equal(HttpStatusCode.OK, exportDcd.StatusCode);

        // 6. Aptamer analysis: .dump (bound contacts >= 2), .dcd
        HttpResponseMessage analyzeDump = await _client.PostAsync("/api/endocrine/analyze/dna_sample.dump", null);
        Assert.Equal(HttpStatusCode.OK, analyzeDump.StatusCode);
        AptamerTrajectoryMetrics? dumpMetrics = await analyzeDump.Content.ReadFromJsonAsync<AptamerTrajectoryMetrics>();
        Assert.NotNull(dumpMetrics);
        Assert.True(dumpMetrics.BindingOccupancyRatio >= 0.50);

        HttpResponseMessage analyzeDcd = await _client.PostAsync("/api/endocrine/analyze/dna_sample.dcd", null);
        Assert.Equal(HttpStatusCode.OK, analyzeDcd.StatusCode);
    }
}

/// <summary>
/// Mock trajectory parser emitting an empty coordinate slice to test zero-atom frame handling.
/// </summary>
public sealed class MockEmptyFrameParser : ITrajectoryParser
{
    /// <inheritdoc/>
    public string FormatName => "MockEmpty";

    /// <inheritdoc/>
    public async Task ParseAsync(
        PipeReader reader,
        FrameParsedCallback onFrameAvailable,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Reads the pipe to completion and emits an empty coordinate span.
        // WHY:  Tests the coordinates.IsEmpty check in downstream writers.
        // HOW:  Invokes onFrameAvailable with ReadOnlySpan<AtomCoordinate>.Empty.
        await reader.ReadAsync(cancellationToken);
        await onFrameAvailable(0, ReadOnlySpan<AtomCoordinate>.Empty);
    }
}
