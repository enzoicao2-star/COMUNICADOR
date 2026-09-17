import base64
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import MessageType, ProtocolError  # noqa: E402


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
