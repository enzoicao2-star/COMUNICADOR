using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;
using Comunicador.Services;
using Microsoft.Win32;

namespace Comunicador.ViewModels;

public sealed class MensagensViewModel : ViewModelBase
{
    private readonly ComputadoresViewModel _computadores;
    private readonly EnviadorNotificacoes _enviador;
    private readonly HistoricoRepository _historico;
    private readonly CloudSyncService _cloud;
    private readonly ReenvioRepository _reenvios;
    private readonly Dictionary<string, List<MidiaMonitorEditavel>> _midiasPorMonitor = new(StringComparer.Ordinal);
    private readonly HashSet<Computador> _computadoresObservados = new();

    private string _titulo = string.Empty;
    private string _mensagem = string.Empty;
    private bool _permitirResposta = true;
    private bool _exibirImagemCentral;
    private bool _exibirAvisoObrigatorio;
    private bool _exibirMensagemCentral;
    private double _tempoImagemSegundos = 15;
    private bool _permitirFecharImagem = true;
    private bool _tocarSom = true;
    private string _tipoSom = ProtocolConstants.SoundType.Information;
    private string _posicaoAviso = ProtocolConstants.ToastPosition.BottomRight;
    private string _corDestaque = "#0067C0";
    private double _escalaTexto = 100;
    private double _tempoAvisoSegundos = 20;
    private string? _caminhoImagem;
    private string? _nomeImagem;
    private string? _mimeImagem;
    private byte[]? _dadosImagem;
    private string? _caminhoVideo;
    private string? _nomeVideo;
    private string? _mimeVideo;
    private byte[]? _dadosVideo;
    private string? _caminhoAudio;
    private string? _nomeAudio;
    private string? _mimeAudio;
    private byte[]? _dadosAudio;
    private bool _repetirVideo;
    private bool _repetirAudio;
    private bool _limitarDuracaoAudio;
    private bool _definirComoPapelDeParede;
    private bool _definirComoTelaDeBloqueio;
    private string? _statusOperacao;
    private string _novoGrupoNome = string.Empty;
    private string _novoModeloNome = string.Empty;
    private GrupoComputadoresGlobal? _grupoSelecionado;
    private ModeloMensagemGlobal? _modeloSelecionado;

    public ObservableCollection<ComputadorSelecionavel> Destinatarios { get; } = new();
    public ObservableCollection<DestinoMonitor> MonitoresDestino { get; } = new();
    public ObservableCollection<GrupoComputadoresGlobal> GruposComputadores { get; } = new();
    public ObservableCollection<ModeloMensagemGlobal> ModelosMensagem { get; } = new();

    public GrupoComputadoresGlobal? GrupoSelecionado
    {
        get => _grupoSelecionado;
        set => SetField(ref _grupoSelecionado, value);
    }
    public ModeloMensagemGlobal? ModeloSelecionado
    {
        get => _modeloSelecionado;
        set => SetField(ref _modeloSelecionado, value);
    }
    public string NovoGrupoNome { get => _novoGrupoNome; set => SetField(ref _novoGrupoNome, value); }
    public string NovoModeloNome { get => _novoModeloNome; set => SetField(ref _novoModeloNome, value); }
    public bool PodeGerenciarBiblioteca => _cloud.IsAdmin && _cloud.GlobalConfig is not null;
    public bool PodeGerenciarCarrossel => _cloud.IsAdmin;

    /// <summary>Botões de resposta rápida que vão junto com o aviso.</summary>
    public ObservableCollection<BotaoRespostaEditavel> Botoes { get; } = new();

    private string _novoBotaoRotulo = string.Empty;
    private string _novoBotaoUrl = string.Empty;

    public string NovoBotaoRotulo
    {
        get => _novoBotaoRotulo;
        set => SetField(ref _novoBotaoRotulo, value);
    }

    public string NovoBotaoUrl
    {
        get => _novoBotaoUrl;
        set => SetField(ref _novoBotaoUrl, value);
    }

    public ICommand AdicionarBotaoCommand { get; }
    public ICommand RemoverBotaoCommand { get; }

    public string Titulo
    {
        get => _titulo;
        set => SetField(ref _titulo, value);
    }

    public string Mensagem
    {
        get => _mensagem;
        set => SetField(ref _mensagem, value);
    }

    public bool PermitirResposta
    {
        get => _permitirResposta;
        set => SetField(ref _permitirResposta, value);
    }

