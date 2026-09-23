using System.IO;
using Comunicador.Models;
using Comunicador.Services;
using Comunicador.Storage;
using Xunit;

namespace Comunicador.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public void Historico_SnapshotEMesclar_MantemOrdemEAtualizaPorId()
    {
        WithTemporaryStore(directory =>
        {
            var now = DateTime.UtcNow;
            var store = new JsonStore<HistoricoEntry>(Path.Combine(directory, "history.json"));
            store.Save([
                new HistoricoEntry { Id = "older", Timestamp = now.AddHours(-3) },
                new HistoricoEntry { Id = "newest", Timestamp = now },
            ]);
            var repository = new HistoricoRepository(store);

            Assert.Equal(["newest"], repository.Snapshot(1).Select(entry => entry.Id));
            var changes = repository.Mesclar([
                new HistoricoEntry { Id = "middle", Timestamp = now.AddHours(-1) },
                new HistoricoEntry
                {
                    Id = "older", Timestamp = now.AddHours(-3), Status = StatusEnvio.Respondido,
                    RespostaTexto = "Recebido",
                },
            ]);

            Assert.Equal(2, changes);
            Assert.Equal(["newest", "middle", "older"], repository.Snapshot(3).Select(entry => entry.Id));
            Assert.Equal(StatusEnvio.Respondido, repository.Snapshot(3).Last().Status);
            Assert.Equal(0, repository.Mesclar([
                new HistoricoEntry { Id = "middle", Timestamp = now.AddHours(-1) },
            ]));
            repository.Adicionar(new HistoricoEntry { Id = "middle", Timestamp = now.AddHours(-1) });
            Assert.Equal(3, repository.Itens.Count);
        });
    }

    [Fact]
    public void Logs_SnapshotEMesclar_MantemOrdemEDeduplica()
    {
        WithTemporaryStore(directory =>
        {
            var now = DateTime.UtcNow;
            var store = new JsonStore<LogEntry>(Path.Combine(directory, "logs.json"));
            store.Save([
                new LogEntry { Id = "older", TimestampUtc = now.AddHours(-3) },
                new LogEntry { Id = "newest", TimestampUtc = now },
            ]);
            var repository = new LogRepository(store);

            Assert.Equal(1, repository.Mesclar([
                new LogEntry { Id = "middle", TimestampUtc = now.AddHours(-1) },
                new LogEntry { Id = "newest", TimestampUtc = now },
            ]));
            Assert.Equal(["newest", "middle", "older"], repository.Snapshot(3).Select(entry => entry.Id));
            Assert.Equal(0, repository.Mesclar([
                new LogEntry { Id = "middle", TimestampUtc = now.AddHours(-1) },
            ]));
        });
    }

    private static void WithTemporaryStore(Action<string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
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
