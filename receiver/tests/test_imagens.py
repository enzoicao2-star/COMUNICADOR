import base64
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import ErrorCode, MessageType, ProtocolError  # noqa: E402

PNG_UM_PIXEL = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
GIF_UM_PIXEL = "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="


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


def test_gif_central_valido_e_preservado_no_transporte():
    msg = notificacao()
    msg["image"] = {
        "name": "animacao.gif", "mime_type": "image/gif", "data_base64": GIF_UM_PIXEL,
    }

    protocolo.validate(msg)
    recebida = protocolo.parse(protocolo.frame(msg).rstrip(b"\n"))
    assert recebida["image"]["mime_type"] == "image/gif"
    assert recebida["image"]["data_base64"] == GIF_UM_PIXEL


def test_gif_maior_que_quatro_mb_e_aceito_ate_o_novo_limite():
    dados = b"GIF89a" + bytes(protocolo.MAX_IMAGE_BYTES - 5)
    msg = notificacao()
    msg["image"] = {
        "name": "animacao.gif",
        "mime_type": "image/gif",
        "data_base64": base64.b64encode(dados).decode("ascii"),
    }

    protocolo.validate(msg)


def test_png_maior_que_quatro_mb_continua_sendo_recusado():
    dados = b"\x89PNG\r\n\x1a\n" + bytes(protocolo.MAX_IMAGE_BYTES - 7)
    msg = notificacao()
    msg["image"]["data_base64"] = base64.b64encode(dados).decode("ascii")

    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.PAYLOAD_TOO_LARGE


def test_imagem_central_sem_titulo_nem_mensagem():
    msg = notificacao()
    msg["title"] = ""
    msg["message"] = ""
    protocolo.validate(msg)


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
