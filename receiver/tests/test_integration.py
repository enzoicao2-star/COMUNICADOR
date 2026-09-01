"""Testes de integração ponta-a-ponta: sobem receptor.py de verdade (--test-mode,
sem GUI) como subprocesso e conversam com ele por sockets TCP crus, exercitando
o protocolo exatamente como o painel C# faria."""

import json
import socket
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
from protocolo import MessageType  # noqa: E402

RECEPTOR_PATH = Path(__file__).resolve().parent.parent / "receptor.py"


@pytest.fixture()
def receptor(tmp_path):
    proc = subprocess.Popen(
        [sys.executable, str(RECEPTOR_PATH), "--test-mode", "--port", "0", "--udp-port", "0",
         "--config-dir", str(tmp_path), "--computer-name", "TESTE-PC"],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1,
    )
    tcp_port = None
    deadline = time.time() + 15
    try:
        while time.time() < deadline:
            line = proc.stdout.readline()
            if not line:
                if proc.poll() is not None:
                    raise RuntimeError("receptor.py encerrou antes de ficar pronto")
                continue
            if line.startswith("COMUNICADOR_RECEPTOR_READY"):
                parts = dict(p.split("=") for p in line.strip().split()[1:])
                tcp_port = int(parts["tcp"])
                break
        if tcp_port is None:
            raise RuntimeError("Tempo esgotado esperando receptor.py ficar pronto")
        yield {"port": tcp_port, "proc": proc}
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


def send_and_receive(port, message, timeout=10):
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as sock:
        sock.sendall(protocolo.frame(message))
        sock.settimeout(timeout)
        buffer = b""
        while b"\n" not in buffer:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buffer += chunk
        line, _, _ = buffer.partition(b"\n")
        return json.loads(line.decode("utf-8")) if line else None


def pair(port, panel_id=None, panel_name="PAINEL-TESTE"):
    msg = protocolo.base_message(MessageType.PAIR_REQUEST)
    msg["panel_id"] = panel_id or str(uuid.uuid4())
    msg["panel_name"] = panel_name
    response = send_and_receive(port, msg)
    assert response["type"] == MessageType.PAIR_RESPONSE
    assert response["accepted"] is True
    return response["token"]


def test_pareamento(receptor):
    token = pair(receptor["port"])
    assert len(token) > 0


def test_ping_pong_apos_pareamento(receptor):
    token = pair(receptor["port"])
    msg = protocolo.base_message(MessageType.PING)
    msg["token"] = token
    response = send_and_receive(receptor["port"], msg)
    assert response["type"] == MessageType.PONG
    assert response["status"] == "online"


def test_ping_sem_pareamento_e_recusado(receptor):
    msg = protocolo.base_message(MessageType.PING)
    msg["token"] = "token-invalido"
    response = send_and_receive(receptor["port"], msg)
    assert response["type"] == MessageType.ERROR
    assert response["code"] == "UNAUTHORIZED"


def test_notificacao_com_resposta_automatica_em_modo_teste(receptor):
    token = pair(receptor["port"])
    notif = protocolo.base_message(MessageType.NOTIFICATION)
    notif["token"] = token
    notif["sender"] = "PAINEL-TESTE"
    notif["title"] = "Aviso"
    notif["message"] = "Mensagem de teste"
    notif["allow_reply"] = True

    with socket.create_connection(("127.0.0.1", receptor["port"]), timeout=10) as sock:
        sock.sendall(protocolo.frame(notif))
        sock.settimeout(10)
        buffer = b""
        messages = []
        while len(messages) < 2:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buffer += chunk
            while b"\n" in buffer:
                line, _, buffer = buffer.partition(b"\n")
                messages.append(json.loads(line.decode("utf-8")))

    assert messages[0]["type"] == MessageType.ACK
    assert messages[0]["in_reply_to"] == notif["id"]
    assert messages[1]["type"] == MessageType.REPLY
    assert messages[1]["in_reply_to"] == notif["id"]
    assert messages[1]["reply_text"]


def test_notificacao_sem_permitir_resposta(receptor):
    token = pair(receptor["port"])
    notif = protocolo.base_message(MessageType.NOTIFICATION)
    notif["token"] = token
    notif["sender"] = "PAINEL-TESTE"
    notif["title"] = "Aviso"
    notif["message"] = "Sem resposta"
    notif["allow_reply"] = False
    response = send_and_receive(receptor["port"], notif)
    assert response["type"] == MessageType.ACK


def test_json_invalido(receptor):
    with socket.create_connection(("127.0.0.1", receptor["port"]), timeout=5) as sock:
        sock.sendall(b"{isso nao e json valido\n")
        sock.settimeout(5)
        buffer = b""
        while b"\n" not in buffer:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buffer += chunk
        line, _, _ = buffer.partition(b"\n")
        response = json.loads(line.decode("utf-8"))
    assert response["type"] == MessageType.ERROR
    assert response["code"] == "INVALID_JSON"


def test_campos_ausentes(receptor):
    token = pair(receptor["port"])
    notif = protocolo.base_message(MessageType.NOTIFICATION)
    notif["token"] = token
    notif["sender"] = "PAINEL-TESTE"
    response = send_and_receive(receptor["port"], notif)
    assert response["type"] == MessageType.ERROR
    assert response["code"] == "MISSING_FIELD"


def test_multiplos_paineis_pareiam_independentemente(receptor):
    token_a = pair(receptor["port"], panel_name="PAINEL-A")
    token_b = pair(receptor["port"], panel_name="PAINEL-B")
    assert token_a != token_b

    for token in (token_a, token_b):
        msg = protocolo.base_message(MessageType.PING)
        msg["token"] = token
        response = send_and_receive(receptor["port"], msg)
        assert response["type"] == MessageType.PONG


def test_conexoes_concorrentes_de_varios_paineis(receptor):
    token = pair(receptor["port"])
    resultados = []
    lock = threading.Lock()

    def enviar(i):
        notif = protocolo.base_message(MessageType.NOTIFICATION)
        notif["token"] = token
        notif["sender"] = f"PAINEL-{i}"
        notif["title"] = f"Aviso {i}"
        notif["message"] = "Concorrente"
        notif["allow_reply"] = False
        resultado = send_and_receive(receptor["port"], notif)
        with lock:
            resultados.append(resultado)

    threads = [threading.Thread(target=enviar, args=(i,)) for i in range(5)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=10)

    assert len(resultados) == 5
    assert all(r["type"] == MessageType.ACK for r in resultados)


def test_reconexao_apos_fechar_conexao(receptor):
    token = pair(receptor["port"])
    for _ in range(3):
        msg = protocolo.base_message(MessageType.PING)
        msg["token"] = token
        response = send_and_receive(receptor["port"], msg)
        assert response["type"] == MessageType.PONG


def test_computador_offline_conexao_e_recusada():
    with pytest.raises(OSError):
        socket.create_connection(("127.0.0.1", 1), timeout=2)
