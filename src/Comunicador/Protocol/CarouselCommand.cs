using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Transfers a carousel one image at a time so there is no image-count limit
/// in a single TCP frame. The receiver stages images until commit.</summary>
public sealed class CarouselCommand
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("min_minutes")]
    public int? MinMinutes { get; set; }

    [JsonPropertyName("max_minutes")]
    public int? MaxMinutes { get; set; }

    [JsonPropertyName("repeat")]
    public bool? Repeat { get; set; }

    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }
}
