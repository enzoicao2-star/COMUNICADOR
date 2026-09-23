using Comunicador.Protocol;

namespace Comunicador.Models;

public sealed class ConfiguracaoGlobalPrograma
{
    public string Tema { get; set; } = "Escuro";
    public string Paleta { get; set; } = "Azul";
    public string FundoPainel { get; set; } = "Topográfico";
    public bool ReduzirMovimento { get; set; }
    public int TransparenciaCards { get; set; } = 70;
    public int BlurCards { get; set; } = 18;
    public int VelocidadeFundo { get; set; } = 100;
    public bool PermitirMidias { get; set; } = true;
    public bool PermitirLinks { get; set; } = true;
    public bool PermitirPapelParedeRemoto { get; set; } = true;
    public string PoliticaInicializacaoWindows { get; set; } = "local";
    public List<ModeloBadgeGlobal> ModelosBadge { get; set; } = [];
    public List<GrupoComputadoresGlobal> GruposComputadores { get; set; } = [];
    public List<ModeloMensagemGlobal> ModelosMensagem { get; set; } = [];

    public ConfiguracaoGlobalPrograma Clone() => new()
    {
        Tema = Tema, Paleta = Paleta, FundoPainel = FundoPainel,
        ReduzirMovimento = ReduzirMovimento, TransparenciaCards = TransparenciaCards,
        BlurCards = BlurCards, VelocidadeFundo = VelocidadeFundo,
        PermitirMidias = PermitirMidias, PermitirLinks = PermitirLinks,
        PermitirPapelParedeRemoto = PermitirPapelParedeRemoto,
        PoliticaInicializacaoWindows = PoliticaInicializacaoWindows,
        ModelosBadge = ModelosBadge.ToList(),
        GruposComputadores = GruposComputadores.ToList(),
        ModelosMensagem = ModelosMensagem.ToList(),
    };
}

public sealed class GrupoComputadoresGlobal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = string.Empty;
    public List<string> ComputadorIds { get; set; } = [];
}

public sealed class ModeloMensagemGlobal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public bool PermitirResposta { get; set; } = true;
    public string ModoExibicao { get; set; } = ProtocolConstants.DisplayMode.Toast;
    public List<BotaoResposta> Botoes { get; set; } = [];
    public AparenciaNotificacao Aparencia { get; set; } = new();
}

public sealed class ModeloBadgeGlobal : ObservableModel
{
    private string _nome = "Nova tag";
    private string _texto = "MEMBRO";
    private string _cor = "#4C8DFF";
    private string _icone = "Estrela";
    private string _estilo = "Holográfica";
    private bool _brilho = true;
    private bool _efeitoMouse = true;
    private bool _animacaoFlutuante;
    private List<string> _permissoes = [];
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get => _nome; set => SetField(ref _nome, value); }
    public string Texto { get => _texto; set => SetField(ref _texto, value); }
    public string Cor { get => _cor; set => SetField(ref _cor, value); }
    public string Icone { get => _icone; set => SetField(ref _icone, value); }
    public string Estilo { get => _estilo; set => SetField(ref _estilo, value); }
    public bool Brilho { get => _brilho; set => SetField(ref _brilho, value); }
    public bool EfeitoMouse { get => _efeitoMouse; set => SetField(ref _efeitoMouse, value); }
    public bool AnimacaoFlutuante { get => _animacaoFlutuante; set => SetField(ref _animacaoFlutuante, value); }
    public List<string> Permissoes { get => _permissoes; set { if (SetField(ref _permissoes, value ?? [])) NotificarPermissoes(); } }
    public bool GerenciarPerfis { get => Permissoes.Contains("manage_profiles"); set => SetPermissao("manage_profiles", value); }
    public bool GerenciarBadges { get => Permissoes.Contains("manage_badges"); set => SetPermissao("manage_badges", value); }
    public bool EnviarMidias { get => Permissoes.Contains("send_media"); set => SetPermissao("send_media", value); }
    public bool InstalarPainel { get => Permissoes.Contains("remote_install"); set => SetPermissao("remote_install", value); }
    public bool ControlarAcessoRemoto { get => Permissoes.Contains("remote_panel_access"); set => SetPermissao("remote_panel_access", value); }
    public bool AtualizarReceptor { get => Permissoes.Contains("remote_receiver"); set => SetPermissao("remote_receiver", value); }

    private void SetPermissao(string permissao, bool ativa)
    {
        var lista = Permissoes.Where(p => p != permissao).ToList();
        if (ativa) lista.Add(permissao);
        Permissoes = lista;
    }
    private void NotificarPermissoes()
    {
        OnPropertyChanged(nameof(GerenciarPerfis)); OnPropertyChanged(nameof(GerenciarBadges));
        OnPropertyChanged(nameof(EnviarMidias)); OnPropertyChanged(nameof(InstalarPainel));
        OnPropertyChanged(nameof(ControlarAcessoRemoto)); OnPropertyChanged(nameof(AtualizarReceptor));
    }
}
