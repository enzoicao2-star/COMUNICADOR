using System.IO;
using Comunicador.Models;
using Comunicador.Services;
using Comunicador.Storage;
using Xunit;

namespace Comunicador.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public void JsonStore_LeArquivoLegadoIndentadoESubstituiSemArquivoTemporario()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "history.json");
            File.WriteAllText(path, "[\n  { \"Id\": \"antigo\" }\n]");
            var store = new JsonStore<HistoricoEntry>(path);

            Assert.Equal("antigo", Assert.Single(store.Load()).Id);
            store.Save([new HistoricoEntry { Id = "novo" }]);

            Assert.Equal("novo", Assert.Single(store.Load()).Id);
            Assert.False(File.Exists(path + ".tmp"));
        });
    }

    [Fact]
    public void JsonStore_SalvaEmRecuperacaoQuandoArquivoPrincipalEstaBloqueado()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "history.json");
            File.WriteAllText(path, "[]");
            var store = new JsonStore<HistoricoEntry>(path);

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                store.Save([new HistoricoEntry { Id = "saved-during-lock" }]);
                Assert.Equal("saved-during-lock", Assert.Single(store.Load()).Id);
                Assert.True(File.Exists(path + ".recovery"));
            }

            store.Save([new HistoricoEntry { Id = "primary-restored" }]);

            Assert.Equal("primary-restored", Assert.Single(store.Load()).Id);
            Assert.False(File.Exists(path + ".recovery"));
        });
    }

    [Fact]
    public void JsonStore_ConservaSnapshotQuandoPrincipalERecuperacaoEstaoBloqueados()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "history.json");
            File.WriteAllText(path, "[]");
            File.WriteAllText(path + ".recovery", "[]");
            var store = new JsonStore<HistoricoEntry>(path);

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                for (var index = 0; index < 5; index++)
                    store.Save([new HistoricoEntry { Id = $"snapshot-{index}" }]);
                Assert.Equal("snapshot-4", Assert.Single(store.Load()).Id);
                Assert.InRange(Directory.EnumerateFiles(directory, "history.json.*.tmp").Count(), 1, 3);
            }

            store.Save([new HistoricoEntry { Id = "primary-restored" }]);
            Assert.Equal("primary-restored", Assert.Single(store.Load()).Id);
        });
    }

    [Fact]
    public async Task AtomicFileStore_ConcurrentWritersLeaveOneCompleteFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "state.json");
            var expected = Enumerable.Range(0, 4)
                .Select(index => $"{{\"writer\":{index},\"payload\":\"{new string((char)('a' + index), 8192)}\"}}")
                .ToArray();
            var writes = Enumerable.Range(0, expected.Length).Select(index => Task.Run(() =>
            {
                for (var iteration = 0; iteration < 20; iteration++)
                    AtomicFileStore.WriteAllText(path, expected[index]);
            }));

            await Task.WhenAll(writes);

            Assert.Contains(File.ReadAllText(path), expected);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicFileStore_RecuperaConfiguracaoQuandoArquivoPrincipalEstaAberto()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "config.json");
            AtomicFileStore.WriteAllText(path, "{\"version\":1}");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                AtomicFileStore.WriteAllTextWithRecovery(path, "{\"version\":2}");
                Assert.Equal("{\"version\":2}", AtomicFileStore.ReadAllTextWithRecovery(path));
                Assert.True(File.Exists(path + ".recovery"));
            }

            AtomicFileStore.WriteAllTextWithRecovery(path, "{\"version\":3}");

            Assert.Equal("{\"version\":3}", AtomicFileStore.ReadAllTextWithRecovery(path));
            Assert.False(File.Exists(path + ".recovery"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        });
    }

    [Fact]
    public void AtomicFileStore_ConservaSnapshotQuandoOsDoisDestinosEstaoBloqueados()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "{\"version\":1}");
            File.WriteAllText(path + ".recovery", "{\"version\":1}");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                for (var version = 2; version <= 6; version++)
                    AtomicFileStore.WriteAllTextWithRecovery(path, $"{{\"version\":{version}}}");
                Assert.Equal("{\"version\":6}", AtomicFileStore.ReadAllTextWithRecovery(path));
                Assert.InRange(Directory.EnumerateFiles(directory, "config.json.*.tmp").Count(), 1, 3);
            }

            AtomicFileStore.WriteAllTextWithRecovery(path, "{\"version\":7}");
            Assert.Equal("{\"version\":7}", AtomicFileStore.ReadAllTextWithRecovery(path));
        });
    }

    [Fact]
    public void Historico_NaoInterrompeOperacaoQuandoArquivoPrincipalERecuperacaoEstaoBloqueados()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "history.json");
            File.WriteAllText(path, "[]");
            File.WriteAllText(path + ".recovery", "[]");
            var store = new JsonStore<HistoricoEntry>(path);
            var history = new HistoricoRepository(store);

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var entry = new HistoricoEntry { Id = "kept-in-memory" };

                var exception = Record.Exception(() => history.Adicionar(entry));

                Assert.Null(exception);
                Assert.Equal("kept-in-memory", Assert.Single(history.Itens).Id);
            }
        });
    }

    [Fact]
    public void Perfis_MantemEdicaoEmMemoriaQuandoArquivosLocaisEstaoBloqueados()
    {
        WithTemporaryStore(directory =>
        {
            var path = Path.Combine(directory, "profiles.json");
            File.WriteAllText(path, "[]");
            File.WriteAllText(path + ".recovery", "[]");
            var repository = new PerfilComputadorRepository(new JsonStore<PerfilComputador>(path));

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var exception = Record.Exception(() => repository.Salvar(
                    "computer-1", "Nome atualizado", false, [], "computer-1"));

                Assert.Null(exception);
                Assert.Equal("Nome atualizado", repository.Obter("computer-1")?.NomePublico);
            }
        });
    }

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
