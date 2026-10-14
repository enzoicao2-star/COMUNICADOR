import json
import sys
import tempfile
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
        "panel_uninstalled": False,
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
            "panel_uninstalled": False,
            "media_blocked": False,
        },
    )

    config.refresh_shared_preferences()

    assert config.computer_id == panel_id
    assert config.has_panel is True
    assert config.data["panel_version"] == "2.5.17"
    assert config.panel_uninstalled is False
    assert len(saves) == 1


def test_receiver_refreshes_and_clears_panel_uninstall_marker(monkeypatch):
    device_id = "9b1e0000-2222-4333-8444-555555555555"
    config, saves = _config_with_shared_identity(
        monkeypatch,
        device_id,
        {
            "device_id": device_id,
            "has_panel": False,
            "panel_version": None,
            "panel_uninstalled": True,
        },
    )

    config.refresh_shared_preferences()
    assert config.panel_uninstalled is True

    with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as temporary:
        config.directory = Path(temporary) / "Comunicador" / "Receptor"
        config.directory.parent.mkdir(parents=True)
        shared_file = config.directory.parent / "device.json"
        shared_file.write_text(json.dumps({"device_id": device_id, "panel_uninstalled": True}), encoding="utf-8")
        config.save = lambda: saves.append(dict(config.data))
        config.clear_panel_uninstalled_marker()

        assert config.panel_uninstalled is False
        assert json.loads(shared_file.read_text(encoding="utf-8"))["panel_uninstalled"] is False


def test_cloud_registration_retries_panel_uninstall_and_clears_marker():
    device_id = "9b1e0000-2222-4333-8444-555555555555"

    class FakeConfig:
        computer_id = device_id
        computer_name = "DESKTOP-TESTE"
        panel_version = None
        receiver_version = "2.5.21"
        has_panel = False
        media_blocked = False
        panel_uninstalled = True

        def refresh_shared_preferences(self):
            pass

        def clear_panel_uninstalled_marker(self):
            self.panel_uninstalled = False

    worker = receptor.CloudDeliveryWorker.__new__(receptor.CloudDeliveryWorker)
    worker.config = FakeConfig()
    calls = []
    worker._autorizado = lambda path, method, body: calls.append((path, method, body))

    worker._registrar()

    assert [call[0] for call in calls] == [
        "/rest/v1/rpc/register_device_status",
        "/rest/v1/rpc/unregister_panel",
    ]
    assert calls[1][2] == {"p_device_id": device_id}
    assert worker.config.panel_uninstalled is False


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
