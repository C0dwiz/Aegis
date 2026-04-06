# Redis Key Schema (Protocol Security)

## Replay Cache Keys

- `hs:v2:replay:{api_id}:{nonce_sha256}`
  - Value: `1`
  - TTL: `ProtocolSecurity:V2ReplayWindowSeconds` (default 120 sec)
  - Purpose: nonce deduplication for V2 handshake across all server instances.

## Handshake Cookie Keys (optional distributed mode)

- `hs:v2:cookie:{connection_id}`
  - Value: packed blob with `cookie`, `expected_proof_sha256`, `expires_at`
  - TTL: `ProtocolSecurity:V2HandshakeCookieTtlMs`
  - Purpose: stateless edge nodes can validate finish stage after rebalance.

## Session Salt Rotation Keys

- `sess:salt:active:{session_id}`
  - Value: JSON `{ currentSalt, rotatedAt }`
  - TTL: 24h rolling
- `sess:salt:prev:{session_id}`
  - Value: JSON `{ previousSalt, validUntil }`
  - TTL: grace period (default 30m)

## Auth Throttling Keys

- `auth:login:{remote_ip}:{username}`
  - Value: integer failed attempt count
  - TTL: 10 minutes
- `auth:register:{remote_ip}`
  - Value: integer failed attempt count
  - TTL: 1 hour

## CSRF Keys (if moved from in-memory store)

- `csrf:{session_token}`
  - Value: token hash (hex)
  - TTL: 12 hours
  - Note: store hash only, compare with constant-time equality.
