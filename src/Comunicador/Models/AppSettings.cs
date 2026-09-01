using Comunicador.Protocol;

namespace Comunicador.Models;

public sealed class AppSettings
{
    public string PainelId { get; set; } = Guid.NewGuid().ToString();
    public int PortaTcp { get; set; } = ProtocolConstants.TcpPort;
    public int PortaDescobertaUdp { get; set; } = ProtocolConstants.UdpDiscoveryPort;
    public int IntervaloDescobertaSegundos { get; set; } = 15;
    public int IntervaloPingSegundos { get; set; } = 20;
    public string NomePainel { get; set; } = Environment.MachineName;
    public bool IniciarComWindows { get; set; }

    /// <summary>Se verdadeiro (padrão), este Comunicador aceita mensagens de outros painéis,
    /// funcionando como seu próprio receptor — sem precisar instalar receptor.py aqui também.
    /// Se falso, este computador só envia; não aparece na descoberta de outros painéis e
    /// recusa qualquer pareamento/mensagem recebida.</summary>
    public bool AceitarMensagensDeOutrosPaineis { get; set; } = true;
}
