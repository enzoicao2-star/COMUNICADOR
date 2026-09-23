using System.IO;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Comunicador.ViewModels;
using Xunit;

namespace Comunicador.Tests;

public sealed class LembretesInputTests
{
    [Fact]
    public void CamposDeTexto_AceitamDataEHoraValidasERejeitamValoresInvalidos()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"comunicador-lembretes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var settings = new AppSettings { PainelId = "painel-teste", NomePainel = "Painel teste" };
        var profiles = new PerfilComputadorRepository(
            new JsonStore<PerfilComputador>(Path.Combine(folder, "profiles.json")));
        using var discovery = new DiscoveryService(settings);
        using var cloud = new CloudSyncService(new SupabaseClient(), settings, profiles);
        var client = new ReceptorClient(settings.PainelId, settings.NomePainel);
        var connections = new RegistroConexoesReversas();
        var computers = new ComputadoresViewModel(
            new JsonStore<Computador>(Path.Combine(folder, "computers.json")),
            discovery, client, new AtualizadorReceptor(client, connections), settings, profiles, cloud);
        var history = new HistoricoRepository(
            new JsonStore<HistoricoEntry>(Path.Combine(folder, "history.json")));
        using var scheduler = new LembreteSchedulerService(() => [], _ => null,
            new EnviadorNotificacoes(client, connections, settings));
        var viewModel = new LembretesViewModel(
            new JsonStore<Lembrete>(Path.Combine(folder, "reminders.json")),
            computers, history, scheduler, cloud);

        try
        {
            computers.Computadores.Add(new Computador { Id = "pc-teste", Nome = "PC teste", Pareado = true });
            viewModel.Destinatarios.Single().Selecionado = true;
            viewModel.Titulo = "Lembrete de teste";
            viewModel.Mensagem = "Texto";
            viewModel.DataTexto = "31/12/2030";
            viewModel.HoraTexto = "14:30";

            Assert.Equal(new DateTime(2030, 12, 31, 14, 30, 0), viewModel.DataHora);
            Assert.True(viewModel.CriarCommand.CanExecute(null));

            viewModel.DataTexto = "31/02/2030";
            Assert.False(viewModel.CriarCommand.CanExecute(null));

            viewModel.DataTexto = "31/12/2030";
            viewModel.HoraTexto = "25:00";
            Assert.False(viewModel.CriarCommand.CanExecute(null));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
