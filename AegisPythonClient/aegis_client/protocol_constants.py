"""
Константы протокола Aegis
"""


class ProtocolConstants:
    # Magic number для идентификации протокола
    MAGIC = 0xAE6C5D7
    
    # Версия протокола
    VERSION_MAJOR = 1
    VERSION_MINOR = 0
    
    # Размеры заголовков
    HEADER_SIZE = 4 + 1 + 1 + 1 + 2 + 8 + 4  # 20 байт
    MAC_SIZE = 32  # SHA256 HMAC
    MAX_MESSAGE_SIZE = 1024 * 1024  # 1MB
    MAX_PAYLOAD_SIZE = MAX_MESSAGE_SIZE - HEADER_SIZE - MAC_SIZE
    
    # Типы сообщений
    TYPE_UNKNOWN = 0
    TYPE_AUTH = 1
    TYPE_PING = 2
    TYPE_MESSAGE = 3
    TYPE_ACK = 4
    TYPE_ERROR = 5
    TYPE_HANDSHAKE = 6
    TYPE_NACK = 7
    TYPE_RETRANSMIT_REQUEST = 8
    TYPE_USER_PRESENCE = 9
    TYPE_GROUP_MESSAGE = 10
    TYPE_GROUP_CREATE = 11
    TYPE_GROUP_LEAVE = 12
    TYPE_CHANNEL_MESSAGE = 13
    TYPE_CHANNEL_CREATE = 14
    TYPE_CHANNEL_JOIN = 15
    TYPE_CHANNEL_LEAVE = 16
    TYPE_PRIVATE_CHAT_MESSAGE = 17
    TYPE_USER_SEARCH = 18
    TYPE_USER_SEARCH_RESULT = 19
    TYPE_REGISTER = 20
    TYPE_REGISTER_RESPONSE = 21
    
    # Флаги сообщений
    FLAG_NONE = 0x00
    FLAG_REQUIRES_ACK = 0x01
    FLAG_IS_RETRANSMIT = 0x02
    FLAG_COMPRESSED = 0x04
    FLAG_ENCRYPTED = 0x08
    FLAG_PRIORITY = 0x10
    
    # Коды подтверждения
    ACK_OK = 0
    ACK_ERROR = 1
    ACK_RETRY = 2
    ACK_NOT_IMPLEMENTED = 3
