using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        AtomicFileStore.WriteAllBytes(path, bytes);
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

    public static ConteudoImagem? GetPreview()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            var path = key?.GetValue("WallPaper") as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                var themes = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes");
                var transcoded = Path.Combine(themes, "TranscodedWallpaper");
                path = File.Exists(transcoded) ? transcoded : Directory.Exists(Path.Combine(themes, "CachedFiles"))
                    ? Directory.EnumerateFiles(Path.Combine(themes, "CachedFiles"))
                        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            BitmapSource source = decoder.Frames[0];
            var scale = Math.Min(1d, Math.Min(480d / source.PixelWidth, 270d / source.PixelHeight));
            if (scale < 1)
            {
                var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                resized.Freeze();
                source = resized;
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            var bytes = output.ToArray();
            return new ConteudoImagem
            {
                Name = Path.GetFileName(path),
                MimeType = "image/jpeg",
                DataBase64 = Convert.ToBase64String(bytes),
                RawData = bytes,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static void SaveOriginalWallpaper()
    {
        var backup = Path.Combine(AppPaths.LocalSharedDir, "wallpaper-original.txt");
        if (File.Exists(backup)) return;
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
        var originalPath = key?.GetValue("WallPaper") as string;
        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
            AtomicFileStore.WriteAllText(backup, originalPath);
    }
}
