using System.Collections.ObjectModel;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Storage;

namespace Comunicador.ViewModels;

public sealed class ComputadoresViewModel : ViewModelBase
{
    private readonly JsonStore<Computador> _store;
    private readonly DiscoveryService _discovery;
    private readonly ReceptorClient _client;
    private string? _statusMensagem;

    public ObservableCollection<Computador> Computadores { get; } = new();

    public string? StatusMensagem
    {
        get => _statusMensagem;
        set => SetField(ref _statusMensagem, value);
    }

    public ICommand PairearCommand { get; }
    public ICommand RemoverCommand { get; }
    public ICommand AtualizarAgoraCommand { get; }

    public ComputadoresViewModel(JsonStore<Computador> store, DiscoveryService discovery, ReceptorClient client)
    {
        _store = store;
        _discovery = discovery;
        _client = client;

        foreach (var computador in _store.Load())
        {
            Computadores.Add(computador);
        }

        _discovery.ReceptorDescoberto += OnReceptorDescoberto;

        PairearCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador)
            {
                await PairearAsync(computador).ConfigureAwait(true);
            }
        });

        RemoverCommand = new RelayCommand(param =>
        {
            if (param is Computador computador)
            {
                Computadores.Remove(computador);
                Persist();
            }
        });

        AtualizarAgoraCommand = new AsyncRelayCommand(() => _discovery.BroadcastOnceAsync());
    }

    public IReadOnlyList<Computador> Snapshot() => Computadores.ToList();

    public void AtualizarStatus(string computadorId, StatusComputador status)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var computador = Computadores.FirstOrDefault(c => c.Id == computadorId);
            if (computador is not null)
            {
                computador.Status = status;
                computador.UltimaVezVisto = DateTime.UtcNow;
            }
        });
    }

    private void OnReceptorDescoberto(AnnounceInfo info)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var existente = Computadores.FirstOrDefault(c => c.Id == info.ComputerId);
            if (existente is null)
            {
                Computadores.Add(new Computador
                {
                    Id = info.ComputerId,
                    Nome = info.ComputerName,
                    EnderecoIp = info.IpAddress,
                    PortaTcp = info.TcpPort,
                    Pareado = info.Paired,
                    Status = StatusComputador.Online,
                    UltimaVezVisto = DateTime.UtcNow,
                });
                StatusMensagem = $"Novo computador encontrado: {info.ComputerName}";
            }
            else
            {
                existente.EnderecoIp = info.IpAddress;
                existente.PortaTcp = info.TcpPort;
                existente.Nome = info.ComputerName;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
            }

            Persist();
        });
    }

    private async Task PairearAsync(Computador computador)
    {
        try
        {
            StatusMensagem = $"Pareando com {computador.Nome}...";
            var resultado = await _client.PairAsync(computador.EnderecoIp, computador.PortaTcp).ConfigureAwait(true);
            computador.Token = resultado.Token;
            computador.Pareado = true;
            computador.Nome = resultado.ComputerName;
            computador.Status = StatusComputador.Online;
            Persist();
            StatusMensagem = $"Pareado com {computador.Nome}.";
        }
        catch (ReceptorComunicacaoException ex)
        {
            StatusMensagem = $"Falha ao parear com {computador.Nome}: {ex.Message}";
        }
    }

    private void Persist() => _store.Save(Computadores);
}
