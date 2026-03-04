#!/usr/bin/env python3
"""
Полный пример использования Aegis Python Client
демонстрирует все возможности клиента
"""

import sys
import os
import time

# Добавляем путь к родительской директории для импорта модуля
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from aegis_client import AegisClient, MessageContentType, ChannelType


class CompleteAegisExample:
    """Полный пример использования Aegis Client"""
    
    def __init__(self):
        self.client = AegisClient()
        self.current_user = None
        self.current_channel = None
        self.running = True
    
    def run(self):
        """Запуск полного примера"""
        print("🚀 Запуск полного примера Aegis Python Client")
        
        try:
            # Настройка обработчиков сообщений
            self._setup_message_handlers()
            
            # Шаг 1: Подключение к серверу
            self._connect_to_server()
            
            # Шаг 2: Регистрация и аутентификация
            self._register_and_authenticate()
            
            # Шаг 3: Поиск пользователей
            self._demonstrate_user_search()
            
            # Шаг 4: Работа с каналами
            self._demonstrate_channel_operations()
            
            # Шаг 5: Приватные сообщения
            self._demonstrate_private_messaging()
            
            # Шаг 6: Обработка сообщений в реальном времени
            self._demonstrate_realtime_messaging()
            
            print("✅ Все демонстрации успешно завершены!")
            
        except Exception as e:
            print(f"❌ Ошибка в примере: {e}")
        finally:
            self._cleanup()
    
    def _connect_to_server(self):
        """Подключение к Aegis серверу"""
        print("\n📡 Подключение к серверу...")
        
        try:
            self.client.connect('localhost', 8888)
            print("✅ Подключено к серверу успешно")
        except Exception as e:
            print(f"❌ Не удалось подключиться: {e}")
            raise
    
    def _register_and_authenticate(self):
        """Регистрация пользователя и аутентификация"""
        print('\n👤 Регистрация пользователя и аутентификация')
        
        try:
            # Регистрация нового пользователя
            timestamp = int(time.time())
            username = f'user_{timestamp}'
            email = f'user_{timestamp}@example.com'
            
            print(f'📝 Регистрация пользователя: {username}')
            registration_response = self.client.register(
                username,
                email,
                'secure_password_123',
                'generated_public_key_here'
            )
            
            if registration_response.success and registration_response.user:
                self.current_user = registration_response.user
                print('✅ Пользователь успешно зарегистрирован')
                print(f'   ID: {self.current_user.id}')
                print(f'   Имя пользователя: {self.current_user.username}')
            else:
                print(f'❌ Регистрация не удалась: {registration_response.message}')
                return
            
            # Аутентификация
            print('🔐 Аутентификация...')
            self.client.authenticate(f'auth_token_for_{self.current_user.id}')
            print('✅ Аутентификация успешна')
            
        except Exception as e:
            print(f'❌ Ошибка регистрации/аутентификации: {e}')
            raise
    
    def _demonstrate_user_search(self):
        """Демонстрация поиска пользователей"""
        if not self.current_user:
            return
        
        print('\n🔍 Демонстрация поиска пользователей')
        
        try:
            # Поиск пользователей по имени
            print('🔎 Поиск пользователей с "user" в имени...')
            search_response = self.client.search_users('user', limit=5)
            
            if search_response.success:
                print(f'✅ Найдено пользователей: {len(search_response.users)}')
                for user in search_response.users:
                    print(f'   👤 {user.username} (ID: {user.id})')
                    if user.email:
                        print(f'      📧 {user.email}')
            else:
                print(f'❌ Поиск не удался: {search_response.message}')
            
            # Поиск конкретного пользователя
            if self.current_user:
                print(f'🔎 Поиск текущего пользователя: {self.current_user.username}')
                specific_search = self.client.search_users(self.current_user.username, limit=1)
                
                if specific_search.success and specific_search.users:
                    found_user = specific_search.users[0]
                    print(f'✅ Найден текущий пользователь: {found_user.username}')
            
        except Exception as e:
            print(f'❌ Ошибка поиска пользователей: {e}')
    
    def _demonstrate_channel_operations(self):
        """Демонстрация операций с каналами"""
        if not self.current_user:
            return
        
        print('\n📢 Демонстрация операций с каналами')
        
        try:
            # Создание публичного канала
            print('🏗️ Создание публичного канала...')
            channel_name = f'Test Channel {int(time.time())}'
            channel_response = self.client.create_channel(
                channel_name,
                description='A test channel for demonstrating Aegis Python client',
                channel_type=ChannelType.PUBLIC
            )
            
            if channel_response.success and channel_response.channel:
                self.current_channel = channel_response.channel
                print('✅ Канал создан успешно')
                print(f'   📢 Название: {self.current_channel.name}')
                print(f'   🆔 ID: {self.current_channel.id}')
                print(f'   📝 Описание: {self.current_channel.description or "None"}')
                print(f'   🔓 Тип: {self.current_channel.type}')
                print(f'   👥 Участников: {self.current_channel.member_count}')
                
                # Присоединение к каналу
                print('\n🚪 Присоединение к каналу...')
                join_response = self.client.join_channel(self.current_channel.id)
                if join_response.success:
                    print('✅ Присоединение к каналу успешно')
                else:
                    print(f'❌ Не удалось присоединиться: {join_response.message}')
                
                # Отправка различных типов сообщений в канал
                self._send_channel_messages()
                
            else:
                print(f'❌ Создание канала не удалось: {channel_response.message}')
            
        except Exception as e:
            print(f'❌ Ошибка операций с каналами: {e}')
    
    def _send_channel_messages(self):
        """Отправка различных типов сообщений в канал"""
        if not self.current_channel:
            return
        
        print('📨 Отправка сообщений в канал...')
        
        try:
            # Отправка текстового сообщения
            print('📝 Отправка текстового сообщения...')
            text_response = self.client.send_channel_message(
                self.current_channel.id,
                'Hello from Python client! 🐍',
                content_type=MessageContentType.TEXT
            )
            
            if text_response.success:
                print('✅ Текстовое сообщение отправлено')
                if text_response.message:
                    print(f'   🆔 ID сообщения: {text_response.message.id}')
            else:
                print(f'❌ Не удалось отправить текстовое сообщение: {text_response.message_text}')
            
            # Отправка ответа на сообщение
            if text_response.message:
                print('💬 Отправка ответа на сообщение...')
                reply_response = self.client.send_channel_message(
                    self.current_channel.id,
                    'I agree with your point! 👍',
                    reply_to_message_id=text_response.message.id
                )
                
                if reply_response.success:
                    print('✅ Ответ отправлен')
                else:
                    print(f'❌ Не удалось отправить ответ: {reply_response.message_text}')
            
            # Отправка другого типа контента
            print('📎 Отправка файла...')
            file_response = self.client.send_channel_message(
                self.current_channel.id,
                'Check out this file!',
                content_type=MessageContentType.FILE
            )
            
            if file_response.success:
                print('✅ Файл отправлен')
            
        except Exception as e:
            print(f'❌ Ошибка отправки сообщений в канал: {e}')
    
    def _demonstrate_private_messaging(self):
        """Демонстрация приватных сообщений"""
        if not self.current_user:
            return
        
        print('\n💬 Демонстрация приватных сообщений')
        
        try:
            # Поиск пользователя для приватного сообщения
            print('🔎 Поиск пользователя для приватного сообщения...')
            search_response = self.client.search_users('user', limit=10)
            
            if search_response.success and len(search_response.users) > 1:
                # Находим пользователя, который не является текущим
                other_user = None
                for user in search_response.users:
                    if user.id != self.current_user.id:
                        other_user = user
                        break
                
                if other_user:
                    print(f'👤 Найден пользователь: {other_user.username} (ID: {other_user.id})')
                    
                    # Отправка приватного сообщения
                    print('📨 Отправка приватного сообщения...')
                    private_response = self.client.send_private_message(
                        other_user.id,
                        f'Hello {other_user.username}! This is a private message from {self.current_user.username} 🤖',
                        content_type=MessageContentType.TEXT
                    )
                    
                    if private_response.success:
                        print('✅ Приватное сообщение отправлено успешно')
                        if private_response.message:
                            print(f'   🆔 ID сообщения: {private_response.message.id}')
                        if private_response.private_chat:
                            print(f'   💬 ID чата: {private_response.private_chat.id}')
                    else:
                        print(f'❌ Не удалось отправить приватное сообщение: {private_response.message_text}')
                else:
                    print('ℹ️ Другие пользователи не найдены для демонстрации приватных сообщений')
            else:
                print('ℹ️ Другие пользователи не найдены для демонстрации приватных сообщений')
            
        except Exception as e:
            print(f'❌ Ошибка приватных сообщений: {e}')
    
    def _demonstrate_realtime_messaging(self):
        """Демонстрация обработки сообщений в реальном времени"""
        print('\n⚡ Демонстрация обработки сообщений в реальном времени')
        print('👂 Прослушивание входящих сообщений в течение 10 секунд...')
        
        # Обработка сообщений уже настроена в _setup_message_handlers()
        time.sleep(10)
        print('⏹️ Демонстрация реального времени завершена')
    
    def _setup_message_handlers(self):
        """Настройка обработчиков сообщений"""
        print('🔧 Настройка обработчиков сообщений...')
        
        def handle_message(message):
            """Обработка входящих сообщений"""
            message_type = message.type.name.lower()
            print(f'📩 Получено сообщение: {message_type}')
            
            if message_type == 'message':
                self._handle_basic_message(message)
            elif message_type == 'channel_message':
                self._handle_channel_message(message)
            elif message_type == 'private_chat_message':
                self._handle_private_message(message)
            elif message_type == 'user_search_result':
                self._handle_user_search_result(message)
            elif message_type == 'register_response':
                self._handle_registration_response(message)
            elif message_type == 'ping':
                self._handle_ping_message(message)
            elif message_type == 'error':
                self._handle_error_message(message)
            else:
                print(f'📩 Получено необработанное сообщение: {message_type}')
        
        def handle_disconnect():
            """Обработка отключения"""
            print('🔌 Отключено от сервера')
            self.running = False
        
        # Назначаем обработчики
        self.client.messages.listen = handle_message
        self.client.disconnects.listen = handle_disconnect
    
    def _handle_basic_message(self, message):
        """Обработка базовых сообщений"""
        try:
            if len(message.payload) > 21:
                text = message.payload[21:].decode('utf-8', errors='ignore')
                print(f'📨 Базовое сообщение: {text}')
        except Exception as e:
            print(f'❌ Ошибка разбора базового сообщения: {e}')
    
    def _handle_channel_message(self, message):
        """Обработка сообщений канала"""
        print('📢 Получено сообщение канала')
        try:
            # В реальной реализации здесь был бы разбор JSON payload
            print(f'   🆔 Sequence ID: {message.sequence_id}')
        except Exception as e:
            print(f'❌ Ошибка разбора сообщения канала: {e}')
    
    def _handle_private_message(self, message):
        """Обработка приватных сообщений"""
        print('💬 Получено приватное сообщение')
        try:
            print(f'   🆔 Sequence ID: {message.sequence_id}')
        except Exception as e:
            print(f'❌ Ошибка разбора приватного сообщения: {e}')
    
    def _handle_user_search_result(self, message):
        """Обработка результатов поиска пользователей"""
        print('🔍 Получен результат поиска пользователей')
        try:
            print(f'   📊 Размер payload: {len(message.payload)} байт')
        except Exception as e:
            print(f'❌ Ошибка разбора результата поиска: {e}')
    
    def _handle_registration_response(self, message):
        """Обработка ответа регистрации"""
        print('📝 Получен ответ регистрации')
        try:
            print(f'   🆔 Sequence ID: {message.sequence_id}')
        except Exception as e:
            print(f'❌ Ошибка разбора ответа регистрации: {e}')
    
    def _handle_ping_message(self, message):
        """Обработка ping сообщений"""
        try:
            if len(message.payload) >= 8:
                timestamp = int.from_bytes(message.payload[:8], byteorder='big')
                latency = int(time.time() * 1000) - timestamp
                print(f'🏓 Ping получен: {latency}ms задержка')
        except Exception as e:
            print(f'❌ Ошибка разбора ping сообщения: {e}')
    
    def _handle_error_message(self, message):
        """Обработка сообщений об ошибках"""
        try:
            if len(message.payload) >= 4:
                error_code = int.from_bytes(message.payload[:2], byteorder='big')
                error_text = message.payload[4:].decode('utf-8', errors='ignore')
                print(f'❌ Ошибка {error_code}: {error_text}')
        except Exception as e:
            print(f'❌ Ошибка разбора сообщения об ошибке: {e}')
    
    def _cleanup(self):
        """Очистка ресурсов"""
        print('\n🧹 Очистка ресурсов...')
        
        try:
            self.client.disconnect()
            self.client.dispose()
            print('✅ Очистка завершена')
        except Exception as e:
            print(f'❌ Ошибка очистки: {e}')


def main():
    """Основная функция"""
    example = CompleteAegisExample()
    example.run()


if __name__ == "__main__":
    main()
