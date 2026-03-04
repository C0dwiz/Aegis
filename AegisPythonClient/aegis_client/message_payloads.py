"""
Payload классы для сообщений Aegis
"""
import json
from typing import List, Optional, Dict, Any
from dataclasses import dataclass


class MessageContentType:
    """Типы контента сообщений"""
    TEXT = 0
    IMAGE = 1
    VIDEO = 2
    AUDIO = 3
    FILE = 4
    LOCATION = 5
    
    @classmethod
    def from_value(cls, value: int) -> str:
        """Получить имя типа по значению"""
        types = {
            0: 'text',
            1: 'image', 
            2: 'video',
            3: 'audio',
            4: 'file',
            5: 'location'
        }
        return types.get(value, 'text')


class ChannelType:
    """Типы каналов"""
    PUBLIC = 0
    PRIVATE = 1
    GROUP = 2
    
    @classmethod
    def from_value(cls, value: int) -> str:
        """Получить имя типа по значению"""
        types = {
            0: 'public',
            1: 'private',
            2: 'group'
        }
        return types.get(value, 'public')


@dataclass
class RegistrationRequest:
    """Запрос на регистрацию"""
    username: str
    email: str
    password: str
    public_key: str
    
    def to_bytes(self) -> bytes:
        return json.dumps({
            'Username': self.username,
            'Email': self.email,
            'Password': self.password,
            'PublicKey': self.public_key
        }).encode('utf-8')


@dataclass
class User:
    """Пользователь"""
    id: int
    username: str
    email: str
    public_key: str
    identity_key_fingerprint: Optional[str] = None
    is_active: bool = True
    created_at: Optional[str] = None
    updated_at: Optional[str] = None
    last_seen_at: Optional[str] = None
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'User':
        return cls(
            id=data['Id'],
            username=data['Username'],
            email=data['Email'],
            public_key=data['PublicKey'],
            identity_key_fingerprint=data.get('IdentityKeyFingerprint'),
            is_active=data.get('IsActive', True),
            created_at=data.get('CreatedAt'),
            updated_at=data.get('UpdatedAt'),
            last_seen_at=data.get('LastSeenAt')
        )


@dataclass
class RegistrationResponse:
    """Ответ на регистрацию"""
    success: bool
    message: Optional[str] = None
    user: Optional[User] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'RegistrationResponse':
        json_data = json.loads(data.decode('utf-8'))
        user_data = json_data.get('User')
        user = User.from_dict(user_data) if user_data else None
        
        return cls(
            success=json_data['Success'],
            message=json_data.get('Message'),
            user=user
        )


@dataclass
class UserSearchRequest:
    """Запрос поиска пользователей"""
    query: str
    limit: int = 20
    
    def to_bytes(self) -> bytes:
        return json.dumps({
            'Query': self.query,
            'Limit': self.limit
        }).encode('utf-8')


@dataclass
class UserSearchResult:
    """Результат поиска пользователя"""
    id: int
    username: str
    email: Optional[str] = None
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'UserSearchResult':
        return cls(
            id=data['Id'],
            username=data['Username'],
            email=data.get('Email')
        )


@dataclass
class UserSearchResponse:
    """Ответ на поиск пользователей"""
    success: bool
    users: List[UserSearchResult]
    message: Optional[str] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'UserSearchResponse':
        json_data = json.loads(data.decode('utf-8'))
        users = [UserSearchResult.from_dict(user_data) for user_data in json_data.get('Users', [])]
        
        return cls(
            success=json_data['Success'],
            users=users,
            message=json_data.get('Message')
        )


@dataclass
class ChannelMessageRequest:
    """Запрос сообщения в канал"""
    channel_id: int
    content: str
    content_type: int = MessageContentType.TEXT
    reply_to_message_id: Optional[int] = None
    
    def to_bytes(self) -> bytes:
        data = {
            'ChannelId': self.channel_id,
            'Content': self.content,
            'ContentType': self.content_type
        }
        if self.reply_to_message_id is not None:
            data['ReplyToMessageId'] = self.reply_to_message_id
        
        return json.dumps(data).encode('utf-8')


@dataclass
class ChannelMessage:
    """Сообщение в канале"""
    id: int
    channel_id: int
    from_user_id: int
    content: str
    content_type: int
    created_at: str
    edited_at: Optional[str] = None
    is_edited: bool = False
    reply_to_message_id: Optional[int] = None
    is_pinned: bool = False
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'ChannelMessage':
        return cls(
            id=data['Id'],
            channel_id=data['ChannelId'],
            from_user_id=data['FromUserId'],
            content=data['Content'],
            content_type=data['ContentType'],
            created_at=data['CreatedAt'],
            edited_at=data.get('EditedAt'),
            is_edited=data.get('IsEdited', False),
            reply_to_message_id=data.get('ReplyToMessageId'),
            is_pinned=data.get('IsPinned', False)
        )


@dataclass
class ChannelMessageResponse:
    """Ответ на сообщение в канал"""
    success: bool
    message: Optional[ChannelMessage] = None
    message_text: Optional[str] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'ChannelMessageResponse':
        json_data = json.loads(data.decode('utf-8'))
        message_data = json_data.get('Message')
        message = ChannelMessage.from_dict(message_data) if message_data else None
        
        return cls(
            success=json_data['Success'],
            message=message,
            message_text=json_data.get('MessageText')
        )


