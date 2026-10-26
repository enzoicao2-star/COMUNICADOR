using System.Text.Json;
using Comunicador.Models;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class ComputadorTests
{
    [Fact]
    public void ReceptorSemPainelContinuaIdentificavelNaLista()
    {
        var computador = new Computador { TemPainel = false, EnderecoIp = string.Empty };
        Assert.Contains("Somente receptor", computador.StatusVersaoPainel);
        Assert.Equal("registrado na nuvem", computador.EnderecoIpExibicao);
    }

    [Fact]
    public void QuantidadeMonitores_AcompanhaListaSemFazerParteDoJson()
    {
        var computador = new Computador();
        var notified = false;
        computador.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Computador.QuantidadeMonitores)) notified = true;
        };

        computador.Monitores = [new MonitorInfo(), new MonitorInfo()];

        Assert.Equal(2, computador.QuantidadeMonitores);
        Assert.True(notified);
        Assert.DoesNotContain(nameof(Computador.QuantidadeMonitores), JsonSerializer.Serialize(computador));
    }

    [Fact]
    public void SinalNaNuvemNaoAfirmaQueReceptorRespondeuDiretamente()
    {
        var now = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
        var computador = new Computador
        {
            UltimaVezVisto = now.AddSeconds(-10),
            UltimoSinalNuvemUtc = now.AddSeconds(-10),
            UltimaVerificacaoDiretaUtc = now.AddSeconds(-2),
            ConexaoDiretaOk = false,
        };

        computador.AtualizarDiagnostico(now, now);

        Assert.Contains("Ativo na nuvem", computador.DiagnosticoConexao);
        Assert.Contains("sem confirmação direta", computador.DiagnosticoConexao);
        Assert.DoesNotContain(nameof(Computador.DiagnosticoConexao), JsonSerializer.Serialize(computador));
    }

    [Fact]
    public void SincronizacaoAtrasadaNaoClassificaComputadorComoDesligado()
    {
        var now = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
        var computador = new Computador { UltimaVezVisto = now.AddMinutes(-2) };

        computador.AtualizarDiagnostico(now, now.AddMinutes(-1));

        Assert.Contains("Sincronização atrasada", computador.DiagnosticoConexao);
        Assert.DoesNotContain("desligado", computador.DiagnosticoConexao);
    }
}
