import base64
import json
import os
import subprocess
import sys
import uuid
import zlib
from pathlib import Path
from types import SimpleNamespace

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import receptor  # noqa: E402
import protocolo  # noqa: E402


PNG = base64.b64encode(b"\x89PNG\r\n\x1a\nexample").decode("ascii")


def test_carousel_stages_all_images_before_replacing_active(tmp_path):
    config = SimpleNamespace(directory=tmp_path / "Receptor")
    session = str(uuid.uuid4())
    command = {"action": "begin", "session_id": session, "target": "center_image",
               "count": 2, "min_minutes": 3, "max_minutes": 7, "repeat": True,
               "duration_seconds": 15}
    image = {"name": "test.png", "mime_type": "image/png", "data_base64": PNG}
    root = config.directory / "Carousel"

    assert receptor.handle_carousel(command, None, config, test_mode=True) == "carousel_staged"
    assert receptor.handle_carousel({"action": "item", "session_id": session, "index": 0},
                                    image, config, test_mode=True) == "carousel_item_saved"
    with pytest.raises(protocolo.ProtocolError):
        receptor.handle_carousel({"action": "commit", "session_id": session},
                                 None, config, test_mode=True)
    assert not (root / "active.json").exists()

    receptor.handle_carousel({"action": "item", "session_id": session, "index": 1},
                             image, config, test_mode=True)
    assert receptor.handle_carousel({"action": "commit", "session_id": session},
                                    None, config, test_mode=True) == "carousel_started"
    active = json.loads((root / "active.json").read_text(encoding="utf-8"))
    assert active["target"] == "center_image" and active["repeat"] is True
    assert len(active["images"]) == 2
    assert all((root / active["folder"] / name).exists() for name in active["images"])
    assert receptor.handle_carousel({"action": "stop", "session_id": str(uuid.uuid4())},
                                    None, config, test_mode=True) == "carousel_stopped"
    assert json.loads((root / "active.json").read_text(encoding="utf-8"))["enabled"] is False


def test_embedded_worker_matches_shared_source():
    source = Path(__file__).resolve().parent.parent / "carousel-worker.ps1"
    assert zlib.decompress(base64.b64decode(receptor.CAROUSEL_WORKER_ZLIB)) == \
        source.read_bytes().replace(b"\r\n", b"\n")


def test_receiver_desativa_carrossel_legado_ao_iniciar(tmp_path):
    root = tmp_path / "Carousel"
    root.mkdir()
    path = root / "active.json"
    path.write_text(json.dumps({"target": "wallpaper", "enabled": True}), encoding="utf-8")
    assert receptor.disable_legacy_carousel(root, test_mode=True)
    assert json.loads(path.read_text(encoding="utf-8"))["enabled"] is False


@pytest.mark.skipif(sys.platform != "win32", reason="Windows PowerShell 5.1")
@pytest.mark.parametrize("repeat", [False, True])
def test_worker_advances_and_repeats_without_changing_windows(tmp_path, repeat):
    root = tmp_path / "Carousel"
    folder = root / "session-test"
    folder.mkdir(parents=True)
    (folder / "0.png").write_bytes(b"first")
    (folder / "1.png").write_bytes(b"second")
    active = {"session_id": "test", "folder": "session-test", "target": "center_image",
              "count": 2, "min_minutes": 1, "max_minutes": 1, "repeat": repeat,
              "duration_seconds": 15,
              "enabled": True, "images": ["0.png", "1.png"]}
    (root / "active.json").write_text(json.dumps(active), encoding="utf-8")
    script = Path(__file__).resolve().parent.parent / "carousel-worker.ps1"
    # The OS location can differ from the drive containing the test workspace.
    powershell = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"

    def step():
        result = subprocess.run([str(powershell), "-NoProfile", "-ExecutionPolicy", "Bypass",
                                 "-File", str(script), "-RootPath", str(root), "-Once", "-DryRun"],
                                capture_output=True, text=True, timeout=20)
        assert result.returncode == 0, result.stderr

    step()
    state = json.loads((root / "state.json").read_text(encoding="utf-8"))
    assert state["next_index"] == 1
    state["next_at_utc"] = "2000-01-01T00:00:00Z"
    (root / "state.json").write_text(json.dumps(state), encoding="utf-8")
    step()
    assert len((root / "applied.log").read_text(encoding="utf-8").splitlines()) == 2
    assert all(line.startswith("center_image|") for line in
               (root / "applied.log").read_text(encoding="utf-8").splitlines())
    if repeat:
        assert json.loads((root / "state.json").read_text(encoding="utf-8"))["next_index"] == 0
    else:
        state = json.loads((root / "state.json").read_text(encoding="utf-8"))
        state["next_at_utc"] = "2000-01-01T00:00:00Z"
        (root / "state.json").write_text(json.dumps(state), encoding="utf-8")
        step()
        assert json.loads((root / "active.json").read_text(encoding="utf-8"))["enabled"] is False


@pytest.mark.skipif(sys.platform != "win32", reason="Windows PowerShell 5.1")
def test_worker_desativa_carrossel_antigo_sem_alterar_papel_de_parede(tmp_path):
    root = tmp_path / "Carousel"
    root.mkdir()
    (root / "active.json").write_text(json.dumps({"session_id": "antigo", "enabled": True,
        "target": "wallpaper", "images": ["0.png"]}), encoding="utf-8")
    script = Path(__file__).resolve().parent.parent / "carousel-worker.ps1"
    powershell = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    result = subprocess.run([str(powershell), "-NoProfile", "-ExecutionPolicy", "Bypass",
                             "-File", str(script), "-RootPath", str(root), "-Once", "-DryRun"],
                            capture_output=True, text=True, timeout=20)
    assert result.returncode == 0, result.stderr
    assert json.loads((root / "active.json").read_text(encoding="utf-8"))["enabled"] is False
    assert not (root / "applied.log").exists()
