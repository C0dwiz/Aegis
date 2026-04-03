## Быстрый старт

```bash
cp .env.example .env
```

Минимум заполните в `.env`:

- `POSTGRES_PASSWORD`
- `AEGIS_Security__TotpEncryptionKey` (Base64, ровно 32 байта после декодирования)

Запуск full-стека (с MinIO и Elasticsearch):

```bash
docker compose --profile full up -d --build
```

Запуск core-стека (без MinIO и Elasticsearch):

```bash
AEGIS_MINIO_ENABLED=false AEGIS_ELASTICSEARCH_ENABLED=false docker compose up -d --build postgres redis aegis-server aegis-botapi
```

## One-off миграция TOTP секретов

```bash
export AEGIS_Security__TotpEncryptionKey="<BASE64_32_BYTE_KEY>"
docker compose --profile migration up --build aegis-reencrypt-totp
```

## Опционально: signed server handshake response

```bash
python gen_secrets.py
```

После генерации выставьте в `.env`:

- `AEGIS_REQUIRE_SIGNED_HANDSHAKE_RESPONSES=true`
- `AEGIS_HANDSHAKE_SIGNING_PRIVATE_KEY_BASE64=<generated-private-key-base64>`
