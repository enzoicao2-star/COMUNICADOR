namespace Comunicador.Models;

/// <summary>Nome público e badges compartilhados entre painéis. A revisão UTC
/// implementa resolução simples de conflito: a edição mais recente prevalece.</summary>
public sealed class PerfilComputador
{
    public string ComputerId { get; set; } = string.Empty;
    public string NomePublico { get; set; } = string.Empty;
    public bool EhOwner { get; set; }
    public List<BadgeUsuario> Badges { get; set; } = new();
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
    public string AtualizadoPorPainelId { get; set; } = string.Empty;
}
