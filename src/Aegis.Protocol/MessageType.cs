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
    RegisterResponse = 21
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

