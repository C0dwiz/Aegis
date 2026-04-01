как запускать:

cp .env.example .env
заполнить реальные значения в .env
docker compose up -d --build

если хотите дополнительно подписывать server handshake response, включите `AEGIS_REQUIRE_SIGNED_HANDSHAKE_RESPONSES=true` и сгенерируйте ключ через `python gen_secrets.py`
