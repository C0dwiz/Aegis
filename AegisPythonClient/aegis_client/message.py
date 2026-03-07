"""Message primitives for the Aegis wire protocol."""

from __future__ import annotations

import struct
from enum import IntEnum

from .exceptions import ProtocolError
from .protocol_constants import ProtocolConstants


HEADER_FORMAT = ">IBBBHQI"


class MessageType(IntEnum):
    """Типы сообщений протокола."""

    UNKNOWN = ProtocolConstants.TYPE_UNKNOWN
    AUTH = ProtocolConstants.TYPE_AUTH
    PING = ProtocolConstants.TYPE_PING
    MESSAGE = ProtocolConstants.TYPE_MESSAGE
    ACK = ProtocolConstants.TYPE_ACK
    ERROR = ProtocolConstants.TYPE_ERROR
    HANDSHAKE = ProtocolConstants.TYPE_HANDSHAKE
    NACK = ProtocolConstants.TYPE_NACK
    RETRANSMIT_REQUEST = ProtocolConstants.TYPE_RETRANSMIT_REQUEST
    USER_PRESENCE = ProtocolConstants.TYPE_USER_PRESENCE
    GROUP_MESSAGE = ProtocolConstants.TYPE_GROUP_MESSAGE
    GROUP_CREATE = ProtocolConstants.TYPE_GROUP_CREATE
    GROUP_LEAVE = ProtocolConstants.TYPE_GROUP_LEAVE
    CHANNEL_MESSAGE = ProtocolConstants.TYPE_CHANNEL_MESSAGE
    CHANNEL_CREATE = ProtocolConstants.TYPE_CHANNEL_CREATE
    CHANNEL_JOIN = ProtocolConstants.TYPE_CHANNEL_JOIN
    CHANNEL_LEAVE = ProtocolConstants.TYPE_CHANNEL_LEAVE
    PRIVATE_CHAT_MESSAGE = ProtocolConstants.TYPE_PRIVATE_CHAT_MESSAGE
    USER_SEARCH = ProtocolConstants.TYPE_USER_SEARCH
    USER_SEARCH_RESULT = ProtocolConstants.TYPE_USER_SEARCH_RESULT
    REGISTER = ProtocolConstants.TYPE_REGISTER
    REGISTER_RESPONSE = ProtocolConstants.TYPE_REGISTER_RESPONSE
    PROFILE_UPDATE = ProtocolConstants.TYPE_PROFILE_UPDATE
    PROFILE_UPDATE_RESPONSE = ProtocolConstants.TYPE_PROFILE_UPDATE_RESPONSE
    PROFILE_GET = ProtocolConstants.TYPE_PROFILE_GET
    PROFILE_GET_RESPONSE = ProtocolConstants.TYPE_PROFILE_GET_RESPONSE

    @classmethod
    def from_value(cls, value: int) -> "MessageType":
        try:
            return cls(value)
        except ValueError:
            return cls.UNKNOWN


class Message:
    """Класс сообщения протокола."""

    def __init__(self, message_type: MessageType = MessageType.UNKNOWN):
        self.magic: int = ProtocolConstants.MAGIC
        self.version_major: int = ProtocolConstants.VERSION_MAJOR
        self.version_minor: int = ProtocolConstants.VERSION_MINOR
        self.flags: int = ProtocolConstants.FLAG_NONE
        self.type: MessageType = message_type
        self.sequence_id: int = 0
        self.payload_length: int = 0
        self.payload: bytes = b""
        self.mac: bytes = bytes(ProtocolConstants.MAC_SIZE)

    @classmethod
    def with_type(cls, message_type: MessageType, payload: bytes = b"") -> "Message":
        message = cls(message_type)
        message.payload = payload
        message.payload_length = len(payload)
        return message

    @classmethod
    def from_bytes(cls, data: bytes) -> "Message":
        if len(data) < ProtocolConstants.HEADER_SIZE:
            raise ProtocolError("Message too short for header")

        header = struct.unpack(HEADER_FORMAT, data[: ProtocolConstants.HEADER_SIZE])
        magic, version_major, version_minor, flags, msg_type, sequence_id, payload_length = header

        if magic != ProtocolConstants.MAGIC:
            raise ProtocolError(f"Invalid magic number: {magic}")

        total_size = ProtocolConstants.HEADER_SIZE + payload_length + ProtocolConstants.MAC_SIZE
        if len(data) < total_size:
            raise ProtocolError("Message too short for complete data")

        message = cls(MessageType.from_value(msg_type))
        message.magic = magic
        message.version_major = version_major
        message.version_minor = version_minor
        message.flags = flags
        message.sequence_id = sequence_id
        message.payload_length = payload_length
        payload_end = ProtocolConstants.HEADER_SIZE + payload_length
        message.payload = data[ProtocolConstants.HEADER_SIZE:payload_end]
        message.mac = data[payload_end:payload_end + ProtocolConstants.MAC_SIZE]
        return message

    def to_bytes(self) -> bytes:
        self.payload_length = len(self.payload)

        if len(self.mac) != ProtocolConstants.MAC_SIZE:
            self.mac = bytes(ProtocolConstants.MAC_SIZE)

        header = struct.pack(
            HEADER_FORMAT,
            self.magic,
            self.version_major,
            self.version_minor,
            self.flags,
            self.type.value,
            self.sequence_id,
            self.payload_length,
        )
        return header + self.payload + self.mac

    @property
    def total_size(self) -> int:
        return ProtocolConstants.HEADER_SIZE + self.payload_length + ProtocolConstants.MAC_SIZE

    def __repr__(self) -> str:
        return (
            f"Message(type={self.type.name}, sequence_id={self.sequence_id}, "
            f"payload_length={self.payload_length}, flags=0x{self.flags:02x})"
        )


class MessageFlags:
    """Флаги сообщений."""

    NONE = ProtocolConstants.FLAG_NONE
    REQUIRES_ACK = ProtocolConstants.FLAG_REQUIRES_ACK
    IS_RETRANSMIT = ProtocolConstants.FLAG_IS_RETRANSMIT
    COMPRESSED = ProtocolConstants.FLAG_COMPRESSED
    ENCRYPTED = ProtocolConstants.FLAG_ENCRYPTED
    PRIORITY = ProtocolConstants.FLAG_PRIORITY


class AckStatus(IntEnum):
    """Статусы подтверждения."""

    OK = ProtocolConstants.ACK_OK
    ERROR = ProtocolConstants.ACK_ERROR
    RETRY = ProtocolConstants.ACK_RETRY
    NOT_IMPLEMENTED = ProtocolConstants.ACK_NOT_IMPLEMENTED
