using System.ComponentModel;
using System.Runtime.CompilerServices;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Gui.Core.ViewModels;

/// <summary>
/// ViewModel coordinating collaborative single-use share keys, peer laboratory public key registries,
/// dataset export bundles, post-quantum encrypted archives, and P2P trajectory swarm federation.
/// </summary>
/// <remarks>
/// // WHAT: Provides presentation state and asynchronous commands for dataset transfers and decentralized federation.
/// // WHY:  Issue #169 (P166): Enables research nodes to exchange signed simulation datasets and distribute trajectory chunks.
/// // HOW:  Delegates to <see cref="INanoWeaverNodeClient"/>, manages busy states, and exposes observable artifacts.
/// </remarks>
public sealed class FederationAndDatasetWorkspaceViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// REST client communicating with the connected sovereign nanoweaver-hrt node.
    /// </summary>
    private readonly INanoWeaverNodeClient _client;

    /// <summary>
    /// Callback delegate resolving the active instance connection profile.
    /// </summary>
    private readonly Func<InstanceProfile?>? _profileProvider;

    /// <summary>
    /// Callback delegate resolving the active zeroizable Bearer session token.
    /// </summary>
    private readonly Func<string?>? _tokenProvider;

    /// <summary>
    /// Explicit override for active instance connection profile.
    /// </summary>
    private InstanceProfile? _activeProfile;

    /// <summary>
    /// Explicit override for active Bearer authentication token.
    /// </summary>
    private string? _activeBearerToken;

    /// <summary>
    /// Indicates whether an asynchronous dataset or federation operation is currently running.
    /// </summary>
    private bool _isBusy;

    /// <summary>
    /// Last error or validation message encountered during operation execution.
    /// </summary>
    private string? _errorMessage;

    /// <summary>
    /// Collection of single-use collaborative share keys and active peer tokens.
    /// </summary>
    private IReadOnlyList<ShareKeyRecord>? _shareKeys;

    /// <summary>
    /// Listing of this node's public signing key and registered peer laboratory keys.
    /// </summary>
    private PeerKeyListResponse? _peerKeys;

    /// <summary>
    /// Collection of packaged dataset archives, manifests, and signatures in the export volume.
    /// </summary>
    private IReadOnlyList<ExportFileItem>? _exportedBundles;

    /// <summary>
    /// Collection of trajectory file descriptors available in the shared simulation volume.
    /// </summary>
    private IReadOnlyList<TrajectoryFileItem>? _trajectories;

    /// <summary>
    /// Collection of discovered peer nodes in the decentralized trajectory federation swarm.
    /// </summary>
    private IReadOnlyList<FederationPeerNode>? _federationPeers;

    /// <summary>
    /// Result of the most recently built and signed trajectory federation chunk manifest.
    /// </summary>
    private TrajectoryFederationManifest? _lastBuiltManifest;

    /// <summary>
    /// Result of the most recent trajectory federation manifest verification check.
    /// </summary>
    private FederationVerifyManifestResponse? _lastVerifyResponse;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationAndDatasetWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="client">REST API client for issuing federation and dataset requests.</param>
    /// <param name="profileProvider">Optional delegate resolving the active instance profile.</param>
    /// <param name="tokenProvider">Optional delegate resolving the active Bearer token.</param>
    public FederationAndDatasetWorkspaceViewModel(
        INanoWeaverNodeClient client,
        Func<InstanceProfile?>? profileProvider = null,
        Func<string?>? tokenProvider = null)
    {
        // WHAT: Injects mandatory client and optional ambient session providers.
        // WHY:  Allows flexible usage in both integrated shell contexts and isolated headless unit tests.
        // HOW:  Validates non-null client and assigns internal delegate fields.
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _profileProvider = profileProvider;
        _tokenProvider = tokenProvider;
    }

    /// <summary>
    /// Gets or sets an explicit instance profile override.
    /// </summary>
    public InstanceProfile? ActiveProfile
    {
        get => _activeProfile;
        set => SetField(ref _activeProfile, value);
    }

    /// <summary>
    /// Gets or sets an explicit Bearer session token override.
    /// </summary>
    public string? ActiveBearerToken
    {
        get => _activeBearerToken;
        set => SetField(ref _activeBearerToken, value);
    }

    /// <summary>
    /// Gets a value indicating whether an asynchronous operation is currently in flight.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// Gets the most recent operational error message, or <see langword="null"/> if successful.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets the collection of created single-use share keys and connected remote peer instances.
    /// </summary>
    public IReadOnlyList<ShareKeyRecord>? ShareKeys
    {
        get => _shareKeys;
        private set => SetField(ref _shareKeys, value);
    }

    /// <summary>
    /// Gets the response containing this node's public key and registered peer laboratory keys.
    /// </summary>
    public PeerKeyListResponse? PeerKeys
    {
        get => _peerKeys;
        private set => SetField(ref _peerKeys, value);
    }

    /// <summary>
    /// Gets the list of packaged dataset archives, manifests, and signatures in the export volume.
    /// </summary>
    public IReadOnlyList<ExportFileItem>? ExportedBundles
    {
        get => _exportedBundles;
        private set => SetField(ref _exportedBundles, value);
    }

    /// <summary>
    /// Gets the collection of available trajectory files in the active simulation volume.
    /// </summary>
    public IReadOnlyList<TrajectoryFileItem>? Trajectories
    {
        get => _trajectories;
        private set => SetField(ref _trajectories, value);
    }

    /// <summary>
    /// Gets the collection of discovered peer nodes in the trajectory federation swarm.
    /// </summary>
    public IReadOnlyList<FederationPeerNode>? FederationPeers
    {
        get => _federationPeers;
        private set => SetField(ref _federationPeers, value);
    }

    /// <summary>
    /// Gets the result of the most recently built trajectory federation manifest.
    /// </summary>
    public TrajectoryFederationManifest? LastBuiltManifest
    {
        get => _lastBuiltManifest;
        private set => SetField(ref _lastBuiltManifest, value);
    }

    /// <summary>
    /// Gets the result of the most recent trajectory federation manifest verification check.
    /// </summary>
    public FederationVerifyManifestResponse? LastVerifyResponse
    {
        get => _lastVerifyResponse;
        private set => SetField(ref _lastVerifyResponse, value);
    }

    /// <summary>
    /// Retrieves all created single-use share keys and connected remote peer instances via GET /api/share-keys.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of <see cref="ShareKeyRecord"/> entries.</returns>
    public async Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(CancellationToken ct = default)
    {
        // WHAT: Dispatches a query for all active and historical share keys.
        // WHY:  Allows reviewing collaborative share keys, redemption statuses, and expiration timestamps.
        // HOW:  Resolves session credentials, queries client, sets ShareKeys property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListShareKeysAsync(profile, token, ct).ConfigureAwait(false);
            ShareKeys = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Generates a single-use collaborative share key via POST /api/share-keys/create.
    /// </summary>
    /// <param name="request">Request parameters specifying label, lifetime, and permissions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created single-use <see cref="ShareKeyRecord"/>.</returns>
    public async Task<ShareKeyRecord> CreateShareKeyAsync(
        CreateShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Issues a request to generate a cryptographic single-use share key.
        // WHY:  Allows secure invitation of remote clinicians or researchers to access specific node resources.
        // HOW:  Validates request, resolves session, calls client, and captures operational errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.CreateShareKeyAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Validates and atomically consumes a single-use share key via POST /api/share-keys/redeem.
    /// </summary>
    /// <param name="request">Payload containing single-use share token to redeem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of redemption including granted session token and role.</returns>
    public async Task<ShareKeyConsumeResult> RedeemShareKeyAsync(
        RedeemShareKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Dispatches an unauthenticated single-use share key redemption request.
        // WHY:  Exchanges an invitation token for an authenticated Bearer session on the target node.
        // HOW:  Validates request, resolves effective profile, calls client, and manages busy state.
        ArgumentNullException.ThrowIfNull(request);
        var profile = ResolveEffectiveProfile();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.RedeemShareKeyAsync(profile, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lists this node's public dataset-signing identity and all registered peer laboratory keys via GET /api/peer-keys (Administrator role required).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Peer key listing response containing local node public key and registered peer keys.</returns>
    public async Task<PeerKeyListResponse> ListPeerKeysAsync(CancellationToken ct = default)
    {
        // WHAT: Queries this node's signing public key and all registered peer laboratory keys.
        // WHY:  Supports cryptographic verification of imported datasets from trusted collaborator nodes.
        // HOW:  Resolves session, queries client, sets PeerKeys property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListPeerKeysAsync(profile, token, ct).ConfigureAwait(false);
            PeerKeys = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Registers a trusted peer laboratory's dataset-signing public key via POST /api/peer-keys (Administrator role required).
    /// </summary>
    /// <param name="request">Registration payload containing peer label, algorithm, and Base64 public key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Registered peer key record.</returns>
    public async Task<PeerKeyRecord> RegisterPeerKeyAsync(
        RegisterPeerKeyRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Registers a public signing key belonging to an external collaborator or research institution.
        // WHY:  Establishes trust anchors for validating signed simulation bundles and manifests.
        // HOW:  Validates request, resolves session, calls client, and captures operational errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.RegisterPeerKeyAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Permanently revokes an active peer dataset-signing public key via POST /api/peer-keys/{keyId}/revoke (Administrator role required).
    /// </summary>
    /// <param name="keyId">Identifier of the peer key to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RevokePeerKeyAsync(
        string keyId,
        CancellationToken ct = default)
    {
        // WHAT: Revokes an active peer laboratory public signing key in the trust registry.
        // WHY:  Prevents acceptance of future datasets signed by compromised or decommissioned peer nodes.
        // HOW:  Validates keyId, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.RevokePeerKeyAsync(profile, token, keyId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lists all packaged dataset archives, manifests, and signatures in the export volume via GET /api/datasets/exports.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of export file metadata items.</returns>
    public async Task<IReadOnlyList<ExportFileItem>> ListExportedDatasetBundlesAsync(CancellationToken ct = default)
    {
        // WHAT: Queries the list of exported dataset archive bundles, manifests, and signatures.
        // WHY:  Provides researchers with download and verification links for exported bundles.
        // HOW:  Resolves session, queries client, sets ExportedBundles property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListExportedDatasetBundlesAsync(profile, token, ct).ConfigureAwait(false);
            ExportedBundles = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Initiates a dataset export bundle with an immutable cryptographic manifest via POST /api/datasets/export.
    /// </summary>
    /// <param name="request">Export specification specifying dataset name and files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created dataset manifest.</returns>
    public async Task<DatasetManifest> ExportDatasetAsync(
        ExportRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Packages selected simulation coordinates and trajectory files into a tar.gz bundle.
        // WHY:  Creates self-contained dataset archives accompanied by cryptographic SHA-256 manifests.
        // HOW:  Validates request, resolves session, calls client, and captures operational errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.ExportDatasetAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Packages and envelope-encrypts a trajectory dataset using post-quantum ML-KEM-768 and signs the manifest with ML-DSA-65 via POST /api/datasets/export-encrypted.
    /// </summary>
    /// <param name="request">Request parameters specifying source trajectory, subject ID, and optional destination directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result metadata detailing bundle path, algorithm suite, and subject identifier.</returns>
    public async Task<ExportEncryptedDatasetResult> ExportEncryptedDatasetAsync(
        ExportEncryptedDatasetRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Packages a trajectory dataset and envelope-encrypts it under a subject's post-quantum key.
        // WHY:  Guarantees confidentiality during cross-institutional transit against harvest-now-decrypt-later adversaries.
        // HOW:  Validates request, resolves session, awaits client, and manages busy state.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.ExportEncryptedDatasetAsync(profile, token, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cryptographically verifies and imports a simulation dataset archive and manifest via POST /api/datasets/import.
    /// </summary>
    /// <param name="archiveBytes">Raw compressed archive byte payload (.tar.gz).</param>
    /// <param name="archiveFileName">File name of the archive bundle.</param>
    /// <param name="manifestBytes">Raw JSON bytes of the dataset provenance manifest.</param>
    /// <param name="manifestFileName">File name of the provenance manifest JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result and provenance descriptor from the server.</returns>
    public async Task<DatasetImportResponse> ImportDatasetArchiveAsync(
        byte[] archiveBytes,
        string archiveFileName,
        byte[] manifestBytes,
        string manifestFileName,
        CancellationToken ct = default)
    {
        // WHAT: Uploads a compressed dataset archive bundle and provenance manifest for cryptographic import.
        // WHY:  Verifies digital signatures and SHA-256 chunk hashes before uncompressing trajectories into the shared volume.
        // HOW:  Validates byte buffers and file names, resolves session, calls client, and captures errors.
        ArgumentNullException.ThrowIfNull(archiveBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.ImportDatasetArchiveAsync(
                profile,
                token,
                archiveBytes,
                archiveFileName,
                manifestBytes,
                manifestFileName,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Streams raw binary bytes of an exported dataset archive or manifest via GET /api/datasets/download/{fileName}.
    /// </summary>
    /// <param name="fileName">Name of the exported archive or manifest file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw byte buffer of the downloaded artifact.</returns>
    public async Task<byte[]> DownloadDatasetArchiveAsync(
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Downloads the raw binary payload of an exported dataset archive or manifest.
        // WHY:  Allows local storage or offline verification of packaged simulation results.
        // HOW:  Validates fileName, resolves session, calls client, and captures operational errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.DownloadDatasetArchiveAsync(profile, token, fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Purges a packaged dataset bundle or manifest from the export volume via DELETE /api/datasets/exports/{fileName}.
    /// </summary>
    /// <param name="fileName">Name of the exported file to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing asynchronous completion.</returns>
    public async Task DeleteExportedDatasetBundleAsync(
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Deletes a packaged dataset export bundle or manifest from disk.
        // WHY:  Frees storage volume space after export artifacts have been downloaded or federated.
        // HOW:  Validates fileName, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.DeleteExportedDatasetBundleAsync(profile, token, fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Discovers active peer nodes in the trajectory federation swarm via GET /api/federation/peers.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only collection of discovered swarm peer nodes.</returns>
    public async Task<IReadOnlyList<FederationPeerNode>> DiscoverFederationPeersAsync(CancellationToken ct = default)
    {
        // WHAT: Discovers live peer nodes participating in the decentralized trajectory federation swarm.
        // WHY:  Builds peer routing tables for distributed chunk replication and bitstream downloads.
        // HOW:  Resolves session, queries client, sets FederationPeers property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.DiscoverFederationPeersAsync(profile, token, ct).ConfigureAwait(false);
            FederationPeers = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Builds and cryptographically signs a trajectory federation chunk manifest via POST /api/federation/manifests/build.
    /// </summary>
    /// <param name="request">Parameters specifying the trajectory file name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated trajectory federation manifest.</returns>
    public async Task<TrajectoryFederationManifest> BuildFederationManifestAsync(
        FederationBuildManifestRequest request,
        CancellationToken ct = default)
    {
        // WHAT: Generates Merkle chunk hashes and post-quantum signatures for a simulation trajectory.
        // WHY:  Produces authoritative manifests required for swarm-based P2P trajectory distribution.
        // HOW:  Validates request, resolves session, calls client, updates LastBuiltManifest, and captures errors.
        ArgumentNullException.ThrowIfNull(request);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.BuildFederationManifestAsync(profile, token, request, ct).ConfigureAwait(false);
            LastBuiltManifest = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Verifies the digital signature and chunk integrity of a federation manifest via POST /api/federation/manifests/verify.
    /// </summary>
    /// <param name="manifest">Trajectory federation manifest to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result detailing validity status and chunk statistics.</returns>
    public async Task<FederationVerifyManifestResponse> VerifyFederationManifestAsync(
        TrajectoryFederationManifest manifest,
        CancellationToken ct = default)
    {
        // WHAT: Verifies the post-quantum signature and integrity proofs across a federation manifest.
        // WHY:  Confirms authenticity of chunks before participating in swarm seeding or downloading.
        // HOW:  Validates manifest, resolves session, invokes client, sets LastVerifyResponse, and manages busy state.
        ArgumentNullException.ThrowIfNull(manifest);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.VerifyFederationManifestAsync(profile, token, manifest, ct).ConfigureAwait(false);
            LastVerifyResponse = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads a specific binary trajectory chunk by index for P2P swarm distribution via GET /api/federation/chunks/{fileName}.
    /// </summary>
    /// <param name="fileName">Name of the federated trajectory file.</param>
    /// <param name="chunkIndex">Zero-based chunk index.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Binary bytes of the requested chunk.</returns>
    public async Task<byte[]> GetFederationChunkAsync(
        string fileName,
        int? chunkIndex = null,
        CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific fixed-size binary chunk of a federated trajectory file.
        // WHY:  Powers parallel, multi-source chunk transfers across decentralized peer nodes.
        // HOW:  Validates fileName, resolves session, calls client, and captures operational errors.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.GetFederationChunkAsync(profile, token, fileName, chunkIndex, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retrieves the list of available trajectory files in the active simulation volume via GET /api/datasets.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of trajectory file descriptors.</returns>
    public async Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(CancellationToken ct = default)
    {
        // WHAT: Queries all trajectory files (.xyz, .dcd, .dump) residing in the simulation data volume.
        // WHY:  Provides researchers with inventory of completed simulation runs available for export or streaming.
        // HOW:  Resolves session, queries client, sets Trajectories property, and manages busy state.
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _client.ListTrajectoriesAsync(profile, token, ct).ConfigureAwait(false);
            Trajectories = result;
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Streams raw simulation trajectory coordinates from the shared volume via GET /api/trajectories/download/{fileName}.
    /// </summary>
    /// <param name="fileName">Name of the trajectory file (.xyz, .dcd, .dump).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw byte buffer of the simulation trajectory.</returns>
    public async Task<byte[]> DownloadTrajectoryAsync(
        string fileName,
        CancellationToken ct = default)
    {
        // WHAT: Downloads the full binary content of a simulation trajectory file.
        // WHY:  Enables local molecular dynamics visualization in ChimeraX, VMD, or desktop viewer.
        // HOW:  Validates fileName, resolves session, invokes client, and manages busy state.
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var (profile, token) = ResolveEffectiveSession();

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            return await _client.DownloadTrajectoryAsync(profile, token, fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Resolves the effective instance connection profile.
    /// </summary>
    /// <returns>The active <see cref="InstanceProfile"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active profile is available.</exception>
    private InstanceProfile ResolveEffectiveProfile()
    {
        // WHAT: Obtains active instance profile from local property or ambient delegate.
        // WHY:  Guarantees REST requests cannot be issued without an endpoint profile.
        // HOW:  Evaluates explicit property override with fallback to injected provider callback.
        var profile = ActiveProfile ?? _profileProvider?.Invoke();
        if (profile == null)
        {
            throw new InvalidOperationException("An active instance profile is required to execute federation operations.");
        }

        return profile;
    }

    /// <summary>
    /// Resolves the effective instance connection profile and Bearer authentication token.
    /// </summary>
    /// <returns>A tuple containing the active profile and Bearer token string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active profile or bearer token is available.</exception>
    private (InstanceProfile Profile, string BearerToken) ResolveEffectiveSession()
    {
        // WHAT: Obtains active instance profile and Bearer token from local properties or ambient delegates.
        // WHY:  Guarantees authenticated REST requests cannot be issued without complete session credentials.
        // HOW:  Resolves profile first, then evaluates token property with fallback to token provider delegate.
        var profile = ResolveEffectiveProfile();

        var token = ActiveBearerToken ?? _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("An active Bearer token is required to execute federation operations.");
        }

        return (profile, token);
    }

    /// <summary>
    /// Helper method to mutate property values and dispatch PropertyChanged events when altered.
    /// </summary>
    /// <typeparam name="T">The type of the property value.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">New value to assign.</param>
    /// <param name="propertyName">Name of the property being modified.</param>
    /// <returns>True if the property value changed; otherwise false.</returns>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // WHAT: Performs equality check and conditionally mutates backing field.
        // WHY:  Avoids redundant PropertyChanged event churn across data bindings.
        // HOW:  Compares values via EqualityComparer<T>.Default; if distinct, sets field and invokes OnPropertyChanged.
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">Name of the modified property.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // WHAT: Dispatches PropertyChanged event to subscribers.
        // WHY:  Signals Avalonia UI binding engine to update presentation elements.
        // HOW:  Invokes PropertyChanged handler with PropertyChangedEventArgs.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
