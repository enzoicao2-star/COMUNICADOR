"""Keep the two-file Python receiver update compatible with older installations.

The receiver must carry its PowerShell worker inside receptor.py because the
remote update protocol for existing receivers only accepts two source files.
"""
from pathlib import Path
import base64
import re
import zlib

root = Path(__file__).resolve().parent.parent
worker = (root / "receiver" / "carousel-worker.ps1").read_bytes().replace(b"\r\n", b"\n")
encoded = base64.b64encode(zlib.compress(worker, level=9)).decode("ascii")
path = root / "receiver" / "receptor.py"
source = path.read_text(encoding="utf-8")
updated, count = re.subn(r'(?m)^CAROUSEL_WORKER_ZLIB = "[A-Za-z0-9+/=]+"$',
                         f'CAROUSEL_WORKER_ZLIB = "{encoded}"', source)
if count != 1:
    raise SystemExit("CAROUSEL_WORKER_ZLIB missing or duplicated")
path.write_text(updated, encoding="utf-8")
print(f"Embedded {len(worker)} worker bytes in receptor.py")
