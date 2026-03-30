# Aegis Protocol Overview

`Aegis Protocol` is a binary messaging protocol for persistent chat connections.

## Transport

- TCP connection per client.
- Optional TLS (`Tls:Enabled=true`) at transport level.
- Optional XOR transport masking (`Server:EnableTransportMasking`).

## Session Lifecycle

1. Client connects.
2. Client sends `Handshake (6)` with ECDH public key.
3. Server replies with its ECDH public key and optional ECDSA signature.
4. Both sides derive a 32-byte session key via HKDF-SHA256 (`AegisKeyDerivation`).
5. Client authenticates with `Auth (1)`.
6. Client uses app methods (messages, channels, profile, receipts).

Note: `Register (20)` is allowed before handshake/auth.

## Frame Layout

Header is always 21 bytes, big-endian:

- `Magic` (4)
- `VersionMajor` (1)
- `VersionMinor` (1)
- `Flags` (1)
- `Type` (2)
- `SequenceId` (8)
- `PayloadLength` (4)
- `Payload` (N)

## Security Model

- ECDH P-256 for ephemeral key exchange.
- AES-256-GCM for payload encryption after handshake.
- AAD = frame header bytes.
- Optional handshake signature verification (`ECDSA_P256_SHA256`).
- Anti-replay via sequence window.

## Reliability

- `Ack (4)`, `Nack (7)`, `RetransmitRequest (8)`.
- `Ack` payload = `SequenceId(8, big-endian)` + `AckStatus(1)`.

## Message Catalog

See strict wire-level table and payload contracts in `wire-spec.md`.

## Source Of Truth

- `src/Aegis.Protocol/MessageType.cs`
- `src/Aegis.Protocol/MessageEncoder.cs`
- `src/Aegis.Handlers/HandshakeHandler.cs`
- `src/Aegis.Server/Program.cs`
- `src/Aegis.Server/ServerMessageSender.cs`

## Generate MessageType Table

Use:

```bash
python tools/generate_message_type_table.py
```
