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
import base64
import io
import json
import logging
import os
import queue
import re
import socket
import socketserver
import subprocess
import sys
import tempfile
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
RECEIVER_VERSION = "2.4.0"
REPLY_WAIT_SECONDS = 300
PANEL_RESCAN_SECONDS = 30
NO_REPLY_AUTO_CLOSE_SECONDS = 20

MEDIA_PLAYER_SCRIPT = r'''param(
    [Parameter(Mandatory=$true)][string]$MediaPath,
    [Parameter(Mandatory=$true)][string]$Kind,
    [int]$X = 0, [int]$Y = 0, [int]$MonitorWidth = 1280, [int]$MonitorHeight = 720,
    [int]$WidthPercent = 70, [switch]$Loop, [int]$DurationSeconds = 0,
    [switch]$AllowClose
)
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
$window = New-Object System.Windows.Window
$window.WindowStyle = [System.Windows.WindowStyle]::None
$window.ResizeMode = [System.Windows.ResizeMode]::NoResize
$window.ShowInTaskbar = $false
$window.ShowActivated = $false
$window.Topmost = $true
$window.AllowsTransparency = $true
$window.Background = [System.Windows.Media.Brushes]::Transparent
$media = New-Object System.Windows.Controls.MediaElement
$media.LoadedBehavior = [System.Windows.Controls.MediaState]::Manual
$media.UnloadedBehavior = [System.Windows.Controls.MediaState]::Manual
$media.Stretch = [System.Windows.Media.Stretch]::Uniform
$media.Source = [Uri]::new($MediaPath)
$window.Content = $media
$isAudio = $Kind -eq 'audio'
if ($isAudio) {
    $window.Width = 1; $window.Height = 1; $window.Left = -32000; $window.Top = -32000
    $window.Opacity = 0; $window.IsHitTestVisible = $false
} else {
    $window.Width = [Math]::Max(160, $MonitorWidth * $WidthPercent / 100.0)
    $window.Height = [Math]::Min($MonitorHeight, [Math]::Max(90, $window.Width * 9.0 / 16.0))
    $window.Left = $X + ($MonitorWidth - $window.Width) / 2.0
    $window.Top = $Y + ($MonitorHeight - $window.Height) / 2.0
    $window.Opacity = 0
    if ($AllowClose) {
        $window.Cursor = [System.Windows.Input.Cursors]::Hand
        $window.Add_MouseLeftButtonDown({ $window.Close() })
        $window.Add_KeyDown({ param($sender, $eventArgs); if ($eventArgs.Key -eq [System.Windows.Input.Key]::Escape) { $window.Close() } })
    }
}
$media.add_MediaOpened({
    if (-not $isAudio -and $media.NaturalVideoWidth -gt 0 -and $media.NaturalVideoHeight -gt 0) {
        $maxW = [Math]::Max(1, $MonitorWidth * $WidthPercent / 100.0)
        $maxH = [Math]::Max(1, $MonitorHeight - 20)
        $scale = [Math]::Min($maxW / $media.NaturalVideoWidth, $maxH / $media.NaturalVideoHeight)
        $window.Width = [Math]::Max(1, $media.NaturalVideoWidth * $scale)
        $window.Height = [Math]::Max(1, $media.NaturalVideoHeight * $scale)
        $window.Left = $X + ($MonitorWidth - $window.Width) / 2.0
        $window.Top = $Y + ($MonitorHeight - $window.Height) / 2.0
        $window.Opacity = 1
    }
})
$media.add_MediaEnded({
    if ($Loop) { $media.Position = [TimeSpan]::Zero; $media.Play() }
    else { $window.Close() }
})
$media.add_MediaFailed({ $window.Close() })
$timer = $null
if ($DurationSeconds -gt 0) {
    $timer = New-Object System.Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromSeconds($DurationSeconds)
    $timer.Add_Tick({ $timer.Stop(); $window.Close() })
}
$window.Add_Loaded({ if ($timer) { $timer.Start() }; $media.Play() })
$window.Add_Closed({ if ($timer) { $timer.Stop() }; $media.Stop(); $media.Close() })
$null = $window.ShowDialog()
'''


