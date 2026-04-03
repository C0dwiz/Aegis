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

    // Profile fields
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public DateOnly? BirthDate { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    // Auth hardening fields
    public bool IsEmailVerified { get; set; } = true;
    public DateTime? EmailVerifiedAt { get; set; }
    public bool TwoFactorEnabled { get; set; } = false;
    public string? TotpSecret { get; set; }
    public string? RecoveryPhraseHash { get; set; }

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
    public ICollection<Bot> OwnedBots { get; set; } = new List<Bot>();
    public ICollection<Bot> BotAccounts { get; set; } = new List<Bot>();
    public ICollection<UserAvatar> Avatars { get; set; } = new List<UserAvatar>();
}

public class UserAvatar
{
    public ulong Id { get; set; }
    public ulong UserId { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

public class Bot
{
    public ulong Id { get; set; }
    public ulong OwnerUserId { get; set; }
    public ulong UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? OwnerUser { get; set; }
    public User? User { get; set; }
    public ICollection<BotToken> Tokens { get; set; } = new List<BotToken>();
}

public class BotToken
{
    public ulong Id { get; set; }
    public ulong BotId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public Bot? Bot { get; set; }
}

public class BotConversationState
{
    public ulong UserId { get; set; }
    public BotConversationStep Step { get; set; } = BotConversationStep.Idle;
    public string? DraftDisplayName { get; set; }
    public string? DraftUsername { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

public enum BotConversationStep
{
    Idle = 0,
    AwaitingDisplayName = 1,
    AwaitingUsername = 2,
    AwaitingTokenUsername = 3,
    AwaitingRevokeUsername = 4
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
/// Replay window persistence for handshake nonces.
/// Allows cross-instance deduplication and forensic tracing.
/// </summary>
public class HandshakeReplayEntry
{
    public ulong Id { get; set; }
    public int AppId { get; set; }
    public string NonceHash { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? SourceIp { get; set; }
}

/// <summary>
/// Tracks active and previous salts for a session to support safe rotation.
/// </summary>
public class SessionSaltState
{
    public ulong Id { get; set; }
    public ulong SessionId { get; set; }
    public long CurrentSalt { get; set; }
    public long? PreviousSalt { get; set; }
    public DateTime RotatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PreviousSaltValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Persisted Double Ratchet chain state per user-peer direction.
/// </summary>
public class SignalChainState
{
    public ulong Id { get; set; }
    public ulong OwnerUserId { get; set; }
    public ulong PeerUserId { get; set; }
    public string RootKeyBase64 { get; set; } = string.Empty;
    public string SendingChainKeyBase64 { get; set; } = string.Empty;
    public string ReceivingChainKeyBase64 { get; set; } = string.Empty;
    public uint NextSendingMessageNumber { get; set; }
    public uint NextReceivingMessageNumber { get; set; }
    public string? LastMessageKeyHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Message delivery status tracking entity
/// </summary>
public class MessageDelivery
{
    public ulong Id { get; set; }
    public ulong MessageId { get; set; }
    public ulong UserId { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Sent;
    public DateTime StatusUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DeviceId { get; set; }

    // Navigation properties
    // NOTE: Message navigation removed - messages stored in ZoneTree, MessageId is data only
    public User User { get; set; } = null!;
}

/// <summary>
/// Delivery status enumeration
/// </summary>
public enum DeliveryStatus
{
    Sent = 0,        // Message sent to server
    Delivered = 1,   // Message delivered to user device
    Read = 2,         // Message read by user
    Failed = 3        // Delivery failed
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

    // Delivery and read status
    public bool IsDelivered { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    // Message editing/deletion
    public bool IsEdited { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Additional metadata
    public string? MediaId { get; set; }
    public ulong? ReplyToMessageId { get; set; }
    public bool IsPinned { get; set; }
    public string? EditHistory { get; set; } // JSON array of previous content

    // Navigation properties
    public User? FromUser { get; set; }
    public User? ToUser { get; set; }
    public Message? ReplyToMessage { get; set; }
    // NOTE: Removed DeliveryStatus navigation - delivery tracking is separate in PostgreSQL
    // Messages are stored in ZoneTree; MessageDelivery tracks delivery status independently
    public ICollection<Message> Replies { get; set; } = new List<Message>();
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
    public string? AvatarUrl { get; set; }
    public ulong CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int MemberCount { get; set; } = 0;

    // SERVER-006: visibility / join-rule settings
    public JoinRule JoinRule { get; set; } = JoinRule.InviteOnly;
    public HistoryVisibility HistoryVisibility { get; set; } = HistoryVisibility.Joined;

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

    // Granular permissions
    public bool CanSendMessages { get; set; } = true;
    public bool CanDeleteOthersMessages { get; set; }
    public bool CanEditGroupInfo { get; set; }
    public bool CanInviteUsers { get; set; }
    public bool CanRemoveUsers { get; set; }
    public bool CanPinMessages { get; set; }
    public bool CanManageRoles { get; set; }

    // Navigation properties
    public Group? Group { get; set; }
    public User? User { get; set; }
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
    public DateTime? EditedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ulong? ReplyToMessageId { get; set; }
    public bool IsPinned { get; set; }

    // Delivery tracking fields
    public bool IsDelivered { get; set; } = false;
    public DateTime? DeliveredAt { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    // Navigation properties
    public Group? Group { get; set; }
    public User? FromUser { get; set; }
    public GroupMessage? ReplyToMessage { get; set; }
    public ICollection<GroupMessage> Replies { get; set; } = new List<GroupMessage>();
    public ICollection<MessageDelivery> DeliveryStatus { get; set; } = new List<MessageDelivery>();
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
    public string? AvatarUrl { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Public;
    public ulong CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? InviteCode { get; set; }
    public string? PublicAlias { get; set; }
    public int MemberCount { get; set; } = 0;

    // SERVER-006: visibility / join-rule settings
    public JoinRule JoinRule { get; set; } = JoinRule.Open;
    public HistoryVisibility HistoryVisibility { get; set; } = HistoryVisibility.Joined;

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

    // Granular permissions
    public bool CanSendMessages { get; set; } = true;
    public bool CanDeleteOthersMessages { get; set; }
    public bool CanEditChannelInfo { get; set; }
    public bool CanInviteUsers { get; set; }
    public bool CanRemoveUsers { get; set; }
    public bool CanPinMessages { get; set; }
    public bool CanManageRoles { get; set; }

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
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ulong? ReplyToMessageId { get; set; }
    public bool IsPinned { get; set; } = false;

    // Delivery tracking fields
    public bool IsDelivered { get; set; } = false;
    public DateTime? DeliveredAt { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    // Navigation properties
    public Channel? Channel { get; set; }
    public User? FromUser { get; set; }
    public ChannelMessage? ReplyToMessage { get; set; }
    public ICollection<ChannelMessage> Replies { get; set; } = new List<ChannelMessage>();
    public ICollection<MessageDelivery> DeliveryStatus { get; set; } = new List<MessageDelivery>();
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

/// <summary>
/// Third-party application credentials (similar to Telegram api_id/api_hash).
/// Developers register their client applications here and receive credentials
/// that must be passed in each protocol handshake.
/// </summary>
public class AppCredential
{
    /// <summary>Auto-incremented numeric application identifier.</summary>
    public int AppId { get; set; }

    /// <summary>Cryptographically random 32-byte secret (hex-encoded, 64 chars).</summary>
    public string AppHash { get; set; } = string.Empty;

    /// <summary>Human-readable application name shown in the developer portal.</summary>
    public string AppTitle { get; set; } = string.Empty;

    /// <summary>Short identifier used for logging/display (e.g. "myapp_android").</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Optional description of what the application does.</summary>
    public string? Description { get; set; }

    /// <summary>Optional website URL for the application.</summary>
    public string? Website { get; set; }

    /// <summary>Target platform: android, ios, web, desktop, other.</summary>
    public string Platform { get; set; } = "other";

    /// <summary>Owner user id (the developer who created this app).</summary>
    public ulong OwnerId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Navigation
    public User? Owner { get; set; }
}

// ===================== SERVER-005: REACTIONS =====================

/// <summary>
/// Emoji reaction on a message (private, channel, or group scope).
/// </summary>
public class MessageReaction
{
    public ulong Id { get; set; }

    /// <summary>"private", "channel", or "group"</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>ID of the reacted-to message within the given scope.</summary>
    public ulong MessageId { get; set; }

    public ulong UserId { get; set; }

    /// <summary>Unicode emoji string (1-4 chars, validated at handler layer).</summary>
    public string Emoji { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}

// ===================== SERVER-006: ROOM SETTINGS =====================

/// <summary>
/// Who may join a group or channel.
/// </summary>
public enum JoinRule
{
    /// <summary>Anyone can join (public channel default).</summary>
    Open = 0,
    /// <summary>Only members can invite others (group default).</summary>
    InviteOnly = 1,
    /// <summary>Joining requires owner/admin approval.</summary>
    Approval = 2
}

/// <summary>
/// Who can read the message history of a room.
/// </summary>
public enum HistoryVisibility
{
    /// <summary>History visible to anyone (unauthenticated too, for public channels).</summary>
    WorldReadable = 0,
    /// <summary>History visible to current members only.</summary>
    Joined = 1,
    /// <summary>New members cannot see messages sent before they joined.</summary>
    Invited = 2
}
