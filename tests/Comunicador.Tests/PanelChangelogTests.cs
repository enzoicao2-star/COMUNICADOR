using System.IO;
using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class PanelChangelogTests
{
    [Fact]
    public void ExecutavelIncluiHistoricoAteAVersaoAtual()
    {
        using var stream = typeof(PanelUpdateService).Assembly.GetManifestResourceStream(
            "Comunicador.Release.panel-changelog.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var text = PanelChangelog.Format(reader.ReadToEnd(), new Version(2, 5, 12, 0),
            new Version(2, 5, 14, 0), "Reserva", []);
        Assert.Contains("Versão 2.5.13.0", text);
        Assert.Contains("Versão 2.5.14.0", text);
        Assert.DoesNotContain("versÃ", text);
    }

    [Fact]
    public void MostraTodasAsVersoesEntreAInstaladaEAAtual()
    {
        const string json = """
            [
              {"version":"2.5.15.0","summary":"Antiga","changes":["Antes"]},
              {"version":"2.5.16.0","summary":"Primeira","changes":["Mudança 16"]},
              {"version":"2.5.20.0","summary":"Intermediária","changes":["Mudança 20"]},
              {"version":"2.5.25.0","summary":"Atual","changes":["Mudança 25"]},
              {"version":"2.5.26.0","summary":"Futura","changes":["Depois"]}
            ]
            """;

        var text = PanelChangelog.Format(json, new Version(2, 5, 15, 0),
            new Version(2, 5, 25, 0), "Reserva", []);

        Assert.DoesNotContain("Antes", text);
        Assert.Contains("Mudança 16", text);
        Assert.Contains("Mudança 20", text);
        Assert.Contains("Mudança 25", text);
        Assert.DoesNotContain("Depois", text);
        Assert.True(text.IndexOf("Mudança 16", StringComparison.Ordinal)
            < text.IndexOf("Mudança 20", StringComparison.Ordinal));
    }
}
