using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

public sealed class ComunicadorMessage
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = ProtocolConstants.Version;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("panel_id")]
    public string? PanelId { get; set; }

    [JsonPropertyName("sender_name")]
    public string? SenderName { get; set; }

    [JsonPropertyName("computer_id")]
    public string? ComputerId { get; set; }

    [JsonPropertyName("computer_name")]
    public string? ComputerName { get; set; }

    [JsonPropertyName("tcp_port")]
    public int? TcpPort { get; set; }

    [JsonPropertyName("paired")]
    public bool? Paired { get; set; }

    [JsonPropertyName("panel_name")]
    public string? PanelName { get; set; }

    [JsonPropertyName("accepted")]
    public bool? Accepted { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("allow_reply")]
    public bool? AllowReply { get; set; }

    [JsonPropertyName("in_reply_to")]
    public string? InReplyTo { get; set; }

    [JsonPropertyName("reply_text")]
    public string? ReplyText { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Botões de resposta rápida do aviso (opcional).</summary>
    [JsonPropertyName("buttons")]
    public List<BotaoResposta>? Buttons { get; set; }

    public static ComunicadorMessage CreateBase(string type) => new()
    {
        Type = type,
        Id = Guid.NewGuid().ToString(),
        Timestamp = DateTime.UtcNow.ToString("o"),
    };

    public static ComunicadorMessage Error(string code, string message, string? inReplyTo = null)
    {
        var msg = CreateBase(ProtocolConstants.MessageType.Error);
        msg.Code = code;
        msg.Message = message;
        msg.InReplyTo = inReplyTo;
        return msg;
    }
}
