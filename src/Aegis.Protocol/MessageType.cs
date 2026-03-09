namespace Aegis.Protocol;

public enum MessageType : ushort
{
    Unknown = 0,
    Auth = 1,
    Ping = 2,
    Message = 3,
    Ack = 4,
    Error = 5,
    Handshake = 6,
    Nack = 7,
    RetransmitRequest = 8,
    UserPresence = 9,
    GroupMessage = 10,
    GroupCreate = 11,
    GroupLeave = 12,
    ChannelMessage = 13,
    ChannelCreate = 14,
    ChannelJoin = 15,
    ChannelLeave = 16,
    PrivateChatMessage = 17,
    UserSearch = 18,
    UserSearchResult = 19,
    Register = 20,
    RegisterResponse = 21,
    
    // Profile management
    ProfileUpdate = 22,
    ProfileUpdateResponse = 23,
    ProfileGet = 24,
    ProfileGetResponse = 25,
    
    // Message edit/delete
    MessageEdit = 26,
    MessageEditResponse = 27,
    MessageDelete = 28,
    MessageDeleteResponse = 29,
    
    // Channel/Group editing
    ChannelEdit = 30,
    ChannelEditResponse = 31,
    GroupEdit = 32,
    GroupEditResponse = 33,
    
    // Admin/permissions management
    MemberRoleUpdate = 34,
    MemberRoleUpdateResponse = 35,
    MemberPermissionUpdate = 36,
    MemberPermissionUpdateResponse = 37,
    
    // Group messaging
    GroupMessageSend = 38,
    GroupMessageResponse = 39,

    // Group management
    GroupCreateResponse = 40,

    // Chat bootstrap APIs and server-side events
    ChatListRequest = 41,
    ChatListResponse = 42,
    PrivateChatHistoryRequest = 43,
    PrivateChatHistoryResponse = 44,
    ChannelHistoryRequest = 45,
    ChannelHistoryResponse = 46,
    PrivateChatMessageEvent = 47,
    ChannelMessageEvent = 48
}

/// <summary>
/// Message flags for protocol features
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    None = 0x00,
    RequiresAck = 0x01,           // Требует подтверждение доставки
    IsRetransmit = 0x02,          // Это повторная отправка
    Compressed = 0x04,            // Полезная нагрузка сжата
    Encrypted = 0x08,             // Полезная нагрузка зашифрована
    Priority = 0x10                // Высокий приоритет доставки
}

/// <summary>
/// Acknowledgment status codes
/// </summary>
public enum AckStatus : byte
{
    Ok = 0,                       // Сообщение успешно доставлено
    Error = 1,                    // Ошибка при обработке
    Retry = 2,                    // Требуется повтор
    NotImplemented = 3            // Тип сообщения не поддерживается
}

