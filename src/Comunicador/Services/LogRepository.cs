using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Comunicador.Models;
using Comunicador.Storage;

namespace Comunicador.Services;

public sealed class LogRepository
{
    private const int MaxStoredEntries = 10_000;
    private readonly JsonStore<LogEntry> _store;
    private readonly BatchObservableCollection<LogEntry> _itens = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly object _persistGate = new();
    private List<LogEntry>? _pendingPersist;
    private Task? _persistTask;
    public ObservableCollection<LogEntry> Itens => _itens;

    public LogRepository(JsonStore<LogEntry> store)
    {
        _store = store;
        _itens.ReplaceWith(_store.Load().OrderByDescending(i => i.TimestampUtc).Take(MaxStoredEntries));
        foreach (var item in Itens) _ids.Add(item.Id);
    }

    public void Adicionar(LogEntry entry)
    {
        UiDispatcher.Invoke(() =>
        {
            if (!_ids.Add(entry.Id)) return;
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
            var novos = new List<LogEntry>();
            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
            {
                if (!_ids.Add(entry.Id)) continue;
                novos.Add(entry);
                adicionados++;
            }
            if (adicionados > 0)
            {
                _itens.ReplaceWith(MesclarOrdenado(Itens, novos));
                _ids.Clear();
                foreach (var item in Itens) _ids.Add(item.Id);
                Persist();
            }
        });
        return adicionados;
    }

    public IReadOnlyList<LogEntry> Snapshot(int max = 500) =>
        Itens.Take(max).ToList();

    public void Limpar()
    {
        UiDispatcher.Invoke(() =>
        {
            Itens.Clear();
            _ids.Clear();
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
        var low = 0;
        var high = Itens.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Itens[middle].TimestampUtc >= entry.TimestampUtc) low = middle + 1;
            else high = middle;
        }
        Itens.Insert(low, entry);
    }

    private void Aparar()
    {
        while (Itens.Count > MaxStoredEntries)
        {
            var last = Itens[^1];
            Itens.RemoveAt(Itens.Count - 1);
            _ids.Remove(last.Id);
        }
    }

    private static IReadOnlyList<LogEntry> MesclarOrdenado(IReadOnlyList<LogEntry> atuais, List<LogEntry> novos)
    {
        novos.Sort((left, right) => right.TimestampUtc.CompareTo(left.TimestampUtc));
        var resultado = new List<LogEntry>(Math.Min(MaxStoredEntries, atuais.Count + novos.Count));
        var atualIndex = 0;
        var novoIndex = 0;
        while (resultado.Count < MaxStoredEntries && (atualIndex < atuais.Count || novoIndex < novos.Count))
        {
            if (novoIndex >= novos.Count || atualIndex < atuais.Count
                && atuais[atualIndex].TimestampUtc >= novos[novoIndex].TimestampUtc)
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

    private void Persist()
    {
        // Copiar a lista na thread da interface é rápido. Serializar milhares de
        // entradas e esperar o Windows liberar arquivos bloqueados fica em segundo plano.
        var snapshot = Itens.ToList();
        lock (_persistGate)
        {
            _pendingPersist = snapshot;
            _persistTask ??= Task.Run(PersistPending);
        }
    }

    private void PersistPending()
    {
        try
        {
            while (true)
            {
                List<LogEntry>? snapshot;
                lock (_persistGate)
                {
                    snapshot = _pendingPersist;
                    _pendingPersist = null;
                }
                if (snapshot is null) return;
                try { _store.Save(snapshot); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // O log de texto já foi gravado. A próxima mudança tentará salvar
                    // novamente o histórico estruturado sem travar a interface.
                }
            }
        }
        finally
        {
            lock (_persistGate)
            {
                _persistTask = null;
                if (_pendingPersist is not null) _persistTask = Task.Run(PersistPending);
            }
        }
    }

    internal async Task FlushPendingAsync()
    {
        while (true)
        {
            Task? task;
            lock (_persistGate) task = _persistTask;
            if (task is null) return;
            await task.ConfigureAwait(false);
        }
    }
}
