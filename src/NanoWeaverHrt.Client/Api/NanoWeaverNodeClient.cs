using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations.Models;
using NanoWeaverHrt;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Default implementation of <see cref="INanoWeaverNodeClient"/> communicating with nanoweaver-hrt instances
/// via REST API and executing client-side asymmetric PKI challenge signing.
/// </summary>
public sealed class NanoWeaverNodeClient : INanoWeaverNodeClient
{
    /// <summary>
    /// Shared JSON serializer options for client DTO deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// The HTTP client instance used for REST transmissions.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// The key ring vault used for AES-GCM envelope unsealing and cryptographic signing.
    /// </summary>
    private readonly IClientKeyRingVault _keyRingVault;

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoWeaverNodeClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for REST API requests.</param>
    /// <param name="keyRingVault">The key ring vault used for AES-GCM unsealing and cryptographic signing.</param>
    public NanoWeaverNodeClient(HttpClient httpClient, IClientKeyRingVault keyRingVault)
    {
        // WHAT: Initializes REST API client dependencies.
        // WHY:  Decouples transport (HttpClient) and cryptographic unsealing (IClientKeyRingVault) for zero-trust client security.
        // HOW:  Validates non-null arguments and assigns internal fields.
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(keyRingVault);
        _httpClient = httpClient;
        _keyRingVault = keyRingVault;
    }

