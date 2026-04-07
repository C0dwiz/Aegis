#!/usr/bin/env bash
# -----------------------------------------------------------------------
# Aegis Server – Quick Configuration Script
# Usage:
#   ./configure-server.sh [OPTIONS]
#   Without arguments, runs interactively (asks for each value).
#
# Options (any can be combined with interactive mode):
#   --env <dev|prod>          Select appsettings target (default: prod)
#   --port <N>                TCP port (default: 8888)
#   --max-connections <N>     Max simultaneous TCP connections
#   --idle-timeout <seconds>  Close idle connections after N seconds
#   --masking <on|off>        Toggle transport XOR masking
#   --masking-key <base64>    Base64 masking key (leave empty to auto-generate)
#   --db <conn_str>           PostgreSQL connection string
#   --redis <conn_str>        Redis connection string
#   --auth-rate <N>           Max auth attempts per minute per connection
#   --msg-rate <N>            Max messages per second per connection
#   --conn-rate <N>           Max connections per minute per IP
#   --offline-ttl <seconds>   Undelivered message TTL (0 = keep forever)
#   --offline-queue <N>       Max queued offline messages per user
#   --offline-interval <seconds> Offline delivery loop interval
#   --session-ttl <days>      Auth session token lifetime in days (DB)
#   --handshake-ttl <ms>      V2 handshake cookie TTL in milliseconds
#   --replay-window <seconds> V2 replay dedup window in seconds
#   --clock-skew <ms>         Allowed client clock skew in milliseconds
#   --require-app-creds <on|off> Require AppId/AppHash credentials
#   --tls <on|off>            Enable TLS on TCP transport
#   --tls-cert <path>         Path to .pfx certificate
#   --tls-pass <password>     PFX certificate password
#   --metrics-port <N>        Prometheus /metrics HTTP port (default: 9091)
#   --log-level <level>       Logging level (Verbose/Debug/Information/Warning/Error)
#   -y / --yes                Non-interactive: apply without confirmation
#   -h / --help               Show this help
# -----------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/src/Aegis.Server"

# ── Defaults ────────────────────────────────────────────────────────────
ENV="prod"
NON_INTERACTIVE=false

PORT=""
MAX_CONNECTIONS=""
IDLE_TIMEOUT=""
MASKING=""
MASKING_KEY=""
DB_CONN=""
REDIS_CONN=""
AUTH_RATE=""
MSG_RATE=""
CONN_RATE=""
OFFLINE_TTL=""
OFFLINE_QUEUE=""
OFFLINE_INTERVAL=""
SESSION_TTL_DAYS=""
HANDSHAKE_TTL=""
REPLAY_WINDOW=""
CLOCK_SKEW=""
REQUIRE_APP_CREDS=""
TLS_ENABLED=""
TLS_CERT=""
TLS_PASS=""
METRICS_PORT=""
LOG_LEVEL=""

# ── Colours ─────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
info()    { echo -e "${CYAN}[info]${NC}  $*"; }
ok()      { echo -e "${GREEN}[ok]${NC}    $*"; }
warn()    { echo -e "${YELLOW}[warn]${NC}  $*"; }
err()     { echo -e "${RED}[err]${NC}   $*" >&2; }

# ── Argument parsing ────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    --env)              ENV="$2";              shift 2 ;;
    --port)             PORT="$2";             shift 2 ;;
    --max-connections)  MAX_CONNECTIONS="$2";  shift 2 ;;
    --idle-timeout)     IDLE_TIMEOUT="$2";     shift 2 ;;
    --masking)          MASKING="$2";          shift 2 ;;
    --masking-key)      MASKING_KEY="$2";      shift 2 ;;
    --db)               DB_CONN="$2";          shift 2 ;;
    --redis)            REDIS_CONN="$2";       shift 2 ;;
    --auth-rate)        AUTH_RATE="$2";        shift 2 ;;
    --msg-rate)         MSG_RATE="$2";         shift 2 ;;
    --conn-rate)        CONN_RATE="$2";        shift 2 ;;
    --offline-ttl)      OFFLINE_TTL="$2";      shift 2 ;;
    --offline-queue)    OFFLINE_QUEUE="$2";    shift 2 ;;
    --offline-interval) OFFLINE_INTERVAL="$2"; shift 2 ;;
    --session-ttl)      SESSION_TTL_DAYS="$2"; shift 2 ;;
    --handshake-ttl)    HANDSHAKE_TTL="$2";    shift 2 ;;
    --replay-window)    REPLAY_WINDOW="$2";    shift 2 ;;
    --clock-skew)       CLOCK_SKEW="$2";       shift 2 ;;
    --require-app-creds) REQUIRE_APP_CREDS="$2"; shift 2 ;;
    --tls)              TLS_ENABLED="$2";      shift 2 ;;
    --tls-cert)         TLS_CERT="$2";         shift 2 ;;
    --tls-pass)         TLS_PASS="$2";         shift 2 ;;
    --metrics-port)     METRICS_PORT="$2";     shift 2 ;;
    --log-level)        LOG_LEVEL="$2";        shift 2 ;;
    -y|--yes)           NON_INTERACTIVE=true;  shift   ;;
    -h|--help)
      sed -n '/^# Usage/,/^# ------/p' "$0" | head -n -1
      exit 0 ;;
    *) err "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Resolve target appsettings file ─────────────────────────────────────
