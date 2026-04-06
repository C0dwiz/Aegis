# Aegis Secure Protocol v2

## 1. Design Goals

Aegis Secure Protocol v2 is inspired by MTProto's split architecture (transport, crypto/session, RPC layer) while simplifying implementation risks for a C#/F# codebase.

Core goals:
- Keep client onboarding simple: only `api_id` and `api_hash` are needed to initialize a client app.
- Ensure confidentiality, integrity, replay safety, and mutual freshness checks.
- Support high connection density and asynchronous processing.
- Make traffic censorship-resistant via transport polymorphism and protocol camouflage.
- Avoid stale/unused key material and overcomplicated key schedules.

## 2. MTProto Concepts Carried Forward

Based on MTProto references (`mtproto`, `description`, `auth_key`, `samples-auth_key`, `service_messages`, `service_messages_about_messages`, `serialize`):
- Multi-layer architecture: transport framing separate from message semantics.
- Explicit handshake transcript with nonces and server time.
- Strict anti-replay checks with time windows and monotonic identifiers.
- Service messages for ACK/state/resend semantics.
- TL-style binary object schema with constructor IDs and vectors.

## 3. Aegis v2 Handshake (Improved)

### 3.1 Messages
- `client_hello_v2`
  - `api_id:int32`
  - `app_hash:string`
  - `client_nonce:bytes[32]`
  - `client_ephemeral_pub:bytes[32]` (X25519)
  - `client_time_ms:int64`
  - `transport_hint:string` (`h2`, `ws`, `direct`, `obfs`)
- `server_hello_v2`
  - `server_nonce:bytes[32]`
  - `server_ephemeral_pub:bytes[32]`
  - `cookie:bytes[24]` (stateless anti-DoS token)
  - `server_time_ms:int64`
  - `key_id:int64`
  - `signature:bytes` (Ed25519 over transcript hash)
- `client_finish_v2`
  - `cookie:bytes[24]`
  - `proof:bytes[32]` (`HMAC(handshake_secret, transcript_hash || "finish")`)

### 3.2 Derivation
- ECDH: X25519(`client_ephemeral_priv`, `server_ephemeral_pub`) => `dh_secret`
- `handshake_secret = HKDF-SHA256(dh_secret, salt=client_nonce||server_nonce, info="aegis-v2/hs")`
- Session keys:
  - `c2s_key = HKDF(..., info="aegis-v2/c2s")`
  - `s2c_key = HKDF(..., info="aegis-v2/s2c")`
  - `ack_key = HKDF(..., info="aegis-v2/ack")`

### 3.3 Anti-Replay and Freshness
- Reject if `abs(client_time_ms - server_time_ms) > 90_000`.
- Track `(api_id, client_nonce)` in a bounded replay cache with TTL 2 minutes.
- Require monotonic sequence IDs per session with sliding window acceptance.
- Rotate server salt every 30 minutes, accept previous salt for grace period.

## 4. Payload Protection

- AEAD: AES-256-GCM or ChaCha20-Poly1305 (platform capability driven).
- AAD includes immutable header fields: version, type, seq, salt id, payload length.
- Nonce format: `session_nonce_prefix[8] || seq[4]` for deterministic uniqueness.
- Optional random padding in encrypted payload to reduce traffic fingerprinting.

## 5. TL-Like Serialization Profile

- All schema objects have a 32-bit constructor ID.
- Scalars: LE `int32`, LE `int64`, byte arrays with length prefix + 4-byte padding.
- Vectors: constructor ID + count + item payloads.
- Unknown constructor IDs are rejected as protocol errors.

## 6. Service Messages

- `ack(msg_ids: vector<long>)`
- `nack(msg_id, reason_code)`
- `state_req(msg_ids)` / `state_info(...)`
- `resend_req(msg_ids)`
- `bad_msg_notification(bad_msg_id, code, server_time_ms, new_salt?)`

Policy:
- ACKs are batchable up to 8192 IDs.
- Content-related messages MUST be acked.
- Duplicate msg IDs produce `msg_detailed_info`-style response, no duplicate processing.

## 7. Censorship Resistance

Transport modes:
- Native TCP (current framing).
- WebSocket over TLS.
- HTTP/2 stream mode with binary payload chunks.
- Optional obfuscated mode:
  - randomized frame padding
  - jittered packet timing
  - ALPN mimic (`h2`, `http/1.1`)
  - proxy-friendly upstream mode (SOCKS5/HTTPS CONNECT)

