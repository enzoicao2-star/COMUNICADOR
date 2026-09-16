using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class LogRepository
{
    private const int MaxStoredEntries = 10_000;
    private readonly JsonStore<LogEntry> _store;
    public ObservableCollection<LogEntry> Itens { get; } = new();

    public LogRepository(JsonStore<LogEntry> store)
    {
        _store = store;
        foreach (var item in _store.Load().OrderByDescending(i => i.TimestampUtc).Take(MaxStoredEntries))
        {
            Itens.Add(item);
        }
    }

    public void Adicionar(LogEntry entry)
    {
        UiDispatcher.Invoke(() =>
        {
            if (Itens.Any(i => i.Id == entry.Id)) return;
            InserirOrdenado(entry);
            Aparar();
            Persist();
        });
    }

    public int Mesclar(IEnumerable<LogEntry> entries)
    {
        var adicionados = 0;
        UiDispatcher.Invoke(() =>
        {
            var ids = Itens.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
            {
                if (!ids.Add(entry.Id)) continue;
                InserirOrdenado(entry);
                adicionados++;
            }
            if (adicionados > 0)
            {
                Aparar();
                Persist();
            }
        });
        return adicionados;
    }

    public IReadOnlyList<LogEntry> Snapshot(int max = 500) =>
        Itens.OrderByDescending(i => i.TimestampUtc).Take(max).ToList();

    public void Limpar()
    {
        UiDispatcher.Invoke(() =>
        {
            Itens.Clear();
            Persist();
        });
    }

    public void ExportarTxt(string path, IEnumerable<LogEntry>? entries = null)
    {
        var linhas = (entries ?? Itens).OrderBy(i => i.TimestampUtc).Select(item =>
            $"{item.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{item.Nivel.ToString().ToUpperInvariant()}] "
            + $"[{item.TipoOrigem}:{item.OrigemNome}] [{item.Categoria}] {item.Mensagem}"
            + (string.IsNullOrWhiteSpace(item.Detalhes) ? string.Empty : $" | {item.Detalhes}"));
        File.WriteAllLines(path, linhas, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void InserirOrdenado(LogEntry entry)
    {
        var index = 0;
        while (index < Itens.Count && Itens[index].TimestampUtc >= entry.TimestampUtc) index++;
        Itens.Insert(index, entry);
    }

    private void Aparar()
    {
        while (Itens.Count > MaxStoredEntries) Itens.RemoveAt(Itens.Count - 1);
    }

    private void Persist() => _store.Save(Itens);
}