if [[ "$ENV" == "dev" || "$ENV" == "development" ]]; then
  SETTINGS="$SERVER_DIR/appsettings.Development.json"
else
  SETTINGS="$SERVER_DIR/appsettings.json"
fi

if [[ ! -f "$SETTINGS" ]]; then
  err "Settings file not found: $SETTINGS"
  exit 1
fi

# ── Check for jq ────────────────────────────────────────────────────────
if ! command -v jq &>/dev/null; then
  err "'jq' is required but not installed.  Install it with:  sudo apt install jq"
  exit 1
fi

# ── Helper: read current value from JSON ────────────────────────────────
jq_get() { jq -r "$1 // empty" "$SETTINGS"; }

# ── Interactive prompt helper ────────────────────────────────────────────
# ask <var_ref> <prompt> <current_value>
ask() {
  local -n _ref="$1"
  local prompt="$2"
  local current="$3"
  if [[ -n "${_ref:-}" ]]; then
    return   # already set via CLI
  fi
  if "$NON_INTERACTIVE"; then
    _ref="$current"
    return
  fi
  read -rp "$(echo -e "${CYAN}${prompt}${NC} [${current}]: ")" _input
  _ref="${_input:-$current}"
}

# ── Read current values from file ────────────────────────────────────────
CUR_PORT=$(jq_get '.Server.Port')
CUR_MAX=$(jq_get '.Server.MaxConnections')
CUR_IDLE=$(jq_get '.Server.IdleTimeoutSeconds')
CUR_MASKING=$(jq_get '.Server.EnableTransportMasking')
CUR_MASKING_KEY=$(jq_get '.Server.TransportMaskingKey')
CUR_METRICS=$(jq_get '.Server.MetricsPort // "9091"')
CUR_DB=$(jq_get '.Database.ConnectionString')
CUR_REDIS=$(jq_get '.Redis.ConnectionString')
CUR_AUTH_RATE=$(jq_get '.RateLimit.MaxAuthAttemptsPerMinute')
CUR_MSG_RATE=$(jq_get '.RateLimit.MaxMessagesPerSecond')
CUR_CONN_RATE=$(jq_get '.RateLimit.MaxConnectionsPerIP')
CUR_OFFLINE_TTL=$(jq_get '.OfflineMessage.MessageTtlSeconds')
CUR_OFFLINE_QUEUE=$(jq_get '.OfflineMessage.MaxQueuedPerUser')
CUR_OFFLINE_INTERVAL=$(jq_get '.OfflineMessage.DeliveryIntervalSeconds')
CUR_HANDSHAKE_TTL=$(jq_get '.ProtocolSecurity.V2HandshakeCookieTtlMs')
CUR_REPLAY=$(jq_get '.ProtocolSecurity.V2ReplayWindowSeconds')
CUR_SKEW=$(jq_get '.ProtocolSecurity.V2HandshakeClockSkewMs')
CUR_APP_CREDS=$(jq_get '.ProtocolSecurity.RequireAppCredentials')
CUR_TLS=$(jq_get '.Tls.Enabled')
CUR_TLS_CERT=$(jq_get '.Tls.CertificatePath')
CUR_LOG=$(jq_get '.AegisLogging.MinimumLevel')

echo
echo -e "${CYAN}━━━━━━━━ Aegis Server Configuration ━━━━━━━━${NC}"
echo -e "  Target: ${YELLOW}${SETTINGS}${NC}"
echo

# ── Interactive prompts ──────────────────────────────────────────────────
echo -e "${CYAN}── Server ──────────────────────────────────────────${NC}"
ask PORT             "TCP port"                        "$CUR_PORT"
ask MAX_CONNECTIONS  "Max connections"                 "$CUR_MAX"
ask IDLE_TIMEOUT     "Idle timeout (seconds)"          "$CUR_IDLE"
ask MASKING          "Transport XOR masking (true/false)" "$CUR_MASKING"
if [[ "${MASKING,,}" == "true" || "${MASKING,,}" == "on" ]]; then
  MASKING="true"
  if [[ -z "$MASKING_KEY" && "$CUR_MASKING_KEY" == "" ]]; then
    MASKING_KEY=$(openssl rand -base64 32)
    info "Auto-generated masking key"
  else
    ask MASKING_KEY "Transport masking key (base64, empty to keep current)" "$CUR_MASKING_KEY"
  fi
