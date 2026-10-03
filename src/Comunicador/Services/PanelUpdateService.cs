using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class PanelUpdateService
{
    private const string ManifestUrl = "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/release/panel-version.json";
    private const string UpdaterUrl = "https://raw.githubusercontent.com/enzoicao2-star/COMUNICADOR/main/tools/Atualizar-Comunicador.ps1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    public string ProgressFilePath => Path.Combine(AppPaths.RootDir, "atualizacao-painel-progresso.json");

    public async Task<PanelUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
#if TEST_BUILD
        // O executavel de teste nunca consulta nem instala uma release estavel.
        return new PanelUpdateInfo(CurrentVersion, CurrentVersion, false,
            "Versão de teste isolada.", Array.Empty<string>());
#else
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{ManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<PanelManifest>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Manifesto de atualização vazio.");
        if (!Version.TryParse(manifest.Version, out var latest))
            throw new InvalidDataException("Versão publicada inválida.");
        return new PanelUpdateInfo(CurrentVersion, latest, latest > CurrentVersion,
            manifest.Summary ?? string.Empty, manifest.Changes ?? Array.Empty<string>());
#endif
    }

    public async Task StartUpdateAsync(
        PanelUpdateInfo info, CancellationToken cancellationToken = default, bool forceReinstall = false)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível localizar o executável atual.");
        var updater = FindUpdater(executable);
        if (updater is null || !SupportsStagedRestart(updater))
        {
            try
            {
                var updatedScript = await Http.GetStringAsync(
                    $"{UpdaterUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", cancellationToken);
                updater = Path.Combine(AppPaths.RootDir, "Atualizar-Comunicador-atualizado.ps1");
                await File.WriteAllTextAsync(updater, updatedScript, cancellationToken);
            }
            catch (Exception ex) when (updater is not null
                && ex is HttpRequestException or IOException or UnauthorizedAccessException) { }
        }
        if (updater is null) throw new InvalidOperationException("Não foi possível localizar o atualizador do Comunicador.");
        if (!SupportsStagedRestart(updater))
            throw new InvalidOperationException("Não foi possível obter o atualizador com reinício programado.");
        var suportaProgresso = SupportsProgress(updater);

        var process = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        process.ArgumentList.Add("-NoProfile");
        process.ArgumentList.Add("-NonInteractive");
        process.ArgumentList.Add("-ExecutionPolicy");
        process.ArgumentList.Add("Bypass");
        process.ArgumentList.Add("-File");
        process.ArgumentList.Add(updater);
        if (suportaProgresso)
        {
            if (File.Exists(ProgressFilePath)) File.Delete(ProgressFilePath);
            process.ArgumentList.Add("-ProgressPath");
            process.ArgumentList.Add(ProgressFilePath);
            process.ArgumentList.Add("-PanelProcessId");
            process.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        process.ArgumentList.Add("-ExecutablePath");
        process.ArgumentList.Add(executable);
        var root = FindInstallRoot(executable);
        if (root is not null)
        {
            process.ArgumentList.Add("-InstallRoot");
            process.ArgumentList.Add(root);
        }
        process.ArgumentList.Add("-RestartAfterUpdate");
        if (forceReinstall) process.ArgumentList.Add("-ForceReinstall");
        using var started = Process.Start(process);
        if (started is null) throw new InvalidOperationException("O atualizador não pôde ser iniciado.");
    }

    /// <summary>Confirma ao atualizador que a nova interface continuou ativa após abrir.
    /// A confirmação é ligada à instalação pendente por um token exclusivo.</summary>
    public static async Task ReportHealthyStartupAsync()
    {
        var executable = Environment.ProcessPath;
        var directory = executable is null ? null : Path.GetDirectoryName(executable);
        if (directory is null) return;
        var pendingPath = Path.Combine(directory, "Comunicador.atualizacao-pendente.json");
        if (!File.Exists(pendingPath)) return;
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            if (!File.Exists(pendingPath)) return;
            using var pending = JsonDocument.Parse(await File.ReadAllTextAsync(pendingPath).ConfigureAwait(false));
            var root = pending.RootElement;
            var token = root.GetProperty("token").GetString();
            var version = root.GetProperty("version").GetString();
            var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            if (string.IsNullOrWhiteSpace(token) || version != current) return;
            var healthPath = Path.Combine(directory, "Comunicador.inicializacao-ok.json");
            var temporary = healthPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                token,
                version,
                process_id = Environment.ProcessId,
                confirmed_at = DateTimeOffset.UtcNow,
            })).ConfigureAwait(false);
            File.Move(temporary, healthPath, true);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Não foi possível confirmar a abertura após a atualização: {ex.Message}");
        }
    }

    public string? ConsumeUpdateSummary()
    {
        var path = Path.Combine(AppPaths.RootDir, "ultima-atualizacao.json");
        if (!File.Exists(path)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<PanelManifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null) return null;
            var previousText = manifest.PreviousVersion;
            if (string.IsNullOrWhiteSpace(previousText))
            {
                var executable = Environment.ProcessPath;
                var backup = executable is null ? null : Path.Combine(
                    Path.GetDirectoryName(executable)!, "Comunicador.anterior.exe");
                if (backup is not null && File.Exists(backup))
                    previousText = FileVersionInfo.GetVersionInfo(backup).FileVersion;
            }
            if (Version.TryParse(previousText, out var previous)
                && Version.TryParse(manifest.Version, out var current))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "Comunicador.Release.panel-changelog.json");
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    var summary = PanelChangelog.Format(reader.ReadToEnd(), previous, current,
                        manifest.Summary ?? string.Empty, manifest.Changes ?? []);
                    File.Delete(path);
                    return summary;
                }
            }
            var changes = manifest.Changes is { Length: > 0 }
                ? "\n\n" + string.Join("\n", manifest.Changes.Select(c => $"• {c}")) : string.Empty;
            var fallback = $"Comunicador atualizado para {manifest.Version}.\n\n{manifest.Summary}{changes}".Trim();
            File.Delete(path);
            return fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FindUpdater(string executable)
    {
        var directory = Path.GetDirectoryName(executable);
        if (directory is null) return null;
        foreach (var root in new[] { directory, Directory.GetParent(directory)?.FullName })
        {
            if (root is null) continue;
            var candidate = Path.Combine(root, "tools", "Atualizar-Comunicador.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool SupportsProgress(string path)
    {
        try { return File.ReadAllText(path).Contains("[string]$ProgressPath", StringComparison.Ordinal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool SupportsStagedRestart(string path)
    {
        try { return File.ReadAllText(path).Contains("'download_complete'", StringComparison.Ordinal); }
        catch { return false; }
    }

    private static string? FindInstallRoot(string executable)
    {
        var directory = Path.GetDirectoryName(executable);
        if (directory is null || !Path.GetFileName(directory).Equals("release", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Directory.GetParent(directory)?.FullName;
        return root is not null && File.Exists(Path.Combine(root, "ABRIR_COMUNICADOR.bat")) ? root : null;
    }

    private sealed class PanelManifest
    {
        public string Version { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("previous_version")]
        public string? PreviousVersion { get; set; }
        public string? Summary { get; set; }
        public string[]? Changes { get; set; }
    }
}

public sealed record PanelUpdateInfo(
    Version CurrentVersion, Version LatestVersion, bool IsAvailable, string Summary, IReadOnlyList<string> Changes);

public sealed class PanelUpdateProgress
{
    public string Phase { get; set; } = "running";
    public int Percent { get; set; }
    public string Message { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("remaining_seconds")]
    public int RemainingSeconds { get; set; }
}
