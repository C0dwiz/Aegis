using Aegis.Common.Logging;
using System.Collections.Concurrent;

namespace Aegis.Common;

/// <summary>
/// Health check service for monitoring server status
/// </summary>
public class HealthCheckService
{
    private readonly ILogger _logger;
    private DateTime _startTime;
    private long _totalMessagesProcessed;
    private long _totalConnectionsAccepted;
    private int _activeConnections;

    public HealthCheckService(ILogger? logger = null)
    {
        _logger = logger ?? new NullLogger();
        _startTime = DateTime.UtcNow;
    }

    public void RecordConnectionAccepted()
    {
        Interlocked.Increment(ref _totalConnectionsAccepted);
        Interlocked.Increment(ref _activeConnections);
    }

    public void RecordConnectionClosed()
    {
        Interlocked.Decrement(ref _activeConnections);
    }

    public void RecordMessageProcessed()
    {
        Interlocked.Increment(ref _totalMessagesProcessed);
    }

    public HealthStatus GetStatus()
    {
        return new HealthStatus
        {
            IsHealthy = true,
            Uptime = DateTime.UtcNow - _startTime,
            ActiveConnections = _activeConnections,
            TotalConnections = _totalConnectionsAccepted,
            TotalMessages = _totalMessagesProcessed,
            Timestamp = DateTime.UtcNow
        };
    }

    public void LogStatus()
    {
        var status = GetStatus();
        _logger.Info($"Health Status - Uptime: {status.Uptime:hh\\:mm\\:ss}, " +
            $"Active: {status.ActiveConnections}, " +
            $"Total Connections: {status.TotalConnections}, " +
            $"Total Messages: {status.TotalMessages}");
    }
}

/// <summary>
/// Health status information
/// </summary>
public class HealthStatus
{
    public bool IsHealthy { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ActiveConnections { get; set; }
    public long TotalConnections { get; set; }
    public long TotalMessages { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Manages graceful shutdown process
/// </summary>
public class GracefulShutdownManager
{
    private readonly CancellationTokenSource _shutdownCts;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;
    private readonly List<Func<Task>> _shutdownHandlers;

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public GracefulShutdownManager(int timeoutSeconds = 30, ILogger? logger = null)
    {
        _shutdownCts = new CancellationTokenSource();
        _timeoutSeconds = timeoutSeconds;
        _logger = logger ?? new NullLogger();
        _shutdownHandlers = new List<Func<Task>>();
    }

    /// <summary>
    /// Register a handler to be called during shutdown
    /// </summary>
    public void RegisterShutdownHandler(Func<Task> handler)
    {
        _shutdownHandlers.Add(handler);
    }

    /// <summary>
    /// Initiate graceful shutdown
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.Info("Initiating graceful shutdown...");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        
        try
        {
            // Execute all registered shutdown handlers
            foreach (var handler in _shutdownHandlers)
            {
                try
                {
                    _logger.Info("Executing shutdown handler...");
                    await handler();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error during shutdown handler execution", ex);
                }
            }

            _logger.Info("Graceful shutdown completed");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Graceful shutdown timed out after {_timeoutSeconds} seconds");
        }
        finally
        {
            _shutdownCts.Cancel();
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        _shutdownCts.Dispose();
    }
}

/// <summary>
/// Request context for tracking request lifecycle
/// </summary>
public class RequestMetrics
{
    public ulong ConnectionId { get; set; }
    public ulong SequenceId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public long ProcessingTimeMs => EndTime.HasValue 
        ? (long)(EndTime.Value - StartTime).TotalMilliseconds 
        : 0;
    public bool IsCompleted => EndTime.HasValue;

    public void Complete()
    {
        EndTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Metrics aggregator for monitoring
/// </summary>
public class MetricsAggregator
{
    private readonly ConcurrentDictionary<ulong, RequestMetrics> _activeRequests;
    private readonly Queue<RequestMetrics> _completedRequests;
    private readonly int _maxHistorySize;
    private readonly ILogger _logger;

    public MetricsAggregator(int maxHistorySize = 10000, ILogger? logger = null)
    {
        _activeRequests = new ConcurrentDictionary<ulong, RequestMetrics>();
        _completedRequests = new Queue<RequestMetrics>(maxHistorySize);
        _maxHistorySize = maxHistorySize;
        _logger = logger ?? new NullLogger();
    }

    public void StartRequest(ulong connectionId, ulong sequenceId)
    {
        var metrics = new RequestMetrics
        {
            ConnectionId = connectionId,
            SequenceId = sequenceId
        };
        _activeRequests.TryAdd(sequenceId, metrics);
    }

    public void CompleteRequest(ulong sequenceId)
    {
        if (_activeRequests.TryRemove(sequenceId, out var metrics))
        {
            metrics.Complete();
            lock (_completedRequests)
            {
                _completedRequests.Enqueue(metrics);
                while (_completedRequests.Count > _maxHistorySize)
                {
                    _completedRequests.Dequeue();
                }
            }
        }
    }

    public AverageMetrics GetAverageMetrics()
    {
        lock (_completedRequests)
        {
            if (_completedRequests.Count == 0)
                return new AverageMetrics();

            var avgTime = _completedRequests.Average(m => m.ProcessingTimeMs);
            var minTime = _completedRequests.Min(m => m.ProcessingTimeMs);
            var maxTime = _completedRequests.Max(m => m.ProcessingTimeMs);

            return new AverageMetrics
            {
                AverageProcessingTimeMs = avgTime,
                MinProcessingTimeMs = minTime,
                MaxProcessingTimeMs = maxTime,
                TotalRequests = _completedRequests.Count
            };
        }
    }
}

/// <summary>
/// Average metrics data
/// </summary>
public class AverageMetrics
{
    public double AverageProcessingTimeMs { get; set; }
    public long MinProcessingTimeMs { get; set; }
    public long MaxProcessingTimeMs { get; set; }
    public int TotalRequests { get; set; }
}
