#!/usr/bin/env python3
"""
Aegis Messenger - Production Smoke Test (protocol-aware)

Checks in one run:
  1) health-connect      TCP/TLS connect
  2) handshake-a         ECDH handshake + session key
  3) register-a
  4) handshake-b         ECDH handshake + session key
  5) register-b
  6) auth-a
  7) auth-b
  8) private-msg         a -> b
  9) channel-create      by a
 10) channel-msg         by a

Requirements:
  pip install msgpack cryptography

Env vars:
  AEGIS_HOST=127.0.0.1
  AEGIS_PORT=8888
  AEGIS_TLS=1
  AEGIS_TLS_NO_VERIFY=1
  AEGIS_TRUSTED_HANDSHAKE_SIGNING_PUBLIC_KEY_BASE64=<base64 raw 65-byte pubkey>
  AEGIS_REQUIRE_SIGNED_HANDSHAKE=0|1
"""

from __future__ import annotations

import argparse
import base64
import os
import socket
import ssl
import struct
import sys
import time
import traceback
import uuid
from typing import Any, Optional

try:
    import msgpack
except ImportError:
    print("ERROR: missing dependency 'msgpack'. Run: pip install msgpack", file=sys.stderr)
    sys.exit(1)

try:
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.asymmetric import ec
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
    from cryptography.hazmat.primitives.kdf.hkdf import HKDF
    from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
except ImportError:
    print("ERROR: missing dependency 'cryptography'. Run: pip install cryptography", file=sys.stderr)
    sys.exit(1)

# Protocol constants
MAGIC = 0x0AE6C5D7
VERSION_MAJOR = 1
VERSION_MINOR = 0
HEADER_SIZE = 21

FLAG_NONE = 0x00
FLAG_ENCRYPTED = 0x08

MSG_AUTH = 1
MSG_PING = 2
MSG_ACK = 4
MSG_HANDSHAKE = 6
MSG_REGISTER = 20
MSG_PRIVATE_MSG = 17
MSG_CHANNEL_CREATE = 14
MSG_CHANNEL_MSG = 13

PASS = "\\033[32mPASS\\033[0m"
FAIL = "\\033[31mFAIL\\033[0m"
SKIP = "\\033[33mSKIP\\033[0m"


def _b2i32_le(v: int) -> bytes:
    return struct.pack("<I", v)


def _pack_header(flags: int, msg_type: int, seq_id: int, payload_len: int) -> bytes:
    # Wire format: Magic(4) | VerMaj(1) | VerMin(1) | Flags(1) | Type(2) | Seq(8) | PayloadLen(4)
    return struct.pack(
        ">IBBBHQI",
        MAGIC,
        VERSION_MAJOR,
        VERSION_MINOR,
        flags,
        msg_type,
        seq_id,
        payload_len,
    )


def _parse_header(data: bytes) -> dict[str, int]:
    if len(data) != HEADER_SIZE:
        raise ValueError(f"invalid header size: {len(data)}")
    magic, vmaj, vmin, flags, msg_type, seq_id, payload_len = struct.unpack(
        ">IBBBHQI", data
    )
    return {
        "magic": magic,
        "version_major": vmaj,
        "version_minor": vmin,
        "flags": flags,
        "type": msg_type,
        "seq_id": seq_id,
        "payload_length": payload_len,
    }


def _step(name: str, ok: bool, detail: str = "") -> bool:
    mark = PASS if ok else FAIL
    suffix = f"  {detail}" if detail else ""
    print(f"  [{mark}]  {name}{suffix}")
    return ok


