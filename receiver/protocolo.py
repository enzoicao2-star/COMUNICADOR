"""Protocolo Comunicador (TCP + JSON) — implementação Python.

Espelha exatamente src/Comunicador/Protocol/*.cs. Qualquer mudança aqui
deve ser replicada lá (e vice-versa) e documentada em PROTOCOLO.md.
"""

from __future__ import annotations

import base64
import binascii
import hashlib
import json
import re
import uuid
from datetime import datetime, timezone
from typing import Any

PROTOCOL_VERSION = 1

TCP_PORT = 57931
UDP_DISCOVERY_PORT = 57932

MAX_TCP_MESSAGE_BYTES = 24 * 1024 * 1024
MAX_UDP_MESSAGE_BYTES = 2048

MAX_TITLE_LENGTH = 200
MAX_MESSAGE_LENGTH = 4000
MAX_NAME_LENGTH = 100

MAX_BOTOES = 4
MAX_BOTAO_LABEL_LENGTH = 40
MAX_BOTAO_URL_LENGTH = 500

MAX_IMAGE_BYTES = 4 * 1024 * 1024
MAX_IMAGE_NAME_LENGTH = 255
MAX_IMAGE_BASE64_LENGTH = ((MAX_IMAGE_BYTES + 2) // 3) * 4
MIN_IMAGE_DURATION_SECONDS = 3
MAX_IMAGE_DURATION_SECONDS = 3600
MAX_MONITORS = 12
MAX_SCREEN_IMAGES = 12
MAX_TOTAL_IMAGE_BYTES = 16 * 1024 * 1024
MAX_UPDATE_FILES = 2
MAX_UPDATE_SOURCE_BYTES = 2 * 1024 * 1024
MAX_UPDATE_TOTAL_BYTES = 4 * 1024 * 1024
MAX_SYNC_ENTRIES = 500
MAX_LOG_CATEGORY_LENGTH = 100
MAX_LOG_DETAIL_LENGTH = 8000
MIN_IMAGE_WIDTH_PERCENT = 10
MAX_IMAGE_WIDTH_PERCENT = 100
MIN_TOAST_DURATION_SECONDS = 5
MAX_TOAST_DURATION_SECONDS = 300
MIN_FONT_SCALE_PERCENT = 80
MAX_FONT_SCALE_PERCENT = 160

DISPLAY_MODE_TOAST = "toast"
DISPLAY_MODE_CENTER_IMAGE = "center_image"
DISPLAY_MODE_CENTER_ALERT = "center_alert"
DISPLAY_MODES = {DISPLAY_MODE_TOAST, DISPLAY_MODE_CENTER_IMAGE, DISPLAY_MODE_CENTER_ALERT}
SOUND_TYPES = {"information", "warning", "error"}
TOAST_POSITIONS = {"bottom_right", "top_right"}

# Só http/https nos botões com link. Bloquear os demais esquemas é o que impede
# uma mensagem vinda da rede de disparar file:, javascript:, ms-* etc.
ESQUEMAS_URL_PERMITIDOS = ("http://", "https://")


class MessageType:
    DISCOVER = "discover"
    ANNOUNCE = "announce"
    PAIR_REQUEST = "pair_request"
    PAIR_RESPONSE = "pair_response"
    PING = "ping"
    PONG = "pong"
    NOTIFICATION = "notification"
    ACK = "ack"
    REPLY = "reply"
    ERROR = "error"
    REGISTER = "register"
    REGISTER_ACK = "register_ack"
    UPDATE_REQUEST = "update_request"
    UPDATE_STATUS = "update_status"
    SYNC_REQUEST = "sync_request"
    SYNC_RESPONSE = "sync_response"


ALL_TYPES = {
    MessageType.DISCOVER, MessageType.ANNOUNCE, MessageType.PAIR_REQUEST, MessageType.PAIR_RESPONSE,
    MessageType.PING, MessageType.PONG, MessageType.NOTIFICATION, MessageType.ACK, MessageType.REPLY,
    MessageType.ERROR, MessageType.REGISTER, MessageType.REGISTER_ACK,
    MessageType.UPDATE_REQUEST, MessageType.UPDATE_STATUS,
    MessageType.SYNC_REQUEST, MessageType.SYNC_RESPONSE,
}


class ErrorCode:
    INVALID_JSON = "INVALID_JSON"
    UNKNOWN_TYPE = "UNKNOWN_TYPE"
    MISSING_FIELD = "MISSING_FIELD"
    INVALID_FIELD_TYPE = "INVALID_FIELD_TYPE"
    FIELD_TOO_LONG = "FIELD_TOO_LONG"
    PAYLOAD_TOO_LARGE = "PAYLOAD_TOO_LARGE"
    INVALID_ID = "INVALID_ID"
    UNAUTHORIZED = "UNAUTHORIZED"
    PROTOCOL_VERSION_UNSUPPORTED = "PROTOCOL_VERSION_UNSUPPORTED"
    CONTENT_BLOCKED = "CONTENT_BLOCKED"
    INTERNAL_ERROR = "INTERNAL_ERROR"


class ProtocolError(Exception):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


_UUID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")


def is_valid_uuid(value: Any) -> bool:
    return isinstance(value, str) and bool(_UUID_RE.match(value))


def new_id() -> str:
    return str(uuid.uuid4())


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def base_message(msg_type: str) -> dict:
    return {
        "protocol_version": PROTOCOL_VERSION,
        "type": msg_type,
        "id": new_id(),
        "timestamp": now_iso(),
    }


def make_error(code: str, message: str, in_reply_to: str | None = None) -> dict:
    msg = base_message(MessageType.ERROR)
    msg["code"] = code
    msg["message"] = message
    if in_reply_to is not None:
        msg["in_reply_to"] = in_reply_to
    return msg


def frame(message: dict) -> bytes:
    return (json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")


def validate_size(byte_length: int, is_udp: bool) -> None:
    limit = MAX_UDP_MESSAGE_BYTES if is_udp else MAX_TCP_MESSAGE_BYTES
    if byte_length > limit:
        raise ProtocolError(ErrorCode.PAYLOAD_TOO_LARGE, f"Payload excede o limite de {limit} bytes.")


def parse(payload: bytes) -> dict:
    try:
        obj = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError(ErrorCode.INVALID_JSON, f"JSON inválido: {exc}") from exc

    if not isinstance(obj, dict):
        raise ProtocolError(ErrorCode.INVALID_JSON, "Mensagem precisa ser um objeto JSON.")

    return obj


def _require_str(obj: dict, name: str, max_len: int) -> str:
    value = obj.get(name)
    if not isinstance(value, str) or value == "":
        raise ProtocolError(ErrorCode.MISSING_FIELD, f"Campo obrigatório ausente: {name}")
    if len(value) > max_len:
        raise ProtocolError(ErrorCode.FIELD_TOO_LONG, f"Campo '{name}' excede {max_len} caracteres.")
    return value


def _require_bool(obj: dict, name: str) -> bool:
    value = obj.get(name)
    if not isinstance(value, bool):
        raise ProtocolError(ErrorCode.MISSING_FIELD, f"Campo obrigatório ausente: {name}")
    return value


def _require_int(obj: dict, name: str) -> int:
    value = obj.get(name)
    if not isinstance(value, int) or isinstance(value, bool):
        raise ProtocolError(ErrorCode.MISSING_FIELD, f"Campo obrigatório ausente: {name}")
    return value


def url_permitida(url) -> bool:
    """Aceita apenas http/https. Vale para qualquer URL que chegue pela rede."""
    return isinstance(url, str) and url.lower().startswith(ESQUEMAS_URL_PERMITIDOS)


def _validar_botoes(msg: dict) -> None:
    botoes = msg.get("buttons")
    if botoes is None:
        return

    if not isinstance(botoes, list):
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Campo 'buttons' precisa ser uma lista.")

    if len(botoes) > MAX_BOTOES:
        raise ProtocolError(ErrorCode.FIELD_TOO_LONG, f"São permitidos no máximo {MAX_BOTOES} botões.")

    for botao in botoes:
        if not isinstance(botao, dict):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Cada item de 'buttons' precisa ser um objeto.")

        label = botao.get("label")
        if not isinstance(label, str) or not label.strip():
            raise ProtocolError(ErrorCode.MISSING_FIELD, "Campo obrigatório ausente: buttons[].label")
        if len(label) > MAX_BOTAO_LABEL_LENGTH:
            raise ProtocolError(
                ErrorCode.FIELD_TOO_LONG, f"Rótulo de botão excede {MAX_BOTAO_LABEL_LENGTH} caracteres.")

        url = botao.get("url")
        if url is None or url == "":
            continue
        if not isinstance(url, str) or len(url) > MAX_BOTAO_URL_LENGTH:
            raise ProtocolError(ErrorCode.FIELD_TOO_LONG, f"URL de botão excede {MAX_BOTAO_URL_LENGTH} caracteres.")
        if not url_permitida(url):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "URL de botão precisa ser http:// ou https://.")


def _assinatura_imagem_valida(mime_type: str, dados: bytes) -> bool:
    if mime_type == "image/png":
        return dados.startswith(b"\x89PNG\r\n\x1a\n")
    if mime_type == "image/jpeg":
        return dados.startswith(b"\xff\xd8\xff")
    if mime_type == "image/gif":
        return dados.startswith((b"GIF87a", b"GIF89a"))
    if mime_type == "image/bmp":
        return dados.startswith(b"BM")
    return False


def _validar_imagem(imagem: dict) -> int:
    if not isinstance(imagem, dict):
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Campo 'image' precisa ser um objeto.")

    _require_str(imagem, "name", MAX_IMAGE_NAME_LENGTH)
    mime_type = _require_str(imagem, "mime_type", 40)
    if mime_type not in {"image/png", "image/jpeg", "image/gif", "image/bmp"}:
        raise ProtocolError(
            ErrorCode.INVALID_FIELD_TYPE, "Formato de imagem não permitido. Use PNG, JPEG, GIF ou BMP.")

    data_base64 = _require_str(imagem, "data_base64", MAX_IMAGE_BASE64_LENGTH)
    try:
        dados = base64.b64decode(data_base64, validate=True)
    except (ValueError, binascii.Error) as exc:
        raise ProtocolError(
            ErrorCode.INVALID_FIELD_TYPE, "Campo 'image.data_base64' não é Base64 válido.") from exc

    if not dados or len(dados) > MAX_IMAGE_BYTES:
        raise ProtocolError(
            ErrorCode.PAYLOAD_TOO_LARGE, f"Imagem precisa ter entre 1 e {MAX_IMAGE_BYTES} bytes.")
    if not _assinatura_imagem_valida(mime_type, dados):
        raise ProtocolError(
            ErrorCode.INVALID_FIELD_TYPE,
            "O conteúdo do arquivo não corresponde ao formato de imagem informado.")
    return len(dados)


def _validar_conteudo_visual(msg: dict) -> None:
    modo = msg.get("display_mode", DISPLAY_MODE_TOAST)
    if not isinstance(modo, str) or modo not in DISPLAY_MODES:
        raise ProtocolError(
            ErrorCode.INVALID_FIELD_TYPE,
            "Campo 'display_mode' precisa ser 'toast', 'center_image' ou 'center_alert'.")

    imagem = msg.get("image")
    imagens_por_monitor = msg.get("screen_images")
    if modo == DISPLAY_MODE_CENTER_IMAGE:
        if not isinstance(imagem, dict) and not (
                isinstance(imagens_por_monitor, list) and imagens_por_monitor):
            raise ProtocolError(
                ErrorCode.MISSING_FIELD, "Aviso central precisa de 'image' ou 'screen_images'.")

        duracao = msg.get("image_duration_seconds")
        if not isinstance(duracao, int) or isinstance(duracao, bool):
            raise ProtocolError(
                ErrorCode.MISSING_FIELD, "Aviso central precisa do campo 'image_duration_seconds'.")
        if not MIN_IMAGE_DURATION_SECONDS <= duracao <= MAX_IMAGE_DURATION_SECONDS:
            raise ProtocolError(
                ErrorCode.INVALID_FIELD_TYPE,
                f"'image_duration_seconds' precisa ficar entre {MIN_IMAGE_DURATION_SECONDS} e "
                f"{MAX_IMAGE_DURATION_SECONDS}.")

        if not isinstance(msg.get("allow_manual_close"), bool):
            raise ProtocolError(
                ErrorCode.MISSING_FIELD, "Aviso central precisa do campo 'allow_manual_close'.")

    total = _validar_imagem(imagem) if imagem is not None else 0
    if imagens_por_monitor is not None:
        if not isinstance(imagens_por_monitor, list):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "'screen_images' precisa ser uma lista.")
        if len(imagens_por_monitor) > MAX_SCREEN_IMAGES:
            raise ProtocolError(
                ErrorCode.FIELD_TOO_LONG, f"São permitidas no máximo {MAX_SCREEN_IMAGES} imagens por mensagem.")
        for item in imagens_por_monitor:
            if not isinstance(item, dict):
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Item de 'screen_images' inválido.")
            indice = _require_int(item, "monitor_index")
            percentual = _require_int(item, "width_percent")
            if not 0 <= indice < MAX_MONITORS:
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Índice de monitor inválido.")
            if not MIN_IMAGE_WIDTH_PERCENT <= percentual <= MAX_IMAGE_WIDTH_PERCENT:
                raise ProtocolError(
                    ErrorCode.INVALID_FIELD_TYPE,
                    f"Tamanho da imagem precisa ficar entre {MIN_IMAGE_WIDTH_PERCENT}% e "
                    f"{MAX_IMAGE_WIDTH_PERCENT}%.")
            total += _validar_imagem(item.get("image"))

    if total > MAX_TOTAL_IMAGE_BYTES:
        raise ProtocolError(
            ErrorCode.PAYLOAD_TOO_LARGE, f"O conjunto de imagens excede {MAX_TOTAL_IMAGE_BYTES} bytes.")


