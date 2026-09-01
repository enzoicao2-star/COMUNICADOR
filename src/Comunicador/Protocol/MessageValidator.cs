using System.Text;
using System.Text.Json;
using static Comunicador.Protocol.ProtocolConstants;

namespace Comunicador.Protocol;

public static class MessageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static ValidationResult ValidateSize(int byteLength, bool isUdp)
    {
        var max = isUdp ? MaxUdpMessageBytes : MaxTcpMessageBytes;
        return byteLength > max
            ? ValidationResult.Fail(ErrorCode.PayloadTooLarge, $"Payload excede o limite de {max} bytes.")
            : ValidationResult.Ok();
    }

    public static bool TryParse(byte[] payload, out ComunicadorMessage? message, out ValidationResult result)
    {
        try
        {
            message = JsonSerializer.Deserialize<ComunicadorMessage>(payload, JsonOptions);
            if (message is null)
            {
                result = ValidationResult.Fail(ErrorCode.InvalidJson, "Mensagem JSON vazia ou nula.");
                return false;
            }

            result = ValidationResult.Ok();
            return true;
        }
        catch (JsonException ex)
        {
            message = null;
            result = ValidationResult.Fail(ErrorCode.InvalidJson, $"JSON inválido: {ex.Message}");
            return false;
        }
    }

    public static ValidationResult Validate(ComunicadorMessage msg)
    {
        if (msg.ProtocolVersion != ProtocolConstants.Version)
        {
            return ValidationResult.Fail(
                ErrorCode.ProtocolVersionUnsupported,
                $"Versão de protocolo não suportada: {msg.ProtocolVersion}");
        }

        if (string.IsNullOrWhiteSpace(msg.Type) || !MessageType.All.Contains(msg.Type))
        {
            return ValidationResult.Fail(ErrorCode.UnknownType, $"Tipo de mensagem desconhecido: '{msg.Type}'");
        }

        if (!Guid.TryParse(msg.Id, out _))
        {
            return ValidationResult.Fail(ErrorCode.InvalidId, "Campo 'id' não é um UUID válido.");
        }

        if (string.IsNullOrWhiteSpace(msg.Timestamp))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, "Campo obrigatório ausente: timestamp");
        }

        var fieldsCheck = msg.Type switch
        {
            MessageType.Discover => RequireUuid(msg.PanelId, "panel_id")
                ?? RequireString(msg.SenderName, "sender_name", MaxNameLength),

            MessageType.Announce => RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireInt(msg.TcpPort, "tcp_port")
                ?? RequireBool(msg.Paired, "paired"),

            MessageType.PairRequest => RequireUuid(msg.PanelId, "panel_id")
                ?? RequireString(msg.PanelName, "panel_name", MaxNameLength),

            MessageType.PairResponse => RequireBool(msg.Accepted, "accepted")
                ?? (msg.Accepted == true
                    ? RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                        ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                        ?? RequireString(msg.Token, "token", MaxNameLength)
                    : null),

            MessageType.Ping => RequireString(msg.Token, "token", MaxNameLength),

            MessageType.Pong => RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireString(msg.Status, "status", MaxNameLength),

            MessageType.Notification => RequireString(msg.Token, "token", MaxNameLength)
                ?? RequireString(msg.Sender, "sender", MaxNameLength)
                ?? RequireString(msg.Title, "title", MaxTitleLength)
                ?? RequireString(msg.Message, "message", MaxMessageLength)
                ?? RequireBool(msg.AllowReply, "allow_reply"),

            MessageType.Ack => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? RequireString(msg.Status, "status", MaxNameLength),

            MessageType.Reply => RequireUuid(msg.InReplyTo, "in_reply_to")
                ?? RequireString(msg.ComputerId, "computer_id", MaxNameLength)
                ?? RequireString(msg.ComputerName, "computer_name", MaxNameLength)
                ?? RequireString(msg.ReplyText, "reply_text", MaxMessageLength),

            MessageType.Error => RequireString(msg.Code, "code", MaxNameLength)
                ?? RequireString(msg.Message, "message", MaxMessageLength),

            _ => ValidationResult.Fail(ErrorCode.UnknownType, $"Tipo de mensagem desconhecido: '{msg.Type}'"),
        };

        return fieldsCheck ?? ValidationResult.Ok();
    }

    public static byte[] Frame(ComunicadorMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    private static ValidationResult? RequireString(string? value, string name, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");
        }

        if (value.Length > maxLen)
        {
            return ValidationResult.Fail(ErrorCode.FieldTooLong, $"Campo '{name}' excede {maxLen} caracteres.");
        }

        return null;
    }

    private static ValidationResult? RequireUuid(string? value, string name)
    {
        var missing = RequireString(value, name, MaxNameLength);
        if (missing is not null)
        {
            return missing;
        }

        return Guid.TryParse(value, out _)
            ? null
            : ValidationResult.Fail(ErrorCode.InvalidId, $"Campo '{name}' não é um UUID válido.");
    }

    private static ValidationResult? RequireBool(bool? value, string name) =>
        value.HasValue ? null : ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");

    private static ValidationResult? RequireInt(int? value, string name) =>
        value.HasValue ? null : ValidationResult.Fail(ErrorCode.MissingField, $"Campo obrigatório ausente: {name}");
}
