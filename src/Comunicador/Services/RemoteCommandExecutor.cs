using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Comunicador.Services;

/// <summary>Executa um único comando com o token normal da sessão do Comunicador.</summary>
public static class RemoteCommandExecutor
{
    public const int MaxCommandLength = 500;
    public const int MaxResultLength = 950; // respond_to_delivery limita a resposta a 1000 caracteres.
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);

    public static bool IsValid(string? command) => !string.IsNullOrWhiteSpace(command)
        && command.Length <= MaxCommandLength && !command.Contains('\0');

    public static bool IsFresh(string? expiresAt) =>
        DateTimeOffset.TryParse(expiresAt, out var expiry)
        && expiry >= DateTimeOffset.UtcNow
        && expiry <= DateTimeOffset.UtcNow.AddMinutes(6);

    public static (string? Action, string? Value, string? Error) ParseAudioCommand(string command)
    {
        var input = command.Trim().ToLowerInvariant();
        if (input == "audio devices") return ("list", string.Empty, null);
        var select = Regex.Match(input, @"^audio select\s+(\d+)$", RegexOptions.CultureInvariant);
        if (select.Success)
            return int.TryParse(select.Groups[1].Value, out var index) && index is >= 1 and <= 50
                ? ("select", index.ToString(), null) : (null, null, "Escolha um dispositivo entre 1 e 50.");
        var volume = Regex.Match(input, @"^volume\s+(\d+)$", RegexOptions.CultureInvariant);
        if (volume.Success)
            return int.TryParse(volume.Groups[1].Value, out var percent) && percent is >= 0 and <= 100
                ? ("volume", percent.ToString(), null) : (null, null, "Volume deve estar entre 0 e 100.");
        if (input.StartsWith("audio ") || input.StartsWith("volume "))
            return (null, null, "Use audio devices, audio select N ou volume N.");
        return (null, null, null);
    }

    public static async Task<string> RunAsync(string command, CancellationToken ct = default)
    {
        if (!IsValid(command)) return "Comando vazio ou longo demais (máximo de 500 caracteres).";
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return "Comando recusado: o Comunicador está aberto como administrador. Abra-o normalmente para usar o CMD remoto.";

        var audio = ParseAudioCommand(command);
        if (audio.Error is not null) return audio.Error;
        var startInfo = new ProcessStartInfo(audio.Action is null ? "cmd.exe" : "powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (audio.Action is null)
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            using var stream = typeof(RemoteCommandExecutor).Assembly
                .GetManifestResourceStream("Comunicador.Receiver.audio-control.ps1")
                ?? throw new InvalidOperationException("Controle de áudio não incluído no painel.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var script = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            startInfo.Environment["COMUNICADOR_AUDIO_ACTION"] = audio.Action;
            startInfo.Environment["COMUNICADOR_AUDIO_VALUE"] = audio.Value ?? string.Empty;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(MaxDuration);
            var stdout = DrainAsync(process.StandardOutput, MaxResultLength);
            var stderr = DrainAsync(process.StandardError, MaxResultLength);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                return ct.IsCancellationRequested ? "Comando cancelado." : "Comando encerrado após 30 segundos.";
            }
            var output = (await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false)).Trim();
            var result = $"Código de saída: {process.ExitCode}\n{output}";
            return result.Length <= MaxResultLength ? result : result[..MaxResultLength] + "…";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"Falha ao executar CMD: {ex.Message}"[..Math.Min(MaxResultLength, $"Falha ao executar CMD: {ex.Message}".Length)];
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, int limit)
    {
        var buffer = new char[1024];
        var output = new StringBuilder(Math.Min(limit, 1024));
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            if (output.Length < limit) output.Append(buffer, 0, Math.Min(read, limit - output.Length));
        return output.ToString();
    }
}
