using Comunicador.Models;
using Comunicador.Networking;
using Comunicador.Protocol;

namespace Comunicador.Services;

public sealed record SyncSummary(int ComputersReached, int HistoryAdded, int LogsAdded, int Failures);

/// <summary>Troca histórico e logs com painéis e coleta erros dos receptores.
/// IDs estáveis deduplicam os dados, formando a mesma visão consolidada em cada painel.</summary>
public sealed class SyncCoordinatorService : IDisposable
{
    private readonly Func<IReadOnlyList<Computador>> _computers;
    private readonly ReceptorClient _client;
    private readonly RegistroConexoesReversas _connections;
    private readonly HistoricoRepository _history;
    private readonly LogRepository _logs;
    private readonly PerfilComputadorRepository _profiles;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private Task? _loop;

    public SyncCoordinatorService(
        Func<IReadOnlyList<Computador>> computers,
        ReceptorClient client,
        RegistroConexoesReversas connections,
        HistoricoRepository history,
        LogRepository logs,
        PerfilComputadorRepository profiles)
    {
        _computers = computers;
        _client = client;
        _connections = connections;
        _history = history;
        _logs = logs;
        _profiles = profiles;
    }

    public void Start() => _loop ??= Task.Run(() => LoopAsync(_stop.Token));

    public async Task<SyncSummary> SyncNowAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<Computador> computers = [];
            UiDispatcher.Invoke(() => computers = _computers()
                .Where(c => c.Pareado
                    && c.Status != StatusComputador.Offline
                    // Receptores anteriores a 2.1 não conhecem sync_request e
                    // encerram a sessão ao recebê-lo. Eles continuam online e
                    // podem ser atualizados uma última vez pelo instalador.
                    && ProtocolConstants.SupportsRemoteManagement(c.VersaoReceptor))
                .ToList());

            var reached = 0;
            var historyAdded = 0;
            var logsAdded = 0;
            var failures = 0;
            foreach (var computer in computers)
            {
                var result = await SyncComputerAsync(computer, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    failures++;
                    continue;
                }

                reached++;
                historyAdded += _history.Mesclar(result.HistoryEntries.Select(i => i.ToModel()));
                logsAdded += _logs.Mesclar(result.LogEntries.Select(i => i.ToModel()));
                _profiles.Mesclar(result.ComputerProfiles);
            }
            return new(reached, historyAdded, logsAdded, failures);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<SyncResult> SyncComputerAsync(Computador computer, CancellationToken ct)
    {
        var connection = _connections.Obter(computer.Id);
        var request = ComunicadorMessage.CreateBase(ProtocolConstants.MessageType.SyncRequest);
        request.Token = connection?.Token ?? computer.Token ?? string.Empty;
        request.IncludeHistory = computer.TemPainel;
        request.IncludeLogs = true;
        request.HistoryEntries = computer.TemPainel
            ? _history.Snapshot(ProtocolConstants.MaxSyncEntries).Select(i => i.ToSync()).ToList()
            : [];
        request.LogEntries = computer.TemPainel
            ? _logs.Snapshot(ProtocolConstants.MaxSyncEntries).Select(i => i.ToSync()).ToList()
            : [];
        request.ComputerProfiles = computer.TemPainel
            ? _profiles.Snapshot(ProtocolConstants.MaxSyncProfiles).ToList()
            : [];

        var validation = MessageValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new(false, [], [], [], validation.Message);
        }
        var framed = MessageValidator.Frame(request);
        var size = MessageValidator.ValidateSize(framed.Length, isUdp: false);
        if (!size.IsValid)
        {
            return new(false, [], [], [], size.Message);
        }

        return connection is not null
            ? await connection.SincronizarAsync(request, ct, framed).ConfigureAwait(false)
            : await _client.SyncAsync(computer.EnderecoIp, computer.PortaTcp, request, ct,
                framed).ConfigureAwait(false);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                await SyncNowAsync(ct).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // encerramento normal.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _stop.Dispose();
        _syncLock.Dispose();
    }
}
