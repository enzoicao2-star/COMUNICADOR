using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Uma imagem e o monitor em que ela deve ser exibida. Quando várias
/// imagens apontam para o mesmo monitor, o receptor as organiza juntas no centro.</summary>
public sealed class ImagemMonitor
{
    [JsonPropertyName("monitor_index")]
    public int MonitorIndex { get; set; }

    [JsonPropertyName("width_percent")]
    public int WidthPercent { get; set; } = 70;

    [JsonPropertyName("image")]
    public ConteudoImagem Image { get; set; } = new();
}
