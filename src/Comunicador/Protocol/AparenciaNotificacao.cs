using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Preferências visuais e sonoras que acompanham uma notificação.</summary>
public sealed class AparenciaNotificacao
{
    [JsonPropertyName("accent_color")]
    public string AccentColor { get; set; } = "#0067C0";

    [JsonPropertyName("font_scale_percent")]
    public int FontScalePercent { get; set; } = 100;

    [JsonPropertyName("play_sound")]
    public bool PlaySound { get; set; } = true;

    [JsonPropertyName("sound_type")]
    public string SoundType { get; set; } = ProtocolConstants.SoundType.Information;

    [JsonPropertyName("toast_duration_seconds")]
    public int ToastDurationSeconds { get; set; } = 20;

    [JsonPropertyName("toast_position")]
    public string ToastPosition { get; set; } = ProtocolConstants.ToastPosition.BottomRight;
}
