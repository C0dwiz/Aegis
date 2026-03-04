"""
Основной класс Aegis Client
"""
import time
import struct
from typing import Optional
from .transport import AegisTransport
from .message import Message, MessageType, MessageFlags
from .exceptions import NotConnectedException, TimeoutException
from .message_payloads import (
    RegistrationRequest, RegistrationResponse,
    UserSearchRequest, UserSearchResponse,
    ChannelCreateRequest, ChannelCreateResponse,
    ChannelMessageRequest, ChannelMessageResponse,
    PrivateChatMessageRequest, PrivateChatMessageResponse,
    MessageContentType, ChannelType
)


class AegisClient:
    """Основной класс клиента Aegis"""
    
    def __init__(self):
        self._transport = AegisTransport()
        self._auth_token: Optional[str] = None
        self._is_authenticated = False
        self._sequence_counter = 0
    
    @property
    def messages(self):
        """Поток входящих сообщений"""
        return self._transport.messages
    
    @property
    def disconnects(self):
        """Поток событий отключения"""
        return self._transport.disconnects
    
    @property
    def is_connected(self) -> bool:
        """Проверка подключения к серверу"""
        return self._transport.is_connected
    
    @property
    def is_authenticated(self) -> bool:
        """Проверка аутентификации"""
        return self._is_authenticated
    
    def connect(self, host: str, port: int, timeout: Optional[float] = None) -> None:
        """Подключение к Aegis серверу"""
        self._transport.connect(host, port, timeout)
        
        # Отправка handshake сообщения
        self._send_handshake()
    
    def authenticate(self, auth_token: str) -> None:
        """Аутентификация на сервере"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        message = Message.with_type(MessageType.AUTH, auth_token.encode('utf-8'))
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        self._auth_token = auth_token
        
        # Ожидание ACK ответа (упрощено)
        self._wait_for_message(MessageType.ACK, timeout=10)
        self._is_authenticated = True
    
    def register(self, username: str, email: str, password: str, public_key: str) -> RegistrationResponse:
        """Регистрация нового пользователя"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        request = RegistrationRequest(username, email, password, public_key)
        message = Message.with_type(MessageType.REGISTER, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа
        response_message = self._wait_for_message(MessageType.REGISTER_RESPONSE, timeout=10)
        return RegistrationResponse.from_bytes(response_message.payload)
    
    def search_users(self, query: str, limit: int = 20) -> UserSearchResponse:
        """Поиск пользователей по имени"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        request = UserSearchRequest(query, limit)
        message = Message.with_type(MessageType.USER_SEARCH, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа
        response_message = self._wait_for_message(MessageType.USER_SEARCH_RESULT, timeout=10)
        return UserSearchResponse.from_bytes(response_message.payload)
    
    def create_channel(self, name: str, description: Optional[str] = None, 
                      channel_type: int = ChannelType.PUBLIC) -> ChannelCreateResponse:
        """Создание нового канала"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        request = ChannelCreateRequest(name, description, channel_type)
        message = Message.with_type(MessageType.CHANNEL_CREATE, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа
        response_message = self._wait_for_message(MessageType.CHANNEL_CREATE, timeout=10)
        return ChannelCreateResponse.from_bytes(response_message.payload)
    
    def join_channel(self, channel_id: int):
        """Присоединение к каналу"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        # Для простоты используем MessageType.CHANNEL_CREATE как ответ
        # В реальной реализации должен быть отдельный тип ответа
        from .message_payloads import ChannelJoinRequest, ChannelJoinResponse
        
        request = ChannelJoinRequest(channel_id)
        message = Message.with_type(MessageType.CHANNEL_JOIN, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа (используем CHANNEL_CREATE как заглушку)
        response_message = self._wait_for_message(MessageType.CHANNEL_JOIN, timeout=10)
        return ChannelJoinResponse.from_bytes(response_message.payload)
    
    def send_channel_message(self, channel_id: int, content: str, 
                           content_type: int = MessageContentType.TEXT,
                           reply_to_message_id: Optional[int] = None) -> ChannelMessageResponse:
        """Отправка сообщения в канал"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        request = ChannelMessageRequest(channel_id, content, content_type, reply_to_message_id)
        message = Message.with_type(MessageType.CHANNEL_MESSAGE, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа
        response_message = self._wait_for_message(MessageType.CHANNEL_MESSAGE, timeout=10)
        return ChannelMessageResponse.from_bytes(response_message.payload)
    
    def send_private_message(self, to_user_id: int, content: str,
                           content_type: int = MessageContentType.TEXT) -> PrivateChatMessageResponse:
        """Отправка приватного сообщения"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        request = PrivateChatMessageRequest(to_user_id, content, content_type)
        message = Message.with_type(MessageType.PRIVATE_CHAT_MESSAGE, request.to_bytes())
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
        
        # Ожидание ответа
        response_message = self._wait_for_message(MessageType.PRIVATE_CHAT_MESSAGE, timeout=10)
        return PrivateChatMessageResponse.from_bytes(response_message.payload)
    
    def send_message(self, text: str, to_user_id: Optional[int] = None) -> None:
        """Отправка текстового сообщения (legacy метод)"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        if not self._is_authenticated:
            raise Exception("Client is not authenticated")
        
        # Создание payload: fromId(8) + toId(8) + messageType(1) + reserved(3) + text
        payload = bytearray()
        
        # From user ID (заглушка)
        payload.extend(struct.pack('>Q', 0))
        
        # To user ID (0 для broadcast)
        payload.extend(struct.pack('>Q', to_user_id or 0))
        
        # Message type (0 = text)
        payload.append(0)
        
        # Reserved bytes
        payload.extend([0, 0, 0])
        
        # Message text
        payload.extend(text.encode('utf-8'))
        
        message = Message.with_type(MessageType.MESSAGE, bytes(payload))
        message.flags = MessageFlags.REQUIRES_ACK
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
    
    def ping(self) -> None:
        """Отправка ping сообщения"""
        if not self._transport.is_connected:
            raise NotConnectedException()
        
        timestamp = int(time.time() * 1000)
        message = Message.with_type(MessageType.PING, struct.pack('>Q', timestamp))
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
    
    def disconnect(self) -> None:
        """Отключение от сервера"""
        self._transport.disconnect()
        self._is_authenticated = False
        self._auth_token = None
    
    def _send_handshake(self) -> None:
        """Отправка handshake сообщения"""
        message = Message.with_type(MessageType.HANDSHAKE)
        
        # Создание payload: clientVersion(4) + nonce(12) + publicKey(var)
        payload = bytearray()
        
        # Client version (заглушка)
        payload.extend(struct.pack('>I', 1000))
        
        # Nonce (12 случайных байт)
        nonce = int(time.time() * 1000000).to_bytes(12, 'big')
        payload.extend(nonce)
        
        # Public key (заглушка)
        payload.extend(b'client_public_key_placeholder')
        
        message.payload = bytes(payload)
        message.sequence_id = self._get_next_sequence_id()
        
        self._transport.send_message(message)
    
    def _get_next_sequence_id(self) -> int:
        """Получить следующий sequence ID"""
        self._sequence_counter += 1
        return self._sequence_counter
    
    def _wait_for_message(self, message_type: MessageType, timeout: float = 10.0) -> Message:
        """Ожидание сообщения определенного типа"""
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            message = self._transport.get_message(timeout=0.1)
            if message and message.type == message_type:
                return message
        
        raise TimeoutException(f"Timeout waiting for {message_type.name} message")
    
    def dispose(self) -> None:
        """Освобождение ресурсов"""
        self.disconnect()
