"""JSON payload DTOs used by the Aegis Python client."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Dict, List, Optional


class MessageContentType:
    TEXT = 0
    IMAGE = 1
    VIDEO = 2
    AUDIO = 3
    FILE = 4
    LOCATION = 5


class ChannelType:
    PUBLIC = 0
    PRIVATE = 1
    GROUP = 2


@dataclass
class RegistrationRequest:
    username: str
    email: str
    password: str
    public_key: str

    def to_bytes(self) -> bytes:
        return json.dumps(
            {
                "Username": self.username,
                "Email": self.email,
                "Password": self.password,
                "PublicKey": self.public_key,
            }
        ).encode("utf-8")


@dataclass
class RegisteredUserInfo:
    id: int
    username: str

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "RegisteredUserInfo":
        return cls(id=data["Id"], username=data["Username"])


@dataclass
class RegistrationResponse:
    success: bool
    message: Optional[str] = None
    user: Optional[RegisteredUserInfo] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "RegistrationResponse":
        json_data = json.loads(data.decode("utf-8"))
        user_data = json_data.get("User")
        return cls(
            success=json_data.get("Success", False),
            message=json_data.get("Message"),
            user=RegisteredUserInfo.from_dict(user_data) if user_data else None,
        )


@dataclass
class AuthResponse:
    success: bool
    user_id: int = 0
    username: str = ""
    session_token: str = ""
    error: str = ""

    @classmethod
    def from_bytes(cls, data: bytes) -> "AuthResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            user_id=json_data.get("UserId", 0),
            username=json_data.get("Username", ""),
            session_token=json_data.get("SessionToken", ""),
            error=json_data.get("Error", ""),
        )


@dataclass
class HandshakeResponse:
    success: bool
    server_public_key: Optional[str] = None
    message: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "HandshakeResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            server_public_key=json_data.get("ServerPublicKey"),
            message=json_data.get("Message"),
        )


@dataclass
class UserSearchRequest:
    query: str
    limit: int = 20

    def to_bytes(self) -> bytes:
        return json.dumps({"Query": self.query, "Limit": self.limit}).encode("utf-8")


@dataclass
class UserSearchResult:
    id: int
    username: str

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "UserSearchResult":
        return cls(id=data["Id"], username=data["Username"])


@dataclass
class UserSearchResponse:
    success: bool
    users: List[UserSearchResult]
    message: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "UserSearchResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            users=[UserSearchResult.from_dict(item) for item in json_data.get("Users", [])],
            message=json_data.get("Message"),
        )


@dataclass
class ChannelCreateRequest:
    name: str
    description: Optional[str] = None
    type: int = ChannelType.PUBLIC

    def to_bytes(self) -> bytes:
        data = {"Name": self.name, "Type": self.type}
        if self.description is not None:
            data["Description"] = self.description
        return json.dumps(data).encode("utf-8")


@dataclass
class ChannelCreateResponse:
    success: bool
    channel_id: int = 0
    message: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "ChannelCreateResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            channel_id=json_data.get("ChannelId", 0),
            message=json_data.get("Message"),
        )


@dataclass
class ChannelJoinRequest:
    channel_id: int

    def to_bytes(self) -> bytes:
        return json.dumps({"ChannelId": self.channel_id}).encode("utf-8")


@dataclass
class ChannelJoinResponse:
    success: bool
    message: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "ChannelJoinResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(success=json_data.get("Success", False), message=json_data.get("Message"))


@dataclass
class ChannelMessageRequest:
    channel_id: int
    content: str
    content_type: int = MessageContentType.TEXT
    reply_to_message_id: Optional[int] = None

    def to_bytes(self) -> bytes:
        data = {
            "ChannelId": self.channel_id,
            "Content": self.content,
            "ContentType": self.content_type,
        }
        if self.reply_to_message_id is not None:
            data["ReplyToMessageId"] = self.reply_to_message_id
        return json.dumps(data).encode("utf-8")


@dataclass
class ChannelMessageResponse:
    success: bool
    message_id: int = 0
    message_text: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "ChannelMessageResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            message_id=json_data.get("MessageId", 0),
            message_text=json_data.get("MessageText"),
        )


@dataclass
class PrivateChatMessageRequest:
    to_user_id: int
    content: str
    content_type: int = MessageContentType.TEXT

    def to_bytes(self) -> bytes:
        return json.dumps(
            {
                "ToUserId": self.to_user_id,
                "Content": self.content,
                "ContentType": self.content_type,
            }
        ).encode("utf-8")


@dataclass
class PrivateChatMessageResponse:
    success: bool
    message_id: int = 0
    message_text: Optional[str] = None

    @classmethod
    def from_bytes(cls, data: bytes) -> "PrivateChatMessageResponse":
        json_data = json.loads(data.decode("utf-8"))
        return cls(
            success=json_data.get("Success", False),
            message_id=json_data.get("MessageId", 0),
            message_text=json_data.get("MessageText"),
        )