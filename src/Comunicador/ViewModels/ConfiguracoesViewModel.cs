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
    private readonly CloudSyncService _cloud;

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
    private int _transparenciaCards;
    private int _blurCards;
    private int _velocidadeFundo;
    private string _nomeNovaPaleta = "Minha cor";
    private string _corNovaPaleta = "#4C8DFF";
    private string _corNovaPaletaVisual = "#4C8DFF";
    private string _secaoConfiguracoes = "personalizacao";
    private string? _statusOperacao;
    private string? _statusAtualizacaoPainel;
    private string _globalTema = "Escuro";
    private string _globalPaleta = "Azul";
    private string _globalFundo = "Topográfico";
    private int _globalTransparencia = 70;
    private int _globalBlur = 18;
    private int _globalVelocidade = 100;
    private bool _globalReduzirMovimento;
    private bool _globalPermitirMidias = true;
    private bool _globalPermitirLinks = true;
    private bool _globalPermitirPapelParede = true;
    private string _globalPoliticaInicializacaoWindows = "local";
    private string? _statusGlobal;
    private PanelUpdateInfo? _ultimaVerificacaoPainel;

    public ObservableCollection<PainelPareado> PaineisPareados { get; }
    public ObservableCollection<PaletaPersonalizada> PaletasPersonalizadas { get; }
    public IReadOnlyList<string> Temas { get; } = ["Escuro", "Claro"];
    public IReadOnlyList<string> Paletas { get; } = ["Azul", "Violeta", "Verde", "Coral"];
    public IReadOnlyList<string> PoliticasInicializacaoWindows { get; } = ["local", "always", "never"];
    public IReadOnlyList<string> EstilosBadge { get; } = ["Holográfica", "Metal", "Pílula", "Contorno", "Selo"];
    public IReadOnlyList<string> IconesBadge { get; } = ["Coroa", "Estrela", "Escudo", "Raio", "Diamante", "Fogo", "Coração", "Usuário", "Código", "Música", "Jogo", "Casa", "Medalha", "Chave", "Globo"];
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

    public bool EstePainelEhOwner => _cloud.IsAdmin;
    public string StatusAdministrador => _cloud.IsAdmin
        ? "ADMIN SUPREMO ativo neste computador"
        : string.IsNullOrWhiteSpace(_cloud.AdminDeviceId)
            ? "Nenhum administrador definido"
            : "Administrador definido em outro computador";
    public string StatusSincronizacao => _cloud.Status;
    public bool PodeEditarConfiguracaoGlobal => _cloud.IsAdmin;
    public bool PodeEditarPersonalizacaoLocal => _cloud.IsAdmin || _cloud.GlobalConfig is null;
    public bool PodeAlterarInicializacaoLocal => _cloud.GlobalConfig?.PoliticaInicializacaoWindows is null or "local";
    public bool ExibirPoliticaInicializacaoGlobal => !PodeAlterarInicializacaoLocal;
    public string StatusGlobal { get => _statusGlobal ?? "Carregando configurações globais…"; private set => SetField(ref _statusGlobal, value); }
    public ObservableCollection<ModeloBadgeGlobal> ModelosBadgeGlobal { get; } = [];
    public string GlobalTema { get => _globalTema; set => SetField(ref _globalTema, value); }
    public string GlobalPaleta { get => _globalPaleta; set => SetField(ref _globalPaleta, value); }
    public string GlobalFundo { get => _globalFundo; set => SetField(ref _globalFundo, value); }
    public int GlobalTransparencia { get => _globalTransparencia; set => SetField(ref _globalTransparencia, Math.Clamp(value, 0, 90)); }
    public int GlobalBlur { get => _globalBlur; set => SetField(ref _globalBlur, Math.Clamp(value, 0, 40)); }
    public int GlobalVelocidade { get => _globalVelocidade; set => SetField(ref _globalVelocidade, Math.Clamp(value, 5, 100)); }
    public bool GlobalReduzirMovimento { get => _globalReduzirMovimento; set => SetField(ref _globalReduzirMovimento, value); }
    public bool GlobalPermitirMidias { get => _globalPermitirMidias; set => SetField(ref _globalPermitirMidias, value); }
    public bool GlobalPermitirLinks { get => _globalPermitirLinks; set => SetField(ref _globalPermitirLinks, value); }
    public bool GlobalPermitirPapelParede { get => _globalPermitirPapelParede; set => SetField(ref _globalPermitirPapelParede, value); }
    public string GlobalPoliticaInicializacaoWindows { get => _globalPoliticaInicializacaoWindows; set => SetField(ref _globalPoliticaInicializacaoWindows, value); }
    public ICommand SalvarConfiguracoesGlobaisCommand { get; }
    public ICommand AdicionarModeloBadgeGlobalCommand { get; }
    public ICommand RemoverModeloBadgeGlobalCommand { get; }

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
                ThemeService.SetReduceMotion(value);
                AgendarSalvamento();
            }
        }
    }

    public int TransparenciaCards
    {
        get => _transparenciaCards;
        set
        {
            value = Math.Clamp(value, 0, 90);
            if (!SetField(ref _transparenciaCards, value)) return;
            _settings.TransparenciaCards = value;
            ThemeService.ApplyCardAppearance(_settings);
            AgendarSalvamento();
        }
    }

    public int BlurCards
    {
        get => _blurCards;
        set
        {
            value = Math.Clamp(value, 0, 40);
            if (!SetField(ref _blurCards, value)) return;
            _settings.BlurCards = value;
            ThemeService.ApplyCardAppearance(_settings);
            AgendarSalvamento();
        }
    }

    public int VelocidadeFundo
    {
        get => _velocidadeFundo;
        set
        {
            value = Math.Clamp(value, 5, 100);
            if (!SetField(ref _velocidadeFundo, value)) return;
            _settings.VelocidadeFundo = value;
            AgendarSalvamento();
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
        PerfilComputadorRepository perfis, ReceptorClient client, PanelUpdateService panelUpdate,
        CloudSyncService cloud)
    {
        _settings = settings;
        _embeddedReceptorServer = embeddedReceptorServer;
        _paineisPareadosStore = paineisPareadosStore;
        _perfis = perfis;
        _client = client;
        _panelUpdate = panelUpdate;
        _cloud = cloud;
        PaineisPareados = paineisPareados;
        PaletasPersonalizadas = new ObservableCollection<PaletaPersonalizada>(
            settings.PaletasPersonalizadas ?? new List<PaletaPersonalizada>());

        _nomePainel = settings.NomePainel;
        _portaTcp = settings.PortaTcp;
        _portaUdp = settings.PortaDescobertaUdp;
        _intervaloDescoberta = settings.IntervaloDescobertaSegundos;
        _intervaloPing = settings.IntervaloPingSegundos;
        _iniciarComWindows = StartupManager.EstaHabilitado();
        _settings.PreferenciaInicializacaoComWindows ??= _iniciarComWindows;
        _aceitarMensagensDeOutrosPaineis = settings.AceitarMensagensDeOutrosPaineis;
        _aceitarImagensDeOutrosPaineis = settings.AceitarImagensDeOutrosPaineis;
        _aceitarBotoesComLinks = settings.AceitarBotoesComLinks;
        _tema = settings.Tema;
        _paleta = settings.Paleta;
        _fundoPainel = settings.FundoPainel;
        settings.IntensidadeFundo = 100;
        _reduzirMovimento = settings.ReduzirMovimento;
        _transparenciaCards = Math.Clamp(settings.TransparenciaCards, 0, 90);
        _blurCards = Math.Clamp(settings.BlurCards, 0, 40);
        _velocidadeFundo = Math.Clamp(settings.VelocidadeFundo, 5, 100);
        _statusReceptorEmbutido = CalcularStatusReceptor();
        AtualizarSelecaoPaletas();
        _cloud.StateChanged += OnCloudStateChanged;
        _cloud.GlobalConfigReceived += OnGlobalConfigReceived;

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
                && (secao is "personalizacao" or "recebimento" or "geral" or "administrador")
                && (secao != "administrador" || _cloud.IsAdmin))
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
        SalvarConfiguracoesGlobaisCommand = new AsyncRelayCommand(_ => SalvarConfiguracoesGlobaisAsync(), _ => _cloud.IsAdmin);
        AdicionarModeloBadgeGlobalCommand = new RelayCommand(_ =>
        {
            if (!_cloud.IsAdmin || ModelosBadgeGlobal.Count >= 20) return;
            ModelosBadgeGlobal.Add(new ModeloBadgeGlobal());
        }, _ => _cloud.IsAdmin && ModelosBadgeGlobal.Count < 20);
        RemoverModeloBadgeGlobalCommand = new RelayCommand(param =>
        {
            if (_cloud.IsAdmin && param is ModeloBadgeGlobal model) ModelosBadgeGlobal.Remove(model);
        }, param => _cloud.IsAdmin && param is ModeloBadgeGlobal);
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

    public async Task<string> AlternarAdministradorAsync(string senha)
    {
        try
        {
            var resultado = await _cloud.ToggleAdminAsync(senha).ConfigureAwait(true);
            OnCloudStateChanged();
            return resultado.Status;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível validar o administrador: {ex.Message}";
            return "unavailable";
        }
    }

    private void OnCloudStateChanged() => UiDispatcher.Invoke(() =>
    {
        OnPropertyChanged(nameof(EstePainelEhOwner));
        OnPropertyChanged(nameof(StatusAdministrador));
        OnPropertyChanged(nameof(StatusSincronizacao));
        OnPropertyChanged(nameof(PodeEditarConfiguracaoGlobal));
        OnPropertyChanged(nameof(PodeEditarPersonalizacaoLocal));
        CommandManager.InvalidateRequerySuggested();
        StatusOperacao = _cloud.Status;
    });

    private void OnGlobalConfigReceived(ConfiguracaoGlobalPrograma config) => UiDispatcher.Invoke(() =>
    {
        _globalTema = config.Tema;
        _globalPaleta = config.Paleta;
        _globalFundo = config.FundoPainel;
        _globalReduzirMovimento = config.ReduzirMovimento;
        _globalTransparencia = Math.Clamp(config.TransparenciaCards, 0, 90);
        _globalBlur = Math.Clamp(config.BlurCards, 0, 40);
        _globalVelocidade = Math.Clamp(config.VelocidadeFundo, 5, 100);
        _globalPermitirMidias = config.PermitirMidias;
        _globalPermitirLinks = config.PermitirLinks;
        _globalPermitirPapelParede = config.PermitirPapelParedeRemoto;
        _globalPoliticaInicializacaoWindows = config.PoliticaInicializacaoWindows;
        ModelosBadgeGlobal.Clear();
        foreach (var model in config.ModelosBadge) ModelosBadgeGlobal.Add(model);
        OnPropertyChanged(nameof(GlobalTema)); OnPropertyChanged(nameof(GlobalPaleta));
        OnPropertyChanged(nameof(GlobalFundo)); OnPropertyChanged(nameof(GlobalReduzirMovimento));
        OnPropertyChanged(nameof(GlobalTransparencia)); OnPropertyChanged(nameof(GlobalBlur));
        OnPropertyChanged(nameof(GlobalVelocidade)); OnPropertyChanged(nameof(GlobalPermitirMidias));
        OnPropertyChanged(nameof(GlobalPermitirLinks)); OnPropertyChanged(nameof(GlobalPermitirPapelParede));
        OnPropertyChanged(nameof(GlobalPoliticaInicializacaoWindows));
        AplicarConfiguracaoGlobalLocal(config);
        StatusGlobal = "Configurações globais sincronizadas.";
        OnPropertyChanged(nameof(PodeEditarPersonalizacaoLocal));
    });

    private void AplicarConfiguracaoGlobalLocal(ConfiguracaoGlobalPrograma config)
    {
        _settings.Tema = config.Tema;
        _settings.Paleta = config.Paleta;
        _settings.FundoPainel = config.FundoPainel;
        _settings.ReduzirMovimento = config.ReduzirMovimento;
        _settings.TransparenciaCards = Math.Clamp(config.TransparenciaCards, 0, 90);
        _settings.BlurCards = Math.Clamp(config.BlurCards, 0, 40);
        _settings.VelocidadeFundo = Math.Clamp(config.VelocidadeFundo, 5, 100);
        _settings.MidiasPermitidasGlobalmente = config.PermitirMidias;
        _settings.LinksPermitidosGlobalmente = config.PermitirLinks;
        _settings.PapelParedeRemotoPermitidoGlobalmente = config.PermitirPapelParedeRemoto;
        var politicaAnterior = _cloud.GlobalConfig?.PoliticaInicializacaoWindows ?? "local";
        var politicaAtual = config.PoliticaInicializacaoWindows is "always" or "never"
            ? config.PoliticaInicializacaoWindows : "local";
        _settings.IniciarComWindows = politicaAtual switch
        {
            "always" => true,
            "never" => false,
            _ => _settings.PreferenciaInicializacaoComWindows ?? StartupManager.EstaHabilitado(),
        };
        if (politicaAnterior != politicaAtual || StartupManager.EstaHabilitado() != _settings.IniciarComWindows)
            StartupManager.Aplicar(_settings.IniciarComWindows);
        _iniciarComWindows = _settings.IniciarComWindows;
        _tema = config.Tema; _paleta = config.Paleta; _fundoPainel = config.FundoPainel;
        _reduzirMovimento = config.ReduzirMovimento;
        _transparenciaCards = _settings.TransparenciaCards; _blurCards = _settings.BlurCards;
        _velocidadeFundo = _settings.VelocidadeFundo;
        OnPropertyChanged(nameof(Tema)); OnPropertyChanged(nameof(Paleta)); OnPropertyChanged(nameof(FundoPainel));
        OnPropertyChanged(nameof(ReduzirMovimento)); OnPropertyChanged(nameof(TransparenciaCards));
        OnPropertyChanged(nameof(BlurCards)); OnPropertyChanged(nameof(VelocidadeFundo));
        OnPropertyChanged(nameof(ExibirFundoAnimado));
        OnPropertyChanged(nameof(IniciarComWindows));
        OnPropertyChanged(nameof(PodeAlterarInicializacaoLocal));
        OnPropertyChanged(nameof(ExibirPoliticaInicializacaoGlobal));
        AtualizarSelecaoPaletas();
        SettingsStore.Save(_settings);
        ThemeService.Apply(_settings);
        ThemeService.ApplyCardAppearance(_settings);
        ThemeService.SetReduceMotion(_settings.ReduzirMovimento);
    }

    private async Task SalvarConfiguracoesGlobaisAsync()
    {
        if (!_cloud.IsAdmin) return;
        if (!Fundos.Contains(GlobalFundo) || GlobalTema is not ("Escuro" or "Claro")
            || GlobalPaleta is not ("Azul" or "Violeta" or "Verde" or "Coral")
            || !PoliticasInicializacaoWindows.Contains(GlobalPoliticaInicializacaoWindows))
        {
            StatusGlobal = "Revise tema, paleta e fundo antes de aplicar.";
            return;
        }
        foreach (var model in ModelosBadgeGlobal)
        {
            if (model.Id is "owner" or "admin" || string.IsNullOrWhiteSpace(model.Nome)
                || model.Nome.Length > 40 || string.IsNullOrWhiteSpace(model.Texto) || model.Texto.Length > 28
                || !ThemeService.TryNormalizeColor(model.Cor, out var cor)
                || !EstilosBadge.Contains(model.Estilo) || !IconesBadge.Contains(model.Icone))
            {
                StatusGlobal = "Revise nome, texto, cor, estilo e ícone das tags antes de aplicar.";
                return;
            }
            model.Cor = cor;
        }
        var config = new ConfiguracaoGlobalPrograma
        {
            Tema = GlobalTema, Paleta = GlobalPaleta, FundoPainel = GlobalFundo,
            ReduzirMovimento = GlobalReduzirMovimento,
            TransparenciaCards = GlobalTransparencia, BlurCards = GlobalBlur,
            VelocidadeFundo = GlobalVelocidade, PermitirMidias = GlobalPermitirMidias,
            PermitirLinks = GlobalPermitirLinks, PermitirPapelParedeRemoto = GlobalPermitirPapelParede,
            PoliticaInicializacaoWindows = GlobalPoliticaInicializacaoWindows,
            ModelosBadge = ModelosBadgeGlobal.ToList(),
        };
        try
        {
            StatusGlobal = "Aplicando nos painéis…";
            await _cloud.SaveGlobalConfigAsync(config).ConfigureAwait(true);
            OnGlobalConfigReceived(config);
            StatusGlobal = "Configurações globais aplicadas. Os outros painéis recebem na próxima sincronização.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            StatusGlobal = $"Não foi possível salvar no Supabase: {ex.Message}";
        }
    }

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
        _settings.EstePainelEhOwner = _cloud.IsAdmin;
        _settings.PortaTcp = PortaTcp;
        _settings.PortaDescobertaUdp = PortaUdp;
        _settings.IntervaloDescobertaSegundos = IntervaloDescobertaSegundos;
        _settings.IntervaloPingSegundos = IntervaloPingSegundos;
        _settings.IniciarComWindows = IniciarComWindows;
        if (_cloud.GlobalConfig?.PoliticaInicializacaoWindows is null or "local")
            _settings.PreferenciaInicializacaoComWindows = IniciarComWindows;
        _settings.AceitarMensagensDeOutrosPaineis = true;
        _settings.AceitarImagensDeOutrosPaineis = AceitarImagensDeOutrosPaineis;
        _settings.AceitarBotoesComLinks = AceitarBotoesComLinks;
        _settings.Tema = Tema;
        _settings.Paleta = Paleta;
        SincronizarPaletasNasConfiguracoes();
        _settings.FundoPainel = FundoPainel;
        _settings.IntensidadeFundo = 100;
        _settings.ReduzirMovimento = ReduzirMovimento;
        _settings.TransparenciaCards = TransparenciaCards;
        _settings.BlurCards = BlurCards;
        _settings.VelocidadeFundo = VelocidadeFundo;
        SettingsStore.Save(_settings);
        _client.UpdatePanelName(_settings.NomePainel);
        var perfilAtual = _perfis.Obter(_settings.PainelId);
        var badges = perfilAtual?.Badges.Select(b => b.Clone()).ToList() ?? new List<BadgeUsuario>();
        if (_cloud.IsAdmin && badges.All(b => b.Id != "owner"))
        {
            badges = badges.Take(Protocol.ProtocolConstants.MaxBadgesPerComputer - 1).ToList();
            badges.Insert(0, new BadgeUsuario
            {
                Id = "owner", Texto = "OWNER", Cor = "#F2B84B", Estilo = "Holográfica",
                Icone = "Coroa", Brilho = true, EfeitoMouse = true,
            });
        }
        else if (!_cloud.IsAdmin)
        {
            badges.RemoveAll(b => b.Id == "owner");
        }
        _perfis.Salvar(_settings.PainelId, _settings.NomePainel, _cloud.IsAdmin,
            badges, _settings.PainelId);
        _ = SincronizarPerfilLocalAsync(_settings.NomePainel, badges);
        if (inicializacaoMudou) StartupManager.Aplicar(IniciarComWindows);
        _embeddedReceptorServer.AtualizarDisponibilidade();
        StatusReceptorEmbutido = CalcularStatusReceptor();

        StatusOperacao = automatico
            ? "Salvo automaticamente neste painel."
            : "Configurações salvas neste painel.";
    }

    private string CalcularStatusReceptor() =>
        _embeddedReceptorServer.Ativo
            ? "Ativo — mensagens e lembretes são obrigatórios; mídias seguem sua permissão."
            : _embeddedReceptorServer.UltimoErro ?? "Receptor embutido indisponível.";

    private async Task SincronizarPerfilLocalAsync(string nome, IReadOnlyList<BadgeUsuario> badges)
    {
        try
        {
            await _cloud.SaveProfileAsync(_settings.PainelId, nome, badges).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            UiDispatcher.Invoke(() => StatusOperacao = $"Salvo localmente; Supabase pendente: {ex.Message}");
        }
    }

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
