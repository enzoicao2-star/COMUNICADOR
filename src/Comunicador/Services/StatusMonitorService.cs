using Comunicador.Models;
using Comunicador.Networking;

namespace Comunicador.Services;

/// <summary>Periodically pings every paired computer to keep its Online/Offline status current.</summary>
public sealed class StatusMonitorService : IDisposable
{
    private readonly Func<IReadOnlyList<Computador>> _getComputadores;
    private readonly EnviadorNotificacoes _enviador;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private static readonly TimeSpan TempoSemSinalParaOffline = TimeSpan.FromMinutes(1);
    private const int MaxVerificacoesSimultaneas = 8;

    public event Action<string, StatusComputador, double?>? StatusAtualizado;

    public StatusMonitorService(
        Func<IReadOnlyList<Computador>> getComputadores, EnviadorNotificacoes enviador, AppSettings settings)
    {
        _getComputadores = getComputadores;
        _enviador = enviador;
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
            IReadOnlyList<Computador> pareados;
            try
            {
                pareados = _getComputadores()
                    .Where(c => !EhComputadorLocal(c)
                        && (c.Pareado || !string.IsNullOrWhiteSpace(c.EnderecoIp)))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Warning($"A lista de computadores será consultada novamente: {ex.Message}",
                    "status", ex.ToString());
                try { await Task.Delay(TimeSpan.FromSeconds(_settings.IntervaloPingSegundos), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                continue;
            }
            using var limite = new SemaphoreSlim(MaxVerificacoesSimultaneas);
            var checks = pareados.Select(async computador =>
            {
                await limite.WaitAsync(ct).ConfigureAwait(false);
                try { await CheckOneAsync(computador, ct).ConfigureAwait(false); }
                finally { limite.Release(); }
            });
            try { await Task.WhenAll(checks).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Logger.Warning($"A verificação de presença será repetida: {ex.Message}",
                    "status", ex.ToString());
            }

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
        try
        {
            var resultado = await _enviador.MedirStatusAsync(computador, ct).ConfigureAwait(false);
            if (resultado.Online)
            {
                StatusAtualizado?.Invoke(computador.Id, StatusComputador.Online, resultado.PingMs);
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning($"Falha ao verificar presença de {computador.NomeExibicao}: {ex.Message}");
        }

        // A brief packet loss must not make the card flicker offline. Cloud
        // heartbeats and reverse connections refresh UltimaVezVisto as well.
        var ultimoSinal = computador.UltimaVezVisto;
        if (DateTime.UtcNow - ultimoSinal >= TempoSemSinalParaOffline
            && computador.Status != StatusComputador.Offline)
            StatusAtualizado?.Invoke(computador.Id, StatusComputador.Offline, null);
    }

    private bool EhComputadorLocal(Computador computador) =>
        string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(computador.Nome, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
