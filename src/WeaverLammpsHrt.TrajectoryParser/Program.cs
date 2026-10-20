using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Abstractions;
using WeaverLammpsHrt.TrajectoryParser.Compliance;
using WeaverLammpsHrt.TrajectoryParser.Components;
using WeaverLammpsHrt.TrajectoryParser.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Fabrication;
using WeaverLammpsHrt.TrajectoryParser.Genomics;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Pipelines;
using WeaverLammpsHrt.TrajectoryParser.Security.Cryptography;
using WeaverLammpsHrt.TrajectoryParser.Security.KeyManagement;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;
using WeaverLammpsHrt.TrajectoryParser.Security.Storage;
using WeaverLammpsHrt.TrajectoryParser.Services;
using WeaverLammpsHrt.TrajectoryParser.Services.Stack;
using WeaverLammpsHrt.TrajectoryParser.Storage;

namespace WeaverLammpsHrt.TrajectoryParser;

/// <summary>
/// Application entry point configuring the ASP.NET Core 10 web microservice, zero-allocation trajectory 
/// streaming pipelines, Blazor interactive server UI, and sovereign dataset management endpoints.
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
    /// Gets the global count of total frames processed by the zero-allocation parser.
    /// </summary>
    public static long TotalFramesParsed => Interlocked.Read(ref _totalFramesParsed);

    /// <summary>
    /// Main entry point for the microservice.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // WHAT: Registers Blazor Interactive Server services for single-source C# UI management.
        // WHY:  Unifies web interface, service telemetry, and domain algorithms into a single high-performance C# 13 codebase.
        // HOW:  Adds Razor Components and Interactive Server Render Mode into ASP.NET Core 10 DI.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddHttpContextAccessor();

        // WHAT: Registers core sovereign data services, database configuration repository, and zero-allocation parser components.
        // WHY:  Decouples the web presentation and cryptographic transfer layer from the low-level pipeline readers.
        // HOW:  Registers singleton services into the Microsoft.Extensions.DependencyInjection container.
        builder.Services.AddSingleton<IConfigurationRepository, SystemConfigurationStore>();
        builder.Services.AddSingleton<IStackServiceManager, StackServiceManager>();
        builder.Services.AddSingleton<IFabricationService, FabricationService>();
        builder.Services.AddSingleton<IPharmacogenomicsService, PharmacogenomicsService>();
        builder.Services.AddSingleton<IDatasetManager, DatasetTransferService>();
        builder.Services.AddSingleton<XyzPipeReader>();
        builder.Services.AddSingleton<DcdPipeReader>();
        builder.Services.AddSingleton<LammpsCustomPipeReader>();
        builder.Services.AddSingleton<IParquetTrajectoryWriter, DuckDbParquetWriter>();
        builder.Services.AddSingleton<EndocrineStateEngine>();
        builder.Services.AddSingleton<IPostQuantumCryptoProvider, PostQuantumCryptoProvider>();
        builder.Services.AddSingleton<IKeyManagementService, KeyManagementService>();
        builder.Services.AddSingleton<IComplianceAuditService, ComplianceAuditService>();
        builder.Services.AddSingleton<IEncryptedStorageService, EncryptedStorageService>();

        // WHAT: Configures Swagger & OpenAPI generation with XML comments and Swashbuckle annotations.
        // WHY:  Provides interactive API exploration (/swagger) and ReDoc documentation (/documentation).
        // HOW:  Registers API explorer, SwaggerGen with OpenApiInfo, XML doc comments, and annotations.
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Weaver-LAMMPS-HRT Sovereign API",
                Version = "v1",
                Description = "Sovereign Hormone Replacement Therapy (HRT) DNA Origami, Endocrine Simulation, and Trajectory Analytics API.",
                Contact = new OpenApiContact
                {
                    Name = "Weaver-LAMMPS-HRT Research Initiative",
                    Url = new Uri("https://github.com/lantea-one/dotnet-weaver-lammps-hrt")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://github.com/lantea-one/dotnet-weaver-lammps-hrt/blob/main/LICENSE")
                }
            });

            c.EnableAnnotations();
            c.IncludeXmlComments(Assembly.GetExecutingAssembly(), includeControllerXmlComments: true);
        });

        // Configure JSON serialization options
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.WriteIndented = true;
        });

        WebApplication app = builder.Build();

        // WHAT: Initializes authoritative database tables for configuration and single-use share keys.
        // WHY:  Guarantees that database-first precedence is active before requests arrive.
        // HOW:  Executes table verification via IConfigurationRepository.
        IConfigurationRepository configRepo = app.Services.GetRequiredService<IConfigurationRepository>();
        await configRepo.InitializeAsync();

        // WHAT: Serves static assets (CSS, JS) and Blazor Interactive Server WebSockets.
        // WHY:  Enables sovereign Web UI navigation without requiring external NPM/Node toolchains.
        // HOW:  Standard ASP.NET Core static files and Blazor endpoint mapping.
        app.UseStaticFiles();
        app.UseAntiforgery();

        // WHAT: Enables Swagger JSON, Swagger UI (/swagger), and ReDoc (/documentation).
        // WHY:  Exposes interactive OpenAPI 3.0 interfaces and comprehensive 3-column reference.
        // HOW:  UseSwagger, UseSwaggerUI, and UseReDoc middleware.
        app.UseSwagger(c =>
        {
            c.RouteTemplate = "swagger/{documentName}/swagger.json";
        });
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Weaver-LAMMPS-HRT API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Weaver-LAMMPS-HRT Swagger UI";
        });
        app.UseReDoc(c =>
        {
            c.SpecUrl = "/swagger/v1/swagger.json";
            c.RoutePrefix = "documentation";
            c.DocumentTitle = "Weaver-LAMMPS-HRT API Documentation";
        });

        app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html")).ExcludeFromDescription();
        app.MapGet("/documentation", () => Results.Redirect("/documentation/index.html")).ExcludeFromDescription();

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // ---------------------------------------------------------------------
        // REST API: Trajectory Discovery
        // ---------------------------------------------------------------------
        app.MapGet("/api/datasets", async (IDatasetManager manager, CancellationToken ct) =>
        {
            IReadOnlyList<TrajectoryFileItem> items = await manager.ListTrajectoriesAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListTrajectories")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Lists available trajectory files in the active simulation volume.")
        .WithDescription("Scans the shared simulation directory for .xyz, .dump, and .dcd molecular dynamics coordinate files.")
        .Produces<IReadOnlyList<TrajectoryFileItem>>(StatusCodes.Status200OK);

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
        .WithTags("Datasets & Trajectories")
        .WithSummary("Packages selected simulation files into a .tar.gz bundle with an immutable cryptographic manifest.")
        .WithDescription("Creates a tarball archive of simulation coordinate files and computes an immutable SHA-256 manifest.")
        .Produces<DatasetManifest>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

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
        .WithTags("Datasets & Trajectories")
        .WithSummary("Downloads an exported dataset bundle or its manifest.")
        .WithDescription("Streams raw binary or JSON bytes from the export storage directory.")
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .Produces(StatusCodes.Status404NotFound);

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
        .WithTags("Datasets & Trajectories")
        .WithSummary("Verifies cryptographic SHA-256 integrity and imports a dataset archive into the active volume.")
        .WithDescription("Validates the uploaded archive against the accompanying SHA-256 checksum manifest and extracts it into active storage.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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
        .WithTags("Zero-Allocation Parser")
        .WithSummary("Triggers the zero-allocation parser on a specific trajectory file and returns performance metrics.")
        .WithDescription("Streams coordinates via PipeReader and evaluates SIMD throughput with zero heap allocations.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

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
        .WithTags("Zero-Allocation Parser")
        .WithSummary("Converts a raw trajectory stream to a columnar Apache Parquet file using DuckDB and SIMD intrinsics.")
        .WithDescription("Ingests ASCII or binary trajectory frames and serializes columnar coordinate batches into an Apache Parquet file.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

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
        .WithTags("System Telemetry")
        .WithSummary("Provides real-time telemetry on GC allocations, memory consumption, and GPU status.")
        .WithDescription("Reports active accelerator backends, cumulative frames parsed, heap memory, and GC Gen 0/1/2 collection counts.")
        .Produces<SystemTelemetry>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Endocrine Target Profile & Toggle Switch
        // ---------------------------------------------------------------------
        app.MapGet("/api/endocrine/profile", (EndocrineStateEngine engine) =>
        {
            return Results.Ok(engine.GetActiveProfile());
        })
        .WithName("GetEndocrineProfile")
        .WithTags("Endocrine & Pharmacodynamics")
        .WithSummary("Retrieves the currently active patient endocrine target profile and operational toggle state.")
        .WithDescription("Returns target setpoints for 17β-estradiol, total testosterone, progesterone, and clearance rates.")
        .Produces<EndocrineTargetProfile>(StatusCodes.Status200OK);

        app.MapPost("/api/endocrine/toggle", (ToggleSwitchRequest request, EndocrineStateEngine engine) =>
        {
            EndocrineTargetProfile updated = engine.UpdateToggleState(request);
            return Results.Ok(updated);
        })
        .WithName("UpdateEndocrineToggle")
        .WithTags("Endocrine & Pharmacodynamics")
        .WithSummary("Toggles the synthetic endocrine regulatory state between Estrogenic and Androgenic dominance.")
        .WithDescription("Updates the bistable feedback setpoints for MtF or FtM hormone replacement therapy.")
        .Produces<EndocrineTargetProfile>(StatusCodes.Status200OK);

        app.MapGet("/api/endocrine/kinetics", (EndocrineStateEngine engine) =>
        {
            return Results.Ok(engine.SimulateKinetics());
        })
        .WithName("GetHormoneKinetics")
        .WithTags("Endocrine & Pharmacodynamics")
        .WithSummary("Calculates pharmacodynamic hormone concentration stabilization curves over a 72-hour horizon.")
        .WithDescription("Solves coupled differential equations representing sustained-release aptamer nanocarriers and negative feedback loops.")
        .Produces<IReadOnlyList<HormoneCurvePoint>>(StatusCodes.Status200OK);

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
        .WithTags("Endocrine & Pharmacodynamics")
        .WithSummary("Streams trajectory coordinates to compute real-time ligand-pocket contact ratios and binding stability.")
        .WithDescription("Calculates average contact counts per frame and evaluates binding occupancy ratios to output a stability verdict.")
        .Produces<AptamerTrajectoryMetrics>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Authoritative Configuration Management ("Database Wins")
        // ---------------------------------------------------------------------
        app.MapGet("/api/configuration", async (IConfigurationRepository repo, CancellationToken ct) =>
        {
            IReadOnlyList<ConfigurationItem> configs = await repo.GetAllConfigurationsAsync(ct);
            return Results.Ok(configs);
        })
        .WithName("GetAllConfigurations")
        .WithTags("Configuration Management")
        .WithSummary("Retrieves all stack configurations, enforcing database overrides over environment variables.")
        .WithDescription("Returns all configuration keys, their current effective values, and an indication of database precedence.")
        .Produces<IReadOnlyList<ConfigurationItem>>(StatusCodes.Status200OK);

        app.MapPost("/api/configuration/override", async (ConfigOverrideRequest req, IConfigurationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Key))
            {
                return Results.BadRequest(new { error = "Configuration key cannot be empty." });
            }

            await repo.SetDatabaseOverrideAsync(req.Key, req.Value, req.Description ?? "", ct);
            return Results.Ok(new { message = $"Database override saved for '{req.Key}'. Database setting is now active.", key = req.Key, value = req.Value });
        })
        .WithName("SetConfigurationOverride")
        .WithTags("Configuration Management")
        .WithSummary("Persists an authoritative configuration override to the database.")
        .WithDescription("Saves a configuration key-value pair to the database, instantly overriding environment variable settings.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapDelete("/api/configuration/override/{key}", async (string key, IConfigurationRepository repo, CancellationToken ct) =>
        {
            await repo.RevertToDefaultAsync(key, ct);
            return Results.Ok(new { message = $"Database override for '{key}' reverted to default/environment fallback." });
        })
        .WithName("RevertConfigurationOverride")
        .WithTags("Configuration Management")
        .WithSummary("Purges a database override, reverting the setting to its environment variable or default fallback.")
        .WithDescription("Removes the database override record, returning the stack to standard environment variable configuration.")
        .Produces(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Collaborative Single-Use Share Keys & Peer Federation
        // ---------------------------------------------------------------------
        app.MapGet("/api/share-keys", async (IConfigurationRepository repo, CancellationToken ct) =>
        {
            IReadOnlyList<ShareKeyRecord> keys = await repo.ListShareKeysAsync(ct);
            return Results.Ok(keys);
        })
        .WithName("ListShareKeys")
        .WithTags("Sovereign Peering")
        .WithSummary("Retrieves all created single-use share keys and connected remote peer instances.")
        .WithDescription("Lists active, expired, and redeemed peering tokens used for federating remote UI sessions.")
        .Produces<IReadOnlyList<ShareKeyRecord>>(StatusCodes.Status200OK);

        app.MapPost("/api/share-keys/create", async (CreateShareKeyRequest req, IConfigurationRepository repo, CancellationToken ct) =>
        {
            TimeSpan lifetime = ParseExpirationWindow(req.ExpirationWindow, req.LifetimeHours);
            ShareKeyRecord record = await repo.CreateShareKeyAsync(req.Label, lifetime, req.Permissions ?? "FullCollaborator", ct);
            return Results.Ok(record);
        })
        .WithName("CreateShareKey")
        .WithTags("Sovereign Peering")
        .WithSummary("Generates a single-use collaborative share key and persists it to the database.")
        .WithDescription("Creates a cryptographically random single-use token (nanoweaver_hrt_...) with configurable duration.")
        .Produces<ShareKeyRecord>(StatusCodes.Status200OK);

        app.MapPost("/api/share-keys/redeem", async (RedeemShareKeyRequest req, IConfigurationRepository repo, HttpContext http, CancellationToken ct) =>
        {
            string remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? "External UI Instance";
            ShareKeyConsumeResult result = await repo.ConsumeShareKeyAsync(req.Token, remoteIp, ct);
            if (!result.Success)
            {
                return Results.BadRequest(result);
            }
            return Results.Ok(result);
        })
        .WithName("RedeemShareKey")
        .WithTags("Sovereign Peering")
        .WithSummary("Validates and atomically consumes a single-use share key, establishing an authenticated peer session.")
        .WithDescription("Consumes the provided token, records the remote IP, and marks the token redeemed to prevent replay.")
        .Produces<ShareKeyConsumeResult>(StatusCodes.Status200OK)
        .Produces<ShareKeyConsumeResult>(StatusCodes.Status400BadRequest);

        // ---------------------------------------------------------------------
        // REST API: Stack Service Management & Diagnostic Topology
        // ---------------------------------------------------------------------
        app.MapGet("/api/services", async (IStackServiceManager stackMgr, CancellationToken ct) =>
        {
            IReadOnlyList<StackServiceInfo> svcs = await stackMgr.GetStackServicesAsync(ct);
            return Results.Ok(svcs);
        })
        .WithName("GetStackServices")
        .WithTags("Stack Services")
        .WithSummary("Returns real-time status and telemetry for each service in the stack.")
        .WithDescription("Checks connectivity and latency to PostgreSQL, DuckDB, oxDNA, LAMMPS, and Redis.")
        .Produces<IReadOnlyList<StackServiceInfo>>(StatusCodes.Status200OK);

        app.MapPost("/api/services/ping/{serviceId}", async (string serviceId, IStackServiceManager stackMgr, CancellationToken ct) =>
        {
            StackServiceInfo svc = await stackMgr.PingServiceAsync(serviceId, ct);
            return Results.Ok(svc);
        })
        .WithName("PingStackService")
        .WithTags("Stack Services")
        .WithSummary("Tests connectivity and returns fresh telemetry for a specific stack service.")
        .WithDescription("Performs a low-latency socket ping or query to a single dependency.")
        .Produces<StackServiceInfo>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: DNA Origami Fabrication Specifications
        // ---------------------------------------------------------------------
        app.MapGet("/api/fabrication/presets", (IFabricationService fab) =>
        {
            return Results.Ok(fab.GetPresetSamples());
        })
        .WithName("GetFabricationPresets")
        .WithTags("DNA Origami Fabrication")
        .WithSummary("Returns built-in sample digitized DNA designs available for immediate testing.")
        .WithDescription("Provides standard M13mp18 scaffold architectures including 6-helix bundles, origami boxes, and tiles.")
        .Produces<IReadOnlyList<DnaOrigamiSample>>(StatusCodes.Status200OK);

        app.MapPost("/api/fabrication/generate", (FabricationGenerateRequest req, IFabricationService fab) =>
        {
            FabricationPackage pkg = fab.GenerateFabricationPackage(
                req.SequenceOrSampleId,
                req.Regimen,
                req.Geometry ?? "Hexagonal 6-Helix Bundle Nanotube (6HB)",
                req.TargetKd > 0 ? req.TargetKd : 4.2);

            return Results.Ok(pkg);
        })
        .WithName("GenerateFabricationPackage")
        .WithTags("DNA Origami Fabrication")
        .WithSummary("Synthesizes a complete staple routing manifest, annealing schedule, and SHA-256 provenance hash.")
        .WithDescription("Performs algorithmic sequence routing, calculates thermal ramps, and generates pipetting instructions.")
        .Produces<FabricationPackage>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Pharmacogenomics & Genetic Predisposition Screening
        // ---------------------------------------------------------------------
        app.MapGet("/api/genomics/markers", (IPharmacogenomicsService genomics) =>
        {
            return Results.Ok(genomics.GetCatalog());
        })
        .WithName("GetGenomicMarkers")
        .WithTags("Pharmacogenomics")
        .WithSummary("Retrieves the catalog of clinically characterized pharmacogenomic and hemostatic markers.")
        .WithDescription("Lists high-impact variants such as Factor V Leiden, Prothrombin G20210A, AR CAG, and COMT Val158Met.")
        .Produces<IReadOnlyList<GeneticMarker>>(StatusCodes.Status200OK);

        app.MapGet("/api/genomics/presets", (IPharmacogenomicsService genomics) =>
        {
            return Results.Ok(genomics.GetPresetProfiles());
        })
        .WithName("GetGenomicPresets")
        .WithTags("Pharmacogenomics")
        .WithSummary("Retrieves standard clinical genetic test panels for rapid patient profiling.")
        .WithDescription("Provides preset clinical panels (e.g. Thrombophilia Risk, Androgen Insensitivity, Metabolic Wildtype).")
        .Produces<IReadOnlyList<PatientGenomicProfile>>(StatusCodes.Status200OK);

        app.MapPost("/api/genomics/analyze", (GenomicAnalysisRequest req, IPharmacogenomicsService genomics) =>
        {
            PatientGenomicProfile profile = genomics.AnalyzeProfile(
                req.PatientId ?? "ANON-PATIENT-001",
                req.MarkerIds ?? Array.Empty<string>(),
                req.Regimen);

            return Results.Ok(profile);
        })
        .WithName("AnalyzeGenomicProfile")
        .WithTags("Pharmacogenomics")
        .WithSummary("Evaluates patient genetic markers and generates severity-tiered call-out alerts and kinetic modifiers.")
        .WithDescription("Maps observed SNPs to clinical severity tiers and calculates dosage safety adjustments.")
        .Produces<PatientGenomicProfile>(StatusCodes.Status200OK);

        app.MapPost("/api/genomics/analyze-sequence", (SequenceAnalysisRequest req, IPharmacogenomicsService genomics) =>
        {
            PatientGenomicProfile profile = genomics.AnalyzeSequence(
                req.PatientId ?? "ANON-PATIENT-001",
                req.Sequence ?? string.Empty,
                req.Regimen);

            return Results.Ok(profile);
        })
        .WithName("AnalyzeSequenceGenomics")
        .WithTags("Pharmacogenomics")
        .WithSummary("Scans nucleotide sequences for CpG density (TLR9 immunogenicity) and CAG repeat length (AR sensitivity).")
        .WithDescription("Extracts dinucleotide frequencies and tandem repeat counts directly from raw FASTA strings.")
        .Produces<PatientGenomicProfile>(StatusCodes.Status200OK);

        app.MapPost("/api/genomics/apply-adjustments", (GenomicAnalysisRequest req, IPharmacogenomicsService genomics, EndocrineStateEngine engine) =>
        {
            PatientGenomicProfile genomicProfile = genomics.AnalyzeProfile(
                req.PatientId ?? "ANON-PATIENT-001",
                req.MarkerIds ?? Array.Empty<string>(),
                req.Regimen);

            EndocrineTargetProfile baseProfile = engine.GetActiveProfile();
            EndocrineTargetProfile adjustedProfile = genomics.ApplyGeneticAdjustments(baseProfile, genomicProfile);
            engine.SetActiveProfile(adjustedProfile);

            return Results.Ok(new
            {
                genomic_profile = genomicProfile,
                adjusted_endocrine_profile = adjustedProfile
            });
        })
        .WithName("ApplyGenomicAdjustments")
        .WithTags("Pharmacogenomics")
        .WithSummary("Applies patient pharmacogenomic adjustments to the active endocrine target profile.")
        .WithDescription("Updates the active endocrine setpoints with patient-specific clearance and safety caps.")
        .Produces(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Post-Quantum Cryptography & Regulatory Compliance
        // ---------------------------------------------------------------------
        app.MapGet("/api/compliance/status", (IComplianceAuditService compliance) =>
        {
            ComplianceStatusReport report = compliance.GetComplianceStatus();
            return Results.Ok(report);
        })
        .WithName("GetComplianceStatus")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Retrieves current post-quantum encryption at rest and regulatory compliance scorecard.")
        .WithDescription("Reports HIPAA Safe Harbor, GDPR Article 17 crypto-shredding, GINA, and FDA 21 CFR Part 11 integrity status.")
        .Produces<ComplianceStatusReport>(StatusCodes.Status200OK);

        app.MapGet("/api/compliance/audit-ledger", (int? limit, IComplianceAuditService compliance) =>
        {
            IReadOnlyList<AuditRecord> records = compliance.GetAuditRecords(limit ?? 100);
            return Results.Ok(records);
        })
        .WithName("GetAuditLedger")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Retrieves FDA 21 CFR Part 11 immutable hash-chained audit log records.")
        .WithDescription("Returns recent audit events signed with the node's post-quantum ML-DSA-65 key.")
        .Produces<IReadOnlyList<AuditRecord>>(StatusCodes.Status200OK);

        app.MapGet("/api/compliance/verify-ledger", (IComplianceAuditService compliance) =>
        {
            AuditLedgerVerificationResult verification = compliance.VerifyLedgerIntegrity();
            return Results.Ok(verification);
        })
        .WithName("VerifyAuditLedger")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Cryptographically verifies unbroken hash chaining and ML-DSA-65 signatures across the audit ledger.")
        .WithDescription("Performs full mathematical recalculation of SHA-384 digests and verifies all digital signatures.")
        .Produces<AuditLedgerVerificationResult>(StatusCodes.Status200OK);

        app.MapPost("/api/compliance/de-identify", (DeIdentifyRequest req, IComplianceAuditService compliance) =>
        {
            if (string.IsNullOrWhiteSpace(req.RawSubjectIdentifier))
            {
                return Results.BadRequest(new { error = "RawSubjectIdentifier cannot be empty." });
            }

            try
            {
                DeIdentificationResult result = compliance.DeIdentifySubject(
                    req.RawSubjectIdentifier,
                    req.RawGenomicData ?? string.Empty,
                    req.GdprConsentConfirmed,
                    req.GinaPurposeLimited);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DeIdentifySubject")
        .WithSummary("Applies HIPAA Safe Harbor de-identification and GINA/GDPR Article 9 consent validation.");

        app.MapPost("/api/compliance/crypto-shred", (CryptoShredRequest req, IKeyManagementService keyMgmt, IComplianceAuditService compliance) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId))
            {
                return Results.BadRequest(new { error = "SubjectId cannot be empty." });
            }

            bool shredded = keyMgmt.CryptoShredSubjectKey(req.SubjectId);
            if (!shredded)
            {
                return Results.NotFound(new { error = $"Subject '{req.SubjectId}' not found or already crypto-shredded." });
            }

            compliance.LogAction(
                "API_CALLER",
                "CryptoShredSubjectKey",
                req.SubjectId,
                $"{{\"status\":\"destroyed\",\"standard\":\"GDPR_Article_17\"}}");

            return Results.Ok(new
            {
                subject_id = req.SubjectId,
                status = "Crypto-Shredded",
                message = "Subject private decryption keys permanently destroyed pursuant to GDPR Article 17."
            });
        })
        .WithName("CryptoShredSubject")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Executes GDPR Article 17 right-to-erasure via cryptographic destruction of subject decryption keys.")
        .WithDescription("Zeroizes private ML-KEM-768 key material, permanently rendering historical ciphertexts unrecoverable.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/compliance/encrypt-record", (EncryptRecordRequest req, IEncryptedStorageService storage) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId) || req.Payload == null)
            {
                return Results.BadRequest(new { error = "SubjectId and Payload cannot be empty." });
            }

            try
            {
                EncryptedEnvelope envelope = storage.EncryptSubjectRecord(req.SubjectId, req.Payload);
                return Results.Ok(envelope);
            }
            catch (CryptographicException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("EncryptSubjectRecord")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Envelope-encrypts an arbitrary JSON record under the subject's post-quantum ML-KEM-768 key.")
        .WithDescription("Encapsulates a shared AES-256-GCM secret using ML-KEM-768 and returns an authenticated envelope.")
        .Produces<EncryptedEnvelope>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/api/compliance/decrypt-record", (DecryptRecordRequest req, IEncryptedStorageService storage) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId) || req.Envelope == null)
            {
                return Results.BadRequest(new { error = "SubjectId and Envelope cannot be empty." });
            }

            try
            {
                JsonElement payload = storage.DecryptSubjectRecord<JsonElement>(req.SubjectId, req.Envelope);
                return Results.Ok(payload);
            }
            catch (CryptographicException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DecryptSubjectRecord")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Decrypts an authenticated post-quantum envelope using the subject's private key.")
        .WithDescription("Decapsulates the AES key and verifies the GCM authentication tag before returning the decrypted payload.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/api/datasets/export-encrypted", async (
            ExportEncryptedDatasetRequest req,
            IEncryptedStorageService storage,
            IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TrajectoryFileName))
            {
                return Results.BadRequest(new { error = "TrajectoryFileName cannot be empty." });
            }

            string exportDir = req.ExportDirectory ?? config["TRAJECTORY_EXPORT_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "exports");
            string subjectId = string.IsNullOrWhiteSpace(req.SubjectId) ? "sub_default_researcher" : req.SubjectId;

            try
            {
                string bundlePath = await storage.ExportEncryptedDatasetBundleAsync(req.TrajectoryFileName, subjectId, exportDir, ct);
                return Results.Ok(new
                {
                    bundle_path = bundlePath,
                    algorithm = "ML-KEM-768+AES-256-GCM",
                    subject_id = subjectId,
                    message = "Encrypted dataset bundle packaged and signed with ML-DSA-65."
                });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ExportEncryptedDatasetBundle")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Exports a trajectory archive envelope-encrypted with ML-KEM-768 and signed with ML-DSA-65.")
        .WithDescription("Creates a tarball of trajectory coordinates, encrypts it under the subject's post-quantum key, and signs the manifest.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        app.Run();
    }

    /// <summary>
    /// Parses an expiration window string or falls back to lifetime hours.
    /// Supports standard duration windows: '1h', '4h', '24h', '7d', '1m', '1y', and 'never'.
    /// </summary>
    /// <param name="window">Expiration window code (e.g. '1m', '1y', 'never').</param>
    /// <param name="fallbackHours">Numeric fallback hours if window string is not provided.</param>
    /// <returns>A TimeSpan representing the expiration duration.</returns>
    public static TimeSpan ParseExpirationWindow(string? window, int fallbackHours = 24)
    {
        if (string.IsNullOrWhiteSpace(window))
        {
            return TimeSpan.FromHours(fallbackHours > 0 ? fallbackHours : 24);
        }

        return window.Trim().ToLowerInvariant() switch
        {
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            "1m" or "30d" => TimeSpan.FromDays(30),
            "1y" or "365d" => TimeSpan.FromDays(365),
            "never" or "infinite" => TimeSpan.FromDays(36525), // 100 years
            _ => int.TryParse(window, out int parsedHours) ? TimeSpan.FromHours(parsedHours) : TimeSpan.FromHours(fallbackHours > 0 ? fallbackHours : 24)
        };
    }
}

/// <summary>
/// Request payload for initiating a dataset export.
/// </summary>
[SwaggerSchema(Title = "ExportRequest", Description = "Request payload for initiating an export of molecular dynamics trajectories with SHA-256 manifests.")]
public sealed record ExportRequest
{
    /// <summary>
    /// Desired name of the exported dataset bundle.
    /// </summary>
    [Description("Desired name of the exported dataset bundle.")]
    [JsonPropertyName("dataset_name")]
    public string DatasetName { get; init; } = string.Empty;

    /// <summary>
    /// Optional subset of specific file names to package. If null or empty, all files in the directory are included.
    /// </summary>
    [Description("Optional subset of specific file names to package. If null or empty, all files in the directory are included.")]
    [JsonPropertyName("files")]
    public string[]? Files { get; init; }
}

/// <summary>
/// Request payload for persisting a configuration override to the authoritative database.
/// </summary>
[SwaggerSchema(Title = "ConfigOverrideRequest", Description = "Request payload for persisting an authoritative configuration override to the database.")]
public sealed record ConfigOverrideRequest
{
    /// <summary>
    /// Target configuration setting key.
    /// </summary>
    [Description("Target configuration setting key.")]
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// New setting value to persist.
    /// </summary>
    [Description("New setting value to persist.")]
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Optional description for the setting.
    /// </summary>
    [Description("Optional description for the setting.")]
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Request payload for creating a single-use collaborative share key.
/// </summary>
[SwaggerSchema(Title = "CreateShareKeyRequest", Description = "Request payload for generating a cryptographically random single-use collaborative share key.")]
public sealed record CreateShareKeyRequest
{
    /// <summary>
    /// Label or peer description identifying the intended collaborator.
    /// </summary>
    [Description("Label or peer description identifying the intended collaborator.")]
    [JsonPropertyName("label")]
    public string Label { get; init; } = "External Collaborator";

    /// <summary>
    /// Expiration lifetime in hours.
    /// </summary>
    [Description("Expiration lifetime in hours.")]
    [JsonPropertyName("lifetime_hours")]
    public int LifetimeHours { get; init; } = 24;

    /// <summary>
    /// Named expiration window ('1h', '4h', '24h', '7d', '1m', '1y', 'never').
    /// </summary>
    [Description("Named expiration window ('1h', '4h', '24h', '7d', '1m', '1y', 'never').")]
    [JsonPropertyName("expiration_window")]
    public string? ExpirationWindow { get; init; }

    /// <summary>
    /// Permission scope granted (e.g., 'FullCollaborator', 'ReadOnlyTelemetry').
    /// </summary>
    [Description("Permission scope granted (e.g., 'FullCollaborator', 'ReadOnlyTelemetry').")]
    [JsonPropertyName("permissions")]
    public string? Permissions { get; init; }
}

/// <summary>
/// Request payload for redeeming a single-use share key.
/// </summary>
[SwaggerSchema(Title = "RedeemShareKeyRequest", Description = "Request payload for redeeming a single-use share key to establish an authenticated session.")]
public sealed record RedeemShareKeyRequest
{
    /// <summary>
    /// Secret share token string (e.g., 'nanoweaver_hrt_...').
    /// </summary>
    [Description("Secret share token string (e.g., 'nanoweaver_hrt_...').")]
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// Request payload for generating a complete fabrication package.
/// </summary>
[SwaggerSchema(Title = "FabricationGenerateRequest", Description = "Request payload for generating DNA origami staple routing, annealing ramps, and manifests.")]
public sealed record FabricationGenerateRequest
{
    /// <summary>
    /// Raw sequence string or preset sample ID.
    /// </summary>
    [Description("Raw sequence string or preset sample ID.")]
    [JsonPropertyName("sequence_or_sample_id")]
    public string SequenceOrSampleId { get; init; } = string.Empty;

    /// <summary>
    /// Target therapeutic regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [Description("Target therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Target chassis delivery geometry.
    /// </summary>
    [Description("Target chassis delivery geometry.")]
    [JsonPropertyName("geometry")]
    public string? Geometry { get; init; }

    /// <summary>
    /// Target dissociation constant in nanomolar.
    /// </summary>
    [Description("Target dissociation constant in nanomolar.")]
    [JsonPropertyName("target_kd")]
    public double TargetKd { get; init; } = 4.2;
}

/// <summary>
/// Request payload for evaluating a patient's pharmacogenomic profile from marker IDs.
/// </summary>
[SwaggerSchema(Title = "GenomicAnalysisRequest", Description = "Request payload for screening patient genetic markers and computing personalized kinetic modifiers.")]
public sealed record GenomicAnalysisRequest
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    [Description("Patient identifier.")]
    [JsonPropertyName("patient_id")]
    public string? PatientId { get; init; } = "ANON-PATIENT-001";

    /// <summary>
    /// Array of observed genetic marker identifiers (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').
    /// </summary>
    [Description("Array of observed genetic marker identifiers (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').")]
    [JsonPropertyName("marker_ids")]
    public string[]? MarkerIds { get; init; }

    /// <summary>
    /// Active therapeutic regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [Description("Active therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;
}

/// <summary>
/// Request payload for scanning a DNA nucleotide sequence for CpG density and repeat motifs.
/// </summary>
[SwaggerSchema(Title = "SequenceAnalysisRequest", Description = "Request payload for scanning raw nucleotide sequences for immunogenicity and repeat lengths.")]
public sealed record SequenceAnalysisRequest
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    [Description("Patient identifier.")]
    [JsonPropertyName("patient_id")]
    public string? PatientId { get; init; } = "ANON-PATIENT-001";

    /// <summary>
    /// Raw FASTA or nucleotide sequence string.
    /// </summary>
    [Description("Raw FASTA or nucleotide sequence string.")]
    [JsonPropertyName("sequence")]
    public string? Sequence { get; init; } = string.Empty;

    /// <summary>
    /// Active therapeutic regimen.
    /// </summary>
    [Description("Active therapeutic regimen.")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;
}

/// <summary>
/// Request payload for de-identifying a patient identity and FASTA payload.
/// </summary>
[SwaggerSchema(Title = "DeIdentifyRequest", Description = "Request payload for HIPAA Safe Harbor de-identification and GINA/GDPR consent confirmation.")]
public sealed record DeIdentifyRequest
{
    /// <summary>
    /// Raw patient identifier or name.
    /// </summary>
    [Description("Raw patient identifier or name.")]
    [JsonPropertyName("raw_subject_identifier")]
    public string RawSubjectIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Raw genomic sequence or SNP profile string.
    /// </summary>
    [Description("Raw genomic sequence or SNP profile string.")]
    [JsonPropertyName("raw_genomic_data")]
    public string? RawGenomicData { get; init; }

    /// <summary>
    /// Confirmation of explicit subject consent under GDPR Article 9.
    /// </summary>
    [Description("Confirmation of explicit subject consent under GDPR Article 9.")]
    [JsonPropertyName("gdpr_consent_confirmed")]
    public bool GdprConsentConfirmed { get; init; } = true;

    /// <summary>
    /// Confirmation that data use is purpose-limited to therapeutic nanocarrier design under GINA.
    /// </summary>
    [Description("Confirmation that data use is purpose-limited to therapeutic nanocarrier design under GINA.")]
    [JsonPropertyName("gina_purpose_limited")]
    public bool GinaPurposeLimited { get; init; } = true;
}

/// <summary>
/// Request payload for crypto-shredding an individual subject's decryption keys.
/// </summary>
[SwaggerSchema(Title = "CryptoShredRequest", Description = "Request payload for executing GDPR Article 17 right-to-erasure via key zeroization.")]
public sealed record CryptoShredRequest
{
    /// <summary>
    /// Pseudonymized subject identifier to crypto-shred.
    /// </summary>
    [Description("Pseudonymized subject identifier to crypto-shred.")]
    [JsonPropertyName("subject_id")]
    public string SubjectId { get; init; } = string.Empty;
}

/// <summary>
/// Request payload for envelope-encrypting an arbitrary JSON object under a subject's post-quantum key.
/// </summary>
[SwaggerSchema(Title = "EncryptRecordRequest", Description = "Request payload for envelope-encrypting arbitrary health payloads with ML-KEM-768 + AES-256-GCM.")]
public sealed record EncryptRecordRequest
{
    /// <summary>
    /// Pseudonymized subject identifier.
    /// </summary>
    [Description("Pseudonymized subject identifier.")]
    [JsonPropertyName("subject_id")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Arbitrary payload object or JSON element.
    /// </summary>
    [Description("Arbitrary payload object or JSON element.")]
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }
}

/// <summary>
/// Request payload for decrypting an envelope-encrypted record.
/// </summary>
[SwaggerSchema(Title = "DecryptRecordRequest", Description = "Request payload for decrypting an authenticated post-quantum envelope.")]
public sealed record DecryptRecordRequest
{
    /// <summary>
    /// Pseudonymized subject identifier.
    /// </summary>
    [Description("Pseudonymized subject identifier.")]
    [JsonPropertyName("subject_id")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Encrypted envelope container.
    /// </summary>
    [Description("Encrypted envelope container.")]
    [JsonPropertyName("envelope")]
    public EncryptedEnvelope? Envelope { get; init; }
}

/// <summary>
/// Request payload for exporting an envelope-encrypted, post-quantum signed dataset bundle.
/// </summary>
[SwaggerSchema(Title = "ExportEncryptedDatasetRequest", Description = "Request payload for exporting a trajectory archive envelope-encrypted with ML-KEM-768 and signed with ML-DSA-65.")]
public sealed record ExportEncryptedDatasetRequest
{
    /// <summary>
    /// Source trajectory filename in shared storage.
    /// </summary>
    [Description("Source trajectory filename in shared storage.")]
    [JsonPropertyName("trajectory_file_name")]
    public string TrajectoryFileName { get; init; } = string.Empty;

    /// <summary>
    /// Target pseudonymized subject identifier.
    /// </summary>
    [Description("Target pseudonymized subject identifier.")]
    [JsonPropertyName("subject_id")]
    public string? SubjectId { get; init; }

    /// <summary>
    /// Destination export directory.
    /// </summary>
    [Description("Destination export directory.")]
    [JsonPropertyName("export_directory")]
    public string? ExportDirectory { get; init; }
}
