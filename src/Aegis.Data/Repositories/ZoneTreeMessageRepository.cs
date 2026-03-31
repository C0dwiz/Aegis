using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Tenray.ZoneTree;
using Tenray.ZoneTree.Options;
using Aegis.Data.Entities;

namespace Aegis.Data.Repositories;

public class ZoneTreeMessageRepository : IMessageRepository, IDisposable
{
    private readonly IZoneTree<ulong, Message> _zoneTree;
    private const int DefaultPageSize = 100;
    
    // In-memory cache for unread counts per user (TTL = 5 min)
    // Invalidated on MarkMessagesReadAsync to prevent stale data
    private readonly ConcurrentDictionary<ulong, (Dictionary<ulong, int> Counts, DateTime ExpiresAt)> _unreadCountCache = new();
    private const int UnreadCountCacheTtlSeconds = 300;

    public ZoneTreeMessageRepository(string dbPath)
    {
        _zoneTree = new ZoneTreeFactory<ulong, Message>()
            .SetDataDirectory(dbPath)
            .SetKeySerializer(new UlongSerializer())
            .SetValueSerializer(new MessageSerializer())
            .OpenOrCreate();
    }

    public Task<Message?> GetByIdAsync(ulong id)
    {
        return Task.FromResult(_zoneTree.TryGet(id, out var message) ? message : null);
    }

    public Task<IEnumerable<Message>> GetAllAsync()
    {
        var all = new List<Message>();
        using var iterator = _zoneTree.CreateIterator();
        while (iterator.Next())
        {
            all.Add(iterator.CurrentValue);
        }
        return Task.FromResult<IEnumerable<Message>>(all);
    }

    public Task<IEnumerable<Message>> FindAsync(System.Linq.Expressions.Expression<Func<Message, bool>> predicate)
    {
        var compiledPredicate = predicate.Compile();
        var result = new List<Message>();

        using var iterator = _zoneTree.CreateIterator();
        while (iterator.Next())
        {
            if (compiledPredicate(iterator.CurrentValue))
                result.Add(iterator.CurrentValue);
        }
        return Task.FromResult<IEnumerable<Message>>(result);
    }

    public Task<Message> CreateAsync(Message entity)
    {
        _zoneTree.Upsert(entity.Id, entity);
        return Task.FromResult(entity);
    }

    public Task<Message> UpdateAsync(Message entity)
    {
        _zoneTree.Upsert(entity.Id, entity);
        return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(ulong id)
    {
        return Task.FromResult(_zoneTree.TryDelete(id, out _));
    }

    public Task<IEnumerable<Message>> GetConversationAsync(ulong userId1, ulong userId2, int limit = DefaultPageSize)
    {
        var result = new List<Message>();
        using var iterator = _zoneTree.CreateIterator();

        while (iterator.Next() && result.Count < limit)
        {
            var m = iterator.CurrentValue;
            if ((m.FromUserId == userId1 && m.ToUserId == userId2) ||
                (m.FromUserId == userId2 && m.ToUserId == userId1))
            {
                result.Add(m);
            }
        }
        return Task.FromResult<IEnumerable<Message>>(result.OrderBy(m => m.Id));
    }

    public Task<IEnumerable<Message>> GetConversationBeforeAsync(ulong userId1, ulong userId2, ulong? beforeMessageId, int limit = DefaultPageSize)
    {
        var result = new List<Message>();
        using var iterator = _zoneTree.CreateIterator();

        while (iterator.Next())
        {
            var m = iterator.CurrentValue;
            if (beforeMessageId.HasValue && m.Id >= beforeMessageId.Value) continue;

            if ((m.FromUserId == userId1 && m.ToUserId == userId2) ||
                (m.FromUserId == userId2 && m.ToUserId == userId1))
            {
                result.Add(m);
            }
        }
        return Task.FromResult<IEnumerable<Message>>(result.OrderByDescending(m => m.Id).Take(limit));
    }

    public Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId)
    {
        var result = new List<Message>();
        using var iterator = _zoneTree.CreateIterator();
        while (iterator.Next())
        {
            var m = iterator.CurrentValue;
            if (m.ToUserId == userId && !m.IsDelivered)
                result.Add(m);
        }
        return Task.FromResult<IEnumerable<Message>>(result);
    }

