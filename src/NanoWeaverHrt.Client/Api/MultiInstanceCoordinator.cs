using System.Collections.Concurrent;
using NanoWeaverHrt.Client.Storage;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Coordinates multi-instance authentication sessions, background telemetry polling,
/// and state-transition alert detection across managed nanoweaver-hrt nodes.
/// </summary>
public sealed class MultiInstanceCoordinator : IMultiInstanceCoordinator, IDisposable
{
    /// <summary>
    /// Repository used to retrieve and persist instance profiles.
    /// </summary>
    private readonly IInstanceProfileRepository _profileRepository;

    /// <summary>
    /// REST API client for communicating with individual nanoweaver-hrt nodes.
    /// </summary>
    private readonly INanoWeaverNodeClient _nodeClient;

    /// <summary>
    /// Live point-in-time snapshots of all tracked instances.
    /// </summary>
    private readonly ConcurrentDictionary<string, InstanceConnectionSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cached Bearer token buffers held in memory for authenticated instances.
    /// </summary>
    private readonly ConcurrentDictionary<string, char[]> _cachedTokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Disposed flag indicating whether resources have been released.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiInstanceCoordinator"/> class.
    /// </summary>
    /// <param name="profileRepository">Repository used to query and persist instance profiles.</param>
    /// <param name="nodeClient">REST API client for communicating with nanoweaver-hrt instances.</param>
    public MultiInstanceCoordinator(
        IInstanceProfileRepository profileRepository,
        INanoWeaverNodeClient nodeClient)
    {
        // WHAT: Initializes coordinator dependencies.
        // WHY:  Coordinates multi-instance auth, polling, and alert event generation.
        // HOW:  Validates non-null arguments and stores repository and API client references.
        ArgumentNullException.ThrowIfNull(profileRepository);
        ArgumentNullException.ThrowIfNull(nodeClient);
        _profileRepository = profileRepository;
        _nodeClient = nodeClient;
    }

