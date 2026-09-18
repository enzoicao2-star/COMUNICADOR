using Comunicador.Models;
using Comunicador.Protocol;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class PerfilComputadorRepository
{
    private const int MaxProfiles = 250;
    private readonly JsonStore<PerfilComputador> _store;
    private readonly object _gate = new();
    private readonly List<PerfilComputador> _profiles;

    public event Action? Alterado;

    public PerfilComputadorRepository(JsonStore<PerfilComputador> store)
    {
        _store = store;
        _profiles = store.Load();
    }

    public PerfilComputador? Obter(string computerId)
    {
        lock (_gate)
        {
            return _profiles.FirstOrDefault(p => p.ComputerId == computerId);
        }
    }

    public void Salvar(
        string computerId,
        string displayName,
        bool isOwner,
        IEnumerable<BadgeUsuario> badges,
        string panelId,
        bool adminOverride = false)
    {
        // Cada painel é a única autoridade sobre o próprio nome e suas badges.
        // Isso impede que outro IP altere o perfil local e depois propague a
        // modificação pela sincronização da rede.
        if (!string.Equals(computerId, panelId, StringComparison.OrdinalIgnoreCase) && !adminOverride) return;

        lock (_gate)
        {
            var existing = _profiles.FirstOrDefault(p => p.ComputerId == computerId);
            if (existing is null)
            {
                existing = new PerfilComputador { ComputerId = computerId };
                _profiles.Add(existing);
            }
            existing.NomePublico = displayName.Trim();
            existing.EhOwner = isOwner;
            existing.Badges = badges.Take(4).Select(b => b.Clone()).ToList();
            existing.AtualizadoEmUtc = DateTime.UtcNow;
            existing.AtualizadoPorPainelId = computerId;
            ApararEPersistir();
        }
        Alterado?.Invoke();
    }

    public int Mesclar(IEnumerable<PerfilComputadorSincronizado> remote)
    {
        var changed = 0;
        lock (_gate)
        {
            foreach (var item in remote.Take(MaxProfiles))
            {
                if (string.IsNullOrWhiteSpace(item.ComputerId) ||
                    !string.Equals(item.ComputerId, item.UpdatedBy, StringComparison.OrdinalIgnoreCase) ||
                    !DateTime.TryParse(item.UpdatedAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var updated))
                    continue;

                updated = updated.ToUniversalTime();
                var local = _profiles.FirstOrDefault(p => p.ComputerId == item.ComputerId);
                if (local is not null && local.AtualizadoEmUtc >= updated) continue;
                if (local is null)
                {
                    local = new PerfilComputador { ComputerId = item.ComputerId };
                    _profiles.Add(local);
                }
                local.NomePublico = item.DisplayName;
                local.EhOwner = item.IsOwner;
                local.Badges = item.Badges.Take(4).Select(b => b.Clone()).ToList();
                local.AtualizadoEmUtc = updated;
                local.AtualizadoPorPainelId = item.UpdatedBy;
                changed++;
            }
            if (changed > 0) ApararEPersistir();
        }
        if (changed > 0) Alterado?.Invoke();
        return changed;
    }

    public IReadOnlyList<PerfilComputadorSincronizado> Snapshot(int max = 250)
    {
        lock (_gate)
        {
            return _profiles.OrderByDescending(p => p.AtualizadoEmUtc).Take(max).Select(p => new PerfilComputadorSincronizado
            {
                ComputerId = p.ComputerId,
                DisplayName = p.NomePublico,
                IsOwner = p.EhOwner,
                Badges = p.Badges.Select(b => b.Clone()).ToList(),
                UpdatedAt = p.AtualizadoEmUtc.ToUniversalTime().ToString("o"),
                UpdatedBy = p.AtualizadoPorPainelId,
            }).ToList();
        }
    }

    private void ApararEPersistir()
    {
        if (_profiles.Count > MaxProfiles)
        {
            _profiles.RemoveAll(p => !_profiles.OrderByDescending(x => x.AtualizadoEmUtc)
                .Take(MaxProfiles).Contains(p));
        }
        _store.Save(_profiles);
    }

}