    public bool ExibirImagemCentral
    {
        get => _exibirImagemCentral;
        set
        {
            if (SetField(ref _exibirImagemCentral, value))
            {
                if (value) { DefinirComoPapelDeParede = false; DefinirComoTelaDeBloqueio = false; }
                if (value && _exibirAvisoObrigatorio)
                {
                    _exibirAvisoObrigatorio = false;
                    OnPropertyChanged(nameof(ExibirAvisoObrigatorio));
                }
                if (value && _exibirMensagemCentral)
                {
                    _exibirMensagemCentral = false;
                    OnPropertyChanged(nameof(ExibirMensagemCentral));
                }
                OnPropertyChanged(nameof(DescricaoFormato));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? CaminhoImagem
    {
        get => _caminhoImagem;
        private set => SetField(ref _caminhoImagem, value);
    }

    public string? NomeImagem
    {
        get => _nomeImagem;
        private set => SetField(ref _nomeImagem, value);
    }

    public string? MimeImagem => _mimeImagem;

    public string? ImagemPreviewBase64 => _dadosImagem is { Length: > 0 }
        ? Convert.ToBase64String(_dadosImagem)
        : null;

    public bool TemImagem => _dadosImagem is { Length: > 0 };
    public bool ImagemCompativelPapelParede => TemImagem
        && (_mimeImagem == "image/png" || _mimeImagem == "image/jpeg");
    public bool TemVideo => _dadosVideo is { Length: > 0 };
    public bool TemAudio => _dadosAudio is { Length: > 0 };
    public bool TemMidia => TemImagem || TemVideo || TemAudio;
    public bool TemVideoConfigurado => TemVideo || MonitoresDestino.Any(m => m.Midias.Any(i => i.EhVideo));
    public bool PodeConfigurarMonitores => !TemAudio;
    public string? CaminhoMidia => CaminhoImagem ?? _caminhoVideo ?? _caminhoAudio;
    public string? NomeMidia => NomeImagem ?? _nomeVideo ?? _nomeAudio;
    public string TipoMidiaTexto => TemAudio ? "Áudio" : TemVideo ? "Vídeo"
        : TemImagem ? (_mimeImagem == "image/gif" ? "GIF" : "Imagem") : "Mídia";
    public bool PodeAlterarPapelParede => _cloud.IsAdmin
        && _cloud.GlobalConfig?.PermitirPapelParedeRemoto != false;
    public bool AlterarImagemSistema => DefinirComoPapelDeParede || DefinirComoTelaDeBloqueio;
    public bool DefinirComoPapelDeParede
    {
        get => _definirComoPapelDeParede;
        set
        {
            if (value && (!PodeAlterarPapelParede || !ImagemCompativelPapelParede)) return;
            if (!SetField(ref _definirComoPapelDeParede, value)) return;
            if (value)
            {
                DefinirComoTelaDeBloqueio = false;
                ExibirImagemCentral = false;
                ExibirAvisoObrigatorio = false;
                ExibirMensagemCentral = false;
            }
            else if (TemImagem && !AlterarImagemSistema)
            {
                ExibirImagemCentral = true;
            }
            OnPropertyChanged(nameof(AlterarImagemSistema));
            OnPropertyChanged(nameof(DescricaoFormato));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool DefinirComoTelaDeBloqueio
    {
        get => _definirComoTelaDeBloqueio;
        set
        {
            if (value && (!PodeAlterarPapelParede || !ImagemCompativelPapelParede)) return;
            if (!SetField(ref _definirComoTelaDeBloqueio, value)) return;
            if (value)
            {
                DefinirComoPapelDeParede = false;
                ExibirImagemCentral = false;
                ExibirAvisoObrigatorio = false;
                ExibirMensagemCentral = false;
            }
            else if (TemImagem && !AlterarImagemSistema)
            {
                ExibirImagemCentral = true;
            }
            OnPropertyChanged(nameof(AlterarImagemSistema));
            OnPropertyChanged(nameof(DescricaoFormato));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool RepetirVideo
    {
        get => _repetirVideo;
        set { if (SetField(ref _repetirVideo, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool RepetirAudio
    {
        get => _repetirAudio;
        set
        {
            if (SetField(ref _repetirAudio, value) && value)
            {
                LimitarDuracaoAudio = true;
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool LimitarDuracaoAudio
    {
        get => _limitarDuracaoAudio;
        set
        {
            var valor = RepetirAudio || value;
            if (SetField(ref _limitarDuracaoAudio, valor))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string DescricaoFormato => DefinirComoPapelDeParede
        ? "A imagem será aplicada como papel de parede, sem abrir uma janela no computador de destino."
        : DefinirComoTelaDeBloqueio
        ? "A imagem será aplicada à tela de bloqueio da conta do destinatário, sem abrir uma janela."
        : ExibirImagemCentral
        ? TemAudio
            ? "O áudio tocará em segundo plano, sem abrir nenhuma janela."
            : "A mídia aparecerá centralizada, sem moldura. Título e mensagem não são necessários."
        : ExibirMensagemCentral
            ? ExibirAvisoObrigatorio
                ? "A mensagem aparecerá no centro e permanecerá até a pessoa responder, clicar em um botão ou confirmar."
                : "A mensagem aparecerá no centro da tela e poderá receber respostas ou ações."
        : ExibirAvisoObrigatorio
            ? "O aviso aparecerá no canto e permanecerá até a pessoa responder, clicar em um botão ou confirmar."
            : "O aviso aparecerá no canto inferior direito, no estilo do Windows.";

    public bool ExibirMensagemCentral
    {
        get => _exibirMensagemCentral;
        set
        {
            if (!SetField(ref _exibirMensagemCentral, value)) return;
            if (value)
            {
                DefinirComoPapelDeParede = false;
                DefinirComoTelaDeBloqueio = false;
                if (_exibirImagemCentral) { _exibirImagemCentral = false; OnPropertyChanged(nameof(ExibirImagemCentral)); }
            }
            OnPropertyChanged(nameof(DescricaoFormato));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ExibirAvisoObrigatorio
    {
        get => _exibirAvisoObrigatorio;
        set
        {
            if (SetField(ref _exibirAvisoObrigatorio, value))
            {
                if (value) { DefinirComoPapelDeParede = false; DefinirComoTelaDeBloqueio = false; }
                if (value && _exibirImagemCentral)
                {
                    _exibirImagemCentral = false;
                    OnPropertyChanged(nameof(ExibirImagemCentral));
                }
                OnPropertyChanged(nameof(DescricaoFormato));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double TempoImagemSegundos
    {
        get => _tempoImagemSegundos;
        set => SetField(ref _tempoImagemSegundos, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinImageDurationSeconds, 300)));
    }

    public bool PermitirFecharImagem
    {
        get => _permitirFecharImagem;
        set => SetField(ref _permitirFecharImagem, value);
    }

    public bool TocarSom { get => _tocarSom; set => SetField(ref _tocarSom, value); }
    public string TipoSom { get => _tipoSom; set => SetField(ref _tipoSom, value); }
    public string PosicaoAviso { get => _posicaoAviso; set => SetField(ref _posicaoAviso, value); }

    public string CorDestaque
    {
        get => _corDestaque;
        set => SetField(ref _corDestaque, value);
    }

    public double EscalaTexto
    {
        get => _escalaTexto;
        set => SetField(ref _escalaTexto, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinFontScalePercent, ProtocolConstants.MaxFontScalePercent)));
    }

    public double TempoAvisoSegundos
    {
        get => _tempoAvisoSegundos;
        set => SetField(ref _tempoAvisoSegundos, Math.Round(Math.Clamp(
            value, ProtocolConstants.MinToastDurationSeconds, ProtocolConstants.MaxToastDurationSeconds)));
    }

    public string? StatusOperacao
    {
        get => _statusOperacao;
        set => SetField(ref _statusOperacao, value);
    }

    public ICommand EnviarCommand { get; }
    public ICommand PrevisualizarCommand { get; }
    public ICommand AplicarGrupoCommand { get; }
    public ICommand SalvarGrupoCommand { get; }
    public ICommand RemoverGrupoCommand { get; }
    public ICommand AplicarModeloCommand { get; }
    public ICommand SalvarModeloCommand { get; }
    public ICommand RemoverModeloCommand { get; }
    public ICommand AtualizarDestinatariosCommand { get; }
    public ICommand SelecionarImagemCommand { get; }
    public ICommand AbrirCarrosselCommand { get; }
    public ICommand SelecionarVideoCommand { get; }
    public ICommand SelecionarAudioCommand { get; }
    public ICommand RemoverImagemCommand { get; }
    public ICommand UsarImagemCarregadaNoMonitorCommand { get; }
    public ICommand AdicionarImagemMonitorCommand { get; }
    public ICommand AdicionarVideoMonitorCommand { get; }
    public ICommand RemoverImagemMonitorCommand { get; }

    public MensagensViewModel(
        ComputadoresViewModel computadores, EnviadorNotificacoes enviador,
        HistoricoRepository historico, CloudSyncService cloud, ReenvioRepository reenvios)
    {
        _computadores = computadores;
        _enviador = enviador;
        _historico = historico;
        _cloud = cloud;
        _reenvios = reenvios;
        _cloud.StateChanged += () => UiDispatcher.Invoke(() =>
        {
            if (!_cloud.IsAdmin) { DefinirComoPapelDeParede = false; DefinirComoTelaDeBloqueio = false; }
            OnPropertyChanged(nameof(PodeAlterarPapelParede));
            OnPropertyChanged(nameof(PodeGerenciarCarrossel));
            OnPropertyChanged(nameof(PodeGerenciarBiblioteca));
            CommandManager.InvalidateRequerySuggested();
        });
        _cloud.GlobalConfigReceived += config => UiDispatcher.Invoke(() =>
        {
            if (!config.PermitirPapelParedeRemoto) { DefinirComoPapelDeParede = false; DefinirComoTelaDeBloqueio = false; }
            OnPropertyChanged(nameof(PodeAlterarPapelParede));
            AtualizarBiblioteca(config);
            OnPropertyChanged(nameof(PodeGerenciarBiblioteca));
            CommandManager.InvalidateRequerySuggested();
        });

        EnviarCommand = new AsyncRelayCommand(EnviarAsync, PodeEnviar);
        PrevisualizarCommand = new AsyncRelayCommand(PrevisualizarAsync,
            () => PodeEnviar() && !AlterarImagemSistema);
        AplicarGrupoCommand = new RelayCommand(_ => AplicarGrupo(), _ => GrupoSelecionado is not null);
        SalvarGrupoCommand = new AsyncRelayCommand(SalvarGrupoAsync,
            () => PodeGerenciarBiblioteca && Destinatarios.Any(d => d.Selecionado));
        RemoverGrupoCommand = new AsyncRelayCommand(RemoverGrupoAsync,
            () => PodeGerenciarBiblioteca && GrupoSelecionado is not null);
        AplicarModeloCommand = new RelayCommand(_ => AplicarModelo(), _ => ModeloSelecionado is not null);
        SalvarModeloCommand = new AsyncRelayCommand(SalvarModeloAsync,
            () => PodeGerenciarBiblioteca && !string.IsNullOrWhiteSpace(Titulo)
                && !string.IsNullOrWhiteSpace(Mensagem) && !ExibirImagemCentral
                && !AlterarImagemSistema);
        RemoverModeloCommand = new AsyncRelayCommand(RemoverModeloAsync,
            () => PodeGerenciarBiblioteca && ModeloSelecionado is not null);
        AtualizarDestinatariosCommand = new RelayCommand(_ => AtualizarDestinatarios());
        SelecionarImagemCommand = new RelayCommand(_ => SelecionarImagem());
        AbrirCarrosselCommand = new RelayCommand(_ =>
        {
            var window = new Views.CarrosselWindow(_enviador, Destinatarios,
                () => PodeGerenciarCarrossel, () => PodeGerenciarCarrossel)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            window.ShowDialog();
        }, _ => PodeGerenciarCarrossel);
        SelecionarVideoCommand = new RelayCommand(_ => SelecionarVideo());
        SelecionarAudioCommand = new RelayCommand(_ => SelecionarAudio());
        RemoverImagemCommand = new RelayCommand(_ => RemoverMidia(), _ => TemMidia);
        UsarImagemCarregadaNoMonitorCommand = new RelayCommand(param =>
        {
            if (param is DestinoMonitor destino)
            {
                UsarMidiaCarregadaNoMonitor(destino);
            }
        }, param => param is DestinoMonitor && (TemImagem || TemVideo));
        AdicionarImagemMonitorCommand = new RelayCommand(param =>
        {
            if (param is DestinoMonitor destino)
            {
                AdicionarImagensAoMonitor(destino);
            }
        });
        AdicionarVideoMonitorCommand = new RelayCommand(param =>
        {
            if (param is DestinoMonitor destino)
            {
                AdicionarVideoAoMonitor(destino);
            }
        });
        RemoverImagemMonitorCommand = new RelayCommand(param =>
        {
            if (param is MidiaMonitorEditavel midia)
            {
                foreach (var destino in MonitoresDestino)
                {
                    if (destino.Midias.Remove(midia))
                    {
                        GuardarMidias(destino);
                        break;
                    }
                }
                OnPropertyChanged(nameof(TemVideoConfigurado));
                CommandManager.InvalidateRequerySuggested();
            }
        });

        AdicionarBotaoCommand = new RelayCommand(_ => AdicionarBotao(), _ => PodeAdicionarBotao());
        RemoverBotaoCommand = new RelayCommand(param =>
        {
            if (param is BotaoRespostaEditavel botao)
            {
                Botoes.Remove(botao);
            }
        });

        _computadores.Computadores.CollectionChanged += (_, _) => AtualizarDestinatarios();
        AtualizarDestinatarios();
        if (_cloud.GlobalConfig is { } globalConfig) AtualizarBiblioteca(globalConfig);
    }

    private void AtualizarBiblioteca(ConfiguracaoGlobalPrograma config)
    {
        var grupoId = GrupoSelecionado?.Id;
        var modeloId = ModeloSelecionado?.Id;
        GruposComputadores.Clear();
        foreach (var grupo in config.GruposComputadores.OrderBy(g => g.Nome)) GruposComputadores.Add(grupo);
        ModelosMensagem.Clear();
        foreach (var modelo in config.ModelosMensagem.OrderBy(m => m.Nome)) ModelosMensagem.Add(modelo);
        GrupoSelecionado = GruposComputadores.FirstOrDefault(g => g.Id == grupoId);
        ModeloSelecionado = ModelosMensagem.FirstOrDefault(m => m.Id == modeloId);
        CommandManager.InvalidateRequerySuggested();
    }

    private void AplicarGrupo()
    {
        if (GrupoSelecionado is null) return;
        var ids = GrupoSelecionado.ComputadorIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var destino in Destinatarios) destino.Selecionado = ids.Contains(destino.Computador.Id);
        var selecionados = Destinatarios.Count(d => d.Selecionado);
        StatusOperacao = $"Grupo '{GrupoSelecionado.Nome}': {selecionados} computador(es) disponível(is) selecionado(s).";
    }

    private async Task SalvarGrupoAsync()
    {
        var nome = string.IsNullOrWhiteSpace(NovoGrupoNome) ? GrupoSelecionado?.Nome : NovoGrupoNome.Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 60)
        {
            StatusOperacao = "Informe um nome de grupo com até 60 caracteres.";
            return;
        }
        var ids = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).Distinct().ToList();
        if (ids.Count == 0) { StatusOperacao = "Selecione pelo menos um computador."; return; }
        var config = _cloud.GlobalConfig?.Clone();
        if (config is null) return;
        if (GrupoSelecionado is null && config.GruposComputadores.Count >= 30)
        { StatusOperacao = "Limite de 30 grupos compartilhados."; return; }
        var grupo = new GrupoComputadoresGlobal
        {
            Id = GrupoSelecionado?.Id ?? Guid.NewGuid().ToString("N"),
            Nome = nome,
            ComputadorIds = ids,
        };
        config.GruposComputadores.RemoveAll(g => g.Id == grupo.Id);
        config.GruposComputadores.Add(grupo);
        if (await SalvarBibliotecaAsync(config))
        {
            GrupoSelecionado = GruposComputadores.FirstOrDefault(g => g.Id == grupo.Id);
            NovoGrupoNome = string.Empty;
            StatusOperacao = $"Grupo '{nome}' sincronizado com os painéis.";
        }
    }

    private async Task RemoverGrupoAsync()
    {
        if (GrupoSelecionado is null || _cloud.GlobalConfig?.Clone() is not { } config) return;
        var nome = GrupoSelecionado.Nome;
        config.GruposComputadores.RemoveAll(g => g.Id == GrupoSelecionado.Id);
        if (await SalvarBibliotecaAsync(config)) StatusOperacao = $"Grupo '{nome}' removido dos painéis.";
    }

    private void AplicarModelo()
    {
        if (ModeloSelecionado is null) return;
        var modelo = ModeloSelecionado;
        Titulo = modelo.Titulo;
        Mensagem = modelo.Mensagem;
        PermitirResposta = modelo.PermitirResposta;
        ExibirImagemCentral = false;
        DefinirComoPapelDeParede = false;
        DefinirComoTelaDeBloqueio = false;
        ExibirMensagemCentral = modelo.ModoExibicao is ProtocolConstants.DisplayMode.CenterMessage
            or ProtocolConstants.DisplayMode.CenterAlert;
        ExibirAvisoObrigatorio = modelo.ConfirmacaoObrigatoria
            || modelo.ModoExibicao == ProtocolConstants.DisplayMode.CenterAlert;
        CorDestaque = modelo.Aparencia.AccentColor;
        EscalaTexto = modelo.Aparencia.FontScalePercent;
        TocarSom = modelo.Aparencia.PlaySound;
        TipoSom = modelo.Aparencia.SoundType;
        TempoAvisoSegundos = modelo.Aparencia.ToastDurationSeconds;
        PosicaoAviso = modelo.Aparencia.ToastPosition;
        Botoes.Clear();
        foreach (var botao in modelo.Botoes.Take(ProtocolConstants.MaxBotoes))
            Botoes.Add(new BotaoRespostaEditavel { Rotulo = botao.Label, Url = botao.Url });
        StatusOperacao = $"Modelo '{modelo.Nome}' carregado. Confira a prévia antes de enviar.";
    }

    private async Task SalvarModeloAsync()
    {
        var nome = string.IsNullOrWhiteSpace(NovoModeloNome) ? ModeloSelecionado?.Nome : NovoModeloNome.Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 60)
        { StatusOperacao = "Informe um nome de modelo com até 60 caracteres."; return; }
        if (string.IsNullOrWhiteSpace(Titulo) || string.IsNullOrWhiteSpace(Mensagem)
            || ExibirImagemCentral || AlterarImagemSistema)
        { StatusOperacao = "Modelos compartilhados guardam avisos de texto e botões."; return; }
        var config = _cloud.GlobalConfig?.Clone();
        if (config is null) return;
        if (ModeloSelecionado is null && config.ModelosMensagem.Count >= 50)
        { StatusOperacao = "Limite de 50 modelos compartilhados."; return; }
        var modelo = new ModeloMensagemGlobal
        {
            Id = ModeloSelecionado?.Id ?? Guid.NewGuid().ToString("N"),
            Nome = nome,
            Titulo = Titulo.Trim(), Mensagem = Mensagem.Trim(), PermitirResposta = PermitirResposta,
            ModoExibicao = ExibirMensagemCentral ? ProtocolConstants.DisplayMode.CenterMessage : ProtocolConstants.DisplayMode.Toast,
            ConfirmacaoObrigatoria = ExibirAvisoObrigatorio,
            Botoes = Botoes.Select(b => b.ParaProtocolo()).ToList(),
            Aparencia = new AparenciaNotificacao
            {
                AccentColor = CorDestaque.Trim(), FontScalePercent = (int)EscalaTexto,
                PlaySound = TocarSom, SoundType = TipoSom,
                ToastDurationSeconds = (int)TempoAvisoSegundos, ToastPosition = PosicaoAviso,
            },
        };
        config.ModelosMensagem.RemoveAll(m => m.Id == modelo.Id);
        config.ModelosMensagem.Add(modelo);
        if (await SalvarBibliotecaAsync(config))
        {
            ModeloSelecionado = ModelosMensagem.FirstOrDefault(m => m.Id == modelo.Id);
            NovoModeloNome = string.Empty;
            StatusOperacao = $"Modelo '{nome}' sincronizado com os painéis.";
        }
    }

    private async Task RemoverModeloAsync()
    {
        if (ModeloSelecionado is null || _cloud.GlobalConfig?.Clone() is not { } config) return;
        var nome = ModeloSelecionado.Nome;
        config.ModelosMensagem.RemoveAll(m => m.Id == ModeloSelecionado.Id);
        if (await SalvarBibliotecaAsync(config)) StatusOperacao = $"Modelo '{nome}' removido dos painéis.";
    }

    private async Task<bool> SalvarBibliotecaAsync(ConfiguracaoGlobalPrograma config)
    {
        try
        {
            await _cloud.SaveGlobalConfigAsync(config).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException
            or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível sincronizar: {ex.Message}";
            return false;
        }
    }

    private void AtualizarDestinatarios()
    {
        var idsSelecionados = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToHashSet();

        foreach (var destinatario in Destinatarios)
        {
            destinatario.PropertyChanged -= OnDestinatarioPropertyChanged;
        }

        foreach (var computador in _computadoresObservados)
        {
            computador.PropertyChanged -= OnComputadorPropertyChanged;
        }
        _computadoresObservados.Clear();

        Destinatarios.Clear();
        foreach (var computador in _computadores.Computadores)
        {
            if (_computadoresObservados.Add(computador))
            {
                computador.PropertyChanged += OnComputadorPropertyChanged;
            }

            if (!computador.Pareado)
            {
                continue;
            }

            var destinatario = new ComputadorSelecionavel(computador)
            {
                Selecionado = idsSelecionados.Contains(computador.Id),
            };
            destinatario.PropertyChanged += OnDestinatarioPropertyChanged;
            Destinatarios.Add(destinatario);
        }

        AtualizarMonitoresDestino();
    }

    private void OnDestinatarioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComputadorSelecionavel.Selecionado))
        {
            return;
        }

        AtualizarMonitoresDestino();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnComputadorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Computador.Pareado)
            or nameof(Computador.Monitores)
            or nameof(Computador.Nome)
            or nameof(Computador.Apelido))
        {
            AtualizarDestinatarios();
        }
    }

    private void AtualizarMonitoresDestino()
    {
        foreach (var destino in MonitoresDestino)
        {
            GuardarMidias(destino);
        }
        MonitoresDestino.Clear();

        foreach (var computador in Destinatarios
                     .Where(d => d.Selecionado)
                     .Select(d => d.Computador)
                     .DistinctBy(c => c.Id))
        {
            var monitores = computador.Monitores.Count > 0
                ? computador.Monitores
                : new List<MonitorInfo> { new() { Index = 0, Name = "Monitor principal", Primary = true } };

            foreach (var monitor in monitores.OrderBy(m => m.Index))
            {
                var destino = new DestinoMonitor { Computador = computador, Monitor = monitor };
                if (_midiasPorMonitor.TryGetValue(destino.Chave, out var midias))
                {
                    foreach (var midia in midias)
                    {
                        destino.Midias.Add(midia);
                    }
                }
                MonitoresDestino.Add(destino);
            }
        }
    }

    private void GuardarMidias(DestinoMonitor destino) =>
        _midiasPorMonitor[destino.Chave] = destino.Midias.ToList();

    private bool PodeAdicionarBotao()
    {
        if (string.IsNullOrWhiteSpace(NovoBotaoRotulo) || Botoes.Count >= ProtocolConstants.MaxBotoes)
        {
            return false;
        }

        // URL é opcional, mas se preenchida precisa ser http/https
        return string.IsNullOrWhiteSpace(NovoBotaoUrl) || BotaoResposta.UrlPermitida(NovoBotaoUrl.Trim());
    }

    private void AdicionarBotao()
    {
        Botoes.Add(new BotaoRespostaEditavel
        {
            Rotulo = NovoBotaoRotulo.Trim(),
            Url = string.IsNullOrWhiteSpace(NovoBotaoUrl) ? null : NovoBotaoUrl.Trim(),
        });

        NovoBotaoRotulo = string.Empty;
        NovoBotaoUrl = string.Empty;
    }

    private bool PodeEnviar()
    {
        var temConteudo = AlterarImagemSistema
            ? PodeAlterarPapelParede && ImagemCompativelPapelParede
            : ExibirImagemCentral
            ? TemMidia || DestinatariosComMidiaEspecifica()
            : !string.IsNullOrWhiteSpace(Titulo) && !string.IsNullOrWhiteSpace(Mensagem);
        return temConteudo && Destinatarios.Any(d => d.Selecionado);
    }

    private bool DestinatariosComMidiaEspecifica()
    {
        var selecionados = Destinatarios.Where(d => d.Selecionado).Select(d => d.Computador.Id).ToList();
        return selecionados.Count > 0
            && selecionados.All(id => MonitoresDestino.Any(m => m.Computador.Id == id && m.Midias.Count > 0));
    }

    private void AdicionarImagensAoMonitor(DestinoMonitor destino)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Escolher imagens para {destino.Titulo}",
            Filter = "Imagens permitidas|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|GIF|*.gif|Bitmap|*.bmp",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var caminho in dialog.FileNames)
        {
            try
            {
                var arquivo = new FileInfo(caminho);
                var mime = MimeImagemPelaExtensao(caminho);
                var limiteBytes = ProtocolConstants.MaxImageBytesForMime(mime);
                if (arquivo.Length <= 0 || arquivo.Length > limiteBytes)
                {
                    StatusOperacao = $"{arquivo.Name}: o limite é {limiteBytes / 1024 / 1024} MB para este formato.";
                    continue;
                }

                var dados = File.ReadAllBytes(caminho);
                if (!ConteudoImagem.MimePermitido(mime) || !ConteudoImagem.AssinaturaCorresponde(mime, dados))
                {
                    StatusOperacao = $"{arquivo.Name}: formato inválido.";
                    continue;
                }

                if (!PodeAdicionarMidia(destino, TipoMidiaMonitor.Imagem, dados.Length))
                {
                    break;
                }

                AdicionarMidia(destino, new MidiaMonitorEditavel
                {
                    Caminho = caminho,
                    Nome = arquivo.Name,
                    MimeType = mime,
                    Dados = dados,
                    Tipo = TipoMidiaMonitor.Imagem,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusOperacao = $"Não foi possível abrir a imagem: {ex.Message}";
            }
        }

        ExibirImagemCentral = true;
        CommandManager.InvalidateRequerySuggested();
    }

    private void UsarMidiaCarregadaNoMonitor(DestinoMonitor destino)
    {
        var ehVideo = TemVideo;
        var dados = ehVideo ? _dadosVideo : _dadosImagem;
        var mime = ehVideo ? _mimeVideo : _mimeImagem;
        var nome = ehVideo ? _nomeVideo : NomeImagem;
        var caminho = ehVideo ? _caminhoVideo : CaminhoImagem;
        var tipo = ehVideo ? TipoMidiaMonitor.Video : TipoMidiaMonitor.Imagem;

        if (dados is not { Length: > 0 } || string.IsNullOrWhiteSpace(mime) || string.IsNullOrWhiteSpace(nome))
        {
            StatusOperacao = "Carregue uma imagem ou um vídeo antes de escolher o monitor.";
            return;
        }

        if (destino.Midias.Any(i =>
                i.Tipo == tipo
                && string.Equals(i.MimeType, mime, StringComparison.OrdinalIgnoreCase)
                && i.Dados.SequenceEqual(dados)))
        {
            StatusOperacao = $"{nome} já está no {destino.Titulo}.";
            return;
        }

        if (!PodeAdicionarMidia(destino, tipo, dados.Length))
        {
            return;
        }

        AdicionarMidia(destino, new MidiaMonitorEditavel
        {
            Caminho = caminho ?? string.Empty,
            Nome = nome,
            MimeType = mime,
            Dados = dados.ToArray(),
            Tipo = tipo,
        });
        ExibirImagemCentral = true;
        CommandManager.InvalidateRequerySuggested();
    }

    private bool PodeAdicionarMidia(DestinoMonitor destino, TipoMidiaMonitor tipo, int tamanhoBytes)
    {
        var midiasDoComputador = MonitoresDestino
            .Where(m => m.Computador.Id == destino.Computador.Id)
            .SelectMany(m => m.Midias)
            .ToList();
        var totalBytes = midiasDoComputador.Sum(i => (long)i.Dados.Length);
        var quantidadeTipo = midiasDoComputador.Count(i => i.Tipo == tipo);
        var limiteQuantidade = tipo == TipoMidiaMonitor.Video
            ? ProtocolConstants.MaxScreenVideos
            : ProtocolConstants.MaxScreenImages;
        var limiteBytes = tipo == TipoMidiaMonitor.Video
            ? ProtocolConstants.MaxTotalMediaBytes
            : ProtocolConstants.MaxTotalImageBytes;
        if (quantidadeTipo >= limiteQuantidade || totalBytes + tamanhoBytes > limiteBytes)
        {
            StatusOperacao = tipo == TipoMidiaMonitor.Video
                ? "Limite por computador: 4 vídeos e 40 MB no total."
                : "Limite por computador: 12 imagens e 16 MB no total.";
            return false;
        }

        return true;
    }

    private void AdicionarMidia(DestinoMonitor destino, MidiaMonitorEditavel midia)
    {
        RemoverMidiasDeOutroTipo(destino.Computador.Id, midia.Tipo);
        if (midia.EhVideo)
        {
            foreach (var existente in destino.Midias.Where(i => i.EhVideo).ToList())
            {
                destino.Midias.Remove(existente);
            }
        }
        destino.Midias.Add(midia);
        GuardarMidias(destino);
        OnPropertyChanged(nameof(TemVideoConfigurado));
        StatusOperacao = $"{midia.Nome} adicionada ao {destino.Titulo}.";
    }

    private void RemoverMidiasDeOutroTipo(string computadorId, TipoMidiaMonitor tipo)
    {
        foreach (var destino in MonitoresDestino.Where(m => m.Computador.Id == computadorId))
        {
            foreach (var item in destino.Midias.Where(i => i.Tipo != tipo).ToList())
            {
                destino.Midias.Remove(item);
            }
            GuardarMidias(destino);
        }
    }

    private static string MimeImagemPelaExtensao(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => string.Empty,
    };

    private static string MimeVideoPelaExtensao(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".wmv" => "video/x-ms-wmv",
        _ => string.Empty,
    };

    private static string MimeAudioPelaExtensao(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        _ => string.Empty,
    };

    private List<ImagemMonitor> CriarImagensPorMonitor(string computadorId) => MonitoresDestino
        .Where(m => m.Computador.Id == computadorId)
        .SelectMany(m => m.Midias.Where(i => i.EhImagem).Select(i => i.ParaProtocolo(m.Monitor.Index)))
        .ToList();

    private List<VideoMonitor> CriarVideosPorMonitor(string computadorId) => MonitoresDestino
        .Where(m => m.Computador.Id == computadorId)
        .SelectMany(m => m.Midias.Where(i => i.EhVideo).Select(i => i.ParaVideoProtocolo(m.Monitor.Index)))
        .ToList();

    private void SelecionarImagem()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem para o aviso",
            Filter = "Imagens permitidas|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|GIF|*.gif|Bitmap|*.bmp",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var arquivo = new FileInfo(dialog.FileName);
            var mime = MimeImagemPelaExtensao(dialog.FileName);
            var limiteBytes = ProtocolConstants.MaxImageBytesForMime(mime);
            if (arquivo.Length <= 0 || arquivo.Length > limiteBytes)
            {
                StatusOperacao = $"O limite para este formato é {limiteBytes / 1024 / 1024} MB.";
                return;
            }

            var dados = File.ReadAllBytes(dialog.FileName);
            if (!ConteudoImagem.MimePermitido(mime) || !ConteudoImagem.AssinaturaCorresponde(mime, dados))
            {
                StatusOperacao = "Arquivo inválido. Escolha uma imagem PNG, JPEG, GIF ou BMP.";
                return;
            }

            LimparMidiaGeral();
            _dadosImagem = dados;
            _mimeImagem = mime;
            _caminhoImagem = dialog.FileName;
            _nomeImagem = arquivo.Name;
            if (!ImagemCompativelPapelParede)
            {
                DefinirComoPapelDeParede = false;
                DefinirComoTelaDeBloqueio = false;
            }
            if (!AlterarImagemSistema)
                ExibirImagemCentral = true;
            var tipoSelecionado = mime == "image/gif" ? "GIF" : "Imagem";
            StatusOperacao = $"{tipoSelecionado} selecionado: {arquivo.Name} ({arquivo.Length / 1024d:0.#} KB).";
            NotificarMidiaAlterada();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível abrir a imagem: {ex.Message}";
        }
    }

    private void SelecionarVideo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher vídeo para reproduzir no centro da tela",
            Filter = "Vídeos permitidos|*.mp4;*.wmv|MP4|*.mp4|Windows Media Video|*.wmv",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var arquivo = new FileInfo(dialog.FileName);
            if (arquivo.Length <= 0 || arquivo.Length > ProtocolConstants.MaxVideoBytes)
            {
                StatusOperacao = "O vídeo precisa ter no máximo 24 MB.";
                return;
            }
            var mime = MimeVideoPelaExtensao(dialog.FileName);
            var dados = File.ReadAllBytes(dialog.FileName);
            if (!ConteudoVideo.MimePermitido(mime) || !ConteudoVideo.AssinaturaCorresponde(mime, dados))
            {
                StatusOperacao = "Arquivo inválido. Escolha um vídeo MP4 ou WMV.";
                return;
            }

            LimparMidiaGeral();
            _dadosVideo = dados;
            _mimeVideo = mime;
            _caminhoVideo = dialog.FileName;
            _nomeVideo = arquivo.Name;
            ExibirImagemCentral = true;
            StatusOperacao = $"Vídeo selecionado: {arquivo.Name} ({arquivo.Length / 1024d / 1024d:0.#} MB).";
            NotificarMidiaAlterada();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível abrir o vídeo: {ex.Message}";
        }
    }

    private void SelecionarAudio()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher áudio para tocar em segundo plano",
            Filter = "Áudios permitidos|*.mp3;*.wav|MP3|*.mp3|WAV|*.wav",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var arquivo = new FileInfo(dialog.FileName);
            if (arquivo.Length <= 0 || arquivo.Length > ProtocolConstants.MaxAudioBytes)
            {
                StatusOperacao = "O áudio precisa ter no máximo 12 MB.";
                return;
            }
            var mime = MimeAudioPelaExtensao(dialog.FileName);
            var dados = File.ReadAllBytes(dialog.FileName);
            if (!ConteudoAudio.MimePermitido(mime) || !ConteudoAudio.AssinaturaCorresponde(mime, dados))
            {
                StatusOperacao = "Arquivo inválido. Escolha um áudio MP3 ou WAV.";
                return;
            }

            LimparMidiaGeral();
            _dadosAudio = dados;
            _mimeAudio = mime;
            _caminhoAudio = dialog.FileName;
            _nomeAudio = arquivo.Name;
            ExibirImagemCentral = true;
            StatusOperacao = $"Áudio selecionado: {arquivo.Name} ({arquivo.Length / 1024d / 1024d:0.#} MB).";
            NotificarMidiaAlterada();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível abrir o áudio: {ex.Message}";
        }
    }

    private void AdicionarVideoAoMonitor(DestinoMonitor destino)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Escolher vídeo para {destino.Titulo}",
            Filter = "Vídeos permitidos|*.mp4;*.wmv|MP4|*.mp4|Windows Media Video|*.wmv",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var arquivo = new FileInfo(dialog.FileName);
            if (arquivo.Length <= 0 || arquivo.Length > ProtocolConstants.MaxVideoBytes)
            {
                StatusOperacao = "O vídeo precisa ter no máximo 24 MB.";
                return;
            }
            var mime = MimeVideoPelaExtensao(dialog.FileName);
            var dados = File.ReadAllBytes(dialog.FileName);
            if (!ConteudoVideo.MimePermitido(mime) || !ConteudoVideo.AssinaturaCorresponde(mime, dados))
            {
                StatusOperacao = "Arquivo inválido. Escolha um vídeo MP4 ou WMV.";
                return;
            }
            if (!PodeAdicionarMidia(destino, TipoMidiaMonitor.Video, dados.Length))
            {
                return;
            }
            AdicionarMidia(destino, new MidiaMonitorEditavel
            {
                Caminho = dialog.FileName,
                Nome = arquivo.Name,
                MimeType = mime,
                Dados = dados,
                Tipo = TipoMidiaMonitor.Video,
            });
            ExibirImagemCentral = true;
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusOperacao = $"Não foi possível abrir o vídeo: {ex.Message}";
        }
    }

