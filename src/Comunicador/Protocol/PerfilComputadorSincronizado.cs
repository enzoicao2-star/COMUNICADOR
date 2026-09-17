using System.Text.Json.Serialization;
using Comunicador.Models;

namespace Comunicador.Protocol;

public sealed class PerfilComputadorSincronizado
{
    [JsonPropertyName("computer_id")] public string ComputerId { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("is_owner")] public bool IsOwner { get; set; }
    [JsonPropertyName("badges")] public List<BadgeUsuario> Badges { get; set; } = new();
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = string.Empty;
    [JsonPropertyName("updated_by")] public string UpdatedBy { get; set; } = string.Empty;
}
