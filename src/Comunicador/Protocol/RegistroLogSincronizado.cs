using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

public sealed class RegistroLogSincronizado
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = string.Empty;
    [JsonPropertyName("level")] public string Level { get; set; } = string.Empty;
    [JsonPropertyName("origin_type")] public string OriginType { get; set; } = string.Empty;
    [JsonPropertyName("origin_id")] public string OriginId { get; set; } = string.Empty;
    [JsonPropertyName("origin_name")] public string OriginName { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("details")] public string? Details { get; set; }
}
