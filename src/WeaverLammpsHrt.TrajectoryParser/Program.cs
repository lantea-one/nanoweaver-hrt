using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaverLammpsHrt.TrajectoryParser.Abstractions;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Pipelines;
using WeaverLammpsHrt.TrajectoryParser.Services;
using WeaverLammpsHrt.TrajectoryParser.Storage;

namespace WeaverLammpsHrt.TrajectoryParser;

/// <summary>
/// Application entry point configuring the ASP.NET Core 10 web microservice, zero-allocation trajectory 
/// streaming pipelines, and sovereign dataset management endpoints.
/// </summary>
public class Program
{
    /// <summary>
    /// Service startup timestamp to track node uptime.
    /// </summary>
    private static readonly DateTime StartTimeUtc = DateTime.UtcNow;

    /// <summary>
    /// Global counter tracking total molecular simulation frames processed across all trajectories.
    /// </summary>
    private static long _totalFramesParsed;

    /// <summary>
    /// Main entry point for the microservice.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // WHAT: Registers core sovereign data services and zero-allocation parser components.
        // WHY:  Decouples the web presentation and cryptographic transfer layer from the low-level pipeline readers.
        // HOW:  Registers singleton services into the Microsoft.Extensions.DependencyInjection container.
        builder.Services.AddSingleton<IDatasetManager, DatasetTransferService>();
        builder.Services.AddSingleton<XyzPipeReader>();
        builder.Services.AddSingleton<DcdPipeReader>();
        builder.Services.AddSingleton<LammpsCustomPipeReader>();
        builder.Services.AddSingleton<IParquetTrajectoryWriter, DuckDbParquetWriter>();
        builder.Services.AddSingleton<EndocrineStateEngine>();

