using Comunicador.Models;
using Comunicador.Networking;
using System.Collections.Concurrent;

namespace Comunicador.Services;

/// <summary>Periodically pings every paired computer to keep its Online/Offline status current.</summary>
public sealed class StatusMonitorService : IDisposable
{
    private readonly Func<IReadOnlyList<Computador>> _getComputadores;
    private readonly EnviadorNotificacoes _enviador;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private readonly ConcurrentDictionary<string, int> _falhasConsecutivas = new(StringComparer.OrdinalIgnoreCase);
    private const int FalhasAntesDeOffline = 3;
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
            var pareados = _getComputadores()
                .Where(c => !EhComputadorLocal(c)
                    && (c.Pareado || !string.IsNullOrWhiteSpace(c.EnderecoIp)))
                .ToList();
            using var limite = new SemaphoreSlim(MaxVerificacoesSimultaneas);
            var checks = pareados.Select(async computador =>
            {
                await limite.WaitAsync(ct).ConfigureAwait(false);
                try { await CheckOneAsync(computador, ct).ConfigureAwait(false); }
                finally { limite.Release(); }
            });
            try { await Task.WhenAll(checks).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }

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
                _falhasConsecutivas.TryRemove(computador.Id, out _);
                StatusAtualizado?.Invoke(computador.Id, StatusComputador.Online, resultado.PingMs);
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning($"Falha ao verificar presença de {computador.NomeExibicao}: {ex.Message}");
        }

        var falhas = _falhasConsecutivas.AddOrUpdate(computador.Id, 1, (_, atuais) => atuais + 1);
        if (falhas >= FalhasAntesDeOffline)
            StatusAtualizado?.Invoke(computador.Id, StatusComputador.Offline, null);
    }

    private bool EhComputadorLocal(Computador computador) =>
        string.Equals(computador.Id, _settings.PainelId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(computador.Nome, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _falhasConsecutivas.Clear();
    }
}