else
  MASKING="false"
  MASKING_KEY="${MASKING_KEY:-$CUR_MASKING_KEY}"
fi
ask METRICS_PORT "Prometheus metrics port" "$CUR_METRICS"

echo -e "${CYAN}── Database & Redis ────────────────────────────────${NC}"
ask DB_CONN    "PostgreSQL connection string" "$CUR_DB"
ask REDIS_CONN "Redis connection string"      "$CUR_REDIS"

echo -e "${CYAN}── Rate limits ──────────────────────────────────────${NC}"
ask AUTH_RATE   "Max auth attempts per minute (per connection)" "$CUR_AUTH_RATE"
ask MSG_RATE    "Max messages per second (per connection)"      "$CUR_MSG_RATE"
ask CONN_RATE   "Max connections per minute (per IP)"           "$CUR_CONN_RATE"

echo -e "${CYAN}── Offline message TTL ──────────────────────────────${NC}"
ask OFFLINE_TTL      "Offline message TTL in seconds (0 = keep forever, 604800 = 7 days)" "$CUR_OFFLINE_TTL"
ask OFFLINE_QUEUE    "Max offline messages queued per user"   "$CUR_OFFLINE_QUEUE"
ask OFFLINE_INTERVAL "Delivery loop interval (seconds)"       "$CUR_OFFLINE_INTERVAL"

echo -e "${CYAN}── Protocol security / Handshake TTL ───────────────${NC}"
ask HANDSHAKE_TTL    "V2 handshake cookie TTL (ms, default 60000)"  "$CUR_HANDSHAKE_TTL"
ask REPLAY_WINDOW    "V2 replay dedup window (seconds, default 120)" "$CUR_REPLAY"
ask CLOCK_SKEW       "Client clock skew tolerance (ms, default 90000)" "$CUR_SKEW"
ask REQUIRE_APP_CREDS "Require AppId/AppHash credentials (true/false)" "$CUR_APP_CREDS"
[[ "${REQUIRE_APP_CREDS,,}" == "on"  ]] && REQUIRE_APP_CREDS="true"
[[ "${REQUIRE_APP_CREDS,,}" == "off" ]] && REQUIRE_APP_CREDS="false"

echo -e "${CYAN}── TLS ──────────────────────────────────────────────${NC}"
ask TLS_ENABLED "Enable TLS on TCP transport (true/false)" "$CUR_TLS"
[[ "${TLS_ENABLED,,}" == "on"  ]] && TLS_ENABLED="true"
[[ "${TLS_ENABLED,,}" == "off" ]] && TLS_ENABLED="false"
if [[ "${TLS_ENABLED,,}" == "true" ]]; then
  ask TLS_CERT "Path to PFX certificate" "$CUR_TLS_CERT"
  if [[ -z "$TLS_PASS" ]] && ! "$NON_INTERACTIVE"; then
    read -rsp "$(echo -e "${CYAN}PFX certificate password${NC} (hidden): ")" TLS_PASS
    echo
  fi
fi

echo -e "${CYAN}── Logging ──────────────────────────────────────────${NC}"
ask LOG_LEVEL "Log level (Verbose/Debug/Information/Warning/Error)" "$CUR_LOG"

# ── Preview & confirm ────────────────────────────────────────────────────
echo
echo -e "${CYAN}── Summary of changes ───────────────────────────────${NC}"
echo "  Port:                $PORT"
echo "  MaxConnections:      $MAX_CONNECTIONS"
echo "  IdleTimeout:         ${IDLE_TIMEOUT}s"
echo "  TransportMasking:    $MASKING"
echo "  MetricsPort:         $METRICS_PORT"
echo "  DB:                  ${DB_CONN:0:40}..."
echo "  Redis:               $REDIS_CONN"
echo "  Auth rate limit:     ${AUTH_RATE}/min"
echo "  Msg rate limit:      ${MSG_RATE}/sec"
echo "  Conn rate limit:     ${CONN_RATE}/min per IP"
echo "  Offline msg TTL:     ${OFFLINE_TTL}s"
echo "  Offline queue:       $OFFLINE_QUEUE msgs/user"
echo "  Delivery interval:   ${OFFLINE_INTERVAL}s"
echo "  Handshake cookie TTL:${HANDSHAKE_TTL}ms"
echo "  Replay window:       ${REPLAY_WINDOW}s"
echo "  Clock skew:          ${CLOCK_SKEW}ms"
echo "  Require app creds:   $REQUIRE_APP_CREDS"
echo "  TLS:                 $TLS_ENABLED"
[[ "${TLS_ENABLED,,}" == "true" ]] && echo "  TLS cert:            $TLS_CERT"
echo "  Log level:           $LOG_LEVEL"
echo

