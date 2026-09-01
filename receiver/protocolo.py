"""Protocolo Comunicador (TCP + JSON) — implementação Python.

Espelha exatamente src/Comunicador/Protocol/*.cs. Qualquer mudança aqui
deve ser replicada lá (e vice-versa) e documentada em PROTOCOLO.md.
"""

from __future__ import annotations

import json
import re
import uuid
from datetime import datetime, timezone
from typing import Any

PROTOCOL_VERSION = 1

TCP_PORT = 57931
UDP_DISCOVERY_PORT = 57932

MAX_TCP_MESSAGE_BYTES = 65536
MAX_UDP_MESSAGE_BYTES = 2048

MAX_TITLE_LENGTH = 200
MAX_MESSAGE_LENGTH = 4000
MAX_NAME_LENGTH = 100


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


ALL_TYPES = {
    MessageType.DISCOVER, MessageType.ANNOUNCE, MessageType.PAIR_REQUEST, MessageType.PAIR_RESPONSE,
    MessageType.PING, MessageType.PONG, MessageType.NOTIFICATION, MessageType.ACK, MessageType.REPLY,
    MessageType.ERROR, MessageType.REGISTER, MessageType.REGISTER_ACK,
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


def parse_and_validate(payload: bytes, is_udp: bool) -> dict:
    validate_size(len(payload), is_udp)
    msg = parse(payload)
    validate(msg)
    return msg
