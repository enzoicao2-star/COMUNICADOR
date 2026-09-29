namespace Comunicador.Models;

/// <summary>Nome público e badges compartilhados entre painéis.</summary>
public sealed class PerfilComputador
{
    public string ComputerId { get; set; } = string.Empty;
    public string NomePublico { get; set; } = string.Empty;
    public bool EhOwner { get; set; }
    public List<BadgeUsuario> Badges { get; set; } = new();
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
    public string AtualizadoPorPainelId { get; set; } = string.Empty;
    /// <summary>Uma edição local só deixa de estar pendente após o banco confirmá-la.</summary>
    public string? RevisaoLocalPendente { get; set; }
    /// <summary>Impede que um cache antigo vindo da rede substitua a versão do banco.</summary>
    public bool SincronizadoPelaNuvem { get; set; }
}
