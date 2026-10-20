import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from receptor import NotificationUi  # noqa: E402


class Window:
    def winfo_exists(self):
        return True

    def after(self, _milliseconds, _callback):
        return "timer-1"

    def after_cancel(self, _timer):
        pass


def test_snapshot_uses_actual_monitor_placement_and_stays_local(tmp_path):
    ui = NotificationUi(test_mode=True, config_dir=tmp_path)
    window = Window()
    monitor = {"index": 1, "x": 1920, "y": -100, "width": 1920, "height": 1080}
    placement = (window, monitor, 1920, -100, 384, 216)
    ui._active_pinned_images["image-1"] = {
        "window": window, "monitor": monitor,
        "item": {"image": {"name": "cartaz.png"}, "x_percent": 10,
                 "y_percent": 10, "width_percent": 20, "keep_until_removed": True},
        "janelas": [placement], "thumbnail_base64": "miniatura",
    }

    ui._write_active_media_snapshot()

    items = json.loads((tmp_path / "active-media.json").read_text(encoding="utf-8"))
    assert len(items) == 1
    assert items[0]["id"] == "image-1"
    assert items[0]["left_percent"] == 0
    assert items[0]["top_percent"] == 0
    assert items[0]["display_width_percent"] == 20
    assert items[0]["display_height_percent"] == 20
    assert items[0]["thumbnail_base64"] == "miniatura"

    ui._active_pinned_images.clear()
    ui._write_active_media_snapshot()
    assert json.loads((tmp_path / "active-media.json").read_text(encoding="utf-8")) == []


def test_persistent_image_can_receive_a_later_removal_time(tmp_path):
    ui = NotificationUi(test_mode=True, config_dir=tmp_path)
    window = Window()
    monitor = {"index": 0, "x": 0, "y": 0, "width": 1920, "height": 1080}
    ui._active_pinned_images["image-1"] = {
        "window": window, "monitor": monitor,
        "item": {"image": {"name": "cartaz.png"}, "x_percent": 50,
                 "y_percent": 50, "width_percent": 30, "keep_until_removed": True},
        "janelas": [(window, monitor, 672, 378, 576, 324)],
    }

    result = ui._controlar_imagem_na_ui("image-1", "set_timeout", None, None, None, 45)

    assert result == "Imagem será removida em 45 segundo(s)."
    assert ui._active_pinned_images["image-1"]["timeout_after_id"] == "timer-1"
    saved = json.loads((tmp_path / "active-media.json").read_text(encoding="utf-8"))
    assert saved[0]["expires_at"]
