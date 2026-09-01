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
        CancellationToken ct = default)
    {
        var conexao = _conexoesReversas.Obter(computador.Id);
        if (conexao is not null)
        {
            var notificacao = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Notification);
            notificacao.Token = computador.Token ?? string.Empty;
            notificacao.Sender = _settings.NomePainel;
            notificacao.Title = titulo;
            notificacao.Message = mensagem;
            notificacao.AllowReply = permitirResposta;

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
            titulo, mensagem, permitirResposta, ct).ConfigureAwait(false);
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
