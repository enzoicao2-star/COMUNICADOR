"""A checagem do instalador nao pode ignorar um Python valido no Windows."""

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import pytest


INSTALLER = Path(__file__).resolve().parent.parent / "INSTALAR_RECEPTOR.bat"


@pytest.mark.skipif(os.name != "nt", reason="Instalador BAT do Windows")
def test_detects_installed_python_without_running_installer():
    result = subprocess.run(
        ["cmd.exe", "/d", "/c", str(INSTALLER), "--verificar-python", sys.executable],
        capture_output=True, text=True, timeout=15,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Python encontrado em:" in result.stdout


@pytest.mark.skipif(os.name != "nt", reason="Instalador BAT do Windows")
def test_detects_python_when_path_contains_spaces(tmp_path):
    dll = Path(sys.executable).parent / f"python{sys.version_info.major}{sys.version_info.minor}.dll"
    pythonw = Path(sys.executable).with_name("pythonw.exe")
    if not dll.is_file() or not pythonw.is_file():
        pytest.skip("Esta instalacao nao tem DLL e pythonw.exe copiaveis")

    folder = tmp_path / "Python With Spaces"
    folder.mkdir()
    for source in (Path(sys.executable), pythonw, dll):
        shutil.copy2(source, folder / source.name)
    candidate = folder / "python.exe"
    env = os.environ.copy()
    env["PYTHONHOME"] = sys.base_prefix
    result = subprocess.run(
        ["cmd.exe", "/d", "/c", str(INSTALLER), "--verificar-python", str(candidate)],
        env=env, capture_output=True, text=True, timeout=15,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert str(candidate) in result.stdout


@pytest.mark.skipif(os.name != "nt", reason="Instalador BAT do Windows")
def test_installer_failure_prints_exact_log_error_on_screen():
    with tempfile.NamedTemporaryFile(
        mode="w", encoding="utf-8", prefix="Comunicador-Python-install-test-",
        suffix=".log", delete=False,
    ) as log:
        log.write("[1234] Error 0x80070643: falha original do Python\n")
        path = Path(log.name)
    try:
        result = subprocess.run(
            ["cmd.exe", "/d", "/c", str(INSTALLER), "--verificar-erro-python", str(path), "1603"],
            capture_output=True, text=True, timeout=15,
        )
        assert result.returncode == 0, result.stdout + result.stderr
        assert "[1234] Error 0x80070643: falha original do Python" in result.stdout
        assert "Codigo de saida: 1603" in result.stdout
    finally:
        path.unlink(missing_ok=True)
