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
}
