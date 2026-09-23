using System.Text.Json;
using Comunicador.Models;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class ComputadorTests
{
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
}
