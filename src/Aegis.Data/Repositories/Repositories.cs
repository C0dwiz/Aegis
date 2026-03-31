using Microsoft.EntityFrameworkCore;
using Aegis.Data.Entities;

namespace Aegis.Data.Repositories;

/// <summary>
/// Generic repository interface
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(ulong id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate);
    Task<T> CreateAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<bool> DeleteAsync(ulong id);
}

/// <summary>
/// Generic repository implementation
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    private readonly AegisDbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(AegisDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(ulong id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public async Task<IEnumerable<T>> FindAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.Where(predicate).ToListAsync();
    }

    public async Task<T> CreateAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T> UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(ulong id)
    {
        var entity = await GetByIdAsync(id);
        if (entity == null) return false;

        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }
}

// ===================== USER REPOSITORY =====================

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> SearchByUsernameAsync(string pattern);
    Task<IEnumerable<User>> SearchByEmailAsync(string pattern);
    Task<IEnumerable<User>> SearchAsync(string query, int limit = 20);
    Task<IDictionary<ulong, string>> GetUsernamesByIdsAsync(IEnumerable<ulong> userIds);
}

public interface IUserAvatarRepository : IRepository<UserAvatar>
{
    Task<IEnumerable<UserAvatar>> GetByUserIdAsync(ulong userId);
    Task<UserAvatar?> GetPrimaryByUserIdAsync(ulong userId);
    Task<UserAvatar> AddForUserAsync(ulong userId, string avatarUrl, bool makePrimary);
    Task<bool> DeleteForUserAsync(ulong userId, ulong avatarId);
    Task<bool> SetPrimaryAsync(ulong userId, ulong avatarId);
}

public class UserAvatarRepository : Repository<UserAvatar>, IUserAvatarRepository
{
    private readonly AegisDbContext _context;

    public UserAvatarRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<UserAvatar>> GetByUserIdAsync(ulong userId)
    {
        return await _context.UserAvatars
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsPrimary)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<UserAvatar?> GetPrimaryByUserIdAsync(ulong userId)
    {
        return await _context.UserAvatars
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsPrimary)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<UserAvatar> AddForUserAsync(ulong userId, string avatarUrl, bool makePrimary)
    {
        if (makePrimary)
        {
            var existingPrimary = await _context.UserAvatars
                .Where(a => a.UserId == userId && a.IsPrimary)
                .ToListAsync();

            foreach (var avatar in existingPrimary)
            {
                avatar.IsPrimary = false;
            }
        }

        var entity = new UserAvatar
        {
            UserId = userId,
            AvatarUrl = avatarUrl,
            IsPrimary = makePrimary,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserAvatars.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteForUserAsync(ulong userId, ulong avatarId)
    {
        var avatar = await _context.UserAvatars
            .FirstOrDefaultAsync(a => a.Id == avatarId && a.UserId == userId);
        if (avatar == null)
        {
            return false;
        }

        var wasPrimary = avatar.IsPrimary;
        _context.UserAvatars.Remove(avatar);
        await _context.SaveChangesAsync();

        if (wasPrimary)
        {
            var next = await _context.UserAvatars
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();
            if (next != null)
            {
                next.IsPrimary = true;
                await _context.SaveChangesAsync();
            }
        }

        return true;
    }

    public async Task<bool> SetPrimaryAsync(ulong userId, ulong avatarId)
    {
        var target = await _context.UserAvatars
            .FirstOrDefaultAsync(a => a.Id == avatarId && a.UserId == userId);
        if (target == null)
        {
            return false;
        }

        var avatars = await _context.UserAvatars
            .Where(a => a.UserId == userId)
            .ToListAsync();

        foreach (var avatar in avatars)
        {
            avatar.IsPrimary = avatar.Id == target.Id;
        }

        await _context.SaveChangesAsync();
        return true;
    }
}

public class UserRepository : Repository<User>, IUserRepository
{
    private readonly AegisDbContext _context;

    public UserRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var normalized = (username ?? string.Empty).Trim();
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == normalized);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == normalized);
    }

    public async Task<IEnumerable<User>> SearchByUsernameAsync(string pattern)
    {
        var normalized = (pattern ?? string.Empty).Trim();
        var query = _context.Users.AsNoTracking().Where(u => u.IsActive);

        if (IsInMemoryProvider())
        {
            return await query
                .Where(u => u.Username.ToLower().Contains(normalized.ToLower()))
                .OrderBy(u => u.Username)
                .ToListAsync();
        }

        return await query
            .Where(u => EF.Functions.ILike(u.Username, $"%{normalized}%"))
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> SearchByEmailAsync(string pattern)
    {
        var normalized = (pattern ?? string.Empty).Trim();
        var query = _context.Users.AsNoTracking().Where(u => u.IsActive);

        if (IsInMemoryProvider())
        {
            return await query
                .Where(u => u.Email.ToLower().Contains(normalized.ToLower()))
                .OrderBy(u => u.Email)
                .ToListAsync();
        }

        return await query
            .Where(u => EF.Functions.ILike(u.Email, $"%{normalized}%"))
            .OrderBy(u => u.Email)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> SearchAsync(string query, int limit = 20)
    {
        var normalized = (query ?? string.Empty).Trim();
        var safeLimit = Math.Clamp(limit, 1, 100);
        var baseQuery = _context.Users.AsNoTracking().Where(u => u.IsActive);

        if (IsInMemoryProvider())
        {
            return await baseQuery
                .Where(u =>
                    u.Username.ToLower().Contains(normalized.ToLower()) ||
                    u.Email.ToLower().Contains(normalized.ToLower()))
                .OrderBy(u => u.Username)
                .Take(safeLimit)
                .ToListAsync();
        }

        return await baseQuery
            .Where(u =>
                EF.Functions.ILike(u.Username, $"%{normalized}%") ||
                EF.Functions.ILike(u.Email, $"%{normalized}%"))
            .OrderBy(u => u.Username)
            .Take(safeLimit)
            .ToListAsync();
    }

    public async Task<IDictionary<ulong, string>> GetUsernamesByIdsAsync(IEnumerable<ulong> userIds)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<ulong, string>();
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username);
    }

    private bool IsInMemoryProvider()
    {
        return string.Equals(
            _context.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);
    }
}

