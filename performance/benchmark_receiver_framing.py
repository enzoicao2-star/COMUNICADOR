"""Mede somente o custo de montar um quadro TCP de mídia no receptor.

Uso: python performance/benchmark_receiver_framing.py
O socket simulado devolve blocos do tamanho requisitado pelo receptor.
"""

import statistics
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "receiver"))

from receptor import ReceptorTcpHandler  # noqa: E402


class ChunkSocket:
    def __init__(self, data):
        self.data = memoryview(data)
        self.offset = 0

    def recv(self, size):
        end = min(self.offset + size, len(self.data))
        chunk = self.data[self.offset:end].tobytes()
        self.offset = end
        return chunk


for mib in (1, 4, 8):
    payload = b"x" * (mib * 1024 * 1024) + b"\n"
    measurements = []
    for _ in range(5):
        handler = object.__new__(ReceptorTcpHandler)
        handler.request = ChunkSocket(payload)
        start = time.perf_counter_ns()
        result = handler._read_message(b"")
        measurements.append((time.perf_counter_ns() - start) / 1_000_000)
        assert result == payload[:-1]
    print(f"{mib} MiB: {statistics.median(measurements):.2f} ms (mediana de 5)")