@dataclass
class Channel:
    """Канал"""
    id: int
    name: str
    description: Optional[str] = None
    type: int = ChannelType.PUBLIC
    created_by_user_id: int = 0
    created_at: Optional[str] = None
    updated_at: Optional[str] = None
    is_active: bool = True
    invite_code: Optional[str] = None
    member_count: int = 0
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'Channel':
        return cls(
            id=data['Id'],
            name=data['Name'],
            description=data.get('Description'),
            type=data.get('Type', ChannelType.PUBLIC),
            created_by_user_id=data.get('CreatedByUserId', 0),
            created_at=data.get('CreatedAt'),
            updated_at=data.get('UpdatedAt'),
            is_active=data.get('IsActive', True),
            invite_code=data.get('InviteCode'),
            member_count=data.get('MemberCount', 0)
        )


@dataclass
class ChannelCreateRequest:
    """Запрос на создание канала"""
    name: str
    description: Optional[str] = None
    type: int = ChannelType.PUBLIC
    
    def to_bytes(self) -> bytes:
        data = {
            'Name': self.name,
            'Type': self.type
        }
        if self.description is not None:
            data['Description'] = self.description
        
        return json.dumps(data).encode('utf-8')


@dataclass
class ChannelCreateResponse:
    """Ответ на создание канала"""
    success: bool
    channel: Optional[Channel] = None
    message: Optional[str] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'ChannelCreateResponse':
        json_data = json.loads(data.decode('utf-8'))
        channel_data = json_data.get('Channel')
        channel = Channel.from_dict(channel_data) if channel_data else None
        
        return cls(
            success=json_data['Success'],
            channel=channel,
            message=json_data.get('Message')
        )


@dataclass
class ChannelJoinRequest:
    """Запрос на присоединение к каналу"""
    channel_id: int
    
    def to_bytes(self) -> bytes:
        return json.dumps({
            'ChannelId': self.channel_id
        }).encode('utf-8')


@dataclass
class ChannelJoinResponse:
    """Ответ на присоединение к каналу"""
    success: bool
    channel: Optional[Channel] = None
    message: Optional[str] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'ChannelJoinResponse':
        json_data = json.loads(data.decode('utf-8'))
        channel_data = json_data.get('Channel')
        channel = Channel.from_dict(channel_data) if channel_data else None
        
        return cls(
            success=json_data['Success'],
            channel=channel,
            message=json_data.get('Message')
        )


@dataclass
class PrivateChatMessageRequest:
    """Запрос на приватное сообщение"""
    to_user_id: int
    content: str
    content_type: int = MessageContentType.TEXT
    
    def to_bytes(self) -> bytes:
        return json.dumps({
            'ToUserId': self.to_user_id,
            'Content': self.content,
            'ContentType': self.content_type
        }).encode('utf-8')


@dataclass
class Message:
    """Сообщение"""
    id: int
    from_user_id: int
    to_user_id: int
    content: str
    content_type: int
    sequence_number: int
    is_delivered: bool = False
    is_read: bool = False
    created_at: Optional[str] = None
    delivered_at: Optional[str] = None
    read_at: Optional[str] = None
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'Message':
        return cls(
            id=data['Id'],
            from_user_id=data['FromUserId'],
            to_user_id=data['ToUserId'],
            content=data['Content'],
            content_type=data['ContentType'],
            sequence_number=data['SequenceNumber'],
            is_delivered=data.get('IsDelivered', False),
            is_read=data.get('IsRead', False),
            created_at=data.get('CreatedAt'),
            delivered_at=data.get('DeliveredAt'),
            read_at=data.get('ReadAt')
        )


@dataclass
class PrivateChat:
    """Приватный чат"""
    id: int
    user1_id: int
    user2_id: int
    created_at: Optional[str] = None
    last_activity_at: Optional[str] = None
    last_message_id: Optional[int] = None
    is_active: bool = True
    last_message: Optional[Message] = None
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'PrivateChat':
        message_data = data.get('LastMessage')
        last_message = Message.from_dict(message_data) if message_data else None
        
        return cls(
            id=data['Id'],
            user1_id=data['User1Id'],
            user2_id=data['User2Id'],
            created_at=data.get('CreatedAt'),
            last_activity_at=data.get('LastActivityAt'),
            last_message_id=data.get('LastMessageId'),
            is_active=data.get('IsActive', True),
            last_message=last_message
        )


@dataclass
class PrivateChatMessageResponse:
    """Ответ на приватное сообщение"""
    success: bool
    message: Optional[Message] = None
    private_chat: Optional[PrivateChat] = None
    message_text: Optional[str] = None
    
    @classmethod
    def from_bytes(cls, data: bytes) -> 'PrivateChatMessageResponse':
        json_data = json.loads(data.decode('utf-8'))
        message_data = json_data.get('Message')
        message = Message.from_dict(message_data) if message_data else None
        
        chat_data = json_data.get('PrivateChat')
        private_chat = PrivateChat.from_dict(chat_data) if chat_data else None
        
        return cls(
            success=json_data['Success'],
            message=message,
            private_chat=private_chat,
            message_text=json_data.get('MessageText')
        )
