using System.Collections.ObjectModel;
using System.Windows.Input;
using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Services;
using Comunicador.Storage;

namespace Comunicador.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DiscoveryService _discovery;
    private readonly StatusMonitorService _statusMonitor;
    private readonly LembreteSchedulerService _scheduler;
    private readonly HistoricoRepository _historicoRepositorio;
    private readonly EmbeddedReceptorServer _embeddedReceptorServer;
    private readonly RegistroConexoesReversas _conexoesReversas;
    private readonly LogRepository _logsRepositorio;
    private readonly SyncCoordinatorService _sync;

    private object _secaoAtual;
    private double? _pingMedioMs;

    public AppSettings Settings { get; }
    public ComputadoresViewModel Computadores { get; }
    public MensagensViewModel Mensagens { get; }
    public LembretesViewModel Lembretes { get; }
    public HistoricoViewModel Historico { get; }
    public LogsViewModel Logs { get; }
    public ConfiguracoesViewModel Configuracoes { get; }

    public object SecaoAtual
    {
        get => _secaoAtual;
        set => SetField(ref _secaoAtual, value);
    }

    private string _secaoAtiva = "computadores";

    /// <summary>Chave da secao aberta, usada pela barra lateral para realcar o item.</summary>
    public string SecaoAtiva
    {
        get => _secaoAtiva;
        private set
        {
            if (SetField(ref _secaoAtiva, value))
            {
                OnPropertyChanged(nameof(IndiceSecaoAtiva));
            }
        }
    }

    public int IndiceSecaoAtiva => SecaoAtiva switch
    {
        "computadores" => 0,
        "mensagens" => 1,
        "lembretes" => 2,
        "historico" => 3,
        "logs" => 4,
        "configuracoes" => 5,
        _ => 0,
    };

    public string PingTexto => _pingMedioMs is null
        ? "sem comunicação"
        : double.IsNaN(_pingMedioMs.Value)
            ? "conectado"
        : $"{_pingMedioMs.Value:0} ms em média";

    public string PingNivel => _pingMedioMs switch
    {
        null => "offline",
        double value when double.IsNaN(value) => "excelente",
        <= 50 => "excelente",
        <= 150 => "medio",
        _ => "alto",
    };

    public ICommand NavegarCommand { get; }

    public MainViewModel()
    {
        Settings = SettingsStore.Load();
        ThemeService.Apply(Settings, animate: false);

        var computadoresStore = new JsonStore<Computador>(AppPaths.ComputadoresFile);
        var lembretesStore = new JsonStore<Lembrete>(AppPaths.LembretesFile);
        var historicoStore = new JsonStore<HistoricoEntry>(AppPaths.HistoricoFile);
        var logsStore = new JsonStore<LogEntry>(AppPaths.LogsFile);
        var paineisPareadosStore = new JsonStore<PainelPareado>(AppPaths.PaineisPareadosFile);

        var client = new ReceptorClient(Settings.PainelId, Settings.NomePainel);
        _discovery = new DiscoveryService(Settings);
        _historicoRepositorio = new HistoricoRepository(historicoStore);
        _logsRepositorio = new LogRepository(logsStore);
        Logger.Configure(_logsRepositorio, Settings.PainelId, Settings.NomePainel);
        _conexoesReversas = new RegistroConexoesReversas();
        var enviador = new EnviadorNotificacoes(client, _conexoesReversas, Settings);
        var atualizador = new AtualizadorReceptor(client, _conexoesReversas);

        Computadores = new ComputadoresViewModel(
            computadoresStore, _discovery, client, atualizador, Settings);
        _sync = new SyncCoordinatorService(
            Computadores.Snapshot, client, _conexoesReversas, _historicoRepositorio, _logsRepositorio);
        Historico = new HistoricoViewModel(_historicoRepositorio);
        Logs = new LogsViewModel(_logsRepositorio, _sync);
        Mensagens = new MensagensViewModel(Computadores, enviador, _historicoRepositorio);

        _statusMonitor = new StatusMonitorService(Computadores.Snapshot, enviador, Settings);
        _statusMonitor.StatusAtualizado += Computadores.AtualizarStatus;
        Computadores.PingMedioAtualizado += AtualizarPingMedio;

        _scheduler = new LembreteSchedulerService(
            () => LembretesSnapshot(),
            id => Computadores.Computadores.FirstOrDefault(c => c.Id == id),
            enviador);

        Lembretes = new LembretesViewModel(lembretesStore, Computadores, _historicoRepositorio, _scheduler);

        var paineisPareados = new ObservableCollection<PainelPareado>(paineisPareadosStore.Load());
        _embeddedReceptorServer = new EmbeddedReceptorServer(
            Settings, paineisPareados, paineisPareadosStore, _historicoRepositorio,
            _logsRepositorio, _conexoesReversas);
        _embeddedReceptorServer.ReceptorRegistrado += Computadores.RegistrarViaConexaoReversa;
        Configuracoes = new ConfiguracoesViewModel(Settings, paineisPareados, paineisPareadosStore, _embeddedReceptorServer);

        _secaoAtual = Computadores;

        NavegarCommand = new RelayCommand(param =>
        {
            SecaoAtual = param switch
            {
                "computadores" => Computadores,
                "mensagens" => Mensagens,
                "lembretes" => Lembretes,
                "historico" => Historico,
                "logs" => Logs,
                "configuracoes" => Configuracoes,
                _ => SecaoAtual,
            };

            if (param is string chave)
            {
                SecaoAtiva = chave;
            }
        });
    }

    private IReadOnlyList<Lembrete> LembretesSnapshot()
    {
        IReadOnlyList<Lembrete> resultado = Array.Empty<Lembrete>();
        UiDispatcher.Invoke(() => resultado = Lembretes.Snapshot());
        return resultado;
    }

    private void AtualizarPingMedio(double? pingMs)
    {
        _pingMedioMs = pingMs;
        OnPropertyChanged(nameof(PingTexto));
        OnPropertyChanged(nameof(PingNivel));
    }

    public void Start()
    {
        _discovery.Start();
        _statusMonitor.Start();
        _scheduler.Start();
        _embeddedReceptorServer.AtualizarDisponibilidade();
        _sync.Start();
        Configuracoes.AtualizarStatusReceptor();
    }

    public void Dispose()
    {
        Computadores.PingMedioAtualizado -= AtualizarPingMedio;
        _discovery.Dispose();
        _statusMonitor.Dispose();
        _scheduler.Dispose();
        _sync.Dispose();
        _embeddedReceptorServer.Dispose();
    }
}
