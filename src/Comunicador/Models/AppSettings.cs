using Comunicador.Protocol;

namespace Comunicador.Models;

public sealed class AppSettings
{
    public int VersaoConfiguracao { get; set; } = 2;
    public string PainelId { get; set; } = Guid.NewGuid().ToString();
    public int PortaTcp { get; set; } = ProtocolConstants.TcpPort;
    public int PortaDescobertaUdp { get; set; } = ProtocolConstants.UdpDiscoveryPort;
    public int IntervaloDescobertaSegundos { get; set; } = 15;
    public int IntervaloPingSegundos { get; set; } = 5;
    public string NomePainel { get; set; } = Environment.MachineName;
    public bool EstePainelEhOwner { get; set; }
    public bool IniciarComWindows { get; set; }
    public bool? PreferenciaInicializacaoComWindows { get; set; }
    public bool EntradaPainelHabilitada { get; set; } = true;

    /// <summary>Compatibilidade com configurações antigas. Mensagens e lembretes são
    /// obrigatórios; somente mídias e links podem ser bloqueados.</summary>
    public bool AceitarMensagensDeOutrosPaineis { get; set; } = true;
    public bool AceitarImagensDeOutrosPaineis { get; set; } = true;
    public bool MidiasPermitidasGlobalmente { get; set; } = true;
    public bool LinksPermitidosGlobalmente { get; set; } = true;
    public bool PapelParedeRemotoPermitidoGlobalmente { get; set; } = true;
    public bool AceitarBotoesComLinks { get; set; } = true;

    public string Tema { get; set; } = "Escuro";
    public string Paleta { get; set; } = "Azul";
    public List<PaletaPersonalizada> PaletasPersonalizadas { get; set; } = new();
    public string FundoPainel { get; set; } = "Topográfico";
    public int IntensidadeFundo { get; set; } = 100;
    public bool ReduzirMovimento { get; set; }
    public int TransparenciaCards { get; set; } = 70;
    public int BlurCards { get; set; } = 18;
    public int VelocidadeFundo { get; set; } = 100;
}
