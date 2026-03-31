import base64
import queue
import socket
import threading
import unittest

import msgpack
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

from smoke_test import (
    AegisApiCredentials,
    AegisConn,
    FLAG_NONE,
    MSG_HANDSHAKE,
    _pack_header,
    _parse_header,
    perform_handshake,
)


def _recv_exact(sock: socket.socket, length: int) -> bytes:
    chunks = bytearray()
    while len(chunks) < length:
        chunk = sock.recv(length - len(chunks))
        if not chunk:
            raise ConnectionError("socket closed before all bytes were received")
        chunks.extend(chunk)
    return bytes(chunks)


class SmokeApiCredentialsIntegrationTests(unittest.TestCase):
    def _capture_handshake_payload(self, api_credentials: AegisApiCredentials | None) -> dict:
        captured_payloads: queue.Queue[dict] = queue.Queue(maxsize=1)
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.bind(("127.0.0.1", 0))
        server.listen(1)
        port = server.getsockname()[1]

        def serve_once() -> None:
            try:
                conn, _ = server.accept()
                with conn:
                    header_raw = _recv_exact(conn, 21)
                    header = _parse_header(header_raw)
                    payload_raw = _recv_exact(conn, header["payload_length"])
                    payload = msgpack.unpackb(payload_raw, raw=False)
                    captured_payloads.put(payload)

                    server_private = ec.generate_private_key(ec.SECP256R1())
                    server_public = server_private.public_key().public_bytes(
                        Encoding.X962,
                        PublicFormat.UncompressedPoint,
                    )
                    response_payload = msgpack.packb(
                        {
                            "Success": True,
                            "ServerPublicKey": base64.b64encode(server_public).decode("ascii"),
                        },
                        use_bin_type=True,
                    )
                    conn.sendall(
                        _pack_header(
                            FLAG_NONE,
                            MSG_HANDSHAKE,
                            header["seq_id"],
                            len(response_payload),
                        )
                        + response_payload
                    )
            finally:
                server.close()

        thread = threading.Thread(target=serve_once, daemon=True)
        thread.start()

        conn = AegisConn("127.0.0.1", port, use_tls=False, tls_verify=False, timeout=5.0)
        try:
            ok = perform_handshake(
                conn,
                "handshake-test",
                trusted_signing_public_key_b64=None,
                require_signed=False,
                api_credentials=api_credentials,
            )
            self.assertTrue(ok)
            payload = captured_payloads.get(timeout=5.0)
        finally:
            conn.close()
            thread.join(timeout=5.0)

        return payload

    def test_handshake_payload_contains_explicit_api_credentials(self) -> None:
        payload = self._capture_handshake_payload(
            AegisApiCredentials.custom(777001, "integration-app-hash"),
        )

        self.assertEqual(payload["AppId"], 777001)
        self.assertEqual(payload["AppHash"], "integration-app-hash")

    def test_handshake_payload_omits_api_credentials_when_disabled(self) -> None:
        payload = self._capture_handshake_payload(None)

        self.assertNotIn("AppId", payload)
        self.assertNotIn("AppHash", payload)


if __name__ == "__main__":
    unittest.main()