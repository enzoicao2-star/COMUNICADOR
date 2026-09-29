import sys
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
import receptor  # noqa: E402


class RecordingUi:
    def mostrar(self, *args, **kwargs):
        self.args = args
        self.kwargs = kwargs


def _worker(media_blocked=False, is_owner=False):
    worker = object.__new__(receptor.CloudDeliveryWorker)
    worker.config = SimpleNamespace(media_blocked=media_blocked)
    worker.ui = RecordingUi()
    worker.is_current_owner = lambda _panel_id: is_owner
    worker.updated = []
    worker._atualizar_entrega = lambda delivery_id, response: worker.updated.append(
        (delivery_id, response))
    return worker


def test_cloud_notification_keeps_buttons_appearance_and_confirmation():
    worker = _worker()
    payload = {
        "kind": "notification",
        "sender": "Painel Maia",
        "title": "Atenção",
        "message": "Confira este aviso.",
        "allow_reply": True,
        "confirmation_required": True,
        "buttons": [{"label": "Recebido", "value": "ok"}],
        "display_mode": protocolo.DISPLAY_MODE_CENTER_MESSAGE,
        "appearance": {"accent_color": "#4C8DFF"},
    }

    worker._mostrar_entrega({"id": "delivery-1", "sender_device_id": "panel-1", "payload": payload})

    assert worker.ui.args[:4] == ("Painel Maia", "Atenção", "Confira este aviso.", True)
    assert worker.ui.kwargs["buttons"] == payload["buttons"]
    assert worker.ui.kwargs["display_mode"] == protocolo.DISPLAY_MODE_CENTER_MESSAGE
    assert worker.ui.kwargs["confirmation_required"] is True
    assert worker.ui.kwargs["appearance"] == {"accent_color": "#4C8DFF"}


def test_cloud_media_block_keeps_text_but_removes_media_for_non_owner():
    worker = _worker(media_blocked=True)
    payload = {
        "kind": "notification",
        "sender": "Outro painel",
        "title": "Aviso com mídia",
        "message": "Leia esta mensagem.",
        "allow_reply": False,
        "display_mode": protocolo.DISPLAY_MODE_CENTER_IMAGE,
        "image": {"mime_type": "image/gif", "data_base64": "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="},
    }

    worker._mostrar_entrega({"id": "delivery-2", "sender_device_id": "panel-2", "payload": payload})

    assert worker.ui.args[2] == "Leia esta mensagem."
    assert worker.ui.kwargs["image"] is None
    assert worker.ui.kwargs["display_mode"] == "toast"


def test_cloud_wallpaper_requires_owner_and_reports_success(monkeypatch):
    worker = _worker(media_blocked=True, is_owner=True)
    applied = []
    monkeypatch.setattr(receptor, "aplicar_papel_parede", lambda image, _config: applied.append(image))
    image = {"mime_type": "image/png", "data_base64": "AA=="}
    payload = {
        "kind": "notification",
        "display_mode": protocolo.DISPLAY_MODE_WALLPAPER,
        "image": image,
    }

    worker._mostrar_entrega({"id": "delivery-3", "sender_device_id": "owner-id", "payload": payload})

    assert applied == [image]
    assert worker.updated == [("delivery-3", "Papel de parede atualizado.")]
    assert not hasattr(worker.ui, "args")


def test_admin_response_is_left_for_panel_without_toast_or_ack():
    worker = _worker()
    confirmed = []
    worker._confirmar_resposta = confirmed.append

    worker._mostrar_resposta({
        "id": "admin-result",
        "payload": {"kind": "admin_command", "command": "run_cmd"},
        "response_text": "Código de saída: 0",
    })

    assert confirmed == []
    assert not hasattr(worker.ui, "args")


def test_response_search_excludes_admin_commands_but_keeps_messages():
    from urllib.parse import parse_qs, urlsplit

    worker = _worker()
    worker.config.computer_id = "panel-1"
    requested = []
    worker._autorizado = lambda path: requested.append(path) or []

    worker._buscar_respostas()

    query = parse_qs(urlsplit(requested[0]).query)
    assert query["or"] == [
        "(payload->>kind.is.null,payload->>kind.neq.admin_command)"]
    assert query["sender_device_id"] == ["eq.panel-1"]


def test_normal_response_still_shows_notification_and_is_acknowledged():
    worker = _worker()
    confirmed = []
    worker._confirmar_resposta = confirmed.append

    worker._mostrar_resposta({
        "id": "message-reply",
        "payload": {"kind": "notification", "title": "Aviso"},
        "response_text": "Recebido",
    })

    assert confirmed == ["message-reply"]
    assert worker.ui.args[1:3] == ("Resposta: Aviso", "Recebido")
