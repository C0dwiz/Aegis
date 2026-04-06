using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aegis.Server.Services;

public sealed record BalancerNodeSnapshot(
    string NodeId,
    bool IsHealthy,
    int ActiveConnections,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    DateTime LastHeartbeatUtc
);

internal sealed class BalancerNodeState
{
    public string NodeId { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public bool IsHealthy { get; set; } = true;
    public int ActiveConnections;
    public double CpuLoadPercent { get; set; }
    public double MemoryLoadPercent { get; set; }
}

public sealed class ConnectionBalancer
{
    private static readonly TimeSpan NodeMaxAge = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, BalancerNodeState> _nodes = new();
    private readonly ILogger<ConnectionBalancer> _logger;
    private string? _localNodeId;

    public ConnectionBalancer(ILogger<ConnectionBalancer> logger)
    {
        _logger = logger;
    }

    public void RegisterOrUpdateNode(string nodeId, bool isHealthy, double cpuLoadPercent, double memoryLoadPercent)
    {
        var now = DateTime.UtcNow;
        _nodes.AddOrUpdate(
            nodeId,
            _ => new BalancerNodeState
            {
                NodeId = nodeId,
                IsHealthy = isHealthy,
                CpuLoadPercent = cpuLoadPercent,
                MemoryLoadPercent = memoryLoadPercent,
                LastHeartbeatUtc = now
            },
            (_, existing) =>
            {
                existing.IsHealthy = isHealthy;
                existing.CpuLoadPercent = cpuLoadPercent;
                existing.MemoryLoadPercent = memoryLoadPercent;
                existing.LastHeartbeatUtc = now;
                return existing;
            });

        CleanupExpiredNodes(now);
    }

    public void ConfigureLocalNode(string nodeId)
    {
        _localNodeId = nodeId;
        RegisterOrUpdateNode(nodeId, isHealthy: true, cpuLoadPercent: 0, memoryLoadPercent: 0);
    }

    public bool ShouldAcceptLocalConnection()
    {
        if (string.IsNullOrWhiteSpace(_localNodeId))
        {
            return true;
        }

        if (!_nodes.TryGetValue(_localNodeId, out var node))
        {
            return false;
        }

        if (ShouldDrainNode(_localNodeId))
        {
            return false;
        }
        return true;
    }

    public void RecordLocalConnectionAccepted()
    {
        if (string.IsNullOrWhiteSpace(_localNodeId))
        {
            return;
        }

        if (_nodes.TryGetValue(_localNodeId, out var node))
        {
            Interlocked.Increment(ref node.ActiveConnections);
        }
    }

    public void ReleaseLocalConnection()
    {
        if (string.IsNullOrWhiteSpace(_localNodeId))
        {
            return;
        }

        ReleaseConnection(_localNodeId);
    }

    public void UpdateLocalHealth(bool isHealthy, double cpuLoadPercent, double memoryLoadPercent)
    {
        if (string.IsNullOrWhiteSpace(_localNodeId))
        {
            return;
        }

        RegisterOrUpdateNode(_localNodeId, isHealthy, cpuLoadPercent, memoryLoadPercent);
    }

    public string? SelectBestNode()
    {
        var now = DateTime.UtcNow;
        CleanupExpiredNodes(now);

        var candidate = _nodes.Values
            .Where(n => n.IsHealthy)
            .OrderBy(n => n.ActiveConnections)
            .ThenBy(n => n.CpuLoadPercent + n.MemoryLoadPercent)
            .FirstOrDefault();

        if (candidate == null)
        {
            return null;
        }

        Interlocked.Increment(ref candidate.ActiveConnections);
        return candidate.NodeId;
    }

    public void ReleaseConnection(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            InterlockedExtensions.DecrementClampToZero(ref node.ActiveConnections);
        }
    }

    public IReadOnlyList<BalancerNodeSnapshot> GetSnapshots()
    {
        var now = DateTime.UtcNow;
        CleanupExpiredNodes(now);

        return _nodes.Values
            .Select(n => new BalancerNodeSnapshot(
                n.NodeId,
                n.IsHealthy,
                Volatile.Read(ref n.ActiveConnections),
                n.CpuLoadPercent,
                n.MemoryLoadPercent,
                n.LastHeartbeatUtc))
            .OrderBy(s => s.NodeId)
            .ToArray();
    }

    public bool ShouldDrainNode(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            return false;
        }

        return !node.IsHealthy || node.CpuLoadPercent > 90 || node.MemoryLoadPercent > 90;
    }

    private void CleanupExpiredNodes(DateTime nowUtc)
    {
        foreach (var pair in _nodes)
        {
            if (nowUtc - pair.Value.LastHeartbeatUtc > NodeMaxAge)
            {
                if (_nodes.TryRemove(pair.Key, out _))
                {
                    _logger.LogInformation("Removed stale balancer node {NodeId}", pair.Key);
                }
            }
        }
    }
}

internal static class InterlockedExtensions
{
    public static void DecrementClampToZero(ref int target)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (current <= 0)
            {
                return;
            }

            var next = current - 1;
            if (Interlocked.CompareExchange(ref target, next, current) == current)
            {
                return;
            }
        }
    }
}
