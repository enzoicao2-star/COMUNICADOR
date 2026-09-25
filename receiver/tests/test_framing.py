"""Frames de mídia grandes e mensagens concatenadas na conexão reversa."""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from receptor import ReceptorTcpHandler, ReverseConnection, read_framed_message  # noqa: E402


class ChunkSocket:
    def __init__(self, chunks):
        self.chunks = list(chunks)
        self.reads = 0

    def recv(self, size):
        self.reads += 1
        if not self.chunks:
            return b""
        chunk = self.chunks.pop(0)
        if len(chunk) > size:
            self.chunks.insert(0, chunk[size:])
            return chunk[:size]
        return chunk


def test_direct_read_handles_fragmented_large_media():
    payload = b"x" * (4 * 1024 * 1024)
    sock = ChunkSocket([payload[:2], payload[2:] + b"\n"])
    handler = object.__new__(ReceptorTcpHandler)
    handler.request = sock

    assert handler._read_message(b"") == payload
    assert sock.reads > 2


def test_reverse_read_keeps_second_message_without_another_recv():
    first = protocolo.base_message(protocolo.MessageType.PING)
    first["token"] = "secret"
    second = protocolo.base_message(protocolo.MessageType.PING)
    second["token"] = "second"
    sock = ChunkSocket([protocolo.frame(first) + protocolo.frame(second)])
    connection = object.__new__(ReverseConnection)
    connection._sock = sock
    connection.host = "localhost"

    message, remaining = connection._ler(b"")
    assert message == first
    message, remaining = connection._ler(remaining)
    assert message == second
    assert remaining == b""
    assert sock.reads == 1


def test_partial_frame_at_eof_is_not_accepted_by_reverse_connection():
    sock = ChunkSocket([b'{"type":"ping"'])
    assert read_framed_message(sock, b"") == (None, b'{"type":"ping"')


def test_frame_size_limit_is_checked_before_full_message(monkeypatch):
    monkeypatch.setattr(protocolo, "MAX_TCP_MESSAGE_BYTES", 100)
    sock = ChunkSocket([b"x" * 101])
    with pytest.raises(protocolo.ProtocolError):
        read_framed_message(sock, b"")
