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
    private readonly AppSettings _settings;
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
    public ICommand RenomearCommand { get; }
    public ICommand ConfirmarRenomeCommand { get; }

    public ComputadoresViewModel(
        JsonStore<Computador> store, DiscoveryService discovery, ReceptorClient client, AppSettings settings)
    {
        _store = store;
        _discovery = discovery;
        _client = client;
        _settings = settings;
        _novaPorta = settings.PortaTcp.ToString();

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

        AtualizarAgoraCommand = new AsyncRelayCommand(ProcurarAsync);

        AdicionarManualCommand = new RelayCommand(_ => AdicionarManual(), _ => PodeAdicionarManual());

        RenomearCommand = new RelayCommand(param =>
        {
            if (param is Computador computador)
            {
                // um de cada vez, para nao ficar varios cartoes em edicao
                foreach (var outro in Computadores)
                {
                    outro.EmEdicao = false;
                }

                computador.EmEdicao = true;
            }
        });

        ConfirmarRenomeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador)
            {
                computador.EmEdicao = false;
                Persist();
                StatusMensagem = $"Renomeado para \"{computador.NomeExibicao}\".";
            }
        });
    }

    public IReadOnlyList<Computador> Snapshot() => Computadores.ToList();

    /// <summary>Um receptor abriu conexao para este painel e se registrou. Ele ja chega
    /// pareado e online, sem precisar de descoberta nem de porta aberta no lado dele.</summary>
    public void RegistrarViaConexaoReversa(ConexaoReversa conexao)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var existente = Computadores.FirstOrDefault(c => c.Id == conexao.ComputerId)
                ?? Computadores.FirstOrDefault(c => !c.Pareado && c.EnderecoIp == conexao.EnderecoIp);

            if (existente is null)
            {
                Computadores.Add(new Computador
                {
                    Id = conexao.ComputerId,
                    Nome = conexao.ComputerName,
                    EnderecoIp = conexao.EnderecoIp,
                    PortaTcp = _settings.PortaTcp,
                    Pareado = true,
                    // sem guardar o token a notificacao sai sem ele e o receptor a rejeita
                    Token = conexao.Token,
                    Status = StatusComputador.Online,
                    UltimaVezVisto = DateTime.UtcNow,
                });
                StatusMensagem = $"{conexao.ComputerName} conectou-se e já está pronto para receber mensagens.";
            }
            else
            {
                existente.Id = conexao.ComputerId;
                existente.Nome = conexao.ComputerName;
                existente.EnderecoIp = conexao.EnderecoIp;
                existente.Pareado = true;
                existente.Token = conexao.Token;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
                StatusMensagem = $"{conexao.ComputerName} reconectou-se.";
            }

            Persist();
        });
    }

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

    /// <summary>Faz o broadcast UDP e, em seguida, varre a rede na porta do receptor.
    /// A varredura cobre o caso do broadcast nao passar (firewall, isolamento de AP),
    /// que e a causa mais comum de "nao encontra o outro computador".</summary>
    private async Task ProcurarAsync()
    {
        StatusMensagem = "Procurando na rede...";
        await _discovery.BroadcastOnceAsync().ConfigureAwait(true);

        var scanner = new LanScanner(_settings.PortaTcp);
        var encontrados = await scanner.VarrerAsync().ConfigureAwait(true);

        var novos = 0;
        foreach (var ip in encontrados)
        {
            if (Computadores.Any(c => c.EnderecoIp == ip && c.PortaTcp == _settings.PortaTcp))
            {
                continue;
            }

            Computadores.Add(new Computador
            {
                Id = Guid.NewGuid().ToString(),
                Nome = ip,
                EnderecoIp = ip,
                PortaTcp = _settings.PortaTcp,
                Pareado = false,
                Status = StatusComputador.Online,
                UltimaVezVisto = DateTime.UtcNow,
            });
            novos++;
        }

        if (novos > 0)
        {
            Persist();
            StatusMensagem = $"{novos} computador(es) encontrado(s) na varredura. Clique em \"Parear\".";
        }
        else if (encontrados.Count > 0)
        {
            StatusMensagem = "Nenhum computador novo — os encontrados já estão na lista.";
        }
        else
        {
            StatusMensagem =
                $"Nenhum receptor encontrado na rede (porta {_settings.PortaTcp}). " +
                "Confirme que o receptor está rodando no outro computador.";
        }
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