def obter_monitores() -> list[dict]:
    """Coleta a topologia de monitores com APIs normais do Windows.

    O índice retornado também é usado para posicionar imagens recebidas. Em caso
    de falha, devolve o monitor principal para o painel continuar utilizável.
    """
    if os.name != "nt":
        return []

    try:
        import ctypes
        from ctypes import wintypes

        class MonitorInfoEx(ctypes.Structure):
            _fields_ = [
                ("cbSize", wintypes.DWORD),
                ("rcMonitor", wintypes.RECT),
                ("rcWork", wintypes.RECT),
                ("dwFlags", wintypes.DWORD),
                ("szDevice", wintypes.WCHAR * 32),
            ]

        encontrados = []
        callback_type = ctypes.WINFUNCTYPE(
            wintypes.BOOL, wintypes.HANDLE, wintypes.HDC,
            ctypes.POINTER(wintypes.RECT), wintypes.LPARAM)

        def callback(handle, _hdc, _rect, _data):
            info = MonitorInfoEx()
            info.cbSize = ctypes.sizeof(MonitorInfoEx)
            if ctypes.windll.user32.GetMonitorInfoW(handle, ctypes.byref(info)):
                rect = info.rcMonitor
                encontrados.append({
                    "index": len(encontrados),
                    "name": info.szDevice or f"Monitor {len(encontrados) + 1}",
                    "width": rect.right - rect.left,
                    "height": rect.bottom - rect.top,
                    "x": rect.left,
                    "y": rect.top,
                    "primary": bool(info.dwFlags & 1),
                })
            return True

        callback_ref = callback_type(callback)
        ctypes.windll.user32.EnumDisplayMonitors(None, None, callback_ref, 0)
        if encontrados:
            return encontrados[:protocolo.MAX_MONITORS]
    except Exception as exc:
        logging.warning("Não foi possível coletar os monitores: %s", exc)

    return [{
        "index": 0,
        "name": "Monitor principal",
        "width": 0,
        "height": 0,
        "x": 0,
        "y": 0,
        "primary": True,
    }]


def aplicar_atualizacao_oficial(msg: dict, test_mode: bool = False) -> str:
    """Valida sintaxe e substitui apenas receptor.py/protocolo.py no diretório atual.

    O pacote já passou pela validação de nome, tamanho e SHA-256 do protocolo.
    Mantemos cópias .bak e restauramos tudo se qualquer substituição falhar.
    """
    destino = Path(__file__).resolve().parent
    conteudos: dict[str, bytes] = {}
    for arquivo in msg["update_files"]:
        nome = arquivo["name"]
        dados = base64.b64decode(arquivo["content_base64"], validate=True)
        texto = dados.decode("utf-8", errors="strict")
        compile(texto, nome, "exec")
        conteudos[nome] = dados

    versao = msg["target_version"]
    if test_mode:
        logging.info("Atualização %s validada em modo de teste.", versao)
        return versao

    temporarios: dict[str, Path] = {}
    backups: dict[str, Path] = {}
    substituidos: list[str] = []
    try:
        for nome, dados in conteudos.items():
            temporario = destino / f".{nome}.update.tmp"
            with temporario.open("wb") as stream:
                stream.write(dados)
                stream.flush()
                os.fsync(stream.fileno())
            temporarios[nome] = temporario

        for nome in ("protocolo.py", "receptor.py"):
            atual = destino / nome
            backup = destino / f"{nome}.bak"
            if atual.exists():
                backup.write_bytes(atual.read_bytes())
                backups[nome] = backup
            os.replace(temporarios[nome], atual)
            substituidos.append(nome)

        logging.info("Receptor atualizado para %s; reinício agendado.", versao)
        return versao
    except Exception:
        for nome in reversed(substituidos):
            backup = backups.get(nome)
            if backup is not None and backup.exists():
                os.replace(backup, destino / nome)
        raise
    finally:
        for temporario in temporarios.values():
            try:
                temporario.unlink(missing_ok=True)
            except OSError:
                pass


def agendar_reinicio() -> None:
    """Reinicia o mesmo receptor depois de a confirmação chegar ao painel."""
    def reiniciar():
        time.sleep(1.0)
        script = str(Path(__file__).resolve())
        os.execv(sys.executable, [sys.executable, script, *sys.argv[1:]])

    threading.Thread(target=reiniciar, daemon=True, name="reinicio-atualizacao").start()


