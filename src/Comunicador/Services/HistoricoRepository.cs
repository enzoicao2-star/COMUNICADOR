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
            InserirOrdenado(entry);
            Aparar();
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

    public int Mesclar(IEnumerable<HistoricoEntry> entries)
    {
        var alterados = 0;
        UiDispatcher.Invoke(() =>
        {
            foreach (var remoto in entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
            {
                var local = Itens.FirstOrDefault(i => i.Id == remoto.Id);
                if (local is null)
                {
                    InserirOrdenado(remoto);
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
                Aparar();
                Persist();
            }
        });
        return alterados;
    }

    public IReadOnlyList<HistoricoEntry> Snapshot(int max = 500) =>
        Itens.OrderByDescending(i => i.Timestamp).Take(max).ToList();

    private void InserirOrdenado(HistoricoEntry entry)
    {
        var index = 0;
        while (index < Itens.Count && Itens[index].Timestamp >= entry.Timestamp) index++;
        Itens.Insert(index, entry);
    }

    private void Aparar()
    {
        while (Itens.Count > MaxStoredEntries) Itens.RemoveAt(Itens.Count - 1);
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
