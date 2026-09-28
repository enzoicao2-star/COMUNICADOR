import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import receptor  # noqa: E402


def _config_with_shared_identity(monkeypatch, computer_id, identity):
    config = receptor.Config.__new__(receptor.Config)
    config.directory = Path("virtual-root") / "Comunicador" / "Receptor"
    config.data = {
        "computer_id": computer_id,
        "has_panel": False,
        "panel_version": None,
        "media_blocked": False,
    }
    shared_file = config.directory.parent / "device.json"
    serialized_identity = json.dumps(identity)
    original_read_text = Path.read_text
    monkeypatch.setattr(
        Path,
        "read_text",
        lambda path, encoding=None, errors=None: serialized_identity
        if path == shared_file
        else original_read_text(path, encoding=encoding, errors=errors),
    )
    saves = []
    config.save = lambda: saves.append(dict(config.data))
    return config, saves


def test_running_receiver_adopts_panel_identity(monkeypatch):
    old_id = "8a0d0000-1111-4222-8333-444444444444"
    panel_id = "9b1e0000-2222-4333-8444-555555555555"
    config, saves = _config_with_shared_identity(
        monkeypatch,
        old_id,
        {
            "device_id": panel_id,
            "has_panel": True,
            "panel_version": "2.5.17",
            "media_blocked": False,
        },
    )

    config.refresh_shared_preferences()

    assert config.computer_id == panel_id
    assert config.has_panel is True
    assert config.data["panel_version"] == "2.5.17"
    assert len(saves) == 1


def test_receiver_does_not_adopt_an_unrelated_identity_without_a_panel(monkeypatch):
    old_id = "8a0d0000-1111-4222-8333-444444444444"
    config, saves = _config_with_shared_identity(
        monkeypatch,
        old_id,
        {
            "device_id": "9b1e0000-2222-4333-8444-555555555555",
            "has_panel": False,
        },
    )

    config.refresh_shared_preferences()

    assert config.computer_id == old_id
    assert saves == []