def coletar_logs_receptor(config: "Config", limite: int = protocolo.MAX_SYNC_ENTRIES) -> list[dict]:
    """Converte avisos/erros do arquivo local em registros deduplicáveis pelo painel."""
    caminho = config.directory / "receptor.log"
    try:
        linhas = caminho.read_text(encoding="utf-8", errors="replace").splitlines()[-5000:]
    except OSError:
        return []

    registros = []
    padrao = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),?\d* \[(WARNING|ERROR|CRITICAL)\] (.*)$")
    atual = None
    for linha in linhas:
        correspondencia = padrao.match(linha)
        if not correspondencia:
            if atual is not None and len(atual["details"]) < protocolo.MAX_LOG_DETAIL_LENGTH:
                atual["details"] = (atual["details"] + "\n" + linha)[:protocolo.MAX_LOG_DETAIL_LENGTH]
            continue
        data_texto, nivel_python, mensagem = correspondencia.groups()
        try:
            data = datetime.strptime(data_texto, "%Y-%m-%d %H:%M:%S").astimezone(timezone.utc)
            timestamp = data.isoformat().replace("+00:00", "Z")
        except ValueError:
            timestamp = protocolo.now_iso()
        identificador = str(uuid.uuid5(
            uuid.NAMESPACE_URL, f"comunicador:{config.computer_id}:{linha}"))
        atual = {
            "id": identificador,
            "timestamp": timestamp,
            "level": "erro" if nivel_python in {"ERROR", "CRITICAL"} else "aviso",
            "origin_type": "receiver",
            "origin_id": config.computer_id,
            "origin_name": config.computer_name,
            "category": "receptor",
            "message": mensagem[:protocolo.MAX_MESSAGE_LENGTH],
            "details": linha[:protocolo.MAX_LOG_DETAIL_LENGTH],
        }
        registros.append(atual)
    return list(reversed(registros[-limite:]))


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

    def mostrar(
            self, sender: str, title: str, message: str, allow_reply: bool, on_result,
            buttons=None, display_mode="toast", image=None, screen_images=None,
            image_duration_seconds=None, allow_manual_close=None, appearance=None,
            video=None, screen_videos=None, video_loop=False, audio=None, audio_loop=False):
        """Agenda a exibição de um aviso. `on_result(reply_text_or_None)` é chamado
        quando o usuário responde, clica num botão, fecha a janela, ou o tempo esgota."""
        if self.test_mode:
            resposta_teste = (buttons or [{}])[0].get("label") if buttons else (
                "Recebido automaticamente (modo de teste)." if allow_reply else None)
            on_result(resposta_teste)
            return
        self._pending.put((
            sender, title, message, allow_reply, on_result, buttons or [], display_mode,
            image, screen_images or [], image_duration_seconds, allow_manual_close,
            appearance or {}, video, screen_videos or [], video_loop, audio, audio_loop))

    def _poll(self):
        try:
            while True:
                item = self._pending.get_nowait()
                try:
                    self._exibir_janela(*item)
                except Exception:
                    logging.exception("Falha isolada ao montar uma janela de notificação.")
                    try:
                        item[4](None)
                    except Exception:
                        logging.exception("Falha ao devolver o resultado da notificação.")
        except queue.Empty:
            pass
        self._root.after(100, self._poll)

    @staticmethod
    def _aparencia(appearance):
        return {
            "accent_color": appearance.get("accent_color", "#0067C0"),
            "font_scale_percent": appearance.get("font_scale_percent", 100),
            "play_sound": appearance.get("play_sound", True),
            "sound_type": appearance.get("sound_type", "information"),
            "toast_duration_seconds": appearance.get("toast_duration_seconds", NO_REPLY_AUTO_CLOSE_SECONDS),
            "toast_position": appearance.get("toast_position", "bottom_right"),
        }

    @staticmethod
    def _tocar_som(tipo="information"):
        if os.name != "nt":
            return
        try:
            import winsound
            codigo = {
                "warning": winsound.MB_ICONEXCLAMATION,
                "error": winsound.MB_ICONHAND,
            }.get(tipo, winsound.MB_ICONASTERISK)
            winsound.MessageBeep(codigo)
        except Exception:
            logging.debug("Som da notificação não pôde ser reproduzido.")

    @staticmethod
    def _monitor_por_indice(indice):
        monitores = obter_monitores()
        return next((m for m in monitores if m["index"] == indice), None) \
            or next((m for m in monitores if m["primary"]), None) \
            or (monitores[0] if monitores else {
                "index": 0, "width": 1280, "height": 720, "x": 0, "y": 0, "primary": True})

    @staticmethod
    def _foto_tk(imagem_obj, largura_maxima, altura_maxima):
        from PIL import Image, ImageTk

        dados = base64.b64decode(imagem_obj["data_base64"], validate=True)
        with Image.open(io.BytesIO(dados)) as original:
            imagem = original.copy()
        filtro = getattr(Image, "Resampling", Image).LANCZOS
        limite_largura = max(1, int(largura_maxima))
        limite_altura = max(1, int(altura_maxima))
        escala = min(limite_largura / max(1, imagem.width),
                     limite_altura / max(1, imagem.height))
        tamanho = (max(1, round(imagem.width * escala)),
                   max(1, round(imagem.height * escala)))
        if tamanho != imagem.size:
            imagem = imagem.resize(tamanho, filtro)
        return ImageTk.PhotoImage(imagem)

    def _exibir_janela(
            self, sender, title, message, allow_reply, on_result, buttons=None,
            display_mode="toast", image=None, screen_images=None,
            image_duration_seconds=None, allow_manual_close=None, appearance=None,
            video=None, screen_videos=None, video_loop=False, audio=None, audio_loop=False):
        if display_mode == protocolo.DISPLAY_MODE_CENTER_IMAGE:
            self._exibir_imagens_monitores(
                sender, title, message, allow_reply, on_result, buttons or [], image,
                screen_images or [], image_duration_seconds or 15,
                allow_manual_close is not False, appearance or {})
            return
        if display_mode == protocolo.DISPLAY_MODE_CENTER_VIDEO:
            self._reproduzir_midia_windows(
                on_result, video=video, screen_videos=screen_videos or [],
                repetir=bool(video_loop), duracao=image_duration_seconds,
                permitir_fechar=allow_manual_close is not False)
            return
        if display_mode == protocolo.DISPLAY_MODE_AUDIO:
            self._reproduzir_midia_windows(
                on_result, audio=audio, repetir=bool(audio_loop),
                duracao=image_duration_seconds, permitir_fechar=False)
            return
        if display_mode == protocolo.DISPLAY_MODE_CENTER_ALERT:
            self._exibir_alerta_obrigatorio(sender, title, message, on_result, appearance or {})
            return

        visual = self._aparencia(appearance or {})
        if (display_mode in {protocolo.DISPLAY_MODE_TOAST, protocolo.DISPLAY_MODE_CENTER_MESSAGE}
                and not allow_reply and not buttons
                and visual["sound_type"] in {"warning", "error"} and os.name == "nt"):
            import ctypes
            icone = 0x10 if visual["sound_type"] == "error" else 0x30
            corpo = f"{title}\n\n{message}" if title else message
            ctypes.windll.user32.MessageBoxW(None, corpo, sender, icone | 0x00040000)
            on_result(None)
            return

        self._exibir_toast(sender, title, message, allow_reply, on_result, buttons or [], appearance or {},
                           centralizado=display_mode == protocolo.DISPLAY_MODE_CENTER_MESSAGE)

    @staticmethod
    def _gravar_midia_temporaria(conteudo):
        mime = conteudo.get("mime_type", "")
        extensao = {
            "video/mp4": ".mp4", "video/x-ms-wmv": ".wmv",
            "audio/mpeg": ".mp3", "audio/wav": ".wav",
        }.get(mime, ".bin")
        dados = base64.b64decode(conteudo["data_base64"], validate=True)
        pasta = Path(tempfile.gettempdir()) / "Comunicador" / "midia"
        pasta.mkdir(parents=True, exist_ok=True)
        caminho = pasta / f"{uuid.uuid4().hex}{extensao}"
        caminho.write_bytes(dados)
        return caminho

    @staticmethod
    def _script_player_path():
        pasta = Path(tempfile.gettempdir()) / "Comunicador"
        pasta.mkdir(parents=True, exist_ok=True)
        caminho = pasta / f"media-player-{RECEIVER_VERSION}.ps1"
        caminho.write_text(MEDIA_PLAYER_SCRIPT, encoding="utf-8")
        return caminho

    def _reproduzir_midia_windows(
            self, on_result, video=None, screen_videos=None, repetir=False,
            duracao=None, permitir_fechar=True, audio=None):
        if os.name != "nt":
            on_result(None)
            return

        itens = []
        if audio is not None:
            itens.append(("audio", audio, self._monitor_por_indice(0), 100))
        elif screen_videos:
            for item in screen_videos:
                itens.append((
                    "video", item["video"],
                    self._monitor_por_indice(item.get("monitor_index", 0)),
                    item.get("width_percent", 70)))
        elif video is not None:
            itens.append(("video", video, self._monitor_por_indice(0), 70))

        if not itens:
            on_result(None)
            return

        temporarios = []
        processos = []
        try:
            script = self._script_player_path()
            for tipo, conteudo, monitor, percentual in itens:
                arquivo = self._gravar_midia_temporaria(conteudo)
                temporarios.append(arquivo)
                comando = [
                    "powershell.exe", "-NoProfile", "-NonInteractive",
                    "-ExecutionPolicy", "Bypass", "-STA", "-File", str(script),
                    "-MediaPath", str(arquivo), "-Kind", tipo,
                    "-X", str(monitor.get("x", 0)), "-Y", str(monitor.get("y", 0)),
                    "-MonitorWidth", str(max(1, monitor.get("width", 1280))),
                    "-MonitorHeight", str(max(1, monitor.get("height", 720))),
                    "-WidthPercent", str(percentual),
                    "-DurationSeconds", str(int(duracao or 0)),
                ]
                if repetir:
                    comando.append("-Loop")
                if permitir_fechar:
                    comando.append("-AllowClose")
                processos.append(subprocess.Popen(
                    comando, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)))
        except Exception:
            logging.exception("Não foi possível iniciar a reprodução da mídia recebida.")
            for caminho in temporarios:
                try:
                    caminho.unlink(missing_ok=True)
                except OSError:
                    pass
            on_result(None)
            return

        def aguardar():
            for processo in processos:
                try:
                    processo.wait()
                except Exception:
                    logging.exception("Falha ao aguardar o player de mídia.")
            for caminho in temporarios:
                try:
                    caminho.unlink(missing_ok=True)
                except OSError:
                    pass
            on_result(None)

        threading.Thread(target=aguardar, daemon=True, name="reproducao-midia").start()

    def _nova_janela(self):
        tk = self._tk
        win = tk.Toplevel(self._root)
        win.title(APP_NAME)
        win.attributes("-topmost", True)
        win.resizable(False, False)
        win.overrideredirect(True)
        win.configure(bg="#2C2C2C")
        return win

    def _exibir_toast(self, sender, title, message, allow_reply, on_result, buttons, appearance,
                      centralizado=False):
        tk = self._tk
        visual = self._aparencia(appearance)
        escala = visual["font_scale_percent"] / 100.0
        win = self._nova_janela()
        corpo = tk.Frame(win, bg="#2C2C2C", highlightbackground="#484848", highlightthickness=1)
        corpo.pack(fill="both", expand=True)

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

        cabecalho = tk.Frame(corpo, bg="#2C2C2C")
        cabecalho.pack(fill="x", padx=14, pady=(12, 4))
        tk.Label(cabecalho, text=sender, font=("Segoe UI", 9),
                 fg="#C8C8C8", bg="#2C2C2C").pack(side="left")
        tk.Button(cabecalho, text="×", command=lambda: finish(None), bd=0, relief="flat",
                  bg="#2C2C2C", activebackground="#454545", fg="#DDDDDD",
                  activeforeground="white", font=("Segoe UI", 12)).pack(side="right")
        tk.Label(corpo, text=title, font=("Segoe UI", max(10, round(14 * escala)), "bold"),
                 wraplength=350, justify="left", fg="white", bg="#2C2C2C").pack(
            anchor="w", padx=14, pady=(2, 6))
        tk.Label(corpo, text=message, font=("Segoe UI", max(9, round(12 * escala))),
                 wraplength=350, justify="left", fg="#E2E2E2", bg="#2C2C2C").pack(
            anchor="w", padx=14)

        # Botões de resposta rápida enviados junto com o aviso. Se o botão tiver
        # link, além de responder ele abre o endereço no navegador padrão — e a
        # URL é revalidada aqui, porque veio pela rede.
        botoes_frame = tk.Frame(corpo, bg="#2C2C2C")
        if buttons:
            botoes_frame.pack(fill="x", padx=10, pady=(10, 0))
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

            tk.Button(botoes_frame, text=texto, font=("Segoe UI", max(9, round(10 * escala))), command=ao_clicar,
                      bg="#3B3B3B", activebackground="#4A4A4A", fg="white", activeforeground="white",
                      relief="flat", bd=0).pack(side="left", fill="x", expand=True, padx=4)

        if allow_reply:
            entry = tk.Entry(corpo, font=("Segoe UI", max(9, round(10 * escala))), bg="#3B3B3B",
                             fg="white", insertbackground="white", relief="flat")
            entry.pack(fill="x", padx=14, pady=(12, 6))
            entry.focus_set()

            btns = tk.Frame(corpo, bg="#2C2C2C")
            btns.pack(pady=6)
            tk.Button(btns, text="Responder", width=12, command=lambda: finish(entry.get()),
                      bg=visual["accent_color"], fg="white", relief="flat").pack(side="left", padx=4)
            tk.Button(btns, text="Fechar", width=12, command=lambda: finish(None),
                      bg="#3B3B3B", fg="white", relief="flat").pack(side="left", padx=4)
            entry.bind("<Return>", lambda _e: finish(entry.get()))
        elif not buttons:
            tk.Button(corpo, text="OK", width=12, command=lambda: finish(None),
                      bg="#3B3B3B", fg="white", relief="flat").pack(pady=14)
            win.after(visual["toast_duration_seconds"] * 1000, lambda: finish(None))

        win.protocol("WM_DELETE_WINDOW", lambda: finish(None))
        win.update_idletasks()
        largura = max(380, win.winfo_reqwidth())
        altura = win.winfo_reqheight()
        tela_largura = win.winfo_screenwidth()
        tela_altura = win.winfo_screenheight()
        if centralizado:
            x = max(0, (tela_largura - largura) // 2)
            y = max(0, (tela_altura - altura) // 2)
        else:
            x = tela_largura - largura - 18
            y = 18 if visual["toast_position"] == "top_right" else tela_altura - altura - 58
        win.geometry(f"{largura}x{altura}+{x}+{y}")
        if visual["play_sound"]:
            self._tocar_som(visual["sound_type"])

    def _exibir_imagens_monitores(
            self, sender, title, message, allow_reply, on_result, buttons, image,
            screen_images, duration, manual_close, appearance):
        tk = self._tk
        visual = self._aparencia(appearance)
        itens = list(screen_images)
        if not itens and image is not None:
            principal = self._monitor_por_indice(0)
            itens = [{"monitor_index": principal["index"], "width_percent": 70, "image": image}]

        grupos = {}
        for item in itens:
            grupos.setdefault(item.get("monitor_index", 0), []).append(item)

        janelas = []
        finalizado = {"done": False}

        def finish(value):
            if finalizado["done"]:
                return
            finalizado["done"] = True
            for janela in list(janelas):
                try:
                    janela.destroy()
                except tk.TclError:
                    pass
            on_result(value)

        # Cada arquivo recebe uma janela exatamente do tamanho da imagem. Assim
        # não existe painel, cabeçalho, botão, borda ou retângulo preto ao redor.
        for indice, imagens in grupos.items():
            monitor = self._monitor_por_indice(indice)
            colunas = 1 if len(imagens) == 1 else 2
            linhas = max(1, (len(imagens) + colunas - 1) // colunas)
            espaco = 12
            largura_celula = max(1, (monitor["width"] - 40 - espaco * (colunas - 1)) // colunas)
            altura_celula = max(1, (monitor["height"] - 40 - espaco * (linhas - 1)) // linhas)

            fotos = []
            for posicao, item in enumerate(imagens):
                max_largura = min(
                    monitor["width"] * item.get("width_percent", 70) / 100,
                    largura_celula)
                max_altura = altura_celula
                foto = self._foto_tk(item["image"], max_largura, max_altura)
                fotos.append((posicao, foto))

            largura_grade = colunas * largura_celula + espaco * (colunas - 1)
            altura_grade = linhas * altura_celula + espaco * (linhas - 1)
            origem_x = monitor["x"] + (monitor["width"] - largura_grade) // 2
            origem_y = monitor["y"] + (monitor["height"] - altura_grade) // 2

            for posicao, foto in fotos:
                largura = foto.width()
                altura = foto.height()
                coluna = posicao % colunas
                linha = posicao // colunas
                x = origem_x + coluna * (largura_celula + espaco) + (largura_celula - largura) // 2
                y = origem_y + linha * (altura_celula + espaco) + (altura_celula - altura) // 2

                win = tk.Toplevel(self._root)
                win.title(APP_NAME)
                win.attributes("-topmost", True)
                win.resizable(False, False)
                win.overrideredirect(True)
                chave_transparente = "#010203"
                win.configure(bg=chave_transparente)
                try:
                    win.wm_attributes("-transparentcolor", chave_transparente)
                except tk.TclError:
                    pass

                label = tk.Label(win, image=foto, bg=chave_transparente,
                                 bd=0, relief="flat", highlightthickness=0)
                label.pack()
                win._image_refs = [foto]  # mantém PhotoImage viva
                win.geometry(f"{largura}x{altura}{x:+d}{y:+d}")
                if manual_close:
                    label.configure(cursor="hand2")
                    label.bind("<Button-1>", lambda _e: finish(None))
                    win.bind("<Escape>", lambda _e: finish(None))
                win.protocol("WM_DELETE_WINDOW", lambda: finish(None) if manual_close else None)
                win.lift()
                janelas.append(win)

        if not janelas:
            on_result(None)
            return
        self._root.after(max(3, int(duration)) * 1000, lambda: finish(None))
        if visual["play_sound"]:
            self._tocar_som(visual["sound_type"])

    def _exibir_alerta_obrigatorio(self, sender, title, message, on_result, appearance):
        tk = self._tk
        visual = self._aparencia(appearance)
        escala = visual["font_scale_percent"] / 100.0
        monitor = self._monitor_por_indice(0)
        win = self._nova_janela()
        win.configure(bg="#111111")
        win.geometry(
            f"{max(640, monitor['width'])}x{max(480, monitor['height'])}"
            f"{monitor['x']:+d}{monitor['y']:+d}")

        finalizado = {"done": False}

        def finish():
            if finalizado["done"]:
                return
            finalizado["done"] = True
            try:
                win.grab_release()
            except tk.TclError:
                pass
            win.destroy()
            on_result(None)

        cartao = tk.Frame(win, bg="#2C2C2C", highlightbackground=visual["accent_color"],
                          highlightthickness=2, padx=28, pady=24)
        cartao.place(relx=0.5, rely=0.5, anchor="center", width=min(620, monitor["width"] - 60))
        tk.Label(cartao, text="Comunicador", bg="#2C2C2C", fg="#C8C8C8",
                 font=("Segoe UI", 10)).pack(anchor="w")
        tk.Label(cartao, text=title, bg="#2C2C2C", fg="white",
                 font=("Segoe UI", max(14, round(22 * escala)), "bold"),
                 wraplength=540, justify="left").pack(anchor="w", pady=(14, 8))
        tk.Label(cartao, text=message, bg="#2C2C2C", fg="#EEEEEE",
                 font=("Segoe UI", max(10, round(14 * escala))),
                 wraplength=540, justify="left").pack(anchor="w")
        tk.Button(cartao, text="OK", command=finish, bg=visual["accent_color"], fg="white",
                  activeforeground="white", relief="flat", font=("Segoe UI", 11, "bold"),
                  height=2).pack(fill="x", pady=(22, 0))

        def clique(event):
            atual = event.widget
            while atual is not None:
                if atual == cartao:
                    return
                atual = getattr(atual, "master", None)
            self._tocar_som("warning")

        win.bind("<Button-1>", clique, add="+")
        win.protocol("WM_DELETE_WINDOW", lambda: self._tocar_som("warning"))
        try:
            win.grab_set_global()
        except tk.TclError:
            win.grab_set()
        win.focus_force()
        if visual["play_sound"]:
            self._tocar_som(visual["sound_type"])

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
            elif msg_type == MessageType.UPDATE_REQUEST:
                self._handle_update_request(msg, server)
            elif msg_type == MessageType.SYNC_REQUEST:
                self._handle_sync_request(msg, server)
            else:
                raise ProtocolError(
                    ErrorCode.UNKNOWN_TYPE, f"Tipo não esperado nesta conexão: '{msg_type}'")
        except ProtocolError as exc:
            logging.warning("Erro de protocolo (%s): %s", self.client_address, exc)
            self._safe_send(protocolo.make_error(exc.code, exc.message, in_reply_to=msg.get("id")))
        except Exception:
            # Uma mensagem defeituosa jamais deve encerrar o servidor receptor.
            # Os detalhes completos ficam no log; a rede recebe apenas um erro
            # genérico, sem expor caminhos ou dados internos da máquina.
            logging.exception(
                "Falha isolada ao processar %s de %s (id=%s).",
                msg_type, self.client_address, msg.get("id"))
            self._safe_send(protocolo.make_error(
                ErrorCode.INTERNAL_ERROR,
                "O receptor não conseguiu processar esta solicitação.",
                in_reply_to=msg.get("id")))

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
        pong["has_panel"] = False
        pong["monitors"] = obter_monitores()
        pong["receiver_version"] = RECEIVER_VERSION
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
        response["has_panel"] = False
        response["monitors"] = obter_monitores()
        response["receiver_version"] = RECEIVER_VERSION
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
            buttons=msg.get("buttons"), display_mode=msg.get("display_mode", "toast"),
            image=msg.get("image"), screen_images=msg.get("screen_images"),
            image_duration_seconds=msg.get("image_duration_seconds"),
            allow_manual_close=msg.get("allow_manual_close"), appearance=msg.get("appearance"),
            video=msg.get("video"), screen_videos=msg.get("screen_videos"),
            video_loop=msg.get("video_loop", False), audio=msg.get("audio"),
            audio_loop=msg.get("audio_loop", False))

        ack = protocolo.base_message(MessageType.ACK)
        ack["in_reply_to"] = notification_id
        ack["status"] = "shown"
        self._safe_send(ack)

        if not allow_reply and not msg.get("buttons"):
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

    def _handle_update_request(self, msg: dict, server: "ReceptorTcpServer") -> None:
        if not server.config.token_is_valid(msg["token"]):
            raise ProtocolError(ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.")

        resposta = protocolo.base_message(MessageType.UPDATE_STATUS)
        resposta["in_reply_to"] = msg["id"]
        resposta["receiver_version"] = RECEIVER_VERSION
        try:
            nova_versao = aplicar_atualizacao_oficial(msg, server.ui.test_mode)
            resposta["success"] = True
            resposta["status"] = "updated"
            resposta["receiver_version"] = nova_versao
            resposta["message"] = "Atualização instalada; o receptor vai reiniciar."
            self._safe_send(resposta)
            if not server.ui.test_mode:
                agendar_reinicio()
        except Exception as exc:
            logging.exception("Falha ao aplicar atualização oficial do receptor.")
            resposta["success"] = False
            resposta["status"] = "failed"
            resposta["message"] = f"Falha ao aplicar atualização: {exc}"
            self._safe_send(resposta)

    def _handle_sync_request(self, msg: dict, server: "ReceptorTcpServer") -> None:
        if not server.config.token_is_valid(msg["token"]):
            raise ProtocolError(ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.")
        resposta = protocolo.base_message(MessageType.SYNC_RESPONSE)
        resposta["in_reply_to"] = msg["id"]
        resposta["history_entries"] = []
        resposta["log_entries"] = coletar_logs_receptor(server.config) if msg["include_logs"] else []
        self._safe_send(resposta)

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
            announce["has_panel"] = False
            announce["monitors"] = obter_monitores()
            announce["receiver_version"] = RECEIVER_VERSION
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
        self._send_lock = threading.Lock()

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
            except Exception:
                logging.exception("Falha isolada na conexão reversa com %s:%s.", self.host, self.port)
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
        registro["has_panel"] = False
        registro["monitors"] = obter_monitores()
        registro["receiver_version"] = RECEIVER_VERSION
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
                threading.Thread(
                    target=self._tratar_notificacao_segura, args=(msg,), daemon=True,
                    name=f"aviso-{msg.get('id', '')[:8]}").start()
            elif msg.get("type") == MessageType.UPDATE_REQUEST:
                threading.Thread(
                    target=self._tratar_atualizacao_segura, args=(msg,), daemon=True,
                    name=f"atualizacao-{msg.get('id', '')[:8]}").start()
            elif msg.get("type") == MessageType.SYNC_REQUEST:
                self._tratar_sync(msg)
            elif msg.get("type") == MessageType.PING:
                self._tratar_ping(msg)
            else:
                self._enviar(protocolo.make_error(
                    ErrorCode.UNKNOWN_TYPE,
                    f"Tipo não esperado na conexão reversa: '{msg.get('type')}'",
                    in_reply_to=msg.get("id")))

    def _tratar_ping(self, msg: dict) -> None:
        if not self.config.token_is_valid(msg["token"]):
            self._enviar(protocolo.make_error(
                ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.", msg["id"]))
            return
        pong = protocolo.base_message(MessageType.PONG)
        pong["computer_id"] = self.config.computer_id
        pong["computer_name"] = self.config.computer_name
        pong["has_panel"] = False
        pong["monitors"] = obter_monitores()
        pong["receiver_version"] = RECEIVER_VERSION
        pong["status"] = "online"
        self._enviar(pong)

    def _tratar_notificacao_segura(self, msg: dict) -> None:
        try:
            self._tratar_notificacao(msg)
        except Exception:
            logging.exception("Falha isolada ao tratar notificação reversa %s.", msg.get("id"))

    def _tratar_notificacao(self, msg: dict) -> None:
        allow_reply = msg.get("allow_reply", False)
        notification_id = msg["id"]
        result_queue: "queue.Queue[Optional[str]]" = queue.Queue()

        self.ui.mostrar(
            sender=msg.get("sender", "Painel"), title=msg.get("title", ""),
            message=msg.get("message", ""), allow_reply=allow_reply,
            on_result=lambda value: result_queue.put(value),
            buttons=msg.get("buttons"), display_mode=msg.get("display_mode", "toast"),
            image=msg.get("image"), screen_images=msg.get("screen_images"),
            image_duration_seconds=msg.get("image_duration_seconds"),
            allow_manual_close=msg.get("allow_manual_close"), appearance=msg.get("appearance"),
            video=msg.get("video"), screen_videos=msg.get("screen_videos"),
            video_loop=msg.get("video_loop", False), audio=msg.get("audio"),
            audio_loop=msg.get("audio_loop", False))

        ack = protocolo.base_message(MessageType.ACK)
        ack["in_reply_to"] = notification_id
        ack["status"] = "shown"
        self._enviar(ack)

        if not allow_reply and not msg.get("buttons"):
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

    def _tratar_atualizacao_segura(self, msg: dict) -> None:
        resposta = protocolo.base_message(MessageType.UPDATE_STATUS)
        resposta["in_reply_to"] = msg["id"]
        resposta["receiver_version"] = RECEIVER_VERSION
        try:
            if not self.config.token_is_valid(msg["token"]):
                raise ProtocolError(ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.")
            nova_versao = aplicar_atualizacao_oficial(msg, self.ui.test_mode)
            resposta["success"] = True
            resposta["status"] = "updated"
            resposta["receiver_version"] = nova_versao
            resposta["message"] = "Atualização instalada; o receptor vai reiniciar."
            self._enviar(resposta)
            if not self.ui.test_mode:
                agendar_reinicio()
        except Exception as exc:
            logging.exception("Falha ao aplicar atualização recebida pela conexão reversa.")
            resposta["success"] = False
            resposta["status"] = "failed"
            resposta["message"] = f"Falha ao aplicar atualização: {exc}"
            self._enviar(resposta)

    def _tratar_sync(self, msg: dict) -> None:
        if not self.config.token_is_valid(msg["token"]):
            self._enviar(protocolo.make_error(
                ErrorCode.UNAUTHORIZED, "Token inválido ou painel não pareado.", msg["id"]))
            return
        resposta = protocolo.base_message(MessageType.SYNC_RESPONSE)
        resposta["in_reply_to"] = msg["id"]
        resposta["history_entries"] = []
        resposta["log_entries"] = coletar_logs_receptor(self.config) if msg["include_logs"] else []
        self._enviar(resposta)

    def _enviar(self, mensagem: dict) -> None:
        if self._sock is None:
            return
        try:
            with self._send_lock:
                if self._sock is not None:
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
    config_dir.mkdir(parents=True, exist_ok=True)
    handlers = [logging.FileHandler(config_dir / "receptor.log", encoding="utf-8")]
    if test_mode:
        handlers.append(logging.StreamHandler(sys.stdout))
    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", handlers=handlers)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Comunicador Receptor")
    parser.add_argument("--version", action="version", version=RECEIVER_VERSION)
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
    try:
        tcp_server = ReceptorTcpServer(("0.0.0.0", args.port), config, ui)
    except OSError as exc:
        # Um painel aberto nesta mesma máquina já pode estar usando a porta
        # principal. O receptor continua útil pela conexão reversa e, por isso,
        # ocupa uma porta livre em vez de encerrar ou disputar a porta.
        logging.warning(
            "Porta TCP %s ocupada (%s). Receptor continuará em uma porta automática.",
            args.port, exc)
        tcp_server = ReceptorTcpServer(("0.0.0.0", 0), config, ui)
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
