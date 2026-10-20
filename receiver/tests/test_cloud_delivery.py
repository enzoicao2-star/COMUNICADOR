import json
import sys
import threading
import time
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo  # noqa: E402
import receptor  # noqa: E402


class RecordingUi:
    def mostrar(self, *args, **kwargs):
        self.args = args
        self.kwargs = kwargs


def _session_worker(tmp_path):
    worker = object.__new__(receptor.CloudDeliveryWorker)
    worker._session_path = tmp_path / "cloud_session.json"
    worker._session_lock = threading.Lock()
    worker._session = None
    return worker


def test_cloud_session_uses_recovery_when_primary_is_locked(tmp_path, monkeypatch):
    worker = _session_worker(tmp_path)
    worker._session_path.write_text('{"access_token":"old"}', encoding="utf-8")
    original_replace = Path.replace

    def replace_with_locked_primary(path, destination):
        if destination == worker._session_path:
            raise PermissionError("file is in use")
        return original_replace(path, destination)

    monkeypatch.setattr(Path, "replace", replace_with_locked_primary)
    worker._salvar_sessao({"access_token": "new", "refresh_token": "token"})

    assert worker._ler_sessao_arquivo()["access_token"] == "new"
    assert (tmp_path / "cloud_session.json.recovery").exists()
    assert not list(tmp_path.glob("*.tmp"))


def test_cloud_session_keeps_working_and_preserves_snapshot_when_moves_fail(tmp_path, monkeypatch):
    worker = _session_worker(tmp_path)
    worker._json_request = lambda *_args: {
        "access_token": "in-memory", "refresh_token": "token", "expires_in": 3600}
    monkeypatch.setattr(Path, "replace", lambda *_args: (_ for _ in ()).throw(PermissionError("locked")))

    session = worker._obter_sessao()

    assert session["access_token"] == "in-memory"
    assert session["expires_at"] > time.time()
    assert len(list(tmp_path.glob("*.tmp"))) == 1
    assert worker._ler_sessao_arquivo()["access_token"] == "in-memory"

    for index in range(5):
        worker._salvar_sessao({"access_token": f"new-{index}", "expires_at": int(time.time()) + 3600})
    assert 1 <= len(list(tmp_path.glob("*.tmp"))) <= 3
    assert worker._ler_sessao_arquivo()["access_token"] == "new-4"


def test_cloud_session_reuses_newer_session_from_panel(tmp_path):
    worker = _session_worker(tmp_path)
    worker._session = {"access_token": "expired", "refresh_token": "old", "expires_at": 1}
    newer = {"access_token": "fresh", "refresh_token": "new", "expires_at": int(time.time()) + 3600}
    worker._session_path.write_text(json.dumps(newer), encoding="utf-8")
    worker._json_request = lambda *_args: (_ for _ in ()).throw(AssertionError("unexpected refresh"))

    assert worker._obter_sessao()["access_token"] == "fresh"


def _worker(media_blocked=False, is_owner=False):
    worker = object.__new__(receptor.CloudDeliveryWorker)
    worker.config = SimpleNamespace(media_blocked=media_blocked, computer_id="receiver-id")
    worker.ui = RecordingUi()
    worker.is_current_owner = lambda panel_id: is_owner and panel_id != "receiver-id"
    worker.can_change_wallpaper = lambda _panel_id: is_owner
    worker.updated = []
    worker._atualizar_entrega = lambda delivery_id, response: worker.updated.append(
        (delivery_id, response))
    return worker


def test_owner_target_rejects_cloud_delivery_without_showing_it():
    worker = _worker()
    worker.is_current_owner = lambda panel_id: panel_id == "receiver-id"
    delivery = {
        "id": "owner-target",
        "sender_device_id": "panel-2",
        "payload": {"kind": "notification", "title": "Aviso", "message": "Conteúdo"},
    }

    worker._mostrar_entrega(delivery)

    assert not hasattr(worker.ui, "args")
    assert worker.updated == [(
        "owner-target", "Entrega recusada: o OWNER está protegido e não recebe envios.")]


def test_receiver_detects_owner_target_for_local_delivery():
    config = object.__new__(receptor.Config)
    config.data = {"computer_id": "owner-id"}
    config.cloud_worker = SimpleNamespace(
        is_current_owner=lambda panel_id: panel_id == "owner-id")

    assert config.is_owner_device() is True


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


