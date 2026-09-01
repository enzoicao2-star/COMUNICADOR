using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Comunicador.Views;

namespace Comunicador.Networking;

/// <summary>Faz este Comunicador agir como seu próprio receptor: aceita pareamento,
/// ping e notificações de outros painéis pela rede, sem precisar do receptor.py
/// nesta máquina. Pode ser desligado a qualquer momento via
/// AppSettings.AceitarMensagensDeOutrosPaineis (bloqueia toda mensagem recebida).</summary>
public sealed class EmbeddedReceptorServer : IDisposable
{
    private static readonly TimeSpan ReplyWait = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly ObservableCollection<PainelPareado> _paineisPareados;
    private readonly JsonStore<PainelPareado> _paineisPareadosStore;
    private readonly HistoricoRepository _historico;

    private TcpListener? _tcpListener;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    public bool Ativo { get; private set; }

    /// <summary>Preenchido quando Start() não conseguiu ativar o receptor embutido —
    /// tipicamente porque receptor.py (ou outra instância) já está usando a porta nesta
    /// máquina. Nesse caso o painel segue funcionando normalmente para ENVIAR mensagens;
    /// ele só não vai também receber, e quem já está ouvindo aquela porta continua ouvindo.</summary>
    public string? UltimoErro { get; private set; }

    public EmbeddedReceptorServer(
        AppSettings settings, ObservableCollection<PainelPareado> paineisPareados,
        JsonStore<PainelPareado> paineisPareadosStore, HistoricoRepository historico)
    {
        _settings = settings;
        _paineisPareados = paineisPareados;
        _paineisPareadosStore = paineisPareadosStore;
        _historico = historico;
    }

    public void AtualizarDisponibilidade()
    {
        if (_settings.AceitarMensagensDeOutrosPaineis && !Ativo)
        {
            Start();
        }
        else if (!_settings.AceitarMensagensDeOutrosPaineis && Ativo)
        {
            Stop();
        }
    }

