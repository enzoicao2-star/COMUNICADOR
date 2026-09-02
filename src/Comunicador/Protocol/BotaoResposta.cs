using System.Text.Json.Serialization;

namespace Comunicador.Protocol;

/// <summary>Botão de resposta rápida mostrado no aviso. Sem <see cref="Url"/>, clicar
/// devolve o rótulo como resposta. Com <see cref="Url"/>, além de responder, abre o
/// endereço no navegador padrão — só http/https, e só por clique do usuário.</summary>
public sealed class BotaoResposta
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    public bool TemLink => !string.IsNullOrWhiteSpace(Url);

    /// <summary>Só http e https. Bloquear os demais esquemas é o que impede uma
    /// mensagem vinda da rede de disparar coisas como file:, javascript: ou ms-*.</summary>
    public static bool UrlPermitida(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
