using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
        var path = Path.Combine(AppPaths.LocalSharedDir, "wallpaper" + extension);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, path, UpdateIniFile | SendWinIniChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return path;
    }
}