// ===================== BOT REPOSITORIES =====================

public interface IBotRepository : IRepository<Bot>
{
    Task<Bot?> GetByUsernameAsync(string username);
    Task<Bot?> GetByUserIdAsync(ulong userId);
    Task<IEnumerable<Bot>> GetByOwnerUserIdAsync(ulong ownerUserId);
}

public class BotRepository : Repository<Bot>, IBotRepository
{
    private readonly AegisDbContext _context;

    public BotRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Bot?> GetByUsernameAsync(string username)
    {
        return await _context.Bots
            .Include(b => b.User)
            .Include(b => b.OwnerUser)
            .FirstOrDefaultAsync(b => b.Username == username && b.IsActive);
    }

    public async Task<Bot?> GetByUserIdAsync(ulong userId)
    {
        return await _context.Bots
            .Include(b => b.User)
            .Include(b => b.OwnerUser)
            .FirstOrDefaultAsync(b => b.UserId == userId && b.IsActive);
    }

    public async Task<IEnumerable<Bot>> GetByOwnerUserIdAsync(ulong ownerUserId)
    {
        return await _context.Bots
            .Include(b => b.User)
            .Where(b => b.OwnerUserId == ownerUserId && b.IsActive)
            .OrderBy(b => b.Username)
            .ToListAsync();
    }
}

public interface IBotTokenRepository : IRepository<BotToken>
{
    Task<BotToken?> GetActiveByTokenHashAsync(string tokenHash);
    Task<BotToken?> GetLatestActiveByBotIdAsync(ulong botId);
    Task RevokeAllActiveByBotIdAsync(ulong botId);
}

public class BotTokenRepository : Repository<BotToken>, IBotTokenRepository
{
    private readonly AegisDbContext _context;

    public BotTokenRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<BotToken?> GetActiveByTokenHashAsync(string tokenHash)
    {
        return await _context.BotTokens
            .Include(t => t.Bot)
            .ThenInclude(b => b!.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.Bot!.IsActive);
    }

