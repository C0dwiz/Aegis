"""
Aegis Python Client
Python клиентская библиотека для протокола Aegis Messenger
"""

from .aegis_client import AegisClient
from .message import Message, MessageType
from .message_payloads import (
    RegistrationRequest, RegistrationResponse,
    UserSearchRequest, UserSearchResponse, UserSearchResult,
    ChannelCreateRequest, ChannelCreateResponse, Channel,
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
    "RegistrationRequest", "RegistrationResponse",
    "UserSearchRequest", "UserSearchResponse", "UserSearchResult",
    "ChannelCreateRequest", "ChannelCreateResponse", "Channel",
    "ChannelMessageRequest", "ChannelMessageResponse",
    "PrivateChatMessageRequest", "PrivateChatMessageResponse",
    "MessageContentType", "ChannelType",
    "AegisException", "ConnectionException", 
    "NotConnectedException", "TimeoutException", "ProtocolError",
    "ProtocolConstants"
]
