"""Cryptographic helpers for the Aegis Python client."""

from __future__ import annotations

import base64
import hashlib
import hmac
from secrets import compare_digest

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.kdf.hkdf import HKDF
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat, load_der_public_key

from .exceptions import ProtocolError


class AegisSessionCrypto:
    """Implements the same ECDH + HKDF flow that the Aegis server uses."""

    def __init__(self) -> None:
        self._private_key = ec.generate_private_key(ec.SECP256R1())

    @property
    def public_key_bytes(self) -> bytes:
        return self._private_key.public_key().public_bytes(
            encoding=Encoding.DER,
            format=PublicFormat.SubjectPublicKeyInfo,
        )

    @property
    def public_key_base64(self) -> str:
        return base64.b64encode(self.public_key_bytes).decode("ascii")

    def derive_keys(self, server_public_key_base64: str) -> tuple[bytes, bytes]:
        try:
            server_public_key = load_der_public_key(base64.b64decode(server_public_key_base64))
        except Exception as exc:
            raise ProtocolError("Invalid server public key in handshake response") from exc

        if not isinstance(server_public_key, ec.EllipticCurvePublicKey):
            raise ProtocolError("Server public key is not an EC public key")

        shared_secret = self._private_key.exchange(ec.ECDH(), server_public_key)
        derived = HKDF(
            algorithm=hashes.SHA256(),
            length=64,
            salt=None,
            info=b"AegisKeyDerivation",
        ).derive(shared_secret)
        return derived[:32], derived[32:]


def compute_mac(data: bytes, mac_key: bytes) -> bytes:
    return hmac.new(mac_key, data, hashlib.sha256).digest()


def verify_mac(data: bytes, mac_key: bytes, received_mac: bytes) -> bool:
    return compare_digest(compute_mac(data, mac_key), received_mac)