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
        Computador computador, CancellationToken ct = default)
    {
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

        var arquivos = ReceiverUpdatePackage.Create();
        var conexao = _conexoes.Obter(computador.Id);
        if (conexao is not null)
        {
            var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.UpdateRequest);
            request.Token = conexao.Token;
            request.TargetVersion = ProtocolConstants.CurrentReceiverVersion;
            request.UpdateFiles = arquivos;
            return await conexao.SolicitarAtualizacaoAsync(request, ct).ConfigureAwait(false);
        }

        return await _client.UpdateReceiverAsync(
            computador.EnderecoIp, computador.PortaTcp, computador.Token ?? string.Empty,
            arquivos, ct).ConfigureAwait(false);
    }
}