    private void LimparMidiaGeral()
    {
        _dadosImagem = null;
        _mimeImagem = null;
        _caminhoImagem = null;
        _nomeImagem = null;
        _dadosVideo = null;
        _mimeVideo = null;
        _caminhoVideo = null;
        _nomeVideo = null;
        _dadosAudio = null;
        _mimeAudio = null;
        _caminhoAudio = null;
        _nomeAudio = null;
        _repetirVideo = false;
        _repetirAudio = false;
        _limitarDuracaoAudio = false;
    }

    private void RemoverMidia()
    {
        LimparMidiaGeral();
        DefinirComoPapelDeParede = false;
        DefinirComoTelaDeBloqueio = false;
        ExibirImagemCentral = MonitoresDestino.Any(m => m.Midias.Count > 0);
        StatusOperacao = "Mídia carregada removida.";
        NotificarMidiaAlterada();
    }

    private void NotificarMidiaAlterada()
    {
        OnPropertyChanged(nameof(CaminhoImagem));
        OnPropertyChanged(nameof(NomeImagem));
        OnPropertyChanged(nameof(MimeImagem));
        OnPropertyChanged(nameof(ImagemPreviewBase64));
        OnPropertyChanged(nameof(TemImagem));
        OnPropertyChanged(nameof(ImagemCompativelPapelParede));
        OnPropertyChanged(nameof(TemVideo));
        OnPropertyChanged(nameof(TemAudio));
        OnPropertyChanged(nameof(TemMidia));
        OnPropertyChanged(nameof(TemVideoConfigurado));
        OnPropertyChanged(nameof(PodeConfigurarMonitores));
        OnPropertyChanged(nameof(CaminhoMidia));
        OnPropertyChanged(nameof(NomeMidia));
        OnPropertyChanged(nameof(TipoMidiaTexto));
        OnPropertyChanged(nameof(RepetirVideo));
        OnPropertyChanged(nameof(RepetirAudio));
        OnPropertyChanged(nameof(LimitarDuracaoAudio));
        OnPropertyChanged(nameof(DescricaoFormato));
        CommandManager.InvalidateRequerySuggested();
    }

