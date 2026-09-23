using System.IO;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Xunit;

namespace Comunicador.Tests;

public sealed class ReenvioTests
{
    [Fact]
    public void Falha_guarda_conteudo_e_reenvio_atualiza_o_historico()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "comunicador-reenvio-" + Guid.NewGuid().ToString("N"));
        var repositorio = new ReenvioRepository(pasta);
        var id = Guid.NewGuid().ToString();
        try
        {
            var original = new EnvioPendente
            {
                Titulo = "Aviso",
                Mensagem = "Confirme o recebimento",
                PermitirResposta = true,
                Botoes = [new BotaoResposta { Label = "Recebi" }],
                ModoExibicao = ProtocolConstants.DisplayMode.CenterMessage,
                DuracaoSegundos = 25,
            };
            repositorio.Salvar(id, original);
            var recuperado = Assert.IsType<EnvioPendente>(repositorio.Ler(id));
            Assert.Equal(original.Titulo, recuperado.Titulo);
            Assert.Equal(original.Mensagem, recuperado.Mensagem);
            Assert.Equal("Recebi", Assert.Single(recuperado.Botoes).Label);
            Assert.Equal(25, recuperado.DuracaoSegundos);

            var historico = new HistoricoEntry { Id = id, Status = StatusEnvio.Erro };
            EnvioPendente.AtualizarHistorico(historico,
                new NotificationResult(true, true, true, "Recebi", null), true);
            Assert.Equal(StatusEnvio.Respondido, historico.Status);
            Assert.Equal("Recebi", historico.RespostaTexto);

            repositorio.Remover(id);
            Assert.False(repositorio.Existe(id));
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Reenvio_rejeita_identificador_que_saia_da_pasta()
    {
        var repositorio = new ReenvioRepository(Path.Combine(Path.GetTempPath(), "comunicador-reenvio-test"));
        Assert.False(repositorio.Existe("../outra-pasta"));
        Assert.Throws<ArgumentException>(() => repositorio.Salvar("../outra-pasta", new EnvioPendente()));
    }
}
