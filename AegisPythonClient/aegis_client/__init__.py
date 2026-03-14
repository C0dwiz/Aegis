"""
Aegis Python Client
Python клиентская библиотека для протокола Aegis Messenger
"""

from .aegis_client import AegisClient
from .message import Message, MessageType
from .message_payloads import (
    AuthResponse,
    HandshakeResponse,
    RegistrationRequest, RegistrationResponse,
    UserSearchRequest, UserSearchResponse, UserSearchResult,
    ChannelCreateRequest, ChannelCreateResponse,
    ChannelMessageRequest, ChannelMessageResponse,
    PrivateChatMessageRequest, PrivateChatMessageResponse,
    ChatListRequest, ChatListResponse, ChatListItem,
    PrivateChatHistoryRequest, PrivateChatHistoryResponse, PrivateChatHistoryItem,
    ChannelHistoryRequest, ChannelHistoryResponse, ChannelHistoryItem,
    PrivateChatMessageEvent, ChannelMessageEvent,
    MediaAttachment, parse_media_attachments,
    MessageContentType, ChannelType
)
from .exceptions import (
    AegisException, ConnectionException, 
    NotConnectedException, TimeoutException, ProtocolError
)
from .protocol_constants import ProtocolConstants

__version__ = "1.0.0"
__all__ = [
    "AegisClient",
    "Message", "MessageType",
    "AuthResponse", "HandshakeResponse",
    "RegistrationRequest", "RegistrationResponse",
    "UserSearchRequest", "UserSearchResponse", "UserSearchResult",
    "ChannelCreateRequest", "ChannelCreateResponse",
    "ChannelMessageRequest", "ChannelMessageResponse",
    "PrivateChatMessageRequest", "PrivateChatMessageResponse",
    "ChatListRequest", "ChatListResponse", "ChatListItem",
    "PrivateChatHistoryRequest", "PrivateChatHistoryResponse", "PrivateChatHistoryItem",
    "ChannelHistoryRequest", "ChannelHistoryResponse", "ChannelHistoryItem",
    "PrivateChatMessageEvent", "ChannelMessageEvent",
    "MediaAttachment", "parse_media_attachments",
    "MessageContentType", "ChannelType",
    "AegisException", "ConnectionException", 
    "NotConnectedException", "TimeoutException", "ProtocolError",
    "ProtocolConstants"
]