class AegisConn:
    def __init__(self, host: str, port: int, use_tls: bool, tls_verify: bool, timeout: float):
        raw = socket.create_connection((host, port), timeout=timeout)
        raw.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

        if use_tls:
            ctx = ssl.create_default_context()
            if not tls_verify:
                ctx.check_hostname = False
                ctx.verify_mode = ssl.CERT_NONE
            self._sock = ctx.wrap_socket(raw, server_hostname=host)
        else:
            self._sock = raw

        self._sock.settimeout(timeout)
        self._seq = 0
        self._session_key: Optional[bytes] = None

    def close(self) -> None:
        try:
            self._sock.close()
        except Exception:
            pass

    def _next_seq(self) -> int:
        self._seq += 1
        return self._seq

    def set_session_key(self, session_key: bytes) -> None:
        if len(session_key) != 32:
            raise ValueError("session key must be 32 bytes")
        self._session_key = session_key

    def send_mp(self, msg_type: int, obj: Any) -> int:
        payload = msgpack.packb(obj, use_bin_type=True)
        return self.send(msg_type, payload)

    def send(self, msg_type: int, payload: bytes = b"") -> int:
        seq = self._next_seq()
        flags = FLAG_NONE
        out_payload = payload

        if self._session_key is not None and msg_type != MSG_HANDSHAKE:
            flags = FLAG_ENCRYPTED
            out_payload = self._encrypt_payload(flags, msg_type, seq, payload)

        header = _pack_header(flags, msg_type, seq, len(out_payload))
        self._sock.sendall(header + out_payload)
        return seq

    def recv_frame(self, timeout_override: Optional[float] = None) -> tuple[dict[str, int], bytes, bytes]:
        if timeout_override is not None:
            self._sock.settimeout(timeout_override)
        header_raw = self._recv_exact(HEADER_SIZE)
        header = _parse_header(header_raw)
        payload = self._recv_exact(header["payload_length"]) if header["payload_length"] else b""
        return header, payload, header_raw

    def recv_message(self, timeout_override: Optional[float] = None) -> tuple[dict[str, int], bytes]:
        header, payload, header_raw = self.recv_frame(timeout_override)
        if (header["flags"] & FLAG_ENCRYPTED) != 0:
            if self._session_key is None:
                raise RuntimeError("received encrypted frame before handshake")
            payload = self._decrypt_payload(payload, header_raw)
            header["flags"] &= ~FLAG_ENCRYPTED
            header["payload_length"] = len(payload)
        return header, payload

    def recv_mp(self, timeout_override: Optional[float] = None) -> tuple[dict[str, int], Any]:
        header, payload = self.recv_message(timeout_override)
        obj = msgpack.unpackb(payload, raw=False) if payload else {}
        return header, obj

    def wait_response_mp(
        self,
        seq_id: int,
        expected_types: set[int],
        timeout_sec: float,
    ) -> tuple[dict[str, int], Any]:
        deadline = time.time() + timeout_sec
        last_hdr: Optional[dict[str, int]] = None
        while time.time() < deadline:
            remaining = max(0.1, deadline - time.time())
            hdr, obj = self.recv_mp(timeout_override=remaining)
            last_hdr = hdr
            if hdr["seq_id"] == seq_id and hdr["type"] in expected_types:
                return hdr, obj
        raise TimeoutError(f"timeout waiting response seq={seq_id}; last={last_hdr}")

    def _encrypt_payload(self, flags: int, msg_type: int, seq_id: int, plain_payload: bytes) -> bytes:
        assert self._session_key is not None
        nonce = os.urandom(12)
        aad = _pack_header(flags, msg_type, seq_id, 12 + len(plain_payload) + 16)
        ciphertext_and_tag = AESGCM(self._session_key).encrypt(nonce, plain_payload, aad)
        return nonce + ciphertext_and_tag

    def _decrypt_payload(self, encrypted_payload: bytes, header_raw: bytes) -> bytes:
        assert self._session_key is not None
        if len(encrypted_payload) < 28:
            raise RuntimeError("encrypted payload too short")
        nonce = encrypted_payload[:12]
        ciphertext_and_tag = encrypted_payload[12:]
        return AESGCM(self._session_key).decrypt(nonce, ciphertext_and_tag, header_raw)

    def _recv_exact(self, n: int) -> bytes:
        buf = bytearray()
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("server closed connection unexpectedly")
            buf.extend(chunk)
        return bytes(buf)


def do_register(conn: AegisConn, username: str, email: str, password: str) -> tuple[bool, int]:
    seq = conn.send_mp(
        MSG_REGISTER,
        {
            "Username": username,
            "Email": email,
            "Password": password,
            "PublicKey": "smoke-test-public-key",
        },
    )
    _, resp = conn.wait_response_mp(seq, {MSG_REGISTER, 21, MSG_ACK}, timeout_sec=10.0)
    ok = bool(resp.get("Success", False)) if isinstance(resp, dict) else False
    user = resp.get("User", {}) if isinstance(resp, dict) else {}
    user_id = int(user.get("Id", 0)) if isinstance(user, dict) else 0
    _step(f"register {username}", ok, str(resp.get("Message", "")) if isinstance(resp, dict) else "")
    return ok, user_id


def do_auth(conn: AegisConn, username: str, password: str) -> tuple[bool, int]:
    seq = conn.send_mp(
        MSG_AUTH,
        {
            "Username": username,
            "Password": password,
            "ClientInfo": "smoke-test/2.0",
        },
    )
    _, resp = conn.wait_response_mp(seq, {MSG_AUTH, MSG_ACK}, timeout_sec=10.0)
    ok = bool(resp.get("Success", False)) if isinstance(resp, dict) else False
    uid = int(resp.get("UserId", 0)) if isinstance(resp, dict) else 0
    _step(f"auth {username}", ok, str(resp.get("Error", "")) if isinstance(resp, dict) else "")
    return ok, uid


