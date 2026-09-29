using System.Text.Json;
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
            return _profiles.FirstOrDefault(p =>
                string.Equals(p.ComputerId, computerId, StringComparison.OrdinalIgnoreCase));
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
            var existing = _profiles.FirstOrDefault(p =>
                string.Equals(p.ComputerId, computerId, StringComparison.OrdinalIgnoreCase));
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
            existing.RevisaoLocalPendente = Guid.NewGuid().ToString("N");
            ApararEPersistir();
        }
        Alterado?.Invoke();
    }

    public int Mesclar(IEnumerable<PerfilComputadorSincronizado> remote) =>
        MesclarCore(remote, nuvemAutoritativa: false);

    /// <summary>O banco é a fonte de verdade depois que uma edição local foi confirmada.</summary>
    public int MesclarDaNuvem(IEnumerable<PerfilComputadorSincronizado> remote) =>
        MesclarCore(remote, nuvemAutoritativa: true);

    private int MesclarCore(IEnumerable<PerfilComputadorSincronizado> remote, bool nuvemAutoritativa)
    {
        var changed = 0;
        lock (_gate)
        {
            var byId = new Dictionary<string, PerfilComputador>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _profiles) byId.TryAdd(profile.ComputerId, profile);
            foreach (var item in remote.Take(MaxProfiles))
            {
                if (string.IsNullOrWhiteSpace(item.ComputerId) ||
                    !string.Equals(item.ComputerId, item.UpdatedBy, StringComparison.OrdinalIgnoreCase) ||
                    !DateTime.TryParse(item.UpdatedAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var updated))
                    continue;

                updated = updated.ToUniversalTime();
                byId.TryGetValue(item.ComputerId, out var local);
                if (local is not null && local.RevisaoLocalPendente is not null)
                {
                    // Uma gravação anterior pode ter chegado ao banco antes de o
                    // aplicativo fechar. Nesse caso a leitura já a confirma.
                    if (nuvemAutoritativa && MesmoConteudo(local, item))
                    {
                        local.RevisaoLocalPendente = null;
                        local.SincronizadoPelaNuvem = true;
                        local.EhOwner = item.IsOwner;
                        local.AtualizadoEmUtc = updated;
                        local.AtualizadoPorPainelId = item.UpdatedBy;
                        changed++;
                    }
                    continue;
                }
                if (local is not null && !nuvemAutoritativa && local.SincronizadoPelaNuvem)
                    continue;
                if (local is not null && !nuvemAutoritativa && local.AtualizadoEmUtc >= updated)
                    continue;
                if (local is not null && nuvemAutoritativa
                    && local.AtualizadoEmUtc == updated && MesmoConteudo(local, item)
                    && local.EhOwner == item.IsOwner && local.SincronizadoPelaNuvem) continue;
                if (local is null)
                {
                    local = new PerfilComputador { ComputerId = item.ComputerId };
                    _profiles.Add(local);
                    byId.Add(item.ComputerId, local);
                }
                local.NomePublico = item.DisplayName;
                local.EhOwner = item.IsOwner;
                local.Badges = item.Badges.Take(4).Select(b => b.Clone()).ToList();
                local.AtualizadoEmUtc = updated;
                local.AtualizadoPorPainelId = item.UpdatedBy;
                local.RevisaoLocalPendente = null;
                local.SincronizadoPelaNuvem = nuvemAutoritativa;
                changed++;
            }
            if (changed > 0) ApararEPersistir();
        }
        if (changed > 0) Alterado?.Invoke();
        return changed;
    }

    public IReadOnlyList<PerfilComputador> ObterPendentes()
    {
        lock (_gate)
        {
            return _profiles.Where(p => p.RevisaoLocalPendente is not null)
                .Select(p => new PerfilComputador
                {
                    ComputerId = p.ComputerId,
                    NomePublico = p.NomePublico,
                    EhOwner = p.EhOwner,
                    Badges = p.Badges.Select(b => b.Clone()).ToList(),
                    AtualizadoEmUtc = p.AtualizadoEmUtc,
                    AtualizadoPorPainelId = p.AtualizadoPorPainelId,
                    RevisaoLocalPendente = p.RevisaoLocalPendente,
                    SincronizadoPelaNuvem = p.SincronizadoPelaNuvem,
                }).ToList();
        }
    }

    /// <summary>
    /// Perfis gravados por versões antigas não tinham revisão pendente. Depois de
    /// receber o banco, recupera apenas os que ainda não existem lá e pertencem
    /// a dispositivos registrados que este painel pode editar.
    /// </summary>
    public int PrepararPerfisLegados(IEnumerable<string> deviceIdsEditaveis,
        IEnumerable<string> deviceIdsComPerfilNaNuvem)
    {
        var editaveis = deviceIdsEditaveis.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existentesNaNuvem = deviceIdsComPerfilNaNuvem.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preparados = 0;
        lock (_gate)
        {
            foreach (var profile in _profiles)
            {
                if (profile.RevisaoLocalPendente is not null || profile.SincronizadoPelaNuvem
                    || string.IsNullOrWhiteSpace(profile.NomePublico)
                    || !editaveis.Contains(profile.ComputerId)
                    || existentesNaNuvem.Contains(profile.ComputerId)) continue;

                profile.RevisaoLocalPendente = Guid.NewGuid().ToString("N");
                preparados++;
            }
            if (preparados > 0) ApararEPersistir();
        }
        return preparados;
    }

    public void ConfirmarSincronizacao(string computerId, string? revisao)
    {
        if (revisao is null) return;
        lock (_gate)
        {
            var local = _profiles.FirstOrDefault(p =>
                string.Equals(p.ComputerId, computerId, StringComparison.OrdinalIgnoreCase));
            if (local is null || local.RevisaoLocalPendente != revisao) return;
            local.RevisaoLocalPendente = null;
            ApararEPersistir();
        }
    }

    public void DescartarPendente(string computerId, string? revisao)
    {
        if (revisao is null) return;
        var removed = false;
        lock (_gate)
        {
            var local = _profiles.FirstOrDefault(p =>
                string.Equals(p.ComputerId, computerId, StringComparison.OrdinalIgnoreCase));
            if (local is null || local.RevisaoLocalPendente != revisao) return;
            _profiles.Remove(local);
            ApararEPersistir();
            removed = true;
        }
        if (removed) Alterado?.Invoke();
    }

    private static bool MesmoConteudo(PerfilComputador local, PerfilComputadorSincronizado remote) =>
        string.Equals(local.NomePublico, remote.DisplayName, StringComparison.Ordinal)
        && JsonSerializer.Serialize(local.Badges) == JsonSerializer.Serialize(remote.Badges);

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
            var keep = _profiles.OrderByDescending(x => x.AtualizadoEmUtc)
                .Take(MaxProfiles).ToHashSet();
            _profiles.RemoveAll(p => !keep.Contains(p));
        }
        _store.Save(_profiles);
    }

}
