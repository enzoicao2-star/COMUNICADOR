using System.IO;
using System.Text.Json;
using Comunicador.Models;

namespace Comunicador.Storage;

/// <summary>Loads/saves the single <see cref="AppSettings"/> object. On first run it generates and
/// persists a stable PainelId immediately, since that id must survive process restarts.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.ConfiguracoesFile))
        {
            var fresh = new AppSettings();
            Save(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.ConfiguracoesFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            var fresh = new AppSettings();
            Save(fresh);
            return fresh;
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(AppPaths.ConfiguracoesFile, json);
    }
}
