using System.IO;

namespace Comunicador.Services;

/// <summary>Marcadores locais permitem ao painel e ao receptor cancelar entre processos.</summary>
public static class RemoteCommandCancellation
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static bool Request(string? requestId)
    {
        var path = GetPath(requestId);
        if (path is null) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool IsRequested(string? requestId)
    {
        var path = GetPath(requestId);
        if (path is null || !File.Exists(path)) return false;
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= MaxAge) return true;
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    public static void Clear(string? requestId)
    {
        var path = GetPath(requestId);
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? GetPath(string? requestId) => Guid.TryParse(requestId, out var id)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Comunicador", "RemoteCmdCancel", $"{id:N}.cancel")
        : null;
}
