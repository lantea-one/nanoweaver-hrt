using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Background hosted worker periodically sweeping enabled federated peer subscriptions to pull signed
/// ML-DSA-65 lot revocation feeds on their configured cadence.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> An ASP.NET Core <see cref="BackgroundService"/> waking on a configurable
/// poll tick (<c>FederatedSync:PollTickSeconds</c>, default 60 s) and running due-only peer revocation sync sweeps.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P206): Ensures sovereign clinics automatically ingest upstream
/// recalls in the background without requiring manual operator clicks.
/// </para>
/// <para>
/// <strong>How it works:</strong> Exits immediately when <c>FederatedSync:Enabled</c> is false; otherwise delays
/// first via an injectable delay delegate (preventing startup races during integration testing) and executes
/// <see cref="ExecuteSyncSweepAsync"/> inside a resilient fault-isolating loop.
/// </para>
/// </remarks>
public sealed class FederatedRevocationSyncHostedService : BackgroundService
{
    /// <summary>
    /// Default poll tick interval in seconds when <c>FederatedSync:PollTickSeconds</c> is omitted or invalid.
    /// </summary>
    public const int DefaultPollTickSeconds = 60;

    /// <summary>
    /// Maximum clamped poll tick interval in seconds (24 hours = 86,400 seconds).
    /// </summary>
    public const int MaxPollTickSeconds = 86_400;

    /// <summary>
    /// Domain service performing SSRF-hardened peer revocation pull-sync sweeps.
    /// </summary>
    private readonly IFederatedPeerSyncService _syncService;

    /// <summary>
    /// Optional structured logger for fault-isolated background sweep diagnostics.
    /// </summary>
    private readonly ILogger<FederatedRevocationSyncHostedService>? _logger;

    /// <summary>
    /// Injectable delay delegate enabling deterministic zero-sleep unit testing.
    /// </summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    /// <summary>
    /// Whether periodic background sweeps are enabled via <c>FederatedSync:Enabled</c>.
    /// </summary>
    private readonly bool _enabled;

    /// <summary>
    /// Clamped interval between background sweep ticks.
    /// </summary>
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedRevocationSyncHostedService"/> class.
    /// </summary>
    /// <param name="syncService">The federated peer revocation pull-sync service.</param>
    /// <param name="configuration">Application configuration providing <c>FederatedSync:Enabled</c> and <c>FederatedSync:PollTickSeconds</c>.</param>
    /// <param name="logger">Optional logger for sweep diagnostics.</param>
    /// <param name="delayAsync">Optional deterministic delay delegate for unit testing; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public FederatedRevocationSyncHostedService(
        IFederatedPeerSyncService syncService,
        IConfiguration configuration,
        ILogger<FederatedRevocationSyncHostedService>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        // WHAT: Parses configuration flags and stores dependencies for periodic peer revocation sweeps.
        // WHY:  Allows operators to disable or tune background polling and guarantees deterministic test execution.
        // HOW:  Validates required arguments, reads FederatedSync config keys with safe clamping, and stores injectable delay delegate.
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(configuration);

        _syncService = syncService;
        _logger = logger;
        _delayAsync = delayAsync ?? ((interval, ct) => Task.Delay(interval, ct));

        string? rawEnabled = configuration["FederatedSync:Enabled"];
        _enabled = !bool.TryParse(rawEnabled, out bool parsedEnabled) || parsedEnabled;

        string? rawTick = configuration["FederatedSync:PollTickSeconds"];
        int tickSeconds = int.TryParse(rawTick, out int parsedTick)
            ? Math.Clamp(parsedTick, 1, MaxPollTickSeconds)
            : DefaultPollTickSeconds;
        _pollInterval = TimeSpan.FromSeconds(tickSeconds);
    }

    /// <summary>
    /// Gets a value indicating whether background sweeps are enabled for this host.
    /// </summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// Gets the configured polling interval between sweep ticks.
    /// </summary>
    public TimeSpan PollInterval => _pollInterval;

    /// <summary>
    /// Executes a single due-only federated peer revocation pull-sync sweep immediately.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The aggregate <see cref="FederatedPeerSyncSweepResult"/> produced by the sweep.</returns>
    public Task<FederatedPeerSyncSweepResult> ExecuteSyncSweepAsync(CancellationToken cancellationToken = default)
    {
        // WHAT: Triggers a due-only revocation pull-sync sweep attributed to the system worker identity.
        // WHY:  Provides a directly testable single-tick entry point decoupled from BackgroundService timers.
        // HOW:  Delegates to IFederatedPeerSyncService.SyncAllEnabledPeersAsync with dueOnly: true.
        return _syncService.SyncAllEnabledPeersAsync(
            FederatedPeerSyncService.SystemOperatorId,
            dueOnly: true,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // WHAT: Runs the periodic background peer revocation pull-sync loop until host shutdown.
        // WHY:  Keeps the federated recall ledger synchronized continuously while isolating transient sweep errors.
        // HOW:  Returns immediately when disabled; otherwise delays first, executes ExecuteSyncSweepAsync, and catches non-cancellation faults.
        if (!_enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _delayAsync(_pollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ExecuteSyncSweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Federated peer revocation background sweep encountered an unexpected error.");
            }
        }
    }
}
