using System.IO;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>
/// Keeps filesystem retries off the UI thread and replaces obsolete pending saves
/// with the most recent complete snapshot.
/// </summary>
public sealed class AsyncJsonStoreWriter<T>
{
    private readonly JsonStore<T> _store;
    private readonly Action<Exception>? _onError;
    private readonly object _gate = new();
    private byte[]? _pending;
    private Task? _worker;

    public AsyncJsonStoreWriter(JsonStore<T> store, Action<Exception>? onError = null)
    {
        _store = store;
        _onError = onError;
    }

    public void Queue(IEnumerable<T> items)
    {
        // Capture an independent value snapshot before leaving the caller's thread.
        // Filesystem access, deserialization and retry delays happen in the worker.
        var snapshot = JsonSerializer.SerializeToUtf8Bytes(items.ToList());
        lock (_gate)
        {
            _pending = snapshot;
            _worker ??= Task.Run(Drain);
        }
    }

    private void Drain()
    {
        try
        {
            while (true)
            {
                byte[]? snapshot;
                lock (_gate)
                {
                    snapshot = _pending;
                    _pending = null;
                }
                if (snapshot is null) return;

                try
                {
                    _store.Save(JsonSerializer.Deserialize<List<T>>(snapshot) ?? []);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or JsonException or InvalidOperationException)
                {
                    try { _onError?.Invoke(ex); }
                    catch { /* An error notification must not stop the writer. */ }
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _worker = null;
                if (_pending is not null) _worker = Task.Run(Drain);
            }
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task? worker;
            lock (_gate) worker = _worker;
            if (worker is null) return;
            await worker.ConfigureAwait(false);
        }
    }
}
