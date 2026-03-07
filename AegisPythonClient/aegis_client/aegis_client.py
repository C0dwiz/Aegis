"""Main high-level client for the Aegis protocol."""

from __future__ import annotations

import json
import struct
import time
from collections import deque
from typing import Iterable, Optional

from .crypto import AegisSessionCrypto, compute_mac, verify_mac
from .exceptions import AuthenticationException, NotConnectedException, ProtocolError, TimeoutException
from .message import Message, MessageFlags, MessageType
from .message_payloads import (
    AuthResponse,
    ChannelCreateRequest,
    ChannelCreateResponse,
    ChannelJoinRequest,
    ChannelJoinResponse,
    ChannelMessageRequest,
    ChannelMessageResponse,
    ChannelType,
    HandshakeResponse,
    MessageContentType,
    PrivateChatMessageRequest,
    PrivateChatMessageResponse,
    RegistrationRequest,
    RegistrationResponse,
    UserSearchRequest,
    UserSearchResponse,
)
from .protocol_constants import ProtocolConstants
from .transport import AegisTransport


class AegisClient:
    """Основной класс клиента Aegis."""

    def __init__(self):
        self._transport = AegisTransport()
        self._auth_token: Optional[str] = None
        self._is_authenticated = False
        self._sequence_counter = 0
        self._pending_messages: deque[Message] = deque()
        self._session_key: Optional[bytes] = None
        self._mac_key: Optional[bytes] = None
        self._handshake_complete = False

    @property
    def messages(self):
        return self._transport.messages

    @property
    def disconnects(self):
        return self._transport.disconnects

    @property
    def is_connected(self) -> bool:
        return self._transport.is_connected

    @property
    def is_authenticated(self) -> bool:
        return self._is_authenticated

    def add_message_listener(self, listener) -> None:
        self._transport.add_message_listener(listener)

    def add_disconnect_listener(self, listener) -> None:
        self._transport.add_disconnect_listener(listener)

    def connect(self, host: str, port: int, timeout: Optional[float] = None) -> None:
        self._transport.connect(host, port, timeout)
        self._perform_handshake()

    def authenticate(self, auth_token: str, client_info: str = "AegisPythonClient") -> AuthResponse:
        return self._authenticate(token=auth_token, client_info=client_info)

    def login(self, username: str, password: str, client_info: str = "AegisPythonClient") -> AuthResponse:
        return self._authenticate(username=username, password=password, client_info=client_info)

    def register(self, username: str, email: str, password: str, public_key: str) -> RegistrationResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()

        request = RegistrationRequest(username, email, password, public_key)
        sequence_id = self._send_request(MessageType.REGISTER, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.REGISTER_RESPONSE,), timeout=10)
        return RegistrationResponse.from_bytes(response_message.payload)

    def search_users(self, query: str, limit: int = 20) -> UserSearchResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        request = UserSearchRequest(query, limit)
        sequence_id = self._send_request(MessageType.USER_SEARCH, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.USER_SEARCH_RESULT,), timeout=10)
        return UserSearchResponse.from_bytes(response_message.payload)

    def create_channel(
        self,
        name: str,
        description: Optional[str] = None,
        channel_type: int = ChannelType.PUBLIC,
    ) -> ChannelCreateResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        request = ChannelCreateRequest(name, description, channel_type)
        sequence_id = self._send_request(MessageType.CHANNEL_CREATE, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.ACK,), timeout=10)
        return ChannelCreateResponse.from_bytes(response_message.payload)

    def join_channel(self, channel_id: int) -> ChannelJoinResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        request = ChannelJoinRequest(channel_id)
        sequence_id = self._send_request(MessageType.CHANNEL_JOIN, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.ACK,), timeout=10)
        return ChannelJoinResponse.from_bytes(response_message.payload)

    def send_channel_message(
        self,
        channel_id: int,
        content: str,
        content_type: int = MessageContentType.TEXT,
        reply_to_message_id: Optional[int] = None,
    ) -> ChannelMessageResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        request = ChannelMessageRequest(channel_id, content, content_type, reply_to_message_id)
        sequence_id = self._send_request(MessageType.CHANNEL_MESSAGE, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.ACK,), timeout=10)
        return ChannelMessageResponse.from_bytes(response_message.payload)

    def send_private_message(
        self,
        to_user_id: int,
        content: str,
        content_type: int = MessageContentType.TEXT,
    ) -> PrivateChatMessageResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        request = PrivateChatMessageRequest(to_user_id, content, content_type)
        sequence_id = self._send_request(MessageType.PRIVATE_CHAT_MESSAGE, request.to_bytes(), MessageFlags.REQUIRES_ACK)
        response_message = self._wait_for_response(sequence_id, (MessageType.ACK,), timeout=10)
        return PrivateChatMessageResponse.from_bytes(response_message.payload)

    def send_message(self, text: str, to_user_id: Optional[int] = None) -> None:
        if not self._transport.is_connected:
            raise NotConnectedException()
        if not self._is_authenticated:
            raise AuthenticationException("Client is not authenticated")

        payload = bytearray()
        payload.extend(struct.pack(">Q", 0))
        payload.extend(struct.pack(">Q", to_user_id or 0))
        payload.append(0)
        payload.extend([0, 0, 0])
        payload.extend(text.encode("utf-8"))

        message = self._build_message(MessageType.MESSAGE, bytes(payload), MessageFlags.REQUIRES_ACK)
        self._transport.send_message(message)

    def ping(self) -> None:
        if not self._transport.is_connected:
            raise NotConnectedException()

        timestamp = int(time.time() * 1000)
        message = self._build_message(MessageType.PING, struct.pack(">Q", timestamp))
        self._transport.send_message(message)

    def disconnect(self) -> None:
        self._transport.disconnect()
        self._is_authenticated = False
        self._auth_token = None
        self._handshake_complete = False
        self._session_key = None
        self._mac_key = None
        self._pending_messages.clear()

    def dispose(self) -> None:
        self.disconnect()

    def _authenticate(
        self,
        token: Optional[str] = None,
        username: Optional[str] = None,
        password: Optional[str] = None,
        client_info: str = "AegisPythonClient",
    ) -> AuthResponse:
        if not self._transport.is_connected:
            raise NotConnectedException()

        request_payload = {
            "Username": username or "",
            "Password": password or "",
            "Token": token or "",
            "ClientInfo": client_info,
        }
        sequence_id = self._send_request(
            MessageType.AUTH,
            json.dumps(request_payload).encode("utf-8"),
            MessageFlags.REQUIRES_ACK,
        )
        response_message = self._wait_for_response(sequence_id, (MessageType.ACK,), timeout=10)
        response = AuthResponse.from_bytes(response_message.payload)
        if not response.success:
            raise AuthenticationException(response.error or "Authentication failed")

        self._auth_token = token
        self._is_authenticated = True
        return response

    def _perform_handshake(self) -> None:
        crypto = AegisSessionCrypto()
        payload = json.dumps(
            {
                "PublicKey": crypto.public_key_base64,
                "ClientVersion": 1000,
            }
        ).encode("utf-8")

        message = self._build_message(MessageType.HANDSHAKE, payload, sign=False)
        self._transport.send_message(message)

        response_message = self._wait_for_response(message.sequence_id, (MessageType.HANDSHAKE,), timeout=10)
        response = HandshakeResponse.from_bytes(response_message.payload)
        if not response.success or not response.server_public_key:
            raise ProtocolError(response.message or "Handshake failed")

        session_key, mac_key = crypto.derive_keys(response.server_public_key)
        if any(response_message.mac) and not verify_mac(
            response_message.to_bytes()[:-ProtocolConstants.MAC_SIZE],
            mac_key,
            response_message.mac,
        ):
            raise ProtocolError("Handshake response MAC verification failed")

        self._session_key = session_key
        self._mac_key = mac_key
        self._handshake_complete = True

    def _build_message(
        self,
        message_type: MessageType,
        payload: bytes = b"",
        flags: int = MessageFlags.NONE,
        sign: bool = True,
    ) -> Message:
        message = Message.with_type(message_type, payload)
        message.flags = flags
        message.sequence_id = self._get_next_sequence_id()
        message.mac = bytes(ProtocolConstants.MAC_SIZE)
        if sign:
            self._sign_message(message)
        return message

    def _send_request(self, message_type: MessageType, payload: bytes, flags: int = MessageFlags.NONE) -> int:
        message = self._build_message(message_type, payload, flags)
        self._transport.send_message(message)
        return message.sequence_id

    def _sign_message(self, message: Message) -> None:
        if not self._handshake_complete or not self._mac_key:
            raise ProtocolError("Handshake must complete before sending signed messages")

        message.mac = bytes(ProtocolConstants.MAC_SIZE)
        encoded = message.to_bytes()
        message.mac = compute_mac(encoded[:-ProtocolConstants.MAC_SIZE], self._mac_key)

    def _get_next_sequence_id(self) -> int:
        self._sequence_counter += 1
        return self._sequence_counter

    def _wait_for_response(self, sequence_id: int, message_types: Iterable[MessageType], timeout: float = 10.0) -> Message:
        message_type_set = set(message_types)
        start_time = time.time()

        while time.time() - start_time < timeout:
            message = self._take_pending_match(sequence_id, message_type_set)
            if message is not None:
                return message

            message = self._transport.get_message(timeout=0.1)
            if message is None:
                continue

            if message.sequence_id == sequence_id and message.type in message_type_set:
                return message

            if message.sequence_id == sequence_id and message.type == MessageType.ERROR:
                raise ProtocolError(message.payload.decode("utf-8", errors="replace") or "Server returned an error")

            self._pending_messages.append(message)

        type_names = ", ".join(message_type.name for message_type in message_type_set)
        raise TimeoutException(f"Timeout waiting for {type_names} response")

    def _take_pending_match(self, sequence_id: int, message_types: set[MessageType]) -> Optional[Message]:
        for _ in range(len(self._pending_messages)):
            message = self._pending_messages.popleft()
            if message.sequence_id == sequence_id and message.type in message_types:
                return message

            if message.sequence_id == sequence_id and message.type == MessageType.ERROR:
                raise ProtocolError(message.payload.decode("utf-8", errors="replace") or "Server returned an error")

            self._pending_messages.append(message)

        return None