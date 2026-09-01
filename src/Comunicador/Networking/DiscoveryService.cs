using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Comunicador.Models;
using Comunicador.Protocol;

namespace Comunicador.Networking;

/// <summary>Broadcasts UDP "discover" packets and raises an event for every "announce" reply seen,
/// so the Computadores screen can pick up new receivers as soon as they answer.</summary>
public sealed class DiscoveryService : IDisposable
{
    private readonly AppSettings _settings;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<AnnounceInfo>? ReceptorDescoberto;

    public DiscoveryService(AppSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _client = new UdpClient(0) { EnableBroadcast = true };
        _loopTask = Task.WhenAll(BroadcastLoopAsync(_cts.Token), ReceiveLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _client?.Close();
        _client?.Dispose();
        _client = null;
        _loopTask = null;
    }

    public async Task BroadcastOnceAsync(CancellationToken ct = default)
    {
        if (_client is null)
        {
            return;
        }

        var discover = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.Discover);
        discover.PanelId = _settings.PainelId;
        discover.SenderName = _settings.NomePainel;
        var payload = MessageValidator.Frame(discover);

        // 255.255.255.255 sai por uma interface só (a da rota padrão), o que falha
        // quando existe adaptador virtual/VPN roubando a rota. Mandamos também um
        // broadcast dirigido por sub-rede em cada placa física ativa.
        var destinos = new List<IPAddress> { IPAddress.Broadcast };
        destinos.AddRange(EnderecosBroadcastPorInterface());

        foreach (var destino in destinos)
        {
            try
            {
                var endpoint = new IPEndPoint(destino, _settings.PortaDescobertaUdp);
                await _client.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // interface indisponível no momento; as outras seguem normalmente.
            }
        }
    }

    private static IEnumerable<IPAddress> EnderecosBroadcastPorInterface()
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

                var ip = info.Address.GetAddressBytes();
                var mask = info.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcast[i] = (byte)(ip[i] | ~mask[i]);
                }

                yield return new IPAddress(broadcast);
            }
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync(ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // rede indisponível momentaneamente; tenta de novo no próximo ciclo.
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.IntervaloDescobertaSegundos), ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            HandleDatagram(result.Buffer, result.RemoteEndPoint);
        }
    }

    private void HandleDatagram(byte[] buffer, IPEndPoint remote)
    {
        if (!MessageValidator.ValidateSize(buffer.Length, isUdp: true).IsValid)
        {
            return;
        }

        if (!MessageValidator.TryParse(buffer, out var msg, out _) || msg is null)
        {
            return;
        }

        if (msg.Type != ProtocolConstants.MessageType.Announce)
        {
            return;
        }

        if (!MessageValidator.Validate(msg).IsValid)
        {
            return;
        }

        ReceptorDescoberto?.Invoke(new AnnounceInfo(
            msg.ComputerId!, msg.ComputerName!, remote.Address.ToString(), msg.TcpPort!.Value, msg.Paired ?? false));
    }

    public void Dispose() => Stop();
}
