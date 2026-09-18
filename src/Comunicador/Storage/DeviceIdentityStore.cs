using System.IO;
using System.Text.Json;

namespace Comunicador.Storage;

/// <summary>Identidade única desta instalação. Não há opção de edição na interface;
/// painel e receptor usam o mesmo arquivo para representar um único computador.</summary>
public static class DeviceIdentityStore
{
    private static readonly object Gate = new();

    public static string GetOrCreate(string? legacyPanelId = null)
    {
        lock (Gate)
        {
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

            var id = Guid.TryParse(legacyPanelId, out var legacy) ? legacy : Guid.NewGuid();
            Save(id.ToString(), hasPanel: true);
            return id.ToString();
        }
    }

    public static void MarkPanelInstalled(
        string deviceId, bool hasPanel = true, bool? mediaBlocked = null) =>
        Save(deviceId, hasPanel, mediaBlocked);

    private static void Save(string deviceId, bool hasPanel, bool? mediaBlocked = null)
    {
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
            panel_version = Protocol.ProtocolConstants.CurrentPanelVersion,
            media_blocked = blocked,
            created_at = createdAt,
        }, new JsonSerializerOptions { WriteIndented = true });
        var temporary = AppPaths.DeviceIdentityFile + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, AppPaths.DeviceIdentityFile, true);
    }
}
