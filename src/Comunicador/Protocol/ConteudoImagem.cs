using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Imagem transportada dentro da notificação. Os bytes são codificados
/// em Base64 para o protocolo continuar sendo JSON independente de linguagem.</summary>
public sealed class ConteudoImagem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("data_base64")]
    public string DataBase64 { get; set; } = string.Empty;

    public static bool MimePermitido(string? mimeType) => mimeType is
        "image/png" or "image/jpeg" or "image/gif" or "image/bmp";

    public static bool AssinaturaCorresponde(string mimeType, ReadOnlySpan<byte> dados) => mimeType switch
    {
        "image/png" => dados.Length >= 8
            && dados[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpeg" => dados.Length >= 3 && dados[0] == 0xFF && dados[1] == 0xD8 && dados[2] == 0xFF,
        "image/gif" => dados.Length >= 6
            && (dados[..6].SequenceEqual("GIF87a"u8) || dados[..6].SequenceEqual("GIF89a"u8)),
        "image/bmp" => dados.Length >= 2 && dados[0] == (byte)'B' && dados[1] == (byte)'M',
        _ => false,
    };
}
