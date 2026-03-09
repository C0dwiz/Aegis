using System.Collections.Concurrent;
using Aegis.Common.Logging;

namespace Aegis.Common;

/// <summary>
/// Manages acknowledgments and retransmissions for reliable message delivery
/// </summary>
public class AcknowledgmentManager
{
    private readonly ConcurrentDictionary<ulong, PendingMessage> _pendingMessages;
    private readonly ConcurrentDictionary<ulong, DateTime> _lastAckTime;
    private readonly ILogger _logger;
    private readonly int _retransmitTimeoutMs;
    private readonly int _maxRetries;
    private Timer? _cleanupTimer;

    public AcknowledgmentManager(ILogger? logger = null, int retransmitTimeoutMs = 5000, int maxRetries = 3)
    {
        _pendingMessages = new ConcurrentDictionary<ulong, PendingMessage>();
        _lastAckTime = new ConcurrentDictionary<ulong, DateTime>();
        _logger = logger ?? new NullLogger();
        _retransmitTimeoutMs = retransmitTimeoutMs;
        _maxRetries = maxRetries;
        
        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupExpiredMessages, null, 
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Register a pending message that requires acknowledgment
    /// </summary>
    public void RegisterPendingMessage(ulong connectionId, ulong sequenceId, byte[] messageData)
    {
        var pending = new PendingMessage
        {
            ConnectionId = connectionId,
            SequenceId = sequenceId,
            MessageData = messageData,
            SentAt = DateTime.UtcNow,
            RetryCount = 0
        };

        _pendingMessages.TryAdd(sequenceId, pending);
        _logger.Info($"Registered pending message {sequenceId} for connection {connectionId}");
    }

    /// <summary>
    /// Mark a message as acknowledged
    /// </summary>
    public bool AcknowledgeMessage(ulong sequenceId)
    {
        var removed = _pendingMessages.TryRemove(sequenceId, out var pending);
        if (removed && pending != null)
        {
            _lastAckTime.AddOrUpdate(pending.ConnectionId, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            _logger.Info($"Message {sequenceId} acknowledged");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a message needs retransmission
    /// </summary>
    public bool ShouldRetransmit(ulong sequenceId, out PendingMessage? message)
    {
        message = null;
        if (!_pendingMessages.TryGetValue(sequenceId, out var pending))
            return false;

        var elapsed = DateTime.UtcNow - pending.SentAt;
        if (elapsed.TotalMilliseconds >= _retransmitTimeoutMs && pending.RetryCount < _maxRetries)
        {
            message = pending;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get all messages pending acknowledgment for a connection
    /// </summary>
    public List<PendingMessage> GetPendingMessages(ulong connectionId)
    {
        return _pendingMessages.Values
            .Where(m => m.ConnectionId == connectionId)
            .ToList();
    }

    /// <summary>
    /// Increment retry count for a message
    /// </summary>
    public void IncrementRetryCount(ulong sequenceId)
    {
        if (_pendingMessages.TryGetValue(sequenceId, out var message))
        {
            message.RetryCount++;
            message.SentAt = DateTime.UtcNow;
            
            if (message.RetryCount >= _maxRetries)
            {
                _logger.Warning($"Message {sequenceId} exceeded max retries ({_maxRetries})");
                _pendingMessages.TryRemove(sequenceId, out _);
            }
        }
    }

    /// <summary>
    /// Remove all pending messages for a connection
    /// </summary>
    public void RemoveConnectionMessages(ulong connectionId)
    {
        var toRemove = _pendingMessages.Values
            .Where(m => m.ConnectionId == connectionId)
            .Select(m => m.SequenceId)
            .ToList();

        foreach (var seqId in toRemove)
        {
            _pendingMessages.TryRemove(seqId, out _);
        }

        _logger.Info($"Removed {toRemove.Count} pending messages for connection {connectionId}");
    }

    private void CleanupExpiredMessages(object? state)
    {
        var now = DateTime.UtcNow;
        var maxAge = TimeSpan.FromSeconds(60);

        var toRemove = _pendingMessages.Values
            .Where(m => now - m.SentAt > maxAge && m.RetryCount >= _maxRetries)
            .Select(m => m.SequenceId)
            .ToList();

        foreach (var seqId in toRemove)
        {
            if (_pendingMessages.TryRemove(seqId, out var msg))
            {
                _logger.Warning($"Cleaned up expired message {seqId}");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Represents a message pending acknowledgment
/// </summary>
public class PendingMessage
{
    public ulong ConnectionId { get; set; }
    public ulong SequenceId { get; set; }
    public byte[] MessageData { get; set; } = Array.Empty<byte>();
    public DateTime SentAt { get; set; }
    public int RetryCount { get; set; }
}

/// <summary>
/// Handles message deduplication
/// </summary>
public class MessageDeduplicator
{
    private const ulong DefaultWindowSize = 1024;
    private readonly ConcurrentDictionary<ulong, SequenceWindow> _sequenceWindows;
    private readonly ILogger _logger;
    private Timer? _cleanupTimer;

    public MessageDeduplicator(ILogger? logger = null)
    {
        _sequenceWindows = new ConcurrentDictionary<ulong, SequenceWindow>();
        _logger = logger ?? new NullLogger();
        
        _cleanupTimer = new Timer(CleanupOldEntries, null, 
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool TryAcceptSequence(ulong connectionId, ulong sequenceId, out string rejectionReason)
    {
        rejectionReason = string.Empty;
        var window = _sequenceWindows.GetOrAdd(connectionId, _ => new SequenceWindow());

        lock (window.Sync)
        {
            if (window.Sequences.Contains(sequenceId))
            {
                rejectionReason = "duplicate sequence";
                return false;
            }

            if (window.HighestSequenceId > 0 && sequenceId < window.HighestSequenceId)
            {
                var delta = window.HighestSequenceId - sequenceId;
                if (delta >= DefaultWindowSize)
                {
                    rejectionReason = $"stale sequence outside sliding window (highest={window.HighestSequenceId}, incoming={sequenceId})";
                    return false;
                }
            }

            if (sequenceId > window.HighestSequenceId)
            {
                window.HighestSequenceId = sequenceId;
            }

            window.Sequences.Add(sequenceId);

            if (window.HighestSequenceId > DefaultWindowSize)
            {
                var minAllowed = window.HighestSequenceId - DefaultWindowSize;
                window.Sequences.RemoveWhere(x => x < minAllowed);
            }

            window.LastUpdatedAt = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    public bool IsProcessed(ulong connectionId, ulong sequenceId)
    {
        if (_sequenceWindows.TryGetValue(connectionId, out var window))
        {
            lock (window.Sync)
            {
                return window.Sequences.Contains(sequenceId);
            }
        }
        return false;
    }

    /// <summary>
    /// Mark message as processed
    /// </summary>
    public void MarkAsProcessed(ulong connectionId, ulong sequenceId)
    {
        _ = TryAcceptSequence(connectionId, sequenceId, out _);
    }

    /// <summary>
    /// Clear processed messages for a connection
    /// </summary>
    public void ClearConnection(ulong connectionId)
    {
        _sequenceWindows.TryRemove(connectionId, out _);
    }

    private void CleanupOldEntries(object? state)
    {
        var staleThreshold = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kvp in _sequenceWindows)
        {
            var window = kvp.Value;
            lock (window.Sync)
            {
                if (window.LastUpdatedAt < staleThreshold)
                {
                    _sequenceWindows.TryRemove(kvp.Key, out _);
                    continue;
                }

                if (window.HighestSequenceId > DefaultWindowSize)
                {
                    var minAllowed = window.HighestSequenceId - DefaultWindowSize;
                    window.Sequences.RemoveWhere(x => x < minAllowed);
                }
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private sealed class SequenceWindow
    {
        public object Sync { get; } = new object();
        public ulong HighestSequenceId { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        public HashSet<ulong> Sequences { get; } = new HashSet<ulong>();
    }
}
