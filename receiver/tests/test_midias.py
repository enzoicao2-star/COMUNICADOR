import base64
import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import MessageType, ProtocolError  # noqa: E402
from receptor import Config  # noqa: E402


def test_owner_override_exige_identidade_e_token_do_mesmo_painel(tmp_path):
    config = Config(tmp_path / "Receptor")
    config.paired_panels["owner-id"] = {"token": "token-owner"}

    class Worker:
        def __init__(self):
            self.consultas = 0

        def is_current_owner(self, panel_id):
            self.consultas += 1
            return panel_id == "owner-id"

    worker = Worker()
    config.cloud_worker = worker
    assert not config.sender_is_owner({"panel_id": "outro", "token": "token-owner"})
    assert not config.sender_is_owner({"panel_id": "owner-id", "token": "errado"})
    assert worker.consultas == 0
    assert config.sender_is_owner({"panel_id": "owner-id", "token": "token-owner"})
    assert worker.consultas == 1


def test_bloqueio_de_midia_muda_sem_reiniciar_receptor(tmp_path):
    config = Config(tmp_path / "Receptor")
    identity = tmp_path / "device.json"
    identity.write_text(json.dumps({"device_id": config.computer_id,
                                    "has_panel": True, "media_blocked": True}), encoding="utf-8")
    assert config.media_blocked
    assert config.has_panel
    identity.write_text(json.dumps({"device_id": config.computer_id,
                                    "has_panel": True, "media_blocked": False}), encoding="utf-8")
    assert not config.media_blocked


def base(modo):
    msg = protocolo.base_message(MessageType.NOTIFICATION)
    msg.update({
        "token": "token", "sender": "PAINEL", "title": "", "message": "",
        "allow_reply": False, "display_mode": modo,
    })
    return msg


def video():
    dados = b"\x00\x00\x00\x18ftypisom"
    return {
        "name": "video.mp4", "mime_type": "video/mp4",
        "data_base64": base64.b64encode(dados).decode("ascii"),
    }


def audio():
    dados = b"RIFF\x04\x00\x00\x00WAVE"
    return {
        "name": "som.wav", "mime_type": "audio/wav",
        "data_base64": base64.b64encode(dados).decode("ascii"),
    }


def test_video_sem_loop_termina_com_o_arquivo():
    msg = base(protocolo.DISPLAY_MODE_CENTER_VIDEO)
    msg.update({"video": video(), "video_loop": False, "allow_manual_close": True})
    protocolo.validate(msg)


def test_video_em_loop_exige_duracao():
    msg = base(protocolo.DISPLAY_MODE_CENTER_VIDEO)
    msg.update({"video": video(), "video_loop": True, "allow_manual_close": True})
    with pytest.raises(ProtocolError):
        protocolo.validate(msg)
    msg["image_duration_seconds"] = 30
    protocolo.validate(msg)


def test_audio_sem_limite_ou_com_tempo_definido():
    msg = base(protocolo.DISPLAY_MODE_AUDIO)
    msg.update({"audio": audio(), "audio_loop": False})
    protocolo.validate(msg)
    msg["image_duration_seconds"] = 12
    protocolo.validate(msg)


def test_audio_em_loop_exige_duracao():
    msg = base(protocolo.DISPLAY_MODE_AUDIO)
    msg.update({"audio": audio(), "audio_loop": True})
    with pytest.raises(ProtocolError):
        protocolo.validate(msg)
