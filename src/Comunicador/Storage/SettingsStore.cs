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
    private static int _noncriticalSaveFailureLogged;

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        var json = AtomicFileStore.ReadAllTextWithRecovery(AppPaths.ConfiguracoesFile);
        if (json is null)
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate();
            SaveBestEffort(fresh);
            return fresh;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            settings.AceitarMensagensDeOutrosPaineis = true;
            var stableId = DeviceIdentityStore.GetOrCreate(settings.PainelId);
            var needsSave = false;
            if (!string.Equals(settings.PainelId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                settings.PainelId = stableId;
                needsSave = true;
            }
            TryMarkPanelInstalled(settings);
            if (settings.VersaoConfiguracao < 2)
            {
                settings.VersaoConfiguracao = 2;
                settings.IntervaloPingSegundos = 5;
                settings.IntensidadeFundo = 100;
                needsSave = true;
            }
            if (settings.VersaoConfiguracao < 3)
            {
                settings.VersaoConfiguracao = 3;
                settings.IniciarComWindows = true;
                settings.PreferenciaInicializacaoComWindows = true;
                needsSave = true;
            }
            if (settings.VersaoConfiguracao < 4)
            {
                settings.VersaoConfiguracao = 4;
                settings.TransicoesAtivas = false;
                needsSave = true;
            }
            if (needsSave) SaveBestEffort(settings);
            return settings;
        }
        catch (JsonException)
        {
            var fresh = new AppSettings();
            fresh.PainelId = DeviceIdentityStore.GetOrCreate();
            SaveBestEffort(fresh);
            return fresh;
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate)
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(settings, Options);
            if (!string.Equals(AtomicFileStore.ReadAllTextWithRecovery(AppPaths.ConfiguracoesFile),
                json, StringComparison.Ordinal))
                AtomicFileStore.WriteAllTextWithRecovery(AppPaths.ConfiguracoesFile, json);
            TryMarkPanelInstalled(settings);
        }
    }

    public static bool SaveBestEffort(AppSettings settings)
    {
        try
        {
            Save(settings);
            Interlocked.Exchange(ref _noncriticalSaveFailureLogged, 0);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Interlocked.Exchange(ref _noncriticalSaveFailureLogged, 1) == 0)
                Logger.Warning($"As preferências continuam em memória; o arquivo local será atualizado depois: {ex.Message}",
                    "armazenamento");
            return false;
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
