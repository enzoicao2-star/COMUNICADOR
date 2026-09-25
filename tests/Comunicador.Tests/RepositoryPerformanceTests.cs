using System.Diagnostics;
using System.IO;
using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Comunicador.Tests;

public sealed class RepositoryPerformanceTests(ITestOutputHelper output)
{
    private const int ExistingEntries = 10_000;
    private const int Snapshots = 250;
    private const int RemoteEntries = 500;

    [Fact]
    [Trait("Category", "Performance")]
    public void HistoryAndLogSnapshotAndMergeTimings()
    {
        WithTemporaryStore(directory =>
        {
            var now = DateTime.UtcNow;
            var historyStore = new JsonStore<HistoricoEntry>(Path.Combine(directory, "history.json"));
            historyStore.Save(Enumerable.Range(0, ExistingEntries).Select(index => new HistoricoEntry
            {
                Id = $"history-{index}", Timestamp = now.AddSeconds(-index),
            }));
            var history = new HistoricoRepository(historyStore);

            var historySnapshotTime = Time(() =>
            {
                for (var i = 0; i < Snapshots; i++) history.Snapshot(ExistingEntries);
            });
            var historyRemote = Enumerable.Range(0, RemoteEntries).Select(index => new HistoricoEntry
            {
                Id = $"remote-history-{index}", Timestamp = now.AddSeconds(index + 1),
            }).ToArray();
            var historyMergeTime = Time(() => Assert.Equal(RemoteEntries, history.Mesclar(historyRemote)));

            var logStore = new JsonStore<LogEntry>(Path.Combine(directory, "logs.json"));
            logStore.Save(Enumerable.Range(0, ExistingEntries).Select(index => new LogEntry
            {
                Id = $"log-{index}", TimestampUtc = now.AddSeconds(-index),
            }));
            var logs = new LogRepository(logStore);

            var logSnapshotTime = Time(() =>
            {
                for (var i = 0; i < Snapshots; i++) logs.Snapshot(ExistingEntries);
            });
            var logRemote = Enumerable.Range(0, RemoteEntries).Select(index => new LogEntry
            {
                Id = $"remote-log-{index}", TimestampUtc = now.AddSeconds(index + 1),
            }).ToArray();
            var logMergeTime = Time(() => Assert.Equal(RemoteEntries, logs.Mesclar(logRemote)));

            output.WriteLine($"Historico Snapshot ({Snapshots} x {ExistingEntries:N0}): {historySnapshotTime:F2} ms");
            output.WriteLine($"Logs Snapshot ({Snapshots} x {ExistingEntries:N0}): {logSnapshotTime:F2} ms");
            output.WriteLine($"Historico Mesclar ({RemoteEntries} em {ExistingEntries:N0}): {historyMergeTime:F2} ms");
            output.WriteLine($"Logs Mesclar ({RemoteEntries} em {ExistingEntries:N0}): {logMergeTime:F2} ms");
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void HistoryStoreSaveTimings()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "history.json");
            var store = new JsonStore<HistoricoEntry>(path);
            var entries = Enumerable.Range(0, ExistingEntries).Select(index => new HistoricoEntry
            {
                Id = $"history-{index}",
                Timestamp = DateTime.UtcNow.AddSeconds(-index),
                Titulo = "Aviso de teste",
                Mensagem = new string('x', 300),
            }).ToList();
            var measurements = new List<double>();
            for (var i = 0; i < 5; i++) measurements.Add(Time(() => store.Save(entries)));
            measurements.Sort();
            output.WriteLine($"Historico Save (10.000): {measurements[2]:F2} ms mediana; {new FileInfo(path).Length:N0} bytes");
            Assert.Equal(ExistingEntries, store.Load().Count);
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ProfileMergeAndTrimTimings()
    {
        WithTemporaryStore(directory =>
        {
            var now = DateTime.UtcNow;
            var store = new JsonStore<PerfilComputador>(Path.Combine(directory, "profiles.json"));
            store.Save(Enumerable.Range(0, 250).Select(index => new PerfilComputador
            {
                ComputerId = $"old-{index}", AtualizadoEmUtc = now.AddDays(-1),
            }));
            var repository = new PerfilComputadorRepository(store);
            var remote = Enumerable.Range(0, 250).Select(index => new PerfilComputadorSincronizado
            {
                ComputerId = $"new-{index}", UpdatedBy = $"new-{index}",
                UpdatedAt = now.AddSeconds(index).ToString("o"),
            }).ToArray();

            var elapsed = Time(() => Assert.Equal(250, repository.Mesclar(remote)));
            output.WriteLine($"Perfis Mesclar e aparar (250 + 250): {elapsed:F2} ms");
            Assert.Equal(250, repository.Snapshot().Count);
            Assert.Null(repository.Obter("old-0"));
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void LargeFrameTimings()
    {
        var message = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ping);
        message.Token = new string('a', 8 * 1024 * 1024);
        var measurements = new List<double>();
        byte[] frame = [];
        for (var i = 0; i < 5; i++) measurements.Add(Time(() => frame = MessageValidator.Frame(message)));
        measurements.Sort();
        output.WriteLine($"Frame JSON (8 MiB): {measurements[2]:F2} ms mediana; {frame.Length:N0} bytes");
        Assert.Equal((byte)'\n', frame[^1]);
    }

    private static double Time(Action action)
    {
        var timer = Stopwatch.StartNew();
        action();
        return timer.Elapsed.TotalMilliseconds;
    }

    private static void WithTemporaryStore(Action<string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            assertion(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
