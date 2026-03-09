#!/usr/bin/env python3
"""Basic smoke test for the Aegis Python client against a live server."""

import os
import sys
import time
from uuid import uuid4

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from aegis_client import AegisClient, ChannelType, MessageContentType


def main():
    print("=== Aegis Protocol Smoke Test ===")

    client = AegisClient()
    run_id = uuid4().hex[:8]
    username = f"py_{run_id}"
    email = f"{username}@example.com"
    password = f"Pwd-{run_id}-12345"
    public_key = f"python-client-{run_id}"

    try:
        print("Подключение к серверу...")
        client.connect("localhost", 8888)
        print("Подключено успешно, handshake завершен.")

        def handle_message(message):
            print(f"[event/raw] type={message.type.name} seq={message.sequence_id}")

        def handle_private_event(event):
            print(
                "[event/private] "
                f"id={event.id} from={event.from_user_id} "
                f"content={event.content!r}"
            )

        def handle_channel_event(event):
            print(
                "[event/channel] "
                f"id={event.id} channel={event.channel_id} "
                f"content={event.content!r}"
            )

        def handle_disconnect():
            print("Отключено от сервера")

        client.messages.listen = handle_message
        client.add_private_message_event_listener(handle_private_event)
        client.add_channel_message_event_listener(handle_channel_event)
        client.disconnects.listen = handle_disconnect

        print("\n--- Регистрация пользователя ---")
        registration_response = client.register(username, email, password, public_key)
        if not registration_response.success:
            print(f"Ошибка регистрации: {registration_response.message}")
            return 1

        print("Пользователь успешно зарегистрирован!")
        if registration_response.user:
            print(f"ID пользователя: {registration_response.user.id}")
            print(f"Имя пользователя: {registration_response.user.username}")

        print("\n--- Аутентификация ---")
        auth_response = client.login(username, password)
        print("Аутентификация успешна!")
        print(f"UserId: {auth_response.user_id}, Username: {auth_response.username}")

        print("\n--- Поиск пользователей ---")
        search_response = client.search_users(username[:4], limit=10)
        if search_response.success:
            print(f"Найдено пользователей: {len(search_response.users)}")
            for user in search_response.users:
                print(f"  - {user.username} (ID: {user.id})")
        else:
            print(f"Поиск не удался: {search_response.message}")
            return 1

        print("\n--- Работа с каналами ---")
        channel_response = client.create_channel(
            f"Test Channel {run_id}",
            description="Protocol smoke-test channel",
            channel_type=ChannelType.PUBLIC,
        )
        if not channel_response.success or not channel_response.channel_id:
            print(f"Ошибка создания канала: {channel_response.message}")
            return 1

        print("Канал создан успешно!")
        print(f"  ID: {channel_response.channel_id}")

        print("\n--- get_chat_list ---")
        chat_list = client.get_chat_list()
        print(f"ChatList success: {chat_list.success}, chats: {len(chat_list.chats)}")
        for chat in chat_list.chats[:5]:
            print(
                f"  - chatId={chat.chat_id} type={chat.type} "
                f"title={chat.title!r} unread={chat.unread_count}"
            )

        message_response = client.send_channel_message(
            channel_response.channel_id,
            "Hello from Python client!",
            content_type=MessageContentType.TEXT,
        )
        if not message_response.success:
            print(f"Ошибка отправки сообщения в канал: {message_response.message_text}")
            return 1

        print("Сообщение в канал отправлено успешно!")
        print(f"  ID сообщения: {message_response.message_id}")

        print("\n--- Базовое сообщение ---")
        client.send_message("Hello from Python client! (legacy method)")
        print("Базовое сообщение отправлено!")

        print("\n--- Private event demo (message to self) ---")
        if auth_response.user_id > 0:
            pm = client.send_private_message(
                auth_response.user_id,
                "Self private message for event subscription demo",
                content_type=MessageContentType.TEXT,
            )
            print(f"Private send success: {pm.success}, id={pm.message_id}")

        print("\n--- Ping ---")
        client.ping()
        print("Ping отправлен!")

        print("\nSmoke test завершен. Ждем 2 секунды для входящих событий...")
        time.sleep(2)
        return 0
    except KeyboardInterrupt:
        print("\nПолучен сигнал прерывания...")
        return 130
    except Exception as exc:
        print(f"Ошибка smoke test: {exc}")
        return 1
    finally:
        print("\nОтключение...")
        try:
            client.disconnect()
            client.dispose()
        except Exception:
            pass
        print("Готово!")


if __name__ == "__main__":
    raise SystemExit(main())