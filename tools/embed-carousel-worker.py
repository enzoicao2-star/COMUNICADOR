"""Embed the shared carousel worker in the standalone Python receiver."""

import base64
import re
import zlib
from pathlib import Path


root = Path(__file__).resolve().parent.parent / "receiver"
source = (root / "carousel-worker.ps1").read_bytes().replace(b"\r\n", b"\n")
encoded = base64.b64encode(zlib.compress(source, level=9)).decode("ascii")
receiver = root / "receptor.py"
original = receiver.read_text(encoding="utf-8")
updated, count = re.subn(
    r'^CAROUSEL_WORKER_ZLIB = "[^"]+"$',
    lambda _: f'CAROUSEL_WORKER_ZLIB = "{encoded}"',
    original,
    flags=re.MULTILINE,
)
if count != 1:
    raise SystemExit("Expected exactly one embedded carousel worker")
receiver.write_text(updated, encoding="utf-8", newline="\n")
print(f"Embedded {len(source)} worker bytes in {receiver}")
