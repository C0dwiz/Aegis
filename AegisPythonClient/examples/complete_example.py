#!/usr/bin/env python3
"""Extended live demo for the current Aegis Python client."""

import os
import sys
import time
from uuid import uuid4

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from aegis_client import AegisClient, ChannelType, MessageContentType


def main():
    client = AegisClient()
    run_id = uuid4().hex[:8]
    username = f"demo_{run_id}"
    email = f"{username}@example.com"
    password = f"Pwd-{run_id}-12345"

    try:
        print("== Aegis Complete Demo ==")
        client.connect("localhost", 8888)
        print("Connected and handshake completed")

        client.messages.listen = lambda message: print(
            f"[event] {message.type.name} seq={message.sequence_id} payload={len(message.payload)}"
        )
        client.disconnects.listen = lambda: print("Disconnected from server")

        registration = client.register(username, email, password, f"demo-key-{run_id}")
        if not registration.success:
            print(f"Registration failed: {registration.message}")
            return 1

        print(f"Registered user {registration.user.username} with id {registration.user.id}")

        auth = client.login(username, password)
        print(f"Authenticated as {auth.username} ({auth.user_id})")

        search = client.search_users(username[:4], limit=5)
        print(f"Search success={search.success}, results={[user.username for user in search.users]}")

        channel = client.create_channel(
            f"demo-channel-{run_id}",
            description="Extended Python client demo",
            channel_type=ChannelType.PUBLIC,
        )
        if not channel.success:
            print(f"Channel create failed: {channel.message}")
            return 1

        print(f"Created channel {channel.channel_id}")

        channel_message = client.send_channel_message(
            channel.channel_id,
            "Hello from complete example",
            content_type=MessageContentType.TEXT,
        )
        print(
            f"Channel message success={channel_message.success}, "
            f"message_id={channel_message.message_id}, text={channel_message.message_text}"
        )

        client.ping()
        client.send_message("Legacy message path check")
        time.sleep(2)
        return 0
    except Exception as exc:
        print(f"Demo failed: {exc}")
        return 1
    finally:
        client.dispose()


if __name__ == "__main__":
    raise SystemExit(main())