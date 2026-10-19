using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Áudio embutido na notificação, limitado a formatos reproduzidos
/// nativamente pelo Windows.</summary>
public sealed class ConteudoAudio
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("data_base64")]
    public string DataBase64 { get; set; } = string.Empty;

    [JsonIgnore]
    internal byte[]? RawData { get; set; }

    [JsonPropertyName("media_cache_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaCacheId { get; set; }

    public static bool MimePermitido(string? mimeType) => mimeType is
        "audio/mpeg" or "audio/wav";

    public static bool AssinaturaCorresponde(string mimeType, ReadOnlySpan<byte> dados) => mimeType switch
    {
        "audio/mpeg" => dados.Length >= 3
            && (dados[..3].SequenceEqual("ID3"u8)
                || (dados[0] == 0xFF && (dados[1] & 0xE0) == 0xE0)),
        "audio/wav" => dados.Length >= 12
            && dados[..4].SequenceEqual("RIFF"u8)
            && dados.Slice(8, 4).SequenceEqual("WAVE"u8),
        _ => false,
    };

    public static string ExtensaoTemporaria(string mimeType) =>
        mimeType == "audio/wav" ? ".wav" : ".mp3";
}
