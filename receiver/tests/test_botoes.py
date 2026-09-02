"""Botões de resposta rápida, com atenção especial ao link.

O link chega pela rede, então o ponto crítico é recusar tudo que não seja
http/https — file:, javascript:, ms-settings: e afins não podem passar.
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import ErrorCode, MessageType, ProtocolError  # noqa: E402


def notificacao(buttons=None):
    msg = protocolo.base_message(MessageType.NOTIFICATION)
    msg["token"] = "abc123"
    msg["sender"] = "PAINEL"
    msg["title"] = "Aviso"
    msg["message"] = "Corpo"
    msg["allow_reply"] = True
    if buttons is not None:
        msg["buttons"] = buttons
    return msg


def test_sem_botoes_continua_valido():
    protocolo.validate(notificacao())


def test_botao_so_com_rotulo_e_valido():
    protocolo.validate(notificacao([{"label": "Estou indo"}]))


def test_botao_com_link_http_e_valido():
    protocolo.validate(notificacao([{"label": "Abrir", "url": "http://exemplo.com/a"}]))


def test_botao_com_link_https_e_valido():
    protocolo.validate(notificacao([{"label": "Abrir", "url": "https://exemplo.com/a"}]))


def test_botao_sem_rotulo_e_recusado():
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(notificacao([{"url": "https://exemplo.com"}]))
    assert exc.value.code == ErrorCode.MISSING_FIELD


def test_rotulo_muito_longo_e_recusado():
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(notificacao([{"label": "x" * 41}]))
    assert exc.value.code == ErrorCode.FIELD_TOO_LONG


def test_botoes_demais_sao_recusados():
    demais = [{"label": f"b{i}"} for i in range(protocolo.MAX_BOTOES + 1)]
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(notificacao(demais))
    assert exc.value.code == ErrorCode.FIELD_TOO_LONG


@pytest.mark.parametrize("url", [
    "file:///C:/Windows/System32/cmd.exe",
    "javascript:alert(1)",
    "ms-settings:windowsdefender",
    "vbscript:msgbox(1)",
    "data:text/html,<script>alert(1)</script>",
    r"\\servidor\compartilhamento\coisa.exe",
    "ftp://exemplo.com/arquivo",
])
def test_esquemas_perigosos_sao_recusados(url):
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(notificacao([{"label": "Clique", "url": url}]))
    assert exc.value.code == ErrorCode.INVALID_FIELD_TYPE


def test_buttons_precisa_ser_lista():
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(notificacao("nao é lista"))
    assert exc.value.code == ErrorCode.INVALID_FIELD_TYPE


def test_url_permitida_direto():
    assert protocolo.url_permitida("https://a.com")
    assert protocolo.url_permitida("HTTP://a.com")
    assert not protocolo.url_permitida("file:///x")
    assert not protocolo.url_permitida("")
    assert not protocolo.url_permitida(None)
