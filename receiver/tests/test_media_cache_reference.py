import base64
import hashlib
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocolo
import receptor


@pytest.mark.parametrize("mime,data,validator", [
    ("image/png", b"\x89PNG\r\n\x1a\nbytes", protocolo._validar_imagem),
    ("image/gif", b"GIF89abytes", protocolo._validar_imagem),
    ("video/mp4", b"\x00\x00\x00\x18ftypisombytes", protocolo._validar_video),
    ("audio/wav", b"RIFF\x04\x00\x00\x00WAVEaudio", protocolo._validar_audio),
])
def test_cache_reference_is_only_valid_without_partial_payload(mime, data, validator):
    reference = {
        "name": "media.bin",
        "mime_type": mime,
        "data_base64": "",
        "media_cache_id": hashlib.sha256(data).hexdigest(),
    }
    assert validator(reference) == 0

    reference["data_base64"] = base64.b64encode(data).decode("ascii")
    with pytest.raises(protocolo.ProtocolError, match="(?i)referência.*inválida"):
        validator(reference)


def test_receiver_refuses_to_show_media_missing_from_cache(tmp_path: Path):
    digest = "a" * 64
    message = {"image": {
        "name": "photo.png", "mime_type": "image/png", "data_base64": "",
        "media_cache_id": digest,
    }}

    with pytest.raises(protocolo.ProtocolError, match="MEDIA_NOT_READY"):
        receptor._resolver_midias_cache(message, SimpleNamespace(directory=tmp_path))


def test_receiver_materializes_only_a_complete_hash_verified_file(tmp_path: Path):
    data = b"\x89PNG\r\n\x1a\ncomplete-image-data"
    digest = hashlib.sha256(data).hexdigest()
    cache = tmp_path / "media-cache"
    cache.mkdir()
    (cache / digest).write_bytes(data)
    message = {"screen_images": [{"image": {
        "name": "photo.png", "mime_type": "image/png", "data_base64": "",
        "media_cache_id": digest,
    }}]}

    receptor._resolver_midias_cache(message, SimpleNamespace(directory=tmp_path))

    image = message["screen_images"][0]["image"]
    assert base64.b64decode(image["data_base64"]) == data
    assert "media_cache_id" not in image


def test_receiver_materializes_hash_verified_audio_from_local_cache(tmp_path: Path):
    data = b"RIFF\x04\x00\x00\x00WAVEaudio-data"
    digest = hashlib.sha256(data).hexdigest()
    cache = tmp_path / "media-cache"
    cache.mkdir()
    (cache / digest).write_bytes(data)
    message = {"audio": {
        "name": "sound.wav", "mime_type": "audio/wav", "data_base64": "",
        "media_cache_id": digest,
    }}

    receptor._resolver_midias_cache(message, SimpleNamespace(directory=tmp_path))

    audio = message["audio"]
    assert base64.b64decode(audio["data_base64"]) == data
    assert "media_cache_id" not in audio
