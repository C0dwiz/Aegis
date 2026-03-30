#!/usr/bin/env python3
"""Generate MessageType markdown table from src/Aegis.Protocol/MessageType.cs."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "Aegis.Protocol" / "MessageType.cs"


def parse_message_types(text: str) -> list[tuple[int, str]]:
    enum_block = re.search(r"public enum MessageType\s*:\s*ushort\s*\{(?P<body>.*?)\n\}", text, re.S)
    if not enum_block:
        raise RuntimeError("MessageType enum not found")

    items: list[tuple[int, str]] = []
    for raw_line in enum_block.group("body").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("//"):
            continue
        m = re.match(r"([A-Za-z0-9_]+)\s*=\s*(\d+)\s*,?", line)
        if not m:
            continue
        name, code = m.group(1), int(m.group(2))
        items.append((code, name))

    return sorted(items, key=lambda x: x[0])


def render_markdown(items: list[tuple[int, str]]) -> str:
    out = ["| Code | Name |", "|------|------|"]
    for code, name in items:
        out.append(f"| {code} | `{name}` |")
    return "\n".join(out) + "\n"


def main() -> int:
    text = SRC.read_text(encoding="utf-8")
    items = parse_message_types(text)
    print(render_markdown(items), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