def test_cloud_media_block_allows_image_from_owner():
    worker = _worker(media_blocked=True, is_owner=True)
    image = {"mime_type": "image/gif", "data_base64": "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="}
    payload = {
        "kind": "notification",
        "sender": "OWNER",
        "title": "",
        "message": "",
        "allow_reply": False,
        "display_mode": protocolo.DISPLAY_MODE_CENTER_IMAGE,
        "image": image,
    }

    worker._mostrar_entrega({"id": "owner-image", "sender_device_id": "owner-id", "payload": payload})

    assert worker.ui.kwargs["image"] == image
    assert worker.ui.kwargs["display_mode"] == protocolo.DISPLAY_MODE_CENTER_IMAGE


def test_cloud_wallpaper_permission_reports_success(monkeypatch):
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


def test_cloud_wallpaper_allows_individual_permission(monkeypatch):
    worker = _worker(media_blocked=True)
    worker.can_change_wallpaper = lambda _panel_id: True
    applied = []
    monkeypatch.setattr(receptor, "aplicar_papel_parede", lambda image, _config: applied.append(image))
    image = {"mime_type": "image/png", "data_base64": "AA=="}
    worker._mostrar_entrega({
        "id": "delivery-delegated",
        "sender_device_id": "panel-delegated",
        "payload": {
            "kind": "notification",
            "display_mode": protocolo.DISPLAY_MODE_WALLPAPER,
            "image": image,
        },
    })

    assert applied == [image]
    assert worker.updated == [("delivery-delegated", "Papel de parede atualizado.")]


def test_cloud_wallpaper_rejects_sender_without_permission(monkeypatch):
    worker = _worker(media_blocked=True)
    worker.can_change_wallpaper = lambda _panel_id: False
    applied = []
    monkeypatch.setattr(receptor, "aplicar_papel_parede", lambda image, _config: applied.append(image))

    worker._mostrar_entrega({
        "id": "delivery-denied",
        "sender_device_id": "panel-unpermitted",
        "payload": {
            "kind": "notification",
            "display_mode": protocolo.DISPLAY_MODE_WALLPAPER,
            "image": {"mime_type": "image/png", "data_base64": "AA=="},
        },
    })

    assert applied == []
    assert "não tem permissão" in worker.updated[0][1]


def test_wallpaper_permission_reads_individual_profile_permission():
    worker = _worker()
    worker.__dict__.pop("can_change_wallpaper")
    worker.is_current_owner = lambda _panel_id: False

    def authorized(path, method="GET", body=None):
        if path == "/rest/v1/rpc/get_global_config":
            return [{"config": {"permitir_papel_parede_remoto": True, "modelos_badge": []}}]
        if path == "/rest/v1/rpc/get_admin_state":
            return []
        if path == "/rest/v1/panel_profiles?device_id=eq.panel-7&select=badges":
            return [{"device_id": "panel-7", "badges": [
                {"id": "__individual_permissions",
                 "permissoes_individuais": ["change_wallpaper"]},
            ]}]
        raise AssertionError(f"Caminho inesperado: {path}")

    worker._autorizado = authorized

    assert worker.can_change_wallpaper("panel-7") is True


def test_remote_command_permission_reads_individual_profile_permission():
    worker = _worker()
    worker.__dict__.pop("can_change_wallpaper")
    worker.is_current_owner = lambda _panel_id: False

    def authorized(path, method="GET", body=None):
        if path == "/rest/v1/panel_profiles?device_id=eq.panel-remote&select=badges":
            return [{"device_id": "panel-remote", "badges": [
                {"id": "__individual_permissions",
                 "permissoes_individuais": ["remote_command"]},
            ]}]
        raise AssertionError(f"Caminho inesperado: {path}")

    worker._autorizado = authorized

    assert worker.has_panel_permission("panel-remote", "remote_command") is True