    /// <inheritdoc/>
    public async Task<NodeAuthSession> AuthenticateAsync(
        InstanceProfile profile,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Executes zero-knowledge asymmetric challenge-response authentication.
        // WHY:  Passwords are never stored; nodes verify digital signatures via Ed25519 or ML-DSA-65.
        // HOW:  1. POST /api/auth/challenge with username to receive challenge nonce.
        //       2. Unseal private key with passphrase and sign the challenge nonce.
        //       3. POST /api/auth/token with username, challengeId, and signatureBase64.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.EncryptedPrivateKeyEnvelopeBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.KeyAlgorithm);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);

        // Step 1: Request Challenge
        Uri challengeUri = new(baseUri, "/api/auth/challenge");
        using HttpRequestMessage challengeReq = new(HttpMethod.Post, challengeUri)
        {
            Content = JsonContent.Create(new { username = profile.Username })
        };

        using HttpResponseMessage challengeResp = await _httpClient.SendAsync(challengeReq, ct);
        if (!challengeResp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(challengeResp, ct);
        }

        ChallengeResponsePayload? challenge = await challengeResp.Content.ReadFromJsonAsync<ChallengeResponsePayload>(JsonOptions, ct);
        if (challenge == null || string.IsNullOrWhiteSpace(challenge.ChallengeId) || string.IsNullOrWhiteSpace(challenge.NonceBase64))
        {
            throw new NodeApiException((int)challengeResp.StatusCode, "Invalid challenge response received from node.");
        }

        // Step 2: Unseal and Sign Challenge
        byte[] nonceBytes = Convert.FromBase64String(challenge.NonceBase64);
        string signatureBase64 = _keyRingVault.SignChallenge(
            profile.KeyAlgorithm,
            profile.EncryptedPrivateKeyEnvelopeBase64,
            passphrase.Span,
            nonceBytes);

        // Step 3: Exchange for Token
        Uri tokenUri = new(baseUri, "/api/auth/token");
        using HttpRequestMessage tokenReq = new(HttpMethod.Post, tokenUri)
        {
            Content = JsonContent.Create(new
            {
                username = profile.Username,
                challengeId = challenge.ChallengeId,
                signatureBase64
            })
        };

        using HttpResponseMessage tokenResp = await _httpClient.SendAsync(tokenReq, ct);
        if (!tokenResp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(tokenResp, ct);
        }

        TokenResponsePayload? tokenResult = await tokenResp.Content.ReadFromJsonAsync<TokenResponsePayload>(JsonOptions, ct);
        if (tokenResult == null || string.IsNullOrWhiteSpace(tokenResult.Token))
        {
            throw new NodeApiException((int)tokenResp.StatusCode, "Invalid token response received from node.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        int expiresIn = tokenResult.ExpiresInSeconds > 0 ? tokenResult.ExpiresInSeconds : 28800;
        string role = tokenResult.User?.Role ?? profile.Role;
        string username = tokenResult.User?.Username ?? profile.Username;

        return new NodeAuthSession(
            tokenResult.Token,
            tokenResult.TokenType ?? "Bearer",
            expiresIn,
            nowUtc,
            nowUtc.AddSeconds(expiresIn),
            username,
            role);
    }

    /// <inheritdoc/>
    public async Task<InstanceConnectionSnapshot> PollSnapshotAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Polls telemetry and simulation status to construct a point-in-time instance snapshot.
        // WHY:  Allows multi-instance coordinator and UI to monitor GPU load, throughput, and status.
        // HOW:  Sends GET /api/system/telemetry with Bearer auth; handles transport errors and 401/403 states gracefully.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        try
        {
            Uri telemetryUri = new(baseUri, "/api/system/telemetry");
            using HttpRequestMessage req = new(HttpMethod.Get, telemetryUri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new InstanceConnectionSnapshot(
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceConnectionState.Unauthorized,
                    0.0,
                    0.0,
                    0.0,
                    0,
                    0.0,
                    nowUtc,
                    "Unauthorized (HTTP 401/403): Token expired or rejected.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                NodeApiException apiEx = await NodeApiException.CreateAsync(resp, ct);
                return new InstanceConnectionSnapshot(
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceConnectionState.Degraded,
                    0.0,
                    0.0,
                    0.0,
                    0,
                    0.0,
                    nowUtc,
                    apiEx.Title);
            }

            NodeTelemetrySnapshot? telemetry = await resp.Content.ReadFromJsonAsync<NodeTelemetrySnapshot>(JsonOptions, ct);
            if (telemetry == null)
            {
                return new InstanceConnectionSnapshot(
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceConnectionState.Degraded,
                    0.0,
                    0.0,
                    0.0,
                    0,
                    0.0,
                    nowUtc,
                    "Empty or malformed telemetry payload received.");
            }

            InstanceConnectionState state = telemetry.GpuTemperatureCelsius >= 85.0
                ? InstanceConnectionState.Degraded
                : InstanceConnectionState.Online;

            string? errorTitle = telemetry.GpuTemperatureCelsius >= 85.0
                ? "Elevated GPU temperature (thermal throttling risk)"
                : null;

            return new InstanceConnectionSnapshot(
                profile.InstanceId,
                profile.DisplayName,
                state,
                telemetry.CpuUtilizationPercent,
                telemetry.GpuUtilizationPercent,
                telemetry.GpuTemperatureCelsius,
                telemetry.ActiveSimulationCount,
                telemetry.FramesPerSecond,
                nowUtc,
                errorTitle);
        }
        catch (HttpRequestException ex)
        {
            return new InstanceConnectionSnapshot(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                nowUtc,
                ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new InstanceConnectionSnapshot(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                nowUtc,
                "Connection timed out.");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> StartSimulationCampaignAsync(
        InstanceProfile profile,
        string bearerToken,
        string simulationType,
        CancellationToken ct = default)
    {
        // WHAT: Triggers a named simulation campaign on the target node.
        // WHY:  Permits remote orchestrators to execute oxDNA, LAMMPS, and PBPK campaigns.
        // HOW:  Validates campaign slug, builds POST /api/simulations/run/{slug}, and verifies 2xx response.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(simulationType);

        string normalizedSlug = simulationType.Trim().ToLowerInvariant();
        if (normalizedSlug is not ("serum-stability" or "aptamer-gating" or "payload-leakage" or "pbpk-endocrine"))
        {
            throw new ArgumentException(
                $"Unsupported simulation type '{simulationType}'. Must be one of: serum-stability, aptamer-gating, payload-leakage, pbpk-endocrine.",
                nameof(simulationType));
        }

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri runUri = new(baseUri, $"/api/simulations/run/{normalizedSlug}");

        using HttpRequestMessage req = new(HttpMethod.Post, runUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<NodeTelemetrySnapshot> GetTelemetryAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries raw node telemetry metrics from /api/system/telemetry.
        // WHY:  Provides detailed metrics on GC heaps, working set, frames parsed, and accelerator backends.
        // HOW:  Sends GET request with Bearer authorization and deserializes NodeTelemetrySnapshot.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri telemetryUri = new(baseUri, "/api/system/telemetry");

        using HttpRequestMessage req = new(HttpMethod.Get, telemetryUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        NodeTelemetrySnapshot? result = await resp.Content.ReadFromJsonAsync<NodeTelemetrySnapshot>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty telemetry response received.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SimulationCampaignSummary>> GetCampaignsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries the catalog of available simulation campaigns from /api/simulations/campaigns.
        // WHY:  Enumerates available oxDNA and LAMMPS campaigns supported by the node.
        // HOW:  Sends GET request with Bearer authorization and deserializes campaign list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri campaignsUri = new(baseUri, "/api/simulations/campaigns");

        using HttpRequestMessage req = new(HttpMethod.Get, campaignsUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<SimulationCampaignSummary>? result = await resp.Content.ReadFromJsonAsync<List<SimulationCampaignSummary>>(JsonOptions, ct);
        return result ?? (IReadOnlyList<SimulationCampaignSummary>)Array.Empty<SimulationCampaignSummary>();
    }

    /// <inheritdoc/>
    public async Task<EndocrineProfileSummary> GetEndocrineProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries the active endocrine regulatory profile from /api/endocrine/profile.
        // WHY:  Allows client GUI to display target steroid concentrations and dominance regime.
        // HOW:  Sends GET request with Bearer authorization and deserializes EndocrineProfileSummary.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endocrineUri = new(baseUri, "/api/endocrine/profile");

        using HttpRequestMessage req = new(HttpMethod.Get, endocrineUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        EndocrineProfileSummary? result = await resp.Content.ReadFromJsonAsync<EndocrineProfileSummary>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty endocrine profile response received.");
    }

    /// <inheritdoc/>
    public async Task<ClosedLoopSimulationResult> SimulateClosedLoopEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        ClosedLoopSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches closed-loop numerical integration parameters to POST /api/endocrine/closed-loop/simulate.
        // WHY:  Allows desktop and mobile clients to model dynamic LH/FSH feedback trajectories.
        // HOW:  Validates inputs, sets Bearer header, serializes request JSON, and returns ClosedLoopSimulationResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/endocrine/closed-loop/simulate");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        ClosedLoopSimulationResult? result = await resp.Content.ReadFromJsonAsync<ClosedLoopSimulationResult>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty simulation result response received.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FederationPeerNode>> DiscoverFederationPeersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Discovers active P2P trajectory federation peers from GET /api/federation/peers.
        // WHY:  Enables clients to visualize swarm topology and distributed node health.
        // HOW:  Validates arguments, sends authenticated GET request, and deserializes peer list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/federation/peers");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<FederationPeerNode>? result = await resp.Content.ReadFromJsonAsync<List<FederationPeerNode>>(JsonOptions, ct);
        return (IReadOnlyList<FederationPeerNode>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<TrajectoryFederationManifest> BuildFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        FederationBuildManifestRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Builds and signs a Merkle chunk manifest via POST /api/federation/manifests/build.
        // WHY:  Prepares local simulation trajectories for decentralized P2P federation transfer.
        // HOW:  Validates arguments, posts request payload with Bearer token, and deserializes manifest.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/federation/manifests/build");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        TrajectoryFederationManifest? result = await resp.Content.ReadFromJsonAsync<TrajectoryFederationManifest>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty federation manifest response received.");
    }

    /// <inheritdoc/>
    public async Task<FederationVerifyManifestResponse> VerifyFederationManifestAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryFederationManifest manifest,
        CancellationToken ct = default)
    {
        // WHAT: Cryptographically verifies a federation manifest via POST /api/federation/manifests/verify.
        // WHY:  Guarantees integrity of incoming trajectory chunk trees before accepting payloads.
        // HOW:  Validates arguments, posts manifest payload with Bearer header, and deserializes response.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(manifest);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/federation/manifests/verify");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(manifest, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        FederationVerifyManifestResponse? result = await resp.Content.ReadFromJsonAsync<FederationVerifyManifestResponse>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty federation verification response received.");
    }

    /// <inheritdoc/>
    public async Task<GelAnalysisResult> AnalyzeGelAsync(
        InstanceProfile profile,
        string bearerToken,
        GelAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits agarose gel electrophoresis image data to POST /api/fabrication/gel/analyze.
        // WHY:  Performs automated lane detection, band segmentation, and folding yield quantification.
        // HOW:  Validates inputs, transmits request payload with Bearer token, and deserializes analysis result.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/fabrication/gel/analyze");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        GelAnalysisResult? result = await resp.Content.ReadFromJsonAsync<GelAnalysisResult>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty gel analysis response received.");
    }

    /// <inheritdoc/>
    public async Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Triggers an on-demand accelerator hardware qualification run via POST /api/system/benchmark/run.
        // WHY:  Measures FP32/FP64 TFLOPS and memory bandwidth before launching large-scale simulations.
        // HOW:  Validates profile and token, posts empty content with Bearer header, and deserializes report.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/system/benchmark/run");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AcceleratorBenchmarkReport? result = await resp.Content.ReadFromJsonAsync<AcceleratorBenchmarkReport>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty accelerator benchmark response received.");
    }

    /// <inheritdoc/>
    public async Task<AcceleratorBenchmarkReport?> GetLatestBenchmarkAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches the most recent cached accelerator qualification report via GET /api/system/benchmark/latest.
        // WHY:  Allows client dashboards to display accelerator readiness without re-running compute tests.
        // HOW:  Validates inputs, sends GET with Bearer header; returns null on 404 NotFound, or deserializes report.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/system/benchmark/latest");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return await resp.Content.ReadFromJsonAsync<AcceleratorBenchmarkReport>(JsonOptions, ct);
    }

    /// <inheritdoc/>
    public async Task<OpentronsProtocolResult> GenerateOpentronsProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Generates an Opentrons Protocol API v2 script via POST /api/fabrication/opentrons/generate.
        // WHY:  Automates wet-lab DNA origami stoichiometric liquid handling pipetting.
        // HOW:  Validates inputs, posts script generation parameters with Bearer header, and deserializes protocol result.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/fabrication/opentrons/generate");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        OpentronsProtocolResult? result = await resp.Content.ReadFromJsonAsync<OpentronsProtocolResult>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty Opentrons protocol response received.");
    }

    /// <inheritdoc/>
    public async Task<PatientDigitalDnaPackage> GetPatientDnaRegimensAsync(
        InstanceProfile profile,
        string bearerToken,
        string? patientId = null,
        CancellationToken ct = default)
    {
        // WHAT: Fetches a patient-specific Digital DNA blueprint package via GET /api/patient/dna.
        // WHY:  Retrieves synthesized staple routing, annealing ramps, and SHA-256 provenance for the client.
        // HOW:  Constructs endpoint with optional patientId query param, sends GET with Bearer token, and deserializes package.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        string relativePath = string.IsNullOrWhiteSpace(patientId)
            ? "/api/patient/dna"
            : $"/api/patient/dna?patientId={Uri.EscapeDataString(patientId)}";

        Uri endpointUri = new(baseUri, relativePath);

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientDigitalDnaPackage? result = await resp.Content.ReadFromJsonAsync<PatientDigitalDnaPackage>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty patient DNA package response received.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDigitalDnaRecord>> GetPatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Queries persistent digitized DNA records for a patient via GET /api/patients/{username}/dna.
        // WHY:  Allows reviewing stored nanocarrier architecture records and clinical regimens.
        // HOW:  Validates arguments, sends GET with Bearer header, and deserializes list of records.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/dna");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientDigitalDnaRecord>? result = await resp.Content.ReadFromJsonAsync<List<PatientDigitalDnaRecord>>(JsonOptions, ct);
        return (IReadOnlyList<PatientDigitalDnaRecord>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Discovers trajectory coordinate files via GET /api/datasets.
        // WHY:  Allows desktop and mobile operators to view active simulation coordinate files.
        // HOW:  Validates profile and token, sends authenticated GET request, and deserializes file descriptors.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/datasets");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<TrajectoryFileItem>? result = await resp.Content.ReadFromJsonAsync<List<TrajectoryFileItem>>(JsonOptions, ct);
        return (IReadOnlyList<TrajectoryFileItem>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<DatasetManifest> ExportDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches dataset bundle export via POST /api/datasets/export.
        // WHY:  Enforces cryptographic manifest generation and tarball archiving from client.
        // HOW:  Validates inputs, sends POST with JSON payload, and returns immutable DatasetManifest.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/datasets/export");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        DatasetManifest? result = await resp.Content.ReadFromJsonAsync<DatasetManifest>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty dataset export manifest received.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves stack configuration settings via GET /api/configuration.
        // WHY:  Allows administrators to audit configuration state and database overrides.
        // HOW:  Validates arguments, sends GET with Bearer token, and deserializes configuration list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/configuration");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<ConfigurationItem>? result = await resp.Content.ReadFromJsonAsync<List<ConfigurationItem>>(JsonOptions, ct);
        return (IReadOnlyList<ConfigurationItem>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task SetConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        ConfigOverrideRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Persists a configuration override key-value pair via POST /api/configuration/override.
        // WHY:  Allows administrators to update active runtime settings with database precedence.
        // HOW:  Validates parameters, sends POST with override DTO, and verifies HTTP 200 response.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/configuration/override");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteConfigurationOverrideAsync(
        InstanceProfile profile,
        string bearerToken,
        string key,
        CancellationToken ct = default)
    {
        // WHAT: Reverts a configuration override to default via DELETE /api/configuration/override/{key}.
        // WHY:  Allows administrators to restore environment variable defaults for specific keys.
        // HOW:  Validates arguments, sends DELETE request to override URL, and verifies success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/configuration/override/{Uri.EscapeDataString(key)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGrant>> ListSubjectGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string? researcher = null,
        string? subject = null,
        CancellationToken ct = default)
    {
        // WHAT: Queries active researcher subject grants via GET /api/subject-access/grants.
        // WHY:  Allows auditing tiered clinical trial and genomic review permissions.
        // HOW:  Builds query parameters, sends GET with Bearer token, and deserializes subject grant list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        string queryString = string.Empty;
        if (!string.IsNullOrWhiteSpace(researcher) || !string.IsNullOrWhiteSpace(subject))
        {
            List<string> qParams = [];
            if (!string.IsNullOrWhiteSpace(researcher))
            {
                qParams.Add($"researcher={Uri.EscapeDataString(researcher)}");
            }
            if (!string.IsNullOrWhiteSpace(subject))
            {
                qParams.Add($"subject={Uri.EscapeDataString(subject)}");
            }
            queryString = "?" + string.Join("&", qParams);
        }
        Uri endpointUri = new(baseUri, "/api/subject-access/grants" + queryString);

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<SubjectGrant>? result = await resp.Content.ReadFromJsonAsync<List<SubjectGrant>>(JsonOptions, ct);
        return (IReadOnlyList<SubjectGrant>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<ComplianceStatusReport> GetComplianceStatusAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves regulatory compliance scorecard via GET /api/compliance/status.
        // WHY:  Allows administrators to audit post-quantum encryption, HIPAA, GDPR, and FDA 21 CFR Part 11 status.
        // HOW:  Validates arguments, sends GET with Bearer token, and deserializes ComplianceStatusReport.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/status");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        ComplianceStatusReport? result = await resp.Content.ReadFromJsonAsync<ComplianceStatusReport>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty compliance status response received.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AuditRecord>> GetAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        int? limit = null,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves immutable audit ledger records via GET /api/compliance/audit-ledger.
        // WHY:  Permits verification of FDA 21 CFR Part 11 hash-chained audit trails.
        // HOW:  Appends optional limit query parameter, sends authenticated GET, and deserializes list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        string queryString = limit.HasValue ? $"?limit={limit.Value}" : string.Empty;
        Uri endpointUri = new(baseUri, "/api/compliance/audit-ledger" + queryString);

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<AuditRecord>? result = await resp.Content.ReadFromJsonAsync<List<AuditRecord>>(JsonOptions, ct);
        return (IReadOnlyList<AuditRecord>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<PatientGenomicProfile> AnalyzeGenomicsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Evaluates genetic markers via POST /api/genomics/analyze.
        // WHY:  Enables automated risk detection (thrombophilia, AR insensitivity) and kinetic dosage adjustments.
        // HOW:  Validates inputs, sends POST with JSON request, and deserializes PatientGenomicProfile.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/genomics/analyze");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientGenomicProfile? result = await resp.Content.ReadFromJsonAsync<PatientGenomicProfile>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty genomic analysis response received.");
    }

    /// <inheritdoc/>
    public async Task<AuditLedgerVerificationResult> VerifyAuditLedgerAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches audit ledger cryptographic verification request via GET /api/compliance/verify-ledger.
        // WHY:  Mathematically checks SHA-384 unbroken hash-chaining and NIST FIPS 204 signatures.
        // HOW:  Validates inputs, executes GET request with Bearer token, and deserializes verification report.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/verify-ledger");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AuditLedgerVerificationResult? result = await resp.Content.ReadFromJsonAsync<AuditLedgerVerificationResult>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty audit ledger verification response received.");
    }

    /// <inheritdoc/>
    public async Task<PatientGenomicProfile> AnalyzeGenomicSequenceAsync(
        InstanceProfile profile,
        string bearerToken,
        SequenceAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Submits raw FASTA nucleotide sequence to POST /api/genomics/analyze-sequence.
        // WHY:  Evaluates CpG dinucleotide density (TLR9) and AR CAG repeat tracts from sequences.
        // HOW:  Validates inputs, transmits request payload with Bearer token, and deserializes genomic profile.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/genomics/analyze-sequence");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientGenomicProfile? result = await resp.Content.ReadFromJsonAsync<PatientGenomicProfile>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty sequence analysis response received.");
    }

    /// <inheritdoc/>
    public async Task<TrajectoryAnalyticsApiResponse> QueryTrajectoryAnalyticsAsync(
        InstanceProfile profile,
        string bearerToken,
        TrajectoryAnalyticsApiRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Executes read-only columnar SQL query via POST /api/trajectories/analytics/query.
        // WHY:  Enables remote researchers to query Parquet simulation frames directly via DuckDB.
        // HOW:  Validates arguments, sends POST with analytical query request, and deserializes response.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/trajectories/analytics/query");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        TrajectoryAnalyticsApiResponse? result = await resp.Content.ReadFromJsonAsync<TrajectoryAnalyticsApiResponse>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty trajectory analytics query response received.");
    }

    /// <inheritdoc/>
    public async Task<PeerKeyListResponse> ListPeerKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves registered peer keys and local node identity via GET /api/peer-keys.
        // WHY:  Allows administrators to audit trusted peer laboratories and dataset signers.
        // HOW:  Validates arguments, sends authenticated GET, and deserializes PeerKeyListResponse.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/peer-keys");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PeerKeyListResponse? result = await resp.Content.ReadFromJsonAsync<PeerKeyListResponse>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty peer keys response received.");
    }

    /// <inheritdoc/>
    public async Task<PeerKeyRecord> RegisterPeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        RegisterPeerKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Registers a trusted peer laboratory public key via POST /api/peer-keys.
        // WHY:  Enforces cryptographic provenance verification on federated dataset bundles.
        // HOW:  Validates inputs, transmits registration payload with Bearer token, and deserializes PeerKeyRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/peer-keys");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PeerKeyRecord? result = await resp.Content.ReadFromJsonAsync<PeerKeyRecord>(JsonOptions, ct);
        return result ?? throw new NodeApiException((int)resp.StatusCode, "Empty register peer key response received.");
    }

    /// <inheritdoc/>
    public async Task RevokePeerKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        string keyId,
        CancellationToken ct = default)
    {
        // WHAT: Revokes a peer laboratory public key via POST /api/peer-keys/{keyId}/revoke.
        // WHY:  Terminates trust for compromised or retired peer signing keys.
        // HOW:  Validates inputs, dispatches authenticated POST request, and checks HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/peer-keys/{Uri.EscapeDataString(keyId)}/revoke");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<SubjectGrant> CreateSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        GrantSubjectAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-access/grants to grant researcher direct access to a patient.
        // WHY:  Allows administrators to assign tiered patient access permissions.
        // HOW:  Validates parameters, posts serialized JSON payload with Bearer header, and deserializes SubjectGrant.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/subject-access/grants");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SubjectGrant? grant = await resp.Content.ReadFromJsonAsync<SubjectGrant>(JsonOptions, ct);
        return grant ?? throw new InvalidOperationException("Endpoint returned empty or null subject grant payload.");
    }

    /// <inheritdoc/>
    public async Task RevokeSubjectGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string researcherUsername,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-access/grants/{researcherUsername}/{subjectUsername}.
        // WHY:  Allows revoking researcher direct access to a patient.
        // HOW:  Validates parameters, sends authenticated DELETE request, and checks HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(researcherUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectUsername);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-access/grants/{Uri.EscapeDataString(researcherUsername)}/{Uri.EscapeDataString(subjectUsername)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGroup>> ListSubjectGroupsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries accessible patient cohort groups via GET /api/subject-groups.
        // WHY:  Enables managing study cohort groups and trials.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes list of SubjectGroup.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/subject-groups");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<SubjectGroup>? result = await resp.Content.ReadFromJsonAsync<List<SubjectGroup>>(JsonOptions, ct);
        return (IReadOnlyList<SubjectGroup>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<SubjectGroup> CreateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-groups to create a new patient cohort group.
        // WHY:  Enables clinical and study trial cohort creation.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and deserializes SubjectGroup.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/subject-groups");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SubjectGroup? group = await resp.Content.ReadFromJsonAsync<SubjectGroup>(JsonOptions, ct);
        return group ?? throw new InvalidOperationException("Endpoint returned empty or null subject group payload.");
    }

    /// <inheritdoc/>
    public async Task DeleteSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-groups/{groupId} to permanently delete a cohort group.
        // WHY:  Retires superseded study groups.
        // HOW:  Validates inputs, sends authenticated DELETE request, and checks HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries all created single-use share keys via GET /api/share-keys.
        // WHY:  Allows monitoring and managing active peer federation invitation links.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes list of ShareKeyRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/share-keys");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<ShareKeyRecord>? result = await resp.Content.ReadFromJsonAsync<List<ShareKeyRecord>>(JsonOptions, ct);
        return (IReadOnlyList<ShareKeyRecord>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListGroupMembersAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId}/members to list enrolled subjects.
        // WHY:  Allows inspecting cohort membership for studies and delivery protocols.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes string username list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/members");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<string>? result = await resp.Content.ReadFromJsonAsync<List<string>>(JsonOptions, ct);
        return (IReadOnlyList<string>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task AddGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        AddGroupMemberRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-groups/{groupId}/members to enroll a subject.
        // WHY:  Allows expanding cohort enrollment.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and verifies HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/members");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveGroupMemberAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string subjectUsername,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-groups/{groupId}/members/{subjectUsername} to un-enroll a subject.
        // WHY:  Allows withdrawing a subject from a cohort group.
        // HOW:  Validates inputs, sends authenticated DELETE request, and verifies HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectUsername);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/members/{Uri.EscapeDataString(subjectUsername)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubjectGroupGrant>> ListGroupGrantsAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId}/grants to list group-level grants.
        // WHY:  Allows administrators to audit permissions granted over an entire cohort group.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes SubjectGroupGrant list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/grants");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<SubjectGroupGrant>? result = await resp.Content.ReadFromJsonAsync<List<SubjectGroupGrant>>(JsonOptions, ct);
        return (IReadOnlyList<SubjectGroupGrant>?)result ?? [];
    }

    /// <inheritdoc/>
    public async Task<SubjectGroupGrant> CreateGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        GrantGroupAccessRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/subject-groups/{groupId}/grants to grant a researcher access over a cohort group.
        // WHY:  Allows administrators to assign group-level access permissions.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and deserializes SubjectGroupGrant.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/grants");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SubjectGroupGrant? grant = await resp.Content.ReadFromJsonAsync<SubjectGroupGrant>(JsonOptions, ct);
        return grant ?? throw new InvalidOperationException("Endpoint returned empty or null subject group grant payload.");
    }

    /// <inheritdoc/>
    public async Task RevokeGroupGrantAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        string researcherUsername,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/subject-groups/{groupId}/grants/{researcherUsername} to revoke a group-level grant.
        // WHY:  Allows administrators to revoke group access permissions.
        // HOW:  Validates inputs, sends authenticated DELETE request, and verifies HTTP success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(researcherUsername);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}/grants/{Uri.EscapeDataString(researcherUsername)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<ShareKeyRecord> CreateShareKeyAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/share-keys/create to generate a single-use collaborative share key.
        // WHY:  Allows creating peering invitation tokens with configurable expiration and permissions.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and deserializes ShareKeyRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/share-keys/create");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        ShareKeyRecord? record = await resp.Content.ReadFromJsonAsync<ShareKeyRecord>(JsonOptions, ct);
        return record ?? throw new InvalidOperationException("Endpoint returned empty or null share key payload.");
    }

    /// <inheritdoc/>
    public async Task<ShareKeyConsumeResult> RedeemShareKeyAsync(
        InstanceProfile profile,
        RedeemShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/share-keys/redeem to consume a single-use share key.
        // WHY:  Allows remote instances to authenticate and establish a federated session without prior credentials.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes ShareKeyConsumeResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/share-keys/redeem");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        ShareKeyConsumeResult? result = await resp.Content.ReadFromJsonAsync<ShareKeyConsumeResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null share key redemption payload.");
    }

    /// <inheritdoc/>
    public async Task<SubjectGroup> GetSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-groups/{groupId} to fetch group metadata.
        // WHY:  Allows inspecting cohort group details and properties.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes SubjectGroup.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SubjectGroup? group = await resp.Content.ReadFromJsonAsync<SubjectGroup>(JsonOptions, ct);
        return group ?? throw new InvalidOperationException("Endpoint returned empty or null subject group payload.");
    }

    /// <inheritdoc/>
    public async Task<SubjectGroup> UpdateSubjectGroupAsync(
        InstanceProfile profile,
        string bearerToken,
        string groupId,
        UpdateSubjectGroupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches PUT /api/subject-groups/{groupId} to update group name and description.
        // WHY:  Allows administrators and managing researchers to update cohort group attributes.
        // HOW:  Validates inputs, sends authenticated PUT with JSON payload, and deserializes updated SubjectGroup.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/subject-groups/{Uri.EscapeDataString(groupId)}");

        using HttpRequestMessage req = new(HttpMethod.Put, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SubjectGroup? group = await resp.Content.ReadFromJsonAsync<SubjectGroup>(JsonOptions, ct);
        return group ?? throw new InvalidOperationException("Endpoint returned empty or null subject group payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AccessibleSubjectItem>> ListMySubjectsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/subject-access/my-subjects to list caller accessible subjects.
        // WHY:  Allows clients to populate subject dropdowns filtered to authorized patient identifiers.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes AccessibleSubjectItem list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/subject-access/my-subjects");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<AccessibleSubjectItem>? list = await resp.Content.ReadFromJsonAsync<List<AccessibleSubjectItem>>(JsonOptions, ct);
        return (IReadOnlyList<AccessibleSubjectItem>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DnaOrigamiSample>> GetFabricationPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/fabrication/presets to fetch standard scaffold geometries.
        // WHY:  Allows clients to display built-in sample designs for immediate origami routing.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes DnaOrigamiSample list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/fabrication/presets");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<DnaOrigamiSample>? list = await resp.Content.ReadFromJsonAsync<List<DnaOrigamiSample>>(JsonOptions, ct);
        return (IReadOnlyList<DnaOrigamiSample>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<FabricationPackage> GenerateFabricationProtocolAsync(
        InstanceProfile profile,
        string bearerToken,
        FabricationGenerateRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/fabrication/generate to synthesize staple routes and thermal ramp.
        // WHY:  Allows generating a complete wet-lab DNA origami fabrication package with SHA-256 integrity.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and deserializes FabricationPackage.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/fabrication/generate");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        FabricationPackage? pkg = await resp.Content.ReadFromJsonAsync<FabricationPackage>(JsonOptions, ct);
        return pkg ?? throw new InvalidOperationException("Endpoint returned empty or null fabrication package payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientGenomicProfile>> GetGenomicPresetsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/genomics/presets to fetch standard genetic test panels.
        // WHY:  Allows clinicians and researchers to load preset clinical panels for screening.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes PatientGenomicProfile list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/genomics/presets");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientGenomicProfile>? list = await resp.Content.ReadFromJsonAsync<List<PatientGenomicProfile>>(JsonOptions, ct);
        return (IReadOnlyList<PatientGenomicProfile>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GeneticMarker>> GetGenomicMarkersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/genomics/markers to fetch clinically characterized pharmacogenomic markers.
        // WHY:  Provides reference marker details including Factor V Leiden, Prothrombin, and AR CAG alleles.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes GeneticMarker list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/genomics/markers");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<GeneticMarker>? list = await resp.Content.ReadFromJsonAsync<List<GeneticMarker>>(JsonOptions, ct);
        return (IReadOnlyList<GeneticMarker>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StackServiceInfo>> GetStackServicesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/services to retrieve container health, status, and telemetry.
        // WHY:  Allows monitoring database, simulation engine, and cache readiness across cluster instances.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes StackServiceInfo list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/services");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<StackServiceInfo>? list = await resp.Content.ReadFromJsonAsync<List<StackServiceInfo>>(JsonOptions, ct);
        return (IReadOnlyList<StackServiceInfo>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<StackServiceInfo> PingStackServiceAsync(
        InstanceProfile profile,
        string bearerToken,
        string serviceId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/services/ping/{serviceId} to test connectivity and latency to a single service.
        // WHY:  Enables diagnostic socket verification for individual microservice containers.
        // HOW:  Validates inputs, sends authenticated POST request, and deserializes StackServiceInfo.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/services/ping/{Uri.EscapeDataString(serviceId)}");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        StackServiceInfo? info = await resp.Content.ReadFromJsonAsync<StackServiceInfo>(JsonOptions, ct);
        return info ?? throw new InvalidOperationException("Endpoint returned empty or null stack service info payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HormoneCurvePoint>> GetEndocrineKineticsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/endocrine/kinetics to calculate 72-hour hormone concentration curves.
        // WHY:  Powers visualizer charts for estradiol, testosterone, and progesterone stabilization curves.
        // HOW:  Validates inputs, sends authenticated GET request, and deserializes HormoneCurvePoint list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/endocrine/kinetics");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<HormoneCurvePoint>? list = await resp.Content.ReadFromJsonAsync<List<HormoneCurvePoint>>(JsonOptions, ct);
        return (IReadOnlyList<HormoneCurvePoint>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<EndocrineTargetProfile> ToggleEndocrineSwitchAsync(
        InstanceProfile profile,
        string bearerToken,
        ToggleSwitchRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/endocrine/toggle to switch between Estrogenic and Androgenic setpoints.
        // WHY:  Allows updating hormonal target profiles with bistable feedback loop calculations.
        // HOW:  Validates inputs, posts serialized JSON payload with Bearer header, and deserializes EndocrineTargetProfile.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/endocrine/toggle");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        EndocrineTargetProfile? target = await resp.Content.ReadFromJsonAsync<EndocrineTargetProfile>(JsonOptions, ct);
        return target ?? throw new InvalidOperationException("Endpoint returned empty or null endocrine profile payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users to list all registered sovereign users.
        // WHY:  Permits administrative enumeration of active accounts and permissions.
        // HOW:  Validates inputs, executes GET request with Bearer token, and deserializes list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/users");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<UserAccount>? list = await resp.Content.ReadFromJsonAsync<List<UserAccount>>(JsonOptions, ct);
        return (IReadOnlyList<UserAccount>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<UserAccount> CreateUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users to provision a new user account.
        // WHY:  Allows administrators to provision new accounts with cryptographic keys.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes created account.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/users");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        UserAccount? user = await resp.Content.ReadFromJsonAsync<UserAccount>(JsonOptions, ct);
        return user ?? throw new InvalidOperationException("Endpoint returned empty or null user account payload.");
    }

    /// <inheritdoc/>
    public async Task DeleteUserAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username} to revoke a user account.
        // WHY:  Supports permanent decommissioning of revoked patient or researcher identities.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<UserProfile> GetUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/profile to fetch demographic and clinical profile data.
        // WHY:  Provides personalized regimen details, pronoun preferences, and clinical baseline data.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes UserProfile.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/profile");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        UserProfile? prof = await resp.Content.ReadFromJsonAsync<UserProfile>(JsonOptions, ct);
        return prof ?? throw new InvalidOperationException("Endpoint returned empty or null user profile payload.");
    }

    /// <inheritdoc/>
    public async Task<UserProfile> UpdateUserProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        UserProfile userProfile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches PUT /api/users/{username}/profile to create or update profile metadata.
        // WHY:  Allows patients and clinicians to maintain up-to-date demographic and clinical regimen data.
        // HOW:  Validates inputs, issues PUT request with JSON payload, and deserializes updated profile.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(userProfile);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/profile");

        using HttpRequestMessage req = new(HttpMethod.Put, endpointUri)
        {
            Content = JsonContent.Create(userProfile, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        UserProfile? prof = await resp.Content.ReadFromJsonAsync<UserProfile>(JsonOptions, ct);
        return prof ?? throw new InvalidOperationException("Endpoint returned empty or null user profile payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientAddress>> ListUserAddressesAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/addresses to retrieve registered user addresses.
        // WHY:  Supplies shipping and physical contact destinations for digital fabrication samples.
        // HOW:  Validates inputs, executes GET request with Bearer authorization, and deserializes list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/addresses");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientAddress>? list = await resp.Content.ReadFromJsonAsync<List<PatientAddress>>(JsonOptions, ct);
        return (IReadOnlyList<PatientAddress>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<PatientAddress> AddUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientAddress address,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users/{username}/addresses to add or update an address record.
        // WHY:  Supports recording and modifying structured geographic addresses.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes stored address.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(address);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/addresses");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(address, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientAddress? addr = await resp.Content.ReadFromJsonAsync<PatientAddress>(JsonOptions, ct);
        return addr ?? throw new InvalidOperationException("Endpoint returned empty or null patient address payload.");
    }

    /// <inheritdoc/>
    public async Task DeleteUserAddressAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string addressId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username}/addresses/{id} to remove an address.
        // WHY:  Permits removing stale or revoked patient residential locations.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/addresses/{Uri.EscapeDataString(addressId)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientContact>> ListUserContactsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/users/{username}/contacts to retrieve emergency outreach contacts.
        // WHY:  Maintains critical contact relationships for monitoring adverse endocrine events.
        // HOW:  Validates inputs, executes GET request with Bearer authorization, and deserializes list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/contacts");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientContact>? list = await resp.Content.ReadFromJsonAsync<List<PatientContact>>(JsonOptions, ct);
        return (IReadOnlyList<PatientContact>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<PatientContact> AddUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        PatientContact contact,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/users/{username}/contacts to add or update an emergency contact.
        // WHY:  Allows patients and clinicians to configure emergency contacts with relationship metadata.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes stored contact.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(contact);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/contacts");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(contact, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientContact? saved = await resp.Content.ReadFromJsonAsync<PatientContact>(JsonOptions, ct);
        return saved ?? throw new InvalidOperationException("Endpoint returned empty or null patient contact payload.");
    }

    /// <inheritdoc/>
    public async Task DeleteUserContactAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string contactId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/users/{username}/contacts/{id} to remove an emergency contact.
        // WHY:  Allows patients and administrators to revoke obsolete emergency contacts.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(contactId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/users/{Uri.EscapeDataString(username)}/contacts/{Uri.EscapeDataString(contactId)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<PatientDigitalDnaRecord> GetPatientDigitalDnaRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/dna/{id} to retrieve a digitized DNA blueprint.
        // WHY:  Supplies detailed staple sequences, annealing schedules, and provenance for review or fabrication.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes PatientDigitalDnaRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/dna/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientDigitalDnaRecord? rec = await resp.Content.ReadFromJsonAsync<PatientDigitalDnaRecord>(JsonOptions, ct);
        return rec ?? throw new InvalidOperationException("Endpoint returned empty or null patient DNA payload.");
    }

    /// <inheritdoc/>
    public async Task DeletePatientDigitalDnaAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/dna/{id} to delete a digitized DNA blueprint.
        // WHY:  Allows removing invalid or superseded nanocarrier blueprints from the dossier.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/dna/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDnaImportRecord>> ListPatientImportsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/imports to retrieve imported sequence files.
        // WHY:  Provides an audit archive of uploaded genomic sequences, FASTA datasets, and variant panels.
        // HOW:  Validates inputs, executes GET request with Bearer authorization, and deserializes list.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/imports");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientDnaImportRecord>? list = await resp.Content.ReadFromJsonAsync<List<PatientDnaImportRecord>>(JsonOptions, ct);
        return (IReadOnlyList<PatientDnaImportRecord>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<PatientDnaImportRecord> GetPatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/imports/{id} to retrieve a specific sequence import record.
        // WHY:  Supplies raw sequence content and metadata for downstream alignment or origami folding.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes PatientDnaImportRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/imports/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientDnaImportRecord? rec = await resp.Content.ReadFromJsonAsync<PatientDnaImportRecord>(JsonOptions, ct);
        return rec ?? throw new InvalidOperationException("Endpoint returned empty or null patient import payload.");
    }

    /// <inheritdoc/>
    public async Task<PatientDnaImportRecord> CreatePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientImportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/patients/{username}/imports to upload sequence data or variant panels.
        // WHY:  Allows clinicians and researchers to import raw genomic datasets into the patient dossier.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes created import record.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/imports");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientDnaImportRecord? rec = await resp.Content.ReadFromJsonAsync<PatientDnaImportRecord>(JsonOptions, ct);
        return rec ?? throw new InvalidOperationException("Endpoint returned empty or null patient import payload.");
    }

    /// <inheritdoc/>
    public async Task DeletePatientImportAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/imports/{id} to delete an imported sequence record.
        // WHY:  Supports removal of erroneous or superseded raw sequence imports.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/imports/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientResultRecord>> ListPatientClinicalResultsAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string? resultType = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/results to retrieve simulation and clinical outcome records.
        // WHY:  Allows reviewing synthesis packages, oxDNA/LAMMPS simulations, and clinical laboratory assays.
        // HOW:  Validates inputs, builds route with optional resultType query parameter, and executes GET request.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        string relative = string.IsNullOrWhiteSpace(resultType)
            ? $"/api/patients/{Uri.EscapeDataString(username)}/results"
            : $"/api/patients/{Uri.EscapeDataString(username)}/results?resultType={Uri.EscapeDataString(resultType)}";
        Uri endpointUri = new(baseUri, relative);

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<PatientResultRecord>? list = await resp.Content.ReadFromJsonAsync<List<PatientResultRecord>>(JsonOptions, ct);
        return (IReadOnlyList<PatientResultRecord>?)list ?? [];
    }

    /// <inheritdoc/>
    public async Task<PatientResultRecord> GetPatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/patients/{username}/results/{id} to retrieve a specific result record.
        // WHY:  Supplies metrics, charts, and outcome parameters for a single clinical or simulation assay.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes PatientResultRecord.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/results/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientResultRecord? rec = await resp.Content.ReadFromJsonAsync<PatientResultRecord>(JsonOptions, ct);
        return rec ?? throw new InvalidOperationException("Endpoint returned empty or null patient clinical result payload.");
    }

    /// <inheritdoc/>
    public async Task<PatientResultRecord> CreatePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        CreatePatientResultRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/patients/{username}/results to register a new simulation or clinical result.
        // WHY:  Enables saving physical lab assays and custom simulation outcomes to the patient dossier.
        // HOW:  Validates inputs, posts serialized JSON payload, and deserializes created result record.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/results");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PatientResultRecord? rec = await resp.Content.ReadFromJsonAsync<PatientResultRecord>(JsonOptions, ct);
        return rec ?? throw new InvalidOperationException("Endpoint returned empty or null patient clinical result payload.");
    }

    /// <inheritdoc/>
    public async Task DeletePatientClinicalResultAsync(
        InstanceProfile profile,
        string bearerToken,
        string username,
        string id,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/patients/{username}/results/{id} to delete a simulation or clinical result.
        // WHY:  Supports removal of invalid, test, or superseded result records.
        // HOW:  Validates inputs, issues DELETE request, and verifies successful response status.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/patients/{Uri.EscapeDataString(username)}/results/{Uri.EscapeDataString(id)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<UserAccount> GetCurrentUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/auth/me to fetch identity and claims of current authenticated session.
        // WHY:  Allows desktop/mobile clients to inspect role permissions and active username.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes UserAccount.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/auth/me");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        UserAccount? user = await resp.Content.ReadFromJsonAsync<UserAccount>(JsonOptions, ct);
        return user ?? throw new InvalidOperationException("Endpoint returned empty or null current user payload.");
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/auth/logout to invalidate the session.
        // WHY:  Ensures node-side session state and cookies are cleared upon explicit logout.
        // HOW:  Validates inputs, issues POST request with Bearer authorization, and asserts success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/auth/logout");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<SetupStatusResponse> GetSetupStatusAsync(
        InstanceProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/setup/status to determine if first-launch setup wizard is required.
        // WHY:  Allows clients to detect uninitialized nodes before prompting for authentication.
        // HOW:  Validates profile, issues unauthenticated GET request, and deserializes SetupStatusResponse.
        ArgumentNullException.ThrowIfNull(profile);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/setup/status");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SetupStatusResponse? status = await resp.Content.ReadFromJsonAsync<SetupStatusResponse>(JsonOptions, ct);
        return status ?? throw new InvalidOperationException("Endpoint returned empty or null setup status payload.");
    }

    /// <inheritdoc/>
    public async Task CryptoShredUserAsync(
        InstanceProfile profile,
        string bearerToken,
        CryptoShredRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/crypto-shred to destroy subject decryption keys per GDPR Article 17.
        // WHY:  Irreversibly renders historical subject health records unrecoverable upon patient request.
        // HOW:  Validates inputs, posts serialized CryptoShredRequest, and asserts success.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/crypto-shred");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<DeIdentificationResult> DeIdentifyDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        DeIdentifyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/de-identify for HIPAA Safe Harbor and GINA de-identification.
        // WHY:  Ensures patient identifiers and genomic datasets are scrubbed before secondary research use.
        // HOW:  Validates inputs, posts serialized DeIdentifyRequest, and deserializes DeIdentificationResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/de-identify");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        DeIdentificationResult? result = await resp.Content.ReadFromJsonAsync<DeIdentificationResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null de-identification result payload.");
    }

    /// <inheritdoc/>
    public async Task<EncryptedEnvelope> EncryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        EncryptRecordRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/encrypt-record to envelope-encrypt health records under ML-KEM-768.
        // WHY:  Protects sensitive demographic and endocrine records with post-quantum confidentiality.
        // HOW:  Validates inputs, posts serialized EncryptRecordRequest, and deserializes EncryptedEnvelope.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/encrypt-record");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        EncryptedEnvelope? envelope = await resp.Content.ReadFromJsonAsync<EncryptedEnvelope>(JsonOptions, ct);
        return envelope ?? throw new InvalidOperationException("Endpoint returned empty or null encrypted envelope payload.");
    }

    /// <inheritdoc/>
    public async Task<JsonElement> DecryptRecordAsync(
        InstanceProfile profile,
        string bearerToken,
        DecryptRecordRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/compliance/decrypt-record to decrypt an authenticated post-quantum envelope.
        // WHY:  Enables authorized clients to decrypt encrypted health records using subject keys.
        // HOW:  Validates inputs, posts serialized DecryptRecordRequest, and deserializes decrypted JsonElement.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/compliance/decrypt-record");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        JsonElement element = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return element;
    }

    /// <inheritdoc/>
    public async Task<DatasetImportResponse> ImportDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        byte[] archiveBytes,
        string archiveFileName,
        byte[] manifestBytes,
        string manifestFileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches multipart POST /api/datasets/import with archive and manifest payloads.
        // WHY:  Allows remote clients to verify signatures, check hashes, and import simulation bundles.
        // HOW:  Constructs MultipartFormDataContent containing archive and manifest byte arrays, posts to endpoint with Bearer auth.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(archiveBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/datasets/import");

        using MultipartFormDataContent form = new();
        using ByteArrayContent archiveContent = new(archiveBytes);
        using ByteArrayContent manifestContent = new(manifestBytes);
        archiveContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        manifestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        form.Add(archiveContent, "archive", archiveFileName);
        form.Add(manifestContent, "manifest", manifestFileName);

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = form
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        DatasetImportResponse? result = await resp.Content.ReadFromJsonAsync<DatasetImportResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null dataset import payload.");
    }

    /// <inheritdoc/>
    public async Task<ExportEncryptedDatasetResult> ExportEncryptedDatasetAsync(
        InstanceProfile profile,
        string bearerToken,
        ExportEncryptedDatasetRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/datasets/export-encrypted to export ML-KEM-768 encrypted bundle.
        // WHY:  Enables clients to trigger post-quantum encrypted dataset exports.
        // HOW:  Validates inputs, posts serialized ExportEncryptedDatasetRequest, and deserializes ExportEncryptedDatasetResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/datasets/export-encrypted");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        ExportEncryptedDatasetResult? result = await resp.Content.ReadFromJsonAsync<ExportEncryptedDatasetResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null export encrypted dataset payload.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportFileItem>> ListExportedDatasetBundlesAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/datasets/exports to enumerate generated dataset bundles.
        // WHY:  Allows desktop/mobile clients to inspect available archives, manifests, and signatures.
        // HOW:  Issues GET request with Bearer token and deserializes array of ExportFileItem.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/datasets/exports");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        List<ExportFileItem>? items = await resp.Content.ReadFromJsonAsync<List<ExportFileItem>>(JsonOptions, ct);
        return items ?? (IReadOnlyList<ExportFileItem>)Array.Empty<ExportFileItem>();
    }

    /// <inheritdoc/>
    public async Task DeleteExportedDatasetBundleAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches DELETE /api/datasets/exports/{fileName} to remove an exported bundle.
        // WHY:  Enables clients to clean up obsolete or downloaded dataset files from the node.
        // HOW:  Validates inputs, escapes fileName into URL path, and verifies response status code.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/datasets/exports/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Delete, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadDatasetArchiveAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/datasets/download/{fileName} to stream binary file contents.
        // WHY:  Retrieves raw packaged archives, manifests, or signatures for local processing.
        // HOW:  Validates inputs, escapes fileName into path, issues GET with Bearer token, and reads byte array.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/datasets/download/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadTrajectoryAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/trajectories/download/{fileName} to stream trajectory coordinates.
        // WHY:  Allows researchers to download active coordinate files (.xyz, .dcd, .dump) for offline visualization.
        // HOW:  Validates inputs, escapes fileName into path, issues GET with Bearer auth, and returns byte array.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/trajectories/download/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<AuthTokenResponse> InitializeSetupAdminAsync(
        InstanceProfile profile,
        InitialSetupRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches unauthenticated POST /api/setup/initialize to bootstrap master Administrator.
        // WHY:  Provisions initial identity and unlocks operational API endpoints during first launch.
        // HOW:  Validates inputs, posts serialized InitialSetupRequest without Bearer auth, and deserializes AuthTokenResponse.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/setup/initialize");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AuthTokenResponse? result = await resp.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null setup auth token payload.");
    }

    /// <inheritdoc/>
    public async Task<SystemHealthOverview> GetSystemHealthAsync(
        InstanceProfile profile,
        string? bearerToken = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/system/health to evaluate node status and subsystem diagnostics.
        // WHY:  Provides operational uptime to container probes and detailed diagnostics when authenticated.
        // HOW:  Validates inputs, sends GET request optionally attaching Bearer authorization, and deserializes SystemHealthOverview.
        ArgumentNullException.ThrowIfNull(profile);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/system/health");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SystemHealthOverview? result = await resp.Content.ReadFromJsonAsync<SystemHealthOverview>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null system health payload.");
    }

    /// <inheritdoc/>
    public async Task<EventBusTelemetry> GetEventBusMetricsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/system/event-bus to retrieve telemetry on event bus and Valkey bridge.
        // WHY:  Allows monitoring of event subscriber counts and message distribution health.
        // HOW:  Validates inputs, issues GET request with Bearer authorization, and deserializes EventBusTelemetry.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/system/event-bus");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        EventBusTelemetry? result = await resp.Content.ReadFromJsonAsync<EventBusTelemetry>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null event bus telemetry payload.");
    }

    /// <inheritdoc/>
    public async Task<TrajectoryParserProcessResult> ProcessTrajectoryCoordinatesAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/parser/process/{fileName} to trigger zero-allocation coordinate parsing.
        // WHY:  Allows measuring parser frame rate and processing atom counts on active trajectory streams.
        // HOW:  Validates inputs, escapes fileName into URL path, issues POST with Bearer auth, and deserializes result.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/parser/process/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        TrajectoryParserProcessResult? result = await resp.Content.ReadFromJsonAsync<TrajectoryParserProcessResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null parser process result payload.");
    }

    /// <inheritdoc/>
    public async Task<TrajectoryParquetExportResult> ExportCoordinatesParquetAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/parser/export-parquet/{fileName} to serialize coordinate stream into Parquet.
        // WHY:  Produces columnar Apache Parquet files for downstream DuckDB and analytics engines.
        // HOW:  Validates inputs, escapes fileName into path, issues POST with Bearer auth, and deserializes result.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/parser/export-parquet/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        TrajectoryParquetExportResult? result = await resp.Content.ReadFromJsonAsync<TrajectoryParquetExportResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null parquet export result payload.");
    }

    /// <inheritdoc/>
    public async Task<AptamerTrajectoryMetrics> AnalyzeTrajectoryEndocrineAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/endocrine/analyze/{fileName} to evaluate aptamer binding stability.
        // WHY:  Calculates ligand-pocket contact ratios and binding occupancy from trajectory streams.
        // HOW:  Validates inputs, escapes fileName into path, issues POST with Bearer auth, and deserializes metrics.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/endocrine/analyze/{Uri.EscapeDataString(fileName)}");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AptamerTrajectoryMetrics? result = await resp.Content.ReadFromJsonAsync<AptamerTrajectoryMetrics>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null aptamer trajectory metrics payload.");
    }

    /// <inheritdoc/>
    public async Task<SimulationInputDeckResult> GetSimulationCampaignConfigAsync(
        InstanceProfile profile,
        string bearerToken,
        string campaignId,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/simulations/configs/{campaignId} to generate simulation input decks.
        // WHY:  Outputs baseline oxDNA or LAMMPS scripts configured for the requested campaign.
        // HOW:  Validates inputs, escapes campaignId into path, issues GET with Bearer auth, and deserializes result.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, $"/api/simulations/configs/{Uri.EscapeDataString(campaignId)}");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SimulationInputDeckResult? result = await resp.Content.ReadFromJsonAsync<SimulationInputDeckResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null simulation input deck payload.");
    }

    /// <inheritdoc/>
    public async Task<GenomicAdjustmentResult> ApplyGenomicAdjustmentsAsync(
        InstanceProfile profile,
        string bearerToken,
        GenomicAnalysisRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/genomics/apply-adjustments to apply patient pharmacogenomic profile to setpoints.
        // WHY:  Calculates patient clearance and safety caps to update the active synthetic endocrine profile.
        // HOW:  Validates inputs, posts serialized GenomicAnalysisRequest with Bearer auth, and deserializes GenomicAdjustmentResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/genomics/apply-adjustments");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        GenomicAdjustmentResult? result = await resp.Content.ReadFromJsonAsync<GenomicAdjustmentResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null genomic adjustment result payload.");
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadOpentronsScriptAsync(
        InstanceProfile profile,
        string bearerToken,
        GenerateOpentronsScriptRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/fabrication/opentrons/download to generate and stream executable Python protocol.
        // WHY:  Allows liquid handler automation robots to ingest the generated protocol script directly.
        // HOW:  Validates inputs, posts serialized GenerateOpentronsScriptRequest with Bearer auth, and reads script bytes.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/fabrication/opentrons/download");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetFederationChunkAsync(
        InstanceProfile profile,
        string bearerToken,
        string fileName,
        int? chunkIndex = null,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /api/federation/chunks/{fileName}?index={chunkIndex} to retrieve swarm trajectory chunk.
        // WHY:  Transfers binary chunks across decentralized peer nodes for P2P trajectory distribution.
        // HOW:  Validates inputs, builds query string with optional chunk index, issues GET with Bearer auth, and returns bytes.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        string relativePath = chunkIndex.HasValue
            ? $"/api/federation/chunks/{Uri.EscapeDataString(fileName)}?index={chunkIndex.Value}"
            : $"/api/federation/chunks/{Uri.EscapeDataString(fileName)}";
        Uri endpointUri = new(baseUri, relativePath);

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<SerumStabilityAnalysisResult> RunSerumStabilityAsync(
        InstanceProfile profile,
        string bearerToken,
        SerumStabilityRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/serum-stability to execute degradation kinetics simulation.
        // WHY:  Allows client applications to run serum stability analysis campaigns.
        // HOW:  Validates inputs, posts serialized SerumStabilityRequest with Bearer auth, and deserializes SerumStabilityAnalysisResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/simulations/run/serum-stability");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SerumStabilityAnalysisResult? result = await resp.Content.ReadFromJsonAsync<SerumStabilityAnalysisResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null serum stability result payload.");
    }

    /// <inheritdoc/>
    public async Task<AptamerGatingAnalysisResult> RunAptamerGatingAsync(
        InstanceProfile profile,
        string bearerToken,
        AptamerGatingRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/aptamer-gating to execute ligand-gated conformational switching simulation.
        // WHY:  Enables computational verification of aptamer latch release thermodynamics before wet-lab synthesis.
        // HOW:  Validates inputs, posts serialized AptamerGatingRequest with Bearer auth, and deserializes AptamerGatingAnalysisResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/simulations/run/aptamer-gating");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AptamerGatingAnalysisResult? result = await resp.Content.ReadFromJsonAsync<AptamerGatingAnalysisResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null aptamer gating result payload.");
    }

    /// <inheritdoc/>
    public async Task<PayloadLeakageAnalysisResult> RunPayloadLeakageAsync(
        InstanceProfile profile,
        string bearerToken,
        PayloadLeakageRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/payload-leakage to execute premature encapsulation release simulation.
        // WHY:  Assesses cargo retention integrity across diverse biological pH and temperature gradients.
        // HOW:  Validates inputs, posts serialized PayloadLeakageRequest with Bearer auth, and deserializes PayloadLeakageAnalysisResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/simulations/run/payload-leakage");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PayloadLeakageAnalysisResult? result = await resp.Content.ReadFromJsonAsync<PayloadLeakageAnalysisResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null payload leakage result payload.");
    }

    /// <inheritdoc/>
    public async Task<PbpkEndocrineSimulationResult> RunPbpkSimulationAsync(
        InstanceProfile profile,
        string bearerToken,
        PbpkSimulationRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/simulations/run/pbpk-endocrine to run multi-compartment pharmacokinetic simulation.
        // WHY:  Simulates systemic biodistribution and target organ receptor kinetics for hormone replacement delivery.
        // HOW:  Validates inputs, posts serialized PbpkSimulationRequest with Bearer auth, and deserializes PbpkEndocrineSimulationResult.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/simulations/run/pbpk-endocrine");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        PbpkEndocrineSimulationResult? result = await resp.Content.ReadFromJsonAsync<PbpkEndocrineSimulationResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null PBPK simulation result payload.");
    }

    /// <inheritdoc/>
    public async Task<AuthTokenResponse> ExchangeWebTokenAsync(
        InstanceProfile profile,
        TokenExchangeRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches POST /api/auth/web-token to authenticate and establish a browser session cookie.
        // WHY:  Verifies digital challenge signature and mints session token for Web UI environments.
        // HOW:  Validates inputs, posts serialized TokenExchangeRequest, and deserializes AuthTokenResponse.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/api/auth/web-token");

        using HttpRequestMessage req = new(HttpMethod.Post, endpointUri)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        AuthTokenResponse? result = await resp.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null auth token response payload.");
    }

    /// <inheritdoc/>
    public async Task<SystemHealthOverview> GetHealthzAsync(
        InstanceProfile profile,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches GET /healthz to query low-overhead container runtime operational health.
        // WHY:  Provides kubernetes and docker health probes with deterministic cluster readiness state.
        // HOW:  Validates inputs, executes unauthenticated GET /healthz, and deserializes SystemHealthOverview.
        ArgumentNullException.ThrowIfNull(profile);

        Uri baseUri = ValidateBaseUri(profile.BaseUri);
        Uri endpointUri = new(baseUri, "/healthz");

        using HttpRequestMessage req = new(HttpMethod.Get, endpointUri);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw await NodeApiException.CreateAsync(resp, ct);
        }

        SystemHealthOverview? result = await resp.Content.ReadFromJsonAsync<SystemHealthOverview>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Endpoint returned empty or null healthz overview payload.");
    }

    /// <summary>
    /// Validates that an instance base URI is absolute and enforces HTTPS for remote non-loopback endpoints.
    /// </summary>
    /// <param name="baseUri">The base URI to validate.</param>
    /// <returns>A validated <see cref="Uri"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when the URI is invalid or uses plaintext HTTP on a remote host.</exception>
    private static Uri ValidateBaseUri(string baseUri)
    {
        // WHAT: Validates base URI scheme and loopback status per Prime Directive 9.
        // WHY:  Remote plaintext HTTP connections expose challenge nonces, bearer tokens, and PHI payloads to interception.
        // HOW:  Checks that URI is absolute, and ensures that HTTP is permitted only for localhost/loopback addresses.
        if (string.IsNullOrWhiteSpace(baseUri) || !Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("BaseUri must be a valid absolute URI.", nameof(baseUri));
        }

        bool isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isLoopbackHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new ArgumentException(
                "BaseUri must use HTTPS for remote instances, or HTTP for localhost/loopback instances.",
                nameof(baseUri));
        }

        return uri;
    }

    /// <summary>
    /// Internal DTO representing the response from POST /api/auth/challenge.
    /// </summary>
    private sealed class ChallengeResponsePayload
    {
        /// <summary>
        /// Gets or sets the unique challenge session identifier.
        /// </summary>
        [JsonPropertyName("challengeId")]
        public string ChallengeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Base64-encoded challenge nonce to be signed.
        /// </summary>
        [JsonPropertyName("nonceBase64")]
        public string NonceBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal DTO representing the response from POST /api/auth/token.
    /// </summary>
    private sealed class TokenResponsePayload
    {
        /// <summary>
        /// Gets or sets the issued Bearer JWT.
        /// </summary>
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the token type scheme (Bearer).
        /// </summary>
        [JsonPropertyName("tokenType")]
        public string? TokenType { get; set; }

        /// <summary>
        /// Gets or sets the validity duration in seconds.
        /// </summary>
        [JsonPropertyName("expiresInSeconds")]
        public int ExpiresInSeconds { get; set; }

        /// <summary>
        /// Gets or sets the authenticated user metadata.
        /// </summary>
        [JsonPropertyName("user")]
        public TokenUserPayload? User { get; set; }
    }

    /// <summary>
    /// Internal DTO for user identity within the token response.
    /// </summary>
    private sealed class TokenUserPayload
    {
        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets the raw role JSON element.
        /// </summary>
        [JsonPropertyName("role")]
        public JsonElement RoleElement { get; set; }

        /// <summary>
        /// Gets the resolved role string.
        /// </summary>
        [JsonIgnore]
        public string? Role => RoleElement.ValueKind switch
        {
            JsonValueKind.String => RoleElement.GetString(),
            JsonValueKind.Number when RoleElement.TryGetInt32(out int r) => r switch
            {
                0 => "Administrator",
                1 => "Researcher",
                2 => "Patient",
                _ => r.ToString(CultureInfo.InvariantCulture)
            },
            _ => null
        };
    }
}
