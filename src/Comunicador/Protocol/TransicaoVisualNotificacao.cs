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

    [JsonPropertyName("entrance_timing_mode")]
    public string EntranceTimingMode { get; set; } = "speed";

    [JsonPropertyName("exit_timing_mode")]
    public string ExitTimingMode { get; set; } = "speed";

    [JsonPropertyName("entrance_duration_ms")]
    public int EntranceDurationMs { get; set; } = 600;

    [JsonPropertyName("exit_duration_ms")]
    public int ExitDurationMs { get; set; } = 600;

    [JsonPropertyName("block_close_during_transition")]
    public bool BlockCloseDuringTransition { get; set; } = true;
}
