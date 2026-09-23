using System.Collections.ObjectModel;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

/// <summary>Single shared source of truth for sent-message history, backing the Histórico screen
/// and consumed by Mensagens/Lembretes whenever they send something.</summary>
public sealed class HistoricoRepository
{
    private const int MaxStoredEntries = 10_000;
    private readonly JsonStore<HistoricoEntry> _store;
    private readonly BatchObservableCollection<HistoricoEntry> _itens = new();
    private readonly Dictionary<string, HistoricoEntry> _porId = new(StringComparer.Ordinal);

    public ObservableCollection<HistoricoEntry> Itens => _itens;
    public event Action? Changed;

    public HistoricoRepository(JsonStore<HistoricoEntry> store)
    {
        _store = store;
        _itens.ReplaceWith(_store.Load().OrderByDescending(i => i.Timestamp).Take(MaxStoredEntries));
        Reindexar();
    }

    public void Adicionar(HistoricoEntry entry)
    {
        UiDispatcher.Invoke(() =>
        {
            if (_porId.ContainsKey(entry.Id)) return;
            InserirOrdenado(entry);
            _porId.Add(entry.Id, entry);
            Aparar();
            Persist();
            Changed?.Invoke();
        });
    }

    public void AtualizarExistente(string id, Action<HistoricoEntry> aplicar)
    {
        UiDispatcher.Invoke(() =>
        {
            if (_porId.TryGetValue(id, out var item))
            {
                aplicar(item);
                Persist();
                Changed?.Invoke();
            }
        });
    }

    public void Limpar()
    {
        UiDispatcher.Invoke(() =>
        {
            Itens.Clear();
            _porId.Clear();
            Persist();
            Changed?.Invoke();
        });
    }

    public int Mesclar(IEnumerable<HistoricoEntry> entries)
    {
        var alterados = 0;
        UiDispatcher.Invoke(() =>
        {
            var novos = new List<HistoricoEntry>();
            foreach (var remoto in entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
            {
                if (!_porId.TryGetValue(remoto.Id, out var local))
                {
                    _porId.Add(remoto.Id, remoto);
                    novos.Add(remoto);
                    alterados++;
                    continue;
                }

                if (Prioridade(remoto.Status) >= Prioridade(local.Status))
                {
                    var mudou = local.Status != remoto.Status
                        || local.RespostaTexto != remoto.RespostaTexto
                        || local.ErroDetalhe != remoto.ErroDetalhe;
                    local.Status = remoto.Status;
                    local.RespostaTexto = remoto.RespostaTexto;
                    local.ErroDetalhe = remoto.ErroDetalhe;
                    if (mudou) alterados++;
                }
            }

            if (alterados > 0)
            {
                if (novos.Count > 0)
                {
                    _itens.ReplaceWith(MesclarOrdenado(Itens, novos));
                    Reindexar();
                }
                Persist();
                Changed?.Invoke();
            }
        });
        return alterados;
    }

    public IReadOnlyList<HistoricoEntry> Snapshot(int max = 500) =>
        Itens.Take(max).ToList();

    private void InserirOrdenado(HistoricoEntry entry)
    {
        var low = 0;
        var high = Itens.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Itens[middle].Timestamp >= entry.Timestamp) low = middle + 1;
            else high = middle;
        }
        Itens.Insert(low, entry);
    }

    private void Aparar()
    {
        var changed = false;
        while (Itens.Count > MaxStoredEntries)
        {
            Itens.RemoveAt(Itens.Count - 1);
            changed = true;
        }
        if (changed) Reindexar();
    }

    private static IReadOnlyList<HistoricoEntry> MesclarOrdenado(
        IReadOnlyList<HistoricoEntry> atuais, List<HistoricoEntry> novos)
    {
        novos.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));
        var resultado = new List<HistoricoEntry>(Math.Min(MaxStoredEntries, atuais.Count + novos.Count));
        var atualIndex = 0;
        var novoIndex = 0;
        while (resultado.Count < MaxStoredEntries && (atualIndex < atuais.Count || novoIndex < novos.Count))
        {
            if (novoIndex >= novos.Count || atualIndex < atuais.Count
                && atuais[atualIndex].Timestamp >= novos[novoIndex].Timestamp)
            {
                resultado.Add(atuais[atualIndex++]);
            }
            else
            {
                resultado.Add(novos[novoIndex++]);
            }
        }
        return resultado;
    }

    private void Reindexar()
    {
        _porId.Clear();
        foreach (var item in Itens) _porId.TryAdd(item.Id, item);
    }

    private static int Prioridade(StatusEnvio status) => status switch
    {
        StatusEnvio.Enviando => 0,
        StatusEnvio.Entregue => 1,
        StatusEnvio.Exibido => 2,
        StatusEnvio.SemResposta => 3,
        StatusEnvio.Respondido => 4,
        StatusEnvio.Erro => 4,
        _ => 0,
    };

    private void Persist() => _store.Save(Itens);
}
