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
/// nesta máquina. As preferências do usuário filtram o conteúdo antes da exibição.</summary>
public sealed class EmbeddedReceptorServer : IDisposable
{
    private static readonly TimeSpan ReplyWait = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly ObservableCollection<PainelPareado> _paineisPareados;
    private readonly JsonStore<PainelPareado> _paineisPareadosStore;
    private readonly HistoricoRepository _historico;
    private readonly LogRepository _logs;
    private readonly RegistroConexoesReversas _conexoesReversas;
    private readonly PerfilComputadorRepository _perfis;

    /// <summary>Disparado quando um receptor se registra abrindo conexao para este painel.</summary>
    public event Action<ConexaoReversa>? ReceptorRegistrado;

    private TcpListener? _tcpListener;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private int _portaTcpAtiva;
    private int _portaUdpAtiva;

    public bool Ativo { get; private set; }

    /// <summary>Preenchido quando Start() não conseguiu ativar o receptor embutido —
    /// tipicamente porque receptor.py (ou outra instância) já está usando a porta nesta
    /// máquina. Nesse caso o painel segue funcionando normalmente para ENVIAR mensagens;
    /// ele só não vai também receber, e quem já está ouvindo aquela porta continua ouvindo.</summary>
    public string? UltimoErro { get; private set; }

    public EmbeddedReceptorServer(
        AppSettings settings, ObservableCollection<PainelPareado> paineisPareados,
        JsonStore<PainelPareado> paineisPareadosStore, HistoricoRepository historico,
        LogRepository logs,
        RegistroConexoesReversas conexoesReversas,
        PerfilComputadorRepository perfis)
    {
        _settings = settings;
        _paineisPareados = paineisPareados;
        _paineisPareadosStore = paineisPareadosStore;
        _historico = historico;
        _logs = logs;
        _conexoesReversas = conexoesReversas;
        _perfis = perfis;
    }

