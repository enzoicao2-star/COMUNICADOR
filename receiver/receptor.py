#!/usr/bin/env python3
"""receptor.py — recebe avisos do painel Comunicador (C#) via TCP/JSON.

Roda em segundo plano (pensado para pythonw.exe, sem console), aceita
conexões concorrentes de vários painéis ao mesmo tempo, responde a
descoberta UDP e mostra os avisos numa janela simples do Tkinter.

Uso normal (produção):
    pythonw.exe receptor.py

Uso em teste automatizado (sem GUI, respostas simuladas):
    python receptor.py --test-mode --port 0 --udp-port 0 --config-dir <tmp>
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import queue
import socket
import socketserver
import sys
import threading
import time
import uuid
import webbrowser
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

import protocolo
from protocolo import ErrorCode, MessageType, ProtocolError

APP_NAME = "Comunicador Receptor"
REPLY_WAIT_SECONDS = 300
PANEL_RESCAN_SECONDS = 30
NO_REPLY_AUTO_CLOSE_SECONDS = 20


# --------------------------------------------------------------------------- config

def default_config_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home())
    return Path(base) / "Comunicador" / "Receptor"


class Config:
    def __init__(self, directory: Path):
        self.directory = directory
        self.path = directory / "config.json"
        self.data: dict = {}
        self._lock = threading.Lock()
        self.load()

    def load(self) -> None:
        self.directory.mkdir(parents=True, exist_ok=True)
        if self.path.exists():
            try:
                self.data = json.loads(self.path.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError):
                self.data = {}
        else:
            self.data = {}

        changed = False
        if "computer_id" not in self.data:
            self.data["computer_id"] = str(uuid.uuid4())
            changed = True
        if "computer_name" not in self.data:
            self.data["computer_name"] = socket.gethostname()
            changed = True
        if "paired_panels" not in self.data:
            self.data["paired_panels"] = {}
            changed = True
        if changed:
            self.save()

    def save(self) -> None:
        with self._lock:
            self.directory.mkdir(parents=True, exist_ok=True)
            tmp = self.path.with_suffix(".tmp")
            tmp.write_text(json.dumps(self.data, indent=2, ensure_ascii=False), encoding="utf-8")
            tmp.replace(self.path)

    @property
    def computer_id(self) -> str:
        return self.data["computer_id"]

    @property
    def computer_name(self) -> str:
        return self.data["computer_name"]

    @property
    def paired_panels(self) -> dict:
        return self.data["paired_panels"]

    def is_paired_with(self, panel_id: str) -> bool:
        return panel_id in self.paired_panels

    def token_is_valid(self, token: str) -> bool:
        return any(p.get("token") == token for p in self.paired_panels.values())

    def pair(self, panel_id: str, panel_name: str) -> str:
        token = uuid.uuid4().hex + uuid.uuid4().hex
        with self._lock:
            self.paired_panels[panel_id] = {
                "token": token,
                "panel_name": panel_name,
                "paired_at": datetime.now(timezone.utc).isoformat(),
            }
        self.save()
        return token

    @property
    def panel_hosts(self) -> list:
        """IPs de painéis para os quais este receptor disca (conexão reversa)."""
        return self.data.setdefault("panel_hosts", [])

    def remember_panel_host(self, host: str) -> None:
        if host and host not in self.panel_hosts:
            self.panel_hosts.append(host)
            self.save()

    def token_for_panel_host(self) -> Optional[str]:
        """Token de qualquer painel já pareado, usado ao se registrar novamente."""
        for p in self.paired_panels.values():
            if p.get("token"):
                return p["token"]
        return None

    def store_reverse_token(self, token: str, panel_name: str) -> None:
        """Guarda o token emitido pelo painel durante o registro reverso."""
        with self._lock:
            self.paired_panels[f"reverso:{panel_name}"] = {
                "token": token,
                "panel_name": panel_name,
                "paired_at": datetime.now(timezone.utc).isoformat(),
            }
        self.save()


# --------------------------------------------------------------------------- notificações (UI)

class NotificationUi:
    """Interface Tkinter das notificações. Roda inteiramente na thread principal;
    threads de rede só enfileiram pedidos via `mostrar()` (thread-safe)."""

    def __init__(self, test_mode: bool):
        self.test_mode = test_mode
        self._pending: "queue.Queue[tuple]" = queue.Queue()
        self._root = None
        if not test_mode:
            import tkinter as tk
            self._tk = tk
            self._root = tk.Tk()
            self._root.withdraw()
            self._root.after(100, self._poll)

    def mostrar(self, sender: str, title: str, message: str, allow_reply: bool, on_result, buttons=None):
        """Agenda a exibição de um aviso. `on_result(reply_text_or_None)` é chamado
        quando o usuário responde, clica num botão, fecha a janela, ou o tempo esgota."""
        if self.test_mode:
            on_result("Recebido automaticamente (modo de teste)." if allow_reply else None)
            return
        self._pending.put((sender, title, message, allow_reply, on_result, buttons or []))

    def _poll(self):
        try:
            while True:
                item = self._pending.get_nowait()
                self._exibir_janela(*item)
        except queue.Empty:
            pass
        self._root.after(100, self._poll)

    def _exibir_janela(self, sender, title, message, allow_reply, on_result, buttons=None):
        tk = self._tk
        win = tk.Toplevel(self._root)
        win.title(APP_NAME)
        win.attributes("-topmost", True)
        win.resizable(False, False)
        win.geometry("360x220+80+80")

        result_holder = {"done": False}

        def finish(value):
            if result_holder["done"]:
                return
            result_holder["done"] = True
            try:
                win.destroy()
            except tk.TclError:
                pass
            on_result(value)

        tk.Label(win, text=f"De: {sender}", font=("Segoe UI", 9), fg="#6B7280").pack(anchor="w", padx=14, pady=(14, 0))
        tk.Label(win, text=title, font=("Segoe UI", 12, "bold"), wraplength=330, justify="left").pack(
            anchor="w", padx=14, pady=(2, 6))
        tk.Label(win, text=message, font=("Segoe UI", 10), wraplength=330, justify="left").pack(
            anchor="w", padx=14)

        # Botões de resposta rápida enviados junto com o aviso. Se o botão tiver
        # link, além de responder ele abre o endereço no navegador padrão — e a
        # URL é revalidada aqui, porque veio pela rede.
        for botao in (buttons or []):
            rotulo = botao.get("label", "")
            url = botao.get("url")
            texto = f"{rotulo}  ↗" if url else rotulo

            def ao_clicar(rot=rotulo, endereco=url):
                if endereco:
                    if protocolo.url_permitida(endereco):
                        webbrowser.open(endereco)
                    else:
                        logging.warning("Link recusado no botão '%s': só http/https.", rot)
                finish(rot)

            tk.Button(win, text=texto, font=("Segoe UI", 10), command=ao_clicar).pack(
                fill="x", padx=14, pady=(6, 0))

        if allow_reply:
            entry = tk.Entry(win, font=("Segoe UI", 10))
            entry.pack(fill="x", padx=14, pady=(12, 6))
            entry.focus_set()

            btns = tk.Frame(win)
            btns.pack(pady=6)
            tk.Button(btns, text="Responder", width=12, command=lambda: finish(entry.get())).pack(side="left", padx=4)
            tk.Button(btns, text="Fechar", width=12, command=lambda: finish(None)).pack(side="left", padx=4)
            entry.bind("<Return>", lambda _e: finish(entry.get()))
        else:
            tk.Button(win, text="OK", width=12, command=lambda: finish(None)).pack(pady=14)
            win.after(NO_REPLY_AUTO_CLOSE_SECONDS * 1000, lambda: finish(None))

        win.protocol("WM_DELETE_WINDOW", lambda: finish(None))

    def run_forever(self):
        if self._root is not None:
            self._root.mainloop()
        else:
            try:
                while True:
                    time.sleep(0.5)
            except KeyboardInterrupt:
                pass

    def stop(self):
        if self._root is not None:
            self._root.after(0, self._root.quit)


# --------------------------------------------------------------------------- TCP

class ReceptorTcpHandler(socketserver.BaseRequestHandler):
    def handle(self):
        server: ReceptorTcpServer = self.server  # type: ignore[assignment]
        self.request.settimeout(REPLY_WAIT_SECONDS + 10)
        buffer = b""

        try:
            payload = self._read_message(buffer)
        except (ProtocolError, ConnectionError, OSError) as exc:
            self._safe_send_error(exc)
            return
        if payload is None:
            return

        try:
            msg = protocolo.parse_and_validate(payload, is_udp=False)
        except ProtocolError as exc:
            logging.warning("Mensagem inválida de %s: %s", self.client_address, exc)
            self._safe_send(protocolo.make_error(exc.code, exc.message))
            return

        msg_type = msg.get("type")
        logging.info("TCP %s de %s (id=%s)", msg_type, self.client_address, msg.get("id"))

        try:
            if msg_type == MessageType.PING:
                self._handle_ping(msg, server)
            elif msg_type == MessageType.PAIR_REQUEST:
                self._handle_pair_request(msg, server)
            elif msg_type == MessageType.NOTIFICATION:
                self._handle_notification(msg, server)
            else:
                raise ProtocolError(
                    ErrorCode.UNKNOWN_TYPE, f"Tipo não esperado nesta conexão: '{msg_type}'")
        except ProtocolError as exc:
            logging.warning("Erro de protocolo (%s): %s", self.client_address, exc)
            self._safe_send(protocolo.make_error(exc.code, exc.message, in_reply_to=msg.get("id")))

    def _read_message(self, buffer: bytes) -> Optional[bytes]:
        sock = self.request
        while True:
            chunk = sock.recv(4096)
            if not chunk:
                return None if not buffer else buffer
            buffer += chunk
            protocolo.validate_size(len(buffer), is_udp=False)
            if b"\n" in buffer:
                line, _, _rest = buffer.partition(b"\n")
                return line

    def _handle_ping(self, msg: dict, server: "ReceptorTcpServer") -> None:
        token = msg["token"]
        if not server.config.token_is_valid(token):
            raise ProtocolError(ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.")

        pong = protocolo.base_message(MessageType.PONG)
        pong["computer_id"] = server.config.computer_id
        pong["computer_name"] = server.config.computer_name
        pong["status"] = "online"
        self._safe_send(pong)

    def _handle_pair_request(self, msg: dict, server: "ReceptorTcpServer") -> None:
        panel_id = msg["panel_id"]
        panel_name = msg["panel_name"]
        token = server.config.pair(panel_id, panel_name)
        logging.info("Pareado com painel '%s' (%s)", panel_name, panel_id)

        response = protocolo.base_message(MessageType.PAIR_RESPONSE)
        response["accepted"] = True
        response["computer_id"] = server.config.computer_id
        response["computer_name"] = server.config.computer_name
        response["token"] = token
        self._safe_send(response)

    def _handle_notification(self, msg: dict, server: "ReceptorTcpServer") -> None:
        token = msg["token"]
        if not server.config.token_is_valid(token):
            raise ProtocolError(ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.")

        allow_reply = msg["allow_reply"]
        notification_id = msg["id"]
        result_queue: "queue.Queue[Optional[str]]" = queue.Queue()

        server.ui.mostrar(
            sender=msg["sender"], title=msg["title"], message=msg["message"],
            allow_reply=allow_reply, on_result=lambda value: result_queue.put(value),
            buttons=msg.get("buttons"))

        ack = protocolo.base_message(MessageType.ACK)
        ack["in_reply_to"] = notification_id
        ack["status"] = "shown"
        self._safe_send(ack)

        if not allow_reply:
            return

        try:
            reply_text = result_queue.get(timeout=REPLY_WAIT_SECONDS)
        except queue.Empty:
            return

        if reply_text is None:
            return

        reply = protocolo.base_message(MessageType.REPLY)
        reply["in_reply_to"] = notification_id
        reply["computer_id"] = server.config.computer_id
        reply["computer_name"] = server.config.computer_name
        reply["reply_text"] = reply_text
        self._safe_send(reply)

    def _safe_send(self, message: dict) -> None:
        try:
            self.request.sendall(protocolo.frame(message))
        except OSError:
            pass

    def _safe_send_error(self, exc: Exception) -> None:
        code = exc.code if isinstance(exc, ProtocolError) else ErrorCode.INVALID_JSON
        message = str(exc)
        self._safe_send(protocolo.make_error(code, message))


class ReceptorTcpServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, config: Config, ui: NotificationUi):
        super().__init__(address, ReceptorTcpHandler)
        self.config = config
        self.ui = ui


# --------------------------------------------------------------------------- UDP (descoberta)

class DiscoveryResponder(threading.Thread):
    def __init__(self, address, config: Config, tcp_port: int):
        super().__init__(daemon=True)
        self.config = config
        self.tcp_port = tcp_port
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._sock.bind(address)
        self._running = True

    @property
    def bound_port(self) -> int:
        return self._sock.getsockname()[1]

    def run(self) -> None:
        while self._running:
            try:
                data, addr = self._sock.recvfrom(4096)
            except OSError:
                return

            try:
                msg = protocolo.parse_and_validate(data, is_udp=True)
            except ProtocolError as exc:
                logging.debug("Pacote UDP inválido de %s: %s", addr, exc)
                continue

            if msg.get("type") != MessageType.DISCOVER:
                continue

            announce = protocolo.base_message(MessageType.ANNOUNCE)
            announce["computer_id"] = self.config.computer_id
            announce["computer_name"] = self.config.computer_name
            announce["tcp_port"] = self.tcp_port
            announce["paired"] = self.config.is_paired_with(msg["panel_id"])

            try:
                self._sock.sendto(protocolo.frame(announce)[: protocolo.MAX_UDP_MESSAGE_BYTES], addr)
            except OSError as exc:
                logging.warning("Falha ao responder descoberta para %s: %s", addr, exc)

    def stop(self) -> None:
        self._running = False
        try:
            self._sock.close()
        except OSError:
            pass


# --------------------------------------------------------------------------- conexão reversa

class ReverseConnection(threading.Thread):
    """Disca do receptor PARA o painel e mantém a conexão aberta.

    É o caminho que dispensa qualquer porta de entrada liberada nesta máquina:
    quem inicia a conexão é o receptor, e firewall doméstico praticamente nunca
    bloqueia conexões de saída. O painel manda as notificações de volta por
    essa mesma conexão, e as respostas do usuário sobem por ela.
    """

    RECONNECT_SECONDS = 15

    def __init__(self, config: Config, ui: "NotificationUi", host: str, port: int):
        super().__init__(daemon=True)
        self.config = config
        self.ui = ui
        self.host = host
        self.port = port
        self._running = True
        self._sock: Optional[socket.socket] = None

    def stop(self) -> None:
        self._running = False
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass

    def run(self) -> None:
        while self._running:
            try:
                self._sessao()
            except (OSError, ProtocolError) as exc:
                logging.debug("Conexão reversa com %s:%s caiu: %s", self.host, self.port, exc)
            finally:
                if self._sock is not None:
                    try:
                        self._sock.close()
                    except OSError:
                        pass
                    self._sock = None

            if not self._running:
                return

            for _ in range(self.RECONNECT_SECONDS):
                if not self._running:
                    return
                time.sleep(1)

    def _sessao(self) -> None:
        self._sock = socket.create_connection((self.host, self.port), timeout=10)
        self._sock.settimeout(None)

        registro = protocolo.base_message(MessageType.REGISTER)
        registro["computer_id"] = self.config.computer_id
        registro["computer_name"] = self.config.computer_name
        token = self.config.token_for_panel_host()
        if token:
            registro["token"] = token
        self._sock.sendall(protocolo.frame(registro))

        buffer = b""
        resposta, buffer = self._ler(buffer)

        # O host pode ser outro RECEPTOR (também escuta nesta porta) e não um
        # painel: nesse caso ele responde UNKNOWN_TYPE. Desistimos dele de vez,
        # em vez de ficar reconectando para sempre.
        if resposta is not None and resposta.get("type") == MessageType.ERROR:
            logging.info(
                "%s:%s não é um painel (%s); parando de tentar.",
                self.host, self.port, resposta.get("code"))
            self._running = False
            return

        if resposta is None or resposta.get("type") != MessageType.REGISTER_ACK:
            tipo = resposta.get("type") if resposta else "nenhuma"
            logging.warning("Registro recusado por %s:%s (resposta: %s)", self.host, self.port, tipo)
            return

        if resposta.get("accepted") is not True:
            logging.warning("Painel %s recusou o registro.", self.host)
            self._running = False
            return

        self.config.store_reverse_token(resposta["token"], resposta.get("computer_name", self.host))
        self.config.remember_panel_host(self.host)
        logging.info("Registrado no painel %s:%s via conexão reversa.", self.host, self.port)

        # A partir daqui a conexão fica aberta recebendo notificações do painel.
        while self._running:
            msg, buffer = self._ler(buffer)
            if msg is None:
                return
            if msg.get("type") == MessageType.NOTIFICATION:
                self._tratar_notificacao(msg)

    def _tratar_notificacao(self, msg: dict) -> None:
        allow_reply = msg.get("allow_reply", False)
        notification_id = msg["id"]
        result_queue: "queue.Queue[Optional[str]]" = queue.Queue()

        self.ui.mostrar(
            sender=msg.get("sender", "Painel"), title=msg.get("title", ""),
            message=msg.get("message", ""), allow_reply=allow_reply,
            on_result=lambda value: result_queue.put(value),
            buttons=msg.get("buttons"))

        ack = protocolo.base_message(MessageType.ACK)
        ack["in_reply_to"] = notification_id
        ack["status"] = "shown"
        self._enviar(ack)

        if not allow_reply:
            return

        try:
            reply_text = result_queue.get(timeout=REPLY_WAIT_SECONDS)
        except queue.Empty:
            return

        if reply_text is None:
            return

        reply = protocolo.base_message(MessageType.REPLY)
        reply["in_reply_to"] = notification_id
        reply["computer_id"] = self.config.computer_id
        reply["computer_name"] = self.config.computer_name
        reply["reply_text"] = reply_text
        self._enviar(reply)

    def _enviar(self, mensagem: dict) -> None:
        if self._sock is None:
            return
        try:
            self._sock.sendall(protocolo.frame(mensagem))
        except OSError:
            pass

    def _ler(self, buffer: bytes):
        """Lê uma mensagem completa, devolvendo (mensagem, buffer_restante)."""
        while b"\n" not in buffer:
            if self._sock is None:
                return None, buffer
            chunk = self._sock.recv(4096)
            if not chunk:
                return None, buffer
            buffer += chunk
            protocolo.validate_size(len(buffer), is_udp=False)

        linha, _, resto = buffer.partition(b"\n")
        try:
            msg = protocolo.parse(linha)
            protocolo.validate(msg)
            return msg, resto
        except ProtocolError as exc:
            logging.warning("Mensagem inválida do painel %s: %s", self.host, exc)
            return None, resto


def descobrir_paineis(port: int, timeout: float = 4.0) -> list:
    """Procura painéis na LAN testando a porta conhecida do protocolo.

    Usado quando o receptor ainda não sabe o IP de nenhum painel — evita
    exigir configuração manual do endereço.
    """
    encontrados = []
    redes = set()

    for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
        ip = info[4][0]
        if ip.startswith("127."):
            continue
        redes.add(".".join(ip.split(".")[:3]))

    def testar(alvo: str) -> None:
        try:
            with socket.create_connection((alvo, port), timeout=timeout):
                encontrados.append(alvo)
        except OSError:
            pass

    threads = []
    for rede in redes:
        for ultimo in range(1, 255):
            alvo = f"{rede}.{ultimo}"
            t = threading.Thread(target=testar, args=(alvo,), daemon=True)
            t.start()
            threads.append(t)

    for t in threads:
        t.join(timeout=timeout + 1)

    return encontrados


# --------------------------------------------------------------------------- bandeja (opcional)

def start_tray_icon(config: Config, on_exit) -> Optional[object]:
    try:
        import pystray
        from PIL import Image, ImageDraw
    except ImportError:
        logging.info("pystray/Pillow não instalados — ícone de bandeja desabilitado.")
        return None

    image = Image.new("RGB", (64, 64), "#1F2430")
    draw = ImageDraw.Draw(image)
    draw.ellipse((14, 14, 50, 50), fill="#3A7AFE")

    menu = pystray.Menu(
        pystray.MenuItem(f"Pareado com {len(config.paired_panels)} painel(éis)", None, enabled=False),
        pystray.MenuItem(f"Computador: {config.computer_name}", None, enabled=False),
        pystray.MenuItem("Sair", lambda icon, _item: (icon.stop(), on_exit())),
    )
    icon = pystray.Icon(APP_NAME, image, APP_NAME, menu)
    threading.Thread(target=icon.run, daemon=True).start()
    return icon


# --------------------------------------------------------------------------- main

def setup_logging(config_dir: Path, test_mode: bool) -> None:
    handlers = [logging.FileHandler(config_dir / "receptor.log", encoding="utf-8")]
    if test_mode:
        handlers.append(logging.StreamHandler(sys.stdout))
    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", handlers=handlers)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Comunicador Receptor")
    parser.add_argument("--port", type=int, default=protocolo.TCP_PORT)
    parser.add_argument("--udp-port", type=int, default=protocolo.UDP_DISCOVERY_PORT)
    parser.add_argument("--config-dir", type=str, default=None)
    parser.add_argument("--computer-name", type=str, default=None)
    parser.add_argument("--test-mode", action="store_true")
    parser.add_argument(
        "--painel", action="append", default=None, metavar="IP",
        help="IP de um painel para conexao reversa (pode repetir). Se omitido, "
             "o receptor procura paineis na rede automaticamente.")
    parser.add_argument(
        "--painel-porta", type=int, default=None,
        help="Porta TCP em que o painel escuta (padrao: a mesma de --port).")
    parser.add_argument(
        "--sem-conexao-reversa", action="store_true",
        help="Nao disca para paineis; so espera conexoes de entrada.")
    return parser.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    config_dir = Path(args.config_dir) if args.config_dir else default_config_dir()
    setup_logging(config_dir, args.test_mode)

    config = Config(config_dir)
    if args.computer_name:
        config.data["computer_name"] = args.computer_name
        config.save()

    ui = NotificationUi(test_mode=args.test_mode)
    tcp_server = ReceptorTcpServer(("0.0.0.0", args.port), config, ui)
    tcp_thread = threading.Thread(target=tcp_server.serve_forever, daemon=True)
    tcp_thread.start()

    discovery = DiscoveryResponder(("0.0.0.0", args.udp_port), config, tcp_server.server_address[1])
    discovery.start()

    # Conexão reversa: o receptor disca para o painel e mantém a conexão aberta.
    # Assim esta máquina não precisa de nenhuma porta de entrada liberada.
    conexoes_reversas = []

    porta_painel = args.painel_porta or args.port

    def iniciar_conexoes_reversas():
        """Mantém conexão com os painéis, procurando de novo enquanto não achar.

        A varredura precisa repetir: é normal o painel ainda não estar aberto
        quando o receptor sobe (ex.: logo após o login). Procurar só uma vez
        deixaria o receptor invisível até alguém reiniciá-lo.
        """
        tentando = set()

        while True:
            # Descarta as conexões que já morreram para não acumular threads
            # nem impedir uma nova tentativa para o mesmo endereço.
            vivas = [c for c in conexoes_reversas if c.is_alive()]
            conexoes_reversas[:] = vivas
            tentando &= {c.host for c in vivas}

            hosts = list(args.painel) if args.painel else list(config.panel_hosts)

            # Sem nenhuma conexão viva, vale varrer a rede de novo: o painel
            # pode ter aberto agora, ou mudado de IP desde a última vez.
            if not vivas:
                encontrados = descobrir_paineis(porta_painel)
                if encontrados:
                    logging.info("Painéis encontrados na rede: %s", encontrados)
                for host in encontrados:
                    if host not in hosts:
                        hosts.append(host)

            for host in hosts:
                if host in tentando:
                    continue
                tentando.add(host)
                conexao = ReverseConnection(config, ui, host, porta_painel)
                conexao.start()
                conexoes_reversas.append(conexao)

            time.sleep(PANEL_RESCAN_SECONDS)

    if args.painel:
        logging.info("Painel informado explicitamente: %s", args.painel)

    # Em modo de teste so conectamos se o painel foi informado explicitamente,
    # para os testes automatizados poderem exercitar este caminho sem varrer a rede.
    reverso_habilitado = not args.sem_conexao_reversa and (not args.test_mode or args.painel)
    if reverso_habilitado:
        # em thread separada: a varredura da rede leva alguns segundos e o
        # receptor ja deve estar respondendo enquanto isso.
        threading.Thread(target=iniciar_conexoes_reversas, daemon=True).start()

    def shutdown():
        for conexao in conexoes_reversas:
            conexao.stop()
        discovery.stop()
        tcp_server.shutdown()
        tcp_server.server_close()
        ui.stop()

    tray_icon = None
    if not args.test_mode:
        tray_icon = start_tray_icon(config, shutdown)

    logging.info(
        "Comunicador Receptor pronto. computer_id=%s nome=%s tcp=%s udp=%s",
        config.computer_id, config.computer_name, tcp_server.server_address[1], discovery.bound_port)

    if args.test_mode:
        print(f"COMUNICADOR_RECEPTOR_READY tcp={tcp_server.server_address[1]} udp={discovery.bound_port}", flush=True)

    try:
        ui.run_forever()
    except KeyboardInterrupt:
        pass
    finally:
        shutdown()
        if tray_icon is not None:
            tray_icon.stop()

    return 0


if __name__ == "__main__":
    sys.exit(main())
