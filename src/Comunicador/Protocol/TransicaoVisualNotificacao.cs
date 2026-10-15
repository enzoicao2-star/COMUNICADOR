using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Transições aplicadas à exibição central de imagens e vídeos.</summary>
public sealed class TransicaoVisualNotificacao
{
    [JsonPropertyName("entrance_effect")]
    public string EntranceEffect { get; set; } = "fade";

    [JsonPropertyName("exit_effect")]
    public string ExitEffect { get; set; } = "fade";

    [JsonPropertyName("entrance_speed_percent")]
    public int EntranceSpeedPercent { get; set; } = 100;

    [JsonPropertyName("exit_speed_percent")]
    public int ExitSpeedPercent { get; set; } = 100;
}