def test_local_sender_wallpaper_permission_requires_paired_panel_token():
    config = object.__new__(receptor.Config)
    config.data = {"paired_panels": {"panel-9": {"token": "valid-token"}}}
    config.cloud_worker = SimpleNamespace(can_change_wallpaper=lambda panel_id: panel_id == "panel-9")

    assert config.sender_can_change_wallpaper({"panel_id": "panel-9", "token": "valid-token"}) is True
    assert config.sender_can_change_wallpaper({"panel_id": "panel-9", "token": "wrong-token"}) is False
    assert config.sender_can_change_wallpaper({"panel_id": "unknown", "token": "valid-token"}) is False


def test_wallpaper_permission_accepts_role_and_respects_global_block():
    worker = _worker()
    worker.__dict__.pop("can_change_wallpaper")
    worker.is_current_owner = lambda _panel_id: False
    responses = {
        "/rest/v1/rpc/get_global_config": [{"config": {
            "permitir_papel_parede_remoto": True,
            "modelos_badge": [{"id": "wallpaper-admin", "permissoes": ["change_wallpaper"]}],
        }}],
        "/rest/v1/rpc/get_admin_state": [],
        "/rest/v1/panel_profiles?device_id=eq.panel-8&select=badges": [{
            "device_id": "panel-8", "badges": [{"id": "badge-1", "role_id": "wallpaper-admin"}],
        }],
    }
    worker._autorizado = lambda path, *_args: responses[path]

    assert worker.can_change_wallpaper("panel-8") is True

    responses["/rest/v1/rpc/get_global_config"] = [{"config": {
        "permitir_papel_parede_remoto": False,
    }}]
    assert worker.can_change_wallpaper("panel-8") is False


def test_cloud_restore_wallpaper_uses_delegated_permission(monkeypatch):
    worker = _worker(media_blocked=True)
    worker.can_change_wallpaper = lambda _panel_id: True
    restored = []
    monkeypatch.setattr(receptor, "restaurar_papel_parede", lambda config: restored.append(config))

    worker._mostrar_entrega({
        "id": "restore-delegated",
        "sender_device_id": "panel-delegated",
        "payload": {
            "kind": "notification",
            "display_mode": protocolo.DISPLAY_MODE_RESTORE_WALLPAPER,
        },
    })

    assert restored == [worker.config]
    assert worker.updated == [("restore-delegated", "Papel de parede restaurado.")]


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


def test_wallpaper_preview_request_sends_local_thumbnail_to_owner(monkeypatch):
    import uuid
    from datetime import datetime, timedelta, timezone

    worker = _worker(is_owner=True)
    queued = []
    worker._autorizado = lambda path, method, body: queued.append((path, method, body))
    monkeypatch.setattr(receptor, "obter_miniatura_papel_parede", lambda _config: {
        "data_base64": "jpeg-thumbnail"})
    request_id = uuid.uuid4().hex
    worker._mostrar_entrega({
        "id": "preview-request", "sender_device_id": "owner-panel",
        "payload": {"kind": "wallpaper_preview_request", "request_id": request_id,
                    "monitor_index": 1,
                    "expires_at": (datetime.now(timezone.utc) + timedelta(minutes=2)).isoformat()},
    })

    assert queued[0][0:2] == ("/rest/v1/deliveries", "POST")
    assert queued[0][2]["target_device_id"] == "owner-panel"
    assert queued[0][2]["payload"] == {
        "kind": "wallpaper_preview_result", "request_id": request_id,
        "monitor_index": 1, "image_base64": "jpeg-thumbnail"}
    assert worker.updated == [("preview-request", None)]
    assert not hasattr(worker.ui, "args")


def test_wallpaper_preview_request_rejects_non_owner(monkeypatch):
    import uuid
    from datetime import datetime, timedelta, timezone

    worker = _worker(is_owner=False)
    queued = []
    worker._autorizado = lambda *args: queued.append(args)
    monkeypatch.setattr(receptor, "obter_miniatura_papel_parede", lambda _config: {
        "data_base64": "jpeg-thumbnail"})
    worker._mostrar_entrega({
        "id": "untrusted-preview", "sender_device_id": "other-panel",
        "payload": {"kind": "wallpaper_preview_request", "request_id": uuid.uuid4().hex,
                    "monitor_index": 0,
                    "expires_at": (datetime.now(timezone.utc) + timedelta(minutes=2)).isoformat()},
    })

    assert queued == []
    assert worker.updated == [("untrusted-preview", None)]