        // Configure JSON serialization options
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.WriteIndented = true;
        });

        WebApplication app = builder.Build();

        // WHAT: Serves the sovereign Web UI from the wwwroot directory.
        // WHY:  Allows researchers and engineers to manage trajectories and monitor telemetry via browser without CLI dependencies.
        // HOW:  Standard ASP.NET Core static file middleware.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // ---------------------------------------------------------------------
        // REST API: Trajectory Discovery
        // ---------------------------------------------------------------------
        app.MapGet("/api/datasets", async (IDatasetManager manager, CancellationToken ct) =>
        {
            IReadOnlyList<TrajectoryFileItem> items = await manager.ListTrajectoriesAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListTrajectories")
        .WithSummary("Lists available trajectory files in the active simulation volume.");

        // ---------------------------------------------------------------------
        // REST API: Dataset Export with SHA-256 Provenance
        // ---------------------------------------------------------------------
        app.MapPost("/api/datasets/export", async (ExportRequest request, IDatasetManager manager, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DatasetName))
            {
                return Results.BadRequest(new { error = "Dataset name must not be empty." });
            }

            DatasetManifest manifest = await manager.ExportDatasetAsync(request.DatasetName, request.Files, ct);
            return Results.Ok(manifest);
        })
        .WithName("ExportDataset")
        .WithSummary("Packages selected simulation files into a .tar.gz bundle with an immutable cryptographic manifest.");

        // ---------------------------------------------------------------------
        // REST API: Download Exported Tarball or Manifest
        // ---------------------------------------------------------------------
        app.MapGet("/api/datasets/download/{fileName}", (string fileName, IConfiguration config) =>
        {
            string exportDir = config["TRAJECTORY_EXPORT_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "exports");
            string safePath = Path.Combine(exportDir, Path.GetFileName(fileName));

            if (!File.Exists(safePath))
            {
                return Results.NotFound(new { error = "Exported file not found." });
            }

            return Results.File(safePath, "application/octet-stream", fileDownloadName: fileName);
        })
        .WithName("DownloadExport")
        .WithSummary("Downloads an exported dataset bundle or its manifest.");

        // ---------------------------------------------------------------------
        // REST API: Dataset Import & Cryptographic Verification
        // ---------------------------------------------------------------------
        app.MapPost("/api/datasets/import", async (IFormFile archive, IFormFile manifest, IDatasetManager manager, CancellationToken ct) =>
        {
            if (archive == null || manifest == null)
            {
                return Results.BadRequest(new { error = "Both archive (.tar.gz) and manifest (.json) files are required." });
            }

            DatasetManifest? parsedManifest;
            try
            {
                using Stream manifestStream = manifest.OpenReadStream();
                parsedManifest = await JsonSerializer.DeserializeAsync<DatasetManifest>(manifestStream, cancellationToken: ct);
            }
            catch (JsonException)
            {
                parsedManifest = null;
            }

            if (parsedManifest == null)
            {
                return Results.BadRequest(new { error = "Failed to parse the uploaded manifest JSON." });
            }

            using (Stream archiveStream = archive.OpenReadStream())
            {
                bool success = await manager.ImportDatasetAsync(archiveStream, parsedManifest, ct);
                if (!success)
                {
                    return Results.BadRequest(new { error = "Cryptographic integrity check failed. SHA-256 hash mismatch." });
                }
            }

            return Results.Ok(new { message = "Dataset successfully verified and imported.", manifest = parsedManifest });
        })
        .WithName("ImportDataset")
        .WithSummary("Verifies cryptographic SHA-256 integrity and imports a dataset archive into the active volume.")
        .DisableAntiforgery();

        // ---------------------------------------------------------------------
        // REST API: Zero-Allocation Trajectory Parser Trigger
        // ---------------------------------------------------------------------
        app.MapPost("/api/parser/process/{fileName}", async (
            string fileName,
            IConfiguration config,
            XyzPipeReader xyzParser,
            DcdPipeReader dcdParser,
            LammpsCustomPipeReader lammpsParser,
            CancellationToken ct) =>
        {
            string dataDir = config["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
            string safePath = Path.Combine(dataDir, Path.GetFileName(fileName));

            if (!File.Exists(safePath))
            {
                return Results.NotFound(new { error = "Target trajectory file does not exist." });
            }

            string ext = Path.GetExtension(safePath).ToLowerInvariant();
            ITrajectoryParser parser = ext switch
            {
                ".xyz" => xyzParser,
                ".dcd" => dcdParser,
                ".lammpstrj" or ".dump" or ".custom" => lammpsParser,
                _ => throw new NotSupportedException($"Unsupported trajectory format extension: {ext}")
            };

            Stopwatch sw = Stopwatch.StartNew();
            long framesProcessed = 0;
            long totalAtomsEncountered = 0;

            // WHAT: Opens a high-performance PipeReader over the trajectory file.
            // WHY:  Enables zero-allocation chunked stream processing directly from disk.
            // HOW:  PipeReader.Create wraps the FileStream.
            using FileStream fileStream = new(safePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            PipeReader pipeReader = PipeReader.Create(fileStream);

            await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
            {
                framesProcessed++;
                totalAtomsEncountered += coordinates.Length;
                Interlocked.Increment(ref _totalFramesParsed);
                return ValueTask.CompletedTask;
            }, ct);

            sw.Stop();

            return Results.Ok(new
            {
                file_name = fileName,
                format = parser.FormatName,
                frames_parsed = framesProcessed,
                total_atoms_processed = totalAtomsEncountered,
                elapsed_milliseconds = sw.ElapsedMilliseconds,
                frames_per_second = framesProcessed > 0 && sw.Elapsed.TotalSeconds > 0
                    ? framesProcessed / sw.Elapsed.TotalSeconds
                    : 0
            });
        })
        .WithName("ProcessTrajectory")
        .WithSummary("Triggers the zero-allocation parser on a specific trajectory file and returns performance metrics.");

        // ---------------------------------------------------------------------
        // REST API: Columnar Parquet Serialization via DuckDB
        // ---------------------------------------------------------------------
        app.MapPost("/api/parser/export-parquet/{fileName}", async (
            string fileName,
            IConfiguration config,
            IParquetTrajectoryWriter parquetWriter,
            XyzPipeReader xyzParser,
            DcdPipeReader dcdParser,
            LammpsCustomPipeReader lammpsParser,
            CancellationToken ct) =>
        {
            string dataDir = config["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
            string safeSourcePath = Path.Combine(dataDir, Path.GetFileName(fileName));

            if (!File.Exists(safeSourcePath))
            {
                return Results.NotFound(new { error = "Target trajectory file does not exist." });
            }

            string parquetDir = config["TRAJECTORY_PARQUET_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "parquet");
            string parquetFileName = $"{Path.GetFileNameWithoutExtension(fileName)}.parquet";
            string safeDestinationPath = Path.Combine(parquetDir, parquetFileName);

            string ext = Path.GetExtension(safeSourcePath).ToLowerInvariant();
            ITrajectoryParser parser = ext switch
            {
                ".xyz" => xyzParser,
                ".dcd" => dcdParser,
                ".lammpstrj" or ".dump" or ".custom" => lammpsParser,
                _ => throw new NotSupportedException($"Unsupported trajectory format extension: {ext}")
            };

            Stopwatch sw = Stopwatch.StartNew();
            long totalRecords = await parquetWriter.ConvertTrajectoryToParquetAsync(
                safeSourcePath,
                safeDestinationPath,
                parser,
                ct);
            sw.Stop();

            FileInfo parquetInfo = new(safeDestinationPath);

            return Results.Ok(new
            {
                source_file = fileName,
                parquet_file = parquetFileName,
                format = parser.FormatName,
                total_records_serialized = totalRecords,
                file_size_bytes = parquetInfo.Exists ? parquetInfo.Length : 0,
                elapsed_milliseconds = sw.ElapsedMilliseconds
            });
        })
        .WithName("ExportTrajectoryToParquet")
        .WithSummary("Converts a raw trajectory stream to a columnar Apache Parquet file using DuckDB and SIMD intrinsics.");

        // ---------------------------------------------------------------------
        // REST API: Node Telemetry & Accelerator Monitoring
        // ---------------------------------------------------------------------
        app.MapGet("/api/system/telemetry", () =>
        {
            Process currentProcess = Process.GetCurrentProcess();
            TimeSpan uptime = DateTime.UtcNow - StartTimeUtc;

            string backend = Environment.GetEnvironmentVariable("ACCELERATOR_BACKEND") ?? "Intel iGPU / OpenCL (Default)";

            SystemTelemetry telemetry = new()
            {
                NodeName = Environment.MachineName,
                AcceleratorBackend = backend,
                FramesParsed = Interlocked.Read(ref _totalFramesParsed),
                FramesPerSecond = 0.0,
                HeapMemoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0),
                WorkingSetMb = currentProcess.WorkingSet64 / (1024.0 * 1024.0),
                GcGen0Collections = GC.CollectionCount(0),
                GcGen1Collections = GC.CollectionCount(1),
                GcGen2Collections = GC.CollectionCount(2),
                Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
            };

            return Results.Ok(telemetry);
        })
        .WithName("GetSystemTelemetry")
        .WithSummary("Provides real-time telemetry on GC allocations, memory consumption, and GPU status.");

        // ---------------------------------------------------------------------
        // REST API: Endocrine Target Profile & Toggle Switch
        // ---------------------------------------------------------------------
        app.MapGet("/api/endocrine/profile", (EndocrineStateEngine engine) =>
        {
            return Results.Ok(engine.GetActiveProfile());
        })
        .WithName("GetEndocrineProfile")
        .WithSummary("Retrieves the currently active patient endocrine target profile and operational toggle state.");

        app.MapPost("/api/endocrine/toggle", (ToggleSwitchRequest request, EndocrineStateEngine engine) =>
        {
            EndocrineTargetProfile updated = engine.UpdateToggleState(request);
            return Results.Ok(updated);
        })
        .WithName("UpdateEndocrineToggle")
        .WithSummary("Toggles the synthetic endocrine regulatory state between Estrogenic and Androgenic dominance.");

        app.MapGet("/api/endocrine/kinetics", (EndocrineStateEngine engine) =>
        {
            return Results.Ok(engine.SimulateKinetics());
        })
        .WithName("GetHormoneKinetics")
        .WithSummary("Calculates pharmacodynamic hormone concentration stabilization curves over a 72-hour horizon.");

        app.MapPost("/api/endocrine/analyze/{fileName}", async (
            string fileName,
            IConfiguration config,
            XyzPipeReader xyzParser,
            DcdPipeReader dcdParser,
            LammpsCustomPipeReader lammpsParser,
            CancellationToken ct) =>
        {
            string dataDir = config["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
            string safePath = Path.Combine(dataDir, Path.GetFileName(fileName));

            if (!File.Exists(safePath))
            {
                return Results.NotFound(new { error = "Target trajectory file does not exist." });
            }

            string ext = Path.GetExtension(safePath).ToLowerInvariant();
            ITrajectoryParser parser = ext switch
            {
                ".xyz" => xyzParser,
                ".dcd" => dcdParser,
                ".lammpstrj" or ".dump" or ".custom" => lammpsParser,
                _ => throw new NotSupportedException($"Unsupported trajectory format extension: {ext}")
            };

            long framesAnalyzed = 0;
            long totalContacts = 0;
            long boundFramesCount = 0;

            using FileStream fileStream = new(safePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            PipeReader pipeReader = PipeReader.Create(fileStream);

            // Define simulated pocket indices (first 4 atoms) and ligand index (last atom) for sample evaluation
            await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
            {
                if (coordinates.Length >= 2)
                {
                    framesAnalyzed++;
                    int pocketCount = Math.Min(4, coordinates.Length - 1);
                    Span<int> pocketIndices = stackalloc int[pocketCount];
                    for (int i = 0; i < pocketCount; i++) pocketIndices[i] = i;
                    int ligandIndex = coordinates.Length - 1;

                    int contacts = AptamerAffinityAnalyzer.CountBindingContacts(coordinates, pocketIndices, ligandIndex);
                    totalContacts += contacts;

                    // Bound criteria: at least 2 simultaneous contacts within 5.0 Å
                    if (contacts >= 2)
                    {
                        boundFramesCount++;
                    }
                }
                return ValueTask.CompletedTask;
            }, ct);

            double avgContacts = framesAnalyzed > 0 ? (double)totalContacts / framesAnalyzed : 0;
            double occupancyRatio = framesAnalyzed > 0 ? (double)boundFramesCount / framesAnalyzed : 0;
            string verdict = occupancyRatio >= 0.85 ? "Stable High-Affinity Complex"
                           : occupancyRatio >= 0.50 ? "Moderate / Dynamic Equilibrium"
                           : "Unstable / Dissociated Complex";

            AptamerTrajectoryMetrics metrics = new()
            {
                TrajectoryFileName = fileName,
                FramesAnalyzed = framesAnalyzed,
                AverageContactsPerFrame = Math.Round(avgContacts, 2),
                BindingOccupancyRatio = Math.Round(occupancyRatio, 3),
                StabilityVerdict = verdict
            };

            return Results.Ok(metrics);
        })
        .WithName("AnalyzeAptamerAffinity")
        .WithSummary("Streams trajectory coordinates to compute real-time ligand-pocket contact ratios and binding stability.");

        app.Run();
    }
}

/// <summary>
/// Request payload for initiating a dataset export.
/// </summary>
public sealed record ExportRequest
{
    /// <summary>
    /// Desired name of the exported dataset bundle.
    /// </summary>
    [JsonPropertyName("dataset_name")]
    public string DatasetName { get; init; } = string.Empty;

    /// <summary>
    /// Optional subset of specific file names to package. If null or empty, all files in the directory are included.
    /// </summary>
    [JsonPropertyName("files")]
    public string[]? Files { get; init; }
}
