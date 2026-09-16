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
    public bool IniciarComWindows { get; set; }

    /// <summary>Controla a exibição de mensagens recebidas. O receptor embutido continua
    /// visível na rede para que os outros painéis identifiquem esta máquina corretamente.</summary>
    public bool AceitarMensagensDeOutrosPaineis { get; set; } = true;
    public bool AceitarImagensDeOutrosPaineis { get; set; } = true;
    public bool AceitarBotoesComLinks { get; set; } = true;

    public string Tema { get; set; } = "Escuro";
    public string Paleta { get; set; } = "Azul";
    public List<PaletaPersonalizada> PaletasPersonalizadas { get; set; } = new();
    public string FundoPainel { get; set; } = "Topográfico";
    public int IntensidadeFundo { get; set; } = 100;
    public bool ReduzirMovimento { get; set; }
}