def _validar_aparencia(msg: dict) -> None:
    aparencia = msg.get("appearance")
    if aparencia is None:
        return
    if not isinstance(aparencia, dict):
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Campo 'appearance' precisa ser um objeto.")

    cor = aparencia.get("accent_color")
    if not isinstance(cor, str) or not re.fullmatch(r"#[0-9a-fA-F]{6}", cor):
        raise ProtocolError(
            ErrorCode.INVALID_FIELD_TYPE, "'appearance.accent_color' precisa usar o formato #RRGGBB.")
    escala = _require_int(aparencia, "font_scale_percent")
    if not MIN_FONT_SCALE_PERCENT <= escala <= MAX_FONT_SCALE_PERCENT:
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Escala do texto inválida.")
    _require_bool(aparencia, "play_sound")
    if aparencia.get("sound_type") not in SOUND_TYPES:
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Tipo de som inválido.")
    duracao = _require_int(aparencia, "toast_duration_seconds")
    if not MIN_TOAST_DURATION_SECONDS <= duracao <= MAX_TOAST_DURATION_SECONDS:
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Duração do aviso inválida.")
    if aparencia.get("toast_position") not in TOAST_POSITIONS:
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Posição do aviso inválida.")


def _validar_monitores(msg: dict) -> None:
    monitores = msg.get("monitors")
    if monitores is None:
        return
    if not isinstance(monitores, list) or len(monitores) > MAX_MONITORS:
        raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Lista de monitores inválida.")
    for monitor in monitores:
        if not isinstance(monitor, dict):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Dados de monitor inválidos.")
        indice = _require_int(monitor, "index")
        largura = _require_int(monitor, "width")
        altura = _require_int(monitor, "height")
        eixo_x = _require_int(monitor, "x")
        eixo_y = _require_int(monitor, "y")
        _require_str(monitor, "name", MAX_NAME_LENGTH)
        _require_bool(monitor, "primary")
        if (not 0 <= indice < MAX_MONITORS or not 0 <= largura <= 32768 or not 0 <= altura <= 32768
                or not -65536 <= eixo_x <= 65536 or not -65536 <= eixo_y <= 65536):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Dados de monitor inválidos.")


