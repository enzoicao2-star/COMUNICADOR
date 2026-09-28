using System.Text.Json;

namespace Comunicador.Services;

public static class PanelChangelog
{
    public static string Format(string json, Version previous, Version current,
        string fallbackSummary, IReadOnlyList<string> fallbackChanges)
    {
        var entries = JsonSerializer.Deserialize<List<ReleaseNote>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var applicable = entries
            .Select(entry => new { Entry = entry, Valid = Version.TryParse(entry.Version, out var version) ? version : null })
            .Where(item => item.Valid is not null && item.Valid > previous && item.Valid <= current)
            .OrderBy(item => item.Valid)
            .Select(item => item.Entry)
            .ToList();
        if (applicable.Count == 0)
            return $"Comunicador atualizado para {current}.\n\n{fallbackSummary}\n" +
                string.Join("\n", fallbackChanges.Select(change => $"• {change}"));
        var sections = applicable.Select(note =>
            $"Versão {note.Version} — {note.Summary}\n" +
            string.Join("\n", note.Changes.Select(change => $"• {change}")));
        return $"Comunicador atualizado de {previous} para {current}.\n\n" +
            string.Join("\n\n", sections);
    }

    private sealed class ReleaseNote
    {
        public string Version { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string[] Changes { get; set; } = [];
    }
}
