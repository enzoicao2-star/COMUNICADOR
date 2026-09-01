using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Comunicador.Networking;

/// <summary>Varre a sub-rede local tentando abrir a porta TCP do receptor em cada host.
/// Serve de plano B quando o broadcast UDP de descoberta nao passa (firewall, AP
/// isolation, switch que nao encaminha broadcast). So testa a porta conhecida do
/// protocolo — nao e um scanner de portas.</summary>
public sealed class LanScanner
{
    private static readonly TimeSpan HostTimeout = TimeSpan.FromMilliseconds(600);
    private const int MaxHostsPorRede = 254;

    private readonly int _porta;

    public LanScanner(int porta)
    {
        _porta = porta;
    }

    /// <summary>Retorna os IPs da rede local que aceitaram conexao na porta do receptor.</summary>
    public async Task<IReadOnlyList<string>> VarrerAsync(CancellationToken ct = default)
    {
        var candidatos = EnumerarCandidatos().Distinct().Take(MaxHostsPorRede * 4).ToList();
        var tarefas = candidatos.Select(ip => TestarHostAsync(ip, ct));
        var resultados = await Task.WhenAll(tarefas).ConfigureAwait(false);
        return resultados.Where(r => r is not null).Select(r => r!).ToList();
    }

    private async Task<string?> TestarHostAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HostTimeout);
            await client.ConnectAsync(ip, _porta, cts.Token).ConfigureAwait(false);
            return ip;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Gera os IPs de cada sub-rede IPv4 local, ignorando redes grandes demais
    /// (mascara menor que /24) para nao virar varredura interminavel.</summary>
    private static IEnumerable<string> EnumerarCandidatos()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask is null)
                {
                    continue;
                }

                var mask = info.IPv4Mask.GetAddressBytes();
                // aceita apenas /24 ou menor (ex.: 255.255.255.0); redes maiores
                // teriam milhares de hosts e a varredura ficaria inviavel.
                if (mask[0] != 255 || mask[1] != 255 || mask[2] != 255)
                {
                    continue;
                }

                var ip = info.Address.GetAddressBytes();
                for (var ultimo = 1; ultimo <= MaxHostsPorRede; ultimo++)
                {
                    if (ultimo == ip[3])
                    {
                        continue; // o proprio host ja e conhecido
                    }

                    yield return new IPAddress(new[] { ip[0], ip[1], ip[2], (byte)ultimo }).ToString();
                }
            }
        }
    }
}
