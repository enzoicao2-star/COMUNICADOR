"""Embed the audio helper so already installed receiver launchers need no new download."""
import base64
import re
import zlib
from pathlib import Path

root = Path(__file__).resolve().parent.parent
source = (root / "receiver" / "audio-control.ps1").read_text(encoding="utf-8").replace("\r\n", "\n")
encoded = base64.b64encode(zlib.compress(source.encode("utf-8"), 9)).decode("ascii")
target = root / "receiver" / "receptor.py"
content = target.read_text(encoding="utf-8")
content, count = re.subn(r'^AUDIO_CONTROL_ZLIB = "[^"]*"$',
                         f'AUDIO_CONTROL_ZLIB = "{encoded}"', content, count=1, flags=re.MULTILINE)
if count != 1:
    raise RuntimeError("AUDIO_CONTROL_ZLIB not found")
target.write_text(content, encoding="utf-8", newline="\n")
