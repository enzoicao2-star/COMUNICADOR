using System.IO;
using System.Text.Json;
using Comunicador.Models;

namespace Comunicador.Storage;

/// <summary>Loads/saves the single <see cref="AppSettings"/> object. On first run it generates and
/// persists a stable PainelId immediately, since that id must survive process restarts.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.ConfiguracoesFile))
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate(fresh.PainelId);
            Save(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.ConfiguracoesFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.AceitarMensagensDeOutrosPaineis = true;
            var stableId = DeviceIdentityStore.GetOrCreate(settings.PainelId);
            if (!string.Equals(settings.PainelId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                settings.PainelId = stableId;
                Save(settings);
            }
            DeviceIdentityStore.MarkPanelInstalled(settings.PainelId);
            if (settings.VersaoConfiguracao < 2)
            {
                settings.VersaoConfiguracao = 2;
                settings.IntervaloPingSegundos = 5;
                settings.IntensidadeFundo = 100;
                Save(settings);
            }
            return settings;
        }
        catch (JsonException)
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate(fresh.PainelId);
            Save(fresh);
            return fresh;
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate)
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(settings, Options);
            var temporario = AppPaths.ConfiguracoesFile + ".tmp";
            File.WriteAllText(temporario, json);
            File.Move(temporario, AppPaths.ConfiguracoesFile, overwrite: true);
            DeviceIdentityStore.MarkPanelInstalled(
                settings.PainelId, hasPanel: true,
                mediaBlocked: !settings.AceitarImagensDeOutrosPaineis);
        }
    }
}
