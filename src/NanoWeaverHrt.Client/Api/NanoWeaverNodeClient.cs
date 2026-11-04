using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
