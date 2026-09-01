using System.Collections.ObjectModel;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

/// <summary>Single shared source of truth for sent-message history, backing the Histórico screen
/// and consumed by Mensagens/Lembretes whenever they send something.</summary>
public sealed class HistoricoRepository
{
    private readonly JsonStore<HistoricoEntry> _store;

    public ObservableCollection<HistoricoEntry> Itens { get; } = new();

    public HistoricoRepository(JsonStore<HistoricoEntry> store)
    {
        _store = store;
        foreach (var item in _store.Load().OrderByDescending(i => i.Timestamp))
        {
            Itens.Add(item);
        }
    }

    public void Adicionar(HistoricoEntry entry)
    {
        UiDispatcher.Invoke(() =>
        {
            Itens.Insert(0, entry);
            Persist();
        });
    }

    public void AtualizarExistente(string id, Action<HistoricoEntry> aplicar)
    {
        UiDispatcher.Invoke(() =>
        {
            var item = Itens.FirstOrDefault(i => i.Id == id);
            if (item is not null)
            {
                aplicar(item);
                Persist();
            }
        });
    }

    public void Limpar()
    {
        UiDispatcher.Invoke(() =>
        {
            Itens.Clear();
            Persist();
        });
    }

    private void Persist() => _store.Save(Itens);
}
