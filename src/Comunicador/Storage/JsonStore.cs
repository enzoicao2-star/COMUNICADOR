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
    private readonly object _lock;

    public JsonStore(string path)
    {
        _path = Path.GetFullPath(path);
        _lock = PathLocks.GetOrAdd(_path, static _ => new object());
    }

    public List<T> Load()
    {
        lock (_lock)
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(_path))
            {
                return new List<T>();
            }

            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
                return JsonSerializer.Deserialize<List<T>>(stream, Options) ?? new List<T>();
            }
            catch (JsonException)
            {
                return new List<T>();
            }
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

                MoveWithRetry(tmpPath, _path);
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
        return @"Local\Comunicador.JsonStore." + Convert.ToHexString(digest);
    }

    private static void MoveWithRetry(string source, string destination)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < 7)
            {
                lastError = ex;
                Thread.Sleep(25 * (attempt + 1));
            }
        }

        throw lastError ?? new IOException($"Could not replace local data file: {destination}");
    }
}
