using System.IO;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>Load/save helper for a JSON-serialized list persisted to a single file, one per model type.</summary>
public sealed class JsonStore<T>
{
    private static readonly JsonSerializerOptions Options = new();
    private readonly string _path;
    private readonly object _lock = new();

    public JsonStore(string path)
    {
        _path = path;
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
                using var stream = File.OpenRead(_path);
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
            var tmpPath = _path + ".tmp";
            using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write,
                       FileShare.None, bufferSize: 64 * 1024))
            {
                JsonSerializer.Serialize(stream, items, Options);
            }
            File.Move(tmpPath, _path, overwrite: true);
        }
    }
}