    private ConteudoImagem? CriarConteudoImagem()
    {
        if ((!ExibirImagemCentral && !AlterarImagemSistema)
            || _dadosImagem is not { Length: > 0 } || _mimeImagem is null || NomeImagem is null)
        {
            return null;
        }

        return new ConteudoImagem
        {
            Name = NomeImagem,
            MimeType = _mimeImagem,
            DataBase64 = Convert.ToBase64String(_dadosImagem),
        };
    }

    private ConteudoVideo? CriarConteudoVideo()
    {
        if (!ExibirImagemCentral || _dadosVideo is not { Length: > 0 }
            || _mimeVideo is null || _nomeVideo is null)
        {
            return null;
        }
        return new ConteudoVideo
        {
            Name = _nomeVideo,
            MimeType = _mimeVideo,
            DataBase64 = Convert.ToBase64String(_dadosVideo),
        };
    }

    private ConteudoAudio? CriarConteudoAudio()
    {
        if (!ExibirImagemCentral || _dadosAudio is not { Length: > 0 }
            || _mimeAudio is null || _nomeAudio is null)
        {
            return null;
        }
        return new ConteudoAudio
        {
            Name = _nomeAudio,
            MimeType = _mimeAudio,
            DataBase64 = Convert.ToBase64String(_dadosAudio),
        };
    }

