using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

public sealed class HistoricoSincronizado
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = string.Empty;
    [JsonPropertyName("direction")] public string Direction { get; set; } = string.Empty;
    [JsonPropertyName("computer_id")] public string ComputerId { get; set; } = string.Empty;
    [JsonPropertyName("computer_name")] public string ComputerName { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("reply_text")] public string? ReplyText { get; set; }
    [JsonPropertyName("error_detail")] public string? ErrorDetail { get; set; }
}
