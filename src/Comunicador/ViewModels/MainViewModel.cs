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

    private object _secaoAtual;

    public AppSettings Settings { get; }
    public ComputadoresViewModel Computadores { get; }
    public MensagensViewModel Mensagens { get; }
    public LembretesViewModel Lembretes { get; }
    public HistoricoViewModel Historico { get; }
    public ConfiguracoesViewModel Configuracoes { get; }

    public object SecaoAtual
    {
        get => _secaoAtual;
        set => SetField(ref _secaoAtual, value);
    }

    public ICommand NavegarCommand { get; }

    public MainViewModel()
    {
        Settings = SettingsStore.Load();

        var computadoresStore = new JsonStore<Computador>(AppPaths.ComputadoresFile);
        var lembretesStore = new JsonStore<Lembrete>(AppPaths.LembretesFile);
        var historicoStore = new JsonStore<HistoricoEntry>(AppPaths.HistoricoFile);
        var paineisPareadosStore = new JsonStore<PainelPareado>(AppPaths.PaineisPareadosFile);

        var client = new ReceptorClient(Settings.PainelId, Settings.NomePainel);
        _discovery = new DiscoveryService(Settings);
        _historicoRepositorio = new HistoricoRepository(historicoStore);

        Computadores = new ComputadoresViewModel(computadoresStore, _discovery, client);
        Historico = new HistoricoViewModel(_historicoRepositorio);
        Mensagens = new MensagensViewModel(Computadores, client, _historicoRepositorio);

        _statusMonitor = new StatusMonitorService(Computadores.Snapshot, client, Settings);
        _statusMonitor.StatusAtualizado += Computadores.AtualizarStatus;

        _scheduler = new LembreteSchedulerService(
            () => LembretesSnapshot(),
            id => Computadores.Computadores.FirstOrDefault(c => c.Id == id),
            client);

        Lembretes = new LembretesViewModel(lembretesStore, Computadores, _historicoRepositorio, _scheduler);

        var paineisPareados = new ObservableCollection<PainelPareado>(paineisPareadosStore.Load());
        _embeddedReceptorServer = new EmbeddedReceptorServer(Settings, paineisPareados, paineisPareadosStore, _historicoRepositorio);
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
                "configuracoes" => Configuracoes,
                _ => SecaoAtual,
            };
        });
    }

    private IReadOnlyList<Lembrete> LembretesSnapshot()
    {
        IReadOnlyList<Lembrete> resultado = Array.Empty<Lembrete>();
        UiDispatcher.Invoke(() => resultado = Lembretes.Snapshot());
        return resultado;
    }

    public void Start()
    {
        _discovery.Start();
        _statusMonitor.Start();
        _scheduler.Start();
        _embeddedReceptorServer.AtualizarDisponibilidade();
    }

    public void Dispose()
    {
        _discovery.Dispose();
        _statusMonitor.Dispose();
        _scheduler.Dispose();
        _embeddedReceptorServer.Dispose();
    }
}
