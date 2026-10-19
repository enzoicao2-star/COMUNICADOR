using System.Security.Cryptography;
using System.Text;
using Comunicador.Protocol;

using System.IO;

namespace Comunicador.Services;

/// <summary>Guarda miniaturas por computador e monitor para que a seleção de destino
/// abra imediatamente, mesmo quando a rede estiver lenta ou indisponível.</summary>
public static class WallpaperPreviewStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Comunicador", "WallpaperPreviews");

    public static byte[]? Load(string computerId, int monitorIndex)
    {
        try
        {
            var path = GetPath(computerId, monitorIndex);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length is > 0 and <= 512 * 1024 ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static bool IsStale(string computerId, int monitorIndex, TimeSpan maximumAge)
    {
        try
        {
            var path = GetPath(computerId, monitorIndex);
            return !File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= maximumAge;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    public static void Save(string computerId, int monitorIndex, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(computerId) || monitorIndex is < 0 or >= ProtocolConstants.MaxMonitors
            || bytes.Length is 0 or > 512 * 1024
            || !ConteudoImagem.AssinaturaCorresponde("image/jpeg", bytes)) return;

        Directory.CreateDirectory(Folder);
        var path = GetPath(computerId, monitorIndex);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string GetPath(string computerId, int monitorIndex)
    {
        if (string.IsNullOrWhiteSpace(computerId)) throw new ArgumentException("Computer ID is required.");
        var key = Encoding.UTF8.GetBytes($"{computerId.Trim().ToLowerInvariant()}:{monitorIndex}");
        var file = Convert.ToHexString(SHA256.HashData(key)).ToLowerInvariant() + ".jpg";
        return Path.Combine(Folder, file);
    }
}