def _validar_arquivos_atualizacao(msg: dict) -> None:
    arquivos = msg.get("update_files")
    if not isinstance(arquivos, list) or len(arquivos) != MAX_UPDATE_FILES:
        raise ProtocolError(
            ErrorCode.MISSING_FIELD, "A atualização precisa conter receptor.py e protocolo.py.")

    permitidos = {"receptor.py", "protocolo.py"}
    encontrados = set()
    total = 0
    for arquivo in arquivos:
        if not isinstance(arquivo, dict):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Arquivo de atualização inválido.")
        nome = arquivo.get("name")
        if nome not in permitidos or nome in encontrados:
            raise ProtocolError(
                ErrorCode.INVALID_FIELD_TYPE,
                "A atualização contém nome de arquivo inválido ou repetido.")
        encontrados.add(nome)

        hash_informado = arquivo.get("sha256")
        if not isinstance(hash_informado, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", hash_informado):
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "SHA-256 de atualização inválido.")
        conteudo_base64 = arquivo.get("content_base64")
        if not isinstance(conteudo_base64, str):
            raise ProtocolError(ErrorCode.MISSING_FIELD, "Conteúdo da atualização ausente.")
        try:
            conteudo = base64.b64decode(conteudo_base64, validate=True)
            conteudo.decode("utf-8", errors="strict")
        except (ValueError, UnicodeDecodeError, binascii.Error) as exc:
            raise ProtocolError(
                ErrorCode.INVALID_FIELD_TYPE,
                "Arquivo de atualização não contém texto UTF-8 válido.") from exc
        if not conteudo or len(conteudo) > MAX_UPDATE_SOURCE_BYTES:
            raise ProtocolError(
                ErrorCode.PAYLOAD_TOO_LARGE, "Arquivo de atualização excede o limite permitido.")
        if hashlib.sha256(conteudo).hexdigest().lower() != hash_informado.lower():
            raise ProtocolError(
                ErrorCode.INVALID_FIELD_TYPE, "SHA-256 do arquivo de atualização não confere.")
        total += len(conteudo)

    if encontrados != permitidos:
        raise ProtocolError(
            ErrorCode.MISSING_FIELD, "A atualização precisa conter receptor.py e protocolo.py.")
    if total > MAX_UPDATE_TOTAL_BYTES:
        raise ProtocolError(
            ErrorCode.PAYLOAD_TOO_LARGE, "Pacote de atualização excede o limite permitido.")


