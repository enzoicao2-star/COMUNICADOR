using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Comunicador.Protocol;
using Comunicador.Storage;
using Microsoft.Win32;

namespace Comunicador.Services;

/// <summary>Stages images independently, then atomically selects the complete set.
/// A separate per-user worker keeps changing the images after the panel closes.</summary>
public static class CarouselService
{
    private static readonly object Gate = new();
    private static readonly string Root = Path.Combine(AppPaths.LocalSharedDir, "Carousel");
    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ComunicadorCarousel";

    private sealed class StoredCarousel
    {
        [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
        [JsonPropertyName("folder")] public string Folder { get; set; } = "";
        [JsonPropertyName("target")] public string Target { get; set; } = "center_image";
        [JsonPropertyName("duration_seconds")] public int DurationSeconds { get; set; } = 15;
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("min_minutes")] public int MinMinutes { get; set; }
        [JsonPropertyName("max_minutes")] public int MaxMinutes { get; set; }
        [JsonPropertyName("repeat")] public bool Repeat { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
        [JsonPropertyName("received")] public Dictionary<int, string> Received { get; set; } = [];
    }

    public static bool DisableLegacyCarousel()
    {
        lock (Gate)
        {
            var activePath = Path.Combine(Root, "active.json");
            var active = Read(activePath);
            if (active is null || !active.Enabled || active.Target == "center_image") return false;
            active.Enabled = false;
            Save(activePath, active);
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunName, throwOnMissingValue: false);
            return true;
        }
    }

    public static string Handle(CarouselCommand command, ConteudoImagem? image)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Root);
            if (command.Action == "stop")
            {
                var activePath = Path.Combine(Root, "active.json");
                var active = Read(activePath);
                if (active is not null)
                {
                    active.Enabled = false;
                    Save(activePath, active);
                }
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(RunName, throwOnMissingValue: false);
                WaitForWorkerStop();
                return "carousel_stopped";
            }

            var folderName = "session-" + Guid.Parse(command.SessionId).ToString("N");
            var folder = Path.Combine(Root, folderName);
            var stagePath = Path.Combine(folder, "stage.json");
            if (command.Action == "begin")
            {
                if (Directory.Exists(folder)) throw new InvalidOperationException("Essa transferência já foi iniciada.");
                Directory.CreateDirectory(folder);
                Save(stagePath, new StoredCarousel
                {
                    SessionId = command.SessionId,
                    Folder = folderName,
                    Target = command.Target!,
                    DurationSeconds = command.DurationSeconds!.Value,
                    Count = command.Count!.Value,
                    MinMinutes = command.MinMinutes!.Value,
                    MaxMinutes = command.MaxMinutes!.Value,
                    Repeat = command.Repeat!.Value,
                });
                return "carousel_staged";
            }

            var stage = Read(stagePath) ?? throw new InvalidOperationException("Inicie o carrossel antes de enviar imagens.");
            if (stage.SessionId != command.SessionId) throw new InvalidOperationException("Sessão do carrossel não corresponde.");
            if (command.Action == "item")
            {
                var index = command.Index!.Value;
                if (index >= stage.Count) throw new InvalidOperationException("Índice fora do carrossel.");
                var extension = image!.MimeType == "image/png" ? ".png" : ".jpg";
                var fileName = index.ToString("D8") + extension;
                var path = Path.Combine(folder, fileName);
                var temporary = path + ".tmp";
                File.WriteAllBytes(temporary, Convert.FromBase64String(image.DataBase64));
                File.Move(temporary, path, overwrite: true);
                if (stage.Received.TryGetValue(index, out var previous) && previous != fileName)
                    File.Delete(Path.Combine(folder, previous));
                stage.Received[index] = fileName;
                Save(stagePath, stage);
                return "carousel_item_saved";
            }

            if (command.Action != "commit") throw new InvalidOperationException("Ação desconhecida.");
            if (stage.Received.Count != stage.Count)
                throw new InvalidOperationException("Ainda há imagens pendentes; o carrossel anterior continua ativo.");
            stage.Images = Enumerable.Range(0, stage.Count)
                .Select(index => stage.Received.TryGetValue(index, out var name) ? name : string.Empty)
                .ToList();
            if (stage.Images.Any(name => string.IsNullOrEmpty(name) || !File.Exists(Path.Combine(folder, name))))
                throw new InvalidOperationException("Ainda há imagens pendentes; o carrossel anterior continua ativo.");

            InstallWorker();
            stage.Enabled = true;
            Save(Path.Combine(Root, "active.json"), stage);
            try
            {
                var worker = Path.Combine(Root, "carousel-worker.ps1");
                var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                var commandLine = $"\"{powerShell}\" -NoProfile -NonInteractive -Sta -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{worker}\"";
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(RunName, commandLine, RegistryValueKind.String);
                var start = new ProcessStartInfo(powerShell)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Sta", "-WindowStyle", "Hidden",
                    "-ExecutionPolicy", "Bypass", "-File", worker }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o carrossel.");
            }
            catch
            {
                stage.Enabled = false;
                Save(Path.Combine(Root, "active.json"), stage);
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(RunName, throwOnMissingValue: false);
                throw;
            }
            return "carousel_started";
        }
    }

    private static StoredCarousel? Read(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<StoredCarousel>(File.ReadAllText(path))
        : null;

    private static void Save(string path, StoredCarousel value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, path, overwrite: true);
    }

    private static void InstallWorker()
    {
        var name = "Comunicador.Receiver.carousel-worker.ps1";
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new IOException("O carrossel não foi incluído no painel.");
        var temporary = Path.Combine(Root, "carousel-worker.ps1.tmp");
        using (var output = File.Create(temporary)) resource.CopyTo(output);
        File.Move(temporary, Path.Combine(Root, "carousel-worker.ps1"), overwrite: true);
    }

    private static void WaitForWorkerStop()
    {
        var lockPath = Path.Combine(Root, "worker.lock");
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var unused = File.Open(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) when (attempt < 49)
            {
                Thread.Sleep(100);
            }
        }
        throw new IOException("O carrossel anterior ainda está encerrando. Tente novamente.");
    }
}