    public async Task<BotToken?> GetLatestActiveByBotIdAsync(ulong botId)
    {
        return await _context.BotTokens
            .Where(t => t.BotId == botId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task RevokeAllActiveByBotIdAsync(ulong botId)
    {
        var now = DateTime.UtcNow;
        var tokens = await _context.BotTokens
            .Where(t => t.BotId == botId && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        await _context.SaveChangesAsync();
    }
}

public interface IBotConversationStateRepository
{
    Task<BotConversationState> GetOrCreateAsync(ulong userId);
    Task<BotConversationState> UpdateAsync(BotConversationState state);
    Task ResetAsync(ulong userId);
}

public class BotConversationStateRepository : IBotConversationStateRepository
{
    private readonly AegisDbContext _context;

    public BotConversationStateRepository(AegisDbContext context)
    {
        _context = context;
    }

    public async Task<BotConversationState> GetOrCreateAsync(ulong userId)
    {
        var state = await _context.BotConversationStates.FirstOrDefaultAsync(x => x.UserId == userId);
        if (state != null)
        {
            return state;
        }

        state = new BotConversationState
        {
            UserId = userId,
            Step = BotConversationStep.Idle,
            UpdatedAt = DateTime.UtcNow
        };

        _context.BotConversationStates.Add(state);
        await _context.SaveChangesAsync();
        return state;
    }

    public async Task<BotConversationState> UpdateAsync(BotConversationState state)
    {
        state.UpdatedAt = DateTime.UtcNow;
        _context.BotConversationStates.Update(state);
        await _context.SaveChangesAsync();
        return state;
    }

    public async Task ResetAsync(ulong userId)
    {
        var state = await GetOrCreateAsync(userId);
        state.Step = BotConversationStep.Idle;
        state.DraftDisplayName = null;
        state.DraftUsername = null;
        state.UpdatedAt = DateTime.UtcNow;
        _context.BotConversationStates.Update(state);
        await _context.SaveChangesAsync();
    }
}

// ===================== SESSION REPOSITORY =====================

public interface ISessionRepository : IRepository<Session>
{
    Task<Session?> GetByTokenAsync(string token);
    Task<IEnumerable<Session>> GetUserActiveSessions(ulong userId);
    Task<bool> DeleteExpiredSessionsAsync();
    Task<Session?> GetByConnectionIdAsync(string connectionId);
    /// <summary>
    /// Gets expired sessions in batches for cleanup. Used by SessionCleanupBackgroundService.
    /// </summary>
    /// <param name="cutoffTime">Sessions with ExpiresAt &lt; cutoffTime are considered expired</param>
    /// <param name="maxResults">Maximum number of results to return per batch (default 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<IEnumerable<Session>> GetExpiredSessionsAsync(DateTime cutoffTime, int maxResults = 1000, CancellationToken cancellationToken = default);
}

public class SessionRepository : Repository<Session>, ISessionRepository
{
    private readonly AegisDbContext _context;

    public SessionRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Session?> GetByTokenAsync(string token)
    {
        return await _context.Sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive);
    }

    public async Task<IEnumerable<Session>> GetUserActiveSessions(ulong userId)
    {
        return await _context.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();
    }

    public async Task<bool> DeleteExpiredSessionsAsync()
    {
        var expiredSessions = _context.Sessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToList();

        if (expiredSessions.Count == 0) return false;

        _context.Sessions.RemoveRange(expiredSessions);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<Session?> GetByConnectionIdAsync(string connectionId)
    {
        return await _context.Sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.ConnectionId == connectionId && s.IsActive);
    }

    public async Task<IEnumerable<Session>> GetExpiredSessionsAsync(
        DateTime cutoffTime, int maxResults = 1000, CancellationToken cancellationToken = default)
    {
        return await _context.Sessions
            .Where(s => s.ExpiresAt < cutoffTime)
            .OrderBy(s => s.ExpiresAt)  // Process oldest first
            .Take(maxResults)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}

// ===================== MESSAGE REPOSITORY =====================

public interface IMessageRepository : IRepository<Message>
{
    Task<IEnumerable<Message>> GetConversationAsync(ulong userId1, ulong userId2, int limit = 50);
    Task<IEnumerable<Message>> GetConversationBeforeAsync(ulong userId1, ulong userId2, ulong? beforeMessageId, int limit = 50);
    Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId);
    Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId);
    Task<IDictionary<ulong, int>> GetUnreadCountsBySenderAsync(ulong userId);
    Task MarkMessagesDeliveredAsync(IEnumerable<ulong> messageIds, ulong recipientUserId);
    Task MarkMessagesReadAsync(IEnumerable<ulong> messageIds, ulong readerUserId);
    Task<Message?> GetMessageForEditAsync(ulong messageId, ulong userId);
}

public class MessageRepository : Repository<Message>, IMessageRepository
{
    private readonly AegisDbContext _context;

