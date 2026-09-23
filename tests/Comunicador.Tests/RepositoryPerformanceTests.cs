using System.Diagnostics;
using System.IO;
using Comunicador.Models;
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
