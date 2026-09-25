import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import receptor  # noqa: E402
import protocolo  # noqa: E402


@pytest.mark.parametrize("mode,expected", [
    (protocolo.DISPLAY_MODE_WALLPAPER, "wallpaper_applied"),
    (protocolo.DISPLAY_MODE_LOCK_SCREEN, "lock_screen_applied"),
])
def test_system_image_is_applied_without_opening_notification(monkeypatch, mode, expected):
    applied = []
    acknowledgements = []
    monkeypatch.setattr(receptor, "aplicar_papel_parede",
                        lambda image, config: applied.append("wallpaper"))
    monkeypatch.setattr(receptor, "aplicar_tela_bloqueio",
                        lambda image, config: applied.append("lock_screen"))
    monkeypatch.setattr(receptor.ReceptorTcpHandler, "_safe_send",
                        lambda self, message: acknowledgements.append(message))
    handler = object.__new__(receptor.ReceptorTcpHandler)
    server = SimpleNamespace(config=SimpleNamespace(token_is_valid=lambda token: True,
                                                    media_blocked=False))
    message = {"id": "notification-id", "token": "token", "display_mode": mode,
               "image": {"name": "photo.png", "mime_type": "image/png", "data_base64": "..."}}

    handler._handle_notification(message, server)

    assert applied == [mode]
    assert len(acknowledgements) == 1
    assert acknowledgements[0]["status"] == expected
    assert acknowledgements[0]["in_reply_to"] == "notification-id"
