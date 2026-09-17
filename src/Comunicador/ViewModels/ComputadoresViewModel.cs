using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Comunicador.Storage;
using Microsoft.Win32;

namespace Comunicador.ViewModels;

public sealed class ComputadoresViewModel : ViewModelBase
{
    public event Action<double?>? PingMedioAtualizado;
    private readonly JsonStore<Computador> _store;
    private readonly DiscoveryService _discovery;
    private readonly ReceptorClient _client;
    private readonly AtualizadorReceptor _atualizador;
    private readonly AppSettings _settings;
    private readonly PerfilComputadorRepository _perfis;
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
    public ICommand AtualizarReceptorCommand { get; }
    public ICommand AdicionarBadgeCommand { get; }
    public ICommand RemoverBadgeCommand { get; }
    public ICommand CarregarIconeBadgeCommand { get; }
    public ICommand SalvarPerfilCommand { get; }

    public IReadOnlyList<string> EstilosBadge { get; } = ["Holográfica", "Metal", "Pílula", "Contorno", "Selo"];
    public IReadOnlyList<string> IconesBadge { get; } =
        ["Coroa", "Estrela", "Escudo", "Raio", "Diamante", "Fogo", "Coração", "Usuário", "Código", "Música", "Jogo", "Casa", "Medalha", "Chave", "Globo"];

