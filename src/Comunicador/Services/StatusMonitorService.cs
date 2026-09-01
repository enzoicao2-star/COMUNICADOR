using Comunicador.Models;
using Comunicador.Networking;

namespace Comunicador.Services;

/// <summary>Periodically pings every paired computer to keep its Online/Offline status current.</summary>
public sealed class StatusMonitorService : IDisposable
{
    private readonly Func<IReadOnlyList<Computador>> _getComputadores;
    private readonly ReceptorClient _client;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<string, StatusComputador>? StatusAtualizado;

    public StatusMonitorService(Func<IReadOnlyList<Computador>> getComputadores, ReceptorClient client, AppSettings settings)
    {
        _getComputadores = getComputadores;
        _client = client;
        _settings = settings;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _loopTask = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pareados = _getComputadores().Where(c => c.Pareado).ToList();
            var checks = pareados.Select(c => CheckOneAsync(c, ct));
            await Task.WhenAll(checks).ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.IntervaloPingSegundos), ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckOneAsync(Computador computador, CancellationToken ct)
    {
        var online = await _client.PingAsync(computador.EnderecoIp, computador.PortaTcp, computador.Token!, ct)
            .ConfigureAwait(false);
        StatusAtualizado?.Invoke(computador.Id, online ? StatusComputador.Online : StatusComputador.Offline);
    }

    public void Dispose() => Stop();
}