    public void AtualizarDisponibilidade()
    {
        if (!Ativo)
        {
            Start();
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

        SocketException? ultimoErroSocket = null;
        var candidatos = new[]
        {
            (_settings.PortaTcp, _settings.PortaDescobertaUdp),
            (ProtocolConstants.PanelFallbackTcpPort, ProtocolConstants.PanelFallbackUdpDiscoveryPort),
        }.Distinct().ToList();

        foreach (var (portaTcp, portaUdp) in candidatos)
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, portaTcp);
                tcpListener.Start();
                udpClient = new UdpClient();
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, portaUdp));
                _portaTcpAtiva = portaTcp;
                _portaUdpAtiva = portaUdp;
                break;
            }
            catch (SocketException ex)
            {
                ultimoErroSocket = ex;
                tcpListener?.Stop();
                udpClient?.Dispose();
                tcpListener = null;
                udpClient = null;
            }
        }

        if (tcpListener is null || udpClient is null)
        {
            cts.Dispose();
            UltimoErro =
                $"Receptor embutido não ativado: a porta TCP {_settings.PortaTcp} ou UDP {_settings.PortaDescobertaUdp} " +
                $"e as portas alternativas TCP {ProtocolConstants.PanelFallbackTcpPort}/UDP " +
                $"{ProtocolConstants.PanelFallbackUdpDiscoveryPort} estão indisponíveis.";
            Logger.Info(UltimoErro + $" (SocketErrorCode={ultimoErroSocket?.SocketErrorCode})");
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
        Logger.Info($"Receptor embutido ativo (TCP {_portaTcpAtiva}, UDP {_portaUdpAtiva}).");
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
        _portaTcpAtiva = 0;
        _portaUdpAtiva = 0;
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
        // Uma conexao "register" e mantida viva no registro de conexoes reversas,
        // entao nesse caso NAO liberamos o socket ao sair deste metodo.
        var manterViva = false;
        var stream = client.GetStream();
        try
        {
            manterViva = await ProcessarConexaoAsync(client, stream, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!manterViva)
            {
                stream.Dispose();
                client.Dispose();
            }
        }
    }

    /// <summary>Retorna true quando a conexao deve permanecer aberta (registro reverso).</summary>
    private async Task<bool> ProcessarConexaoAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
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
            return false;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }

        if (payload is null)
        {
            return false;
        }

        var sizeCheck = MessageValidator.ValidateSize(payload.Length, isUdp: false);
        if (!sizeCheck.IsValid)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(sizeCheck.Code!, sizeCheck.Message!), ct).ConfigureAwait(false);
            return false;
        }

        if (!MessageValidator.TryParse(payload, out var msg, out var parseResult) || msg is null)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(parseResult.Code!, parseResult.Message!), ct).ConfigureAwait(false);
            return false;
        }

        var structCheck = MessageValidator.Validate(msg);
        if (!structCheck.IsValid)
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(structCheck.Code!, structCheck.Message!, msg.Id), ct).ConfigureAwait(false);
            return false;
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
            case ProtocolConstants.MessageType.Register:
                // conexao reversa: NAO fecha o socket — ele fica vivo no registro
                // para o painel enviar notificacoes de volta por ele.
                return await TratarRegisterAsync(client, stream, msg, ct).ConfigureAwait(false);
            case ProtocolConstants.MessageType.SyncRequest:
                await TratarSyncAsync(stream, msg, ct).ConfigureAwait(false);
                break;
            case ProtocolConstants.MessageType.UpdateRequest:
                await EnviarAsync(
                    stream,
                    ComunicadorMessage.Error(
                        ProtocolConstants.ErrorCode.ContentBlocked,
                        "O receptor incorporado é atualizado junto com Comunicador.exe.", msg.Id),
                    ct).ConfigureAwait(false);
                break;
            default:
                await EnviarAsync(
                    stream,
                    ComunicadorMessage.Error(ProtocolConstants.ErrorCode.UnknownType, $"Tipo não esperado nesta conexão: '{msg.Type}'", msg.Id),
                    ct).ConfigureAwait(false);
                break;
        }

        return false;
    }

    /// <summary>Aceita a conexao que o RECEPTOR abriu em direcao a este painel e a guarda
    /// viva. E o caminho que dispensa qualquer porta de entrada aberta na maquina do
    /// receptor — quem disca e ele. Retorna true para o socket nao ser fechado.</summary>
    private async Task<bool> TratarRegisterAsync(
        TcpClient client, NetworkStream stream, ComunicadorMessage msg, CancellationToken ct)
    {
        var computerId = msg.ComputerId!;
        var computerName = msg.ComputerName!;

        // Se ja existe pareamento para este computador, exige o token correto.
        // Se e a primeira vez, o pareamento acontece aqui mesmo e um token e emitido.
        PainelPareado? pareado = null;
        UiDispatcher.Invoke(() => pareado = _paineisPareados.FirstOrDefault(p => p.PanelId == computerId));

        if (pareado is not null && !string.IsNullOrEmpty(msg.Token) && msg.Token != pareado.Token)
        {
            await EnviarAsync(
                stream,
                ComunicadorMessage.Error(ProtocolConstants.ErrorCode.Unauthorized, "Token invalido para este computador.", msg.Id),
                ct).ConfigureAwait(false);
            return false;
        }

        var token = pareado?.Token ?? (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        if (pareado is null)
        {
            var novo = new PainelPareado
            {
                PanelId = computerId,
                PanelName = computerName,
                Token = token,
                PareadoEm = DateTime.UtcNow,
            };
            UiDispatcher.Invoke(() =>
            {
                _paineisPareados.Add(novo);
                _paineisPareadosStore.Save(_paineisPareados);
            });
        }

        var resposta = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.RegisterAck);
        resposta.Accepted = true;
        resposta.Token = token;
        resposta.ComputerId = _settings.PainelId;
        resposta.ComputerName = _settings.NomePainel;
        resposta.HasPanel = true;
        resposta.Monitors = MonitorInfo.ListarLocais();
        resposta.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
        resposta.PanelVersion = ProtocolConstants.CurrentPanelVersion;
        resposta.IsOwner = _settings.EstePainelEhOwner;
        await EnviarAsync(stream, resposta, ct).ConfigureAwait(false);

        var ip = (client.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
        var conexao = new ConexaoReversa(
            client, stream, computerId, computerName, ip, token,
            msg.HasPanel ?? false, msg.Monitors, msg.ReceiverVersion,
            msg.PanelVersion, msg.IsOwner ?? false);
        _conexoesReversas.Registrar(conexao);
        ReceptorRegistrado?.Invoke(conexao);
        Logger.Info($"Receptor '{computerName}' registrou-se via conexao reversa de {ip}.");
        return true;
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
        pong.HasPanel = true;
        pong.Monitors = MonitorInfo.ListarLocais();
        pong.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
        pong.PanelVersion = ProtocolConstants.CurrentPanelVersion;
        pong.IsOwner = _settings.EstePainelEhOwner;
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
        response.HasPanel = true;
        response.Monitors = MonitorInfo.ListarLocais();
        response.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
        response.PanelVersion = ProtocolConstants.CurrentPanelVersion;
        response.IsOwner = _settings.EstePainelEhOwner;
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

        if ((msg.Image is not null || msg.ScreenImages is { Count: > 0 }
                || msg.Video is not null || msg.ScreenVideos is { Count: > 0 }
                || msg.Audio is not null)
            && (!_settings.AceitarImagensDeOutrosPaineis || !_settings.MidiasPermitidasGlobalmente))
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(
                ProtocolConstants.ErrorCode.ContentBlocked,
                "Este painel bloqueou o recebimento de imagens, vídeos e áudios.", msg.Id), ct).ConfigureAwait(false);
            return;
        }

        if (msg.Buttons?.Any(b => b.TemLink) == true
            && (!_settings.AceitarBotoesComLinks || !_settings.LinksPermitidosGlobalmente))
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(
                ProtocolConstants.ErrorCode.ContentBlocked,
                "Este painel bloqueou mensagens com botões de link.", msg.Id), ct).ConfigureAwait(false);
            return;
        }

        if (msg.DisplayMode == ProtocolConstants.DisplayMode.Carousel)
        {
            if (msg.Carousel?.Action != "stop" && !_settings.PapelParedeRemotoPermitidoGlobalmente)
            {
                await EnviarAsync(stream, ComunicadorMessage.Error(
                    ProtocolConstants.ErrorCode.ContentBlocked,
                    "O admin desativou a alteração remota das imagens do sistema.", msg.Id), ct).ConfigureAwait(false);
                return;
            }
            try
            {
                var status = CarouselService.Handle(msg.Carousel!, msg.Image);
                var carouselAck = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
                carouselAck.InReplyTo = msg.Id;
                carouselAck.Status = status;
                await EnviarAsync(stream, carouselAck, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException
                or FormatException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                await EnviarAsync(stream, ComunicadorMessage.Error(
                    ProtocolConstants.ErrorCode.InternalError,
                    $"Não foi possível configurar o carrossel: {ex.Message}", msg.Id), ct).ConfigureAwait(false);
            }
            return;
        }

        var allowReply = msg.AllowReply == true;
        var systemImage = msg.DisplayMode is ProtocolConstants.DisplayMode.Wallpaper
            or ProtocolConstants.DisplayMode.LockScreen;
        var entry = new HistoricoEntry
        {
            Direcao = DirecaoHistorico.Recebida,
            ComputadorId = msg.PanelId ?? msg.Sender!,
            ComputadorNome = msg.Sender!,
            Titulo = systemImage
                ? msg.DisplayMode == ProtocolConstants.DisplayMode.LockScreen ? "Tela de bloqueio" : "Papel de parede"
                : msg.Title!,
            Mensagem = systemImage ? msg.Image?.Name ?? "Imagem recebida" : msg.Message!,
            Status = systemImage ? StatusEnvio.Enviando : StatusEnvio.Exibido,
        };
        _historico.Adicionar(entry);

        if (systemImage && msg.Image is not null)
        {
            if (!_settings.PapelParedeRemotoPermitidoGlobalmente)
            {
                _historico.AtualizarExistente(entry.Id, item => item.Status = StatusEnvio.Erro);
                await EnviarAsync(stream, ComunicadorMessage.Error(
                    ProtocolConstants.ErrorCode.ContentBlocked,
                    "O admin desativou a alteração remota das imagens do sistema.", msg.Id), ct).ConfigureAwait(false);
                return;
            }

            var ackImage = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
            ackImage.InReplyTo = msg.Id;
            try
            {
                if (msg.DisplayMode == ProtocolConstants.DisplayMode.LockScreen)
                {
                    await LockScreenService.ApplyAsync(msg.Image, ct).ConfigureAwait(false);
                    ackImage.Status = "lock_screen_applied";
                }
                else
                {
                    WallpaperService.Apply(msg.Image);
                    ackImage.Status = "wallpaper_applied";
                }
                _historico.AtualizarExistente(entry.Id, item => item.Status = StatusEnvio.Entregue);
            }
            catch (Exception ex) when (ex is IOException or FormatException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                _historico.AtualizarExistente(entry.Id, item => { item.Status = StatusEnvio.Erro; item.ErroDetalhe = ex.Message; });
                await EnviarAsync(stream, ComunicadorMessage.Error(
                    ProtocolConstants.ErrorCode.InternalError,
                    $"Não foi possível alterar a imagem do sistema: {ex.Message}", msg.Id), ct).ConfigureAwait(false);
                return;
            }
            await EnviarAsync(stream, ackImage, ct).ConfigureAwait(false);
            return;
        }

        var mostrarTask = NotificacaoRecebidaWindow.MostrarAsync(
            msg.Sender!, msg.Title!, msg.Message!, allowReply, msg.Buttons,
            msg.DisplayMode, msg.Image, msg.ScreenImages, msg.ImageDurationSeconds,
            msg.AllowManualClose, msg.Appearance, msg.Video, msg.ScreenVideos,
            msg.VideoLoop, msg.Audio, msg.AudioLoop);

        var ack = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Ack);
        ack.InReplyTo = msg.Id;
        ack.Status = "shown";
        await EnviarAsync(stream, ack, ct).ConfigureAwait(false);

        if (!allowReply && msg.Buttons is not { Count: > 0 })
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

    private async Task TratarSyncAsync(NetworkStream stream, ComunicadorMessage msg, CancellationToken ct)
    {
        if (!TokenValido(msg.Token))
        {
            await EnviarAsync(stream, ComunicadorMessage.Error(
                ProtocolConstants.ErrorCode.Unauthorized,
                "Token inválido ou painel não pareado.", msg.Id), ct).ConfigureAwait(false);
            return;
        }

        if (msg.HistoryEntries is { Count: > 0 })
        {
            _historico.Mesclar(msg.HistoryEntries.Select(i => i.ToModel()));
        }
        if (msg.LogEntries is { Count: > 0 })
        {
            _logs.Mesclar(msg.LogEntries.Select(i => i.ToModel()));
        }
        if (msg.ComputerProfiles is { Count: > 0 })
        {
            _perfis.Mesclar(msg.ComputerProfiles);
        }

        var response = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.SyncResponse);
        response.InReplyTo = msg.Id;
        response.HistoryEntries = msg.IncludeHistory == true
            ? _historico.Snapshot(ProtocolConstants.MaxSyncEntries).Select(i => i.ToSync()).ToList()
            : [];
        response.LogEntries = msg.IncludeLogs == true
            ? _logs.Snapshot(ProtocolConstants.MaxSyncEntries).Select(i => i.ToSync()).ToList()
            : [];
        response.ComputerProfiles = _perfis.Snapshot(ProtocolConstants.MaxSyncProfiles).ToList();
        await EnviarAsync(stream, response, ct).ConfigureAwait(false);
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
            announce.HasPanel = true;
            announce.Monitors = MonitorInfo.ListarLocais();
            announce.ReceiverVersion = ProtocolConstants.CurrentReceiverVersion;
            announce.PanelVersion = ProtocolConstants.CurrentPanelVersion;
            announce.IsOwner = _settings.EstePainelEhOwner;
            announce.TcpPort = _portaTcpAtiva;
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