    public ComputadoresViewModel(
        JsonStore<Computador> store, DiscoveryService discovery, ReceptorClient client,
        AtualizadorReceptor atualizador, AppSettings settings,
        PerfilComputadorRepository perfis)
    {
        _store = store;
        _discovery = discovery;
        _client = client;
        _atualizador = atualizador;
        _settings = settings;
        _perfis = perfis;
        _novaPorta = settings.PortaTcp.ToString();

        foreach (var computador in _store.Load())
        {
            if (computador.Monitores.Count == 0)
            {
                computador.Monitores = MonitoresOuPadrao(null);
            }
            AplicarPerfil(computador);
            Computadores.Add(computador);
        }

        _perfis.Alterado += AplicarPerfis;

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
                PublicarPingMedio();
            }
        });

        AtualizarAgoraCommand = new AsyncRelayCommand(ProcurarAsync);

        AtualizarReceptorCommand = new AsyncRelayCommand(async param =>
        {
            if (param is Computador computador)
            {
                await AtualizarReceptorAsync(computador).ConfigureAwait(true);
            }
        }, param => param is Computador { PodeAtualizarReceptor: true });

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
                SalvarPerfil(computador);
                StatusMensagem = $"Renomeado para \"{computador.NomeExibicao}\".";
            }
        });

        AdicionarBadgeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador) AdicionarBadge(computador);
        });
        RemoverBadgeCommand = new RelayCommand(param =>
        {
            if (param is not BadgeUsuario badge) return;
            var computador = Computadores.FirstOrDefault(c => c.Id == badge.ComputerId || c.Badges.Contains(badge));
            if (computador is null) return;
            computador.Badges = computador.Badges.Where(b => b.Id != badge.Id).ToList();
            SalvarPerfil(computador);
            StatusMensagem = $"Badge '{badge.Texto}' removida de {computador.NomeExibicao}.";
        });
        CarregarIconeBadgeCommand = new RelayCommand(param =>
        {
            if (param is Computador computador) CarregarIconePersonalizado(computador);
        });
        SalvarPerfilCommand = new RelayCommand(param =>
        {
            if (param is Computador computador)
            {
                SalvarPerfil(computador);
                computador.EmEdicao = false;
                StatusMensagem = $"Nome e badges de {computador.NomeExibicao} sincronizados com os painéis.";
            }
        });
    }

    private void AdicionarBadge(Computador computador)
    {
        if (computador.Badges.Count >= ProtocolConstants.MaxBadgesPerComputer)
        {
            StatusMensagem = "Cada computador pode ter até 4 badges.";
            return;
        }
        var texto = computador.NovoBadgeTexto.Trim();
        if (string.IsNullOrWhiteSpace(texto)) texto = "Badge";
        if (texto.Length > ProtocolConstants.MaxBadgeTextLength) texto = texto[..ProtocolConstants.MaxBadgeTextLength];
        if (!ThemeService.TryNormalizeColor(computador.NovoBadgeCor, out var cor))
        {
            StatusMensagem = "Cor inválida. Use uma cor como #4C8DFF.";
            return;
        }
        var badge = new BadgeUsuario
        {
            ComputerId = computador.Id,
            Texto = texto,
            Cor = cor,
            Estilo = EstilosBadge.Contains(computador.NovoBadgeEstilo) ? computador.NovoBadgeEstilo : "Holográfica",
            Icone = IconesBadge.Contains(computador.NovoBadgeIcone) ? computador.NovoBadgeIcone : "Estrela",
            IconePersonalizadoBase64 = computador.NovoBadgeIconePersonalizadoBase64,
            Brilho = computador.NovoBadgeBrilho,
            EfeitoMouse = computador.NovoBadgeEfeitoMouse,
        };
        computador.Badges = computador.Badges.Append(badge).ToList();
        computador.NovoBadgeTexto = "Destaque";
        computador.NovoBadgeIconePersonalizadoBase64 = null;
        SalvarPerfil(computador);
        StatusMensagem = $"Badge '{badge.Texto}' adicionada e compartilhada.";
    }

    private void CarregarIconePersonalizado(Computador computador)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher ícone personalizado para a badge",
            Filter = "Imagens|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length == 0 || bytes.Length > ProtocolConstants.MaxCustomBadgeIconBytes)
            {
                StatusMensagem = "O ícone personalizado precisa ter no máximo 64 KB.";
                return;
            }
            var png = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var jpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            if (!png && !jpeg)
            {
                StatusMensagem = "Ícone inválido. Escolha PNG ou JPEG.";
                return;
            }
            computador.NovoBadgeIconePersonalizadoBase64 = Convert.ToBase64String(bytes);
            StatusMensagem = $"Ícone {Path.GetFileName(dialog.FileName)} carregado para a próxima badge.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMensagem = $"Não foi possível carregar o ícone: {ex.Message}";
        }
    }

    private void SalvarPerfil(Computador computador)
    {
        var nome = string.IsNullOrWhiteSpace(computador.Apelido) ? computador.Nome : computador.Apelido!;
        _perfis.Salvar(computador.Id, nome, computador.EhOwner, computador.Badges, _settings.PainelId);
        Persist();
    }

    private void AplicarPerfis() => UiDispatcher.Invoke(() =>
    {
        foreach (var computador in Computadores) AplicarPerfil(computador);
        Persist();
    });

    private void AplicarPerfil(Computador computador)
    {
        var perfil = _perfis.Obter(computador.Id);
        if (perfil is null)
        {
            foreach (var badge in computador.Badges) badge.ComputerId = computador.Id;
            return;
        }
        computador.Apelido = string.IsNullOrWhiteSpace(perfil.NomePublico) ? null : perfil.NomePublico;
        computador.EhOwner = perfil.EhOwner;
        computador.Badges = perfil.Badges.Select(b =>
        {
            var clone = b.Clone();
            clone.ComputerId = computador.Id;
            return clone;
        }).ToList();
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
                    TemPainel = conexao.HasPanel,
                    Monitores = MonitoresOuPadrao(conexao.Monitors),
                    VersaoReceptor = conexao.ReceiverVersion,
                    VersaoPainel = conexao.PanelVersion,
                    EhOwner = conexao.IsOwner,
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
                existente.TemPainel = conexao.HasPanel;
                existente.Monitores = MonitoresOuPadrao(conexao.Monitors);
                existente.VersaoReceptor = conexao.ReceiverVersion;
                existente.VersaoPainel = conexao.PanelVersion;
                existente.EhOwner = conexao.IsOwner;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
                StatusMensagem = $"{conexao.ComputerName} reconectou-se.";
            }

            var atualizado = Computadores.First(c => c.Id == conexao.ComputerId);
            AplicarPerfil(atualizado);
            Persist();
            PublicarPingMedio();
        });
    }

    public void AtualizarStatus(string computadorId, StatusComputador status, double? pingMs)
    {
        Services.UiDispatcher.Invoke(() =>
        {
            var computador = Computadores.FirstOrDefault(c => c.Id == computadorId);
            if (computador is not null)
            {
                computador.Status = status;
                computador.PingMs = status == StatusComputador.Online ? pingMs : null;
                computador.UltimaVezVisto = DateTime.UtcNow;
            }

            PublicarPingMedio();
        });
    }

    private void PublicarPingMedio()
    {
        var onlineRemotos = Computadores
            .Where(c => c.Pareado && c.Status == StatusComputador.Online && !EhComputadorLocal(c))
            .ToList();
        var pings = onlineRemotos
            .Where(c => c.PingMs.HasValue)
            .Select(c => c.PingMs!.Value)
            .ToList();

        PingMedioAtualizado?.Invoke(pings.Count > 0
            ? pings.Average()
            : onlineRemotos.Count > 0 ? double.NaN : null);
    }

    private bool EhComputadorLocal(Computador computador) =>
        string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(computador.Nome, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

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
                    TemPainel = info.HasPanel,
                    Monitores = MonitoresOuPadrao(info.Monitors),
                    VersaoReceptor = info.ReceiverVersion,
                    VersaoPainel = info.PanelVersion,
                    EhOwner = info.IsOwner,
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
                existente.TemPainel = info.HasPanel;
                existente.Monitores = MonitoresOuPadrao(info.Monitors);
                existente.VersaoReceptor = info.ReceiverVersion;
                existente.VersaoPainel = info.PanelVersion;
                existente.EhOwner = info.IsOwner;
                existente.Status = StatusComputador.Online;
                existente.UltimaVezVisto = DateTime.UtcNow;
            }

            AplicarPerfil(Computadores.First(c => c.Id == info.ComputerId));
            Persist();
            PublicarPingMedio();
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
            computador.TemPainel = resultado.HasPanel;
            computador.Monitores = MonitoresOuPadrao(resultado.Monitors);
            computador.VersaoReceptor = resultado.ReceiverVersion;
            computador.VersaoPainel = resultado.PanelVersion;
            computador.EhOwner = resultado.IsOwner;
            computador.Status = StatusComputador.Online;
            AplicarPerfil(computador);
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

    private async Task AtualizarReceptorAsync(Computador computador)
    {
        computador.AtualizandoReceptor = true;
        StatusMensagem = $"Enviando a atualização oficial para {computador.NomeExibicao}...";
        try
        {
            var resultado = await _atualizador.AtualizarAsync(computador).ConfigureAwait(true);
            if (resultado.Success)
            {
                computador.VersaoReceptor = string.IsNullOrWhiteSpace(resultado.ReceiverVersion)
                    ? ProtocolConstants.CurrentReceiverVersion
                    : resultado.ReceiverVersion;
                Persist();
                StatusMensagem = $"{computador.NomeExibicao} foi atualizado. O receptor vai reconectar automaticamente.";
                Logger.Info(
                    $"Receptor de {computador.NomeExibicao} atualizado para {computador.VersaoReceptor}.",
                    "atualizacao");
            }
            else
            {
                var detalhe = string.IsNullOrWhiteSpace(resultado.Message)
                    ? resultado.Status
                    : resultado.Message;
                StatusMensagem = $"Não foi possível atualizar {computador.NomeExibicao}: {detalhe}. "
                    + "Se ele usa uma versão antiga, execute o instalador uma última vez nessa máquina.";
                Logger.Error(
                    $"Falha ao atualizar o receptor de {computador.NomeExibicao}.",
                    "atualizacao", detalhe);
            }
        }
        finally
        {
            computador.AtualizandoReceptor = false;
        }
    }

    private void Persist() => _store.Save(Computadores);

    private static List<MonitorInfo> MonitoresOuPadrao(IReadOnlyList<MonitorInfo>? monitores) =>
        monitores is { Count: > 0 }
            ? monitores.ToList()
            : new List<MonitorInfo>
            {
                new() { Index = 0, Name = "Monitor principal", Primary = true },
            };
}
