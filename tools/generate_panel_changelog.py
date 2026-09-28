"""Rebuild the cumulative panel release notes from published manifests in Git."""

import json
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = "release/panel-version.json"
DESTINATION = ROOT / "release/panel-changelog.json"


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True, encoding="utf-8").strip()


def key(version: str) -> tuple[int, ...]:
    return tuple(int(part) for part in version.split("."))


def build() -> list[dict]:
    history: dict[str, dict] = {}
    if DESTINATION.exists():
        for entry in json.loads(DESTINATION.read_text(encoding="utf-8")):
            # Earlier Windows console decoding used cp1252 for UTF-8 Git blobs.
            for field in ("summary",):
                value = entry.get(field, "")
                if "Ã" in value or "â€" in value:
                    try:
                        entry[field] = value.encode("cp1252").decode("utf-8")
                    except UnicodeError:
                        pass
            repaired = []
            for change in entry.get("changes", []):
                if "Ã" in change or "â€" in change:
                    try:
                        change = change.encode("cp1252").decode("utf-8")
                    except UnicodeError:
                        pass
                repaired.append(change)
            entry["changes"] = repaired
            history[entry["version"]] = entry
    else:
        for commit in git("log", "--format=%H", "--", MANIFEST).splitlines():
            try:
                manifest = json.loads(git("show", f"{commit}:{MANIFEST}"))
                version = manifest["version"]
                key(version)
                history.setdefault(version, {
                    "version": version,
                    "summary": manifest.get("summary", ""),
                    "changes": manifest.get("changes", []),
                })
            except (ValueError, KeyError, subprocess.CalledProcessError):
                continue
    current = json.loads((ROOT / MANIFEST).read_text(encoding="utf-8-sig"))
    history[current["version"]] = {
        "version": current["version"],
        "summary": current.get("summary", ""),
        "changes": current.get("changes", []),
    }
    return sorted(history.values(), key=lambda entry: key(entry["version"]))


if __name__ == "__main__":
    entries = build()
    DESTINATION.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(entries)} versões em {DESTINATION}")
