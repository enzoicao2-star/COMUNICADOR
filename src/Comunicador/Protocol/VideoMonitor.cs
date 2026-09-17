using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Um vídeo e o monitor no qual deve ser reproduzido.</summary>
public sealed class VideoMonitor
{
    [JsonPropertyName("monitor_index")]
    public int MonitorIndex { get; set; }

    [JsonPropertyName("width_percent")]
    public int WidthPercent { get; set; } = 70;

    [JsonPropertyName("video")]
    public ConteudoVideo Video { get; set; } = new();
}