    private async Task PrevisualizarAsync()
    {
        if (AlterarImagemSistema)
        {
            StatusOperacao = "Imagem do sistema não abre prévia em tela cheia. Confira a miniatura antes de enviar.";
            return;
        }
        var computador = Destinatarios.FirstOrDefault(d => d.Selecionado)?.Computador;
        if (computador is null)
        {
            StatusOperacao = "Selecione um computador para testar o aviso antes do envio.";
            return;
        }
        var imagens = CriarImagensPorMonitor(computador.Id);
        var videos = CriarVideosPorMonitor(computador.Id);
        var imagem = CriarConteudoImagem();
        var video = CriarConteudoVideo();
        var audio = CriarConteudoAudio();
        var modo = !ExibirImagemCentral ? ExibirMensagemCentral ? ProtocolConstants.DisplayMode.CenterMessage : ProtocolConstants.DisplayMode.Toast
            : audio is not null ? ProtocolConstants.DisplayMode.Audio
            : videos.Count > 0 || video is not null ? ProtocolConstants.DisplayMode.CenterVideo
            : ProtocolConstants.DisplayMode.CenterImage;
        var permiteInteracao = !ExibirImagemCentral && !AlterarImagemSistema;
        var aparencia = new AparenciaNotificacao
        {
            AccentColor = CorDestaque.Trim(),
            FontScalePercent = (int)EscalaTexto,
            PlaySound = TocarSom && !TemAudio,
            SoundType = TipoSom,
            ToastDurationSeconds = (int)TempoAvisoSegundos,
            ToastPosition = PosicaoAviso,
        };
        StatusOperacao = $"Mostrando prévia de {computador.NomeExibicao} neste computador…";
        await Views.NotificacaoRecebidaWindow.MostrarAsync(
            $"Prévia · {computador.NomeExibicao}", Titulo, Mensagem,
            allowReply: PermitirResposta && permiteInteracao,
            botoes: permiteInteracao ? Botoes.Select(b => b.ParaProtocolo()).ToList() : [],
            modoExibicao: modo,
            imagem: imagens.Count > 0 ? null : imagem,
            imagensPorMonitor: imagens,
            duracaoImagemSegundos: (int)TempoImagemSegundos,
            permitirFecharManualmente: true,
            aparencia: aparencia,
            video: videos.Count > 0 ? null : video,
            videosPorMonitor: videos,
            repetirVideo: RepetirVideo,
            audio: audio,
            repetirAudio: RepetirAudio,
            previewMode: true,
            confirmationRequired: ExibirAvisoObrigatorio).ConfigureAwait(true);
        StatusOperacao = "Prévia encerrada. Nenhuma mensagem foi enviada.";
    }

