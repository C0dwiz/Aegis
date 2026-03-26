#!/usr/bin/env python3
import re
import secrets
import string
from pathlib import Path

ENV_PATH = Path(".env")

def gen(chars: str, length: int) -> str:
    return "".join(secrets.choice(chars) for _ in range(length))

# Наборы символов
ALNUM = string.ascii_letters + string.digits
LOWER_ALNUM = string.ascii_lowercase + string.digits
SAFE_PWD = string.ascii_letters + string.digits + "_-"

# Генерация значений
postgres_password = gen(SAFE_PWD, 40)
minio_user = gen(LOWER_ALNUM, 20)
minio_password = gen(SAFE_PWD, 48)

text = ENV_PATH.read_text(encoding="utf-8")

# Обновляем .env
text = re.sub(r"^POSTGRES_PASSWORD=.*$", f"POSTGRES_PASSWORD={postgres_password}", text, flags=re.MULTILINE)
text = re.sub(r"^MINIO_ROOT_USER=.*$", f"MINIO_ROOT_USER={minio_user}", text, flags=re.MULTILINE)
text = re.sub(r"^MINIO_ROOT_PASSWORD=.*$", f"MINIO_ROOT_PASSWORD={minio_password}", text, flags=re.MULTILINE)

# Чтобы connection string совпадал с POSTGRES_PASSWORD
text = re.sub(
    r"^AEGIS_DB_CONNECTION_STRING=.*$",
    f"AEGIS_DB_CONNECTION_STRING=Host=postgres;Port=5432;Database=aegis;Username=aegis;Password={postgres_password}",
    text,
    flags=re.MULTILINE,
)

ENV_PATH.write_text(text, encoding="utf-8")
print("✅ .env обновлён безопасными секретами")
print(f"POSTGRES_PASSWORD: {postgres_password}")
print(f"MINIO_ROOT_USER:  {minio_user}")
print(f"MINIO_ROOT_PASSWORD: {minio_password}")