def do_private_msg(conn: AegisConn, to_user_id: int) -> bool:
    seq = conn.send_mp(
        MSG_PRIVATE_MSG,
        {
            "ToUserId": to_user_id,
            "Content": "smoke test private message",
            "ContentType": 0,
        },
    )
    hdr, resp = conn.wait_response_mp(seq, {MSG_PRIVATE_MSG, MSG_ACK}, timeout_sec=10.0)
    ok = bool(resp.get("Success", False)) if isinstance(resp, dict) else hdr["type"] == MSG_ACK
    return _step("private-msg", ok, f"type={hdr['type']}")


def do_channel_create(conn: AegisConn) -> tuple[bool, int]:
    name = f"smoke-{uuid.uuid4().hex[:8]}"
    seq = conn.send_mp(
        MSG_CHANNEL_CREATE,
        {
            "Name": name,
            "Description": "smoke test channel",
            "Type": 0,
        },
    )
    _, resp = conn.wait_response_mp(seq, {MSG_CHANNEL_CREATE, MSG_ACK}, timeout_sec=10.0)
    ok = bool(resp.get("Success", False)) if isinstance(resp, dict) else False
    channel_id = int(resp.get("ChannelId", 0)) if isinstance(resp, dict) else 0
    _step("channel-create", ok, f"id={channel_id} name={name}")
    return ok, channel_id


def do_channel_msg(conn: AegisConn, channel_id: int) -> bool:
    seq = conn.send_mp(
        MSG_CHANNEL_MSG,
        {
            "ChannelId": channel_id,
            "Content": "smoke test channel message",
            "ContentType": 0,
        },
    )
    hdr, resp = conn.wait_response_mp(seq, {MSG_CHANNEL_MSG, MSG_ACK}, timeout_sec=10.0)
    ok = bool(resp.get("Success", False)) if isinstance(resp, dict) else hdr["type"] == MSG_ACK
    return _step("channel-msg", ok, f"type={hdr['type']}")


def perform_handshake(
    conn: AegisConn,
    step_name: str,
    trusted_signing_public_key_b64: Optional[str],
    require_signed: bool,
) -> bool:
    # Build the ephemeral key once and derive session key after response
    private_key = ec.generate_private_key(ec.SECP256R1())
    client_pub = private_key.public_key().public_bytes(Encoding.X962, PublicFormat.UncompressedPoint)

    seq = conn.send_mp(
        MSG_HANDSHAKE,
        {
            "PublicKey": base64.b64encode(client_pub).decode("ascii"),
            "ClientVersion": VERSION_MAJOR * 1000 + VERSION_MINOR,
        },
    )

    _, resp = conn.wait_response_mp(seq, {MSG_HANDSHAKE}, timeout_sec=10.0)
    if not isinstance(resp, dict):
        return _step(step_name, False, "invalid handshake response")

    if not resp.get("Success", False):
        return _step(step_name, False, str(resp.get("Message", "handshake failed")))

    server_pub_b64 = str(resp.get("ServerPublicKey") or "")
    if not server_pub_b64:
        return _step(step_name, False, "missing ServerPublicKey")

    try:
        server_pub = base64.b64decode(server_pub_b64)
        if len(server_pub) != 65 or server_pub[0] != 0x04:
            return _step(step_name, False, "invalid server public key format")

        if require_signed:
            sig_b64 = str(resp.get("Signature") or "")
            if not trusted_signing_public_key_b64:
                return _step(step_name, False, "trusted signing public key is required")
            if not sig_b64:
                return _step(step_name, False, "missing handshake signature")

            signing_pub = base64.b64decode(trusted_signing_public_key_b64)
            signing_key = ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), signing_pub)
            transcript = (
                b"AEGIS-HANDSHAKE-V1"
                + _b2i32_le(len(server_pub))
                + server_pub
                + _b2i32_le(len(client_pub))
                + client_pub
            )
            signing_key.verify(base64.b64decode(sig_b64), transcript, ec.ECDSA(hashes.SHA256()))

        peer_key = ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), server_pub)
        shared_secret = private_key.exchange(ec.ECDH(), peer_key)
        session_key = HKDF(
            algorithm=hashes.SHA256(),
            length=32,
            salt=None,
            info=b"AegisKeyDerivation",
        ).derive(shared_secret)

        conn.set_session_key(session_key)
        return _step(step_name, True)
    except Exception as exc:
        return _step(step_name, False, str(exc))


