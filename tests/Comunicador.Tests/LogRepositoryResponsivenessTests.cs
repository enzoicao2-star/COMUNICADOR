using System.Diagnostics;
using System.IO;
using Comunicador.Models;
using Comunicador.Services;
using Comunicador.Storage;
using Xunit;

namespace Comunicador.Tests;

public sealed class LogRepositoryResponsivenessTests
{
    [Fact]
    public async Task ArquivoBloqueadoNaoPrendeQuemRegistraOEvento()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "logs.json");
            var store = new JsonStore<LogEntry>(path);
            store.Save(Enumerable.Range(0, 10_000).Select(index => new LogEntry
            {
                Id = $"old-{index}", TimestampUtc = DateTime.UtcNow.AddSeconds(-index),
                Mensagem = new string('x', 100),
            }));
            var repository = new LogRepository(store);
            File.WriteAllText(path + ".recovery", "[]");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var watch = Stopwatch.StartNew();
                repository.Adicionar(new LogEntry { Id = "new", TimestampUtc = DateTime.UtcNow });
                watch.Stop();

                Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500),
                    $"Registrar log bloqueou por {watch.Elapsed.TotalMilliseconds:F0} ms.");
                await repository.FlushPendingAsync();
                Assert.Contains(store.Load(), entry => entry.Id == "new");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
