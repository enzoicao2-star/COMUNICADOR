using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

/// <summary>Botões de resposta e, principalmente, a validação do link: ele chega
/// pela rede, então só http/https pode passar.</summary>
public class BotoesRespostaTests
{
    private static ComunicadorMessage Notificacao(params BotaoResposta[] botoes)
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
        msg.Token = "abc123";
        msg.Sender = "PAINEL";
        msg.Title = "Aviso";
        msg.Message = "Corpo";
        msg.AllowReply = true;
        if (botoes.Length > 0)
        {
            msg.Buttons = botoes.ToList();
        }

        return msg;
    }

    [Fact]
    public void SemBotoes_ContinuaValido()
    {
        Assert.True(MessageValidator.Validate(Notificacao()).IsValid);
    }

    [Fact]
    public void BotaoSoComRotulo_EhValido()
    {
        var r = MessageValidator.Validate(Notificacao(new BotaoResposta { Label = "Estou indo" }));
        Assert.True(r.IsValid);
    }

    [Theory]
    [InlineData("http://exemplo.com/a")]
    [InlineData("https://exemplo.com/a")]
    [InlineData("HTTPS://EXEMPLO.COM")]
    public void LinkHttpOuHttps_EhValido(string url)
    {
        var r = MessageValidator.Validate(Notificacao(new BotaoResposta { Label = "Abrir", Url = url }));
        Assert.True(r.IsValid);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:windowsdefender")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://exemplo.com/arquivo")]
    [InlineData(@"\\servidor\compartilhamento\coisa.exe")]
    public void EsquemasPerigosos_SaoRecusados(string url)
    {
        var r = MessageValidator.Validate(Notificacao(new BotaoResposta { Label = "Clique", Url = url }));
        Assert.False(r.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.InvalidFieldType, r.Code);
    }

    [Fact]
    public void UrlPermitida_ConcordaComOValidador()
    {
        Assert.True(BotaoResposta.UrlPermitida("https://a.com"));
        Assert.True(BotaoResposta.UrlPermitida("http://a.com"));
        Assert.False(BotaoResposta.UrlPermitida("file:///x"));
        Assert.False(BotaoResposta.UrlPermitida("javascript:x"));
        Assert.False(BotaoResposta.UrlPermitida(""));
        Assert.False(BotaoResposta.UrlPermitida(null));
    }

    [Fact]
    public void BotaoSemRotulo_EhRecusado()
    {
        var r = MessageValidator.Validate(Notificacao(new BotaoResposta { Url = "https://exemplo.com" }));
        Assert.False(r.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, r.Code);
    }

    [Fact]
    public void RotuloMuitoLongo_EhRecusado()
    {
        var r = MessageValidator.Validate(Notificacao(
            new BotaoResposta { Label = new string('x', ProtocolConstants.MaxBotaoLabelLength + 1) }));
        Assert.False(r.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.FieldTooLong, r.Code);
    }

    [Fact]
    public void BotoesDemais_SaoRecusados()
    {
        var demais = Enumerable.Range(0, ProtocolConstants.MaxBotoes + 1)
            .Select(i => new BotaoResposta { Label = $"b{i}" })
            .ToArray();

        var r = MessageValidator.Validate(Notificacao(demais));
        Assert.False(r.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.FieldTooLong, r.Code);
    }

    [Fact]
    public void BotoesSobrevivemAoRoundTripJson()
    {
        var msg = Notificacao(
            new BotaoResposta { Label = "Abrir relatório", Url = "https://exemplo.com/rel" },
            new BotaoResposta { Label = "Estou indo" });

        var framed = MessageValidator.Frame(msg);
        var semQuebra = framed[..^1];

        Assert.True(MessageValidator.TryParse(semQuebra, out var lido, out _));
        Assert.NotNull(lido!.Buttons);
        Assert.Equal(2, lido.Buttons!.Count);
        Assert.Equal("Abrir relatório", lido.Buttons[0].Label);
        Assert.Equal("https://exemplo.com/rel", lido.Buttons[0].Url);
        Assert.True(lido.Buttons[0].TemLink);
        Assert.Null(lido.Buttons[1].Url);
        Assert.False(lido.Buttons[1].TemLink);
    }
}
