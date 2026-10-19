using System.IO;
using System.Text.Json;
using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class PanelUpdateProgressTests
{
    [Fact]
    public void LeContagemDeReinicioGravadaPeloAtualizador()
    {
        const string json = """
            {"phase":"restart_wait","percent":100,"message":"Reiniciando","remaining_seconds":10}
            """;

        var progress = JsonSerializer.Deserialize<PanelUpdateProgress>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(progress);
        Assert.Equal("restart_wait", progress.Phase);
        Assert.Equal(10, progress.RemainingSeconds);
    }

    [Fact]
    public async Task LeCopiaTemporariaQuandoTrocaDoArquivoDeProgressoFalha()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "progress.json");
            File.WriteAllText(path, "{\"phase\":\"running\",\"percent\":5}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            File.WriteAllText(path + ".tmp", "{\"phase\":\"done\",\"percent\":100}");

            var progress = await PanelUpdateService.ReadLatestProgressAsync(
                path, DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.NotNull(progress);
            Assert.Equal("done", progress.Phase);
            Assert.Equal(100, progress.Percent);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task IgnoraProgressoAntigoDeOutraAtualizacao()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Comunicador-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "progress.json");
            File.WriteAllText(path + ".tmp", "{\"phase\":\"done\"}");
            File.SetLastWriteTimeUtc(path + ".tmp", DateTime.UtcNow.AddMinutes(-10));

            var progress = await PanelUpdateService.ReadLatestProgressAsync(
                path, DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Null(progress);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
