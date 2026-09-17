using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Threading;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Services;
using Comunicador.Storage;

namespace Comunicador.ViewModels;

public sealed class ConfiguracoesViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly EmbeddedReceptorServer _embeddedReceptorServer;
    private readonly JsonStore<PainelPareado> _paineisPareadosStore;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly PerfilComputadorRepository _perfis;
    private readonly ReceptorClient _client;
    private readonly PanelUpdateService _panelUpdate;

    private string _nomePainel;
    private int _portaTcp;
    private int _portaUdp;
    private int _intervaloDescoberta;
    private int _intervaloPing;
    private bool _iniciarComWindows;
    private bool _aceitarMensagensDeOutrosPaineis;
    private bool _aceitarImagensDeOutrosPaineis;
    private bool _aceitarBotoesComLinks;
    private string _tema;
    private string _paleta;
    private string _fundoPainel;
    private bool _reduzirMovimento;
    private bool _estePainelEhOwner;
    private string _nomeNovaPaleta = "Minha cor";
    private string _corNovaPaleta = "#4C8DFF";
    private string _corNovaPaletaVisual = "#4C8DFF";
    private string _secaoConfiguracoes = "personalizacao";
    private string? _statusOperacao;
    private string? _statusAtualizacaoPainel;
    private PanelUpdateInfo? _ultimaVerificacaoPainel;

    public ObservableCollection<PainelPareado> PaineisPareados { get; }
    public ObservableCollection<PaletaPersonalizada> PaletasPersonalizadas { get; }
    public IReadOnlyList<string> Temas { get; } = ["Escuro", "Claro"];
    public IReadOnlyList<string> Paletas { get; } = ["Azul", "Violeta", "Verde", "Coral"];
    public IReadOnlyList<string> Fundos { get; } =
        ["Sem fundo", "Topográfico", "Caminhos flutuantes", "Vórtice", "Ondas luminosas", "Constelação", "Grade fluida", "Partículas fluidas", "Onda de partículas"];

    public string NomeNovaPaleta
    {
        get => _nomeNovaPaleta;
        set => SetField(ref _nomeNovaPaleta, value);
    }

    public string CorNovaPaleta
    {
        get => _corNovaPaleta;
        set
        {
            if (!SetField(ref _corNovaPaleta, value)) return;
            if (ThemeService.TryNormalizeColor(value, out var normalized))
            {
                _corNovaPaletaVisual = normalized;
                OnPropertyChanged(nameof(CorNovaPaletaVisual));
            }
        }
    }

    public string CorNovaPaletaVisual => _corNovaPaletaVisual;

    public string NomePainel
    {
        get => _nomePainel;
        set { if (SetField(ref _nomePainel, value)) AgendarSalvamento(); }
    }

    public bool EstePainelEhOwner
    {
        get => _estePainelEhOwner;
        set { if (SetField(ref _estePainelEhOwner, value)) AgendarSalvamento(); }
    }

    public int PortaTcp
    {
        get => _portaTcp;
        set { if (SetField(ref _portaTcp, value)) AgendarSalvamento(); }
    }

    public int PortaUdp
    {
        get => _portaUdp;
        set { if (SetField(ref _portaUdp, value)) AgendarSalvamento(); }
    }

    public int IntervaloDescobertaSegundos
    {
        get => _intervaloDescoberta;
        set { if (SetField(ref _intervaloDescoberta, value)) AgendarSalvamento(); }
    }

    public int IntervaloPingSegundos
    {
        get => _intervaloPing;
        set { if (SetField(ref _intervaloPing, value)) AgendarSalvamento(); }
    }

    public bool IniciarComWindows
    {
        get => _iniciarComWindows;
        set { if (SetField(ref _iniciarComWindows, value)) AgendarSalvamento(); }
    }

    public bool AceitarMensagensDeOutrosPaineis
    {
        get => _aceitarMensagensDeOutrosPaineis;
        set { if (SetField(ref _aceitarMensagensDeOutrosPaineis, value)) AgendarSalvamento(); }
    }

    public bool AceitarImagensDeOutrosPaineis
    {
        get => _aceitarImagensDeOutrosPaineis;
        set { if (SetField(ref _aceitarImagensDeOutrosPaineis, value)) AgendarSalvamento(); }
    }

    public bool AceitarBotoesComLinks
    {
        get => _aceitarBotoesComLinks;
        set { if (SetField(ref _aceitarBotoesComLinks, value)) AgendarSalvamento(); }
    }

    public string Tema
    {
        get => _tema;
        set
        {
            if (SetField(ref _tema, value))
            {
                _settings.Tema = value;
                ThemeService.Apply(_settings);
                AgendarSalvamento();
            }
        }
    }

    public string Paleta
    {
        get => _paleta;
        set
        {
            if (SetField(ref _paleta, value))
            {
                _settings.Paleta = value;
                AtualizarSelecaoPaletas();
                ThemeService.Apply(_settings);
                AgendarSalvamento();
            }
        }
    }

    public string FundoPainel
    {
        get => _fundoPainel;
        set
        {
            if (SetField(ref _fundoPainel, value))
            {
                _settings.FundoPainel = value;
                OnPropertyChanged(nameof(ExibirFundoAnimado));
                AgendarSalvamento();
            }
        }
    }

    public double IntensidadeFundo => 100;

    public bool ReduzirMovimento
    {
        get => _reduzirMovimento;
        set
        {
            if (SetField(ref _reduzirMovimento, value))
            {
                _settings.ReduzirMovimento = value;
                AgendarSalvamento();
            }
        }
    }

    public bool ExibirFundoAnimado => FundoPainel != "Sem fundo";

    public string SecaoConfiguracoes
    {
        get => _secaoConfiguracoes;
        private set => SetField(ref _secaoConfiguracoes, value);
    }

    private string? _statusReceptorEmbutido;

    public string? StatusReceptorEmbutido
    {
        get => _statusReceptorEmbutido;
        private set => SetField(ref _statusReceptorEmbutido, value);
    }

    public string? StatusOperacao
    {
        get => _statusOperacao;
        set => SetField(ref _statusOperacao, value);
    }

    public string PainelId => _settings.PainelId;
    public string VersaoPainelAtual => $"Versão instalada: {Protocol.ProtocolConstants.CurrentPanelVersion}";
    public bool AtualizacaoPainelDisponivel => _ultimaVerificacaoPainel?.IsAvailable == true;
    public string? StatusAtualizacaoPainel
    {
        get => _statusAtualizacaoPainel;
        private set => SetField(ref _statusAtualizacaoPainel, value);
    }

    public ICommand SalvarCommand { get; }
    public ICommand SelecionarTemaCommand { get; }
    public ICommand SelecionarPaletaCommand { get; }
    public ICommand SelecionarFundoCommand { get; }
    public ICommand SalvarPaletaPersonalizadaCommand { get; }
    public ICommand RemoverPaletaPersonalizadaCommand { get; }
    public ICommand NavegarConfiguracaoCommand { get; }
    public ICommand RemoverPainelPareadoCommand { get; }
    public ICommand VerificarAtualizacaoPainelCommand { get; }
    public ICommand AtualizarPainelCommand { get; }

    public ConfiguracoesViewModel(
        AppSettings settings, ObservableCollection<PainelPareado> paineisPareados,
        JsonStore<PainelPareado> paineisPareadosStore, EmbeddedReceptorServer embeddedReceptorServer,
        PerfilComputadorRepository perfis, ReceptorClient client, PanelUpdateService panelUpdate)
    {
        _settings = settings;
        _embeddedReceptorServer = embeddedReceptorServer;
        _paineisPareadosStore = paineisPareadosStore;
        _perfis = perfis;
        _client = client;
        _panelUpdate = panelUpdate;
        PaineisPareados = paineisPareados;
        PaletasPersonalizadas = new ObservableCollection<PaletaPersonalizada>(
            settings.PaletasPersonalizadas ?? new List<PaletaPersonalizada>());

        _nomePainel = settings.NomePainel;
        _estePainelEhOwner = settings.EstePainelEhOwner;
        _portaTcp = settings.PortaTcp;
        _portaUdp = settings.PortaDescobertaUdp;
        _intervaloDescoberta = settings.IntervaloDescobertaSegundos;
        _intervaloPing = settings.IntervaloPingSegundos;
        _iniciarComWindows = StartupManager.EstaHabilitado();
        _aceitarMensagensDeOutrosPaineis = settings.AceitarMensagensDeOutrosPaineis;
        _aceitarImagensDeOutrosPaineis = settings.AceitarImagensDeOutrosPaineis;
        _aceitarBotoesComLinks = settings.AceitarBotoesComLinks;
        _tema = settings.Tema;
        _paleta = settings.Paleta;
        _fundoPainel = settings.FundoPainel;
        settings.IntensidadeFundo = 100;
        _reduzirMovimento = settings.ReduzirMovimento;
        _statusReceptorEmbutido = CalcularStatusReceptor();
        AtualizarSelecaoPaletas();

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            Salvar(automatico: true);
        };

        SalvarCommand = new RelayCommand(_ => Salvar(automatico: false));
        SelecionarTemaCommand = new RelayCommand(param =>
        {
            if (param is string tema && Temas.Contains(tema)) Tema = tema;
        });
        SelecionarPaletaCommand = new RelayCommand(param =>
        {
            if (param is string paleta && (Paletas.Contains(paleta)
                || PaletasPersonalizadas.Any(p => p.Id == paleta))) Paleta = paleta;
        });
        SalvarPaletaPersonalizadaCommand = new RelayCommand(_ => SalvarPaletaPersonalizada());
        RemoverPaletaPersonalizadaCommand = new RelayCommand(param =>
        {
            if (param is not PaletaPersonalizada paleta) return;
            PaletasPersonalizadas.Remove(paleta);
            if (Paleta == paleta.Id) Paleta = "Azul";
            SincronizarPaletasNasConfiguracoes();
            AgendarSalvamento();
            StatusOperacao = $"Predefinição '{paleta.Nome}' removida.";
        });
        SelecionarFundoCommand = new RelayCommand(param =>
        {
            if (param is string fundo && Fundos.Contains(fundo)) FundoPainel = fundo;
        });
        NavegarConfiguracaoCommand = new RelayCommand(param =>
        {
            if (param is string secao
                && secao is "personalizacao" or "recebimento" or "geral")
            {
                SecaoConfiguracoes = secao;
            }
        });
        RemoverPainelPareadoCommand = new RelayCommand(param =>
        {
            if (param is PainelPareado pareado)
            {
                PaineisPareados.Remove(pareado);
                _paineisPareadosStore.Save(PaineisPareados);
                StatusOperacao = $"Painel '{pareado.PanelName}' removido — ele precisará parear novamente para enviar mensagens.";
            }
        });
        VerificarAtualizacaoPainelCommand = new AsyncRelayCommand(_ => VerificarAtualizacaoPainelAsync());
        AtualizarPainelCommand = new AsyncRelayCommand(_ => AtualizarPainelAsync(),
            _ => AtualizacaoPainelDisponivel);
    }

    public async Task VerificarAtualizacaoPainelAsync()
    {
        StatusAtualizacaoPainel = "Verificando a versão publicada…";
        try
        {
            _ultimaVerificacaoPainel = await _panelUpdate.CheckAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(AtualizacaoPainelDisponivel));
            StatusAtualizacaoPainel = _ultimaVerificacaoPainel.IsAvailable
                ? $"Versão {_ultimaVerificacaoPainel.LatestVersion} disponível."
                : "Este painel já está atualizado.";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidDataException)
        {
            StatusAtualizacaoPainel = $"Não foi possível verificar agora: {ex.Message}";
        }
    }

    private async Task AtualizarPainelAsync()
    {
        if (_ultimaVerificacaoPainel is not { IsAvailable: true } info) return;
        StatusAtualizacaoPainel = "Baixando a atualização. O painel reiniciará sozinho…";
        try
        {
            await _panelUpdate.StartUpdateAsync(info).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusAtualizacaoPainel = $"Falha ao iniciar a atualização: {ex.Message}";
        }
    }

    /// <summary>Chamado pelo MainViewModel depois que o receptor embutido efetivamente
    /// tenta iniciar (Start()/AtualizarDisponibilidade() são assíncronos em relação à
    /// construção desta ViewModel), para o texto de status não ficar desatualizado.</summary>
    public void AtualizarStatusReceptor() => StatusReceptorEmbutido = CalcularStatusReceptor();

    private void AgendarSalvamento()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
        StatusOperacao = "Salvando automaticamente…";
    }

    private void Salvar(bool automatico)
    {
        if (string.IsNullOrWhiteSpace(NomePainel)
            || PortaTcp is <= 0 or > 65535
            || PortaUdp is <= 0 or > 65535
            || IntervaloDescobertaSegundos is < 3 or > 3600
            || IntervaloPingSegundos is < 3 or > 3600)
        {
            StatusOperacao = "Há um valor inválido; esta alteração ainda não foi salva.";
            return;
        }

        var inicializacaoMudou = _settings.IniciarComWindows != IniciarComWindows;
        _settings.NomePainel = NomePainel.Trim();
        _settings.EstePainelEhOwner = EstePainelEhOwner;
        _settings.PortaTcp = PortaTcp;
        _settings.PortaDescobertaUdp = PortaUdp;
        _settings.IntervaloDescobertaSegundos = IntervaloDescobertaSegundos;
        _settings.IntervaloPingSegundos = IntervaloPingSegundos;
        _settings.IniciarComWindows = IniciarComWindows;
        _settings.AceitarMensagensDeOutrosPaineis = AceitarMensagensDeOutrosPaineis;
        _settings.AceitarImagensDeOutrosPaineis = AceitarImagensDeOutrosPaineis;
        _settings.AceitarBotoesComLinks = AceitarBotoesComLinks;
        _settings.Tema = Tema;
        _settings.Paleta = Paleta;
        SincronizarPaletasNasConfiguracoes();
        _settings.FundoPainel = FundoPainel;
        _settings.IntensidadeFundo = 100;
        _settings.ReduzirMovimento = ReduzirMovimento;
        SettingsStore.Save(_settings);
        _client.UpdatePanelName(_settings.NomePainel);
        var perfilAtual = _perfis.Obter(_settings.PainelId);
        _perfis.Salvar(_settings.PainelId, _settings.NomePainel, _settings.EstePainelEhOwner,
            perfilAtual?.Badges ?? (IEnumerable<BadgeUsuario>)Array.Empty<BadgeUsuario>(), _settings.PainelId);
        if (inicializacaoMudou) StartupManager.Aplicar(IniciarComWindows);
        _embeddedReceptorServer.AtualizarDisponibilidade();
        StatusReceptorEmbutido = CalcularStatusReceptor();

        StatusOperacao = automatico
            ? "Salvo automaticamente neste painel."
            : "Configurações salvas neste painel.";
    }

    private string CalcularStatusReceptor() =>
        _embeddedReceptorServer.Ativo
            ? AceitarMensagensDeOutrosPaineis
                ? "Ativo — este computador aparece como painel e pode receber mensagens."
                : "Ativo e visível como painel — mensagens recebidas estão bloqueadas."
            : _embeddedReceptorServer.UltimoErro ?? "Receptor embutido indisponível.";

    private void SalvarPaletaPersonalizada()
    {
        if (PaletasPersonalizadas.Count >= 24)
        {
            StatusOperacao = "Limite de 24 predefinições personalizadas atingido.";
            return;
        }
        if (!ThemeService.TryNormalizeColor(CorNovaPaleta, out var cor))
        {
            StatusOperacao = "Cor inválida. Use, por exemplo, #4C8DFF.";
            return;
        }

        var nome = string.IsNullOrWhiteSpace(NomeNovaPaleta)
            ? $"Cor {PaletasPersonalizadas.Count + 1}"
            : NomeNovaPaleta.Trim();
        if (nome.Length > 40) nome = nome[..40];
        var paleta = new PaletaPersonalizada { Nome = nome, Cor = cor };
        PaletasPersonalizadas.Add(paleta);
        SincronizarPaletasNasConfiguracoes();
        Paleta = paleta.Id;
        NomeNovaPaleta = "Minha cor";
        StatusOperacao = $"Predefinição '{nome}' salva neste painel.";
    }

    private void AtualizarSelecaoPaletas()
    {
        foreach (var item in PaletasPersonalizadas) item.Selecionada = item.Id == _paleta;
    }

    private void SincronizarPaletasNasConfiguracoes() =>
        _settings.PaletasPersonalizadas = PaletasPersonalizadas.ToList();
}
