#!/usr/bin/env python3
import re
import secrets
import string
import subprocess
import base64
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


def generate_handshake_signing_keys():
    """
    Generate ECDSA P-256 signing keys using OpenSSL.
    Returns: (private_key_pkcs8_base64, public_key_raw65_base64)
    """
    private_der = subprocess.check_output(
        [
            "openssl",
            "genpkey",
            "-algorithm",
            "EC",
            "-pkeyopt",
            "ec_paramgen_curve:P-256",
            "-outform",
            "DER",
        ],
        stderr=subprocess.DEVNULL,
    )

    public_der = subprocess.check_output(
        ["openssl", "pkey", "-pubout", "-outform", "DER"],
        input=private_der,
        stderr=subprocess.DEVNULL,
    )

    # SPKI DER suffix for P-256 uncompressed point starts after 26-byte prefix.
    if len(public_der) < 91:
        raise RuntimeError("Unexpected ECDSA public key format from OpenSSL")
    raw_public = public_der[-65:]
    if raw_public[0] != 0x04:
        raise RuntimeError("Expected uncompressed EC public key")

    private_b64 = base64.b64encode(private_der).decode("ascii")
    public_b64 = base64.b64encode(raw_public).decode("ascii")
    return private_b64, public_b64

text = ENV_PATH.read_text(encoding="utf-8")


# upsert style helper: replace existing key=value, or append when key is missing

def upsert_env_var(env_text: str, key: str, value: str) -> str:
    pattern = rf"^{re.escape(key)}=.*$"
    replacement = f"{key}={value}"
    if re.search(pattern, env_text, flags=re.MULTILINE):
        return re.sub(pattern, replacement, env_text, flags=re.MULTILINE)

    if not env_text.endswith("\n"):
        env_text += "\n"
    return env_text + replacement + "\n"


handshake_private_b64 = ""
handshake_public_b64 = ""
try:
    private_b64, public_b64 = generate_handshake_signing_keys()
    handshake_private_b64 = private_b64
    handshake_public_b64 = public_b64
except Exception:
    # Keep existing values if OpenSSL is unavailable.
    pass

# Обновляем .env
text = upsert_env_var(text, "POSTGRES_PASSWORD", postgres_password)
text = upsert_env_var(text, "MINIO_ROOT_USER", minio_user)
text = upsert_env_var(text, "MINIO_ROOT_PASSWORD", minio_password)

# Чтобы connection string совпадал с POSTGRES_PASSWORD
text = upsert_env_var(
    text,
    "AEGIS_DB_CONNECTION_STRING",
    f"Host=postgres;Port=5432;Database=aegis;Username=aegis;Password={postgres_password}",
)

if handshake_private_b64:
    text = upsert_env_var(
        text,
        "AEGIS_HANDSHAKE_SIGNING_PRIVATE_KEY_BASE64",
        handshake_private_b64,
    )

if handshake_public_b64:
    text = upsert_env_var(
        text,
        "AEGIS_HANDSHAKE_SIGNING_PUBLIC_KEY_BASE64",
        handshake_public_b64,
    )

ENV_PATH.write_text(text, encoding="utf-8")
print("✅ .env обновлён безопасными секретами")
print(f"POSTGRES_PASSWORD: {postgres_password}")
print(f"MINIO_ROOT_USER:  {minio_user}")
print(f"MINIO_ROOT_PASSWORD: {minio_password}")
if handshake_public_b64:
    print("HANDSHAKE_SIGNING_KEYS: generated")
