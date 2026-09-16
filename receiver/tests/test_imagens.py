import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import ErrorCode, MessageType, ProtocolError  # noqa: E402

PNG_UM_PIXEL = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="


def imagem():
    return {"name": "pixel.png", "mime_type": "image/png", "data_base64": PNG_UM_PIXEL}


def notificacao():
    msg = protocolo.base_message(MessageType.NOTIFICATION)
    msg.update({
        "token": "token", "sender": "PAINEL", "title": "Imagem", "message": "Confira",
        "allow_reply": False, "display_mode": "center_image", "image": imagem(),
        "image_duration_seconds": 15, "allow_manual_close": True,
    })
    return msg


def test_imagem_central_valida():
    protocolo.validate(notificacao())


def test_base64_invalido_e_recusado():
    msg = notificacao()
    msg["image"]["data_base64"] = "não-base64"
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.INVALID_FIELD_TYPE


def test_varias_imagens_em_monitores():
    msg = notificacao()
    del msg["image"]
    msg["screen_images"] = [
        {"monitor_index": 0, "width_percent": 50, "image": imagem()},
        {"monitor_index": 1, "width_percent": 80, "image": imagem()},
    ]
    protocolo.validate(msg)


def test_aviso_obrigatorio_nao_exige_imagem():
    msg = notificacao()
    msg["display_mode"] = "center_alert"
    del msg["image"]
    del msg["image_duration_seconds"]
    del msg["allow_manual_close"]
    protocolo.validate(msg)
