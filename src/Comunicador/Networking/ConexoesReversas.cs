using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using Comunicador.Protocol;

namespace Comunicador.Networking;

/// <summary>Uma conexao aberta PELO receptor em direcao ao painel, mantida viva para o
/// painel poder enviar notificacoes de volta por ela. Como quem disca e o receptor, a
/// maquina dele nao precisa de nenhuma porta de entrada liberada no firewall.</summary>
public sealed class ConexaoReversa : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _envioLock = new(1, 1);

    public string ComputerId { get; }
    public string ComputerName { get; }
    public string EnderecoIp { get; }
    public bool HasPanel { get; }
    public IReadOnlyList<MonitorInfo> Monitors { get; }
    public string? ReceiverVersion { get; }
    public string? PanelVersion { get; }
    public bool IsOwner { get; }

    /// <summary>Token emitido para este receptor no registro. Precisa acompanhar a
    /// conexao: sem ele a notificacao sai sem token e o receptor a rejeita na
    /// validacao do protocolo.</summary>
    public string Token { get; }

    public DateTime RegistradaEm { get; } = DateTime.UtcNow;

    public bool Conectada
    {
        get
        {
            try
            {
                return _client.Connected;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    public ConexaoReversa(
        TcpClient client, NetworkStream stream, string computerId, string computerName,
        string enderecoIp, string token, bool hasPanel = false,
        IReadOnlyList<MonitorInfo>? monitors = null, string? receiverVersion = null,
        string? panelVersion = null, bool isOwner = false)
    {
        _client = client;
        _stream = stream;
        ComputerId = computerId;
        ComputerName = computerName;
        EnderecoIp = enderecoIp;
        Token = token;
        HasPanel = hasPanel;
        Monitors = monitors is null ? Array.Empty<MonitorInfo>() : monitors.ToList();
        ReceiverVersion = receiverVersion;
        PanelVersion = panelVersion;
        IsOwner = isOwner;
    }

    /// <summary>Envia a notificacao pela conexao ja aberta e aguarda ack e, se pedido,
    /// a resposta do usuario. Serializado por conexao para duas notificacoes simultaneas
    /// nao embaralharem suas respostas no mesmo socket.</summary>
    public async Task<NotificationResult> EnviarNotificacaoAsync(
        ComunicadorMessage notificacao, bool aguardarResposta, TimeSpan timeoutResposta, CancellationToken ct)
    {
        await _envioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await TcpFraming.WriteMessageAsync(_stream, notificacao, ct).ConfigureAwait(false);

            var ack = await LerAsync(ct).ConfigureAwait(false);
            if (ack is null)
            {
                return new NotificationResult(false, false, false, null, "Conexao encerrada pelo receptor.");
            }

            if (ack.Type == ProtocolConstants.MessageType.Error)
            {
                return new NotificationResult(false, false, false, null, ack.Message);
            }

            if (ack.Type != ProtocolConstants.MessageType.Ack)
            {
                return new NotificationResult(false, false, false, null, $"Resposta inesperada: '{ack.Type}'.");
            }

            var exibida = ack.Status == "shown";
            if (!aguardarResposta)
            {
                return new NotificationResult(true, exibida, false, null, null);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutResposta);
            try
            {
                var resposta = await LerAsync(cts.Token).ConfigureAwait(false);
                if (resposta?.Type == ProtocolConstants.MessageType.Reply)
                {
                    return new NotificationResult(true, true, true, resposta.ReplyText, null);
                }

                return new NotificationResult(true, exibida, false, null, null);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                return new NotificationResult(true, exibida, false, null, null);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            return new NotificationResult(false, false, false, null, ex.Message);
        }
        finally
        {
            _envioLock.Release();
        }
    }

    public async Task<ReceiverUpdateResult> SolicitarAtualizacaoAsync(
        ComunicadorMessage solicitacao, CancellationToken ct)
    {
        await _envioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));
            await TcpFraming.WriteMessageAsync(_stream, solicitacao, cts.Token).ConfigureAwait(false);
            var resposta = await LerAsync(cts.Token).ConfigureAwait(false);
            if (resposta is null)
            {
                return new(false, "connection_closed", ReceiverVersion ?? string.Empty,
                    "A conexão foi encerrada antes da confirmação da atualização.");
            }

            if (resposta.Type == ProtocolConstants.MessageType.Error)
            {
                return new(false, "rejected", ReceiverVersion ?? string.Empty, resposta.Message);
            }

            if (resposta.Type != ProtocolConstants.MessageType.UpdateStatus)
            {
                return new(false, "unexpected_response", ReceiverVersion ?? string.Empty,
                    $"Resposta inesperada: '{resposta.Type}'.");
            }

            return new(
                resposta.Success == true,
                resposta.Status ?? "unknown",
                resposta.ReceiverVersion ?? ReceiverVersion ?? string.Empty,
                resposta.Message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
            or OperationCanceledException)
        {
            return new(false, "communication_error", ReceiverVersion ?? string.Empty, ex.Message);
        }
        finally
        {
            _envioLock.Release();
        }
    }

    public async Task<SyncResult> SincronizarAsync(ComunicadorMessage request, CancellationToken ct)
    {
        await _envioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await TcpFraming.WriteMessageAsync(_stream, request, cts.Token).ConfigureAwait(false);
            var response = await LerAsync(cts.Token).ConfigureAwait(false);
            if (response is null)
            {
                return new(false, [], [], [], "A conexão foi encerrada durante a coleta.");
            }
            if (response.Type == ProtocolConstants.MessageType.Error)
            {
                return new(false, [], [], [], response.Message);
            }
            if (response.Type != ProtocolConstants.MessageType.SyncResponse)
            {
                return new(false, [], [], [], $"Resposta inesperada: '{response.Type}'.");
            }
            return new(true, response.HistoryEntries ?? [], response.LogEntries ?? [],
                response.ComputerProfiles ?? [], null);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
            or OperationCanceledException)
        {
            return new(false, [], [], [], ex.Message);
        }
        finally
        {
            _envioLock.Release();
        }
    }

    /// <summary>Mede uma ida e volta pela conexão já aberta. Retorna null quando
    /// ela está ocupada com uma notificação/resposta; nesse caso continua online.</summary>
    public async Task<double?> MedirPingAsync(CancellationToken ct)
    {
        if (!await _envioLock.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var ping = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ping);
            ping.Token = Token;
            var cronometro = Stopwatch.StartNew();
            await TcpFraming.WriteMessageAsync(_stream, ping, cts.Token).ConfigureAwait(false);
            var resposta = await LerAsync(cts.Token).ConfigureAwait(false);
            cronometro.Stop();
            return resposta?.Type == ProtocolConstants.MessageType.Pong
                ? cronometro.Elapsed.TotalMilliseconds
                : -1;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
            or OperationCanceledException)
        {
            return -1;
        }
        finally
        {
            _envioLock.Release();
        }
    }

    private async Task<ComunicadorMessage?> LerAsync(CancellationToken ct)
    {
        var payload = await TcpFraming.ReadMessageAsync(_stream, ct).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        if (!MessageValidator.ValidateSize(payload.Length, isUdp: false).IsValid)
        {
            return null;
        }

        if (!MessageValidator.TryParse(payload, out var msg, out _) || msg is null)
        {
            return null;
        }

        return MessageValidator.Validate(msg).IsValid ? msg : null;
    }

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
            _client.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // ja liberado.
        }

        _envioLock.Dispose();
    }
}

/// <summary>Guarda as conexoes reversas ativas, uma por computador.</summary>
public sealed class RegistroConexoesReversas
{
    private readonly ConcurrentDictionary<string, ConexaoReversa> _conexoes = new();

    public event Action? Alterado;

    public void Registrar(ConexaoReversa conexao)
    {
        if (_conexoes.TryRemove(conexao.ComputerId, out var anterior))
        {
            anterior.Dispose();
        }

        _conexoes[conexao.ComputerId] = conexao;
        Alterado?.Invoke();
    }

    public void Remover(string computerId)
    {
        if (_conexoes.TryRemove(computerId, out var conexao))
        {
            conexao.Dispose();
            Alterado?.Invoke();
        }
    }

    public ConexaoReversa? Obter(string computerId) =>
        _conexoes.TryGetValue(computerId, out var conexao) && conexao.Conectada ? conexao : null;

    public IReadOnlyList<ConexaoReversa> Todas() =>
        _conexoes.Values.Where(c => c.Conectada).ToList();
}
