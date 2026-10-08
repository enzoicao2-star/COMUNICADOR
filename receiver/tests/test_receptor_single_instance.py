"""Regression coverage for duplicate Windows logon and recovery launches."""

import os
import random
import subprocess
import sys
from pathlib import Path

import pytest


@pytest.mark.skipif(os.name != "nt", reason="Mutex nomeado do Receptor usa Win32")
def test_second_receiver_process_exits_when_instance_is_already_active():
    receiver_dir = Path(__file__).resolve().parents[1]
    port = random.randint(30000, 60000)
    env = os.environ.copy()
    env["PYTHONPATH"] = str(receiver_dir) + os.pathsep + env.get("PYTHONPATH", "")

    keep_alive = (
        "import receptor, time; "
        f"assert receptor.claim_single_instance({port}); "
        "print('ACTIVE', flush=True); time.sleep(30)"
    )
    first = subprocess.Popen(
        [sys.executable, "-c", keep_alive], cwd=receiver_dir, env=env,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
    )
    try:
        assert first.stdout is not None
        assert first.stdout.readline().strip() == "ACTIVE"

        second_code = (
            "import receptor; "
            f"print('ACTIVE' if receptor.claim_single_instance({port}) else 'DUPLICATE')"
        )
        second = subprocess.run(
            [sys.executable, "-c", second_code], cwd=receiver_dir, env=env,
            capture_output=True, text=True, timeout=15,
        )
        assert second.returncode == 0, second.stderr
        assert second.stdout.strip() == "DUPLICATE"
    finally:
        first.terminate()
        first.wait(timeout=10)
