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
    private readonly PanelUpdateService _panelUpdate;
    private readonly CloudSyncService _cloudSync;

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
        ? "sem PCs conectados"
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
        var perfis = new PerfilComputadorRepository(new JsonStore<PerfilComputador>(AppPaths.PerfisComputadoresFile));
        _panelUpdate = new PanelUpdateService();
        var perfilLocal = perfis.Obter(Settings.PainelId);
        var badgesLocais = perfilLocal?.Badges.Select(b => b.Clone()).ToList() ?? new List<BadgeUsuario>();
        if (perfilLocal is null ||
            !string.Equals(perfilLocal.NomePublico, Settings.NomePainel, StringComparison.Ordinal))
        {
            perfis.Salvar(Settings.PainelId, Settings.NomePainel, false,
                badgesLocais, Settings.PainelId);
        }

        _cloudSync = new CloudSyncService(new SupabaseClient(), Settings, perfis);
        _cloudSync.ResponseReceived += OnCloudResponseReceived;
        _cloudSync.DeliveryReceived += OnCloudDeliveryReceived;

        var client = new ReceptorClient(Settings.PainelId, Settings.NomePainel);
        _discovery = new DiscoveryService(Settings);
        _historicoRepositorio = new HistoricoRepository(historicoStore);
        _logsRepositorio = new LogRepository(logsStore);
        Logger.Configure(_logsRepositorio, Settings.PainelId, Settings.NomePainel);
        _conexoesReversas = new RegistroConexoesReversas();
        var enviador = new EnviadorNotificacoes(client, _conexoesReversas, Settings);
        var reenvios = new ReenvioRepository();
        var atualizador = new AtualizadorReceptor(client, _conexoesReversas);

        Computadores = new ComputadoresViewModel(
            computadoresStore, _discovery, client, atualizador, Settings, perfis, _cloudSync);
        _sync = new SyncCoordinatorService(
            Computadores.Snapshot, client, _conexoesReversas, _historicoRepositorio, _logsRepositorio, perfis);
        Historico = new HistoricoViewModel(_historicoRepositorio, reenvios, Computadores, enviador);
        Logs = new LogsViewModel(_logsRepositorio, _sync);
        Mensagens = new MensagensViewModel(Computadores, enviador, _historicoRepositorio, _cloudSync, reenvios);

        _statusMonitor = new StatusMonitorService(Computadores.Snapshot, enviador, Settings);
        _statusMonitor.StatusAtualizado += Computadores.AtualizarStatus;
        Computadores.PingMedioAtualizado += AtualizarPingMedio;

        _scheduler = new LembreteSchedulerService(
            () => LembretesSnapshot(),
            id => Computadores.Computadores.FirstOrDefault(c => c.Id == id),
            enviador);

        Lembretes = new LembretesViewModel(lembretesStore, Computadores, _historicoRepositorio, _scheduler, _cloudSync);

        var paineisPareados = new ObservableCollection<PainelPareado>(paineisPareadosStore.Load());
        _embeddedReceptorServer = new EmbeddedReceptorServer(
            Settings, paineisPareados, paineisPareadosStore, _historicoRepositorio,
            _logsRepositorio, _conexoesReversas, perfis);
        _embeddedReceptorServer.ReceptorRegistrado += Computadores.RegistrarViaConexaoReversa;
        Configuracoes = new ConfiguracoesViewModel(Settings, paineisPareados, paineisPareadosStore,
            _embeddedReceptorServer, perfis, client, _panelUpdate, _cloudSync);

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

    private void OnCloudResponseReceived(CloudDelivery resposta) => UiDispatcher.Invoke(() =>
    {
        var computador = Computadores.Computadores.FirstOrDefault(c => c.Id == resposta.TargetDeviceId);
        var tituloOriginal = resposta.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && resposta.Payload.TryGetProperty("title", out var titulo)
                ? titulo.GetString() : null;
        var nome = computador?.NomeExibicao ?? resposta.TargetDeviceId;
        _historicoRepositorio.Adicionar(new HistoricoEntry
        {
            ComputadorId = resposta.TargetDeviceId,
            ComputadorNome = nome,
            Titulo = tituloOriginal ?? "Resposta recebida",
            Mensagem = "Resposta a uma entrega agendada",
            Status = StatusEnvio.Respondido,
            RespostaTexto = resposta.ResponseText,
        });
        _ = Views.NotificacaoRecebidaWindow.MostrarAsync(
            nome, "Resposta recebida", resposta.ResponseText ?? "O usuário confirmou o recebimento.",
            allowReply: false);
    });

    private void OnCloudDeliveryReceived(CloudDelivery delivery) =>
        UiDispatcher.Invoke(() => _ = HandleCloudDeliveryAsync(delivery));

    private async Task HandleCloudDeliveryAsync(CloudDelivery delivery)
    {
        var payload = delivery.Payload;
        if (payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && payload.TryGetProperty("kind", out var kindNode)
            && kindNode.GetString() == "admin_command")
        {
            await HandleAdminCommandAsync(delivery).ConfigureAwait(true);
            return;
        }
        var sender = payload.TryGetProperty("sender", out var senderNode)
            ? senderNode.GetString() ?? delivery.SenderDeviceId : delivery.SenderDeviceId;
        var title = payload.TryGetProperty("title", out var titleNode)
            ? titleNode.GetString() ?? "Lembrete" : "Lembrete";
        var message = payload.TryGetProperty("message", out var messageNode)
            ? messageNode.GetString() ?? string.Empty : string.Empty;
        var allowReply = payload.TryGetProperty("allow_reply", out var replyNode) && replyNode.GetBoolean();
        var result = await Views.NotificacaoRecebidaWindow.MostrarAsync(
            sender, title, message, allowReply).ConfigureAwait(true);
        _historicoRepositorio.Adicionar(new HistoricoEntry
        {
            ComputadorId = delivery.SenderDeviceId,
            ComputadorNome = sender,
            Titulo = title,
            Mensagem = message,
            Status = result is null ? StatusEnvio.Exibido : StatusEnvio.Respondido,
            RespostaTexto = result,
        });
        if (result is not null)
            await _cloudSync.RespondToDeliveryAsync(delivery.Id, result).ConfigureAwait(true);
    }

    private async Task HandleAdminCommandAsync(CloudDelivery delivery)
    {
        var payload = delivery.Payload;
        var command = payload.TryGetProperty("command", out var commandNode) ? commandNode.GetString() : null;
        var requiredPermission = command switch
        {
            "install_panel" or "reinstall_panel" => "remote_install",
            "disable_panel" or "enable_panel" => "remote_panel_access",
            "reinstall_receiver" => "remote_receiver",
            _ => string.Empty,
        };
        var trustedSender = !string.IsNullOrWhiteSpace(requiredPermission)
            && _cloudSync.HasPermissionForDevice(delivery.SenderDeviceId, requiredPermission);
        string result;
        if (!trustedSender)
        {
            result = "Comando recusado: o remetente não tem a permissão necessária.";
        }
        else
        {
            switch (command)
            {
                case "disable_panel":
                    Settings.EntradaPainelHabilitada = false;
                    SettingsStore.Save(Settings);
                    _embeddedReceptorServer.Stop();
                    result = "A entrada de conexões locais do painel foi bloqueada.";
                    break;
                case "enable_panel":
                    Settings.EntradaPainelHabilitada = true;
                    SettingsStore.Save(Settings);
                    _embeddedReceptorServer.AtualizarDisponibilidade();
                    result = "A entrada de conexões locais do painel foi habilitada.";
                    break;
                case "reinstall_panel":
                    try
                    {
                        var info = await _panelUpdate.CheckAsync().ConfigureAwait(true);
                        await _panelUpdate.StartUpdateAsync(info, forceReinstall: true).ConfigureAwait(true);
                        result = $"Reinstalação do painel {info.LatestVersion} iniciada. O Comunicador será reaberto ao terminar.";
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Não foi possível reinstalar o painel remotamente.", "atualizacao", ex.Message);
                        result = $"Não foi possível iniciar a reinstalação do painel: {ex.Message}";
                    }
                    break;
                default:
                    result = "Comando desconhecido ou incompatível com este painel.";
                    break;
            }
        }
        await _cloudSync.RespondToDeliveryAsync(delivery.Id, result).ConfigureAwait(true);
    }

    public void Start()
    {
        _cloudSync.Start();
        _discovery.Start();
        _statusMonitor.Start();
        _scheduler.Start();
        if (Settings.EntradaPainelHabilitada) _embeddedReceptorServer.AtualizarDisponibilidade();
        _sync.Start();
        Configuracoes.AtualizarStatusReceptor();
        var resumo = _panelUpdate.ConsumeUpdateSummary();
        if (!string.IsNullOrWhiteSpace(resumo))
            System.Windows.MessageBox.Show(resumo, "Atualização concluída",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public void Dispose()
    {
        Computadores.PingMedioAtualizado -= AtualizarPingMedio;
        _cloudSync.ResponseReceived -= OnCloudResponseReceived;
        _cloudSync.DeliveryReceived -= OnCloudDeliveryReceived;
        _discovery.Dispose();
        _statusMonitor.Dispose();
        _scheduler.Dispose();
        _sync.Dispose();
        _cloudSync.Dispose();
        _embeddedReceptorServer.Dispose();
    }
}
