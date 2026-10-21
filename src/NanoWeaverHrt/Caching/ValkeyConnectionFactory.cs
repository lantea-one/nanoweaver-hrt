using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace NanoWeaverHrt.Caching;

/// <summary>
/// Thread-safe connection factory managing multiplexed connections to a Valkey instance.
/// Provides automatic reconnection, credential redaction, and offline resilience.
/// </summary>
public sealed class ValkeyConnectionFactory : IValkeyConnectionFactory
{
    /// <summary>
    /// Regular expression pattern used to sanitize credentials and passwords from connection strings.
    /// </summary>
    private static readonly Regex PasswordMaskRegex = new(@"password\s*=\s*[^,;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The active underlying StackExchange.Redis multiplexer instance.
    /// </summary>
    private IConnectionMultiplexer? _multiplexer;

    /// <summary>
    /// Logger instance used to record connection diagnostics and topology events.
    /// </summary>
    private readonly ILogger<ValkeyConnectionFactory> _logger;

    /// <summary>
    /// Sanitized connection endpoint descriptor safe for logging and public telemetry.
    /// </summary>
    private readonly string _sanitizedEndpoint;

    /// <summary>
    /// Synchronization lock object guarding multiplexer initialization and teardown.
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// Indicates whether the factory owns the multiplexer lifecycle and should dispose it.
    /// </summary>
    private readonly bool _ownsMultiplexer;

    /// <summary>
    /// Tracking flag indicating whether this factory has been disposed.
    /// </summary>
    private bool _isDisposed;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            IConnectionMultiplexer? mux = _multiplexer;
            return mux != null && mux.IsConnected;
        }
    }

    /// <inheritdoc/>
    public string SanitizedEndpoint => _sanitizedEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValkeyConnectionFactory"/> class from application configuration.
    /// </summary>
    /// <param name="configuration">Configuration provider containing VALKEY_CONNECTION_STRING.</param>
    /// <param name="logger">Optional logger instance for diagnostics.</param>
    public ValkeyConnectionFactory(IConfiguration configuration, ILogger<ValkeyConnectionFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? NullLogger<ValkeyConnectionFactory>.Instance;

        string? connStr = configuration["VALKEY_CONNECTION_STRING"]
            ?? configuration["WeaverConfig:ValkeyConnectionString"]
            ?? Environment.GetEnvironmentVariable("VALKEY_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connStr))
        {
            _sanitizedEndpoint = "none (in-memory standalone)";
            _ownsMultiplexer = false;
            _multiplexer = null;
            _logger.LogInformation("Valkey connection string not provided. Operating in sovereign standalone in-memory mode.");
            return;
        }

        _sanitizedEndpoint = SanitizeConnectionString(connStr);
        _ownsMultiplexer = true;

        try
        {
            // WHAT: Parses configuration options with non-blocking initial connection.
            // WHY:  Ensures that a slow or restarting Valkey container does not block node boot.
            // HOW:  Sets AbortOnConnectFail = false and ConnectTimeout = 2500ms.
            ConfigurationOptions options = ConfigurationOptions.Parse(connStr);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2500;
            options.AsyncTimeout = 2500;

            _multiplexer = ConnectionMultiplexer.Connect(options);
            _logger.LogInformation("Multiplexed connection initiated to Valkey endpoint: {Endpoint}", _sanitizedEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Valkey multiplexer for {Endpoint}. Falling back to in-memory mode.", _sanitizedEndpoint);
            _multiplexer = null;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValkeyConnectionFactory"/> class with an externally managed multiplexer (primarily for unit testing).
    /// </summary>
    /// <param name="multiplexer">Injected multiplexer instance or mock.</param>
    /// <param name="endpoint">Optional custom sanitized endpoint description.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ValkeyConnectionFactory(IConnectionMultiplexer? multiplexer, string? endpoint = null, ILogger<ValkeyConnectionFactory>? logger = null)
    {
        _multiplexer = multiplexer;
        _ownsMultiplexer = false;
        _sanitizedEndpoint = endpoint ?? (multiplexer != null ? "custom-multiplexer" : "none (in-memory standalone)");
        _logger = logger ?? NullLogger<ValkeyConnectionFactory>.Instance;
    }

    /// <inheritdoc/>
    public IConnectionMultiplexer? GetConnection()
    {
        return _multiplexer;
    }

    /// <inheritdoc/>
    public IDatabase? GetDatabase(int db = -1)
    {
        // WHAT: Returns the Redis/Valkey database accessor if connected.
        // WHY:  Allows consumers to perform low-level caching, key-value operations, and hash manipulations.
        // HOW:  Delegates to IConnectionMultiplexer.GetDatabase if not null.
        IConnectionMultiplexer? mux = _multiplexer;
        if (mux == null || !mux.IsConnected)
        {
            return null;
        }

        return mux.GetDatabase(db);
    }

    /// <inheritdoc/>
    public ISubscriber? GetSubscriber()
    {
        // WHAT: Returns the Valkey pub/sub subscriber if connected.
        // WHY:  Allows consumers to publish and subscribe to cluster-wide channels.
        // HOW:  Delegates to IConnectionMultiplexer.GetSubscriber if not null.
        IConnectionMultiplexer? mux = _multiplexer;
        if (mux == null || !mux.IsConnected)
        {
            return null;
        }

        return mux.GetSubscriber();
    }

    /// <summary>
    /// Sanitizes connection strings by removing plaintext passwords and sensitive tokens.
    /// </summary>
    /// <param name="connectionString">The raw connection string.</param>
    /// <returns>A sanitized connection string safe for telemetry and logs.</returns>
    private static string SanitizeConnectionString(string connectionString)
    {
        // WHAT: Strips raw credentials using regular expression substitution.
        // WHY:  Upholds zero-information-disclosure security posture.
        // HOW:  Replaces password=<value> with password=[REDACTED].
        return PasswordMaskRegex.Replace(connectionString, "password=[REDACTED]");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Synchronously closes and disposes the multiplexer if owned.
        // WHY:  Releases sockets and background thread resources on shutdown.
        // HOW:  Checks _ownsMultiplexer and calls Dispose under lock.
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_ownsMultiplexer && _multiplexer != null)
            {
                _multiplexer.Dispose();
                _multiplexer = null;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // WHAT: Asynchronously disposes the multiplexer if owned.
        // WHY:  Prevents thread pool blocking during graceful application shutdown.
        // HOW:  Calls CloseAsync and DisposeAsync on the underlying connection multiplexer.
        IConnectionMultiplexer? muxToDispose = null;

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_ownsMultiplexer && _multiplexer != null)
            {
                muxToDispose = _multiplexer;
                _multiplexer = null;
            }
        }

        if (muxToDispose != null)
        {
            await muxToDispose.CloseAsync().ConfigureAwait(false);
            muxToDispose.Dispose();
        }
    }
}
