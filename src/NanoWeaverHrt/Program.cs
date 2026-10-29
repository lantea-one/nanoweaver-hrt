using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Security.Storage;
using NanoWeaverHrt.Services;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Storage;
using NanoWeaverHrt.Swagger;
using NanoWeaverHrt.Formatting;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.ExceptionHandling;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Simulations.Models;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Bridges;
using NanoWeaverHrt.Security.Integrity;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Localization;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Hardware.Benchmarking;

namespace NanoWeaverHrt;

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
    /// <returns>
    /// A task that completes when the web host has shut down, which under normal operation is when the
    /// container or the operator stops the service.
    /// </returns>
    /// <remarks>
    /// Startup order matters here: node key material is provisioned before anything can sign a token or
    /// encrypt patient data, then the databases are initialized, then any patient profile still protected by
    /// the historic published key is re-encrypted, and only afterwards is the request pipeline built.
    /// </remarks>
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

        // WHAT: Registers the node secret store ahead of every other security service.
        // WHY:  The bearer-token signing key and the patient-data encryption key are generated on first launch
        //       and persisted on this node; nothing that signs a token or encrypts PHI may be constructed
        //       before that key material exists.
        // HOW:  A singleton whose constructor performs the whole generate-or-load sequence.
        builder.Services.AddSingleton<INodeSecretStore, NodeSecretStore>();
        builder.Services.AddSingleton<INodeDatasetSigner, NodeDatasetSigner>();
        builder.Services.AddSingleton<DatasetManifestSignatureVerifier>();
        builder.Services.AddSingleton<IPeerKeyRegistry, PeerKeyRepository>();
        builder.Services.AddSingleton<VerifiedDatasetImporter>();
        builder.Services.AddSingleton<IPostQuantumCryptoProvider, PostQuantumCryptoProvider>();
        builder.Services.AddSingleton<IKeyManagementService, KeyManagementService>();
        builder.Services.AddSingleton<IComplianceAuditService, ComplianceAuditService>();
        builder.Services.AddSingleton<IEncryptedStorageService, EncryptedStorageService>();
        builder.Services.AddSingleton<ISubjectKeyRepository, SubjectKeyRepository>();
        builder.Services.AddSingleton<SubjectErasureJournal>();
        builder.Services.AddSingleton<SubjectKeyVault>();
        builder.Services.AddSingleton<ISimulationCampaignService, SimulationCampaignService>();
        builder.Services.AddSingleton<DatabaseMigrationRunner>();
        builder.Services.AddSingleton<IWeaverDbConnectionFactory, WeaverDbConnectionFactory>();
        builder.Services.AddSingleton<IAuditLedgerRepository, AuditLedgerRepository>();
        builder.Services.AddSingleton<IUserRepository, UserRepository>();
        builder.Services.AddSingleton<ProfileDataProtector>();
        builder.Services.AddSingleton<IUserProfileRepository, UserProfileRepository>();
        builder.Services.AddSingleton<IPatientDnaRepository, PatientDnaRepository>();
        builder.Services.AddSingleton<IAssetIntegrityService, AssetIntegrityService>();
        builder.Services.AddSingleton<ISubjectAccessRepository, SubjectAccessRepository>();
        builder.Services.AddSingleton<ISubjectAccessService, SubjectAccessService>();
        // WHAT: Registers the authentication service behind the server-side idle expiration decorator.
        // WHY:  Prime Directive 12 requires idle sign-out to be validated server-side. The idle clock
        //       therefore lives in a singleton registry that no browser can touch, and every token
        //       validation - REST middleware and Blazor circuit alike - passes through the decorator.
        // HOW:  Registers the concrete service, the activity registry, and the decorator that joins them.
        builder.Services.AddSingleton<IServerSessionActivityTracker>(_ => new ServerSessionActivityTracker());
        builder.Services.AddSingleton<UserAuthenticationService>();
        builder.Services.AddSingleton<IUserAuthenticationService>(sp => new SessionIdleEnforcingAuthenticationService(
            sp.GetRequiredService<UserAuthenticationService>(),
            sp.GetRequiredService<IServerSessionActivityTracker>(),
            sp.GetRequiredService<IHttpContextAccessor>()));
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

        // WHAT: Registers community localization services and ambient culture context.
        // WHY:  Supplies localized strings and regional formatting across Web, Desktop, and API boundaries.
        // HOW:  Registers WeaverLocalizer as a singleton IWeaverLocalizer into the DI container.
        builder.Services.AddSingleton<IWeaverCultureContext, WeaverCultureContext>();
        builder.Services.AddSingleton<IWeaverLocalizer, WeaverLocalizer>();

        // WHAT: Registers the automated Opentrons wet-lab protocol generator.
        // WHY:  Transforms DNA origami fabrication packages into executable Opentrons Protocol API v2 Python scripts.
        // HOW:  Registers OpentronsProtocolGenerator as singleton IOpentronsProtocolGenerator into the DI container.
        builder.Services.AddSingleton<IOpentronsProtocolGenerator, OpentronsProtocolGenerator>();

        // WHAT: Registers in-process DuckDB columnar analytical query engine for trajectory Parquet analysis.
        // WHY:  Enables zero-copy analytical queries and structural summary metric computation across molecular trajectories.
        // HOW:  Registers DuckDbTrajectoryAnalyticsService as singleton ITrajectoryAnalyticsService into the DI container.
        builder.Services.AddSingleton<ITrajectoryAnalyticsService, DuckDbTrajectoryAnalyticsService>();

        // WHAT: Registers the hardware accelerator qualification and diagnostic benchmark engine.
        // WHY:  Permits cluster operators and researchers to benchmark compute throughput (FP32/FP64) and memory bandwidth before launching workloads.
        // HOW:  Registers AcceleratorBenchmarkService as singleton IAcceleratorBenchmarkService into the DI container.
        builder.Services.AddSingleton<IAcceleratorBenchmarkService, AcceleratorBenchmarkService>();

        // WHAT: Configures Swagger & OpenAPI generation with XML comments and Swashbuckle annotations.
        // WHY:  Provides interactive API exploration (/swagger) and ReDoc documentation (/documentation).
        // HOW:  Registers API explorer, SwaggerGen with OpenApiInfo, XML doc comments, and annotations.
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "NanoWeaverHrt (nanoweaver-hrt) Sovereign API",
                Version = "v1",
                Description = "Sovereign Hormone Replacement Therapy (HRT) DNA Origami, Endocrine Simulation, and Trajectory Analytics API.",
                Contact = new OpenApiContact
                {
                    Name = "NanoWeaverHrt Research Initiative",
                    Url = new Uri("https://github.com/lantea-one/nanoweaver-hrt")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://github.com/lantea-one/nanoweaver-hrt/blob/main/LICENSE")
                }
            });

            c.EnableAnnotations();
            c.IncludeXmlComments(Assembly.GetExecutingAssembly(), includeControllerXmlComments: true);
            c.SchemaFilter<OpenApiMultiFormatExampleFilter>();
            c.OperationFilter<OpenApiMultiFormatExampleFilter>();

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Asymmetric Private-Key Authenticated Bearer Token (issued by /api/auth/token or /api/auth/web-token). Example: 'Bearer {token}'",
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

        // WHAT: Forces the node's key material to be provisioned now, before the first request.
        // WHY:  Provisioning must either succeed or stop the service. If the store were resolved lazily, a
        //       damaged or unreadable key file would surface as a failed login hours later instead of as a
        //       failed start, and the container healthcheck would never notice.
        // HOW:  Resolve the singleton eagerly; NodeSecretStore's constructor generates or loads both keys and
        //       throws NodeSecretProvisioningException if it cannot.
        INodeSecretStore nodeSecrets = app.Services.GetRequiredService<INodeSecretStore>();
        app.Logger.LogInformation(
            "Node secret provisioning complete. Token signing key: {TokenKeyOrigin}. PHI master key: {PhiKeyOrigin}.",
            nodeSecrets.TokenSigningKeyOrigin,
            nodeSecrets.PhiMasterKeyOrigin);

        // WHAT: Eagerly resolves the node's persistent ML-DSA-65 signing key at startup and logs its public fingerprint.
        // WHY:  A corrupted or unreadable signing key seed file must abort startup immediately rather than failing on the first audit log or export hours later, and the public fingerprint lets auditors match ledger signatures to this node.
        // HOW:  Resolves IKeyManagementService (whose constructor loads or creates node_signing_mldsa65.key) and logs Origin and KeyId.
        IKeyManagementService nodeKeys = app.Services.GetRequiredService<IKeyManagementService>();
        app.Logger.LogInformation(
            "Node audit signing key ready. Algorithm: ML-DSA-65. Origin: {Origin}. Fingerprint: {KeyId}.",
            nodeKeys.NodeSigningKeyOrigin,
            nodeKeys.GetNodeSigningKey().KeyId);

        // WHAT: Eagerly resolves this node's persistent Ed25519 dataset manifest signer and logs its public fingerprint.
        // WHY:  A damaged dataset_signing_ed25519.key file must fail startup immediately rather than on a later export, and the fingerprint lets operators register this node in peer key registries out of band.
        // HOW:  Resolves INodeDatasetSigner and logs KeyOrigin and KeyId.
        INodeDatasetSigner datasetSigner = app.Services.GetRequiredService<INodeDatasetSigner>();
        app.Logger.LogInformation(
            "Dataset signing key ready. Origin: {Origin}. Fingerprint: {KeyId}.",
            datasetSigner.KeyOrigin,
            datasetSigner.KeyId);

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

        // WHAT: Upgrades any patient profile field still encrypted with the historic published key.
        // WHY:  Those values were never really protected (the key was in the public source), and leaving them
        //       behind would mean the node kept depending on a world-known key to read live patient data.
        //       Doing it here means no operator ever handles a secret and no patient record is lost.
        // HOW:  ReEncryptLegacyProfilePhiAsync reads each profile and writes back only the rows whose
        //       ciphertext needed the legacy key, which re-encrypts them with this node's own key.
        ProfileDataProtector profileProtector = app.Services.GetRequiredService<ProfileDataProtector>();
        IUserProfileRepository profileRepo = app.Services.GetRequiredService<IUserProfileRepository>();
        int reEncryptedProfiles = await ReEncryptLegacyProfilePhiAsync(userRepo, profileRepo, profileProtector);
        app.Logger.LogInformation(
            "Legacy patient profile re-encryption pass complete. Profiles upgraded to the node PHI master key: {UpgradedProfileCount}.",
            reEncryptedProfiles);

        // WHAT: Eagerly loads and verifies every stored subject ML-KEM-768 key and erasure tombstone.
        // WHY:  A lost or replaced PHI master key, or a tampered subject key row, must stop the service at
        //       startup rather than surfacing as failed decryption on a patient request hours later.
        // HOW:  Resolves SubjectKeyVault and awaits InitializeAsync, which opens every active row with the
        //       current node PHI key and throws SubjectKeyStoreLoadException if any row fails verification.
        SubjectKeyVault subjectKeyVault = app.Services.GetRequiredService<SubjectKeyVault>();
        SubjectKeyLoadReport subjectKeyReport = await subjectKeyVault.InitializeAsync();
        app.Logger.LogInformation(
            "Subject key store loaded. Active subject keys: {ActiveCount}. Crypto-shredded subjects: {ErasedCount}.",
            subjectKeyReport.ActiveCount,
            subjectKeyReport.ErasedCount);
        if (subjectKeyReport.ReappliedErasureCount + subjectKeyReport.RebuiltTombstoneCount > 0)
        {
            app.Logger.LogWarning(
                "Subject erasure reconciliation re-applied {ReappliedCount} erasures and restored {RebuiltCount} erasure records that the database did not contain. A database restored from a backup taken before those erasures is the usual cause.",
                subjectKeyReport.ReappliedErasureCount,
                subjectKeyReport.RebuiltTombstoneCount);
        }

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
                    // WHAT: Validates the optional probe session token through a failure-tolerant wrapper.
                    // WHY:  A liveness probe must never fail because a browser presented a malformed session
                    //       cookie; the raw validator throws FormatException on non-Base64 token segments.
                    // HOW:  TryValidateTokenAsync reports malformed encodings as an ordinary validation failure,
                    //       so the probe simply degrades to the unauthenticated high-level overview.
                    var (isHealthTokenValid, healthUser, _) = await TryValidateTokenAsync(authService, candidateHealthToken, context.RequestAborted);
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

                if (!isSetupRequired && path.Equals("/api/setup/initialize", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteSecureProblemAsync(context, StatusCodes.Status400BadRequest, "System has already completed initial Administrator setup.");
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
                    await WriteSecureProblemAsync(
                        context,
                        StatusCodes.Status503ServiceUnavailable,
                        "First-launch system setup is required. Please initialize the master Administrator via /api/setup/initialize or navigate to /setup.",
                        includeSetupRequiredFlag: true);
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
                path.Equals("/api/auth/web-token", StringComparison.OrdinalIgnoreCase))
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
                await WriteSecureProblemAsync(context, StatusCodes.Status401Unauthorized, "Authentication required. Provide a valid Bearer token in Authorization header.");
                return;
            }

            var (valid, user, errorReason) = await TryValidateTokenAsync(authService, token, context.RequestAborted);
            if (!valid || user == null)
            {
                await WriteSecureProblemAsync(context, StatusCodes.Status401Unauthorized, errorReason ?? "Invalid or expired authentication token.");
                return;
            }

            // 7. Access Gating: Web-only users cannot access REST API endpoints
            if (user.AccessGate == AccessGate.Web)
            {
                await WriteSecureProblemAsync(context, StatusCodes.Status403Forbidden, "Access Denied: This user account is gated to Web interface access only.");
                return;
            }

            // 8. Role Gating: per-subject identity, profile, and PHI record routes (self-service, Administrator, or tiered Researcher)
            PathString userSubPath = default;
            PathString patientSubPath = default;
            bool isUsersPath = path.StartsWithSegments("/api/users", out userSubPath);
            bool isPatientsPath = path.StartsWithSegments("/api/patients", out patientSubPath);
            if (isUsersPath || isPatientsPath)
            {
                // WHAT: Resolves the subject of the record from the route and compares it with the session identity.
                // WHY:  Allows patients self-service, administrators full oversight, and researchers tiered access.
                // HOW:  Queries effective access level for researchers, applies ResolveSubjectRecordAccess, and masks cross-subject denials.
                PathString effectiveSubPath = isUsersPath ? userSubPath : patientSubPath;
                SubjectAccessLevel? grantedLevel = null;
                if (user.Role == UserRole.Researcher)
                {
                    string sub = (effectiveSubPath.Value ?? string.Empty).TrimStart('/');
                    int slashIndex = sub.IndexOf('/');
                    string targetSubject = slashIndex >= 0 ? sub[..slashIndex] : sub;
                    if (!string.IsNullOrEmpty(targetSubject))
                    {
                        var subjectAccess = context.RequestServices.GetService<ISubjectAccessService>();
                        if (subjectAccess != null)
                        {
                            grantedLevel = await subjectAccess.GetEffectiveAccessLevelAsync(user.Username, targetSubject, context.RequestAborted);
                            if (grantedLevel.HasValue)
                            {
                                context.Items["SubjectAccessLevel"] = grantedLevel.Value;
                            }
                        }
                    }
                }

                SubjectRecordAccessDecision decision = ResolveSubjectRecordAccess(
                    effectiveSubPath.Value,
                    user.Username,
                    user.Role,
                    grantedLevel,
                    context.Request.Method);

                if (decision == SubjectRecordAccessDecision.MaskedAsNotFound)
                {
                    await WriteSecureProblemAsync(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
                    return;
                }

                if (decision == SubjectRecordAccessDecision.Forbidden)
                {
                    await WriteSecureProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden: Administrator role required for this administrative resource.");
                    return;
                }
            }
            else
            {
                // 9. Role Gating: every other API route resolves its requirement from the declarative route tables.
                // WHAT: Looks up the minimum role for the requested verb and path, then enforces it.
                // WHY:  Prime Directive 12 role gating must not depend on hand-maintained string prefixes that can
                //       silently stop matching; ResolveRouteRoleRequirement is pure and exhaustively unit tested.
                // HOW:  Resolves the requirement once, then rejects with RFC 7807 problem details when unmet.
                RouteRoleRequirement requirement = ResolveRouteRoleRequirement(context.Request.Method, path);
                if (!IsRoleAuthorizedForRequirement(user.Role, requirement))
                {
                    await WriteSecureProblemAsync(context, StatusCodes.Status403Forbidden, DescribeRoleRequirement(requirement));
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
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "NanoWeaverHrt API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "NanoWeaverHrt Swagger UI";
        });
        app.UseReDoc(c =>
        {
            c.SpecUrl = "/swagger/v1/swagger.json";
            c.RoutePrefix = "documentation";
            c.DocumentTitle = "NanoWeaverHrt API Documentation";
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
            string safeFileName = Path.GetFileName(fileName);
            string safePath = Path.Combine(exportDir, safeFileName);

            // WHAT: Restricts the endpoint to packaged export artifacts, answering 404 for anything else.
            // WHY:  A download route must only ever serve the artifact class it documents; an allow-list keeps
            //       any future non-export file dropped into the export volume unreachable (Prime Directive 14).
            // HOW:  Treats a disallowed extension exactly like a missing file, so the response discloses
            //       neither the allow-list itself nor whether the requested file exists on disk.
            if (!IsDownloadableFileName(safeFileName, DownloadableExportExtensions) || !File.Exists(safePath))
            {
                return Results.NotFound(new { error = "Exported file not found." });
            }

            return Results.File(safePath, "application/octet-stream", fileDownloadName: fileName);
        })
        .WithName("DownloadExport")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Downloads an exported dataset bundle or its manifest.")
        .WithDescription("Streams raw binary or JSON bytes from the export storage directory. Requires the Administrator or Researcher role.")
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Download Active Trajectory File
        // ---------------------------------------------------------------------
        app.MapGet("/api/trajectories/download/{fileName}", (string fileName, IConfiguration config) =>
        {
            string incomingDir = config["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
            string safeFileName = Path.GetFileName(fileName);
            string safePath = Path.Combine(incomingDir, safeFileName);

            // WHAT: Restricts the endpoint to molecular dynamics coordinate artifacts, answering 404 for anything else.
            // WHY:  The same shared volume stores the authoritative SQLite identity and patient-record database
            //       ('weaver_config.db'), which this route previously streamed verbatim to any valid token. The
            //       authoritative database must never leave the node through a download route, at any role,
            //       Administrator included.
            // HOW:  Treats any extension outside the trajectory allow-list — which excludes '.db' and every
            //       SQLite side file by construction — exactly like a missing file, so the response discloses
            //       neither the allow-list nor the presence of the database on disk.
            if (!IsDownloadableFileName(safeFileName, DownloadableTrajectoryExtensions) || !File.Exists(safePath))
            {
                return Results.NotFound(new { error = "Trajectory file not found." });
            }

            return Results.File(safePath, "application/octet-stream", fileDownloadName: fileName);
        })
        .WithName("DownloadTrajectory")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Downloads a raw trajectory file from the active shared volume.")
        .WithDescription("Streams raw .xyz, .dcd, or .dump simulation coordinates to the client. Requires the Administrator or Researcher role; patients obtain their own payload from /api/patient/dna.")
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: List Exported Dataset Artifacts
        // ---------------------------------------------------------------------
        app.MapGet("/api/datasets/exports", async (IDatasetManager manager, CancellationToken ct) =>
        {
            IReadOnlyList<ExportFileItem> items = await manager.ListExportsAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListExports")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Lists all packaged dataset archives, manifests, and signatures in the export volume.")
        .WithDescription("Retrieves metadata descriptors and direct download URLs for generated export files.")
        .Produces<IReadOnlyList<ExportFileItem>>(StatusCodes.Status200OK);

        // ---------------------------------------------------------------------
        // REST API: Delete Exported Dataset Artifact
        // ---------------------------------------------------------------------
        app.MapDelete("/api/datasets/exports/{fileName}", async (string fileName, IDatasetManager manager, CancellationToken ct) =>
        {
            bool deleted = await manager.DeleteExportAsync(fileName, ct);
            if (!deleted)
            {
                return Results.NotFound(new { error = "Exported file not found." });
            }
            return Results.Ok(new { message = $"Export artifact '{Path.GetFileName(fileName)}' deleted successfully." });
        })
        .WithName("DeleteExport")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Deletes an export bundle or manifest from disk.")
        .WithDescription("Permanently purges a designated file from the export volume.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Dataset Import & Cryptographic Verification
        // ---------------------------------------------------------------------
        app.MapPost("/api/datasets/import", async (IFormFile archive, IFormFile manifest, VerifiedDatasetImporter importer, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Authenticates dataset provenance and verifies archive integrity before extracting into active storage.
            // WHY:  Enforces peer key trust, signature verification, strict-mode policy, and SHA-256/size integrity.
            // HOW:  Parses the manifest JSON, delegates provenance and integrity checks to VerifiedDatasetImporter, and returns RFC 7807 on rejection.
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

            DatasetImportResult result;
            using (Stream archiveStream = archive.OpenReadStream())
            {
                result = await importer.ImportAsync(archiveStream, parsedManifest, ct);
            }

            if (result.Outcome != DatasetImportOutcome.ImportedSigned && result.Outcome != DatasetImportOutcome.ImportedUnsigned)
            {
                (int status, string detail) = VerifiedDatasetImporter.DescribeRejection(result.Outcome);
                return SecureProblemResult(context, status, detail);
            }

            string provenance = result.Outcome == DatasetImportOutcome.ImportedSigned ? "signed" : "unsigned";
            return Results.Ok(new
            {
                message = "Dataset successfully verified and imported.",
                manifest = parsedManifest,
                provenance,
                signerKeyId = result.SignerKeyId
            });
        })
        .WithName("ImportDataset")
        .WithTags("Datasets & Trajectories")
        .WithSummary("Verifies manifest digital signatures, cryptographic SHA-256 integrity, and imports a dataset archive into the active volume.")
        .WithDescription("Validates the uploaded manifest signature against this node's key and the peer key registry, enforces strict-mode policy for unsigned bundles, verifies the archive SHA-256 checksum and byte size, and extracts verified files into active storage.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
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
            // WHAT: Returns the effective configuration listing with every secret-bearing value redacted.
            // WHY:  Prime Directive 14 forbids disclosing database connection strings or credentials to API
            //       clients at any role, so the live POSTGRES_CONNECTION_STRING (password included) is never
            //       serialized — redaction, not role gating, is what keeps the credential inside the node.
            // HOW:  Pipes the repository listing through RedactSecretConfigurationValues before serialization.
            IReadOnlyList<ConfigurationItem> configs = await repo.GetAllConfigurationsAsync(ct);
            return Results.Ok(RedactSecretConfigurationValues(configs));
        })
        .WithName("GetAllConfigurations")
        .WithTags("Configuration Management")
        .WithSummary("Retrieves all stack configurations, enforcing database overrides over environment variables.")
        .WithDescription("Returns all configuration keys, their current effective values, and an indication of database precedence. Requires the Administrator role; credential-bearing values always read '[REDACTED]'.")
        .Produces<IReadOnlyList<ConfigurationItem>>(StatusCodes.Status200OK);

        app.MapPost("/api/configuration/override", async (ConfigOverrideRequest req, IConfigurationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Key))
            {
                return Results.BadRequest(new { error = "Configuration key cannot be empty." });
            }

            await repo.SetDatabaseOverrideAsync(req.Key, req.Value, req.Description ?? "", ct);

            // WHAT: Echoes the stored key, redacting the value when the key carries a credential.
            // WHY:  Echoing a connection string back would re-publish the credential the GET route redacts.
            // HOW:  Substitutes the redaction placeholder for secret-bearing keys before responding.
            string echoedValue = IsSecretConfigurationKey(req.Key) ? RedactedConfigurationValue : req.Value;
            return Results.Ok(new { message = $"Database override saved for '{req.Key}'. Database setting is now active.", key = req.Key, value = echoedValue });
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
        .WithDescription("Checks connectivity and latency to PostgreSQL, DuckDB, oxDNA, LAMMPS, and Valkey (Administrator or Researcher role required).")
        .Produces<IReadOnlyList<StackServiceInfo>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/services/ping/{serviceId}", async (string serviceId, IStackServiceManager stackMgr, CancellationToken ct) =>
        {
            StackServiceInfo svc = await stackMgr.PingServiceAsync(serviceId, ct);
            return Results.Ok(svc);
        })
        .WithName("PingStackService")
        .WithTags("Stack Services")
        .WithSummary("Tests connectivity and returns fresh telemetry for a specific stack service.")
        .WithDescription("Performs a low-latency socket ping or query to a single dependency (Administrator or Researcher role required).")
        .Produces<StackServiceInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

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

        app.MapPost("/api/compliance/de-identify", (DeIdentifyRequest req, IComplianceAuditService compliance, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(req.RawSubjectIdentifier))
            {
                // WHY: This endpoint's rejections now all use SecureProblemDetails, so a caller does not
                //      have to parse one schema for a validation failure and another for a fault.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "RawSubjectIdentifier cannot be empty.");
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
                // WHAT: Publishes a sanitized RFC 7807 rejection instead of the raw exception sentence.
                // WHY:  Directive 14 forbids exposing filesystem paths, connection strings, credentials,
                //       PEM material or SQL fragments to API clients. A hand-rolled
                //       '{ error = ex.Message }' RETURNS NORMALLY, so SecureExceptionHandlerMiddleware
                //       never sees it and SecureExceptionSanitizer never runs. The exception text reached
                //       the client verbatim, and an exception thrown inside a repository can carry any of
                //       those secrets in its Message.
                // HOW:  Routes the sentence through the sanitizer, then publishes SecureProblemDetails,
                //       the one rejection schema the middleware and the auth endpoints already use.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
            }
        })
        .WithName("DeIdentifySubject")
        .WithSummary("Applies HIPAA Safe Harbor de-identification and GINA/GDPR Article 9 consent validation.");

        app.MapPost("/api/compliance/crypto-shred", async (CryptoShredRequest req, IKeyManagementService keyMgmt, IComplianceAuditService compliance, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId))
            {
                return Results.BadRequest(new { error = "SubjectId cannot be empty." });
            }

            SubjectErasureOutcome outcome = await keyMgmt.CryptoShredSubjectKeyAsync(req.SubjectId, ct);
            if (outcome != SubjectErasureOutcome.Erased)
            {
                return Results.NotFound(new
                {
                    error = outcome == SubjectErasureOutcome.AlreadyErased
                        ? ShredAlreadyMessage(req.SubjectId)
                        : ShredUnknownMessage(req.SubjectId)
                });
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
                message = ShredOkMessage
            });
        })
        .WithName("CryptoShredSubject")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Executes GDPR Article 17 right-to-erasure via cryptographic destruction of subject decryption keys.")
        .WithDescription("Zeroizes private ML-KEM-768 key material, permanently rendering historical ciphertexts unrecoverable.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/compliance/encrypt-record", async (EncryptRecordRequest req, IEncryptedStorageService storage, HttpContext context, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId) || req.Payload == null)
            {
                // WHY: This endpoint's rejections now all use SecureProblemDetails, so a caller does not
                //      have to parse one schema for a validation failure and another for a fault.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "SubjectId and Payload cannot be empty.");
            }

            IResult response;
            try
            {
                EncryptedEnvelope envelope = await storage.EncryptSubjectRecordAsync(req.SubjectId, req.Payload, ct);
                response = Results.Ok(envelope);
            }
            catch (CryptographicException ex)
            {
                // WHY: As above -- a hand-rolled '{ error = ex.Message }' returns normally and so never
                //      reaches the middleware's sanitizer, publishing raw exception text to the client.
                response = SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
            }

            return response;
        })
        .WithName("EncryptSubjectRecord")
        .WithTags("Post-Quantum Cryptography & Compliance")
        .WithSummary("Envelope-encrypts an arbitrary JSON record under the subject's post-quantum ML-KEM-768 key.")
        .WithDescription("Encapsulates a shared AES-256-GCM secret using ML-KEM-768 and returns an authenticated envelope.")
        .Produces<EncryptedEnvelope>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/api/compliance/decrypt-record", (DecryptRecordRequest req, IEncryptedStorageService storage, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubjectId) || req.Envelope == null)
            {
                // WHY: This endpoint's rejections now all use SecureProblemDetails, so a caller does not
                //      have to parse one schema for a validation failure and another for a fault.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "SubjectId and Envelope cannot be empty.");
            }

            try
            {
                JsonElement payload = storage.DecryptSubjectRecord<JsonElement>(req.SubjectId, req.Envelope);
                return Results.Ok(payload);
            }
            catch (CryptographicException ex)
            {
                // WHY: As above -- a hand-rolled '{ error = ex.Message }' returns normally and so never
                //      reaches the middleware's sanitizer, publishing raw exception text to the client.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
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
            HttpContext context,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TrajectoryFileName))
            {
                // WHY: This endpoint's rejections now all use SecureProblemDetails, so a caller does not
                //      have to parse one schema for a validation failure and another for a fault.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "TrajectoryFileName cannot be empty.");
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
                // WHY: The worst of these. FileNotFoundException.Message embeds the full path it
                //      probed, so this branch handed every caller an absolute server path -- exactly
                //      what Directive 14 names first. The sanitizer rewrites it to [REDACTED_PATH].
                return SecureProblemResult(
                    context,
                    StatusCodes.Status404NotFound,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status404NotFound, context.TraceIdentifier));
            }
            catch (Exception ex)
            {
                // WHY: As above -- a hand-rolled '{ error = ex.Message }' returns normally and so never
                //      reaches the middleware's sanitizer, publishing raw exception text to the client.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
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
        app.MapPost("/api/setup/initialize", async (InitialSetupRequest request, IUserAuthenticationService auth, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Provisions the master initial Administrator and locks the first-launch wizard.
            // WHY:  Bootstraps the node's authoritative cryptographic identity and unblocks operational API endpoints.
            // HOW:  Validates inputs, registers administrator account, and issues initial Bearer token.
            if (string.IsNullOrWhiteSpace(request.AdminUsername) || string.IsNullOrWhiteSpace(request.PublicKeyBase64))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Administrator username and public key are required.");
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
                // WHAT: Converts a setup conflict into sanitized RFC 7807 problem details.
                // WHY:  Directive 14 requires the single SecureProblemDetails schema for API rejections and
                //       forbids exception text reaching clients with paths, credentials or SQL still in it.
                // HOW:  Routes the conflict sentence through SecureExceptionSanitizer before publishing it.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
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
        // REST API: Authentication Challenge Issuance
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/challenge", (ChallengeRequest request, IUserAuthenticationService auth, HttpContext context) =>
        {
            // WHAT: Mints a time-bounded cryptographic challenge nonce.
            // WHY:  Initiates zero-knowledge asymmetric challenge-response authentication.
            // HOW:  Generates 32 random bytes and stores in active challenges with a 120-second TTL, returning 429 if the cap is reached.
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Username is required.");
            }

            try
            {
                AuthChallenge challenge = auth.CreateChallenge(request.Username);
                return Results.Ok(challenge);
            }
            catch (ChallengeStoreCapacityExceededException)
            {
                return SecureProblemResult(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "Too many pending authentication challenges. Please retry shortly.");
            }
        })
        .WithName("CreateAuthChallenge")
        .WithTags("Authentication & Setup")
        .WithSummary("Issues a cryptographic challenge nonce for private-key authentication.")
        .WithDescription("Mints a time-bounded (120s) 256-bit challenge nonce that must be signed by the user's private key.")
        .Accepts<ChallengeRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthChallenge>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status429TooManyRequests);

        // ---------------------------------------------------------------------
        // REST API: Challenge-Response Token Exchange
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/token", async (TokenExchangeRequest request, IUserAuthenticationService auth, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Verifies a digitally signed challenge nonce and mints a Bearer token.
            // WHY:  Completes private-key proof of possession without transmitting private key bytes over the wire.
            // HOW:  Validates signature against user's registered public key and derives HMAC-signed token.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.SignatureBase64))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Username, challengeId, and signatureBase64 are all required.");
            }

            var (success, errorMessage, token, user) = await auth.VerifyChallengeAndIssueTokenAsync(
                request.Username,
                request.ChallengeId,
                request.SignatureBase64,
                isWebRequest: false,
                ct);

            if (!success || token == null || user == null)
            {
                return SecureProblemResult(context, StatusCodes.Status401Unauthorized, errorMessage ?? "Authentication failed.");
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
        app.MapPost("/api/users", async (CreateUserRequest request, IUserAuthenticationService auth, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Provisions a new sovereign user account with a pre-existing public key.
            // WHY:  Allows Administrator to add users who bring their own key material.
            // HOW:  Validates input, stores user record in database, and logs compliance audit event.
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.PublicKeyBase64))
            {
                // WHY: This endpoint's rejections now all use SecureProblemDetails, so a caller does not
                //      have to parse one schema for a validation failure and another for a fault.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Username and publicKeyBase64 are required.");
            }

            try
            {
                UserAccount? user = await auth.TryCreateUserAsync(
                    request.Username,
                    request.Role,
                    request.AccessGate,
                    request.PublicKeyBase64,
                    request.Algorithm,
                    ct);

                if (user is null)
                {
                    // WHY: This endpoint provisions NEW accounts only; silently rebinding an existing
                    //      account's key and role would be an account takeover primitive.
                    return SecureProblemResult(
                        context,
                        StatusCodes.Status409Conflict,
                        "A user with that username already exists.");
                }

                return Results.Created($"/api/users/{user.Username}", user);
            }
            catch (InvalidOperationException ex)
            {
                // WHY: As above -- a hand-rolled '{ error = ex.Message }' returns normally and so never
                //      reaches the middleware's sanitizer, publishing raw exception text to the client.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
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
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict);

        // ---------------------------------------------------------------------
        // REST API: User Management - Delete User
        // ---------------------------------------------------------------------
        app.MapDelete("/api/users/{username}", async (string username, IUserAuthenticationService auth, HttpContext context, CancellationToken ct) =>
        {
            // WHAT: Deletes a user account.
            // WHY:  Enforces security hygiene and user de-provisioning, protecting against deletion of the last admin.
            // HOW:  Invokes IUserAuthenticationService.DeleteUserAsync.
            try
            {
                bool deleted = await auth.DeleteUserAsync(username, ct);
                if (!deleted)
                {
                    // WHY: Same single rejection schema as this endpoint's fault path below.
                    return SecureProblemResult(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Cannot delete user. User not found.");
                }

                return Results.Ok(new { message = $"User '{username}' successfully deleted." });
            }
            catch (InvalidOperationException ex)
            {
                // WHY: As above -- a hand-rolled '{ error = ex.Message }' returns normally and so never
                //      reaches the middleware's sanitizer, publishing raw exception text to the client.
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    SecureExceptionSanitizer.SanitizeDetail(ex.Message, StatusCodes.Status400BadRequest, context.TraceIdentifier));
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
        // REST API: User Profile & Patient Demographic Directory
        // ---------------------------------------------------------------------
        app.MapGet("/api/users/{username}/profile", async (
            string username,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves demographic, clinical, and pronoun profile data for a specific user.
            // WHY:  Allows users to inspect their profile and administrators to view patient demographic information.
            // HOW:  Validates caller authorization, checks account existence, and queries IUserProfileRepository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasProfileSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            UserProfile? profile = await profileRepo.GetProfileAsync(username, ct);
            if (profile == null)
            {
                return Results.NotFound(new { error = $"Profile for user '{username}' not found." });
            }

            return Results.Ok(profile);
        })
        .WithName("GetUserProfile")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Retrieves demographic, clinical, and pronoun profile for a user.")
        .WithDescription("Returns user demographic profile, preferred name, encrypted avatar, regimen, biological sex at birth, and pronouns (self-access or Administrator role required).")
        .Produces<UserProfile>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/api/users/{username}/profile", async (
            string username,
            UserProfile inputProfile,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Inserts or updates demographic, clinical, and pronoun profile data for a specific user.
            // WHY:  Allows patients and users to keep their profiles up-to-date with encrypted storage of sensitive assets.
            // HOW:  Validates caller authorization, verifies account existence, binds the username, and upserts into repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (currentUser == null || (currentUser.Role != UserRole.Administrator && !string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            UserProfile updatedProfile = inputProfile with
            {
                Username = username,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await profileRepo.UpsertProfileAsync(updatedProfile, ct);
            return Results.Ok(updatedProfile);
        })
        .WithName("UpdateUserProfile")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Creates or updates a user profile record.")
        .WithDescription("Upserts demographic fields, pronouns, regimen, and encrypts profile picture data-URIs at rest (self-access or Administrator role required).")
        .Produces<UserProfile>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient Addresses Lookup & Management
        // ---------------------------------------------------------------------
        app.MapGet("/api/users/{username}/addresses", async (
            string username,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves all structured addresses registered for a patient or user.
            // WHY:  Provides delivery, residential, or clinic address lookup for patient logistics.
            // HOW:  Validates caller permissions, checks target existence, and fetches address records.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasFullReadSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            IReadOnlyList<PatientAddress> addresses = await profileRepo.GetAddressesAsync(username, ct);
            return Results.Ok(addresses);
        })
        .WithName("GetUserAddresses")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Retrieves all registered addresses for a user.")
        .WithDescription("Returns primary, postal, clinic, and delivery addresses associated with the specified user account.")
        .Produces<IReadOnlyList<PatientAddress>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/users/{username}/addresses", async (
            string username,
            PatientAddress inputAddress,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Adds or updates a structured address record for a specified patient or user.
            // WHY:  Enables maintenance of clinic, home, or nanocarrier delivery addresses.
            // HOW:  Validates required fields, sets ownership username and GUID identifier, and persists via repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (currentUser == null || (currentUser.Role != UserRole.Administrator && !string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            if (string.IsNullOrWhiteSpace(inputAddress.AddressLine1) || string.IsNullOrWhiteSpace(inputAddress.City))
            {
                return Results.BadRequest(new { error = "Address line 1 and city are required fields." });
            }

            PatientAddress address = inputAddress with
            {
                Id = string.IsNullOrWhiteSpace(inputAddress.Id) ? Guid.NewGuid().ToString("N") : inputAddress.Id,
                Username = username,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await profileRepo.UpsertAddressAsync(address, ct);
            return Results.Created($"/api/users/{username}/addresses/{address.Id}", address);
        })
        .WithName("AddOrUpdateUserAddress")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Registers or updates a patient address.")
        .WithDescription("Adds or updates a structured residential, postal, clinic, or delivery address (self-access or Administrator role required).")
        .Produces<PatientAddress>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/users/{username}/addresses/{id}", async (
            string username,
            string id,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Removes an existing address record for a specified patient or user.
            // WHY:  Allows pruning outdated or erroneous postal/clinic addresses.
            // HOW:  Validates permissions, verifies target account, and deletes entry via repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (currentUser == null || (currentUser.Role != UserRole.Administrator && !string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            bool deleted = await profileRepo.DeleteAddressAsync(id, username, ct);
            if (!deleted)
            {
                return Results.NotFound(new { error = $"Address '{id}' not found for user '{username}'." });
            }

            return Results.Ok(new { message = $"Address '{id}' successfully removed." });
        })
        .WithName("DeleteUserAddress")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Deletes an address record.")
        .WithDescription("Permanently removes the specified address entry from the patient directory.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient Emergency & Caregiver Outreach Contacts
        // ---------------------------------------------------------------------
        app.MapGet("/api/users/{username}/contacts", async (
            string username,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves the trusted contact lookup table for a specified patient or user.
            // WHY:  Identifies outreach individuals, caregivers, and emergency contacts if patient cannot be reached.
            // HOW:  Validates permissions, checks account existence, and queries contacts ordered by priority.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasFullReadSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            IReadOnlyList<PatientContact> contacts = await profileRepo.GetContactsAsync(username, ct);
            return Results.Ok(contacts);
        })
        .WithName("GetUserContacts")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Retrieves emergency and caregiver contact lookup table for a patient.")
        .WithDescription("Returns trusted outreach contacts to communicate with in case the patient cannot be reached directly.")
        .Produces<IReadOnlyList<PatientContact>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/users/{username}/contacts", async (
            string username,
            PatientContact inputContact,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Adds or updates a trusted outreach/emergency contact entry for a patient.
            // WHY:  Maintains critical contact relationships for safety and clinical follow-up.
            // HOW:  Validates required contact name and phone number, binds username, and persists via repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (currentUser == null || (currentUser.Role != UserRole.Administrator && !string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            if (string.IsNullOrWhiteSpace(inputContact.ContactName) || string.IsNullOrWhiteSpace(inputContact.PhoneNumber))
            {
                return Results.BadRequest(new { error = "Contact name and phone number are required fields." });
            }

            PatientContact contact = inputContact with
            {
                Id = string.IsNullOrWhiteSpace(inputContact.Id) ? Guid.NewGuid().ToString("N") : inputContact.Id,
                Username = username,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await profileRepo.UpsertContactAsync(contact, ct);
            return Results.Created($"/api/users/{username}/contacts/{contact.Id}", contact);
        })
        .WithName("AddOrUpdateUserContact")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Registers or updates an emergency or outreach contact.")
        .WithDescription("Adds or updates a trusted contact person with relationship, priority order, and disclosure consent.")
        .Produces<PatientContact>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/users/{username}/contacts/{id}", async (
            string username,
            string id,
            IUserProfileRepository profileRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Removes an emergency contact from a patient's contact lookup table.
            // WHY:  Allows patients to revoke or modify authorized outreach individuals.
            // HOW:  Validates caller permissions, checks target existence, and deletes entry via repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (currentUser == null || (currentUser.Role != UserRole.Administrator && !string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You may only view or manage your own profile, addresses, and contacts.");
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            bool deleted = await profileRepo.DeleteContactAsync(id, username, ct);
            if (!deleted)
            {
                return Results.NotFound(new { error = $"Contact '{id}' not found for user '{username}'." });
            }

            return Results.Ok(new { message = $"Contact '{id}' successfully removed." });
        })
        .WithName("DeleteUserContact")
        .WithTags("User Profile & Patient Directory")
        .WithSummary("Deletes an outreach contact.")
        .WithDescription("Permanently removes the specified contact entry from the patient lookup table.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient Digital DNA Package Generation & Download
        // ---------------------------------------------------------------------
        app.MapGet("/api/patient/dna", async (
            HttpContext context,
            IFabricationService fab,
            ISubjectAccessService subjectAccess,
            string? patientId,
            TherapeuticRegimen? regimen,
            string? geometry,
            double? targetKd,
            CancellationToken ct) =>
        {
            // WHAT: Synthesizes a patient-targeted Digital DNA package.
            // WHY:  Allows Patients, Researchers, and Administrators to download digital DNA blueprints.
            // HOW:  Generates nanocarrier sequences, staple routing tables, annealing schedule, and SHA-256 provenance.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            string effectivePatientId;
            if (!string.IsNullOrWhiteSpace(patientId))
            {
                string trimmedId = patientId.Trim().ToLowerInvariant();
                if (!IsValidPatientIdentifier(trimmedId))
                {
                    return SecureProblemResult(
                        context,
                        StatusCodes.Status400BadRequest,
                        "The requested patient identifier format is invalid.");
                }

                bool isAuthorized = currentUser != null && (
                    currentUser.Role == UserRole.Administrator ||
                    string.Equals(currentUser.Username, trimmedId, StringComparison.OrdinalIgnoreCase));

                if (!isAuthorized && currentUser?.Role == UserRole.Researcher)
                {
                    SubjectAccessLevel? lvl = await subjectAccess.GetEffectiveAccessLevelAsync(currentUser.Username, trimmedId, ct);
                    if (lvl.HasValue && lvl.Value >= SubjectAccessLevel.GenomicAccess)
                    {
                        isAuthorized = true;
                    }
                }

                if (!isAuthorized)
                {
                    return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
                }

                effectivePatientId = trimmedId;
            }
            else
            {
                effectivePatientId = currentUser?.Username ?? "patient-sovereign-01";
            }

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
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient Digitized DNA Blueprints
        // ---------------------------------------------------------------------
        app.MapGet("/api/patients/{username}/dna", async (
            string username,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves all persistent digitized DNA blueprint records for a patient.
            // WHY:  Allows patients, clinicians, and researchers to review nanocarrier architectures and sequences.
            // HOW:  Validates permissions, verifies patient existence, and queries the repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            IReadOnlyList<PatientDigitalDnaRecord> list = await dnaRepo.GetDnaRecordsAsync(username, ct);
            return Results.Ok(list);
        })
        .WithName("GetPatientDnaRecords")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Lists all digitized DNA blueprint records for a patient.")
        .WithDescription("Retrieves the collection of persistent DNA nanocarrier blueprints associated with the specified patient.")
        .Produces<IReadOnlyList<PatientDigitalDnaRecord>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/patients/{username}/dna/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves a specific digitized DNA blueprint by id.
            // WHY:  Supplies detailed staple routings, annealing schedule, and provenance for inspection or download.
            // HOW:  Validates authorization, checks record existence, and verifies patient ownership.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientDigitalDnaRecord? record = await dnaRepo.GetDnaRecordByIdAsync(id, ct);
            if (record == null || !string.Equals(record.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"DNA blueprint '{id}' not found for patient '{username}'." });
            }

            return Results.Ok(record);
        })
        .WithName("GetPatientDnaRecordById")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Retrieves a specific digitized DNA blueprint by ID.")
        .WithDescription("Retrieves detailed staple sequences and annealing schedules for the designated DNA blueprint.")
        .Produces<PatientDigitalDnaRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/patients/{username}/dna", async (
            string username,
            CreatePatientDnaRequest request,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            IUserProfileRepository profileRepo,
            IFabricationService fab,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Synthesizes and registers a new digitized DNA blueprint for a patient.
            // WHY:  Allows clinicians and researchers to design and assign custom nanocarrier configurations.
            // HOW:  Synthesizes origami components via IFabricationService, computes composition, and saves blueprint and kinetics result.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            UserProfile? profile = await profileRepo.GetProfileAsync(username, ct);
            TherapeuticRegimen effectiveRegimen = request.Regimen ?? profile?.Regimen ?? TherapeuticRegimen.MaleToFemale;
            string effectiveGeometry = !string.IsNullOrWhiteSpace(request.NanocarrierGeometry) ? request.NanocarrierGeometry : "Hexagonal Nanotube (6HB)";
            double effectiveKd = request.TargetAptamerKdNm ?? 3.5;

            DnaOrigamiSample sample = fab.GetPresetSamples().FirstOrDefault(s => s.Regimen == effectiveRegimen)
                ?? fab.GetPresetSamples()[0];
            string effectiveScaffold = !string.IsNullOrWhiteSpace(request.ScaffoldFasta) ? request.ScaffoldFasta : sample.AptamerSequence;
            var (_, _, _, _, gcPercent, mwKDa) = fab.AnalyzeSequenceComposition(effectiveScaffold);

            FabricationPackage pkg = fab.GenerateFabricationPackage(sample.Id, effectiveRegimen, effectiveGeometry, effectiveKd);

            string dnaId = "dna_" + Guid.NewGuid().ToString("N");
            string title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : $"{effectiveGeometry} - {sample.ActiveLigand}";

            PatientDigitalDnaRecord dnaRecord = new()
            {
                Id = dnaId,
                Username = username,
                Title = title,
                Regimen = effectiveRegimen,
                TargetTissue = !string.IsNullOrWhiteSpace(request.TargetTissue) ? request.TargetTissue : sample.TargetTissue,
                ActiveLigand = !string.IsNullOrWhiteSpace(request.ActiveLigand) ? request.ActiveLigand : sample.ActiveLigand,
                NanocarrierGeometry = effectiveGeometry,
                TargetAptamerKdNm = effectiveKd,
                ScaffoldFasta = effectiveScaffold,
                GcPercentage = gcPercent,
                EstimatedMwKDa = mwKDa,
                Staples = pkg.StapleOligos,
                AnnealingProtocol = pkg.AnnealingSchedule,
                ProvenanceSha256 = pkg.Sha256Manifest,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Notes = request.Notes
            };

            await dnaRepo.UpsertDnaRecordAsync(dnaRecord, ct);

            // Automatically record corresponding synthesis/fabrication result
            PatientResultRecord resultRecord = PatientResultRecord.FromFabricationPackage(pkg, username, dnaId);
            await dnaRepo.UpsertResultAsync(resultRecord, ct);

            return Results.Created($"/api/patients/{username}/dna/{dnaId}", dnaRecord);
        })
        .WithName("CreatePatientDnaRecord")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Synthesizes and registers a digitized DNA blueprint for a patient.")
        .WithDescription("Calculates folded geometry, synthetic staple routing, and annealing protocol, persisting the blueprint to the patient dossier.")
        .Produces<PatientDigitalDnaRecord>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/patients/{username}/dna/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Removes a digitized DNA blueprint record by id.
            // WHY:  Allows researchers and patients to retire superseded blueprints.
            // HOW:  Validates permissions, verifies ownership, and deletes record via repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientDigitalDnaRecord? existing = await dnaRepo.GetDnaRecordByIdAsync(id, ct);
            if (existing == null || !string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"DNA blueprint '{id}' not found for patient '{username}'." });
            }

            await dnaRepo.DeleteDnaRecordAsync(id, ct);
            return Results.Ok(new { message = $"DNA blueprint '{id}' successfully removed." });
        })
        .WithName("DeletePatientDnaRecord")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Deletes a digitized DNA blueprint.")
        .WithDescription("Permanently removes the specified DNA blueprint record from the patient dossier.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient DNA & Sequence Imports
        // ---------------------------------------------------------------------
        app.MapGet("/api/patients/{username}/imports", async (
            string username,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves all imported sequence files and datasets for a patient.
            // WHY:  Supplies imported file assets to the DNA management interface.
            // HOW:  Validates authorization, checks user existence, and queries repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            IReadOnlyList<PatientDnaImportRecord> list = await dnaRepo.GetImportsAsync(username, ct);
            return Results.Ok(list);
        })
        .WithName("GetPatientImports")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Lists all imported sequence and dataset files for a patient.")
        .WithDescription("Retrieves the collection of imported external files and variant panels associated with the specified patient.")
        .Produces<IReadOnlyList<PatientDnaImportRecord>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/patients/{username}/imports/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves an imported sequence or dataset record by ID.
            // WHY:  Allows reviewing the raw sequence text, checksums, and metadata of an import.
            // HOW:  Validates caller permissions and queries import by ID.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientDnaImportRecord? record = await dnaRepo.GetImportByIdAsync(id, ct);
            if (record == null || !string.Equals(record.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"Import '{id}' not found for patient '{username}'." });
            }

            return Results.Ok(record);
        })
        .WithName("GetPatientImportById")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Retrieves an imported sequence record by ID.")
        .WithDescription("Retrieves raw sequence data and checksum verification for the specified import record.")
        .Produces<PatientDnaImportRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/patients/{username}/imports", async (
            string username,
            CreatePatientImportRequest request,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            IUserProfileRepository profileRepo,
            IPharmacogenomicsService genomicsService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Imports raw DNA sequence data, variant panels, or trajectory archives for a patient.
            // WHY:  Integrates external patient genetic sequences into the decentralized sovereign platform.
            // HOW:  Calculates SHA-256 checksum, persists import record, and optionally triggers automated pharmacogenomics analysis.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return Results.BadRequest(new { error = "FileName is required for DNA data import." });
            }

            if (string.IsNullOrWhiteSpace(request.SequenceOrData))
            {
                return Results.BadRequest(new { error = "SequenceOrData content is required for DNA data import." });
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(request.SequenceOrData);
            byte[] hash = SHA256.HashData(dataBytes);
            string sha256Hex = Convert.ToHexStringLower(hash);

            string importId = "imp_" + Guid.NewGuid().ToString("N");
            PatientDnaImportRecord importRecord = new()
            {
                Id = importId,
                Username = username,
                FileName = request.FileName,
                ImportType = !string.IsNullOrWhiteSpace(request.ImportType) ? request.ImportType : "FASTA",
                SequenceOrData = request.SequenceOrData,
                SizeBytes = dataBytes.Length,
                Sha256Checksum = sha256Hex,
                ImportedBy = currentUser.Username,
                Status = !string.IsNullOrWhiteSpace(request.Status) ? request.Status : "Processed",
                MetadataJson = request.MetadataJson ?? "{}",
                ImportedAtUtc = DateTimeOffset.UtcNow
            };

            await dnaRepo.UpsertImportAsync(importRecord, ct);

            // Automated analysis upon import if requested
            if (request.AutoAnalyze)
            {
                UserProfile? profile = await profileRepo.GetProfileAsync(username, ct);
                TherapeuticRegimen regimen = profile?.Regimen ?? TherapeuticRegimen.MaleToFemale;

                PatientGenomicProfile genomicProfile = genomicsService.AnalyzeSequence(username, request.SequenceOrData, regimen);
                PatientResultRecord analysisResult = PatientResultRecord.FromGenomicProfile(genomicProfile, importId);
                await dnaRepo.UpsertResultAsync(analysisResult, ct);
            }

            return Results.Created($"/api/patients/{username}/imports/{importId}", importRecord);
        })
        .WithName("CreatePatientImport")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Imports DNA sequence data or variant panels for a patient.")
        .WithDescription("Persists raw sequence payload with SHA-256 integrity hash, optionally executing automated sequence analysis.")
        .Produces<PatientDnaImportRecord>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/patients/{username}/imports/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Deletes an imported sequence or dataset record by ID.
            // WHY:  Allows cleaning up superseded or corrupted import files.
            // HOW:  Validates caller permissions and deletes entry from repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientDnaImportRecord? existing = await dnaRepo.GetImportByIdAsync(id, ct);
            if (existing == null || !string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"Import '{id}' not found for patient '{username}'." });
            }

            await dnaRepo.DeleteImportAsync(id, ct);
            return Results.Ok(new { message = $"Import '{id}' successfully removed." });
        })
        .WithName("DeletePatientImport")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Deletes an imported sequence record.")
        .WithDescription("Permanently removes the specified import record from the patient profile.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Patient Simulation, Endocrine & Clinical Results
        // ---------------------------------------------------------------------
        app.MapGet("/api/patients/{username}/results", async (
            string username,
            string? resultType,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves all clinical, simulation, and pharmacogenomic results for a patient.
            // WHY:  Allows patients and clinicians to review physiological responses and nanocarrier metrics.
            // HOW:  Validates permissions, queries results, and applies optional resultType filtering.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            IReadOnlyList<PatientResultRecord> list = await dnaRepo.GetResultsAsync(username, ct);
            if (!string.IsNullOrWhiteSpace(resultType))
            {
                list = list.Where(r => string.Equals(r.ResultType, resultType, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return Results.Ok(list);
        })
        .WithName("GetPatientResults")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Lists all simulation and clinical results for a patient.")
        .WithDescription("Retrieves the collection of analytical findings, binding affinities, and kinetic models associated with the patient.")
        .Produces<IReadOnlyList<PatientResultRecord>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/patients/{username}/results/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves a specific simulation or clinical result record by ID.
            // WHY:  Provides fine-grained access to individual metric reports and findings.
            // HOW:  Validates caller permissions and queries result by ID.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientResultRecord? record = await dnaRepo.GetResultByIdAsync(id, ct);
            if (record == null || !string.Equals(record.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"Result '{id}' not found for patient '{username}'." });
            }

            return Results.Ok(record);
        })
        .WithName("GetPatientResultById")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Retrieves a specific result record by ID.")
        .WithDescription("Retrieves detailed quantitative metrics and findings for the specified result record.")
        .Produces<PatientResultRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/patients/{username}/results", async (
            string username,
            CreatePatientResultRequest request,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Manually creates or registers a simulation/clinical result record for a patient.
            // WHY:  Allows laboratory tools and simulation runners to record outcomes against the patient.
            // HOW:  Validates input fields, verifies target patient existence, and persists result record.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.ResultType) || string.IsNullOrWhiteSpace(request.Summary))
            {
                return Results.BadRequest(new { error = "Title, ResultType, and Summary are required fields." });
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            string resultId = "res_" + Guid.NewGuid().ToString("N");
            PatientResultRecord resultRecord = new()
            {
                Id = resultId,
                Username = username,
                DnaRecordId = request.DnaRecordId,
                ImportId = request.ImportId,
                ResultType = request.ResultType,
                Title = request.Title,
                Status = !string.IsNullOrWhiteSpace(request.Status) ? request.Status : "Complete",
                MetricsJson = request.MetricsJson ?? "{}",
                Summary = request.Summary,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            await dnaRepo.UpsertResultAsync(resultRecord, ct);
            return Results.Created($"/api/patients/{username}/results/{resultId}", resultRecord);
        })
        .WithName("CreatePatientResult")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Registers a new simulation or clinical result record.")
        .WithDescription("Persists analytical findings, binding kinetics, or simulation outcome metrics for a patient.")
        .Produces<PatientResultRecord>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/patients/{username}/results/{id}", async (
            string username,
            string id,
            IPatientDnaRepository dnaRepo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Deletes a simulation or clinical result record by ID.
            // WHY:  Allows cleaning up test or superseded result snapshots.
            // HOW:  Validates caller permissions and deletes entry from repository.
            UserAccount? currentUser = context.Items["CurrentUser"] as UserAccount;
            if (!HasGenomicSubjectAccess(currentUser, username, context))
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, SubjectRecordNotFoundDetail);
            }

            UserAccount? targetAccount = await userRepo.GetUserAsync(username, ct);
            if (targetAccount == null)
            {
                return Results.NotFound(new { error = $"User '{username}' does not exist." });
            }

            PatientResultRecord? existing = await dnaRepo.GetResultByIdAsync(id, ct);
            if (existing == null || !string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"Result '{id}' not found for patient '{username}'." });
            }

            await dnaRepo.DeleteResultAsync(id, ct);
            return Results.Ok(new { message = $"Result '{id}' successfully removed." });
        })
        .WithName("DeletePatientResult")
        .WithTags("Patient DNA & Imports")
        .WithSummary("Deletes a result record.")
        .WithDescription("Permanently removes the specified result record from the patient profile.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Peer Key Registry (Administrator Only)
        // ---------------------------------------------------------------------
        app.MapPost("/api/peer-keys", async (
            RegisterPeerKeyRequest request,
            IPeerKeyRegistry registry,
            INodeDatasetSigner localSigner,
            IComplianceAuditService audit,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Validates and registers a trusted peer laboratory's dataset-signing public key.
            // WHY:  Establishes an out-of-band trust anchor for verifying imported dataset manifests.
            // HOW:  Normalizes the label and SubjectPublicKeyInfo DER, rejects self-keys and duplicates, stores the record, and logs to the compliance ledger.
            if (!PeerKeyValidation.TryNormalizeLabel(request.Label, out string label))
            {
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "label must be 1 to 128 characters with no control characters.");
            }

            if (!PeerKeyValidation.TryNormalizePublicKey(request.Algorithm, request.PublicKeyBase64, out string publicKeyBase64, out string keyId))
            {
                return SecureProblemResult(
                    context,
                    StatusCodes.Status400BadRequest,
                    "publicKeyBase64 must be a Base64 SubjectPublicKeyInfo public key matching the declared algorithm.");
            }

            const string conflictDetail = "That key is already registered, has been revoked, or is this node's own dataset signing key.";
            if (string.Equals(keyId, localSigner.KeyId, StringComparison.Ordinal))
            {
                return SecureProblemResult(context, StatusCodes.Status409Conflict, conflictDetail);
            }

            // WHY `!`: AdministratorRoutes guarantees an authenticated Administrator session populated CurrentUser before the handler runs.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            PeerKeyRecord record = new()
            {
                KeyId = keyId,
                Label = label,
                Algorithm = request.Algorithm,
                PublicKeyBase64 = publicKeyBase64,
                RegisteredBy = caller.Username,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            };

            if (!await registry.TryRegisterAsync(record, ct))
            {
                return SecureProblemResult(context, StatusCodes.Status409Conflict, conflictDetail);
            }

            audit.LogAction(
                caller.Username,
                "RegisterPeerKey",
                $"peer-key:{keyId}",
                JsonSerializer.Serialize(new { algorithm = request.Algorithm, label }));

            return Results.Created($"/api/peer-keys/{keyId}", record);
        })
        .WithName("RegisterPeerKey")
        .WithTags("Peer Key Registry")
        .WithSummary("Registers a trusted peer laboratory's dataset-signing public key.")
        .WithDescription("Validates and stores an Ed25519 or ML-DSA-65 SubjectPublicKeyInfo public key in the peer key registry after out-of-band fingerprint verification. Requires Administrator role.")
        .Accepts<RegisterPeerKeyRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<PeerKeyRecord>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict);

        app.MapGet("/api/peer-keys", async (
            IPeerKeyRegistry registry,
            INodeDatasetSigner localSigner,
            CancellationToken ct) =>
        {
            // WHAT: Lists this node's own dataset-signing public key alongside all registered peer keys.
            // WHY:  Allows administrators to export this node's public key out of band and audit active and revoked peers.
            // HOW:  Reads localSigner identity and queries registry.ListAsync.
            IReadOnlyList<PeerKeyRecord> peers = await registry.ListAsync(ct);
            return Results.Ok(new PeerKeyListResponse
            {
                LocalNode = new(localSigner.KeyId, localSigner.Algorithm, localSigner.PublicKeyBase64),
                Peers = peers
            });
        })
        .WithName("ListPeerKeys")
        .WithTags("Peer Key Registry")
        .WithSummary("Lists this node's dataset-signing public key and all registered peer keys.")
        .WithDescription("Returns this node's public dataset signing identity alongside all active and revoked peer laboratory keys. Requires Administrator role.")
        .Produces<PeerKeyListResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/peer-keys/{keyId}/revoke", async (
            string keyId,
            IPeerKeyRegistry registry,
            IComplianceAuditService audit,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Permanently revokes an active peer dataset-signing public key.
            // WHY:  Stops trusting datasets signed by a compromised or decommissioned peer while preserving the revocation audit trail.
            // HOW:  Calls registry.RevokeAsync, returns 404 when no active key matches, and records the action in the compliance ledger.
            // WHY `!`: AdministratorRoutes guarantees an authenticated Administrator session populated CurrentUser before the handler runs.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            PeerKeyRecord? revoked = await registry.RevokeAsync(keyId, caller.Username, DateTimeOffset.UtcNow, ct);
            if (revoked is null)
            {
                return SecureProblemResult(context, StatusCodes.Status404NotFound, "No active peer key has that identifier.");
            }

            audit.LogAction(caller.Username, "RevokePeerKey", $"peer-key:{keyId}", "{}");
            return Results.Ok(revoked);
        })
        .WithName("RevokePeerKey")
        .WithTags("Peer Key Registry")
        .WithSummary("Permanently revokes an active peer dataset-signing public key.")
        .WithDescription("Marks an active peer key as permanently revoked so datasets signed by it are no longer trusted. Revoked keys remain in the registry for audit history and can never be reactivated. Requires Administrator role.")
        .Produces<PeerKeyRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Browser Challenge-Response Session Exchange (Web UI transport)
        // ---------------------------------------------------------------------
        app.MapPost("/api/auth/web-token", async (
            TokenExchangeRequest request,
            IUserAuthenticationService auth,
            IConfigurationRepository configRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Verifies a browser-signed challenge nonce and establishes an authenticated Web UI session.
            // WHY:  Completes challenge-response login for the browser UI without ever transmitting the user's private key to the server (Audit C8).
            // HOW:  Enforces same-origin rules, validates the signature with isWebRequest: true, sets the HttpOnly weaver_session cookie, and returns AuthTokenResponse.
            if (!await IsAllowedWebTokenOriginAsync(context, configRepo, ct))
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Cross-origin browser session requests are forbidden.");
            }

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.SignatureBase64))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Username, challengeId, and signatureBase64 are all required.");
            }

            var (success, errorMessage, token, user) = await auth.VerifyChallengeAndIssueTokenAsync(
                request.Username,
                request.ChallengeId,
                request.SignatureBase64,
                isWebRequest: true,
                ct);

            if (!success || token == null || user == null)
            {
                return SecureProblemResult(context, StatusCodes.Status401Unauthorized, errorMessage ?? "Authentication failed.");
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
        .WithName("ExchangeWebAuthToken")
        .WithTags("Authentication & Setup")
        .WithSummary("Exchanges a browser-signed challenge nonce for an interactive Web UI session cookie and token.")
        .WithDescription("Web-transport endpoint: refuses Api-only accounts, verifies the challenge signature against the registered public key, and sets the HttpOnly weaver_session cookie.")
        .Accepts<TokenExchangeRequest>("application/json", "application/xml", "application/x-yaml")
        .Produces<AuthTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: Subject Access Grants (Administrator Only)
        // ---------------------------------------------------------------------
        app.MapGet("/api/subject-access/grants", async (
            string? researcher,
            string? subject,
            ISubjectAccessRepository repo,
            CancellationToken ct) =>
        {
            // WHAT: Lists active direct subject access grants with optional researcher or subject filters.
            // WHY:  Allows administrators to audit and inspect all direct patient access grants.
            // HOW:  Queries ISubjectAccessRepository with optional filter criteria.
            IReadOnlyList<SubjectGrant> grants = await repo.ListSubjectGrantsAsync(researcher, subject, ct);
            return Results.Ok(grants);
        })
        .WithName("ListSubjectGrants")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Lists direct researcher access grants for patients or subjects.")
        .WithDescription("Returns direct subject access grants filtered optionally by researcher username or subject username (Administrator role required).")
        .Produces<IReadOnlyList<SubjectGrant>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/subject-access/grants", async (
            GrantSubjectAccessRequest request,
            ISubjectAccessService accessService,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Grants a researcher tiered direct access over a target patient or subject.
            // WHY:  Enables administrators to establish clinical trial or genomic review permissions.
            // HOW:  Validates caller role, checks researcher and subject existence, and persists grant via ISubjectAccessService.
            if (string.IsNullOrWhiteSpace(request.ResearcherUsername) || string.IsNullOrWhiteSpace(request.SubjectUsername))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "researcherUsername and subjectUsername are required.");
            }

            UserAccount? caller = (UserAccount)context.Items["CurrentUser"]!;
            UserAccount? researcher = await userRepo.GetUserAsync(request.ResearcherUsername.Trim().ToLowerInvariant(), ct);
            if (researcher == null || researcher.Role != UserRole.Researcher)
            {
                return Results.NotFound(new { error = $"Researcher '{request.ResearcherUsername}' does not exist or does not hold the Researcher role." });
            }

            UserAccount? subject = await userRepo.GetUserAsync(request.SubjectUsername.Trim().ToLowerInvariant(), ct);
            if (subject == null || subject.Role != UserRole.Patient)
            {
                return Results.NotFound(new { error = $"Patient '{request.SubjectUsername}' does not exist or does not hold the Patient role." });
            }

            SubjectGrant grant = await accessService.GrantSubjectAccessAsync(
                caller.Username,
                request.ResearcherUsername,
                request.SubjectUsername,
                request.AccessLevel,
                request.Notes,
                ct);

            return Results.Created($"/api/subject-access/grants/{grant.ResearcherUsername}/{grant.SubjectUsername}", grant);
        })
        .WithName("GrantSubjectAccess")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Grants a researcher tiered direct access to a patient or subject.")
        .WithDescription("Persists a direct grant and emits a cryptographically chained audit record (Administrator role required).")
        .Accepts<GrantSubjectAccessRequest>("application/json")
        .Produces<SubjectGrant>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/subject-access/grants/{researcherUsername}/{subjectUsername}", async (
            string researcherUsername,
            string subjectUsername,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Revokes a researcher's direct access to a patient.
            // WHY:  Allows administrators to revoke access when clinical oversight or trials conclude.
            // HOW:  Calls ISubjectAccessService.RevokeSubjectAccessAsync and records audit action.
            UserAccount? caller = (UserAccount)context.Items["CurrentUser"]!;
            bool revoked = await accessService.RevokeSubjectAccessAsync(caller.Username, researcherUsername, subjectUsername, ct);
            if (!revoked)
            {
                return Results.NotFound(new { error = $"Grant for researcher '{researcherUsername}' on subject '{subjectUsername}' not found." });
            }

            return Results.Ok(new { message = $"Grant for researcher '{researcherUsername}' on subject '{subjectUsername}' successfully revoked." });
        })
        .WithName("RevokeSubjectAccess")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Revokes a researcher's direct access to a patient.")
        .WithDescription("Removes the grant from the registry and records revocation in the audit ledger (Administrator role required).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/subject-access/my-subjects", async (
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Lists all subjects accessible to the authenticated caller with effective access tiers.
            // WHY:  Powers dropdown selectors and cohort views across Web UI and API clients.
            // HOW:  Queries accessible usernames and resolves effective tier per subject.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            IReadOnlyList<string> subjects = await accessService.GetAccessibleSubjectUsernamesAsync(caller.Username, caller.Role, SubjectAccessLevel.ReadAccess, ct);
            List<AccessibleSubjectItem> items = new(subjects.Count);
            foreach (string subj in subjects)
            {
                SubjectAccessLevel lvl = caller.Role == UserRole.Administrator
                    ? SubjectAccessLevel.FullReadAccess
                    : (await accessService.GetEffectiveAccessLevelAsync(caller.Username, subj, ct) ?? SubjectAccessLevel.ReadAccess);
                items.Add(new AccessibleSubjectItem
                {
                    SubjectUsername = subj,
                    EffectiveLevel = lvl
                });
            }

            return Results.Ok(items);
        })
        .WithName("GetMyAccessibleSubjects")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Lists all subjects accessible to the caller with effective access tiers.")
        .WithDescription("Returns accessible patients for dropdown selectors and cohort views (Administrator or Researcher role required).")
        .Produces<IReadOnlyList<AccessibleSubjectItem>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: Patient Cohort Groups (Administrator & Researcher)
        // ---------------------------------------------------------------------
        app.MapGet("/api/subject-groups", async (
            ISubjectAccessRepository repo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Lists patient cohort groups accessible to the calling identity.
            // WHY:  Administrators see all groups; Researchers see created cohorts and granted cohorts.
            // HOW:  Queries repository based on caller role and aggregates unique groups.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            if (caller.Role == UserRole.Administrator)
            {
                var allGroups = await repo.ListGroupsAsync(null, ct);
                return Results.Ok(allGroups);
            }

            var created = await repo.ListGroupsAsync(createdBy: caller.Username, ct);
            var granted = await repo.ListGroupGrantsAsync(researcherUsername: caller.Username, ct: ct);
            HashSet<string> groupIds = new(created.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
            List<SubjectGroup> result = [.. created];
            foreach (var g in granted)
            {
                if (groupIds.Add(g.GroupId))
                {
                    SubjectGroup? grp = await repo.GetGroupAsync(g.GroupId, ct);
                    if (grp != null)
                    {
                        result.Add(grp);
                    }
                }
            }

            return Results.Ok(result.OrderBy(g => g.Name, StringComparer.Ordinal).ToList());
        })
        .WithName("ListSubjectGroups")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Lists patient cohort groups accessible to the caller.")
        .WithDescription("Returns all cohort groups for Administrators, or created/granted cohorts for Researchers.")
        .Produces<IReadOnlyList<SubjectGroup>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/subject-groups", async (
            CreateSubjectGroupRequest request,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Creates a new patient cohort group.
            // WHY:  Enables organizing patients for study cohorts and delivery trials.
            // HOW:  Validates group name, enforces subject accessibility for researchers, and saves group.
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Group name is required.");
            }

            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            try
            {
                SubjectGroup group = await accessService.CreateGroupAsync(
                    caller.Username,
                    caller.Role,
                    request.Name,
                    request.Description,
                    request.SubjectUsernames ?? [],
                    ct);

                return Results.Created($"/api/subject-groups/{group.Id}", group);
            }
            catch (UnauthorizedAccessException ex)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, ex.Message);
            }
        })
        .WithName("CreateSubjectGroup")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Creates a new patient cohort group.")
        .WithDescription("Creates a cohort group header and enrolls initial members (validating subject accessibility for Researchers).")
        .Accepts<CreateSubjectGroupRequest>("application/json")
        .Produces<SubjectGroup>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapGet("/api/subject-groups/{groupId}", async (
            string groupId,
            ISubjectAccessRepository repo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Retrieves cohort group details by ID.
            // WHY:  Provides group metadata and enrollment numbers for cohort detail inspection.
            // HOW:  Validates caller access (Admin, group creator, or active group grant) and queries repository.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            SubjectGroup? group = await repo.GetGroupAsync(groupId, ct);
            if (group == null)
            {
                return Results.NotFound(new { error = $"Group '{groupId}' not found." });
            }

            if (caller.Role == UserRole.Researcher)
            {
                bool isCreator = string.Equals(group.CreatedBy, caller.Username, StringComparison.OrdinalIgnoreCase);
                SubjectGroupGrant? grant = await repo.GetGroupGrantAsync(groupId, caller.Username, ct);
                if (!isCreator && grant == null)
                {
                    return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You do not have access to this cohort group.");
                }
            }

            return Results.Ok(group);
        })
        .WithName("GetSubjectGroupById")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Retrieves cohort group details by ID.")
        .WithDescription("Returns group metadata if the caller is an Administrator or an authorized Researcher.")
        .Produces<SubjectGroup>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/api/subject-groups/{groupId}", async (
            string groupId,
            UpdateSubjectGroupRequest request,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Updates cohort group name and description.
            // WHY:  Allows maintaining study cohort information.
            // HOW:  Validates permissions via ISubjectAccessService.UpdateGroupAsync and persists updates.
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Group name is required.");
            }

            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            try
            {
                bool updated = await accessService.UpdateGroupAsync(
                    caller.Username,
                    caller.Role,
                    groupId,
                    request.Name,
                    request.Description,
                    ct);

                if (!updated)
                {
                    return Results.NotFound(new { error = $"Group '{groupId}' not found." });
                }

                return Results.Ok(new { message = $"Group '{groupId}' successfully updated." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, ex.Message);
            }
        })
        .WithName("UpdateSubjectGroup")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Updates cohort group name and description.")
        .WithDescription("Updates group metadata (Administrator or cohort creator only).")
        .Accepts<UpdateSubjectGroupRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/subject-groups/{groupId}", async (
            string groupId,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Permanently deletes a cohort group.
            // WHY:  Retires superseded study groups and cascades member/grant cleanup.
            // HOW:  Validates caller ownership and executes deletion via ISubjectAccessService.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            try
            {
                bool deleted = await accessService.DeleteGroupAsync(
                    caller.Username,
                    caller.Role,
                    groupId,
                    ct);

                if (!deleted)
                {
                    return Results.NotFound(new { error = $"Group '{groupId}' not found." });
                }

                return Results.Ok(new { message = $"Group '{groupId}' successfully deleted." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, ex.Message);
            }
        })
        .WithName("DeleteSubjectGroup")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Permanently deletes a cohort group.")
        .WithDescription("Deletes the cohort and cascades removal of members and group grants (Administrator or cohort creator only).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/subject-groups/{groupId}/members", async (
            string groupId,
            ISubjectAccessRepository repo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Lists enrolled subject usernames for a cohort group.
            // WHY:  Supplies membership lists for cohort analysis and trial delivery.
            // HOW:  Validates caller permissions and queries member usernames from repository.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            SubjectGroup? group = await repo.GetGroupAsync(groupId, ct);
            if (group == null)
            {
                return Results.NotFound(new { error = $"Group '{groupId}' not found." });
            }

            if (caller.Role == UserRole.Researcher)
            {
                bool isCreator = string.Equals(group.CreatedBy, caller.Username, StringComparison.OrdinalIgnoreCase);
                SubjectGroupGrant? grant = await repo.GetGroupGrantAsync(groupId, caller.Username, ct);
                if (!isCreator && grant == null)
                {
                    return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: You do not have access to this cohort group.");
                }
            }

            var members = await repo.ListGroupMemberUsernamesAsync(groupId, ct);
            return Results.Ok(members);
        })
        .WithName("ListGroupMembers")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Lists enrolled subject usernames for a cohort group.")
        .WithDescription("Returns all enrolled patient usernames for an accessible cohort group.")
        .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/subject-groups/{groupId}/members", async (
            string groupId,
            AddGroupMemberRequest request,
            ISubjectAccessService accessService,
            ISubjectAccessRepository repo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Enrolls a patient or subject into a cohort group.
            // WHY:  Expands cohort enrollment with strict boundary checks.
            // HOW:  Verifies target existence, enforces that researchers have active access to the candidate, and adds member.
            if (string.IsNullOrWhiteSpace(request.SubjectUsername))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Subject username is required.");
            }

            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            SubjectGroup? group = await repo.GetGroupAsync(groupId, ct);
            if (group == null)
            {
                return Results.NotFound(new { error = $"Group '{groupId}' not found." });
            }

            UserAccount? target = await userRepo.GetUserAsync(request.SubjectUsername.Trim().ToLowerInvariant(), ct);
            if (target == null || target.Role != UserRole.Patient)
            {
                return Results.NotFound(new { error = $"Patient '{request.SubjectUsername}' does not exist or does not hold the Patient role." });
            }

            try
            {
                bool added = await accessService.AddGroupMemberAsync(
                    caller.Username,
                    caller.Role,
                    groupId,
                    request.SubjectUsername,
                    ct);

                if (!added)
                {
                    return Results.Conflict(new { error = $"Subject '{request.SubjectUsername}' is already enrolled in group '{groupId}'." });
                }

                return Results.Created($"/api/subject-groups/{groupId}/members/{request.SubjectUsername.Trim().ToLowerInvariant()}", new { message = $"Subject '{request.SubjectUsername}' successfully enrolled in group '{groupId}'." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, ex.Message);
            }
        })
        .WithName("AddGroupMember")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Enrolls a patient into a cohort group.")
        .WithDescription("Enrolls an accessible patient into a cohort (Administrator or cohort creator only).")
        .Accepts<AddGroupMemberRequest>("application/json")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        app.MapDelete("/api/subject-groups/{groupId}/members/{subjectUsername}", async (
            string groupId,
            string subjectUsername,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Removes a patient from a cohort group.
            // WHY:  Allows pruning patients who discontinue trials or transfer cohorts.
            // HOW:  Validates permissions and removes member via ISubjectAccessService.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            try
            {
                bool removed = await accessService.RemoveGroupMemberAsync(
                    caller.Username,
                    caller.Role,
                    groupId,
                    subjectUsername,
                    ct);

                if (!removed)
                {
                    return Results.NotFound(new { error = $"Subject '{subjectUsername}' is not enrolled in group '{groupId}'." });
                }

                return Results.Ok(new { message = $"Subject '{subjectUsername}' successfully removed from group '{groupId}'." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, ex.Message);
            }
        })
        .WithName("RemoveGroupMember")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Removes a patient from a cohort group.")
        .WithDescription("Un-enrolls a patient from a cohort group (Administrator or cohort creator only).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Cohort Group Grants (Administrator Only)
        // ---------------------------------------------------------------------
        app.MapGet("/api/subject-groups/{groupId}/grants", async (
            string groupId,
            ISubjectAccessRepository repo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Lists active researcher access grants for a cohort group.
            // WHY:  Allows administrators to audit permissions granted over an entire cohort.
            // HOW:  Validates Administrator role, checks group existence, and queries group grants.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            if (caller.Role != UserRole.Administrator)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: Administrator role required for cohort group grants.");
            }

            SubjectGroup? group = await repo.GetGroupAsync(groupId, ct);
            if (group == null)
            {
                return Results.NotFound(new { error = $"Group '{groupId}' not found." });
            }

            var grants = await repo.ListGroupGrantsAsync(groupId: groupId, ct: ct);
            return Results.Ok(grants);
        })
        .WithName("ListGroupGrants")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Lists all researcher access grants for a cohort group.")
        .WithDescription("Returns active group access grants (Administrator role required).")
        .Produces<IReadOnlyList<SubjectGroupGrant>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/subject-groups/{groupId}/grants", async (
            string groupId,
            GrantGroupAccessRequest request,
            ISubjectAccessService accessService,
            ISubjectAccessRepository repo,
            IUserRepository userRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Grants a researcher tiered access over an entire cohort group.
            // WHY:  Allows administrators to assign study cohorts to researchers.
            // HOW:  Validates Administrator role, checks group and researcher existence, and saves group grant.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            if (caller.Role != UserRole.Administrator)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: Administrator role required for cohort group grants.");
            }

            if (string.IsNullOrWhiteSpace(request.ResearcherUsername))
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "Researcher username is required.");
            }

            SubjectGroup? group = await repo.GetGroupAsync(groupId, ct);
            if (group == null)
            {
                return Results.NotFound(new { error = $"Group '{groupId}' not found." });
            }

            UserAccount? researcher = await userRepo.GetUserAsync(request.ResearcherUsername.Trim().ToLowerInvariant(), ct);
            if (researcher == null || researcher.Role != UserRole.Researcher)
            {
                return Results.NotFound(new { error = $"Researcher '{request.ResearcherUsername}' does not exist or does not hold the Researcher role." });
            }

            SubjectGroupGrant grant = await accessService.GrantGroupAccessAsync(
                caller.Username,
                groupId,
                request.ResearcherUsername,
                request.AccessLevel,
                request.Notes,
                ct);

            return Results.Created($"/api/subject-groups/{groupId}/grants/{grant.ResearcherUsername}", grant);
        })
        .WithName("GrantGroupAccess")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Grants a researcher tiered access over an entire cohort group.")
        .WithDescription("Assigns cohort group access to a researcher and records audit entry (Administrator role required).")
        .Accepts<GrantGroupAccessRequest>("application/json")
        .Produces<SubjectGroupGrant>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/subject-groups/{groupId}/grants/{researcherUsername}", async (
            string groupId,
            string researcherUsername,
            ISubjectAccessService accessService,
            HttpContext context,
            CancellationToken ct) =>
        {
            // WHAT: Revokes a researcher's access to a cohort group.
            // WHY:  Allows administrators to revoke cohort access when study rotations conclude.
            // HOW:  Validates Administrator role, calls ISubjectAccessService.RevokeGroupAccessAsync, and logs audit record.
            UserAccount caller = (UserAccount)context.Items["CurrentUser"]!;
            if (caller.Role != UserRole.Administrator)
            {
                return SecureProblemResult(context, StatusCodes.Status403Forbidden, "Forbidden: Administrator role required for cohort group grants.");
            }

            bool revoked = await accessService.RevokeGroupAccessAsync(caller.Username, groupId, researcherUsername, ct);
            if (!revoked)
            {
                return Results.NotFound(new { error = $"Group grant for researcher '{researcherUsername}' on group '{groupId}' not found." });
            }

            return Results.Ok(new { message = $"Group grant for researcher '{researcherUsername}' on group '{groupId}' successfully revoked." });
        })
        .WithName("RevokeGroupAccess")
        .WithTags("Subject Access & Cohort Management")
        .WithSummary("Revokes a researcher's access to a cohort group.")
        .WithDescription("Removes the group grant and records audit entry (Administrator role required).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------------
        // REST API: Wet-Lab Liquid Handler Protocol Generation & Script Export
        // ---------------------------------------------------------------------
        app.MapPost("/api/fabrication/opentrons/generate", (
            GenerateOpentronsScriptRequest req,
            IFabricationService fabService,
            IOpentronsProtocolGenerator generator,
            HttpContext context) =>
        {
            // WHAT: Generates an Opentrons Protocol API v2 Python script and deck layout summary.
            // WHY:  Allows wet-lab researchers to automate DNA origami stoichiometric pipetting on OT-2 or Flex robots.
            // HOW:  Validates inputs, synthesizes fabrication package via IFabricationService, and builds protocol script.
            IResult? validationResult = ValidateOpentronsRequest(req, context);
            if (validationResult != null)
            {
                return validationResult;
            }

            OpentronsProtocolOptions options = req.Options ?? new();
            FabricationPackage package = fabService.GenerateFabricationPackage(
                req.SequenceOrSampleId,
                req.Regimen,
                string.IsNullOrWhiteSpace(req.Geometry) ? "Nanotube" : req.Geometry,
                req.TargetKd > 0 ? req.TargetKd : 4.8);

            OpentronsProtocolResult result = generator.GenerateProtocol(package, options);
            return Results.Ok(result);
        })
        .WithName("GenerateOpentronsProtocol")
        .WithTags("DNA Origami Fabrication")
        .WithSummary("Generates an automated Opentrons OT-2 or Flex Python protocol script for wet-lab stoichiometric pipetting.")
        .WithDescription("Synthesizes DNA origami staple routing, determines deck layouts, and generates an executable Opentrons Protocol API v2 script (Administrator or Researcher role required).")
        .Produces<OpentronsProtocolResult>(StatusCodes.Status200OK)
        .Produces<SecureProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/fabrication/opentrons/download", (
            GenerateOpentronsScriptRequest req,
            IFabricationService fabService,
            IOpentronsProtocolGenerator generator,
            HttpContext context) =>
        {
            // WHAT: Generates and streams a downloadable Opentrons Python protocol script (.py).
            // WHY:  Provides a direct file download for immediate transfer to liquid handler robot controllers.
            // HOW:  Validates parameters, generates Python protocol script, and streams as UTF-8 attachment.
            IResult? validationResult = ValidateOpentronsRequest(req, context);
            if (validationResult != null)
            {
                return validationResult;
            }

            OpentronsProtocolOptions options = req.Options ?? new();
            FabricationPackage package = fabService.GenerateFabricationPackage(
                req.SequenceOrSampleId,
                req.Regimen,
                string.IsNullOrWhiteSpace(req.Geometry) ? "Nanotube" : req.Geometry,
                req.TargetKd > 0 ? req.TargetKd : 4.8);

            OpentronsProtocolResult result = generator.GenerateProtocol(package, options);
            byte[] scriptBytes = System.Text.Encoding.UTF8.GetBytes(result.PythonScript);
            return Results.File(scriptBytes, "text/x-python; charset=utf-8", "nanoweaver_origami_opentrons.py");
        })
        .WithName("DownloadOpentronsProtocol")
        .WithTags("DNA Origami Fabrication")
        .WithSummary("Generates and downloads an executable Opentrons Python protocol script file.")
        .WithDescription("Streams the generated Python protocol script directly as an attachment with MIME type text/x-python (Administrator or Researcher role required).")
        .Produces(StatusCodes.Status200OK, contentType: "text/x-python")
        .Produces<SecureProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------------
        // REST API: Trajectory Parquet Analytics Query
        // ---------------------------------------------------------------------
        app.MapPost("/api/trajectories/analytics/query", async (
            TrajectoryAnalyticsApiRequest request,
            ITrajectoryAnalyticsService analyticsService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // WHAT: Validates input, executes read-only SQL query via DuckDbTrajectoryAnalyticsService, and returns tabular analytics result.
            // WHY:  Permits researchers and computational nanotechnologists to run ad-hoc columnar SQL directly over trajectory Parquet datasets.
            // HOW:  Validates request non-null, maps to TrajectoryAnalyticsQuery, invokes ExecuteQueryAsync, wraps in TrajectoryAnalyticsApiResponse.
            if (request is null || string.IsNullOrWhiteSpace(request.DatasetName) || string.IsNullOrWhiteSpace(request.Query))
            {
                return SecureProblemResult(httpContext, StatusCodes.Status400BadRequest, "Dataset name and query must not be empty.");
            }

            try
            {
                int maxRows = request.MaxRows.GetValueOrDefault(1000);
                TrajectoryAnalyticsQuery query = new(
                    DatasetName: request.DatasetName.Trim(),
                    Query: request.Query.Trim(),
                    MaxRows: maxRows > 0 ? maxRows : 1000);

                TrajectoryAnalyticsResult result = await analyticsService.ExecuteQueryAsync(query, ct);
                TrajectoryAnalyticsApiResponse response = new(
                    DatasetName: request.DatasetName,
                    Columns: result.Columns,
                    Rows: result.Rows,
                    TotalRows: result.TotalRows,
                    ElapsedMilliseconds: result.ElapsedMilliseconds);

                return Results.Ok(response);
            }
            catch (FileNotFoundException fnfEx)
            {
                return SecureProblemResult(httpContext, StatusCodes.Status404NotFound, fnfEx.Message);
            }
            catch (InvalidOperationException invEx)
            {
                return SecureProblemResult(httpContext, StatusCodes.Status400BadRequest, invEx.Message);
            }
        })
        .WithName("QueryTrajectoryAnalytics")
        .WithTags("Trajectories")
        .WithSummary("Executes a read-only analytical SQL query against a trajectory dataset.")
        .WithDescription("Runs columnar SQL queries directly over trajectory Parquet datasets or in-memory analytical sessions via DuckDB.")
        .Produces<TrajectoryAnalyticsApiResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        app.Run();
    }

    /// <summary>
    /// Re-encrypts patient profile fields that are still protected by the historic published key.
    /// </summary>
    /// <param name="userRepository">Repository enumerating the node's user accounts.</param>
    /// <param name="profileRepository">Repository reading and writing patient profiles.</param>
    /// <param name="protector">Profile protector reporting legacy decryptions through its counter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of profiles that were re-encrypted under the node's current PHI master key.</returns>
    /// <remarks>
    /// <para>
    /// <strong>What this is:</strong> a one-time upgrade pass. Before the node generated its own PHI master
    /// key, patient avatars and profile fields were encrypted with the SHA-256 of a constant published in this
    /// repository. Those values are still readable (the protector keeps that key as a decrypt-only fallback),
    /// but they are not actually protected from anyone who has read the source, so they are rewritten with the
    /// node's own key.
    /// </para>
    /// <para>
    /// <strong>Why it is not an <c>IDatabaseMigration</c>:</strong> Prime Directive 13 governs schema
    /// evolution, and this pass adds no table, column, index or seed row — it rewrites the value of an existing
    /// column through the ordinary repository API, which already applies every pending schema migration when it
    /// opens its connection. There is no DDL here to put in a migration class, and none is executed ad hoc.
    /// </para>
    /// <para>
    /// <strong>How it works:</strong> for each account, the profile is read (which decrypts) and the
    /// protector's legacy-decryption counter is sampled either side of the read. If the counter moved, that row
    /// carried pre-upgrade ciphertext, so the profile is written straight back; the write encrypts with the
    /// current key. Rows already using the current key are left completely untouched, so a steady-state
    /// restart performs no writes at all.
    /// </para>
    /// </remarks>
    internal static async Task<int> ReEncryptLegacyProfilePhiAsync(
        IUserRepository userRepository,
        IUserProfileRepository profileRepository,
        ProfileDataProtector protector,
        CancellationToken ct = default)
    {
        int upgraded = 0;
        IReadOnlyList<UserAccount> accounts = await userRepository.ListUsersAsync(ct);

        foreach (UserAccount account in accounts)
        {
            long legacyBefore = protector.LegacyDecryptionCount;
            UserProfile? profile = await profileRepository.GetProfileAsync(account.Username, ct);
            if (profile is null)
            {
                continue;
            }

            if (protector.LegacyDecryptionCount == legacyBefore)
            {
                continue;
            }

            await profileRepository.UpsertProfileAsync(profile, ct);
            upgraded++;
        }

        return upgraded;
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
    /// Declares the minimum authorization role an authenticated identity must hold to reach a REST API route.
    /// </summary>
    /// <remarks>
    /// Prime Directive 12 defines three roles. <see cref="UserRole.Administrator"/> owns cluster oversight,
    /// user lifecycle, database overrides and configuration management; <see cref="UserRole.Researcher"/> owns
    /// simulation execution, trajectory streaming, the endocrine hub and dataset export/import;
    /// <see cref="UserRole.Patient"/> may only view its own personalized results and download its own DNA payload.
    /// </remarks>
    public enum RouteRoleRequirement
    {
        /// <summary>
        /// Any authenticated identity whose access gate permits REST API transport may call the route.
        /// </summary>
        None,

        /// <summary>
        /// Only the Administrator and Researcher roles may call the route.
        /// </summary>
        AdministratorOrResearcher,

        /// <summary>
        /// Only the Administrator role may call the route.
        /// </summary>
        AdministratorOnly
    }

    /// <summary>
    /// Outcome of an authorization decision on a per-subject identity, profile, or PHI record route.
    /// </summary>
    /// <remarks>
    /// Prime Directive 12 grants the Researcher role "simulation execution, trajectory streaming, endocrine hub
    /// access, and dataset export/import" — patient records are not in that grant — and scopes the Patient role
    /// to <em>personalized</em> results. Only the Administrator role spans subjects.
    /// </remarks>
    public enum SubjectRecordAccessDecision
    {
        /// <summary>
        /// The caller may proceed: it is the subject of the record, or it holds the Administrator role.
        /// </summary>
        Allowed,

        /// <summary>
        /// The caller may not proceed and is told so plainly, because the route carries no subject whose
        /// existence could be inferred from the rejection (for example the user administration collection).
        /// </summary>
        Forbidden,

        /// <summary>
        /// The caller may not proceed and the rejection is masked as an ordinary 404, so that requesting
        /// another subject's record is indistinguishable from requesting a record that does not exist.
        /// </summary>
        MaskedAsNotFound
    }

    /// <summary>
    /// Sub-resources beneath '/api/patients/{username}' and '/api/users/{username}' that hold protected health
    /// information and are therefore readable and writable only by their own subject or an Administrator.
    /// </summary>
    private static readonly string[] SubjectHealthRecordResources = ["dna", "imports", "results"];

    /// <summary>
    /// Sub-resources beneath '/api/users/{username}' that hold self-service identity details.
    /// </summary>
    private static readonly string[] SubjectSelfServiceResources = ["profile", "addresses", "contacts"];

    /// <summary>
    /// Rejection sentence published whenever a caller asks for a subject record it may not see.
    /// </summary>
    /// <remarks>
    /// Deliberately identical for "this subject's record exists but is not yours" and "no such subject",
    /// so that a Patient cannot enumerate the other patients registered on the node.
    /// </remarks>
    private const string SubjectRecordNotFoundDetail = "The requested patient record was not found.";

    /// <summary>
    /// Describes a single role-gated REST API route pattern.
    /// </summary>
    /// <param name="Pattern">Absolute route path spelled out in complete URL segments (e.g. '/api/configuration').</param>
    /// <param name="MatchDescendants">True to gate every descendant route beneath the pattern as well as the pattern itself.</param>
    /// <remarks>
    /// Matching uses <see cref="PathString.StartsWithSegments(PathString, StringComparison)"/> and
    /// <see cref="PathString.Equals(PathString, StringComparison)"/>, both of which respect URL segment boundaries.
    /// That is deliberate: the historical gate tested the partial segment '/api/config', which never matched the
    /// real '/api/configuration' route and therefore left every configuration override reachable by any role.
    /// Every pattern below must be a complete segment path that a mapped endpoint actually serves.
    /// </remarks>
    private readonly record struct RoleGatedRoute(string Pattern, bool MatchDescendants);

    /// <summary>
    /// Routes reserved exclusively for the Administrator role regardless of HTTP verb.
    /// </summary>
    private static readonly RoleGatedRoute[] AdministratorRoutes =
    [
        new("/api/configuration", MatchDescendants: true),
        new("/api/share-keys", MatchDescendants: false),
        new("/api/share-keys/create", MatchDescendants: false),
        new("/api/compliance/de-identify", MatchDescendants: false),
        new("/api/compliance/crypto-shred", MatchDescendants: false),
        new("/api/compliance/encrypt-record", MatchDescendants: false),
        new("/api/compliance/decrypt-record", MatchDescendants: false),
        new("/api/compliance/status", MatchDescendants: false),
        new("/api/compliance/audit-ledger", MatchDescendants: false),
        // Trust anchors for peer dataset verification are cluster oversight under Prime Directive 12.
        new("/api/peer-keys", MatchDescendants: true),
        // Direct subject access grants are cluster oversight under Prime Directive 12.
        new("/api/subject-access/grants", MatchDescendants: true)
    ];

    /// <summary>
    /// Routes whose state-changing verbs are reserved exclusively for the Administrator role.
    /// </summary>
    /// <remarks>
    /// '/api/services' appears here and in <see cref="ResearchRoutes"/> on purpose, and the two entries do not
    /// contradict each other: <see cref="ResolveRouteRoleRequirement(string, PathString)"/> tests the
    /// Administrator tables first and therefore returns the stricter answer. Actively probing a dependency
    /// (<c>POST /api/services/ping/{serviceId}</c>) stays Administrator-only, while merely reading the stack
    /// telemetry is also available to a Researcher.
    /// </remarks>
    private static readonly RoleGatedRoute[] AdministratorMutationRoutes =
    [
        new("/api/services", MatchDescendants: true)
    ];

    /// <summary>
    /// Routes reserved for the Administrator and Researcher roles regardless of HTTP verb.
    /// </summary>
    /// <remarks>
    /// '/api/services' is here because reading the stack telemetry — which of PostgreSQL, Valkey, oxDNA and
    /// LAMMPS are reachable, and how quickly they answer — is cluster infrastructure diagnostics. Prime
    /// Directive 12 gives cluster oversight to the Administrator and computational work to the Researcher who
    /// depends on those services; a Patient's mandate is its own results and DNA payloads, so a Patient token
    /// must not be able to map the node's backing services. The stricter mutation entry above still reserves
    /// the on-demand probe verb for the Administrator.
    /// </remarks>
    private static readonly RoleGatedRoute[] ResearchRoutes =
    [
        new("/api/services", MatchDescendants: true),
        new("/api/parser/process", MatchDescendants: true),
        new("/api/parser/export-parquet", MatchDescendants: true),
        new("/api/simulations", MatchDescendants: true),
        new("/api/fabrication/generate", MatchDescendants: true),
        new("/api/endocrine/analyze", MatchDescendants: true),
        new("/api/endocrine/toggle", MatchDescendants: false),
        new("/api/datasets/export", MatchDescendants: false),
        new("/api/datasets/import", MatchDescendants: false),
        new("/api/datasets/export-encrypted", MatchDescendants: false),
        new("/api/datasets/exports", MatchDescendants: true),
        new("/api/datasets/download", MatchDescendants: true),
        new("/api/trajectories/download", MatchDescendants: true),
        new("/api/compliance/verify-ledger", MatchDescendants: false),
        new("/api/subject-access/my-subjects", MatchDescendants: false),
        new("/api/subject-groups", MatchDescendants: true),
        new("/api/fabrication/opentrons", MatchDescendants: true)
    ];

    /// <summary>
    /// File extensions that the trajectory download endpoint is permitted to stream.
    /// </summary>
    /// <remarks>
    /// The shared simulation volume also holds the authoritative SQLite identity database
    /// ('weaver_config.db', plus its '-wal' and '-shm' side files). An allow-list — rather than a
    /// deny-list — guarantees that neither the database nor any future non-trajectory artifact
    /// placed in that directory can ever be streamed to an API client, at any role.
    /// </remarks>
    private static readonly string[] DownloadableTrajectoryExtensions =
    [
        ".xyz", ".dcd", ".dump", ".lammpstrj", ".top", ".dat", ".conf", ".in"
    ];

    /// <summary>
    /// File extensions that the dataset export download endpoint is permitted to stream.
    /// </summary>
    private static readonly string[] DownloadableExportExtensions =
    [
        ".gz", ".tgz", ".tar", ".zip", ".json", ".parquet", ".sig", ".pqc"
    ];

    /// <summary>
    /// Replacement text substituted for any configuration value that carries a credential or secret.
    /// </summary>
    private const string RedactedConfigurationValue = "[REDACTED]";

    /// <summary>
    /// JSON serialization options used for RFC 7807 problem detail rejection bodies.
    /// </summary>
    private static readonly JsonSerializerOptions SecureProblemJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Resolves the minimum authorization role required to reach a REST API route.
    /// </summary>
    /// <param name="method">HTTP verb of the incoming request (e.g. 'GET', 'POST').</param>
    /// <param name="path">Request path of the incoming request.</param>
    /// <returns>The strictest <see cref="RouteRoleRequirement"/> matching the route, or <see cref="RouteRoleRequirement.None"/>.</returns>
    /// <remarks>
    /// Resolution is a pure function of verb and path so that it can be exhaustively unit tested; the
    /// authentication middleware merely applies the answer. Read-only GET requests are evaluated against the
    /// unconditional tables only, so listing endpoints stay broadly readable while every state-changing verb
    /// on an administrative resource is gated.
    /// </remarks>
    public static RouteRoleRequirement ResolveRouteRoleRequirement(string method, PathString path)
    {
        // WHAT: Resolves the role requirement for a route from four declarative, segment-exact route tables.
        // WHY:  Prime Directive 12 demands strict role gating, and the previous hand-maintained string-prefix
        //       chain silently matched nothing ('/api/config' is not a segment prefix of '/api/configuration',
        //       and no route ever began '/api/stack'), leaving administrative routes open to every role.
        // HOW:  Tests the unconditional tables first, then the mutation-only table for non-GET verbs,
        //       returning the strictest requirement that matches the request.
        ArgumentNullException.ThrowIfNull(method);

        bool isReadOnly = HttpMethods.IsGet(method);

        if (MatchesAnyRoute(path, AdministratorRoutes) || (!isReadOnly && MatchesAnyRoute(path, AdministratorMutationRoutes)))
        {
            return RouteRoleRequirement.AdministratorOnly;
        }

        if (MatchesAnyRoute(path, ResearchRoutes))
        {
            return RouteRoleRequirement.AdministratorOrResearcher;
        }

        return RouteRoleRequirement.None;
    }

    /// <summary>
    /// Resolves whether a caller may reach a per-subject identity, profile, or PHI record route.
    /// </summary>
    /// <param name="subPath">Path remainder below '/api/users' or '/api/patients' (e.g. '/alice/dna/42').</param>
    /// <param name="callerUsername">Username taken from the validated session, never from the request body.</param>
    /// <param name="callerRole">Role taken from the validated session.</param>
    /// <param name="grantedLevel">Optional tiered access level granted to the researcher over the target subject.</param>
    /// <param name="httpMethod">Optional HTTP method requested.</param>
    /// <returns>The access decision the authentication middleware must apply.</returns>
    /// <remarks>
    /// The subject is read out of the route itself and compared against the authenticated identity, so a caller
    /// can never widen its own scope with a client-supplied identifier. Researchers with active grants may access
    /// genomic or profile resources corresponding to their granted tier.
    /// </remarks>
    public static SubjectRecordAccessDecision ResolveSubjectRecordAccess(
        string? subPath,
        string callerUsername,
        UserRole callerRole,
        SubjectAccessLevel? grantedLevel = null,
        string? httpMethod = null)
    {
        // WHAT: Decides self / Administrator / tiered Researcher / deny for patient record and profile sub-resources.
        // WHY:  Allows researchers with configurable tiers (ReadAccess: 1, GenomicAccess: 2, FullReadAccess: 3)
        //       to access permitted resources while masking unauthorized requests as 404 to prevent subject enumeration.
        // HOW:  Splits the subject username off the route remainder, compares with session identity and granted tiers.
        ArgumentNullException.ThrowIfNull(callerUsername);

        if (callerRole == UserRole.Administrator)
        {
            return SubjectRecordAccessDecision.Allowed;
        }

        string sub = (subPath ?? string.Empty).TrimStart('/');
        int slashIndex = sub.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex <= 0)
        {
            // Administrative collection routes such as '/api/users' or '/api/users/{username}' carry no
            // subject sub-resource, so a plain rejection discloses nothing.
            return SubjectRecordAccessDecision.Forbidden;
        }

        string targetSubject = sub[..slashIndex];
        string resource = sub[(slashIndex + 1)..];
        bool isSelf = string.Equals(callerUsername, targetSubject, StringComparison.OrdinalIgnoreCase);

        bool matchesHealth = MatchesSubjectResource(resource, SubjectHealthRecordResources);
        bool matchesSelfService = MatchesSubjectResource(resource, SubjectSelfServiceResources);

        if (matchesHealth)
        {
            if (isSelf)
            {
                return SubjectRecordAccessDecision.Allowed;
            }

            if (callerRole == UserRole.Researcher && grantedLevel.HasValue && grantedLevel.Value >= SubjectAccessLevel.GenomicAccess)
            {
                return SubjectRecordAccessDecision.Allowed;
            }

            return SubjectRecordAccessDecision.MaskedAsNotFound;
        }

        if (matchesSelfService)
        {
            if (isSelf)
            {
                return SubjectRecordAccessDecision.Allowed;
            }

            if (callerRole == UserRole.Researcher && grantedLevel.HasValue)
            {
                bool isGet = string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase);

                if (resource.StartsWith("profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (isGet && grantedLevel.Value >= SubjectAccessLevel.ReadAccess)
                    {
                        return SubjectRecordAccessDecision.Allowed;
                    }
                    return SubjectRecordAccessDecision.Forbidden;
                }

                if (resource.StartsWith("addresses", StringComparison.OrdinalIgnoreCase) ||
                    resource.StartsWith("contacts", StringComparison.OrdinalIgnoreCase))
                {
                    if (isGet && grantedLevel.Value >= SubjectAccessLevel.FullReadAccess)
                    {
                        return SubjectRecordAccessDecision.Allowed;
                    }
                    return SubjectRecordAccessDecision.Forbidden;
                }
            }

            return SubjectRecordAccessDecision.Forbidden;
        }

        return SubjectRecordAccessDecision.Forbidden;
    }

    /// <summary>
    /// Produces a non-disclosing descriptor for a configured storage directory.
    /// </summary>
    /// <param name="environmentVariableName">Name of the environment variable that configures the directory.</param>
    /// <returns>A sentence naming the configuring variable without revealing the absolute path.</returns>
    /// <remarks>
    /// Health telemetry answers "is storage reachable", which the accompanying writable flags express. The
    /// absolute mount path is server topology that Prime Directive 14 keeps off every API response, so it is
    /// replaced by a pointer to the setting that controls it.
    /// </remarks>
    public static string DescribeStorageLocation(string environmentVariableName)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableName);

        return $"Configured by {environmentVariableName}; absolute path withheld.";
    }

    /// <summary>
    /// Determines whether a route remainder addresses one of the named subject sub-resources.
    /// </summary>
    /// <param name="resource">Route remainder below the subject username (e.g. 'dna/42').</param>
    /// <param name="resourceNames">Sub-resource names to test against.</param>
    /// <returns>True when the remainder addresses one of the named sub-resources; otherwise false.</returns>
    private static bool MatchesSubjectResource(string resource, string[] resourceNames)
    {
        foreach (string resourceName in resourceNames)
        {
            if (resource.StartsWith(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a request path matches any pattern in a role-gated route table.
    /// </summary>
    /// <param name="path">Request path to test.</param>
    /// <param name="routes">Route table to test against.</param>
    /// <returns>True when the path matches at least one pattern; otherwise false.</returns>
    private static bool MatchesAnyRoute(PathString path, RoleGatedRoute[] routes)
    {
        // WHAT: Scans a route table for a segment-boundary match against the request path.
        // WHY:  Segment-aware comparison prevents both partial-segment misses and over-broad prefix matches.
        // HOW:  Uses StartsWithSegments for descendant patterns and an exact Equals for leaf patterns.
        foreach (RoleGatedRoute route in routes)
        {
            if (route.MatchDescendants
                ? path.StartsWithSegments(route.Pattern, StringComparison.OrdinalIgnoreCase)
                : path.Equals(route.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an authenticated role satisfies a resolved route role requirement.
    /// </summary>
    /// <param name="role">Role carried by the authenticated identity.</param>
    /// <param name="requirement">Requirement resolved by <see cref="ResolveRouteRoleRequirement(string, PathString)"/>.</param>
    /// <returns>True when the role is permitted to proceed; otherwise false.</returns>
    public static bool IsRoleAuthorizedForRequirement(UserRole role, RouteRoleRequirement requirement) => requirement switch
    {
        RouteRoleRequirement.AdministratorOnly => role == UserRole.Administrator,
        RouteRoleRequirement.AdministratorOrResearcher => role is UserRole.Administrator or UserRole.Researcher,
        _ => true
    };

    /// <summary>
    /// Returns the human-readable rejection sentence describing an unsatisfied route role requirement.
    /// </summary>
    /// <param name="requirement">The unsatisfied requirement.</param>
    /// <returns>A rejection sentence safe to publish to API clients.</returns>
    private static string DescribeRoleRequirement(RouteRoleRequirement requirement) => requirement switch
    {
        RouteRoleRequirement.AdministratorOnly => "Forbidden: Administrator role required for this administrative resource.",
        _ => "Forbidden: Administrator or Researcher role required to execute simulations, analyses, dataset transfers, and infrastructure telemetry."
    };

    /// <summary>
    /// Determines whether a configuration key carries a credential, connection string, or other secret value.
    /// </summary>
    /// <param name="key">Configuration key such as 'Storage:PostgresConnectionString'.</param>
    /// <returns>True when the key's value must never be published to an API client; otherwise false.</returns>
    public static bool IsSecretConfigurationKey(string key)
    {
        // WHAT: Classifies a configuration key as secret-bearing.
        // WHY:  Prime Directive 14 forbids disclosing database connection strings or credentials to API
        //       clients at any role, so these values are redacted rather than merely role-gated.
        // HOW:  Delegates to the configuration store, which owns the authoritative IsSecret catalog flag,
        //       so the REST API and the Web UI configuration screen cannot drift apart.
        return SystemConfigurationStore.IsSecretConfigurationKey(key);
    }

    /// <summary>
    /// Produces a copy of a configuration listing in which every secret-bearing value is redacted.
    /// </summary>
    /// <param name="items">Configuration items resolved from the database, environment, and code defaults.</param>
    /// <returns>A new list in which secret-bearing active values and defaults read '[REDACTED]'.</returns>
    public static IReadOnlyList<ConfigurationItem> RedactSecretConfigurationValues(IReadOnlyList<ConfigurationItem> items)
    {
        // WHAT: Rewrites secret-bearing configuration values before they leave the process.
        // WHY:  GET /api/configuration previously returned the live PostgreSQL connection string, password
        //       included, to every authenticated caller — a direct Prime Directive 14 violation.
        // HOW:  Copies each record, substituting the redaction placeholder for both the active and default value.
        ArgumentNullException.ThrowIfNull(items);

        List<ConfigurationItem> redacted = new(items.Count);
        foreach (ConfigurationItem item in items)
        {
            // The item's own IsSecret flag is authoritative, and the key classifier is a second, independent
            // check: an item constructed without the flag still cannot leak a credential-bearing value.
            redacted.Add(item.IsSecret || IsSecretConfigurationKey(item.Key)
                ? item with { Value = RedactedConfigurationValue, DefaultValue = RedactedConfigurationValue, IsSecret = true }
                : item);
        }

        return redacted;
    }

    /// <summary>
    /// Determines whether a requested download file name carries one of the permitted extensions.
    /// </summary>
    /// <param name="fileName">Bare file name (already stripped of directory components).</param>
    /// <param name="permittedExtensions">Allow-list of permitted lower-case file extensions.</param>
    /// <returns>True when the file may be streamed to the caller; otherwise false.</returns>
    public static bool IsDownloadableFileName(string fileName, string[] permittedExtensions)
    {
        // WHAT: Enforces an extension allow-list on a download request.
        // WHY:  The shared simulation volume also stores the authoritative identity/PHI database, so a
        //       deny-list would eventually be outrun by a new artifact landing in the same directory.
        // HOW:  Extracts the final extension and checks membership in the supplied allow-list.
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(permittedExtensions);

        string extension = Path.GetExtension(fileName);
        foreach (string permitted in permittedExtensions)
        {
            if (string.Equals(extension, permitted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Serializes an RFC 7807 <see cref="SecureProblemDetails"/> rejection body for the active request.
    /// </summary>
    /// <param name="context">Active HTTP context supplying the trace identifier and request path.</param>
    /// <param name="statusCode">HTTP status code of the rejection.</param>
    /// <param name="detail">Rejection sentence. Always an explicitly published sentence, never raw exception text.</param>
    /// <param name="includeSetupRequiredFlag">True to append the 'setupRequired' RFC 7807 extension member.</param>
    /// <returns>Indented JSON problem details text.</returns>
    private static string SerializeSecureProblem(HttpContext context, int statusCode, string detail, bool includeSetupRequiredFlag)
    {
        // WHAT: Builds the single rejection schema used by the authentication middleware and auth/setup endpoints.
        // WHY:  Prime Directive 14 mandates SecureProblemDetails for API errors; hand-rolled '{ error }' objects
        //       gave API clients a second, incompatible error schema.
        // HOW:  Serializes SecureProblemDetails, then attaches the optional RFC 7807 extension member.
        SecureProblemDetails problem = new()
        {
            Type = SecureExceptionSanitizer.GetProblemType(statusCode),
            Title = SecureExceptionSanitizer.GetTitle(statusCode),
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            TraceId = context.TraceIdentifier,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Justification: SerializeToNode never returns null for a non-null reference-type instance.
        JsonObject payload = JsonSerializer.SerializeToNode(problem, SecureProblemJsonOptions)!.AsObject();
        if (includeSetupRequiredFlag)
        {
            payload["setupRequired"] = true;
        }

        return payload.ToJsonString(SecureProblemJsonOptions);
    }

    /// <summary>
    /// Writes an RFC 7807 <see cref="SecureProblemDetails"/> rejection directly to the HTTP response.
    /// </summary>
    /// <param name="context">Active HTTP context.</param>
    /// <param name="statusCode">HTTP status code of the rejection.</param>
    /// <param name="detail">Rejection sentence safe to publish to API clients.</param>
    /// <param name="includeSetupRequiredFlag">True to append the 'setupRequired' RFC 7807 extension member.</param>
    /// <returns>A task that completes once the rejection body has been written.</returns>
    private static async Task WriteSecureProblemAsync(HttpContext context, int statusCode, string detail, bool includeSetupRequiredFlag = false)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsync(SerializeSecureProblem(context, statusCode, detail, includeSetupRequiredFlag));
    }

    /// <summary>
    /// Builds an RFC 7807 <see cref="SecureProblemDetails"/> rejection result for a minimal API endpoint handler.
    /// </summary>
    /// <param name="context">Active HTTP context.</param>
    /// <param name="statusCode">HTTP status code of the rejection.</param>
    /// <param name="detail">Rejection sentence safe to publish to API clients.</param>
    /// <returns>An <see cref="IResult"/> producing the problem details body.</returns>
    public static IResult SecureProblemResult(HttpContext context, int statusCode, string detail)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Results.Text(
            SerializeSecureProblem(context, statusCode, detail, includeSetupRequiredFlag: false),
            "application/problem+json; charset=utf-8",
            statusCode: statusCode);
    }

    /// <summary>
    /// Validates that a browser session token exchange request did not originate from an untrusted cross-origin site.
    /// </summary>
    /// <param name="context">The active HTTP request context.</param>
    /// <param name="configRepo">The system configuration repository holding the configured public endpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the request origin is permitted; otherwise, <c>false</c>.</returns>
    private static async Task<bool> IsAllowedWebTokenOriginAsync(
        HttpContext context,
        IConfigurationRepository configRepo,
        CancellationToken ct)
    {
        // WHAT: Checks Sec-Fetch-Site and Origin headers against the request host and configured public origin.
        // WHY:  /api/auth/web-token sets a browser session cookie; rejecting cross-origin requests blocks cross-site
        //       session fixation while still permitting reverse-proxy overlays (Caddy/Nginx) where Origin matches the public domain.
        // HOW:  Rejects Sec-Fetch-Site values other than 'same-origin'/'none', and when Origin is present, verifies its host
        //       matches HttpContext.Request.Host, Peer:CollaborativeWorkerEndpoint, or the DOMAIN environment variable.
        string? secFetchSite = context.Request.Headers["Sec-Fetch-Site"];
        if (!string.IsNullOrWhiteSpace(secFetchSite) &&
            !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? originHeader = context.Request.Headers.Origin;
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out Uri? originUri))
        {
            return false;
        }

        if (string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string configuredEndpoint = await configRepo.GetValueAsync("Peer:CollaborativeWorkerEndpoint", "http://localhost:5000", ct);
        if (Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out Uri? configuredUri) &&
            string.Equals(originUri.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? envDomain = Environment.GetEnvironmentVariable("DOMAIN");
        if (!string.IsNullOrWhiteSpace(envDomain) &&
            string.Equals(originUri.Host, envDomain.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates a bearer token without ever propagating a malformed-encoding exception to the caller.
    /// </summary>
    /// <param name="authService">Authentication service performing signature and claim validation.</param>
    /// <param name="token">Raw bearer token taken from the Authorization header or session cookie.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validation outcome; a malformed token encoding is reported as an ordinary validation failure.</returns>
    private static async Task<(bool Valid, UserAccount? User, string? ErrorReason)> TryValidateTokenAsync(
        IUserAuthenticationService authService,
        string token,
        CancellationToken ct)
    {
        // WHAT: Converts a malformed token encoding into a plain validation failure.
        // WHY:  Token decoding throws FormatException on non-Base64 input, which surfaced as an unhandled 500
        //       (and, under ASPNETCORE_ENVIRONMENT=Development, a full stack trace) on the /healthz and /health
        //       liveness probes, and as a misleading 400 instead of 401 on /api routes.
        // HOW:  Awaits the real validation inside a narrowly scoped FormatException handler.
        try
        {
            return await authService.ValidateTokenAsync(token, ct);
        }
        catch (FormatException)
        {
            return (false, null, "Malformed bearer token encoding.");
        }
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
                // WHAT: Reports storage accessibility without ever naming a local filesystem path.
                // WHY:  Prime Directive 14 forbids disclosing local filesystem paths to API clients and grants
                //       no role an exemption; the absolute mount layout is server topology, not health data.
                // HOW:  Publishes the configuring environment variable name plus the accessibility booleans.
                //       An operator who needs the configured values reads them from the Administrator-gated
                //       /api/configuration surface instead.
                Storage = new StorageHealthInfo
                {
                    Status = "Healthy",
                    IncomingDirectory = DescribeStorageLocation("TRAJECTORY_INCOMING_DIR"),
                    IncomingWritable = Directory.Exists(incomingDir),
                    ExportDirectory = DescribeStorageLocation("TRAJECTORY_EXPORT_DIR"),
                    ExportWritable = Directory.Exists(exportDir),
                    ParquetDirectory = DescribeStorageLocation("TRAJECTORY_PARQUET_DIR"),
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

    /// <summary>
    /// Client-safe confirmation message returned when a subject's private decryption key is destroyed.
    /// </summary>
    internal const string ShredOkMessage =
        "Subject private decryption key destroyed in this node's key store (GDPR Article 17 erasure). Sealed copies remain in any backup taken before this moment until that backup is expired.";

    /// <summary>
    /// Formats the client-safe message returned when a crypto-shred request targets an already-shredded subject.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The formatted client-safe message.</returns>
    internal static string ShredAlreadyMessage(string subjectId) =>
        $"Subject '{subjectId}' was already crypto-shredded; nothing further was destroyed.";

    /// <summary>
    /// Formats the client-safe message returned when a crypto-shred request targets an unknown subject.
    /// </summary>
    /// <param name="subjectId">The subject identifier.</param>
    /// <returns>The formatted client-safe message.</returns>
    internal static string ShredUnknownMessage(string subjectId) =>
        $"This node holds no stored key for subject '{subjectId}'; nothing was erased.";

    /// <summary>
    /// Validates that a supplied patient identifier is non-empty, at most 64 characters, and contains only safe ASCII identifier characters.
    /// </summary>
    /// <param name="patientId">The candidate patient identifier to validate.</param>
    /// <returns><c>true</c> if the identifier is syntactically valid; otherwise <c>false</c>.</returns>
    internal static bool IsValidPatientIdentifier(string patientId)
    {
        // WHAT: Checks length and character set of a patient identifier query parameter.
        // WHY:  Prevents log/manifest injection or path-like characters in synthesized DNA package metadata.
        // HOW:  Enforces 1..64 characters drawn from ASCII letters, digits, '-', '_', or '.'.
        if (patientId.Length == 0 || patientId.Length > 64)
        {
            return false;
        }

        foreach (char c in patientId)
        {
            bool allowed = char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether the authenticated caller has genomic access (Tier 2 or above) over the target patient records.
    /// </summary>
    /// <param name="currentUser">The authenticated caller account.</param>
    /// <param name="username">The target patient username.</param>
    /// <param name="context">The HTTP request context.</param>
    /// <returns>True if access is authorized; otherwise false.</returns>
    internal static bool HasGenomicSubjectAccess([NotNullWhen(true)] UserAccount? currentUser, string username, HttpContext context)
    {
        if (currentUser == null)
        {
            return false;
        }

        if (currentUser.Role == UserRole.Administrator)
        {
            return true;
        }

        if (string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentUser.Role == UserRole.Researcher &&
            context.Items["SubjectAccessLevel"] is SubjectAccessLevel lvl)
        {
            return lvl >= SubjectAccessLevel.GenomicAccess;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the authenticated caller has full read access (Tier 3) over patient addresses or contacts.
    /// </summary>
    /// <param name="currentUser">The authenticated caller account.</param>
    /// <param name="username">The target patient username.</param>
    /// <param name="context">The HTTP request context.</param>
    /// <returns>True if access is authorized; otherwise false.</returns>
    internal static bool HasFullReadSubjectAccess([NotNullWhen(true)] UserAccount? currentUser, string username, HttpContext context)
    {
        if (currentUser == null)
        {
            return false;
        }

        if (currentUser.Role == UserRole.Administrator)
        {
            return true;
        }

        if (string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentUser.Role == UserRole.Researcher &&
            context.Items["SubjectAccessLevel"] is SubjectAccessLevel lvl)
        {
            return lvl >= SubjectAccessLevel.FullReadAccess;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the authenticated caller has profile read access (Tier 1 or above) over patient demographic profile.
    /// </summary>
    /// <param name="currentUser">The authenticated caller account.</param>
    /// <param name="username">The target patient username.</param>
    /// <param name="context">The HTTP request context.</param>
    /// <returns>True if access is authorized; otherwise false.</returns>
    internal static bool HasProfileSubjectAccess([NotNullWhen(true)] UserAccount? currentUser, string username, HttpContext context)
    {
        if (currentUser == null)
        {
            return false;
        }

        if (currentUser.Role == UserRole.Administrator)
        {
            return true;
        }

        if (string.Equals(currentUser.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentUser.Role == UserRole.Researcher &&
            context.Items["SubjectAccessLevel"] is SubjectAccessLevel lvl)
        {
            return lvl >= SubjectAccessLevel.ReadAccess;
        }

        return false;
    }

    /// <summary>
    /// Validates an incoming Opentrons liquid handler script generation request.
    /// </summary>
    /// <param name="req">The protocol generation request payload.</param>
    /// <param name="context">The active HTTP context.</param>
    /// <returns>A rejection <see cref="IResult"/> when validation fails; otherwise, <c>null</c>.</returns>
    internal static IResult? ValidateOpentronsRequest(GenerateOpentronsScriptRequest req, HttpContext context)
    {
        // WHAT: Enforces biophysical parameter boundaries and non-empty sequence inputs.
        // WHY:  A liquid handler protocol cannot be synthesized without valid stoichiometry and sequence targets.
        // HOW:  Inspects sequence, options boundaries (volume, concentration, excess ratio), and dissociation constant.
        if (string.IsNullOrWhiteSpace(req.SequenceOrSampleId))
        {
            return SecureProblemResult(context, StatusCodes.Status400BadRequest, "SequenceOrSampleId cannot be empty.");
        }

        if (req.Options != null)
        {
            if (req.Options.TotalReactionVolumeMicroliters <= 0)
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "TotalReactionVolumeMicroliters must be greater than zero.");
            }

            if (req.Options.ScaffoldConcentrationNanomolar <= 0)
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "ScaffoldConcentrationNanomolar must be greater than zero.");
            }

            if (req.Options.StapleMolarExcessRatio <= 0)
            {
                return SecureProblemResult(context, StatusCodes.Status400BadRequest, "StapleMolarExcessRatio must be greater than zero.");
            }
        }

        if (req.TargetKd < 0)
        {
            return SecureProblemResult(context, StatusCodes.Status400BadRequest, "TargetKd cannot be negative.");
        }

        return null;
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
