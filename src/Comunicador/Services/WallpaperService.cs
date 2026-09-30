using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

public static class WallpaperService
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint UpdateIniFile = 0x01;
    private const uint SendWinIniChange = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint action, uint parameter, string value, uint flags);

    public static string Apply(ConteudoImagem image)
    {
        var extension = image.MimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => throw new InvalidDataException("Formato de papel de parede não suportado."),
        };
        var bytes = Convert.FromBase64String(image.DataBase64);
        AppPaths.EnsureCreated();
        SaveOriginalWallpaper();
        var path = Path.Combine(AppPaths.LocalSharedDir, "wallpaper" + extension);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, path, UpdateIniFile | SendWinIniChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return path;
    }

    public static string Restore()
    {
        var backup = Path.Combine(AppPaths.LocalSharedDir, "wallpaper-original.txt");
        if (!File.Exists(backup))
            throw new FileNotFoundException("Este computador ainda não tem um papel de parede anterior salvo.");
        var originalPath = File.ReadAllText(backup).Trim();
        if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
            throw new FileNotFoundException("O arquivo do papel de parede anterior não está mais disponível.");
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, originalPath, UpdateIniFile | SendWinIniChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        File.Delete(backup);
        return originalPath;
    }

    private static void SaveOriginalWallpaper()
    {
        var backup = Path.Combine(AppPaths.LocalSharedDir, "wallpaper-original.txt");
        if (File.Exists(backup)) return;
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
        var originalPath = key?.GetValue("WallPaper") as string;
        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
            File.WriteAllText(backup, originalPath);
    }
}
