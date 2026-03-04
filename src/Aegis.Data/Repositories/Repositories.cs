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

/// <summary>
/// User repository interface
/// </summary>
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> SearchByUsernameAsync(string pattern);
}

/// <summary>
/// User repository implementation
/// </summary>
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
            .Where(u => u.Username.Contains(pattern))
            .ToListAsync();
    }
}

/// <summary>
/// Session repository interface
/// </summary>
public interface ISessionRepository : IRepository<Session>
{
    Task<Session?> GetByTokenAsync(string token);
    Task<IEnumerable<Session>> GetUserActiveSessions(ulong userId);
    Task<bool> DeleteExpiredSessionsAsync();
}

/// <summary>
/// Session repository implementation
/// </summary>
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
}

/// <summary>
/// Message repository interface
/// </summary>
public interface IMessageRepository : IRepository<Message>
{
    Task<IEnumerable<Message>> GetConversationAsync(ulong userId1, ulong userId2, int limit = 50);
    Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId);
    Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId);
}

/// <summary>
/// Message repository implementation
/// </summary>
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
            .Where(m => (m.FromUserId == userId1 && m.ToUserId == userId2) ||
                        (m.FromUserId == userId2 && m.ToUserId == userId1))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetUndeliveredMessagesAsync(ulong userId)
    {
        return await _context.Messages
            .Where(m => m.ToUserId == userId && !m.IsDelivered)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetUnreadMessagesAsync(ulong userId)
    {
        return await _context.Messages
            .Where(m => m.ToUserId == userId && !m.IsRead)
            .ToListAsync();
    }
}

/// <summary>
/// Channel repository interface
/// </summary>
public interface IChannelRepository : IRepository<Channel>
{
    Task<IEnumerable<Channel>> GetUserChannelsAsync(ulong userId);
    Task<Channel?> GetChannelWithMembersAsync(ulong channelId);
    Task<bool> IsUserMemberAsync(ulong channelId, ulong userId);
    Task<ChannelMember?> GetChannelMemberAsync(ulong channelId, ulong userId);
    Task<IEnumerable<ChannelMessage>> GetChannelMessagesAsync(ulong channelId, int limit = 50);
}

/// <summary>
/// Channel repository implementation
/// </summary>
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
            .Where(cm => cm.ChannelId == channelId)
            .OrderByDescending(cm => cm.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}

/// <summary>
/// Private chat repository interface
/// </summary>
public interface IPrivateChatRepository : IRepository<PrivateChat>
{
    Task<PrivateChat?> GetPrivateChatAsync(ulong userId1, ulong userId2);
    Task<IEnumerable<PrivateChat>> GetUserPrivateChatsAsync(ulong userId);
    Task<PrivateChat> CreatePrivateChatAsync(ulong userId1, ulong userId2);
}

/// <summary>
/// Private chat repository implementation
/// </summary>
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
