#!/usr/bin/env bash
set -euo pipefail

wait_for_port() {
  local host="$1"
  local port="$2"
  local name="$3"
  local timeout_seconds="${4:-30}"

  local end=$((SECONDS + timeout_seconds))
  while (( SECONDS < end )); do
    if bash -lc ": > /dev/tcp/${host}/${port}" >/dev/null 2>&1; then
      echo "${name} is reachable at ${host}:${port}"
      return 0
    fi
    sleep 1
  done

  echo "${name} is not reachable at ${host}:${port}"
  return 1
}

if [[ -z "${AEGIS_Security__TotpEncryptionKey:-}" ]]; then
  echo "AEGIS_Security__TotpEncryptionKey is not set"
  echo "Example: export AEGIS_Security__TotpEncryptionKey=\"$(openssl rand -base64 32)\""
  exit 1
fi

if ! key_bytes=$(printf '%s' "${AEGIS_Security__TotpEncryptionKey}" | base64 --decode 2>/dev/null | wc -c); then
  echo "AEGIS_Security__TotpEncryptionKey must be valid Base64"
  exit 1
fi

if [[ "${key_bytes}" -ne 32 ]]; then
  echo "AEGIS_Security__TotpEncryptionKey must decode to exactly 32 bytes (got ${key_bytes})"
  exit 1
fi

# For one-off migration we only need Postgres and Redis; disable optional dependency checks unless explicitly overridden.
export AEGIS_Minio__Enabled="${AEGIS_Minio__Enabled:-false}"
export AEGIS_Elasticsearch__Enabled="${AEGIS_Elasticsearch__Enabled:-false}"

db_host="localhost"
db_port="5432"
if [[ -n "${AEGIS_Database__ConnectionString:-}" ]]; then
  host_part=$(printf '%s' "${AEGIS_Database__ConnectionString}" | sed -n 's/.*Host=\([^;]*\).*/\1/p')
  port_part=$(printf '%s' "${AEGIS_Database__ConnectionString}" | sed -n 's/.*Port=\([^;]*\).*/\1/p')
  if [[ -n "${host_part}" ]]; then
    db_host="${host_part}"
  fi
  if [[ -n "${port_part}" ]]; then
    db_port="${port_part}"
  fi
fi

redis_host="localhost"
redis_port="6379"
if [[ -n "${AEGIS_Redis__ConnectionString:-}" ]]; then
  redis_host="${AEGIS_Redis__ConnectionString%%:*}"
  redis_port="${AEGIS_Redis__ConnectionString##*:}"
fi

wait_for_port "${db_host}" "${db_port}" "PostgreSQL" 45
wait_for_port "${redis_host}" "${redis_port}" "Redis" 45

cd "$(dirname "$0")/.."
dotnet run --project src/Aegis.Server/Aegis.Server.csproj -- --reencrypt-totp-secrets
