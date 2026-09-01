using System.IO;
using System.Net.Sockets;
using Comunicador.Protocol;

namespace Comunicador.Networking;

/// <summary>TCP client used by the panel to talk to a single receptor: pairing, health-check
/// pings and sending notifications (optionally waiting for a reply on the same connection).</summary>
public sealed class ReceptorClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMinutes(5);

    private readonly string _panelId;
    private readonly string _panelName;

    public ReceptorClient(string panelId, string panelName)
    {
        _panelId = panelId;
        _panelName = panelName;
    }

    public async Task<PairResult> PairAsync(string ipAddress, int tcpPort, CancellationToken ct = default)
    {
        using var client = await ConnectAsync(ipAddress, tcpPort, ConnectTimeout, ct).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.PairRequest);
        request.PanelId = _panelId;
        request.PanelName = _panelName;
        await TcpFraming.WriteMessageAsync(stream, request, ct).ConfigureAwait(false);

        var response = await ReadValidatedAsync(stream, ct).ConfigureAwait(false);
        if (response.Type != ProtocolConstants.MessageType.PairResponse)
        {
            throw new ReceptorComunicacaoException($"Resposta inesperada ao parear: '{response.Type}'.");
        }

        if (response.Accepted != true)
        {
            throw new ReceptorComunicacaoException("Pareamento recusado pelo receptor.");
        }

        return new PairResult(true, response.ComputerId!, response.ComputerName!, response.Token!);
    }

    public async Task<bool> PingAsync(string ipAddress, int tcpPort, string token, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PingTimeout);

            using var client = await ConnectAsync(ipAddress, tcpPort, ConnectTimeout, cts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();

            var ping = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ping);
            ping.Token = token;
            await TcpFraming.WriteMessageAsync(stream, ping, cts.Token).ConfigureAwait(false);

            var response = await ReadValidatedAsync(stream, cts.Token).ConfigureAwait(false);
            return response.Type == ProtocolConstants.MessageType.Pong;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ReceptorComunicacaoException)
        {
            return false;
        }
    }

    public async Task<NotificationResult> SendNotificationAsync(
        string ipAddress, int tcpPort, string token, string title, string message, bool allowReply,
        CancellationToken ct = default)
    {
        try
        {
            using var client = await ConnectAsync(ipAddress, tcpPort, ConnectTimeout, ct).ConfigureAwait(false);
            await using var stream = client.GetStream();

            var notification = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            notification.Token = token;
            notification.Sender = _panelName;
            notification.Title = title;
            notification.Message = message;
            notification.AllowReply = allowReply;
            await TcpFraming.WriteMessageAsync(stream, notification, ct).ConfigureAwait(false);

            var ack = await ReadValidatedAsync(stream, ct).ConfigureAwait(false);
            if (ack.Type == ProtocolConstants.MessageType.Error)
            {
                return new NotificationResult(false, false, false, null, ack.Message);
            }

            if (ack.Type != ProtocolConstants.MessageType.Ack)
            {
                return new NotificationResult(false, false, false, null, $"Resposta inesperada: '{ack.Type}'.");
            }

            var shown = ack.Status == "shown";

            if (!allowReply)
            {
                return new NotificationResult(true, shown, false, null, null);
            }

            using var replyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            replyCts.CancelAfter(ReplyTimeout);
            try
            {
                var reply = await ReadValidatedAsync(stream, replyCts.Token).ConfigureAwait(false);
                if (reply.Type == ProtocolConstants.MessageType.Reply)
                {
                    return new NotificationResult(true, true, true, reply.ReplyText, null);
                }

                return new NotificationResult(true, shown, false, null, null);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                return new NotificationResult(true, shown, false, null, null);
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return new NotificationResult(false, false, false, null, ex.Message);
        }
    }

    private static async Task<TcpClient> ConnectAsync(string ipAddress, int port, TimeSpan timeout, CancellationToken ct)
    {
        var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(ipAddress, port, cts.Token).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new ReceptorComunicacaoException($"Tempo esgotado ao conectar em {ipAddress}:{port}.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<ComunicadorMessage> ReadValidatedAsync(Stream stream, CancellationToken ct)
    {
        var payload = await TcpFraming.ReadMessageAsync(stream, ct).ConfigureAwait(false);
        if (payload is null)
        {
            throw new ReceptorComunicacaoException("Conexão encerrada pelo receptor sem resposta.");
        }

        var sizeCheck = MessageValidator.ValidateSize(payload.Length, isUdp: false);
        if (!sizeCheck.IsValid)
        {
            throw new ReceptorComunicacaoException(sizeCheck.Message!);
        }

        if (!MessageValidator.TryParse(payload, out var msg, out var parseResult) || msg is null)
        {
            throw new ReceptorComunicacaoException(parseResult.Message!);
        }

        var structResult = MessageValidator.Validate(msg);
        if (!structResult.IsValid)
        {
            throw new ReceptorComunicacaoException(structResult.Message!);
        }

        return msg;
    }
}
