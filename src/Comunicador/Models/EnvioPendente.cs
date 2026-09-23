using Comunicador.Networking;
using Comunicador.Protocol;

namespace Comunicador.Models;

/// <summary>Conteúdo original de um envio com falha. Não inclui o token de pareamento;
/// o reenvio usa as credenciais atuais do computador selecionado.</summary>
public sealed class EnvioPendente
{
    public string Titulo { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public bool PermitirResposta { get; set; }
    public List<BotaoResposta> Botoes { get; set; } = [];
    public string ModoExibicao { get; set; } = ProtocolConstants.DisplayMode.Toast;
    public ConteudoImagem? Imagem { get; set; }
    public List<ImagemMonitor> ImagensPorMonitor { get; set; } = [];
    public ConteudoVideo? Video { get; set; }
    public List<VideoMonitor> VideosPorMonitor { get; set; } = [];
    public ConteudoAudio? Audio { get; set; }
    public int? DuracaoSegundos { get; set; }
    public bool? PermitirFecharManualmente { get; set; }
    public bool? RepetirVideo { get; set; }
    public bool? RepetirAudio { get; set; }
    public AparenciaNotificacao Aparencia { get; set; } = new();

    public Task<NotificationResult> EnviarAsync(
        EnviadorNotificacoes enviador, Computador computador, CancellationToken ct = default) =>
        enviador.EnviarAsync(computador, Titulo, Mensagem, PermitirResposta, Botoes,
            ModoExibicao, Imagem, ImagensPorMonitor, DuracaoSegundos,
            PermitirFecharManualmente, Aparencia, Video, VideosPorMonitor,
            RepetirVideo, Audio, RepetirAudio, ct);

    public static void AtualizarHistorico(HistoricoEntry item, NotificationResult resultado, bool permitirResposta)
    {
        if (!resultado.Delivered)
        {
            item.Status = StatusEnvio.Erro;
            item.ErroDetalhe = resultado.ErrorMessage;
        }
        else if (resultado.GotReply)
        {
            item.Status = StatusEnvio.Respondido;
            item.RespostaTexto = resultado.ReplyText;
            item.ErroDetalhe = null;
        }
        else if (permitirResposta)
        {
            item.Status = StatusEnvio.SemResposta;
            item.ErroDetalhe = null;
        }
        else
        {
            item.Status = resultado.WasShown ? StatusEnvio.Exibido : StatusEnvio.Entregue;
            item.ErroDetalhe = null;
        }
    }
}