def _validar_itens_sync(msg: dict) -> None:
    historico = msg.get("history_entries")
    logs = msg.get("log_entries")
    if historico is not None:
        if not isinstance(historico, list) or len(historico) > MAX_SYNC_ENTRIES:
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Lista de histórico sincronizado inválida.")
        for item in historico:
            if not isinstance(item, dict) or not is_valid_uuid(item.get("id")):
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Item de histórico sincronizado inválido.")
            _require_str(item, "timestamp", MAX_NAME_LENGTH)
            _require_str(item, "computer_id", MAX_NAME_LENGTH)
            _require_str(item, "computer_name", MAX_NAME_LENGTH)
            _require_str(item, "title", MAX_TITLE_LENGTH)
            _require_str(item, "message", MAX_MESSAGE_LENGTH)
            if item.get("direction") not in {"enviada", "recebida"}:
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Direção do histórico inválida.")
            if item.get("status") not in {
                    "enviando", "entregue", "exibido", "respondido", "erro", "semresposta"}:
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Status do histórico inválido.")

    if logs is not None:
        if not isinstance(logs, list) or len(logs) > MAX_SYNC_ENTRIES:
            raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Lista de logs sincronizados inválida.")
        for item in logs:
            if not isinstance(item, dict) or not is_valid_uuid(item.get("id")):
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Registro de log sincronizado inválido.")
            _require_str(item, "timestamp", MAX_NAME_LENGTH)
            _require_str(item, "origin_id", MAX_NAME_LENGTH)
            _require_str(item, "origin_name", MAX_NAME_LENGTH)
            _require_str(item, "category", MAX_LOG_CATEGORY_LENGTH)
            _require_str(item, "message", MAX_MESSAGE_LENGTH)
            if item.get("level") not in {"info", "aviso", "erro"}:
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Nível de log inválido.")
            if item.get("origin_type") not in {"panel", "receiver"}:
                raise ProtocolError(ErrorCode.INVALID_FIELD_TYPE, "Origem de log inválida.")
            detalhes = item.get("details")
            if detalhes is not None and (not isinstance(detalhes, str) or len(detalhes) > MAX_LOG_DETAIL_LENGTH):
                raise ProtocolError(ErrorCode.FIELD_TOO_LONG, "Detalhes do log excedem o limite.")
