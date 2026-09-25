import base64
import ctypes
import os
import sys
import zlib
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import receptor  # noqa: E402


def test_audio_helper_is_embedded_current_version():
    source = (Path(receptor.__file__).parent / "audio-control.ps1").read_text(encoding="utf-8")
    embedded = zlib.decompress(base64.b64decode(receptor.AUDIO_CONTROL_ZLIB)).decode("utf-8")
    assert embedded == source.replace("\r\n", "\n")


@pytest.mark.parametrize("input_,action,value,error", [
    ("audio devices", "list", "", None),
    ("audio select 2", "select", "2", None),
    ("volume 80", "volume", "80", None),
    ("volume 150", None, None, "Volume deve estar entre 0 e 100."),
    ("audio select 0", None, None, "Escolha um dispositivo entre 1 e 50."),
    ("hostname", None, None, None),
])
def test_audio_command_parser(input_, action, value, error):
    assert receptor.parse_audio_command(input_) == (action, value, error)


def test_remote_command_expiry():
    now = datetime.now(timezone.utc)
    assert receptor.remote_command_is_fresh((now + timedelta(minutes=5)).isoformat())
    assert not receptor.remote_command_is_fresh((now - timedelta(seconds=1)).isoformat())
    assert not receptor.remote_command_is_fresh((now + timedelta(hours=1)).isoformat())
    assert not receptor.remote_command_is_fresh(None)


def test_non_owner_cannot_run_cmd(monkeypatch):
    worker = object.__new__(receptor.CloudDeliveryWorker)
    responses = []
    worker._autorizado = lambda *args: [{"admin_device_id": "the-owner"}]
    worker._atualizar_entrega = lambda delivery_id, result: responses.append(result)
    monkeypatch.setattr(receptor, "executar_cmd_local", lambda command: pytest.fail("must not run"))
    worker._executar_comando_admin({"id": "delivery", "sender_device_id": "someone-else",
                                  "payload": {"command": "run_cmd", "line": "echo hello",
                                              "expires_at": (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat()}})
    assert responses == ["Comando recusado: o remetente não é o OWNER atual."]


def test_owner_command_runs_and_returns_response(monkeypatch):
    worker = object.__new__(receptor.CloudDeliveryWorker)
    responses = []
    worker._autorizado = lambda *args: [{"admin_device_id": "the-owner"}]
    worker._atualizar_entrega = lambda delivery_id, result: responses.append((delivery_id, result))
    monkeypatch.setattr(receptor, "executar_cmd_local", lambda command: "Código de saída: 0\nok")
    worker._executar_comando_admin({"id": "delivery", "sender_device_id": "the-owner",
                                  "payload": {"command": "run_cmd", "line": "echo ok",
                                              "expires_at": (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat()}})
    assert responses == [("delivery", "Código de saída: 0\nok")]


@pytest.mark.skipif(os.name != "nt", reason="Windows CMD")
def test_runs_harmless_cmd_without_elevation():
    if ctypes.windll.shell32.IsUserAnAdmin():
        pytest.skip("This test is intended for a normal user session")
    result = receptor.executar_cmd_local("echo codex-remote-test")
    assert "codex-remote-test" in result
    assert "Código de saída: 0" in result


@pytest.mark.skipif(os.name != "nt", reason="Windows CoreAudio")
def test_lists_audio_without_changing_it():
    if ctypes.windll.shell32.IsUserAnAdmin():
        pytest.skip("This test is intended for a normal user session")
    result = receptor.executar_cmd_local("audio devices")
    assert "Falha ao controlar áudio" not in result
    assert "Código de saída: 0" in result
