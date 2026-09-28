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
