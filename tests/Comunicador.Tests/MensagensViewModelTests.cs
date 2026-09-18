using System.IO;
using System.Reflection;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Comunicador.ViewModels;
using Xunit;

namespace Comunicador.Tests;

public sealed class MensagensViewModelTests
{
    [Fact]
    public void Monitores_e_imagem_seguem_somente_o_computador_selecionado()
    {
        var pasta = Path.Combine(Path.GetTempPath(), $"comunicador-mensagens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        using var contexto = CriarContexto(pasta);

        var painelLocal = ComputadorPareado("local", "Painel local", 2);
        var receptor = ComputadorPareado("remoto", "PC remoto", 3);
        contexto.Computadores.Computadores.Add(painelLocal);
        contexto.Computadores.Computadores.Add(receptor);

        Assert.Empty(contexto.Mensagens.MonitoresDestino);

        contexto.Mensagens.Destinatarios.Single(d => d.Computador.Id == receptor.Id).Selecionado = true;

        Assert.Equal(3, contexto.Mensagens.MonitoresDestino.Count);
        Assert.All(contexto.Mensagens.MonitoresDestino, destino => Assert.Same(receptor, destino.Computador));

        DefinirImagemCarregada(contexto.Mensagens);
        var monitorEscolhido = contexto.Mensagens.MonitoresDestino[1];
        Assert.True(contexto.Mensagens.UsarImagemCarregadaNoMonitorCommand.CanExecute(monitorEscolhido));
        contexto.Mensagens.UsarImagemCarregadaNoMonitorCommand.Execute(monitorEscolhido);

        Assert.Single(monitorEscolhido.Midias);
        Assert.Equal("teste.png", monitorEscolhido.Midias[0].Nome);

        contexto.Mensagens.Destinatarios.Single(d => d.Computador.Id == receptor.Id).Selecionado = false;
        Assert.Empty(contexto.Mensagens.MonitoresDestino);

        contexto.Mensagens.Destinatarios.Single(d => d.Computador.Id == receptor.Id).Selecionado = true;
        Assert.Single(contexto.Mensagens.MonitoresDestino.Single(m => m.Monitor.Index == 1).Midias);
    }

    [Fact]
    public void Atualiza_os_monitores_quando_o_receptor_envia_nova_topologia()
    {
        var pasta = Path.Combine(Path.GetTempPath(), $"comunicador-monitores-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        using var contexto = CriarContexto(pasta);
        var receptor = ComputadorPareado("remoto", "PC remoto", 1);
        contexto.Computadores.Computadores.Add(receptor);
        contexto.Mensagens.Destinatarios.Single().Selecionado = true;

        receptor.Monitores = CriarMonitores(2);

        Assert.Equal(2, contexto.Mensagens.MonitoresDestino.Count);
        Assert.All(contexto.Mensagens.MonitoresDestino, destino => Assert.Same(receptor, destino.Computador));
        Assert.True(contexto.Mensagens.Destinatarios.Single().Selecionado);
    }

    private static Contexto CriarContexto(string pasta)
    {
        var settings = new AppSettings { PainelId = "painel-teste", NomePainel = "Painel teste" };
        var discovery = new DiscoveryService(settings);
        var client = new ReceptorClient(settings.PainelId, settings.NomePainel);
        var conexoes = new RegistroConexoesReversas();
        var perfis = new PerfilComputadorRepository(
            new JsonStore<PerfilComputador>(Path.Combine(pasta, "perfis.json")));
        var cloud = new CloudSyncService(new SupabaseClient(), settings, perfis);
        var computadores = new ComputadoresViewModel(
            new JsonStore<Computador>(Path.Combine(pasta, "computadores.json")),
            discovery,
            client,
            new AtualizadorReceptor(client, conexoes),
            settings,
            perfis,
            cloud);
        var mensagens = new MensagensViewModel(
            computadores,
            new EnviadorNotificacoes(client, conexoes, settings),
            new HistoricoRepository(new JsonStore<HistoricoEntry>(Path.Combine(pasta, "historico.json"))),
            cloud);
        return new Contexto(pasta, discovery, computadores, mensagens, cloud);
    }

    private static Computador ComputadorPareado(string id, string nome, int monitores) => new()
    {
        Id = id,
        Nome = nome,
        EnderecoIp = "192.168.0.2",
        PortaTcp = ProtocolConstants.TcpPort,
        Pareado = true,
        Monitores = CriarMonitores(monitores),
    };

    private static List<MonitorInfo> CriarMonitores(int quantidade) => Enumerable.Range(0, quantidade)
        .Select(i => new MonitorInfo
        {
            Index = i,
            Name = $"Monitor {i + 1}",
            Width = 1920,
            Height = 1080,
            Primary = i == 0,
        })
        .ToList();

    private static void DefinirImagemCarregada(MensagensViewModel mensagens)
    {
        DefinirCampo(mensagens, "_dadosImagem", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        DefinirCampo(mensagens, "_mimeImagem", "image/png");
        DefinirCampo(mensagens, "_nomeImagem", "teste.png");
        DefinirCampo(mensagens, "_caminhoImagem", "C:\\teste.png");
    }

    private static void DefinirCampo(MensagensViewModel mensagens, string nome, object valor) =>
        typeof(MensagensViewModel)
            .GetField(nome, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mensagens, valor);

    private sealed record Contexto(
        string Pasta,
        DiscoveryService Discovery,
        ComputadoresViewModel Computadores,
        MensagensViewModel Mensagens,
        CloudSyncService Cloud) : IDisposable
    {
        public void Dispose()
        {
            Discovery.Dispose();
            Cloud.Dispose();
            try
            {
                Directory.Delete(Pasta, recursive: true);
            }
            catch (IOException)
            {
                // Arquivos temporários não afetam o resultado funcional do teste.
            }
        }
    }
}
