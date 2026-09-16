using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Arquivo oficial do receptor incorporado no Comunicador.exe.
/// O receptor só aceita os dois nomes conhecidos e confere o SHA-256 antes de substituir.</summary>
public sealed class ArquivoAtualizacao
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("content_base64")]
    public string ContentBase64 { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