def _require_uuid(obj: dict, name: str) -> str:
    value = _require_str(obj, name, MAX_NAME_LENGTH)
    if not is_valid_uuid(value):
        raise ProtocolError(ErrorCode.INVALID_ID, f"Campo '{name}' não é um UUID válido.")
    return value


def validate(msg: dict) -> None:
    """Levanta ProtocolError na primeira violação encontrada. Deve ser chamada
    depois de validate_size() + parse() sobre todo payload recebido, dos dois lados."""

    version = msg.get("protocol_version")
    if not isinstance(version, int) or isinstance(version, bool) or version != PROTOCOL_VERSION:
        raise ProtocolError(
            ErrorCode.PROTOCOL_VERSION_UNSUPPORTED, f"Versão de protocolo não suportada: {version!r}")

    msg_type = msg.get("type")
    if msg_type not in ALL_TYPES:
        raise ProtocolError(ErrorCode.UNKNOWN_TYPE, f"Tipo de mensagem desconhecido: '{msg_type}'")

    if not is_valid_uuid(msg.get("id")):
        raise ProtocolError(ErrorCode.INVALID_ID, "Campo 'id' não é um UUID válido.")

    if not isinstance(msg.get("timestamp"), str) or not msg.get("timestamp"):
        raise ProtocolError(ErrorCode.MISSING_FIELD, "Campo obrigatório ausente: timestamp")

    if msg_type == MessageType.DISCOVER:
        _require_uuid(msg, "panel_id")
        _require_str(msg, "sender_name", MAX_NAME_LENGTH)

    elif msg_type == MessageType.ANNOUNCE:
        _require_str(msg, "computer_id", MAX_NAME_LENGTH)
        _require_str(msg, "computer_name", MAX_NAME_LENGTH)
        _require_int(msg, "tcp_port")
        _require_bool(msg, "paired")

    elif msg_type == MessageType.PAIR_REQUEST:
        _require_uuid(msg, "panel_id")
        _require_str(msg, "panel_name", MAX_NAME_LENGTH)

    elif msg_type == MessageType.PAIR_RESPONSE:
        accepted = _require_bool(msg, "accepted")
        if accepted:
            _require_str(msg, "computer_id", MAX_NAME_LENGTH)
            _require_str(msg, "computer_name", MAX_NAME_LENGTH)
            _require_str(msg, "token", MAX_NAME_LENGTH)

    elif msg_type == MessageType.PING:
        _require_str(msg, "token", MAX_NAME_LENGTH)

    elif msg_type == MessageType.PONG:
        _require_str(msg, "computer_id", MAX_NAME_LENGTH)
        _require_str(msg, "computer_name", MAX_NAME_LENGTH)
        _require_str(msg, "status", MAX_NAME_LENGTH)

    elif msg_type == MessageType.NOTIFICATION:
        _require_str(msg, "token", MAX_NAME_LENGTH)
        _require_str(msg, "sender", MAX_NAME_LENGTH)
        _require_str(msg, "title", MAX_TITLE_LENGTH)
        _require_str(msg, "message", MAX_MESSAGE_LENGTH)
        _require_bool(msg, "allow_reply")
        _validar_botoes(msg)
        _validar_conteudo_visual(msg)
        _validar_aparencia(msg)

    elif msg_type == MessageType.ACK:
        _require_uuid(msg, "in_reply_to")
        _require_str(msg, "status", MAX_NAME_LENGTH)

    elif msg_type == MessageType.REPLY:
        _require_uuid(msg, "in_reply_to")
        _require_str(msg, "computer_id", MAX_NAME_LENGTH)
        _require_str(msg, "computer_name", MAX_NAME_LENGTH)
        _require_str(msg, "reply_text", MAX_MESSAGE_LENGTH)

    elif msg_type == MessageType.ERROR:
        _require_str(msg, "code", MAX_NAME_LENGTH)
        _require_str(msg, "message", MAX_MESSAGE_LENGTH)

    elif msg_type == MessageType.REGISTER:
        # conexao reversa: o receptor abre a conexao e se registra no painel.
        # token e opcional — na primeira vez o receptor ainda nao tem um.
        _require_str(msg, "computer_id", MAX_NAME_LENGTH)
        _require_str(msg, "computer_name", MAX_NAME_LENGTH)

    elif msg_type == MessageType.REGISTER_ACK:
        accepted = _require_bool(msg, "accepted")
        if accepted:
            _require_str(msg, "token", MAX_NAME_LENGTH)

    elif msg_type == MessageType.UPDATE_REQUEST:
        _require_str(msg, "token", MAX_NAME_LENGTH)
        _require_str(msg, "target_version", MAX_NAME_LENGTH)
        _validar_arquivos_atualizacao(msg)

    elif msg_type == MessageType.UPDATE_STATUS:
        _require_uuid(msg, "in_reply_to")
        _require_bool(msg, "success")
        _require_str(msg, "status", MAX_NAME_LENGTH)
        _require_str(msg, "receiver_version", MAX_NAME_LENGTH)
        if msg.get("message") is not None:
            _require_str(msg, "message", MAX_MESSAGE_LENGTH)

    elif msg_type == MessageType.SYNC_REQUEST:
        _require_str(msg, "token", MAX_NAME_LENGTH)
        _require_bool(msg, "include_history")
        _require_bool(msg, "include_logs")
        _validar_itens_sync(msg)

    elif msg_type == MessageType.SYNC_RESPONSE:
        _require_uuid(msg, "in_reply_to")
        _validar_itens_sync(msg)

    _validar_monitores(msg)


def parse_and_validate(payload: bytes, is_udp: bool) -> dict:
    validate_size(len(payload), is_udp)
    msg = parse(payload)
    validate(msg)
    return msg
