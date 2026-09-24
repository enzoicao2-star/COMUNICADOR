using System.Net;
using System.Net.Sockets;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Xunit;

namespace Comunicador.Tests;

public sealed class AtualizadorReceptorTests
{
    [Fact]
    public async Task AtualizacaoDireta_SoConcluiDepoisDoPingNaVersaoNova()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var servidor = Task.Run(async () =>
            {
                using (var updateClient = await listener.AcceptTcpClientAsync(timeout.Token))
                {
                    var updateStream = updateClient.GetStream();
                    var bytes = await TcpFraming.ReadMessageAsync(updateStream, timeout.Token);
                    Assert.True(MessageValidator.TryParse(bytes!, out var request, out _));
                    Assert.Equal(ProtocolConstants.MessageType.UpdateRequest, request!.Type);
                    var status = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.UpdateStatus);
                    status.InReplyTo = request.Id;
                    status.Success = true;
                    status.Status = "updated";
                    status.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
                    await TcpFraming.WriteMessageAsync(updateStream, status, timeout.Token);
                }

                using var pingClient = await listener.AcceptTcpClientAsync(timeout.Token);
                var pingStream = pingClient.GetStream();
                var pingBytes = await TcpFraming.ReadMessageAsync(pingStream, timeout.Token);
                Assert.True(MessageValidator.TryParse(pingBytes!, out var ping, out _));
                Assert.Equal(ProtocolConstants.MessageType.Ping, ping!.Type);
                var pong = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Pong);
                pong.ComputerId = "pc-teste";
                pong.ComputerName = "PC-TESTE";
                pong.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
                pong.Status = "online";
                await TcpFraming.WriteMessageAsync(pingStream, pong, timeout.Token);
            }, timeout.Token);

            var computer = new Computador
            {
                Id = "pc-teste", Nome = "PC-TESTE", EnderecoIp = "127.0.0.1",
                PortaTcp = port, Token = "tok-teste", Pareado = true,
                VersaoReceptor = ProtocolConstants.CurrentReceiverVersion,
            };
            var progress = new List<ReceiverUpdateProgress>();
            var updater = new AtualizadorReceptor(
                new ReceptorClient("painel-teste", "PAINEL"), new RegistroConexoesReversas());
            var updating = updater.AtualizarAsync(computer, progress.Add, timeout.Token);
            await Task.Delay(300, timeout.Token);
            Assert.False(updating.IsCompleted);

            var result = await updating.WaitAsync(timeout.Token);
            await servidor.WaitAsync(timeout.Token);
            Assert.True(result.Success, result.Message);
            Assert.Equal(100, progress[^1].Percent);
            Assert.Contains(progress, value => value.Percent == 85);
        }
        finally
        {
            listener.Stop();
        }
    }
}
