using System.Net;
using System.Net.Sockets;
using Comunicador.Networking;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

/// <summary>Exercita o caminho de conexao reversa: quem abre a conexao e o receptor,
/// e o painel manda a notificacao de volta pela mesma conexao. E o caminho que
/// dispensa porta de entrada aberta na maquina do receptor.</summary>
public class ConexaoReversaTests
{
    /// <summary>Monta um par realmente conectado: o "receptor" disca e o "painel" aceita,
    /// exatamente como acontece no fluxo reverso.</summary>
    private static async Task<(TcpClient ladoPainel, TcpClient ladoReceptor, TcpListener listener)> ParConectadoAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var porta = ((IPEndPoint)listener.LocalEndpoint).Port;

        var ladoReceptor = new TcpClient();
        var conectando = ladoReceptor.ConnectAsync(IPAddress.Loopback, porta);
        var ladoPainel = await listener.AcceptTcpClientAsync();
        await conectando;

        return (ladoPainel, ladoReceptor, listener);
    }

    [Fact]
    public async Task NotificacaoViaConexaoReversa_RecebeAckEResposta()
    {
        var (ladoPainel, ladoReceptor, listener) = await ParConectadoAsync();
        try
        {
            var conexao = new ConexaoReversa(
                ladoPainel, ladoPainel.GetStream(), "pc-quarto", "PC-QUARTO", "127.0.0.1");

            var notificacao = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            notificacao.Token = "tok";
            notificacao.Sender = "PAINEL";
            notificacao.Title = "Aviso";
            notificacao.Message = "Ola";
            notificacao.AllowReply = true;

            var envio = conexao.EnviarNotificacaoAsync(notificacao, true, TimeSpan.FromSeconds(15), default);

            var streamReceptor = ladoReceptor.GetStream();
            var recebida = await TcpFraming.ReadMessageAsync(streamReceptor);
            Assert.NotNull(recebida);
            MessageValidator.TryParse(recebida!, out var msgRecebida, out _);
            Assert.Equal(ProtocolConstants.MessageType.Notification, msgRecebida!.Type);
            Assert.Equal("Aviso", msgRecebida.Title);

            var ack = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
            ack.InReplyTo = msgRecebida.Id;
            ack.Status = "shown";
            await TcpFraming.WriteMessageAsync(streamReceptor, ack);

            var reply = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Reply);
            reply.InReplyTo = msgRecebida.Id;
            reply.ComputerId = "pc-quarto";
            reply.ComputerName = "PC-QUARTO";
            reply.ReplyText = "Recebido!";
            await TcpFraming.WriteMessageAsync(streamReceptor, reply);

            var resultado = await envio;
            Assert.True(resultado.Delivered);
            Assert.True(resultado.GotReply);
            Assert.Equal("Recebido!", resultado.ReplyText);
        }
        finally
        {
            ladoReceptor.Dispose();
            ladoPainel.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task SemPermitirResposta_NaoEsperaReply()
    {
        var (ladoPainel, ladoReceptor, listener) = await ParConectadoAsync();
        try
        {
            var conexao = new ConexaoReversa(
                ladoPainel, ladoPainel.GetStream(), "pc-quarto", "PC-QUARTO", "127.0.0.1");

            var notificacao = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            notificacao.Token = "tok";
            notificacao.Sender = "PAINEL";
            notificacao.Title = "Aviso";
            notificacao.Message = "Sem resposta";
            notificacao.AllowReply = false;

            var envio = conexao.EnviarNotificacaoAsync(notificacao, false, TimeSpan.FromSeconds(15), default);

            var streamReceptor = ladoReceptor.GetStream();
            var recebida = await TcpFraming.ReadMessageAsync(streamReceptor);
            MessageValidator.TryParse(recebida!, out var msgRecebida, out _);

            var ack = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
            ack.InReplyTo = msgRecebida!.Id;
            ack.Status = "shown";
            await TcpFraming.WriteMessageAsync(streamReceptor, ack);

            var resultado = await envio;
            Assert.True(resultado.Delivered);
            Assert.False(resultado.GotReply);
        }
        finally
        {
            ladoReceptor.Dispose();
            ladoPainel.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task RegistroConexoes_GuardaERemoveConexao()
    {
        var (ladoPainel, ladoReceptor, listener) = await ParConectadoAsync();
        try
        {
            var registro = new RegistroConexoesReversas();
            var conexao = new ConexaoReversa(
                ladoPainel, ladoPainel.GetStream(), "pc-quarto", "PC-QUARTO", "127.0.0.1");

            registro.Registrar(conexao);
            Assert.NotNull(registro.Obter("pc-quarto"));
            Assert.Single(registro.Todas());

            registro.Remover("pc-quarto");
            Assert.Null(registro.Obter("pc-quarto"));
            Assert.Empty(registro.Todas());
        }
        finally
        {
            ladoReceptor.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void ValidaRegister_ExigeComputerIdENome()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Register);
        var semNada = MessageValidator.Validate(msg);
        Assert.False(semNada.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, semNada.Code);

        msg.ComputerId = "pc-quarto";
        msg.ComputerName = "PC-QUARTO";
        Assert.True(MessageValidator.Validate(msg).IsValid);

        // token e opcional no register (na primeira conexao o receptor ainda nao tem)
        msg.Token = "abc";
        Assert.True(MessageValidator.Validate(msg).IsValid);
    }

    [Fact]
    public void ValidaRegisterAck_ExigeTokenQuandoAceito()
    {
        var msg = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.RegisterAck);
        msg.Accepted = true;
        var semToken = MessageValidator.Validate(msg);
        Assert.False(semToken.IsValid);
        Assert.Equal(ProtocolConstants.ErrorCode.MissingField, semToken.Code);

        msg.Token = "tok";
        Assert.True(MessageValidator.Validate(msg).IsValid);

        var recusado = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.RegisterAck);
        recusado.Accepted = false;
        Assert.True(MessageValidator.Validate(recusado).IsValid);
    }
}