    public void Start()
    {
        if (Ativo)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        TcpListener? tcpListener = null;
        UdpClient? udpClient = null;

        try
        {
            // Sem ReuseAddress de propósito: se a porta já estiver em uso (ex.: receptor.py
            // já rodando nesta máquina), queremos uma falha clara aqui, não os dois
            // processos dividindo a mesma porta de forma imprevisível.
            tcpListener = new TcpListener(IPAddress.Any, _settings.PortaTcp);
            tcpListener.Start();

            udpClient = new UdpClient();
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _settings.PortaDescobertaUdp));
        }
        catch (SocketException ex)
        {
            tcpListener?.Stop();
            udpClient?.Dispose();
            cts.Dispose();

            UltimoErro =
                $"Receptor embutido não ativado: a porta TCP {_settings.PortaTcp} ou UDP {_settings.PortaDescobertaUdp} " +
                "já está em uso nesta máquina (provavelmente o receptor.py já está rodando aqui). " +
                "O Comunicador continua funcionando normalmente para enviar mensagens; quem já está " +
                "ouvindo essa porta continua recebendo normalmente.";
            Logger.Info(UltimoErro + $" (SocketErrorCode={ex.SocketErrorCode})");
            Ativo = false;
            return;
        }

        _cts = cts;
        _tcpListener = tcpListener;
        _udpClient = udpClient;

        _ = AceitarConexoesAsync(_cts.Token);
        _ = ResponderDescobertaAsync(_cts.Token);

        UltimoErro = null;
        Ativo = true;
        Logger.Info($"Receptor embutido ativo (TCP {_settings.PortaTcp}, UDP {_settings.PortaDescobertaUdp}).");
    }

    public void Stop()
    {
        if (!Ativo)
        {
            return;
        }

        _cts?.Cancel();
        _tcpListener?.Stop();
        _udpClient?.Close();
        _udpClient?.Dispose();
        Ativo = false;
        Logger.Info("Receptor embutido desativado (bloqueado nas configurações ou encerrando).");
    }

    private async Task AceitarConexoesAsync(CancellationToken ct)
    {
        if (_tcpListener is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => TratarConexaoAsync(client, ct), ct);
        }
    }

    private async Task TratarConexaoAsync(TcpClient client, CancellationToken ct)
    {
        using var clientDisposable = client;
        await using var stream = client.GetStream();

        byte[]? payload;
        try
        {
            payload = await TcpFraming.ReadMessageAsync(stream, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            await EnviarAsync(
                stream,
                ComunicadorMessage.Error(ProtocolConstants.ErrorCode.PayloadTooLarge, "Mensagem excede o tamanho máximo."),
                ct).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (payload is null)
        {
            return;
        }

        var sizeCheck = MessageValidator.ValidateSize(payload.Length, isUdp: false);
        if (!sizeCheck.IsValid)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(sizeCheck.Code!, sizeCheck.Message!), ct).ConfigureAwait(false);
            return;
        }

        if (!MessageValidator.TryParse(payload, out var msg, out var parseResult) || msg is null)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(parseResult.Code!, parseResult.Message!), ct).ConfigureAwait(false);
            return;
        }

        var structCheck = MessageValidator.Validate(msg);
        if (!structCheck.IsValid)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(structCheck.Code!, structCheck.Message!, msg.Id), ct).ConfigureAwait(false);
            return;
        }

        switch (msg.Type)
        {
            case ProtocolConstants.MessageType.Ping:
                await TratarPingAsync(stream, msg, ct).ConfigureAwait(false);
                break;
            case ProtocolConstants.MessageType.PairRequest:
                await TratarPairRequestAsync(stream, msg, ct).ConfigureAwait(false);
                break;
            case ProtocolConstants.MessageType.Notification:
                await TratarNotificationAsync(stream, msg, ct).ConfigureAwait(false);
                break;
            default:
                await EnviarAsync(
                    stream,
                    ComunicadorMessage.Error(ProtocolConstants.ErrorCode.UnknownType, $"Tipo não esperado nesta conexão: '{msg.Type}'", msg.Id),
                    ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task TratarPingAsync(NetworkStream stream, ComunicadorMessage msg, CancellationToken ct)
    {
        if (!TokenValido(msg.Token))
        {
            await EnviarAsync(
                stream,
                ComunicadorMessage.Error(ProtocolConstants.ErrorCode.Unauthorized, "Token inválido ou painel não pareado.", msg.Id),
                ct).ConfigureAwait(false);
            return;
        }

        var pong = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Pong);
        pong.ComputerId = _settings.PainelId;
        pong.ComputerName = _settings.NomePainel;
        pong.Status = "online";
        await EnviarAsync(stream, pong, ct).ConfigureAwait(false);
    }

    private async Task TratarPairRequestAsync(NetworkStream stream, ComunicadorMessage msg, CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var pareado = new PainelPareado
        {
            PanelId = msg.PanelId!,
            PanelName = msg.PanelName!,
            Token = token,
            PareadoEm = DateTime.UtcNow,
        };

        UiDispatcher.Invoke(() =>
        {
            var existente = _paineisPareados.FirstOrDefault(p => p.PanelId == pareado.PanelId);
            if (existente is not null)
            {
                _paineisPareados.Remove(existente);
            }

            _paineisPareados.Add(pareado);
            _paineisPareadosStore.Save(_paineisPareados);
        });

        Logger.Info($"Pareado com painel '{pareado.PanelName}' ({pareado.PanelId}).");

        var response = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.PairResponse);
        response.Accepted = true;
        response.ComputerId = _settings.PainelId;
        response.ComputerName = _settings.NomePainel;
        response.Token = token;
        await EnviarAsync(stream, response, ct).ConfigureAwait(false);
    }

    private async Task TratarNotificationAsync(NetworkStream stream, ComunicadorMessage msg, CancellationToken ct)
    {
        if (!TokenValido(msg.Token))
        {
            await EnviarAsync(
                stream,
                ComunicadorMessage.Error(ProtocolConstants.ErrorCode.Unauthorized, "Token inválido ou painel não pareado.", msg.Id),
                ct).ConfigureAwait(false);
            return;
        }

        var allowReply = msg.AllowReply == true;
        var entry = new HistoricoEntry
        {
            Direcao = DirecaoHistorico.Recebida,
            ComputadorNome = msg.Sender!,
            Titulo = msg.Title!,
            Mensagem = msg.Message!,
            Status = StatusEnvio.Exibido,
        };
        _historico.Adicionar(entry);

        var mostrarTask = NotificacaoRecebidaWindow.MostrarAsync(msg.Sender!, msg.Title!, msg.Message!, allowReply);

        var ack = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
        ack.InReplyTo = msg.Id;
        ack.Status = "shown";
        await EnviarAsync(stream, ack, ct).ConfigureAwait(false);

        if (!allowReply)
        {
            return;
        }

        string? resposta;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ReplyWait);
            resposta = await mostrarTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.IsNullOrEmpty(resposta))
        {
            return;
        }

        _historico.AtualizarExistente(entry.Id, item =>
        {
            item.Status = StatusEnvio.Respondido;
            item.RespostaTexto = resposta;
        });

        var reply = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Reply);
        reply.InReplyTo = msg.Id;
        reply.ComputerId = _settings.PainelId;
        reply.ComputerName = _settings.NomePainel;
        reply.ReplyText = resposta;
        await EnviarAsync(stream, reply, ct).ConfigureAwait(false);
    }

    private bool TokenValido(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var encontrado = false;
        UiDispatcher.Invoke(() => encontrado = _paineisPareados.Any(p => p.Token == token));
        return encontrado;
    }

    private async Task ResponderDescobertaAsync(CancellationToken ct)
    {
        if (_udpClient is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            if (!MessageValidator.ValidateSize(result.Buffer.Length, isUdp: true).IsValid)
            {
                continue;
            }

            if (!MessageValidator.TryParse(result.Buffer, out var msg, out _) || msg is null)
            {
                continue;
            }

            if (msg.Type != ProtocolConstants.MessageType.Discover || !MessageValidator.Validate(msg).IsValid)
            {
                continue;
            }

            var pareadoComEsse = false;
            UiDispatcher.Invoke(() => pareadoComEsse = _paineisPareados.Any(p => p.PanelId == msg.PanelId));

            var announce = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Announce);
            announce.ComputerId = _settings.PainelId;
            announce.ComputerName = _settings.NomePainel;
            announce.TcpPort = _settings.PortaTcp;
            announce.Paired = pareadoComEsse;

            try
            {
                var framed = MessageValidator.Frame(announce);
                await _udpClient.SendAsync(framed, framed.Length, result.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // rede indisponível momentaneamente; ignora e segue ouvindo.
            }
        }
    }

    private static async Task EnviarAsync(NetworkStream stream, ComunicadorMessage message, CancellationToken ct)
    {
        try
        {
            await TcpFraming.WriteMessageAsync(stream, message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // peer já desconectou; nada a fazer.
        }
    }

    public void Dispose() => Stop();
}
