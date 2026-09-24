using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Services;

namespace Comunicador.Networking;

/// <summary>Atualiza somente o receptor Python oficial incorporado no painel.
/// A operação é iniciada pelo botão do usuário e usa o pareamento já existente.</summary>
public sealed class AtualizadorReceptor
{
    private readonly ReceptorClient _client;
    private readonly RegistroConexoesReversas _conexoes;

    public AtualizadorReceptor(ReceptorClient client, RegistroConexoesReversas conexoes)
    {
        _client = client;
        _conexoes = conexoes;
    }

    public async Task<ReceiverUpdateResult> AtualizarAsync(
        Computador computador, Action<ReceiverUpdateProgress>? report = null,
        CancellationToken ct = default)
    {
        report?.Invoke(new(3, "Verificando o receptor"));
        if (computador.TemPainel)
        {
            return new(false, "not_applicable", computador.VersaoReceptor ?? string.Empty,
                "Computadores com o painel usam o receptor incorporado ao Comunicador.exe.");
        }

        if (!ProtocolConstants.SupportsRemoteManagement(computador.VersaoReceptor))
        {
            return new(false, "legacy_requires_installer", computador.VersaoReceptor ?? string.Empty,
                "Esta versão antiga ainda não possui o atualizador remoto. Execute o "
                + "INSTALAR_RECEPTOR.bat atualizado uma última vez nessa máquina; "
                + "as próximas versões poderão ser instaladas diretamente pelo painel.");
        }

        report?.Invoke(new(8, "Preparando os arquivos oficiais"));
        var arquivos = ReceiverUpdatePackage.Create();
        var conexao = _conexoes.Obter(computador.Id);
        report?.Invoke(new(12, "Conectando ao computador"));
        void TransferProgress(long sent, long total)
        {
            var percent = 12 + (int)(66 * sent / total);
            report?.Invoke(new(percent, $"Enviando arquivos: {sent / 1024:N0} de {total / 1024:N0} KB"));
        }

        ReceiverUpdateResult result;
        if (conexao is not null)
        {
            var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.UpdateRequest);
            request.Token = conexao.Token;
            request.TargetVersion = ProtocolConstants.CurrentReceiverVersion;
            request.UpdateFiles = arquivos;
            result = await conexao.SolicitarAtualizacaoAsync(request, ct,
                TransferProgress).ConfigureAwait(false);
        }
        else
        {
            result = await _client.UpdateReceiverAsync(
                computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty,
                arquivos, ct, TransferProgress).ConfigureAwait(false);
        }

        if (!result.Success) return result;

        report?.Invoke(new(85, "Arquivos instalados; aguardando reinício"));
        var confirmed = await ConfirmarReconexaoAsync(computador, conexao, report, ct)
            .ConfigureAwait(false);
        if (!confirmed)
            return new(false, "reconnect_timeout", result.ReceiverVersion,
                "Os arquivos foram instalados, mas o receptor não voltou à rede na versão esperada em 30 segundos.");

        report?.Invoke(new(100, "Receptor atualizado e reconectado"));
        return result;
    }

    private async Task<bool> ConfirmarReconexaoAsync(Computador computador,
        ConexaoReversa? antiga, Action<ReceiverUpdateProgress>? report, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            // O receptor aguarda um segundo antes de reiniciar após enviar o status.
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token).ConfigureAwait(false);
            while (!timeout.IsCancellationRequested)
            {
                var nova = _conexoes.Obter(computador.Id);
                if (nova is not null && !ReferenceEquals(nova, antiga) &&
                    nova.ReceiverVersion == ProtocolConstants.CurrentReceiverVersion)
                    return true;

                // PCs sem conexão reversa ainda podem confirmar pela porta TCP.
                if (antiga is null)
                {
                    var versao = await _client.GetReceiverVersionAsync(
                        computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty,
                        timeout.Token).ConfigureAwait(false);
                    if (versao == ProtocolConstants.CurrentReceiverVersion) return true;
                }

                report?.Invoke(new(92, "Aguardando o receptor voltar à rede"));
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        return false;
    }
}
