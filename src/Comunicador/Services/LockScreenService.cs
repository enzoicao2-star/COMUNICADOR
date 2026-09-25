using System.Diagnostics;
using System.IO;
using System.Text;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

public static class LockScreenService
{
    // Windows PowerShell 5.1 exposes the WinRT desktop projection without an extra runtime/package.
    // Use the current user's session: the Windows lock screen is a per-user setting.
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $storage = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
        $lockScreen = [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime]
        $methods = [System.WindowsRuntimeSystemExtensions].GetMethods()
        $loadMethod = $methods | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
        $loadTask = $loadMethod.MakeGenericMethod($storage).Invoke($null, @($storage::GetFileFromPathAsync($env:COMUNICADOR_LOCK_IMAGE_PATH)))
        $loadTask.Wait()
        $applyMethod = $methods | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
        $applyTask = $applyMethod.Invoke($null, @($lockScreen::SetImageFileAsync($loadTask.Result)))
        $applyTask.Wait()
        """;

    public static async Task ApplyAsync(ConteudoImagem image, CancellationToken cancellationToken = default)
    {
        var extension = image.MimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => throw new InvalidDataException("Use uma imagem PNG ou JPEG para a tela de bloqueio."),
        };

        AppPaths.EnsureCreated();
        var path = Path.Combine(AppPaths.LocalSharedDir, $"lock-screen-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, Convert.FromBase64String(image.DataBase64));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        start.Environment["COMUNICADOR_LOCK_IMAGE_PATH"] = path;

        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o Windows PowerShell.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            throw new IOException("O Windows demorou demais para alterar a tela de bloqueio.");
        }
        var error = await errorTask.ConfigureAwait(false);
        _ = await outputTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw new IOException($"O Windows recusou a imagem da tela de bloqueio: {error.Trim()}");
        }

        try
        {
            foreach (var oldFile in Directory.EnumerateFiles(AppPaths.LocalSharedDir, "lock-screen-*")
                         .Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(oldFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