def run_smoke(
    host: str,
    port: int,
    use_tls: bool,
    tls_verify: bool,
    timeout: float,
    trusted_signing_public_key_b64: Optional[str],
    require_signed_handshake: bool,
) -> bool:
    proto = "tls" if use_tls else "tcp"
    print(f"\\nAegis Smoke Test -> {proto}://{host}:{port}\\n")
    print("-" * 56)

    suffix = uuid.uuid4().hex[:6]
    user_a = f"smoke_a_{suffix}"
    user_b = f"smoke_b_{suffix}"
    email_a = f"smoke_a_{suffix}@example.com"
    email_b = f"smoke_b_{suffix}@example.com"
    pwd = "SmokeP@ss1!"

    results: list[bool] = []

    try:
        conn_a = AegisConn(host, port, use_tls, tls_verify, timeout)
        conn_b = AegisConn(host, port, use_tls, tls_verify, timeout)
    except Exception as exc:
        print(f"  [{FAIL}]  health-connect  {exc}")
        return False

    results.append(_step("health-connect", True))

    try:
        results.append(
            perform_handshake(
                conn_a,
                "handshake-a",
                trusted_signing_public_key_b64,
                require_signed_handshake,
            )
        )
        ok_reg_a, _ = do_register(conn_a, user_a, email_a, pwd)
        results.append(ok_reg_a)

        results.append(
            perform_handshake(
                conn_b,
                "handshake-b",
                trusted_signing_public_key_b64,
                require_signed_handshake,
            )
        )
        ok_reg_b, uid_b = do_register(conn_b, user_b, email_b, pwd)
        results.append(ok_reg_b)

        ok_auth_a, _ = do_auth(conn_a, user_a, pwd)
        ok_auth_b, uid_b_auth = do_auth(conn_b, user_b, pwd)
        results.append(ok_auth_a)
        results.append(ok_auth_b)

        target_uid = uid_b_auth or uid_b
        if ok_auth_a and target_uid:
            results.append(do_private_msg(conn_a, target_uid))
        else:
            print(f"  [{SKIP}]  private-msg  (auth failed or uid unknown)")
            results.append(False)

        if ok_auth_a:
            ok_chan, channel_id = do_channel_create(conn_a)
            results.append(ok_chan)
            if ok_chan and channel_id:
                results.append(do_channel_msg(conn_a, channel_id))
            else:
                print(f"  [{SKIP}]  channel-msg  (channel not created)")
                results.append(False)
        else:
            print(f"  [{SKIP}]  channel-create  (auth failed)")
            print(f"  [{SKIP}]  channel-msg  (auth failed)")
            results.extend([False, False])
    finally:
        conn_a.close()
        conn_b.close()

    passed = sum(1 for x in results if bool(x))
    total = len(results)
    print("-" * 56)
    if all(results):
        print(f"\\n  \\033[32mOK: {passed}/{total} checks passed.\\033[0m\\n")
        return True

    print(f"\\n  \\033[31mFAILED: {passed}/{total} checks passed.\\033[0m\\n")
    return False


def main() -> None:
    parser = argparse.ArgumentParser(description="Aegis protocol-aware smoke test")
    parser.add_argument("--host", default=os.getenv("AEGIS_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.getenv("AEGIS_PORT", "8888")))
    parser.add_argument(
        "--tls",
        action="store_true",
        default=os.getenv("AEGIS_TLS", "") == "1",
        help="Connect using TLS",
    )
    parser.add_argument(
        "--tls-no-verify",
        action="store_true",
        default=os.getenv("AEGIS_TLS_NO_VERIFY", "") == "1",
        help="Skip TLS certificate verification (dev only)",
    )
    parser.add_argument("--timeout", type=float, default=15.0)
    parser.add_argument(
        "--trusted-handshake-signing-public-key-base64",
        default=os.getenv("AEGIS_TRUSTED_HANDSHAKE_SIGNING_PUBLIC_KEY_BASE64", "") or None,
    )
    parser.add_argument(
        "--require-signed-handshake",
        action="store_true",
        default=os.getenv("AEGIS_REQUIRE_SIGNED_HANDSHAKE", "0") == "1",
        help="Fail if handshake signature is missing/invalid",
    )
    args = parser.parse_args()

    try:
        ok = run_smoke(
            host=args.host,
            port=args.port,
            use_tls=args.tls,
            tls_verify=not args.tls_no_verify,
            timeout=args.timeout,
            trusted_signing_public_key_b64=args.trusted_handshake_signing_public_key_base64,
            require_signed_handshake=args.require_signed_handshake,
        )
    except Exception:
        traceback.print_exc()
        ok = False

    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
