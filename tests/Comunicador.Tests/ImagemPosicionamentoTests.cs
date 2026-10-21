using System.Windows;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Views;
using Xunit;

namespace Comunicador.Tests;

public class ImagemPosicionamentoTests
{
    [Fact]
    public void SnapshotMostraLocalRealDaImagemNoMonitor()
    {
        using var document = JsonDocument.Parse("""
            {"id":"image-1","name":"cartaz.png","monitor_index":1,
             "x_percent":10,"y_percent":10,"width_percent":20,
             "left_percent":0,"top_percent":0,
             "display_width_percent":20,"display_height_percent":20}
            """);

        var image = ImagemPersistenteAtiva.FromSnapshot("computer-1", document.RootElement);

        Assert.NotNull(image);
        Assert.Equal("Monitor 2", image.MonitorTexto);
        Assert.Equal(0, image.PreviewLeft);
        Assert.Equal(0, image.PreviewTop);
        Assert.Equal(64, image.PreviewWidth);
        Assert.Equal(36, image.PreviewHeight);
    }

    [Fact]
    public void MiniaturaVisualNaoEntraNoArquivoDeComputadores()
    {
        const string tinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/RkwAAAAASUVORK5CYII=";
        using var document = JsonDocument.Parse($$"""
            {"id":"image-1","name":"cartaz.png","thumbnail_base64":"{{tinyPng}}"}
            """);
        var image = ImagemPersistenteAtiva.FromSnapshot("computer-1", document.RootElement);

        Assert.NotNull(image);
        Assert.NotNull(image.Miniatura);
        var json = JsonSerializer.Serialize(image);
        Assert.DoesNotContain("Miniatura", json);
        Assert.Contains("image-1", json);
    }

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
