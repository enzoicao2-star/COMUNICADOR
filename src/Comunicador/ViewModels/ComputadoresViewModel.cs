using System.Collections.ObjectModel;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.ViewModels;

public sealed class ComputadoresViewModel : ViewModelBase
{
    private readonly JsonStore<Computador> _store;
    private readonly DiscoveryService _discovery;
    private readonly ReceptorClient _client;
    private string? _statusMensagem;
    private string _novoIp = string.Empty;
    private string _novaPorta = ProtocolConstants.TcpPort.ToString();

    public ObservableCollection<Computador> Computadores { get; } = new();

    public string? StatusMensagem
    {
        get => _statusMensagem;
        set => SetField(ref _statusMensagem, value);
    }

    public string NovoIp
    {
        get => _novoIp;
        set => SetField(ref _novoIp, value);
    }

    public string NovaPorta
    {
        get => _novaPorta;
        set => SetField(ref _novaPorta, value);
    }

    public ICommand PairearCommand { get; }
    public ICommand RemoverCommand { get; }
    public ICommand AtualizarAgoraCommand { get; }
    public ICommand AdicionarManualCommand { get; }

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

        AdicionarManualCommand = new RelayCommand(_ => AdicionarManual(), _ => PodeAdicionarManual());
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
            // primeiro tenta casar pelo id real; se não achar, casa uma entrada adicionada
            // manualmente (mesmo IP:porta, ainda sem id real) pra não duplicar a linha.
            var existente = Computadores.FirstOrDefault(c => c.Id == info.ComputerId)
                ?? Computadores.FirstOrDefault(c => !c.Pareado && c.EnderecoIp == info.IpAddress && c.PortaTcp == info.TcpPort);

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
                existente.Id = info.ComputerId;
                existente.EnderecoIp = info.IpAddress;
                existente.PortaTcp = info.TcpPort;
                existente.Nome = info.ComputerName;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
            }

            Persist();
        });
    }

    private bool PodeAdicionarManual() =>
        !string.IsNullOrWhiteSpace(NovoIp) && int.TryParse(NovaPorta, out var porta) && porta is > 0 and <= 65535;

    private void AdicionarManual()
    {
        var ip = NovoIp.Trim();
        var porta = int.Parse(NovaPorta);

        var duplicado = Computadores.Any(c => c.EnderecoIp == ip && c.PortaTcp == porta);
        if (duplicado)
        {
            StatusMensagem = $"{ip}:{porta} já está na lista.";
            return;
        }

        Computadores.Add(new Computador
        {
            Id = Guid.NewGuid().ToString(),
            Nome = ip,
            EnderecoIp = ip,
            PortaTcp = porta,
            Pareado = false,
            Status = StatusComputador.Desconhecido,
            UltimaVezVisto = DateTime.UtcNow,
        });
        Persist();

        StatusMensagem = $"{ip}:{porta} adicionado. Clique em \"Parear\" para conectar.";
        NovoIp = string.Empty;
        NovaPorta = ProtocolConstants.TcpPort.ToString();
    }

    private async Task PairearAsync(Computador computador)
    {
        try
        {
            StatusMensagem = $"Pareando com {computador.Nome}...";
            var resultado = await _client.PairAsync(computador.EnderecoIp, computador.PortaTcp).ConfigureAwait(true);
            computador.Id = resultado.ComputerId;
            computador.Token = resultado.Token;
            computador.Pareado = true;
            computador.Nome = resultado.ComputerName;
            computador.Status = StatusComputador.Online;
            Persist();
            StatusMensagem = $"Pareado com {computador.Nome}.";
        }
        catch (ReceptorComunicacaoException ex)
        {
            StatusMensagem = $"Falha ao parear com {computador.Nome}: {ex.Message} " +
                "Verifique se o Firewall do Windows permite conexões na porta TCP " +
                $"{computador.PortaTcp} nas duas máquinas.";
        }
    }

    private void Persist() => _store.Save(Computadores);
}