    private async Task EnviarAsync()
    {
        var selecionados = Destinatarios.Where(d => d.Selecionado).ToList();
        // Mídia central é conteúdo puro: não exige título, mensagem nem interação.
        var permiteInteracao = !ExibirImagemCentral && !AlterarImagemSistema;
        var botoesProtocolo = permiteInteracao
            ? Botoes.Select(b => b.ParaProtocolo()).ToList()
            : new List<BotaoResposta>();
        var imagem = CriarConteudoImagem();
        var video = CriarConteudoVideo();
        var audio = CriarConteudoAudio();
        var permitirRespostaEfetiva = PermitirResposta && permiteInteracao;
        var aparencia = new AparenciaNotificacao
        {
            AccentColor = CorDestaque.Trim(),
            FontScalePercent = (int)EscalaTexto,
            PlaySound = TocarSom && !TemAudio,
            SoundType = TipoSom,
            ToastDurationSeconds = (int)TempoAvisoSegundos,
            ToastPosition = PosicaoAviso,
        };
        StatusOperacao = $"Enviando para {selecionados.Count} computador(es)...";

        var enviados = 0;
        var erros = new List<string>();
        var enviosPendentes = new List<Task>();

        foreach (var destino in selecionados)
        {
            var computador = destino.Computador;
            var imagensPorMonitor = CriarImagensPorMonitor(computador.Id);
            var videosPorMonitor = CriarVideosPorMonitor(computador.Id);
            var modoExibicao = DefinirComoPapelDeParede
                ? ProtocolConstants.DisplayMode.Wallpaper
                : DefinirComoTelaDeBloqueio
                ? ProtocolConstants.DisplayMode.LockScreen
                : !ExibirImagemCentral
                ? ExibirMensagemCentral
                        ? ProtocolConstants.DisplayMode.CenterMessage
                        : ProtocolConstants.DisplayMode.Toast
                : audio is not null
                    ? ProtocolConstants.DisplayMode.Audio
                    : videosPorMonitor.Count > 0 || video is not null
                        ? ProtocolConstants.DisplayMode.CenterVideo
                        : ProtocolConstants.DisplayMode.CenterImage;
            if (AlterarImagemSistema)
            {
                imagensPorMonitor.Clear();
                videosPorMonitor.Clear();
            }
            var imagemParaEsteComputador = imagensPorMonitor.Count > 0 ? null : imagem;
            var videoParaEsteComputador = videosPorMonitor.Count > 0 ? null : video;
            var duracaoMidia = modoExibicao switch
            {
                ProtocolConstants.DisplayMode.CenterImage => (int?)TempoImagemSegundos,
                ProtocolConstants.DisplayMode.CenterVideo when RepetirVideo => (int?)TempoImagemSegundos,
                ProtocolConstants.DisplayMode.Audio when LimitarDuracaoAudio || RepetirAudio => (int?)TempoImagemSegundos,
                _ => null,
            };
            var permitirFechar = modoExibicao is ProtocolConstants.DisplayMode.CenterImage
                or ProtocolConstants.DisplayMode.CenterVideo
                ? PermitirFecharImagem
                : (bool?)null;
            var nomeTipo = modoExibicao switch
            {
                ProtocolConstants.DisplayMode.CenterVideo => "Vídeo",
                ProtocolConstants.DisplayMode.Audio => "Áudio",
                ProtocolConstants.DisplayMode.CenterImage => "Imagem",
                ProtocolConstants.DisplayMode.Wallpaper => "Papel de parede",
                ProtocolConstants.DisplayMode.LockScreen => "Tela de bloqueio",
                _ => "Mensagem",
            };
            var tituloHistorico = AlterarImagemSistema
                ? nomeTipo
                : ExibirImagemCentral && string.IsNullOrWhiteSpace(Titulo)
                ? nomeTipo
                : Titulo;
            var mensagemHistorico = AlterarImagemSistema
                ? NomeImagem ?? "Imagem aplicada"
                : ExibirImagemCentral && string.IsNullOrWhiteSpace(Mensagem)
                ? NomeMidia ?? $"{nomeTipo} enviado"
                : Mensagem;
            var entry = new HistoricoEntry
            {
                ComputadorId = computador.Id,
                ComputadorNome = computador.Nome,
                Titulo = tituloHistorico,
                Mensagem = mensagemHistorico,
                Status = StatusEnvio.Enviando,
            };
            _historico.Adicionar(entry);

            var envio = new EnvioPendente
            {
                Titulo = AlterarImagemSistema ? string.Empty : Titulo,
                Mensagem = AlterarImagemSistema ? string.Empty : Mensagem,
                PermitirResposta = permitirRespostaEfetiva,
                ConfirmacaoObrigatoria = ExibirAvisoObrigatorio,
                Botoes = botoesProtocolo,
                ModoExibicao = modoExibicao,
                Imagem = imagemParaEsteComputador,
                ImagensPorMonitor = imagensPorMonitor,
                Video = videoParaEsteComputador,
                VideosPorMonitor = videosPorMonitor,
                Audio = modoExibicao == ProtocolConstants.DisplayMode.Audio ? audio : null,
                DuracaoSegundos = duracaoMidia,
                PermitirFecharManualmente = permitirFechar,
                RepetirVideo = modoExibicao == ProtocolConstants.DisplayMode.CenterVideo ? RepetirVideo : null,
                RepetirAudio = modoExibicao == ProtocolConstants.DisplayMode.Audio ? RepetirAudio : null,
                Aparencia = aparencia,
            };
            // Dispara cada destinatário antes de esperar respostas. Um aviso
            // obrigatório aberto em um PC não atrasa a entrega aos demais.
            enviosPendentes.Add(ProcessarEnvioAsync(computador, entry, envio,
                permitirRespostaEfetiva, modoExibicao));
        }

        await Task.WhenAll(enviosPendentes).ConfigureAwait(true);

        async Task ProcessarEnvioAsync(Computador computador, HistoricoEntry entry,
            EnvioPendente envio, bool permitirResposta, string modoExibicao)
        {
            var resultado = await envio.EnviarAsync(_enviador, computador).ConfigureAwait(true);
            _historico.AtualizarExistente(
                entry.Id, item => EnvioPendente.AtualizarHistorico(item, resultado,
                    permitirResposta || envio.ConfirmacaoObrigatoria));
            if (resultado.GotReply)
            {
                _ = Views.NotificacaoRecebidaWindow.MostrarAsync(
                    computador.NomeExibicao, "Resposta recebida",
                    resultado.ReplyText ?? "O usuário confirmou o recebimento.", allowReply: false);
            }
            if (resultado.Delivered)
            {
                enviados++;
                return;
            }

            try { _reenvios.Salvar(entry.Id, envio); CommandManager.InvalidateRequerySuggested(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Error($"Não foi possível guardar o envio para repetir: {ex.Message}", "envio");
            }
            var detalhe = resultado.ErrorMessage ?? "falha sem detalhe";
            erros.Add($"{computador.Nome}: {detalhe}");
            Logger.Error(
                $"Falha ao enviar mensagem para {computador.NomeExibicao} ({computador.EnderecoIp}:{computador.PortaTcp}).",
                "envio",
                $"Título: {envio.Titulo} | Modo: {modoExibicao} | Erro: {detalhe}");
        }

        var nomeConteudo = DefinirComoPapelDeParede ? "Papel de parede"
            : DefinirComoTelaDeBloqueio ? "Tela de bloqueio"
            : ExibirImagemCentral ? TipoMidiaTexto : "Mensagem";
        StatusOperacao = erros.Count == 0
            ? AlterarImagemSistema
                ? $"{nomeConteudo} aplicada em {enviados} computador(es), sem abrir a imagem na tela."
                : $"{nomeConteudo} exibida em {enviados} computador(es)."
            : $"Concluído em {enviados}; falhou em {erros.Count}. {string.Join(" | ", erros)}";
        Titulo = string.Empty;
        Mensagem = string.Empty;
    }

}
