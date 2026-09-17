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

    public async Task<PanelUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
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
    }

    public async Task StartUpdateAsync(PanelUpdateInfo info, CancellationToken cancellationToken = default)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível localizar o executável atual.");
        var updater = FindUpdater(executable);
        if (updater is null)
        {
            updater = Path.Combine(Path.GetTempPath(), $"Atualizar-Comunicador-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(updater, await Http.GetStringAsync(UpdaterUrl, cancellationToken), cancellationToken);
        }

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
        process.ArgumentList.Add("-ExecutablePath");
        process.ArgumentList.Add(executable);
        var root = FindInstallRoot(executable);
        if (root is not null)
        {
            process.ArgumentList.Add("-InstallRoot");
            process.ArgumentList.Add(root);
        }
        process.ArgumentList.Add("-RestartAfterUpdate");
        var started = Process.Start(process);
        if (started is null) throw new InvalidOperationException("O atualizador não pôde ser iniciado.");
    }

    public string? ConsumeUpdateSummary()
    {
        var path = Path.Combine(AppPaths.RootDir, "ultima-atualizacao.json");
        if (!File.Exists(path)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<PanelManifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            File.Delete(path);
            if (manifest is null) return null;
            var changes = manifest.Changes is { Length: > 0 }
                ? "\n\n" + string.Join("\n", manifest.Changes.Select(c => $"• {c}")) : string.Empty;
            return $"Comunicador atualizado para {manifest.Version}.\n\n{manifest.Summary}{changes}".Trim();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
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
        public string? Summary { get; set; }
        public string[]? Changes { get; set; }
    }
}

public sealed record PanelUpdateInfo(
    Version CurrentVersion, Version LatestVersion, bool IsAvailable, string Summary, IReadOnlyList<string> Changes);
