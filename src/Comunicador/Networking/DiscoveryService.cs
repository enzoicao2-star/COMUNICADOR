using System.Net;
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
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _settings.PortaDescobertaUdp);
        await _client.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
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
