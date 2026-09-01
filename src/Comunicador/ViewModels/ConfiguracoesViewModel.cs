using System.Collections.ObjectModel;
using System.Windows.Input;
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

    private string _nomePainel;
    private int _portaTcp;
    private int _portaUdp;
    private int _intervaloDescoberta;
    private int _intervaloPing;
    private bool _iniciarComWindows;
    private bool _aceitarMensagensDeOutrosPaineis;
    private string? _statusOperacao;

    public ObservableCollection<PainelPareado> PaineisPareados { get; }

    public string NomePainel
    {
        get => _nomePainel;
        set => SetField(ref _nomePainel, value);
    }

    public int PortaTcp
    {
        get => _portaTcp;
        set => SetField(ref _portaTcp, value);
    }

    public int PortaUdp
    {
        get => _portaUdp;
        set => SetField(ref _portaUdp, value);
    }

    public int IntervaloDescobertaSegundos
    {
        get => _intervaloDescoberta;
        set => SetField(ref _intervaloDescoberta, value);
    }

    public int IntervaloPingSegundos
    {
        get => _intervaloPing;
        set => SetField(ref _intervaloPing, value);
    }

    public bool IniciarComWindows
    {
        get => _iniciarComWindows;
        set => SetField(ref _iniciarComWindows, value);
    }

    public bool AceitarMensagensDeOutrosPaineis
    {
        get => _aceitarMensagensDeOutrosPaineis;
        set => SetField(ref _aceitarMensagensDeOutrosPaineis, value);
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

    public ICommand SalvarCommand { get; }
    public ICommand RemoverPainelPareadoCommand { get; }

    public ConfiguracoesViewModel(
        AppSettings settings, ObservableCollection<PainelPareado> paineisPareados,
        JsonStore<PainelPareado> paineisPareadosStore, EmbeddedReceptorServer embeddedReceptorServer)
    {
        _settings = settings;
        _embeddedReceptorServer = embeddedReceptorServer;
        _paineisPareadosStore = paineisPareadosStore;
        PaineisPareados = paineisPareados;

        _nomePainel = settings.NomePainel;
        _portaTcp = settings.PortaTcp;
        _portaUdp = settings.PortaDescobertaUdp;
        _intervaloDescoberta = settings.IntervaloDescobertaSegundos;
        _intervaloPing = settings.IntervaloPingSegundos;
        _iniciarComWindows = StartupManager.EstaHabilitado();
        _aceitarMensagensDeOutrosPaineis = settings.AceitarMensagensDeOutrosPaineis;
        _statusReceptorEmbutido = CalcularStatusReceptor();

        SalvarCommand = new RelayCommand(_ => Salvar());
        RemoverPainelPareadoCommand = new RelayCommand(param =>
        {
            if (param is PainelPareado pareado)
            {
                PaineisPareados.Remove(pareado);
                _paineisPareadosStore.Save(PaineisPareados);
                StatusOperacao = $"Painel '{pareado.PanelName}' removido — ele precisará parear novamente para enviar mensagens.";
            }
        });
    }

    /// <summary>Chamado pelo MainViewModel depois que o receptor embutido efetivamente
    /// tenta iniciar (Start()/AtualizarDisponibilidade() são assíncronos em relação à
    /// construção desta ViewModel), para o texto de status não ficar desatualizado.</summary>
    public void AtualizarStatusReceptor() => StatusReceptorEmbutido = CalcularStatusReceptor();

    private void Salvar()
    {
        _settings.NomePainel = NomePainel;
        _settings.PortaTcp = PortaTcp;
        _settings.PortaDescobertaUdp = PortaUdp;
        _settings.IntervaloDescobertaSegundos = IntervaloDescobertaSegundos;
        _settings.IntervaloPingSegundos = IntervaloPingSegundos;
        _settings.IniciarComWindows = IniciarComWindows;
        _settings.AceitarMensagensDeOutrosPaineis = AceitarMensagensDeOutrosPaineis;
        SettingsStore.Save(_settings);
        StartupManager.Aplicar(IniciarComWindows);
        _embeddedReceptorServer.AtualizarDisponibilidade();
        StatusReceptorEmbutido = CalcularStatusReceptor();

        StatusOperacao = "Configurações salvas. Reinicie o Comunicador para aplicar mudanças de porta.";
    }

    private string CalcularStatusReceptor() =>
        _embeddedReceptorServer.Ativo
            ? "Ativo — este computador também recebe mensagens de outros painéis, sem precisar do receptor.py."
            : _embeddedReceptorServer.UltimoErro ?? "Desativado (bloqueado aqui nas configurações).";
}
