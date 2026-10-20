using System.Diagnostics;
using System.IO;
using Comunicador.Models;
using Comunicador.Storage;
using Xunit;

namespace Comunicador.Tests;

public sealed class AsyncJsonStoreWriterTests
{
    [Fact]
    public async Task ArquivoBloqueadoNaoPrendeInterfaceENovaAtualizacaoSubstituiAnterior()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "computadores.json");
            var store = new JsonStore<Computador>(path);
            store.Save([new Computador { Id = "pc", Nome = "Antigo" }]);
            File.WriteAllText(path + ".recovery", "[]");
            var writer = new AsyncJsonStoreWriter<Computador>(store);
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new FileStream(path + ".recovery", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var computer = new Computador { Id = "pc", Nome = "Primeiro" };
                var watch = Stopwatch.StartNew();
                writer.Queue([computer]);
                computer.Nome = "Mudou depois da cópia";
                writer.Queue([new Computador { Id = "pc", Nome = "Mais recente" }]);
                watch.Stop();
                Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500),
                    $"Salvar computadores bloqueou por {watch.Elapsed.TotalMilliseconds:F0} ms.");
                await writer.FlushAsync();
                Assert.Equal("Mais recente", Assert.Single(store.Load()).Nome);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
