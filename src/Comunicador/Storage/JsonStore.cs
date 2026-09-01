using System.IO;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>Load/save helper for a JSON-serialized list persisted to a single file, one per model type.</summary>
public sealed class JsonStore<T>
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
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
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
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
            var json = JsonSerializer.Serialize(items.ToList(), Options);
            var tmpPath = _path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Copy(tmpPath, _path, overwrite: true);
            File.Delete(tmpPath);
        }
    }
}
