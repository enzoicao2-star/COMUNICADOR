namespace Comunicador.Models;

public sealed class AdminAuditEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string? TargetName { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Quando => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Quem => string.IsNullOrWhiteSpace(ActorName) ? "Painel desconhecido" : ActorName;
}
