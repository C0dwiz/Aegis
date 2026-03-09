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
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> SearchByUsernameAsync(string pattern)
    {
        return await _context.Users
            .Where(u => u.Username.Contains(pattern) && u.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> SearchByEmailAsync(string pattern)
    {
        return await _context.Users
            .Where(u => u.Email.Contains(pattern) && u.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> SearchAsync(string query, int limit = 20)
    {
        return await _context.Users
            .Where(u => (u.Username.Contains(query) || u.Email.Contains(query)) && u.IsActive)
            .Take(limit)
            .ToListAsync();
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
}

// ===================== MESSAGE REPOSITORY =====================

public interface IMessageRepository : IRepository<Message>
{
    Task<IEnumerable<Message>> GetConversationAsync(ulong userId1, ulong userId2, int limit = 50);
    Task<IEnumerable<Message>> GetConversationBeforeAsync(ulong userId1, ulong userId2, ulong? beforeMessageId, int limit = 50);
    Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId);
    Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId);
    Task<IDictionary<ulong, int>> GetUnreadCountsBySenderAsync(ulong userId);
    Task MarkMessagesDeliveredAsync(IEnumerable<ulong> messageIds);
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
            .Where(m => m.ToUserId == userId && !m.IsDelivered && !m.IsDeleted)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId)
    {
        return await _context.Messages
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

    public async Task MarkMessagesDeliveredAsync(IEnumerable<ulong> messageIds)
    {
        var ids = messageIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var messages = await _context.Messages
            .Where(m => ids.Contains(m.Id) && !m.IsDelivered)
            .ToListAsync();

        foreach (var message in messages)
        {
            message.IsDelivered = true;
            message.DeliveredAt = now;
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
}

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
            .Where(cm => cm.UserId == userId && cm.IsActive)
            .Select(cm => cm.Channel!)
            .ToListAsync();
    }

    public async Task<Channel?> GetChannelWithMembersAsync(ulong channelId)
    {
        return await _context.Channels
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
            .Include(cm => cm.User)
            .FirstOrDefaultAsync(cm => cm.ChannelId == channelId && cm.UserId == userId && cm.IsActive);
    }

    public async Task<IEnumerable<ChannelMessage>> GetChannelMessagesAsync(ulong channelId, int limit = 50)
    {
        return await _context.ChannelMessages
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
            .Include(cm => cm.User)
            .Where(cm => cm.ChannelId == channelId && cm.IsActive)
            .ToListAsync();
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
