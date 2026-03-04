#!/usr/bin/env python3
"""
Базовый пример использования Aegis Python Client
"""

import sys
import os
import time

# Добавляем путь к родительской директории для импорта модуля
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from aegis_client import AegisClient, MessageContentType, ChannelType


def main():
    """Основная функция примера"""
    print("=== Aegis Python Client Example ===")
    
    # Создаем экземпляр клиента
    client = AegisClient()
    
    try:
        # Подключение к серверу
        print("Подключение к серверу...")
        client.connect('localhost', 8888)
        print("Подключено успешно!")
        
        # Слушаем входящие сообщения
        def handle_message(message):
            print(f"Получено сообщение: {message.type}")
            if message.type.name == 'MESSAGE':
                # Простой разбор текстового сообщения
                if len(message.payload) > 21:
                    text = message.payload[21:].decode('utf-8', errors='ignore')
                    print(f"Текст: {text}")
        
        def handle_disconnect():
            print("Отключено от сервера")
        
        client.messages.listen = handle_message
        client.disconnects.listen = handle_disconnect
        
        # Пример 1: Регистрация нового пользователя
        print("\n--- Регистрация пользователя ---")
        try:
            registration_response = client.register(
                'testuser',
                'test@example.com',
                'password123',
                'public_key_placeholder'
            )
            
            if registration_response.success:
                print("Пользователь успешно зарегистрирован!")
                if registration_response.user:
                    print(f"ID пользователя: {registration_response.user.id}")
                    print(f"Имя пользователя: {registration_response.user.username}")
            else:
                print(f"Ошибка регистрации: {registration_response.message}")
        except Exception as e:
            print(f"Ошибка регистрации: {e}")
        
        # Пример 2: Аутентификация
        print("\n--- Аутентификация ---")
        try:
            client.authenticate('your_auth_token_here')
            print("Аутентификация успешна!")
        except Exception as e:
            print(f"Ошибка аутентификации: {e}")
        
        # Пример 3: Поиск пользователей (требует аутентификации)
        if client.is_authenticated:
            print("\n--- Поиск пользователей ---")
            try:
                search_response = client.search_users('test', limit=10)
                
                if search_response.success:
                    print(f"Найдено пользователей: {len(search_response.users)}")
                    for user in search_response.users:
                        print(f"  - {user.username} (ID: {user.id})")
                        if user.email:
                            print(f"    Email: {user.email}")
                else:
                    print(f"Поиск не удался: {search_response.message}")
            except Exception as e:
                print(f"Ошибка поиска: {e}")
        
        # Пример 4: Работа с каналами (требует аутентификации)
        if client.is_authenticated:
            print("\n--- Работа с каналами ---")
            try:
                # Создание канала
                channel_response = client.create_channel(
                    'Test Channel',
                    description='A test channel created from Python client',
                    channel_type=ChannelType.PUBLIC
                )
                
                if channel_response.success and channel_response.channel:
                    channel = channel_response.channel
                    print("Канал создан успешно!")
                    print(f"  ID: {channel.id}")
                    print(f"  Название: {channel.name}")
                    print(f"  Тип: {channel.type}")
                    
                    # Присоединение к каналу
                    print("\nПрисоединение к каналу...")
                    join_response = client.join_channel(channel.id)
                    if join_response.success:
                        print("Присоединение к каналу успешно!")
                    
                    # Отправка сообщения в канал
                    print("\nОтправка сообщения в канал...")
                    message_response = client.send_channel_message(
                        channel.id,
                        'Hello from Python client! 🐍',
                        content_type=MessageContentType.TEXT
                    )
                    
                    if message_response.success:
                        print("Сообщение в канал отправлено успешно!")
                        if message_response.message:
                            print(f"  ID сообщения: {message_response.message.id}")
                    else:
                        print(f"Ошибка отправки: {message_response.message_text}")
                else:
                    print(f"Ошибка создания канала: {channel_response.message}")
            except Exception as e:
                print(f"Ошибка работы с каналами: {e}")
        
        # Пример 5: Приватные сообщения (требует аутентификации)
        if client.is_authenticated:
            print("\n--- Приватные сообщения ---")
            try:
                # Отправка приватного сообщения
                private_response = client.send_private_message(
                    12345,  # ID целевого пользователя
                    'Hello! This is a private message from Python client 🤖',
                    content_type=MessageContentType.TEXT
                )
                
                if private_response.success:
                    print("Приватное сообщение отправлено успешно!")
                    if private_response.message:
                        print(f"  ID сообщения: {private_response.message.id}")
                    if private_response.private_chat:
                        print(f"  ID чата: {private_response.private_chat.id}")
                else:
                    print(f"Ошибка отправки: {private_response.message_text}")
            except Exception as e:
                print(f"Ошибка отправки приватного сообщения: {e}")
        
        # Пример 6: Базовое сообщение (legacy метод)
        print("\n--- Базовое сообщение ---")
        try:
            client.send_message('Hello from Python client! (legacy method)')
            print("Базовое сообщение отправлено!")
        except Exception as e:
            print(f"Ошибка отправки базового сообщения: {e}")
        
        # Пример 7: Ping
        print("\n--- Ping ---")
        try:
            client.ping()
            print("Ping отправлен!")
        except Exception as e:
            print(f"Ошибка ping: {e}")
        
        # Поддерживаем соединение активным для демонстрации
        print("\nНажмите Ctrl+C для отключения...")
        time.sleep(30)
        
    except KeyboardInterrupt:
        print("\nПолучен сигнал прерывания...")
    except Exception as e:
        print(f"Ошибка: {e}")
    finally:
        # Очистка
        print("\nОтключение...")
        try:
            client.disconnect()
            client.dispose()
        except Exception:
            pass
        print("Готово!")


if __name__ == "__main__":
    main()