    public MessageRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Message>> GetConversationAsync(ulong userId1, ulong userId2, int limit = 50)
    {
        return await _context.Messages
            .AsNoTracking()
            .Where(m => !m.IsDeleted &&
                ((m.FromUserId == userId1 && m.ToUserId == userId2) ||
                 (m.FromUserId == userId2 && m.ToUserId == userId1)))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetConversationBeforeAsync(ulong userId1, ulong userId2, ulong? beforeMessageId, int limit = 50)
    {
        var query = _context.Messages
            .AsNoTracking()
            .Where(m => !m.IsDeleted &&
                ((m.FromUserId == userId1 && m.ToUserId == userId2) ||
                 (m.FromUserId == userId2 && m.ToUserId == userId1)));

        if (beforeMessageId.HasValue)
        {
            query = query.Where(m => m.Id < beforeMessageId.Value);
        }

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId)
    {
        return await _context.Messages
            .AsNoTracking()
            .Where(m => m.ToUserId == userId && !m.IsDelivered && !m.IsDeleted)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId)
    {
        return await _context.Messages
            .AsNoTracking()
            .Where(m => m.ToUserId == userId && !m.IsRead && !m.IsDeleted)
            .ToListAsync();
    }

    public async Task<IDictionary<ulong, int>> GetUnreadCountsBySenderAsync(ulong userId)
    {
        return await _context.Messages
            .Where(m => m.ToUserId == userId && !m.IsRead && !m.IsDeleted)
            .GroupBy(m => m.FromUserId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SenderId, x => x.Count);
    }

    public async Task MarkMessagesDeliveredAsync(IEnumerable<ulong> messageIds, ulong recipientUserId)
    {
        var ids = messageIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var messages = await _context.Messages
            .Where(m => ids.Contains(m.Id) && m.ToUserId == recipientUserId && !m.IsDelivered && !m.IsDeleted)
            .ToListAsync();

        foreach (var message in messages)
        {
            message.IsDelivered = true;
            message.DeliveredAt = now;
        }

        await _context.SaveChangesAsync();
    }

    public async Task MarkMessagesReadAsync(IEnumerable<ulong> messageIds, ulong readerUserId)
    {
        var ids = messageIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var messages = await _context.Messages
            .Where(m => ids.Contains(m.Id) && m.ToUserId == readerUserId && !m.IsRead && !m.IsDeleted)
            .ToListAsync();

        foreach (var message in messages)
        {
            message.IsRead = true;
            message.ReadAt = now;
            if (!message.IsDelivered)
            {
                message.IsDelivered = true;
                message.DeliveredAt = now;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<Message?> GetMessageForEditAsync(ulong messageId, ulong userId)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.FromUserId == userId && !m.IsDeleted);
    }
}

// ===================== CHANNEL REPOSITORY =====================

public interface IChannelRepository : IRepository<Channel>
{
    Task<IEnumerable<Channel>> GetUserChannelsAsync(ulong userId);
    Task<Channel?> GetChannelWithMembersAsync(ulong channelId);
    Task<bool> IsUserMemberAsync(ulong channelId, ulong userId);
    Task<ChannelMember?> GetChannelMemberAsync(ulong channelId, ulong userId);
    Task<IEnumerable<ChannelMessage>> GetChannelMessagesAsync(ulong channelId, int limit = 50);
    Task<IEnumerable<ChannelMessage>> GetChannelMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit = 50);
    Task<ChannelMessage?> GetLatestChannelMessageAsync(ulong channelId);
    Task<int> GetUnreadCountAsync(ulong channelId, DateTime? lastReadAtUtc);
    Task<ChannelMember> AddMemberAsync(ChannelMember member);
    Task<ChannelMember> UpdateMemberAsync(ChannelMember member);
    Task<ChannelMessage> AddChannelMessageAsync(ChannelMessage message);
    Task<ChannelMessage?> GetChannelMessageAsync(ulong messageId);
    Task<ChannelMessage> UpdateChannelMessageAsync(ChannelMessage message);
    Task<IEnumerable<ChannelMember>> GetChannelMembersAsync(ulong channelId);
    Task<IEnumerable<ChannelChatSummary>> GetUserChannelChatSummariesAsync(ulong userId);
    Task<Channel?> GetByInviteCodeAsync(string inviteCode);
    Task<Channel?> GetByPublicAliasAsync(string publicAlias);
    Task<bool> IsPublicAliasTakenAsync(string publicAlias, ulong? exceptChannelId = null);
}

public sealed record ChannelChatSummary(
    ulong ChannelId,
    string Name,
    string? AvatarUrl,
    ChannelType Type,
    string? LastMessage,
    DateTime? LastMessageAt,
    int UnreadCount
);

// SERVER-002: summary record for groups in chat list
public sealed record GroupChatSummary(
    ulong GroupId,
    string Name,
    string? AvatarUrl,
    JoinRule JoinRule,
    HistoryVisibility HistoryVisibility,
    string? LastMessage,
    DateTime? LastMessageAt
);

public class ChannelRepository : Repository<Channel>, IChannelRepository
{
    private readonly AegisDbContext _context;

