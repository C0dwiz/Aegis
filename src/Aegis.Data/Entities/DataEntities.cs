namespace Aegis.Data.Entities;

/// <summary>
/// User entity for database storage
/// </summary>
public class User
{
    public ulong Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? IdentityKeyFingerprint { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    
    // Navigation properties
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<Message> ReceivedMessages { get; set; } = new List<Message>();
    public ICollection<Group> CreatedGroups { get; set; } = new List<Group>();
    public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
    public ICollection<Channel> CreatedChannels { get; set; } = new List<Channel>();
    public ICollection<ChannelMember> ChannelMemberships { get; set; } = new List<ChannelMember>();
    public ICollection<PrivateChat> PrivateChats1 { get; set; } = new List<PrivateChat>();
    public ICollection<PrivateChat> PrivateChats2 { get; set; } = new List<PrivateChat>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}

/// <summary>
/// Session entity for tracking user sessions
/// </summary>
public class Session
{
    public ulong Id { get; set; }
    public ulong UserId { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string SessionKeyHash { get; set; } = string.Empty;
    public string ClientInfo { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public User? User { get; set; }
}

/// <summary>
/// Message entity for message storage
/// </summary>
public class Message
{
    public ulong Id { get; set; }
    public ulong FromUserId { get; set; }
    public ulong ToUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageContentType ContentType { get; set; } = MessageContentType.Text;
    public ulong SequenceNumber { get; set; }
    public bool IsDelivered { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    
    // Navigation properties
    public User? FromUser { get; set; }
    public User? ToUser { get; set; }
}

/// <summary>
/// Message content type enumeration
/// </summary>
public enum MessageContentType
{
    Text = 0,
    Image = 1,
    Video = 2,
    Audio = 3,
    File = 4,
    Location = 5
}

/// <summary>
/// Group chat entity
/// </summary>
public class Group
{
    public ulong Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ulong CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public User? CreatedByUser { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<GroupMessage> Messages { get; set; } = new List<GroupMessage>();
}

/// <summary>
/// Group member relationship entity
/// </summary>
public class GroupMember
{
    public ulong Id { get; set; }
    public ulong GroupId { get; set; }
    public ulong UserId { get; set; }
    public GroupMemberRole Role { get; set; } = GroupMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public Group? Group { get; set; }
}

/// <summary>
/// Group message entity
/// </summary>
public class GroupMessage
{
    public ulong Id { get; set; }
    public ulong GroupId { get; set; }
    public ulong FromUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageContentType ContentType { get; set; } = MessageContentType.Text;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public Group? Group { get; set; }
}

/// <summary>
/// Group member role enumeration
/// </summary>
public enum GroupMemberRole
{
    Member = 0,
    Moderator = 1,
    Admin = 2,
    Owner = 3
}

/// <summary>
/// Pre-key entity for X3DH key exchange
/// </summary>
public class PreKey
{
    public ulong Id { get; set; }
    public ulong UserId { get; set; }
    public uint KeyId { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public bool IsOneTime { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}

/// <summary>
/// Device entity for tracking user devices
/// </summary>
public class Device
{
    public ulong Id { get; set; }
    public ulong UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
}

/// <summary>
/// Channel entity for public/private channels
/// </summary>
public class Channel
{
    public ulong Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Public;
    public ulong CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? InviteCode { get; set; }
    public int MemberCount { get; set; } = 0;
    
    // Navigation properties
    public User? CreatedByUser { get; set; }
    public ICollection<ChannelMember> Members { get; set; } = new List<ChannelMember>();
    public ICollection<ChannelMessage> Messages { get; set; } = new List<ChannelMessage>();
}

/// <summary>
/// Channel member relationship entity
/// </summary>
public class ChannelMember
{
    public ulong Id { get; set; }
    public ulong ChannelId { get; set; }
    public ulong UserId { get; set; }
    public ChannelMemberRole Role { get; set; } = ChannelMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReadAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsMuted { get; set; } = false;
    
    // Navigation properties
    public Channel? Channel { get; set; }
    public User? User { get; set; }
}

/// <summary>
/// Channel message entity
/// </summary>
public class ChannelMessage
{
    public ulong Id { get; set; }
    public ulong ChannelId { get; set; }
    public ulong FromUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageContentType ContentType { get; set; } = MessageContentType.Text;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool IsEdited { get; set; } = false;
    public ulong? ReplyToMessageId { get; set; }
    public bool IsPinned { get; set; } = false;
    
    // Navigation properties
    public Channel? Channel { get; set; }
    public User? FromUser { get; set; }
    public ChannelMessage? ReplyToMessage { get; set; }
    public ICollection<ChannelMessage> Replies { get; set; } = new List<ChannelMessage>();
}

/// <summary>
/// Private chat entity
/// </summary>
public class PrivateChat
{
    public ulong Id { get; set; }
    public ulong User1Id { get; set; }
    public ulong User2Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActivityAt { get; set; }
    public ulong? LastMessageId { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public User? User1 { get; set; }
    public User? User2 { get; set; }
    public Message? LastMessage { get; set; }
}

/// <summary>
/// Channel type enumeration
/// </summary>
public enum ChannelType
{
    Public = 0,    // Публичный канал
    Private = 1,   // Приватный канал
    Group = 2      // Групповой чат
}

/// <summary>
/// Channel member role enumeration
/// </summary>
public enum ChannelMemberRole
{
    Member = 0,
    Moderator = 1,
    Admin = 2,
    Owner = 3
}