    public Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId)
    {
        var result = new List<Message>();
        using var iterator = _zoneTree.CreateIterator();
        while (iterator.Next())
        {
            var m = iterator.CurrentValue;
            if (m.ToUserId == userId && !m.IsRead)
                result.Add(m);
        }
        return Task.FromResult<IEnumerable<Message>>(result);
    }

    public Task<IDictionary<ulong, int>> GetUnreadCountsBySenderAsync(ulong userId)
    {
        // Check cache first to avoid full ZoneTree scans
        if (_unreadCountCache.TryGetValue(userId, out var cached) && 
            cached.ExpiresAt > DateTime.UtcNow)
        {
            return Task.FromResult<IDictionary<ulong, int>>(
                new Dictionary<ulong, int>(cached.Counts));
        }

        // Cache miss: scan ZoneTree
        var dict = new Dictionary<ulong, int>();
        using var iterator = _zoneTree.CreateIterator();
        while (iterator.Next())
        {
            var m = iterator.CurrentValue;
            if (m.ToUserId == userId && !m.IsRead)
            {
                if (!dict.TryAdd(m.FromUserId, 1))
                    dict[m.FromUserId]++;
            }
        }

        // Update cache with TTL
        _unreadCountCache[userId] = (dict, DateTime.UtcNow.AddSeconds(UnreadCountCacheTtlSeconds));
        return Task.FromResult<IDictionary<ulong, int>>(dict);
    }

    public Task MarkMessagesDeliveredAsync(IEnumerable<ulong> messageIds, ulong recipientUserId)
    {
        foreach (var id in messageIds.Distinct())
        {
            if (_zoneTree.TryGet(id, out var message) &&
                message.ToUserId == recipientUserId &&
                !message.IsDeleted &&
                !message.IsDelivered)
            {
                message.IsDelivered = true;
                message.DeliveredAt = DateTime.UtcNow;
                _zoneTree.Upsert(id, message);
            }
        }
        return Task.CompletedTask;
    }

    public Task MarkMessagesReadAsync(IEnumerable<ulong> messageIds, ulong readerUserId)
    {
        var now = DateTime.UtcNow;
        var invalidateAffectedSenders = new HashSet<ulong>();
        
        foreach (var id in messageIds.Distinct())
        {
            if (_zoneTree.TryGet(id, out var message) &&
                message.ToUserId == readerUserId &&
                !message.IsDeleted &&
                !message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = now;
                if (!message.IsDelivered)
                {
                    message.IsDelivered = true;
                    message.DeliveredAt = now;
                }

                _zoneTree.Upsert(id, message);
                // Track senders whose message counts changed
                invalidateAffectedSenders.Add(message.FromUserId);
            }
        }

        // Invalidate unread count cache for this reader (since we updated IsRead)
        _unreadCountCache.TryRemove(readerUserId, out _);
        
        // Optionally invalidate cache for affected senders (if tracking their sent→read transitions)
        // Uncomment if sender-side read receipts need real-time cache updates:
        // foreach (var senderId in invalidateAffectedSenders)
        //     _unreadCountCache.TryRemove(senderId, out _);

        return Task.CompletedTask;
    }

    public Task<Message?> GetMessageForEditAsync(ulong messageId, ulong userId)
    {
        if (_zoneTree.TryGet(messageId, out var message) && message.FromUserId == userId)
        {
            return Task.FromResult<Message?>(message);
        }
        return Task.FromResult<Message?>(null);
    }

    public void Dispose()
    {
        _zoneTree?.Dispose();
    }
}
