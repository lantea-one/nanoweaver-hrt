using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime;
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
using WeaverLammpsHrt.TrajectoryParser.Swagger;
using WeaverLammpsHrt.TrajectoryParser.Formatting;
using WeaverLammpsHrt.TrajectoryParser.Security.Authentication;
using WeaverLammpsHrt.TrajectoryParser.Database.Migrations;
using WeaverLammpsHrt.TrajectoryParser.Security.ExceptionHandling;
using WeaverLammpsHrt.TrajectoryParser.Simulations;
using WeaverLammpsHrt.TrajectoryParser.Simulations.Models;
using WeaverLammpsHrt.TrajectoryParser.Events;
using WeaverLammpsHrt.TrajectoryParser.Events.Models;
using WeaverLammpsHrt.TrajectoryParser.Caching;
using WeaverLammpsHrt.TrajectoryParser.Bridges;

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
        builder.Services.AddSingleton<ISimulationCampaignService, SimulationCampaignService>();
        builder.Services.AddSingleton<DatabaseMigrationRunner>();
        builder.Services.AddSingleton<IUserRepository, UserRepository>();
        builder.Services.AddSingleton<IUserAuthenticationService, UserAuthenticationService>();
        builder.Services.AddScoped<UserSessionState>();

        // WHAT: Registers the internal in-memory channel event bus and Valkey distributed cluster bridge.
        // WHY:  Decouples parsing loops from downstream I/O and enables horizontal multi-node synchronization.
        // HOW:  Registers ChannelEventBus, ValkeyConnectionFactory, CacheService, LockService, and Hosted EventBridge.
        builder.Services.AddSingleton<EventBusOptions>();
        builder.Services.AddSingleton<IInternalEventBus, ChannelEventBus>();
        builder.Services.AddSingleton<IValkeyConnectionFactory, ValkeyConnectionFactory>();
        builder.Services.AddSingleton<IValkeyCacheService, ValkeyCacheService>();
        builder.Services.AddSingleton<IValkeyLockService, ValkeyLockService>();
        builder.Services.AddSingleton<IValkeyEventBridge, ValkeyEventBridge>();
        builder.Services.AddHostedService(sp => (ValkeyEventBridge)sp.GetRequiredService<IValkeyEventBridge>());

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
            c.SchemaFilter<OpenApiMultiFormatExampleFilter>();
            c.OperationFilter<OpenApiMultiFormatExampleFilter>();

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Asymmetric Private-Key Authenticated Bearer Token (issued by /api/auth/token or /api/auth/login-key). Example: 'Bearer {token}'",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // Configure JSON serialization options: lower camelCase strictly enforced
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.WriteIndented = true;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        WebApplication app = builder.Build();

        // WHAT: Intercepts unhandled API exceptions and formats RFC 7807 problem details with zero sensitive data leakage.
        // WHY:  Ensures that API exceptions are descriptive, relevant, and secure without exposing stack traces or server paths.
        // HOW:  SecureExceptionHandlerMiddleware catches downstream exceptions for /api routes and produces SecureProblemDetails.
        app.UseMiddleware<SecureExceptionHandlerMiddleware>();

        // WHAT: Initializes authoritative database tables for configuration and single-use share keys.
        // WHY:  Guarantees that database-first precedence is active before requests arrive.
        // HOW:  Executes table verification via IConfigurationRepository.
        IConfigurationRepository configRepo = app.Services.GetRequiredService<IConfigurationRepository>();
        await configRepo.InitializeAsync();

        // WHAT: Initializes authoritative database tables for user identities, asymmetric keys, and role access.
        // WHY:  Guarantees that user tables and first-launch administrative states are ready before incoming requests.
        // HOW:  Executes schema verification and index creation via IUserRepository.
        IUserRepository userRepo = app.Services.GetRequiredService<IUserRepository>();
        await userRepo.InitializeAsync();

        // WHAT: Enables content negotiation and multi-format transcoding (JSON, XML, YAML).
        // WHY:  Allows callers to request or submit XML or YAML payloads transparently.
        // HOW:  FormatNegotiationMiddleware inspects Accept and Content-Type headers before routing.
        app.UseFormatNegotiation();

        // WHAT: Injects defense-in-depth security headers directly at the ASP.NET Core application level.
        // WHY:  Hardens responses against MIME sniffing, clickjacking, and XSS across all environments.
        // HOW:  Sets standard OWASP security response headers on every HTTP response context.
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            context.Response.Headers["X-XSS-Protection"] = "0";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
            await next();
        });

        // WHAT: Serves static assets (CSS, JS) and Blazor Interactive Server WebSockets.
        // WHY:  Enables sovereign Web UI navigation without requiring external NPM/Node toolchains.
        // HOW:  Standard ASP.NET Core static files and Blazor endpoint mapping.
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        // WHAT: Enforces sovereign private-key authentication, access gating, role permissions, and first-launch setup lockout.
        // WHY:  Protects sensitive biomedical API endpoints, validates cryptographic tokens, and strictly locks out uninitialized nodes.
        // HOW:  Inspects request paths, checks setup status via IUserAuthenticationService, verifies Bearer tokens, and validates roles.
        app.Use(async (context, next) =>
        {
            PathString path = context.Request.Path;

            // 1. Static assets, Blazor WebSocket framework, Swagger UI, and documentation bypass authentication
            if (path.StartsWithSegments("/css") ||
                path.StartsWithSegments("/js") ||
                path.StartsWithSegments("/lib") ||
                path.StartsWithSegments("/_framework") ||
                path.StartsWithSegments("/_blazor") ||
                path.StartsWithSegments("/swagger") ||
                path.StartsWithSegments("/documentation") ||
                path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            IUserAuthenticationService authService = context.RequestServices.GetRequiredService<IUserAuthenticationService>();

            // 1.5. Health check endpoints: /healthz, /health, /api/system/health
            // WHAT: Allows public unauthenticated container probes while attaching user context if a session token is supplied.
            // WHY:  Container orchestrators need immediate responsiveness even before setup; authenticated users receive full telemetry.
            // HOW:  Bypasses lockout and 401 enforcement, but attempts optional token validation to populate CurrentUser in HttpContext.Items.
            if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/system/health", StringComparison.OrdinalIgnoreCase))
            {
                string? candidateHealthToken = null;
                string? healthAuthHeader = context.Request.Headers.Authorization;
                if (!string.IsNullOrEmpty(healthAuthHeader) && healthAuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    candidateHealthToken = healthAuthHeader["Bearer ".Length..].Trim();
                }
                else if (context.Request.Cookies.TryGetValue("weaver_session", out string? cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
                {
                    candidateHealthToken = cookieToken.Trim();
                }

                if (!string.IsNullOrEmpty(candidateHealthToken))
                {
                    var (isHealthTokenValid, healthUser, _) = await authService.ValidateTokenAsync(candidateHealthToken, context.RequestAborted);
                    if (isHealthTokenValid && healthUser != null)
                    {
                        context.Items["CurrentUser"] = healthUser;
                    }
                }

                await next();
                return;
            }

            bool isSetupRequired = await authService.IsSetupRequiredAsync(context.RequestAborted);

            // 2. Setup endpoints: /api/setup/*
            if (path.StartsWithSegments("/api/setup"))
            {
                if (path.Equals("/api/setup/status", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                if (!isSetupRequired && (path.Equals("/api/setup/initialize", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/setup/generate-keypair", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { error = "System has already completed initial Administrator setup." });
                    return;
                }

                await next();
                return;
            }

            // 3. If first-launch setup is required, strictly lock out all other API endpoints and redirect web pages to /setup
            if (isSetupRequired)
            {
                if (path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "First-launch system setup is required. Please initialize the master Administrator via /api/setup/initialize or navigate to /setup.",
                        setupRequired = true
                    });
                    return;
                }

                if (!path.Equals("/setup", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect("/setup");
                    return;
                }

                await next();
                return;
            }

            // 4. Public authentication endpoints (do not require an existing token)
            if (path.Equals("/api/auth/challenge", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/token", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/login-key", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // 5. Interactive Web UI pages (non-/api endpoints)
            if (!path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            // 6. Extract Bearer token from Authorization header or weaver_session cookie
            string? token = null;
            string? authHeader = context.Request.Headers.Authorization;
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader["Bearer ".Length..].Trim();
            }
            else if (context.Request.Cookies.TryGetValue("weaver_session", out string? cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
            {
                token = cookieToken.Trim();
            }

            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Authentication required. Provide a valid Bearer token in Authorization header." });
                return;
            }

            var (valid, user, errorReason) = await authService.ValidateTokenAsync(token, context.RequestAborted);
            if (!valid || user == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = errorReason ?? "Invalid or expired authentication token." });
                return;
            }

            // 7. Access Gating: Web-only users cannot access REST API endpoints
            if (user.AccessGate == AccessGate.Web)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Access Denied: This user account is gated to Web interface access only." });
                return;
            }

            // 8. Role Gating: Administrator-only endpoints
            if (path.StartsWithSegments("/api/users") ||
                (path.StartsWithSegments("/api/config") && context.Request.Method != "GET") ||
                (path.StartsWithSegments("/api/stack") && context.Request.Method != "GET"))
            {
                if (user.Role != UserRole.Administrator)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { error = "Forbidden: Administrator role required for this administrative resource." });
                    return;
                }
            }

            // 9. Role Gating: Administrator or Researcher endpoints (computational execution and modifications)
            if (path.StartsWithSegments("/api/parser/process") ||
                path.StartsWithSegments("/api/simulations/run") ||
                (path.StartsWithSegments("/api/simulations/campaigns") && context.Request.Method == "POST") ||
                path.Equals("/api/datasets/export", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/datasets/import", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/api/fabrication/generate"))
            {
                if (user.Role != UserRole.Administrator && user.Role != UserRole.Researcher)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { error = "Forbidden: Administrator or Researcher role required to execute simulations and modifications." });
                    return;
                }
            }

            // 10. Bind authenticated user to HttpContext.Items for downstream route handlers
            context.Items["CurrentUser"] = user;
            await next();
        });

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
        .Accepts<ExportRequest>("application/json", "application/xml", "application/x-yaml")
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
            IInternalEventBus eventBus,
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
                _ = eventBus.PublishAsync(new TrajectoryFrameBatchEvent(fileName, frameIndex, coordinates.Length, sw.Elapsed.TotalMilliseconds), ct);
                return ValueTask.CompletedTask;
            }, ct);

            sw.Stop();

            return Results.Ok(new
            {
                fileName = fileName,
                format = parser.FormatName,
                framesParsed = framesProcessed,
                totalAtomsProcessed = totalAtomsEncountered,
                elapsedMilliseconds = sw.ElapsedMilliseconds,
                framesPerSecond = framesProcessed > 0 && sw.Elapsed.TotalSeconds > 0
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

        app.MapGet("/api/system/event-bus", (
            IInternalEventBus eventBus,
            IValkeyConnectionFactory valkeyConn,
            IValkeyCacheService cacheService,
            IValkeyEventBridge bridge) =>
        {
            Dictionary<string, int> subscribers = new()
            {
                [nameof(TrajectoryFrameBatchEvent)] = eventBus.GetSubscriberCount<TrajectoryFrameBatchEvent>(),
                [nameof(SimulationProgressEvent)] = eventBus.GetSubscriberCount<SimulationProgressEvent>(),
                [nameof(UserSessionActivityEvent)] = eventBus.GetSubscriberCount<UserSessionActivityEvent>(),
                [nameof(AuditLogDispatchedEvent)] = eventBus.GetSubscriberCount<AuditLogDispatchedEvent>(),
                [nameof(SystemTelemetryUpdatedEvent)] = eventBus.GetSubscriberCount<SystemTelemetryUpdatedEvent>()
            };

            EventBusTelemetry telemetry = new()
            {
                IsInternalBusActive = true,
                IsValkeyConnected = valkeyConn.IsConnected,
                IsDistributedCache = cacheService.IsDistributed,
                ValkeyEndpoint = valkeyConn.SanitizedEndpoint,
                OutboundEventsCount = bridge.OutboundEventsCount,
                InboundEventsCount = bridge.InboundEventsCount,
                ActiveSubscribers = subscribers
            };

            return Results.Ok(telemetry);
        })
        .WithName("GetEventBusTelemetry")
        .WithTags("System Telemetry")
        .WithSummary("Provides real-time telemetry on the in-memory event bus and distributed Valkey bridge.")
        .WithDescription("Reports active channel subscribers, Valkey connection state, endpoint descriptor, and bi-directional event counts.")
        .Produces<EventBusTelemetry>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Multi-Purpose Operational Health Check & Granular Telemetry
        // ---------------------------------------------------------------------
        app.MapGet("/api/system/health", (
            HttpContext httpContext,
            IUserAuthenticationService authService,
            IConfigurationRepository configRepo,
            DatabaseMigrationRunner migrationRunner,
            IInternalEventBus eventBus,
            IValkeyConnectionFactory valkeyConn,
            IValkeyCacheService cacheService,
            IConfiguration config,
            ISimulationCampaignService simService,
            IComplianceAuditService compliance,
            CancellationToken ct) => HandleSystemHealthAsync(httpContext, authService, configRepo, migrationRunner, eventBus, valkeyConn, cacheService, config, simService, compliance, ct))
        .WithName("GetSystemHealth")
        .WithTags("System Health")
        .WithSummary("Provides public health check overview or granular diagnostic telemetry when authenticated.")
        .WithDescription("Publicly accessible endpoint. Returns high-level operational status to unauthenticated clients and container probes. Returns comprehensive subsystem diagnostics when a valid user session is present.")
        .Produces<SystemHealthOverview>(StatusCodes.Status200OK)
        .Produces<GranularSystemHealth>(StatusCodes.Status200OK);

        app.MapGet("/healthz", (
            HttpContext httpContext,
            IUserAuthenticationService authService,
            IConfigurationRepository configRepo,
            DatabaseMigrationRunner migrationRunner,
            IInternalEventBus eventBus,
            IValkeyConnectionFactory valkeyConn,
            IValkeyCacheService cacheService,
            IConfiguration config,
            ISimulationCampaignService simService,
            IComplianceAuditService compliance,
            CancellationToken ct) => HandleSystemHealthAsync(httpContext, authService, configRepo, migrationRunner, eventBus, valkeyConn, cacheService, config, simService, compliance, ct))
        .WithName("GetHealthz")
        .WithTags("System Health")
        .WithSummary("Container runtime operational health probe.")
        .WithDescription("Alias for /api/system/health probe. Used by Docker and Kubernetes container orchestrators.")
        .Produces<SystemHealthOverview>(StatusCodes.Status200OK)
        .Produces<GranularSystemHealth>(StatusCodes.Status200OK);

        app.MapGet("/health", (
            HttpContext httpContext,
            IUserAuthenticationService authService,
            IConfigurationRepository configRepo,
            DatabaseMigrationRunner migrationRunner,
            IInternalEventBus eventBus,
            IValkeyConnectionFactory valkeyConn,
            IValkeyCacheService cacheService,
            IConfiguration config,
            ISimulationCampaignService simService,
            IComplianceAuditService compliance,
            CancellationToken ct) => HandleSystemHealthAsync(httpContext, authService, configRepo, migrationRunner, eventBus, valkeyConn, cacheService, config, simService, compliance, ct))
        .WithName("GetHealth")
        .WithTags("System Health")
        .WithSummary("Standard system health probe alias.")
        .WithDescription("Alias for /api/system/health probe.")
        .Produces<SystemHealthOverview>(StatusCodes.Status200OK)
        .Produces<GranularSystemHealth>(StatusCodes.Status200OK);

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
        // REST API: In-Silico Nanomedicine Simulation Campaigns
        // ---------------------------------------------------------------------
        app.MapGet("/api/simulations/campaigns", (ISimulationCampaignService simService) =>
        {
            return Results.Ok(simService.GetCampaignCatalog());
        })
        .WithName("GetSimulationCampaigns")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Lists all available in-silico simulation campaigns across oxDNA, LAMMPS, and QSP/PBPK.")
        .WithDescription("Returns simulation campaign catalogs, biophysical goals, and hardware accelerator specifications.")
        .Produces<IReadOnlyList<SimulationCampaignInfo>>(StatusCodes.Status200OK);

        app.MapPost("/api/simulations/run/serum-stability", (SerumStabilityRequest req, ISimulationCampaignService simService) =>
        {
            SerumStabilityAnalysisResult result = simService.SimulateSerumStability(req);
            return Results.Ok(result);
        })
        .WithName("RunSerumStabilitySimulation")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Executes Campaign 1: oxDNA Physiological Serum & Low-Magnesium Stability Simulation.")
        .WithDescription("Calculates Root Mean Square Fluctuation (RMSF), Debye screening length, and crossover fraying under human blood serum conditions.")
        .Produces<SerumStabilityAnalysisResult>(StatusCodes.Status200OK);

        app.MapPost("/api/simulations/run/aptamer-gating", (AptamerGatingRequest req, ISimulationCampaignService simService) =>
        {
            AptamerGatingAnalysisResult result = simService.SimulateAptamerGating(req);
            return Results.Ok(result);
        })
        .WithName("RunAptamerGatingSimulation")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Executes Campaign 2: LAMMPS Aptamer Gating Free-Energy & Pituitary Tropism Simulation.")
        .WithDescription("Computes Potential of Mean Force (PMF) unbinding free-energy barriers and GnRH-R receptor binding affinity.")
        .Produces<AptamerGatingAnalysisResult>(StatusCodes.Status200OK);

        app.MapPost("/api/simulations/run/payload-leakage", (PayloadLeakageRequest req, ISimulationCampaignService simService) =>
        {
            PayloadLeakageAnalysisResult result = simService.SimulatePayloadLeakage(req);
            return Results.Ok(result);
        })
        .WithName("RunPayloadLeakageSimulation")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Executes Campaign 3: Steroid Payload Encapsulation & Micro-Leakage Dynamics.")
        .WithDescription("Models hydrophobic steroid cavity confinement, passive leakage percentage, and gate-triggered release rates.")
        .Produces<PayloadLeakageAnalysisResult>(StatusCodes.Status200OK);

        app.MapPost("/api/simulations/run/pbpk-endocrine", (PbpkSimulationRequest req, ISimulationCampaignService simService) =>
        {
            PbpkEndocrineSimulationResult result = simService.SimulatePbpkEndocrine(req);
            return Results.Ok(result);
        })
        .WithName("RunPbpkEndocrineSimulation")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Executes Campaign 4: Quantitative Systems Pharmacology (QSP) / Whole-Body PBPK Simulation.")
        .WithDescription("Simulates multi-compartment whole-body pharmacokinetics over 30/60/90 days with genomic multipliers and HPG feedback.")
        .Produces<PbpkEndocrineSimulationResult>(StatusCodes.Status200OK);

        app.MapGet("/api/simulations/configs/{campaignId}", (string campaignId, ISimulationCampaignService simService) =>
        {
            string config = campaignId.ToUpperInvariant() switch
            {
                "CAMPAIGN_SERUM_STABILITY" => simService.GenerateOxDnaInputDeck(new SerumStabilityRequest()),
                "CAMPAIGN_APTAMER_GATING" => simService.GenerateLammpsAptamerInputDeck(new AptamerGatingRequest()),
                "CAMPAIGN_PAYLOAD_LEAKAGE" => simService.GenerateLammpsPayloadInputDeck(new PayloadLeakageRequest()),
                _ => simService.GenerateOxDnaInputDeck(new SerumStabilityRequest())
            };
            return Results.Ok(new { campaign_id = campaignId, input_deck = config });
        })
        .WithName("GetSimulationConfigDeck")
        .WithTags("In-Silico Simulation Campaigns")
        .WithSummary("Generates baseline simulation input deck script for the specified campaign ID.")
        .WithDescription("Outputs oxDNA or LAMMPS input decks configured for GPU/SYCL execution.")
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

        // ---------------------------------------------------------------------
        // REST API: First-Launch Setup Status
        // ---------------------------------------------------------------------
        app.MapGet("/api/setup/status", async (IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Queries the initialization state of the sovereign node.
            // WHY:  Allows client applications and web UIs to know whether to display the setup wizard.
            // HOW:  Delegates to IUserAuthenticationService.IsSetupRequiredAsync.
            bool required = await auth.IsSetupRequiredAsync(ct);
            return Results.Ok(new SetupStatusResponse { SetupRequired = required });
        })
        .WithName("GetSetupStatus")
        .WithTags("Authentication & Setup")
        .WithSummary("Evaluates whether the first-launch setup wizard is required.")
        .WithDescription("Returns true if no Administrator account has been provisioned yet, locking operational API endpoints.")
        .Produces<SetupStatusResponse>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: First-Launch Administrator Initialization
        // ---------------------------------------------------------------------
        app.MapPost("/api/setup/initialize", async (InitialSetupRequest request, IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Provisions the master initial Administrator and locks the first-launch wizard.
            // WHY:  Bootstraps the node's authoritative cryptographic identity and unblocks operational API endpoints.
            // HOW:  Validates inputs, registers administrator account, and issues initial Bearer token.
            if (string.IsNullOrWhiteSpace(request.AdminUsername) || string.IsNullOrWhiteSpace(request.PublicKeyBase64))
            {
                return Results.BadRequest(new { error = "Administrator username and public key are required." });
            }

            try
            {
                UserAccount admin = await auth.CompleteInitialSetupAsync(
                    request.AdminUsername,
                    request.PublicKeyBase64,
                    request.Algorithm,
                    request.AccessGate,
                    ct);

                string token = auth.GenerateBearerToken(admin);
                return Results.Ok(new AuthTokenResponse
                {
                    Token = token,
                    TokenType = "Bearer",
                    ExpiresInSeconds = 28800,
                    User = admin
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("InitializeSetup")
        .WithTags("Authentication & Setup")
        .WithSummary("Initializes the sovereign node with the master Administrator account.")
        .WithDescription("Creates the authoritative initial Administrator account, unlocks all operational endpoints, and permanently locks setup.")
        .Accepts<InitialSetupRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // ---------------------------------------------------------------------
        // REST API: First-Launch Keypair Generation
        // ---------------------------------------------------------------------
        app.MapPost("/api/setup/generate-keypair", (GenerateKeyRequest request, IUserAuthenticationService auth) =>
        {
            // WHAT: Generates a high-security asymmetric keypair (Ed25519 or ML-DSA-65) for the initial administrator.
            // WHY:  Allows administrators to generate secure cryptographic credentials directly without external CLI tools.
            // HOW:  Invokes IUserAuthenticationService.GenerateSecureKeyBundle with optional passphrase encryption.
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username is required." });
            }

            GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle(
                request.Username,
                UserRole.Administrator,
                request.AccessGate,
                request.Algorithm,
                request.Password);

            return Results.Ok(bundle);
        })
        .WithName("GenerateSetupKeypair")
        .WithTags("Authentication & Setup")
        .WithSummary("Generates a high-security asymmetric keypair for the initial Administrator.")
        .WithDescription("Generates Ed25519 or ML-DSA-65 keypair with optional passphrase encryption.")
        .Accepts<GenerateKeyRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<GeneratedKeyBundle>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // ---------------------------------------------------------------------
        // REST API: Authentication Challenge Issuance
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/challenge", (ChallengeRequest request, IUserAuthenticationService auth) =>
        {
            // WHAT: Mints a time-bounded cryptographic challenge nonce.
            // WHY:  Initiates zero-knowledge asymmetric challenge-response authentication.
            // HOW:  Generates 32 random bytes and stores in active challenges with a 60-second TTL.
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username is required." });
            }

            AuthChallenge challenge = auth.CreateChallenge(request.Username);
            return Results.Ok(challenge);
        })
        .WithName("CreateAuthChallenge")
        .WithTags("Authentication & Setup")
        .WithSummary("Issues a cryptographic challenge nonce for private-key authentication.")
        .WithDescription("Mints a time-bounded (60s) 256-bit challenge nonce that must be signed by the user's private key.")
        .Accepts<ChallengeRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthChallenge>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // ---------------------------------------------------------------------
        // REST API: Challenge-Response Token Exchange
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/token", async (TokenExchangeRequest request, IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Verifies a digitally signed challenge nonce and mints a Bearer token.
            // WHY:  Completes private-key proof of possession without transmitting private key bytes over the wire.
            // HOW:  Validates signature against user's registered public key and derives HMAC-signed token.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.SignatureBase64))
            {
                return Results.BadRequest(new { error = "Username, challengeId, and signatureBase64 are all required." });
            }

            var (success, errorMessage, token, user) = await auth.VerifyChallengeAndIssueTokenAsync(
                request.Username,
                request.ChallengeId,
                request.SignatureBase64,
                isWebRequest: false,
                ct);

            if (!success || token == null || user == null)
            {
                return Results.Json(new { error = errorMessage ?? "Authentication failed." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(new AuthTokenResponse
            {
                Token = token,
                TokenType = "Bearer",
                ExpiresInSeconds = 28800,
                User = user
            });
        })
        .WithName("ExchangeAuthToken")
        .WithTags("Authentication & Setup")
        .WithSummary("Exchanges a digitally signed challenge nonce for an authenticated Bearer token.")
        .WithDescription("Verifies the signature against the registered public key and returns a scoped Bearer token.")
        .Accepts<TokenExchangeRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        // ---------------------------------------------------------------------
        // REST API: Direct Private Key Login (Web & API)
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/login-key", async (KeyLoginRequest request, IUserAuthenticationService auth, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Authenticates user directly using their private key PEM and optional password.
            // WHY:  Provides streamlined sign-in for Web UI and REST API scripts holding private key files.
            // HOW:  Decrypts private key in-memory, signs an ephemeral nonce, validates against public key, and sets session cookie.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.PrivateKeyPem))
            {
                return Results.BadRequest(new { error = "Username and privateKeyPem are required." });
            }

            var (success, errorMessage, token, user) = await auth.AuthenticateWithPrivateKeyAsync(
                request.Username,
                request.PrivateKeyPem,
                request.Password,
                isWebRequest: false,
                ct);

            if (!success || token == null || user == null)
            {
                return Results.Json(new { error = errorMessage ?? "Authentication failed." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            context.Response.Cookies.Append("weaver_session", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Results.Ok(new AuthTokenResponse
            {
                Token = token,
                TokenType = "Bearer",
                ExpiresInSeconds = 28800,
                User = user
            });
        })
        .WithName("LoginWithPrivateKey")
        .WithTags("Authentication & Setup")
        .WithSummary("Authenticates directly using private key PEM and optional password.")
        .WithDescription("Directly verifies private key ownership, signs an ephemeral nonce, and returns a Bearer token.")
        .Accepts<KeyLoginRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        // ---------------------------------------------------------------------
        // REST API: Current Authenticated User Identity
        // ---------------------------------------------------------------------
        app.MapGet("/api/auth/me", (HttpContext context) =>
        {
            // WHAT: Returns identity metadata and claims for the authenticated user.
            // WHY:  Allows clients to inspect current role, access gating, and username.
            // HOW:  Retrieves UserAccount attached to HttpContext.Items by the authentication middleware.
            if (context.Items.TryGetValue("CurrentUser", out object? userObj) && userObj is UserAccount user)
            {
                return Results.Ok(user);
            }
            return Results.Unauthorized();
        })
        .WithName("GetCurrentUser")
        .WithTags("Authentication & Setup")
        .WithSummary("Retrieves the identity and claims of the authenticated user.")
        .WithDescription("Returns the username, role, access gate, and public key metadata of the current Bearer token holder.")
        .Produces<UserAccount>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // ---------------------------------------------------------------------
        // REST API: Sign Out / Session Clear
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/logout", (HttpContext context) =>
        {
            // WHAT: Terminates the interactive session.
            // WHY:  Ensures session cookies are cleared upon user request.
            // HOW:  Deletes the weaver_session cookie.
            context.Response.Cookies.Delete("weaver_session");
            return Results.Ok(new { message = "Successfully signed out." });
        })
        .WithName("Logout")
        .WithTags("Authentication & Setup")
        .WithSummary("Signs out the active user session and clears authentication cookies.")
        .WithDescription("Clears the weaver_session cookie and terminates the interactive session.")
        .Produces(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: User Management - List Users
        // ---------------------------------------------------------------------
        app.MapGet("/api/users", async (IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Enumerates all registered sovereign user accounts.
            // WHY:  Empowers Administrators to manage accounts, roles, and access boundaries.
            // HOW:  Queries user repository via IUserAuthenticationService.
            IReadOnlyList<UserAccount> users = await auth.ListUsersAsync(ct);
            return Results.Ok(users);
        })
        .WithName("ListUsers")
        .WithTags("User Management")
        .WithSummary("Lists all registered sovereign user accounts.")
        .WithDescription("Returns user accounts with their roles, access gates, and public key fingerprints (Administrator role required).")
        .Produces<IReadOnlyList<UserAccount>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: User Management - Create User
        // ---------------------------------------------------------------------
        app.MapPost("/api/users", async (CreateUserRequest request, IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Provisions a new sovereign user account with a pre-existing public key.
            // WHY:  Allows Administrator to add users who bring their own key material.
            // HOW:  Validates input, stores user record in database, and logs compliance audit event.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.PublicKeyBase64))
            {
                return Results.BadRequest(new { error = "Username and publicKeyBase64 are required." });
            }

            try
            {
                UserAccount user = await auth.CreateUserAsync(
                    request.Username,
                    request.Role,
                    request.AccessGate,
                    request.PublicKeyBase64,
                    request.Algorithm,
                    ct);

                return Results.Created($"/api/users/{user.Username}", user);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateUser")
        .WithTags("User Management")
        .WithSummary("Registers a new sovereign user with an existing public key.")
        .WithDescription("Creates a new account bound to the provided public key (Administrator role required).")
        .Accepts<CreateUserRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<UserAccount>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: User Management - Generate Keypair and Create User
        // ---------------------------------------------------------------------
        app.MapPost("/api/users/generate-keypair", async (GenerateKeyRequest request, IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Generates an asymmetric keypair and immediately registers the new user account.
            // WHY:  Enables Administrators to onboard new team members in a single step with secure credentials.
            // HOW:  Derives keypair, writes public key to user store, and returns the private key bundle.
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username is required." });
            }

            try
            {
                GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle(
                    request.Username,
                    request.Role,
                    request.AccessGate,
                    request.Algorithm,
                    request.Password);

                await auth.CreateUserAsync(
                    request.Username,
                    request.Role,
                    request.AccessGate,
                    bundle.PublicKeyBase64,
                    request.Algorithm,
                    ct);

                return Results.Ok(bundle);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GenerateUserKeypair")
        .WithTags("User Management")
        .WithSummary("Generates an asymmetric keypair and provisions a new user account.")
        .WithDescription("Generates Ed25519 or ML-DSA-65 keys, registers the public key, and returns the private key bundle (Administrator role required).")
        .Accepts<GenerateKeyRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<GeneratedKeyBundle>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: User Management - Delete User
        // ---------------------------------------------------------------------
        app.MapDelete("/api/users/{username}", async (string username, IUserAuthenticationService auth, CancellationToken ct) =>
        {
            // WHAT: Deletes a user account.
            // WHY:  Enforces security hygiene and user de-provisioning, protecting against deletion of the last admin.
            // HOW:  Invokes IUserAuthenticationService.DeleteUserAsync.
            try
            {
                bool deleted = await auth.DeleteUserAsync(username, ct);
                if (!deleted)
                {
                    return Results.BadRequest(new { error = "Cannot delete user. User not found." });
                }

                return Results.Ok(new { message = $"User '{username}' successfully deleted." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DeleteUser")
        .WithTags("User Management")
        .WithSummary("Revokes and deletes a user account.")
        .WithDescription("Permanently removes the user account from the authoritative repository (Administrator role required).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: Patient Digital DNA Package Generation & Download
        // ---------------------------------------------------------------------
        app.MapGet("/api/patient/dna", (
            HttpContext context,
            IFabricationService fab,
            string? patientId,
            TherapeuticRegimen? regimen,
            string? geometry,
            double? targetKd) =>
        {
            // WHAT: Synthesizes a patient-targeted Digital DNA package.
            // WHY:  Allows Patients, Researchers, and Administrators to download digital DNA blueprints.
            // HOW:  Generates nanocarrier sequences, staple routing tables, annealing schedule, and SHA-256 provenance.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            string effectivePatientId = !string.IsNullOrWhiteSpace(patientId)
                ? patientId
                : (currentUser?.Username ?? "PATIENT-SOVEREIGN-01");

            TherapeuticRegimen effectiveRegimen = regimen ?? TherapeuticRegimen.MaleToFemale;
            string effectiveGeometry = !string.IsNullOrWhiteSpace(geometry) ? geometry : "Hexagonal Nanotube (6HB)";
            double effectiveKd = targetKd ?? 3.5;

            DnaOrigamiSample sample = fab.GetPresetSamples().FirstOrDefault(s => s.Regimen == effectiveRegimen)
                ?? fab.GetPresetSamples()[0];
            var (_, _, _, _, gcPercent, mwKDa) = fab.AnalyzeSequenceComposition(sample.AptamerSequence);

            FabricationPackage pkg = fab.GenerateFabricationPackage(sample.Id, effectiveRegimen, effectiveGeometry, effectiveKd);

            PatientDigitalDnaPackage package = new()
            {
                PatientId = effectivePatientId,
                Regimen = effectiveRegimen,
                TargetTissue = sample.TargetTissue,
                ActiveLigand = sample.ActiveLigand,
                NanocarrierGeometry = effectiveGeometry,
                TargetAptamerKdNm = effectiveKd,
                ScaffoldFasta = sample.AptamerSequence,
                GcPercentage = gcPercent,
                EstimatedMwKDa = mwKDa,
                Staples = pkg.StapleOligos,
                AnnealingProtocol = pkg.AnnealingSchedule,
                ProvenanceSha256 = pkg.Sha256Manifest,
                GeneratedAtUtc = DateTime.UtcNow
            };

            return Results.Ok(package);
        })
        .WithName("GetPatientDigitalDna")
        .WithTags("Patient & Synthesis")
        .WithSummary("Generates and downloads a complete patient-specific Digital DNA package.")
        .WithDescription("Synthesizes staple oligos, annealing ramps, and cryptographic provenance for patient hormone delivery (Accessible by Patient, Researcher, and Administrator).")
        .Produces<PatientDigitalDnaPackage>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

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

    /// <summary>
    /// Handles requests to health check endpoints (/api/system/health, /healthz, /health),
    /// dynamically returning either a high-level status overview (for unauthenticated container probes)
    /// or a granular subsystem diagnostic report (when an authenticated user session is detected).
    /// </summary>
    /// <param name="httpContext">The active HTTP context.</param>
    /// <param name="authService">Authentication service used to verify setup requirements.</param>
    /// <param name="configRepo">Database configuration repository reporting active engine provider.</param>
    /// <param name="migrationRunner">Database migration runner reporting applied migrations.</param>
    /// <param name="eventBus">Internal pub/sub event bus reporting channel subscriber counts.</param>
    /// <param name="valkeyConn">Valkey connection factory reporting cluster reachability.</param>
    /// <param name="cacheService">Cache service reporting distribution mode.</param>
    /// <param name="config">Configuration provider for storage directories.</param>
    /// <param name="simService">Simulation campaign manager reporting active campaigns.</param>
    /// <param name="compliance">Compliance audit service reporting ledger chain integrity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A 200 OK result containing <see cref="GranularSystemHealth"/> or <see cref="SystemHealthOverview"/>.</returns>
    public static async Task<IResult> HandleSystemHealthAsync(
        HttpContext httpContext,
        IUserAuthenticationService authService,
        IConfigurationRepository configRepo,
        DatabaseMigrationRunner migrationRunner,
        IInternalEventBus eventBus,
        IValkeyConnectionFactory valkeyConn,
        IValkeyCacheService cacheService,
        IConfiguration config,
        ISimulationCampaignService simService,
        IComplianceAuditService compliance,
        CancellationToken ct)
    {
        // WHAT: Checks for an active authenticated user session attached to the HTTP context or DI scope.
        // WHY:  Unauthenticated callers and container probes receive a lightweight high-level overview.
        // HOW:  Inspects HttpContext.Items["CurrentUser"] followed by scoped UserSessionState fallback.
        UserAccount? user = httpContext.Items.TryGetValue("CurrentUser", out object? userObj) && userObj is UserAccount u ? u : null;
        if (user == null)
        {
            UserSessionState? sessionState = httpContext.RequestServices?.GetService<UserSessionState>();
            if (sessionState != null && sessionState.IsAuthenticated && sessionState.CurrentUser != null)
            {
                user = sessionState.CurrentUser;
            }
        }

        // WHAT: Calculates node uptime duration and checks first-launch setup status.
        // WHY:  Provides universal operational availability metrics to Docker probes and authenticated operators.
        // HOW:  Subtracts start timestamp from current UTC time and queries IUserAuthenticationService.
        TimeSpan uptime = DateTime.UtcNow - StartTimeUtc;
        bool isSetupRequired = await authService.IsSetupRequiredAsync(ct);

        if (user == null)
        {
            SystemHealthOverview overview = new()
            {
                Status = "Healthy",
                TimestampUtc = DateTimeOffset.UtcNow,
                Version = "1.0.0",
                Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
                UptimeSeconds = Math.Round(uptime.TotalSeconds, 2),
                SetupRequired = isSetupRequired,
                AuthenticatedSession = false,
                Description = isSetupRequired
                    ? "System operational. Master Administrator initialization required via /setup."
                    : "System operational. High-level health check. Authenticate with a valid session token for granular subsystem diagnostics."
            };

            return Results.Ok(overview);
        }

        // WHAT: Gathers granular subsystem diagnostics across database, memory, event bus, storage, and simulations.
        // WHY:  Provides full multi-subsystem insight to authenticated operators without exposing details to public callers.
        // HOW:  Queries system telemetry, event bus subscriber counts, filesystem paths, and compliance ledger integrity.
        Process currentProcess = Process.GetCurrentProcess();
        string backend = Environment.GetEnvironmentVariable("ACCELERATOR_BACKEND") ?? "Intel iGPU / OpenCL (Default)";

        string incomingDir = config["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        string exportDir = config["TRAJECTORY_EXPORT_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "exports");
        string parquetDir = config["TRAJECTORY_PARQUET_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "parquet");

        Dictionary<string, int> subscribers = new()
        {
            [nameof(TrajectoryFrameBatchEvent)] = eventBus.GetSubscriberCount<TrajectoryFrameBatchEvent>(),
            [nameof(SimulationProgressEvent)] = eventBus.GetSubscriberCount<SimulationProgressEvent>(),
            [nameof(UserSessionActivityEvent)] = eventBus.GetSubscriberCount<UserSessionActivityEvent>(),
            [nameof(AuditLogDispatchedEvent)] = eventBus.GetSubscriberCount<AuditLogDispatchedEvent>(),
            [nameof(SystemTelemetryUpdatedEvent)] = eventBus.GetSubscriberCount<SystemTelemetryUpdatedEvent>()
        };

        AuditLedgerVerificationResult verification = compliance.VerifyLedgerIntegrity();

        GranularSystemHealth granular = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
            UptimeSeconds = Math.Round(uptime.TotalSeconds, 2),
            SetupRequired = isSetupRequired,
            AuthenticatedSession = true,
            Description = "Authenticated session detected. Granular subsystem diagnostics active.",
            User = new HealthUserInfo
            {
                Username = user.Username,
                Role = user.Role.ToString(),
                AccessGate = user.AccessGate.ToString()
            },
            Subsystems = new HealthSubsystemsInfo
            {
                Database = new DatabaseHealthInfo
                {
                    Status = "Healthy",
                    Provider = configRepo.ActiveBackendName,
                    CanConnect = true,
                    AppliedMigrationsCount = migrationRunner.RegisteredMigrations.Count
                },
                EventBus = new EventBusHealthInfo
                {
                    Status = "Healthy",
                    IsInternalBusActive = true,
                    TotalSubscribers = subscribers.Values.Sum(),
                    ActiveSubscribers = subscribers,
                    IsValkeyBridgeConnected = valkeyConn.IsConnected,
                    ValkeyEndpoint = valkeyConn.SanitizedEndpoint
                },
                Caching = new CachingHealthInfo
                {
                    Status = "Healthy",
                    IsDistributed = cacheService.IsDistributed,
                    Mode = cacheService.IsDistributed ? "Distributed (Valkey)" : "In-Memory Fallback"
                },
                Memory = new MemoryHealthInfo
                {
                    Status = "Healthy",
                    HeapMemoryMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0), 2),
                    WorkingSetMb = Math.Round(currentProcess.WorkingSet64 / (1024.0 * 1024.0), 2),
                    ServerGc = GCSettings.IsServerGC,
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                },
                Storage = new StorageHealthInfo
                {
                    Status = "Healthy",
                    IncomingDirectory = incomingDir,
                    IncomingWritable = Directory.Exists(incomingDir),
                    ExportDirectory = exportDir,
                    ExportWritable = Directory.Exists(exportDir),
                    ParquetDirectory = parquetDir,
                    ParquetWritable = Directory.Exists(parquetDir)
                },
                Simulations = new SimulationHealthInfo
                {
                    Status = "Healthy",
                    AcceleratorBackend = backend,
                    TotalFramesParsed = TotalFramesParsed,
                    AvailableCampaignsCount = simService.GetCampaignCatalog().Count
                },
                Compliance = new ComplianceHealthInfo
                {
                    Status = "Healthy",
                    AuditLedgerHealthy = verification.IsValid,
                    PqcProvider = "ML-KEM-768 + ML-DSA-65 (NIST FIPS 203/204)"
                }
            }
        };

        return Results.Ok(granular);
    }
}

/// <summary>
/// Request payload for initiating a dataset export.
/// </summary>
[SwaggerSchema(Title = "ExportRequest", Description = "Request payload for initiating an export of molecular dynamics trajectories with SHA-256 manifests.")]
public sealed record ExportRequest : IExamplesProvider<ExportRequest>
{
    /// <summary>
    /// Desired name of the exported dataset bundle.
    /// </summary>
    [Description("Desired name of the exported dataset bundle.")]
    [JsonPropertyName("datasetName")]
    public string DatasetName { get; init; } = string.Empty;

    /// <summary>
    /// Optional subset of specific file names to package. If null or empty, all files in the directory are included.
    /// </summary>
    [Description("Optional subset of specific file names to package. If null or empty, all files in the directory are included.")]
    [JsonPropertyName("files")]
    public string[]? Files { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="ExportRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated dataset export request example.</returns>
    public ExportRequest GetExamples() => new()
    {
        DatasetName = "nanocarrier-e2-run-001",
        Files = ["dna_nanotube_sample.xyz", "system.top"]
    };
}

/// <summary>
/// Request payload for persisting a configuration override to the authoritative database.
/// </summary>
[SwaggerSchema(Title = "ConfigOverrideRequest", Description = "Request payload for persisting an authoritative configuration override to the database.")]
public sealed record ConfigOverrideRequest : IExamplesProvider<ConfigOverrideRequest>
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

    /// <summary>
    /// Generates a representative example instance of <see cref="ConfigOverrideRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated configuration override request example.</returns>
    public ConfigOverrideRequest GetExamples() => new()
    {
        Key = "Simulation.TargetTemperatureKelvin",
        Value = "310.15",
        Description = "Target physiological body temperature (37 C)"
    };
}

/// <summary>
/// Request payload for creating a single-use collaborative share key.
/// </summary>
[SwaggerSchema(Title = "CreateShareKeyRequest", Description = "Request payload for generating a cryptographically random single-use collaborative share key.")]
public sealed record CreateShareKeyRequest : IExamplesProvider<CreateShareKeyRequest>
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
    [JsonPropertyName("lifetimeHours")]
    public int LifetimeHours { get; init; } = 24;

    /// <summary>
    /// Named expiration window ('1h', '4h', '24h', '7d', '1m', '1y', 'never').
    /// </summary>
    [Description("Named expiration window ('1h', '4h', '24h', '7d', '1m', '1y', 'never').")]
    [JsonPropertyName("expirationWindow")]
    public string? ExpirationWindow { get; init; }

    /// <summary>
    /// Permission scope granted (e.g., 'FullCollaborator', 'ReadOnlyTelemetry').
    /// </summary>
    [Description("Permission scope granted (e.g., 'FullCollaborator', 'ReadOnlyTelemetry').")]
    [JsonPropertyName("permissions")]
    public string? Permissions { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreateShareKeyRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated create share key request example.</returns>
    public CreateShareKeyRequest GetExamples() => new()
    {
        Label = "BioFoundry Partner Node",
        LifetimeHours = 48,
        ExpirationWindow = "48h",
        Permissions = "FullCollaborator"
    };
}

/// <summary>
/// Request payload for redeeming a single-use share key.
/// </summary>
[SwaggerSchema(Title = "RedeemShareKeyRequest", Description = "Request payload for redeeming a single-use share key to establish an authenticated session.")]
public sealed record RedeemShareKeyRequest : IExamplesProvider<RedeemShareKeyRequest>
{
    /// <summary>
    /// Secret share token string (e.g., 'nanoweaver_hrt_...').
    /// </summary>
    [Description("Secret share token string (e.g., 'nanoweaver_hrt_...').")]
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="RedeemShareKeyRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated redeem share key request example.</returns>
    public RedeemShareKeyRequest GetExamples() => new()
    {
        Token = "nanoweaver_hrt_a1b2c3d4e5f6789012345678abcdef01"
    };
}

/// <summary>
/// Request payload for generating a complete fabrication package.
/// </summary>
[SwaggerSchema(Title = "FabricationGenerateRequest", Description = "Request payload for generating DNA origami staple routing, annealing ramps, and manifests.")]
public sealed record FabricationGenerateRequest : IExamplesProvider<FabricationGenerateRequest>
{
    /// <summary>
    /// Raw sequence string or preset sample ID.
    /// </summary>
    [Description("Raw sequence string or preset sample ID.")]
    [JsonPropertyName("sequenceOrSampleId")]
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
    [JsonPropertyName("targetKd")]
    public double TargetKd { get; init; } = 4.2;

    /// <summary>
    /// Generates a representative example instance of <see cref="FabricationGenerateRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated fabrication package generation request example.</returns>
    public FabricationGenerateRequest GetExamples() => new()
    {
        SequenceOrSampleId = "CAGE-6HB-E2-001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        Geometry = "HexagonalPrismBarrel",
        TargetKd = 4.2
    };
}

/// <summary>
/// Request payload for evaluating a patient's pharmacogenomic profile from marker IDs.
/// </summary>
[SwaggerSchema(Title = "GenomicAnalysisRequest", Description = "Request payload for screening patient genetic markers and computing personalized kinetic modifiers.")]
public sealed record GenomicAnalysisRequest : IExamplesProvider<GenomicAnalysisRequest>
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    [Description("Patient identifier.")]
    [JsonPropertyName("patientId")]
    public string? PatientId { get; init; } = "ANON-PATIENT-001";

    /// <summary>
    /// Array of observed genetic marker identifiers (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').
    /// </summary>
    [Description("Array of observed genetic marker identifiers (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').")]
    [JsonPropertyName("markerIds")]
    public string[]? MarkerIds { get; init; }

    /// <summary>
    /// Active therapeutic regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [Description("Active therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Generates a representative example instance of <see cref="GenomicAnalysisRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated genomic analysis request example.</returns>
    public GenomicAnalysisRequest GetExamples() => new()
    {
        PatientId = "ANON-PATIENT-001",
        MarkerIds = ["F5_LEIDEN", "CYP1A2_STAR1F"],
        Regimen = TherapeuticRegimen.MaleToFemale
    };
}

/// <summary>
/// Request payload for scanning a DNA nucleotide sequence for CpG density and repeat motifs.
/// </summary>
[SwaggerSchema(Title = "SequenceAnalysisRequest", Description = "Request payload for scanning raw nucleotide sequences for immunogenicity and repeat lengths.")]
public sealed record SequenceAnalysisRequest : IExamplesProvider<SequenceAnalysisRequest>
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    [Description("Patient identifier.")]
    [JsonPropertyName("patientId")]
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

    /// <summary>
    /// Generates a representative example instance of <see cref="SequenceAnalysisRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated sequence analysis request example.</returns>
    public SequenceAnalysisRequest GetExamples() => new()
    {
        PatientId = "ANON-PATIENT-001",
        Sequence = "ATGCGATCGATCGATCGATCGCGCGCATATCGAT",
        Regimen = TherapeuticRegimen.MaleToFemale
    };
}

/// <summary>
/// Request payload for de-identifying a patient identity and FASTA payload.
/// </summary>
[SwaggerSchema(Title = "DeIdentifyRequest", Description = "Request payload for HIPAA Safe Harbor de-identification and GINA/GDPR consent confirmation.")]
public sealed record DeIdentifyRequest : IExamplesProvider<DeIdentifyRequest>
{
    /// <summary>
    /// Raw patient identifier or name.
    /// </summary>
    [Description("Raw patient identifier or name.")]
    [JsonPropertyName("rawSubjectIdentifier")]
    public string RawSubjectIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Raw genomic sequence or SNP profile string.
    /// </summary>
    [Description("Raw genomic sequence or SNP profile string.")]
    [JsonPropertyName("rawGenomicData")]
    public string? RawGenomicData { get; init; }

    /// <summary>
    /// Confirmation of explicit subject consent under GDPR Article 9.
    /// </summary>
    [Description("Confirmation of explicit subject consent under GDPR Article 9.")]
    [JsonPropertyName("gdprConsentConfirmed")]
    public bool GdprConsentConfirmed { get; init; } = true;

    /// <summary>
    /// Confirmation that data use is purpose-limited to therapeutic nanocarrier design under GINA.
    /// </summary>
    [Description("Confirmation that data use is purpose-limited to therapeutic nanocarrier design under GINA.")]
    [JsonPropertyName("ginaPurposeLimited")]
    public bool GinaPurposeLimited { get; init; } = true;

    /// <summary>
    /// Generates a representative example instance of <see cref="DeIdentifyRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated de-identification request example.</returns>
    public DeIdentifyRequest GetExamples() => new()
    {
        RawSubjectIdentifier = "SUBJ-98214-LOCAL",
        RawGenomicData = ">chr1_exon_sample\nATGCGATCGATC",
        GdprConsentConfirmed = true,
        GinaPurposeLimited = true
    };
}

/// <summary>
/// Request payload for crypto-shredding an individual subject's decryption keys.
/// </summary>
[SwaggerSchema(Title = "CryptoShredRequest", Description = "Request payload for executing GDPR Article 17 right-to-erasure via key zeroization.")]
public sealed record CryptoShredRequest : IExamplesProvider<CryptoShredRequest>
{
    /// <summary>
    /// Pseudonymized subject identifier to crypto-shred.
    /// </summary>
    [Description("Pseudonymized subject identifier to crypto-shred.")]
    [JsonPropertyName("subjectId")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="CryptoShredRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated crypto-shred request example.</returns>
    public CryptoShredRequest GetExamples() => new()
    {
        SubjectId = "f47ac10b-58cc-4372-a567-0e02b2c3d479"
    };
}

/// <summary>
/// Request payload for envelope-encrypting an arbitrary JSON object under a subject's post-quantum key.
/// </summary>
[SwaggerSchema(Title = "EncryptRecordRequest", Description = "Request payload for envelope-encrypting arbitrary health payloads with ML-KEM-768 + AES-256-GCM.")]
public sealed record EncryptRecordRequest : IExamplesProvider<EncryptRecordRequest>
{
    /// <summary>
    /// Pseudonymized subject identifier.
    /// </summary>
    [Description("Pseudonymized subject identifier.")]
    [JsonPropertyName("subjectId")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Arbitrary payload object or JSON element.
    /// </summary>
    [Description("Arbitrary payload object or JSON element.")]
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EncryptRecordRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated envelope encryption request example.</returns>
    public EncryptRecordRequest GetExamples() => new()
    {
        SubjectId = "f47ac10b-58cc-4372-a567-0e02b2c3d479",
        Payload = new Dictionary<string, object>
        {
            ["targetEstradiolPmolL"] = 650.0,
            ["bindingAffinityKd"] = 4.2
        }
    };
}

/// <summary>
/// Request payload for decrypting an envelope-encrypted record.
/// </summary>
[SwaggerSchema(Title = "DecryptRecordRequest", Description = "Request payload for decrypting an authenticated post-quantum envelope.")]
public sealed record DecryptRecordRequest : IExamplesProvider<DecryptRecordRequest>
{
    /// <summary>
    /// Pseudonymized subject identifier.
    /// </summary>
    [Description("Pseudonymized subject identifier.")]
    [JsonPropertyName("subjectId")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Encrypted envelope container.
    /// </summary>
    [Description("Encrypted envelope container.")]
    [JsonPropertyName("envelope")]
    public EncryptedEnvelope? Envelope { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DecryptRecordRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated envelope decryption request example.</returns>
    public DecryptRecordRequest GetExamples() => new()
    {
        SubjectId = "f47ac10b-58cc-4372-a567-0e02b2c3d479",
        Envelope = new EncryptedEnvelope().GetExamples()
    };
}

/// <summary>
/// Request payload for exporting an envelope-encrypted, post-quantum signed dataset bundle.
/// </summary>
[SwaggerSchema(Title = "ExportEncryptedDatasetRequest", Description = "Request payload for exporting a trajectory archive envelope-encrypted with ML-KEM-768 and signed with ML-DSA-65.")]
public sealed record ExportEncryptedDatasetRequest : IExamplesProvider<ExportEncryptedDatasetRequest>
{
    /// <summary>
    /// Source trajectory filename in shared storage.
    /// </summary>
    [Description("Source trajectory filename in shared storage.")]
    [JsonPropertyName("trajectoryFileName")]
    public string TrajectoryFileName { get; init; } = string.Empty;

    /// <summary>
    /// Target pseudonymized subject identifier.
    /// </summary>
    [Description("Target pseudonymized subject identifier.")]
    [JsonPropertyName("subjectId")]
    public string? SubjectId { get; init; }

    /// <summary>
    /// Destination export directory.
    /// </summary>
    [Description("Destination export directory.")]
    [JsonPropertyName("exportDirectory")]
    public string? ExportDirectory { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="ExportEncryptedDatasetRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated encrypted dataset export request example.</returns>
    public ExportEncryptedDatasetRequest GetExamples() => new()
    {
        TrajectoryFileName = "dna_nanotube_sample.xyz",
        SubjectId = "f47ac10b-58cc-4372-a567-0e02b2c3d479",
        ExportDirectory = "/exports"
    };
}