Deployment recommendation:
- Terminate TLS at Envoy/HAProxy with domain fronting-compatible edge setups.
- Keep protocol endpoint behind ordinary HTTPS traffic profiles.

## 8. High-Load Runtime Model

Server architecture:
- Async socket accept loop + per-connection bounded channels.
- Dedicated read/decode and execute pipelines.
- Buffer pooling (`ArrayPool<byte>`), no per-message large allocations.
- Backpressure on queue overflow (`drop oldest non-critical`, keep control frames).
- CPU pinning for crypto workers only when profiling justifies it.

## 9. Database Architecture

Primary DB: PostgreSQL
- Strong consistency for identity, sessions, app credentials, ACL data.
- Native partitioning and mature indexing.

Hot-path cache: Redis
- Session tokens / CSRF metadata / auth throttling counters.
- Presence, short-lived replay windows, request dedupe markers.

Suggested tables and indexes:
- `app_credentials(app_id PK, owner_id, app_hash, is_active, created_at, last_used_at)`
- Indexes:
  - unique `(app_hash)`
  - `(owner_id, is_active, created_at desc)`
  - partial `(is_active) where is_active = true`
- `sessions(session_token PK, user_id, is_active, expires_at, last_activity_at)`
  - `(user_id, is_active)`
  - `(expires_at)`

Partitioning:
- Message/event tables by month (`created_at`) for retention and vacuum control.
- Optionally hash-subpartition by `chat_id` for high fanout workloads.

## 10. Website for api_id/api_hash

Stack: ASP.NET Core Minimal API + static portal UI
- Login/register (local credentials and optional OAuth providers).
- Create App form fields:
  - `App title`
  - `App description`
  - `App platform` (`android`, `desktop`, `web`)
  - `App URL` optional
- Generate:
  - `api_id`: DB identity integer
  - `api_hash`: CSPRNG 32 bytes, hex-encoded 64 chars
- Manage apps:
  - list apps
  - reveal hash (owner + CSRF-protected endpoint)
  - revoke app credentials

Security controls:
- Password hashing: BCrypt with cost >= 12.
- CSRF: double-submit token bound to session token.
- XSS: output escaping + strict CSP.
- Brute force: per-IP and per-account throttles with Redis/distributed cache.
- Constant-time hash compare for credential validation.

## 11. Key Hygiene (remove unnecessary keys)

Rules:
- Keep only:
  - long-lived server identity keys (Ed25519 signing key)
  - short-lived handshake ephemeral keys
  - derived session traffic keys
- Do not persist raw ephemeral secrets.
- Zero memory for temporary key material after derivation.
- Mark all key classes explicitly: `Permanent`, `Ephemeral`, `Derived`, `Transient`.

Static analysis and checks:
- Roslyn analyzers for dead constants and unused secret fields.
- CI rule: fail on unused `*Key`, `*Secret`, `*Nonce` symbols.
- Secret scanning in CI (gitleaks/trufflehog).

## 12. CVE Threat Model and Mitigations

Typical risks and controls:
- Replay attacks (CWE-294)
  - nonce cache + seq window + timestamp checks.
- Handshake downgrade/confusion
  - transcript hash signed by server identity key, strict version pinning.
- Weak RNG (CWE-338)
  - `RandomNumberGenerator.Fill` only.
- Timing oracle in secret comparison (CWE-208)
  - `CryptographicOperations.FixedTimeEquals`.
- Resource exhaustion / DoS
  - stateless cookie in handshake + bounded channels + rate limits.
- Memory disclosure
  - key zeroization and minimized secret lifetime.
- Supply-chain CVEs
  - Dependabot, `dotnet list package --vulnerable`, SBOM, pinned versions.

Operational controls:
- Mandatory SAST + dependency scanning in CI.
- Protocol fuzz tests (decode/parse and state machine transitions).
- Periodic third-party crypto review.

## 13. Hybrid C#/F# split

- C#:
  - transport I/O
  - binary codec
  - crypto primitives
  - endpoint plumbing
- F#:
  - handshake state machine
  - domain invariants
  - policy decisions (accept/reject transitions)

This split keeps low-level performance-critical code in C# and formal transition logic in F#.
