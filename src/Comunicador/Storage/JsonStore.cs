using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>Load/save helper for a JSON-serialized list persisted to a single file, one per model type.</summary>
public sealed class JsonStore<T>
{
    private static readonly JsonSerializerOptions Options = new();
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private readonly string _recoveryPath;
    private readonly object _lock;

    public JsonStore(string path)
    {
        _path = Path.GetFullPath(path);
        _recoveryPath = _path + ".recovery";
        _lock = PathLocks.GetOrAdd(_path, static _ => new object());
    }

    public List<T> Load()
    {
        lock (_lock)
        {
            AppPaths.EnsureCreated();
            var hasPrimary = File.Exists(_path);
            var hasRecovery = File.Exists(_recoveryPath);
            if (!hasPrimary && !hasRecovery)
            {
                return new List<T>();
            }

            // A recovery copy is written only after Windows refuses to replace the
            // primary file. Prefer whichever copy was written most recently.
            var recoveryFirst = hasRecovery && (!hasPrimary
                || File.GetLastWriteTimeUtc(_recoveryPath) >= File.GetLastWriteTimeUtc(_path));
            if (recoveryFirst)
            {
                if (TryLoad(_recoveryPath, out var recovered)) return recovered;
                if (TryLoad(_path, out var primaryAfterRecoveryFailure)) return primaryAfterRecoveryFailure;
            }
            else
            {
                if (TryLoad(_path, out var primary)) return primary;
                if (TryLoad(_recoveryPath, out var recovery)) return recovery;
            }

            return new List<T>();
        }
    }

    public void Save(IEnumerable<T> items)
    {
        lock (_lock)
        {
            AppPaths.EnsureCreated();
            using var processMutex = new Mutex(false, MutexName(_path));
            var ownsMutex = false;
            var tmpPath = _path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                try
                {
                    ownsMutex = processMutex.WaitOne(TimeSpan.FromSeconds(10));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                    throw new IOException($"Timed out waiting to save local data: {_path}");

                using (var stream = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, bufferSize: 64 * 1024, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, items.ToList(), Options);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    MoveWithRetry(tmpPath, _path);
                    TryDelete(_recoveryPath);
                }
                catch (Exception primaryError) when (primaryError is IOException or UnauthorizedAccessException)
                {
                    // Keep a durable copy even when another process (for example,
                    // antivirus or a stale app instance) holds the primary file.
                    // Load() will use it until a later save can update the primary.
                    try
                    {
                        MoveWithRetry(tmpPath, _recoveryPath);
                    }
                    catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
                    {
                        throw new IOException(
                            $"Could not save local data to either '{_path}' or its recovery copy.",
                            new AggregateException(primaryError, recoveryError));
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (ownsMutex) processMutex.ReleaseMutex();
            }
        }
    }

    private static string MutexName(string path)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        // App instances started in separate Windows sessions share the roaming
        // profile but do not share Local\ mutexes. Global\ serializes those writes.
        return @"Global\Comunicador.JsonStore." + Convert.ToHexString(digest);
    }

    private static void MoveWithRetry(string source, string destination)
    {
        const int attempts = 4;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < attempts - 1)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static bool TryLoad(string path, out List<T> items)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            items = JsonSerializer.Deserialize<List<T>>(stream, Options) ?? new List<T>();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            items = new List<T>();
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