    /// <inheritdoc/>
    public IReadOnlyList<InstanceConnectionSnapshot> GetSnapshots()
    {
        // WHAT: Returns current snapshots across all tracked instances.
        // WHY:  Allows dashboard viewmodels and tray menus to render aggregate instance status.
        // HOW:  Returns snapshots ordered deterministically by display name.
        ThrowIfDisposed();
        return _snapshots.Values
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Pre-registers an instance profile into the snapshot collection with a Disconnected state.
    /// </summary>
    /// <param name="profile">The instance profile to register.</param>
    public void RegisterProfile(InstanceProfile profile)
    {
        // WHAT: Adds a profile to tracked snapshots if not already present.
        // WHY:  Populates the instance list prior to authentication attempts.
        // HOW:  Inserts a Disconnected snapshot into the concurrent snapshot dictionary.
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(profile);
        _snapshots.TryAdd(
            profile.InstanceId,
            new InstanceConnectionSnapshot(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Disconnected,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                profile.AddedAtUtc,
                null));
    }

    /// <summary>
    /// Synchronizes the coordinator's snapshot cache with all instance profiles persisted in the repository.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous synchronization operation.</returns>
    public async Task SyncProfilesAsync(CancellationToken ct = default)
    {
        // WHAT: Queries all stored profiles and adds any missing profiles to snapshots.
        // WHY:  Keeps coordinator state in sync with local SQLite profile storage.
        // HOW:  Fetches profiles via IInstanceProfileRepository.ListProfilesAsync and registers missing ones.
        ThrowIfDisposed();
        IReadOnlyList<InstanceProfile> profiles = await _profileRepository.ListProfilesAsync(ct);
        foreach (InstanceProfile profile in profiles)
        {
            RegisterProfile(profile);
        }
    }

    /// <inheritdoc/>
    public async Task<InstanceConnectionSnapshot> ConnectInstanceAsync(
        string instanceId,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Unlocks the sealed private key envelope, completes challenge-response PKI auth, and captures initial snapshot.
        // WHY:  Establishes authenticated session without storing plaintext passphrases or unsealed private keys.
        // HOW:  Fetches profile, sets state to Connecting, calls AuthenticateAsync, caches token, and polls snapshot.
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        InstanceProfile? profile = await _profileRepository.GetProfileAsync(instanceId, ct);
        if (profile == null)
        {
            throw new KeyNotFoundException($"Instance profile with ID '{instanceId}' was not found.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        _snapshots[instanceId] = new InstanceConnectionSnapshot(
            profile.InstanceId,
            profile.DisplayName,
            InstanceConnectionState.Connecting,
            0.0,
            0.0,
            0.0,
            0,
            0.0,
            nowUtc,
            null);

        try
        {
            NodeAuthSession session = await _nodeClient.AuthenticateAsync(profile, passphrase, ct);

            // Cache token as char array for zeroing capability on disconnect
            StoreCachedToken(instanceId, session.Token);

            // Update profile with last connected timestamp
            await _profileRepository.UpdateLastConnectedAsync(instanceId, DateTimeOffset.UtcNow, ct);

            // Poll live telemetry snapshot
            InstanceConnectionSnapshot snapshot = await _nodeClient.PollSnapshotAsync(profile, session.Token, ct);
            _snapshots[instanceId] = snapshot;
            return snapshot;
        }
        catch (NodeApiException ex)
        {
            InstanceConnectionState state = ex.StatusCode is 401 or 403
                ? InstanceConnectionState.Unauthorized
                : InstanceConnectionState.Degraded;

            InstanceConnectionSnapshot errorSnapshot = new(
                profile.InstanceId,
                profile.DisplayName,
                state,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                DateTimeOffset.UtcNow,
                ex.Title);

            _snapshots[instanceId] = errorSnapshot;
            return errorSnapshot;
        }
        catch (HttpRequestException ex)
        {
            InstanceConnectionSnapshot errorSnapshot = new(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Offline,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                DateTimeOffset.UtcNow,
                ex.Message);

            _snapshots[instanceId] = errorSnapshot;
            return errorSnapshot;
        }
        catch (Exception ex)
        {
            InstanceConnectionSnapshot errorSnapshot = new(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Unauthorized,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                DateTimeOffset.UtcNow,
                ex.Message);

            _snapshots[instanceId] = errorSnapshot;
            return errorSnapshot;
        }
    }

    /// <inheritdoc/>
    public void DisconnectInstance(string instanceId)
    {
        // WHAT: Disconnects the specified instance and wipes its cached Bearer token.
        // WHY:  Mandatory PKI hygiene and idle security invariant (Prime Directive 12).
        // HOW:  Removes and zeroes out the char buffer in _cachedTokens, updates snapshot to Disconnected.
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (_cachedTokens.TryRemove(instanceId, out char[]? buffer))
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        if (_snapshots.TryGetValue(instanceId, out InstanceConnectionSnapshot? current))
        {
            _snapshots[instanceId] = new InstanceConnectionSnapshot(
                current.InstanceId,
                current.DisplayName,
                InstanceConnectionState.Disconnected,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                DateTimeOffset.UtcNow,
                null);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstanceAlertEvent>> PollAllConnectedAsync(CancellationToken ct = default)
    {
        // WHAT: Polls all authenticated instances and diffs state changes to generate structured alerts.
        // WHY:  Drives tray badges and notification toasts for node offline, completion, and thermal events.
        // HOW:  Iterates _cachedTokens, polls snapshot via INanoWeaverNodeClient, diffs with previous snapshot.
        ThrowIfDisposed();

        List<InstanceAlertEvent> alerts = new();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        string[] connectedIds = _cachedTokens.Keys.ToArray();

        foreach (string instanceId in connectedIds)
        {
            if (!_cachedTokens.TryGetValue(instanceId, out char[]? tokenChars) || tokenChars == null)
            {
                continue;
            }

            InstanceProfile? profile = await _profileRepository.GetProfileAsync(instanceId, ct);
            if (profile == null)
            {
                continue;
            }

            string token = new(tokenChars);

            _snapshots.TryGetValue(instanceId, out InstanceConnectionSnapshot? previousSnapshot);
            previousSnapshot ??= new InstanceConnectionSnapshot(
                profile.InstanceId,
                profile.DisplayName,
                InstanceConnectionState.Disconnected,
                0.0,
                0.0,
                0.0,
                0,
                0.0,
                nowUtc,
                null);

            InstanceConnectionSnapshot newSnapshot = await _nodeClient.PollSnapshotAsync(profile, token, ct);
            _snapshots[instanceId] = newSnapshot;

            // Diff 1: Online / Degraded -> Offline
            if ((previousSnapshot.State is InstanceConnectionState.Online or InstanceConnectionState.Degraded) &&
                newSnapshot.State == InstanceConnectionState.Offline)
            {
                alerts.Add(new InstanceAlertEvent(
                    Guid.NewGuid().ToString("N"),
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceAlertSeverity.Critical,
                    "Instance Offline",
                    $"Instance '{profile.DisplayName}' ({profile.InstanceId}) transitioned to offline: {newSnapshot.LastErrorTitle ?? "Connection lost."}",
                    nowUtc));
            }

            // Diff 2: Offline -> Online
            if (previousSnapshot.State == InstanceConnectionState.Offline &&
                newSnapshot.State == InstanceConnectionState.Online)
            {
                alerts.Add(new InstanceAlertEvent(
                    Guid.NewGuid().ToString("N"),
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceAlertSeverity.Info,
                    "Instance Online",
                    $"Instance '{profile.DisplayName}' ({profile.InstanceId}) is back online and responding normally.",
                    nowUtc));
            }

            // Diff 3: Simulation completed (ActiveSimulationCount > 0 -> 0)
            if (previousSnapshot.ActiveSimulationCount > 0 && newSnapshot.ActiveSimulationCount == 0)
            {
                alerts.Add(new InstanceAlertEvent(
                    Guid.NewGuid().ToString("N"),
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceAlertSeverity.Info,
                    "Simulation Completed",
                    $"Simulation campaign completed on instance '{profile.DisplayName}'.",
                    nowUtc));
            }

            // Diff 4: Thermal alert (GpuTemperatureCelsius >= 85.0 when previous < 85.0)
            if (newSnapshot.GpuTemperatureCelsius >= 85.0 && previousSnapshot.GpuTemperatureCelsius < 85.0)
            {
                alerts.Add(new InstanceAlertEvent(
                    Guid.NewGuid().ToString("N"),
                    profile.InstanceId,
                    profile.DisplayName,
                    InstanceAlertSeverity.Warning,
                    "Elevated GPU Temperature",
                    $"GPU temperature on instance '{profile.DisplayName}' reached {newSnapshot.GpuTemperatureCelsius:F1}°C (threshold: 85.0°C).",
                    nowUtc));
            }

            // Handle unauthorized status: wipe cached token so we don't keep polling with stale credentials
            if (newSnapshot.State == InstanceConnectionState.Unauthorized)
            {
                DisconnectInstance(instanceId);
                _snapshots[instanceId] = newSnapshot;
            }
        }

        return alerts;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Disposes coordinator resources and zeroes cached token memory.
        // WHY:  Mandatory cleanup to prevent credential leakage in long-running processes.
        // HOW:  Clears and zeroes all token buffers in _cachedTokens.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (char[] buffer in _cachedTokens.Values)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        _cachedTokens.Clear();
        _snapshots.Clear();
    }

    /// <summary>
    /// Safely stores the bearer token into the session cache as a character buffer.
    /// </summary>
    /// <param name="instanceId">Instance identifier.</param>
    /// <param name="token">Bearer token string.</param>
    private void StoreCachedToken(string instanceId, string token)
    {
        // WHAT: Copies token into a dedicated char buffer in the session cache.
        // WHY:  Enables zeroing the memory buffer upon disconnection per Prime Directive 12.
        // HOW:  Removes any prior buffer, zeroes it, and inserts the new token characters.
        if (_cachedTokens.TryRemove(instanceId, out char[]? oldBuffer))
        {
            Array.Clear(oldBuffer, 0, oldBuffer.Length);
        }

        char[] newBuffer = token.ToCharArray();
        _cachedTokens[instanceId] = newBuffer;
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        // WHAT: Guards operations against use-after-dispose.
        // WHY:  Ensures fail-fast semantics if coordinator has already been shut down.
        // HOW:  Checks _disposed boolean flag.
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
