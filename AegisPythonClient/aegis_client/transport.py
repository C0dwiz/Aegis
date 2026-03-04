"""
Транспортный слой Aegis
"""
import socket
import threading
import queue
from typing import Callable, Optional
from .message import Message
from .exceptions import ConnectionException, NotConnectedException


class AegisTransport:
    """Транспортный слой для работы с TCP соединением"""
    
    def __init__(self):
        self._socket: Optional[socket.socket] = None
        self._connected = False
        self._receive_thread: Optional[threading.Thread] = None
        self._message_queue = queue.Queue()
        self._disconnect_listeners: list[Callable[[], None]] = []
        self._message_listeners: list[Callable[[Message], None]] = []
        self._running = False
        self._lock = threading.Lock()
    
    @property
    def is_connected(self) -> bool:
        """Проверка состояния подключения"""
        return self._connected
    
    def connect(self, host: str, port: int, timeout: Optional[float] = None) -> None:
        """Подключение к серверу"""
        if self._connected:
            return
        
        try:
            self._socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self._socket.settimeout(timeout or 10.0)
            self._socket.connect((host, port))
            self._socket.settimeout(None)  # Убираем таймаут после подключения
            
            self._connected = True
            self._running = True
            
            # Запускаем поток приема сообщений
            self._receive_thread = threading.Thread(target=self._receive_loop, daemon=True)
            self._receive_thread.start()
            
        except socket.error as e:
            self._cleanup()
            raise ConnectionException(f"Failed to connect to {host}:{port}: {e}")
    
    def disconnect(self) -> None:
        """Отключение от сервера"""
        with self._lock:
            if not self._connected:
                return
            
            self._running = False
            self._cleanup()
            
            # Уведомляем слушателей о отключении
            for listener in self._disconnect_listeners:
                try:
                    listener()
                except Exception:
                    pass  # Игнорируем ошибки в слушателях
    
    def send_message(self, message: Message) -> None:
        """Отправка сообщения"""
        if not self._connected or not self._socket:
            raise NotConnectedException("Not connected to server")
        
        try:
            data = message.to_bytes()
            self._socket.sendall(data)
        except socket.error as e:
            self._connected = False
            raise ConnectionException(f"Failed to send message: {e}")
    
    def _receive_loop(self) -> None:
        """Основной цикл приема сообщений"""
        buffer = b''
        
        while self._running and self._connected:
            try:
                # Получаем данные
                data = self._socket.recv(4096)
                if not data:
                    break
                
                buffer += data
                
                # Обрабатываем полные сообщения
                while len(buffer) >= 20:  # Минимальный размер заголовка
                    # Извлекаем длину payload
                    payload_length = int.from_bytes(buffer[16:20], byteorder='big')
                    total_size = 20 + payload_length + 32  # header + payload + MAC
                    
                    if len(buffer) < total_size:
                        break  # Недостаточно данных для полного сообщения
                    
                    # Извлекаем сообщение
                    message_data = buffer[:total_size]
                    buffer = buffer[total_size:]
                    
                    try:
                        message = Message.from_bytes(message_data)
                        self._handle_message(message)
                    except Exception as e:
                        print(f"Error parsing message: {e}")
                        continue
                
            except socket.error:
                break
            except Exception as e:
                print(f"Error in receive loop: {e}")
                break
        
        # Соединение разорвано
        self._connected = False
        self._cleanup()
        
        # Уведомляем о отключении
        for listener in self._disconnect_listeners:
            try:
                listener()
            except Exception:
                pass
    
    def _handle_message(self, message: Message) -> None:
        """Обработка полученного сообщения"""
        # Помещаем в очередь
        self._message_queue.put(message)
        
        # Уведомляем слушателей
        for listener in self._message_listeners:
            try:
                listener(message)
            except Exception:
                pass  # Игнорируем ошибки в слушателях
    
    def _cleanup(self) -> None:
        """Очистка ресурсов"""
        try:
            if self._socket:
                self._socket.close()
        except Exception:
            pass
        finally:
            self._socket = None
            self._connected = False
    
    def add_disconnect_listener(self, listener: Callable[[], None]) -> None:
        """Добавить слушателя события отключения"""
        self._disconnect_listeners.append(listener)
    
    def add_message_listener(self, listener: Callable[[Message], None]) -> None:
        """Добавить слушателя сообщений"""
        self._message_listeners.append(listener)
    
    def get_message(self, timeout: Optional[float] = None) -> Optional[Message]:
        """Получить сообщение из очереди"""
        try:
            return self._message_queue.get(timeout=timeout)
        except queue.Empty:
            return None
    
    @property
    def messages(self) -> queue.Queue:
        """Очередь сообщений для чтения"""
        return self._message_queue
    
    @property
    def disconnects(self) -> list[Callable[[], None]]:
        """Список слушателей отключения"""
        return self._disconnect_listeners
    
    def dispose(self) -> None:
        """Освобождение ресурсов"""
        self.disconnect()
