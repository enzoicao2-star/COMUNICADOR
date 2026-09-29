using Comunicador.Models;
using Xunit;

namespace Comunicador.Tests;

public sealed class ComputadorPresentationTests
{
    [Fact]
    public void BadgeIgualRecebidaNovamente_PreservaInstanciaVisual()
    {
        var computador = new Computador { Id = "painel-a" };
        computador.Badges = [new BadgeUsuario { Id = "admin", Texto = "ADMIN" }];
        var badgesOriginais = computador.Badges;
        var alteracoes = 0;
        computador.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Computador.Badges)) alteracoes++;
        };

        computador.Badges = [badgesOriginais[0].Clone()];
        Assert.Same(badgesOriginais, computador.Badges);
        Assert.Equal(0, alteracoes);

        computador.Badges = [new BadgeUsuario { Id = "admin", Texto = "NOVO" }];
        Assert.Equal(1, alteracoes);
        Assert.Equal("NOVO", computador.Badges[0].Texto);
    }

    [Fact]
    public void Ping_AtualizaTextoSomenteQuandoValorExibidoMuda()
    {
        var computador = new Computador { Status = StatusComputador.Online };
        var alteracoes = 0;
        computador.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Computador.PingTexto)) alteracoes++;
        };

        computador.PingMs = 12.1;
        computador.PingMs = 12.2;
        computador.PingMs = 13.1;
        computador.Status = StatusComputador.Offline;

        Assert.Equal(3, alteracoes);
        Assert.Equal("— ms", computador.PingTexto);
    }
}
