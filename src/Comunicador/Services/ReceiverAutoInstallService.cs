using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

/// <summary>
/// Installs the standalone receiver once per Windows user so it keeps serving
/// this machine after the panel window is closed. The installer runs hidden and
/// records diagnostics locally; setup failures never interrupt panel startup.
/// </summary>
public static class ReceiverAutoInstallService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static int _started;

#if TEST_BUILD
    private const string InstallerUrl = "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/teste/INSTALAR_RECEPTOR_TESTE.bat";
    private const int ReceiverPort = 58931;
#else
    private const string InstallerUrl = "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/receiver/INSTALAR_RECEPTOR.bat";
    private const int ReceiverPort = 57931;
#endif

    private static string ReceiverRoot => Path.Combine(AppPaths.LocalSharedDir, "Receptor");
    private static string ReceiverScript => Path.Combine(ReceiverRoot, "app", "receptor.py");
    private static string InstallerMarker => Path.Combine(ReceiverRoot, "instalado-pelo-painel.marker");
    private static string PythonwPathFile => Path.Combine(ReceiverRoot, "pythonw.path");
    private static string InstallLog => Path.Combine(AppPaths.LocalSharedDir, "instalacao-receptor.log");

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(EnsureInstalledAsync);
    }

    private static async Task EnsureInstalledAsync()
    {
        try
        {
            AppPaths.EnsureCreated();
            Directory.CreateDirectory(ReceiverRoot);

            if (File.Exists(InstallerMarker) && File.Exists(ReceiverScript))
            {
                // The Windows logon task normally starts the receiver first.
                // Give it a moment before attempting recovery to avoid a second
                // process during simultaneous logon and panel startup.
                if (await WaitForReceiverAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false)) return;

                if (TryStartInstalledReceiver()
                    && await WaitForReceiverAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                {
                    Logger.Info("Receptor separado reativado pelo painel.");
                    return;
                }
            }

            await InstallLatestAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await AppendLogAsync($"Falha na preparação automática: {ex}").ConfigureAwait(false);
            Logger.Warning($"A instalação automática do receptor não concluiu; o painel continuará aberto. Detalhes em {InstallLog}");
        }
    }

    private static async Task InstallLatestAsync()
    {
        var installerPath = Path.Combine(Path.GetTempPath(), $"Comunicador-Receptor-{Guid.NewGuid():N}.bat");
        try
        {
            using var response = await Http.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var installer = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (installer.Length < 1024)
                throw new InvalidDataException("O instalador baixado está incompleto.");

            await File.WriteAllBytesAsync(installerPath, installer).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = $"/d /s /c \"\"{installerPath}\" --automatic\"",
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Não foi possível iniciar o instalador oculto do receptor.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            await AppendLogAsync($"Instalador finalizado com código {process.ExitCode}.{Environment.NewLine}{await stdout.ConfigureAwait(false)}{await stderr.ConfigureAwait(false)}").ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(InstallerMarker) || !File.Exists(ReceiverScript))
                throw new InvalidOperationException($"O instalador oculto não confirmou a instalação (código {process.ExitCode}).");

            if (!await WaitForReceiverAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false))
                Logger.Warning($"O receptor foi instalado, mas ainda não respondeu na porta {ReceiverPort}. Consulte {InstallLog}");
            else
                Logger.Info("Receptor separado instalado e ativo; continuará iniciando com o Windows.");
        }
        finally
        {
            try { File.Delete(installerPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool TryStartInstalledReceiver()
    {
        try
        {
            if (!File.Exists(PythonwPathFile)) return false;
            var pythonw = File.ReadAllText(PythonwPathFile, Encoding.UTF8).Trim();
            if (!File.Exists(pythonw) || !File.Exists(ReceiverScript)) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonw,
                Arguments = $"\"{ReceiverScript}\"",
                WorkingDirectory = Path.GetDirectoryName(ReceiverScript)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Logger.Warning($"Não foi possível reativar o receptor instalado: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> WaitForReceiverAsync(TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, ReceiverPort, deadline.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                if (deadline.IsCancellationRequested) break;
                try { await Task.Delay(300, deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        return false;
    }

    private static async Task AppendLogAsync(string message)
    {
        try
        {
            await File.AppendAllTextAsync(
                InstallLog,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}",
                Encoding.UTF8).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"Não foi possível salvar o log do instalador do receptor: {ex.Message}");
        }
    }
}
