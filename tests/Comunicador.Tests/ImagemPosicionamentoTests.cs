using System.Windows;
using Comunicador.Views;
using Xunit;

namespace Comunicador.Tests;

public class ImagemPosicionamentoTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(50, 50, 864, 486)]
    [InlineData(100, 100, 1728, 972)]
    public void ImagemPequenaSegueOCentroEscolhidoSemSairDoMonitor(
        double xPercent, double yPercent, double expectedX, double expectedY)
    {
        var point = NotificacaoRecebidaWindow.CalcularPosicaoImagem(
            new Rect(0, 0, 1920, 1080), 192, 108, xPercent, yPercent);

        Assert.Equal(expectedX, point.X);
        Assert.Equal(expectedY, point.Y);
    }

    [Fact]
    public void PosicaoRespeitaOrigemDeUmSegundoMonitor()
    {
        var point = NotificacaoRecebidaWindow.CalcularPosicaoImagem(
            new Rect(1920, -200, 1280, 1024), 128, 100, 25, 75);

        Assert.Equal(2176, point.X);
        Assert.Equal(518, point.Y);
    }
}
