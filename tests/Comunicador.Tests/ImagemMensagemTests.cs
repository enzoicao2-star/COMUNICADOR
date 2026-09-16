using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class ImagemMensagemTests
{
    private const string PngUmPixel =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private static ConteudoImagem Imagem() => new()
    {
        Name = "pixel.png",
        MimeType = "image/png",
        DataBase64 = PngUmPixel,
    };

    private static ComunicadorMessage NotificacaoCentral()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
        msg.Token = "token";
        msg.Sender = "PAINEL";
        msg.Title = "Imagem";
        msg.Message = "Confira";
        msg.AllowReply = false;
        msg.DisplayMode = ProtocolConstants.DisplayMode.CenterImage;
        msg.ImageDurationSeconds = 15;
        msg.AllowManualClose = true;
        msg.Image = Imagem();
        return msg;
    }

    [Fact]
    public void ImagemCentralValida_Passa()
    {
        Assert.True(MessageValidator.Validate(NotificacaoCentral()).IsValid);
    }

    [Fact]
    public void Base64Invalido_EhRecusado()
    {
        var msg = NotificacaoCentral();
        msg.Image!.DataBase64 = "não-base64";
        var resultado = MessageValidator.Validate(msg);
        Assert.False(resultado.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.InvalidFieldType, resultado.Code);
    }

    [Fact]
    public void AssinaturaDiferenteDoMime_EhRecusada()
    {
        var msg = NotificacaoCentral();
        msg.Image!.MimeType = "image/jpeg";
        Assert.False(MessageValidator.Validate(msg).IsValid);
    }

    [Fact]
    public void VariasImagensEmMonitores_SobrevivemAoJson()
    {
        var msg = NotificacaoCentral();
        msg.Image = null;
        msg.ScreenImages = new List<ImagemMonitor>
        {
            new() { MonitorIndex = 0, WidthPercent = 50, Image = Imagem() },
            new() { MonitorIndex = 1, WidthPercent = 80, Image = Imagem() },
        };
        msg.Appearance = new AparenciaNotificacao();

        Assert.True(MessageValidator.Validate(msg).IsValid);
        var payload = MessageValidator.Frame(msg)[..^1];
        Assert.True(MessageValidator.TryParse(payload, out var lido, out _));
        Assert.Equal(2, lido!.ScreenImages!.Count);
        Assert.Equal(1, lido.ScreenImages[1].MonitorIndex);
        Assert.Equal(80, lido.ScreenImages[1].WidthPercent);
    }

    [Fact]
    public void AvisoObrigatorio_NaoExigeImagem()
    {
        var msg = NotificacaoCentral();
        msg.DisplayMode = ProtocolConstants.DisplayMode.CenterAlert;
        msg.Image = null;
        msg.ImageDurationSeconds = null;
        msg.AllowManualClose = null;
        Assert.True(MessageValidator.Validate(msg).IsValid);
    }
}
