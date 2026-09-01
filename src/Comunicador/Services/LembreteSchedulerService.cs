using Comunicador.Models;
using Comunicador.Networking;

namespace Comunicador.Services;

/// <summary>Background loop that checks pending Lembretes every 20s and sends the ones whose
/// scheduled time has arrived, to every selected computer.</summary>
public sealed class LembreteSchedulerService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    private readonly Func<IReadOnlyList<Lembrete>> _getLembretes;
    private readonly Func<string, Computador?> _resolveComputador;
    private readonly ReceptorClient _client;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<Lembrete>? LembreteConcluido;
    public event Action<Lembrete, Computador, NotificationResult>? NotificacaoEnviada;

    public LembreteSchedulerService(
        Func<IReadOnlyList<Lembrete>> getLembretes,
        Func<string, Computador?> resolveComputador,
        ReceptorClient client)
    {
        _getLembretes = getLembretes;
        _resolveComputador = resolveComputador;
        _client = client;
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
            var vencidos = _getLembretes()
                .Where(l => !l.Enviado && l.DataHora <= DateTime.Now)
                .ToList();

            foreach (var lembrete in vencidos)
            {
                await DispararAsync(lembrete, ct).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task DispararAsync(Lembrete lembrete, CancellationToken ct)
    {
        foreach (var computadorId in lembrete.ComputadorIds)
        {
            var computador = _resolveComputador(computadorId);
            if (computador is null || !computador.Pareado)
            {
                continue;
            }

            var result = await _client.SendNotificationAsync(
                computador.EnderecoIp, computador.PortaTcp, computador.Token!,
                lembrete.Titulo, lembrete.Mensagem, lembrete.PermitirResposta, ct).ConfigureAwait(false);

            NotificacaoEnviada?.Invoke(lembrete, computador, result);
        }

        lembrete.Enviado = true;
        LembreteConcluido?.Invoke(lembrete);
    }

    public void Dispose() => Stop();
}
