using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Vídeo transportado dentro da notificação. O receptor grava os bytes em
/// um arquivo temporário validado e o remove assim que a reprodução termina.</summary>
public sealed class ConteudoVideo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("data_base64")]
    public string DataBase64 { get; set; } = string.Empty;

    public static bool MimePermitido(string? mimeType) => mimeType is
        "video/mp4" or "video/x-ms-wmv";

    public static bool AssinaturaCorresponde(string mimeType, ReadOnlySpan<byte> dados) => mimeType switch
    {
        "video/mp4" => dados.Length >= 12
            && dados.Slice(4, 4).SequenceEqual("ftyp"u8),
        "video/x-ms-wmv" => dados.Length >= 16
            && dados[..16].SequenceEqual(new byte[]
            {
                0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
                0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C,
            }),
        _ => false,
    };

    public static string ExtensaoTemporaria(string mimeType) =>
        mimeType == "video/x-ms-wmv" ? ".wmv" : ".mp4";
}
