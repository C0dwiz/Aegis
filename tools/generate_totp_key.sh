#!/usr/bin/env bash
set -euo pipefail

key="$(openssl rand -base64 32)"

echo "Generated Security:TotpEncryptionKey (Base64, 32-byte key):"
echo "$key"
echo
echo "Server env (uses AEGIS_ prefix in Program.cs):"
echo "export AEGIS_Security__TotpEncryptionKey=\"$key\""
echo
echo "BotApi env:"
echo "export Security__TotpEncryptionKey=\"$key\""