    public ChannelRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Channel>> GetUserChannelsAsync(ulong userId)
    {
        return await _context.ChannelMembers
            .AsNoTracking()
            .Where(cm => cm.UserId == userId && cm.IsActive)
            .Select(cm => cm.Channel!)
            .ToListAsync();
    }

    public async Task<Channel?> GetChannelWithMembersAsync(ulong channelId)
    {
        return await _context.Channels
            .AsNoTracking()
            .Include(c => c.Members)
            .ThenInclude(cm => cm.User)
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == channelId && c.IsActive);
    }

    public async Task<bool> IsUserMemberAsync(ulong channelId, ulong userId)
    {
        return await _context.ChannelMembers
            .AnyAsync(cm => cm.ChannelId == channelId && cm.UserId == userId && cm.IsActive);
    }

    public async Task<ChannelMember?> GetChannelMemberAsync(ulong channelId, ulong userId)
    {
        return await _context.ChannelMembers
            .AsNoTracking()
            .Include(cm => cm.User)
            .FirstOrDefaultAsync(cm => cm.ChannelId == channelId && cm.UserId == userId && cm.IsActive);
    }

    public async Task<IEnumerable<ChannelMessage>> GetChannelMessagesAsync(ulong channelId, int limit = 50)
    {
        return await _context.ChannelMessages
            .AsNoTracking()
            .Include(cm => cm.FromUser)
            .Include(cm => cm.ReplyToMessage)
            .Where(cm => cm.ChannelId == channelId && !cm.IsDeleted)
            .OrderByDescending(cm => cm.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<ChannelMessage>> GetChannelMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit = 50)
    {
        var query = _context.ChannelMessages
            .AsNoTracking()
            .Include(cm => cm.FromUser)
            .Include(cm => cm.ReplyToMessage)
            .Where(cm => cm.ChannelId == channelId && !cm.IsDeleted);

        if (beforeMessageId.HasValue)
        {
            query = query.Where(cm => cm.Id < beforeMessageId.Value);
        }

        return await query
            .OrderByDescending(cm => cm.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<ChannelMessage?> GetLatestChannelMessageAsync(ulong channelId)
    {
        return await _context.ChannelMessages
            .AsNoTracking()
            .Include(cm => cm.FromUser)
            .Where(cm => cm.ChannelId == channelId && !cm.IsDeleted)
            .OrderByDescending(cm => cm.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<int> GetUnreadCountAsync(ulong channelId, DateTime? lastReadAtUtc)
    {
        var query = _context.ChannelMessages
            .Where(cm => cm.ChannelId == channelId && !cm.IsDeleted);

        if (lastReadAtUtc.HasValue)
        {
            query = query.Where(cm => cm.CreatedAt > lastReadAtUtc.Value);
        }

        return await query.CountAsync();
    }

    public async Task<ChannelMember> AddMemberAsync(ChannelMember member)
    {
        _context.ChannelMembers.Add(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task<ChannelMember> UpdateMemberAsync(ChannelMember member)
    {
        _context.ChannelMembers.Update(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task<ChannelMessage> AddChannelMessageAsync(ChannelMessage message)
    {
        _context.ChannelMessages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<ChannelMessage?> GetChannelMessageAsync(ulong messageId)
    {
        return await _context.ChannelMessages
            .AsNoTracking()
            .Include(cm => cm.FromUser)
            .FirstOrDefaultAsync(cm => cm.Id == messageId && !cm.IsDeleted);
    }

    public async Task<ChannelMessage> UpdateChannelMessageAsync(ChannelMessage message)
    {
        _context.ChannelMessages.Update(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<IEnumerable<ChannelMember>> GetChannelMembersAsync(ulong channelId)
    {
        return await _context.ChannelMembers
            .AsNoTracking()
            .Include(cm => cm.User)
            .Where(cm => cm.ChannelId == channelId && cm.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<ChannelChatSummary>> GetUserChannelChatSummariesAsync(ulong userId)
    {
        return await _context.ChannelMembers
            .AsNoTracking()
            .Where(cm => cm.UserId == userId && cm.IsActive)
            .Select(cm => new ChannelChatSummary(
                cm.ChannelId,
                cm.Channel!.Name,
                cm.Channel.AvatarUrl,
                cm.Channel.Type,
                _context.ChannelMessages
                    .Where(m => m.ChannelId == cm.ChannelId && !m.IsDeleted)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => m.Content)
                    .FirstOrDefault(),
                _context.ChannelMessages
                    .Where(m => m.ChannelId == cm.ChannelId && !m.IsDeleted)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefault(),
                _context.ChannelMessages
                    .Where(m => m.ChannelId == cm.ChannelId && !m.IsDeleted &&
                        (!cm.LastReadAt.HasValue || m.CreatedAt > cm.LastReadAt.Value))
                    .Count()
            ))
            .ToListAsync();
    }

    public async Task<Channel?> GetByInviteCodeAsync(string inviteCode)
    {
        var normalized = (inviteCode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return await _context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.InviteCode == normalized);
    }

    public async Task<Channel?> GetByPublicAliasAsync(string publicAlias)
    {
        var normalized = (publicAlias ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return await _context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.PublicAlias == normalized);
    }

    public async Task<bool> IsPublicAliasTakenAsync(string publicAlias, ulong? exceptChannelId = null)
    {
        var normalized = (publicAlias ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return await _context.Channels
            .AnyAsync(c => c.PublicAlias == normalized && (!exceptChannelId.HasValue || c.Id != exceptChannelId.Value));
    }
}

// ===================== GROUP REPOSITORY =====================

public interface IGroupRepository : IRepository<Group>
{
    Task<IEnumerable<Group>> GetUserGroupsAsync(ulong userId);
    Task<Group?> GetGroupWithMembersAsync(ulong groupId);
    Task<bool> IsUserMemberAsync(ulong groupId, ulong userId);
    Task<GroupMember?> GetGroupMemberAsync(ulong groupId, ulong userId);
    Task<GroupMember> AddMemberAsync(GroupMember member);
    Task<GroupMember> UpdateMemberAsync(GroupMember member);
    Task<IEnumerable<GroupMember>> GetGroupMembersAsync(ulong groupId);
    Task<GroupMessage> AddGroupMessageAsync(GroupMessage message);
    Task<GroupMessage?> GetGroupMessageAsync(ulong messageId);
    Task<GroupMessage> UpdateGroupMessageAsync(GroupMessage message);
    Task<IEnumerable<GroupMessage>> GetGroupMessagesAsync(ulong groupId, int limit = 50);
    // SERVER-002: paged history
    Task<IEnumerable<GroupMessage>> GetGroupMessagesBeforeAsync(ulong groupId, ulong? beforeMessageId, int limit = 50);
    // SERVER-002: chat list summaries for groups
    Task<IEnumerable<GroupChatSummary>> GetUserGroupChatSummariesAsync(ulong userId);
}

public class GroupRepository : Repository<Group>, IGroupRepository
{
    private readonly AegisDbContext _context;

    public GroupRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Group>> GetUserGroupsAsync(ulong userId)
    {
        return await _context.GroupMembers
            .Where(gm => gm.UserId == userId && gm.IsActive)
            .Select(gm => gm.Group!)
            .ToListAsync();
    }

    public async Task<Group?> GetGroupWithMembersAsync(ulong groupId)
    {
        return await _context.Groups
            .Include(g => g.Members).ThenInclude(gm => gm.User)
            .Include(g => g.CreatedByUser)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive);
    }

    public async Task<bool> IsUserMemberAsync(ulong groupId, ulong userId)
    {
        return await _context.GroupMembers
            .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId && gm.IsActive);
    }

    public async Task<GroupMember?> GetGroupMemberAsync(ulong groupId, ulong userId)
    {
        return await _context.GroupMembers
            .Include(gm => gm.User)
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId && gm.IsActive);
    }

    public async Task<GroupMember> AddMemberAsync(GroupMember member)
    {
        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task<GroupMember> UpdateMemberAsync(GroupMember member)
    {
        _context.GroupMembers.Update(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task<IEnumerable<GroupMember>> GetGroupMembersAsync(ulong groupId)
    {
        return await _context.GroupMembers
            .Include(gm => gm.User)
            .Where(gm => gm.GroupId == groupId && gm.IsActive)
            .ToListAsync();
    }

    public async Task<GroupMessage> AddGroupMessageAsync(GroupMessage message)
    {
        _context.GroupMessages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<GroupMessage?> GetGroupMessageAsync(ulong messageId)
    {
        return await _context.GroupMessages
            .Include(gm => gm.FromUser)
            .FirstOrDefaultAsync(gm => gm.Id == messageId && !gm.IsDeleted);
    }

    public async Task<GroupMessage> UpdateGroupMessageAsync(GroupMessage message)
    {
        _context.GroupMessages.Update(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<IEnumerable<GroupMessage>> GetGroupMessagesAsync(ulong groupId, int limit = 50)
    {
        return await _context.GroupMessages
            .Include(gm => gm.FromUser)
            .Where(gm => gm.GroupId == groupId && !gm.IsDeleted)
            .OrderByDescending(gm => gm.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<GroupMessage>> GetGroupMessagesBeforeAsync(ulong groupId, ulong? beforeMessageId, int limit = 50)
    {
        var query = _context.GroupMessages
            .Include(gm => gm.FromUser)
            .Where(gm => gm.GroupId == groupId && !gm.IsDeleted);

        if (beforeMessageId.HasValue)
        {
            var pivot = await _context.GroupMessages
                .Where(gm => gm.Id == beforeMessageId.Value)
                .Select(gm => gm.CreatedAt)
                .FirstOrDefaultAsync();
            query = query.Where(gm => gm.CreatedAt < pivot);
        }

        return await query
            .OrderByDescending(gm => gm.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<GroupChatSummary>> GetUserGroupChatSummariesAsync(ulong userId)
    {
        return await _context.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == userId && gm.IsActive)
            .Join(_context.Groups,
                gm => gm.GroupId,
                g => g.Id,
                (gm, g) => new { Group = g })
            .Where(x => x.Group.IsActive)
            .Select(x => new GroupChatSummary(
                x.Group.Id,
                x.Group.Name,
                x.Group.AvatarUrl,
                x.Group.JoinRule,
                x.Group.HistoryVisibility,
                _context.GroupMessages
                    .Where(gm => gm.GroupId == x.Group.Id && !gm.IsDeleted)
                    .OrderByDescending(gm => gm.CreatedAt)
                    .Select(gm => gm.Content)
                    .FirstOrDefault(),
                _context.GroupMessages
                    .Where(gm => gm.GroupId == x.Group.Id && !gm.IsDeleted)
                    .OrderByDescending(gm => gm.CreatedAt)
                    .Select(gm => (DateTime?)gm.CreatedAt)
                    .FirstOrDefault()))
            .ToListAsync();
    }
}

// ===================== PRIVATE CHAT REPOSITORY =====================

public interface IPrivateChatRepository : IRepository<PrivateChat>
{
    Task<PrivateChat?> GetPrivateChatAsync(ulong userId1, ulong userId2);
    Task<IEnumerable<PrivateChat>> GetUserPrivateChatsAsync(ulong userId);
    Task<PrivateChat> CreatePrivateChatAsync(ulong userId1, ulong userId2);
}

public class PrivateChatRepository : Repository<PrivateChat>, IPrivateChatRepository
{
    private readonly AegisDbContext _context;

    public PrivateChatRepository(AegisDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<PrivateChat?> GetPrivateChatAsync(ulong userId1, ulong userId2)
    {
        return await _context.PrivateChats
            .Include(pc => pc.User1)
            .Include(pc => pc.User2)
            .Include(pc => pc.LastMessage)
            .FirstOrDefaultAsync(pc =>
                (pc.User1Id == userId1 && pc.User2Id == userId2) ||
                (pc.User1Id == userId2 && pc.User2Id == userId1));
    }

    public async Task<IEnumerable<PrivateChat>> GetUserPrivateChatsAsync(ulong userId)
    {
        return await _context.PrivateChats
            .AsNoTracking()
            .Include(pc => pc.User1)
            .Include(pc => pc.User2)
            .Include(pc => pc.LastMessage)
            .Where(pc => (pc.User1Id == userId || pc.User2Id == userId) && pc.IsActive)
            .OrderByDescending(pc => pc.LastActivityAt)
            .ToListAsync();
    }

    public async Task<PrivateChat> CreatePrivateChatAsync(ulong userId1, ulong userId2)
    {
        var privateChat = new PrivateChat
        {
            User1Id = Math.Min(userId1, userId2),
            User2Id = Math.Max(userId1, userId2),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        return await CreateAsync(privateChat);
    }
}

// ===================== APP CREDENTIAL REPOSITORY =====================

public interface IAppCredentialRepository
{
    Task<AppCredential?> GetByAppIdAsync(int appId);
    Task<AppCredential?> ValidateAsync(int appId, string appHash);
    Task<IEnumerable<AppCredential>> GetByOwnerAsync(ulong ownerId);
    Task<AppCredential> CreateAsync(AppCredential entity);
    Task<AppCredential> UpdateAsync(AppCredential entity);
    Task<bool> RevokeAsync(int appId, ulong ownerId);
}

public class AppCredentialRepository : IAppCredentialRepository
{
    private readonly AegisDbContext _context;

    public AppCredentialRepository(AegisDbContext context)
    {
        _context = context;
    }

    public async Task<AppCredential?> GetByAppIdAsync(int appId)
    {
        return await _context.AppCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppId == appId && a.IsActive);
    }

    public async Task<AppCredential?> ValidateAsync(int appId, string appHash)
    {
        if (string.IsNullOrWhiteSpace(appHash))
            return null;

        var credential = await _context.AppCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppId == appId && a.IsActive);

        if (credential == null)
            return null;

        // Constant-time compare to prevent timing oracle
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(credential.AppHash),
                System.Text.Encoding.UTF8.GetBytes(appHash)))
        {
            return null;
        }

        // Update last-used timestamp (fire-and-forget; don't block handshake)
        await _context.AppCredentials
            .Where(a => a.AppId == appId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastUsedAt, DateTime.UtcNow));

        return credential;
    }

    public async Task<IEnumerable<AppCredential>> GetByOwnerAsync(ulong ownerId)
    {
        return await _context.AppCredentials
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<AppCredential> CreateAsync(AppCredential entity)
    {
        _context.AppCredentials.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<AppCredential> UpdateAsync(AppCredential entity)
    {
        _context.AppCredentials.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> RevokeAsync(int appId, ulong ownerId)
    {
        var rows = await _context.AppCredentials
            .Where(a => a.AppId == appId && a.OwnerId == ownerId && a.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsActive, false)
                .SetProperty(a => a.RevokedAt, DateTime.UtcNow));
        return rows > 0;
    }
}

// ===================== REACTION REPOSITORY (SERVER-005) =====================

public interface IReactionRepository
{
    /// <summary>Returns all reactions on a message, grouped by emoji.</summary>
    Task<IEnumerable<MessageReaction>> GetByMessageAsync(string scope, ulong messageId);
    /// <summary>Adds a reaction. Returns null if the user already reacted with the same emoji.</summary>
    Task<MessageReaction?> AddAsync(string scope, ulong messageId, ulong userId, string emoji);
    /// <summary>Removes a reaction. Returns true if it existed.</summary>
    Task<bool> RemoveAsync(string scope, ulong messageId, ulong userId, string emoji);
}

public class ReactionRepository : IReactionRepository
{
    private readonly AegisDbContext _context;

    public ReactionRepository(AegisDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<MessageReaction>> GetByMessageAsync(string scope, ulong messageId)
    {
        return await _context.MessageReactions
            .AsNoTracking()
            .Where(r => r.Scope == scope && r.MessageId == messageId)
            .ToListAsync();
    }

    public async Task<MessageReaction?> AddAsync(string scope, ulong messageId, ulong userId, string emoji)
    {
        var existing = await _context.MessageReactions
            .FirstOrDefaultAsync(r => r.Scope == scope && r.MessageId == messageId
                                      && r.UserId == userId && r.Emoji == emoji);
        if (existing != null)
            return null; // already reacted

        var reaction = new MessageReaction
        {
            Scope = scope,
            MessageId = messageId,
            UserId = userId,
            Emoji = emoji,
            CreatedAt = DateTime.UtcNow
        };
        _context.MessageReactions.Add(reaction);
        await _context.SaveChangesAsync();
        return reaction;
    }

    public async Task<bool> RemoveAsync(string scope, ulong messageId, ulong userId, string emoji)
    {
        var rows = await _context.MessageReactions
            .Where(r => r.Scope == scope && r.MessageId == messageId
                        && r.UserId == userId && r.Emoji == emoji)
            .ExecuteDeleteAsync();
        return rows > 0;
    }
}
