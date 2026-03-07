"""Transport layer for the Aegis Python client."""

from __future__ import annotations

import queue
import socket
import threading
from typing import Callable, Optional

from .exceptions import ConnectionException, NotConnectedException
from .message import Message
from .protocol_constants import ProtocolConstants


class EventHook:
    """Simple callback collection with the legacy `.listen = callback` API."""

    def __init__(self) -> None:
        self._listeners: list[Callable] = []

    def add_listener(self, listener: Callable) -> None:
        self._listeners.append(listener)

    @property
    def listen(self) -> list[Callable]:
        return self._listeners

    @listen.setter
    def listen(self, listener: Callable) -> None:
        self.add_listener(listener)

    def emit(self, *args) -> None:
        for listener in list(self._listeners):
            try:
                listener(*args)
            except Exception:
                pass


class AegisTransport:
    """TCP transport for the Aegis protocol."""

    def __init__(self):
        self._socket: Optional[socket.socket] = None
        self._connected = False
        self._receive_thread: Optional[threading.Thread] = None
        self._message_queue: queue.Queue[Message] = queue.Queue()
        self._disconnect_events = EventHook()
        self._message_events = EventHook()
        self._running = False
        self._lock = threading.Lock()

    @property
    def is_connected(self) -> bool:
        return self._connected

    def connect(self, host: str, port: int, timeout: Optional[float] = None) -> None:
        if self._connected:
            return

        try:
            self._socket = socket.create_connection((host, port), timeout or 10.0)
            self._socket.settimeout(None)
            self._connected = True
            self._running = True
            self._receive_thread = threading.Thread(target=self._receive_loop, daemon=True)
            self._receive_thread.start()
        except OSError as exc:
            self._cleanup()
            raise ConnectionException(f"Failed to connect to {host}:{port}: {exc}") from exc

    def disconnect(self) -> None:
        with self._lock:
            if not self._connected:
                return

            self._running = False
            self._cleanup()
            self._disconnect_events.emit()

    def send_message(self, message: Message) -> None:
        if not self._connected or not self._socket:
            raise NotConnectedException("Not connected to server")

        try:
            self._socket.sendall(message.to_bytes())
        except OSError as exc:
            self._connected = False
            raise ConnectionException(f"Failed to send message: {exc}") from exc

    def _receive_loop(self) -> None:
        buffer = b""

        while self._running and self._connected:
            try:
                data = self._socket.recv(4096)
                if not data:
                    break

                buffer += data

                while len(buffer) >= ProtocolConstants.HEADER_SIZE:
                    payload_length = int.from_bytes(buffer[17:21], byteorder="big")
                    total_size = ProtocolConstants.HEADER_SIZE + payload_length + ProtocolConstants.MAC_SIZE
                    if len(buffer) < total_size:
                        break

                    message_data = buffer[:total_size]
                    buffer = buffer[total_size:]

                    try:
                        self._handle_message(Message.from_bytes(message_data))
                    except Exception as exc:
                        print(f"Error parsing message: {exc}")
            except OSError:
                break
            except Exception as exc:
                print(f"Error in receive loop: {exc}")
                break

        self._connected = False
        self._cleanup()
        self._disconnect_events.emit()

    def _handle_message(self, message: Message) -> None:
        self._message_queue.put(message)
        self._message_events.emit(message)

    def _cleanup(self) -> None:
        try:
            if self._socket:
                self._socket.close()
        except Exception:
            pass
        finally:
            self._socket = None
            self._connected = False

    def add_disconnect_listener(self, listener: Callable[[], None]) -> None:
        self._disconnect_events.add_listener(listener)

    def add_message_listener(self, listener: Callable[[Message], None]) -> None:
        self._message_events.add_listener(listener)

    def get_message(self, timeout: Optional[float] = None) -> Optional[Message]:
        try:
            return self._message_queue.get(timeout=timeout)
        except queue.Empty:
            return None

    @property
    def messages(self) -> EventHook:
        return self._message_events

    @property
    def disconnects(self) -> EventHook:
        return self._disconnect_events

    def dispose(self) -> None:
        self.disconnect()
