using Comunicador.Models;
using Comunicador.Protocol;
using System.Diagnostics;

namespace Comunicador.Networking;

/// <summary>Ponto unico de envio de notificacoes. Prefere a conexao reversa (aberta pelo
/// proprio receptor) quando existe, porque ela funciona sem nenhuma porta de entrada
/// liberada na maquina do receptor; se nao houver, disca para o receptor como antes.</summary>
public sealed class EnviadorNotificacoes
{
    private static readonly TimeSpan TimeoutResposta = TimeSpan.FromMinutes(5);

    private readonly ReceptorClient _client;
    private readonly RegistroConexoesReversas _conexoesReversas;
    private readonly AppSettings _settings;

    public EnviadorNotificacoes(
        ReceptorClient client, RegistroConexoesReversas conexoesReversas, AppSettings settings)
    {
        _client = client;
        _conexoesReversas = conexoesReversas;
        _settings = settings;
    }

    public async Task<NotificationResult> EnviarAsync(
        Computador computador, string titulo, string mensagem, bool permitirResposta,
        IReadOnlyList<BotaoResposta>? botoes = null,
        string modoExibicao = ProtocolConstants.DisplayMode.Toast,
        ConteudoImagem? imagem = null,
        IReadOnlyList<ImagemMonitor>? imagensPorMonitor = null,
        int? duracaoImagemSegundos = null,
        bool? permitirFecharManualmente = null,
        AparenciaNotificacao? aparencia = null,
        ConteudoVideo? video = null,
        IReadOnlyList<VideoMonitor>? videosPorMonitor = null,
        bool? repetirVideo = null,
        ConteudoAudio? audio = null,
        bool? repetirAudio = null,
        CancellationToken ct = default,
        CarouselCommand? carousel = null)
    {
        var listaBotoes = botoes is { Count: > 0 } ? botoes.ToList() : null;

        var conexao = _conexoesReversas.Obter(computador.Id);
        if (conexao is not null)
        {
            var notificacao = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            // o token da propria conexao e a fonte confiavel: o do Computador pode
            // estar vazio se ele entrou na lista por outro caminho.
            notificacao.Token = string.IsNullOrEmpty(conexao.Token) ? computador.Token ?? string.Empty : conexao.Token;
            notificacao.PanelId = _settings.PainelId;
            notificacao.Sender = _settings.NomePainel;
            notificacao.Title = titulo;
            notificacao.Message = mensagem;
            notificacao.AllowReply = permitirResposta;
            notificacao.Buttons = listaBotoes;
            notificacao.DisplayMode = modoExibicao;
            notificacao.Image = imagem;
            notificacao.ScreenImages = imagensPorMonitor?.ToList();
            notificacao.Video = video;
            notificacao.ScreenVideos = videosPorMonitor?.ToList();
            notificacao.VideoLoop = repetirVideo;
            notificacao.Audio = audio;
            notificacao.AudioLoop = repetirAudio;
            notificacao.ImageDurationSeconds = duracaoImagemSegundos;
            notificacao.AllowManualClose = permitirFecharManualmente;
            notificacao.Appearance = aparencia;
            notificacao.Carousel = carousel;

            var validacao = MessageValidator.Validate(notificacao);
            if (!validacao.IsValid)
            {
                return new NotificationResult(
                    false, false, false, null, $"{validacao.Code}: {validacao.Message}");
            }
            var framed = MessageValidator.Frame(notificacao);
            var tamanho = MessageValidator.ValidateSize(framed.Length, isUdp: false);
            if (!tamanho.IsValid)
            {
                return new NotificationResult(false, false, false, null, tamanho.Message);
            }

            var resultado = await conexao
                .EnviarNotificacaoAsync(notificacao,
                    permitirResposta || listaBotoes is { Count: > 0 }, TimeoutResposta, ct, framed)
                .ConfigureAwait(false);

            if (resultado.Delivered)
            {
                return resultado;
            }

            // conexao reversa caiu: remove do registro e tenta o caminho direto.
            _conexoesReversas.Remover(computador.Id);
        }

        return await _client.SendNotificationAsync(
            computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty,
            titulo, mensagem, permitirResposta, listaBotoes, modoExibicao, imagem,
            imagensPorMonitor?.ToList(), duracaoImagemSegundos,
            permitirFecharManualmente, aparencia, video, videosPorMonitor?.ToList(),
            repetirVideo, audio, repetirAudio, ct, carousel).ConfigureAwait(false);
    }

    /// <summary>Online se existe conexao reversa viva ou se o ping direto responde.</summary>
    public async Task<bool> EstaOnlineAsync(Computador computador, CancellationToken ct = default)
    {
        if (_conexoesReversas.Obter(computador.Id) is not null)
        {
            return true;
        }

        return await _client
            .PingAsync(computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty, ct)
            .ConfigureAwait(false);
    }

    public async Task<(bool Online, double? PingMs)> MedirStatusAsync(
        Computador computador, CancellationToken ct = default)
    {
        var conexao = _conexoesReversas.Obter(computador.Id);
        if (conexao is not null)
        {
            // Receptores anteriores a 2.2 não conhecem ping na conexão reversa.
            // Mantemos a conexão deles ativa até o instalador fazer o upgrade.
            if (!System.Version.TryParse(conexao.ReceiverVersion, out var versao)
                || versao < new System.Version(2, 2, 0))
            {
                return (true, computador.PingMs);
            }
            var latencia = await conexao.MedirPingAsync(ct).ConfigureAwait(false);
            if (latencia is null) return (true, computador.PingMs);
            if (latencia >= 0) return (true, latencia);
            _conexoesReversas.Remover(computador.Id);
        }

        var cronometro = Stopwatch.StartNew();
        var online = await _client
            .PingAsync(computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty, ct)
            .ConfigureAwait(false);
        cronometro.Stop();
        return online ? (true, cronometro.Elapsed.TotalMilliseconds) : (false, null);
    }
}
