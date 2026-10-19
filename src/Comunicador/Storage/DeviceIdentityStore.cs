using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>Identidade única desta instalação. Não há opção de edição na interface;
/// painel e receptor usam o mesmo arquivo para representar um único computador.</summary>
public static class DeviceIdentityStore
{
    private static readonly object Gate = new();
    private static readonly Mutex CrossSessionGate = new(false, BuildCrossSessionMutexName());

    public static string GetOrCreate(string? legacyPanelId = null)
    {
        lock (Gate)
        {
            using var processLock = EnterCrossSessionGate();
            AppPaths.EnsureCreated();
            try
            {
                if (File.Exists(AppPaths.DeviceIdentityFile))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.DeviceIdentityFile));
                    if (document.RootElement.TryGetProperty("device_id", out var value)
                        && Guid.TryParse(value.GetString(), out var existing))
                        return existing.ToString();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Um arquivo inválido é recriado de forma atômica abaixo.
            }

            var id = Guid.TryParse(legacyPanelId, out var legacy)
                ? legacy
                : LerIdentidadeDoReceptor() ?? Guid.NewGuid();
            Save(id.ToString(), hasPanel: true);
            return id.ToString();
        }
    }

    private static Guid? LerIdentidadeDoReceptor()
    {
        try
        {
            var path = Path.Combine(AppPaths.LocalSharedDir, "Receptor", "config.json");
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("computer_id", out var value)
                && Guid.TryParse(value.GetString(), out var id) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void MarkPanelInstalled(
        string deviceId, bool hasPanel = true, bool? mediaBlocked = null) =>
        Save(deviceId, hasPanel, mediaBlocked);

    public static void MarkPanelUninstalled(string deviceId) =>
        Save(deviceId, hasPanel: false, mediaBlocked: null, panelUninstalled: true);

    private static void Save(
        string deviceId, bool hasPanel, bool? mediaBlocked = null, bool panelUninstalled = false)
    {
        lock (Gate)
        {
            using var processLock = EnterCrossSessionGate();
            AppPaths.EnsureCreated();
            var createdAt = DateTime.UtcNow.ToString("o");
            var blocked = mediaBlocked ?? false;
            try
            {
                if (File.Exists(AppPaths.DeviceIdentityFile))
                {
                    using var current = JsonDocument.Parse(File.ReadAllText(AppPaths.DeviceIdentityFile));
                    if (current.RootElement.TryGetProperty("created_at", out var created))
                        createdAt = created.GetString() ?? createdAt;
                    if (!mediaBlocked.HasValue && current.RootElement.TryGetProperty("media_blocked", out var media))
                        blocked = media.GetBoolean();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            var payload = JsonSerializer.Serialize(new
            {
                device_id = deviceId,
                has_panel = hasPanel,
                panel_version = hasPanel ? Protocol.ProtocolConstants.CurrentPanelVersion : null,
                panel_uninstalled = panelUninstalled,
                media_blocked = blocked,
                created_at = createdAt,
            }, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileStore.WriteAllText(AppPaths.DeviceIdentityFile, payload);
        }
    }

    private static IDisposable EnterCrossSessionGate()
    {
        var ownsMutex = false;
        try { ownsMutex = CrossSessionGate.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) throw new IOException("Timed out waiting to load or save the device identity.");
        return new MutexLease(CrossSessionGate);
    }

    private static string BuildCrossSessionMutexName()
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DeviceIdentityFile.ToUpperInvariant()));
        return @"Global\Comunicador.DeviceIdentity." + Convert.ToHexString(digest);
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose() => mutex.ReleaseMutex();
    }
}
