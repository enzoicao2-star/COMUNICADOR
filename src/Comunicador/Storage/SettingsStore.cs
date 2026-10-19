using System.IO;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Services;

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
        var json = AtomicFileStore.ReadAllTextWithRecovery(AppPaths.ConfiguracoesFile);
        if (json is null)
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate();
            Save(fresh);
            return fresh;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.AceitarMensagensDeOutrosPaineis = true;
            var stableId = DeviceIdentityStore.GetOrCreate(settings.PainelId);
            if (!string.Equals(settings.PainelId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                settings.PainelId = stableId;
                Save(settings);
            }
            TryMarkPanelInstalled(settings);
            if (settings.VersaoConfiguracao < 2)
            {
                settings.VersaoConfiguracao = 2;
                settings.IntervaloPingSegundos = 5;
                settings.IntensidadeFundo = 100;
                Save(settings);
            }
            if (settings.VersaoConfiguracao < 3)
            {
                settings.VersaoConfiguracao = 3;
                settings.IniciarComWindows = true;
                settings.PreferenciaInicializacaoComWindows = true;
                Save(settings);
            }
            if (settings.VersaoConfiguracao < 4)
            {
                settings.VersaoConfiguracao = 4;
                settings.TransicoesAtivas = false;
                Save(settings);
            }
            return settings;
        }
        catch (JsonException)
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate();
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
            AtomicFileStore.WriteAllTextWithRecovery(AppPaths.ConfiguracoesFile, json);
            TryMarkPanelInstalled(settings);
        }
    }

    private static void TryMarkPanelInstalled(AppSettings settings)
    {
        try
        {
            DeviceIdentityStore.MarkPanelInstalled(
                settings.PainelId, hasPanel: true,
                mediaBlocked: !settings.AceitarImagensDeOutrosPaineis);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"A identificação local será atualizada novamente: {ex.Message}", "armazenamento");
        }
    }
}
