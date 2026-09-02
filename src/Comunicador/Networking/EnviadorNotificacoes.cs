using Comunicador.Models;
using Comunicador.Protocol;

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
        IReadOnlyList<BotaoResposta>? botoes = null, CancellationToken ct = default)
    {
        var listaBotoes = botoes is { Count: > 0 } ? botoes.ToList() : null;

        var conexao = _conexoesReversas.Obter(computador.Id);
        if (conexao is not null)
        {
            var notificacao = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            // o token da propria conexao e a fonte confiavel: o do Computador pode
            // estar vazio se ele entrou na lista por outro caminho.
            notificacao.Token = string.IsNullOrEmpty(conexao.Token) ? computador.Token ?? string.Empty : conexao.Token;
            notificacao.Sender = _settings.NomePainel;
            notificacao.Title = titulo;
            notificacao.Message = mensagem;
            notificacao.AllowReply = permitirResposta;
            notificacao.Buttons = listaBotoes;

            var resultado = await conexao
                .EnviarNotificacaoAsync(notificacao, permitirResposta, TimeoutResposta, ct)
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
            titulo, mensagem, permitirResposta, listaBotoes, ct).ConfigureAwait(false);
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
}