if ! "$NON_INTERACTIVE"; then
  read -rp "$(echo -e "${YELLOW}Apply these settings? [y/N]: ${NC}")" CONFIRM
  [[ "${CONFIRM,,}" != "y" ]] && { info "Aborted – no changes made."; exit 0; }
fi

# ── Backup & apply ───────────────────────────────────────────────────────
BACKUP="${SETTINGS}.bak.$(date +%Y%m%d%H%M%S)"
cp "$SETTINGS" "$BACKUP"
info "Backup saved to: $BACKUP"

# Convert on/off aliases to true/false for JSON
bool_val() {
  local v="${1,,}"
  [[ "$v" == "on" || "$v" == "true" || "$v" == "1" ]] && echo "true" || echo "false"
}

MASKING_BOOL=$(bool_val "$MASKING")
APP_CREDS_BOOL=$(bool_val "$REQUIRE_APP_CREDS")
TLS_BOOL=$(bool_val "${TLS_ENABLED:-false}")

# Apply with jq
jq \
  --argjson port            "$PORT" \
  --argjson maxconn         "$MAX_CONNECTIONS" \
  --argjson idle            "$IDLE_TIMEOUT" \
  --argjson masking         "$MASKING_BOOL" \
  --arg     maskingKey      "$MASKING_KEY" \
  --argjson metricsPort     "$METRICS_PORT" \
  --arg     db              "$DB_CONN" \
  --arg     redis           "$REDIS_CONN" \
  --argjson authRate        "$AUTH_RATE" \
  --argjson msgRate         "$MSG_RATE" \
  --argjson connRate        "$CONN_RATE" \
  --argjson offlineTtl      "$OFFLINE_TTL" \
  --argjson offlineQueue    "$OFFLINE_QUEUE" \
  --argjson offlineInterval "$OFFLINE_INTERVAL" \
  --argjson handshakeTtl    "$HANDSHAKE_TTL" \
  --argjson replayWindow    "$REPLAY_WINDOW" \
  --argjson clockSkew       "$CLOCK_SKEW" \
  --argjson appCreds        "$APP_CREDS_BOOL" \
  --argjson tls             "$TLS_BOOL" \
  --arg     tlsCert         "${TLS_CERT:-}" \
  --arg     logLevel        "$LOG_LEVEL" \
  '
  .Server.Port                               = $port          |
  .Server.MaxConnections                     = $maxconn        |
  .Server.IdleTimeoutSeconds                 = $idle           |
  .Server.EnableTransportMasking             = $masking        |
  .Server.TransportMaskingKey                = $maskingKey     |
  .Server.MetricsPort                        = $metricsPort    |
  .Database.ConnectionString                 = $db             |
  .Redis.ConnectionString                    = $redis          |
  .RateLimit.MaxAuthAttemptsPerMinute        = $authRate       |
  .RateLimit.MaxMessagesPerSecond            = $msgRate        |
  .RateLimit.MaxConnectionsPerIP             = $connRate       |
  .OfflineMessage.MessageTtlSeconds          = $offlineTtl     |
  .OfflineMessage.MaxQueuedPerUser           = $offlineQueue   |
  .OfflineMessage.DeliveryIntervalSeconds    = $offlineInterval|
  .ProtocolSecurity.V2HandshakeCookieTtlMs   = $handshakeTtl  |
  .ProtocolSecurity.V2ReplayWindowSeconds    = $replayWindow   |
  .ProtocolSecurity.V2HandshakeClockSkewMs   = $clockSkew      |
  .ProtocolSecurity.RequireAppCredentials    = $appCreds       |
  .Tls.Enabled                               = $tls            |
  .Tls.CertificatePath                       = $tlsCert        |
  .AegisLogging.MinimumLevel                 = $logLevel
  ' "$BACKUP" > "$SETTINGS"

# Append TLS password via env var reminder if set
if [[ -n "${TLS_PASS:-}" ]]; then
  warn "TLS password NOT written to file. Set it via environment variable:"
  warn "  export AEGIS_TLS__CERTIFICATEPASSWORD='<your_password>'"
fi

echo
ok "Configuration written to: $SETTINGS"
echo

# ── Optional: restart hint ───────────────────────────────────────────────
if command -v systemctl &>/dev/null && systemctl is-active --quiet aegis-server 2>/dev/null; then
  if ! "$NON_INTERACTIVE"; then
    read -rp "$(echo -e "${YELLOW}Restart aegis-server systemd service now? [y/N]: ${NC}")" RESTART_CONFIRM
    [[ "${RESTART_CONFIRM,,}" == "y" ]] && systemctl restart aegis-server && ok "Service restarted."
  fi
elif [[ -f "$SCRIPT_DIR/docker-compose.yml" ]]; then
  info "To apply changes with Docker Compose run:"
  info "  docker compose restart aegis-server"
fi
