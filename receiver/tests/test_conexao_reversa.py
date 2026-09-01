"""Testa a conexao reversa com o receptor.py REAL: quem disca e o receptor.

Sobe um painel minimo que fala o mesmo protocolo do painel C# e verifica o
fluxo completo — register -> register_ack -> notification -> ack -> reply —
tudo pela mesma conexao aberta pelo receptor.
"""

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


def _ler(sock, buffer):
    while b"\n" not in buffer:
        chunk = sock.recv(4096)
        if not chunk:
            return None, buffer
        buffer += chunk
    linha, _, resto = buffer.partition(b"\n")
    return json.loads(linha.decode("utf-8")), resto


class PainelFake:
    """Painel minimo: aceita o register e conversa pela mesma conexao."""

    def __init__(self, permitir_resposta=True):
        self.permitir_resposta = permitir_resposta
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.listen(5)
        self.sock.settimeout(30)
        self.porta = self.sock.getsockname()[1]
        self.registro = None
        self.ack = None
        self.reply = None
        self.erro = None
        self._thread = threading.Thread(target=self._rodar, daemon=True)

    def start(self):
        self._thread.start()

    def join(self, timeout=30):
        self._thread.join(timeout=timeout)

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass

    def _rodar(self):
        try:
            conn, _addr = self.sock.accept()
        except socket.timeout:
            self.erro = "o receptor nunca discou para o painel"
            return

        with conn:
            buffer = b""
            msg, buffer = _ler(conn, buffer)
            if not msg or msg.get("type") != MessageType.REGISTER:
                self.erro = f"esperava register, veio {msg and msg.get('type')}"
                return
            self.registro = msg

            token = uuid.uuid4().hex
            ack = protocolo.base_message(MessageType.REGISTER_ACK)
            ack["accepted"] = True
            ack["token"] = token
            ack["computer_id"] = str(uuid.uuid4())
            ack["computer_name"] = "PAINEL-FAKE"
            conn.sendall(protocolo.frame(ack))

            notif = protocolo.base_message(MessageType.NOTIFICATION)
            notif["token"] = token
            notif["sender"] = "PAINEL-FAKE"
            notif["title"] = "Teste reverso"
            notif["message"] = "Chegou pela conexao reversa?"
            notif["allow_reply"] = self.permitir_resposta
            conn.sendall(protocolo.frame(notif))

            conn.settimeout(20)
            try:
                m1, buffer = _ler(conn, buffer)
                if m1 and m1.get("type") == MessageType.ACK:
                    self.ack = m1
                    if self.permitir_resposta:
                        m2, buffer = _ler(conn, buffer)
                        if m2 and m2.get("type") == MessageType.REPLY:
                            self.reply = m2
            except socket.timeout:
                self.erro = "timeout esperando ack/reply"


@pytest.fixture()
def painel_e_receptor(tmp_path):
    criados = {}

    def _iniciar(permitir_resposta=True):
        painel = PainelFake(permitir_resposta=permitir_resposta)
        painel.start()
        time.sleep(0.3)

        proc = subprocess.Popen(
            [sys.executable, str(RECEPTOR_PATH), "--test-mode",
             "--port", "0", "--udp-port", "0",
             "--config-dir", str(tmp_path), "--computer-name", "PC-TESTE-REVERSO",
             "--painel", "127.0.0.1", "--painel-porta", str(painel.porta)],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)

        criados["painel"] = painel
        criados["proc"] = proc
        painel.join(timeout=30)
        return painel

    yield _iniciar

    proc = criados.get("proc")
    if proc is not None:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
    painel = criados.get("painel")
    if painel is not None:
        painel.close()


def test_receptor_disca_e_se_registra_no_painel(painel_e_receptor):
    painel = painel_e_receptor()
    assert painel.erro is None, painel.erro
    assert painel.registro is not None
    assert painel.registro["computer_name"] == "PC-TESTE-REVERSO"
    assert painel.registro["computer_id"]


def test_notificacao_chega_pela_conexao_reversa_e_recebe_ack(painel_e_receptor):
    painel = painel_e_receptor()
    assert painel.erro is None, painel.erro
    assert painel.ack is not None
    assert painel.ack["status"] == "shown"


def test_resposta_do_usuario_sobe_pela_conexao_reversa(painel_e_receptor):
    painel = painel_e_receptor()
    assert painel.erro is None, painel.erro
    assert painel.reply is not None
    assert painel.reply["reply_text"]
    assert painel.reply["computer_name"] == "PC-TESTE-REVERSO"


def test_sem_permitir_resposta_recebe_so_o_ack(painel_e_receptor):
    painel = painel_e_receptor(permitir_resposta=False)
    assert painel.erro is None, painel.erro
    assert painel.ack is not None
    assert painel.reply is None
