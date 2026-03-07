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
    "MessageContentType", "ChannelType",
    "AegisException", "ConnectionException", 
    "NotConnectedException", "TimeoutException", "ProtocolError",
    "ProtocolConstants"
]
