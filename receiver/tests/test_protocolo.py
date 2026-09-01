import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import ErrorCode, MessageType, ProtocolError  # noqa: E402


def make_valid_notification():
    msg = protocolo.base_message(MessageType.NOTIFICATION)
    msg["token"] = "abc123"
    msg["sender"] = "PAINEL-PC"
    msg["title"] = "Aviso"
    msg["message"] = "Olá"
    msg["allow_reply"] = True
    return msg


def test_valid_notification_passes():
    protocolo.validate(make_valid_notification())


def test_missing_field_raises():
    msg = make_valid_notification()
    del msg["title"]
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.MISSING_FIELD


def test_unknown_type_raises():
    msg = make_valid_notification()
    msg["type"] = "algo_invalido"
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.UNKNOWN_TYPE


def test_invalid_id_raises():
    msg = make_valid_notification()
    msg["id"] = "nao-e-um-uuid"
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.INVALID_ID


def test_title_too_long_raises():
    msg = make_valid_notification()
    msg["title"] = "x" * (protocolo.MAX_TITLE_LENGTH + 1)
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.FIELD_TOO_LONG


def test_wrong_field_type_raises():
    msg = make_valid_notification()
    msg["allow_reply"] = "sim"
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.MISSING_FIELD


def test_unsupported_protocol_version_raises():
    msg = make_valid_notification()
    msg["protocol_version"] = 99
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.PROTOCOL_VERSION_UNSUPPORTED


def test_payload_too_large_raises():
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate_size(protocolo.MAX_TCP_MESSAGE_BYTES + 1, is_udp=False)
    assert exc.value.code == ErrorCode.PAYLOAD_TOO_LARGE


def test_udp_payload_too_large_raises():
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate_size(protocolo.MAX_UDP_MESSAGE_BYTES + 1, is_udp=True)
    assert exc.value.code == ErrorCode.PAYLOAD_TOO_LARGE


def test_invalid_json_raises():
    with pytest.raises(ProtocolError) as exc:
        protocolo.parse(b"{not valid json")
    assert exc.value.code == ErrorCode.INVALID_JSON


def test_json_that_is_not_an_object_raises():
    with pytest.raises(ProtocolError) as exc:
        protocolo.parse(b"[1, 2, 3]")
    assert exc.value.code == ErrorCode.INVALID_JSON


def test_frame_ends_with_newline_and_round_trips():
    msg = protocolo.base_message(MessageType.PING)
    msg["token"] = "abc"
    framed = protocolo.frame(msg)
    assert framed.endswith(b"\n")
    parsed = protocolo.parse(framed.rstrip(b"\n"))
    assert parsed["type"] == MessageType.PING


def test_pair_response_accepted_requires_token():
    msg = protocolo.base_message(MessageType.PAIR_RESPONSE)
    msg["accepted"] = True
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.MISSING_FIELD


def test_pair_response_rejected_does_not_require_token():
    msg = protocolo.base_message(MessageType.PAIR_RESPONSE)
    msg["accepted"] = False
    protocolo.validate(msg)


def test_discover_requires_panel_id():
    msg = protocolo.base_message(MessageType.DISCOVER)
    msg["sender_name"] = "PAINEL-PC"
    with pytest.raises(ProtocolError) as exc:
        protocolo.validate(msg)
    assert exc.value.code == ErrorCode.MISSING_FIELD


def test_is_valid_uuid():
    assert protocolo.is_valid_uuid(protocolo.new_id())
    assert not protocolo.is_valid_uuid("abc")
    assert not protocolo.is_valid_uuid(123)
